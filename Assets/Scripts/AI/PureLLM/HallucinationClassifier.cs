using System;
using System.Collections.Generic;
using System.Text;
using RPGStrategy.AI.Baseline;
using RPGStrategy.Core.Combat;
using UnityEngine;

namespace RPGStrategy.AI.PureLLM
{
    /// <summary>
    /// Taksonomi halusinasi operasional untuk perlakuan Pure LLM (Y2).
    /// Berbeda dari taksonomi Hybrid (Milestone 5), kategori di sini mengikuti
    /// pure_llm_system_plan.md bagian 6.1.
    /// </summary>
    public enum HallucinationCategory2D
    {
        H1_InvalidTarget = 1,
        H2_SkillCooldownOrOwnership = 2,
        H3_SpatialGeometryViolation = 3,
        H4_ActionProtocolInconsistency = 4,
        H5_OutputSyntaxFailure = 5
    }

    /// <summary>Hasil klasifikasi satu keputusan LLM.</summary>
    public sealed class HallucinationReport
    {
        public readonly List<HallucinationCategory2D> Categories = new List<HallucinationCategory2D>();
        public readonly List<string> DetailsList = new List<string>();

        public bool IsIllogical => Categories.Count > 0;

        public string Details => DetailsList.Count == 0 ? string.Empty : string.Join("; ", DetailsList.ToArray());

        public string ViolationCodesCsv
        {
            get
            {
                if (Categories.Count == 0) return "NONE";
                var sb = new StringBuilder();
                for (int i = 0; i < Categories.Count; i++)
                {
                    if (i > 0) sb.Append('|');
                    sb.Append(Code(Categories[i]));
                }
                return sb.ToString();
            }
        }

        public void Add(HallucinationCategory2D category, string detail)
        {
            if (!Categories.Contains(category)) Categories.Add(category);
            if (!string.IsNullOrEmpty(detail)) DetailsList.Add(Code(category) + ": " + detail);
        }

        public bool Has(HallucinationCategory2D category)
            => Categories.Contains(category);

        public static string Code(HallucinationCategory2D category)
        {
            switch (category)
            {
                case HallucinationCategory2D.H1_InvalidTarget: return "H1";
                case HallucinationCategory2D.H2_SkillCooldownOrOwnership: return "H2";
                case HallucinationCategory2D.H3_SpatialGeometryViolation: return "H3";
                case HallucinationCategory2D.H4_ActionProtocolInconsistency: return "H4";
                case HallucinationCategory2D.H5_OutputSyntaxFailure: return "H5";
                default: return "NONE";
            }
        }
    }

    /// <summary>
    /// Menilai keputusan LLM terhadap snapshot medan laga secara deterministik.
    /// Classifier ini TIDAK mengubah keputusan dan TIDAK menjalankan fallback;
    /// ia hanya mencatat pelanggaran H1-H5 untuk data penelitian Y2.
    /// </summary>
    public class HallucinationClassifier
    {
        /// <summary>Evaluasi tanpa metadata parsing (kompatibel dengan signature rencana).</summary>
        public HallucinationReport EvaluateDecision(
            TacticalDecision2D decision,
            TacticalStateSnapshot2D snapshot,
            TacticalGrid2D grid)
        {
            return EvaluateDecision(decision, snapshot, grid, null);
        }

        /// <summary>Evaluasi lengkap dengan metadata parsing (raw action, raw destination, status H5).</summary>
        public HallucinationReport EvaluateDecision(
            TacticalDecision2D decision,
            TacticalStateSnapshot2D snapshot,
            TacticalGrid2D grid,
            TacticalDecisionParseResult parsed)
        {
            var report = new HallucinationReport();

            if (decision == null)
            {
                report.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency, "Decision object is null.");
                return report;
            }

            if (parsed != null && !parsed.Success)
            {
                report.Add(HallucinationCategory2D.H5_OutputSyntaxFailure,
                    string.IsNullOrEmpty(parsed.Error) ? "LLM output is not valid JSON." : parsed.Error);
                return report;
            }

            if (parsed != null && !parsed.ActionRecognized)
            {
                report.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency,
                    "Unknown action '" + parsed.RawAction + "'.");
                return report;
            }

            if (snapshot == null || snapshot.CurrentActor == null)
            {
                report.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency, "Tactical snapshot or actor context is missing.");
                return report;
            }

            var actor = snapshot.ActorEntity;
            if (actor == null)
            {
                report.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency, "Actor entity reference is missing from snapshot.");
                return report;
            }

            var actorPos = actor.GridPosition;

            switch (decision.Action)
            {
                case TacticalActionType.Move:
                case TacticalActionType.MoveToCover:
                case TacticalActionType.MoveAndShoot:
                case TacticalActionType.MoveAndCastSkill:
                    ValidateDestination(decision, snapshot, grid, parsed, report);
                    break;

                case TacticalActionType.Flee:
                    if (parsed == null || parsed.HasDestination)
                        ValidateDestination(decision, snapshot, grid, parsed, report);
                    break;
            }

            if (decision.Action == TacticalActionType.Shoot ||
                decision.Action == TacticalActionType.MoveAndShoot)
            {
                ValidateShoot(decision, snapshot, grid, parsed, report, actorPos);
            }

            if (decision.Action == TacticalActionType.CastSkill ||
                decision.Action == TacticalActionType.MoveAndCastSkill)
            {
                ValidateSkill(decision, snapshot, grid, parsed, report, actorPos);
            }

            return report;
        }

        private static void ValidateDestination(
            TacticalDecision2D decision,
            TacticalStateSnapshot2D snapshot,
            TacticalGrid2D grid,
            TacticalDecisionParseResult parsed,
            HallucinationReport report)
        {
            var actor = snapshot.ActorEntity;
            if (grid == null || actor == null) return;

            bool hasDestination;
            Vector2Int requested;

            if (parsed != null)
            {
                hasDestination = parsed.HasDestination;
                if (hasDestination && !parsed.DestinationFieldsValid)
                {
                    report.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency,
                        "Destination object is missing numeric x/y fields.");
                    return;
                }
                requested = hasDestination
                    ? new Vector2Int(parsed.DestinationX, parsed.DestinationY)
                    : decision.MoveDestination;
            }
            else
            {
                hasDestination = true;
                requested = decision.MoveDestination;
            }

            if (!hasDestination)
            {
                report.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency,
                    "Action '" + decision.Action + "' requires a destination but none was provided.");
                return;
            }

            if (!grid.IsInBounds(requested))
            {
                report.Add(HallucinationCategory2D.H3_SpatialGeometryViolation,
                    "Destination (" + requested.x + ", " + requested.y + ") is outside grid bounds; the engine will clamp it.");
                return;
            }

            if (requested == actor.GridPosition) return;

            int distance = grid.GetDistance(actor.GridPosition, requested);
            if (distance > actor.MoveRange)
            {
                report.Add(HallucinationCategory2D.H3_SpatialGeometryViolation,
                    "Destination distance " + distance + " exceeds MoveRange " + actor.MoveRange + ".");
                return;
            }

            if (!IsReachable(actor.GridPosition, requested, actor, grid))
            {
                report.Add(HallucinationCategory2D.H3_SpatialGeometryViolation,
                    "Destination (" + requested.x + ", " + requested.y + ") is not reachable through walkable terrain.");
            }
        }

        private static bool IsReachable(Vector2Int from, Vector2Int to, CombatEntity2D actor, TacticalGrid2D grid)
        {
            var reachable = grid.GetReachableTiles(from, actor.MoveRange);
            if (reachable == null) return false;
            for (int i = 0; i < reachable.Count; i++)
            {
                if (reachable[i] == to) return true;
            }
            return false;
        }

        private static void ValidateShoot(
            TacticalDecision2D decision,
            TacticalStateSnapshot2D snapshot,
            TacticalGrid2D grid,
            TacticalDecisionParseResult parsed,
            HallucinationReport report,
            Vector2Int actorPos)
        {
            var actor = snapshot.ActorEntity;
            if (grid == null || actor == null) return;

            var target = decision.TargetEnemy;
            if (target == null)
            {
                string rawTarget = parsed != null ? parsed.TargetId : null;
                if (string.IsNullOrWhiteSpace(rawTarget))
                {
                    report.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency,
                        "Shoot action requires a non-null target_id.");
                }
                else
                {
                    report.Add(HallucinationCategory2D.H1_InvalidTarget,
                        "Target '" + rawTarget + "' is not registered on the battlefield.");
                }
                return;
            }

            if (!target.IsAlive)
            {
                report.Add(HallucinationCategory2D.H1_InvalidTarget,
                    "Target '" + target.EntityId + "' is already dead.");
                return;
            }

            if (target.IsExtracted)
            {
                report.Add(HallucinationCategory2D.H1_InvalidTarget,
                    "Target '" + target.EntityId + "' has already been extracted.");
                return;
            }

            if (target.Team == actor.Team)
            {
                report.Add(HallucinationCategory2D.H1_InvalidTarget,
                    "Friendly fire: target '" + target.EntityId + "' belongs to the actor's own team.");
            }

            Vector2Int shootPos = actorPos;
            if (decision.Action == TacticalActionType.MoveAndShoot && grid != null)
            {
                Vector2Int requested;
                if (parsed != null && parsed.HasDestination && parsed.DestinationFieldsValid)
                    requested = new Vector2Int(parsed.DestinationX, parsed.DestinationY);
                else
                    requested = decision.MoveDestination;

                shootPos = grid.ClampToBounds(requested);
            }

            int distance = grid.GetDistance(shootPos, target.GridPosition);
            if (distance > actor.AttackRange)
            {
                report.Add(HallucinationCategory2D.H3_SpatialGeometryViolation,
                    "Target distance " + distance + " exceeds AttackRange " + actor.AttackRange + ".");
            }

            if (!grid.HasLineOfSight(shootPos, target.GridPosition))
            {
                report.Add(HallucinationCategory2D.H3_SpatialGeometryViolation,
                    "No clear Line of Sight from (" + shootPos.x + ", " + shootPos.y + ") to target '" + target.EntityId + "'.");
            }
        }

        private static void ValidateSkill(
            TacticalDecision2D decision,
            TacticalStateSnapshot2D snapshot,
            TacticalGrid2D grid,
            TacticalDecisionParseResult parsed,
            HallucinationReport report,
            Vector2Int actorPos)
        {
            var actor = snapshot.ActorEntity;
            if (grid == null || actor == null) return;

            string skillId = decision.SkillId;
            if (string.IsNullOrWhiteSpace(skillId))
            {
                report.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency,
                    "Skill action requires a non-null skill_id.");
                return;
            }

            var owned = TacticalSkillCatalog2D.Resolve(actor);
            var requestedSkill = TacticalSkillCatalog2D.Get(skillId);
            if (owned == null || requestedSkill == null ||
                !string.Equals(owned.SkillId, requestedSkill.SkillId, StringComparison.OrdinalIgnoreCase))
            {
                report.Add(HallucinationCategory2D.H2_SkillCooldownOrOwnership,
                    "Actor '" + actor.EntityName + "' does not own skill '" + skillId + "'.");
            }

            if (!actor.IsSkillReady)
            {
                report.Add(HallucinationCategory2D.H2_SkillCooldownOrOwnership,
                    "Skill '" + skillId + "' is on cooldown (" + actor.SkillCooldownTurns + " turn(s) remaining).");
            }

            var target = decision.TargetAlly;
            if (target == null)
            {
                string rawTarget = parsed != null ? parsed.TargetId : null;
                if (string.IsNullOrWhiteSpace(rawTarget))
                {
                    report.Add(HallucinationCategory2D.H4_ActionProtocolInconsistency,
                        "Skill action requires a non-null target_id.");
                }
                else
                {
                    report.Add(HallucinationCategory2D.H1_InvalidTarget,
                        "Skill target '" + rawTarget + "' is not registered on the battlefield.");
                }
                return;
            }

            if (!target.IsAlive)
            {
                report.Add(HallucinationCategory2D.H1_InvalidTarget,
                    "Skill target '" + target.EntityId + "' is already dead.");
                return;
            }

            if (target.IsExtracted)
            {
                report.Add(HallucinationCategory2D.H1_InvalidTarget,
                    "Skill target '" + target.EntityId + "' has already been extracted.");
                return;
            }

            if (target.Team != actor.Team)
            {
                report.Add(HallucinationCategory2D.H1_InvalidTarget,
                    "Skill target '" + target.EntityId + "' is an enemy; support skills may only target allies or self.");
            }

            if (owned == null) return;

            Vector2Int castPos = actorPos;
            if (decision.Action == TacticalActionType.MoveAndCastSkill)
            {
                Vector2Int requested;
                if (parsed != null && parsed.HasDestination && parsed.DestinationFieldsValid)
                    requested = new Vector2Int(parsed.DestinationX, parsed.DestinationY);
                else
                    requested = decision.MoveDestination;

                castPos = grid.ClampToBounds(requested);
            }

            int distance = grid.GetDistance(castPos, target.GridPosition);
            if (distance > owned.Range)
            {
                report.Add(HallucinationCategory2D.H3_SpatialGeometryViolation,
                    "Skill target distance " + distance + " exceeds " + owned.SkillId + " range " + owned.Range + ".");
            }
        }
    }
}
