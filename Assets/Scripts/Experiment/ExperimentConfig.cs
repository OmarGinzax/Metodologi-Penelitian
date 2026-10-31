using System;
using System.Collections.Generic;
using RPGStrategy.AI.Compression;
using RPGStrategy.Core.Command;

namespace RPGStrategy.Experiment
{
    /// <summary>
    /// M7 – Experiment Configuration.
    /// Defines all variables (X1, X2, X3) and their combinations for the batch runner.
    /// X1: Agent Controller Type (Pure LLM, Hybrid Guardrail, HFSM Baseline)
    /// X2: Context Richness (Minimal, Moderate, Full)
    /// X3: Context Compression (None, Mild, Moderate, Aggressive)
    /// </summary>
    [Serializable]
    public class ExperimentConfig
    {
        public string ExperimentId;
        public string ScenarioId;
        public string AgentControllerType;       // "PureLLM" | "Hybrid" | "HFSM"
        public string ContextRichness;           // "Minimal" | "Moderate" | "Full"
        public PromptCompressor.CompressionLevel CompressionLevel;
        public string LLMModel;                  // e.g., "gpt-4o-mini", "gpt-4o"
        public int RepetitionIndex;              // 1..N (N=5 per permutation)
        public int MaxTurns;

        public override string ToString()
        {
            return $"{ExperimentId}|{ScenarioId}|{AgentControllerType}|{ContextRichness}|{CompressionLevel}|{LLMModel}|Rep{RepetitionIndex}";
        }
    }

    /// <summary>
    /// Telemetry data recorded per single turn decision.
    /// </summary>
    [Serializable]
    public class TurnTelemetry
    {
        public string ExperimentId;
        public string ScenarioId;
        public string AgentControllerType;
        public string ContextRichness;
        public string CompressionLevel;
        public string LLMModel;
        public int RepetitionIndex;

        // Per-turn data
        public int TurnNumber;
        public string EntityId;
        public string EntityName;
        public string TraitId;
        public string ActionId;
        public string ActionType;
        public string Compliance;            // OBEY / ADAPT / DEFY
        public bool IsValid;                 // Y2: was action mechanically valid?
        public string HallucinationType;     // H1–H6 or None
        public bool FallbackActivated;       // Y2: guardrail fallback used?
        public bool RetryAttempted;          // Y2: retry was needed?
        public float TraitConsistencyScore;  // Y3: 0–1
        public long LatencyMs;              // Y7
        public int InputTokens;             // Y6
        public int OutputTokens;            // Y6
        public int TotalTokens;             // Y6
        public string RadioChatter;
        public string Reasoning;
        public float ActorHPPercent;
        public int ActorRationality;
        public string TimestampUtc;
    }

    /// <summary>
    /// Summary aggregated per experiment run (single ExperimentConfig permutation).
    /// </summary>
    [Serializable]
    public class ExperimentSummary
    {
        public string ExperimentId;
        public string ScenarioId;
        public string AgentControllerType;
        public string ContextRichness;
        public string CompressionLevel;
        public string LLMModel;
        public int RepetitionIndex;
        public string BattleOutcome;         // "Victory" | "Defeat" | "Draw" | "Timeout"
        public int TotalTurns;

        // Y1: Action Diversity (Shannon Entropy over action_id distribution)
        public float ShannonEntropy;
        public Dictionary<string, int> ActionFrequencyMap;

        // Y2: Illogical / Invalid Action Rate
        public int TotalInvalidActions;
        public int TotalFallbackActivations;
        public float InvalidActionRate;

        // Y3: Trait Consistency
        public float MeanTraitConsistency;

        // Y4: Decision Coherence (Unjustified Reversal Rate)
        public float UnjustifiedReversalRate;

        // Y5: Human Blinded Score placeholder (filled by external raters)
        public float HumanPerceivedRealism; // 0–10; -1 = not yet rated

        // Y6: Token Efficiency
        public long TotalInputTokens;
        public long TotalOutputTokens;
        public float MeanTokensPerTurn;
        public float CompressionRatioAchieved;

        // Y7: Latency
        public long TotalLatencyMs;
        public float MeanLatencyMs;
        public long MaxLatencyMs;

        public string CompletedAtUtc;
    }
}
