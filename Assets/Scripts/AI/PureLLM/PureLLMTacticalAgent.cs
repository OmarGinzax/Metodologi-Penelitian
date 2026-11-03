using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RPGStrategy.AI.Baseline;
using RPGStrategy.AI.LLM;
using RPGStrategy.AI.Telemetry;
using RPGStrategy.Core.Combat;
using UnityEngine;

namespace RPGStrategy.AI.PureLLM
{
    /// <summary>
    /// Agen Pure LLM (X1 = LLM Murni, X2 = Tanpa Pembatasan / Zero HFSM Assistance).
    ///
    /// Pipeline:
    ///   Extract snapshot -> Build prompt -> Call LLM -> Parse JSON -> Classify H1-H5
    ///   -> Log telemetry -> Return decision.
    ///
    /// Agen ini TIDAK memakai HFSM, validator, retry, atau fallback korektif.
    /// Keputusan yang dihasilkan akan dieksekusi apa adanya oleh BattleManager2D.
    /// </summary>
    public class PureLLMTacticalAgent : TacticalAgentControllerBase
    {
        [Header("LLM Infrastructure")]
        [SerializeField] private LLMConfig config;
        [SerializeField] private LLMService2D llmService;

        [Header("Telemetry")]
        [SerializeField] private CombatTelemetryLogger2D telemetryLogger;
        [SerializeField] private bool enableTelemetryLogging = true;

        [Header("Research Metadata")]
        [SerializeField] private string runId = "RUN_001";
        [SerializeField] private string configId = "CFG-02";
        [SerializeField] private string scenarioId = "SCEN_LIVE";

        [Header("Context")]
        [SerializeField] private int maxRecentLogs = 8;

        private TacticalStateExtractor2D _extractor;
        private TacticalPromptBuilder2D _promptBuilder;
        private HallucinationClassifier _classifier;
        private ILLMService2D _llmService;

        public override string ControllerType => "Pure_LLM_Unconstrained";

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

        // --- Last-turn inspection (live Inspector / debugging) -----------------------
        public TacticalStateSnapshot2D LastSnapshot { get; private set; }
        public TacticalDecisionParseResult LastParse { get; private set; }
        public LLMResponseDTO LastResponse { get; private set; }
        public HallucinationReport LastHallucination { get; private set; }
        public string LastSystemPrompt { get; private set; }
        public string LastUserPrompt { get; private set; }

        private void Awake()
        {
            EnsureDependencies();
        }

        /// <summary>Konfigurasi eksplisit untuk pengujian offline / setup editor.</summary>
        public void Configure(LLMConfig newConfig, ILLMService2D service, CombatTelemetryLogger2D logger = null)
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

            if (actor == null || grid == null)
            {
                return new TacticalDecision2D(
                    TacticalActionType.Idle, Vector2Int.zero, null, false,
                    "Invalid state: actor or grid is null.");
            }

            int round = TurnContext != null ? TurnContext.RoundNumber : 1;
            int turn = TurnContext != null ? TurnContext.TurnNumber : 0;

            var snapshot = _extractor.ExtractSnapshot(actor, grid, combatants, round, turn, history);
            LastSnapshot = snapshot;

            string systemPrompt = _promptBuilder.BuildSystemPrompt(actor);
            string userPrompt = _promptBuilder.BuildUserPrompt(snapshot);
            LastSystemPrompt = systemPrompt;
            LastUserPrompt = userPrompt;

            LLMResponseDTO response = null;
            if (_llmService != null)
            {
                response = await _llmService.SendChatCompletionAsync(systemPrompt, userPrompt);
            }

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
            LastResponse = response;

            var parse = TacticalDecisionJsonParser.Parse(response.RawContent);
            LastParse = parse;

            var decision = BuildDecision(parse, actor, grid, combatants);

            // When parsing fails, append the transport/HTTP cause so the HUD and Console
            // explain *why* the turn became an H5 no-op (e.g. model not found).
            if (!parse.Success && response != null && !string.IsNullOrEmpty(response.ErrorMessage))
                decision.Reasoning += " LLM error: " + response.ErrorMessage;

            var report = _classifier.EvaluateDecision(decision, snapshot, grid, parse);
            LastHallucination = report;

            if (enableTelemetryLogging)
                LogTelemetry(actor, snapshot, decision, parse, response, report);

            return decision;
        }

        private TacticalDecision2D BuildDecision(
            TacticalDecisionParseResult parse,
            CombatEntity2D actor,
            TacticalGrid2D grid,
            IReadOnlyList<CombatEntity2D> combatants)
        {
            if (parse == null || !parse.Success)
            {
                string reason = "No-op: LLM output could not be parsed as a valid action JSON.";
                if (parse != null && !string.IsNullOrEmpty(parse.Error)) reason += " " + parse.Error;
                return new TacticalDecision2D(
                    TacticalActionType.Idle, actor.GridPosition, null, false, reason);
            }

            TacticalActionType action = parse.MappedAction;

            Vector2Int requestedDestination = actor.GridPosition;
            if (parse.HasDestination && parse.DestinationFieldsValid)
                requestedDestination = new Vector2Int(parse.DestinationX, parse.DestinationY);

            // Engine hard boundary: out-of-bounds coordinates are clamped, not corrected.
            Vector2Int clampedDestination = grid != null
                ? grid.ClampToBounds(requestedDestination)
                : requestedDestination;

            bool isMovementAction =
                action == TacticalActionType.Move ||
                action == TacticalActionType.MoveToCover ||
                action == TacticalActionType.MoveAndShoot ||
                action == TacticalActionType.MoveAndCastSkill ||
                action == TacticalActionType.Flee;

            Vector2Int executionDestination = isMovementAction
                ? clampedDestination
                : actor.GridPosition;

            CombatEntity2D resolvedTarget = ResolveTarget(parse.TargetId, combatants);
            CombatEntity2D targetEnemy = null;
            CombatEntity2D targetAlly = null;
            bool isFlanking = false;

            if (action == TacticalActionType.Shoot || action == TacticalActionType.MoveAndShoot)
            {
                targetEnemy = resolvedTarget;

                Vector2Int shootPosition = actor.GridPosition;
                if (action == TacticalActionType.MoveAndShoot)
                    shootPosition = clampedDestination;

                if (targetEnemy != null && grid != null)
                    isFlanking = grid.IsFlanked(targetEnemy.GridPosition, shootPosition);
            }
            else if (action == TacticalActionType.CastSkill || action == TacticalActionType.MoveAndCastSkill)
            {
                targetAlly = resolvedTarget;
            }

            string skillId = action == TacticalActionType.CastSkill || action == TacticalActionType.MoveAndCastSkill
                ? TacticalSkillCatalog2D.Normalize(parse.SkillId)
                : string.Empty;

            string reasoning = string.IsNullOrWhiteSpace(parse.Reasoning)
                ? "LLM selected " + (parse.RawAction ?? action.ToString()) + " without providing a rationale."
                : parse.Reasoning;

            return new TacticalDecision2D(
                action,
                executionDestination,
                targetEnemy,
                targetAlly,
                skillId,
                isFlanking,
                reasoning);
        }

        private static CombatEntity2D ResolveTarget(string targetId, IReadOnlyList<CombatEntity2D> combatants)
        {
            if (string.IsNullOrWhiteSpace(targetId) || combatants == null) return null;

            string wanted = targetId.Trim();
            for (int i = 0; i < combatants.Count; i++)
            {
                var candidate = combatants[i];
                if (candidate == null) continue;
                if (string.Equals(candidate.EntityId, wanted, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return null;
        }

        private void LogTelemetry(
            CombatEntity2D actor,
            TacticalStateSnapshot2D snapshot,
            TacticalDecision2D decision,
            TacticalDecisionParseResult parse,
            LLMResponseDTO response,
            HallucinationReport report)
        {
            if (telemetryLogger == null) return;

            string proposedAction = parse != null && !string.IsNullOrWhiteSpace(parse.RawAction)
                ? parse.RawAction
                : decision.Action.ToString();

            string proposedTarget = parse != null ? (parse.TargetId ?? string.Empty) : string.Empty;

            string proposedDestination = string.Empty;
            if (parse != null && parse.HasDestination && parse.DestinationFieldsValid)
                proposedDestination = "(" + parse.DestinationX + "," + parse.DestinationY + ")";

            string finalTarget = decision.TargetEnemy != null
                ? decision.TargetEnemy.EntityId
                : (decision.TargetAlly != null ? decision.TargetAlly.EntityId : proposedTarget);

            bool isMovement = decision.Action == TacticalActionType.Move ||
                              decision.Action == TacticalActionType.MoveToCover ||
                              decision.Action == TacticalActionType.MoveAndShoot ||
                              decision.Action == TacticalActionType.MoveAndCastSkill ||
                              decision.Action == TacticalActionType.Flee;

            var record = new TurnTelemetryRecord
            {
                RunId = runId,
                ConfigId = configId,
                ScenarioId = scenarioId,
                AgentType = ControllerType,
                HasValidator = false,
                IsCompressed = false,
                ModelName = response != null ? (response.ModelName ?? string.Empty) : string.Empty,

                RoundNumber = snapshot != null ? snapshot.RoundNumber : 0,
                TurnNumber = snapshot != null ? snapshot.TurnNumber : 0,
                ActorId = actor != null ? actor.EntityId : string.Empty,
                ActorName = actor != null ? actor.EntityName : string.Empty,
                TraitProfile = actor != null ? actor.CurrentTraitId : string.Empty,

                ProposedAction = proposedAction,
                ProposedTarget = proposedTarget,
                ProposedDestination = proposedDestination,
                IsValid = report == null || !report.IsIllogical,
                ViolationCode = report != null ? report.ViolationCodesCsv : "NONE",
                ViolationDetails = report != null ? report.Details : string.Empty,

                FinalAction = decision.Action.ToString(),
                FinalTarget = finalTarget,
                FinalDestination = isMovement
                    ? "(" + decision.MoveDestination.x + "," + decision.MoveDestination.y + ")"
                    : string.Empty,
                IsFallback = false,

                ParseSucceeded = parse != null && parse.Success,
                RequestSucceeded = response != null && response.RequestSucceeded,
                LlmError = response != null ? (response.ErrorMessage ?? string.Empty) : "No response.",
                Reasoning = decision.Reasoning,
                RawResponse = response != null ? (response.RawContent ?? string.Empty) : string.Empty,

                LatencyMs = response != null ? response.LatencyMs : 0,
                InputTokens = response != null ? response.PromptTokens : 0,
                OutputTokens = response != null ? response.CompletionTokens : 0,
                TotalTokens = response != null
                    ? (response.TotalTokens > 0 ? response.TotalTokens : response.PromptTokens + response.CompletionTokens)
                    : 0,
                TimestampUtc = DateTime.UtcNow.ToString("o")
            };

            telemetryLogger.LogTurnRecord(record);
        }

        private void EnsureDependencies()
        {
            if (_extractor == null) _extractor = new TacticalStateExtractor2D(maxRecentLogs);
            if (_promptBuilder == null) _promptBuilder = new TacticalPromptBuilder2D();
            if (_classifier == null) _classifier = new HallucinationClassifier();

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
