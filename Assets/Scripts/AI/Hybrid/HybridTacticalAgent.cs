using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RPGStrategy.AI.Baseline;
using RPGStrategy.AI.LLM;
using RPGStrategy.AI.PureLLM;
using RPGStrategy.AI.Telemetry;
using RPGStrategy.Core.Combat;
using UnityEngine;

namespace RPGStrategy.AI.Hybrid
{
    /// <summary>
    /// Agen Hybrid (X1 = LLM + HFSM, X2 = Dengan Pembatasan / Guardrail).
    ///
    /// Pipeline:
    ///   Snapshot -> LLM attempt 1 -> deterministic H1-H5 validation
    ///   -> (valid) execute
    ///   -> (invalid) feedback prompt -> LLM attempt 2 -> validation
    ///   -> (valid) execute tagged [RETRY_RESOLVED]
    ///   -> (invalid/transport failure) HFSM fallback tagged [HFSM_FALLBACK]
    ///
    /// Usulan mentah round 1 dan round 2 tetap dicatat untuk Y2, sedangkan aksi
    /// yang dieksekusi selalu berasal dari usulan tervalidasi atau fallback legal.
    /// </summary>
    public class HybridTacticalAgent : TacticalAgentControllerBase
    {
        [Header("LLM Infrastructure")]
        [SerializeField] private LLMConfig config;
        [SerializeField] private LLMService2D llmService;

        [Header("Telemetry")]
        [SerializeField] private CombatTelemetryLogger2D telemetryLogger;
        [SerializeField] private bool enableTelemetryLogging = true;

        [Header("Research Metadata")]
        [SerializeField] private string runId = "RUN_001";
        [SerializeField] private string configId = "CFG-03";
        [SerializeField] private string scenarioId = "SCEN_LIVE";

        [Header("Context")]
        [SerializeField] private int maxRecentLogs = 8;

        [Header("Recovery Policy")]
        [Tooltip("Corrective retries before deterministic HFSM fallback. Research policy: exactly 1.")]
        [SerializeField] private int maxRetries = 1;

        private TacticalStateExtractor2D _extractor;
        private TacticalPromptBuilder2D _promptBuilder;
        private TacticalActionValidator2D _validator;
        private FeedbackPromptBuilder2D _feedbackBuilder;
        private HFSMFallbackBridge _fallbackBridge;
        private ILLMService2D _llmService;

        private string _lastDecisionTag = string.Empty;

        public override string ControllerType => "Hybrid_LLM_HFSM_Guardrail";

        public override string LastDecisionTag => _lastDecisionTag;

        // --- Public configuration (Inspector / editor setup / tests) -----------------
        public LLMConfig Config
        {
            get => config;
            set
            {
                config = value;
                if (llmService != null) llmService.Configure(config);
            }
        }

        public LLMService2D LLMServiceComponent
        {
            get => llmService;
            set
            {
                llmService = value;
                _llmService = value;
            }
        }

        public CombatTelemetryLogger2D TelemetryLogger
        {
            get => telemetryLogger;
            set => telemetryLogger = value;
        }

        public bool EnableTelemetryLogging
        {
            get => enableTelemetryLogging;
            set => enableTelemetryLogging = value;
        }

        public string RunId { get => runId; set => runId = value; }
        public string ConfigId { get => configId; set => configId = value; }
        public string ScenarioId { get => scenarioId; set => scenarioId = value; }
        public int MaxRetries { get => maxRetries; set => maxRetries = Math.Max(0, value); }

        // --- Last-turn inspection (live Inspector / debugging) -----------------------
        public TacticalStateSnapshot2D LastSnapshot { get; private set; }
        public TacticalDecisionParseResult LastParseRound1 { get; private set; }
        public TacticalDecisionParseResult LastParseRound2 { get; private set; }
        public LLMResponseDTO LastResponseRound1 { get; private set; }
        public LLMResponseDTO LastResponseRound2 { get; private set; }
        public ValidationResult2D LastValidationRound1 { get; private set; }
        public ValidationResult2D LastValidationRound2 { get; private set; }
        public TacticalDecision2D LastDecision { get; private set; }
        public string LastSystemPrompt { get; private set; }
        public string LastUserPrompt { get; private set; }
        public string LastFeedbackPrompt { get; private set; }
        public bool RetryConducted { get; private set; }
        public bool FallbackTriggered { get; private set; }
        public string FallbackReason { get; private set; }
        public long LastTotalLatencyMs { get; private set; }

        private void Awake()
        {
            EnsureDependencies();
        }

        /// <summary>Konfigurasi eksplisit untuk pengujian offline / setup editor.</summary>
        public void Configure(
            LLMConfig newConfig,
            ILLMService2D service,
            CombatTelemetryLogger2D logger = null)
        {
            if (newConfig != null) config = newConfig;
            if (service != null) _llmService = service;
            if (logger != null) telemetryLogger = logger;
            if (llmService != null) llmService.Configure(config);
        }

        public override async Task<TacticalDecision2D> DecideTurnAsync(
            CombatEntity2D actor,
            TacticalGrid2D grid,
            IReadOnlyList<CombatEntity2D> combatants,
            IReadOnlyList<string> history)
        {
            EnsureDependencies();
            ResetTurnState();

            var totalWatch = System.Diagnostics.Stopwatch.StartNew();

            if (actor == null || grid == null)
            {
                var invalid = new TacticalDecision2D(
                    TacticalActionType.Idle, Vector2Int.zero, null, false,
                    "Invalid state: actor or grid is null.");
                LastDecision = invalid;
                LastTotalLatencyMs = totalWatch.ElapsedMilliseconds;
                return invalid;
            }

            int round = TurnContext != null ? TurnContext.RoundNumber : 1;
            int turn = TurnContext != null ? TurnContext.TurnNumber : 0;

            var snapshot = _extractor.ExtractSnapshot(actor, grid, combatants, round, turn, history);
            LastSnapshot = snapshot;

            string systemPrompt = _promptBuilder.BuildSystemPrompt(actor);
            string userPrompt = _promptBuilder.BuildUserPrompt(snapshot);
            LastSystemPrompt = systemPrompt;
            LastUserPrompt = userPrompt;

            // ===============================================================
            // Round 1: raw LLM proposal
            // ===============================================================
            long attempt1WatchMs;
            var attempt1 = await SendAttemptAsync(systemPrompt, userPrompt);
            var response1 = attempt1.Response;
            attempt1WatchMs = attempt1.ElapsedMs;
            LastResponseRound1 = response1;

            var parse1 = TacticalDecisionJsonParser.Parse(response1 != null ? response1.RawContent : null);
            LastParseRound1 = parse1;

            var decision1 = TacticalDecisionBuilder2D.Build(parse1, actor, grid, combatants);
            AppendTransportError(decision1, parse1, response1);

            long validatorMs;
            var validation1 = ValidateTimed(decision1, snapshot, grid, parse1, out validatorMs);
            LastValidationRound1 = validation1;

            if (validation1.IsValid)
            {
                _lastDecisionTag = string.Empty;
                LastDecision = decision1;
                LastTotalLatencyMs = totalWatch.ElapsedMilliseconds;

                if (enableTelemetryLogging)
                {
                    LogTelemetry(
                        actor, snapshot, response1, parse1, validation1,
                        false, null, null, null,
                        false, null, decision1, true,
                        attempt1WatchMs, 0, validatorMs, 0, LastTotalLatencyMs);
                }

                return decision1;
            }

            // ===============================================================
            // Round 2: exactly one corrective retry with diagnostic feedback
            // ===============================================================
            RetryConducted = true;

            string feedbackPrompt = _feedbackBuilder.BuildFeedbackPrompt(validation1, snapshot);
            LastFeedbackPrompt = feedbackPrompt;

            long attempt2WatchMs;
            var attempt2 = await SendAttemptAsync(systemPrompt, feedbackPrompt);
            var response2 = attempt2.Response;
            attempt2WatchMs = attempt2.ElapsedMs;
            LastResponseRound2 = response2;

            var parse2 = TacticalDecisionJsonParser.Parse(response2 != null ? response2.RawContent : null);
            LastParseRound2 = parse2;

            var decision2 = TacticalDecisionBuilder2D.Build(parse2, actor, grid, combatants);
            AppendTransportError(decision2, parse2, response2);

            long validator2Ms;
            var validation2 = ValidateTimed(decision2, snapshot, grid, parse2, out validator2Ms);
            LastValidationRound2 = validation2;
            validatorMs += validator2Ms;

            if (validation2.IsValid)
            {
                _lastDecisionTag = "[RETRY_RESOLVED]";
                LastDecision = decision2;
                LastTotalLatencyMs = totalWatch.ElapsedMilliseconds;

                if (enableTelemetryLogging)
                {
                    LogTelemetry(
                        actor, snapshot, response1, parse1, validation1,
                        true, response2, parse2, validation2,
                        false, null, decision2, true,
                        attempt1WatchMs, attempt2WatchMs, validatorMs, 0, LastTotalLatencyMs);
                }

                return decision2;
            }

            // ===============================================================
            // Terminal recovery: deterministic HFSM fallback
            // ===============================================================
            var fallbackWatch = System.Diagnostics.Stopwatch.StartNew();

            string fallbackReason = BuildFallbackReason(validation2, response2);
            FallbackTriggered = true;
            FallbackReason = fallbackReason;

            var fallbackDecision = _fallbackBridge.GetSafeFallbackDecision(
                actor, grid, combatants, fallbackReason);
            var fallbackValidation = _validator.Validate(fallbackDecision, snapshot, grid, null);

            // Final safety net: Idle is always mechanically legal. This guarantees
            // the engine never receives an illegal action even if the baseline
            // itself ever regresses.
            if (!fallbackValidation.IsValid)
            {
                fallbackDecision = new TacticalDecision2D(
                    TacticalActionType.Idle,
                    actor.GridPosition,
                    null,
                    false,
                    "[HFSM FALLBACK - REASON: " + fallbackReason +
                    "] HFSM output was invalid; emergency Idle used. " +
                    fallbackValidation.DiagnosticMessage);
            }

            fallbackWatch.Stop();

            _lastDecisionTag = "[HFSM_FALLBACK]";
            LastDecision = fallbackDecision;
            LastTotalLatencyMs = totalWatch.ElapsedMilliseconds;

            if (enableTelemetryLogging)
            {
                LogTelemetry(
                    actor, snapshot, response1, parse1, validation1,
                    true, response2, parse2, validation2,
                    true, fallbackReason, fallbackDecision, true,
                    attempt1WatchMs, attempt2WatchMs, validatorMs,
                    fallbackWatch.ElapsedMilliseconds, LastTotalLatencyMs);
            }

            return fallbackDecision;
        }

        private sealed class AttemptResult
        {
            public LLMResponseDTO Response;
            public long ElapsedMs;
        }

        private async Task<AttemptResult> SendAttemptAsync(
            string systemPrompt,
            string userPrompt)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            LLMResponseDTO response = null;
            try
            {
                if (_llmService != null)
                    response = await _llmService.SendChatCompletionAsync(systemPrompt, userPrompt);
            }
            catch (Exception ex)
            {
                response = new LLMResponseDTO
                {
                    IsSuccess = false,
                    RequestSucceeded = false,
                    ErrorMessage = "LLM transport exception: " + ex.Message,
                    ModelName = config != null ? config.ModelName : "unknown"
                };
            }

            watch.Stop();

            if (response == null)
            {
                response = new LLMResponseDTO
                {
                    IsSuccess = false,
                    RequestSucceeded = false,
                    ErrorMessage = "No LLM service is configured.",
                    ModelName = config != null ? config.ModelName : "unknown"
                };
            }

            // The service already measures HTTP latency; Stopwatch also includes
            // scheduling overhead, so keep the larger of the two for Y7.
            return new AttemptResult
            {
                Response = response,
                ElapsedMs = Math.Max(response.LatencyMs, watch.ElapsedMilliseconds)
            };
        }

        private ValidationResult2D ValidateTimed(
            TacticalDecision2D decision,
            TacticalStateSnapshot2D snapshot,
            TacticalGrid2D grid,
            TacticalDecisionParseResult parse,
            out long elapsedMs)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = _validator.Validate(decision, snapshot, grid, parse);
            watch.Stop();
            elapsedMs = watch.ElapsedMilliseconds;
            return result;
        }

        private static void AppendTransportError(
            TacticalDecision2D decision,
            TacticalDecisionParseResult parse,
            LLMResponseDTO response)
        {
            if (decision == null || parse == null || parse.Success) return;
            if (response == null || string.IsNullOrEmpty(response.ErrorMessage)) return;

            decision.Reasoning += " LLM error: " + response.ErrorMessage;
        }

        private static string BuildFallbackReason(
            ValidationResult2D validation,
            LLMResponseDTO response)
        {
            string codes = validation != null ? validation.ViolationCodesCsv : "UNKNOWN";
            if (response != null && !response.RequestSucceeded)
            {
                string error = response.ErrorMessage ?? "LLM request failed";
                if (error.Length > 160) error = error.Substring(0, 160) + "...";
                return codes + " (transport failure: " + error + ")";
            }

            string detail = validation != null ? validation.DiagnosticMessage : "validation failed";
            if (detail.Length > 220) detail = detail.Substring(0, 220) + "...";
            return codes + " after 1 corrective retry - " + detail;
        }

        private void LogTelemetry(
            CombatEntity2D actor,
            TacticalStateSnapshot2D snapshot,
            LLMResponseDTO response1,
            TacticalDecisionParseResult parse1,
            ValidationResult2D validation1,
            bool retryConducted,
            LLMResponseDTO response2,
            TacticalDecisionParseResult parse2,
            ValidationResult2D validation2,
            bool fallbackTriggered,
            string fallbackReason,
            TacticalDecision2D finalDecision,
            bool finalExecutedValid,
            long attempt1Ms,
            long attempt2Ms,
            long validatorMs,
            long fallbackMs,
            long totalMs)
        {
            if (telemetryLogger == null) return;

            string proposal1Action = parse1 != null ? (parse1.RawAction ?? string.Empty) : string.Empty;
            if (string.IsNullOrWhiteSpace(proposal1Action)) proposal1Action = "PARSE_FAILURE";

            string proposal1Target = parse1 != null ? (parse1.TargetId ?? string.Empty) : string.Empty;
            string proposal1Destination = FormatDestination(parse1);
            string proposal1Violations = validation1 != null
                ? validation1.ViolationCodesCsv
                : "UNKNOWN";

            string proposal2Action = parse2 != null ? (parse2.RawAction ?? string.Empty) : string.Empty;
            string proposal2Target = parse2 != null ? (parse2.TargetId ?? string.Empty) : string.Empty;
            string proposal2Destination = FormatDestination(parse2);
            string proposal2Violations = validation2 != null
                ? validation2.ViolationCodesCsv
                : string.Empty;

            int inputTokens = (response1 != null ? response1.PromptTokens : 0) +
                              (response2 != null ? response2.PromptTokens : 0);
            int outputTokens = (response1 != null ? response1.CompletionTokens : 0) +
                               (response2 != null ? response2.CompletionTokens : 0);
            int totalTokens = (response1 != null
                                  ? (response1.TotalTokens > 0
                                      ? response1.TotalTokens
                                      : response1.PromptTokens + response1.CompletionTokens)
                                  : 0) +
                              (response2 != null
                                  ? (response2.TotalTokens > 0
                                      ? response2.TotalTokens
                                      : response2.PromptTokens + response2.CompletionTokens)
                                  : 0);

            var finalRecord = finalDecision ?? new TacticalDecision2D(
                TacticalActionType.Idle, actor.GridPosition, null, false, "No decision.");

            string finalTarget = finalRecord.TargetEnemy != null
                ? finalRecord.TargetEnemy.EntityId
                : (finalRecord.TargetAlly != null ? finalRecord.TargetAlly.EntityId : string.Empty);

            bool isMovement = IsMovement(finalRecord.Action);
            string finalDestination = isMovement
                ? "(" + finalRecord.MoveDestination.x + "," + finalRecord.MoveDestination.y + ")"
                : string.Empty;

            var record = new TurnTelemetryRecord
            {
                RunId = runId,
                ConfigId = configId,
                ScenarioId = scenarioId,
                AgentType = ControllerType,
                HasValidator = true,
                IsCompressed = false,
                IsHybrid = true,
                ModelName = response1 != null
                    ? (response1.ModelName ?? string.Empty)
                    : (config != null ? config.ModelName : string.Empty),

                RoundNumber = snapshot != null ? snapshot.RoundNumber : 0,
                TurnNumber = snapshot != null ? snapshot.TurnNumber : 0,
                ActorId = actor != null ? actor.EntityId : string.Empty,
                ActorName = actor != null ? actor.EntityName : string.Empty,
                TraitProfile = actor != null ? actor.CurrentTraitId : string.Empty,

                // Legacy fields intentionally represent the raw round-1 proposal.
                ProposedAction = proposal1Action,
                ProposedTarget = proposal1Target,
                ProposedDestination = proposal1Destination,
                IsValid = validation1 != null && validation1.IsValid,
                ViolationCode = proposal1Violations,
                ViolationDetails = validation1 != null ? validation1.DiagnosticMessage : string.Empty,

                FinalAction = finalRecord.Action.ToString(),
                FinalTarget = finalTarget,
                FinalDestination = finalDestination,
                IsFallback = fallbackTriggered,

                ParseSucceeded = parse1 != null && parse1.Success,
                RequestSucceeded = response1 != null && response1.RequestSucceeded,
                LlmError = BuildLlmError(response1, response2),
                Reasoning = finalRecord.Reasoning,
                RawResponse = response1 != null ? (response1.RawContent ?? string.Empty) : string.Empty,

                // Hybrid detail fields.
                ProposalRound1Action = proposal1Action,
                ProposalRound1Target = proposal1Target,
                ProposalRound1Destination = proposal1Destination,
                ProposalRound1Valid = validation1 != null && validation1.IsValid,
                ProposalRound1Violations = proposal1Violations,

                RetryConducted = retryConducted,
                ProposalRound2Action = proposal2Action,
                ProposalRound2Target = proposal2Target,
                ProposalRound2Destination = proposal2Destination,
                ProposalRound2Valid = validation2 != null && validation2.IsValid,
                ProposalRound2Violations = proposal2Violations,

                FallbackTriggered = fallbackTriggered,
                FallbackReason = fallbackReason ?? string.Empty,
                FinalExecutedAction = finalRecord.Action.ToString(),
                FinalExecutedValid = finalExecutedValid,

                LatencyMs = totalMs,
                LatencyLlmAttempt1Ms = attempt1Ms,
                LatencyLlmAttempt2Ms = attempt2Ms,
                LatencyValidatorMs = validatorMs,
                LatencyFallbackMs = fallbackMs,
                LatencyTotalMs = totalMs,

                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = totalTokens,
                TimestampUtc = DateTime.UtcNow.ToString("o")
            };

            telemetryLogger.LogTurnRecord(record);
        }

        private static string BuildLlmError(LLMResponseDTO first, LLMResponseDTO second)
        {
            var parts = new List<string>();
            if (first != null && !string.IsNullOrEmpty(first.ErrorMessage))
                parts.Add("round1: " + first.ErrorMessage);
            if (second != null && !string.IsNullOrEmpty(second.ErrorMessage))
                parts.Add("round2: " + second.ErrorMessage);
            return string.Join(" | ", parts.ToArray());
        }

        private static string FormatDestination(TacticalDecisionParseResult parse)
        {
            if (parse == null || !parse.HasDestination) return string.Empty;
            if (!parse.DestinationFieldsValid) return "(invalid destination)";
            return "(" + parse.DestinationX + "," + parse.DestinationY + ")";
        }

        private static bool IsMovement(TacticalActionType action)
        {
            return action == TacticalActionType.Move ||
                   action == TacticalActionType.MoveToCover ||
                   action == TacticalActionType.MoveAndShoot ||
                   action == TacticalActionType.MoveAndCastSkill ||
                   action == TacticalActionType.Flee;
        }

        private void ResetTurnState()
        {
            LastSnapshot = null;
            LastParseRound1 = null;
            LastParseRound2 = null;
            LastResponseRound1 = null;
            LastResponseRound2 = null;
            LastValidationRound1 = null;
            LastValidationRound2 = null;
            LastDecision = null;
            LastSystemPrompt = null;
            LastUserPrompt = null;
            LastFeedbackPrompt = null;
            RetryConducted = false;
            FallbackTriggered = false;
            FallbackReason = string.Empty;
            _lastDecisionTag = string.Empty;
            LastTotalLatencyMs = 0;
        }

        private void EnsureDependencies()
        {
            if (_extractor == null) _extractor = new TacticalStateExtractor2D(maxRecentLogs);
            if (_promptBuilder == null) _promptBuilder = new TacticalPromptBuilder2D();
            if (_validator == null) _validator = new TacticalActionValidator2D();
            if (_feedbackBuilder == null) _feedbackBuilder = new FeedbackPromptBuilder2D();
            if (_fallbackBridge == null) _fallbackBridge = new HFSMFallbackBridge();

            if (config == null) config = LLMConfig.CreateRuntimeDefault();

            if (_llmService == null)
            {
                if (llmService == null) llmService = GetComponent<LLMService2D>();
                if (llmService == null) llmService = FindFirstObjectByType<LLMService2D>();
                if (llmService == null) llmService = gameObject.AddComponent<LLMService2D>();
                _llmService = llmService;
            }

            if (llmService != null) llmService.Configure(config);

            if (telemetryLogger == null)
            {
                telemetryLogger = FindFirstObjectByType<CombatTelemetryLogger2D>();
                if (telemetryLogger == null)
                    telemetryLogger = gameObject.AddComponent<CombatTelemetryLogger2D>();
            }
        }
    }
}
