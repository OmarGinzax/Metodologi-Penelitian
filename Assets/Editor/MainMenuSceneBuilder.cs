using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RPGStrategy.UI;

namespace RPGStrategy.Editor
{
    /// <summary>
    /// Editor Script untuk membuat dan memperbarui scene MainMenu ("Assets/Scenes/MainMenu.unity")
    /// dengan 3 tombol navigasi ke:
    ///   - HFSM Baseline Scene ("FSM")
    ///   - Pure LLM Scene ("LLM")
    ///   - Hybrid Agent Scene ("Hybrid")
    ///
    /// Juga mendaftarkan seluruh scene ke EditorBuildSettings secara otomatis.
    /// </summary>
    [InitializeOnLoad]
    public static class MainMenuSceneBuilder
    {
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string FsmScenePath      = "Assets/Scenes/FSM.unity";
        private const string LlmScenePath      = "Assets/Scenes/LLM.unity";
        private const string HybridScenePath   = "Assets/Scenes/Hybrid.unity";

        static MainMenuSceneBuilder()
        {
            // Pastikan seluruh scene terdaftar di Build Settings saat editor dimuat
            EditorApplication.delayCall += EnsureBuildSettings;
        }

        [MenuItem("RPGStrategy/Scenes/Create or Update Main Menu Scene", priority = 1)]
        public static void CreateOrUpdateMainMenuScene()
        {
            // 1. Simpan scene aktif jika kotor
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            }

            // 2. Buat scene baru
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 3. Kamera 2D
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.10f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.transform.position = new Vector3(0f, 0f, -10f);

            // 4. Controller & UI Builder
            var menuControllerGo = new GameObject("[MainMenu_Controller]");
            var menuUI = menuControllerGo.AddComponent<MainMenuUI>();

            // 5. Bangun UI melalui method di MainMenuUI
            menuUI.AutoWireOrBuildUI();

            // 6. Simpan Scene ke Assets/Scenes/MainMenu.unity
            EnsureScenesFolderExists();
            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
            Debug.Log($"[MainMenuSceneBuilder] Main Menu Scene berhasil disimpan ke '{MainMenuScenePath}'!");

            // 7. Daftarkan ke EditorBuildSettings
            EnsureBuildSettings();

            // Ping scene asset di project window
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath);
            if (sceneAsset != null)
            {
                EditorGUIUtility.PingObject(sceneAsset);
                Selection.activeObject = sceneAsset;
            }
        }

        [MenuItem("RPGStrategy/Scenes/Register All Scenes in Build Settings", priority = 2)]
        public static void EnsureBuildSettings()
        {
            var desiredPaths = new List<string>
            {
                MainMenuScenePath,
                FsmScenePath,
                LlmScenePath,
                HybridScenePath
            };

            var currentScenes = EditorBuildSettings.scenes.ToList();
            bool modified = false;

            foreach (var path in desiredPaths)
            {
                if (File.Exists(path))
                {
                    if (!currentScenes.Any(s => s.path == path))
                    {
                        currentScenes.Add(new EditorBuildSettingsScene(path, true));
                        modified = true;
                        Debug.Log($"[MainMenuSceneBuilder] Mendaftarkan scene '{path}' ke EditorBuildSettings.");
                    }
                }
            }

            // Pastikan MainMenu berada di indeks 0 (scene awal saat run)
            int menuIdx = currentScenes.FindIndex(s => s.path == MainMenuScenePath);
            if (menuIdx > 0)
            {
                var menuScene = currentScenes[menuIdx];
                currentScenes.RemoveAt(menuIdx);
                currentScenes.Insert(0, menuScene);
                modified = true;
            }

            if (modified)
            {
                EditorBuildSettings.scenes = currentScenes.ToArray();
                Debug.Log("[MainMenuSceneBuilder] EditorBuildSettings berhasil diperbarui dengan seluruh scene!");
            }
        }

        [MenuItem("RPGStrategy/Scenes/Open Main Menu Scene", priority = 3)]
        public static void OpenMainMenuScene()
        {
            if (!File.Exists(MainMenuScenePath))
            {
                CreateOrUpdateMainMenuScene();
                return;
            }

            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            }

            EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
        }

        private static void EnsureScenesFolderExists()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }
        }
    }
}

