using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.AI.PureLLM
{
    /// <summary>
    /// Snapshot medan laga 2D yang immutable dari sudut pandang agen LLM.
    /// Seluruh data yang dibutuhkan prompt dirangkum dalam DTO tanpa referensi MonoBehaviour
    /// (kecuali referensi runtime non-serialized untuk kebutuhan classifier).
    /// </summary>
    [Serializable]
    public class TacticalStateSnapshot2D
    {
        public int RoundNumber;
        public int TurnNumber;
        public GridStateDTO2D Grid;
        public ActorStateDTO2D CurrentActor;
        public List<AllyStateDTO2D> Allies = new List<AllyStateDTO2D>();
        public List<EnemyStateDTO2D> Enemies = new List<EnemyStateDTO2D>();
        public List<ReachableTileDTO2D> ReachableTiles = new List<ReachableTileDTO2D>();
        public List<ObstacleDTO2D> Obstacles = new List<ObstacleDTO2D>();
        public List<string> RecentLogs = new List<string>();

        /// <summary>Referensi runtime (tidak diserialisasi) untuk classifier; bukan bagian prompt.</summary>
        [NonSerialized] public CombatEntity2D ActorEntity;
        [NonSerialized] public List<CombatEntity2D> AllCombatants;

        public string ToJson()
        {
            var sb = new StringBuilder(2048);

            sb.Append('{');
            sb.Append("\"round\":").Append(RoundNumber).Append(',');
            sb.Append("\"turn\":").Append(TurnNumber).Append(',');

            sb.Append("\"grid\":");
            AppendGridJson(sb, Grid);
            sb.Append(',');

            sb.Append("\"current_actor\":");
            AppendActorJson(sb, CurrentActor);
            sb.Append(',');

            sb.Append("\"allies\":[");
            for (int i = 0; i < Allies.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendAllyJson(sb, Allies[i]);
            }
            sb.Append("],");

            sb.Append("\"enemies\":[");
            for (int i = 0; i < Enemies.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendEnemyJson(sb, Enemies[i]);
            }
            sb.Append("],");

            sb.Append("\"reachable_tiles\":[");
            for (int i = 0; i < ReachableTiles.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendReachableTileJson(sb, ReachableTiles[i]);
            }
            sb.Append("],");

            sb.Append("\"obstacles\":[");
            for (int i = 0; i < Obstacles.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendObstacleJson(sb, Obstacles[i]);
            }
            sb.Append("],");

            sb.Append("\"recent_logs\":[");
            for (int i = 0; i < RecentLogs.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(JsonUtil.Escape(RecentLogs[i])).Append('"');
            }
            sb.Append("]");

            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendGridJson(StringBuilder sb, GridStateDTO2D grid)
        {
            if (grid == null) { sb.Append("null"); return; }
            sb.Append('{');
            sb.Append("\"width\":").Append(grid.Width).Append(',');
            sb.Append("\"height\":").Append(grid.Height).Append(',');
            sb.Append("\"extraction_zone_x\":").Append(grid.ExtractionZoneX).Append(',');
            sb.Append("\"cell_size\":").Append(F(grid.CellSize));
            sb.Append('}');
        }

        private static void AppendActorJson(StringBuilder sb, ActorStateDTO2D a)
        {
            if (a == null) { sb.Append("null"); return; }
            sb.Append('{');
            AppendCommonCombatantFields(sb, a);
            sb.Append(",\"skill_range\":").Append(a.SkillRange);
            sb.Append('}');
        }

        private static void AppendAllyJson(StringBuilder sb, AllyStateDTO2D a)
        {
            if (a == null) { sb.Append("null"); return; }
            sb.Append('{');
            AppendCommonCombatantFields(sb, a);
            sb.Append(",\"distance\":").Append(a.DistanceFromActor);
            sb.Append(",\"has_los\":").Append(a.HasLineOfSight ? "true" : "false");
            sb.Append(",\"cover_against_nearest_enemy\":\"").Append(JsonUtil.Escape(a.CoverAgainstNearestEnemy)).Append('"');
            sb.Append('}');
        }

        private static void AppendEnemyJson(StringBuilder sb, EnemyStateDTO2D e)
        {
            if (e == null) { sb.Append("null"); return; }
            sb.Append('{');
            AppendCommonCombatantFields(sb, e);
            sb.Append(",\"distance\":").Append(e.DistanceFromActor);
            sb.Append(",\"has_los\":").Append(e.HasLineOfSight ? "true" : "false");
            sb.Append(",\"cover_against_actor\":\"").Append(JsonUtil.Escape(e.CoverAgainstActor)).Append('"');
            sb.Append(",\"is_flankable\":").Append(e.IsFlankable ? "true" : "false");
            sb.Append('}');
        }

        private static void AppendCommonCombatantFields(StringBuilder sb, CombatantStateDTO2D c)
        {
            sb.Append("\"entity_id\":\"").Append(JsonUtil.Escape(c.EntityId)).Append('"');
            sb.Append(",\"name\":\"").Append(JsonUtil.Escape(c.Name)).Append('"');
            sb.Append(",\"team\":\"").Append(JsonUtil.Escape(c.Team)).Append('"');
            sb.Append(",\"trait\":\"").Append(JsonUtil.Escape(c.Trait)).Append('"');
            sb.Append(",\"x\":").Append(c.X);
            sb.Append(",\"y\":").Append(c.Y);
            sb.Append(",\"hp\":").Append(c.HP);
            sb.Append(",\"max_hp\":").Append(c.MaxHP);
            sb.Append(",\"mp\":").Append(c.MP);
            sb.Append(",\"max_mp\":").Append(c.MaxMP);
            sb.Append(",\"rationality\":").Append(c.Rationality);
            sb.Append(",\"composure\":\"").Append(JsonUtil.Escape(c.Composure)).Append('"');
            sb.Append(",\"is_alive\":").Append(c.IsAlive ? "true" : "false");
            sb.Append(",\"is_extracted\":").Append(c.IsExtracted ? "true" : "false");
            sb.Append(",\"in_cover\":").Append(c.InCover ? "true" : "false");
            sb.Append(",\"cover_type\":\"").Append(JsonUtil.Escape(c.CoverType)).Append('"');
            sb.Append(",\"is_flanked\":").Append(c.IsFlanked ? "true" : "false");
            sb.Append(",\"is_defending\":").Append(c.IsDefending ? "true" : "false");
            sb.Append(",\"is_fleeing\":").Append(c.IsFleeing ? "true" : "false");
            sb.Append(",\"is_guarded\":").Append(c.IsGuarded ? "true" : "false");
            sb.Append(",\"guard_turns\":").Append(c.GuardTurnsRemaining);
            sb.Append(",\"weapon\":\"").Append(JsonUtil.Escape(c.WeaponName)).Append('"');
            sb.Append(",\"attack_range\":").Append(c.AttackRange);
            sb.Append(",\"move_range\":").Append(c.MoveRange);
            sb.Append(",\"skill_id\":\"").Append(JsonUtil.Escape(c.SkillId)).Append('"');
            sb.Append(",\"skill_ready\":").Append(c.IsSkillReady ? "true" : "false");
            sb.Append(",\"skill_cooldown\":").Append(c.SkillCooldownTurns);
            sb.Append(",\"speed\":").Append(c.Speed);
            sb.Append(",\"immune_to_psych\":").Append(c.IsImmuneToPsychologicalEffects ? "true" : "false");
        }

        private static void AppendReachableTileJson(StringBuilder sb, ReachableTileDTO2D t)
        {
            if (t == null) { sb.Append("null"); return; }
            sb.Append('{');
            sb.Append("\"x\":").Append(t.X).Append(',');
            sb.Append("\"y\":").Append(t.Y).Append(',');
            sb.Append("\"distance\":").Append(t.DistanceFromActor).Append(',');
            sb.Append("\"cover_type\":\"").Append(JsonUtil.Escape(t.CoverType)).Append("\",");
            sb.Append("\"is_flanked\":").Append(t.IsFlanked ? "true" : "false").Append(',');
            sb.Append("\"is_extraction\":").Append(t.IsExtractionZone ? "true" : "false");
            sb.Append('}');
        }

        private static void AppendObstacleJson(StringBuilder sb, ObstacleDTO2D o)
        {
            if (o == null) { sb.Append("null"); return; }
            sb.Append('{');
            sb.Append("\"x\":").Append(o.X).Append(',');
            sb.Append("\"y\":").Append(o.Y).Append(',');
            sb.Append("\"type\":\"").Append(JsonUtil.Escape(o.Type)).Append('"');
            sb.Append('}');
        }

        private static string F(float value)
            => value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    [Serializable]
    public class GridStateDTO2D
    {
        public int Width;
        public int Height;
        public int ExtractionZoneX;
        public float CellSize;
    }

    [Serializable]
    public class CombatantStateDTO2D
    {
        public string EntityId;
        public string Name;
        public string Team;
        public string Trait;
        public int X;
        public int Y;
        public int HP;
        public int MaxHP;
        public int MP;
        public int MaxMP;
        public int Rationality;
        public string Composure;
        public bool IsAlive;
        public bool IsExtracted;
        public bool InCover;
        public string CoverType;
        public bool IsFlanked;
        public bool IsDefending;
        public bool IsFleeing;
        public bool IsGuarded;
        public int GuardTurnsRemaining;
        public string WeaponName;
        public int AttackRange;
        public int MoveRange;
        public string SkillId;
        public bool IsSkillReady;
        public int SkillCooldownTurns;
        public int Speed;
        public bool IsImmuneToPsychologicalEffects;
    }

    [Serializable]
    public class ActorStateDTO2D : CombatantStateDTO2D
    {
        public int SkillRange;
    }

    [Serializable]
    public class AllyStateDTO2D : CombatantStateDTO2D
    {
        public int DistanceFromActor;
        public bool HasLineOfSight;
        public string CoverAgainstNearestEnemy;
    }

    [Serializable]
    public class EnemyStateDTO2D : CombatantStateDTO2D
    {
        public int DistanceFromActor;
        public bool HasLineOfSight;
        public string CoverAgainstActor;
        public bool IsFlankable;
    }

    [Serializable]
    public class ReachableTileDTO2D
    {
        public int X;
        public int Y;
        public int DistanceFromActor;
        public string CoverType;
        public bool IsFlanked;
        public bool IsExtractionZone;
    }

    [Serializable]
    public class ObstacleDTO2D
    {
        public int X;
        public int Y;
        public string Type;
    }
}
