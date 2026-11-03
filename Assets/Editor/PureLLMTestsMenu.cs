using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using RPGStrategy.AI;
using RPGStrategy.AI.LLM;
using RPGStrategy.AI.PureLLM;
using RPGStrategy.AI.Telemetry;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.Editor
{
    /// <summary>
    /// Menu editor untuk menjalankan pengujian Pure LLM System (Fase 1-7) dan
    /// memasang controller Pure LLM pada BattleManager2D di scene aktif.
    /// </summary>
    public static class PureLLMTestsMenu
    {
        private const string ConfigAssetPath = "Assets/Settings/LLMConfig_PureLLM.asset";

        [MenuItem("RPGStrategy/Pure LLM/Run All Pure LLM Tests (NUnit)", priority = 20)]
        public static void RunAllPureLLMTests()
        {
            var testInstance = new RPGStrategy.Tests.PureLLMSystemTests();
            var testMethods = typeof(RPGStrategy.Tests.PureLLMSystemTests)
                .GetMethods()
                .Where(m => m.GetCustomAttributes(typeof(NUnit.Framework.TestAttribute), false).Length > 0)
                .OrderBy(m => m.Name)
                .ToList();

            int passed = 0;
            int failed = 0;
            Debug.Log($"[PureLLMTestRunner] Running {testMethods.Count} Pure LLM unit tests...");

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

            Debug.Log($"[PureLLMTestRunner] Finished! Total: {testMethods.Count} | Passed: {passed} | Failed: {failed}");
        }

        [MenuItem("RPGStrategy/Pure LLM/Attach Pure LLM Controller to BattleManager", priority = 1)]
        public static void AttachPureLLMController()
        {
            var battleManager = UnityEngine.Object.FindFirstObjectByType<BattleManager2D>();
            if (battleManager == null)
            {
                EditorUtility.DisplayDialog(
                    "Pure LLM Setup",
                    "BattleManager2D tidak ditemukan di scene aktif. Siapkan test chamber terlebih dahulu.",
                    "OK");
                return;
            }

            var root = battleManager.gameObject;

            var logger = UnityEngine.Object.FindFirstObjectByType<CombatTelemetryLogger2D>();
            if (logger == null)
            {
                logger = root.GetComponent<CombatTelemetryLogger2D>();
            }
            if (logger == null)
            {
                logger = Undo.AddComponent<CombatTelemetryLogger2D>(root);
            }

            var service = root.GetComponent<LLMService2D>();
            if (service == null)
            {
                service = Undo.AddComponent<LLMService2D>(root);
            }

            var config = LoadOrCreateConfig();
            if (config != null)
            {
                service.Configure(config);
            }

            var agent = root.GetComponent<PureLLMTacticalAgent>();
            if (agent == null)
            {
                agent = Undo.AddComponent<PureLLMTacticalAgent>(root);
            }

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

            Debug.Log($"[PureLLM] Controller Pure LLM dipasang pada '{root.name}'. " +
                      $"Endpoint: {config?.ResolvedEndpoint ?? "(belum diset)"}");
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
            Debug.Log($"[PureLLM] LLMConfig baru dibuat di {ConfigAssetPath}");
            return config;
        }
    }
}
