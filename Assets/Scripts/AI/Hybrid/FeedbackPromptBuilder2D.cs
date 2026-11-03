using System.Text;
using RPGStrategy.AI.PureLLM;

namespace RPGStrategy.AI.Hybrid
{
    /// <summary>
    /// Menyusun prompt korektif yang ringkas dari hasil validasi.
    /// Prompt tidak mengulang seluruh aturan dasar; fokus pada diagnosa pelanggaran
    /// dan opsi sah yang dapat langsung dipilih LLM pada percobaan kedua.
    /// </summary>
    public class FeedbackPromptBuilder2D
    {
        public string BuildFeedbackPrompt(
            ValidationResult2D validation,
            TacticalStateSnapshot2D snapshot)
        {
            var sb = new StringBuilder(700);

            sb.AppendLine("SYSTEM VALIDATION FEEDBACK:");
            sb.AppendLine("Your previous decision was REJECTED due to tactical infeasibility:");

            if (validation != null && validation.Diagnostics.Count > 0)
            {
                for (int i = 0; i < validation.Diagnostics.Count; i++)
                {
                    sb.Append("- ERROR [").Append(validation.Diagnostics[i]).AppendLine("]");
                }
            }
            else
            {
                sb.AppendLine("- ERROR [UNKNOWN]: The previous decision could not be validated.");
            }

            if (validation != null && !string.IsNullOrWhiteSpace(validation.SuggestedCorrection))
            {
                sb.AppendLine();
                sb.AppendLine("CURRENT FEASIBLE OPTIONS:");
                sb.AppendLine(validation.SuggestedCorrection);
            }

            if (snapshot != null && snapshot.CurrentActor != null)
            {
                var a = snapshot.CurrentActor;
                sb.AppendLine();
                sb.Append("YOUR CURRENT STATE: ID=").Append(a.EntityId)
                  .Append(", Position=(").Append(a.X).Append(',').Append(a.Y)
                  .Append("), HP=").Append(a.HP).Append('/').Append(a.MaxHP)
                  .Append(", AttackRange=").Append(a.AttackRange)
                  .Append(", MoveRange=").Append(a.MoveRange)
                  .Append(", Skill=").Append(string.IsNullOrEmpty(a.SkillId) ? "none" : a.SkillId)
                  .Append(a.IsSkillReady ? " (READY)" : " (COOLDOWN)")
                  .AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("REVISE YOUR DECISION: Respond ONLY with a corrected raw JSON object adhering to the schema.");
            sb.AppendLine("Do not repeat the rejected action. Choose only from the feasible options above.");

            return sb.ToString();
        }
    }
}
