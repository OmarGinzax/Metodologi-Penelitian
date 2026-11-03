using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.AI.Baseline
{
    public enum TacticalTraitType
    {
        Neutral,
        Aggressive,
        Passive
    }

    /// <summary>
    /// Base class untuk trait taktis pada HFSM pertarungan 2D.
    /// Menyediakan evaluasi geometris (jarak, LoS, cover XCOM, dan flank angle)
    /// yang digunakan oleh NeutralTrait, AggressiveTrait, dan PassiveTrait.
    /// </summary>
    public abstract class TacticalTrait
    {
        public abstract string TraitName { get; }
        public abstract TacticalTraitType TraitType { get; }
        public abstract float FleeHpThreshold { get; }

        public abstract TacticalDecision2D DecideTurn(
            CombatEntity2D actor, 
            TacticalGrid2D grid, 
            List<CombatEntity2D> allCombatants);

        // ===================================================================
        // Shared Evaluation Helpers
        // ===================================================================

        public virtual bool ShouldFlee(CombatEntity2D actor)
        {
            if (actor == null || !actor.IsAlive || actor.IsExtracted) return false;
            if (actor.Stats == null || actor.Stats.MaxHP <= 0) return false;

            float hpRatio = (float)actor.Stats.CurrentHP / actor.Stats.MaxHP;
            bool hpCritical = hpRatio <= (FleeHpThreshold + 0.0001f);

            // Unit yang imun terhadap efek psikologis tidak pernah mengalami breakdown mental / panik rasionalitas
            bool ratCritical = !actor.IsImmuneToPsychologicalEffects && 
                               ((float)actor.Stats.Rationality / 100f <= (FleeHpThreshold + 0.0001f));

            // Abort check: jika sebelumnya fleeing tapi darah dan rasionalitas telah pulih di atas ambang batas
            if (actor.IsFleeing)
            {
                if (!hpCritical && !ratCritical)
                {
                    actor.SetFleeing(false);
                    actor.SetActionTelemetry("Rallied", "Composure and health restored; returning to combat.");
                    return false;
                }
                return true;
            }

            return hpCritical || ratCritical;
        }

        public TacticalDecision2D CreateFleeDecision(CombatEntity2D actor, TacticalGrid2D grid, List<CombatEntity2D> enemies)
        {
            actor.SetFleeing(true);
            Vector2Int dest = FindFleeDestination(actor, grid, enemies);
            int pct = Mathf.RoundToInt(FleeHpThreshold * 100f);

            float hpRatio = (float)actor.Stats.CurrentHP / actor.Stats.MaxHP;
            string triggerCause = (actor.IsImmuneToPsychologicalEffects || hpRatio <= (FleeHpThreshold + 0.0001f))
                ? $"HP Critical ({actor.Stats.CurrentHP}/{actor.Stats.MaxHP})"
                : $"Rationality Breakdown ({actor.Stats.Rationality}/100)";

            string reason = $"{TraitName}: {triggerCause} <= {pct}% threshold. Retreating to extraction zone at ({dest.x}, {dest.y}).";
            return new TacticalDecision2D(TacticalActionType.Flee, dest, null, false, reason);
        }

        public TacticalDecision2D CheckSupportSkill(CombatEntity2D actor, TacticalGrid2D grid, List<CombatEntity2D> allCombatants)
        {
            if (actor == null || !actor.IsAlive || actor.IsExtracted || grid == null || allCombatants == null) return null;
            if (!actor.IsSkillReady) return null;

            string skillId = "";
            int skillRange = 0;

            string sName = (actor.EntityName + " " + actor.EntityId).ToLowerInvariant();
            if (sName.Contains("marcus"))
            {
                skillId = "Guard";
                skillRange = 4;
            }
            else if (sName.Contains("claire"))
            {
                skillId = "Heal";
                skillRange = 2;
            }
            else
            {
                return null;
            }

            // Cari rekan tim yang berada dalam kondisi 'distress' (fleeing ATAU HP < 50%)
            var distressedAllies = allCombatants
                .Where(a => a != null && a.IsAlive && !a.IsExtracted && a.Team == actor.Team && a != actor)
                .Where(a => a.IsFleeing || a.Stats.HPRatio < 0.50f)
                .ToList();

            if (skillId == "Guard")
            {
                // Jangan targetkan rekan yang sudah aktif dilindungi Guard
                distressedAllies = distressedAllies.Where(a => !a.IsGuarded).ToList();
            }
            else if (skillId == "Heal")
            {
                // Jangan targetkan rekan yang HP-nya sudah penuh
                distressedAllies = distressedAllies.Where(a => a.Stats.CurrentHP < a.Stats.MaxHP).ToList();
            }

            if (distressedAllies.Count == 0) return null;

            // Prioritaskan rekan: yang sedang fleeing duluan, lalu yang HP paling sekarat, lalu jarak terdekat
            var targetAlly = distressedAllies
                .OrderByDescending(a => a.IsFleeing)
                .ThenBy(a => a.Stats.HPRatio)
                .ThenBy(a => grid.GetDistance(actor.GridPosition, a.GridPosition))
                .First();

            int currentDist = grid.GetDistance(actor.GridPosition, targetAlly.GridPosition);

            // Kasus 1: Sudah dalam jangkauan cast dari posisi sekarang
            if (currentDist <= skillRange)
            {
                string reason = $"{actor.EntityName}: Prioritizing {skillId} on distressed ally {targetAlly.EntityName} (Dist: {currentDist}/{skillRange}, HP: {targetAlly.Stats.CurrentHP}/{targetAlly.Stats.MaxHP}).";
                return new TacticalDecision2D(TacticalActionType.CastSkill, actor.GridPosition, null, targetAlly, skillId, false, reason);
            }

            // Kasus 2: Di luar jangkauan cast -> cari petak bergerak (reachable) untuk mendekat & cast
            var reachable = grid.GetReachableTiles(actor.GridPosition, actor.MoveRange);
            if (reachable == null || reachable.Count == 0) return null;

            var inRangeTiles = reachable
                .Where(t => grid.GetDistance(t, targetAlly.GridPosition) <= skillRange)
                .ToList();

            var enemies = GetLivingEnemies(actor, allCombatants);
            var nearestEnemy = GetNearestEnemy(actor.GridPosition, enemies, grid);

            if (inRangeTiles.Count > 0)
            {
                // Bisa berpindah lalu cast dalam 1 giliran (MoveAndCastSkill)!
                Vector2Int bestMoveTile = inRangeTiles
                    .OrderByDescending(t => nearestEnemy != null ? (int)grid.GetCoverAgainst(t, nearestEnemy.GridPosition) : 0)
                    .ThenBy(t => nearestEnemy != null && grid.IsFlanked(t, nearestEnemy.GridPosition) ? 1 : 0)
                    .ThenBy(t => grid.GetDistance(t, targetAlly.GridPosition))
                    .First();

                string reason = $"{actor.EntityName}: Moving to ({bestMoveTile.x},{bestMoveTile.y}) to cast {skillId} on distressed ally {targetAlly.EntityName}.";
                return new TacticalDecision2D(TacticalActionType.MoveAndCastSkill, bestMoveTile, null, targetAlly, skillId, false, reason);
            }

            // Kasus 3: Tidak bisa sampai jangkauan cast dalam 1 giliran -> maju mendekati rekan yang sekarat
            Vector2Int advanceTile = reachable
                .OrderBy(t => grid.GetDistance(t, targetAlly.GridPosition))
                .ThenByDescending(t => nearestEnemy != null ? (int)grid.GetCoverAgainst(t, nearestEnemy.GridPosition) : 0)
                .First();

            string advReason = $"{actor.EntityName}: Advancing towards distressed ally {targetAlly.EntityName} to get within {skillId} range.";
            return new TacticalDecision2D(TacticalActionType.MoveToCover, advanceTile, null, false, advReason);
        }

        public Vector2Int FindFleeDestination(CombatEntity2D actor, TacticalGrid2D grid, List<CombatEntity2D> enemies)
        {
            if (grid == null) return actor.GridPosition;

            // Jika sudah di petak ekstraksi, tetap di sini untuk dievakuasi
            if (grid.IsExtractionTile(actor.GridPosition))
                return actor.GridPosition;

            var reachable = grid.GetReachableTiles(actor.GridPosition, actor.MoveRange);
            if (reachable == null || reachable.Count == 0)
                return actor.GridPosition;

            // Jika ada petak ekstraksi yang bisa langsung dijangkau turn ini
            var reachableExtraction = reachable.Where(t => grid.IsExtractionTile(t)).ToList();
            if (reachableExtraction.Count > 0)
            {
                return reachableExtraction
                    .OrderBy(t => Mathf.Abs(t.y - actor.GridPosition.y))
                    .ThenBy(t => grid.GetDistance(actor.GridPosition, t))
                    .First();
            }

            // Cari petak yang paling mendekatkan unit ke zona ekstraksi melalui rute valid
            var nearestEnemy = GetNearestEnemy(actor.GridPosition, enemies, grid);
            Vector2Int bestTile = actor.GridPosition;
            float bestScore = -99999f;

            foreach (var tile in reachable)
            {
                int pathDist = GetPathDistanceToExtraction(tile, grid, actor);
                float score = 1000f - (pathDist * 40f);

                // Prioritaskan cover terhadap musuh saat kabur
                if (nearestEnemy != null)
                {
                    var cov = grid.GetCoverAgainst(tile, nearestEnemy.GridPosition);
                    if (cov == CoverType.FullCover) score += 20f;
                    else if (cov == CoverType.HalfCover) score += 10f;

                    if (grid.IsFlanked(tile, nearestEnemy.GridPosition)) score -= 15f;
                }

                // Preferensi maju ke arah barat (X lebih kecil)
                score -= tile.x * 2f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTile = tile;
                }
            }

            return bestTile;
        }

        private int GetPathDistanceToExtraction(Vector2Int start, TacticalGrid2D grid, CombatEntity2D actor)
        {
            if (grid.IsExtractionTile(start)) return 0;

            var queue = new Queue<(Vector2Int pos, int dist)>();
            var visited = new HashSet<Vector2Int>();
            queue.Enqueue((start, 0));
            visited.Add(start);

            Vector2Int[] dirs = { Vector2Int.left, Vector2Int.down, Vector2Int.up, Vector2Int.right };

            while (queue.Count > 0)
            {
                var (cur, dist) = queue.Dequeue();
                if (grid.IsExtractionTile(cur))
                    return dist;

                if (dist > 30) continue;

                foreach (var d in dirs)
                {
                    var next = cur + d;
                    if (!grid.IsInBounds(next) || visited.Contains(next)) continue;

                    var cell = grid.GetCell(next);
                    if (cell == null || cell.Type == CellType.Blocked || cell.Type == CellType.FullCover || cell.Type == CellType.HalfCover) continue;

                    if (cell.Occupant != null && cell.Occupant != actor && cell.Occupant.Team != actor.Team) continue;

                    visited.Add(next);
                    queue.Enqueue((next, dist + 1));
                }
            }

            return start.x + 10;
        }

        protected List<CombatEntity2D> GetLivingEnemies(CombatEntity2D actor, List<CombatEntity2D> allCombatants)
        {
            if (actor == null || allCombatants == null) return new List<CombatEntity2D>();
            return allCombatants
                .Where(c => c != null && c.IsAlive && !c.IsExtracted && c.Team != actor.Team)
                .ToList();
        }

        protected CombatEntity2D GetNearestEnemy(Vector2Int fromPos, List<CombatEntity2D> enemies, TacticalGrid2D grid)
        {
            if (enemies == null || enemies.Count == 0 || grid == null) return null;
            return enemies
                .OrderBy(e => grid.GetDistance(fromPos, e.GridPosition))
                .FirstOrDefault();
        }

        /// <summary>
        /// Menemukan semua musuh yang berada dalam jangkauan tembak dan LoS dari posisi tertentu.
        /// </summary>
        protected List<(CombatEntity2D enemy, bool isFlank, int dist)> GetDirectTargetsFrom(
            Vector2Int pos, 
            int attackRange, 
            TacticalGrid2D grid, 
            List<CombatEntity2D> enemies)
        {
            var targets = new List<(CombatEntity2D enemy, bool isFlank, int dist)>();
            if (grid == null || enemies == null) return targets;

            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                if (enemy == null || !enemy.IsAlive) continue;

                int dist = grid.GetDistance(pos, enemy.GridPosition);
                if (dist <= attackRange && grid.HasLineOfSight(pos, enemy.GridPosition))
                {
                    bool isFlank = grid.IsFlanked(enemy.GridPosition, pos);
                    targets.Add((enemy, isFlank, dist));
                }
            }

            return targets;
        }

        /// <summary>
        /// Mengecek apakah aktor di posisi 'pos' terkena flank oleh salah satu musuh yang masih hidup.
        /// XCOM Edge Rule: grid.IsFlanked(defenderPos, attackerPos) bernilai true jika sisi defender tidak ada rintangan cover.
        /// </summary>
        public bool IsFlankedByAnyLivingEnemy(Vector2Int pos, TacticalGrid2D grid, List<CombatEntity2D> enemies)
        {
            if (grid == null || enemies == null || enemies.Count == 0) return false;

            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                if (enemy == null || !enemy.IsAlive) continue;

                if (grid.IsFlanked(pos, enemy.GridPosition))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Menghitung berapa banyak musuh hidup yang memiliki sudut flank langsung terhadap posisi 'pos'.
        /// </summary>
        public int CountFlankingLivingEnemies(Vector2Int pos, TacticalGrid2D grid, List<CombatEntity2D> enemies)
        {
            if (grid == null || enemies == null || enemies.Count == 0) return 0;

            int count = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                if (enemy == null || !enemy.IsAlive) continue;

                if (grid.IsFlanked(pos, enemy.GridPosition))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Menemukan target tembakan terbaik dari petak tertentu.
        /// Prioritas: Target Flanked -> HP terendah -> Jarak terdekat.
        /// </summary>
        protected (CombatEntity2D target, bool isFlank) FindBestTargetFromTile(
            Vector2Int tile, 
            int attackRange, 
            TacticalGrid2D grid, 
            List<CombatEntity2D> enemies)
        {
            var valid = GetDirectTargetsFrom(tile, attackRange, grid, enemies);
            if (valid.Count == 0) return (null, false);

            var best = valid
                .OrderByDescending(t => t.isFlank)
                .ThenBy(t => t.enemy.Stats.CurrentHP)
                .ThenBy(t => t.dist)
                .First();

            return (best.enemy, best.isFlank);
        }
    }

    /// <summary>
    /// Factory untuk menginstansiasi atau mengambil singleton instance dari trait taktis.
    /// Mendukung parsing case-insensitive serta pemetaan nama trait lawas.
    /// </summary>
    public static class TacticalTraitFactory
    {
        private static readonly NeutralTrait    _neutral    = new NeutralTrait();
        private static readonly AggressiveTrait _aggressive = new AggressiveTrait();
        private static readonly PassiveTrait    _passive    = new PassiveTrait();

        public static TacticalTrait GetTrait(string traitId)
        {
            if (string.IsNullOrEmpty(traitId)) return _neutral;

            string s = traitId.ToLowerInvariant().Trim();

            // Mendukung ejaan "aggressive" dan "aggresive"
            if (s.Contains("aggres") || s.Contains("disciplinedprofessional"))
                return _aggressive;

            if (s.Contains("passiv") || s.Contains("cautious") || s.Contains("timid"))
                return _passive;

            if (s.Contains("neutral") || s.Contains("loyal") || s.Contains("protector"))
                return _neutral;

            return _neutral;
        }

        public static TacticalTrait GetTrait(TacticalTraitType type)
        {
            switch (type)
            {
                case TacticalTraitType.Aggressive: return _aggressive;
                case TacticalTraitType.Passive:    return _passive;
                default:                           return _neutral;
            }
        }
    }
}
