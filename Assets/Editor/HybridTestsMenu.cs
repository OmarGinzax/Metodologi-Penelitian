using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using RPGStrategy.AI;
using RPGStrategy.AI.Baseline;
using RPGStrategy.AI.Hybrid;
using RPGStrategy.AI.LLM;
using RPGStrategy.AI.Telemetry;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.Editor
{
    /// <summary>
    /// Menu editor untuk penelitian Hybrid: menjalankan pengujian offline dan
    /// beralih controller pada BattleManager2D tanpa mengubah scene secara manual.
    /// </summary>
    public static class HybridTestsMenu
    {
        private const string ConfigAssetPath = "Assets/Settings/LLMConfig_PureLLM.asset";

        [MenuItem("RPGStrategy/Hybrid/Run All Hybrid Tests (NUnit)", priority = 20)]
        public static void RunAllHybridTests()
        {
            var testInstance = new RPGStrategy.Tests.HybridSystemTests();
            var testMethods = typeof(RPGStrategy.Tests.HybridSystemTests)
                .GetMethods()
                .Where(m => m.GetCustomAttributes(typeof(NUnit.Framework.TestAttribute), false).Length > 0)
                .OrderBy(m => m.Name)
                .ToList();

            int passed = 0;
            int failed = 0;
            Debug.Log($"[HybridTestRunner] Running {testMethods.Count} Hybrid unit tests...");

            foreach (var m in testMethods)
            {
                try
                {
                    testInstance.Setup();
                    m.Invoke(testInstance, null);
                    testInstance.Teardown();
                    passed++;
                    Debug.Log($"<color=#33FF77>✔ [PASS]</color> {m.Name}");
                }
                catch (Exception ex)
                {
                    failed++;
                    var inner = ex.InnerException ?? ex;
                    Debug.LogError($"<color=#FF3344>✘ [FAIL]</color> {m.Name}: {inner.Message}\n{inner.StackTrace}");
                    try { testInstance.Teardown(); } catch { }
                }
            }

            Debug.Log($"[HybridTestRunner] Finished! Total: {testMethods.Count} | Passed: {passed} | Failed: {failed}");
        }

        [MenuItem("RPGStrategy/Hybrid/Attach Hybrid Controller to BattleManager", priority = 1)]
        public static void AttachHybridController()
        {
            var battleManager = UnityEngine.Object.FindFirstObjectByType<BattleManager2D>();
            if (battleManager == null)
            {
                EditorUtility.DisplayDialog(
                    "Hybrid Setup",
                    "BattleManager2D tidak ditemukan di scene aktif. Siapkan test chamber terlebih dahulu.",
                    "OK");
                return;
            }

            var root = battleManager.gameObject;

            var logger = UnityEngine.Object.FindFirstObjectByType<CombatTelemetryLogger2D>();
            if (logger == null) logger = root.GetComponent<CombatTelemetryLogger2D>();
            if (logger == null) logger = Undo.AddComponent<CombatTelemetryLogger2D>(root);

            var service = root.GetComponent<LLMService2D>();
            if (service == null) service = Undo.AddComponent<LLMService2D>(root);

            var config = LoadOrCreateConfig();
            if (config != null) service.Configure(config);

            var agent = root.GetComponent<HybridTacticalAgent>();
            if (agent == null) agent = Undo.AddComponent<HybridTacticalAgent>(root);

            agent.Configure(config, service, logger);
            battleManager.SetAgentController(agent);

            var so = new SerializedObject(battleManager);
            var loggerProperty = so.FindProperty("telemetryLogger");
            if (loggerProperty != null)
            {
                loggerProperty.objectReferenceValue = logger;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(battleManager);
            EditorUtility.SetDirty(agent);
            Selection.activeGameObject = root;

            Debug.Log($"[Hybrid] Controller Hybrid dipasang pada '{root.name}'. " +
                      $"Endpoint: {config?.ResolvedEndpoint ?? "(belum diset)"}");
        }

        [MenuItem("RPGStrategy/Hybrid/Switch to HFSM Baseline", priority = 2)]
        public static void SwitchToHfsmBaseline()
        {
            var battleManager = UnityEngine.Object.FindFirstObjectByType<BattleManager2D>();
            if (battleManager == null)
            {
                EditorUtility.DisplayDialog(
                    "HFSM Baseline",
                    "BattleManager2D tidak ditemukan di scene aktif.",
                    "OK");
                return;
            }

            var root = battleManager.gameObject;
            var agent = root.GetComponent<HFSMTacticalAgent>();
            if (agent == null) agent = Undo.AddComponent<HFSMTacticalAgent>(root);

            battleManager.SetAgentController(agent);
            EditorUtility.SetDirty(battleManager);
            Selection.activeGameObject = root;

            Debug.Log($"[Hybrid] Controller kembali ke HFSM baseline pada '{root.name}'.");
        }

        [MenuItem("RPGStrategy/Hybrid/Export Telemetry Now", priority = 3)]
        public static void ExportTelemetryNow()
        {
            var battleManager = UnityEngine.Object.FindFirstObjectByType<BattleManager2D>();
            if (battleManager == null)
            {
                EditorUtility.DisplayDialog("Telemetry", "BattleManager2D tidak ditemukan.", "OK");
                return;
            }

            battleManager.ExportTelemetryNow();
        }

        private static LLMConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<LLMConfig>(ConfigAssetPath);
            if (config != null) return config;

            const string directory = "Assets/Settings";
            if (!AssetDatabase.IsValidFolder(directory))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }

            config = ScriptableObject.CreateInstance<LLMConfig>();
            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Hybrid] LLMConfig baru dibuat di {ConfigAssetPath}");
            return config;
        }
    }
}
