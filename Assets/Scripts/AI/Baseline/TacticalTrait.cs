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

        public abstract TacticalDecision2D DecideTurn(
            CombatEntity2D actor, 
            TacticalGrid2D grid, 
            List<CombatEntity2D> allCombatants);

        // ===================================================================
        // Shared Evaluation Helpers
        // ===================================================================

        protected List<CombatEntity2D> GetLivingEnemies(CombatEntity2D actor, List<CombatEntity2D> allCombatants)
        {
            if (actor == null || allCombatants == null) return new List<CombatEntity2D>();
            return allCombatants
                .Where(c => c != null && c.IsAlive && c.Team != actor.Team)
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
