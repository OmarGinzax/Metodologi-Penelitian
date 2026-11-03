using System;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.AI.PureLLM
{
    /// <summary>Definisi skill support yang dimiliki seorang prajurit.</summary>
    public sealed class TacticalSkillDefinition2D
    {
        public string SkillId;
        public int Range;
        public int CooldownTurns;
        public string Description;

        public TacticalSkillDefinition2D(string skillId, int range, int cooldownTurns, string description)
        {
            SkillId = skillId;
            Range = range;
            CooldownTurns = cooldownTurns;
            Description = description;
        }
    }

    /// <summary>
    /// Katalog skill statis. Pemetaan mengikuti aturan CombatEntity2D:
    /// Marcus memiliki "Guard" (range 4), Claire memiliki "Heal" (range 2).
    /// Unit lain tidak memiliki skill support.
    /// </summary>
    public static class TacticalSkillCatalog2D
    {
        public const string Guard = "Guard";
        public const string Heal = "Heal";

        private static readonly TacticalSkillDefinition2D GuardDefinition =
            new TacticalSkillDefinition2D(Guard, 4, 2,
                "Redirect all damage taken by a nearby ally to the caster for 2 turns and restore +10% Rationality.");

        private static readonly TacticalSkillDefinition2D HealDefinition =
            new TacticalSkillDefinition2D(Heal, 2, 2,
                "Restore +50 HP and +10% Rationality to self or a nearby ally.");

        /// <summary>Skill milik aktor (null bila unit tidak memiliki skill support).</summary>
        public static TacticalSkillDefinition2D Resolve(CombatEntity2D actor)
        {
            if (actor == null) return null;

            string key = (actor.EntityName + " " + actor.EntityId).ToLowerInvariant();
            if (key.Contains("marcus")) return GuardDefinition;
            if (key.Contains("claire")) return HealDefinition;
            return null;
        }

        /// <summary>Definisi berdasarkan nama skill, case-insensitive.</summary>
        public static TacticalSkillDefinition2D Get(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId)) return null;
            string s = skillId.Trim().ToLowerInvariant();
            if (s == "guard") return GuardDefinition;
            if (s == "heal") return HealDefinition;
            return null;
        }

        /// <summary>Normalisasi nama skill ke ejaan kanonik ("Guard"/"Heal").</summary>
        public static string Normalize(string skillId)
        {
            var def = Get(skillId);
            return def != null ? def.SkillId : (skillId ?? string.Empty).Trim();
        }

        public static bool ActorOwnsSkill(CombatEntity2D actor, string skillId)
        {
            var owned = Resolve(actor);
            var requested = Get(skillId);
            return owned != null && requested != null &&
                   string.Equals(owned.SkillId, requested.SkillId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
