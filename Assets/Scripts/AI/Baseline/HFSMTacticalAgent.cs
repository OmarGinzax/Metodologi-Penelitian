using System.Collections.Generic;
using System.Threading.Tasks;
using RPGStrategy.Core.Combat;
using UnityEngine;

namespace RPGStrategy.AI.Baseline
{
    /// <summary>
    /// Wrapper controller untuk HFSM baseline yang sudah ada.
    /// Memungkinkan SimpleTacticalHFSM tetap dipakai sebagai pembanding deterministik
    /// melalui antarmuka ITacticalAgentController.
    /// </summary>
    public class HFSMTacticalAgent : TacticalAgentControllerBase
    {
        private readonly SimpleTacticalHFSM _hfsm = new SimpleTacticalHFSM();

        public override string ControllerType => "HFSM_Baseline";

        public SimpleTacticalHFSM Hfsm => _hfsm;

        public override Task<TacticalDecision2D> DecideTurnAsync(
            CombatEntity2D actor,
            TacticalGrid2D grid,
            IReadOnlyList<CombatEntity2D> combatants,
            IReadOnlyList<string> history)
        {
            if (actor == null || grid == null)
            {
                return Task.FromResult(
                    new TacticalDecision2D(TacticalActionType.Idle, Vector2Int.zero, null, false, "Invalid HFSM state."));
            }

            var list = combatants as List<CombatEntity2D>;
            if (list == null)
            {
                list = new List<CombatEntity2D>();
                if (combatants != null)
                {
                    for (int i = 0; i < combatants.Count; i++)
                    {
                        if (combatants[i] != null) list.Add(combatants[i]);
                    }
                }
            }

            var decision = _hfsm.DecideTurn(actor, grid, list);
            return Task.FromResult(decision);
        }
    }
}
