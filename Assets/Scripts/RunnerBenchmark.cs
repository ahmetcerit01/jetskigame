#if JETSKI_BENCHMARK
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

// Benchmark builds only (JetskiBuild.MacBenchmark): drives one invulnerable run through all five biomes uncapped and
// writes a per-biome report, then quits. Release builds measure frame time; development builds add render counters.
//   JetskiRunner -benchmarkOut report.txt [-benchmarkCapped] -screen-width 576 -screen-height 1024 -screen-fullscreen 0
// -benchmarkCapped keeps the game's own vsync/60 cap, so slow frames are the ones a player would actually see.
[DefaultExecutionOrder(-1000)]
public sealed class RunnerBenchmark : MonoBehaviour
{
    const int Zones = 5, WarmupFrames = 120;
    const float TimeoutSeconds = 600f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("Benchmark");
        DontDestroyOnLoad(go);
        go.AddComponent<RunnerBenchmark>();
    }

    sealed class Counter
    {
        public string Name; public ProfilerRecorder Rec; public double Sum, Max;
        public void Sample() { double v = Rec.LastValue; Sum += v; if (v > Max) Max = v; }
    }

    sealed class Zone
    {
        public string Biome;
        public readonly List<float> Frames = new();
        public readonly Dictionary<string, (double sum, double max)> Counters = new();
        public string Snapshot;
        public int ShipsAtStart;
        public float EndSpeed;
    }

    static readonly (ProfilerCategory cat, string name)[] CounterNames =
    {
        (ProfilerCategory.Render, "Draw Calls Count"), (ProfilerCategory.Render, "Batches Count"), (ProfilerCategory.Render, "SetPass Calls Count"),
        (ProfilerCategory.Render, "Triangles Count"), (ProfilerCategory.Render, "Shadow Casters Count"),
        (ProfilerCategory.Internal, "Main Thread"), (ProfilerCategory.Memory, "GC Allocated In Frame"),
        (ProfilerCategory.Memory, "Gfx Used Memory"), (ProfilerCategory.Memory, "Total Used Memory"),
    };

    static readonly string[] MarkerNames =
    {
        "Instantiate", "Shader.CreateGPUProgram", "Shader.Parse", "CreateGPUProgram", "GC.Collect", "Camera.Render", "Culling", "Shadows.RenderShadowMap",
        "BehaviourUpdate", "Physics.Processing", "Physics.Simulate", "Physics.SyncColliderTransform", "Physics.SyncRigidbodyTransform", "Animators.Update",
        "Destroy", "CleanupUnusedCachedData", "AssetBundle.LoadAsset", "Gfx.UploadTexture", "Texture.AwakeFromLoad", "Mesh.AwakeFromLoad", "Loading.UpdatePreloading", "UnloadUnusedAssets",
        "PostLateUpdate.UpdateAllRenderers", "PreUpdate.AIUpdate", "TransformChangedDispatch", "ParticleSystem.Update", "Gfx.WaitForPresentOnGfxThread",
    };
    readonly List<(string name, ProfilerRecorder rec)> markers = new();

    readonly Zone[] zones = new Zone[Zones];
    readonly List<Counter> counters = new();
    JetskiRunnerGame game;
    string outPath = "benchmark_report.txt";
    // Screenshots cost a long frame each, so a measuring run takes none: list distances here only to look at a build.
    static readonly int[] ShotsAt = { };

    // What a phone build has to meet.
    const float BudgetFps = 58f, BudgetFrameMs = 50f; const int BudgetDrawCalls = 500;
    int shot;
    int frames;
    bool skipFrame, capped;
    readonly List<string> spikes = new();

    void Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-benchmarkOut") outPath = args[i + 1];
        capped = System.Array.IndexOf(args, "-benchmarkCapped") >= 0;
#if UNITY_IOS
        capped = true;      // a phone is judged at the frame cap it ships with
        outPath = Path.Combine(Application.persistentDataPath, Debug.isDebugBuild ? "dev.txt" : "release.txt");
        try { File.Delete(outPath); } catch (IOException) { }   // no leftover report can pass for this run
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
#endif
        Application.runInBackground = true;
        if (Debug.isDebugBuild)
        {
            foreach (var c in CounterNames) counters.Add(new Counter { Name = c.name, Rec = ProfilerRecorder.StartNew(c.cat, c.name) });
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (System.Array.IndexOf(MarkerNames, d.Name) >= 0) markers.Add((d.Name, ProfilerRecorder.StartNew(d.Category, d.Name)));
            }
        }
    }

    void Update()
    {
        if (!game)
        {
            game = FindFirstObjectByType<JetskiRunnerGame>();
            if (!game) return;
        }
        if (!capped)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;   // the game's Start sets 60, so this is reapplied until the run ends
        }
        frames++;
#if UNITY_IOS
        if (frames == 170) ScreenCapture.CaptureScreenshot((Debug.isDebugBuild ? "dev" : "release") + "_menu.png");
        if (frames < 190) return;   // leave the menu up long enough to photograph it
#endif
        JetskiRunnerGame.TestInvulnerable = true;
        game.BeginRun();

        if (frames <= WarmupFrames + 190) return;
        if (Time.unscaledTime > TimeoutSeconds) { Write("TIMED OUT\n"); Application.Quit(1); enabled = false; return; }

        int index = Mathf.FloorToInt(game.DistanceTravelled / game.BiomeLength);
        if (index >= Zones) { Write(""); Application.Quit(0); enabled = false; return; }
#if UNITY_IOS
        while (shot < ShotsAt.Length && game.DistanceTravelled >= ShotsAt[shot])
        {
            ScreenCapture.CaptureScreenshot((Debug.isDebugBuild ? "dev" : "release") + "_d" + ShotsAt[shot] + ".png");
            shot++;
        }
#endif
        var zone = zones[index] ??= new Zone { Biome = game.CurrentBiome.ToString(), ShipsAtStart = game.ShipsSailed };

        if (skipFrame) skipFrame = false;   // the frame after a snapshot pays for the snapshot, not for the game
        else
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            zone.Frames.Add(ms);
            zone.EndSpeed = game.Speed;
            if (ms > 34f && spikes.Count < 150)
            {
                var sb = new StringBuilder($"  {ms,6:F1} ms at {game.DistanceTravelled,6:F0} m {zone.Biome,-12} main {(counters.Count > 5 ? counters[5].Rec.LastValue / 1e6 : -1),6:F1} | ");
                foreach (var m in markers) { double v = m.rec.LastValue / 1e6; if (v > 2) sb.Append(m.name).Append('=').Append(v.ToString("F1")).Append(' '); }
                sb.Append('\n');
                spikes.Add(sb.ToString());
            }
            foreach (var c in counters)
            {
                double v = c.Rec.LastValue;
                zone.Counters.TryGetValue(c.Name, out var s);
                zone.Counters[c.Name] = (s.sum + v, System.Math.Max(s.max, v));
            }
        }
        if (Debug.isDebugBuild && zone.Snapshot == null && game.DistanceTravelled / game.BiomeLength - index > .5f)
        {
            zone.Snapshot = VisibleMeshes();
            skipFrame = true;
        }
    }

    static string VisibleMeshes()
    {
        var tally = new Dictionary<string, (int count, long tris)>();
        void Add(string mesh, long tris)
        {
            tally.TryGetValue(mesh, out var t);
            tally[mesh] = (t.count + 1, t.tris + tris);
        }
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!r.isVisible) continue;
            var f = r.GetComponent<MeshFilter>();
            if (f && f.sharedMesh) Add(Label(f.sharedMesh, r), Triangles(f.sharedMesh));
        }
        foreach (var r in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            if (r.isVisible && r.sharedMesh) Add(r.sharedMesh.name + " (skinned)", Triangles(r.sharedMesh));
        var sb = new StringBuilder();
        foreach (var e in tally.OrderByDescending(e => e.Value.tris).Take(14))
            sb.Append($"    {e.Value.tris,8} tris  x{e.Value.count,-3} {e.Key}\n");
        return sb.ToString();
    }

    // Meshy meshes all share one name, so their colour texture says which model they are.
    static string Label(Mesh mesh, Renderer r)
    {
        var tex = r.sharedMaterial && r.sharedMaterial.HasProperty("_MainTex") ? r.sharedMaterial.mainTexture : null;
        return mesh.name.StartsWith("Mesh_") || mesh.name.Length == 0 ? (tex ? tex.name : "unnamed " + r.name) : mesh.name;
    }

    static long Triangles(Mesh m)
    {
        long n = 0;
        for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i) / 3;
        return n;
    }

    void Write(string prefix)
    {
        var sb = new StringBuilder(prefix);
        sb.Append($"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")} | {(capped ? "CAPPED" : "UNCAPPED")} | {SystemInfo.deviceModel} | {SystemInfo.graphicsDeviceName} | {SystemInfo.graphicsDeviceType} | {Screen.width}x{Screen.height} | quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} | MSAA {QualitySettings.antiAliasing}\n\n");
        sb.Append("biome          frames   avgFPS  median_ms  p95_ms  p99_ms  max_ms  over16.7ms  over33ms\n");
        foreach (var z in zones.Where(z => z != null && z.Frames.Count > 0))
        {
            var s = z.Frames.OrderBy(f => f).ToArray();
            float P(float q) => s[Mathf.Min(s.Length - 1, (int)(s.Length * q))];
            sb.Append($"{z.Biome,-14} {s.Length,6} {1000f / s.Average(),8:F0} {P(.5f),10:F2} {P(.95f),7:F2} {P(.99f),7:F2} {s[^1],7:F1} {s.Count(f => f > 16.7f),11} {s.Count(f => f > 33.3f),9}\n");
        }
        AppendBudgets(sb);
        sb.Append($"sailing ships per biome: {string.Join(", ", zones.Select((z, i) => z == null ? "-" : ((i + 1 < Zones && zones[i + 1] != null ? zones[i + 1].ShipsAtStart : game.ShipsSailed) - z.ShipsAtStart).ToString()))}\n");
        sb.Append($"speed at end of each biome: {string.Join(", ", zones.Select(z => z == null ? "-" : z.EndSpeed.ToString("F1")))}\n");
        sb.Append($"\nmarkers found: {string.Join(", ", markers.Select(m => m.name))}\nframes over 34 ms (first {spikes.Count}):\n");
        foreach (var l in spikes) sb.Append(l);
        if (Debug.isDebugBuild)
        {
            sb.Append("\ncounters per biome (mean / max):\n");
            foreach (var z in zones.Where(z => z != null && z.Frames.Count > 0))
            {
                sb.Append(z.Biome).Append('\n');
                foreach (var c in z.Counters) sb.Append($"  {c.Key,-24} {c.Value.sum / z.Frames.Count,16:F0} {c.Value.max,16:F0}\n");
                sb.Append("  heaviest visible meshes at mid-zone:\n").Append(z.Snapshot);
            }
            AppendAssets<Texture2D>(sb, "textures");
            AppendAssets<Mesh>(sb, "meshes");
        }
        var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        Directory.CreateDirectory(dir);
        File.WriteAllText(outPath, sb.ToString());
    }

    void AppendBudgets(StringBuilder sb)
    {
        var over = new List<string>();
        foreach (var z in zones.Where(z => z != null && z.Frames.Count > 0))
        {
            float fps = 1000f / z.Frames.Average(), worst = z.Frames.Max();
            if (capped && fps < BudgetFps) over.Add($"{z.Biome} {fps:F0} FPS (min {BudgetFps:F0})");
            if (worst > BudgetFrameMs) over.Add($"{z.Biome} frame {worst:F0} ms (max {BudgetFrameMs:F0})");
            if (z.Counters.TryGetValue("Draw Calls Count", out var draws) && draws.max > BudgetDrawCalls) over.Add($"{z.Biome} {draws.max:F0} draw calls (max {BudgetDrawCalls})");
        }
        bool complete = zones.All(z => z != null && z.Frames.Count > 0);
        string drawNote = Debug.isDebugBuild ? "" : " (draw calls are only measured in a development build)";
        sb.Append("\nBUDGET: ").Append(!complete ? "INCOMPLETE RUN" : over.Count == 0 ? "PASS" : "OVER").Append(drawNote).Append('\n');
        foreach (var o in over) sb.Append("  over: ").Append(o).Append('\n');
        sb.Append('\n');
    }

    static void AppendAssets<T>(StringBuilder sb, string label) where T : Object
    {
        var all = Resources.FindObjectsOfTypeAll<T>().Select(o => (o.name, size: Profiler.GetRuntimeMemorySizeLong(o))).OrderByDescending(e => e.size).ToArray();
        sb.Append($"\nloaded {label}: {all.Length}, {all.Sum(e => e.size) / (1024 * 1024)} MB; largest:\n");
        foreach (var e in all.Take(15)) sb.Append($"  {e.size / (1024f * 1024f),7:F1} MB  {e.name}\n");
    }

    void OnDestroy()
    {
        foreach (var c in counters) c.Rec.Dispose();
        foreach (var m in markers) m.rec.Dispose();
    }
}
#endif
