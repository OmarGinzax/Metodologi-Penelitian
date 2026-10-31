using System;
using UnityEngine;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.AI.Traits
{
    [Serializable]
    public class TraitShiftResult
    {
        public bool ShiftOccurred;
        public string EntityId;
        public string PreviousTraitId;
        public string NewTraitId;
        public string Reason;
        public string RadioMonologue;

        public TraitShiftResult()
        {
            ShiftOccurred = false;
        }

        public static TraitShiftResult CreateShift(string id, string prevTrait, string newTrait, string reason, string monologue)
        {
            return new TraitShiftResult
            {
                ShiftOccurred = true,
                EntityId = id,
                PreviousTraitId = prevTrait,
                NewTraitId = newTrait,
                Reason = reason,
                RadioMonologue = monologue
            };
        }
    }

    public class PersonalityEvolutionEngine
    {
        public TraitShiftResult EvaluateTraumaOnAllyDefeat(CombatEntity observer, CombatEntity deadAlly)
        {
            if (observer == null || deadAlly == null || !observer.IsAlive)
            {
                return new TraitShiftResult();
            }

            int bond = observer.Relationships.GetAffinity(deadAlly.EntityId);
            string currentTrait = observer.CurrentTraitId;

            // Kasus 1: Kematian Rekan Sworn Bond (+75 s.d. +100)
            if (bond >= 75)
            {
                if (currentTrait == "LoyalProtector" || currentTrait == "SteadfastGuardian")
                {
                    string newTrait = "VengefulFanatic";
                    string monologue = $"[RADIO TRANSMISSION: {observer.EntityName}] \"NO!! {deadAlly.EntityName.ToUpper()}! I swore I would protect you! Commander, forget your orders... I'm tearing that machine apart with my bare hands!\"";
                    observer.SetTrait(newTrait);
                    return TraitShiftResult.CreateShift(observer.EntityId, currentTrait, newTrait, $"Sworn comrade {deadAlly.EntityName} killed in action.", monologue);
                }
                else if (currentTrait == "TimidAltruist")
                {
                    string newTrait = "ShellshockedErratic";
                    string monologue = $"[RADIO TRANSMISSION: {observer.EntityName}] \"They're dead... they're all dead! We can't stop this! Someone get us out of here!\"";
                    observer.SetTrait(newTrait);
                    return TraitShiftResult.CreateShift(observer.EntityId, currentTrait, newTrait, $"Severe psychological shock from {deadAlly.EntityName}'s death.", monologue);
                }
            }

            return new TraitShiftResult();
        }

        public TraitShiftResult EvaluateNearDeathSurvival(CombatEntity entity)
        {
            if (entity == null || !entity.IsAlive) return new TraitShiftResult();

            string currentTrait = entity.CurrentTraitId;
            // Jika agen agresif nyaris tewas (HP <= 5% dari Max HP)
            if (currentTrait == "AggressiveBerserker" && entity.Stats.HPRatio <= 0.05f)
            {
                string newTrait = "HumbledVeteran";
                string monologue = $"[RADIO TRANSMISSION: {entity.EntityName}] \"Damn it... that blast nearly took my head clean off. I hear you loud and clear now, team. I'm falling in line.\"";
                entity.SetTrait(newTrait);
                return TraitShiftResult.CreateShift(entity.EntityId, currentTrait, newTrait, "Surviving on the absolute edge of death humbled reckless aggression.", monologue);
            }

            return new TraitShiftResult();
        }

        public TraitShiftResult EvaluateMiracleRescue(CombatEntity medic, CombatEntity savedPatient)
        {
            if (medic == null || !medic.IsAlive) return new TraitShiftResult();

            string currentTrait = medic.CurrentTraitId;
            if (currentTrait == "TimidAltruist")
            {
                string newTrait = "ValiantMedic";
                string monologue = $"[RADIO TRANSMISSION: {medic.EntityName}] \"I did it... {savedPatient.EntityName} is stable! Stop shouting over the comms, Command, I know what I'm doing now!\"";
                medic.SetTrait(newTrait);
                medic.Stats.Rationality = Mathf.Max(medic.Stats.Rationality, 85);
                return TraitShiftResult.CreateShift(medic.EntityId, currentTrait, newTrait, $"Successfully dragged and saved {savedPatient.EntityName} under heavy fire.", monologue);
            }

            return new TraitShiftResult();
        }
    }
}
