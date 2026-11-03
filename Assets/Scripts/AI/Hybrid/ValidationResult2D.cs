using System.Collections.Generic;
using System.Text;
using RPGStrategy.AI.PureLLM;

namespace RPGStrategy.AI.Hybrid
{
    /// <summary>
    /// Hasil validasi deterministik satu usulan aksi LLM pada arsitektur Hybrid.
    /// Kategori pelanggaran memakai taksonomi H1-H5 yang sama dengan Pure LLM
    /// agar data Y2 dapat dibandingkan langsung.
    /// </summary>
    public sealed class ValidationResult2D
    {
        public readonly List<HallucinationCategory2D> Violations =
            new List<HallucinationCategory2D>();

        public readonly List<string> Diagnostics = new List<string>();

        public string SuggestedCorrection = string.Empty;

        public string RejectedAction = string.Empty;
        public string RejectedTarget = string.Empty;
        public string RejectedDestination = string.Empty;

        public bool IsValid => Violations.Count == 0;

        public string DiagnosticMessage =>
            Diagnostics.Count == 0 ? string.Empty : string.Join("; ", Diagnostics.ToArray());

        public string ViolationCodesCsv
        {
            get
            {
                if (Violations.Count == 0) return "NONE";

                var sb = new StringBuilder();
                for (int i = 0; i < Violations.Count; i++)
                {
                    if (i > 0) sb.Append('|');
                    sb.Append(HallucinationReport.Code(Violations[i]));
                }
                return sb.ToString();
            }
        }

        public bool Has(HallucinationCategory2D category) => Violations.Contains(category);

        public void Add(HallucinationCategory2D category, string detail)
        {
            if (!Violations.Contains(category)) Violations.Add(category);
            if (!string.IsNullOrEmpty(detail))
                Diagnostics.Add(HallucinationReport.Code(category) + ": " + detail);
        }
    }
}
