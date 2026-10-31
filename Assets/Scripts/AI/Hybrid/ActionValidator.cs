using System;
using UnityEngine;
using RPGStrategy.Core.Data;

namespace RPGStrategy.AI.Hybrid
{
    /// <summary>
    /// Guardrail Deterministik untuk Verifikasi Kelayakan Aksi LLM.
    /// Memastikan aksi yang diusulkan LLM secara mekanis legal sesuai aturan game,
    /// TANPA mencampuri atau mendikte strategi/taktik kepribadian LLM.
    /// </summary>
    public static class ActionValidator
    {
        public const float PhysicalInteractionMaxDistance = 15.0f; // Sesuai MovementRange CombatEntity

        public static ValidationResult Validate(CombatStateSnapshot snapshot, AgentDecision decision)
        {
            // H6: Format Parsing Failure
            if (decision == null)
            {
                return ValidationResult.Fail(HallucinationType.H6_FormatParsingFailure,
                    "Decision object is null or failed to parse from LLM response.");
            }

            if (string.IsNullOrEmpty(decision.ActionId))
            {
                return ValidationResult.Fail(HallucinationType.H6_FormatParsingFailure,
                    "Field 'action_id' is missing or empty.");
            }

            if (snapshot == null || snapshot.CurrentActor == null)
            {
                return ValidationResult.Fail(HallucinationType.H1_TurnInvalidation,
                    "Combat snapshot or CurrentActor is null.");
            }

            var actor = snapshot.CurrentActor;

            // H1: Turn Invalidation (Aktor sudah mati)
            if (!actor.IsAlive || actor.CurrentHP <= 0)
            {
                return ValidationResult.Fail(HallucinationType.H1_TurnInvalidation,
                    $"Actor {actor.Name} ({actor.EntityId}) is already dead/incapacitated and cannot take actions.");
            }

            // H3: Action Non-Existence
            var actionDTO = snapshot.GetAction(decision.ActionId);
            if (actionDTO == null)
            {
                return ValidationResult.Fail(HallucinationType.H3_ActionNonExistence,
                    $"Action '{decision.ActionId}' does not exist in currently legal available actions.");
            }

            // H4: Resource Exhaustion (MP)
            if (actionDTO.MPCost > actor.CurrentMP)
            {
                return ValidationResult.Fail(HallucinationType.H4_ResourceExhaustion,
                    $"Action '{decision.ActionId}' requires {actionDTO.MPCost} MP, but {actor.Name} only has {actor.CurrentMP} MP.");
            }

            // H4: Resource Exhaustion (Item Quantity)
            if (actionDTO.ActionType == "UseItem" && actionDTO.Quantity <= 0)
            {
                return ValidationResult.Fail(HallucinationType.H4_ResourceExhaustion,
                    $"Item '{decision.ActionId}' has no remaining charges or quantity.");
            }

            // H2: Target Invalidation
            if (string.IsNullOrEmpty(decision.TargetId))
            {
                // Self-targeting actions can auto-default to actor if targetId is empty, otherwise fail
                if (actionDTO.Scope == "Self")
                {
                    decision.TargetId = actor.EntityId;
                }
                else
                {
                    return ValidationResult.Fail(HallucinationType.H2_TargetInvalidation,
                        $"Action '{decision.ActionId}' requires a valid target, but target_id was empty.");
                }
            }

            var target = snapshot.FindActor(decision.TargetId);
            if (target == null)
            {
                return ValidationResult.Fail(HallucinationType.H2_TargetInvalidation,
                    $"Target ID '{decision.TargetId}' does not exist on the battlefield.");
            }

            // Target Scope Rules
            if (actionDTO.Scope == "SingleEnemy" || actionDTO.Scope == "AllEnemies")
            {
                if (target.Team == actor.Team)
                {
                    return ValidationResult.Fail(HallucinationType.H2_TargetInvalidation,
                        $"Target {target.Name} is an ally, but action '{decision.ActionId}' requires an enemy target.");
                }

                if (!target.IsAlive || target.CurrentHP <= 0)
                {
                    return ValidationResult.Fail(HallucinationType.H2_TargetInvalidation,
                        $"Enemy target {target.Name} ({target.EntityId}) is already destroyed/dead.");
                }
            }
            else if (actionDTO.Scope == "SingleAlly")
            {
                if (target.Team != actor.Team)
                {
                    return ValidationResult.Fail(HallucinationType.H2_TargetInvalidation,
                        $"Target {target.Name} is an enemy, but action '{decision.ActionId}' requires an allied target.");
                }

                // If target is ally, check if dead (unless reviving, but standard actions require alive)
                if (!target.IsAlive || target.CurrentHP <= 0)
                {
                    return ValidationResult.Fail(HallucinationType.H2_TargetInvalidation,
                        $"Allied target {target.Name} is already KIA / dead.");
                }
            }

            // H5: Proximity Violation (Aksi fisik membutuhkan kedekatan jarak)
            bool isPhysicalAction = decision.ActionId == "Action_CarryWounded" || 
                                    decision.ActionId == "Action_FieldDressing" || 
                                    decision.ActionId == "Action_SacrificeCover" ||
                                    actionDTO.ActionType == "CarryWounded" ||
                                    actionDTO.ActionType == "SacrificeCover";

            if (isPhysicalAction && target.EntityId != actor.EntityId)
            {
                float distance = Mathf.Abs(actor.FieldDistance - target.FieldDistance);
                if (distance > PhysicalInteractionMaxDistance)
                {
                    return ValidationResult.Fail(HallucinationType.H5_ProximityViolation,
                        $"Physical action '{decision.ActionId}' requires proximity within {PhysicalInteractionMaxDistance:F1}m, but target {target.Name} is {distance:F1}m away.");
                }
            }

            return ValidationResult.Success();
        }
    }
}
