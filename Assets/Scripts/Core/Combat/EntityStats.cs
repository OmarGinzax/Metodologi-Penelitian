using System;
using UnityEngine;

namespace RPGStrategy.Core.Combat
{
    [Serializable]
    public class EntityStats
    {
        [SerializeField] private int maxHP = 100;
        [SerializeField] private int currentHP = 100;
        [SerializeField] private int maxMP = 50;
        [SerializeField] private int currentMP = 50;
        [SerializeField] private int attackPower = 20;
        [SerializeField] private int defensePower = 10;
        [SerializeField] private int speed = 10;

        [Header("Psychological / Morale")]
        [Range(0, 100)]
        [SerializeField] private int rationality = 100; // 100: Composed, 0: Panicked/Broken

        public int MaxHP
        {
            get => maxHP;
            set => maxHP = Mathf.Max(1, value);
        }

        public int CurrentHP
        {
            get => currentHP;
            set => currentHP = Mathf.Clamp(value, 0, maxHP);
        }

        public int MaxMP
        {
            get => maxMP;
            set => maxMP = Mathf.Max(0, value);
        }

        public int CurrentMP
        {
            get => currentMP;
            set => currentMP = Mathf.Clamp(value, 0, maxMP);
        }

        public int AttackPower
        {
            get => attackPower;
            set => attackPower = Mathf.Max(0, value);
        }

        public int DefensePower
        {
            get => defensePower;
            set => defensePower = Mathf.Max(0, value);
        }

        public int Speed
        {
            get => speed;
            set => speed = Mathf.Max(1, value);
        }

        public int Rationality
        {
            get => rationality;
            set => rationality = Mathf.Clamp(value, 0, 100);
        }

        public string ComposureLevel
        {
            get
            {
                if (rationality >= 75) return "Composed";
                if (rationality >= 40) return "Stressed";
                if (rationality >= 15) return "Shaken";
                return "Panicked";
            }
        }

        public float HPRatio => maxHP > 0 ? (float)currentHP / maxHP : 0f;
        public float HPPercentage => HPRatio * 100f;
        public float MPRatio => maxMP > 0 ? (float)currentMP / maxMP : 0f;
        public float MPPercentage => MPRatio * 100f;
        public bool IsAlive => currentHP > 0;

        public EntityStats() { }

        public EntityStats(int hp, int mp, int atk, int def, int spd, int initialRationality = 100)
        {
            maxHP = hp;
            currentHP = hp;
            maxMP = mp;
            currentMP = mp;
            attackPower = atk;
            defensePower = def;
            speed = spd;
            rationality = Mathf.Clamp(initialRationality, 0, 100);
        }

        public EntityStats Clone()
        {
            return new EntityStats
            {
                maxHP = this.maxHP,
                currentHP = this.currentHP,
                maxMP = this.maxMP,
                currentMP = this.currentMP,
                attackPower = this.attackPower,
                defensePower = this.defensePower,
                speed = this.speed,
                rationality = this.rationality
            };
        }
    }
}
