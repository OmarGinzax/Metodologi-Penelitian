using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RPGStrategy.AI.Core;
using RPGStrategy.Core.Data;

namespace RPGStrategy.AI.Baseline
{
    public class HFSMController : IAgentController
    {
        public string ControllerName => "HFSM_Baseline";

        private readonly EmergencyState _emergencyState = new EmergencyState();
        private readonly TacticalSupportState _supportState = new TacticalSupportState();
        private readonly OffensiveState _offensiveState = new OffensiveState();
        private readonly ResourceConservingState _conservingState = new ResourceConservingState();

        public IHFSMState CurrentActiveState { get; private set; }

        public AgentDecision DecideAction(CombatStateSnapshot snapshot)
        {
            if (snapshot == null || snapshot.CurrentActor == null)
            {
                return new AgentDecision("unknown", "Action_Defend", "self", "Null snapshot fallback");
            }

            // 1. Evaluasi Hierarki Status (Strict Priority Hierarchy)
            CurrentActiveState = EvaluateRootTransition(snapshot);

            // 2. Evaluasi keputusan deterministik dari sub-state aktif
            return CurrentActiveState.Evaluate(snapshot);
        }

        public Task<AgentDecision> DecideActionAsync(CombatStateSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            // HFSM murni bersifat komputasi lokal sinkron dan instan
            return Task.FromResult(DecideAction(snapshot));
        }

        private IHFSMState EvaluateRootTransition(CombatStateSnapshot snapshot)
        {
            var actor = snapshot.CurrentActor;

            // Prioritas 1: Emergency (HP <= 25%)
            if (actor.HPPercentage <= 25f)
            {
                return _emergencyState;
            }

            // Prioritas 2: Support Sekutu Kritis (Ada sekutu dengan HP <= 30% atau panik)
            bool hasWoundedAlly = snapshot.Allies.Any(a => a.IsAlive && (a.HPPercentage <= 30f || a.ComposureLevel == "Panicked"));
            if (hasWoundedAlly)
            {
                return _supportState;
            }

            // Prioritas 3: MP Habis
            if (actor.CurrentMP <= 0)
            {
                return _conservingState;
            }

            // Prioritas 4: Offensive (Default combat pressure)
            return _offensiveState;
        }
    }
}
