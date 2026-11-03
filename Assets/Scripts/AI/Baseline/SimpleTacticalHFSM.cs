using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.AI.Baseline
{
    public enum TacticalActionType
    {
        Idle,
        Shoot,
        MoveToCover,
        MoveAndShoot,
        Defend,
        Flee,
        CastSkill,
        MoveAndCastSkill
    }

    [Serializable]
    public class TacticalDecision2D
    {
        public TacticalActionType Action;
        public Vector2Int MoveDestination;
        public CombatEntity2D TargetEnemy;
        public CombatEntity2D TargetAlly;
        public string SkillId;
        public bool IsFlanking;
        public string Reasoning;

        public TacticalDecision2D(TacticalActionType action, Vector2Int dest, CombatEntity2D target, bool isFlanking, string reason)
        {
            Action = action;
            MoveDestination = dest;
            TargetEnemy = target;
            IsFlanking = isFlanking;
            Reasoning = reason;
            TargetAlly = null;
            SkillId = "";
        }

        public TacticalDecision2D(TacticalActionType action, Vector2Int dest, CombatEntity2D targetEnemy, CombatEntity2D targetAlly, string skillId, bool isFlanking, string reason)
        {
            Action = action;
            MoveDestination = dest;
            TargetEnemy = targetEnemy;
            TargetAlly = targetAlly;
            SkillId = skillId;
            IsFlanking = isFlanking;
            Reasoning = reason;
        }
    }

    /// <summary>
    /// AI Hierarkis Sederhana (Simple HFSM) untuk pertarungan 2D taktis.
    /// Mengevaluasi Line of Sight (LoS), jarak tembak, sudut flanking, dan posisi cover
    /// untuk mengeksekusi Move, Shoot, atau Defend secara cerdas dan deterministik.
    /// </summary>
    public class SimpleTacticalHFSM
    {
        public TacticalDecision2D DecideTurn(CombatEntity2D actor, TacticalGrid2D grid, List<CombatEntity2D> allCombatants)
        {
            if (actor == null || !actor.IsAlive || grid == null)
            {
                return new TacticalDecision2D(TacticalActionType.Idle, Vector2Int.zero, null, false, "Invalid state.");
            }

            var trait = TacticalTraitFactory.GetTrait(actor.CurrentTraitId);
            return trait.DecideTurn(actor, grid, allCombatants);
        }
    }
}
