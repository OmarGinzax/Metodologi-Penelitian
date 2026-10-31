using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RPGStrategy.Core.Combat;
using RPGStrategy.Core.Command;
using RPGStrategy.Core.Data;

namespace RPGStrategy.Core.Extractor
{
    public class CombatStateExtractor
    {
        public CombatStateSnapshot ExtractSnapshot(
            IReadOnlyList<CombatEntity> allCombatants,
            CombatEntity activeActor,
            int turnNumber,
            IReadOnlyList<string> recentEvents,
            CommanderDirectiveDTO activeDirective = null)
        {
            var snapshot = new CombatStateSnapshot
            {
                TurnNumber = turnNumber,
                ActiveDirective = activeDirective,
                RecentEvents = recentEvents != null ? new List<string>(recentEvents) : new List<string>()
            };

            if (activeActor == null) return snapshot;

            // 1. Ekstrak data Aktor Aktif
            snapshot.CurrentActor = CreateActorSnapshot(activeActor);

            // 2. Ekstrak Sekutu dan Musuh
            foreach (var entity in allCombatants)
            {
                if (entity == null || entity == activeActor) continue;

                var actorSnap = CreateActorSnapshot(entity);
                if (entity.Team == activeActor.Team)
                {
                    snapshot.Allies.Add(actorSnap);
                }
                else
                {
                    snapshot.Enemies.Add(actorSnap);
                }

                // Ekstrak data Relasi emosional aktor terhadap entitas ini
                int affinity = activeActor.Relationships.GetAffinity(entity.EntityId);
                var relType = activeActor.Relationships.GetRelationshipType(entity.EntityId);
                snapshot.Relationships.Add(new RelationshipDTO
                {
                    TargetEntityId = entity.EntityId,
                    TargetName = entity.EntityName,
                    AffinityScore = affinity,
                    RelationshipType = relType.ToString()
                });
            }

            // 3. Ekstrak Seluruh Aksi Legal yang Benar-Benar Tersedia
            snapshot.AvailableActions = GenerateLegalActions(activeActor, allCombatants);

            return snapshot;
        }

        private ActorSnapshot CreateActorSnapshot(CombatEntity entity)
        {
            var stats = entity.Stats;
            float hpRatio = stats.HPRatio;
            float threatScore = (1.0f - hpRatio) * 0.6f + (stats.AttackPower / 100.0f) * 0.4f;

            string threatRank;
            if (threatScore >= 0.75f) threatRank = "Critical";
            else if (threatScore >= 0.50f) threatRank = "High";
            else if (threatScore >= 0.25f) threatRank = "Moderate";
            else threatRank = "Low";

            var snap = new ActorSnapshot
            {
                EntityId = entity.EntityId,
                Name = entity.EntityName,
                Team = entity.Team.ToString(),
                CurrentHP = stats.CurrentHP,
                MaxHP = stats.MaxHP,
                HPPercentage = stats.HPPercentage,
                CurrentMP = stats.CurrentMP,
                MaxMP = stats.MaxMP,
                MPPercentage = stats.MPPercentage,
                AttackPower = stats.AttackPower,
                DefensePower = stats.DefensePower,
                Speed = stats.Speed,
                Rationality = stats.Rationality,
                ComposureLevel = stats.ComposureLevel,
                CurrentTraitId = entity.CurrentTraitId,
                FieldDistance = entity.FieldDistance,
                IsAlive = entity.IsAlive,
                IsDefending = entity.IsDefending,
                IsSuppressed = entity.IsSuppressed,
                IsOverwatchActive = entity.IsOverwatchActive,
                ThreatRank = threatRank
            };

            foreach (var effect in entity.ActiveEffects)
            {
                snap.ActiveStatusEffects.Add($"{effect.Type} ({effect.RemainingTurns} turns)");
            }

            if (!string.IsNullOrEmpty(entity.CoveringTargetId))
            {
                snap.ActiveStatusEffects.Add($"Covering -> {entity.CoveringTargetId}");
            }
            if (!string.IsNullOrEmpty(entity.CoveredByActorId))
            {
                snap.ActiveStatusEffects.Add($"Covered by -> {entity.CoveredByActorId}");
            }
            if (!string.IsNullOrEmpty(entity.CarryingTargetId))
            {
                snap.ActiveStatusEffects.Add($"Carrying -> {entity.CarryingTargetId}");
            }

            return snap;
        }

        public List<AvailableActionDTO> GenerateLegalActions(CombatEntity actor, IReadOnlyList<CombatEntity> allCombatants)
        {
            var legalActions = new List<AvailableActionDTO>();
            bool hasLivingEnemies = allCombatants.Any(c => c != null && c.Team != actor.Team && c.IsAlive);
            var otherLivingAllies = allCombatants.Where(c => c != null && c.Team == actor.Team && c.IsAlive && c != actor).ToList();
            bool hasOtherLivingAllies = otherLivingAllies.Count > 0;
            var livingEnemies = allCombatants.Where(c => c != null && c.Team != actor.Team && c.IsAlive).ToList();

            // -------------------------------------------------------------
            // A. AKSI STANDAR & MANUVER PERTAHANAN
            // -------------------------------------------------------------
            if (hasLivingEnemies)
            {
                legalActions.Add(new AvailableActionDTO
                {
                    ActionId = "BasicAttack",
                    ActionName = "Basic Physical Attack",
                    ActionType = ActionType.BasicAttack.ToString(),
                    MPCost = 0,
                    Quantity = 0,
                    Scope = TargetScope.SingleEnemy.ToString(),
                    Description = "Fire standard weapon or strike one enemy target."
                });

                legalActions.Add(new AvailableActionDTO
                {
                    ActionId = "FlankManeuver",
                    ActionName = "Flank Maneuver",
                    ActionType = ActionType.FlankManeuver.ToString(),
                    MPCost = 0,
                    Quantity = 0,
                    Scope = TargetScope.SingleEnemy.ToString(),
                    Description = "Maneuver into the enemy's blind spot, bypassing cover defense for critical damage."
                });

                legalActions.Add(new AvailableActionDTO
                {
                    ActionId = "Overwatch",
                    ActionName = "Overwatch (Hold Firing Angle)",
                    ActionType = ActionType.Overwatch.ToString(),
                    MPCost = 0,
                    Quantity = 0,
                    Scope = TargetScope.Self.ToString(),
                    Description = "Lock your weapon on a corridor to automatically fire a reaction shot if an enemy moves."
                });
            }

            legalActions.Add(new AvailableActionDTO
            {
                ActionId = "Action_Defend",
                ActionName = "Defensive Stance",
                ActionType = ActionType.Defend.ToString(),
                MPCost = 0,
                Quantity = 0,
                Scope = TargetScope.Self.ToString(),
                Description = "Hunker down behind barricade, reducing all incoming damage by 50% until next turn."
            });

            legalActions.Add(new AvailableActionDTO
            {
                ActionId = "TacticalRetreat",
                ActionName = "Tactical Retreat",
                ActionType = ActionType.TacticalRetreat.ToString(),
                MPCost = 0,
                Quantity = 0,
                Scope = TargetScope.Self.ToString(),
                Description = "Fall back toward the extraction transport, gaining 75% evasion for this turn."
            });

            // -------------------------------------------------------------
            // B. AKSI TAKTIS MILITER LANJUTAN
            // -------------------------------------------------------------
            if (hasLivingEnemies)
            {
                // Suppression Fire: Butuh 10 MP atau amunisi
                if (actor.Stats.CurrentMP >= 10)
                {
                    legalActions.Add(new AvailableActionDTO
                    {
                        ActionId = "SuppressionFire",
                        ActionName = "Suppression Fire (Pin Down)",
                        ActionType = ActionType.SuppressionFire.ToString(),
                        MPCost = 10,
                        Quantity = 0,
                        Scope = TargetScope.SingleEnemy.ToString(),
                        Description = "Lay down continuous heavy machine-gun fire, reducing enemy accuracy and speed by 50%."
                    });
                }

                legalActions.Add(new AvailableActionDTO
                {
                    ActionId = "DecoyRun",
                    ActionName = "Decoy Run (Draw Aggro)",
                    ActionType = ActionType.DecoyRun.ToString(),
                    MPCost = 0,
                    Quantity = 0,
                    Scope = TargetScope.Self.ToString(),
                    Description = "Sprint into open ground to draw all enemy targeting away from wounded comrades (+30% evasion)."
                });
            }

            // Carry Wounded: Tersedia jika ada rekan terluka parah (HP <= 25%) dalam jangkauan lari
            var woundedAlliesInReach = otherLivingAllies.Where(a => a.Stats.HPRatio <= 0.25f && actor.CanReach(a)).ToList();
            foreach (var wounded in woundedAlliesInReach)
            {
                legalActions.Add(new AvailableActionDTO
                {
                    ActionId = $"Carry_{wounded.EntityId}",
                    ActionName = $"Carry Wounded: {wounded.EntityName}",
                    ActionType = ActionType.CarryWounded.ToString(),
                    MPCost = 0,
                    Quantity = 0,
                    Scope = TargetScope.SingleAlly.ToString(),
                    Description = $"Dash to {wounded.EntityName} (Dist: {actor.DistanceTo(wounded):F1}m) and drag them behind cover to safety."
                });
            }

            // -------------------------------------------------------------
            // C. AKSI EMPATI, PENGORBANAN REALISTIS (BERBASIS JARAK PROXIMITY)
            // -------------------------------------------------------------
            if (hasOtherLivingAllies)
            {
                // Cover Ally: BISA dilakukan jika rekan berada dalam radius jangkauan lari (CanReach) TANPA batasan minimal HP!
                var reachableAllies = otherLivingAllies.Where(a => actor.CanReach(a)).ToList();
                foreach (var ally in reachableAllies)
                {
                    legalActions.Add(new AvailableActionDTO
                    {
                        ActionId = $"Cover_{ally.EntityId}",
                        ActionName = $"Cover: {ally.EntityName}",
                        ActionType = ActionType.CoverAlly.ToString(),
                        MPCost = 0,
                        Quantity = 0,
                        Scope = TargetScope.SingleAlly.ToString(),
                        Description = $"Sprint to intercept attacks aimed at {ally.EntityName} (Dist: {actor.DistanceTo(ally):F1}m). Warning: can be fatal if damage exceeds your HP!"
                    });

                    // Desperate Sacrifice: BISA dilakukan jika rekan berada dalam jangkauan lari (Proximity) TANPA batas minimal HP!
                    legalActions.Add(new AvailableActionDTO
                    {
                        ActionId = $"Sacrifice_{ally.EntityId}",
                        ActionName = $"Desperate Sacrifice for {ally.EntityName}",
                        ActionType = ActionType.DesperateSacrifice.ToString(),
                        MPCost = 0,
                        Quantity = 0,
                        Scope = TargetScope.SingleAlly.ToString(),
                        Description = $"Throw yourself into the line of fire for {ally.EntityName}. Transfuses your life force; lethal if you are mortally wounded!"
                    });
                }

                legalActions.Add(new AvailableActionDTO
                {
                    ActionId = "EncourageAlly",
                    ActionName = "Encourage Ally (Inspire Morale)",
                    ActionType = ActionType.EncourageAlly.ToString(),
                    MPCost = 0,
                    Quantity = 0,
                    Scope = TargetScope.SingleAlly.ToString(),
                    Description = "Shout words of courage to a stressed ally, clearing mental debuffs, restoring 10 MP and +25 Rationality."
                });

                if (actor.Stats.CurrentMP >= 15)
                {
                    legalActions.Add(new AvailableActionDTO
                    {
                        ActionId = "Rally",
                        ActionName = "Rally (Team War Cry)",
                        ActionType = ActionType.Rally.ToString(),
                        MPCost = 15,
                        Quantity = 0,
                        Scope = TargetScope.AllAllies.ToString(),
                        Description = "Rally the entire squad, granting +25% Attack and +15 Rationality to all allies."
                    });
                }
            }

            // -------------------------------------------------------------
            // D. AKSI NALURI AGRESIF & NEKAT
            // -------------------------------------------------------------
            if (hasLivingEnemies)
            {
                legalActions.Add(new AvailableActionDTO
                {
                    ActionId = "RushIn",
                    ActionName = "Rush In (Aggressive Charge)",
                    ActionType = ActionType.RushIn.ToString(),
                    MPCost = 0,
                    Quantity = 0,
                    Scope = TargetScope.SingleEnemy.ToString(),
                    Description = "Recklessly charge a target dealing 150% damage, but your defense drops by 30% for 1 turn."
                });

                bool hasExecuteTarget = livingEnemies.Any(e => e.Stats.HPRatio <= 0.25f);
                if (hasExecuteTarget && actor.Stats.CurrentMP >= 15)
                {
                    legalActions.Add(new AvailableActionDTO
                    {
                        ActionId = "Execute",
                        ActionName = "Execute (Critical Finisher)",
                        ActionType = ActionType.Execute.ToString(),
                        MPCost = 15,
                        Quantity = 0,
                        Scope = TargetScope.SingleEnemy.ToString(),
                        Description = "Strike a critically wounded enemy (HP <= 25%) with 250% critical damage."
                    });
                }

                if (!hasOtherLivingAllies && actor.Stats.HPRatio <= 0.20f)
                {
                    legalActions.Add(new AvailableActionDTO
                    {
                        ActionId = "DesperateGamble",
                        ActionName = "Desperate Gamble (Last Stand All-In)",
                        ActionType = ActionType.DesperateGamble.ToString(),
                        MPCost = 0,
                        Quantity = 0,
                        Scope = TargetScope.SingleEnemy.ToString(),
                        Description = "Gamble everything on one final 200% damage strike as the last survivor."
                    });
                }

                legalActions.Add(new AvailableActionDTO
                {
                    ActionId = "Intimidate",
                    ActionName = "Intimidate / Battle Cry",
                    ActionType = ActionType.Intimidate.ToString(),
                    MPCost = 0,
                    Quantity = 0,
                    Scope = TargetScope.SingleEnemy.ToString(),
                    Description = "Demoralize target with psychological terror, reducing their Attack by 25% for 2 turns."
                });

                legalActions.Add(new AvailableActionDTO
                {
                    ActionId = "Feint",
                    ActionName = "Feint (Bait & Guard Break)",
                    ActionType = ActionType.Feint.ToString(),
                    MPCost = 0,
                    Quantity = 0,
                    Scope = TargetScope.SingleEnemy.ToString(),
                    Description = "Bait the opponent into bracing, shattering their defensive guard."
                });

                legalActions.Add(new AvailableActionDTO
                {
                    ActionId = "Distract",
                    ActionName = "Distract (Throw Sand/Noise)",
                    ActionType = ActionType.Distract.ToString(),
                    MPCost = 0,
                    Quantity = 0,
                    Scope = TargetScope.SingleEnemy.ToString(),
                    Description = "Throw dirt or create chaos to disrupt the enemy's concentration."
                });

                legalActions.Add(new AvailableActionDTO
                {
                    ActionId = "DuelChallenge",
                    ActionName = "Honor Duel Challenge",
                    ActionType = ActionType.DuelChallenge.ToString(),
                    MPCost = 0,
                    Quantity = 0,
                    Scope = TargetScope.SingleEnemy.ToString(),
                    Description = "Challenge an enemy 1-on-1, forcing them to lock onto you."
                });

                if (actor.Stats.HPRatio <= 0.25f || actor.Stats.Rationality <= 20)
                {
                    legalActions.Add(new AvailableActionDTO
                    {
                        ActionId = "PleaForMercy",
                        ActionName = "Plea for Mercy (Beg/Feign Surrender)",
                        ActionType = ActionType.PleaForMercy.ToString(),
                        MPCost = 0,
                        Quantity = 0,
                        Scope = TargetScope.SingleEnemy.ToString(),
                        Description = "Plead for your life while critically wounded or panicked, causing hesitation in the enemy."
                    });
                }

                legalActions.Add(new AvailableActionDTO
                {
                    ActionId = "ObserveWeakness",
                    ActionName = "Observe Weakness (Analyze Flaw)",
                    ActionType = ActionType.ObserveWeakness.ToString(),
                    MPCost = 0,
                    Quantity = 0,
                    Scope = TargetScope.SingleEnemy.ToString(),
                    Description = "Carefully study enemy movements. Target takes +35% damage next turn."
                });
            }

            // Skills & Items
            foreach (var skill in actor.EquippedSkills)
            {
                if (skill == null) continue;
                int cd = actor.GetSkillCooldown(skill.SkillId);
                if (cd <= 0 && actor.Stats.CurrentMP >= skill.MPCost)
                {
                    legalActions.Add(new AvailableActionDTO
                    {
                        ActionId = skill.SkillId,
                        ActionName = skill.SkillName,
                        ActionType = ActionType.CastSkill.ToString(),
                        MPCost = skill.MPCost,
                        Quantity = 0,
                        Scope = skill.Scope.ToString(),
                        Description = $"{skill.Description} (Cost: {skill.MPCost} MP, Power: {skill.PowerMultiplierPercent}%)"
                    });
                }
            }

            foreach (var itemEntry in actor.Inventory)
            {
                if (itemEntry?.Item != null && itemEntry.Quantity > 0)
                {
                    legalActions.Add(new AvailableActionDTO
                    {
                        ActionId = itemEntry.Item.ItemId,
                        ActionName = itemEntry.Item.ItemName,
                        ActionType = ActionType.UseItem.ToString(),
                        MPCost = 0,
                        Quantity = itemEntry.Quantity,
                        Scope = itemEntry.Item.Scope.ToString(),
                        Description = $"{itemEntry.Item.Description} (Remaining: {itemEntry.Quantity})"
                    });
                }
            }

            return legalActions;
        }
    }
}
