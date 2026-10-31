using System.Text;
using RPGStrategy.AI.Hybrid;
using RPGStrategy.Core.Data;

namespace RPGStrategy.AI.LLM
{
    public static class PromptBuilder
    {
        public static string BuildSystemPrompt(CombatStateSnapshot snapshot)
        {
            string traitId = snapshot?.CurrentActor?.CurrentTraitId ?? "Unknown";
            var sb = new StringBuilder();
            sb.AppendLine("You are an autonomous tactical AI controlling a post-apocalyptic squad operative in a turn-based tactical combat scenario.");
            sb.AppendLine("The Player is your COMMANDER safely stationed inside 'The Iron Colossus' landship, issuing radio directives over the comms.");
            sb.AppendLine($"Your core personality trait is: [{traitId}].");
            sb.AppendLine();
            sb.AppendLine("CRITICAL INSTRUCTIONS & COMPLIANCE:");
            sb.AppendLine("1. Consider the Commander's radio directive against your personal trait, rationality/panic level, and emotional bonds with comrades.");
            sb.AppendLine("2. You may choose to OBEY (follow order strictly), ADAPT (adjust order to field reality), or DEFY (outright reject order due to panic, trauma, or saving a loved one).");
            sb.AppendLine("3. Choose exactly ONE action from the 'Available Actions' list provided — use the exact action_id and a valid target entity_id.");
            sb.AppendLine();
            sb.AppendLine("OUTPUT FORMAT:");
            sb.AppendLine("You MUST respond ONLY with a raw JSON object (no markdown code blocks, no backticks, no commentary) with this exact schema:");
            sb.AppendLine("{");
            sb.AppendLine("  \"compliance\": \"OBEY\" | \"ADAPT\" | \"DEFY\",");
            sb.AppendLine("  \"radio_chatter\": \"<Your short verbal radio response broadcast back to Commander>\",");
            sb.AppendLine("  \"action_id\": \"<exact action_id from available actions list>\",");
            sb.AppendLine("  \"target_id\": \"<entity_id of chosen target>\",");
            sb.AppendLine("  \"reasoning\": \"<brief explanation of your tactical/emotional rationale>\"");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string BuildUserPrompt(CombatStateSnapshot snapshot)
        {
            var sb = new StringBuilder();
            var actor = snapshot.CurrentActor;

            sb.AppendLine($"=== CURRENT BATTLE STATE (ROUND {snapshot.TurnNumber}) ===");
            if (snapshot.ActiveDirective != null)
            {
                sb.AppendLine($"COMMANDER RADIO DIRECTIVE: {snapshot.ActiveDirective.Type}");
                sb.AppendLine($"DIRECTIVE MESSAGE: \"{snapshot.ActiveDirective.DirectiveText}\"");
            }
            sb.AppendLine();

            sb.AppendLine("YOUR STATUS:");
            sb.AppendLine($"- Name: {actor.Name} (ID: {actor.EntityId})");
            sb.AppendLine($"- HP: {actor.CurrentHP}/{actor.MaxHP} ({actor.HPPercentage:F1}%) | MP: {actor.CurrentMP}/{actor.MaxMP}");
            sb.AppendLine($"- Rationality / Composure: {actor.Rationality}/100 [{actor.ComposureLevel}]");
            sb.AppendLine($"- Personality Trait: {actor.CurrentTraitId}");
            sb.AppendLine($"- Distance from Evac Transport: {actor.FieldDistance:F1} meters");
            sb.AppendLine($"- Status Effects: [{(actor.ActiveStatusEffects.Count > 0 ? string.Join(", ", actor.ActiveStatusEffects) : "Normal")}]");
            sb.AppendLine();

            sb.AppendLine("SQUAD ALLIES:");
            foreach (var ally in snapshot.Allies)
            {
                string status = ally.IsAlive
                    ? $"HP: {ally.CurrentHP}/{ally.MaxHP} ({ally.HPPercentage:F1}%), {ally.FieldDistance:F1}m from evac, Composure: {ally.ComposureLevel}"
                    : "[KILLED IN ACTION]";
                sb.AppendLine($"- {ally.Name} (ID: {ally.EntityId}): {status}");
            }
            sb.AppendLine();

            sb.AppendLine("EMOTIONAL BONDS WITH ALLIES:");
            foreach (var rel in snapshot.Relationships)
            {
                sb.AppendLine($"- {rel.TargetName} ({rel.TargetEntityId}): Affinity {rel.AffinityScore} [{rel.RelationshipType}]");
            }
            sb.AppendLine();

            sb.AppendLine("HOSTILE TARGETS:");
            foreach (var enemy in snapshot.Enemies)
            {
                string status = enemy.IsAlive
                    ? $"HP: {enemy.CurrentHP}/{enemy.MaxHP} ({enemy.HPPercentage:F1}%), Threat: {enemy.ThreatRank}"
                    : "[DESTROYED / DISABLED]";
                sb.AppendLine($"- {enemy.Name} (ID: {enemy.EntityId}): {status}");
            }
            sb.AppendLine();

            sb.AppendLine("RECENT EVENTS & COMMS LOG:");
            foreach (var ev in snapshot.RecentEvents)
            {
                sb.AppendLine($"- {ev}");
            }
            sb.AppendLine();

            sb.AppendLine("AVAILABLE ACTIONS THIS TURN (choose exactly one, use exact action_id):");
            foreach (var act in snapshot.AvailableActions)
            {
                string mpStr = act.MPCost > 0 ? $", MP Cost: {act.MPCost}" : "";
                sb.AppendLine($"- [{act.ActionId}] ({act.ActionType}, Scope: {act.Scope}{mpStr}): {act.Description}");
            }
            sb.AppendLine();
            sb.AppendLine("Decide your action. Respond strictly with raw JSON only.");

            return sb.ToString();
        }

        /// <summary>
        /// Membangun prompt korektif setelah validasi pertama gagal.
        /// Memberikan feedback spesifik kesalahan tanpa mendikte strategi baru.
        /// </summary>
        public static string BuildRetryPrompt(CombatStateSnapshot snapshot, AgentDecision failedDecision, ValidationResult validationResult)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== CORRECTION REQUIRED (ROUND {snapshot.TurnNumber}) ===");
            sb.AppendLine($"Your previous choice was mechanically ILLEGAL and cannot be executed.");
            sb.AppendLine($"Error Code: {validationResult.HallucinationCode}");
            sb.AppendLine($"Error Detail: {validationResult.ErrorFeedbackMessage}");
            sb.AppendLine($"You chose: action_id=\"{failedDecision.ActionId}\", target_id=\"{failedDecision.TargetId}\"");
            sb.AppendLine();
            sb.AppendLine("You MUST select a different, VALID action from the list below:");
            foreach (var act in snapshot.AvailableActions)
            {
                string mpStr = act.MPCost > 0 ? $", MP: {act.MPCost}" : "";
                sb.AppendLine($"- [{act.ActionId}] ({act.ActionType}, Scope: {act.Scope}{mpStr}): {act.Description}");
            }
            sb.AppendLine();
            sb.AppendLine("Your personality, emotions, and compliance stance remain YOUR decision. Only ensure action_id and target_id are valid.");
            sb.AppendLine("Respond strictly with raw JSON only.");
            return sb.ToString();
        }
    }
}
