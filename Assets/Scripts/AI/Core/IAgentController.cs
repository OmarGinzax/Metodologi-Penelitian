using System.Threading;
using System.Threading.Tasks;
using RPGStrategy.Core.Data;

namespace RPGStrategy.AI.Core
{
    public interface IAgentController
    {
        string ControllerName { get; }

        /// <summary>
        /// Pengambilan keputusan secara asinkron (mendukung panggilan HTTP LLM API tanpa blocking).
        /// </summary>
        Task<AgentDecision> DecideActionAsync(CombatStateSnapshot snapshot, CancellationToken cancellationToken = default);

        /// <summary>
        /// Pengambilan keputusan sinkron untuk eksekusi cepat (HFSM murni / testing headless).
        /// </summary>
        AgentDecision DecideAction(CombatStateSnapshot snapshot);
    }
}
