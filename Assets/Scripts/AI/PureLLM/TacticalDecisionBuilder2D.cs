using System;
using System.Collections.Generic;
using RPGStrategy.AI.Baseline;
using RPGStrategy.Core.Combat;
using UnityEngine;

namespace RPGStrategy.AI.PureLLM
{
    /// <summary>
    /// Membangun TacticalDecision2D dari hasil parsing JSON LLM.
    /// Dipakai bersama oleh PureLLMTacticalAgent dan HybridTacticalAgent agar
    /// pemetaan aksi/target/destination identik pada kedua perlakuan penelitian.
    /// </summary>
    public static class TacticalDecisionBuilder2D
    {
        public static TacticalDecision2D Build(
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
                    TacticalActionType.Idle, actor != null ? actor.GridPosition : Vector2Int.zero,
                    null, false, reason);
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

        public static CombatEntity2D ResolveTarget(string targetId, IReadOnlyList<CombatEntity2D> combatants)
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
    }
}
