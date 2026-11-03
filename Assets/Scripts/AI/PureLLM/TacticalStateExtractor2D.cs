using System;
using System.Collections.Generic;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.AI.PureLLM
{
    /// <summary>
    /// Mengekstrak medan laga CombatEntity2D + TacticalGrid2D menjadi snapshot DTO murni.
    /// Semua perhitungan geometris (LoS, cover XCOM, BFS reachability) dilakukan di sini
    /// sehingga LLM hanya menerima fakta terstruktur tanpa akses langsung ke engine.
    /// </summary>
    public class TacticalStateExtractor2D
    {
        private readonly int _maxRecentLogs;

        public TacticalStateExtractor2D(int maxRecentLogs = 8)
        {
            _maxRecentLogs = Math.Max(0, maxRecentLogs);
        }

        public TacticalStateSnapshot2D ExtractSnapshot(
            CombatEntity2D actor,
            TacticalGrid2D grid,
            IReadOnlyList<CombatEntity2D> combatants,
            int round,
            int turn,
            IReadOnlyList<string> history)
        {
            var snapshot = new TacticalStateSnapshot2D
            {
                RoundNumber = round,
                TurnNumber = turn,
                ActorEntity = actor,
                AllCombatants = new List<CombatEntity2D>()
            };

            if (combatants != null)
            {
                for (int i = 0; i < combatants.Count; i++)
                {
                    if (combatants[i] != null) snapshot.AllCombatants.Add(combatants[i]);
                }
            }

            if (grid != null)
            {
                snapshot.Grid = new GridStateDTO2D
                {
                    Width = grid.Width,
                    Height = grid.Height,
                    ExtractionZoneX = 0,
                    CellSize = grid.CellSize
                };
                snapshot.Obstacles = ExtractObstacles(grid);
            }

            AppendRecentLogs(snapshot, history);

            if (actor == null)
                return snapshot;

            var enemies = GetLivingEnemies(actor, snapshot.AllCombatants);
            var allies = GetLivingAllies(actor, snapshot.AllCombatants);
            var nearestEnemy = GetNearestEnemy(actor.GridPosition, enemies, grid);

            snapshot.CurrentActor = BuildActor(actor, nearestEnemy, grid);

            for (int i = 0; i < allies.Count; i++)
                snapshot.Allies.Add(BuildAlly(allies[i], actor, enemies, grid));

            for (int i = 0; i < enemies.Count; i++)
                snapshot.Enemies.Add(BuildEnemy(enemies[i], actor, grid));

            if (grid != null)
                snapshot.ReachableTiles = BuildReachableTiles(actor, nearestEnemy, grid);

            return snapshot;
        }

        private static ActorStateDTO2D BuildActor(CombatEntity2D actor, CombatEntity2D nearestEnemy, TacticalGrid2D grid)
        {
            var skill = TacticalSkillCatalog2D.Resolve(actor);
            var dto = new ActorStateDTO2D
            {
                SkillRange = skill != null ? skill.Range : 0
            };
            FillCommon(dto, actor, nearestEnemy, grid);
            return dto;
        }

        private static AllyStateDTO2D BuildAlly(
            CombatEntity2D ally,
            CombatEntity2D actor,
            List<CombatEntity2D> enemies,
            TacticalGrid2D grid)
        {
            var nearestEnemy = GetNearestEnemy(ally.GridPosition, enemies, grid);
            var dto = new AllyStateDTO2D();
            FillCommon(dto, ally, nearestEnemy, grid);

            if (grid != null)
            {
                dto.DistanceFromActor = grid.GetDistance(actor.GridPosition, ally.GridPosition);
                dto.HasLineOfSight = grid.HasLineOfSight(actor.GridPosition, ally.GridPosition);
                dto.CoverAgainstNearestEnemy = nearestEnemy != null
                    ? CoverTypeName(grid.GetCoverAgainst(ally.GridPosition, nearestEnemy.GridPosition))
                    : CoverTypeName(CoverType.None);
            }

            return dto;
        }

        private static EnemyStateDTO2D BuildEnemy(CombatEntity2D enemy, CombatEntity2D actor, TacticalGrid2D grid)
        {
            var dto = new EnemyStateDTO2D();
            FillCommon(dto, enemy, actor, grid);

            if (grid != null)
            {
                dto.DistanceFromActor = grid.GetDistance(actor.GridPosition, enemy.GridPosition);
                dto.HasLineOfSight = grid.HasLineOfSight(actor.GridPosition, enemy.GridPosition);
                dto.CoverAgainstActor = CoverTypeName(grid.GetCoverAgainst(enemy.GridPosition, actor.GridPosition));
                dto.IsFlankable = grid.IsFlanked(enemy.GridPosition, actor.GridPosition);
            }

            return dto;
        }

        private static void FillCommon(
            CombatantStateDTO2D dto,
            CombatEntity2D entity,
            CombatEntity2D nearestThreat,
            TacticalGrid2D grid)
        {
            dto.EntityId = entity.EntityId;
            dto.Name = entity.EntityName;
            dto.Team = entity.Team.ToString();
            dto.Trait = entity.CurrentTraitId;
            dto.X = entity.GridPosition.x;
            dto.Y = entity.GridPosition.y;

            if (entity.Stats != null)
            {
                dto.HP = entity.Stats.CurrentHP;
                dto.MaxHP = entity.Stats.MaxHP;
                dto.MP = entity.Stats.CurrentMP;
                dto.MaxMP = entity.Stats.MaxMP;
                dto.Rationality = entity.Stats.Rationality;
                dto.Composure = entity.Stats.ComposureLevel;
                dto.Speed = entity.Stats.Speed;
                dto.IsAlive = entity.Stats.IsAlive;
            }

            dto.IsExtracted = entity.IsExtracted;
            dto.InCover = entity.InCover;
            dto.CoverType = CoverTypeName(entity.ActiveCoverType);
            dto.IsFlanked = entity.IsFlanked;
            dto.IsDefending = entity.IsDefending;
            dto.IsFleeing = entity.IsFleeing;
            dto.IsGuarded = entity.IsGuarded;
            dto.GuardTurnsRemaining = entity.GuardTurnsRemaining;
            dto.WeaponName = entity.WeaponName;
            dto.AttackRange = entity.AttackRange;
            dto.MoveRange = entity.MoveRange;

            var skill = TacticalSkillCatalog2D.Resolve(entity);
            dto.SkillId = skill != null ? skill.SkillId : string.Empty;
            dto.IsSkillReady = entity.IsSkillReady;
            dto.SkillCooldownTurns = entity.SkillCooldownTurns;
            dto.IsImmuneToPsychologicalEffects = entity.IsImmuneToPsychologicalEffects;

            // Cover aktif sudah dihitung engine terhadap ancaman terdekat saat giliran dimulai.
            // Nilai nearestThreat dipertahankan sebagai parameter agar konsisten dengan API extractor.
            if (grid != null && nearestThreat != null && dto.CoverType == CoverTypeName(CoverType.None))
            {
                dto.CoverType = CoverTypeName(grid.GetCoverAgainst(entity.GridPosition, nearestThreat.GridPosition));
            }
        }

        private static List<ReachableTileDTO2D> BuildReachableTiles(
            CombatEntity2D actor,
            CombatEntity2D nearestEnemy,
            TacticalGrid2D grid)
        {
            var result = new List<ReachableTileDTO2D>();
            var reachable = grid.GetReachableTiles(actor.GridPosition, actor.MoveRange);
            if (reachable == null) return result;

            for (int i = 0; i < reachable.Count; i++)
            {
                var tile = reachable[i];
                var cover = CoverType.None;
                bool flanked = false;

                if (nearestEnemy != null)
                {
                    cover = grid.GetCoverAgainst(tile, nearestEnemy.GridPosition);
                    flanked = grid.IsFlanked(tile, nearestEnemy.GridPosition);
                }

                result.Add(new ReachableTileDTO2D
                {
                    X = tile.x,
                    Y = tile.y,
                    DistanceFromActor = grid.GetDistance(actor.GridPosition, tile),
                    CoverType = CoverTypeName(cover),
                    IsFlanked = flanked,
                    IsExtractionZone = grid.IsExtractionTile(tile)
                });
            }

            return result;
        }

        private static List<ObstacleDTO2D> ExtractObstacles(TacticalGrid2D grid)
        {
            var result = new List<ObstacleDTO2D>();
            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    var cell = grid.GetCell(new UnityEngine.Vector2Int(x, y));
                    if (cell == null) continue;
                    if (cell.Type != CellType.HalfCover &&
                        cell.Type != CellType.FullCover &&
                        cell.Type != CellType.Blocked)
                    {
                        continue;
                    }

                    result.Add(new ObstacleDTO2D
                    {
                        X = x,
                        Y = y,
                        Type = cell.Type.ToString()
                    });
                }
            }
            return result;
        }

        private void AppendRecentLogs(TacticalStateSnapshot2D snapshot, IReadOnlyList<string> history)
        {
            if (history == null || history.Count == 0 || _maxRecentLogs <= 0) return;

            int start = Math.Max(0, history.Count - _maxRecentLogs);
            for (int i = start; i < history.Count; i++)
                snapshot.RecentLogs.Add(history[i] ?? string.Empty);
        }

        private static List<CombatEntity2D> GetLivingEnemies(
            CombatEntity2D actor,
            List<CombatEntity2D> combatants)
        {
            var result = new List<CombatEntity2D>();
            if (combatants == null) return result;

            for (int i = 0; i < combatants.Count; i++)
            {
                var c = combatants[i];
                if (c == null || !c.IsAlive || c.IsExtracted || c.Team == actor.Team) continue;
                result.Add(c);
            }
            return result;
        }

        private static List<CombatEntity2D> GetLivingAllies(
            CombatEntity2D actor,
            List<CombatEntity2D> combatants)
        {
            var result = new List<CombatEntity2D>();
            if (combatants == null) return result;

            for (int i = 0; i < combatants.Count; i++)
            {
                var c = combatants[i];
                if (c == null || c == actor || !c.IsAlive || c.IsExtracted || c.Team != actor.Team) continue;
                result.Add(c);
            }
            return result;
        }

        private static CombatEntity2D GetNearestEnemy(
            UnityEngine.Vector2Int from,
            List<CombatEntity2D> enemies,
            TacticalGrid2D grid)
        {
            if (enemies == null || enemies.Count == 0 || grid == null) return null;

            CombatEntity2D best = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e == null) continue;
                int d = grid.GetDistance(from, e.GridPosition);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = e;
                }
            }
            return best;
        }

        public static string CoverTypeName(CoverType cover)
        {
            switch (cover)
            {
                case CoverType.HalfCover: return "HalfCover";
                case CoverType.FullCover: return "FullCover";
                default: return "None";
            }
        }
    }
}
