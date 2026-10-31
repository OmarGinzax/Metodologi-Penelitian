using System;
using System.Collections.Generic;
using System.Text;
using RPGStrategy.Core.Data;

namespace RPGStrategy.AI.Compression
{
    /// <summary>
    /// M6 – Prompt Compressor (Variabel X3: Context Compression).
    /// Reduces token count in user prompts by:
    ///   1. Using compact semantic tag notation instead of verbose English descriptions.
    ///   2. Aggregating recent events into a 3-turn rolling summary window.
    ///   3. Omitting dead entities and zero-value fields.
    /// Enables Y6 (token efficiency) comparison: Full Context vs Compressed Context.
    /// </summary>
    public static class PromptCompressor
    {
        /// <summary>
        /// Compression level:
        ///   None    = Original full prompt (X3 = 0)
        ///   Mild    = Remove dead entities, shorten field labels (X3 = 1)
        ///   Moderate= Semantic tags + 3-turn event window (X3 = 2)
        ///   Aggressive = Max compression, abbreviate all entity data (X3 = 3)
        /// </summary>
        public enum CompressionLevel { None = 0, Mild = 1, Moderate = 2, Aggressive = 3 }

        public static string Compress(CombatStateSnapshot snapshot, CompressionLevel level)
        {
            if (level == CompressionLevel.None)
            {
                return RPGStrategy.AI.LLM.PromptBuilder.BuildUserPrompt(snapshot);
            }

            var sb = new StringBuilder();
            var actor = snapshot.CurrentActor;

            // Header
            sb.Append($"[T{snapshot.TurnNumber}]");
            if (snapshot.ActiveDirective != null)
                sb.AppendLine($" CMD:{snapshot.ActiveDirective.Type}|\"{snapshot.ActiveDirective.DirectiveText}\"");
            else
                sb.AppendLine();

            // Actor stats
            if (level == CompressionLevel.Mild || level == CompressionLevel.Moderate)
            {
                sb.AppendLine($"YOU:{actor.Name}(id:{actor.EntityId}) HP:{actor.CurrentHP}/{actor.MaxHP} MP:{actor.CurrentMP} " +
                              $"RAT:{actor.Rationality}[{actor.ComposureLevel}] TRAIT:{actor.CurrentTraitId} POS:{actor.FieldDistance:F0}m");
            }
            else // Aggressive
            {
                sb.AppendLine($"U:{actor.EntityId}|{actor.CurrentHP}/{actor.MaxHP}|MP:{actor.CurrentMP}|R:{actor.Rationality}|{actor.CurrentTraitId}");
            }

            if (actor.ActiveStatusEffects.Count > 0)
                sb.AppendLine($"FX:[{string.Join(",", actor.ActiveStatusEffects)}]");

            // Allies (skip dead in Moderate+)
            sb.Append("ALLIES:");
            bool firstAlly = true;
            foreach (var a in snapshot.Allies)
            {
                if ((level >= CompressionLevel.Moderate) && !a.IsAlive) continue;
                if (!firstAlly) sb.Append("|");
                if (level == CompressionLevel.Aggressive)
                    sb.Append($"{a.EntityId}:{a.CurrentHP}/{a.MaxHP}");
                else
                    sb.Append(a.IsAlive
                        ? $"{a.Name}({a.EntityId}):HP{a.CurrentHP}/{a.MaxHP},R{a.Rationality},{a.ComposureLevel}"
                        : $"{a.Name}:KIA");
                firstAlly = false;
            }
            sb.AppendLine();

            // Relationships (only non-neutral in Moderate+)
            if (level < CompressionLevel.Aggressive)
            {
                var relParts = new List<string>();
                foreach (var r in snapshot.Relationships)
                {
                    if (level >= CompressionLevel.Moderate && r.RelationshipType == "Neutral") continue;
                    relParts.Add($"{r.TargetEntityId}:{r.AffinityScore}[{r.RelationshipType}]");
                }
                if (relParts.Count > 0)
                    sb.AppendLine($"BONDS:{string.Join("|", relParts)}");
            }

            // Enemies (skip dead in Moderate+)
            sb.Append("ENEMIES:");
            bool firstEnemy = true;
            foreach (var e in snapshot.Enemies)
            {
                if ((level >= CompressionLevel.Moderate) && !e.IsAlive) continue;
                if (!firstEnemy) sb.Append("|");
                if (level == CompressionLevel.Aggressive)
                    sb.Append($"{e.EntityId}:{e.CurrentHP}/{e.MaxHP}");
                else
                    sb.Append($"{e.Name}({e.EntityId}):HP{e.CurrentHP}/{e.MaxHP},T:{e.ThreatRank}");
                firstEnemy = false;
            }
            sb.AppendLine();

            // Events: 3-turn rolling window in Moderate+, full in Mild
            var events = snapshot.RecentEvents;
            if (level >= CompressionLevel.Moderate && events.Count > 3)
                events = events.GetRange(events.Count - 3, 3);
            if (events.Count > 0)
                sb.AppendLine($"LOG:[{string.Join("|", events)}]");

            // Available Actions
            sb.AppendLine("ACTIONS(choose one, exact action_id):");
            foreach (var act in snapshot.AvailableActions)
            {
                if (level == CompressionLevel.Aggressive)
                    sb.AppendLine($"{act.ActionId}/{act.Scope}");
                else
                    sb.AppendLine($"[{act.ActionId}]({act.ActionType},{act.Scope},MP:{act.MPCost}):{act.Description}");
            }
            sb.AppendLine("=>Respond: raw JSON only.");

            return sb.ToString();
        }

        /// <summary>
        /// Estimates token count for a prompt string (1 token ≈ 4 chars).
        /// </summary>
        public static int EstimateTokenCount(string prompt)
        {
            if (string.IsNullOrEmpty(prompt)) return 0;
            return Math.Max(1, prompt.Length / 4);
        }

        /// <summary>
        /// Returns compression ratio: 1 - (compressed / original).
        /// Higher = more compressed.
        /// </summary>
        public static float CompressionRatio(string original, string compressed)
        {
            if (string.IsNullOrEmpty(original) || original.Length == 0) return 0f;
            return 1f - ((float)compressed.Length / original.Length);
        }
    }
}
