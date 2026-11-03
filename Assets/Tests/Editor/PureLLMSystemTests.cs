using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using RPGStrategy.AI;
using RPGStrategy.AI.Baseline;
using RPGStrategy.AI.LLM;
using RPGStrategy.AI.PureLLM;
using RPGStrategy.AI.Telemetry;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.Tests
{
    /// <summary>
    /// Pengujian offline untuk Pure LLM System (Fase 1-7).
    /// Tidak menyentuh CoreTactical2DTests.cs sesuai batasan penelitian.
    /// </summary>
    [TestFixture]
    public class PureLLMSystemTests
    {
        private GameObject _holder;
        private TacticalGrid2D _grid;
        private readonly List<GameObject> _extraObjects = new List<GameObject>();

        [SetUp]
        public void Setup()
        {
            _holder = new GameObject("PureLLM_TestHolder");
            _grid = _holder.AddComponent<TacticalGrid2D>();
            _grid.InitializeGrid(18, 10, 1.0f);
            _extraObjects.Clear();
        }

        [TearDown]
        public void Teardown()
        {
            for (int i = _extraObjects.Count - 1; i >= 0; i--)
            {
                if (_extraObjects[i] != null) UnityEngine.Object.DestroyImmediate(_extraObjects[i]);
            }
            _extraObjects.Clear();

            if (_holder != null) UnityEngine.Object.DestroyImmediate(_holder);
        }

        // ===================================================================
        // Helpers
        // ===================================================================

        private CombatEntity2D CreateEntity(
            string id, string name, TeamSide team, int hp, int atk, Vector2Int pos,
            string trait = "Neutral", int atkRange = 5, int moveRange = 3, bool immune = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_holder.transform);
            var entity = go.AddComponent<CombatEntity2D>();
            entity.Initialize(
                id, name, team, new EntityStats(hp, 50, atk, 5, 10, 80),
                trait, pos, "Test Rifle", atkRange, moveRange, immune);
            _grid.SetOccupant(pos, entity);
            return entity;
        }

        private static List<string> Logs(params string[] lines)
        {
            return new List<string>(lines);
        }

        private sealed class MockLLMService : ILLMService2D
        {
            public string RawContent;
            public int PromptTokens = 123;
            public int CompletionTokens = 45;
            public long LatencyMs = 250;

            public Task<LLMResponseDTO> SendChatCompletionAsync(string systemPrompt, string userPrompt)
            {
                var response = new LLMResponseDTO
                {
                    RawContent = RawContent,
                    PromptTokens = PromptTokens,
                    CompletionTokens = CompletionTokens,
                    TotalTokens = PromptTokens + CompletionTokens,
                    LatencyMs = LatencyMs,
                    IsSuccess = !string.IsNullOrEmpty(RawContent),
                    RequestSucceeded = true,
                    ModelName = "mock-model"
                };
                return Task.FromResult(response);
            }
        }

        // ===================================================================
        // Fase 2: State Extractor & Snapshot Serialization
        // ===================================================================

        [Test]
        public void TC_PLLM_01_StateExtractor_BuildsCompleteSnapshot()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2), "Neutral", 4, 3);
            var claire = CreateEntity("u4_claire", "Claire", TeamSide.PlayerTeam, 70, 10, new Vector2Int(2, 3), "Passive", 4, 3);
            var enemy = CreateEntity("enemy_1", "Scout Drone", TeamSide.EnemyTeam, 50, 15, new Vector2Int(6, 2), "Aggressive", 5, 4, true);
            _grid.SetCellType(new Vector2Int(4, 2), CellType.HalfCover);

            var extractor = new TacticalStateExtractor2D(5);
            var snapshot = extractor.ExtractSnapshot(
                marcus, _grid, new List<CombatEntity2D> { marcus, claire, enemy },
                2, 7, Logs("l1", "l2", "l3", "l4", "l5", "l6", "l7"));

            Assert.AreEqual(2, snapshot.RoundNumber);
            Assert.AreEqual(7, snapshot.TurnNumber);
            Assert.AreEqual("u1_marcus", snapshot.CurrentActor.EntityId);
            Assert.AreEqual("Guard", snapshot.CurrentActor.SkillId);
            Assert.AreEqual(1, snapshot.Allies.Count);
            Assert.AreEqual(1, snapshot.Enemies.Count);
            Assert.Greater(snapshot.ReachableTiles.Count, 0, "Actor must have reachable tiles.");
            Assert.AreEqual(5, snapshot.RecentLogs.Count, "Recent logs must be capped by maxRecentLogs.");
            Assert.IsTrue(snapshot.Obstacles.Exists(o => o.X == 4 && o.Y == 2 && o.Type == "HalfCover"));
        }

        [Test]
        public void TC_PLLM_02_Snapshot_ToJson_IsValidAndContainsCoreSections()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2));
            var enemy = CreateEntity("enemy_1", "Drone", TeamSide.EnemyTeam, 50, 15, new Vector2Int(6, 2), "Aggressive", 5, 4, true);

            var extractor = new TacticalStateExtractor2D();
            var snapshot = extractor.ExtractSnapshot(
                marcus, _grid, new List<CombatEntity2D> { marcus, enemy }, 1, 1, Logs("start"));

            string json = snapshot.ToJson();
            Assert.IsFalse(string.IsNullOrEmpty(json));

            var parsed = JsonUtil.Parse(json) as Dictionary<string, object>;
            Assert.IsNotNull(parsed, "Snapshot JSON must be parseable.");
            Assert.IsTrue(parsed.ContainsKey("current_actor"));
            Assert.IsTrue(parsed.ContainsKey("allies"));
            Assert.IsTrue(parsed.ContainsKey("enemies"));
            Assert.IsTrue(parsed.ContainsKey("reachable_tiles"));
            Assert.IsTrue(parsed.ContainsKey("recent_logs"));
        }

        // ===================================================================
        // Fase 4: Prompt Builder & JSON Parser
        // ===================================================================

        [Test]
        public void TC_PLLM_03_PromptBuilder_ContainsRulesAndDynamicState()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2));
            var enemy = CreateEntity("enemy_1", "Drone", TeamSide.EnemyTeam, 50, 15, new Vector2Int(6, 2), "Aggressive", 5, 4, true);

            var extractor = new TacticalStateExtractor2D();
            var snapshot = extractor.ExtractSnapshot(
                marcus, _grid, new List<CombatEntity2D> { marcus, enemy }, 1, 4, Logs("Opening contact."));

            var builder = new TacticalPromptBuilder2D();
            string system = builder.BuildSystemPrompt(marcus);
            string user = builder.BuildUserPrompt(snapshot);

            Assert.IsTrue(system.Contains("MoveAndShoot"));
            Assert.IsTrue(system.Contains("CastSkill"));
            Assert.IsTrue(system.Contains("Guard"));
            Assert.IsTrue(system.Contains("OUTPUT REQUIREMENTS"));

            Assert.IsTrue(user.Contains("u1_marcus"));
            Assert.IsTrue(user.Contains("REACHABLE TILES"));
            Assert.IsTrue(user.Contains("DETECTED ENEMIES"));
            Assert.IsTrue(user.Contains("enemy_1"));
            Assert.IsTrue(user.Contains("ROUND 1, TURN 4"));
        }

        [Test]
        public void TC_PLLM_04_Parser_ParsesRawJsonDecision()
        {
            var result = TacticalDecisionJsonParser.Parse(
                "{\"action\":\"MoveAndShoot\",\"destination\":{\"x\":4,\"y\":2},\"target_id\":\"enemy_1\",\"reasoning\":\"flank the drone\"}");

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.ActionRecognized);
            Assert.AreEqual(TacticalActionType.MoveAndShoot, result.MappedAction);
            Assert.IsTrue(result.HasDestination);
            Assert.IsTrue(result.DestinationFieldsValid);
            Assert.AreEqual(4, result.DestinationX);
            Assert.AreEqual(2, result.DestinationY);
            Assert.AreEqual("enemy_1", result.TargetId);
            Assert.AreEqual("flank the drone", result.Reasoning);
        }

        [Test]
        public void TC_PLLM_05_Parser_ParsesMarkdownFencedJson()
        {
            var result = TacticalDecisionJsonParser.Parse(
                "```json\n{\"action\":\"Shoot\",\"target_id\":\"enemy_1\",\"reasoning\":\"clear shot\"}\n```");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(TacticalActionType.Shoot, result.MappedAction);
            Assert.AreEqual("enemy_1", result.TargetId);
        }

        [Test]
        public void TC_PLLM_06_Parser_FailsOnNonJsonOutput()
        {
            var result = TacticalDecisionJsonParser.Parse("I will attack the drone from the north.");
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Error.Contains("No JSON"));
        }

        // ===================================================================
        // Fase 5: Hallucination Classifier H1-H5
        // ===================================================================

        [Test]
        public void TC_PLLM_07_Classifier_FlagsH1_DeadTarget()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2));
            var enemy = CreateEntity("enemy_1", "Drone", TeamSide.EnemyTeam, 50, 15, new Vector2Int(4, 2), "Aggressive", 5, 4, true);
            enemy.Stats.CurrentHP = 0;

            var snapshot = new TacticalStateExtractor2D().ExtractSnapshot(
                marcus, _grid, new List<CombatEntity2D> { marcus, enemy }, 1, 1, Logs("Drone destroyed."));

            var parse = TacticalDecisionJsonParser.Parse(
                "{\"action\":\"Shoot\",\"target_id\":\"enemy_1\",\"reasoning\":\"finish it\"}");
            var decision = new TacticalDecision2D(
                TacticalActionType.Shoot, marcus.GridPosition, enemy, null, "", false, parse.Reasoning);

            var report = new HallucinationClassifier().EvaluateDecision(decision, snapshot, _grid, parse);

            Assert.IsTrue(report.IsIllogical);
            Assert.IsTrue(report.Has(HallucinationCategory2D.H1_InvalidTarget));
        }

        [Test]
        public void TC_PLLM_08_Classifier_FlagsH2_SkillOnCooldown()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2));
            var claire = CreateEntity("u4_claire", "Claire", TeamSide.PlayerTeam, 70, 10, new Vector2Int(2, 3), "Passive", 4, 3);
            marcus.PutSkillOnCooldown(2);

            var snapshot = new TacticalStateExtractor2D().ExtractSnapshot(
                marcus, _grid, new List<CombatEntity2D> { marcus, claire }, 1, 1, Logs("start"));

            var parse = TacticalDecisionJsonParser.Parse(
                "{\"action\":\"CastSkill\",\"target_id\":\"u4_claire\",\"skill_id\":\"Guard\",\"reasoning\":\"protect medic\"}");
            var decision = new TacticalDecision2D(
                TacticalActionType.CastSkill, marcus.GridPosition, null, claire, "Guard", false, parse.Reasoning);

            var report = new HallucinationClassifier().EvaluateDecision(decision, snapshot, _grid, parse);

            Assert.IsTrue(report.Has(HallucinationCategory2D.H2_SkillCooldownOrOwnership));
        }

        [Test]
        public void TC_PLLM_09_Classifier_FlagsH3_ShotOutOfRange()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2), "Neutral", 4, 3);
            var enemy = CreateEntity("enemy_1", "Far Drone", TeamSide.EnemyTeam, 50, 15, new Vector2Int(15, 9), "Aggressive", 5, 4, true);

            var snapshot = new TacticalStateExtractor2D().ExtractSnapshot(
                marcus, _grid, new List<CombatEntity2D> { marcus, enemy }, 1, 1, Logs("start"));

            var parse = TacticalDecisionJsonParser.Parse(
                "{\"action\":\"Shoot\",\"target_id\":\"enemy_1\",\"reasoning\":\"long shot\"}");
            var decision = new TacticalDecision2D(
                TacticalActionType.Shoot, marcus.GridPosition, enemy, null, "", false, parse.Reasoning);

            var report = new HallucinationClassifier().EvaluateDecision(decision, snapshot, _grid, parse);

            Assert.IsTrue(report.Has(HallucinationCategory2D.H3_SpatialGeometryViolation));
        }

        [Test]
        public void TC_PLLM_10_Classifier_FlagsH4_UnknownAction()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2));
            var snapshot = new TacticalStateExtractor2D().ExtractSnapshot(
                marcus, _grid, new List<CombatEntity2D> { marcus }, 1, 1, Logs("start"));

            var parse = TacticalDecisionJsonParser.Parse(
                "{\"action\":\"Teleport\",\"destination\":{\"x\":9,\"y\":9},\"reasoning\":\"blink\"}");
            var decision = new TacticalDecision2D(
                TacticalActionType.Idle, marcus.GridPosition, null, false, "unknown action");

            var report = new HallucinationClassifier().EvaluateDecision(decision, snapshot, _grid, parse);

            Assert.IsTrue(report.Has(HallucinationCategory2D.H4_ActionProtocolInconsistency));
        }

        [Test]
        public void TC_PLLM_11_Classifier_FlagsH5_ParseFailure()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2));
            var snapshot = new TacticalStateExtractor2D().ExtractSnapshot(
                marcus, _grid, new List<CombatEntity2D> { marcus }, 1, 1, Logs("start"));

            var parse = TacticalDecisionJsonParser.Parse("not a json response");
            var decision = new TacticalDecision2D(
                TacticalActionType.Idle, marcus.GridPosition, null, false, "parse failed");

            var report = new HallucinationClassifier().EvaluateDecision(decision, snapshot, _grid, parse);

            Assert.IsTrue(report.Has(HallucinationCategory2D.H5_OutputSyntaxFailure));
        }

        // ===================================================================
        // Fase 5: End-to-end Pure LLM Agent dengan mock service
        // ===================================================================

        [Test]
        public void TC_PLLM_12_PureAgentPipeline_ExecutesMockDecisionAndLogsTelemetry()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2), "Neutral", 4, 3);
            var enemy = CreateEntity("enemy_1", "Drone", TeamSide.EnemyTeam, 50, 15, new Vector2Int(4, 2), "Aggressive", 5, 4, true);

            var loggerGo = new GameObject("TelemetryLogger");
            _extraObjects.Add(loggerGo);
            var logger = loggerGo.AddComponent<CombatTelemetryLogger2D>();

            var agentGo = new GameObject("PureLLMAgent");
            _extraObjects.Add(agentGo);
            var agent = agentGo.AddComponent<PureLLMTacticalAgent>();

            var mock = new MockLLMService
            {
                RawContent = "{\"action\":\"Shoot\",\"target_id\":\"enemy_1\",\"reasoning\":\"Clean line of sight.\"}"
            };

            var config = LLMConfig.CreateRuntimeDefault();
            agent.Configure(config, mock, logger);
            agent.TurnContext = new TacticalTurnContext(1, 3);

            var task = agent.DecideTurnAsync(
                marcus, _grid, new List<CombatEntity2D> { marcus, enemy }, Logs("Contact."));
            var decision = task.GetAwaiter().GetResult();

            Assert.IsNotNull(decision);
            Assert.AreEqual(TacticalActionType.Shoot, decision.Action);
            Assert.AreEqual(enemy, decision.TargetEnemy);
            Assert.IsNotNull(agent.LastResponse);
            Assert.AreEqual(123, agent.LastResponse.PromptTokens);
            Assert.AreEqual(45, agent.LastResponse.CompletionTokens);
            Assert.IsNotNull(agent.LastSnapshot);
            Assert.IsNotNull(agent.LastHallucination);
            Assert.IsFalse(agent.LastHallucination.IsIllogical,
                "A legal shot with clear LoS must not be classified as illogical.");

            Assert.AreEqual(1, logger.Records.Count);
            Assert.AreEqual("Shoot", logger.Records[0].FinalAction);
            Assert.AreEqual("enemy_1", logger.Records[0].FinalTarget);
            Assert.IsFalse(logger.Records[0].HasValidator);
            Assert.AreEqual(250, logger.Records[0].LatencyMs);

            UnityEngine.Object.DestroyImmediate(config);
        }

        // ===================================================================
        // Fase 1: HFSM wrapper controller
        // ===================================================================

        [Test]
        public void TC_PLLM_13_HFSMTacticalAgent_WrapsBaselineController()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2), "Neutral", 4, 3);
            var enemy = CreateEntity("enemy_1", "Drone", TeamSide.EnemyTeam, 50, 15, new Vector2Int(4, 2), "Aggressive", 5, 4, true);

            // Neutral HFSM only fires while holding cover. Give Marcus half-cover on the
            // enemy-facing edge, then refresh his cover state relative to that enemy.
            _grid.SetCellType(new Vector2Int(3, 2), CellType.HalfCover);
            marcus.UpdateCoverStatus(_grid, enemy);

            var go = new GameObject("HFSMController");
            _extraObjects.Add(go);
            var controller = go.AddComponent<HFSMTacticalAgent>();

            var task = controller.DecideTurnAsync(
                marcus, _grid, new List<CombatEntity2D> { marcus, enemy }, Logs("start"));
            var decision = task.GetAwaiter().GetResult();

            Assert.IsNotNull(decision);
            Assert.AreEqual(TacticalActionType.Shoot, decision.Action);
            Assert.AreEqual(enemy, decision.TargetEnemy);
            Assert.AreEqual("HFSM_Baseline", controller.ControllerType);
        }

        // ===================================================================
        // Fase 7: Telemetry export
        // ===================================================================

        [Test]
        public void TC_PLLM_14_TelemetryLogger_ExportsCsvAndJson()
        {
            var go = new GameObject("TelemetryExportTest");
            _extraObjects.Add(go);
            var logger = go.AddComponent<CombatTelemetryLogger2D>();

            logger.LogTurnRecord(new TurnTelemetryRecord
            {
                RunId = "RUN_001",
                ConfigId = "CFG-02",
                ScenarioId = "SCEN_A",
                AgentType = "Pure_LLM_Unconstrained",
                HasValidator = false,
                IsCompressed = false,
                ModelName = "mock-model",
                RoundNumber = 1,
                TurnNumber = 1,
                ActorId = "u1_marcus",
                ActorName = "Marcus",
                TraitProfile = "Neutral",
                ProposedAction = "Shoot",
                ProposedTarget = "enemy_1",
                IsValid = true,
                ViolationCode = "NONE",
                FinalAction = "Shoot",
                FinalTarget = "enemy_1",
                IsFallback = false,
                ParseSucceeded = true,
                RequestSucceeded = true,
                Reasoning = "Clear shot.",
                RawResponse = "{\"action\":\"Shoot\"}",
                LatencyMs = 300,
                InputTokens = 500,
                OutputTokens = 30,
                TotalTokens = 530
            });

            logger.LogTurnRecord(new TurnTelemetryRecord
            {
                RunId = "RUN_001",
                ConfigId = "CFG-02",
                ScenarioId = "SCEN_A",
                AgentType = "Pure_LLM_Unconstrained",
                HasValidator = false,
                RoundNumber = 1,
                TurnNumber = 2,
                ActorId = "u4_claire",
                ActorName = "Claire",
                TraitProfile = "Passive",
                ProposedAction = "Shoot",
                ProposedTarget = "enemy_9",
                IsValid = false,
                ViolationCode = "H1",
                ViolationDetails = "H1: Target 'enemy_9' is not registered on the battlefield.",
                FinalAction = "Shoot",
                FinalTarget = "enemy_9",
                ParseSucceeded = true,
                RequestSucceeded = true,
                Reasoning = "Fire at phantom target.",
                LatencyMs = 450,
                InputTokens = 600,
                OutputTokens = 25,
                TotalTokens = 625
            });

            string tempDir = Path.Combine(Path.GetTempPath(), "purellm_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string csvPath = Path.Combine(tempDir, "telemetry.csv");
            string jsonPath = Path.Combine(tempDir, "telemetry.json");

            logger.ExportToCsv(csvPath);
            logger.ExportToJson(jsonPath);

            Assert.IsTrue(File.Exists(csvPath));
            Assert.IsTrue(File.Exists(jsonPath));

            string[] lines = File.ReadAllLines(csvPath);
            Assert.AreEqual(3, lines.Length, "Header + 2 data rows expected.");
            Assert.IsTrue(lines[0].Contains("violation_code"));
            Assert.IsTrue(lines[1].Contains("Shoot"));
            Assert.IsTrue(lines[2].Contains("H1"));

            string json = File.ReadAllText(jsonPath);
            Assert.IsTrue(json.Contains("\"illogical_decision_rate_percent_Y2\""));
            Assert.IsTrue(json.Contains("\"H1\""));

            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
