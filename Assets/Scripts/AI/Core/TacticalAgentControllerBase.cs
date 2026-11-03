using System.Collections.Generic;
using System.Threading.Tasks;
using RPGStrategy.AI.Baseline;
using RPGStrategy.Core.Combat;
using UnityEngine;

namespace RPGStrategy.AI
{
    /// <summary>
    /// Base MonoBehaviour untuk semua controller agen taktis.
    /// Turunan dapat dipasang langsung di Inspector pada BattleManager2D.
    /// </summary>
    public abstract class TacticalAgentControllerBase : MonoBehaviour, ITacticalAgentController
    {
        public TacticalTurnContext TurnContext { get; set; } = new TacticalTurnContext();

        public abstract string ControllerType { get; }

        public abstract Task<TacticalDecision2D> DecideTurnAsync(
            CombatEntity2D actor,
            TacticalGrid2D grid,
            IReadOnlyList<CombatEntity2D> combatants,
            IReadOnlyList<string> history);
    }
}
