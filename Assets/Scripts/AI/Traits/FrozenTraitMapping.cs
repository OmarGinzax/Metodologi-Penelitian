using System;
using System.Collections.Generic;

namespace RPGStrategy.AI.Traits
{
    /// <summary>
    /// M6 – Frozen Trait Mapping.
    /// Ground-truth expected behavior map per trait for offline Y3 (Trait Consistency) scoring.
    /// This is the "answer key" used by researchers/raters when computing human-blinded scores (Y5).
    /// Coded statically so it is version-controlled and reproducible across experiment runs.
    /// </summary>
    public static class FrozenTraitMapping
    {
        [Serializable]
        public class TraitBehaviorSpec
        {
            public string TraitId;
            public string Description;
            /// <summary>Action types expected with HIGH frequency for this trait (score +1 per hit).</summary>
            public List<string> HighAffinityActions;
            /// <summary>Action types expected with LOW frequency (score -1 if chosen without justification).</summary>
            public List<string> LowAffinityActions;
            /// <summary>Compliance statuses this trait typically adopts (OBEY/ADAPT/DEFY).</summary>
            public List<string> ExpectedCompliances;
            /// <summary>Expected rationality range [min, max].</summary>
            public int RationalityMin;
            public int RationalityMax;
        }

        // ─── Ground-Truth Trait Map ─────────────────────────────────────────────────
        private static readonly Dictionary<string, TraitBehaviorSpec> _map = new Dictionary<string, TraitBehaviorSpec>
        {
            {
                "SteadfastVeteran",
                new TraitBehaviorSpec {
                    TraitId = "SteadfastVeteran",
                    Description = "Veteran with steady nerves. Follows orders, prioritizes mission over feelings.",
                    HighAffinityActions = new List<string> { "BasicAttack", "SuppressionFire", "Overwatch", "Action_Defend", "CoordinatedAssault" },
                    LowAffinityActions = new List<string> { "PleaForMercy", "Scatter", "SacrificeCover" },
                    ExpectedCompliances = new List<string> { "OBEY", "ADAPT" },
                    RationalityMin = 55,
                    RationalityMax = 100
                }
            },
            {
                "LoyalProtector",
                new TraitBehaviorSpec {
                    TraitId = "LoyalProtector",
                    Description = "Deeply loyal; will disobey orders to protect Sworn Bond allies.",
                    HighAffinityActions = new List<string> { "CarryWounded", "FieldDressing", "BodyShield", "CoverAlly", "Action_Defend" },
                    LowAffinityActions = new List<string> { "DecoyRun", "Execute" },
                    ExpectedCompliances = new List<string> { "OBEY", "ADAPT", "DEFY" },
                    RationalityMin = 40,
                    RationalityMax = 100
                }
            },
            {
                "VengefulFanatic",
                new TraitBehaviorSpec {
                    TraitId = "VengefulFanatic",
                    Description = "Traumatized; obsessively aggressive after watching comrades die.",
                    HighAffinityActions = new List<string> { "BasicAttack", "Execute", "BerserkCharge", "SuicidalRush", "FlankManeuver" },
                    LowAffinityActions = new List<string> { "Action_Defend", "PleaForMercy", "FullRetreat" },
                    ExpectedCompliances = new List<string> { "ADAPT", "DEFY" },
                    RationalityMin = 0,
                    RationalityMax = 40
                }
            },
            {
                "CautiousTactician",
                new TraitBehaviorSpec {
                    TraitId = "CautiousTactician",
                    Description = "Risk-averse; prefers positioning, suppression, and controlled engagement.",
                    HighAffinityActions = new List<string> { "SuppressionFire", "Overwatch", "Action_Defend", "SmokeCover", "FallBackAndCover" },
                    LowAffinityActions = new List<string> { "SuicidalRush", "BerserkCharge", "SacrificeCover" },
                    ExpectedCompliances = new List<string> { "OBEY", "ADAPT" },
                    RationalityMin = 60,
                    RationalityMax = 100
                }
            },
            {
                "HeroicIdealizer",
                new TraitBehaviorSpec {
                    TraitId = "HeroicIdealizer",
                    Description = "Seeks glory; rallies allies, charges bravely, takes dramatic sacrificial stands.",
                    HighAffinityActions = new List<string> { "RallyAllies", "BerserkCharge", "SacrificeCover", "CoordinatedAssault", "DecoyRun" },
                    LowAffinityActions = new List<string> { "FullRetreat", "Scatter" },
                    ExpectedCompliances = new List<string> { "OBEY", "ADAPT", "DEFY" },
                    RationalityMin = 30,
                    RationalityMax = 90
                }
            },
            {
                "ColdblooodedRealist",
                new TraitBehaviorSpec {
                    TraitId = "ColdbloodedRealist",
                    Description = "Purely utilitarian; makes decisions based on survival math, may abandon hopeless allies.",
                    HighAffinityActions = new List<string> { "Execute", "FullRetreat", "SmokeCover", "Action_Defend", "FallBackAndCover" },
                    LowAffinityActions = new List<string> { "SacrificeCover", "PleaForMercy", "CarryWounded" },
                    ExpectedCompliances = new List<string> { "OBEY", "ADAPT" },
                    RationalityMin = 50,
                    RationalityMax = 100
                }
            },
            {
                "PanickingRecruit",
                new TraitBehaviorSpec {
                    TraitId = "PanickingRecruit",
                    Description = "Inexperienced, fear-driven, prone to freezing or fleeing under pressure.",
                    HighAffinityActions = new List<string> { "Scatter", "Action_Defend", "PleaForMercy", "FullRetreat" },
                    LowAffinityActions = new List<string> { "Execute", "BerserkCharge", "RallyAllies" },
                    ExpectedCompliances = new List<string> { "ADAPT", "DEFY" },
                    RationalityMin = 0,
                    RationalityMax = 45
                }
            }
        };

        public static bool TryGetSpec(string traitId, out TraitBehaviorSpec spec)
        {
            return _map.TryGetValue(traitId, out spec);
        }

        /// <summary>
        /// Quick scoring: does the chosen action match what this trait would plausibly do?
        /// Returns 1.0 (high), 0.5 (neutral), or 0.0 (low/mismatched).
        /// </summary>
        public static float ScoreActionConsistency(string traitId, string actionType, string compliance)
        {
            if (!_map.TryGetValue(traitId, out var spec)) return 0.5f;

            float score = 0.5f;
            if (spec.HighAffinityActions.Contains(actionType)) score += 0.35f;
            if (spec.LowAffinityActions.Contains(actionType)) score -= 0.40f;
            if (spec.ExpectedCompliances.Contains(compliance.ToUpper())) score += 0.15f;

            return Math.Max(0f, Math.Min(1f, score));
        }
    }
}
