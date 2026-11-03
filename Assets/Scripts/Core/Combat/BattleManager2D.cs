using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using RPGStrategy.AI;
using RPGStrategy.AI.Baseline;
using RPGStrategy.AI.Telemetry;
using RPGStrategy.Demo;

namespace RPGStrategy.Core.Combat
{
    /// <summary>
    /// Manajer Pertarungan Taktis 2D (Sector 7 Overpass).
    /// Mengelola inisiatif giliran berbasis kecepatan, memanggil controller agen aktif
    /// (HFSM Baseline / Pure LLM / Hybrid), mengeksekusi keputusan secara langsung,
    /// memperbarui telemetri Inspector, dan memicu efek visual (tracers, floating text).
    /// </summary>
    public class BattleManager2D : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private TacticalGrid2D grid;
        [SerializeField] private VisualFeedbackFX2D fx;

        [Header("Combatants (4-Person Squad vs Enemies)")]
        [SerializeField] private List<CombatEntity2D> combatants = new List<CombatEntity2D>();

        [Header("AI Agent Controller")]
        [Tooltip("Kosongkan untuk memakai HFSM baseline. Isi dengan PureLLMTacticalAgent untuk perlakuan Pure LLM.")]
        [SerializeField] private TacticalAgentControllerBase agentController;

        [Header("Research Telemetry")]
        [SerializeField] private CombatTelemetryLogger2D telemetryLogger;
        [SerializeField] private bool autoExportTelemetry = true;
        [SerializeField] private bool clearTelemetryOnBattleStart = true;

        [Header("Simulation State")]
        [SerializeField] private int roundNumber = 1;
        [SerializeField] private int globalTurnCounter = 0;
        [SerializeField] private BattleOutcome outcome = BattleOutcome.InProgress;
        [SerializeField] private bool autoRun = false;
        [SerializeField] private float autoRunInterval = 0.9f;

        private ITacticalAgentController _agentController;
        private Queue<CombatEntity2D> _turnQueue = new Queue<CombatEntity2D>();
        private CombatEntity2D _currentActor;
        private bool _isTurnInProgress = false;
        private float _autoRunTimer = 0f;
        private readonly List<string> _combatLogs = new List<string>();
        private readonly HashSet<CombatEntity2D> _fleeShockedEntities = new HashSet<CombatEntity2D>();
        private readonly HashSet<CombatEntity2D> _deathShockedEntities = new HashSet<CombatEntity2D>();
        private bool _telemetryExported = false;

        public TacticalGrid2D Grid { get => grid; set => grid = value; }
        public BattleOutcome Outcome => outcome;
        public int RoundNumber => roundNumber;
        public int GlobalTurnCounter => globalTurnCounter;
        public IReadOnlyList<CombatEntity2D> Combatants => combatants;
        public IReadOnlyList<string> CombatLogs => _combatLogs;
        public CombatEntity2D CurrentActor => _currentActor;
        public ITacticalAgentController ActiveAgentController => _agentController;
        public CombatTelemetryLogger2D TelemetryLogger => telemetryLogger;

        private void Awake()
        {
            if (grid == null) grid = FindFirstObjectByType<TacticalGrid2D>();
            if (fx == null) fx = FindFirstObjectByType<VisualFeedbackFX2D>();

            if (agentController == null)
                agentController = GetComponent<TacticalAgentControllerBase>();
            if (agentController == null)
                agentController = gameObject.AddComponent<HFSMTacticalAgent>();

            _agentController = agentController;

            if (telemetryLogger == null)
                telemetryLogger = FindFirstObjectByType<CombatTelemetryLogger2D>();
        }

        /// <summary>Mengganti controller agen aktif pada runtime (HFSM / Pure LLM / Hybrid).</summary>
        public void SetAgentController(ITacticalAgentController controller)
        {
            _agentController = controller;
            agentController = controller as TacticalAgentControllerBase;
        }

        private void Start()
        {
            if (combatants != null && combatants.Count > 0)
            {
                if (grid != null)
                {
                    foreach (var c in combatants)
                    {
                        if (c != null && c.IsAlive && !c.IsExtracted)
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
            _fleeShockedEntities.Clear();
            _deathShockedEntities.Clear();
            _isTurnInProgress = false;
            _telemetryExported = false;

            if (clearTelemetryOnBattleStart && telemetryLogger != null)
                telemetryLogger.Clear();

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
                if (c == null || !c.IsAlive || c.IsExtracted) continue;
                var enemies = combatants.Where(e => e != null && e.IsAlive && !e.IsExtracted && e.Team != c.Team).ToList();
                var nearest = enemies.OrderBy(e => grid.GetDistance(c.GridPosition, e.GridPosition)).FirstOrDefault();
                c.UpdateCoverStatus(grid, nearest);
            }
        }

        private void RebuildTurnQueue()
        {
            var aliveOrdered = combatants
                .Where(c => c != null && c.IsAlive && !c.IsExtracted)
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

        public void ApplyMoraleShock(TeamSide team, int penalty, string reason)
        {
            foreach (var ally in combatants.Where(c => c != null && c.IsAlive && !c.IsExtracted && c.Team == team))
            {
                if (ally.IsImmuneToPsychologicalEffects)
                {
                    if (fx != null && grid != null)
                    {
                        Vector3 pos = grid.GridToWorld(ally.GridPosition);
                        fx.SpawnFloatingText(pos, "RAT IMMUNE", new Color(0.5f, 0.8f, 1f));
                    }
                    continue;
                }

                ally.ReduceRationality(penalty);
                if (fx != null && grid != null)
                {
                    Vector3 pos = grid.GridToWorld(ally.GridPosition);
                    fx.SpawnFloatingText(pos, $"-{penalty}% RAT", new Color(1f, 0.35f, 0.35f));
                }
            }
            AddLog($"[MORALE SHOCK] Squad witnessed {reason}! Rationality -{penalty}%.");
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
            if (_currentActor == null || !_currentActor.IsAlive || _currentActor.IsExtracted)
            {
                _isTurnInProgress = false;
                yield break;
            }

            globalTurnCounter++;

            // Decrement skill cooldown pada awal giliran unit
            _currentActor.DecrementSkillCooldown();

            // Reset status defending awal giliran
            _currentActor.SetDefending(false);

            // Update cover status terhadap ancaman musuh terdekat
            var enemies = combatants.Where(c => c.IsAlive && !c.IsExtracted && c.Team != _currentActor.Team).ToList();
            var nearestEnemy = enemies.OrderBy(e => grid.GetDistance(_currentActor.GridPosition, e.GridPosition)).FirstOrDefault();
            _currentActor.UpdateCoverStatus(grid, nearestEnemy);

            // 2. Minta keputusan dari controller agen aktif (HFSM / Pure LLM / Hybrid).
            //    Panggilan asinkron agar inferensi LLM tidak membekukan main thread Unity.
            if (_agentController == null)
            {
                if (agentController == null) agentController = gameObject.AddComponent<HFSMTacticalAgent>();
                _agentController = agentController;
            }

            _agentController.TurnContext = new TacticalTurnContext(roundNumber, globalTurnCounter);

            Task<TacticalDecision2D> decisionTask = null;
            try
            {
                decisionTask = _agentController.DecideTurnAsync(
                    _currentActor, grid, combatants, _combatLogs);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BattleManager2D] Agent controller threw synchronously: {ex}");
            }

            if (decisionTask != null)
            {
                while (!decisionTask.IsCompleted)
                    yield return null;
            }

            TacticalDecision2D decision;
            if (decisionTask == null || decisionTask.IsFaulted || decisionTask.IsCanceled)
            {
                string error = decisionTask == null
                    ? "controller returned null task"
                    : (decisionTask.Exception != null
                        ? decisionTask.Exception.GetBaseException().Message
                        : "task cancelled");

                Debug.LogError($"[BattleManager2D] Agent decision failed: {error}");
                AddLog($"[AI ERROR] {_currentActor.EntityName}: decision pipeline failed ({error}). Turn treated as Idle no-op.");
                decision = new TacticalDecision2D(
                    TacticalActionType.Idle,
                    _currentActor.GridPosition,
                    null,
                    false,
                    "Infrastructure error: " + error);
            }
            else
            {
                decision = decisionTask.Result;
            }

            if (decision == null)
            {
                decision = new TacticalDecision2D(
                    TacticalActionType.Idle,
                    _currentActor.GridPosition,
                    null,
                    false,
                    "Agent returned a null decision.");
            }

            // Perbarui Telemetri Inspector langsung pada CombatEntity2D!
            string decisionTag = (_agentController as TacticalAgentControllerBase)?.LastDecisionTag;
            string actionLabel = string.IsNullOrEmpty(decisionTag)
                ? decision.Action.ToString()
                : decisionTag + " " + decision.Action;

            _currentActor.SetActionTelemetry(
                actionLabel,
                decision.Reasoning,
                decision.TargetEnemy?.EntityId ?? decision.TargetAlly?.EntityId ?? "",
                decision.MoveDestination);

            AddLog($"[{_agentController.ControllerType}] {_currentActor.EntityName} -> {actionLabel}: {decision.Reasoning}");

            // Pemicu Morale Shock jika unit memutuskan untuk Flee (pertama kali)
            if (decision.Action == TacticalActionType.Flee && !_fleeShockedEntities.Contains(_currentActor))
            {
                _fleeShockedEntities.Add(_currentActor);
                ApplyMoraleShock(_currentActor.Team, 20, $"{_currentActor.EntityName} fleeing");
            }

            // 3. Eksekusi Pergerakan jika ada (Move, MoveToCover, MoveAndShoot, Flee, MoveAndCastSkill)
            if (decision.Action == TacticalActionType.Move ||
                decision.Action == TacticalActionType.MoveToCover || 
                decision.Action == TacticalActionType.MoveAndShoot || 
                decision.Action == TacticalActionType.Flee ||
                decision.Action == TacticalActionType.MoveAndCastSkill)
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
                    enemies = combatants.Where(c => c.IsAlive && !c.IsExtracted && c.Team != _currentActor.Team).ToList();
                    nearestEnemy = enemies.OrderBy(e => grid.GetDistance(_currentActor.GridPosition, e.GridPosition)).FirstOrDefault();
                    _currentActor.UpdateCoverStatus(grid, nearestEnemy);
                    RefreshAllCoverStatuses();

                    if (decision.Action == TacticalActionType.Flee)
                    {
                        AddLog($"{_currentActor.EntityName} is fleeing towards extraction! Moved to ({_currentActor.GridPosition.x}, {_currentActor.GridPosition.y}).");
                    }
                    else
                    {
                        AddLog($"{_currentActor.EntityName} moved to ({_currentActor.GridPosition.x}, {_currentActor.GridPosition.y}) [Cover: {_currentActor.ActiveCoverType}].");
                    }
                    yield return new WaitForSeconds(0.1f);
                }
            }

            // 4. Eksekusi Tembakan jika ada
            if (decision.Action == TacticalActionType.Shoot || decision.Action == TacticalActionType.MoveAndShoot)
            {
                var target = decision.TargetEnemy;
                if (target != null)
                {
                    // Pure LLM direct execution: tembakan ke target mati tetap dipanggil.
                    // ApplyDamage mengembalikan 0 untuk target mati; pelanggaran dicatat classifier.
                    bool targetWasAlive = target.IsAlive;
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

                    // Pemicu Floating Text & Intercept Check
                    if (target.LastDamageWasRedirected && target.LastGuardian != null)
                    {
                        Vector3 guardianPos = grid.GridToWorld(target.LastGuardian.GridPosition);
                        if (fx != null)
                        {
                            fx.SpawnFloatingText(targetPos, "INTERCEPTED!", new Color(0.3f, 0.85f, 1f));
                            fx.SpawnFloatingText(guardianPos, $"GUARD HIT -{dealtDmg}", new Color(1f, 0.6f, 0.2f));
                        }
                        AddLog($"[GUARD INTERCEPT] {target.LastGuardian.EntityName} absorbed {dealtDmg} dmg for {target.EntityName}!");

                        if (!target.LastGuardian.IsAlive)
                        {
                            AddLog($"[ELIMINATED] {target.LastGuardian.EntityName} fell in battle while protecting {target.EntityName}!");
                            grid.ClearOccupant(target.LastGuardian.GridPosition);
                            if (!_deathShockedEntities.Contains(target.LastGuardian))
                            {
                                _deathShockedEntities.Add(target.LastGuardian);
                                ApplyMoraleShock(target.LastGuardian.Team, 40, $"{target.LastGuardian.EntityName} eliminated (K.I.A.)");
                            }
                        }
                    }
                    else
                    {
                        if (fx != null)
                        {
                            if (!targetWasAlive)
                            {
                                fx.SpawnFloatingText(targetPos, "NO EFFECT (DEAD TARGET)", new Color(0.7f, 0.7f, 0.7f));
                            }
                            else
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
                        }

                        string flankTag = decision.IsFlanking ? " [FLANK CRITICAL!]" : "";
                        if (!targetWasAlive)
                        {
                            AddLog($"[INVALID TARGET] {_currentActor.EntityName} fired at already-dead {target.EntityName} (no damage).");
                        }
                        else
                        {
                            AddLog($"{_currentActor.EntityName} fired {_currentActor.WeaponName} at {target.EntityName} dealing {dealtDmg} dmg{flankTag}.");
                        }

                        if (targetWasAlive && !target.IsAlive)
                        {
                            AddLog($"[ELIMINATED] {target.EntityName} was destroyed!");
                            grid.ClearOccupant(target.GridPosition);
                            if (!_deathShockedEntities.Contains(target))
                            {
                                _deathShockedEntities.Add(target);
                                ApplyMoraleShock(target.Team, 40, $"{target.EntityName} eliminated (K.I.A.)");
                            }
                        }
                    }
                }
            }
            else if (decision.Action == TacticalActionType.CastSkill || decision.Action == TacticalActionType.MoveAndCastSkill)
            {
                var allyTarget = decision.TargetAlly;
                if (allyTarget != null)
                {
                    // Direct execution: skill pada target mati / musuh tetap dicoba apa adanya.
                    bool targetWasAlive = allyTarget.IsAlive;
                    string skillId = decision.SkillId ?? string.Empty;

                    Vector3 casterPos = grid.GridToWorld(_currentActor.GridPosition);
                    Vector3 targetPos = grid.GridToWorld(allyTarget.GridPosition);

                    if (string.Equals(skillId, "Guard", StringComparison.OrdinalIgnoreCase))
                    {
                        _currentActor.PutSkillOnCooldown(2);
                        allyTarget.ApplyGuard(_currentActor, 2);
                        allyTarget.Stats.RestoreRationality(10);

                        if (fx != null)
                        {
                            fx.SpawnFloatingText(targetPos, "GUARDED! (+10% RAT)", new Color(0.2f, 0.85f, 1f));
                            fx.PlayBulletTracer(casterPos, targetPos, false, null);
                        }

                        if (targetWasAlive)
                            AddLog($"[SKILL: GUARD] {_currentActor.EntityName} cast Guard on {allyTarget.EntityName}! (Damage redirected for 2 turns, +10% Rationality).");
                        else
                            AddLog($"[INVALID TARGET] {_currentActor.EntityName} attempted Guard on already-dead {allyTarget.EntityName}.");
                    }
                    else if (string.Equals(skillId, "Heal", StringComparison.OrdinalIgnoreCase))
                    {
                        _currentActor.PutSkillOnCooldown(2);
                        allyTarget.Heal(50);
                        allyTarget.Stats.RestoreRationality(10);

                        if (fx != null)
                        {
                            fx.SpawnFloatingText(targetPos, "+50 HP (+10% RAT)", new Color(0.2f, 1f, 0.4f));
                            fx.PlayBulletTracer(casterPos, targetPos, false, null);
                        }

                        if (targetWasAlive)
                            AddLog($"[SKILL: HEAL] {_currentActor.EntityName} healed {allyTarget.EntityName} (+50 HP, +10% Rationality)!");
                        else
                            AddLog($"[INVALID TARGET] {_currentActor.EntityName} attempted Heal on already-dead {allyTarget.EntityName}.");
                    }
                    else
                    {
                        AddLog($"[SKILL: UNKNOWN] {_currentActor.EntityName} attempted to cast '{skillId}' on {allyTarget.EntityName} (no engine effect).");
                    }

                    // Cek apakah flee command pada target ter-abort karena pemulihan HP & Rationality
                    if (targetWasAlive)
                    {
                        var allyTrait = TacticalTraitFactory.GetTrait(allyTarget.CurrentTraitId);
                        if (allyTarget.CheckAndApplyFleeAbort(allyTrait.FleeHpThreshold, allyTrait.FleeHpThreshold))
                        {
                            _fleeShockedEntities.Remove(allyTarget);
                            AddLog($"[RALLY] {allyTarget.EntityName}'s health and composure were restored above threshold! Flee command aborted.");
                            if (fx != null) fx.SpawnFloatingText(targetPos, "RALLIED!", Color.green);
                        }
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
            else if (decision.Action == TacticalActionType.Flee)
            {
                // Cek apakah aktor telah mencapai petak ekstraksi
                if (grid != null && grid.IsExtractionTile(_currentActor.GridPosition))
                {
                    Vector3 evacPos = grid.GridToWorld(_currentActor.GridPosition);
                    if (fx != null) fx.SpawnFloatingText(evacPos, "EXTRACTED!", new Color(0.2f, 1f, 0.85f));

                    grid.ClearOccupant(_currentActor.GridPosition);
                    _currentActor.SetExtracted();

                    AddLog($"[EXTRACTED] {_currentActor.EntityName} reached the extraction zone and safely evacuated the battlefield!");
                    yield return new WaitForSeconds(0.2f);
                }
                else
                {
                    Vector3 actorPos = grid.GridToWorld(_currentActor.GridPosition);
                    if (fx != null) fx.SpawnFloatingText(actorPos, "FLEEING!", new Color(1f, 0.65f, 0.2f));
                }
            }
            else if (decision.Action == TacticalActionType.Idle)
            {
                Vector3 actorPos = grid.GridToWorld(_currentActor.GridPosition);
                if (fx != null) fx.SpawnFloatingText(actorPos, "IDLE / NO-OP", new Color(0.65f, 0.65f, 0.7f));
                AddLog($"{_currentActor.EntityName} passed the turn (Idle / no-op).");
            }

            yield return new WaitForSeconds(0.12f);

            // Akhiri giliran unit jika belum dievakuasi
            if (!_currentActor.IsExtracted)
            {
                _currentActor.DecrementGuardDuration();
                _currentActor.SetActionTelemetry("TurnEnded", "Turn completed.");
            }
            _currentActor = null;

            CheckBattleOutcome();
            _isTurnInProgress = false;
        }

        private void CheckBattleOutcome()
        {
            bool allEnemiesDone = combatants.Where(c => c.Team == TeamSide.EnemyTeam).All(c => !c.IsAlive || c.IsExtracted);
            bool allSquadDone = combatants.Where(c => c.Team == TeamSide.PlayerTeam).All(c => !c.IsAlive || c.IsExtracted);
            bool anySquadExtracted = combatants.Where(c => c.Team == TeamSide.PlayerTeam).Any(c => c.IsExtracted);
            bool allSquadDead = combatants.Where(c => c.Team == TeamSide.PlayerTeam).All(c => !c.IsAlive);

            if (allEnemiesDone && !allSquadDead)
            {
                outcome = BattleOutcome.Victory;
                AddLog("=== VICTORY: All hostile mechanized units eliminated! ===");
            }
            else if (allSquadDone)
            {
                if (anySquadExtracted)
                {
                    outcome = BattleOutcome.Victory;
                    AddLog("=== TACTICAL RETREAT: Surviving squad safely extracted from Sector 7 Overpass! ===");
                }
                else
                {
                    outcome = BattleOutcome.Defeat;
                    AddLog("=== DEFEAT: Squad wiped out on Sector 7 Overpass. ===");
                }
            }

            TryAutoExportTelemetry();
        }

        private CombatTelemetryLogger2D ResolveTelemetryLogger()
        {
            if (telemetryLogger == null)
                telemetryLogger = FindFirstObjectByType<CombatTelemetryLogger2D>();
            return telemetryLogger;
        }

        private void TryAutoExportTelemetry()
        {
            if (!autoExportTelemetry || _telemetryExported) return;
            if (outcome == BattleOutcome.InProgress) return;

            var logger = ResolveTelemetryLogger();
            if (logger == null || logger.Records.Count == 0) return;

            _telemetryExported = true;
            logger.ExportToCsv();
            logger.ExportToJson();
        }

        /// <summary>Ekspor manual telemetri (dapat dipanggil dari HUD/editor).</summary>
        public void ExportTelemetryNow()
        {
            var logger = ResolveTelemetryLogger();
            if (logger != null) logger.ExportAll();
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
