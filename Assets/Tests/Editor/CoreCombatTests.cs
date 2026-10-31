using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using RPGStrategy.Core.Combat;
using RPGStrategy.Core.Command;
using RPGStrategy.Core.Data;
using RPGStrategy.Core.Extractor;

namespace RPGStrategy.Tests
{
    [TestFixture]
    public class CoreCombatTests
    {
        private GameObject _holder;

        [SetUp]
        public void SetUp()
        {
            _holder = new GameObject("TestHolder");
        }

        [TearDown]
        public void TearDown()
        {
            if (_holder != null)
            {
                Object.DestroyImmediate(_holder);
            }
        }

        private CombatEntity CreateTestEntity(string id, string name, TeamSide team, int hp, int mp, int atk, int def, int spd, int rationality = 100, string trait = "LoyalProtector", float fieldDist = 15f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_holder.transform);
            var entity = go.AddComponent<CombatEntity>();
            entity.Initialize(id, name, team, new EntityStats(hp, mp, atk, def, spd, rationality), trait, fieldDist);
            return entity;
        }

        [Test]
        public void TC_CORE_01_DamageMitigation_WhenDefending_HalvesDamage()
        {
            var defender = CreateTestEntity("def_1", "Defender", TeamSide.EnemyTeam, 100, 20, 10, 10, 5);

            defender.SetDefending(false);
            int normalDmg = defender.ApplyDamage(40);
            Assert.AreEqual(35, normalDmg, "Normal damage should be 35.");

            defender.Stats.CurrentHP = 100;
            defender.SetDefending(true);
            int defendedDmg = defender.ApplyDamage(40);
            Assert.AreEqual(18, defendedDmg, "Defended damage should be halved to 18.");
        }

        [Test]
        public void TC_CORE_02_MPExhaustion_OmitsFromLegalActions()
        {
            var actor = CreateTestEntity("actor_1", "Mage", TeamSide.PlayerTeam, 50, 5, 10, 5, 10);
            var enemy = CreateTestEntity("enemy_1", "Goblin", TeamSide.EnemyTeam, 30, 0, 5, 2, 5);

            var expensiveSkill = ScriptableObject.CreateInstance<SkillData>();
            expensiveSkill.Initialize("Skill_Inferno", "Inferno", "High MP spell", 15, 200, TargetScope.SingleEnemy, SkillCategory.MagicDamage);
            actor.AddSkill(expensiveSkill);

            var extractor = new CombatStateExtractor();
            var combatants = new List<CombatEntity> { actor, enemy };
            var snapshot = extractor.ExtractSnapshot(combatants, actor, 1, new List<string>());

            Assert.IsFalse(snapshot.IsActionAvailable("Skill_Inferno"), "Skill requiring 15 MP should NOT be available when actor has 5 MP.");
            Assert.IsTrue(snapshot.IsActionAvailable("BasicAttack"), "Basic attack should always be available.");
            Assert.IsTrue(snapshot.IsActionAvailable("Action_Defend"), "Defend should always be available.");
        }

        [Test]
        public void TC_CORE_03_TurnOrder_SpeedDescendingWithIdTieBreaking()
        {
            var actorA = CreateTestEntity("actor_B", "Fast2", TeamSide.PlayerTeam, 100, 10, 10, 5, 20);
            var actorB = CreateTestEntity("actor_A", "Fast1", TeamSide.PlayerTeam, 100, 10, 10, 5, 20);
            var actorC = CreateTestEntity("actor_C", "Slow", TeamSide.EnemyTeam, 100, 10, 10, 5, 10);
            var actorD = CreateTestEntity("actor_D", "Fastest", TeamSide.EnemyTeam, 100, 10, 10, 5, 30);

            var turnCoordinator = new TurnCoordinator();
            var combatants = new List<CombatEntity> { actorA, actorB, actorC, actorD };

            turnCoordinator.StartNewRound(combatants);

            var order = turnCoordinator.InitiativeQueue;
            Assert.AreEqual(4, order.Count);
            Assert.AreEqual("actor_D", order[0].EntityId, "Fastest (spd 30) must be first.");
            Assert.AreEqual("actor_A", order[1].EntityId, "actor_A (spd 20) should precede actor_B due to tie-breaking.");
            Assert.AreEqual("actor_B", order[2].EntityId, "actor_B (spd 20) should follow actor_A.");
            Assert.AreEqual("actor_C", order[3].EntityId, "Slow (spd 10) must be last.");
        }

        [Test]
        public void TC_CORE_04_ProximityCover_RedirectsDamageToProtector()
        {
            var paladin = CreateTestEntity("paladin_1", "Marcus", TeamSide.PlayerTeam, 150, 40, 20, 10, 15, fieldDist: 15f);
            var mage = CreateTestEntity("mage_1", "Elena", TeamSide.PlayerTeam, 60, 50, 15, 4, 20, fieldDist: 18f); // Jarak 3m (CanReach = true)
            var boss = CreateTestEntity("boss_1", "Strider", TeamSide.EnemyTeam, 100, 20, 40, 10, 10, fieldDist: 35f);

            var bmGO = new GameObject("BattleManager");
            bmGO.transform.SetParent(_holder.transform);
            var battleManager = bmGO.AddComponent<BattleManager>();
            battleManager.InitializeBattle(new[] { paladin, mage, boss }, maxTurnLimit: 10);

            // Marcus pasang badan melindungi Elena
            var coverDecision = new AgentDecision(paladin.EntityId, "CoverAlly", mage.EntityId, "Protecting Elena");
            var coverResult = battleManager.ProcessActionResolution(paladin, coverDecision);
            Assert.IsTrue(coverResult.Success);

            // Boss menyerang Elena -> dialihkan ke Marcus dengan reduksi 25% (40 * 0.75 = 30 raw to Marcus; Marcus Def 10 -> 25 dmg)
            var attackDecision = new AgentDecision(boss.EntityId, "BasicAttack", mage.EntityId, "Targeting Elena");
            var attackResult = battleManager.ProcessActionResolution(boss, attackDecision);

            Assert.IsTrue(attackResult.Success);
            Assert.AreEqual(60, mage.Stats.CurrentHP, "Elena takes 0 damage because Marcus intercepted!");
            Assert.AreEqual(125, paladin.Stats.CurrentHP, "Marcus absorbed 25 intercepted damage.");
        }

        [Test]
        public void TC_CORE_05_ProximitySacrifice_FatalSacrificeResultsInKIAAndSavesAlly()
        {
            // Marcus hanya punya 10 HP tersisa (nyaris mati)
            var marcus = CreateTestEntity("marcus_1", "Marcus", TeamSide.PlayerTeam, 10, 30, 20, 10, 15, fieldDist: 15f);
            var elena = CreateTestEntity("elena_1", "Elena", TeamSide.PlayerTeam, 10, 10, 15, 5, 10, fieldDist: 18f);
            var strider = CreateTestEntity("boss_1", "Strider", TeamSide.EnemyTeam, 100, 20, 40, 10, 10, fieldDist: 30f);

            marcus.SetRelationship("elena_1", 95);

            var bmGO = new GameObject("BattleManager");
            bmGO.transform.SetParent(_holder.transform);
            var battleManager = bmGO.AddComponent<BattleManager>();
            battleManager.InitializeBattle(new[] { marcus, elena, strider }, maxTurnLimit: 10);

            // Marcus pasang badan melindungi Elena
            marcus.SetCoveringTarget(elena.EntityId);
            elena.SetCoveredBy(marcus.EntityId);

            // Boss menembakkan serangan mematikan ke Elena (Raw Atk 40 -> 30 raw to Marcus -> 25 damage)
            // Karena HP Marcus hanya 10, damage 25 membunuhnya seketika!
            var attackDecision = new AgentDecision(strider.EntityId, "BasicAttack", elena.EntityId, "Lethal strike on Elena");
            battleManager.ProcessActionResolution(strider, attackDecision);

            Assert.IsFalse(marcus.IsAlive, "Marcus gave his life and is Killed In Action (KIA)!");
            Assert.AreEqual(10, elena.Stats.CurrentHP, "Elena survives unharmed because Marcus took the fatal blow!");
        }

        [Test]
        public void TC_CORE_06_SuppressionFire_AppliesSuppressedStatus()
        {
            var jax = CreateTestEntity("jax_1", "Jax", TeamSide.PlayerTeam, 130, 30, 30, 15, 14);
            var drone = CreateTestEntity("drone_1", "Drone", TeamSide.EnemyTeam, 60, 10, 20, 5, 25);

            var bmGO = new GameObject("BattleManager");
            bmGO.transform.SetParent(_holder.transform);
            var battleManager = bmGO.AddComponent<BattleManager>();
            battleManager.InitializeBattle(new[] { jax, drone }, maxTurnLimit: 10);

            var suppressDecision = new AgentDecision(jax.EntityId, "SuppressionFire", drone.EntityId, "Pinning down drone");
            var result = battleManager.ProcessActionResolution(jax, suppressDecision);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(drone.HasStatusEffect(StatusEffectType.Suppressed), "Drone must be Suppressed!");
            Assert.AreEqual(20, jax.Stats.CurrentMP, "Jax should have consumed 10 MP (30 -> 20).");
        }

        [Test]
        public void TC_CORE_07_PersonalityEvolution_SwornBondDeathTriggersVengefulFanatic()
        {
            var marcus = CreateTestEntity("marcus_1", "Marcus", TeamSide.PlayerTeam, 140, 30, 24, 18, 12, trait: "LoyalProtector");
            var elena = CreateTestEntity("elena_1", "Elena", TeamSide.PlayerTeam, 20, 40, 35, 8, 20);
            var boss = CreateTestEntity("boss_1", "HOUND-IV", TeamSide.EnemyTeam, 300, 20, 50, 20, 15);

            marcus.SetRelationship("elena_1", 95); // Sworn Bond

            var bmGO = new GameObject("BattleManager");
            bmGO.transform.SetParent(_holder.transform);
            var battleManager = bmGO.AddComponent<BattleManager>();
            battleManager.InitializeBattle(new[] { marcus, elena, boss }, maxTurnLimit: 10);

            // Boss membunuh Elena
            var killDecision = new AgentDecision(boss.EntityId, "BasicAttack", elena.EntityId, "Crushing blow");
            battleManager.ProcessActionResolution(boss, killDecision);

            Assert.IsFalse(elena.IsAlive, "Elena is killed.");
            // Watak Marcus bermutasi secara dinamis menjadi VengefulFanatic!
            Assert.AreEqual("VengefulFanatic", marcus.CurrentTraitId, "Marcus's trait must shift to VengefulFanatic upon sworn comrade's death!");
        }

        [Test]
        public void TC_CORE_08_CommanderDirective_BroadcastsToSnapshot()
        {
            var marcus = CreateTestEntity("marcus_1", "Marcus", TeamSide.PlayerTeam, 100, 20, 20, 10, 15);
            var enemy = CreateTestEntity("boss_1", "Boss", TeamSide.EnemyTeam, 100, 10, 20, 10, 10);

            var bmGO = new GameObject("BattleManager");
            bmGO.transform.SetParent(_holder.transform);
            var battleManager = bmGO.AddComponent<BattleManager>();
            battleManager.InitializeBattle(new[] { marcus, enemy }, maxTurnLimit: 10);

            // Komandan memancarkan perintah mundur darurat
            battleManager.SetCommanderDirective(DirectiveType.FullTacticalRetreat, "", "All units fall back to transport!");

            var extractor = new CombatStateExtractor();
            var snapshot = extractor.ExtractSnapshot(new[] { marcus, enemy }, marcus, 1, new List<string>(), battleManager.ActiveDirective);

            Assert.IsNotNull(snapshot.ActiveDirective);
            Assert.AreEqual(DirectiveType.FullTacticalRetreat, snapshot.ActiveDirective.Type);
            Assert.AreEqual("All units fall back to transport!", snapshot.ActiveDirective.DirectiveText);
        }
    }
}
