#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Screenshots (UI included) of the menu, shop, HUD, new obstacles, chaser, duck pose and game over.
//   Unity -batchmode -projectPath . -executeMethod JetskiFeatureCapture.Run -showMenu [-captureDir <dir>]
[InitializeOnLoad]
public static class JetskiFeatureCapture
{
    const string Key = "JetskiFeatureCapture.";
    const float BootSeconds = 2f;     // the loading screen comes first
    static readonly (float t, string action)[] Steps =
    {
        (-1.0f, "shot:loading"), (.8f, "shop"), (1.2f, "shot:shop"), (1.21f, "settings"), (1.5f, "shot:settings"), (1.51f, "menu"), (1.9f, "shot:menu"), (1.95f, "play"),
        (3.2f, "boost"), (3.28f, "shot:boost"),
        (3.3f, "spawn:CreateLogRow"), (4.15f, "shot:log"),
        (4.4f, "spawn:CreateBannerRow"), (5.25f, "shot:banner"),
        (5.4f, "spawn:CreatePowerUp"), (6.2f, "shot:powerup"),
        (6.3f, "spawn:CreateLavaRock"), (7.25f, "shot:lava"),
        (7.4f, "spawn:CreateLightning"), (8.32f, "shot:lightning_warn"), (9.0f, "shot:lightning"),
        (9.3f, "spawn:CreateIceFloe"), (10.2f, "shot:icefloe"),
        (10.25f, "ships"), (11.0f, "shot:ships_side_by_side"), (11.02f, "skiff"), (11.18f, "shot:skiff"), (11.19f, "fx_rock"), (11.34f, "shot:rock_break"), (11.35f, "fx_break"), (11.48f, "shot:break"), (11.49f, "fx_explode"), (11.59f, "shot:explode"),
        (11.6f, "chase"), (12.7f, "shot:chaser"), (12.75f, "side:chaser_side"),
        (12.9f, "duck"), (13.3f, "side:duck"),
        (13.32f, "ramp_jump"), (13.56f, "shot:ramp_jump"), (13.6f, "crash"), (13.95f, "shot:crash_fly"), (15.4f, "shot:gameover"), (15.5f, "curtain_restart"), (17.5f, "curtain_check"),
    };

    static JetskiFeatureCapture() { if (SessionState.GetBool(Key + "on", false)) EditorApplication.update += Tick; }

    public static void Run()
    {
        string dir = "Logs/FeatureCapture";
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-captureDir") dir = args[i + 1];
        Directory.CreateDirectory(dir);
        SessionState.SetString(Key + "dir", Path.GetFullPath(dir));
        SessionState.SetBool(Key + "on", true); SessionState.SetInt(Key + "step", 0); SessionState.SetFloat(Key + "start", -1);
        EditorSceneManager.OpenScene("Assets/Scenes/OceanRun.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    static RenderTexture rt;
    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        var game = Object.FindFirstObjectByType<JetskiRunnerGame>();
        var ui = Object.FindFirstObjectByType<RunnerUI>();
        if (!game || !ui) return;
        var cam = Camera.main;
        if (!rt)
        {
            rt = new RenderTexture(540, 960, 24);
            cam.targetTexture = rt;                     // the canvas lays out against the camera's target size
            var canvas = ui.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1f;
        }
        float start = SessionState.GetFloat(Key + "start", -1);
        if (start < 0) { SessionState.SetFloat(Key + "start", Time.time); return; }
        float t = Time.time - start - BootSeconds;
        int step = SessionState.GetInt(Key + "step", 0);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var gt = typeof(JetskiRunnerGame);
        while (step < Steps.Length && t >= Steps[step].t)
        {
            var a = Steps[step].action;
            if (a == "shop" || a == "menu" || a == "settings")
            {
                var panel = (GameObject)typeof(RunnerUI).GetField(a, flags).GetValue(ui);
                typeof(RunnerUI).GetMethod("Show", flags).Invoke(ui, new object[] { panel });
            }
            else if (a == "play") { RunnerSave.Bank = 640; game.BeginRun(true); JetskiRunnerGame.TestInvulnerable = true; }   // in-memory bank: enough to offer a continue
            else if (a.StartsWith("spawn:"))
            {
                var m = gt.GetMethod(a.Substring(6), flags);
                float z = game.DistanceTravelled + game.Speed * .85f + 6f;
                // clear anything already in front so the new piece is easy to see
                foreach (var go in (System.Collections.Generic.List<GameObject>)gt.GetField("spawned", flags).GetValue(game))
                    if (go && go.transform.position.z > 1f && go.transform.position.z < 60f) go.SetActive(false);
                var ps = m.GetParameters();
                object[] callArgs = ps.Length == 3 ? new object[] { 0, z, a.Contains("Log") } : new object[] { 1, z };
                m.Invoke(game, callArgs);
            }
            else if (a == "ships")
            {
                foreach (var go in (System.Collections.Generic.List<GameObject>)gt.GetField("spawned", flags).GetValue(game))
                    if (go && go.transform.position.z > 1f && go.transform.position.z < 60f) go.SetActive(false);
                float z = game.DistanceTravelled + game.Speed * .85f + 14f;
                for (int lane = 0; lane < 3; lane++) gt.GetMethod("SpawnShip", flags).Invoke(game, new object[] { lane, z, 0f });
            }
            else if (a == "curtain_restart")
                typeof(RunnerUI).GetMethod("AfterCurtain", flags).Invoke(ui, new object[] { (System.Action)(() => game.Restart(true)) });
            else if (a == "curtain_check")
            {
                var curtain = (UnityEngine.UI.Image)typeof(RunnerUI).GetField("curtain", flags).GetValue(ui);
                Debug.Log($"CURTAIN CHECK alpha={curtain.color.a:F2} state={game.State} loading={((GameObject)typeof(RunnerUI).GetField("loading", flags).GetValue(ui)).activeSelf}");
            }
            else if (a == "fx_rock")
            {
                foreach (var go in (System.Collections.Generic.List<GameObject>)gt.GetField("spawned", flags).GetValue(game))
                    if (go && go.transform.position.z > 1f && go.transform.position.z < 60f) go.SetActive(false);
                gt.GetMethod("BreakApart", flags).Invoke(game, new object[] { GameObject.Find("Player Jetski").transform.position + Vector3.forward * 5f, true });
            }
            else if (a == "fx_break" || a == "fx_explode")
            {
                var at = GameObject.Find("Player Jetski").transform.position + Vector3.forward * 5f;
                if (a == "fx_explode") gt.GetMethod("Explode", flags).Invoke(game, new object[] { at });
                else gt.GetMethod("BreakApart", flags).Invoke(game, new object[] { at, false });
            }
            else if (a == "ramp_jump")
            {
                gt.GetMethod("StartJump", flags).Invoke(game, new object[] { 1.1f, 2.2f });
                gt.GetField("jumpSpin", flags).SetValue(game, true);
            }
            else if (a == "boost") gt.GetField("boostLeft", flags).SetValue(game, 2f);
            else if (a == "skiff")
            {
                foreach (var go in (System.Collections.Generic.List<GameObject>)gt.GetField("spawned", flags).GetValue(game))
                    if (go && go.transform.position.z > 1f && go.transform.position.z < 60f) go.SetActive(false);
                float z = game.DistanceTravelled + 9f;
                var prefab = ((TropicalScenery)gt.GetField("scenery", flags).GetValue(game)).Model("37-pirate-skiff");
                for (int lane = 0; lane < 3; lane++) gt.GetMethod("SpawnSkiff", flags).Invoke(game, new object[] { prefab, lane, z, 0f });
            }
            else if (a == "chase") gt.GetField("chaseLeft", flags).SetValue(game, 9f);
            else if (a == "duck") gt.GetMethod("Duck", flags).Invoke(game, null);
            else if (a == "crash") { JetskiRunnerGame.TestInvulnerable = false; gt.GetMethod("Crash", flags).Invoke(game, null); }
            else if (a.StartsWith("shot:")) Capture(cam, a.Substring(5));
            else if (a.StartsWith("side:"))
            {
                var player = GameObject.Find("Player Jetski").transform;
                var side = new GameObject("side cam").AddComponent<Camera>(); side.CopyFrom(cam); side.fieldOfView = 30;
                side.transform.position = player.position + new Vector3(5.5f, .6f, .5f); side.transform.LookAt(player.position + Vector3.up * .5f);
                var srt = new RenderTexture(720, 720, 24); side.targetTexture = srt;
                Save(side, srt, a.Substring(5)); Object.DestroyImmediate(side.gameObject);
            }
            step++;
        }
        SessionState.SetInt(Key + "step", step);
        if (step >= Steps.Length)
        {
            SessionState.SetBool(Key + "on", false);
            EditorApplication.update -= Tick;
            JetskiRunnerGame.TestInvulnerable = false;
            EditorApplication.isPlaying = false;
            Debug.Log("FEATURE CAPTURE DONE");
            if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }

    static void Capture(Camera cam, string name) => Save(cam, rt, name);

    static void Save(Camera cam, RenderTexture target, string name)
    {
        Canvas.ForceUpdateCanvases();
        cam.Render();
        RenderTexture.active = target;
        var tex = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(SessionState.GetString(Key + "dir", "Logs"), name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
#endif
