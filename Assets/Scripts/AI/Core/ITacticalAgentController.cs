using System.Collections.Generic;
using System.Threading.Tasks;
using RPGStrategy.AI.Baseline;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.AI
{
    /// <summary>
    /// Konteks giliran yang diberikan BattleManager2D kepada controller agen.
    /// Dipakai oleh agen LLM untuk menuliskan nomor ronde/giliran ke dalam snapshot.
    /// </summary>
    public sealed class TacticalTurnContext
    {
        public int RoundNumber { get; set; }
        public int TurnNumber { get; set; }

        public TacticalTurnContext()
        {
            RoundNumber = 1;
            TurnNumber = 0;
        }

        public TacticalTurnContext(int roundNumber, int turnNumber)
        {
            RoundNumber = roundNumber;
            TurnNumber = turnNumber;
        }
    }

    /// <summary>
    /// Abstraksi pengontrol agen taktis. BattleManager2D tidak lagi terikat langsung
    /// pada SimpleTacticalHFSM, sehingga controller dapat diganti antara:
    ///   - HFSM Baseline (deterministik)
    ///   - Pure LLM (tanpa validator / zero HFSM assistance)
    ///   - Hybrid (disiapkan untuk Milestone 5)
    /// </summary>
    public interface ITacticalAgentController
    {
        string ControllerType { get; }

        /// <summary>Konteks ronde/giliran aktif, diisi oleh BattleManager2D sebelum DecideTurnAsync.</summary>
        TacticalTurnContext TurnContext { get; set; }

        Task<TacticalDecision2D> DecideTurnAsync(
            CombatEntity2D actor,
            TacticalGrid2D grid,
            IReadOnlyList<CombatEntity2D> combatants,
            IReadOnlyList<string> history);
    }
}
