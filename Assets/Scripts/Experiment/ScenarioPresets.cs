using System;
using System.Collections.Generic;
using RPGStrategy.AI.Compression;
using RPGStrategy.Core.Combat;
using RPGStrategy.Core.Command;
using RPGStrategy.Core.Data;

namespace RPGStrategy.Experiment
{
    /// <summary>
    /// A data transfer struct for building CombatEntity instances in ScenarioPresets.
    /// Contains all initialization parameters needed to call CombatEntity.Initialize().
    /// </summary>
    [Serializable]
    public struct EntitySpawnData
    {
        public string EntityId;
        public string Name;
        public TeamSide Team;
        public EntityStats Stats;
        public string TraitId;
        public float FieldDistance;
    }

    /// <summary>
    /// M7 – Scenario Presets.
    /// Defines the 4 reproducible battle scenarios for the controlled experiment.
    /// All scenarios use the same Sector 7 Overpass map geometry but vary squad composition
    /// and initial conditions to isolate the X1/X2/X3 independent variables.
    ///
    /// Scenario A: Full Squad (6) vs HOUND-IV + 2 Scout Drones (Standard – baseline)
    /// Scenario B: Reduced Squad (4) vs two Scout Drones (pressure test)
    /// Scenario C: Demoralised Squad (low rationality) vs HOUND-IV
    /// Scenario D: High-relationship Squad vs HOUND-IV (relationship dynamics test)
    /// </summary>
    public static class ScenarioPresets
    {
        public enum ScenarioId { ScenarioA, ScenarioB, ScenarioC, ScenarioD }

        // ─── Scenario A: Standard Engagement ──────────────────────────────────────
        public static List<EntitySpawnData> GetSquadA()
        {
            return new List<EntitySpawnData>
            {
                MakeSquad("agent_marcus",  "Sgt. Marcus Vale",   "SteadfastVeteran",  120, 80,  18, 10, 9,  75, 30f),
                MakeSquad("agent_lyra",   "Cpl. Lyra Kade",     "CautiousTactician", 100, 100, 14, 8,  10, 68, 40f),
                MakeSquad("agent_torres", "Pvt. Torres",        "LoyalProtector",    110, 90,  15, 12, 8,  60, 35f),
                MakeSquad("agent_cross",  "Cpl. Cross",         "HeroicIdealizer",   95,  110, 20, 7,  11, 55, 45f),
                MakeSquad("agent_nyx",    "Medic Nyx",          "LoyalProtector",    85,  120, 10, 9,  7,  70, 38f),
                MakeSquad("agent_rook",   "Pvt. Rook",          "PanickingRecruit",  90,  70,  13, 8,  9,  30, 50f),
            };
        }

        public static List<EntitySpawnData> GetEnemiesA()
        {
            return new List<EntitySpawnData>
            {
                MakeEnemy("enemy_hound_iv", "HOUND-IV Heavy Strider", 300, 50, 28, 18, 6,  10f),
                MakeEnemy("enemy_drone_1",  "Scout Drone Alpha",      60,  30, 12, 4,  13, 20f),
                MakeEnemy("enemy_drone_2",  "Scout Drone Beta",       60,  30, 12, 4,  13, 25f),
            };
        }

        // ─── Scenario B: Reduced Squad ────────────────────────────────────────────
        public static List<EntitySpawnData> GetSquadB()
        {
            var squad = GetSquadA();
            squad.RemoveAt(5); // remove Rook
            squad.RemoveAt(4); // remove Nyx
            return squad;
        }

        public static List<EntitySpawnData> GetEnemiesB()
        {
            return new List<EntitySpawnData>
            {
                MakeEnemy("enemy_drone_1", "Scout Drone Alpha", 80, 30, 14, 5, 14, 18f),
                MakeEnemy("enemy_drone_2", "Scout Drone Beta",  80, 30, 14, 5, 14, 22f),
            };
        }

        // ─── Scenario C: Demoralised Squad ────────────────────────────────────────
        public static List<EntitySpawnData> GetSquadC()
        {
            var squad = GetSquadA();
            for (int i = 0; i < squad.Count; i++)
            {
                var s = squad[i];
                s.Stats = new EntityStats(s.Stats.MaxHP, s.Stats.MaxMP, s.Stats.AttackPower,
                    s.Stats.DefensePower, s.Stats.Speed, Math.Max(10, s.Stats.Rationality - 40));
                squad[i] = s;
            }
            return squad;
        }

        public static List<EntitySpawnData> GetEnemiesC() => GetEnemiesA();

        // ─── Scenario D: High-Relationship Squad ──────────────────────────────────
        public static List<EntitySpawnData> GetSquadD() => GetSquadA();
        public static List<EntitySpawnData> GetEnemiesD() => GetEnemiesA();

        /// <summary>
        /// Returns pre-set strong bonds for Scenario D.
        /// In Scenarios A-C relationships start neutral and evolve dynamically.
        /// </summary>
        /// <summary>
        /// Returns a lookup of (sourceId, targetId) → affinity for Scenario D.
        /// Apply via entity.SetRelationship(targetId, affinity) when initialising CombatEntities.
        /// </summary>
        public static List<(string source, string target, int affinity)> GetRelationshipsD()
        {
            return new List<(string, string, int)>
            {
                ("agent_marcus", "agent_torres", 90),
                ("agent_torres", "agent_marcus", 90),
                ("agent_lyra",   "agent_cross",  75),
                ("agent_cross",  "agent_lyra",   75),
                ("agent_nyx",    "agent_rook",   60),
                ("agent_rook",   "agent_nyx",    60),
            };
        }

        // ─── Commander Directive Presets ──────────────────────────────────────────
        public static CommanderDirectiveDTO GetDirective(DirectiveType type, int issuedAtTurn = 1, string text = null)
        {
            return new CommanderDirectiveDTO(type, null, text ?? DefaultDirectiveText(type), issuedAtTurn);
        }

        private static string DefaultDirectiveText(DirectiveType type)
        {
            switch (type)
            {
                case DirectiveType.FocusFireTarget:          return "All units focus fire on primary target. Eliminate it fast!";
                case DirectiveType.FullTacticalRetreat:      return "All units fall back to the extraction point immediately!";
                case DirectiveType.DefensiveHold:            return "Maintain defensive positions. Do NOT let them advance!";
                case DirectiveType.SuppressiveContainment:   return "Suppress the corridor — lay down constant fire!";
                case DirectiveType.ProtectVulnerableAlly:    return "Prioritize protecting wounded allies. Keep the squad alive!";
                case DirectiveType.FreeEngage:               return "Free engagement — use your own initiative!";
                default:                                      return "Use your best judgment. Stay alive.";
            }
        }

        // ─── Factory Helpers ──────────────────────────────────────────────────────
        private static EntitySpawnData MakeSquad(string id, string name, string traitId,
            int hp, int mp, int atk, int def, int spd, int rat, float dist)
        {
            return new EntitySpawnData
            {
                EntityId = id, Name = name, Team = TeamSide.PlayerTeam, TraitId = traitId, FieldDistance = dist,
                Stats = new EntityStats(hp, mp, atk, def, spd, rat)
            };
        }

        private static EntitySpawnData MakeEnemy(string id, string name,
            int hp, int mp, int atk, int def, int spd, float dist)
        {
            return new EntitySpawnData
            {
                EntityId = id, Name = name, Team = TeamSide.EnemyTeam, TraitId = "HostileUnit", FieldDistance = dist,
                Stats = new EntityStats(hp, mp, atk, def, spd, 100)
            };
        }
    }
}
