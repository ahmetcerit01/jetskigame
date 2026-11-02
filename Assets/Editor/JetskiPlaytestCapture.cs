#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Batch-mode smoke test: plays every runner scene for a few seconds and saves portrait screenshots.
//   Unity -batchmode -projectPath . -executeMethod JetskiPlaytestCapture.Run [-captureDir <dir>]
// Survives the play-mode domain reload through SessionState.
[InitializeOnLoad]
public static class JetskiPlaytestCapture
{
    static readonly string[] Scenes = { "Assets/Scenes/OceanRun.unity", "Assets/Scenes/PirateCove.unity", "Assets/Scenes/IceCave.unity" };
    static readonly float[] ShotTimes = { 1.5f, 4f, 7f };
    const string Key = "JetskiPlaytestCapture.";

    static JetskiPlaytestCapture()
    {
        if (SessionState.GetInt(Key + "scene", -1) >= 0) EditorApplication.update += Tick;
    }

    [MenuItem("Jetski Runner/Playtest Capture")]
    public static void Run()
    {
        string dir = "Logs/PlaytestCapture";
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-captureDir") dir = args[i + 1];
        Directory.CreateDirectory(dir);
        SessionState.SetString(Key + "dir", Path.GetFullPath(dir));
        StartScene(0);
    }

    static void StartScene(int index)
    {
        SessionState.SetInt(Key + "scene", index);
        SessionState.SetInt(Key + "shot", 0);
        SessionState.SetFloat(Key + "start", -1f);
        EditorSceneManager.OpenScene(Scenes[index]);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        int scene = SessionState.GetInt(Key + "scene", -1);
        if (scene < 0) { EditorApplication.update -= Tick; return; }
        if (!EditorApplication.isPlaying)
        {
            int next = SessionState.GetInt(Key + "next", -1);
            if (next >= 0 && !EditorApplication.isPlayingOrWillChangePlaymode) { SessionState.EraseInt(Key + "next"); StartScene(next); }
            return;
        }

        float start = SessionState.GetFloat(Key + "start", -1f);
        if (start < 0) { SessionState.SetFloat(Key + "start", Time.realtimeSinceStartup); return; }
        float t = Time.realtimeSinceStartup - start;
        int shot = SessionState.GetInt(Key + "shot", 0);

        if (shot < ShotTimes.Length && t >= ShotTimes[shot])
        {
            string name = Path.GetFileNameWithoutExtension(Scenes[scene]) + "_" + shot;
            Capture(name + ".png");
            if (shot == 0) CaptureCloseups(name);
            SessionState.SetInt(Key + "shot", ++shot);
        }
        if (shot >= ShotTimes.Length)
        {
            EditorApplication.isPlaying = false;
            if (scene + 1 < Scenes.Length) SessionState.SetInt(Key + "next", scene + 1);
            else
            {
                SessionState.EraseInt(Key + "scene");
                EditorApplication.update -= Tick;
                Debug.Log("PLAYTEST CAPTURE DONE: " + SessionState.GetString(Key + "dir", ""));
                if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(0);
            }
        }
    }

    // Side and three-quarter close-ups of the rider to check seat, grips, heading and waterline.
    static void CaptureCloseups(string name)
    {
        var player = GameObject.Find("Player Jetski");
        if (!player) { Debug.LogError("PLAYTEST: no Player Jetski"); return; }
        var main = Camera.main;
        var cam = new GameObject("Closeup camera").AddComponent<Camera>();
        cam.CopyFrom(main); cam.fieldOfView = 30; cam.aspect = 1f;
        Vector3 c = player.transform.position + Vector3.up * .3f;
        (string tag, Vector3 offset)[] views = { ("side", new Vector3(5.5f, .4f, 0)), ("threequarter", new Vector3(3.2f, 1.8f, 3.6f)) };
        foreach (var (tag, offset) in views)
        {
            cam.transform.position = c + offset; cam.transform.LookAt(c);
            Capture(name + "_" + tag + ".png", cam, 720, 720);
        }
        Object.DestroyImmediate(cam.gameObject);
    }

    static void Capture(string file, Camera cam = null, int width = 540, int height = 960)
    {
        cam = cam ? cam : Camera.main;
        if (!cam) { Debug.LogError("PLAYTEST: no main camera"); return; }
        var rt = new RenderTexture(width, height, 24);
        var previous = cam.targetTexture;
        cam.targetTexture = rt; cam.Render(); cam.targetTexture = previous;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        RenderTexture.active = null;
        string path = Path.Combine(SessionState.GetString(Key + "dir", "Logs"), file);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex); rt.Release();
        Debug.Log("PLAYTEST shot " + path);
    }
}
#endif
