using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using RPGStrategy.AI.Baseline;
using RPGStrategy.Demo;

namespace RPGStrategy.Core.Combat
{
    /// <summary>
    /// Manajer Pertarungan Taktis 2D (Sector 7 Overpass).
    /// Mengelola inisiatif giliran berbasis kecepatan, memanggil AI Simple HFSM,
    /// memperbarui telemetri Inspector tiap prajurit, dan memicu efek visual (tracers, floating text).
    /// </summary>
    public class BattleManager2D : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private TacticalGrid2D grid;
        [SerializeField] private VisualFeedbackFX2D fx;

        [Header("Combatants (4-Person Squad vs Enemies)")]
        [SerializeField] private List<CombatEntity2D> combatants = new List<CombatEntity2D>();

        [Header("Simulation State")]
        [SerializeField] private int roundNumber = 1;
        [SerializeField] private int globalTurnCounter = 0;
        [SerializeField] private BattleOutcome outcome = BattleOutcome.InProgress;
        [SerializeField] private bool autoRun = false;
        [SerializeField] private float autoRunInterval = 0.9f;

        private SimpleTacticalHFSM _hfsm = new SimpleTacticalHFSM();
        private Queue<CombatEntity2D> _turnQueue = new Queue<CombatEntity2D>();
        private CombatEntity2D _currentActor;
        private bool _isTurnInProgress = false;
        private float _autoRunTimer = 0f;
        private readonly List<string> _combatLogs = new List<string>();

        public TacticalGrid2D Grid { get => grid; set => grid = value; }
        public BattleOutcome Outcome => outcome;
        public int RoundNumber => roundNumber;
        public int GlobalTurnCounter => globalTurnCounter;
        public IReadOnlyList<CombatEntity2D> Combatants => combatants;
        public IReadOnlyList<string> CombatLogs => _combatLogs;
        public CombatEntity2D CurrentActor => _currentActor;

        private void Awake()
        {
            if (grid == null) grid = FindFirstObjectByType<TacticalGrid2D>();
            if (fx == null) fx = FindFirstObjectByType<VisualFeedbackFX2D>();
        }

        private void Start()
        {
            if (combatants != null && combatants.Count > 0)
            {
                if (grid != null)
                {
                    foreach (var c in combatants)
                    {
                        if (c != null && c.IsAlive)
                        {
                            grid.SetOccupant(c.GridPosition, c);
                        }
                    }
                }
                RebuildTurnQueue();
            }
        }

        public void InitializeBattle(IEnumerable<CombatEntity2D> initialCombatants)
        {
            combatants = initialCombatants?.Where(c => c != null).ToList() ?? new List<CombatEntity2D>();
            roundNumber = 1;
            globalTurnCounter = 0;
            outcome = BattleOutcome.InProgress;
            _combatLogs.Clear();
            _isTurnInProgress = false;

            // Pastikan setiap combatant terdaftar di grid
            if (grid != null)
            {
                foreach (var c in combatants)
                {
                    grid.SetOccupant(c.GridPosition, c);
                }
            }

            RebuildTurnQueue();
            RefreshAllCoverStatuses();
            AddLog("Battle initialized on Sector 7 Overpass (4-Man Squad vs Hostile Machines).");
        }

        public void RefreshAllCoverStatuses()
        {
            if (grid == null) return;
            foreach (var c in combatants)
            {
                if (c == null || !c.IsAlive) continue;
                var enemies = combatants.Where(e => e != null && e.IsAlive && e.Team != c.Team).ToList();
                var nearest = enemies.OrderBy(e => grid.GetDistance(c.GridPosition, e.GridPosition)).FirstOrDefault();
                c.UpdateCoverStatus(grid, nearest);
            }
        }

        private void RebuildTurnQueue()
        {
            var aliveOrdered = combatants
                .Where(c => c != null && c.IsAlive)
                .OrderByDescending(c => c.Stats.Speed)
                .ToList();

            _turnQueue = new Queue<CombatEntity2D>(aliveOrdered);
            RefreshAllCoverStatuses();
        }

        public void StepSingleTurn()
        {
            if (outcome != BattleOutcome.InProgress || _isTurnInProgress) return;
            StartCoroutine(ExecuteSingleTurnRoutine());
        }

        private void Update()
        {
            if (autoRun && outcome == BattleOutcome.InProgress && !_isTurnInProgress)
            {
                _autoRunTimer += Time.deltaTime;
                if (_autoRunTimer >= autoRunInterval)
                {
                    _autoRunTimer = 0f;
                    StepSingleTurn();
                }
            }
        }

        private IEnumerator ExecuteSingleTurnRoutine()
        {
            _isTurnInProgress = true;

            // 1. Cek antrean ronde
            if (_turnQueue.Count == 0)
            {
                roundNumber++;
                RebuildTurnQueue();
                AddLog($"--- ROUND {roundNumber} BEGINS ---");
            }

            if (_turnQueue.Count == 0)
            {
                CheckBattleOutcome();
                _isTurnInProgress = false;
                yield break;
            }

            _currentActor = _turnQueue.Dequeue();
            if (_currentActor == null || !_currentActor.IsAlive)
            {
                _isTurnInProgress = false;
                yield break;
            }

            globalTurnCounter++;

            // Reset status defending awal giliran
            _currentActor.SetDefending(false);

            // Update cover status terhadap ancaman musuh terdekat
            var enemies = combatants.Where(c => c.IsAlive && c.Team != _currentActor.Team).ToList();
            var nearestEnemy = enemies.OrderBy(e => grid.GetDistance(_currentActor.GridPosition, e.GridPosition)).FirstOrDefault();
            _currentActor.UpdateCoverStatus(grid, nearestEnemy);

            // 2. Minta keputusan AI Simple HFSM
            var decision = _hfsm.DecideTurn(_currentActor, grid, combatants);

            // Perbarui Telemetri Inspector langsung pada CombatEntity2D!
            _currentActor.SetActionTelemetry(
                decision.Action.ToString(), 
                decision.Reasoning, 
                decision.TargetEnemy?.EntityId ?? "", 
                decision.MoveDestination);

            // 3. Eksekusi Pergerakan jika ada
            if (decision.Action == TacticalActionType.MoveToCover || decision.Action == TacticalActionType.MoveAndShoot)
            {
                if (grid != null && decision.MoveDestination != _currentActor.GridPosition)
                {
                    // Tampilkan overlay jangkauan sejenak
                    if (fx != null)
                    {
                        var reachable = grid.GetReachableTiles(_currentActor.GridPosition, _currentActor.MoveRange);
                        fx.ShowMoveRange(reachable, grid);
                    }

                    bool moveFinished = false;
                    _currentActor.MoveTo(decision.MoveDestination, grid, () => moveFinished = true);

                    while (!moveFinished)
                    {
                        yield return null;
                    }

                    if (fx != null) fx.ClearOverlays();

                    // Perbarui cover setelah bergerak dengan musuh terdekat terbaru
                    enemies = combatants.Where(c => c.IsAlive && c.Team != _currentActor.Team).ToList();
                    nearestEnemy = enemies.OrderBy(e => grid.GetDistance(_currentActor.GridPosition, e.GridPosition)).FirstOrDefault();
                    _currentActor.UpdateCoverStatus(grid, nearestEnemy);
                    RefreshAllCoverStatuses();
                    AddLog($"{_currentActor.EntityName} moved to ({_currentActor.GridPosition.x}, {_currentActor.GridPosition.y}) [Cover: {_currentActor.ActiveCoverType}].");
                    yield return new WaitForSeconds(0.1f);
                }
            }

            // 4. Eksekusi Tembakan jika ada
            if (decision.Action == TacticalActionType.Shoot || decision.Action == TacticalActionType.MoveAndShoot)
            {
                var target = decision.TargetEnemy;
                if (target != null && target.IsAlive)
                {
                    bool isBurst = _currentActor.WeaponName.Contains("Vector");
                    Vector3 shooterPos = grid.GridToWorld(_currentActor.GridPosition);
                    Vector3 targetPos = grid.GridToWorld(target.GridPosition);

                    // Pemicu visual Bullet Tracer
                    bool tracerDone = false;
                    if (fx != null)
                    {
                        fx.PlayBulletTracer(shooterPos, targetPos, isBurst, () => tracerDone = true);
                    }
                    else
                    {
                        tracerDone = true;
                    }

                    while (!tracerDone)
                    {
                        yield return null;
                    }

                    // Perhitungan Damage & Cover/Flank
                    int rawAtk = _currentActor.Stats.AttackPower;
                    int dealtDmg = target.ApplyDamage(rawAtk, decision.IsFlanking);

                    // Pemicu Floating Text
                    if (fx != null)
                    {
                        string popup = decision.IsFlanking ? $"CRIT! -{dealtDmg}" : $"-{dealtDmg}";
                        Color txtCol = decision.IsFlanking ? new Color(1f, 0.85f, 0.1f) : new Color(0.95f, 0.25f, 0.2f);
                        if (!decision.IsFlanking && target.ActiveCoverType != CoverType.None)
                        {
                            popup = $"COVER -{dealtDmg}";
                            txtCol = new Color(0.6f, 0.75f, 0.95f);
                        }
                        fx.SpawnFloatingText(targetPos, popup, txtCol);
                    }

                    string flankTag = decision.IsFlanking ? " [FLANK CRITICAL!]" : "";
                    AddLog($"{_currentActor.EntityName} fired {_currentActor.WeaponName} at {target.EntityName} dealing {dealtDmg} dmg{flankTag}.");

                    if (!target.IsAlive)
                    {
                        AddLog($"[ELIMINATED] {target.EntityName} was destroyed!");
                        grid.ClearOccupant(target.GridPosition);
                    }
                }
            }
            else if (decision.Action == TacticalActionType.Defend)
            {
                _currentActor.SetDefending(true);
                Vector3 actorPos = grid.GridToWorld(_currentActor.GridPosition);
                if (fx != null) fx.SpawnFloatingText(actorPos, "DEFENDING!", new Color(0.2f, 0.85f, 1f));
                AddLog($"{_currentActor.EntityName} entered defensive stance behind cover (Hunker Down).");
            }

            yield return new WaitForSeconds(0.12f);

            // Akhiri giliran unit
            _currentActor.SetActionTelemetry("TurnEnded", "Turn completed.");
            _currentActor = null;

            CheckBattleOutcome();
            _isTurnInProgress = false;
        }

        private void CheckBattleOutcome()
        {
            bool allEnemiesDead = combatants.Where(c => c.Team == TeamSide.EnemyTeam).All(c => !c.IsAlive);
            bool allSquadDead = combatants.Where(c => c.Team == TeamSide.PlayerTeam).All(c => !c.IsAlive);

            if (allEnemiesDead && !allSquadDead)
            {
                outcome = BattleOutcome.Victory;
                AddLog("=== VICTORY: All hostile mechanized units eliminated! ===");
            }
            else if (allSquadDead)
            {
                outcome = BattleOutcome.Defeat;
                AddLog("=== DEFEAT: Squad wiped out on Sector 7 Overpass. ===");
            }
        }

        public void AddLog(string msg)
        {
            _combatLogs.Add(msg);
            Debug.Log($"[BattleManager2D] {msg}");
        }

        public bool IsTurnInProgress => _isTurnInProgress;
        public bool AutoRun { get => autoRun; set { autoRun = value; _autoRunTimer = 0f; } }
    }
}
