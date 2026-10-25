#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace Founders.EditorTools
{
    /// <summary>
    /// The launch screen: the SCYDE logo on a painting of the valley washed in the game's blue-grey
    /// (unity/Launch), filling the screen whatever its shape. Unity's own launch storyboards are replaced
    /// by unity/Launch/LaunchScreen.storyboard and the picture is added to the app. If anything is
    /// missing, Unity's launch screen (the logo on blue-grey, set in UiSetup) stays.
    /// </summary>
    public static class IosLaunchScreen
    {
        const string Picture = "FoundersLaunch.png";

        [PostProcessBuild(110)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string launch = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Launch"));
            string storyboard = Path.Combine(launch, "LaunchScreen.storyboard");
            string picture = Path.Combine(launch, Picture);
            if (!File.Exists(storyboard) || !File.Exists(picture))
            {
                Debug.LogWarning("[FOUNDERS] launch screen files missing in " + launch + ": Unity's launch screen stays.");
                return;
            }
            int replaced = 0;
            foreach (var name in new[] { "LaunchScreen-iPhone.storyboard", "LaunchScreen-iPad.storyboard" })
            {
                string dst = Path.Combine(path, name);
                if (!File.Exists(dst)) continue;
                File.Copy(storyboard, dst, true);
                replaced++;
            }
            if (replaced == 0)
            {
                Debug.LogWarning("[FOUNDERS] no launch storyboard in the Xcode project: Unity's launch screen stays.");
                return;
            }
            File.Copy(picture, Path.Combine(path, Picture), true);
            string projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string guid = project.FindFileGuidByProjectPath(Picture) ?? project.AddFile(Picture, Picture, PBXSourceTree.Source);
            project.AddFileToBuild(project.GetUnityMainTargetGuid(), guid);
            project.WriteToFile(projectPath);
            Debug.Log($"[FOUNDERS] launch screen: {replaced} storyboard(s) replaced, {Picture} added.");
        }
    }
}
#endif
