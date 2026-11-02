using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.AI.Baseline
{
    /// <summary>
    /// Passive Trait:
    /// "kalo passive akan mementingkan cover dulu, yang aman dari segala sisi,
    ///  tidak mau kena flankinh sama sekali, dan jika maju untuk menembak akan membuat
    ///  di di mungkinan di flanking malah di akan milih reposition tanpa menembak"
    ///
    /// Karakteristik:
    /// 1. Cek posisi sekarang dulu — jika sudah 100% aman (cover + zero flank) dan ada target, Shoot tanpa gerak!
    /// 2. Preferensi jarak tembak mendekati max range (sniper gaya), bukan mendekat ke musuh.
    /// 3. Hanya berpindah dan menembak (MoveAndShoot) ke petak yang 100% aman dari semua sudut flank.
    /// 4. Jika maju berisiko di-flank, pilih Reposition (MoveToCover) tanpa menembak.
    /// 5. Reposition memperhitungkan jarak optimal (max range): lebih suka posisi jauh-aman daripada dekat-berbahaya.
    /// </summary>
    public class PassiveTrait : TacticalTrait
    {
        public override string TraitName => "Passive";
        public override TacticalTraitType TraitType => TacticalTraitType.Passive;

        public override TacticalDecision2D DecideTurn(
            CombatEntity2D actor,
            TacticalGrid2D grid,
            List<CombatEntity2D> allCombatants)
        {
            if (actor == null || !actor.IsAlive || grid == null)
                return new TacticalDecision2D(TacticalActionType.Idle, Vector2Int.zero, null, false, "Invalid actor state.");

            var enemies = GetLivingEnemies(actor, allCombatants);
            if (enemies.Count == 0)
                return new TacticalDecision2D(TacticalActionType.Idle, actor.GridPosition, null, false, "No hostiles remaining.");

            var nearestEnemy = GetNearestEnemy(actor.GridPosition, enemies, grid);
            var reachableTiles = grid.GetReachableTiles(actor.GridPosition, actor.MoveRange);

            // Jarak optimal: Passive suka jarak max range (aman + jangkauan penuh)
            int optimalMinDist = Mathf.Max(1, Mathf.RoundToInt(actor.AttackRange * 0.65f));
            int optimalMaxDist = actor.AttackRange;

            int currentFlankCount = CountFlankingLivingEnemies(actor.GridPosition, grid, enemies);
            bool currentIsSafe = (currentFlankCount == 0);
            bool currentInCover = (actor.ActiveCoverType != CoverType.None);

            // ===================================================================
            // 1. PRIORITAS: CEK POSISI SEKARANG DULU
            // Jika sudah di cover yang aman dari semua sudut flank dan ada target, langsung Shoot!
            // ===================================================================
            if (currentInCover && currentIsSafe)
            {
                var directTargets = GetDirectTargetsFrom(actor.GridPosition, actor.AttackRange, grid, enemies);
                if (directTargets.Count > 0)
                {
                    var best = directTargets
                        .OrderBy(t => t.enemy.Stats.CurrentHP)
                        .ThenByDescending(t => t.isFlank)
                        .First();

                    return new TacticalDecision2D(
                        TacticalActionType.Shoot,
                        actor.GridPosition,
                        best.enemy,
                        best.isFlank,
                        $"Passive: Holding secure fortified cover — firing {actor.WeaponName} at {best.enemy.EntityName} (zero flank exposure).");
                }
            }

            // ===================================================================
            // 2. CARI PETAK 100% AMAN + COVER + TEMBAKAN (MoveAndShoot)
            // Prioritaskan jarak mendekati max range untuk menjaga posisi aman
            // ===================================================================
            Vector2Int bestSafeShootTile = actor.GridPosition;
            CombatEntity2D bestSafeShootTarget = null;
            bool bestSafeShootIsFlank = false;
            float bestSafeShootScore = -9999f;
            bool foundSafeShootOption = false;

            foreach (var tile in reachableTiles)
            {
                if (tile == actor.GridPosition) continue;

                // Harus ada cover
                var cover = grid.GetCoverAgainst(tile, nearestEnemy.GridPosition);
                if (cover == CoverType.None) continue;

                // "tidak mau kena flanking sama sekali"
                int flankCount = CountFlankingLivingEnemies(tile, grid, enemies);
                if (flankCount > 0) continue; // Tolak — berisiko di-flank

                // Cek apakah ada target yang bisa ditembak dari sini
                var (target, isFlank) = FindBestTargetFromTile(tile, actor.AttackRange, grid, enemies);
                if (target == null) continue;

                foundSafeShootOption = true;

                float score = 1000f;
                score += (cover == CoverType.FullCover) ? 80f : 40f;

                // === JARAK OPTIMAL: Passive suka menembak dari jauh (max range) ===
                int distToTarget = grid.GetDistance(tile, target.GridPosition);
                float rangeRatio = (float)distToTarget / Mathf.Max(1, actor.AttackRange);
                if (rangeRatio >= 0.65f && rangeRatio <= 1.0f)
                    score += 100f; // Sweet spot: mendekati max range
                else if (rangeRatio >= 0.4f && rangeRatio < 0.65f)
                    score += 40f;
                else if (rangeRatio < 0.4f)
                    score -= 40f; // Terlalu dekat, tidak disukai Passive

                // Jarak dari musuh terdekat (Passive suka jauh)
                int distFromNearest = grid.GetDistance(tile, nearestEnemy.GridPosition);
                score += Mathf.Clamp(distFromNearest, 0, optimalMaxDist) * 8f;

                if (score > bestSafeShootScore)
                {
                    bestSafeShootScore = score;
                    bestSafeShootTile = tile;
                    bestSafeShootTarget = target;
                    bestSafeShootIsFlank = isFlank;
                }
            }

            if (foundSafeShootOption && bestSafeShootTarget != null)
            {
                return new TacticalDecision2D(
                    TacticalActionType.MoveAndShoot,
                    bestSafeShootTile,
                    bestSafeShootTarget,
                    bestSafeShootIsFlank,
                    $"Passive: Moving to flank-safe cover near max range ({bestSafeShootTile.x},{bestSafeShootTile.y}), firing at {bestSafeShootTarget.EntityName}.");
            }

            // ===================================================================
            // 3. REPOSITION TANPA MENEMBAK (MoveToCover)
            // "jika maju untuk menembak akan membuat kemungkinan di flanking,
            //  malah dia akan memilih reposition tanpa menembak"
            // Prioritas: petak paling aman (zero flank) + cover kuat + dekat optimal range
            // ===================================================================
            Vector2Int bestRepositionTile = actor.GridPosition;
            float bestRepositionScore = -9999f;

            foreach (var tile in reachableTiles)
            {
                if (tile == actor.GridPosition) continue;

                int flankCount = CountFlankingLivingEnemies(tile, grid, enemies);
                var coverVsNearest = grid.GetCoverAgainst(tile, nearestEnemy.GridPosition);

                float score = 0f;

                // Prioritas utama: ZERO flank exposure
                if (flankCount == 0) score += 500f;
                else score -= flankCount * 200f;

                // Cover kuat
                if (coverVsNearest == CoverType.FullCover) score += 100f;
                else if (coverVsNearest == CoverType.HalfCover) score += 50f;
                else score -= 150f;

                // Jarak aman: mendekati max range (Passive suka jauh)
                int distToNearest = grid.GetDistance(tile, nearestEnemy.GridPosition);
                if (distToNearest >= optimalMinDist && distToNearest <= optimalMaxDist)
                    score += 80f;
                else if (distToNearest > optimalMaxDist)
                    score += 40f; // Terlalu jauh tapi aman
                else
                    score -= 20f; // Terlalu dekat, tidak disukai

                if (score > bestRepositionScore)
                {
                    bestRepositionScore = score;
                    bestRepositionTile = tile;
                }
            }

            if (bestRepositionTile != actor.GridPosition)
            {
                int destFlankCount = CountFlankingLivingEnemies(bestRepositionTile, grid, enemies);
                string safetyNote = (destFlankCount == 0) ? "100% safe" : $"{destFlankCount} flank threat(s)";

                return new TacticalDecision2D(
                    TacticalActionType.MoveToCover,
                    bestRepositionTile,
                    null,
                    false,
                    $"Passive: Repositioning (no shoot) to safe bunker at ({bestRepositionTile.x},{bestRepositionTile.y}) [{safetyNote}].");
            }

            // Sudah di posisi paling aman yang bisa dijangkau
            return new TacticalDecision2D(
                TacticalActionType.Defend,
                actor.GridPosition,
                null,
                false,
                "Passive: Maximum fortification (Hunker Down). Refusing to expose flank.");
        }
    }
}
