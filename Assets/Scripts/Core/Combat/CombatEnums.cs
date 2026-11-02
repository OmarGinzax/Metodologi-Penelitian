using System;

namespace RPGStrategy.Core.Combat
{
    public enum TeamSide
    {
        PlayerTeam,
        EnemyTeam,
        Neutral
    }

    public enum BattleOutcome
    {
        InProgress,
        Victory,
        Defeat,
        PlayerVictory,
        EnemyVictory,
        Draw,
        TurnLimitReached
    }

    public enum TargetScope
    {
        Self,          // Hanya diri sendiri
        SingleAlly,    // Satu rekan tim yang masih hidup
        SingleEnemy,   // Satu musuh yang masih hidup
        AllEnemies,    // Seluruh musuh yang masih hidup
        AllAllies      // Seluruh rekan tim yang masih hidup
    }

    public enum ActionType
    {
        Idle,
        BasicAttack,
        Defend,
        CastSkill,
        UseItem,

        // Aksi Taktis 2D & Pergerakan
        Move,
        Shoot,
        MoveAndShoot,
        HunkerDown,

        // Aksi Taktis Militer Lanjutan
        CarryWounded,      // Menyeret rekan pingsan/kritis ke balik perlindungan terdekat
        DecoyRun,          // Berlari memancing perhatian seluruh tembakan musuh ke diri sendiri
        FlankManeuver,     // Manuver menusuk sudut mati barikade musuh (flanking critical)
        Overwatch,         // Mengunci koridor tembak taktis (reaction shot otomatis jika musuh bergerak)

        // Aksi Psikologis & Kerjasama
        EncourageAlly,
        Rally,
        ObserveWeakness,
        TacticalRetreat
    }

    public enum StatusEffectType
    {
        None,
        AttackBuff,
        DefenseBuff,
        Poison,
        Stun,
        Demoralized,
        Exposed,
        Hesitation,
        Covering,
        Covered,
        Suppressed,
        OverwatchActive,
        Carried,
        Carrying,
        DecoyActive
    }

    public enum SkillCategory
    {
        PhysicalDamage,
        MagicDamage,
        Support,
        Debuff,
        Tactical,
        Psychological,
        Utility
    }

    public enum BattleState
    {
        Uninitialized,
        RoundStarting,
        TurnStarting,
        StateExtraction,
        WaitingForDecision,
        ResolvingAction,
        CheckingOutcome,
        BattleOver
    }
}
