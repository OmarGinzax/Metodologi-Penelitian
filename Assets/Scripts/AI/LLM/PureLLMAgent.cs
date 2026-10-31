using System.Threading;
using System.Threading.Tasks;
using RPGStrategy.AI.Core;
using RPGStrategy.Core.Combat;
using RPGStrategy.Core.Data;

namespace RPGStrategy.AI.LLM
{
    /// <summary>
    /// Pure LLM Agent Controller (Baseline Variabel X1).
    /// Mengirimkan prompt langsung ke LLM tanpa mekanisme verifikasi guardrail atau retry loop.
    /// Digunakan untuk mengukur baseline tingkat halusinasi (Y2) dan latensi (Y7).
    /// </summary>
    public class PureLLMAgent : IAgentController
    {
        private readonly LLMService _llmService;
        public string ControllerName => "Pure_LLM_Agent";

        public LLMCallResult LastCallResult { get; private set; }

        public PureLLMAgent(LLMService llmService)
        {
            _llmService = llmService;
        }

        public async Task<AgentDecision> DecideActionAsync(CombatStateSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (snapshot == null) return FallbackDefend();

            string systemPrompt = PromptBuilder.BuildSystemPrompt(snapshot);
            string userPrompt = PromptBuilder.BuildUserPrompt(snapshot);

            LastCallResult = await _llmService.RequestDecisionAsync(systemPrompt, userPrompt, snapshot, cancellationToken);

            if (LastCallResult != null && LastCallResult.Success && LastCallResult.ParsedDecision != null)
            {
                return LastCallResult.ParsedDecision;
            }

            // Jika offline/gagal total parsing tanpa guardrail, fallback defend
            return FallbackDefend(snapshot.CurrentActor?.EntityId);
        }

        public AgentDecision DecideAction(CombatStateSnapshot snapshot)
        {
            // Sinkron wrapper untuk simulasi headless non-async jika dibutuhkan
            return DecideActionAsync(snapshot).GetAwaiter().GetResult();
        }

        private AgentDecision FallbackDefend(string actorId = "Unknown")
        {
            return new AgentDecision
            {
                ActorId = actorId,
                Compliance = "ADAPT",
                RadioChatter = "Communications jammed or garbled! Taking cover and bracing!",
                ActionId = "Action_Defend",
                TargetId = actorId,
                Reasoning = "Pure LLM fallback executed due to unreachable API or invalid output format."
            };
        }
    }
}
