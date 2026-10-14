using System;
using System.IO;
using System.Linq;
using Founders;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FoundersEditor
{
    /// <summary>
    /// Headless build entry points used by GitHub Actions (GameCI) and the
    /// "FOUNDERS" editor menu. The scene and materials are generated here so the
    /// project never depends on hand-edited scene files.
    /// </summary>
    public static class BuildScript
    {
        const string GeneratedRoot = "Assets/Generated";
        const string ScenePath = GeneratedRoot + "/Main.unity";

        public const string ProductName = "Founders";
        public const string BundleId = "com.founders.longrise";
        public const string Version = "0.3.0";

        /// <summary>
        /// The Unity Cloud project the cloud save uses (Unity Gaming Services: Authentication + Cloud Save), from the
        /// project's page on cloud.unity.com. Empty: the game keeps saves on the device only and Settings says the cloud
        /// is not set up. The FOUNDERS_CLOUD_PROJECT_ID environment variable overrides it.
        /// </summary>
        public const string CloudProjectId = "";

        [MenuItem("FOUNDERS/Generate scene and materials")]
        public static void Generate()
        {
            ConfigurePlayer();
            AssetDatabase.Refresh();
            ArtSetup.Run();
            UiSetup.Run();
            // The player's own art (Assets/Custom): prepare it and list what the build will use.
            try
            {
                CustomAssetsEditor.Prepare();
                Debug.Log(CustomAssetsEditor.Check());
            }
            catch (Exception e) { Debug.LogWarning("[FOUNDERS] custom assets check failed: " + e); }
            GenerateScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("FOUNDERS/Build iOS (Xcode project)")]
        public static void BuildIOS()
        {
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            BuildFor(BuildTarget.iOS, "build/iOS/Founders");
        }

        /// <summary>iOS Simulator build (arm64) for automated screenshots in CI.</summary>
        [MenuItem("FOUNDERS/Build iOS Simulator (Xcode project)")]
        public static void BuildIOSSimulator()
        {
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
            PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;
            BuildFor(BuildTarget.iOS, "build/iOS/Founders");
        }

        [MenuItem("FOUNDERS/Build Android (APK)")]
        public static void BuildAndroid() => BuildFor(BuildTarget.Android, "build/Android/Founders.apk");

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
            Debug.Log($"[FOUNDERS] Building {target} -> {path}");
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[FOUNDERS] Build result: {summary.result}, size {summary.totalSize} bytes, errors {summary.totalErrors}, {summary.totalTime}");
            if (summary.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                    foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                        Debug.LogError($"[FOUNDERS] {step.name}: {m.content}");
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

        /// <summary>Links the build to the cloud project (the setting the editor's Services window writes).</summary>
        static void ConfigureCloud()
        {
            string id = Environment.GetEnvironmentVariable("FOUNDERS_CLOUD_PROJECT_ID");
            if (string.IsNullOrEmpty(id)) id = CloudProjectId;
            if (string.IsNullOrEmpty(id))
            {
                Debug.Log("[FOUNDERS] Cloud save: no cloud project id, saves stay on the device.");
                return;
            }
            if (CloudProjectSettings.projectId == id) return;
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) { Debug.LogWarning("[FOUNDERS] Cloud save: ProjectSettings.asset not found."); return; }
            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("cloudProjectId");
            if (prop == null) { Debug.LogWarning("[FOUNDERS] Cloud save: cloudProjectId setting not found."); return; }
            prop.stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[FOUNDERS] Cloud save: linked to cloud project {id}.");
        }

        static void ConfigurePlayer()
        {
            ConfigureCloud();
            PlayerSettings.companyName = "Founders";
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = Version;
            // Landscape only, either way round.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.useAnimatedAutorotation = true;
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
            // No iPad split view / slide over: those windows can be portrait-shaped.
            PlayerSettings.iOS.requiresFullScreen = true;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void GenerateScene()
        {
            EnsureFolder(GeneratedRoot);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneEnvironment.Apply();
            SceneEnvironment.CreateSun();
            ArtSetup.PopulateScene();
            new GameObject("Game").AddComponent<GameBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
