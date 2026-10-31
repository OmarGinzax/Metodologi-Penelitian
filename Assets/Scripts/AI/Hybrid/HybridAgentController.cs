using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using RPGStrategy.AI.Core;
using RPGStrategy.AI.LLM;
using RPGStrategy.Core.Data;

namespace RPGStrategy.AI.Hybrid
{
    /// <summary>
    /// M5 – Hybrid Agent Controller (Variabel X1 = Hybrid Guardrail).
    /// Pipeline: LLM Call → ActionValidator → (1x Corrective Retry jika invalid) → Failsafe Fallback.
    /// Mengukur: Illogical/Invalid Action Rate (Y2), Latency (Y7), Tokens (Y6).
    /// LLM OWNS all tactical/psychological decisions; guardrail ONLY checks mechanical legality.
    /// </summary>
    public class HybridAgentController : IAgentController
    {
        private readonly LLMService _llmService;

        public string ControllerName => "Hybrid_Guardrail_Agent";

        // Telemetry yang diexpose untuk TelemetryLogger
        public LLMCallResult LastCallResult { get; private set; }
        public ValidationResult LastValidationResult { get; private set; }
        public ValidationResult LastRetryValidationResult { get; private set; }
        public bool FallbackActivated { get; private set; }
        public int RetryAttempted { get; private set; }
        public HallucinationType LastHallucinationType => 
            LastValidationResult != null && !LastValidationResult.IsValid ? LastValidationResult.HallucinationCode : HallucinationType.None;

        public HybridAgentController(LLMService llmService)
        {
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
        }

        public async Task<AgentDecision> DecideActionAsync(CombatStateSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            // Reset telemetry per turn
            FallbackActivated = false;
            RetryAttempted = 0;
            LastValidationResult = null;
            LastRetryValidationResult = null;

            if (snapshot == null || snapshot.CurrentActor == null)
            {
                FallbackActivated = true;
                return BuildFallback("Unknown", "Snapshot is null.");
            }

            string actorId = snapshot.CurrentActor.EntityId;
            string actorName = snapshot.CurrentActor.Name;

            // ─── ATTEMPT 1: Initial LLM Call ──────────────────────────────────────
            string sysPrompt = PromptBuilder.BuildSystemPrompt(snapshot);
            string userPrompt = PromptBuilder.BuildUserPrompt(snapshot);

            LastCallResult = await _llmService.RequestDecisionAsync(sysPrompt, userPrompt, snapshot, cancellationToken);

            if (LastCallResult == null || !LastCallResult.Success || LastCallResult.ParsedDecision == null)
            {
                Debug.LogWarning($"[HybridAgent] {actorName}: LLM call failed entirely. Activating failsafe.");
                FallbackActivated = true;
                LastValidationResult = ValidationResult.Fail(HallucinationType.H6_FormatParsingFailure,
                    "LLM API call failed or response could not be parsed.");
                return BuildFallback(actorId, "API unavailable or JSON malformed.");
            }

            AgentDecision decision = LastCallResult.ParsedDecision;
            decision.ActorId = actorId;

            LastValidationResult = ActionValidator.Validate(snapshot, decision);

            if (LastValidationResult.IsValid)
            {
                return decision; // ✅ Valid on first attempt
            }

            // ─── ATTEMPT 2: Corrective Feedback Retry ─────────────────────────────
            RetryAttempted = 1;
            Debug.LogWarning($"[HybridAgent] {actorName}: Invalid action detected – {LastValidationResult.HallucinationCode}. Sending corrective retry prompt.");

            string retryUserPrompt = PromptBuilder.BuildRetryPrompt(snapshot, decision, LastValidationResult);
            var retryResult = await _llmService.RequestDecisionAsync(sysPrompt, retryUserPrompt, snapshot, cancellationToken);

            // Accumulate token usage
            if (retryResult != null && retryResult.Success && retryResult.ParsedDecision != null)
            {
                AgentDecision retryDecision = retryResult.ParsedDecision;
                retryDecision.ActorId = actorId;

                LastRetryValidationResult = ActionValidator.Validate(snapshot, retryDecision);
                if (LastRetryValidationResult.IsValid)
                {
                    return retryDecision; // ✅ Valid after retry
                }

                Debug.LogWarning($"[HybridAgent] {actorName}: Retry also invalid – {LastRetryValidationResult.HallucinationCode}. Activating failsafe.");
            }
            else
            {
                Debug.LogWarning($"[HybridAgent] {actorName}: Retry LLM call also failed. Activating failsafe.");
                LastRetryValidationResult = ValidationResult.Fail(HallucinationType.H6_FormatParsingFailure,
                    "Retry LLM call failed.");
            }

            // ─── FAILSAFE FALLBACK ────────────────────────────────────────────────
            FallbackActivated = true;
            return BuildFallback(actorId, $"H{(int)LastValidationResult.HallucinationCode}: {LastValidationResult.ErrorFeedbackMessage}");
        }

        public AgentDecision DecideAction(CombatStateSnapshot snapshot)
        {
            return DecideActionAsync(snapshot).GetAwaiter().GetResult();
        }

        private static AgentDecision BuildFallback(string actorId, string reason)
        {
            return new AgentDecision
            {
                ActorId = actorId,
                Compliance = "ADAPT",
                ActionId = "Action_Defend",
                TargetId = actorId,
                RadioChatter = "Taking defensive position! Awaiting clearer orders!",
                Reasoning = $"[GUARDRAIL_FALLBACK] {reason}"
            };
        }
    }
}
