using UnityEngine;

namespace Founders
{
    /// <summary>
    /// Entry point. The only scene contains one object with this component; the world,
    /// camera, UI and game state are created from code, so the game can be developed
    /// without opening the Unity editor.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            SceneEnvironment.Apply();
            if (FindAnyObjectByType<Light>() == null) SceneEnvironment.CreateSun();

            var rig = new GameObject("CameraRig").AddComponent<CameraRig>();
            var world = new GameObject("World").AddComponent<WorldView>();
            var store = gameObject.AddComponent<GameStore>();
            var hud = gameObject.AddComponent<Hud>();

            rig.IsBlocked = hud.BlocksPointer;
            rig.OnTap = store.HandleTap;
            store.Init(rig, world);
            hud.Init(store);
        }
    }
}
