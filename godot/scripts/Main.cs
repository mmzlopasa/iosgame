using Founders.UI;
using Founders.World;
using Godot;

namespace Founders
{
    /// <summary>
    /// Starts the game: the loading screen first, then (a frame later, so it is on screen while the valley
    /// is built) the camera, the world, the sky, the running game, sound and the interface.
    /// </summary>
    public sealed partial class Main : Node3D
    {
        Session session;
        LoadingScreen loading;
        int frames;
        bool started;

        public override void _Ready()
        {
            Prefs.Apply();
            loading = new LoadingScreen();
            AddChild(loading);
        }

        public override void _Process(double delta)
        {
            if (started) return;
            // Two frames so the loading painting is drawn before the heavy work starts.
            if (++frames < 3) return;
            started = true;
            Start();
        }

        async void Start()
        {
            // In steps, a frame apart, so the bar moves and a failure says where it happened.
            string step = "camera";
            try
            {
                async System.Threading.Tasks.Task Next(float value, string text, string name)
                {
                    step = name;
                    loading.Set(value, text);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                await Next(0.2f, "Waking the valley…", "camera");
                var rig = new CameraRig();
                AddChild(rig);
                var world = new WorldView { Name = "World" };
                AddChild(world);
                var sky = new Atmosphere();
                AddChild(sky);
                session = new Session { Name = "Session" };
                AddChild(session);
                AddChild(new AudioDirector { Store = session });

                rig.Raycast = (from, dir) =>
                    world.Ground != null && world.Ground.Raycast(from, dir, out var hit) ? (true, hit) : (false, Vector3.Zero);
                rig.OnTap = session.HandleTap;
                rig.PaintMode = () => session.PaintMode;
                rig.OnPaint = session.HandlePaint;
                rig.OnPaintEnd = session.EndPaintStroke;
                rig.GrabAt = session.GrabsGhost;
                rig.OnGrab = session.DragGhost;
                world.Rebuilt = () =>
                {
                    sky.Ground = world.Ground;
                    sky.Plants = world.Plants;
                    sky.Water = world.WaterMaterial;
                };
                sky.Camera = rig.Cam;
                sky.GameOf = () => session.Game;

                await Next(0.35f, "Raising the hills and forests…", "world");
                session.Init(rig, world);
                await Next(0.85f, "Lighting the fires…", "interface");
                var ui = new GameUi();
                AddChild(ui);
                ui.Init(session);
                rig.Blocks = ui.BlocksPointer;
                await Next(0.95f, "Almost there…", "first frame");
                loading.Finish();
                GD.Print("[FOUNDERS] started");
                if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke") >= 0) Smoke(ui);
            }
            catch (System.Exception e)
            {
                GD.PrintErr($"[FOUNDERS] start failed at {step}: {e}");
                loading.Fail(step, e);
            }
        }
        /// <summary>
        /// CI check (godot --headless -- --smoke): opens every window and card once so a mistake in building them
        /// shows in the log, then quits. Nothing is drawn or saved.
        /// </summary>
        async void Smoke(GameUi ui)
        {
            var tree = GetTree();
            async System.Threading.Tasks.Task Wait(double s) => await ToSignal(tree.CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
            await Wait(2);
            foreach (Panel p in System.Enum.GetValues(typeof(Panel)))
            {
                GD.Print($"[SMOKE] panel {p}");
                session.ActivePanel = p;
                session.Refresh();
                await Wait(0.6);
            }
            session.ActivePanel = Panel.None;
            foreach (var b in session.Game.State.Buildings)
            {
                GD.Print($"[SMOKE] building {b.DefinitionId}");
                session.SelectedBuildingId = b.Id;
                session.Refresh();
                await Wait(0.4);
            }
            session.SelectedBuildingId = null;
            session.Refresh();
            await Wait(1);
            GD.Print($"[SMOKE] done; ui root children {ui.UiRoot.childCount}");
            tree.Quit();
        }
    }
}
