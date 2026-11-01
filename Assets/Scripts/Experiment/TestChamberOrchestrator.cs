using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RPGStrategy.Core.Combat;
using RPGStrategy.Core.Command;
using RPGStrategy.AI.Core;
using RPGStrategy.AI.Baseline;
using RPGStrategy.AI.LLM;
using RPGStrategy.AI.Hybrid;

namespace RPGStrategy.Experiment
{
    public enum SquadControllerType
    {
        HFSM,
        Hybrid,
        PureLLM
    }

    /// <summary>
    /// Runtime orchestrator for the Test Chamber scene.
    /// Manages controller binding, directive execution, step-by-step turn progression,
    /// auto-run simulation, and live OnGUI telemetry monitoring for Omar Ginza's research prototype.
    /// </summary>
    public class TestChamberOrchestrator : MonoBehaviour
    {
        [Header("Chamber References")]
        [SerializeField] private BattleManager battleManager;

        [Header("Scenario Configuration")]
        [SerializeField] private ScenarioPresets.ScenarioId scenarioId = ScenarioPresets.ScenarioId.ScenarioA;
        [SerializeField] private SquadControllerType controllerType = SquadControllerType.HFSM;
        [SerializeField] private LLMConfig llmConfig;

        [Header("Initial Commander Directive")]
        [SerializeField] private DirectiveType initialDirectiveType = DirectiveType.FocusFireTarget;
        [SerializeField] private string initialDirectiveTarget = "enemy_hound_iv";
        [SerializeField] private string initialDirectiveText = "All units focus fire on primary target! Eliminate it quickly!";

        [Header("Runtime Controls")]
        [SerializeField] private bool autoRun = false;
        [SerializeField] private float autoRunInterval = 0.8f;
        [SerializeField] private int maxTurnLimit = 50;

        private float _autoRunTimer = 0f;
        private bool _isStepping = false;
        private Vector2 _logScrollPosition = Vector2.zero;
        private Vector2 _squadScrollPosition = Vector2.zero;

        public BattleManager BattleManager => battleManager;
        public ScenarioPresets.ScenarioId ScenarioId { get => scenarioId; set => scenarioId = value; }
        public SquadControllerType ControllerType { get => controllerType; set => controllerType = value; }
        public LLMConfig LLMConfig { get => llmConfig; set => llmConfig = value; }
        public DirectiveType InitialDirectiveType { get => initialDirectiveType; set => initialDirectiveType = value; }
        public string InitialDirectiveTarget { get => initialDirectiveTarget; set => initialDirectiveTarget = value; }
        public string InitialDirectiveText { get => initialDirectiveText; set => initialDirectiveText = value; }

        private void Awake()
        {
            if (battleManager == null)
            {
                battleManager = GetComponentInChildren<BattleManager>();
                if (battleManager == null)
                {
                    battleManager = FindFirstObjectByType<BattleManager>();
                }
            }

            if (llmConfig == null)
            {
                llmConfig = Resources.Load<LLMConfig>("LLMConfig");
            }
        }

        private void Start()
        {
            InitializeChamber();
        }

        public void InitializeChamber()
        {
            if (battleManager == null)
            {
                Debug.LogError("[TestChamberOrchestrator] BattleManager not found in scene hierarchy!");
                return;
            }

            var allCombatants = GetComponentsInChildren<CombatEntity>(true).ToList();

            // Jika belum ada CombatEntity di hierarki (misal dimainkan dari scene kosong), bangun otomatis
            if (allCombatants.Count == 0)
            {
                BuildDynamicCombatants(out allCombatants);
            }

            var squad = allCombatants.Where(c => c.Team == TeamSide.PlayerTeam).ToList();
            var enemies = allCombatants.Where(c => c.Team == TeamSide.EnemyTeam).ToList();

            // Inisialisasi LLM Service jika diperlukan
            LLMService llmService = null;
            if (controllerType != SquadControllerType.HFSM)
            {
                if (llmConfig == null)
                {
                    Debug.LogWarning("[TestChamberOrchestrator] LLMConfig is null, creating fallback runtime config.");
                    llmConfig = ScriptableObject.CreateInstance<LLMConfig>();
                }
                llmService = new LLMService(llmConfig);
            }

            // Pasangkan AI Controller ke setiap prajurit regu
            foreach (var member in squad)
            {
                member.AssignedController = CreateSquadController(llmService);
            }

            // Musuh menggunakan HFSM deterministik baseline
            foreach (var enemy in enemies)
            {
                enemy.AssignedController = new HFSMController();
            }

            // Pasang relasi emosional untuk Skenario D jika relevan
            if (scenarioId == ScenarioPresets.ScenarioId.ScenarioD)
            {
                var relationships = ScenarioPresets.GetRelationshipsD();
                foreach (var rel in relationships)
                {
                    var source = squad.Find(s => s.EntityId == rel.source);
                    source?.SetRelationship(rel.target, rel.affinity);
                }
            }

            // Mulai pertempuran
            battleManager.InitializeBattle(allCombatants, maxTurnLimit);

            // Terapkan direktif komandan awal
            battleManager.SetCommanderDirective(initialDirectiveType, initialDirectiveTarget, initialDirectiveText);

            Debug.Log($"[TestChamberOrchestrator] Chamber initialized: Scenario={scenarioId}, Controller={controllerType}, Combatants={allCombatants.Count}");
        }

        private IAgentController CreateSquadController(LLMService llmService)
        {
            switch (controllerType)
            {
                case SquadControllerType.Hybrid:
                    return new HybridAgentController(llmService);
                case SquadControllerType.PureLLM:
                    return new PureLLMAgent(llmService);
                case SquadControllerType.HFSM:
                default:
                    return new HFSMController();
            }
        }

        private void BuildDynamicCombatants(out List<CombatEntity> createdCombatants)
        {
            createdCombatants = new List<CombatEntity>();

            List<EntitySpawnData> squadData, enemyData;
            switch (scenarioId)
            {
                case ScenarioPresets.ScenarioId.ScenarioB:
                    squadData = ScenarioPresets.GetSquadB();
                    enemyData = ScenarioPresets.GetEnemiesB();
                    break;
                case ScenarioPresets.ScenarioId.ScenarioC:
                    squadData = ScenarioPresets.GetSquadC();
                    enemyData = ScenarioPresets.GetEnemiesC();
                    break;
                case ScenarioPresets.ScenarioId.ScenarioD:
                    squadData = ScenarioPresets.GetSquadD();
                    enemyData = ScenarioPresets.GetEnemiesD();
                    break;
                default:
                    squadData = ScenarioPresets.GetSquadA();
                    enemyData = ScenarioPresets.GetEnemiesA();
                    break;
            }

            var squadParent = new GameObject("_Squad").transform;
            squadParent.SetParent(this.transform);

            foreach (var s in squadData)
            {
                var go = new GameObject(s.Name);
                go.transform.SetParent(squadParent);
                var entity = go.AddComponent<CombatEntity>();
                entity.Initialize(s.EntityId, s.Name, s.Team, s.Stats, s.TraitId, s.FieldDistance);
                createdCombatants.Add(entity);
            }

            var enemyParent = new GameObject("_Enemies").transform;
            enemyParent.SetParent(this.transform);

            foreach (var e in enemyData)
            {
                var go = new GameObject(e.Name);
                go.transform.SetParent(enemyParent);
                var entity = go.AddComponent<CombatEntity>();
                entity.Initialize(e.EntityId, e.Name, e.Team, e.Stats, e.TraitId, e.FieldDistance);
                createdCombatants.Add(entity);
            }
        }

        private void Update()
        {
            if (autoRun && battleManager != null && battleManager.Outcome == BattleOutcome.InProgress && !_isStepping)
            {
                _autoRunTimer += Time.deltaTime;
                if (_autoRunTimer >= autoRunInterval)
                {
                    _autoRunTimer = 0f;
                    StepSingleTurn();
                }
            }
        }

        public async void StepSingleTurn()
        {
            if (battleManager == null || battleManager.Outcome != BattleOutcome.InProgress || _isStepping)
            {
                return;
            }

            _isStepping = true;
            try
            {
                await battleManager.ExecuteSingleTurnAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TestChamberOrchestrator] Error during turn execution: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                _isStepping = false;
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(15, 15, 540, Screen.height - 30));

            // Header Deck
            GUILayout.Label("=== PROJECT IRON COLOSSUS: TEST CHAMBER DECK ===", GUI.skin.box);
            if (battleManager == null)
            {
                GUILayout.Label("Status: No BattleManager connected.");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"Skenario: {scenarioId} | AI Controller: {controllerType}");
            GUILayout.Label($"Ronde: {battleManager.RoundNumber} | Putaran Total: {battleManager.GlobalTurnCounter} | Status: {battleManager.Outcome}");

            // Perintah Aktif Komandan
            var dir = battleManager.ActiveDirective;
            string dirStr = dir != null ? $"{dir.Type} [Target: {(string.IsNullOrEmpty(dir.TargetEntityId) ? "None" : dir.TargetEntityId)}]\n\"{dir.DirectiveText}\"" : "Free Engage";
            GUILayout.Label($"[RADIO BROADCAST KOMANDAN]:\n{dirStr}", GUI.skin.box);

            // Tombol Interaktif Pengiriman Direktif
            GUILayout.Label("RADIO TRANSMITTER KOMANDAN (KIRIM PERINTAH):");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Fokus Boss"))
            {
                battleManager.SetCommanderDirective(DirectiveType.FocusFireTarget, "enemy_hound_iv", "All units focus fire on the Heavy Strider!");
            }
            if (GUILayout.Button("Pertahankan Garis"))
            {
                battleManager.SetCommanderDirective(DirectiveType.DefensiveHold, "", "Hold defensive positions behind barricades!");
            }
            if (GUILayout.Button("Mundur Taktis!"))
            {
                battleManager.SetCommanderDirective(DirectiveType.FullTacticalRetreat, "", "Fall back to the transport vehicle immediately!");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Lindungi Korban"))
            {
                battleManager.SetCommanderDirective(DirectiveType.ProtectVulnerableAlly, "", "Prioritize shielding and rescuing wounded comrades!");
            }
            if (GUILayout.Button("Bebas Terlibat"))
            {
                battleManager.SetCommanderDirective(DirectiveType.FreeEngage, "", "Free engagement at your discretion.");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Kontrol Eksekusi Turn
            GUILayout.BeginHorizontal();
            GUI.enabled = battleManager.Outcome == BattleOutcome.InProgress && !_isStepping;
            if (GUILayout.Button(_isStepping ? "Memproses..." : "Langkah 1 Giliran (Step)"))
            {
                StepSingleTurn();
            }
            GUI.enabled = battleManager.Outcome == BattleOutcome.InProgress;
            if (GUILayout.Button(autoRun ? "Hentikan Auto-Run" : "Mulai Auto-Run"))
            {
                autoRun = !autoRun;
                _autoRunTimer = 0f;
            }
            GUI.enabled = true;
            if (GUILayout.Button("Reset Pertempuran"))
            {
                autoRun = false;
                InitializeChamber();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Telemetri Prajurit & Musuh
            GUILayout.Label("STATUS UNIT TEMPUR (TELEMETRI SQUAD & ENEMY):", GUI.skin.box);
            _squadScrollPosition = GUILayout.BeginScrollView(_squadScrollPosition, GUILayout.Height(170));
            foreach (var c in battleManager.Combatants)
            {
                if (c == null) continue;
                string teamTag = c.Team == TeamSide.PlayerTeam ? "[SQUAD]" : "[ENEMY]";
                string status = c.IsAlive 
                    ? $"HP:{c.Stats.CurrentHP}/{c.Stats.MaxHP} MP:{c.Stats.CurrentMP} Rasio:{c.Stats.Rationality} [{c.Stats.ComposureLevel}]"
                    : "[KILLED IN ACTION]";
                
                string traitBadge = c.IsAlive ? $" <{c.CurrentTraitId}>" : "";
                string distBadge = c.IsAlive ? $" ({c.FieldDistance:F0}m ke transport)" : "";

                string tacticalBadges = "";
                if (!string.IsNullOrEmpty(c.CoveringTargetId)) tacticalBadges += $" [Shielding: {c.CoveringTargetId}]";
                if (!string.IsNullOrEmpty(c.CoveredByActorId)) tacticalBadges += $" [CoveredBy: {c.CoveredByActorId}]";
                if (!string.IsNullOrEmpty(c.CarryingTargetId)) tacticalBadges += $" [Dragging: {c.CarryingTargetId}]";
                if (c.IsDefending) tacticalBadges += " [DEFENDING]";
                if (c.IsRetreating) tacticalBadges += " [RETREATING]";

                GUILayout.Label($"{teamTag} {c.EntityName}{traitBadge}{distBadge}\n  {status}{tacticalBadges}");
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8);

            // Transkrip Radio & Log Kejadian
            GUILayout.Label("LOG TRANSKRIP RADIO & KEJADIAN TEMPUR:", GUI.skin.box);
            _logScrollPosition = GUILayout.BeginScrollView(_logScrollPosition, GUILayout.Height(150));
            var events = battleManager.RecentEvents;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                GUILayout.Label($"• {events[i]}");
            }
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }
    }
}
