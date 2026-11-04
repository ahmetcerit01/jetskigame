using System.Collections.Generic;
using UnityEngine;

// Run flow (menu / running / game over + paid continue), score multiplier, the chasing rival pirate,
// power-ups, ducking, jump-only and duck-only obstacles, and the biome hazards.
public sealed partial class JetskiRunnerGame
{
    public enum RunState { Menu, Running, Over }
    RunState state = RunState.Running;
    public RunState State => state;
    // set before reloading the scene so "play again" skips the main menu
    public static bool SkipMenuOnce;

    RunnerAudio audioFx;
    RunnerUI ui;

    // ---------- score ----------
    float score; int revives, runCoins; bool newBest, caught;
    public int Score => Mathf.FloorToInt(score);
    public int Multiplier => 1 + Mathf.Min(5, Mathf.FloorToInt(distanceTravelled / 350f));
    public int RunCoins => runCoins;
    public bool NewBest => newBest;
    public bool Caught => caught;
    public int ReviveCost => 100 << Mathf.Min(revives, 5);
    public float OverTime => crashed ? Time.time - crashTime : 0f;
    public bool CanRevive => state == RunState.Over && OverTime < ReviveWindow + 1f && RunnerSave.Bank >= ReviveCost;
    public const float ReviveWindow = 6f;
    public bool Paused => Time.timeScale == 0f;
    public RunnerAudio Audio => audioFx;
    public float BoostLeft => boostLeft;

    // ---------- power-ups ----------
    float magnetLeft, shieldLeft, doubleLeft, graceLeft;
    Transform shieldBubble;
    public struct PowerStatus { public string name; public float fraction; public Color color; }
    readonly List<PowerStatus> powers = new();
    public IReadOnlyList<PowerStatus> ActivePowers
    {
        get
        {
            powers.Clear();
            void Add(string n, float left, RunnerSave.Upgrade u, Color c) { if (left > 0) powers.Add(new PowerStatus { name = n, fraction = left / RunnerSave.Duration(u), color = c }); }
            Add("MAGNET", magnetLeft, RunnerSave.Upgrade.Magnet, new Color(1f, .3f, .3f));
            Add("SHIELD", shieldLeft, RunnerSave.Upgrade.Shield, new Color(.3f, .85f, 1f));
            Add("x2 COINS", doubleLeft, RunnerSave.Upgrade.DoubleCoins, new Color(1f, .8f, .2f));
            if (boostLeft > 0) powers.Add(new PowerStatus { name = "BOOST", fraction = boostLeft / BoostTime, color = new Color(1f, .55f, .2f) });
            return powers;
        }
    }
    float BoostTime => boostDuration + .7f * RunnerSave.Level(RunnerSave.Upgrade.Boost);

    // ---------- duck / chaser ----------
    float duckLeft, stumbleSlow, stumbleBlend, chaseLeft, introChase;
    bool duckAfterLanding;
    int prevLane = 1;
    Transform chaser;
    float chaserZ = -18f;
    public bool Ducking => duckLeft > 0f;
    public bool Chased => chaseLeft > 0f;
    Material ringOrange, ringBlue, boltMaterial, bubbleMaterial;

    void SetupExtras()
    {
        audioFx = RunnerAudio.Create(gameCamera.gameObject);
        ui = RunnerUI.Create(this);
        CreateChaser();
        CreateShieldBubble();
        if (foamRingMaterial)
        {
            ringOrange = new Material(foamRingMaterial); ringOrange.SetColor("_TintColor", new Color(1f, .35f, .05f, .5f));
            ringBlue = new Material(foamRingMaterial); ringBlue.SetColor("_TintColor", new Color(.55f, .75f, 1f, .5f));
        }
        if (splashMaterial)
        {
            boltMaterial = new Material(splashMaterial) { mainTexture = null }; boltMaterial.SetColor("_TintColor", new Color(.85f, .9f, 1f, .5f));
            bubbleMaterial = new Material(splashMaterial) { mainTexture = null };
        }
        // tests and "play again" go straight into a run (-showMenu lets the UI capture see the menu in batch mode)
        bool skip = SkipMenuOnce || (Application.isBatchMode && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-showMenu") < 0);
        SkipMenuOnce = false;
        if (skip) BeginRun(); else state = RunState.Menu;
    }

    public void BeginRun()
    {
        if (state == RunState.Running && distanceTravelled > 0) return;
        state = RunState.Running;
        introChase = 3.2f;          // the rival shows up behind you at the start, like a warning
    }

    public void Restart(bool toMenu)
    {
        Time.timeScale = 1f;
        SkipMenuOnce = !toMenu;
        UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.buildIndex);
    }

    public void TogglePause()
    {
        if (state != RunState.Running || crashed) return;
        Time.timeScale = Time.timeScale > 0 ? 0 : 1;
    }

    public void Revive()
    {
        if (!CanRevive) return;
        RunnerSave.Bank -= ReviveCost; RunnerSave.Save();
        revives++;
        crashed = false; caught = false; state = RunState.Running;
        jumpTime = -1f; duckLeft = 0; chaseLeft = 0; stumbleSlow = 0; stumbleBlend = 0;
        graceLeft = 2.5f;
        if (rider) rider.Revive();
        // clear the water ahead so the continue is not an instant second crash
        foreach (var go in spawned.ToArray())
        {
            if (!go) continue;
            var t = go.GetComponent<RunnerTrigger>();
            if (t && t.Kind >= TriggerKind.Ramp && go.transform.position.z > -3f && go.transform.position.z < 45f)
            {
                if (t.Kind == TriggerKind.Ship) { ImpactSplash(go.transform.position, 1.4f); Destroy(go); }
                else t.Knock(player.position.x);
            }
        }
        LandingSplash(8f);
        audioFx?.PowerUp();
    }

    void FinishRun()
    {
        state = RunState.Over;
        if (Score > RunnerSave.Best) { RunnerSave.Best = Score; newBest = true; }
        RunnerSave.Save();
    }

    // ---------- per-frame ----------

    void UpdateExtras(float speed)
    {
        float dt = Time.deltaTime;
        score += speed * dt * Multiplier;
        magnetLeft = Mathf.Max(0, magnetLeft - dt);
        shieldLeft = Mathf.Max(0, shieldLeft - dt);
        doubleLeft = Mathf.Max(0, doubleLeft - dt);
        graceLeft = Mathf.Max(0, graceLeft - dt);
        stumbleSlow = Mathf.Max(0, stumbleSlow - dt);
        stumbleBlend = Mathf.MoveTowards(stumbleBlend, stumbleSlow > 0 ? 1f : 0f, dt * (stumbleSlow > 0 ? 7f : 1.1f));
        chaseLeft = Mathf.Max(0, chaseLeft - dt);
        introChase = Mathf.Max(0, introChase - dt);
        duckLeft = Mathf.Max(0, duckLeft - dt);
        if (rider) rider.Tuck = duckLeft > 0 ? 1f : 0f;

        if (magnetLeft > 0)
            foreach (var go in spawned)
            {
                if (!go) continue;
                var t = go.GetComponent<RunnerTrigger>();
                if (!t || t.Kind != TriggerKind.Coin) continue;
                float z = go.transform.position.z;
                if (!t.Pulled && (z > 16f || z < -1f)) continue;
                t.Pulled = true;
                var target = player.position + Vector3.up * .4f;
                go.transform.position = Vector3.MoveTowards(go.transform.position, target, (28f + speed) * dt);
            }

        if (shieldBubble)
        {
            bool on = shieldLeft > 0 || graceLeft > 0;
            bool blink = (shieldLeft > 0 && shieldLeft < 2f) || (shieldLeft <= 0 && graceLeft > 0);
            shieldBubble.gameObject.SetActive(on && (!blink || Mathf.Repeat(Time.time * 6f, 1f) > .35f));
            shieldBubble.localScale = Vector3.one * (2.6f + Mathf.Sin(Time.time * 5f) * .06f);
            shieldBubble.Rotate(0, 40f * dt, 0);
        }
        UpdateChaser(speed);
    }

    // ---------- input actions ----------

    void DownAction()
    {
        if (jumpTime >= 0) { Dive(); duckAfterLanding = true; return; }
        Duck();
    }

    void Duck()
    {
        if (duckLeft <= 0) audioFx?.Carve();
        duckLeft = .85f;
    }

    void OnLanded()
    {
        if (duckAfterLanding) { duckAfterLanding = false; duckLeft = .7f; }
    }

    // ---------- hazards ----------

    // Side-swipe while changing lanes, or running into something light like a barrel: stumble and get chased
    // instead of crashing; twice in a row = caught.
    bool TryStumble(RunnerTrigger t)
    {
        if (t.Wide && !t.Light) return false;
        float lateral = Mathf.Abs(player.position.x - t.transform.position.x);
        bool changing = Mathf.Abs(player.position.x - (lane - 1) * laneWidth) > .3f;
        bool glancing = changing && lateral >= laneWidth * .4f;
        if (!glancing && !t.Light) return false;
        if (t.Light) t.Knock(player.position.x);
        audioFx?.Stumble();
        cameraKick = Mathf.Max(cameraKick, .3f);
        ImpactSplash(player.position, .5f);
        if (rider) rider.Hit();
        if (glancing) lane = prevLane;
        if (chaseLeft > 0) { caught = true; Crash(); return true; }
        chaseLeft = 9f; stumbleSlow = 1.3f;
        return true;
    }

    // true when a shield/grace/invulnerability absorbed the hit
    bool AbsorbHit(RunnerTrigger t)
    {
        if (graceLeft > 0 || TestInvulnerable) { if (t.Kind != TriggerKind.Ship) t.Knock(player.position.x); return true; }
        if (shieldLeft > 0)
        {
            shieldLeft = 0; graceLeft = 1f;
            audioFx?.ShieldBreak();
            ImpactSplash(t.transform.position, 1f);
            if (t.Kind != TriggerKind.Ship) t.Knock(player.position.x);
            return true;
        }
        return false;
    }

    void Pickup(RunnerTrigger t)
    {
        switch (t.Kind)
        {
            case TriggerKind.Magnet: magnetLeft = RunnerSave.Duration(RunnerSave.Upgrade.Magnet); break;
            case TriggerKind.Shield: shieldLeft = RunnerSave.Duration(RunnerSave.Upgrade.Shield); break;
            case TriggerKind.DoubleCoins: doubleLeft = RunnerSave.Duration(RunnerSave.Upgrade.DoubleCoins); break;
        }
        audioFx?.PowerUp();
        EmitDrops(t.transform.position, 14, Vector3.zero, 3f, 1f, 4f, .3f, .6f, CurrentSpeed);
        Destroy(t.gameObject);
    }

    void AddCoin()
    {
        int n = doubleLeft > 0 ? 2 : 1;
        coins += n; runCoins += n;
        RunnerSave.Bank += n;
        score += 10 * n * Multiplier;
        audioFx?.Coin();
    }

    // ---------- chaser: a rival pirate on a dark jetski ----------

    void CreateChaser()
    {
        if (!riderPrefab) return;
        var root = new GameObject("Rival pirate (chaser)").transform;
        var model = Instantiate(riderPrefab, root);
        model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
        foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.materials;   // instanced copies: tint the rival dark and blood-red
            foreach (var m in mats)
                foreach (var prop in new[] { "_Color", "_BaseColor", "baseColorFactor" })
                    if (m.HasProperty(prop)) m.SetColor(prop, m.GetColor(prop) * new Color(.3f, .22f, .26f, 1f) + new Color(.14f, 0, .02f, 0));
            r.materials = mats;
        }
        root.position = new Vector3(0, WaterY, chaserZ);
        chaser = root;
        chaser.gameObject.SetActive(false);
    }

    void UpdateChaser(float speed)
    {
        if (!chaser) return;
        bool show = chaseLeft > 0 || introChase > 0 || (caught && crashed);
        // just off your shoulder and a little behind: directly behind would be under the camera's view
        float targetZ = caught ? .2f : show ? (chaseLeft > 0 && chaseLeft < 2f ? -9f : -1.5f) : -20f;
        chaserZ = Mathf.MoveTowards(chaserZ, targetZ, Time.deltaTime * (show ? 14f : 6f));
        bool visible = chaserZ > -15f;
        if (chaser.gameObject.activeSelf != visible) chaser.gameObject.SetActive(visible);
        if (!visible) return;
        var p = chaser.position;
        float side = player.position.x > .5f ? -1f : player.position.x < -.5f ? 1f : (p.x >= player.position.x ? 1f : -1f);
        p.x = Mathf.Lerp(p.x, player.position.x + side * 1.25f, 1f - Mathf.Exp(-3.2f * Time.deltaTime));   // follows your line with a lag
        p.y = WaterY + Mathf.Sin(Time.time * 6.5f + 1f) * .04f;
        p.z = chaserZ;
        chaser.position = p;
        chaser.rotation = Quaternion.Euler(0, 0, Mathf.Clamp((player.position.x - p.x) * -10f, -12f, 12f));
        if (!crashed && Random.value < .6f)
            EmitDrops(new Vector3(p.x + Random.Range(-.5f, .5f), WaterY + .05f, p.z - 1f), 1, new Vector3(0, 0, -2f), 1.5f, 1.5f, 3f, .3f, .7f, speed);
    }

    void CreateShieldBubble()
    {
        if (!splashMaterial) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Shield bubble"; Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(player, false); go.transform.localPosition = new Vector3(0, .2f, 0);
        var m = new Material(splashMaterial) { mainTexture = null };
        m.SetColor("_TintColor", new Color(.3f, .8f, 1f, .12f));
        var r = go.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        shieldBubble = go.transform;
        go.SetActive(false);
    }

    // ---------- spawning ----------

    GameObject Root(string name, int laneIndex, float z, TriggerKind kind, bool wide = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(movingWorld, false);
        go.transform.localPosition = new Vector3(wide ? 0 : (laneIndex - 1) * laneWidth, WaterY, z);
        var t = go.AddComponent<RunnerTrigger>(); t.Init(this, kind); t.Wide = wide;
        spawned.Add(go);
        return go;
    }

    GameObject Part(GameObject parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material m, Vector3 euler = default)
    {
        var p = CreatePrimitive(parent.name + " part", type, parent.transform, pos, scale, m);
        p.transform.localRotation = Quaternion.Euler(euler);
        return p;   // CreatePrimitive already strips the collider
    }

    static void TriggerBox(GameObject go, Vector3 center, Vector3 size)
    {
        var box = go.AddComponent<BoxCollider>(); box.isTrigger = true; box.center = center; box.size = size;
    }

    Material red, white, ice, lavaRock, lavaGlow, sail, bark;
    Material MakeGlowing(string name, Color color, Color emission)
    {
        var m = emissiveTemplate ? new Material(emissiveTemplate) { name = name, color = color } : Make(name, color, 0, .6f);
        m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission);
        return m;
    }

    // Weathered black sail with a skull and crossbones, drawn from distance functions so every edge stays smooth.
    const int SailWidth = 1024, SailHeight = 448, WideSailRepeat = 3;
    const float SailAspect = SailWidth / (float)SailHeight;

    static float Circle(float x, float y, float cx, float cy, float r) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;
    static float Capsule(float x, float y, float ax, float ay, float bx, float by, float r)
    {
        float dx = bx - ax, dy = by - ay, t = Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy));
        return Circle(x, y, ax + dx * t, ay + dy * t, r);
    }
    static float RoundedBox(float x, float y, float cx, float cy, float hx, float hy, float r)
    {
        float qx = Mathf.Abs(x - cx) - hx + r, qy = Mathf.Abs(y - cy) - hy + r;
        return Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    static float Bone(float x, float y, float ax, float ay, float bx, float by)
    {
        float len = Mathf.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)), nx = -(by - ay) / len * .035f, ny = (bx - ax) / len * .035f;
        float d = Capsule(x, y, ax, ay, bx, by, .03f);
        d = Mathf.Min(d, Mathf.Min(Circle(x, y, ax + nx, ay + ny, .043f), Circle(x, y, ax - nx, ay - ny, .043f)));
        return Mathf.Min(d, Mathf.Min(Circle(x, y, bx + nx, by + ny, .043f), Circle(x, y, bx - nx, by - ny, .043f)));
    }

    static float SkullShape(float x, float y)
    {
        float head = Mathf.Min(Circle(x, y, 0f, .1f, .25f), RoundedBox(x, y, 0f, -.14f, .135f, .1f, .04f));
        float holes = Mathf.Min(Circle(x, y, -.095f, .08f, .07f), Circle(x, y, .095f, .08f, .07f));
        holes = Mathf.Min(holes, Capsule(x, y, 0f, -.015f, 0f, -.055f, .022f));
        for (int i = -1; i <= 1; i++) holes = Mathf.Min(holes, Capsule(x, y, i * .058f, -.17f, i * .058f, -.25f, .007f));
        return Mathf.Max(head, -holes);
    }

    static Texture2D SailTexture()
    {
        var pixels = new Color32[SailWidth * SailHeight];
        float edge = 1.5f / SailHeight;
        var ink = new Color(.86f, .83f, .74f);
        for (int py = 0; py < SailHeight; py++) for (int px = 0; px < SailWidth; px++)
        {
            float u = px / (float)SailWidth, v = 1f - py / (float)SailHeight;   // the cube's -Z face shows the texture upside down
            float cloth = .085f + Mathf.PerlinNoise(u * 18f, v * 5f) * .06f;
            var c = new Color(cloth * 1.05f, cloth * .95f, cloth);
            float x = (u - .5f) * SailAspect, y = v - .52f;
            float skull = SkullShape(x, y);
            float bones = Mathf.Min(Bone(x, y, -.62f, -.31f, .62f, .2f), Bone(x, y, -.62f, .2f, .62f, -.31f));
            bones = Mathf.Max(bones, -(Mathf.Min(Circle(x, y, 0f, .1f, .25f), RoundedBox(x, y, 0f, -.14f, .135f, .1f, .04f)) - .03f));   // a dark gap keeps them behind the skull
            float d = Mathf.Min(skull, bones);
            pixels[py * SailWidth + px] = Color.Lerp(c, ink, Mathf.Clamp01(.5f - d / edge));
        }
        var t = new Texture2D(SailWidth, SailHeight, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        t.SetPixels32(pixels); t.Apply(); return t;
    }

    // Long bark grain; wraps around the log along its length.
    static Texture2D BarkTexture()
    {
        const int n = 128; var t = new Texture2D(n, n, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat };
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float g = Mathf.PerlinNoise(x * .22f, y * .03f) * .65f + Mathf.PerlinNoise(x * .6f + 9f, y * .09f) * .35f;
            float crack = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.28f, .46f, g));
            t.SetPixel(x, y, Color.Lerp(new Color(.08f, .04f, .02f), new Color(.42f, .25f, .12f), crack));
        }
        t.Apply(); return t;
    }
    void ExtraMaterials()
    {
        if (red) return;
        red = MakeGlowing("Magnet red", new Color(.9f, .08f, .06f), new Color(.5f, .04f, .03f));
        white = Make("Buoy white", new Color(.95f, .95f, .92f), 0, .5f);
        ice = Make("Ice floe", new Color(.78f, .93f, 1f), 0, .85f);
        lavaRock = Make("Lava rock", new Color(.09f, .06f, .05f), 0, .2f);
        lavaGlow = MakeGlowing("Lava glow", new Color(1f, .38f, .05f), new Color(2.2f, .7f, .08f));
        sail = Make("Banner sail", Color.white, 0, .15f); sail.mainTexture = SailTexture();
        sailWide = new Material(sail) { name = "Banner sail wide", mainTextureScale = new Vector2(WideSailRepeat, 1f) };
        bark = Make("Log bark", Color.white, 0, .22f); bark.mainTexture = BarkTexture();
        rockChip = Make("Rock chip", new Color(.33f, .32f, .31f), 0, .15f);
        blastFire = MakeBurst("Blast fire", new Color(1f, .55f, .12f, 1f));
        blastSmoke = MakeBurst("Blast smoke", new Color(.13f, .11f, .1f, .8f));
    }

    // ---------- impacts ----------

    Material sailWide, rockChip;
    ParticleSystem blastFire, blastSmoke;
    const int BlastFireCount = 24, BlastSmokeCount = 12, BreakPieceCount = 7;

    ParticleSystem MakeBurst(string name, Color color)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 80; main.playOnAwake = false;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var fade = ps.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var material = new Material(splashMaterial) { name = name }; material.SetColor("_TintColor", color);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        ps.Play();
        return ps;
    }

    // A small blast where the player touched a mine or a meteor.
    void Explode(Vector3 at)
    {
        at.y = Mathf.Max(at.y, .5f);
        var p = new ParticleSystem.EmitParams();
        var drift = Vector3.back * CurrentSpeed * .6f;
        for (int i = 0; i < BlastFireCount && blastFire; i++)
        {
            p.position = at + Random.insideUnitSphere * .35f; p.velocity = drift + Random.onUnitSphere * Random.Range(2f, 6.5f) + Vector3.up * 2f;
            p.startSize = Random.Range(.6f, 1.3f); p.startLifetime = Random.Range(.25f, .5f); p.startColor = Color.white;
            blastFire.Emit(p, 1);
        }
        for (int i = 0; i < BlastSmokeCount && blastSmoke; i++)
        {
            p.position = at + Random.insideUnitSphere * .5f; p.velocity = drift + Vector3.up * Random.Range(1.5f, 4f) + Random.insideUnitSphere * 1.5f;
            p.startSize = Random.Range(1f, 1.8f); p.startLifetime = Random.Range(.6f, 1.1f); p.startColor = Color.white;
            blastSmoke.Emit(p, 1);
        }
        ImpactSplash(at, .8f);
        cameraKick = Mathf.Max(cameraKick, .4f);
        audioFx?.Lava();
    }

    // A barrel or log flying apart in planks, or a rock shattering into chips, tumbling off and into the water.
    // Their own materials are mapped for their models and look wrong on a plain cube, so the pieces have their own.
    void BreakApart(Vector3 at, bool chunky = false)
    {
        ExtraMaterials();
        for (int i = 0; i < BreakPieceCount; i++)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(piece.GetComponent<Collider>());
            piece.name = "Broken piece";
            piece.transform.SetParent(movingWorld, false);
            piece.transform.position = at + Vector3.up * .4f + Random.insideUnitSphere * .35f;
            piece.transform.localRotation = Random.rotation;
            piece.transform.localScale = chunky
                ? new Vector3(Random.Range(.28f, .62f), Random.Range(.24f, .5f), Random.Range(.28f, .62f))
                : new Vector3(Random.Range(.14f, .26f), Random.Range(.1f, .2f), Random.Range(.5f, 1.15f));
            var r = piece.GetComponent<Renderer>();
            r.sharedMaterial = chunky ? rockChip : bark;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            piece.AddComponent<BrokenPiece>();
        }
        EmitDrops(at, 10, Vector3.zero, 3f, 2f, 5f, .25f, .55f, CurrentSpeed);
    }

    sealed class BrokenPiece : MonoBehaviour
    {
        const float Life = 1.3f;
        Vector3 velocity, spin; float age;
        void Awake()
        {
            float side = Random.value < .5f ? -1f : 1f;
            velocity = new Vector3(side * Random.Range(2f, 6.5f), Random.Range(4f, 8.5f), Random.Range(1f, 7f));
            spin = Random.onUnitSphere * Random.Range(300f, 760f);
        }
        void Update()
        {
            age += Time.deltaTime;
            velocity += Physics.gravity * Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
            transform.Rotate(spin * Time.deltaTime, Space.World);
            if (age > Life || transform.position.y < -.4f) Destroy(gameObject);
        }
    }

    void CreatePowerUp(int laneIndex, float z)
    {
        ExtraMaterials();
        var kind = (TriggerKind)((int)TriggerKind.Magnet + Random.Range(0, 3));
        var go = Root("Power-up | " + kind, laneIndex, z, kind);
        go.transform.localPosition += Vector3.up * 1.25f;
        Color glow;
        if (kind == TriggerKind.Magnet)
        {
            glow = new Color(1f, .3f, .3f);
            Part(go, PrimitiveType.Cylinder, new Vector3(-.24f, .08f, 0), new Vector3(.2f, .26f, .2f), red);
            Part(go, PrimitiveType.Cylinder, new Vector3(.24f, .08f, 0), new Vector3(.2f, .26f, .2f), red);
            Part(go, PrimitiveType.Cube, new Vector3(0, -.2f, 0), new Vector3(.68f, .2f, .2f), red);
            Part(go, PrimitiveType.Cylinder, new Vector3(-.24f, .4f, 0), new Vector3(.21f, .07f, .21f), white);
            Part(go, PrimitiveType.Cylinder, new Vector3(.24f, .4f, 0), new Vector3(.21f, .07f, .21f), white);
        }
        else if (kind == TriggerKind.Shield)
        {
            glow = new Color(.3f, .85f, 1f);
            Part(go, PrimitiveType.Cylinder, Vector3.zero, new Vector3(.8f, .05f, .8f), cyan, new Vector3(90, 0, 0));
            Part(go, PrimitiveType.Cylinder, new Vector3(0, 0, -.04f), new Vector3(.5f, .05f, .5f), white, new Vector3(90, 0, 0));
            Part(go, PrimitiveType.Cube, new Vector3(0, 0, -.08f), new Vector3(.12f, .42f, .04f), cyan);
            Part(go, PrimitiveType.Cube, new Vector3(0, 0, -.08f), new Vector3(.42f, .12f, .04f), cyan);
        }
        else
        {
            glow = new Color(1f, .8f, .2f);
            Part(go, PrimitiveType.Cylinder, Vector3.zero, new Vector3(.85f, .06f, .85f), gold, new Vector3(90, 0, 0));
            var label = new GameObject("x2"); label.transform.SetParent(go.transform, false);
            label.transform.localPosition = new Vector3(0, 0, -.08f);
            var tm = label.AddComponent<TextMesh>();
            tm.text = "x2"; tm.fontSize = 64; tm.characterSize = .07f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold; tm.color = new Color(.35f, .15f, 0f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font) { tm.font = font; label.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
            label.transform.localRotation = Quaternion.Euler(0, 180, 0);
        }
        if (bubbleMaterial)
        {
            var b = Part(go, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 1.25f, new Material(bubbleMaterial));
            b.GetComponent<Renderer>().sharedMaterial.SetColor("_TintColor", new Color(glow.r * .5f, glow.g * .5f, glow.b * .5f, .14f));
        }
        var s = go.AddComponent<SphereCollider>(); s.isTrigger = true; s.radius = .8f;
        CreateCoinRow((laneIndex + 1) % 3, z, 3);
    }

    // Floating log boom: jump it. Spans one lane, or (later in the run) all three.
    // Logs are light: running into one stumbles the player and breaks it.
    void CreateLogRow(int freeLane, float z, bool allLanes)
    {
        ExtraMaterials();
        int laneIndex = allLanes ? 1 : (freeLane + Random.Range(1, 3)) % 3;
        float length = allLanes ? laneWidth * 3f + .4f : laneWidth * .95f;
        var go = Root(allLanes ? "Log boom | all lanes (jump)" : "Log boom (jump)", laneIndex, z, TriggerKind.Obstacle, allLanes);
        Part(go, PrimitiveType.Cylinder, new Vector3(0, .2f, 0), new Vector3(.55f, length * .5f, .55f), bark, new Vector3(0, 0, 90));
        for (int i = 0; i < (allLanes ? 4 : 2); i++)
        {
            float x = Mathf.Lerp(-length * .5f, length * .5f, allLanes ? i / 3f : i);
            Part(go, PrimitiveType.Sphere, new Vector3(x, .42f, 0), Vector3.one * .42f, i % 2 == 0 ? red : white);
        }
        TriggerBox(go, new Vector3(0, .35f, 0), new Vector3(length, .8f, .5f));
        go.GetComponent<RunnerTrigger>().Light = true;
        // coins arc over the log in one lane to teach the jump
        int coinLane = allLanes ? freeLane : laneIndex;
        float flight = CurrentSpeed * playerJumpDuration;
        for (int i = 0; i < 5; i++)
        {
            float t = (i + .5f) / 5f;
            CreateCoinRow(coinLane, z - flight * .5f + t * flight, 1);
            var c = spawned[spawned.Count - 1].transform;
            c.localPosition = new Vector3(c.localPosition.x, .6f + 4f * playerJumpHeight * t * (1f - t) * .9f, c.localPosition.z);
        }
        if (!allLanes) CreateCoinRow(freeLane, z - 3f, 3);
    }

    // Hanging sail (or icicles in the cave) between two posts: duck under it with swipe down / S.
    void CreateBannerRow(int freeLane, float z, bool allLanes)
    {
        ExtraMaterials();
        bool cave = BiomeAt(z) == EnvironmentPreset.IceCave;
        int laneIndex = allLanes ? 1 : (freeLane + Random.Range(1, 3)) % 3;
        float width = allLanes ? laneWidth * 3f + .2f : laneWidth * .98f;
        var go = Root(cave ? "Icicle curtain (duck)" : "Hanging sail (duck)", laneIndex, z, TriggerKind.Overhead, allLanes);
        for (int s = -1; s <= 1; s += 2)
            Part(go, PrimitiveType.Cylinder, new Vector3(s * width * .5f, 1.4f, 0), new Vector3(.16f, 1.45f, .16f), cave ? ice : bark);
        Part(go, PrimitiveType.Cube, new Vector3(0, 2.8f, 0), new Vector3(width + .3f, .16f, .16f), cave ? ice : bark);
        var icicles = cave && scenery != null ? scenery.Model(allLanes ? "30-icicle-curtain" : "29-icicle-cluster") : null;
        if (icicles)
        {
            // squashed so the tips stop at the top of the duck gap: ducking must visibly clear them
            var m = Instantiate(icicles, go.transform);
            float h = allLanes ? 4.7f : 5f, sx = (allLanes ? width / 12f : width / 3.6f), sy = 1.45f / h;
            m.transform.localPosition = new Vector3(0, 2.9f - h * sy, 0);
            m.transform.localScale = new Vector3(sx, sy, allLanes ? .5f : sx);
        }
        else if (cave)
        {
            int count = Mathf.RoundToInt(width / .3f);
            for (int i = 0; i < count; i++)
            {
                float x = -width * .5f + (i + .5f) * width / count, len = Random.Range(.6f, 1.3f);
                Part(go, PrimitiveType.Capsule, new Vector3(x, 2.75f - len * .5f, 0), new Vector3(.16f, len * .5f, .16f), ice);
            }
        }
        else
        {
            Part(go, PrimitiveType.Cube, new Vector3(0, 2.1f, 0), new Vector3(width, 1.25f, .05f), allLanes ? sailWide : sail);
            Part(go, PrimitiveType.Cube, new Vector3(0, 1.5f, -.03f), new Vector3(width, .1f, .06f), red);   // red hem marks the danger height
        }
        TriggerBox(go, new Vector3(0, 2.05f, 0), new Vector3(width - .2f, 1.4f, .5f));
        CreateCoinRow(allLanes ? freeLane : laneIndex, z - 4f, 3);
    }

    // Biome-specific hazard in one lane; the free lane gets coins.
    void CreateBiomeHazardRow(int freeLane, float z)
    {
        ExtraMaterials();
        var biome = BiomeAt(z);
        int laneIndex = (freeLane + Random.Range(1, 3)) % 3;
        switch (biome)
        {
            case EnvironmentPreset.IceCave: CreateIceFloe(laneIndex, z); break;
            case EnvironmentPreset.Volcanic: CreateLavaRock(laneIndex, z); break;
            case EnvironmentPreset.Storm: CreateLightning(laneIndex, z); break;
            default: if (Random.value < .5f) CreateLogRow(freeLane, z, false); else CreateBannerRow(freeLane, z, false); return;
        }
        CreateCoinRow(freeLane, z - 2f, 4);
    }

    void CreateIceFloe(int laneIndex, float z)
    {
        var go = Root("Drifting ice floe", laneIndex, z, TriggerKind.Obstacle);
        var floe = scenery != null ? scenery.Model("33-ice-floe") : null;
        if (floe)
        {
            var m = Instantiate(floe, go.transform);
            m.transform.localPosition = new Vector3(0, -.15f, 0);
            m.transform.localRotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            m.transform.localScale = new Vector3(.75f, 2.2f, .75f);   // thicker than the flat source, so it reads as something to dodge
        }
        else for (int i = 0; i < 3; i++)
            Part(go, PrimitiveType.Cube, new Vector3(Random.Range(-.35f, .35f), .1f + i * .12f, Random.Range(-.3f, .3f)),
                 new Vector3(Random.Range(.9f, 1.4f), .5f - i * .1f, Random.Range(.8f, 1.2f)), ice, new Vector3(Random.Range(-8, 8), Random.Range(0, 90), Random.Range(-8, 8)));
        TriggerBox(go, new Vector3(0, .3f, 0), new Vector3(1.3f, .9f, 1.1f));
        // drifts slowly towards the middle lane and back, so it can close a lane while you watch
        var t = go.GetComponent<RunnerTrigger>();
        t.DriftCenter = go.transform.localPosition.x; t.DriftRange = laneWidth * .22f;   // never far enough to block the free lane t.DriftDir = laneIndex == 0 ? 1 : laneIndex == 2 ? -1 : (Random.value < .5f ? 1 : -1);
    }

    // Volcanic zone: small meteors keep raining into the water beside the lanes, purely for show.
    const string MeteorModel = "40-meteor";
    const float MeteorHazardScale = 1.25f, MeteorGapMin = .9f, MeteorGapMax = 2.2f, MeteorFall = 26f, MeteorStartHeight = 30f;
    const float MeteorSideMin = 6.5f, MeteorSideMax = 18f, MeteorAheadMin = 35f, MeteorAheadMax = 95f, MeteorZone = .4f;
    float nextMeteorIn;

    void UpdateAmbientMeteors()
    {
        if (atmosphere.embers < MeteorZone || scenery == null) return;
        nextMeteorIn -= Time.deltaTime;
        if (nextMeteorIn > 0f) return;
        nextMeteorIn = Random.Range(MeteorGapMin, MeteorGapMax);
        var model = scenery.Model(MeteorModel);
        if (!model) return;
        var go = Instantiate(model, movingWorld);
        float side = Random.value < .5f ? -1f : 1f;
        go.transform.localPosition = new Vector3(side * Random.Range(MeteorSideMin, MeteorSideMax), MeteorStartHeight, distanceTravelled + Random.Range(MeteorAheadMin, MeteorAheadMax));
        go.transform.localScale = Vector3.one * Random.Range(1.1f, 2.4f);
        go.AddComponent<FallingMeteor>().Init(this, side);
        spawned.Add(go);
    }

    sealed class FallingMeteor : MonoBehaviour
    {
        JetskiRunnerGame game; float side; Vector3 spin;
        public void Init(JetskiRunnerGame owner, float fromSide)
        {
            game = owner; side = fromSide;
            spin = new Vector3(Random.Range(90f, 240f), Random.Range(-120f, 120f), Random.Range(-120f, 120f));
        }
        void Update()
        {
            transform.localPosition += new Vector3(side * 3f, -MeteorFall, 0f) * Time.deltaTime;
            transform.Rotate(spin * Time.deltaTime);
            if (transform.localPosition.y > 0f) return;
            game.ImpactSplash(transform.position, .9f);
            Destroy(gameObject);
        }
    }

    void CreateLavaRock(int laneIndex, float z)
    {
        var go = Root("Falling lava rock", laneIndex, z, TriggerKind.Obstacle);
        var rock = new GameObject("Rock"); rock.transform.SetParent(go.transform, false);
        var meteor = scenery != null ? scenery.Model(MeteorModel) : null;
        if (meteor)
        {
            var m = Instantiate(meteor, rock.transform);
            m.transform.localPosition = new Vector3(0, .1f, 0); m.transform.localScale = Vector3.one * MeteorHazardScale;
        }
        else
        {
            Part(rock, PrimitiveType.Sphere, Vector3.zero, new Vector3(1.3f, 1.05f, 1.2f), lavaRock);
            for (int i = 0; i < 5; i++) Part(rock, PrimitiveType.Sphere, Random.onUnitSphere * .5f, Vector3.one * Random.Range(.25f, .4f), lavaGlow);
        }
        rock.transform.localPosition = new Vector3(0, 22f, 0);
        TriggerBox(go, new Vector3(0, .45f, 0), new Vector3(1.2f, 1.1f, 1.2f));
        var fall = go.GetComponent<RunnerTrigger>();
        fall.Explosive = true;
        fall.SetupFall(rock.transform, Telegraph(go, ringOrange));
    }

    void CreateLightning(int laneIndex, float z)
    {
        var go = Root("Lightning strike", laneIndex, z, TriggerKind.Strike);
        TriggerBox(go, new Vector3(0, 1f, 0), new Vector3(laneWidth * .85f, 3f, 3f));
        go.GetComponent<Collider>().enabled = false;
        var bolt = new GameObject("Bolt").AddComponent<LineRenderer>();
        bolt.transform.SetParent(go.transform, false);
        bolt.useWorldSpace = false; bolt.positionCount = 9; bolt.widthMultiplier = .35f;
        if (boltMaterial) bolt.sharedMaterial = boltMaterial;
        bolt.startColor = bolt.endColor = new Color(.9f, .95f, 1f, 1f);
        bolt.enabled = false;
        go.GetComponent<RunnerTrigger>().SetupStrike(bolt, Telegraph(go, ringBlue));
    }

    Transform Telegraph(GameObject parent, Material m)
    {
        if (!m) return null;
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "Warning ring"; Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(parent.transform, false);
        q.transform.localPosition = new Vector3(0, .06f, 0); q.transform.localRotation = Quaternion.Euler(90, 0, 0);
        q.transform.localScale = Vector3.one * 2.2f;
        var r = q.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return q.transform;
    }

    // ---------- trigger behaviours for the new hazards ----------

    sealed partial class RunnerTrigger
    {
        public bool Wide, Pulled, Light, Explosive, Rocky;
        public float DriftCenter, DriftRange, DriftDir;
        Transform fallingRock, warning; LineRenderer bolt;
        float strikeTime = -1f;

        public void SetupFall(Transform rock, Transform ring) { fallingRock = rock; warning = ring; }
        public void SetupStrike(LineRenderer line, Transform ring) { bolt = line; warning = ring; }

        void UpdateHazard()
        {
            if (warning) warning.localScale = Vector3.one * (2.1f + Mathf.Sin(Time.time * 9f) * .25f);
            if (DriftRange > 0 && !game.crashed)
            {
                var p = transform.localPosition;
                p.x += DriftDir * 1.1f * Time.deltaTime;
                if (Mathf.Abs(p.x - DriftCenter) > DriftRange) { DriftDir = -DriftDir; p.x = DriftCenter + Mathf.Sign(p.x - DriftCenter) * DriftRange; }
                transform.localPosition = p;
            }
            float z = transform.position.z, speed = Mathf.Max(8f, game.CurrentSpeed);
            if (fallingRock && fallingRock.localPosition.y > 0f && z < speed * 2.2f + 8f)
            {
                var p = fallingRock.localPosition;
                p.y = Mathf.Max(0f, p.y - 34f * Time.deltaTime);
                fallingRock.localPosition = p; fallingRock.Rotate(140f * Time.deltaTime, 60f * Time.deltaTime, 0);
                if (p.y <= 0f)
                {
                    game.ImpactSplash(transform.position, 1.3f);
                    game.audioFx?.Lava();
                    if (warning) { Destroy(warning.gameObject); warning = null; }
                }
            }
            if (bolt && strikeTime < 0 && z < speed * .3f + 2.5f)
            {
                strikeTime = 0f; bolt.enabled = true;
                GetComponent<Collider>().enabled = true;
                for (int i = 0; i < bolt.positionCount; i++)
                    bolt.SetPosition(i, new Vector3(i == 0 || i == bolt.positionCount - 1 ? 0 : Random.Range(-.6f, .6f), 26f * (1f - i / (float)(bolt.positionCount - 1)), 0));
                game.lightningFlash = 1f;
                game.audioFx?.Thunder();
                game.ImpactSplash(transform.position, 1.2f);
            }
            if (strikeTime >= 0)
            {
                strikeTime += Time.deltaTime;
                bolt.widthMultiplier = .35f * (1f - strikeTime / .45f) + .05f;
                if (strikeTime > .45f) bolt.enabled = false;
                if (strikeTime > .6f) { bolt.enabled = false; GetComponent<Collider>().enabled = false; if (warning) { Destroy(warning.gameObject); warning = null; } }
            }
        }
    }
}
