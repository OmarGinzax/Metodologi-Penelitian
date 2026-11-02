using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.AI.Baseline
{
    public enum TacticalActionType
    {
        Idle,
        Shoot,
        MoveToCover,
        MoveAndShoot,
        Defend
    }

    [Serializable]
    public class TacticalDecision2D
    {
        public TacticalActionType Action;
        public Vector2Int MoveDestination;
        public CombatEntity2D TargetEnemy;
        public bool IsFlanking;
        public string Reasoning;

        public TacticalDecision2D(TacticalActionType action, Vector2Int dest, CombatEntity2D target, bool isFlanking, string reason)
        {
            Action = action;
            MoveDestination = dest;
            TargetEnemy = target;
            IsFlanking = isFlanking;
            Reasoning = reason;
        }
    }

    /// <summary>
    /// AI Hierarkis Sederhana (Simple HFSM) untuk pertarungan 2D taktis.
    /// Mengevaluasi Line of Sight (LoS), jarak tembak, sudut flanking, dan posisi cover
    /// untuk mengeksekusi Move, Shoot, atau Defend secara cerdas dan deterministik.
    /// </summary>
    public class SimpleTacticalHFSM
    {
        public TacticalDecision2D DecideTurn(CombatEntity2D actor, TacticalGrid2D grid, List<CombatEntity2D> allCombatants)
        {
            if (actor == null || !actor.IsAlive || grid == null)
            {
                return new TacticalDecision2D(TacticalActionType.Idle, Vector2Int.zero, null, false, "Invalid state.");
            }

            var enemies = allCombatants.Where(c => c != null && c.IsAlive && c.Team != actor.Team).ToList();
            if (enemies.Count == 0)
            {
                return new TacticalDecision2D(TacticalActionType.Idle, actor.GridPosition, null, false, "No living hostiles on field.");
            }

            // -------------------------------------------------------------
            // 1. EVALUASI TEMBAKAN LANGSUNG DARI POSISI SAAT INI
            // -------------------------------------------------------------
            var validDirectTargets = new List<(CombatEntity2D enemy, bool isFlank, int dist)>();
            foreach (var enemy in enemies)
            {
                int dist = grid.GetDistance(actor.GridPosition, enemy.GridPosition);
                if (dist <= actor.AttackRange)
                {
                    if (grid.HasLineOfSight(actor.GridPosition, enemy.GridPosition))
                    {
                        bool isFlank = grid.IsFlanked(enemy.GridPosition, actor.GridPosition);
                        validDirectTargets.Add((enemy, isFlank, dist));
                    }
                }
            }

            // Jika Tasha (Recon Flanker dengan Vector Rifle), utamakan manuver flank jika belum flanking
            bool isFlankerUnit = actor.CurrentTraitId.Contains("DisciplinedProfessional") || actor.EntityName.ToLower().Contains("tasha");

            // Jika sudah di dalam cover dan memiliki target langsung, tembak langsung dari cover
            // Kecuali jika unit adalah flanker (Tasha) yang ingin bermanuver mencari sudut FLANK CRITICAL
            if (validDirectTargets.Count > 0 && actor.ActiveCoverType != CoverType.None && !isFlankerUnit)
            {
                // Urutkan prioritas: Target yang Flanked -> Target HP terendah -> Target terdekat
                var bestTarget = validDirectTargets
                    .OrderByDescending(t => t.isFlank)
                    .ThenBy(t => t.enemy.Stats.CurrentHP)
                    .ThenBy(t => t.dist)
                    .First();

                return new TacticalDecision2D(
                    TacticalActionType.Shoot, 
                    actor.GridPosition, 
                    bestTarget.enemy, 
                    bestTarget.isFlank,
                    $"Direct engagement from cover: Firing {actor.WeaponName} at {bestTarget.enemy.EntityName} (Flanked: {bestTarget.isFlank}).");
            }

            // -------------------------------------------------------------
            // 2. EVALUASI MANUVER PERPINDAHAN KE COVER / SUDUT FLANKING
            // -------------------------------------------------------------
            var reachableTiles = grid.GetReachableTiles(actor.GridPosition, actor.MoveRange);
            var nearestEnemy = enemies.OrderBy(e => grid.GetDistance(actor.GridPosition, e.GridPosition)).First();

            Vector2Int bestMoveTile = actor.GridPosition;
            CombatEntity2D bestShootTargetAfterMove = null;
            bool bestMoveIsFlank = false;
            float bestTileScore = -999f;
            int bestDistCloser = -999;
            int bestMoveSteps = 0;

            int currentDistToEnemy = grid.GetDistance(actor.GridPosition, nearestEnemy.GridPosition);

            foreach (var tile in reachableTiles)
            {
                float score = 0f;
                int tileDistToEnemy = grid.GetDistance(tile, nearestEnemy.GridPosition);
                int distCloser = currentDistToEnemy - tileDistToEnemy;
                int moveSteps = grid.GetDistance(actor.GridPosition, tile);

                // A. Heuristik Pergerakan & Penggunaan Jangkauan Penuh (Move Range 3-4)
                // Jika belum dalam jangkauan tembak efektif, manfaatkan seluruh langkah untuk mendekat
                if (validDirectTargets.Count == 0)
                {
                    if (distCloser > 0)
                    {
                        score += distCloser * 25f; // Bonus signifikan setiap petak lebih dekat
                    }
                    else if (distCloser < 0)
                    {
                        score += distCloser * 15f; // Penalti jika mundur menjauh dari medan tempur
                    }
                }

                // B. Evaluasi Cover terhadap musuh terdekat
                var cover = grid.GetCoverAgainst(tile, nearestEnemy.GridPosition);
                if (cover == CoverType.FullCover) score += 50f;
                else if (cover == CoverType.HalfCover) score += 35f;

                // C. Evaluasi Tembakan dari petak tujuan
                CombatEntity2D potentialTarget = null;
                bool canFlankFromTile = false;

                foreach (var enemy in enemies)
                {
                    int dist = grid.GetDistance(tile, enemy.GridPosition);
                    if (dist <= actor.AttackRange && grid.HasLineOfSight(tile, enemy.GridPosition))
                    {
                        bool flank = grid.IsFlanked(enemy.GridPosition, tile);
                        if (flank)
                        {
                            canFlankFromTile = true;
                            potentialTarget = enemy;
                            break;
                        }
                        if (potentialTarget == null)
                        {
                            potentialTarget = enemy;
                        }
                    }
                }

                if (potentialTarget != null)
                {
                    score += 60f; // Bisa menembak musuh dari petak ini
                    if (canFlankFromTile)
                    {
                        score += isFlankerUnit ? 80f : 40f; // Bonus besar untuk sudut flanking Tasha
                    }
                }

                // D. Penyesuaian peran taktis unit
                if (actor.AttackRange <= 4 && tileDistToEnemy <= 3)
                {
                    // Pointman Shotgunner (Marcus) unggul dalam jarak dekat
                    score += 20f;
                }
                else if (actor.AttackRange >= 8 && tileDistToEnemy >= 4 && tileDistToEnemy <= 8)
                {
                    // Sniper (Elena) menjaga jarak tembak optimal
                    score += 20f;
                }

                // E. Penilaian terbaik dan tie-breaking memanfaatkan kapasitas pergerakan
                bool isBetter = false;
                if (score > bestTileScore + 0.01f)
                {
                    isBetter = true;
                }
                else if (Mathf.Abs(score - bestTileScore) <= 0.01f)
                {
                    // Tie-break: pilih petak yang lebih memajukan posisi ke musuh, atau lebih memanfaatkan kapasitas gerak
                    if (distCloser > bestDistCloser)
                    {
                        isBetter = true;
                    }
                    else if (distCloser == bestDistCloser && moveSteps > bestMoveSteps)
                    {
                        isBetter = true;
                    }
                }

                if (isBetter)
                {
                    bestTileScore = score;
                    bestDistCloser = distCloser;
                    bestMoveSteps = moveSteps;
                    bestMoveTile = tile;
                    bestShootTargetAfterMove = potentialTarget;
                    bestMoveIsFlank = canFlankFromTile;
                }
            }

            // -------------------------------------------------------------
            // 3. EKSEKUSI KEPUTUSAN TAKTIS
            // -------------------------------------------------------------
            if (bestMoveTile != actor.GridPosition)
            {
                if (bestShootTargetAfterMove != null)
                {
                    return new TacticalDecision2D(
                        TacticalActionType.MoveAndShoot, 
                        bestMoveTile, 
                        bestShootTargetAfterMove, 
                        bestMoveIsFlank,
                        $"Maneuvering to ({bestMoveTile.x}, {bestMoveTile.y}) and firing at {bestShootTargetAfterMove.EntityName}.");
                }
                else
                {
                    return new TacticalDecision2D(
                        TacticalActionType.MoveToCover, 
                        bestMoveTile, 
                        null, 
                        false,
                        $"Repositioning to defensive cover at ({bestMoveTile.x}, {bestMoveTile.y}).");
                }
            }

            // Jika sudah di posisi terbaik dan ada target tembak
            if (validDirectTargets.Count > 0)
            {
                var direct = validDirectTargets.OrderByDescending(t => t.isFlank).First();
                return new TacticalDecision2D(
                    TacticalActionType.Shoot, 
                    actor.GridPosition, 
                    direct.enemy, 
                    direct.isFlank,
                    $"Holding position and firing {actor.WeaponName} at {direct.enemy.EntityName}.");
            }

            // Jika tidak ada target dan dalam bahaya/cover
            return new TacticalDecision2D(
                TacticalActionType.Defend, 
                actor.GridPosition, 
                null, 
                false,
                "No targets in range/LoS. Fortifying position (Hunker Down).");
        }
    }
}
