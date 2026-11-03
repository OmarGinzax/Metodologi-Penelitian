using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using RPGStrategy.AI;
using RPGStrategy.AI.Baseline;
using RPGStrategy.AI.Hybrid;
using RPGStrategy.AI.LLM;
using RPGStrategy.AI.PureLLM;
using RPGStrategy.AI.Telemetry;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.Tests
{
    /// <summary>
    /// Pengujian offline arsitektur Hybrid (Fase 1-7).
    /// Mencakup validator H1-H5, feedback prompt, retry korektif, fallback HFSM,
    /// kegagalan transport, dan skema telemetri Hybrid.
    /// Tidak menyentuh CoreTactical2DTests.cs sesuai batasan penelitian.
    /// </summary>
    [TestFixture]
    public class HybridSystemTests
    {
        private GameObject _holder;
        private TacticalGrid2D _grid;
        private readonly List<GameObject> _extraObjects = new List<GameObject>();

        [SetUp]
        public void Setup()
        {
            _holder = new GameObject("Hybrid_TestHolder");
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

        private ValidationResult2D ValidateRaw(
            CombatEntity2D actor,
            string rawJson,
            List<CombatEntity2D> combatants,
            out TacticalStateSnapshot2D snapshot)
        {
            snapshot = new TacticalStateExtractor2D().ExtractSnapshot(
                actor, _grid, combatants, 1, 1, Logs("test"));

            var parse = TacticalDecisionJsonParser.Parse(rawJson);
            var decision = TacticalDecisionBuilder2D.Build(parse, actor, _grid, combatants);
            return new TacticalActionValidator2D().Validate(decision, snapshot, _grid, parse);
        }

        private sealed class QueueMockLLMService : ILLMService2D
        {
            private readonly Queue<LLMResponseDTO> _responses = new Queue<LLMResponseDTO>();

            public int CallCount { get; private set; }
            public readonly List<string> UserPrompts = new List<string>();

            public void Enqueue(
                string rawContent,
                bool requestSucceeded = true,
                string errorMessage = null,
                long latencyMs = 120,
                int promptTokens = 100,
                int completionTokens = 20)
            {
                _responses.Enqueue(new LLMResponseDTO
                {
                    RawContent = rawContent,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    TotalTokens = promptTokens + completionTokens,
                    LatencyMs = latencyMs,
                    IsSuccess = requestSucceeded && !string.IsNullOrEmpty(rawContent),
                    RequestSucceeded = requestSucceeded,
                    ErrorMessage = errorMessage,
                    ModelName = "mock-hybrid"
                });
            }

            public Task<LLMResponseDTO> SendChatCompletionAsync(string systemPrompt, string userPrompt)
            {
                CallCount++;
                UserPrompts.Add(userPrompt);

                if (_responses.Count == 0)
                {
                    return Task.FromResult(new LLMResponseDTO
                    {
                        IsSuccess = false,
                        RequestSucceeded = false,
                        ErrorMessage = "Mock response queue is empty.",
                        ModelName = "mock-hybrid"
                    });
                }

                return Task.FromResult(_responses.Dequeue());
            }
        }

        private HybridTacticalAgent CreateHybridAgent(
            ILLMService2D service,
            CombatTelemetryLogger2D logger,
            out LLMConfig config)
        {
            var agentGo = new GameObject("HybridAgent");
            _extraObjects.Add(agentGo);
            var agent = agentGo.AddComponent<HybridTacticalAgent>();

            config = LLMConfig.CreateRuntimeDefault();
            agent.Configure(config, service, logger);
            return agent;
        }

        // ===================================================================
        // Fase 1: Validator 5 lapis (H1-H5)
        // ===================================================================

        [Test]
        public void TC_HYB_01_Validator_FlagsH1_DeadTarget()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2), "Neutral", 4, 3);
            var enemy = CreateEntity("enemy_1", "Drone", TeamSide.EnemyTeam, 50, 15, new Vector2Int(4, 2), "Aggressive", 5, 4, true);
            enemy.Stats.CurrentHP = 0;

            var combatants = new List<CombatEntity2D> { marcus, enemy };
            var validation = ValidateRaw(
                marcus,
                "{\"action\":\"Shoot\",\"target_id\":\"enemy_1\",\"reasoning\":\"finish it\"}",
                combatants,
                out _);

            Assert.IsFalse(validation.IsValid);
            Assert.IsTrue(validation.Has(HallucinationCategory2D.H1_InvalidTarget));
        }

        [Test]
        public void TC_HYB_02_Validator_FlagsH2_SkillCooldown()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2));
            var claire = CreateEntity("u4_claire", "Claire", TeamSide.PlayerTeam, 70, 10, new Vector2Int(2, 3), "Passive", 4, 3);
            marcus.PutSkillOnCooldown(2);

            var combatants = new List<CombatEntity2D> { marcus, claire };
            var validation = ValidateRaw(
                marcus,
                "{\"action\":\"CastSkill\",\"target_id\":\"u4_claire\",\"skill_id\":\"Guard\",\"reasoning\":\"protect\"}",
                combatants,
                out _);

            Assert.IsTrue(validation.Has(HallucinationCategory2D.H2_SkillCooldownOrOwnership));
        }

        [Test]
        public void TC_HYB_03_Validator_FlagsH3_ShotOutOfRange()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2), "Neutral", 4, 3);
            var enemy = CreateEntity("enemy_1", "Far Drone", TeamSide.EnemyTeam, 50, 15, new Vector2Int(15, 9), "Aggressive", 5, 4, true);

            var combatants = new List<CombatEntity2D> { marcus, enemy };
            var validation = ValidateRaw(
                marcus,
                "{\"action\":\"Shoot\",\"target_id\":\"enemy_1\",\"reasoning\":\"long shot\"}",
                combatants,
                out _);

            Assert.IsTrue(validation.Has(HallucinationCategory2D.H3_SpatialGeometryViolation));
        }

        [Test]
        public void TC_HYB_04_Validator_FlagsH4_UnknownAction()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2));
            var combatants = new List<CombatEntity2D> { marcus };

            var validation = ValidateRaw(
                marcus,
                "{\"action\":\"Teleport\",\"destination\":{\"x\":9,\"y\":9},\"reasoning\":\"blink\"}",
                combatants,
                out _);

            Assert.IsTrue(validation.Has(HallucinationCategory2D.H4_ActionProtocolInconsistency));
        }

        [Test]
        public void TC_HYB_05_Validator_FlagsH5_ParseFailure()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2));
            var combatants = new List<CombatEntity2D> { marcus };

            var validation = ValidateRaw(
                marcus,
                "I will flank the drone from the north.",
                combatants,
                out _);

            Assert.IsTrue(validation.Has(HallucinationCategory2D.H5_OutputSyntaxFailure));
        }

        // ===================================================================
        // Fase 2: Feedback prompt
        // ===================================================================

        [Test]
        public void TC_HYB_06_FeedbackPrompt_ContainsDiagnosticsAndFeasibleOptions()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2), "Neutral", 4, 3);
            var enemy = CreateEntity("enemy_1", "Drone", TeamSide.EnemyTeam, 50, 15, new Vector2Int(4, 2), "Aggressive", 5, 4, true);

            var combatants = new List<CombatEntity2D> { marcus, enemy };
            var validation = ValidateRaw(
                marcus,
                "{\"action\":\"Shoot\",\"target_id\":\"enemy_ghost\",\"reasoning\":\"phantom\"}",
                combatants,
                out var snapshot);

            string prompt = new FeedbackPromptBuilder2D().BuildFeedbackPrompt(validation, snapshot);

            Assert.IsTrue(prompt.Contains("SYSTEM VALIDATION FEEDBACK"));
            Assert.IsTrue(prompt.Contains("REJECTED"));
            Assert.IsTrue(prompt.Contains("H1"));
            Assert.IsTrue(prompt.Contains("CURRENT FEASIBLE OPTIONS"));
            Assert.IsTrue(prompt.Contains("REVISE YOUR DECISION"));
            Assert.IsTrue(prompt.Contains("enemy_1"),
                "Feedback must list living targets the LLM can legally shoot.");
        }

        // ===================================================================
        // Fase 4: Hybrid pipeline
        // ===================================================================

        [Test]
        public void TC_HYB_07_Hybrid_RetryResolvesAndExecutesLegalAction()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2), "Neutral", 4, 3);
            var enemy = CreateEntity("enemy_1", "Drone", TeamSide.EnemyTeam, 50, 15, new Vector2Int(4, 2), "Aggressive", 5, 4, true);

            var loggerGo = new GameObject("HybridTelemetry");
            _extraObjects.Add(loggerGo);
            var logger = loggerGo.AddComponent<CombatTelemetryLogger2D>();

            var mock = new QueueMockLLMService();
            mock.Enqueue("{\"action\":\"Shoot\",\"target_id\":\"enemy_ghost\",\"reasoning\":\"phantom target\"}");
            mock.Enqueue("{\"action\":\"Shoot\",\"target_id\":\"enemy_1\",\"reasoning\":\"corrected to a living target\"}");

            var agent = CreateHybridAgent(mock, logger, out var config);
            agent.TurnContext = new TacticalTurnContext(1, 2);

            var combatants = new List<CombatEntity2D> { marcus, enemy };
            var decision = agent.DecideTurnAsync(marcus, _grid, combatants, Logs("Contact."))
                .GetAwaiter().GetResult();

            Assert.AreEqual(2, mock.CallCount, "Exactly one corrective retry is expected.");
            Assert.AreEqual(TacticalActionType.Shoot, decision.Action);
            Assert.AreEqual(enemy, decision.TargetEnemy);
            Assert.IsTrue(agent.RetryConducted);
            Assert.IsFalse(agent.FallbackTriggered);
            Assert.AreEqual("[RETRY_RESOLVED]", agent.LastDecisionTag);

            Assert.IsNotNull(agent.LastValidationRound1);
            Assert.IsFalse(agent.LastValidationRound1.IsValid);
            Assert.IsTrue(agent.LastValidationRound1.Has(HallucinationCategory2D.H1_InvalidTarget));
            Assert.IsNotNull(agent.LastValidationRound2);
            Assert.IsTrue(agent.LastValidationRound2.IsValid);
            Assert.IsTrue(agent.LastFeedbackPrompt.Contains("SYSTEM VALIDATION FEEDBACK"));

            Assert.AreEqual(1, logger.Records.Count);
            var record = logger.Records[0];
            Assert.IsTrue(record.IsHybrid);
            Assert.IsTrue(record.HasValidator);
            Assert.IsFalse(record.ProposalRound1Valid);
            Assert.AreEqual("H1", record.ProposalRound1Violations);
            Assert.IsTrue(record.RetryConducted);
            Assert.IsTrue(record.ProposalRound2Valid);
            Assert.IsFalse(record.FallbackTriggered);
            Assert.IsTrue(record.FinalExecutedValid);
            Assert.AreEqual("Shoot", record.FinalExecutedAction);
            Assert.GreaterOrEqual(record.LatencyLlmAttempt1Ms, 100);
            Assert.Greater(record.LatencyTotalMs, 0);

            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void TC_HYB_08_Hybrid_FallsBackToHFSMAfterRepeatedViolation()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2), "Aggressive", 4, 3);
            var enemy = CreateEntity("enemy_1", "Drone", TeamSide.EnemyTeam, 50, 15, new Vector2Int(4, 2), "Aggressive", 5, 4, true);

            var loggerGo = new GameObject("HybridTelemetry");
            _extraObjects.Add(loggerGo);
            var logger = loggerGo.AddComponent<CombatTelemetryLogger2D>();

            var mock = new QueueMockLLMService();
            mock.Enqueue("{\"action\":\"Shoot\",\"target_id\":\"enemy_ghost\",\"reasoning\":\"phantom target\"}");
            mock.Enqueue("{\"action\":\"Shoot\",\"target_id\":\"enemy_ghost\",\"reasoning\":\"still phantom\"}");

            var agent = CreateHybridAgent(mock, logger, out var config);
            agent.TurnContext = new TacticalTurnContext(1, 3);

            var combatants = new List<CombatEntity2D> { marcus, enemy };
            var decision = agent.DecideTurnAsync(marcus, _grid, combatants, Logs("Contact."))
                .GetAwaiter().GetResult();

            Assert.AreEqual(2, mock.CallCount);
            Assert.IsTrue(agent.RetryConducted);
            Assert.IsTrue(agent.FallbackTriggered);
            Assert.AreEqual("[HFSM_FALLBACK]", agent.LastDecisionTag);
            Assert.IsTrue(decision.Reasoning.Contains("[HFSM FALLBACK - REASON:"));
            Assert.IsFalse(decision.Action == TacticalActionType.Shoot && decision.TargetEnemy == null,
                "A fallback Shoot must always carry a valid target.");

            // The deterministic fallback must itself be legal.
            var fallbackValidation = new TacticalActionValidator2D().Validate(
                decision, agent.LastSnapshot, _grid, null);
            Assert.IsTrue(fallbackValidation.IsValid, fallbackValidation.DiagnosticMessage);

            Assert.AreEqual(1, logger.Records.Count);
            var record = logger.Records[0];
            Assert.IsFalse(record.ProposalRound1Valid);
            Assert.IsTrue(record.RetryConducted);
            Assert.IsFalse(record.ProposalRound2Valid);
            Assert.IsTrue(record.FallbackTriggered);
            Assert.IsTrue(record.FinalExecutedValid);
            Assert.IsFalse(string.IsNullOrEmpty(record.FallbackReason));
            Assert.IsTrue(record.FallbackReason.Contains("H1"));

            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void TC_HYB_09_Hybrid_TransportFailureTriggersFallback()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2), "Aggressive", 4, 3);
            var enemy = CreateEntity("enemy_1", "Drone", TeamSide.EnemyTeam, 50, 15, new Vector2Int(4, 2), "Aggressive", 5, 4, true);

            var loggerGo = new GameObject("HybridTelemetry");
            _extraObjects.Add(loggerGo);
            var logger = loggerGo.AddComponent<CombatTelemetryLogger2D>();

            var mock = new QueueMockLLMService();
            mock.Enqueue(null, requestSucceeded: false, errorMessage: "HTTP 500: connection reset", latencyMs: 40);
            mock.Enqueue(null, requestSucceeded: false, errorMessage: "Timeout after 30s", latencyMs: 30000);

            var agent = CreateHybridAgent(mock, logger, out var config);
            agent.TurnContext = new TacticalTurnContext(1, 4);

            var combatants = new List<CombatEntity2D> { marcus, enemy };
            var decision = agent.DecideTurnAsync(marcus, _grid, combatants, Logs("Network degraded."))
                .GetAwaiter().GetResult();

            Assert.AreEqual(2, mock.CallCount, "A failed first attempt still gets exactly one retry.");
            Assert.IsTrue(agent.FallbackTriggered);
            Assert.AreEqual("[HFSM_FALLBACK]", agent.LastDecisionTag);
            Assert.IsTrue(decision.Reasoning.Contains("[HFSM FALLBACK - REASON:"));

            var record = logger.Records[0];
            Assert.AreEqual("H5", record.ProposalRound1Violations);
            Assert.AreEqual("H5", record.ProposalRound2Violations);
            Assert.IsTrue(record.FallbackTriggered);
            Assert.IsTrue(record.FallbackReason.Contains("transport failure"));
            Assert.IsFalse(record.RequestSucceeded);
            Assert.IsTrue(record.FinalExecutedValid);
            Assert.GreaterOrEqual(record.LatencyLlmAttempt2Ms, 30000);

            UnityEngine.Object.DestroyImmediate(config);
        }

        // ===================================================================
        // Fase 5: Telemetry schema & export
        // ===================================================================

        [Test]
        public void TC_HYB_10_Telemetry_ExportsHybridColumnsAndMetrics()
        {
            var go = new GameObject("HybridTelemetryExport");
            _extraObjects.Add(go);
            var logger = go.AddComponent<CombatTelemetryLogger2D>();

            logger.LogTurnRecord(new TurnTelemetryRecord
            {
                RunId = "RUN_HYB",
                ConfigId = "CFG-03",
                ScenarioId = "SCEN_HYB",
                AgentType = "Hybrid_LLM_HFSM_Guardrail",
                HasValidator = true,
                IsHybrid = true,
                ModelName = "mock-hybrid",
                RoundNumber = 1,
                TurnNumber = 1,
                ActorId = "u1_marcus",
                ActorName = "Marcus",
                TraitProfile = "Aggressive",
                ProposedAction = "Shoot",
                ProposedTarget = "enemy_ghost",
                IsValid = false,
                ViolationCode = "H1",
                ViolationDetails = "H1: Target 'enemy_ghost' is not registered.",
                FinalAction = "Shoot",
                FinalTarget = "enemy_1",
                IsFallback = true,
                ProposalRound1Action = "Shoot",
                ProposalRound1Target = "enemy_ghost",
                ProposalRound1Valid = false,
                ProposalRound1Violations = "H1",
                RetryConducted = true,
                ProposalRound2Action = "Shoot",
                ProposalRound2Target = "enemy_ghost",
                ProposalRound2Valid = false,
                ProposalRound2Violations = "H1",
                FallbackTriggered = true,
                FallbackReason = "H1 after 1 corrective retry",
                FinalExecutedAction = "Shoot",
                FinalExecutedValid = true,
                ParseSucceeded = true,
                RequestSucceeded = true,
                LatencyMs = 1500,
                LatencyLlmAttempt1Ms = 600,
                LatencyLlmAttempt2Ms = 700,
                LatencyValidatorMs = 5,
                LatencyFallbackMs = 2,
                LatencyTotalMs = 1507,
                InputTokens = 500,
                OutputTokens = 40,
                TotalTokens = 540
            });

            string tempDir = Path.Combine(Path.GetTempPath(), "hybrid_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string csvPath = Path.Combine(tempDir, "hybrid.csv");
            string jsonPath = Path.Combine(tempDir, "hybrid.json");

            logger.ExportToCsv(csvPath);
            logger.ExportToJson(jsonPath);

            string[] lines = File.ReadAllLines(csvPath);
            Assert.AreEqual(2, lines.Length, "Header + 1 data row expected.");
            Assert.IsTrue(lines[0].Contains("is_hybrid"));
            Assert.IsTrue(lines[0].Contains("proposal_round_1_valid"));
            Assert.IsTrue(lines[0].Contains("retry_conducted"));
            Assert.IsTrue(lines[0].Contains("fallback_triggered"));
            Assert.IsTrue(lines[0].Contains("latency_total_ms"));

            string json = File.ReadAllText(jsonPath);
            Assert.IsTrue(json.Contains("\"raw_illogical_decision_rate_percent_Y2\""));
            Assert.IsTrue(json.Contains("\"executed_illogical_decision_rate_percent_Y2\""));
            Assert.IsTrue(json.Contains("\"retry_count\": 1"));
            Assert.IsTrue(json.Contains("\"fallback_count\": 1"));
            Assert.IsTrue(json.Contains("\"avg_latency_total_ms_Y7\""));

            // Raw proposal was illegal, but the executed action was legal: 0% executed Y2.
            Assert.IsTrue(json.Contains("\"raw_illogical_decisions\": 1"));
            Assert.IsTrue(json.Contains("\"executed_illogical_decisions\": 0"));

            try { Directory.Delete(tempDir, true); } catch { }
        }

        [Test]
        public void TC_HYB_11_PureLLM_TelemetryPopulatesExecutedFields()
        {
            var marcus = CreateEntity("u1_marcus", "Marcus", TeamSide.PlayerTeam, 140, 20, new Vector2Int(2, 2), "Neutral", 4, 3);
            var enemy = CreateEntity("enemy_1", "Drone", TeamSide.EnemyTeam, 50, 15, new Vector2Int(4, 2), "Aggressive", 5, 4, true);

            var loggerGo = new GameObject("PureTelemetry");
            _extraObjects.Add(loggerGo);
            var logger = loggerGo.AddComponent<CombatTelemetryLogger2D>();

            var agentGo = new GameObject("PureAgent");
            _extraObjects.Add(agentGo);
            var agent = agentGo.AddComponent<PureLLMTacticalAgent>();

            var mock = new QueueMockLLMService();
            mock.Enqueue("{\"action\":\"Shoot\",\"target_id\":\"enemy_ghost\",\"reasoning\":\"phantom\"}");

            var config = LLMConfig.CreateRuntimeDefault();
            agent.Configure(config, mock, logger);
            agent.TurnContext = new TacticalTurnContext(1, 1);

            agent.DecideTurnAsync(marcus, _grid, new List<CombatEntity2D> { marcus, enemy }, Logs("Contact."))
                .GetAwaiter().GetResult();

            var record = logger.Records[0];
            Assert.IsFalse(record.IsHybrid);
            Assert.IsFalse(record.RetryConducted);
            Assert.IsFalse(record.FallbackTriggered);
            Assert.IsFalse(record.ProposalRound1Valid);
            Assert.AreEqual("H1", record.ProposalRound1Violations);
            Assert.IsFalse(record.FinalExecutedValid,
                "Pure LLM executes raw proposals, so the illegal action remains illegal at execution.");
            Assert.AreEqual("Shoot", record.FinalExecutedAction);

            UnityEngine.Object.DestroyImmediate(config);
        }
    }
}
