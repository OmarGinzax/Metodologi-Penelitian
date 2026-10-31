using System;
using UnityEngine;

namespace RPGStrategy.Core.Combat
{
    public enum ItemEffectType
    {
        RestoreHP,
        RestoreMP,
        CureStatus
    }

    [CreateAssetMenu(fileName = "NewItem", menuName = "RPG Combat/Item Data")]
    public class ItemData : ScriptableObject
    {
        [SerializeField] private string itemId = "Item_Potion";
        [SerializeField] private string itemName = "Health Potion";
        [TextArea(2, 4)]
        [SerializeField] private string description = "Restores 50 HP.";
        [SerializeField] private ItemEffectType effectType = ItemEffectType.RestoreHP;
        [SerializeField] private int effectValue = 50;
        [SerializeField] private TargetScope scope = TargetScope.SingleAlly;

        public string ItemId => itemId;
        public string ItemName => itemName;
        public string Description => description;
        public ItemEffectType EffectType => effectType;
        public int EffectValue => effectValue;
        public TargetScope Scope => scope;

        public void Initialize(string id, string name, string desc, ItemEffectType type, int value, TargetScope targetScope = TargetScope.SingleAlly)
        {
            itemId = id;
            itemName = name;
            description = desc;
            effectType = type;
            effectValue = value;
            scope = targetScope;
        }
    }
}
