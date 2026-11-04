#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Gameplay-camera shots of the cave passage: approach, gate, dressed tunnel, ice floe, duck curtain and exit.
//   Unity -batchmode -projectPath . -executeMethod JetskiIceGallery.Run [-captureDir <dir>]
[InitializeOnLoad]
public static class JetskiIceGallery
{
    const string Key = "JetskiIceGallery.";
    static readonly (float d, string action)[] Steps =
    {
        (-1730, "shot:trop_kiz"), (-1510, "shot:trop_yum"), (-930, "shot:harbor_kiz"), (-710, "shot:harbor_yum"), (-520, "shot:harbor_kiz2"), (-320, "shot:harbor_yum2"), (-70, "shot:approach_far"), (-38, "shot:approach"), (-24, "shot:gate_near"), (-12, "shot:gate_inside"),
        (20, "shot:tunnel_a"), (90, "shot:tunnel_b"), (160, "shot:tunnel_c"),
        (200, "floe"), (0, "wait"), (280, "banner"), (0, "wait"), (360, "single"), (0, "wait"),
        (840, "shot:exit_near"), (876, "shot:exit_gate"), (905, "shot:after_exit"),
    };
    static readonly FieldInfo Spawned = typeof(JetskiRunnerGame).GetField("spawned", BindingFlags.NonPublic | BindingFlags.Instance);
    static RenderTexture rt; static int step; static float shootAt = float.MaxValue; static string pending;

    static JetskiIceGallery() { if (SessionState.GetBool(Key + "on", false)) EditorApplication.update += Tick; }

    public static void Run()
    {
        string dir = "Logs/IceGallery";
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-captureDir") dir = args[i + 1];
        Directory.CreateDirectory(dir);
        SessionState.SetString(Key + "dir", Path.GetFullPath(dir));
        SessionState.SetBool(Key + "on", true);
        EditorSceneManager.OpenScene("Assets/Scenes/OceanRun.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        var game = Object.FindFirstObjectByType<JetskiRunnerGame>();
        if (!game) return;
        var cam = Camera.main;
        if (!rt) { rt = new RenderTexture(576, 1024, 24); cam.targetTexture = rt; JetskiRunnerGame.TestInvulnerable = true; }
        float zone = game.BiomeLength * 2f, d = game.DistanceTravelled - zone;
        // fast forward to just before the cave, then normal speed for clean shots
        Time.timeScale = d < -90f ? 2.5f : 1.5f;
        if (pending != null && d >= shootAt) { Save(cam, pending); pending = null; step++; }
        if (pending != null || step >= Steps.Length)
        {
            if (step >= Steps.Length) Stop();
            return;
        }
        var s = Steps[step];
        if (s.action == "wait") { step++; return; }
        if (d < s.d) return;
        var t = typeof(JetskiRunnerGame); var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        float ahead = game.DistanceTravelled + game.Speed * .8f + 8f;
        if (s.action != "shot" && !s.action.StartsWith("shot:"))
            foreach (var go in (System.Collections.Generic.List<GameObject>)Spawned.GetValue(game))
                if (go && !go.name.StartsWith("Cave passage")) go.SetActive(false);       // clear the view for the obstacle being checked
        if (s.action.StartsWith("shot:"))
        {
            Save(cam, s.action.Substring(5)); step++;
            if (s.action == "shot:tunnel_a")
            {
                var up = new GameObject("up cam").AddComponent<Camera>(); up.CopyFrom(cam); up.targetTexture = rt;
                up.transform.position = new Vector3(0, 4, 2); up.transform.rotation = Quaternion.Euler(-18, 0, 0); up.fieldOfView = 80;
                up.Render(); Save(up, "debug_up"); Object.DestroyImmediate(up.gameObject);
            }
        }
        else if (s.action == "floe") { t.GetMethod("CreateIceFloe", flags).Invoke(game, new object[] { 1, ahead }); pending = "floe"; shootAt = d + game.Speed * .55f; }
        else if (s.action == "banner") { t.GetMethod("CreateBannerRow", flags).Invoke(game, new object[] { 1, ahead, true }); pending = "duck_curtain"; shootAt = d + game.Speed * .55f; }
        else if (s.action == "single") { t.GetMethod("CreateBannerRow", flags).Invoke(game, new object[] { 1, ahead, false }); pending = "duck_single"; shootAt = d + game.Speed * .55f; }
    }

    static void Stop()
    {
        SessionState.SetBool(Key + "on", false);
        EditorApplication.update -= Tick;
        JetskiRunnerGame.TestInvulnerable = false; Time.timeScale = 1;
        EditorApplication.isPlaying = false;
        Debug.Log("ICE GALLERY DONE");
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }

    static void Save(Camera cam, string name)
    {
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(SessionState.GetString(Key + "dir", "Logs"), name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
#endif
