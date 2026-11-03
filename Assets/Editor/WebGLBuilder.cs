using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RPGStrategy.Editor
{
    /// <summary>
    /// Utility untuk membangun WebGL build yang siap di-deploy langsung ke GitHub Pages.
    /// Otomatis mengatur:
    ///   - Compression Format: Disabled (mencegah error MIME / gzip di GitHub Pages)
    ///   - File .nojekyll (mencegah GitHub mengabaikan file wasm dan aset internal)
    ///   - Output ke folder 'docs/' atau 'Builds/WebGL/'
    /// </summary>
    public static class WebGLBuilder
    {
        private const string DocsBuildPath = "docs";
        private const string CustomBuildPath = "Builds/WebGL";

        [MenuItem("RPGStrategy/WebGL/Build for GitHub Pages (to 'docs/' folder)", priority = 10)]
        public static void BuildForGitHubPagesDocs()
        {
            PerformWebGLBuild(DocsBuildPath);
        }

        [MenuItem("RPGStrategy/WebGL/Build to 'Builds/WebGL/' folder", priority = 11)]
        public static void BuildToCustomFolder()
        {
            PerformWebGLBuild(CustomBuildPath);
        }

        public static void PerformWebGLBuild(string outputDirectory)
        {
            // 1. Cek apakah WebGL Support terpasang di Unity
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                EditorUtility.DisplayDialog(
                    "WebGL Module Missing",
                    "WebGL Build Support belum terpasang di instalasi Unity ini.\n\n" +
                    "Buka Unity Hub -> Installs -> Unity 6000.3.10f1 -> Add Modules -> Centang 'WebGL Build Support'.",
                    "OK");
                Debug.LogError("[WebGLBuilder] WebGL Build Support module is not installed in Unity Hub!");
                return;
            }

            // 2. Pastikan Build Settings berisi scene yang lengkap
            MainMenuSceneBuilder.EnsureBuildSettings();
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path) && File.Exists(s.path))
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[WebGLBuilder] Tidak ada scene yang valid terdaftar di EditorBuildSettings!");
                return;
            }

            // 3. Konfigurasi PlayerSettings WebGL untuk kompatibilitas GitHub Pages
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.runInBackground = true;

            Debug.Log($"[WebGLBuilder] Memulai WebGL Build ke '{outputDirectory}'...");
            Debug.Log($"[WebGLBuilder] Scenes to build: {string.Join(", ", scenes)}");
            Debug.Log($"[WebGLBuilder] Compression: {PlayerSettings.WebGL.compressionFormat}");

            // 4. Eksekusi Build
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputDirectory,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                // 5. Buat file .nojekyll agar GitHub Pages tidak memfilter file/folder
                string noJekyllPath = Path.Combine(outputDirectory, ".nojekyll");
                File.WriteAllText(noJekyllPath, "");

                Debug.Log($"[WebGLBuilder] Build BERHASIL! Ukuran: {summary.totalSize / (1024 * 1024)} MB.");
                Debug.Log($"[WebGLBuilder] File .nojekyll dibuat di: {noJekyllPath}");

                EditorUtility.DisplayDialog(
                    "WebGL Build Succeeded!",
                    $"Build WebGL berhasil dibuat di folder '{outputDirectory}'!\n\n" +
                    "Langkah GitHub Pages:\n" +
                    "1. Commit dan Push folder 'docs/' ke repository GitHub.\n" +
                    "2. Di GitHub: Settings -> Pages -> Source: Deploy from branch -> Branch: main -> Folder: /docs -> Save.",
                    "Buka Folder");

                EditorUtility.RevealInFinder(outputDirectory);
            }
            else if (summary.result == BuildResult.Failed)
            {
                Debug.LogError($"[WebGLBuilder] Build GAGAL dengan {summary.totalErrors} error.");
            }
        }
    }
}
