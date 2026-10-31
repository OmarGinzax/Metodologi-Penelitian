using System;

namespace RPGStrategy.AI.Hybrid
{
    /// <summary>
    /// Taksonomi Kesalahan & Halusinasi LLM (Variabel Y2: Illogical/Invalid Action Rate).
    /// Mengkategorikan setiap kegagalan pengambilan keputusan LLM secara deterministik.
    /// </summary>
    public enum HallucinationType
    {
        None = 0,

        /// <summary>
        /// H1: Turn Invalidation - Aktor mencoba mengambil aksi ketika bukan gilirannya atau ID aktor tidak sesuai.
        /// </summary>
        H1_TurnInvalidation = 1,

        /// <summary>
        /// H2: Target Invalidation - Target sudah mati (HP <= 0), target tidak ada di snapshot, atau target scope salah (menyerang diri sendiri/kawan tanpa izin).
        /// </summary>
        H2_TargetInvalidation = 2,

        /// <summary>
        /// H3: Action Non-Existence - ActionId tidak terdaftar dalam LegalActions snapshot saat ini.
        /// </summary>
        H3_ActionNonExistence = 3,

        /// <summary>
        /// H4: Resource Exhaustion - Biaya MP melebihi MP aktor saat ini, atau item tidak tersedia dalam inventaris.
        /// </summary>
        H4_ResourceExhaustion = 4,

        /// <summary>
        /// H5: Proximity Violation - Aksi fisik (Carry, Field Dressing, Sacrifice) di luar jangkauan gerak (MovementRange).
        /// </summary>
        H5_ProximityViolation = 5,

        /// <summary>
        /// H6: Format Parsing Failure - Output LLM bukan format JSON yang valid atau field esensial bernilai null/kosong.
        /// </summary>
        H6_FormatParsingFailure = 6
    }

    [Serializable]
    public class ValidationResult
    {
        public bool IsValid;
        public HallucinationType HallucinationCode;
        public string ErrorFeedbackMessage;

        public static ValidationResult Success()
        {
            return new ValidationResult
            {
                IsValid = true,
                HallucinationCode = HallucinationType.None,
                ErrorFeedbackMessage = "Action is mechanically legal."
            };
        }

        public static ValidationResult Fail(HallucinationType code, string message)
        {
            return new ValidationResult
            {
                IsValid = false,
                HallucinationCode = code,
                ErrorFeedbackMessage = message
            };
        }
    }
}
