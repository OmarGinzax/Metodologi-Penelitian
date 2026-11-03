using System;
using System.Collections.Generic;
using System.Text;
using RPGStrategy.AI.Baseline;
using RPGStrategy.AI.PureLLM;
using RPGStrategy.Core.Combat;
using UnityEngine;

namespace RPGStrategy.AI.Hybrid
{
    /// <summary>
    /// Guardrail deterministik untuk arsitektur Hybrid.
    /// Memeriksa usulan LLM melalui 5 lapis:
    ///   1. Integritas format/parsing (H5)
    ///   2. Konsistensi protokol aksi (H4)
    ///   3. Keabsahan target (H1)
    ///   4. Kepemilikan & cooldown skill (H2)
    ///   5. Geometri, jangkauan, dan Line of Sight (H3)
    ///
    /// Validator ini tidak mengubah keputusan; ia hanya menerima/menolak dan
    /// menghasilkan diagnosa untuk prompt korektif.
    /// </summary>
    public class TacticalActionValidator2D
    {
        public ValidationResult2D Validate(
            TacticalDecision2D decision,
            TacticalStateSnapshot2D snapshot,
            TacticalGrid2D grid,
            TacticalDecisionParseResult parse)
        {
            var result = new ValidationResult2D();

            if (decision == null)
            {
                result.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency,
                    "Decision object is null.");
                return result;
            }

            PopulateRejectedProposal(result, decision, parse);

            // ---------------------------------------------------------------
            // Lapis 1: syntax / parsing integrity (H5)
            // ---------------------------------------------------------------
            if (parse != null)
            {
                if (!parse.Success)
                {
                    result.Add(HallucinationCategory2D.H5_OutputSyntaxFailure,
                        string.IsNullOrEmpty(parse.Error)
                            ? "LLM output is not valid JSON."
                            : parse.Error);
                    result.SuggestedCorrection = BuildFeasibleOptions(snapshot, grid);
                    return result;
                }

                if (parse.HasDestination && !parse.DestinationFieldsValid)
                {
                    result.Add(HallucinationCategory2D.H5_OutputSyntaxFailure,
                        "Destination object is missing numeric integer x/y fields.");
                    result.SuggestedCorrection = BuildFeasibleOptions(snapshot, grid);
                    return result;
                }
            }

            if (snapshot == null || snapshot.CurrentActor == null)
            {
                result.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency,
                    "Tactical snapshot or current actor context is missing.");
                return result;
            }

            var actor = snapshot.ActorEntity;
            if (actor == null)
            {
                result.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency,
                    "Actor entity reference is missing from the snapshot.");
                return result;
            }

            if (grid == null)
            {
                result.Add(HallucinationCategory2D.H3_SpatialGeometryViolation,
                    "Tactical grid reference is missing.");
                result.SuggestedCorrection = BuildFeasibleOptions(snapshot, grid);
                return result;
            }

            // ---------------------------------------------------------------
            // Lapis 2: semantic action protocol (H4)
            // ---------------------------------------------------------------
            if (parse != null && !parse.ActionRecognized)
            {
                result.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency,
                    "Unknown action '" + parse.RawAction + "'.");
                result.SuggestedCorrection = BuildFeasibleOptions(snapshot, grid);
                return result;
            }

            TacticalActionType action = decision.Action;
            bool isShoot = action == TacticalActionType.Shoot ||
                           action == TacticalActionType.MoveAndShoot;
            bool isSkill = action == TacticalActionType.CastSkill ||
                           action == TacticalActionType.MoveAndCastSkill;
            bool isMovement = action == TacticalActionType.Move ||
                              action == TacticalActionType.MoveToCover ||
                              action == TacticalActionType.MoveAndShoot ||
                              action == TacticalActionType.MoveAndCastSkill ||
                              action == TacticalActionType.Flee;

            string rawTargetId = parse != null ? parse.TargetId : null;
            bool targetIdProvided = !string.IsNullOrWhiteSpace(rawTargetId);

            if (isShoot && !targetIdProvided && decision.TargetEnemy == null)
            {
                result.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency,
                    "Action '" + action + "' requires a non-null target_id.");
            }

            if (isSkill)
            {
                if (!targetIdProvided && decision.TargetAlly == null)
                {
                    result.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency,
                        "Action '" + action + "' requires a non-null target_id.");
                }

                if (string.IsNullOrWhiteSpace(decision.SkillId))
                {
                    result.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency,
                        "Action '" + action + "' requires a non-null skill_id.");
                }
            }

            Vector2Int? destination = ResolveDestination(parse, decision, isMovement);
            if (isMovement && !destination.HasValue)
            {
                result.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency,
                    "Action '" + action + "' requires a destination object with x/y.");
            }

            // ---------------------------------------------------------------
            // Lapis 3: target validity (H1)
            // ---------------------------------------------------------------
            if (isShoot)
            {
                ValidateEnemyTarget(decision.TargetEnemy, rawTargetId, actor, result);
            }
            else if (isSkill)
            {
                ValidateAllyTarget(decision.TargetAlly, rawTargetId, actor, result);
            }

            // ---------------------------------------------------------------
            // Lapis 4: skill ownership & cooldown (H2)
            // ---------------------------------------------------------------
            if (isSkill)
            {
                if (!string.IsNullOrWhiteSpace(decision.SkillId))
                {
                    if (!TacticalSkillCatalog2D.ActorOwnsSkill(actor, decision.SkillId))
                    {
                        result.Add(HallucinationCategory2D.H2_SkillCooldownOrOwnership,
                            "Actor '" + actor.EntityName + "' does not own skill '" +
                            decision.SkillId + "'.");
                    }
                }

                if (!actor.IsSkillReady)
                {
                    result.Add(HallucinationCategory2D.H2_SkillCooldownOrOwnership,
                        "Skill '" + decision.SkillId + "' is on cooldown (" +
                        actor.SkillCooldownTurns + " turn(s) remaining).");
                }
            }

            // ---------------------------------------------------------------
            // Lapis 5: spatial geometry, range, LoS (H3)
            // ---------------------------------------------------------------
            if (isMovement && destination.HasValue)
            {
                Vector2Int requested = destination.Value;

                if (!grid.IsInBounds(requested))
                {
                    result.Add(HallucinationCategory2D.H3_SpatialGeometryViolation,
                        "Destination (" + requested.x + ", " + requested.y +
                        ") is outside grid bounds.");
                }
                else if (requested != actor.GridPosition &&
                         !IsReachable(actor, requested, grid))
                {
                    result.Add(HallucinationCategory2D.H3_SpatialGeometryViolation,
                        "Destination (" + requested.x + ", " + requested.y +
                        ") is not reachable within MoveRange " + actor.MoveRange + ".");
                }
            }

            if (isShoot && decision.TargetEnemy != null &&
                decision.TargetEnemy.IsAlive && decision.TargetEnemy.Team != actor.Team)
            {
                Vector2Int shootPosition = actor.GridPosition;
                if (action == TacticalActionType.MoveAndShoot && destination.HasValue &&
                    grid.IsInBounds(destination.Value))
                {
                    shootPosition = destination.Value;
                }

                int distance = grid.GetDistance(shootPosition, decision.TargetEnemy.GridPosition);
                if (distance > actor.AttackRange)
                {
                    result.Add(HallucinationCategory2D.H3_SpatialGeometryViolation,
                        "Target distance " + distance + " exceeds AttackRange " +
                        actor.AttackRange + ".");
                }
                else if (!grid.HasLineOfSight(shootPosition, decision.TargetEnemy.GridPosition))
                {
                    result.Add(HallucinationCategory2D.H3_SpatialGeometryViolation,
                        "No clear Line of Sight from (" + shootPosition.x + ", " +
                        shootPosition.y + ") to target '" +
                        decision.TargetEnemy.EntityId + "'.");
                }
            }

            if (isSkill && decision.TargetAlly != null && decision.TargetAlly.IsAlive)
            {
                var owned = TacticalSkillCatalog2D.Resolve(actor);
                int skillRange = owned != null
                    ? owned.Range
                    : (snapshot.CurrentActor != null ? snapshot.CurrentActor.SkillRange : 0);

                Vector2Int castPosition = actor.GridPosition;
                if (action == TacticalActionType.MoveAndCastSkill && destination.HasValue &&
                    grid.IsInBounds(destination.Value))
                {
                    castPosition = destination.Value;
                }

                int distance = grid.GetDistance(castPosition, decision.TargetAlly.GridPosition);
                if (distance > skillRange)
                {
                    result.Add(HallucinationCategory2D.H3_SpatialGeometryViolation,
                        "Skill target distance " + distance + " exceeds " +
                        (owned != null ? owned.SkillId : decision.SkillId) +
                        " range " + skillRange + ".");
                }
            }

            if (!result.IsValid)
                result.SuggestedCorrection = BuildFeasibleOptions(snapshot, grid);

            return result;
        }

        private static void PopulateRejectedProposal(
            ValidationResult2D result,
            TacticalDecision2D decision,
            TacticalDecisionParseResult parse)
        {
            result.RejectedAction = parse != null && !string.IsNullOrWhiteSpace(parse.RawAction)
                ? parse.RawAction
                : decision.Action.ToString();

            result.RejectedTarget = parse != null && !string.IsNullOrWhiteSpace(parse.TargetId)
                ? parse.TargetId
                : (decision.TargetEnemy != null
                    ? decision.TargetEnemy.EntityId
                    : (decision.TargetAlly != null ? decision.TargetAlly.EntityId : string.Empty));

            if (parse != null && parse.HasDestination && parse.DestinationFieldsValid)
            {
                result.RejectedDestination = "(" + parse.DestinationX + "," + parse.DestinationY + ")";
            }
            else if (decision.MoveDestination != Vector2Int.zero)
            {
                result.RejectedDestination = "(" + decision.MoveDestination.x + "," +
                                            decision.MoveDestination.y + ")";
            }
        }

        private static Vector2Int? ResolveDestination(
            TacticalDecisionParseResult parse,
            TacticalDecision2D decision,
            bool isMovement)
        {
            if (!isMovement) return null;

            if (parse != null)
            {
                if (parse.HasDestination && parse.DestinationFieldsValid)
                    return new Vector2Int(parse.DestinationX, parse.DestinationY);
                return null;
            }

            return decision.MoveDestination;
        }

        private static bool IsReachable(CombatEntity2D actor, Vector2Int destination, TacticalGrid2D grid)
        {
            var reachable = grid.GetReachableTiles(actor.GridPosition, actor.MoveRange);
            if (reachable == null) return false;

            for (int i = 0; i < reachable.Count; i++)
            {
                if (reachable[i] == destination) return true;
            }
            return false;
        }

        private static void ValidateEnemyTarget(
            CombatEntity2D target,
            string rawTargetId,
            CombatEntity2D actor,
            ValidationResult2D result)
        {
            if (target == null)
            {
                if (!string.IsNullOrWhiteSpace(rawTargetId))
                {
                    result.Add(HallucinationCategory2D.H1_InvalidTarget,
                        "Target '" + rawTargetId + "' is not registered on the battlefield.");
                }
                return;
            }

            if (!target.IsAlive)
            {
                result.Add(HallucinationCategory2D.H1_InvalidTarget,
                    "Target '" + target.EntityId + "' is already ELIMINATED (HP: " +
                    target.Stats.CurrentHP + ").");
                return;
            }

            if (target.IsExtracted)
            {
                result.Add(HallucinationCategory2D.H1_InvalidTarget,
                    "Target '" + target.EntityId + "' has already been extracted.");
                return;
            }

            if (target.Team == actor.Team)
            {
                result.Add(HallucinationCategory2D.H1_InvalidTarget,
                    "Friendly fire: target '" + target.EntityId +
                    "' belongs to the actor's own team.");
            }
        }

        private static void ValidateAllyTarget(
            CombatEntity2D target,
            string rawTargetId,
            CombatEntity2D actor,
            ValidationResult2D result)
        {
            if (target == null)
            {
                if (!string.IsNullOrWhiteSpace(rawTargetId))
                {
                    result.Add(HallucinationCategory2D.H1_InvalidTarget,
                        "Skill target '" + rawTargetId + "' is not registered on the battlefield.");
                }
                return;
            }

            if (!target.IsAlive)
            {
                result.Add(HallucinationCategory2D.H1_InvalidTarget,
                    "Skill target '" + target.EntityId + "' is already dead.");
                return;
            }

            if (target.IsExtracted)
            {
                result.Add(HallucinationCategory2D.H1_InvalidTarget,
                    "Skill target '" + target.EntityId + "' has already been extracted.");
                return;
            }

            if (target.Team != actor.Team)
            {
                result.Add(HallucinationCategory2D.H1_InvalidTarget,
                    "Skill target '" + target.EntityId +
                    "' is an enemy; support skills may only target allies or self.");
            }
        }

        private static string BuildFeasibleOptions(TacticalStateSnapshot2D snapshot, TacticalGrid2D grid)
        {
            if (snapshot == null) return string.Empty;

            var sb = new StringBuilder(256);

            if (snapshot.CurrentActor != null)
            {
                var a = snapshot.CurrentActor;
                sb.Append("Valid Reachable Tiles: ");
                int tiles = 0;
                for (int i = 0; i < snapshot.ReachableTiles.Count && tiles < 8; i++)
                {
                    var t = snapshot.ReachableTiles[i];
                    if (t == null) continue;
                    if (tiles > 0) sb.Append(", ");
                    sb.Append('(').Append(t.X).Append(',').Append(t.Y).Append(')');
                    if (!string.IsNullOrEmpty(t.CoverType) && t.CoverType != "None")
                        sb.Append(' ').Append(t.CoverType);
                    tiles++;
                }
                if (tiles == 0) sb.Append("(none)");
                sb.AppendLine();

                sb.Append("Living Targets with Clear Line of Sight: ");
                int targets = 0;
                for (int i = 0; i < snapshot.Enemies.Count && targets < 6; i++)
                {
                    var e = snapshot.Enemies[i];
                    if (e == null || !e.IsAlive || e.IsExtracted) continue;
                    if (!e.HasLineOfSight || e.DistanceFromActor > a.AttackRange) continue;

                    if (targets > 0) sb.Append(", ");
                    sb.Append('"').Append(e.EntityId).Append("\" at (")
                      .Append(e.X).Append(',').Append(e.Y).Append(") [Distance: ")
                      .Append(e.DistanceFromActor).Append(", Flank: ")
                      .Append(e.IsFlankable ? "YES" : "NO").Append(']');
                    targets++;
                }
                if (targets == 0) sb.Append("(none from current position)");
                sb.AppendLine();

                sb.Append("Skill: ").Append(string.IsNullOrEmpty(a.SkillId) ? "none" : a.SkillId)
                  .Append(a.IsSkillReady ? " (READY)" : " (COOLDOWN " + a.SkillCooldownTurns + ")");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
