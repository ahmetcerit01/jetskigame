#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Plays OceanRun, triggers a jump and a lane change, and saves frames of the wake / takeoff / landing / carve splashes.
//   Unity -batchmode -projectPath . -executeMethod JetskiSplashCapture.Run [-captureDir <dir>]
[InitializeOnLoad]
public static class JetskiSplashCapture
{
    const string Key = "JetskiSplashCapture.";
    // time (s), action, shot name
    static readonly (float t, string action, string shot)[] Steps =
    {
        (2.5f, null, "wake"), (2.6f, "jump", null), (2.68f, null, "takeoff"), (3.0f, null, "air"),
        (3.42f, null, "landing_a"), (3.55f, null, "landing_b"), (3.75f, null, "landing_c"),
        (4.5f, "lane", null), (4.62f, null, "carve"), (6f, "jump", null), (6.3f, "dive", null), (6.5f, null, "dive_land"),
    };

    static JetskiSplashCapture() { if (SessionState.GetBool(Key + "on", false)) EditorApplication.update += Tick; }

    public static void Run()
    {
        JetskiSplashSetup.Apply();
        string dir = "Logs/SplashCapture";
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-captureDir") dir = args[i + 1];
        Directory.CreateDirectory(dir);
        SessionState.SetString(Key + "dir", Path.GetFullPath(dir));
        SessionState.SetBool(Key + "on", true); SessionState.SetInt(Key + "step", 0); SessionState.SetFloat(Key + "start", -1);
        EditorSceneManager.OpenScene("Assets/Scenes/OceanRun.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        var game = Object.FindFirstObjectByType<JetskiRunnerGame>();
        if (!game) return;
        JetskiRunnerGame.TestInvulnerable = true;
        float start = SessionState.GetFloat(Key + "start", -1);
        if (start < 0) { SessionState.SetFloat(Key + "start", Time.time); return; }
        float t = Time.time - start;
        int step = SessionState.GetInt(Key + "step", 0);
        while (step < Steps.Length && t >= Steps[step].t)
        {
            var s = Steps[step];
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            if (s.action == "jump") typeof(JetskiRunnerGame).GetMethod("PlayerJump", flags).Invoke(game, null);
            if (s.action == "dive") typeof(JetskiRunnerGame).GetMethod("Dive", flags).Invoke(game, null);
            if (s.action == "lane") typeof(JetskiRunnerGame).GetMethod("ChangeLane", flags).Invoke(game, new object[] { 1 });
            if (s.shot != null) Capture(s.shot);
            step++;
        }
        SessionState.SetInt(Key + "step", step);
        if (step >= Steps.Length)
        {
            SessionState.SetBool(Key + "on", false);
            EditorApplication.update -= Tick;
            JetskiRunnerGame.TestInvulnerable = false;
            EditorApplication.isPlaying = false;
            Debug.Log("SPLASH CAPTURE DONE");
            if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }

    static void Capture(string name)
    {
        var cam = Camera.main;
        var rt = new RenderTexture(540, 960, 24);
        cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
        RenderTexture.active = rt;
        var tex = new Texture2D(540, 960, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 540, 960), 0, 0); tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(SessionState.GetString(Key + "dir", "Logs"), name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex); rt.Release();
    }
}
#endif
