using RPGStrategy.Core.Data;

namespace RPGStrategy.AI.Baseline
{
    public interface IHFSMState
    {
        string StateName { get; }
        AgentDecision Evaluate(CombatStateSnapshot snapshot);
    }
}
