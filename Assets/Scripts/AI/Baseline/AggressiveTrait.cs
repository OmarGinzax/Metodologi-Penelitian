using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.AI.Baseline
{
    /// <summary>
    /// Aggressive Trait:
    /// "aggresive juga peduli dengan cover, tapi saat ada kesempatan flanking musuh,
    ///  walaupun tempat serangan ada cover ato tidak akan di ambil"
    ///
    /// Karakteristik:
    /// 1. Cek posisi sekarang dulu — jika ada flanking opportunity dari posisi saat ini, langsung Shoot!
    /// 2. Jika ada petak flanking reachable, bergerak ke sana (MoveAndShoot) meskipun tanah terbuka.
    /// 3. Fallback tanpa flanking: cek posisi saat ini dulu (Shoot from cover), lalu cari petak cover + tembak.
    /// 4. Agresif mendekati musuh, tidak mempermasalahkan jarak terlalu jauh.
    /// </summary>
    public class AggressiveTrait : TacticalTrait
    {
        public override string TraitName => "Aggressive";
        public override TacticalTraitType TraitType => TacticalTraitType.Aggressive;

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

            // ===================================================================
            // 1. PRIORITAS: CEK POSISI SEKARANG DULU
            // Jika sudah di posisi yang bagus (flanking opportunity atau cover + target), Shoot tanpa gerak!
            // ===================================================================
            {
                var directFromCurrent = GetDirectTargetsFrom(actor.GridPosition, actor.AttackRange, grid, enemies);

                // Jika ada flanking dari posisi sekarang → tembak langsung!
                var flankFromCurrent = directFromCurrent.Where(t => t.isFlank).ToList();
                if (flankFromCurrent.Count > 0)
                {
                    var target = flankFromCurrent.OrderBy(t => t.enemy.Stats.CurrentHP).First();
                    return new TacticalDecision2D(
                        TacticalActionType.Shoot,
                        actor.GridPosition,
                        target.enemy,
                        true,
                        $"Aggressive: Holding position — Flank Critical shot at {target.enemy.EntityName}!");
                }

                // Jika ada target + di cover → tembak dari posisi sekarang (tidak perlu gerak)
                if (actor.ActiveCoverType != CoverType.None && directFromCurrent.Count > 0)
                {
                    var target = directFromCurrent.OrderBy(t => t.enemy.Stats.CurrentHP).First();
                    return new TacticalDecision2D(
                        TacticalActionType.Shoot,
                        actor.GridPosition,
                        target.enemy,
                        target.isFlank,
                        $"Aggressive: Holding cover — firing {actor.WeaponName} at {target.enemy.EntityName}.");
                }
            }

            // ===================================================================
            // 2. CARI KESEMPATAN FLANKING DARI PETAK REACHABLE
            // "saat ada kesempatan flanking musuh, walaupun tempat serangan ada cover ato tidak akan di ambil"
            // ===================================================================
            Vector2Int bestFlankTile = actor.GridPosition;
            CombatEntity2D bestFlankTarget = null;
            float bestFlankScore = -9999f;
            bool foundFlankingOpportunity = false;

            foreach (var tile in reachableTiles)
            {
                if (tile == actor.GridPosition) continue;

                var directTargets = GetDirectTargetsFrom(tile, actor.AttackRange, grid, enemies);
                var flankingTargets = directTargets.Where(t => t.isFlank).ToList();

                if (flankingTargets.Count > 0)
                {
                    foundFlankingOpportunity = true;

                    var target = flankingTargets
                        .OrderBy(t => t.enemy.Stats.CurrentHP)
                        .ThenBy(t => t.dist)
                        .First();

                    // Skor flanking sangat masif — cover di titik serang hanya tie-breaker kecil
                    float score = 2000f;
                    score += (200f - Mathf.Clamp(target.enemy.Stats.CurrentHP, 0, 200)) * 0.5f;

                    var tileCover = grid.GetCoverAgainst(tile, target.enemy.GridPosition);
                    if (tileCover == CoverType.FullCover) score += 20f;
                    else if (tileCover == CoverType.HalfCover) score += 10f;

                    score -= target.dist * 2f;

                    if (score > bestFlankScore)
                    {
                        bestFlankScore = score;
                        bestFlankTile = tile;
                        bestFlankTarget = target.enemy;
                    }
                }
            }

            if (foundFlankingOpportunity && bestFlankTarget != null)
            {
                var attackCover = grid.GetCoverAgainst(bestFlankTile, bestFlankTarget.GridPosition);
                string coverNote = (attackCover == CoverType.None) ? "open ground" : attackCover.ToString();
                return new TacticalDecision2D(
                    TacticalActionType.MoveAndShoot,
                    bestFlankTile,
                    bestFlankTarget,
                    true,
                    $"Aggressive: Seizing flank at ({bestFlankTile.x},{bestFlankTile.y}) [{coverNote}] — firing at {bestFlankTarget.EntityName}!");
            }

            // ===================================================================
            // 3. FALLBACK: BERGERAK + TEMBAK DENGAN COVER (tanpa flanking)
            // Aggressive tidak suka diam — selalu berusaha menutup jarak ke musuh
            // ===================================================================
            Vector2Int bestMoveTile = actor.GridPosition;
            CombatEntity2D bestShootTarget = null;
            float bestTileScore = -9999f;
            int currentDist = grid.GetDistance(actor.GridPosition, nearestEnemy.GridPosition);

            foreach (var tile in reachableTiles)
            {
                if (tile == actor.GridPosition) continue;

                float score = 0f;
                int tileDist = grid.GetDistance(tile, nearestEnemy.GridPosition);
                int distCloser = currentDist - tileDist;

                // Aggressive suka menutup jarak
                if (distCloser > 0) score += distCloser * 25f;
                else score += distCloser * 15f;

                // Cover masih dihargai
                var cover = grid.GetCoverAgainst(tile, nearestEnemy.GridPosition);
                if (cover == CoverType.FullCover) score += 50f;
                else if (cover == CoverType.HalfCover) score += 35f;

                // Ada target tembak dari petak ini
                var (target, isFlank) = FindBestTargetFromTile(tile, actor.AttackRange, grid, enemies);
                if (target != null)
                {
                    score += 70f;
                    score += (200f - Mathf.Clamp(target.Stats.CurrentHP, 0, 200)) * 0.15f;
                }

                // Preferensi jarak senjata
                if (actor.AttackRange <= 4 && tileDist <= 3) score += 20f;
                else if (actor.AttackRange >= 8 && tileDist >= 4 && tileDist <= 8) score += 20f;

                if (score > bestTileScore)
                {
                    bestTileScore = score;
                    bestMoveTile = tile;
                    bestShootTarget = target;
                }
            }

            if (bestMoveTile != actor.GridPosition)
            {
                if (bestShootTarget != null)
                {
                    return new TacticalDecision2D(
                        TacticalActionType.MoveAndShoot,
                        bestMoveTile,
                        bestShootTarget,
                        false,
                        $"Aggressive: Advancing to ({bestMoveTile.x},{bestMoveTile.y}) and firing at {bestShootTarget.EntityName}.");
                }
                else
                {
                    return new TacticalDecision2D(
                        TacticalActionType.MoveToCover,
                        bestMoveTile,
                        null,
                        false,
                        $"Aggressive: Closing in using cover at ({bestMoveTile.x},{bestMoveTile.y}).");
                }
            }

            // Sudah di petak terbaik, ada target?
            var directFinal = GetDirectTargetsFrom(actor.GridPosition, actor.AttackRange, grid, enemies);
            if (directFinal.Count > 0)
            {
                var target = directFinal.OrderBy(t => t.enemy.Stats.CurrentHP).First();
                return new TacticalDecision2D(
                    TacticalActionType.Shoot,
                    actor.GridPosition,
                    target.enemy,
                    target.isFlank,
                    $"Aggressive: Firing {actor.WeaponName} at {target.enemy.EntityName}.");
            }

            return new TacticalDecision2D(TacticalActionType.Defend, actor.GridPosition, null, false,
                "Aggressive: Fortifying position while awaiting hostiles.");
        }
    }
}
