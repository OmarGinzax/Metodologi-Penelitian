using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using RPGStrategy.AI.Baseline;
using RPGStrategy.AI.Compression;
using RPGStrategy.AI.Core;
using RPGStrategy.AI.Hybrid;
using RPGStrategy.AI.LLM;
using RPGStrategy.AI.Traits;
using RPGStrategy.Core.Combat;
using RPGStrategy.Core.Command;
using RPGStrategy.Core.Data;
using RPGStrategy.Core.Extractor;
using Debug = UnityEngine.Debug;

namespace RPGStrategy.Experiment
{
    /// <summary>
    /// M7 – Batch Experiment Runner.
    /// Headless automated controller that runs all permutations of X1 × X2 × X3 × Scenarios × N repetitions.
    /// Does NOT require a Unity scene to run — works with headless mode or in Play mode via a bootstrap MonoBehaviour.
    /// </summary>
    public class BatchExperimentRunner
    {
        private readonly LLMConfig _llmConfig;
        private readonly TelemetryLogger _logger;
        private readonly DecisionCoherenceEvaluator _coherenceEval;
        private readonly PersonalityEvolutionEngine _evolutionEngine;
        private readonly CombatStateExtractor _stateExtractor;

        // Configurable run parameters
        public int RepetitionsPerPermutation = 5;
        public int MaxTurnsPerBattle = 40;
        public string OutputDirectory;

        // Progress reporting
        public int TotalRuns { get; private set; }
        public int CompletedRuns { get; private set; }
        public string CurrentRunId { get; private set; }
        public event Action<int, int, string> OnRunProgress; // (completed, total, currentId)
        public event Action<ExperimentSummary> OnRunComplete;

        private static readonly string[] AgentTypes = { "PureLLM", "Hybrid", "HFSM" };
        private static readonly string[] ContextRichnesses = { "Minimal", "Moderate", "Full" };
        private static readonly PromptCompressor.CompressionLevel[] CompressionLevels = {
            PromptCompressor.CompressionLevel.None,
            PromptCompressor.CompressionLevel.Moderate,
            PromptCompressor.CompressionLevel.Aggressive
        };
        private static readonly ScenarioPresets.ScenarioId[] Scenarios = {
            ScenarioPresets.ScenarioId.ScenarioA,
            ScenarioPresets.ScenarioId.ScenarioB,
            ScenarioPresets.ScenarioId.ScenarioC,
            ScenarioPresets.ScenarioId.ScenarioD
        };

        public BatchExperimentRunner(LLMConfig llmConfig, string outputDir)
        {
            _llmConfig = llmConfig;
            OutputDirectory = outputDir;
            _logger = new TelemetryLogger(outputDir);
            _coherenceEval = new DecisionCoherenceEvaluator();
            _evolutionEngine = new PersonalityEvolutionEngine();
            _stateExtractor = new CombatStateExtractor();

            // Total: 3 × 3 × 3 × 4 × N
            TotalRuns = AgentTypes.Length * ContextRichnesses.Length * CompressionLevels.Length
                        * Scenarios.Length * RepetitionsPerPermutation;
        }

        // ─── Full Batch ───────────────────────────────────────────────────────────

        public async Task RunAllAsync(CancellationToken cancellationToken = default)
        {
            CompletedRuns = 0;
            int runIndex = 0;

            foreach (var agentType in AgentTypes)
            foreach (var context in ContextRichnesses)
            foreach (var compression in CompressionLevels)
            foreach (var scenario in Scenarios)
            for (int rep = 1; rep <= RepetitionsPerPermutation; rep++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var config = new ExperimentConfig
                {
                    ExperimentId = $"EXP_{++runIndex:D4}",
                    ScenarioId = scenario.ToString(),
                    AgentControllerType = agentType,
                    ContextRichness = context,
                    CompressionLevel = compression,
                    LLMModel = _llmConfig?.ModelName ?? "mock",
                    RepetitionIndex = rep,
                    MaxTurns = MaxTurnsPerBattle
                };

                CurrentRunId = config.ExperimentId;
                OnRunProgress?.Invoke(CompletedRuns, TotalRuns, config.ToString());

                try
                {
                    var summary = await RunSingleExperimentAsync(config, cancellationToken);
                    if (summary != null) OnRunComplete?.Invoke(summary);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BatchRunner] Run {config.ExperimentId} failed: {ex.Message}");
                }

                CompletedRuns++;
            }

            _logger.ExportSummaryJson();
            Debug.Log($"[BatchRunner] All {CompletedRuns}/{TotalRuns} runs complete. Results saved to {OutputDirectory}");
        }

        // ─── Single Experiment ────────────────────────────────────────────────────

        public async Task<ExperimentSummary> RunSingleExperimentAsync(ExperimentConfig config, CancellationToken cancellationToken = default)
        {
            _logger.BeginExperiment(config);
            _coherenceEval.Reset();

            // Build battlefield
            var (squad, enemies, directives) = BuildScenario(config.ScenarioId);
            var allEntities = new List<CombatEntity>();
            allEntities.AddRange(squad);
            allEntities.AddRange(enemies);

            // Assign agent controller to each squad member
            var agentControllers = new Dictionary<string, IAgentController>();
            var llmService = new LLMService(_llmConfig);

            foreach (var e in squad)
            {
                agentControllers[e.EntityId] = CreateController(config.AgentControllerType, llmService);
            }

            // Battle loop
            var turnQueue = new Queue<CombatEntity>(squad); // simplified round-robin for headless
            string battleOutcome = "Timeout";
            int turn = 0;
            float totalCompressionRatio = 0f;
            int compressionSamples = 0;
            CommanderDirectiveDTO activeDirective = directives.Count > 0 ? directives[0] : null;

            while (turn < config.MaxTurns && !cancellationToken.IsCancellationRequested)
            {
                turn++;

                // Check win/loss conditions
                bool allEnemiesDead = enemies.TrueForAll(e => !e.IsAlive);
                bool allAlliesDead = squad.TrueForAll(e => !e.IsAlive);
                if (allEnemiesDead) { battleOutcome = "Victory"; break; }
                if (allAlliesDead) { battleOutcome = "Defeat"; break; }

                // Rotate directive every 10 turns
                if (directives.Count > 1 && turn % 10 == 0)
                    activeDirective = directives[(turn / 10) % directives.Count];

                // Process each alive squad member
                foreach (var agent in squad)
                {
                    if (!agent.IsAlive || cancellationToken.IsCancellationRequested) continue;

                    // Build snapshot
                    var snapshot = _stateExtractor.ExtractSnapshot(allEntities, agent, turn, _buildEvents(turn), activeDirective);

                    // Compression
                    string originalPrompt = PromptBuilder.BuildUserPrompt(snapshot);
                    string compressedPrompt = PromptCompressor.Compress(snapshot, config.CompressionLevel);
                    float ratio = PromptCompressor.CompressionRatio(originalPrompt, compressedPrompt);
                    totalCompressionRatio += ratio;
                    compressionSamples++;

                    // Get decision
                    var sw = Stopwatch.StartNew();
                    AgentDecision decision = null;
                    ValidationResult validation = null;
                    bool fallback = false;
                    bool retried = false;

                    if (agentControllers.TryGetValue(agent.EntityId, out var controller))
                    {
                        decision = await controller.DecideActionAsync(snapshot, cancellationToken);

                        // Extract validation result if Hybrid
                        if (controller is HybridAgentController hybrid)
                        {
                            validation = hybrid.LastValidationResult ?? ValidationResult.Success();
                            fallback = hybrid.FallbackActivated;
                            retried = hybrid.RetryAttempted > 0;
                        }
                        else
                        {
                            // For Pure LLM and HFSM, validate post-hoc for Y2 measurement
                            validation = ActionValidator.Validate(snapshot, decision);
                        }
                    }
                    sw.Stop();

                    long latency = sw.ElapsedMilliseconds;
                    int inputTok = PromptCompressor.EstimateTokenCount(compressedPrompt);
                    int outputTok = PromptCompressor.EstimateTokenCount(decision?.RadioChatter ?? "" + decision?.Reasoning ?? "");

                    // Coherence evaluation (Y3, Y4)
                    var record = _coherenceEval.Record(agent.EntityId, turn, decision, snapshot);
                    float traitScore = record?.TraitConsistencyScore ?? 0.5f;

                    // Log this turn
                    _logger.LogTurn(turn, agent.EntityId, agent.EntityName, decision, snapshot,
                        validation, fallback, retried, latency, inputTok, outputTok, traitScore);

                    // Execute action (simplified: just apply basic damage for batch mode)
                    if (decision != null && validation != null && validation.IsValid)
                    {
                        ApplySimplifiedAction(agent, decision, enemies, squad);
                    }
                }
            }

            _logger.ExportTurnCsv();

            // Aggregate coherence reports for Y4
            float totalReversalRate = 0f;
            int agentCount = 0;
            foreach (var agent in squad)
            {
                var report = _coherenceEval.GenerateReport(agent.EntityId);
                totalReversalRate += report.UnjustifiedReversalRate;
                agentCount++;
            }
            float meanReversalRate = agentCount > 0 ? totalReversalRate / agentCount : 0f;
            float meanCompression = compressionSamples > 0 ? totalCompressionRatio / compressionSamples : 0f;

            var summary = _logger.EndExperiment(battleOutcome, turn, meanReversalRate, meanCompression);
            return summary;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private IAgentController CreateController(string agentType, LLMService llmService)
        {
            switch (agentType)
            {
                case "PureLLM": return new PureLLMAgent(llmService);
                case "Hybrid": return new HybridAgentController(llmService);
                case "HFSM": return new HFSMController();
                default:
                    Debug.LogWarning($"[BatchRunner] Unknown agent type '{agentType}', defaulting to HFSM.");
                    return new HFSMController();
            }
        }

        private (List<CombatEntity> squad, List<CombatEntity> enemies, List<CommanderDirectiveDTO> directives) BuildScenario(string scenarioId)
        {
            List<EntitySpawnData> squadStats, enemyStats;
            switch (scenarioId)
            {
                case "ScenarioB": squadStats = ScenarioPresets.GetSquadB(); enemyStats = ScenarioPresets.GetEnemiesB(); break;
                case "ScenarioC": squadStats = ScenarioPresets.GetSquadC(); enemyStats = ScenarioPresets.GetEnemiesC(); break;
                case "ScenarioD": squadStats = ScenarioPresets.GetSquadD(); enemyStats = ScenarioPresets.GetEnemiesD(); break;
                default:          squadStats = ScenarioPresets.GetSquadA(); enemyStats = ScenarioPresets.GetEnemiesA(); break;
            }

            var squad = new List<CombatEntity>();
            foreach (var s in squadStats)
            {
                var go = new GameObject(s.Name);
                var e = go.AddComponent<CombatEntity>();
                e.Initialize(s.EntityId, s.Name, s.Team, s.Stats, s.TraitId, s.FieldDistance);
                squad.Add(e);
            }

            var enemies = new List<CombatEntity>();
            foreach (var s in enemyStats)
            {
                var go = new GameObject(s.Name);
                var e = go.AddComponent<CombatEntity>();
                e.Initialize(s.EntityId, s.Name, s.Team, s.Stats, s.TraitId, s.FieldDistance);
                enemies.Add(e);
            }

            var directives = new List<CommanderDirectiveDTO>
            {
                new CommanderDirectiveDTO(DirectiveType.FocusFireTarget,     "enemy_hound_iv", "Focus all fire on primary target!", 0),
                new CommanderDirectiveDTO(DirectiveType.DefensiveHold,       null,             "Hold the line! Do not advance!",    10),
                new CommanderDirectiveDTO(DirectiveType.FullTacticalRetreat, null,             "Fall back to extraction! Now!",      20)
            };

            return (squad, enemies, directives);
        }

        private void ApplySimplifiedAction(CombatEntity actor, AgentDecision decision, List<CombatEntity> enemies, List<CombatEntity> allies)
        {
            // Simplified damage application for headless batch — no visual effects
            if (decision.ActionId == "Action_Defend" || decision.ActionId.Contains("Defend")) return;

            var target = enemies.Find(e => e.IsAlive && e.EntityId == decision.TargetId);
            if (target != null && target.IsAlive)
            {
                int dmg = Math.Max(1, actor.Stats.AttackPower - target.Stats.DefensePower / 2);
                target.ApplyDamage(dmg);
            }
        }

        private List<string> _buildEvents(int turn)
        {
            return new List<string>
            {
                $"Turn {turn}: Engagement ongoing. Sector 7 Overpass.",
                "Heavy gunfire from the north.",
                "Comms are patchy — radio interference detected."
            };
        }
    }
}
