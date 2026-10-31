using System;
using UnityEngine;

namespace RPGStrategy.Core.Combat
{
    [Serializable]
    public class ActiveStatusEffect
    {
        public StatusEffectType Type;
        public int RemainingTurns;
        public int Magnitude;

        public ActiveStatusEffect() { }

        public ActiveStatusEffect(StatusEffectType type, int durationTurns, int magnitude)
        {
            Type = type;
            RemainingTurns = durationTurns;
            Magnitude = magnitude;
        }

        public ActiveStatusEffect Clone()
        {
            return new ActiveStatusEffect(Type, RemainingTurns, Magnitude);
        }
    }
}
