using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RPGStrategy.Core.Combat
{
    public class TurnCoordinator
    {
        private readonly List<CombatEntity> _initiativeQueue = new List<CombatEntity>();
        private int _currentIndex = -1;
        private int _roundNumber = 0;

        public int RoundNumber => _roundNumber;
        public CombatEntity CurrentActor => (_currentIndex >= 0 && _currentIndex < _initiativeQueue.Count) ? _initiativeQueue[_currentIndex] : null;
        public IReadOnlyList<CombatEntity> InitiativeQueue => _initiativeQueue;

        public void StartNewRound(IEnumerable<CombatEntity> combatants)
        {
            _roundNumber++;
            _initiativeQueue.Clear();

            // Saring entitas yang hidup dan urutkan berdasarkan Speed (descending)
            // Tie-breaking: EntityId leksikografis (ascending) agar deterministik 100%
            var sortedLiving = combatants
                .Where(c => c != null && c.IsAlive)
                .OrderByDescending(c => c.Stats.Speed)
                .ThenBy(c => c.EntityId, StringComparer.Ordinal)
                .ToList();

            _initiativeQueue.AddRange(sortedLiving);
            _currentIndex = _initiativeQueue.Count > 0 ? 0 : -1;
        }

        public bool AdvanceToNextActor()
        {
            PruneDeadActors();

            _currentIndex++;
            if (_currentIndex >= _initiativeQueue.Count)
            {
                // Ronde selesai
                return false;
            }
            return true;
        }

        public bool IsActorTurn(string entityId)
        {
            return CurrentActor != null && CurrentActor.EntityId == entityId;
        }

        public void PruneDeadActors()
        {
            for (int i = _initiativeQueue.Count - 1; i >= 0; i--)
            {
                if (!_initiativeQueue[i].IsAlive)
                {
                    if (i < _currentIndex)
                    {
                        _currentIndex--;
                    }
                    _initiativeQueue.RemoveAt(i);
                }
            }
        }
    }
}
