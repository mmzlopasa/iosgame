using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoundersEditor
{
    /// <summary>
    /// UI Toolkit runtime setup (theme + PanelSettings, loaded by the game from Resources),
    /// the iOS app icon, the launch screen and the splash settings. Generated into
    /// Assets/Generated, like the rest of the rendering setup.
    /// </summary>
    public static class UiSetup
    {
        const string ThemePath = "Assets/Generated/UI/FoundersTheme.tss";
        const string PanelPath = "Assets/Generated/Resources/UI/FoundersPanel.asset";
        const string AppIconPath = "Assets/Art/UI/AppIcon/AppIcon.png";
        const string LaunchImagePath = "Assets/Art/UI/Launch/scyde_logo.png";
        /// <summary>The game's dark blue-grey (#202734): the launch screen is never black.</summary>
        static readonly Color Night = new Color32(0x20, 0x27, 0x34, 0xFF);

        public static void Run()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemePath));
            Directory.CreateDirectory(Path.GetDirectoryName(PanelPath));
            File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
            AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceSynchronousImport);
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null) Debug.LogWarning("[FOUNDERS] Theme style sheet did not import.");

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            panel.themeStyleSheet = theme;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            // Landscape design canvas of a regular phone (1082 × 500). The game re-picks the canvas for the device
            // (fewer units on a small phone, portrait turned) and the match at run time, see GameUi.ReferenceFor.
            panel.referenceResolution = Founders.UI.GameUi.ReferenceResolution;
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 1f;
            panel.clearColor = false;
            EditorUtility.SetDirty(panel);

            ConfigureIconAndLaunch();
            AssetDatabase.SaveAssets();
        }

        static void ConfigureIconAndLaunch()
        {
            // Unity 6 lets every license hide the engine splash: the game shows its own loading screen.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.backgroundColor = Night;

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIconPath);
            if (icon == null)
            {
                Debug.LogWarning("[FOUNDERS] App icon missing: " + AppIconPath);
            }
            else
            {
                foreach (var kind in new[] { IconKind.Application, IconKind.Spotlight, IconKind.Settings, IconKind.Notification, IconKind.Store })
                {
                    int count = PlayerSettings.GetIconSizes(NamedBuildTarget.iOS, kind).Length;
                    if (count > 0) PlayerSettings.SetIcons(NamedBuildTarget.iOS, Enumerable.Repeat(icon, count).ToArray(), kind);
                }
            }

            var launch = AssetDatabase.LoadAssetAtPath<Texture2D>(LaunchImagePath);
            if (launch != null)
            {
                PlayerSettings.iOS.SetiPhoneLaunchScreenType(iOSLaunchScreenType.ImageAndBackgroundConstant);
                // The SCYDE logo centred on the blue-grey, for both orientations. Editor/IosLaunchScreen.cs then puts the
                // full launch picture (logo on a painting of the valley) in its place in the Xcode project.
                PlayerSettings.iOS.SetLaunchScreenImage(launch, iOSLaunchScreenImageType.iPhonePortraitImage);
                PlayerSettings.iOS.SetLaunchScreenImage(launch, iOSLaunchScreenImageType.iPhoneLandscapeImage);
                PlayerSettings.iOS.SetiPadLaunchScreenType(iOSLaunchScreenType.ImageAndBackgroundConstant);
                PlayerSettings.iOS.SetLaunchScreenImage(launch, iOSLaunchScreenImageType.iPadImage);
            }
            // The launch screen's background has no public setter: the player settings asset holds it.
            var settings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                foreach (var name in new[] { "iOSLaunchScreenBackgroundColor", "iOSLaunchScreeniPadBackgroundColor" })
                {
                    var prop = so.FindProperty(name);
                    if (prop != null && prop.propertyType == SerializedPropertyType.Color) prop.colorValue = Night;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
