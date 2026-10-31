using System;
using System.Collections.Generic;
using UnityEngine;
using RPGStrategy.Core.Command;

namespace RPGStrategy.Core.Data
{
    [Serializable]
    public class ActorSnapshot
    {
        public string EntityId;
        public string Name;
        public string Team; // "PlayerTeam" or "EnemyTeam"
        public int CurrentHP;
        public int MaxHP;
        public float HPPercentage;
        public int CurrentMP;
        public int MaxMP;
        public float MPPercentage;
        public int AttackPower;
        public int DefensePower;
        public int Speed;
        public int Rationality;         // 0 s.d. 100
        public string ComposureLevel;   // "Composed", "Stressed", "Shaken", "Panicked"
        public string CurrentTraitId;   // misal "LoyalProtector", "VengefulFanatic"
        public float FieldDistance;     // Posisi jarak (meter) dari titik jemput evakuasi
        public bool IsAlive;
        public bool IsDefending;
        public bool IsSuppressed;
        public bool IsOverwatchActive;
        public List<string> ActiveStatusEffects = new List<string>();
        public string ThreatRank;       // "Critical", "High", "Moderate", "Low"
    }

    [Serializable]
    public class RelationshipDTO
    {
        public string TargetEntityId;
        public string TargetName;
        public int AffinityScore;
        public string RelationshipType; // "SwornBond", "TrustedComrade", "Neutral", "Rival"
    }

    [Serializable]
    public class AvailableActionDTO
    {
        public string ActionId;
        public string ActionName;
        public string ActionType; // e.g. "BasicAttack", "SuppressionFire", "CarryWounded", "PleaForMercy"
        public int MPCost;
        public int Quantity;
        public string Scope;      // e.g. "SingleEnemy", "SingleAlly", "Self", "AllAllies"
        public string Description;
    }

    [Serializable]
    public class CombatStateSnapshot
    {
        public int TurnNumber;
        public CommanderDirectiveDTO ActiveDirective;
        public ActorSnapshot CurrentActor;
        public List<ActorSnapshot> Allies = new List<ActorSnapshot>();
        public List<ActorSnapshot> Enemies = new List<ActorSnapshot>();
        public List<RelationshipDTO> Relationships = new List<RelationshipDTO>();
        public List<AvailableActionDTO> AvailableActions = new List<AvailableActionDTO>();
        public List<string> RecentEvents = new List<string>();

        public ActorSnapshot FindActor(string entityId)
        {
            if (CurrentActor != null && CurrentActor.EntityId == entityId)
                return CurrentActor;

            if (Allies != null)
            {
                foreach (var ally in Allies)
                {
                    if (ally.EntityId == entityId)
                        return ally;
                }
            }

            if (Enemies != null)
            {
                foreach (var enemy in Enemies)
                {
                    if (enemy.EntityId == entityId)
                        return enemy;
                }
            }

            return null;
        }

        public bool IsActionAvailable(string actionId)
        {
            if (AvailableActions == null) return false;
            foreach (var act in AvailableActions)
            {
                if (act.ActionId == actionId)
                    return true;
            }
            return false;
        }

        public AvailableActionDTO GetAction(string actionId)
        {
            if (AvailableActions == null) return null;
            foreach (var act in AvailableActions)
            {
                if (act.ActionId == actionId)
                    return act;
            }
            return null;
        }

        public string ToJson(bool prettyPrint = false)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }
    }
}
