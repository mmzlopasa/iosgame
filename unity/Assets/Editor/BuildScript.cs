using System;
using System.IO;
using System.Linq;
using Eonward;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EonwardEditor
{
    /// <summary>
    /// Headless build entry points used by GitHub Actions (GameCI) and the
    /// "EONWARD" editor menu. The scene and materials are generated here so the
    /// project never depends on hand-edited scene files.
    /// </summary>
    public static class BuildScript
    {
        const string GeneratedRoot = "Assets/Generated";
        const string MaterialFolder = GeneratedRoot + "/Resources/" + Palette.ResourceFolder;
        const string ScenePath = GeneratedRoot + "/Main.unity";

        public const string ProductName = "EONWARD";
        public const string BundleId = "com.eonward.frozen";
        public const string Version = "0.2.0";

        [MenuItem("EONWARD/Generate scene and materials")]
        public static void Generate()
        {
            ConfigurePlayer();
            GenerateMaterials();
            GenerateScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("EONWARD/Build iOS (Xcode project)")]
        public static void BuildIOS() => BuildFor(BuildTarget.iOS, "build/iOS/Eonward");

        [MenuItem("EONWARD/Build Android (APK)")]
        public static void BuildAndroid() => BuildFor(BuildTarget.Android, "build/Android/Eonward.apk");

        static void BuildFor(BuildTarget target, string defaultPath)
        {
            Generate();
            string path = Arg("-customBuildPath") ?? defaultPath;
            var dir = target == BuildTarget.iOS ? path : Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (target == BuildTarget.Android) ConfigureAndroid();
            if (target == BuildTarget.iOS) ConfigureIOS();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.None,
            };
            Debug.Log($"[EONWARD] Building {target} -> {path}");
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[EONWARD] Build result: {summary.result}, size {summary.totalSize} bytes, errors {summary.totalErrors}, {summary.totalTime}");
            if (summary.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                    foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                        Debug.LogError($"[EONWARD] {step.name}: {m.content}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Eonward";
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // This project uses the classic Input class; make sure it is enabled.
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                var handler = so.FindProperty("activeInputHandler");
                if (handler != null && handler.intValue != 0 && handler.intValue != 2)
                {
                    handler.intValue = 2; // Both
                    so.ApplyModifiedProperties();
                }
            }
        }

        static void ConfigureAndroid()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = false;
            int build = int.TryParse(Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER"), out var n) ? n : 1;
            PlayerSettings.Android.bundleVersionCode = build;
        }

        static void ConfigureIOS()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.buildNumber = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER") ?? "1";
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void GenerateMaterials()
        {
            EnsureFolder(MaterialFolder);
            foreach (var spec in Palette.All)
            {
                var path = $"{MaterialFolder}/{spec.Name}.mat";
                var material = Palette.Create(spec);
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(material, existing);
                }
                else
                {
                    AssetDatabase.CreateAsset(material, path);
                }
            }
        }

        static void GenerateScene()
        {
            EnsureFolder(GeneratedRoot);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneEnvironment.Apply();
            SceneEnvironment.CreateSun();
            new GameObject("Game").AddComponent<GameBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
