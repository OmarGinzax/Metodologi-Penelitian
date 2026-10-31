using System.Linq;
using NUnit.Framework;
using UnityEngine;
using RPGStrategy.Core.Combat;
using RPGStrategy.Core.Command;
using RPGStrategy.Editor;
using RPGStrategy.Experiment;

namespace RPGStrategy.Tests
{
    [TestFixture]
    public class TestChamberSetupTests
    {
        [TearDown]
        public void Teardown()
        {
            var root = GameObject.Find("[TestChamber]");
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SetupChamber_ScenarioA_CreatesCorrectHierarchyAndCombatants()
        {
            // Act
            TestChamberSetup.SetupChamber(
                ScenarioPresets.ScenarioId.ScenarioA,
                SquadControllerType.HFSM,
                createVisuals: true);

            // Assert
            var root = GameObject.Find("[TestChamber]");
            Assert.IsNotNull(root, "Root [TestChamber] should exist.");

            var bm = root.GetComponentInChildren<BattleManager>();
            Assert.IsNotNull(bm, "BattleManager should be present under [TestChamber].");

            var orch = root.GetComponentInChildren<TestChamberOrchestrator>();
            Assert.IsNotNull(orch, "TestChamberOrchestrator should be present under [TestChamber].");
            Assert.AreEqual(ScenarioPresets.ScenarioId.ScenarioA, orch.ScenarioId);

            var combatants = root.GetComponentsInChildren<CombatEntity>();
            Assert.AreEqual(9, combatants.Length, "Scenario A should have 9 total combatants (6 squad + 3 enemies).");

            int squadCount = combatants.Count(c => c.Team == TeamSide.PlayerTeam);
            int enemyCount = combatants.Count(c => c.Team == TeamSide.EnemyTeam);
            Assert.AreEqual(6, squadCount, "Scenario A should have 6 squad members.");
            Assert.AreEqual(3, enemyCount, "Scenario A should have 3 enemy combatants.");

            // Check Boss
            var boss = combatants.FirstOrDefault(c => c.EntityId == "enemy_hound_iv");
            Assert.IsNotNull(boss, "HOUND-IV Heavy Strider should be spawned.");
            Assert.AreEqual(300, boss.Stats.MaxHP);
        }

        [Test]
        public void SetupChamber_ScenarioD_AppliesStrongEmotionalBonds()
        {
            // Act
            TestChamberSetup.SetupChamber(
                ScenarioPresets.ScenarioId.ScenarioD,
                SquadControllerType.HFSM,
                createVisuals: false);

            var root = GameObject.Find("[TestChamber]");
            var combatants = root.GetComponentsInChildren<CombatEntity>();

            var marcus = combatants.FirstOrDefault(c => c.EntityId == "agent_marcus");
            var torres = combatants.FirstOrDefault(c => c.EntityId == "agent_torres");
            var lyra = combatants.FirstOrDefault(c => c.EntityId == "agent_lyra");
            var cross = combatants.FirstOrDefault(c => c.EntityId == "agent_cross");

            Assert.IsNotNull(marcus);
            Assert.IsNotNull(torres);
            Assert.IsNotNull(lyra);
            Assert.IsNotNull(cross);

            // Verify Scenario D sworn bond affinities (+90)
            Assert.AreEqual(90, marcus.Relationships.GetAffinity("agent_torres"));
            Assert.AreEqual(90, torres.Relationships.GetAffinity("agent_marcus"));

            // Verify Scenario D trusted bonds (+75)
            Assert.AreEqual(75, lyra.Relationships.GetAffinity("agent_cross"));
            Assert.AreEqual(75, cross.Relationships.GetAffinity("agent_lyra"));
        }

        [Test]
        public void ClearChamber_RemovesRootObject()
        {
            // Arrange
            TestChamberSetup.SetupChamber(ScenarioPresets.ScenarioId.ScenarioB);
            Assert.IsNotNull(GameObject.Find("[TestChamber]"));

            // Act
            TestChamberSetup.ClearChamber();

            // Assert
            Assert.IsNull(GameObject.Find("[TestChamber]"), "[TestChamber] should be destroyed after ClearChamber().");
        }
    }
}
