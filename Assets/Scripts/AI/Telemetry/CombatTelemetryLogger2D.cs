using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using RPGStrategy.AI.PureLLM;
using UnityEngine;

namespace RPGStrategy.AI.Telemetry
{
    /// <summary>Satu baris telemetri per giliran agen (Y2, Y6, Y7).</summary>
    [Serializable]
    public class TurnTelemetryRecord
    {
        public string RunId;
        public string ConfigId;
        public string ScenarioId;
        public string AgentType;
        public bool HasValidator;
        public bool IsCompressed;
        public string ModelName;

        public int RoundNumber;
        public int TurnNumber;
        public string ActorId;
        public string ActorName;
        public string TraitProfile;

        public string ProposedAction;
        public string ProposedTarget;
        public string ProposedDestination;
        public bool IsValid;
        public string ViolationCode;
        public string ViolationDetails;

        public string FinalAction;
        public string FinalTarget;
        public string FinalDestination;
        public bool IsFallback;

        public bool ParseSucceeded;
        public bool RequestSucceeded;
        public string LlmError;
        public string Reasoning;
        public string RawResponse;

        public long LatencyMs;
        public int InputTokens;
        public int OutputTokens;
        public int TotalTokens;
        public string TimestampUtc;

        // ===================================================================
        // Hybrid architecture detail (RQ2 / Y2 raw vs executed)
        // ===================================================================

        public bool IsHybrid;

        public string ProposalRound1Action;
        public string ProposalRound1Target;
        public string ProposalRound1Destination;
        public bool ProposalRound1Valid;
        public string ProposalRound1Violations;

        public bool RetryConducted;
        public string ProposalRound2Action;
        public string ProposalRound2Target;
        public string ProposalRound2Destination;
        public bool ProposalRound2Valid;
        public string ProposalRound2Violations;

        public bool FallbackTriggered;
        public string FallbackReason;
        public string FinalExecutedAction;
        public bool FinalExecutedValid;

        public long LatencyLlmAttempt1Ms;
        public long LatencyLlmAttempt2Ms;
        public long LatencyValidatorMs;
        public long LatencyFallbackMs;
        public long LatencyTotalMs;
    }

    /// <summary>
    /// Perekam telemetri penelitian untuk Pure LLM Agent.
    /// Menyimpan record per giliran dan mengekspor CSV granular + ringkasan JSON.
    /// </summary>
    public class CombatTelemetryLogger2D : MonoBehaviour
    {
        [Header("Output")]
        [SerializeField] private string outputDirectory = "Logs";
        [SerializeField] private string csvFileName = "telemetry_pure_llm.csv";
        [SerializeField] private string jsonFileName = "telemetry_pure_llm_summary.json";

        [Header("Debug")]
        [SerializeField] private bool logToConsole = false;
        [SerializeField] private bool autoExportOnBattleEnd = true;

        private readonly List<TurnTelemetryRecord> _records = new List<TurnTelemetryRecord>();

        public IReadOnlyList<TurnTelemetryRecord> Records => _records;
        public bool AutoExportOnBattleEnd { get => autoExportOnBattleEnd; set => autoExportOnBattleEnd = value; }

        public void LogTurnRecord(TurnTelemetryRecord record)
        {
            if (record == null) return;
            if (string.IsNullOrEmpty(record.TimestampUtc))
                record.TimestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            _records.Add(record);

            if (logToConsole)
            {
                string errorSuffix = string.IsNullOrEmpty(record.LlmError)
                    ? string.Empty
                    : " | err=" + (record.LlmError.Length > 180
                        ? record.LlmError.Substring(0, 180) + "..."
                        : record.LlmError);

                Debug.Log("[Telemetry] R" + record.RoundNumber + "T" + record.TurnNumber +
                          " " + record.ActorName + " | " + record.FinalAction +
                          " | valid=" + record.IsValid + " | " + record.ViolationCode +
                          " | " + record.LatencyMs + "ms | tokens " +
                          record.InputTokens + "/" + record.OutputTokens + errorSuffix);
            }
        }

        public void Clear()
        {
            _records.Clear();
        }

        public string ExportToCsv(string path = null)
        {
            string fullPath = ResolvePath(path, csvFileName);
            EnsureDirectory(fullPath);

            var sb = new StringBuilder(4096);
            sb.AppendLine("run_id,config_id,scenario_id,agent_type,has_validator,is_compressed,model_name," +
                          "round_number,turn_number,actor_id,actor_name,trait_profile," +
                          "proposed_action,proposed_target,proposed_destination," +
                          "is_valid,violation_code,violation_details," +
                          "final_action,final_target,final_destination,is_fallback," +
                          "parse_succeeded,request_succeeded,llm_error,reasoning,raw_response," +
                          "latency_ms,input_tokens,output_tokens,total_tokens," +
                          "is_hybrid," +
                          "proposal_round_1_action,proposal_round_1_target,proposal_round_1_destination," +
                          "proposal_round_1_valid,proposal_round_1_violations," +
                          "retry_conducted," +
                          "proposal_round_2_action,proposal_round_2_target,proposal_round_2_destination," +
                          "proposal_round_2_valid,proposal_round_2_violations," +
                          "fallback_triggered,fallback_reason," +
                          "final_executed_action,final_executed_valid," +
                          "latency_llm_attempt_1_ms,latency_llm_attempt_2_ms," +
                          "latency_validator_ms,latency_fallback_ms,latency_total_ms," +
                          "timestamp_utc");

            for (int i = 0; i < _records.Count; i++)
            {
                var r = _records[i];
                sb.Append(Quote(r.RunId)).Append(',');
                sb.Append(Quote(r.ConfigId)).Append(',');
                sb.Append(Quote(r.ScenarioId)).Append(',');
                sb.Append(Quote(r.AgentType)).Append(',');
                sb.Append(r.HasValidator ? "TRUE" : "FALSE").Append(',');
                sb.Append(r.IsCompressed ? "TRUE" : "FALSE").Append(',');
                sb.Append(Quote(r.ModelName)).Append(',');
                sb.Append(r.RoundNumber.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(r.TurnNumber.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(Quote(r.ActorId)).Append(',');
                sb.Append(Quote(r.ActorName)).Append(',');
                sb.Append(Quote(r.TraitProfile)).Append(',');
                sb.Append(Quote(r.ProposedAction)).Append(',');
                sb.Append(Quote(r.ProposedTarget)).Append(',');
                sb.Append(Quote(r.ProposedDestination)).Append(',');
                sb.Append(r.IsValid ? "TRUE" : "FALSE").Append(',');
                sb.Append(Quote(r.ViolationCode)).Append(',');
                sb.Append(Quote(r.ViolationDetails)).Append(',');
                sb.Append(Quote(r.FinalAction)).Append(',');
                sb.Append(Quote(r.FinalTarget)).Append(',');
                sb.Append(Quote(r.FinalDestination)).Append(',');
                sb.Append(r.IsFallback ? "TRUE" : "FALSE").Append(',');
                sb.Append(r.ParseSucceeded ? "TRUE" : "FALSE").Append(',');
                sb.Append(r.RequestSucceeded ? "TRUE" : "FALSE").Append(',');
                sb.Append(Quote(r.LlmError)).Append(',');
                sb.Append(Quote(r.Reasoning)).Append(',');
                sb.Append(Quote(r.RawResponse)).Append(',');
                sb.Append(r.LatencyMs.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(r.InputTokens.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(r.OutputTokens.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(r.TotalTokens.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(r.IsHybrid ? "TRUE" : "FALSE").Append(',');
                sb.Append(Quote(r.ProposalRound1Action)).Append(',');
                sb.Append(Quote(r.ProposalRound1Target)).Append(',');
                sb.Append(Quote(r.ProposalRound1Destination)).Append(',');
                sb.Append(r.ProposalRound1Valid ? "TRUE" : "FALSE").Append(',');
                sb.Append(Quote(r.ProposalRound1Violations)).Append(',');
                sb.Append(r.RetryConducted ? "TRUE" : "FALSE").Append(',');
                sb.Append(Quote(r.ProposalRound2Action)).Append(',');
                sb.Append(Quote(r.ProposalRound2Target)).Append(',');
                sb.Append(Quote(r.ProposalRound2Destination)).Append(',');
                sb.Append(r.ProposalRound2Valid ? "TRUE" : "FALSE").Append(',');
                sb.Append(Quote(r.ProposalRound2Violations)).Append(',');
                sb.Append(r.FallbackTriggered ? "TRUE" : "FALSE").Append(',');
                sb.Append(Quote(r.FallbackReason)).Append(',');
                sb.Append(Quote(r.FinalExecutedAction)).Append(',');
                sb.Append(r.FinalExecutedValid ? "TRUE" : "FALSE").Append(',');
                sb.Append(r.LatencyLlmAttempt1Ms.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(r.LatencyLlmAttempt2Ms.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(r.LatencyValidatorMs.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(r.LatencyFallbackMs.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(r.LatencyTotalMs.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(Quote(r.TimestampUtc));
                sb.AppendLine();
            }

            File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);
            Debug.Log("[CombatTelemetryLogger2D] CSV exported: " + fullPath);
            return fullPath;
        }

        public string ExportToJson(string path = null)
        {
            string fullPath = ResolvePath(path, jsonFileName);
            EnsureDirectory(fullPath);

            var sb = new StringBuilder(4096);
            sb.AppendLine("{");

            sb.AppendLine("  \"experiment_metadata\": {");
            sb.AppendLine("    \"exported_at_utc\": \"" + JsonUtil.Escape(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)) + "\",");
            sb.AppendLine("    \"total_turns\": " + _records.Count.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("  },");

            AppendMetricsJson(sb);

            sb.AppendLine("  \"records\": [");
            for (int i = 0; i < _records.Count; i++)
            {
                if (i > 0) sb.AppendLine(",");
                sb.Append("    ").Append(JsonUtility.ToJson(_records[i]));
            }
            sb.AppendLine();
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);
            Debug.Log("[CombatTelemetryLogger2D] JSON summary exported: " + fullPath);
            return fullPath;
        }

        public void ExportAll(string directoryOrCsvPath = null)
        {
            if (string.IsNullOrEmpty(directoryOrCsvPath))
            {
                ExportToCsv();
                ExportToJson();
                return;
            }

            // Jika diberi jalur direktori, gunakan nama file default di dalamnya.
            if (Directory.Exists(directoryOrCsvPath) || !Path.HasExtension(directoryOrCsvPath))
            {
                ExportToCsv(Path.Combine(directoryOrCsvPath, csvFileName));
                ExportToJson(Path.Combine(directoryOrCsvPath, jsonFileName));
            }
            else
            {
                ExportToCsv(directoryOrCsvPath);
                ExportToJson(Path.ChangeExtension(directoryOrCsvPath, ".json"));
            }
        }

        private void AppendMetricsJson(StringBuilder sb)
        {
            int valid = 0;
            int illogical = 0;
            int transportFailures = 0;
            int rawIllogical = 0;
            int executedIllogical = 0;
            int retryCount = 0;
            int retryResolved = 0;
            int fallbackCount = 0;
            long latencySum = 0;
            long totalLatencySum = 0;
            long attempt1Sum = 0;
            long validatorSum = 0;
            long fallbackLatencySum = 0;
            long inputSum = 0;
            long outputSum = 0;
            long totalSum = 0;

            var actionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var violationCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _records.Count; i++)
            {
                var r = _records[i];
                if (r.IsValid) valid++; else illogical++;
                if (!r.RequestSucceeded) transportFailures++;

                // Raw Y2 uses round 1 when available, otherwise legacy fields.
                bool rawValid = !string.IsNullOrEmpty(r.ProposalRound1Action)
                    ? r.ProposalRound1Valid
                    : r.IsValid;
                if (!rawValid) rawIllogical++;

                // Executed Y2 uses the final validated action when available.
                bool executedValid = !string.IsNullOrEmpty(r.FinalExecutedAction)
                    ? r.FinalExecutedValid
                    : r.IsValid;
                if (!executedValid) executedIllogical++;

                if (r.RetryConducted)
                {
                    retryCount++;
                    if (r.ProposalRound2Valid && !r.FallbackTriggered) retryResolved++;
                }
                if (r.FallbackTriggered) fallbackCount++;

                long recordTotalLatency = r.LatencyTotalMs > 0 ? r.LatencyTotalMs : r.LatencyMs;
                latencySum += r.LatencyMs;
                totalLatencySum += recordTotalLatency;
                attempt1Sum += r.LatencyLlmAttempt1Ms;
                validatorSum += r.LatencyValidatorMs;
                fallbackLatencySum += r.LatencyFallbackMs;
                inputSum += r.InputTokens;
                outputSum += r.OutputTokens;
                totalSum += r.TotalTokens;

                if (!string.IsNullOrEmpty(r.FinalAction))
                {
                    int count;
                    actionCounts.TryGetValue(r.FinalAction, out count);
                    actionCounts[r.FinalAction] = count + 1;
                }

                if (!string.IsNullOrEmpty(r.ViolationCode) && r.ViolationCode != "NONE")
                {
                    var codes = r.ViolationCode.Split('|');
                    for (int c = 0; c < codes.Length; c++)
                    {
                        string code = codes[c].Trim();
                        if (string.IsNullOrEmpty(code)) continue;
                        int count;
                        violationCounts.TryGetValue(code, out count);
                        violationCounts[code] = count + 1;
                    }
                }
            }

            int n = Math.Max(1, _records.Count);

            sb.AppendLine("  \"metrics\": {");
            sb.AppendLine("    \"total_turns\": " + _records.Count.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"valid_decisions\": " + valid.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"illogical_decisions\": " + illogical.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"illogical_decision_rate_percent_Y2\": " +
                          ((illogical / (double)n) * 100.0).ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"raw_illogical_decisions\": " + rawIllogical.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"raw_illogical_decision_rate_percent_Y2\": " +
                          ((rawIllogical / (double)n) * 100.0).ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"executed_illogical_decisions\": " + executedIllogical.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"executed_illogical_decision_rate_percent_Y2\": " +
                          ((executedIllogical / (double)n) * 100.0).ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"retry_count\": " + retryCount.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"retry_rate_percent\": " +
                          ((retryCount / (double)n) * 100.0).ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"retry_resolved_count\": " + retryResolved.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"fallback_count\": " + fallbackCount.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"fallback_rate_percent\": " +
                          ((fallbackCount / (double)n) * 100.0).ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"avg_latency_ms_Y7\": " +
                          (latencySum / (double)n).ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"avg_latency_total_ms_Y7\": " +
                          (totalLatencySum / (double)n).ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"avg_latency_llm_attempt_1_ms\": " +
                          (attempt1Sum / (double)n).ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"avg_latency_validator_ms\": " +
                          (validatorSum / (double)n).ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"avg_latency_fallback_ms\": " +
                          (fallbackLatencySum / (double)n).ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"avg_input_tokens_Y6\": " +
                          (inputSum / (double)n).ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"avg_output_tokens_Y6\": " +
                          (outputSum / (double)n).ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"avg_total_tokens_Y6\": " +
                          (totalSum / (double)n).ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("    \"transport_failures\": " + transportFailures.ToString(CultureInfo.InvariantCulture) + ",");
            sb.Append("    \"violation_counts\": ");
            AppendCounts(sb, violationCounts);
            sb.AppendLine(",");
            sb.Append("    \"action_counts\": ");
            AppendCounts(sb, actionCounts);
            sb.AppendLine();
            sb.AppendLine("  },");
        }

        private static void AppendCounts(StringBuilder sb, Dictionary<string, int> counts)
        {
            sb.Append('{');
            bool first = true;
            foreach (var pair in counts)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(JsonUtil.Escape(pair.Key)).Append("\":").Append(pair.Value);
            }
            sb.Append('}');
        }

        private string ResolvePath(string path, string defaultFileName)
        {
            string candidate = string.IsNullOrEmpty(path) ? defaultFileName : path;
            if (Path.IsPathRooted(candidate)) return candidate;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, outputDirectory);
            return Path.Combine(directory, candidate);
        }

        private static void EnsureDirectory(string fullPath)
        {
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        private static string Quote(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
