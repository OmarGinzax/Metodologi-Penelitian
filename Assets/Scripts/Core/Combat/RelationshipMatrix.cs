using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPGStrategy.Core.Combat
{
    public enum RelationshipType
    {
        SwornBond,      // +75 s.d. +100 (Keluarga / Sahabat Sejati / Sumpah Setia)
        TrustedComrade, // +30 s.d. +74  (Rekan yang dipercaya)
        Neutral,        // -10 s.d. +29  (Rekan biasa)
        Rival           // -100 s.d. -11 (Rivalitas / Ketidaksukaan)
    }

    [Serializable]
    public class RelationshipEntry
    {
        public string TargetEntityId;
        [Range(-100, 100)]
        public int AffinityScore; // -100 to +100

        public RelationshipEntry() { }
        public RelationshipEntry(string targetId, int affinity)
        {
            TargetEntityId = targetId;
            AffinityScore = Mathf.Clamp(affinity, -100, 100);
        }
    }

    [Serializable]
    public class RelationshipMatrix
    {
        [SerializeField] private List<RelationshipEntry> entries = new List<RelationshipEntry>();

        public IReadOnlyList<RelationshipEntry> Entries => entries;

        public int GetAffinity(string targetEntityId)
        {
            if (string.IsNullOrEmpty(targetEntityId)) return 0;
            var entry = entries.Find(e => e.TargetEntityId == targetEntityId);
            return entry?.AffinityScore ?? 0;
        }

        public void SetAffinity(string targetEntityId, int affinity)
        {
            if (string.IsNullOrEmpty(targetEntityId)) return;
            int clamped = Mathf.Clamp(affinity, -100, 100);
            var entry = entries.Find(e => e.TargetEntityId == targetEntityId);
            if (entry != null)
            {
                entry.AffinityScore = clamped;
            }
            else
            {
                entries.Add(new RelationshipEntry(targetEntityId, clamped));
            }
        }

        public RelationshipType GetRelationshipType(string targetEntityId)
        {
            int aff = GetAffinity(targetEntityId);
            if (aff >= 75) return RelationshipType.SwornBond;
            if (aff >= 30) return RelationshipType.TrustedComrade;
            if (aff >= -10) return RelationshipType.Neutral;
            return RelationshipType.Rival;
        }

        public RelationshipMatrix Clone()
        {
            var clone = new RelationshipMatrix();
            foreach (var e in entries)
            {
                clone.SetAffinity(e.TargetEntityId, e.AffinityScore);
            }
            return clone;
        }
    }
}
