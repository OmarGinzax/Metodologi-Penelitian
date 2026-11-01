using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RPGStrategy.AI.Compression;
using RPGStrategy.AI.Hybrid;
using RPGStrategy.AI.LLM;
using RPGStrategy.AI.Traits;
using RPGStrategy.Core.Combat;
using RPGStrategy.Core.Command;
using RPGStrategy.Core.Data;
using RPGStrategy.Core.Extractor;
using RPGStrategy.Experiment;

namespace RPGStrategy.Tests
{
    /// <summary>
    /// M8 – Pilot Experiment Tests.
    /// Integration tests that validate the full pipeline end-to-end in offline/mock mode.
    /// These tests run in the Unity Test Runner (Editor mode) without a real LLM API call.
    /// Covers: Pure LLM offline fallback, Hybrid guardrail validation, Context Compression,
    ///         Telemetry Shannon Entropy, Trait Consistency scoring, and Decision Coherence.
    /// </summary>
    [TestFixture]
    public class PilotExperimentTests
    {
        private GameObject _holder;
        private CombatStateExtractor _extractor;

        [SetUp]
        public void SetUp()
        {
            _holder = new GameObject("PilotTestHolder");
            _extractor = new CombatStateExtractor();
        }

        [TearDown]
        public void TearDown()
        {
            if (_holder != null) Object.DestroyImmediate(_holder);
        }

        // ─── Helper ───────────────────────────────────────────────────────────────

        private CombatEntity CreateEntity(string id, string name, TeamSide team, int hp, int mp, int atk, int def, int spd, int rat = 75, string trait = "SteadfastVeteran", float dist = 20f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_holder.transform);
            var entity = go.AddComponent<CombatEntity>();
            entity.Initialize(id, name, team, new EntityStats(hp, mp, atk, def, spd, rat), trait, dist);
            return entity;
        }

        private CombatStateSnapshot MakeSnapshot(CombatEntity actor, List<CombatEntity> all, DirectiveType directiveType = DirectiveType.FocusFireTarget)
        {
            var directive = new CommanderDirectiveDTO(directiveType, null, "Test directive", 1);
            return _extractor.ExtractSnapshot(all, actor, 1, new List<string> { "Pilot test event" }, directive);
        }

        // ─── TC_PILOT_01: Action Validator rejects dead target (H2) ──────────────

        [Test]
        public void TC_PILOT_01_Validator_H2_RejectsDeadTarget()
        {
            var actor = CreateEntity("p1", "Marcus", TeamSide.PlayerTeam, 100, 80, 18, 10, 9, 75, "SteadfastVeteran");
            var deadEnemy = CreateEntity("e1", "Drone", TeamSide.EnemyTeam, 0, 0, 12, 4, 13, 100, "HostileUnit"); // HP=0 → already dead (IsAlive == false)

            var all = new List<CombatEntity> { actor, deadEnemy };
            var snapshot = MakeSnapshot(actor, all);

            var decision = new AgentDecision("p1", "BasicAttack", "e1", "Attack the drone", "OBEY", "Fire!");
            var result = ActionValidator.Validate(snapshot, decision);

            // Target is dead → H2
            Assert.IsFalse(result.IsValid, "Validator should reject attack on dead enemy.");
            Assert.AreEqual(HallucinationType.H2_TargetInvalidation, result.HallucinationCode);
        }

        // ─── TC_PILOT_02: Action Validator rejects non-existent action (H3) ───────

        [Test]
        public void TC_PILOT_02_Validator_H3_RejectsNonExistentAction()
        {
            var actor = CreateEntity("p1", "Lyra", TeamSide.PlayerTeam, 100, 100, 14, 8, 10, 68, "CautiousTactician");
            var enemy = CreateEntity("e1", "HOUND", TeamSide.EnemyTeam, 300, 50, 28, 18, 6, 100, "HostileUnit", 10f);
            var all = new List<CombatEntity> { actor, enemy };
            var snapshot = MakeSnapshot(actor, all);

            var decision = new AgentDecision("p1", "NUKE_EVERYTHING", "e1", "This action doesn't exist", "DEFY", "BOOM!");
            var result = ActionValidator.Validate(snapshot, decision);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(HallucinationType.H3_ActionNonExistence, result.HallucinationCode, "Hallucinated action must be classified H3.");
        }

        // ─── TC_PILOT_03: Action Validator rejects insufficient MP (H4) ──────────

        [Test]
        public void TC_PILOT_03_Validator_H4_RejectsInsufficientMP()
        {
            var actor = CreateEntity("p1", "Torres", TeamSide.PlayerTeam, 110, 0, 15, 12, 8, 60, "LoyalProtector"); // 0 MP
            var ally = CreateEntity("p2", "Rook", TeamSide.PlayerTeam, 50, 70, 13, 8, 9, 30, "PanickingRecruit");
            var enemy = CreateEntity("e1", "Drone", TeamSide.EnemyTeam, 80, 30, 14, 5, 14, 100, "HostileUnit");
            var all = new List<CombatEntity> { actor, ally, enemy };
            var snapshot = MakeSnapshot(actor, all);

            // Add an action requiring MP to snapshot to test H4 detection
            snapshot.AvailableActions.Add(new AvailableActionDTO
            {
                ActionId = "HeavyStrike",
                ActionName = "Heavy Strike",
                ActionType = ActionType.CastSkill.ToString(),
                MPCost = 15,
                Scope = "SingleEnemy"
            });

            var decision = new AgentDecision("p1", "HeavyStrike", "e1", "Heavy attack", "OBEY", "Striking!");
            var result = ActionValidator.Validate(snapshot, decision);

            // HeavyStrike costs 15 MP; actor has 0 MP
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(HallucinationType.H4_ResourceExhaustion, result.HallucinationCode, "MP exhaustion should be classified H4.");
        }

        // ─── TC_PILOT_04: Proximity Violation for CarryWounded (H5) ──────────────

        [Test]
        public void TC_PILOT_04_Validator_H5_ProximityViolation_CarryWounded()
        {
            // Actor at 5m, wounded ally at 100m → out of 15m interaction range
            var actor = CreateEntity("p1", "Cross", TeamSide.PlayerTeam, 95, 110, 20, 7, 11, 55, "HeroicIdealizer", dist: 5f);
            var farAlly = CreateEntity("p2", "Nyx", TeamSide.PlayerTeam, 20, 120, 10, 9, 7, 70, "LoyalProtector", dist: 100f);
            var enemy = CreateEntity("e1", "Drone", TeamSide.EnemyTeam, 80, 30, 14, 5, 14, 100, "HostileUnit", dist: 20f);
            var all = new List<CombatEntity> { actor, farAlly, enemy };
            var snapshot = MakeSnapshot(actor, all);

            var decision = new AgentDecision("p1", "Action_CarryWounded", "p2", "Carry Nyx to safety!", "DEFY", "Grabbing her!");
            var result = ActionValidator.Validate(snapshot, decision);

            Assert.IsFalse(result.IsValid, "CarryWounded beyond movement range should be rejected.");
            Assert.AreEqual(HallucinationType.H5_ProximityViolation, result.HallucinationCode);
        }

        // ─── TC_PILOT_05: Hybrid fallback to Action_Defend when decision is null ──

        [Test]
        public void TC_PILOT_05_Hybrid_FallbackDefend_OnNullDecision()
        {
            // Simulate H6: null decision (as if LLM completely failed parsing)
            var result = ActionValidator.Validate(null, null);
            Assert.IsFalse(result.IsValid, "Null decision must be rejected.");
            Assert.AreEqual(HallucinationType.H6_FormatParsingFailure, result.HallucinationCode, "Null decision must be H6.");
        }

        // ─── TC_PILOT_06: FrozenTraitMapping – VengefulFanatic high affinity for Execute ──

        [Test]
        public void TC_PILOT_06_FrozenTraitMapping_VengefulFanatic_Execute_IsHighAffinity()
        {
            float score = FrozenTraitMapping.ScoreActionConsistency("VengefulFanatic", "Execute", "DEFY");
            Assert.Greater(score, 0.7f, "VengefulFanatic choosing Execute (DEFY) should score > 0.7 trait consistency.");
        }

        // ─── TC_PILOT_07: FrozenTraitMapping – CautiousTactician low affinity for SuicidalRush ──

        [Test]
        public void TC_PILOT_07_FrozenTraitMapping_CautiousTactician_SuicidalRush_IsLowAffinity()
        {
            float score = FrozenTraitMapping.ScoreActionConsistency("CautiousTactician", "SuicidalRush", "DEFY");
            Assert.Less(score, 0.4f, "CautiousTactician choosing SuicidalRush should score < 0.4 trait consistency.");
        }

        // ─── TC_PILOT_08: PromptCompressor reduces token count ───────────────────

        [Test]
        public void TC_PILOT_08_PromptCompressor_AggressiveMode_ReducesTokenCount()
        {
            var actor = CreateEntity("p1", "Marcus", TeamSide.PlayerTeam, 120, 80, 18, 10, 9, 75, "SteadfastVeteran");
            var enemy = CreateEntity("e1", "HOUND", TeamSide.EnemyTeam, 300, 50, 28, 18, 6, 100, "HostileUnit", 10f);
            var all = new List<CombatEntity> { actor, enemy };
            var snapshot = MakeSnapshot(actor, all);

            string fullPrompt = RPGStrategy.AI.LLM.PromptBuilder.BuildUserPrompt(snapshot);
            string compressed = PromptCompressor.Compress(snapshot, PromptCompressor.CompressionLevel.Aggressive);

            int fullTokens = PromptCompressor.EstimateTokenCount(fullPrompt);
            int compTokens = PromptCompressor.EstimateTokenCount(compressed);

            Assert.Less(compTokens, fullTokens, "Aggressive compression should produce fewer tokens than full prompt.");

            float ratio = PromptCompressor.CompressionRatio(fullPrompt, compressed);
            Assert.Greater(ratio, 0.2f, "Compression ratio should be at least 20% for Aggressive level.");
        }

        // ─── TC_PILOT_09: Shannon Entropy returns 0 for single-action distribution ──

        [Test]
        public void TC_PILOT_09_TelemetryLogger_ShannonEntropy_SingleAction_IsZero()
        {
            var freqMap = new Dictionary<string, int> { { "Action_Defend", 10 } };
            float entropy = TelemetryLogger.CalculateShannonEntropy(freqMap, 10);
            Assert.AreEqual(0f, entropy, 0.001f, "Shannon Entropy for single-action distribution should be 0.");
        }

        // ─── TC_PILOT_10: Shannon Entropy is maximized for uniform distribution ───

        [Test]
        public void TC_PILOT_10_TelemetryLogger_ShannonEntropy_UniformDistribution_IsMaximal()
        {
            // 4 equally likely actions → H = log2(4) = 2.0
            var freqMap = new Dictionary<string, int>
            {
                { "BasicAttack", 10 },
                { "Overwatch", 10 },
                { "Action_Defend", 10 },
                { "CarryWounded", 10 }
            };
            float entropy = TelemetryLogger.CalculateShannonEntropy(freqMap, 40);
            Assert.AreEqual(2.0f, entropy, 0.01f, "Uniform 4-action distribution should have Shannon Entropy ≈ 2.0 bits.");
        }

        // ─── TC_PILOT_11: DecisionCoherenceEvaluator records and reports ─────────

        [Test]
        public void TC_PILOT_11_CoherenceEvaluator_RecordsAndReports_Correctly()
        {
            var actor = CreateEntity("p1", "Rook", TeamSide.PlayerTeam, 90, 70, 13, 8, 9, 30, "PanickingRecruit");
            var enemy = CreateEntity("e1", "Drone", TeamSide.EnemyTeam, 80, 30, 14, 5, 14, 100, "HostileUnit");
            var all = new List<CombatEntity> { actor, enemy };
            var snapshot = MakeSnapshot(actor, all);

            var evaluator = new DecisionCoherenceEvaluator();

            // Record 3 decisions
            var d1 = new AgentDecision("p1", "Scatter", "p1", "Run!", "DEFY", "I'm getting out!");
            var d2 = new AgentDecision("p1", "Action_Defend", "p1", "Hiding!", "ADAPT", "Taking cover.");
            var d3 = new AgentDecision("p1", "Scatter", "p1", "Still running!", "DEFY", "Can't stop now!");

            evaluator.Record("p1", 1, d1, snapshot);
            evaluator.Record("p1", 2, d2, snapshot);
            evaluator.Record("p1", 3, d3, snapshot);

            var report = evaluator.GenerateReport("p1");

            Assert.AreEqual("p1", report.EntityId);
            Assert.AreEqual(3, report.TotalDecisions);
            Assert.GreaterOrEqual(report.MeanTraitConsistency, 0f, "Mean trait consistency must be non-negative.");
            Assert.LessOrEqual(report.MeanTraitConsistency, 1f, "Mean trait consistency must be at most 1.");
        }

        // ─── TC_PILOT_12: ScenarioPresets build valid entity lists ───────────────

        [Test]
        public void TC_PILOT_12_ScenarioPresets_AllScenarios_HaveNonEmptySquads()
        {
            Assert.IsTrue(ScenarioPresets.GetSquadA().Count == 6, "Scenario A should have 6 squad members.");
            Assert.IsTrue(ScenarioPresets.GetSquadB().Count == 4, "Scenario B should have 4 squad members (reduced).");
            Assert.IsTrue(ScenarioPresets.GetSquadC().Count == 6, "Scenario C should have 6 squad members.");
            Assert.IsTrue(ScenarioPresets.GetSquadD().Count == 6, "Scenario D should have 6 squad members.");

            Assert.IsTrue(ScenarioPresets.GetEnemiesA().Count >= 1, "Scenario A must have at least 1 enemy.");
            Assert.IsTrue(ScenarioPresets.GetEnemiesB().Count >= 1, "Scenario B must have at least 1 enemy.");
        }

        // ─── TC_PILOT_13: Validator auto-assigns self-targeting for Self scope ────

        [Test]
        public void TC_PILOT_13_Validator_SelfScope_AutoAssignsActorAsTarget()
        {
            var actor = CreateEntity("p1", "Nyx", TeamSide.PlayerTeam, 85, 120, 10, 9, 7, 70, "LoyalProtector");
            var enemy = CreateEntity("e1", "Drone", TeamSide.EnemyTeam, 80, 30, 14, 5, 14, 100, "HostileUnit");
            var all = new List<CombatEntity> { actor, enemy };
            var snapshot = MakeSnapshot(actor, all);

            // Decision with empty target_id but Self-scope action
            var decision = new AgentDecision("p1", "Action_Defend", "", "Taking cover!", "OBEY", "Brace!");
            var result = ActionValidator.Validate(snapshot, decision);

            // Action_Defend is Self-scope — should auto-assign and validate successfully
            Assert.IsTrue(result.IsValid, "Self-scope action with empty target should auto-succeed by assigning actor as target.");
            Assert.AreEqual("p1", decision.TargetId, "target_id should be auto-set to actor.");
        }
    }
}
