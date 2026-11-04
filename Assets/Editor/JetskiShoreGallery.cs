#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Close-up of every Meshy shore chunk standing in the real water, to check that things sit on the ground.
//   Unity -batchmode -projectPath . -executeMethod JetskiShoreGallery.Run [-captureDir <dir>]
[InitializeOnLoad]
public static class JetskiShoreGallery
{
    const string Key = "JetskiShoreGallery.";
    static readonly string[] Chunks = { "Palm cove", "Palm grove",  "Coast cliff", "Cliff corner", "Rock arch", "Boulders", "Pirate beach", "Pirate camp", "Sile lighthouse" };

    static JetskiShoreGallery() { if (SessionState.GetBool(Key + "on", false)) EditorApplication.update += Tick; }

    public static void Run()
    {
        string dir = "Logs/ShoreGallery";
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-captureDir") dir = args[i + 1];
        Directory.CreateDirectory(dir);
        SessionState.SetString(Key + "dir", Path.GetFullPath(dir));
        SessionState.SetBool(Key + "on", true); SessionState.SetFloat(Key + "start", -1);
        EditorSceneManager.OpenScene("Assets/Scenes/OceanRun.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || !Object.FindFirstObjectByType<JetskiRunnerGame>()) return;
        float start = SessionState.GetFloat(Key + "start", -1);
        if (start < 0) { SessionState.SetFloat(Key + "start", Time.realtimeSinceStartup); return; }
        if (Time.realtimeSinceStartup - start < 1.5f) return;
        SessionState.SetBool(Key + "on", false);
        EditorApplication.update -= Tick;
        Time.timeScale = 0f;
        RenderSettings.fog = false;
        var world = GameObject.Find("Endless scenery and pickups"); if (world) world.SetActive(false);
        var player = GameObject.Find("Player Jetski"); if (player) player.SetActive(false);
        var main = Camera.main;
        var cam = new GameObject("Gallery cam").AddComponent<Camera>(); cam.CopyFrom(main); cam.fieldOfView = 42; main.enabled = false; cam.depthTextureMode = DepthTextureMode.Depth;
        Transform templates = null;
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>()) if (t.name == "Shore chunk templates") templates = t;
        string dir = SessionState.GetString(Key + "dir", "Logs");
        foreach (var name in Chunks)
        {
            var src = templates ? templates.Find(name) : null;   // first variant of each
            if (!src) { Debug.LogError("GALLERY missing " + name); continue; }
            var clone = Object.Instantiate(src.gameObject);
            clone.transform.position = new Vector3(0, -.42f, 40f); clone.SetActive(true);
            var b = new Bounds(clone.transform.position, Vector3.zero);
            foreach (var r in clone.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            float d = Mathf.Max(b.size.x, b.size.y * 1.1f, b.size.z) * 1.35f;
            foreach (var (tag, dir3) in new[] { ("a", new Vector3(-.75f, .26f, -.85f)), ("b", new Vector3(-.95f, .1f, .25f)) })
            {
                cam.transform.position = b.center + dir3.normalized * d; cam.transform.LookAt(b.center);
                var rt = new RenderTexture(760, 620, 24); cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(760, 620, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 760, 620), 0, 0); tex.Apply();
                RenderTexture.active = null; cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine(dir, name.Replace(' ', '_') + "_" + tag + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex); rt.Release();
            }
            Object.DestroyImmediate(clone);
        }
        var m = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Meshy/Shore/14-sandy-shore.prefab").GetComponentInChildren<MeshFilter>().sharedMesh;
        Debug.Log($"GALLERY DONE readable={m.isReadable} verts={m.vertexCount} bounds={m.bounds}");
        Time.timeScale = 1f;
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }
}
#endif
