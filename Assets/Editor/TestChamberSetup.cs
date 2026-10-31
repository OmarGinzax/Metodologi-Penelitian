using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RPGStrategy.Core.Combat;
using RPGStrategy.Core.Command;
using RPGStrategy.AI.LLM;
using RPGStrategy.Experiment;

namespace RPGStrategy.Editor
{
    /// <summary>
    /// Unity Editor script to prepare and configure the Test Chamber scene and battle scenarios
    /// for Omar Ginza's LLM NPC Decision-Making research prototype.
    /// Provides one-click setup via MenuItems and an interactive EditorWindow.
    /// </summary>
    public class TestChamberSetup : EditorWindow
    {
        private ScenarioPresets.ScenarioId _selectedScenario = ScenarioPresets.ScenarioId.ScenarioA;
        private SquadControllerType _controllerType = SquadControllerType.HFSM;
        private LLMConfig _llmConfig;

        private DirectiveType _directiveType = DirectiveType.FocusFireTarget;
        private string _directiveTarget = "enemy_hound_iv";
        private string _directiveText = "All units focus fire on primary target! Eliminate it quickly!";

        private bool _createVisualMarkers = true;
        private Vector2 _windowScrollPos = Vector2.zero;

        [MenuItem("RPGStrategy/Test Chamber/Open Test Chamber Control Window", priority = 0)]
        [MenuItem("Window/RPG Strategy/Test Chamber Setup Window", priority = 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<TestChamberSetup>("Test Chamber Setup");
            window.minSize = new Vector2(480, 580);
            window.Show();
        }

        // =========================================================================
        // ONE-CLICK MENU ITEMS
        // =========================================================================

        [MenuItem("RPGStrategy/Test Chamber/Prepare Scenario A (Standard 6v3)", priority = 10)]
        public static void PrepareScenarioA()
        {
            SetupChamber(ScenarioPresets.ScenarioId.ScenarioA, SquadControllerType.HFSM, true);
        }

        [MenuItem("RPGStrategy/Test Chamber/Prepare Scenario B (Reduced Squad 4v2)", priority = 11)]
        public static void PrepareScenarioB()
        {
            SetupChamber(ScenarioPresets.ScenarioId.ScenarioB, SquadControllerType.HFSM, true);
        }

        [MenuItem("RPGStrategy/Test Chamber/Prepare Scenario C (Demoralized Squad 6v3)", priority = 12)]
        public static void PrepareScenarioC()
        {
            SetupChamber(ScenarioPresets.ScenarioId.ScenarioC, SquadControllerType.HFSM, true);
        }

        [MenuItem("RPGStrategy/Test Chamber/Prepare Scenario D (High Bonds Squad 6v3)", priority = 13)]
        public static void PrepareScenarioD()
        {
            SetupChamber(ScenarioPresets.ScenarioId.ScenarioD, SquadControllerType.HFSM, true);
        }

        [MenuItem("RPGStrategy/Test Chamber/Clear Test Chamber", priority = 30)]
        public static void ClearChamber()
        {
            var root = GameObject.Find("[TestChamber]");
            if (root != null)
            {
                Undo.DestroyObjectImmediate(root);
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                Debug.Log("[TestChamberSetup] Cleared [TestChamber] hierarchy from scene.");
            }
            else
            {
                Debug.Log("[TestChamberSetup] No [TestChamber] root found in the active scene.");
            }
        }

        private void OnEnable()
        {
            _llmConfig = EnsureDefaultLLMConfigAsset();
        }

        private void OnGUI()
        {
            _windowScrollPos = EditorGUILayout.BeginScrollView(_windowScrollPos);

            // Header Banner
            EditorGUILayout.Space(8);
            GUILayout.Label("PROJECT IRON COLOSSUS", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("LLM NPC Decision-Making Prototype Test Chamber Setup", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox(
                "Use this tool to generate the battle chamber hierarchy with squad members, enemies, " +
                "commander directive, and controller bindings for reproducible experiments.", 
                MessageType.Info);

            EditorGUILayout.Space(8);

            // 1. Scenario Selection
            EditorGUILayout.LabelField("1. Scenario Selection", EditorStyles.boldLabel);
            _selectedScenario = (ScenarioPresets.ScenarioId)EditorGUILayout.EnumPopup("Battle Scenario", _selectedScenario);

            string scenarioDescription = _selectedScenario switch
            {
                ScenarioPresets.ScenarioId.ScenarioA => 
                    "Standard Baseline: Full Squad (6 units) vs HOUND-IV Heavy Strider + 2 Scout Drones.",
                ScenarioPresets.ScenarioId.ScenarioB => 
                    "Reduced Squad: 4 units (Sgt. Marcus, Lyra, Torres, Cross) vs 2 Scout Drones (pressure test).",
                ScenarioPresets.ScenarioId.ScenarioC => 
                    "Demoralized Squad: 6 units with low rationality (Base - 40, prone to panic/disobedience) vs HOUND-IV.",
                ScenarioPresets.ScenarioId.ScenarioD => 
                    "High Relationship Squad: 6 units with preset sworn bonds (+90 Marcus-Torres, +75 Lyra-Cross) vs HOUND-IV.",
                _ => ""
            };
            EditorGUILayout.HelpBox(scenarioDescription, MessageType.None);

            EditorGUILayout.Space(8);

            // 2. AI Controller Configuration
            EditorGUILayout.LabelField("2. AI Controller Setup", EditorStyles.boldLabel);
            _controllerType = (SquadControllerType)EditorGUILayout.EnumPopup("Squad Controller", _controllerType);

            _llmConfig = (LLMConfig)EditorGUILayout.ObjectField("LLM Config Asset", _llmConfig, typeof(LLMConfig), false);
            if (_llmConfig == null)
            {
                if (GUILayout.Button("Create / Locate Default LLMConfig Asset"))
                {
                    _llmConfig = EnsureDefaultLLMConfigAsset();
                }
            }

            EditorGUILayout.Space(8);

            // 3. Initial Directive Configuration
            EditorGUILayout.LabelField("3. Initial Commander Directive", EditorStyles.boldLabel);
            _directiveType = (DirectiveType)EditorGUILayout.EnumPopup("Directive Type", _directiveType);
            _directiveTarget = EditorGUILayout.TextField("Target Entity ID", _directiveTarget);
            _directiveText = EditorGUILayout.TextField("Broadcast Radio Text", _directiveText);

            EditorGUILayout.Space(8);

            // 4. Visual Layout Options
            EditorGUILayout.LabelField("4. Visual Representation", EditorStyles.boldLabel);
            _createVisualMarkers = EditorGUILayout.Toggle("Generate 3D Visual Tokens", _createVisualMarkers);

            EditorGUILayout.Space(12);

            // Action Buttons
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
            if (GUILayout.Button("BUILD / REBUILD TEST CHAMBER", GUILayout.Height(38)))
            {
                SetupChamber(_selectedScenario, _controllerType, _createVisualMarkers, _llmConfig, _directiveType, _directiveTarget, _directiveText);
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Clear Test Chamber Hierarchy", GUILayout.Height(24)))
            {
                ClearChamber();
            }

            EditorGUILayout.Space(12);

            // Active Scene Status Card
            DrawSceneStatusCard();

            EditorGUILayout.EndScrollView();
        }

        private void DrawSceneStatusCard()
        {
            var root = GameObject.Find("[TestChamber]");
            EditorGUILayout.LabelField("Active Scene Status", EditorStyles.boldLabel);

            if (root == null)
            {
                EditorGUILayout.HelpBox("No [TestChamber] root currently present in active scene.", MessageType.Warning);
                return;
            }

            var bm = root.GetComponentInChildren<BattleManager>();
            var orch = root.GetComponentInChildren<TestChamberOrchestrator>();
            var combatants = root.GetComponentsInChildren<CombatEntity>();

            int squadCount = combatants.Count(c => c.Team == TeamSide.PlayerTeam);
            int enemyCount = combatants.Count(c => c.Team == TeamSide.EnemyTeam);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"• Root GameObject: Found ({root.name})");
            EditorGUILayout.LabelField($"• BattleManager: {(bm != null ? "Ready" : "Missing!")}");
            EditorGUILayout.LabelField($"• Orchestrator: {(orch != null ? $"Configured ({orch.ScenarioId}, {orch.ControllerType})" : "Missing!")}");
            EditorGUILayout.LabelField($"• Squad Members: {squadCount} units");
            EditorGUILayout.LabelField($"• Hostile Units: {enemyCount} units");
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);
            if (bm != null && GUILayout.Button("Ping BattleManager in Hierarchy"))
            {
                EditorGUIUtility.PingObject(bm.gameObject);
                Selection.activeGameObject = bm.gameObject;
            }
        }

        // =========================================================================
        // CORE SETUP LOGIC
        // =========================================================================

        public static void SetupChamber(
            ScenarioPresets.ScenarioId scenarioId,
            SquadControllerType controllerType = SquadControllerType.HFSM,
            bool createVisuals = true,
            LLMConfig config = null,
            DirectiveType directiveType = DirectiveType.FocusFireTarget,
            string directiveTarget = "enemy_hound_iv",
            string directiveText = "All units focus fire on primary target! Eliminate it quickly!")
        {
            if (config == null)
            {
                config = EnsureDefaultLLMConfigAsset();
            }

            // 1. Dapatkan atau buat root [TestChamber]
            var root = GameObject.Find("[TestChamber]");
            if (root != null)
            {
                Undo.DestroyObjectImmediate(root);
            }

            root = new GameObject("[TestChamber]");
            Undo.RegisterCreatedObjectUndo(root, "Create Test Chamber");

            // 2. Buat _BattleManager
            var bmGo = new GameObject("_BattleManager");
            bmGo.transform.SetParent(root.transform);
            var battleManager = bmGo.AddComponent<BattleManager>();
            var orchestrator = bmGo.AddComponent<TestChamberOrchestrator>();

            orchestrator.ScenarioId = scenarioId;
            orchestrator.ControllerType = controllerType;
            orchestrator.LLMConfig = config;
            orchestrator.InitialDirectiveType = directiveType;
            orchestrator.InitialDirectiveTarget = directiveTarget;
            orchestrator.InitialDirectiveText = directiveText;

            // 3. Dapatkan data skenario
            List<EntitySpawnData> squadData;
            List<EntitySpawnData> enemyData;

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

            var allCombatants = new List<CombatEntity>();

            // 4. Bangun Hierarki Squad
            var squadParent = new GameObject("_Squad");
            squadParent.transform.SetParent(root.transform);

            for (int i = 0; i < squadData.Count; i++)
            {
                var s = squadData[i];
                var squadGo = new GameObject($"[Squad_{i + 1:D2}] {s.Name}");
                squadGo.transform.SetParent(squadParent.transform);

                // Posisi taktis di Sector 7 Overpass
                float xPos = (i - (squadData.Count - 1) * 0.5f) * 2.2f;
                float zPos = s.FieldDistance;
                squadGo.transform.position = new Vector3(xPos, 0f, zPos);

                var entity = squadGo.AddComponent<CombatEntity>();
                entity.Initialize(s.EntityId, s.Name, s.Team, s.Stats, s.TraitId, s.FieldDistance);

                // Tambahkan loadout taktis spesifik
                AttachTacticalLoadout(entity);

                if (createVisuals)
                {
                    CreateTacticalVisualToken(squadGo, new Color(0.2f, 0.6f, 1.0f), 1.8f, 0.7f, s.Name);
                }

                allCombatants.Add(entity);
            }

            // Pasang relasi emosional untuk Skenario D
            if (scenarioId == ScenarioPresets.ScenarioId.ScenarioD)
            {
                var relationships = ScenarioPresets.GetRelationshipsD();
                foreach (var rel in relationships)
                {
                    var source = allCombatants.Find(c => c.EntityId == rel.source);
                    source?.SetRelationship(rel.target, rel.affinity);
                }
            }

            // 5. Bangun Hierarki Musuh
            var enemyParent = new GameObject("_Enemies");
            enemyParent.transform.SetParent(root.transform);

            for (int i = 0; i < enemyData.Count; i++)
            {
                var e = enemyData[i];
                var enemyGo = new GameObject($"[Enemy_{i + 1:D2}] {e.Name}");
                enemyGo.transform.SetParent(enemyParent.transform);

                float xPos = (i - (enemyData.Count - 1) * 0.5f) * 4.0f;
                float zPos = e.FieldDistance + 35f;
                enemyGo.transform.position = new Vector3(xPos, 0f, zPos);

                var entity = enemyGo.AddComponent<CombatEntity>();
                entity.Initialize(e.EntityId, e.Name, e.Team, e.Stats, e.TraitId, e.FieldDistance);

                if (createVisuals)
                {
                    bool isBoss = e.EntityId.Contains("hound");
                    Color col = isBoss ? new Color(0.9f, 0.15f, 0.15f) : new Color(1.0f, 0.5f, 0.1f);
                    float height = isBoss ? 3.0f : 1.2f;
                    float radius = isBoss ? 1.6f : 0.6f;
                    CreateTacticalVisualToken(enemyGo, col, height, radius, e.Name);
                }

                allCombatants.Add(entity);
            }

            // 6. Buat Visual Base Titik Evakuasi Komandan (Z = 0)
            if (createVisuals)
            {
                CreateCommanderTransportMarker(root.transform);
            }

            // 7. Simpan daftar combatants ke BattleManager serialized field
            var serializedBm = new SerializedObject(battleManager);
            var combatantsProp = serializedBm.FindProperty("combatants");
            if (combatantsProp != null)
            {
                combatantsProp.ClearArray();
                for (int i = 0; i < allCombatants.Count; i++)
                {
                    combatantsProp.InsertArrayElementAtIndex(i);
                    combatantsProp.GetArrayElementAtIndex(i).objectReferenceValue = allCombatants[i];
                }
                serializedBm.ApplyModifiedProperties();
            }

            EditorUtility.SetDirty(battleManager);
            EditorUtility.SetDirty(orchestrator);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = bmGo;
            EditorGUIUtility.PingObject(bmGo);

            Debug.Log($"[TestChamberSetup] Successfully prepared Test Chamber for {scenarioId} ({allCombatants.Count} combatants). Ready for Play Mode!");
        }

        private static void AttachTacticalLoadout(CombatEntity entity)
        {
            // Unit Medic (Nyx / Toby) mendapatkan persediaan medikit
            if (entity.EntityId.Contains("nyx") || entity.EntityId.Contains("toby") || entity.EntityName.ToLower().Contains("medic"))
            {
                var medikit = ScriptableObject.CreateInstance<ItemData>();
                medikit.Initialize("Item_Medikit", "Field Medikit", "Restores 45 HP to adjacent comrade.", ItemEffectType.RestoreHP, 45);
                entity.AddItem(medikit, 3);
            }
        }

        private static void CreateTacticalVisualToken(GameObject parent, Color color, float height, float width, string label)
        {
            var token = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            token.name = "VisualToken";
            token.transform.SetParent(parent.transform, false);
            token.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            token.transform.localScale = new Vector3(width, height * 0.5f, width);

            // Hapus collider bawaan agar tidak mengganggu sistem raycast/fisika
            var collider = token.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyImmediate(collider);
            }

            var renderer = token.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                {
                    color = color
                };
                renderer.sharedMaterial = mat;
            }
        }

        private static void CreateCommanderTransportMarker(Transform parent)
        {
            var transport = GameObject.CreatePrimitive(PrimitiveType.Cube);
            transport.name = "_CommanderTransport_IronColossus";
            transport.transform.SetParent(parent, false);
            transport.transform.localPosition = new Vector3(0f, 1.5f, -5f);
            transport.transform.localScale = new Vector3(14f, 3f, 8f);

            var collider = transport.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyImmediate(collider);
            }

            var renderer = transport.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                {
                    color = new Color(0.25f, 0.28f, 0.32f) // Baja berat komandan
                };
                renderer.sharedMaterial = mat;
            }
        }

        public static LLMConfig EnsureDefaultLLMConfigAsset()
        {
            const string resourcesDir = "Assets/Resources";
            const string assetPath = "Assets/Resources/LLMConfig.asset";

            var existing = Resources.Load<LLMConfig>("LLMConfig");
            if (existing != null)
            {
                return existing;
            }

            if (!AssetDatabase.IsValidFolder(resourcesDir))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            var newConfig = ScriptableObject.CreateInstance<LLMConfig>();
            newConfig.ModelName = "llama-3.2-3b-instruct";
            newConfig.BaseUrl = "http://localhost:11434/v1/chat/completions";
            newConfig.UseMockIfOffline = true;
            newConfig.Temperature = 0.2f;

            AssetDatabase.CreateAsset(newConfig, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[TestChamberSetup] Created default LLMConfig asset at {assetPath}");
            return newConfig;
        }
    }
}
