using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using RPGStrategy.AI.Hybrid;
using RPGStrategy.AI.Traits;
using RPGStrategy.Core.Data;

namespace RPGStrategy.Experiment
{
    /// <summary>
    /// M7 – Telemetry Logger.
    /// Writes per-turn and per-experiment data to CSV and JSON for statistical analysis.
    /// Outputs:
    ///   combat_turns_raw.csv   – granular per-turn decision data
    ///   experiment_summary.json – aggregated Y1–Y7 per experiment run
    ///   y5_blinded_export.json  – anonymised data subset for human rater survey
    /// </summary>
    public class TelemetryLogger
    {
        private readonly string _outputDir;
        private readonly List<TurnTelemetry> _turnLog = new List<TurnTelemetry>();
        private readonly List<ExperimentSummary> _summaries = new List<ExperimentSummary>();

        // Action frequency map per experiment (for Shannon Entropy Y1)
        private Dictionary<string, int> _actionFreqMap = new Dictionary<string, int>();
        private ExperimentConfig _currentConfig;
        private long _maxLatencyMs;
        private long _totalLatencyMs;
        private long _totalInputTokens;
        private long _totalOutputTokens;
        private int _invalidCount;
        private int _fallbackCount;
        private float _totalTraitConsistency;
        private int _traitConsistencyCount;

        public TelemetryLogger(string outputDirectory)
        {
            _outputDir = outputDirectory;
            Directory.CreateDirectory(_outputDir);
        }

        // ─── Per-Run Lifecycle ────────────────────────────────────────────────────

        public void BeginExperiment(ExperimentConfig config)
        {
            _currentConfig = config;
            _actionFreqMap = new Dictionary<string, int>();
            _maxLatencyMs = 0;
            _totalLatencyMs = 0;
            _totalInputTokens = 0;
            _totalOutputTokens = 0;
            _invalidCount = 0;
            _fallbackCount = 0;
            _totalTraitConsistency = 0f;
            _traitConsistencyCount = 0;
        }

        public void LogTurn(
            int turn,
            string entityId,
            string entityName,
            AgentDecision decision,
            CombatStateSnapshot snapshot,
            ValidationResult validationResult,
            bool fallbackActivated,
            bool retryAttempted,
            long latencyMs,
            int inputTokens,
            int outputTokens,
            float traitConsistencyScore)
        {
            if (_currentConfig == null) return;

            var actor = snapshot?.CurrentActor;
            string actionType = LookupActionType(snapshot, decision?.ActionId);
            bool isValid = validationResult?.IsValid ?? true;

            // Accumulate for aggregation
            if (!isValid) _invalidCount++;
            if (fallbackActivated) _fallbackCount++;

            _totalLatencyMs += latencyMs;
            _maxLatencyMs = Math.Max(_maxLatencyMs, latencyMs);
            _totalInputTokens += inputTokens;
            _totalOutputTokens += outputTokens;
            _totalTraitConsistency += traitConsistencyScore;
            _traitConsistencyCount++;

            string actionId = decision?.ActionId ?? "UNKNOWN";
            if (_actionFreqMap.ContainsKey(actionId))
                _actionFreqMap[actionId]++;
            else
                _actionFreqMap[actionId] = 1;

            var telemetry = new TurnTelemetry
            {
                ExperimentId = _currentConfig.ExperimentId,
                ScenarioId = _currentConfig.ScenarioId,
                AgentControllerType = _currentConfig.AgentControllerType,
                ContextRichness = _currentConfig.ContextRichness,
                CompressionLevel = _currentConfig.CompressionLevel.ToString(),
                LLMModel = _currentConfig.LLMModel,
                RepetitionIndex = _currentConfig.RepetitionIndex,
                TurnNumber = turn,
                EntityId = entityId,
                EntityName = entityName,
                TraitId = actor?.CurrentTraitId ?? "Unknown",
                ActionId = actionId,
                ActionType = actionType,
                Compliance = decision?.Compliance ?? "UNKNOWN",
                IsValid = isValid,
                HallucinationType = validationResult != null ? validationResult.HallucinationCode.ToString() : "None",
                FallbackActivated = fallbackActivated,
                RetryAttempted = retryAttempted,
                TraitConsistencyScore = traitConsistencyScore,
                LatencyMs = latencyMs,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens,
                RadioChatter = decision?.RadioChatter ?? "",
                Reasoning = decision?.Reasoning ?? "",
                ActorHPPercent = actor?.HPPercentage ?? 0f,
                ActorRationality = actor?.Rationality ?? 0,
                TimestampUtc = DateTime.UtcNow.ToString("o")
            };

            _turnLog.Add(telemetry);
        }

        public ExperimentSummary EndExperiment(string battleOutcome, int totalTurns, float unjustifiedReversalRate, float compressionRatio = 0f)
        {
            if (_currentConfig == null) return null;

            float entropy = CalculateShannonEntropy(_actionFreqMap, totalTurns > 0 ? totalTurns : 1);

            var summary = new ExperimentSummary
            {
                ExperimentId = _currentConfig.ExperimentId,
                ScenarioId = _currentConfig.ScenarioId,
                AgentControllerType = _currentConfig.AgentControllerType,
                ContextRichness = _currentConfig.ContextRichness,
                CompressionLevel = _currentConfig.CompressionLevel.ToString(),
                LLMModel = _currentConfig.LLMModel,
                RepetitionIndex = _currentConfig.RepetitionIndex,
                BattleOutcome = battleOutcome,
                TotalTurns = totalTurns,
                ShannonEntropy = entropy,
                ActionFrequencyMap = new Dictionary<string, int>(_actionFreqMap),
                TotalInvalidActions = _invalidCount,
                TotalFallbackActivations = _fallbackCount,
                InvalidActionRate = totalTurns > 0 ? (float)_invalidCount / totalTurns : 0f,
                MeanTraitConsistency = _traitConsistencyCount > 0 ? _totalTraitConsistency / _traitConsistencyCount : 0f,
                UnjustifiedReversalRate = unjustifiedReversalRate,
                HumanPerceivedRealism = -1f, // Awaiting blinded rater
                TotalInputTokens = _totalInputTokens,
                TotalOutputTokens = _totalOutputTokens,
                MeanTokensPerTurn = totalTurns > 0 ? (float)(_totalInputTokens + _totalOutputTokens) / totalTurns : 0f,
                CompressionRatioAchieved = compressionRatio,
                TotalLatencyMs = _totalLatencyMs,
                MeanLatencyMs = totalTurns > 0 ? (float)_totalLatencyMs / totalTurns : 0f,
                MaxLatencyMs = _maxLatencyMs,
                CompletedAtUtc = DateTime.UtcNow.ToString("o")
            };

            _summaries.Add(summary);
            return summary;
        }

        // ─── Export ───────────────────────────────────────────────────────────────

        public void ExportTurnCsv()
        {
            if (_turnLog.Count == 0) return;
            string path = Path.Combine(_outputDir, "combat_turns_raw.csv");
            bool fileExists = File.Exists(path);

            using (var sw = new StreamWriter(path, append: true, encoding: Encoding.UTF8))
            {
                if (!fileExists)
                {
                    sw.WriteLine("ExperimentId,ScenarioId,AgentControllerType,ContextRichness,CompressionLevel," +
                                 "LLMModel,Rep,Turn,EntityId,EntityName,TraitId,ActionId,ActionType,Compliance," +
                                 "IsValid,HallucinationType,FallbackActivated,RetryAttempted,TraitConsistency," +
                                 "LatencyMs,InputTokens,OutputTokens,TotalTokens,ActorHP,ActorRationality,TimestampUtc");
                }

                foreach (var t in _turnLog)
                {
                    sw.WriteLine(
                        $"{Q(t.ExperimentId)},{Q(t.ScenarioId)},{Q(t.AgentControllerType)},{Q(t.ContextRichness)},{Q(t.CompressionLevel)}," +
                        $"{Q(t.LLMModel)},{t.RepetitionIndex},{t.TurnNumber},{Q(t.EntityId)},{Q(t.EntityName)},{Q(t.TraitId)}," +
                        $"{Q(t.ActionId)},{Q(t.ActionType)},{Q(t.Compliance)},{t.IsValid},{Q(t.HallucinationType)}," +
                        $"{t.FallbackActivated},{t.RetryAttempted},{t.TraitConsistencyScore:F4}," +
                        $"{t.LatencyMs},{t.InputTokens},{t.OutputTokens},{t.TotalTokens}," +
                        $"{t.ActorHPPercent:F2},{t.ActorRationality},{Q(t.TimestampUtc)}"
                    );
                }
            }

            _turnLog.Clear();
            Debug.Log($"[Telemetry] Exported CSV to {path}");
        }

        public void ExportSummaryJson()
        {
            if (_summaries.Count == 0) return;
            string path = Path.Combine(_outputDir, "experiment_summary.json");

            var sb = new StringBuilder();
            sb.AppendLine("{\"experiments\":[");
            for (int i = 0; i < _summaries.Count; i++)
            {
                sb.Append(JsonUtility.ToJson(_summaries[i], true));
                if (i < _summaries.Count - 1) sb.AppendLine(",");
            }
            sb.AppendLine("]}");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[Telemetry] Exported summary JSON to {path}");
        }

        /// <summary>
        /// Exports anonymised radio_chatter + reasoning for Y5 human blinded rating.
        /// Each entry has a unique hash ID so raters cannot identify the agent controller type.
        /// </summary>
        public void ExportBlindedY5Survey()
        {
            string path = Path.Combine(_outputDir, "y5_blinded_export.json");
            var sb = new StringBuilder();
            sb.AppendLine("{\"survey_entries\":[");

            var allEntries = new List<TurnTelemetry>(_turnLog);
            // Shuffle to break ordering bias
            Shuffle(allEntries);

            for (int i = 0; i < allEntries.Count; i++)
            {
                var t = allEntries[i];
                string blindId = $"ENTRY_{i + 1:D4}_{ComputeHash(t.ExperimentId + t.TurnNumber + t.EntityId)}";
                sb.AppendLine($"{{\"id\":\"{blindId}\",\"radio_chatter\":{JsonString(t.RadioChatter)},\"reasoning\":{JsonString(t.Reasoning)},\"turn\":{t.TurnNumber},\"rating_scale\":\"0-10\"}}");
                if (i < allEntries.Count - 1) sb.AppendLine(",");
            }
            sb.AppendLine("]}");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[Telemetry] Exported Y5 blinded survey to {path}");
        }

        // ─── Shannon Entropy (Y1) ─────────────────────────────────────────────────

        public static float CalculateShannonEntropy(Dictionary<string, int> freqMap, int total)
        {
            if (total == 0 || freqMap == null || freqMap.Count == 0) return 0f;
            float entropy = 0f;
            foreach (var kvp in freqMap)
            {
                float p = (float)kvp.Value / total;
                if (p > 0f)
                    entropy -= p * (float)Math.Log(p, 2.0);
            }
            return entropy;
        }

        // ─── Utilities ────────────────────────────────────────────────────────────

        private static string Q(string s) => $"\"{(s ?? "").Replace("\"", "'")}\"";
        private static string JsonString(string s) => $"\"{(s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ")}\"";

        private static string ComputeHash(string input)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in input) hash = hash * 31 + c;
                return Math.Abs(hash).ToString("X6");
            }
        }

        private static string LookupActionType(CombatStateSnapshot snapshot, string actionId)
        {
            if (snapshot == null || string.IsNullOrEmpty(actionId)) return "Unknown";
            var dto = snapshot.GetAction(actionId);
            return dto?.ActionType ?? actionId;
        }

        private static void Shuffle<T>(List<T> list)
        {
            var rng = new System.Random();
            for (int n = list.Count - 1; n > 0; n--)
            {
                int k = rng.Next(n + 1);
                (list[k], list[n]) = (list[n], list[k]);
            }
        }
    }
}
