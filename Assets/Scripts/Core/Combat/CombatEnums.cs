using System;

namespace RPGStrategy.Core.Combat
{
    public enum TeamSide
    {
        PlayerTeam,
        EnemyTeam
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
        // 1. Aksi Dasar
        BasicAttack,
        Defend,
        CastSkill,
        UseItem,

        // 2. Aksi Naluri Agresif & Serangan Nekat
        RushIn,            // Serangan nekat (+50% damage, -30% defense)
        Execute,           // Serangan eksekusi kritis pada musuh sekarat (HP <= 25%)
        DesperateGamble,   // All-in saat sisa 1 orang petarung terakhir

        // 3. Aksi Psikologis & Perang Emosional
        Intimidate,        // Gertakan/Battle cry: memberi debuff Demoralized pada musuh
        PleaForMercy,      // Memohon ampun saat HP kritis: membuat musuh ragu-ragu
        Feint,             // Tipuan untuk memecahkan sikap bertahan (Guard Break) lawan
        DuelChallenge,     // Tantangan kehormatan 1-lawan-1 (mengunci target ke diri sendiri)
        Distract,          // Melempar pasir/gaduh: mengurangi akurasi serangan musuh

        // 4. Aksi Empati, Solidaritas & Kerjasama Tim
        EncourageAlly,     // Menyemangati rekan: menghapus debuff mental + pulihkan 10 MP
        TossItem,          // Melemparkan perbekalan (potion/ether) ke rekan tim
        CoverAlly,         // Pasang badan berbasis jarak jangkauan fisik (proximity intercept)
        DesperateSacrifice,// Pengorbanan heroik berbasis jarak (bisa fatal jika damage melampaui sisa HP)
        Rally,             // Seruan kepemimpinan: buff Attack & Speed seluruh tim

        // 5. Aksi Kognitif & Analisis Taktis
        ObserveWeakness,   // Mengamati celah lawan: target menjadi Exposed (+35% damage masuk)
        TacticalRetreat,   // Mundur teratur: meningkatkan pertahanan 75%, posisi aman

        // 6. Aksi Taktis Militer Lanjutan (Post-Apoc Combat)
        SuppressionFire,   // Tembakan pembungkam terus-menerus: akurasi & gerak musuh berkurang 50%
        CarryWounded,      // Menyeret rekan pingsan/kritis ke balik perlindungan terdekat
        DecoyRun,          // Berlari memancing perhatian seluruh tembakan musuh ke diri sendiri
        FlankManeuver,     // Manuver menusuk sudut mati barikade musuh (flanking critical)
        Overwatch          // Mengunci koridor tembak taktis (reaction shot otomatis jika musuh bergerak)
    }

    public enum StatusEffectType
    {
        None,
        AttackBuff,
        DefenseBuff,
        Poison,
        Stun,
        Demoralized,       // Serangan berkurang 25% (dari Intimidate)
        Exposed,            // Menerima +35% damage tambahan (dari ObserveWeakness/Feint)
        Hesitation,         // Ragu-ragu menyerang aktor tertentu (dari PleaForMercy)
        Covering,           // Sedang melindungi rekan
        Covered,            // Sedang dilindungi oleh rekan
        DuelLocked,         // Terkunci hanya boleh menarget aktor tertentu
        Exhausted,          // Kelelahan setelah DesperateGamble
        Suppressed,         // Tertindas tembakan senapan mesin: akurasi -50%, dilarang serangan berat
        OverwatchActive,    // Mode bidik reaksi aktif
        Carried,            // Sedang dievakuasi/diseret oleh rekan
        Carrying,           // Sedang memanggul/menyeret rekan
        DecoyActive         // Seluruh musuh wajib menarget aktor ini
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

    public enum BattleOutcome
    {
        InProgress,
        PlayerVictory,
        EnemyVictory,
        Draw,
        TurnLimitReached
    }
}
