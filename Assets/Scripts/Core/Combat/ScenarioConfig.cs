using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPGStrategy.Core.Combat
{
    [Serializable]
    public class EntitySpawnConfig
    {
        public string EntityId = "actor_1";
        public string EntityName = "Knight";
        public TeamSide Team = TeamSide.PlayerTeam;
        public EntityStats BaseStats = new EntityStats(100, 50, 25, 10, 15);
        public List<SkillData> Skills = new List<SkillData>();
        public List<InventoryItemEntry> Items = new List<InventoryItemEntry>();
    }

    [CreateAssetMenu(fileName = "NewScenario", menuName = "RPG Combat/Scenario Config")]
    public class ScenarioConfig : ScriptableObject
    {
        [SerializeField] private string scenarioId = "Scenario_A";
        [SerializeField] private string scenarioName = "Critical HP Survival";
        [TextArea(2, 4)]
        [SerializeField] private string description = "NPC begins with low HP against aggressive enemies.";
        [SerializeField] private int turnLimit = 50;
        [SerializeField] private List<EntitySpawnConfig> combatants = new List<EntitySpawnConfig>();

        public string ScenarioId => scenarioId;
        public string ScenarioName => scenarioName;
        public string Description => description;
        public int TurnLimit => turnLimit;
        public IReadOnlyList<EntitySpawnConfig> Combatants => combatants;

        public void Initialize(string id, string name, string desc, int maxTurns)
        {
            scenarioId = id;
            scenarioName = name;
            description = desc;
            turnLimit = maxTurns;
            combatants = new List<EntitySpawnConfig>();
        }

        public void AddCombatant(EntitySpawnConfig config)
        {
            if (config != null) combatants.Add(config);
        }
    }
}
