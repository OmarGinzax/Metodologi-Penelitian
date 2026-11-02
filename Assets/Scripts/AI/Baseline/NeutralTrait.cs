using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.AI.Baseline
{
    /// <summary>
    /// Neutral Trait:
    /// "neutral akan selalu mementingkan cover sebelum menembak,
    ///  akan selalu mencoba cari cover - menembak dalam 1 aksi."
    ///
    /// Karakteristik:
    /// 1. Cek posisi saat ini dulu — jika sudah di cover bagus DAN ada target, langsung Shoot tanpa gerak.
    /// 2. Utamakan menembak dari jarak mendekati max range (jarak optimal = 80-100% attackRange), sambil cover.
    /// 3. Jika belum di posisi optimal, cari petak cover + tembakan dalam 1 aksi (MoveAndShoot).
    /// 4. Jika tidak ada cover + tembak, cari cover terlebih dahulu dengan memprioritaskan jarak mendekati max range.
    /// 5. Tidak mau menembak dari tanah terbuka (None).
    /// </summary>
    public class NeutralTrait : TacticalTrait
    {
        public override string TraitName => "Neutral";
        public override TacticalTraitType TraitType => TacticalTraitType.Neutral;

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

            // Jarak optimal tembak: mendekati max range (80-100% attackRange)
            // Ini mencegah unit selalu "lari mendekat" dan justru menjaga jarak aman
            int optimalMinDist = Mathf.Max(1, Mathf.RoundToInt(actor.AttackRange * 0.65f));
            int optimalMaxDist = actor.AttackRange;

            // ===================================================================
            // 1. PRIORITAS: CEK POSISI SEKARANG DULU
            // Jika sudah di cover yang bagus dan ada target, langsung Shoot tanpa gerak!
            // ===================================================================
            if (actor.ActiveCoverType != CoverType.None)
            {
                var directTargets = GetDirectTargetsFrom(actor.GridPosition, actor.AttackRange, grid, enemies);
                if (directTargets.Count > 0)
                {
                    // Pilih target prioritas terbaik
                    var best = directTargets
                        .OrderByDescending(t => t.isFlank)
                        .ThenBy(t => t.enemy.Stats.CurrentHP)
                        .ThenBy(t => t.dist)
                        .First();

                    // Cek apakah jarak tembak saat ini sudah "optimal" (dalam rentang max range)
                    int currentDist = best.dist;
                    bool atOptimalRange = (currentDist <= optimalMaxDist);

                    if (atOptimalRange)
                    {
                        return new TacticalDecision2D(
                            TacticalActionType.Shoot,
                            actor.GridPosition,
                            best.enemy,
                            best.isFlank,
                            $"Neutral: Holding cover position — firing {actor.WeaponName} at {best.enemy.EntityName} (dist: {currentDist}/{actor.AttackRange}).");
                    }
                }
            }

            // ===================================================================
            // 2. CARI PETAK COVER + TEMBAKAN DALAM 1 AKSI (MoveAndShoot)
            // Skor diprioritaskan berdasarkan kedekatan ke jarak optimal (max range),
            // bukan sekadar mendekat ke musuh.
            // ===================================================================
            Vector2Int bestCoverShootTile = actor.GridPosition;
            CombatEntity2D bestCoverShootTarget = null;
            bool bestCoverShootIsFlank = false;
            float bestCoverShootScore = -9999f;
            bool foundCoverShootOption = false;

            foreach (var tile in reachableTiles)
            {
                if (tile == actor.GridPosition) continue; // sudah dicek di atas

                // Neutral mensyaratkan cover sebelum menembak
                var cover = grid.GetCoverAgainst(tile, nearestEnemy.GridPosition);
                if (cover == CoverType.None) continue;

                // Cari target tembakan dari petak ini
                var (target, isFlank) = FindBestTargetFromTile(tile, actor.AttackRange, grid, enemies);
                if (target == null) continue;

                foundCoverShootOption = true;

                float score = 1000f;

                // Kekuatan cover
                score += (cover == CoverType.FullCover) ? 70f : 35f;

                // Bonus flanking
                if (isFlank) score += 40f;

                // === JARAK OPTIMAL: bonus terbesar di 65-100% attackRange ===
                int distToTarget = grid.GetDistance(tile, target.GridPosition);
                float rangeRatio = (float)distToTarget / Mathf.Max(1, actor.AttackRange);
                if (rangeRatio >= 0.65f && rangeRatio <= 1.0f)
                    score += 80f; // Berada di sweet spot jarak
                else if (rangeRatio >= 0.4f && rangeRatio < 0.65f)
                    score += 30f; // Sedikit lebih dekat dari ideal, masih OK
                else if (rangeRatio < 0.4f)
                    score -= 30f; // Terlalu dekat, tidak disarankan untuk Neutral/jarak jauh

                // HP target rendah = prioritas lebih tinggi
                score += (200f - Mathf.Clamp(target.Stats.CurrentHP, 0, 200)) * 0.15f;

                if (score > bestCoverShootScore)
                {
                    bestCoverShootScore = score;
                    bestCoverShootTile = tile;
                    bestCoverShootTarget = target;
                    bestCoverShootIsFlank = isFlank;
                }
            }

            if (foundCoverShootOption && bestCoverShootTarget != null)
            {
                return new TacticalDecision2D(
                    TacticalActionType.MoveAndShoot,
                    bestCoverShootTile,
                    bestCoverShootTarget,
                    bestCoverShootIsFlank,
                    $"Neutral: Cover + Shoot in 1 action — moving to ({bestCoverShootTile.x},{bestCoverShootTile.y}), firing at {bestCoverShootTarget.EntityName}.");
            }

            // ===================================================================
            // 3. JIKA TIDAK ADA COVER + SHOOT: CARI COVER (MoveToCover)
            // Prioritas: cover kuat + mendekati jarak optimal tembak
            // ===================================================================
            Vector2Int bestMoveCoverTile = actor.GridPosition;
            float bestMoveCoverScore = -9999f;
            bool foundAnyCoverTile = false;

            foreach (var tile in reachableTiles)
            {
                if (tile == actor.GridPosition) continue;

                var cover = grid.GetCoverAgainst(tile, nearestEnemy.GridPosition);
                if (cover == CoverType.None) continue;

                foundAnyCoverTile = true;
                float score = 200f;
                score += (cover == CoverType.FullCover) ? 60f : 30f;

                // Scoring berdasarkan jarak ke musuh terdekat vs optimal range
                int tileDist = grid.GetDistance(tile, nearestEnemy.GridPosition);

                if (tileDist >= optimalMinDist && tileDist <= optimalMaxDist)
                    score += 80f; // Sudah di range optimal
                else if (tileDist > optimalMaxDist)
                    score += Mathf.Max(0f, 60f - (tileDist - optimalMaxDist) * 10f); // Mendekat ke optimal
                else
                    score += Mathf.Max(0f, 20f - (optimalMinDist - tileDist) * 10f); // Terlalu dekat

                if (score > bestMoveCoverScore)
                {
                    bestMoveCoverScore = score;
                    bestMoveCoverTile = tile;
                }
            }

            if (foundAnyCoverTile && bestMoveCoverTile != actor.GridPosition)
            {
                return new TacticalDecision2D(
                    TacticalActionType.MoveToCover,
                    bestMoveCoverTile,
                    null,
                    false,
                    $"Neutral: Moving to cover near optimal range at ({bestMoveCoverTile.x},{bestMoveCoverTile.y}).");
            }

            // ===================================================================
            // 4. SUDAH DI POSISI TERBAIK, TIDAK ADA TARGET: DEFEND
            // ===================================================================
            if (actor.ActiveCoverType != CoverType.None)
            {
                return new TacticalDecision2D(
                    TacticalActionType.Defend,
                    actor.GridPosition,
                    null,
                    false,
                    "Neutral: Holding cover — no valid targets (Hunker Down).");
            }

            // Fallback: tanpa cover, gerak ke petak terdekat ke musuh
            var fallback = reachableTiles
                .Where(t => t != actor.GridPosition)
                .OrderBy(t => Mathf.Abs(grid.GetDistance(t, nearestEnemy.GridPosition) - optimalMaxDist))
                .FirstOrDefault();

            if (fallback != actor.GridPosition && fallback != Vector2Int.zero)
            {
                return new TacticalDecision2D(
                    TacticalActionType.MoveToCover,
                    fallback,
                    null,
                    false,
                    $"Neutral: Advancing towards optimal engagement range at ({fallback.x},{fallback.y}).");
            }

            return new TacticalDecision2D(TacticalActionType.Defend, actor.GridPosition, null, false,
                "Neutral: Holding position.");
        }
    }
}
