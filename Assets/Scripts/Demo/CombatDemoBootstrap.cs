using System.Collections.Generic;
using UnityEngine;
using RPGStrategy.Core.Combat;
using RPGStrategy.Core.Command;

namespace RPGStrategy.Demo
{
    public class CombatDemoBootstrap : MonoBehaviour
    {
        private BattleManager _battleManager;
        private bool _autoRun = false;
        private float _autoRunTimer = 0f;
        [SerializeField] private float autoRunInterval = 0.8f;

        private void Start()
        {
            Setup6PersonSquadBattle();
        }

        public void Setup6PersonSquadBattle()
        {
            if (_battleManager == null)
            {
                var bmGo = new GameObject("BattleManager");
                bmGo.transform.SetParent(this.transform);
                _battleManager = bmGo.AddComponent<BattleManager>();
            }

            foreach (Transform child in transform)
            {
                if (child.gameObject != _battleManager.gameObject)
                {
                    Destroy(child.gameObject);
                }
            }

            var combatants = new List<CombatEntity>();

            // =========================================================
            // REGU 6 PRAJURIT SUBORDINAT
            // =========================================================
            // Unit 01: Marcus "Grizzly" Vance (Pointman Heavy)
            var marcus = CreateSquadUnit("u1_marcus", "Marcus (Pointman)", 150, 30, 24, 18, 12, 90, "LoyalProtector", 15f);
            combatants.Add(marcus);

            // Unit 02: Elena "Wraith" Ramos (Sniper Recon)
            var elena = CreateSquadUnit("u2_elena", "Elena (Sniper)", 70, 65, 38, 8, 20, 95, "CautiousTactician", 25f);
            combatants.Add(elena);

            // Unit 03: Kaelen "Hellhound" Cruz (Shock Raider)
            var kaelen = CreateSquadUnit("u3_kaelen", "Kaelen (CQB Raider)", 100, 25, 40, 10, 22, 60, "AggressiveBerserker", 20f);
            combatants.Add(kaelen);

            // Unit 04: Jax "Anvil" Thorne (Support Gunner)
            var jax = CreateSquadUnit("u4_jax", "Jax (Support Gunner)", 130, 40, 32, 15, 14, 85, "SteadfastGuardian", 18f);
            combatants.Add(jax);

            // Unit 05: Toby "Sparrow" Miller (Field Medic)
            var toby = CreateSquadUnit("u5_toby", "Toby (Medic)", 65, 75, 14, 6, 24, 50, "TimidAltruist", 12f);
            var medikit = ScriptableObject.CreateInstance<ItemData>();
            medikit.Initialize("Item_Medikit", "Field Medikit", "Restores 45 HP.", ItemEffectType.RestoreHP, 45);
            toby.AddItem(medikit, 3);
            combatants.Add(toby);

            // Unit 06: Tasha "Viper" Lin (Recon Flanker)
            var tasha = CreateSquadUnit("u6_tasha", "Tasha (Flanker)", 85, 45, 30, 10, 26, 88, "DisciplinedProfessional", 22f);
            combatants.Add(tasha);

            // =========================================================
            // JARING RELASI EMOSIONAL ANTAR-PRAOwnerURIT
            // =========================================================
            marcus.SetRelationship("u2_elena", 95);  // Sworn Bond (+95)
            marcus.SetRelationship("u5_toby", 85);   // Paternal Bond (+85)
            elena.SetRelationship("u1_marcus", 60);  // Trusted Comrade (+60)
            elena.SetRelationship("u3_kaelen", -40); // Rivalitas (-40)
            kaelen.SetRelationship("u4_jax", 65);    // Sahabat tempur (+65)
            tasha.SetRelationship("u1_marcus", 50);  // Respek militer (+50)

            // =========================================================
            // PASUKAN MUSUH
            // =========================================================
            // Boss: HOUND-IV Heavy Siege Strider
            var boss = CreateSquadUnit("boss_hound", "HOUND-IV Heavy Strider", 350, 40, 45, 20, 15, 100, "AutomatedMachine", 35f, TeamSide.EnemyTeam);
            combatants.Add(boss);

            // Drone 1: Scout Drone Alpha
            var drone1 = CreateSquadUnit("enemy_drone_1", "Scrapper Drone Alpha", 50, 10, 18, 4, 25, 100, "AutomatedMachine", 30f, TeamSide.EnemyTeam);
            combatants.Add(drone1);

            // Drone 2: Scout Drone Beta
            var drone2 = CreateSquadUnit("enemy_drone_2", "Scrapper Drone Beta", 50, 10, 18, 4, 25, 100, "AutomatedMachine", 32f, TeamSide.EnemyTeam);
            combatants.Add(drone2);

            _battleManager.InitializeBattle(combatants, maxTurnLimit: 40);
        }

        private CombatEntity CreateSquadUnit(
            string id, 
            string name, 
            int hp, 
            int mp, 
            int atk, 
            int def, 
            int spd, 
            int rationality, 
            string trait, 
            float dist, 
            TeamSide team = TeamSide.PlayerTeam)
        {
            var go = new GameObject(name);
            go.transform.SetParent(this.transform);
            var entity = go.AddComponent<CombatEntity>();
            entity.Initialize(id, name, team, new EntityStats(hp, mp, atk, def, spd, rationality), trait, dist);
            return entity;
        }

        private void Update()
        {
            if (_autoRun && _battleManager != null && _battleManager.Outcome == BattleOutcome.InProgress)
            {
                _autoRunTimer += Time.deltaTime;
                if (_autoRunTimer >= autoRunInterval)
                {
                    _autoRunTimer = 0f;
                    StepTurn();
                }
            }
        }

        private async void StepTurn()
        {
            if (_battleManager != null && _battleManager.Outcome == BattleOutcome.InProgress)
            {
                await _battleManager.ExecuteSingleTurnAsync();
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(15, 15, 520, Screen.height - 30));

            GUILayout.Label("=== PROJECT IRON COLOSSUS: COMMANDER DECK ===", GUI.skin.box);
            if (_battleManager == null)
            {
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"Ronde: {_battleManager.RoundNumber} | Putaran: {_battleManager.GlobalTurnCounter} | Status: {_battleManager.Outcome}");

            // Tampilkan Perintah Aktif Komandan
            var directive = _battleManager.ActiveDirective;
            string directiveStr = directive != null ? $"{directive.Type}: \"{directive.DirectiveText}\"" : "Free Engage";
            GUILayout.Label($"[RADIO BROADCAST PERINTAH AKTIF]:\n{directiveStr}", GUI.skin.box);

            // Tombol Pemilihan Direktif Komandan
            GUILayout.Label("KIRIMKAN DIREKTIF RADIO KOMANDAN:", GUI.skin.label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Fokus Drone"))
            {
                _battleManager.SetCommanderDirective(DirectiveType.FocusFireTarget, "enemy_drone_1", "Focus fire on the flank drones first!");
            }
            if (GUILayout.Button("Pertahankan Posisi"))
            {
                _battleManager.SetCommanderDirective(DirectiveType.DefensiveHold, "", "Hunker down behind barricades and hold line!");
            }
            if (GUILayout.Button("MUNDUR DARURAT!"))
            {
                _battleManager.SetCommanderDirective(DirectiveType.FullTacticalRetreat, "", "All units abort and fall back to the transport!");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            GUI.enabled = _battleManager.Outcome == BattleOutcome.InProgress;
            if (GUILayout.Button("Eksekusi 1 Giliran (Step)"))
            {
                StepTurn();
            }
            if (GUILayout.Button(_autoRun ? "Stop Auto" : "Auto Run"))
            {
                _autoRun = !_autoRun;
                _autoRunTimer = 0f;
            }
            GUI.enabled = true;
            if (GUILayout.Button("Reset Misi"))
            {
                _autoRun = false;
                Setup6PersonSquadBattle();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("TELEMETRI REGU PRAJURIT (6 PERSON SQUAD):", GUI.skin.box);
            foreach (var c in _battleManager.Combatants)
            {
                if (c == null) continue;
                string status = c.IsAlive 
                    ? $"HP:{c.Stats.CurrentHP}/{c.Stats.MaxHP} MP:{c.Stats.CurrentMP} Rasionalitas:{c.Stats.Rationality} [{c.Stats.ComposureLevel}]" 
                    : "[KILLED IN ACTION]";
                
                string traitBadge = c.IsAlive ? $" <{c.CurrentTraitId}>" : "";
                string distBadge = c.IsAlive ? $" ({c.FieldDistance:F0}m)" : "";
                string tag = c.Team == TeamSide.PlayerTeam ? "[SQUAD]" : "[ENEMY]";

                string effects = "";
                foreach (var eff in c.ActiveEffects)
                {
                    effects += $" [{eff.Type}]";
                }
                if (!string.IsNullOrEmpty(c.CoveringTargetId)) effects += $" [Covering:{c.CoveringTargetId}]";
                if (!string.IsNullOrEmpty(c.CoveredByActorId)) effects += $" [ShieldedBy:{c.CoveredByActorId}]";
                if (!string.IsNullOrEmpty(c.CarryingTargetId)) effects += $" [Dragging:{c.CarryingTargetId}]";

                GUILayout.Label($"{tag} {c.EntityName}{traitBadge}{distBadge}:\n  {status}{effects}");
            }

            GUILayout.Space(8);
            GUILayout.Label("TRANSKRIP RADIO & LOG KEJADIAN:", GUI.skin.box);
            foreach (var ev in _battleManager.RecentEvents)
            {
                GUILayout.Label($"- {ev}");
            }

            GUILayout.EndArea();
        }
    }
}
