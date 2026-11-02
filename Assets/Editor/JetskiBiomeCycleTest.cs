#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Play Mode check of the biome cycle: one continuous OceanRun at 3x speed (invulnerable) through
// Tropical -> Pirate Harbor -> Cave Passage -> Volcanic -> Storm -> Tropical, asserting at every zone that the
// visible world (shore chunks / tunnel, atmosphere, weather, backdrop volcano) belongs to that biome, that the
// HUD state keeps working, then a normal crash and a restart.
//   Unity -batchmode -projectPath . -executeMethod JetskiBiomeCycleTest.Run [-captureDir <dir>]
// Writes biome_cycle_report.txt and screenshots; exits 0 on pass, 1 on failure in batch mode.
[InitializeOnLoad]
public static class JetskiBiomeCycleTest
{
    const string Scene = "Assets/Scenes/OceanRun.unity";
    const string Key = "JetskiBiomeCycleTest.";
    const float TestTimeScale = 3f;
    static readonly JetskiRunnerGame.EnvironmentPreset[] Expected =
    {
        JetskiRunnerGame.EnvironmentPreset.TropicalRun, JetskiRunnerGame.EnvironmentPreset.PirateCove, JetskiRunnerGame.EnvironmentPreset.IceCave,
        JetskiRunnerGame.EnvironmentPreset.Volcanic, JetskiRunnerGame.EnvironmentPreset.Storm, JetskiRunnerGame.EnvironmentPreset.TropicalRun,
    };

    enum Phase { Cycle, Crash, Restart, Done }
    static Phase phase;
    static readonly List<JetskiRunnerGame.EnvironmentPreset> sequence = new();
    static readonly HashSet<int> zonesChecked = new(), boundariesShot = new();
    static readonly List<string> report = new(), failures = new();
    static readonly Dictionary<int, Color> zoneMeanColor = new();
    static float lastDistance, phaseStart, bannerSeenFor = -1;
    static int startCoins, errorsLogged;
    static bool hooked;

    static JetskiBiomeCycleTest()
    {
        if (SessionState.GetBool(Key + "running", false)) Hook();
    }

    [MenuItem("Jetski Runner/Test Biome Cycle (Play Mode)")]
    public static void Run()
    {
        string dir = "Logs/BiomeCycleTest";
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-captureDir") dir = args[i + 1];
        Directory.CreateDirectory(dir);
        SessionState.SetString(Key + "dir", Path.GetFullPath(dir));
        var active = EditorSceneManager.GetActiveScene().path;
        SessionState.SetString(Key + "restore", string.IsNullOrEmpty(active) ? Scene : active);
        SessionState.SetBool(Key + "running", true);
        SessionState.SetBool(Key + "done", false);
        EditorSceneManager.OpenScene(Scene);
        Hook();
        EditorApplication.isPlaying = true;
    }

    static void Hook()
    {
        if (hooked) return;
        hooked = true;
        EditorApplication.update += Tick;
        Application.logMessageReceived += (msg, stack, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error) { errorsLogged++; Note("LOGGED " + type + ": " + msg); }
        };
    }

    static string Dir => SessionState.GetString(Key + "dir", "Logs");
    static void Note(string line) { report.Add(line); Debug.Log("BIOME TEST " + line); }
    static void Check(bool ok, string what) { Note((ok ? "PASS  " : "FAIL  ") + what); if (!ok) failures.Add(what); }

    static void Tick()
    {
        if (!SessionState.GetBool(Key + "running", false)) return;
        if (!EditorApplication.isPlaying)
        {
            if (SessionState.GetBool(Key + "done", false) && !EditorApplication.isPlayingOrWillChangePlaymode) Finish();
            return;
        }
        var game = Object.FindFirstObjectByType<JetskiRunnerGame>();
        if (!game) return;

        switch (phase)
        {
            case Phase.Cycle: CycleTick(game); break;
            case Phase.Crash: CrashTick(game); break;
            case Phase.Restart: RestartTick(game); break;
        }
    }

    // ---------- full biome cycle ----------

    static void CycleTick(JetskiRunnerGame game)
    {
        if (sequence.Count == 0)
        {
            JetskiRunnerGame.TestInvulnerable = true;
            Time.timeScale = TestTimeScale;
            startCoins = game.Coins;
            sequence.Add(game.CurrentBiome);
            Note($"start: biome {game.CurrentBiome}, zone length {game.BiomeLength} m, time scale {TestTimeScale}x");
            Check(game.BannerText == JetskiRunnerGame.BiomeNames[0], "HUD banner shows the starting zone name");
        }
        float d = game.DistanceTravelled;
        if (d + .001f < lastDistance) Check(false, $"distance went backwards {lastDistance:0.0} -> {d:0.0}");
        lastDistance = d;
        if (game.Crashed) { Check(false, $"crashed at {d:0} m while invulnerable"); EndPlay(); return; }

        var biome = game.CurrentBiome;
        if (biome != sequence[sequence.Count - 1])
        {
            sequence.Add(biome);
            Note($"transition at {d:0.0} m -> {biome}");
            bannerSeenFor = d;
        }
        if (bannerSeenFor >= 0 && d - bannerSeenFor > 3f)
        {
            Check(game.BannerText == JetskiRunnerGame.BiomeNames[(int)biome], $"HUD banner announces {JetskiRunnerGame.BiomeNames[(int)biome]}");
            bannerSeenFor = -1;
        }

        int zone = Mathf.FloorToInt(d / game.BiomeLength);
        float inZone = d - zone * game.BiomeLength;
        // approaching a boundary: the next biome must already be visible ahead
        if (inZone > game.BiomeLength - 45f && boundariesShot.Add(zone)) CheckApproach(game, zone);
        if (inZone > game.BiomeLength * .5f && zonesChecked.Add(zone)) CheckZone(game, zone, biome);

        if (sequence.Count >= Expected.Length && inZone > game.BiomeLength * .55f && zonesChecked.Contains(zone))
        {
            Check(sequence.SequenceEqual(Expected), "biome order " + string.Join(" -> ", sequence));
            Check(game.Coins > startCoins, $"coins collected during the run ({startCoins} -> {game.Coins})");
            for (int i = 1; i < 5; i++)
            {
                var a = zoneMeanColor[i - 1]; var b = zoneMeanColor[i];
                float diff = (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b)) / 3f;
                Check(diff > .03f, $"screenshot colour changes {Expected[i - 1]} -> {Expected[i]} (mean diff {diff:0.000})");
            }
            JetskiRunnerGame.TestInvulnerable = false;
            Time.timeScale = 1f;
            phase = Phase.Crash; phaseStart = Time.realtimeSinceStartup;
            Note("cycle done; invulnerability off, waiting for a normal crash");
        }
    }

    static List<Transform> Scenery(JetskiRunnerGame game, float zMin, float zMax)
    {
        var world = GameObject.Find("Endless scenery and pickups");
        var list = new List<Transform>();
        if (!world) return list;
        foreach (Transform t in world.transform)
            if (t.position.z >= zMin && t.position.z <= zMax) list.Add(t);
        return list;
    }

    static int Count(List<Transform> items, string prefix) => items.Count(t => t.name.StartsWith(prefix));

    static void CheckZone(JetskiRunnerGame game, int zone, JetskiRunnerGame.EnvironmentPreset biome)
    {
        string name = JetskiRunnerGame.BiomeNames[(int)biome];
        Note($"--- zone {zone} {biome} at {game.DistanceTravelled:0} m");
        var near = Scenery(game, -10f, 90f);
        int own = Count(near, "Shore | " + name);
        int foreign = near.Count(t => t.name.StartsWith("Shore | ") && !t.name.StartsWith("Shore | " + name));
        int tunnel = near.Count(t => t.name.StartsWith("Cave passage | ice tunnel"));
        if (biome == JetskiRunnerGame.EnvironmentPreset.IceCave)
        {
            Check(tunnel >= 3, $"{name}: ice tunnel segments around the player ({tunnel})");
            Check(own + foreign == 0, $"{name}: no open-sea shore chunks inside the tunnel ({own + foreign})");
        }
        else
        {
            Check(own >= 2, $"{name}: biome shore chunks around the player ({own})");
            Check(foreign == 0, $"{name}: no shore chunks from other biomes nearby ({foreign})");
            Check(tunnel == 0, $"{name}: no tunnel segments ({tunnel})");
        }
        var want = JetskiRunnerGame.AtmosphereOf(biome);
        var have = game.CurrentAtmosphere;
        Check(Close(have.fog, want.fog) && Close(have.skyHorizon, want.skyHorizon) && Close(have.waterShallow, want.waterShallow),
            $"{name}: fog/sky/water colours match the biome (fog {Fmt(have.fog)})");
        Check(Close(RenderSettings.fogColor, want.fog, .25f), $"{name}: RenderSettings fog applied ({Fmt(RenderSettings.fogColor)})");

        int rainCount = Particles("Storm rain"), emberCount = Particles("Volcanic embers");
        bool storm = biome == JetskiRunnerGame.EnvironmentPreset.Storm, volcanic = biome == JetskiRunnerGame.EnvironmentPreset.Volcanic;
        Check(storm ? rainCount > 150 : rainCount == 0, $"{name}: rain particles {rainCount}");
        Check(volcanic ? emberCount > 10 : emberCount == 0, $"{name}: ember particles {emberCount}");
        var volcano = GameObject.Find("Backdrop volcano");
        Check(volcanic ? volcano && volcano.transform.position.y > -10f : !volcano, $"{name}: backdrop volcano {(volcano ? "visible" : "hidden")}");
        zoneMeanColor[Mathf.Min(zone, 5)] = Shot($"zone{zone}_{biome}.png");
    }

    static void CheckApproach(JetskiRunnerGame game, int zone)
    {
        var next = game.BiomeAt((zone + 1) * game.BiomeLength + 1f);
        string name = JetskiRunnerGame.BiomeNames[(int)next];
        var ahead = Scenery(game, 45f, 190f);
        int count = next == JetskiRunnerGame.EnvironmentPreset.IceCave
            ? Count(ahead, "Cave passage")
            : Count(ahead, "Shore | " + name);
        Check(count > 0, $"approaching {name}: its scenery is already spawned ahead ({count})");
        Shot($"zone{zone}_approach_{next}.png");
    }

    static int Particles(string name)
    {
        var go = GameObject.Find(name);
        return go ? go.GetComponent<ParticleSystem>().particleCount : 0;
    }

    static bool Close(Color a, Color b, float tol = .02f) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < tol * 3f;
    static string Fmt(Color c) => $"({c.r:0.00},{c.g:0.00},{c.b:0.00})";

    // ---------- crash + restart: the normal game flow still works after the cycle ----------

    static void CrashTick(JetskiRunnerGame game)
    {
        if (game.Crashed)
        {
            Check(true, $"normal crash still ends the run (at {game.DistanceTravelled:0} m, {game.CurrentBiome})");
            Shot("crash.png");
            UnityEngine.SceneManagement.SceneManager.LoadScene(game.gameObject.scene.buildIndex); // same call as the game's restart
            phase = Phase.Restart; phaseStart = Time.realtimeSinceStartup;
            return;
        }
        if (Time.realtimeSinceStartup - phaseStart > 90f) { Check(false, "no crash within 90 s with invulnerability off"); EndPlay(); }
    }

    static void RestartTick(JetskiRunnerGame game)
    {
        if (Time.realtimeSinceStartup - phaseStart < 1.5f) return;
        Check(!game.Crashed && game.DistanceTravelled < 40f && game.CurrentBiome == JetskiRunnerGame.EnvironmentPreset.TropicalRun,
            $"restart begins a fresh run in {game.CurrentBiome} ({game.DistanceTravelled:0} m)");
        EndPlay();
    }

    static void EndPlay()
    {
        JetskiRunnerGame.TestInvulnerable = false;
        Time.timeScale = 1f;
        Check(errorsLogged == 0, $"no errors or exceptions logged during play ({errorsLogged})");
        bool pass = failures.Count == 0;
        Note(pass ? "RESULT PASS" : "RESULT FAIL (" + failures.Count + "): " + string.Join("; ", failures));
        File.WriteAllText(Path.Combine(Dir, "biome_cycle_report.txt"), string.Join("\n", report) + "\n", Encoding.UTF8);
        SessionState.SetBool(Key + "pass", pass);
        SessionState.SetBool(Key + "done", true);
        phase = Phase.Done;
        EditorApplication.isPlaying = false;
    }

    static void Finish()
    {
        SessionState.SetBool(Key + "running", false);
        SessionState.SetBool(Key + "done", false);
        EditorApplication.update -= Tick;
        // leave the editor where the user was, not in the last test scene
        var restore = SessionState.GetString(Key + "restore", Scene);
        if (File.Exists(restore)) EditorSceneManager.OpenScene(restore);
        bool pass = SessionState.GetBool(Key + "pass", false);
        Debug.Log("BIOME TEST DONE " + (pass ? "PASS" : "FAIL") + " report: " + Path.Combine(Dir, "biome_cycle_report.txt"));
        if (Application.isBatchMode) EditorApplication.Exit(pass ? 0 : 1);
    }

    static Color Shot(string file)
    {
        var cam = Camera.main;
        if (!cam) return Color.black;
        var rt = new RenderTexture(540, 960, 24);
        cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(Dir, file), tex.EncodeToPNG());
        var px = tex.GetPixels32();
        double r = 0, g = 0, b = 0;
        foreach (var p in px) { r += p.r; g += p.g; b += p.b; }
        Object.DestroyImmediate(tex); rt.Release();
        float n = px.Length * 255f;
        return new Color((float)(r / n), (float)(g / n), (float)(b / n));
    }
}
#endif
