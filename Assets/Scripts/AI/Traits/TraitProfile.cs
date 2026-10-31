using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPGStrategy.AI.Traits
{
    /// <summary>
    /// M6 – Trait Profile ScriptableObject.
    /// Mendefinisikan deskripsi naratif, vektor perilaku, dan threshold evolusi untuk
    /// setiap personality trait. Digunakan LLM sebagai grounding untuk koherensi karakter (Y3 & Y4).
    /// </summary>
    [CreateAssetMenu(fileName = "TraitProfile", menuName = "RPGStrategy/Trait Profile")]
    public class TraitProfile : ScriptableObject
    {
        [Header("Identification")]
        public string TraitId;
        public string DisplayName;

        [Header("Narrative Description (for LLM System Prompt)")]
        [TextArea(4, 8)]
        public string NarrativeDescription;

        [Header("Behavioral Vectors (0-100 scale)")]
        [Range(0, 100)] public int AggressionBias;       // Tendency to attack vs defend
        [Range(0, 100)] public int LoyaltyBias;          // Tendency to protect allies
        [Range(0, 100)] public int SelfPreservation;     // Tendency to retreat or take cover
        [Range(0, 100)] public int RiskTolerance;        // Willingness to take dangerous actions
        [Range(0, 100)] public int CommandCompliance;    // Base tendency to obey Commander

        [Header("Expected Action Tendencies")]
        [Tooltip("Action types this trait is likely to choose. Used for Y3 consistency scoring.")]
        public List<string> HighAffinityActionTypes = new List<string>();

        [Tooltip("Action types this trait rarely or never takes unless forced.")]
        public List<string> LowAffinityActionTypes = new List<string>();

        [Header("Trait Evolution Triggers")]
        [Tooltip("Trait IDs this can evolve INTO after a Triumph event.")]
        public List<string> TriumphEvolutionTargets = new List<string>();

        [Tooltip("Trait IDs this can evolve INTO after a Trauma event.")]
        public List<string> TraumaEvolutionTargets = new List<string>();

        [Header("Ground Truth Data (for Y3 Measurement)")]
        [Tooltip("Rationality range where this trait is 'in-character'. LLM deviation below this is flagged.")]
        [Range(0, 100)] public int ExpectedRationalityMin = 20;
        [Range(0, 100)] public int ExpectedRationalityMax = 100;

        /// <summary>
        /// Checks if an action type aligns well with this trait's expected behavior.
        /// Used by DecisionCoherenceEvaluator for Y3 and Y4 scoring.
        /// </summary>
        public float GetActionAffinityScore(string actionType)
        {
            if (HighAffinityActionTypes.Contains(actionType)) return 1.0f;
            if (LowAffinityActionTypes.Contains(actionType)) return 0.1f;
            return 0.5f; // Neutral
        }

        /// <summary>
        /// Checks if a rationality value is within expected range for this trait.
        /// </summary>
        public bool IsRationalityInRange(int rationality)
        {
            return rationality >= ExpectedRationalityMin && rationality <= ExpectedRationalityMax;
        }
    }

    /// <summary>
    /// Runtime container holding all registered trait profiles.
    /// Loaded once at game start; queried by evaluators.
    /// </summary>
    [CreateAssetMenu(fileName = "TraitRegistry", menuName = "RPGStrategy/Trait Registry")]
    public class TraitRegistry : ScriptableObject
    {
        public List<TraitProfile> AllTraits = new List<TraitProfile>();

        private Dictionary<string, TraitProfile> _lookup;

        public void Initialize()
        {
            _lookup = new Dictionary<string, TraitProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in AllTraits)
            {
                if (t != null && !string.IsNullOrEmpty(t.TraitId))
                    _lookup[t.TraitId] = t;
            }
        }

        public TraitProfile GetById(string traitId)
        {
            if (_lookup == null) Initialize();
            _lookup.TryGetValue(traitId, out var profile);
            return profile;
        }

        public bool TryGet(string traitId, out TraitProfile profile)
        {
            if (_lookup == null) Initialize();
            return _lookup.TryGetValue(traitId, out profile);
        }
    }
}
