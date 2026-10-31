using System;
using System.Collections.Generic;
using RPGStrategy.Core.Data;

namespace RPGStrategy.AI.Traits
{
    /// <summary>
    /// M6 – Decision Coherence Evaluator.
    /// Mengukur Y3 (Trait Consistency) dan Y4 (Decision Coherence / Unjustified Reversal Rate).
    /// Tracks decision history per agent to detect mid-combat unexplained personality reversals.
    /// </summary>
    public class DecisionCoherenceEvaluator
    {
        [Serializable]
        public class DecisionRecord
        {
            public int TurnNumber;
            public string TraitId;
            public string ActionId;
            public string ActionType;
            public string Compliance;
            public int Rationality;
            public float TraitConsistencyScore; // 0–1
            public string Reasoning;
        }

        [Serializable]
        public class CoherenceReport
        {
            public string EntityId;
            public int TotalDecisions;
            /// <summary>Y3: Mean trait consistency across all turns (0–1 scale).</summary>
            public float MeanTraitConsistency;
            /// <summary>Y4: Rate of unjustified reversals (abrupt action-type switches without rationality change).</summary>
            public float UnjustifiedReversalRate;
            public List<int> ReversalTurns = new List<int>();
        }

        // Per-agent decision history
        private readonly Dictionary<string, List<DecisionRecord>> _history = new Dictionary<string, List<DecisionRecord>>();

        // Threshold: a compliance or action-type flip within this rationality delta is flagged as unjustified
        private const float ReversalRationalityDeltaThreshold = 15f;
        private const float ReversalConsistencyDropThreshold = 0.35f;

        /// <summary>
        /// Records a decision after execution. Call once per agent per turn.
        /// </summary>
        public DecisionRecord Record(string entityId, int turn, AgentDecision decision, CombatStateSnapshot snapshot)
        {
            if (!_history.ContainsKey(entityId))
                _history[entityId] = new List<DecisionRecord>();

            var actor = snapshot?.CurrentActor;
            string actionType = LookupActionType(snapshot, decision.ActionId);

            float consistencyScore = FrozenTraitMapping.ScoreActionConsistency(
                actor?.CurrentTraitId ?? "Unknown",
                actionType,
                decision.Compliance ?? "OBEY"
            );

            var record = new DecisionRecord
            {
                TurnNumber = turn,
                TraitId = actor?.CurrentTraitId ?? "Unknown",
                ActionId = decision.ActionId,
                ActionType = actionType,
                Compliance = decision.Compliance ?? "OBEY",
                Rationality = actor?.Rationality ?? 50,
                TraitConsistencyScore = consistencyScore,
                Reasoning = decision.Reasoning ?? ""
            };

            _history[entityId].Add(record);
            return record;
        }

        /// <summary>
        /// Generates a full Y3 + Y4 coherence report for an entity.
        /// </summary>
        public CoherenceReport GenerateReport(string entityId)
        {
            var report = new CoherenceReport { EntityId = entityId };

            if (!_history.TryGetValue(entityId, out var records) || records.Count == 0)
                return report;

            report.TotalDecisions = records.Count;

            float totalConsistency = 0f;
            int reversals = 0;

            for (int i = 0; i < records.Count; i++)
            {
                totalConsistency += records[i].TraitConsistencyScore;

                if (i > 0)
                {
                    var prev = records[i - 1];
                    var curr = records[i];

                    // Y4: Unjustified reversal = sharp action-type change + negligible rationality change
                    float rationalityDelta = Math.Abs(curr.Rationality - prev.Rationality);
                    float consistencyDrop = prev.TraitConsistencyScore - curr.TraitConsistencyScore;
                    bool actionTypeSwitched = prev.ActionType != curr.ActionType &&
                        (prev.ActionType == "BasicAttack" || prev.ActionType == "Action_Defend") != 
                        (curr.ActionType == "BasicAttack" || curr.ActionType == "Action_Defend");

                    if (actionTypeSwitched && rationalityDelta < ReversalRationalityDeltaThreshold && 
                        consistencyDrop > ReversalConsistencyDropThreshold)
                    {
                        reversals++;
                        report.ReversalTurns.Add(curr.TurnNumber);
                    }
                }
            }

            report.MeanTraitConsistency = totalConsistency / records.Count;
            report.UnjustifiedReversalRate = records.Count > 1 ? (float)reversals / (records.Count - 1) : 0f;
            return report;
        }

        public List<DecisionRecord> GetHistory(string entityId)
        {
            _history.TryGetValue(entityId, out var list);
            return list ?? new List<DecisionRecord>();
        }

        public void Reset() => _history.Clear();

        private static string LookupActionType(CombatStateSnapshot snapshot, string actionId)
        {
            if (snapshot == null || string.IsNullOrEmpty(actionId)) return "Unknown";
            var dto = snapshot.GetAction(actionId);
            return dto?.ActionType ?? actionId;
        }
    }
}
