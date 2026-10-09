using UnityEngine;

namespace Eonward
{
    /// <summary>
    /// Entry point. The only scene contains one object with this component; everything
    /// else (world, camera, effects, HUD) is created from code so the game can be developed
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

            var settlement = new Settlement(transform);
            settlement.BuildAll();

            var rig = new GameObject("CameraRig").AddComponent<CameraRig>();
            Effects.Snowfall(rig.Cam);
            if (settlement.FurnaceTop != null) Effects.Smoke(settlement.FurnaceTop);
            Survivor.Spawn(transform, settlement.WorkSpots, 14);

            gameObject.AddComponent<Hud>();
        }
    }

    /// <summary>
    /// Placeholder HUD (IMGUI) laid out like a mobile strategy game: resources on top,
    /// furnace status, main actions at the bottom. Values are a visual mock until the
    /// game rules are ported in the next step.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        float meat = 1200, wood = 860, coal = 420, iron = 60;
        float temperature = -21f;
        string toast;
        float toastUntil;

        GUIStyle bar, label, small, button, title, toastStyle;
        Texture2D barTex, buttonTex, toastTex;

        void Update()
        {
            float dt = Time.deltaTime;
            meat += dt * 0.9f;
            wood += dt * 1.4f;
            iron += dt * 0.2f;
            coal = Mathf.Max(0f, coal - dt * 0.35f + dt * 0.6f);
            temperature = -21f + Mathf.Sin(Time.time * 0.05f) * 2f;
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void EnsureStyles()
        {
            if (bar != null) return;
            barTex = Solid(new Color(0.07f, 0.11f, 0.18f, 0.82f));
            buttonTex = Solid(new Color(0.16f, 0.36f, 0.62f, 0.95f));
            toastTex = Solid(new Color(0.05f, 0.08f, 0.12f, 0.9f));
            bar = new GUIStyle { normal = { background = barTex } };
            label = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            small = new GUIStyle(label) { fontSize = 15, fontStyle = FontStyle.Normal, normal = { textColor = new Color(0.8f, 0.88f, 1f) } };
            title = new GUIStyle(label) { fontSize = 26, alignment = TextAnchor.MiddleLeft };
            button = new GUIStyle
            {
                fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { background = buttonTex, textColor = Color.white },
                active = { background = barTex, textColor = Color.white },
            };
            toastStyle = new GUIStyle(label) { fontSize = 18, wordWrap = true, normal = { background = toastTex, textColor = Color.white } };
        }

        void OnGUI()
        {
            EnsureStyles();
            // Design for a 430pt-wide portrait screen and scale to the device.
            float scale = Screen.width / 430f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var safe = Screen.safeArea;
            float w = Screen.width / scale;
            float h = Screen.height / scale;
            float top = (Screen.height - safe.yMax) / scale;
            float bottom = safe.yMin / scale;

            // Top bar: settlement + resources
            GUI.Box(new Rect(0, 0, w, top + 112), GUIContent.none, bar);
            GUI.Label(new Rect(14, top + 6, w - 28, 34), "EONWARD  ·  Frozen Settlement", title);
            GUI.Label(new Rect(14, top + 36, w - 28, 22),
                      $"Furnace LIT  ·  {temperature:0}°C outside  ·  Survivors 14/16", small);
            string[] names = { "Meat", "Wood", "Coal", "Iron" };
            float[] values = { meat, wood, coal, iron };
            float cell = (w - 28) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(14 + i * cell, top + 62, cell - 6, 44);
                GUI.Label(new Rect(r.x, r.y, r.width, 26), Mathf.FloorToInt(values[i]).ToString("N0"), label);
                GUI.Label(new Rect(r.x, r.y + 24, r.width, 18), names[i], small);
            }

            // Bottom actions
            string[] actions = { "Build", "Heroes", "Research", "World", "Alliance" };
            float bh = 64;
            float by = h - bottom - bh - 8;
            GUI.Box(new Rect(0, by - 8, w, bh + 16 + bottom), GUIContent.none, bar);
            float bw = (w - 16) / actions.Length;
            for (int i = 0; i < actions.Length; i++)
            {
                if (GUI.Button(new Rect(8 + i * bw + 3, by, bw - 6, bh), actions[i], button))
                {
                    toast = actions[i] + " is coming in the next builds.";
                    toastUntil = Time.time + 2.5f;
                }
            }

            if (Time.time < toastUntil && !string.IsNullOrEmpty(toast))
            {
                GUI.Label(new Rect(30, by - 70, w - 60, 50), toast, toastStyle);
            }

            GUI.Label(new Rect(0, by - 26, w, 18), "Prototype build · placeholder art · drag to move, pinch to zoom", small);
        }
    }
}
