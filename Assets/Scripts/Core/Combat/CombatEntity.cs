using System;
using System.Collections.Generic;
using UnityEngine;
using RPGStrategy.AI.Core;

namespace RPGStrategy.Core.Combat
{
    [Serializable]
    public class InventoryItemEntry
    {
        public ItemData Item;
        public int Quantity;

        public InventoryItemEntry() { }
        public InventoryItemEntry(ItemData item, int quantity)
        {
            Item = item;
            Quantity = quantity;
        }
    }

    public class CombatEntity : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string entityId = "entity_01";
        [SerializeField] private string entityName = "Warrior";
        [SerializeField] private TeamSide team = TeamSide.PlayerTeam;

        [Header("Personality & Position")]
        [SerializeField] private string currentTraitId = "LoyalProtector";
        [SerializeField] private float fieldDistance = 15f; // Jarak meter dari titik jemput evakuasi transport
        [SerializeField] private float movementRange = 15f; // Jarak lari maksimal per putaran

        [Header("Stats")]
        [SerializeField] private EntityStats stats = new EntityStats();

        [Header("Loadout")]
        [SerializeField] private List<SkillData> equippedSkills = new List<SkillData>();
        [SerializeField] private List<InventoryItemEntry> inventory = new List<InventoryItemEntry>();

        [Header("Social & Psychological")]
        [SerializeField] private RelationshipMatrix relationships = new RelationshipMatrix();

        [Header("Runtime State")]
        [SerializeField] private bool isDefending = false;
        [SerializeField] private bool isRetreating = false;
        [SerializeField] private bool isOverwatchActive = false;
        [SerializeField] private string coveringTargetId = string.Empty;
        [SerializeField] private string coveredByActorId = string.Empty;
        [SerializeField] private string carryingTargetId = string.Empty;
        [SerializeField] private string carriedByActorId = string.Empty;
        [SerializeField] private int vulnerableTurns = 0;

        private readonly List<ActiveStatusEffect> _activeEffects = new List<ActiveStatusEffect>();
        private readonly Dictionary<string, int> _skillCooldowns = new Dictionary<string, int>();

        public string EntityId => entityId;
        public string EntityName => entityName;
        public TeamSide Team => team;
        public string CurrentTraitId => currentTraitId;
        public float FieldDistance { get => fieldDistance; set => fieldDistance = Mathf.Max(0f, value); }
        public float MovementRange => movementRange;
        public EntityStats Stats => stats;
        public RelationshipMatrix Relationships => relationships;
        public IReadOnlyList<SkillData> EquippedSkills => equippedSkills;
        public IReadOnlyList<InventoryItemEntry> Inventory => inventory;
        public IReadOnlyList<ActiveStatusEffect> ActiveEffects => _activeEffects;
        public bool IsDefending => isDefending;
        public bool IsRetreating => isRetreating;
        public bool IsOverwatchActive => isOverwatchActive;
        public bool IsSuppressed => HasStatusEffect(StatusEffectType.Suppressed);
        public string CoveringTargetId => coveringTargetId;
        public string CoveredByActorId => coveredByActorId;
        public string CarryingTargetId => carryingTargetId;
        public string CarriedByActorId => carriedByActorId;
        public bool IsAlive => stats != null && stats.IsAlive;
        public IAgentController AssignedController { get; set; }

        public void Initialize(string id, string name, TeamSide side, EntityStats initialStats, string trait = "LoyalProtector", float initialDist = 15f)
        {
            entityId = id;
            entityName = name;
            team = side;
            currentTraitId = trait;
            fieldDistance = initialDist;
            stats = initialStats?.Clone() ?? new EntityStats();
            isDefending = false;
            isRetreating = false;
            isOverwatchActive = false;
            coveringTargetId = string.Empty;
            coveredByActorId = string.Empty;
            carryingTargetId = string.Empty;
            carriedByActorId = string.Empty;
            vulnerableTurns = 0;
            _activeEffects.Clear();
            _skillCooldowns.Clear();
        }

        public void SetTrait(string newTraitId)
        {
            currentTraitId = newTraitId;
        }

        public float DistanceTo(CombatEntity other)
        {
            if (other == null) return float.MaxValue;
            return Mathf.Abs(this.fieldDistance - other.fieldDistance);
        }

        public bool CanReach(CombatEntity other)
        {
            if (other == null) return false;
            return DistanceTo(other) <= movementRange;
        }

        public void SetRelationship(string targetEntityId, int affinity)
        {
            relationships.SetAffinity(targetEntityId, affinity);
        }

        public void ModifyRationality(int delta)
        {
            if (!IsAlive) return;
            stats.Rationality += delta;
        }

        public void NotifyAllyDamaged(CombatEntity ally, int dmg)
        {
            if (!IsAlive || ally == null || ally == this) return;

            int affinity = relationships.GetAffinity(ally.EntityId);
            if (affinity >= 75 && ally.Stats.HPRatio <= 0.30f)
            {
                ModifyRationality(-15);
            }
        }

        public void NotifyAllyDefeated(CombatEntity ally)
        {
            if (!IsAlive || ally == null || ally == this) return;

            int affinity = relationships.GetAffinity(ally.EntityId);
            if (affinity >= 75)
            {
                ModifyRationality(-35);
            }
            else if (affinity >= 30)
            {
                ModifyRationality(-20);
            }
            else
            {
                ModifyRationality(-10);
            }
        }

        public void AddSkill(SkillData skill)
        {
            if (skill != null && !equippedSkills.Contains(skill))
            {
                equippedSkills.Add(skill);
            }
        }

        public void AddItem(ItemData item, int quantity)
        {
            if (item == null || quantity <= 0) return;
            var existing = inventory.Find(e => e.Item != null && e.Item.ItemId == item.ItemId);
            if (existing != null)
            {
                existing.Quantity += quantity;
            }
            else
            {
                inventory.Add(new InventoryItemEntry(item, quantity));
            }
        }

        public int GetItemQuantity(string itemId)
        {
            var entry = inventory.Find(e => e.Item != null && e.Item.ItemId == itemId);
            return entry?.Quantity ?? 0;
        }

        public ItemData GetItem(string itemId)
        {
            var entry = inventory.Find(e => e.Item != null && e.Item.ItemId == itemId);
            return entry?.Item;
        }

        public int GetSkillCooldown(string skillId)
        {
            return _skillCooldowns.TryGetValue(skillId, out var cd) ? cd : 0;
        }

        public void SetSkillCooldown(string skillId, int turns)
        {
            if (turns <= 0)
                _skillCooldowns.Remove(skillId);
            else
                _skillCooldowns[skillId] = turns;
        }

        public void SetDefending(bool defending) => isDefending = defending;
        public void SetRetreating(bool retreating) => isRetreating = retreating;
        public void SetOverwatch(bool active) => isOverwatchActive = active;
        public void SetVulnerable(int turns) => vulnerableTurns = Mathf.Max(vulnerableTurns, turns);
        public void SetCoveringTarget(string targetId) => coveringTargetId = targetId;
        public void SetCoveredBy(string protectorId) => coveredByActorId = protectorId;

        public void SetCarrying(string targetId) => carryingTargetId = targetId;
        public void SetCarriedBy(string carrierId) => carriedByActorId = carrierId;
        public void ClearCarry()
        {
            carryingTargetId = string.Empty;
            carriedByActorId = string.Empty;
        }

        public void ClearCover()
        {
            coveringTargetId = string.Empty;
            coveredByActorId = string.Empty;
        }

        public int ApplyDamage(int rawDamage, bool isAttackerDemoralized = false)
        {
            if (!IsAlive) return 0;

            int effectiveAtk = rawDamage;
            if (isAttackerDemoralized)
            {
                effectiveAtk = Mathf.Max(1, Mathf.RoundToInt(rawDamage * 0.75f));
            }

            int defPower = stats.DefensePower;
            if (vulnerableTurns > 0)
            {
                defPower = Mathf.Max(0, Mathf.RoundToInt(defPower * 0.7f));
            }

            int mitigatedDamage = Mathf.Max(1, effectiveAtk - (defPower / 2));

            if (HasStatusEffect(StatusEffectType.Exposed))
            {
                mitigatedDamage = Mathf.RoundToInt(mitigatedDamage * 1.35f);
            }

            if (isDefending)
            {
                mitigatedDamage = Mathf.Max(1, Mathf.RoundToInt(mitigatedDamage * 0.5f));
            }

            if (isRetreating)
            {
                mitigatedDamage = Mathf.Max(1, Mathf.RoundToInt(mitigatedDamage * 0.25f));
            }

            int prevHP = stats.CurrentHP;
            stats.CurrentHP -= mitigatedDamage;
            int actualDamageDealt = prevHP - stats.CurrentHP;

            if (actualDamageDealt >= Mathf.RoundToInt(stats.MaxHP * 0.30f))
            {
                ModifyRationality(-12);
            }

            return actualDamageDealt;
        }

        public int ApplyHeal(int healAmount)
        {
            if (!IsAlive) return 0;

            int prevHP = stats.CurrentHP;
            stats.CurrentHP += Mathf.Max(0, healAmount);
            return stats.CurrentHP - prevHP;
        }

        public int ApplyMPRestore(int mpAmount)
        {
            if (!IsAlive) return 0;

            int prevMP = stats.CurrentMP;
            stats.CurrentMP += Mathf.Max(0, mpAmount);
            return stats.CurrentMP - prevMP;
        }

        public bool ConsumeMP(int amount)
        {
            if (amount < 0) return false;
            if (stats.CurrentMP >= amount)
            {
                stats.CurrentMP -= amount;
                return true;
            }
            return false;
        }

        public bool ConsumeHP(int amount)
        {
            if (amount <= 0) return false;
            stats.CurrentHP -= amount;
            return true;
        }

        public bool ConsumeItem(string itemId)
        {
            var entry = inventory.Find(e => e.Item != null && e.Item.ItemId == itemId);
            if (entry != null && entry.Quantity > 0)
            {
                entry.Quantity--;
                return true;
            }
            return false;
        }

        public bool HasStatusEffect(StatusEffectType type)
        {
            return _activeEffects.Exists(e => e.Type == type);
        }

        public void RemoveStatusEffect(StatusEffectType type)
        {
            _activeEffects.RemoveAll(e => e.Type == type);
        }

        public void ClearMentalDebuffs()
        {
            _activeEffects.RemoveAll(e => 
                e.Type == StatusEffectType.Demoralized || 
                e.Type == StatusEffectType.Hesitation || 
                e.Type == StatusEffectType.Stun);
        }

        public void ApplyStatusEffect(StatusEffectType type, int duration, int magnitude)
        {
            if (type == StatusEffectType.None || duration <= 0) return;

            var existing = _activeEffects.Find(e => e.Type == type);
            if (existing != null)
            {
                existing.RemainingTurns = Mathf.Max(existing.RemainingTurns, duration);
                existing.Magnitude = magnitude;
            }
            else
            {
                _activeEffects.Add(new ActiveStatusEffect(type, duration, magnitude));
            }
        }

        public void StartTurn()
        {
            isDefending = false;
            isRetreating = false;
            isOverwatchActive = false;

            if (vulnerableTurns > 0) vulnerableTurns--;

            var keys = new List<string>(_skillCooldowns.Keys);
            foreach (var key in keys)
            {
                _skillCooldowns[key]--;
                if (_skillCooldowns[key] <= 0)
                {
                    _skillCooldowns.Remove(key);
                }
            }
        }

        public List<string> ProcessTurnStatusEffects()
        {
            var eventLogs = new List<string>();
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = _activeEffects[i];
                if (effect.Type == StatusEffectType.Poison)
                {
                    int dmg = ApplyDamage(effect.Magnitude);
                    eventLogs.Add($"{entityName} took {dmg} poison damage.");
                }

                effect.RemainingTurns--;
                if (effect.RemainingTurns <= 0)
                {
                    eventLogs.Add($"{entityName}'s {effect.Type} expired.");
                    _activeEffects.RemoveAt(i);
                }
            }
            return eventLogs;
        }
    }
}
