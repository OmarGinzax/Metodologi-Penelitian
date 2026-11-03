using System.Collections.Generic;
using RPGStrategy.AI.Baseline;
using RPGStrategy.Core.Combat;
using UnityEngine;

namespace RPGStrategy.AI.Hybrid
{
    /// <summary>
    /// Jembatan deterministic ke SimpleTacticalHFSM untuk failsafe Hybrid.
    /// Keputusan fallback selalu diberi tag alasan agar transparan di Inspector
    /// dan dapat direkonstruksi dari telemetri.
    /// </summary>
    public class HFSMFallbackBridge
    {
        private readonly SimpleTacticalHFSM _hfsm = new SimpleTacticalHFSM();

        public TacticalDecision2D GetSafeFallbackDecision(
            CombatEntity2D actor,
            TacticalGrid2D grid,
            IReadOnlyList<CombatEntity2D> combatants)
        {
            return GetSafeFallbackDecision(actor, grid, combatants, "Validation failed after retry");
        }

        public TacticalDecision2D GetSafeFallbackDecision(
            CombatEntity2D actor,
            TacticalGrid2D grid,
            IReadOnlyList<CombatEntity2D> combatants,
            string reason)
        {
            if (actor == null || grid == null)
            {
                return new TacticalDecision2D(
                    TacticalActionType.Idle,
                    actor != null ? actor.GridPosition : Vector2Int.zero,
                    null,
                    false,
                    BuildReason(reason, "Invalid actor or grid state."));
            }

            var list = new List<CombatEntity2D>();
            if (combatants != null)
            {
                for (int i = 0; i < combatants.Count; i++)
                {
                    if (combatants[i] != null) list.Add(combatants[i]);
                }
            }

            var decision = _hfsm.DecideTurn(actor, grid, list);
            if (decision == null)
            {
                return new TacticalDecision2D(
                    TacticalActionType.Idle,
                    actor.GridPosition,
                    null,
                    false,
                    BuildReason(reason, "HFSM returned a null decision."));
            }

            string originalReasoning = decision.Reasoning ?? string.Empty;
            decision.Reasoning = BuildReason(reason, originalReasoning);
            return decision;
        }

        private static string BuildReason(string reason, string details)
        {
            string tag = string.IsNullOrWhiteSpace(reason) ? "unspecified violation" : reason.Trim();
            string body = string.IsNullOrWhiteSpace(details) ? "HFSM deterministic safe action." : details.Trim();
            return "[HFSM FALLBACK - REASON: " + tag + "] " + body;
        }
    }
}
