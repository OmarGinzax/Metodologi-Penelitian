using System;
using UnityEngine;

namespace RPGStrategy.Core.Combat
{
    [CreateAssetMenu(fileName = "NewSkill", menuName = "RPG Combat/Skill Data")]
    public class SkillData : ScriptableObject
    {
        [SerializeField] private string skillId = "Skill_Default";
        [SerializeField] private string skillName = "Default Strike";
        [TextArea(2, 4)]
        [SerializeField] private string description = "A standard combat skill.";
        [SerializeField] private int mpCost = 10;
        [SerializeField] private int powerMultiplierPercent = 120; // 120 = 1.2x attack
        [SerializeField] private TargetScope scope = TargetScope.SingleEnemy;
        [SerializeField] private SkillCategory category = SkillCategory.PhysicalDamage;
        [SerializeField] private int baseCooldown = 0;
        [SerializeField] private StatusEffectType appliesEffect = StatusEffectType.None;
        [SerializeField] private int effectDuration = 0;
        [SerializeField] private int effectMagnitude = 0;

        public string SkillId => skillId;
        public string SkillName => skillName;
        public string Description => description;
        public int MPCost => mpCost;
        public int PowerMultiplierPercent => powerMultiplierPercent;
        public TargetScope Scope => scope;
        public SkillCategory Category => category;
        public int BaseCooldown => baseCooldown;
        public StatusEffectType AppliesEffect => appliesEffect;
        public int EffectDuration => effectDuration;
        public int EffectMagnitude => effectMagnitude;

        public void Initialize(
            string id, 
            string name, 
            string desc, 
            int cost, 
            int power, 
            TargetScope targetScope, 
            SkillCategory cat, 
            int cooldown = 0,
            StatusEffectType effect = StatusEffectType.None,
            int duration = 0,
            int magnitude = 0)
        {
            skillId = id;
            skillName = name;
            description = desc;
            mpCost = cost;
            powerMultiplierPercent = power;
            scope = targetScope;
            category = cat;
            baseCooldown = cooldown;
            appliesEffect = effect;
            effectDuration = duration;
            effectMagnitude = magnitude;
        }
    }
}
