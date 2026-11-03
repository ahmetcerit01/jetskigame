using System.Collections.Generic;
using UnityEngine;

public sealed partial class JetskiRunnerGame : MonoBehaviour
{
    // Biomes in run order. The run cycles through them by distance and loops back to the first.
    public enum EnvironmentPreset { TropicalRun, PirateCove, IceCave, Volcanic, Storm }
    public const int BiomeCount = 5;
    public static readonly string[] BiomeNames = { "TROPICAL ISLAND", "PIRATE HARBOR", "CAVE PASSAGE", "VOLCANIC ZONE", "STORM" };
    [Tooltip("Biome the run starts in; the cycle continues from here.")]
    [SerializeField] EnvironmentPreset environment = EnvironmentPreset.TropicalRun;
    [Header("Biome cycle")]
    [SerializeField] float biomeLength = 320f;
    [SerializeField] float biomeBlend = 24f;
    [Header("Run")]
    [SerializeField] float forwardSpeed = 12f;
    [SerializeField] float laneWidth = 2.3f;
    [SerializeField] float spawnAhead = 72f;
    [SerializeField] Shader tropicalWaterShader;
    [SerializeField] GameObject goldPrefab;
    [SerializeField] GameObject riderPrefab;
    [Header("Pirate Escape assets")]
    [SerializeField] GameObject[] obstaclePrefabs;
    [SerializeField] GameObject fuelPrefab;
    [SerializeField] GameObject rampPrefab;
    [SerializeField] GameObject pirateShipPrefab;
    [SerializeField] float shipSailSpeed = 7f;
    [SerializeField] float shipLength = 8f;
    [SerializeField] GameObject sideRockPrefab;
    [SerializeField] GameObject dockBarrelPrefab;
    [SerializeField] Shader skyShader;
    [SerializeField] Material sprayMaterial;
    [SerializeField] Shader postFxShader;
    [Tooltip("Standard material with emission on: referencing it keeps that shader variant in player builds.")]
    [SerializeField] Material emissiveTemplate;
    [Header("Water splash FX (Jetski Runner > Apply Water Splash FX)")]
    [SerializeField] Material splashMaterial;
    [SerializeField] Material foamRingMaterial;
    [SerializeField] GameObject iceTunnelPrefab;
    [SerializeField] float iceTunnelLength = 24f;
    [SerializeField] float boostMultiplier = 1.6f;
    [SerializeField] float boostDuration = 3.5f;
    [Tooltip("Ramp jump")]
    [SerializeField] float jumpDuration = 1.1f;
    [SerializeField] float jumpHeight = 2.2f;
    [Header("Speed ramp")]
    [Tooltip("Cruising speed the run approaches; forwardSpeed is the starting speed.")]
    [SerializeField] float maxSpeed = 30f;
    [Tooltip("Metres for the speed to cover ~63% of the way from start to max.")]
    [SerializeField] float speedRampDistance = 1500f;
    [Header("Player jump (swipe up / Up / W / Space)")]
    [SerializeField] float playerJumpDuration = .8f;
    [SerializeField] float playerJumpHeight = 1.7f;

    const float WaterY = -.42f;
    Transform player, movingWorld;
    TropicalScenery scenery;
    readonly float[] nextShoreAt = { -12f, -2f };
    readonly List<ParticleSystem> sprays = new();
    readonly List<float> sprayRates = new();
    ParticleSystem splashDrops, splashRings;
    float cameraKick, peakFallSpeed;
    Camera gameCamera;
    readonly List<Transform> recycledWater = new();
    readonly List<GameObject> spawned = new();
    Light sun;
    Material skyMaterial;
    ParticleSystem rain, embers;
    float nextCaveAt, tunnelMinZ, lightningTimer = 3f, lightningFlash, bannerTime;
    bool tunnelMeasured;
    EnvironmentPreset shownBiome;
    Atmosphere atmosphere;
    Material sea, seaLight, foam, gold, orange, cyan, charcoal, skin, wood, rock, palmLeaf;
    Material waterSurfaceMaterial;
    static Shader fallbackShader;
    int lane = 1, coins;
    float distanceTravelled, nextRowAt = 32f, laneBlend, boostLeft, jumpTime = -1f, crashTime;
    float activeJumpDuration = 1f, activeJumpHeight;
    bool crashed, landingCued, touchSwiped;
    PirateRider rider;
    Vector2 touchStart;
    // Subway-Surfers-style ramp: quick early gains that ease off towards maxSpeed.
    float RunSpeed => forwardSpeed + (Mathf.Max(maxSpeed, forwardSpeed) - forwardSpeed) * (1f - Mathf.Exp(-distanceTravelled / Mathf.Max(1f, speedRampDistance)));
    // 0 at the start speed, 1 at max speed; scales spacing, steering and jump timing.
    float SpeedProgress => Mathf.InverseLerp(forwardSpeed, Mathf.Max(maxSpeed, forwardSpeed + .01f), RunSpeed);
    float CurrentSpeed => crashed || state != RunState.Running ? 0f : RunSpeed * (boostLeft > 0 ? boostMultiplier : 1f) * (stumbleSlow > 0 ? .78f : 1f);
    float JumpOffset => jumpTime < 0 ? 0f : 4f * activeJumpHeight * jumpTime * (1f - jumpTime);
    public float Speed => CurrentSpeed;
    public bool Airborne => jumpTime >= 0;

    // Read-only state for the HUD, tests and other systems.
    public EnvironmentPreset CurrentBiome => BiomeAt(distanceTravelled);
    public float DistanceTravelled => distanceTravelled;
    public float BiomeLength => biomeLength;
    public int Coins => coins;
    public bool Crashed => crashed;
    public Atmosphere CurrentAtmosphere => atmosphere;
    public string BannerText => bannerTime > 0 ? BiomeNames[(int)shownBiome] : null;
    public float BannerTime => bannerTime;
    // Play-mode tests drive a full cycle without crashing; obstacles are smashed as if boosting.
    public static bool TestInvulnerable;

    public EnvironmentPreset BiomeAt(float distance) =>
        (EnvironmentPreset)(((int)environment + Mathf.FloorToInt(Mathf.Max(0f, distance) / biomeLength)) % BiomeCount);

    // ---------- biome atmosphere ----------

    [System.Serializable]
    public struct Atmosphere
    {
        public Color sunColor; public float sunIntensity; public Vector3 sunEuler;
        public Color ambientSky, ambientEquator, ambientGround;
        public Color fog; public float fogStart, fogEnd;
        public Color skyTop, skyHorizon, skyBelow;
        public Color waterShallow, waterDeep, waterFar, waterTrough, waterSky; public float waveSpeed;
        public Color mountain, cloud;
        public float rain, embers, lightning, volcano, fov;
        public Color gradeTint; public float reflectionLift, bloom, saturation, contrast, vignette, waterFoam, sunGlow;

        public static Atmosphere Lerp(Atmosphere a, Atmosphere b, float t)
        {
            Color C(Color x, Color y) => Color.Lerp(x, y, t);
            float F(float x, float y) => Mathf.Lerp(x, y, t);
            return new Atmosphere
            {
                sunColor = C(a.sunColor, b.sunColor), sunIntensity = F(a.sunIntensity, b.sunIntensity), sunEuler = Vector3.Lerp(a.sunEuler, b.sunEuler, t),
                ambientSky = C(a.ambientSky, b.ambientSky), ambientEquator = C(a.ambientEquator, b.ambientEquator), ambientGround = C(a.ambientGround, b.ambientGround),
                fog = C(a.fog, b.fog), fogStart = F(a.fogStart, b.fogStart), fogEnd = F(a.fogEnd, b.fogEnd),
                skyTop = C(a.skyTop, b.skyTop), skyHorizon = C(a.skyHorizon, b.skyHorizon), skyBelow = C(a.skyBelow, b.skyBelow),
                waterShallow = C(a.waterShallow, b.waterShallow), waterDeep = C(a.waterDeep, b.waterDeep), waterFar = C(a.waterFar, b.waterFar),
                waterTrough = C(a.waterTrough, b.waterTrough), waterSky = C(a.waterSky, b.waterSky), waveSpeed = F(a.waveSpeed, b.waveSpeed),
                mountain = C(a.mountain, b.mountain), cloud = C(a.cloud, b.cloud),
                rain = F(a.rain, b.rain), embers = F(a.embers, b.embers), lightning = F(a.lightning, b.lightning), volcano = F(a.volcano, b.volcano), fov = F(a.fov, b.fov),
                gradeTint = C(a.gradeTint, b.gradeTint), reflectionLift = F(a.reflectionLift, b.reflectionLift), bloom = F(a.bloom, b.bloom), saturation = F(a.saturation, b.saturation), contrast = F(a.contrast, b.contrast),
                vignette = F(a.vignette, b.vignette), waterFoam = F(a.waterFoam, b.waterFoam), sunGlow = F(a.sunGlow, b.sunGlow),
            };
        }
    }

    static Color Rgb(float r, float g, float b) => new(r, g, b, 1f);

    public static Atmosphere AtmosphereOf(EnvironmentPreset biome)
    {
        switch (biome)
        {
            case EnvironmentPreset.PirateCove: return new Atmosphere   // golden late afternoon over the harbor
            {
                sunColor = Rgb(1f, .83f, .6f), sunIntensity = 1.1f, sunEuler = new Vector3(30, -52, 0),
                ambientSky = Rgb(.8f, .74f, .68f), ambientEquator = Rgb(.72f, .62f, .5f), ambientGround = Rgb(.36f, .4f, .42f),
                fog = Rgb(.93f, .8f, .64f), fogStart = 55, fogEnd = 400,
                skyTop = Rgb(.32f, .48f, .76f), skyHorizon = Rgb(.99f, .8f, .58f), skyBelow = Rgb(.72f, .7f, .66f),
                waterShallow = Rgb(.13f, .6f, .7f), waterDeep = Rgb(.05f, .43f, .64f), waterFar = Rgb(.6f, .6f, .62f), waterTrough = Rgb(.03f, .33f, .53f), waterSky = Rgb(.97f, .82f, .64f), waveSpeed = 1f,
                mountain = Rgb(.52f, .5f, .44f), cloud = Rgb(1f, .88f, .76f), fov = 50,
                gradeTint = Rgb(1.04f, .99f, .92f), bloom = .75f, saturation = 1.04f, contrast = 1.08f, vignette = .28f, waterFoam = .3f, sunGlow = .85f,
            };
            case EnvironmentPreset.IceCave: return new Atmosphere      // dim glacial tunnel
            {
                sunColor = Rgb(.36f, .88f, 1f), sunIntensity = .8f, sunEuler = new Vector3(70, -20, 0),
                ambientSky = Rgb(.27f, .55f, .65f), ambientEquator = Rgb(.25f, .52f, .62f), ambientGround = Rgb(.18f, .4f, .5f),
                fog = Rgb(.015f, .18f, .24f), fogStart = 40, fogEnd = 140,
                skyTop = Rgb(.02f, .14f, .2f), skyHorizon = Rgb(.03f, .2f, .26f), skyBelow = Rgb(.02f, .14f, .2f),
                waterShallow = Rgb(.05f, .45f, .5f), waterDeep = Rgb(.01f, .25f, .33f), waterFar = Rgb(.01f, .17f, .23f), waterTrough = Rgb(.005f, .15f, .21f), waterSky = Rgb(.2f, .62f, .72f), waveSpeed = .8f,
                mountain = Rgb(.3f, .5f, .6f), cloud = Rgb(.35f, .45f, .5f), fov = 56,
                gradeTint = Rgb(.97f, 1.01f, 1.04f), reflectionLift = 1.7f, bloom = .9f, saturation = 1.08f, contrast = 1.1f, vignette = .42f, waterFoam = .2f, sunGlow = 0f,
            };
            case EnvironmentPreset.Volcanic: return new Atmosphere     // ash haze lit by lava
            {
                sunColor = Rgb(1f, .46f, .2f), sunIntensity = .95f, sunEuler = new Vector3(24, -30, 0),
                ambientSky = Rgb(.56f, .3f, .22f), ambientEquator = Rgb(.46f, .23f, .16f), ambientGround = Rgb(.18f, .09f, .06f),
                fog = Rgb(.34f, .14f, .08f), fogStart = 35, fogEnd = 270,
                skyTop = Rgb(.17f, .06f, .06f), skyHorizon = Rgb(.86f, .36f, .15f), skyBelow = Rgb(.3f, .1f, .06f),
                waterShallow = Rgb(.2f, .11f, .09f), waterDeep = Rgb(.11f, .055f, .05f), waterFar = Rgb(.38f, .15f, .08f), waterTrough = Rgb(.06f, .03f, .03f), waterSky = Rgb(.95f, .46f, .2f), waveSpeed = .9f,
                mountain = Rgb(.15f, .11f, .11f), cloud = Rgb(.36f, .26f, .23f), embers = 1, volcano = 1, fov = 52,
                gradeTint = Rgb(1.02f, .98f, .94f), bloom = .9f, saturation = .98f, contrast = 1.08f, vignette = .42f, waterFoam = .25f, sunGlow = .6f,
            };
            case EnvironmentPreset.Storm: return new Atmosphere        // overcast squall, rain and lightning
            {
                sunColor = Rgb(.66f, .73f, .82f), sunIntensity = .55f, sunEuler = new Vector3(60, -10, 0),
                ambientSky = Rgb(.4f, .46f, .52f), ambientEquator = Rgb(.31f, .37f, .41f), ambientGround = Rgb(.15f, .18f, .2f),
                fog = Rgb(.33f, .38f, .42f), fogStart = 22, fogEnd = 210,
                skyTop = Rgb(.17f, .2f, .24f), skyHorizon = Rgb(.43f, .48f, .51f), skyBelow = Rgb(.3f, .35f, .38f),
                waterShallow = Rgb(.13f, .31f, .34f), waterDeep = Rgb(.07f, .2f, .24f), waterFar = Rgb(.31f, .37f, .41f), waterTrough = Rgb(.04f, .13f, .16f), waterSky = Rgb(.46f, .51f, .56f), waveSpeed = 1.7f,
                mountain = Rgb(.22f, .26f, .28f), cloud = Rgb(.3f, .33f, .36f), rain = 1, lightning = 1, fov = 50,
                gradeTint = Rgb(.95f, .98f, 1.04f), bloom = .45f, saturation = .85f, contrast = 1.12f, vignette = .45f, waterFoam = .75f, sunGlow = .05f,
            };
            default: return new Atmosphere                              // bright tropical midday
            {
                sunColor = Rgb(1f, .95f, .86f), sunIntensity = 1.15f, sunEuler = new Vector3(48, -32, 0),
                ambientSky = Rgb(.72f, .82f, .92f), ambientEquator = Rgb(.6f, .74f, .8f), ambientGround = Rgb(.32f, .52f, .58f),
                fog = Rgb(.78f, .87f, .93f), fogStart = 60, fogEnd = 470,
                skyTop = Rgb(.36f, .62f, .84f), skyHorizon = Rgb(.8f, .88f, .93f), skyBelow = Rgb(.55f, .75f, .88f),
                waterShallow = Rgb(.13f, .72f, .83f), waterDeep = Rgb(.06f, .56f, .78f), waterFar = Rgb(.33f, .66f, .84f), waterTrough = Rgb(.03f, .42f, .68f), waterSky = Rgb(.8f, .89f, .95f), waveSpeed = 1f,
                mountain = Rgb(.43f, .56f, .54f), cloud = Rgb(1f, 1f, 1f), fov = 50,
                gradeTint = Rgb(1f, 1f, 1f), bloom = .55f, saturation = 1.04f, contrast = 1.08f, vignette = .22f, waterFoam = .35f, sunGlow = 1f,
            };
        }
    }

    // Atmosphere at a run distance: cross-fades across each biome boundary over +-biomeBlend metres.
    public Atmosphere AtmosphereAt(float distance)
    {
        float boundary = Mathf.Round(distance / biomeLength) * biomeLength;
        var from = AtmosphereOf(BiomeAt(boundary - 1f));
        var to = AtmosphereOf(BiomeAt(boundary + 1f));
        float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(boundary - biomeBlend, boundary + biomeBlend, distance));
        var result = Atmosphere.Lerp(from, to, t);
        // a thicker haze right at the border hides far scenery popping between biomes
        float gate = 1f - Mathf.Abs(t * 2f - 1f);
        result.fogEnd *= 1f - .45f * gate;
        return result;
    }

    // Where the visible sun sits: ahead of the player, so it is in shot and its glitter path lies on the water.
    // The shadow-casting light stays behind the camera, keeping the side of everything facing the player lit.
    static readonly Vector4 GlintSunDir = new(-.13f, .19f, 1f, 0f);
    RunnerPostFX postFx;
    Cubemap reflection;
    float reflectionTimer;

    // Sky-gradient cubemap for the Standard materials, so coins and glossy hulls reflect the current biome.
    void UpdateReflection(Atmosphere a)
    {
        reflectionTimer -= Time.unscaledDeltaTime;
        if (reflectionTimer > 0f) return;
        reflectionTimer = .4f;
        const int size = 16;
        if (!reflection)
        {
            reflection = new Cubemap(size, TextureFormat.RGBAHalf, true) { name = "Biome sky reflection" };
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = reflection;
            RenderSettings.reflectionIntensity = .4f;
        }
        var sun = ((Vector3)GlintSunDir).normalized;
        var px = new Color[size * size];
        for (int face = 0; face < 6; face++)
        {
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size * 2f - 1f, v = (y + .5f) / size * 2f - 1f;
                var d = (face switch
                {
                    0 => new Vector3(1, -v, -u), 1 => new Vector3(-1, -v, u), 2 => new Vector3(u, 1, v),
                    3 => new Vector3(u, -1, -v), 4 => new Vector3(u, -v, 1), _ => new Vector3(-u, -v, -1),
                }).normalized;
                var c = d.y > 0 ? Color.Lerp(a.skyHorizon, a.skyTop, Mathf.Pow(Mathf.Clamp01(d.y * 2.2f), .65f)) : Color.Lerp(a.skyHorizon, a.waterDeep, Mathf.Clamp01(-d.y * 5f));
                c += a.sunColor * a.sunGlow * (Mathf.Pow(Mathf.Max(0f, Vector3.Dot(d, sun)), 40f) * 1.2f);
                c += a.ambientSky * a.reflectionLift;     // a cave has no sky: its ice glows from the ambient instead
                c.a = 1f;
                px[y * size + x] = c;
            }
            reflection.SetPixels(px, (CubemapFace)face);
        }
        reflection.Apply(true);
    }

    void ApplyAtmosphere()
    {
        atmosphere = AtmosphereAt(distanceTravelled);
        var a = atmosphere;
        if (a.lightning > .5f && !crashed)
        {
            lightningTimer -= Time.deltaTime;
            if (lightningTimer <= 0f) { lightningFlash = 1f; lightningTimer = Random.Range(2.2f, 5.5f); if (Random.value < .6f) audioFx?.Thunder(); }
        }
        lightningFlash = Mathf.MoveTowards(lightningFlash, 0f, Time.deltaTime * 4.5f);
        float flash = lightningFlash * a.lightning * (Mathf.Sin(lightningFlash * 40f) > -.3f ? 1f : .3f);

        sun.color = Color.Lerp(a.sunColor, Color.white, flash);
        sun.intensity = a.sunIntensity + flash * 1.6f;
        sun.transform.rotation = Quaternion.Euler(a.sunEuler);
        RenderSettings.ambientSkyColor = a.ambientSky + Color.white * flash * .5f;
        RenderSettings.ambientEquatorColor = a.ambientEquator + Color.white * flash * .4f;
        RenderSettings.ambientGroundColor = a.ambientGround;
        RenderSettings.fogColor = Color.Lerp(a.fog, Rgb(.8f, .85f, .95f), flash * .6f);
        RenderSettings.fogStartDistance = a.fogStart; RenderSettings.fogEndDistance = a.fogEnd;
        if (gameCamera) gameCamera.backgroundColor = RenderSettings.fogColor;
        if (skyMaterial)
        {
            skyMaterial.SetColor("_Top", Color.Lerp(a.skyTop, Rgb(.75f, .8f, .95f), flash * .7f));
            skyMaterial.SetColor("_Horizon", Color.Lerp(a.skyHorizon, Rgb(.9f, .92f, 1f), flash * .7f));
            skyMaterial.SetColor("_Below", a.skyBelow);
        }
        if (waterSurfaceMaterial && waterSurfaceMaterial.HasProperty("_Shallow"))
        {
            waterSurfaceMaterial.SetColor("_Shallow", a.waterShallow); waterSurfaceMaterial.SetColor("_Deep", a.waterDeep);
            waterSurfaceMaterial.SetColor("_Far", a.waterFar); waterSurfaceMaterial.SetColor("_Trough", a.waterTrough);
            waterSurfaceMaterial.SetColor("_Sky", Color.Lerp(a.waterSky, Color.white, flash * .5f)); waterSurfaceMaterial.SetFloat("_Speed", a.waveSpeed);
        }
        var sunGlowColor = (Color)(a.sunColor * a.sunGlow);
        if (skyMaterial) { skyMaterial.SetVector("_SunDir", GlintSunDir); skyMaterial.SetColor("_SunColor", sunGlowColor); }
        if (waterSurfaceMaterial && waterSurfaceMaterial.HasProperty("_Shallow"))
        {
            waterSurfaceMaterial.SetVector("_SunDir", GlintSunDir);
            waterSurfaceMaterial.SetColor("_SunColor", sunGlowColor);
            waterSurfaceMaterial.SetColor("_SkyTop", a.skyTop);
            waterSurfaceMaterial.SetFloat("_Foam", a.waterFoam);
        }
        if (postFx)
        {
            postFx.Bloom = a.bloom; postFx.Saturation = a.saturation; postFx.Contrast = a.contrast; postFx.Vignette = a.vignette; postFx.Tint = a.gradeTint;
            postFx.Radial = Mathf.Lerp(0f, .012f, SpeedProgress) + (boostLeft > 0 ? .03f : 0f);
        }
        UpdateReflection(a);
        scenery?.SetAtmosphere(a.mountain, a.cloud, a.volcano);
        if (rain) { var e = rain.emission; e.rateOverTime = 1400f * a.rain; }
        if (embers) { var e = embers.emission; e.rateOverTime = 70f * a.embers; }

        var biome = CurrentBiome;
        if (biome != shownBiome) { shownBiome = biome; bannerTime = 2.8f; }
        bannerTime = Mathf.Max(0f, bannerTime - Time.deltaTime);
    }

    void Start()
    {
        Application.targetFrameRate = 60;
        CreateMaterials();
        CreateLightingAndSky();
        CreateOcean();
        CreatePlayer();
        CreateCamera();
        CreateShore();
        CreateSpray();
        CreateWeather();
        shownBiome = CurrentBiome; bannerTime = 2.8f;
        ApplyAtmosphere();
        CreateCoinRow(1, 25f, 4);
        CreateCoinRow(0, 54f, 3);
        SetupExtras();
    }

    // Main menu: the jetski idles on the water behind the menu while the scenery keeps its atmosphere.
    void UpdateMenu()
    {
        var p = player.position; p.y = .15f + Mathf.Sin(Time.time * 2.2f) * .05f; player.position = p;
        player.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.7f) * 1.5f, 0, Mathf.Sin(Time.time * 1.3f) * 2.5f);
        foreach (var spray in sprays) { var e = spray.emission; e.enabled = false; }
        if (waterSurfaceMaterial != null) { waterSurfaceMaterial.SetFloat("_PlayerX", player.position.x); waterSurfaceMaterial.SetVector("_PlayerPos", new Vector4(player.position.x, 0, player.position.z, 0)); }
        ApplyAtmosphere();
        FollowCamera();
        audioFx?.SetMotion(0f, 0f, false, false, false);
    }

    void Update()
    {
        if (state == RunState.Menu) { UpdateMenu(); return; }
        if (crashed)
        {
            UpdateChaser(0f);
            audioFx?.SetMotion(0f, 0f, false, false, false);
            // keyboard restart once the game-over panel is up (and the continue offer has run out)
            if (OverTime > (CanRevive ? ReviveWindow : 1.2f) && RestartPressed()) Restart(false);
            return;
        }
        audioFx?.SetMotion(SpeedProgress, 1f, jumpTime >= 0, boostLeft > 0, Paused);
        if (Time.timeScale == 0) return;
        ReadInput();
        float speed = CurrentSpeed;
        distanceTravelled += speed * Time.deltaTime;
        boostLeft = Mathf.Max(0, boostLeft - Time.deltaTime);
        if (jumpTime >= 0)
        {
            jumpTime += Time.deltaTime / activeJumpDuration;
            if (jumpTime >= .72f && !landingCued) { landingCued = true; if (rider) rider.Land(); }
            // vertical speed at touch-down (d/dt of the jump arc) sizes the landing splash
            peakFallSpeed = 4f * activeJumpHeight * Mathf.Abs(1f - 2f * Mathf.Min(jumpTime, 1f)) / activeJumpDuration;
            if (jumpTime >= 1f) { jumpTime = -1f; LandingSplash(peakFallSpeed); OnLanded(); }
        }
        laneBlend = Mathf.MoveTowards(laneBlend, 1f, Time.deltaTime * 5f);
        var desired = new Vector3((lane - 1) * laneWidth, .15f, 0);
        // steering gets snappier as the run speeds up so lane changes keep pace with the traffic
        var position = Vector3.Lerp(player.position, desired, 1f - Mathf.Exp(-9f * Mathf.Lerp(1f, 1.7f, SpeedProgress) * Time.deltaTime));
        position.y = .15f + JumpOffset + (jumpTime < 0 ? Mathf.Sin(Time.time * 7f) * .04f : 0f) - (Ducking ? .16f : 0f);
        player.position = position;
        if (waterSurfaceMaterial != null) { waterSurfaceMaterial.SetFloat("_PlayerX",player.position.x); waterSurfaceMaterial.SetFloat("_Scroll",distanceTravelled); waterSurfaceMaterial.SetVector("_PlayerPos",new Vector4(player.position.x,JumpOffset,player.position.z,0)); }
        float pitch = jumpTime < 0 ? (Ducking ? 5f : 0f) : Mathf.Lerp(-14f, 10f, jumpTime);
        player.rotation = Quaternion.Slerp(player.rotation, Quaternion.Euler(pitch, 0, Mathf.Clamp((desired.x-player.position.x)*-12f,-13f,13f)), Time.deltaTime*7f);
        movingWorld.position += Vector3.back * (speed * Time.deltaTime);
        // wake spray grows with speed and boost, and stops while the hull is out of the water
        float wake = Mathf.Lerp(.75f, 1.6f, SpeedProgress) * (boostLeft > 0 ? 1.45f : 1f);
        for (int i = 0; i < sprays.Count; i++)
        {
            var spray = sprays[i];
            var velocity = spray.velocityOverLifetime; velocity.z = -speed;
            var emission = spray.emission; emission.enabled = jumpTime < 0; emission.rateOverTime = sprayRates[i] * wake;
            var main = spray.main; main.startSpeedMultiplier = wake;
        }
        ApplyAtmosphere();
        FollowCamera();
        UpdateExtras(speed);
        SpawnShoreUpTo(distanceTravelled + ShoreAhead);
        SpawnCaveUpTo(distanceTravelled + ShoreAhead);
        RecycleWater();
        CleanupPassed();
        // look further ahead at speed so rows still appear ~5 s out instead of popping in close
        while (nextRowAt < distanceTravelled + Mathf.Max(spawnAhead, RunSpeed * 5f))
        {
            int pattern = Random.Range(0, 13);
            int selected = Random.Range(0, 3);
            bool hazards = nextRowAt > 60f && obstaclePrefabs != null && obstaclePrefabs.Length > 0;
            float rowLength = 0f;
            if (pattern == 0) CreateCoinRow(selected, nextRowAt, Random.Range(3, 5));
            else if (pattern == 1) CreateZigzagRow(selected, nextRowAt);
            else if (hazards && pattern <= 4) CreateObstacleRow(selected, nextRowAt);
            else if (hazards && pattern <= 6 && pirateShipPrefab && Random.value < ShipChance(BiomeAt(nextRowAt))) { CreateShipRow(selected, nextRowAt); rowLength = shipLength * 2f; }
            else if (hazards && pattern <= 6) CreateObstacleRow(selected, nextRowAt);
            else if (hazards && pattern == 7 && rampPrefab) CreateRampRow(selected, nextRowAt);
            else if (pattern == 8 && fuelPrefab) CreateFuelPickup(selected, nextRowAt);
            else if (hazards && pattern == 9) CreateLogRow(selected, nextRowAt, nextRowAt > 260f && Random.value < .4f);
            else if (hazards && pattern == 10) CreateBannerRow(selected, nextRowAt, nextRowAt > 320f && Random.value < .35f);
            else if (hazards && pattern == 11) CreateBiomeHazardRow(selected, nextRowAt);
            else if (pattern == 12 && nextRowAt > 150f) CreatePowerUp(selected, nextRowAt);
            else CreateCoinRow(selected, nextRowAt, Random.Range(1, 3));
            // rows spread out in metres as speed grows, but less than speed does, so they still arrive faster
            nextRowAt += rowLength + Random.Range(16f, 24f) * Mathf.Lerp(1f, 1.6f, SpeedProgress);
        }
    }

    // Pirate traffic is heaviest around the harbor and in the storm, rare in the volcanic zone.
    static float ShipChance(EnvironmentPreset biome) => biome switch
    {
        EnvironmentPreset.PirateCove => 1f, EnvironmentPreset.Storm => 1f, EnvironmentPreset.IceCave => .6f,
        EnvironmentPreset.Volcanic => .3f, _ => .45f,
    };

    bool RestartPressed() =>
        Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Return) ||
        Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);

    void ReadInput()
    {
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) ChangeLane(-1);
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) ChangeLane(1);
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Space)) PlayerJump();
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) DownAction();
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) TogglePause();
        if (Input.touchCount == 0) return;
        var touch = Input.GetTouch(0);
        // touches that start on a HUD button (pause) are not steering
        if (touch.phase == TouchPhase.Began) { touchStart = touch.position; touchSwiped = ui != null && ui.IsOverUI(touch.fingerId); }
        // a swipe fires as soon as the finger has moved far enough, not on release, so it feels instant
        if (!touchSwiped && (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Ended))
        {
            var delta = touch.position - touchStart;
            if (delta.magnitude > Screen.height * .045f)
            {
                touchSwiped = true;
                if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y)) ChangeLane(delta.x > 0 ? 1 : -1);
                else if (delta.y > 0) PlayerJump();
                else DownAction();
            }
            else if (touch.phase == TouchPhase.Ended) ChangeLane(touch.position.x > Screen.width * .5f ? 1 : -1);
        }
    }

    void StartJump(float duration, float height)
    {
        activeJumpDuration = duration; activeJumpHeight = height;
        jumpTime = 0f; landingCued = false;
        if (rider) rider.Jump();
        duckLeft = 0f;
        audioFx?.Jump();
        TakeoffSplash(height);
    }

    // Hop off the water; a little shorter at top speed so the landing stays readable.
    void PlayerJump()
    {
        if (jumpTime >= 0) return;
        StartJump(playerJumpDuration * Mathf.Lerp(1f, .82f, SpeedProgress), playerJumpHeight);
    }

    // Swipe down in the air: drop straight back to the water, keeping the current height.
    void Dive()
    {
        if (jumpTime < 0 || jumpTime >= .9f) return;
        float t = jumpTime > .5f ? jumpTime : 1f - jumpTime;  // same height, now on the way down
        activeJumpDuration *= .4f;
        jumpTime = t;
    }
    void ChangeLane(int step)
    {
        int next = Mathf.Clamp(lane + step, 0, 2);
        if (next != lane && rider) rider.Lean(step);
        // carving throws a sheet of water off the outside of the turn
        if (next != lane && jumpTime < 0) { CarveSplash(step); audioFx?.Carve(); }
        if (next != lane) prevLane = lane;
        lane = next;
    }

    void CreateMaterials()
    {
        bool ice = environment == EnvironmentPreset.IceCave;
        sea = Make(ice ? "Ice cave | dark teal water" : "Ocean | tropical blue", ice ? new Color(.005f,.2f,.28f) : new Color(.015f,.39f,.59f), .22f,.48f);
        seaLight = Make("Wave facets", ice ? new Color(.05f,.78f,.78f) : new Color(.03f,.73f,.82f), .12f,.52f);
        foam = Make("Sea foam", new Color(.83f,.97f,1f), 0,.3f);
        gold = Make("Gold", new Color(1f,.57f,.025f), .68f,.78f);
        orange = Make("Jetski orange", new Color(1f,.19f,.025f), .14f,.55f);
        cyan = Make("Jetski turquoise", new Color(.015f,.76f,.84f), .2f,.65f);
        charcoal = Make("Graphite", new Color(.018f,.04f,.065f), .18f,.4f);
        skin = Make("Rider skin", new Color(.53f,.24f,.11f), 0,.35f);
        wood = Make("Pirate ship timber", new Color(.24f,.075f,.025f), 0,.32f);
        rock = Make("Island rock", new Color(.29f,.37f,.39f), 0,.25f);
        palmLeaf = Make("Palm leaves", new Color(.16f,.48f,.16f), 0,.35f);
    }
    Material Make(string n, Color c, float metallic, float smooth)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
        if (shader == null)
        {
            if (fallbackShader == null)
            {
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fallbackShader = probe.GetComponent<Renderer>().sharedMaterial.shader;
                Destroy(probe);
            }
            shader = fallbackShader;
        }
        if (shader == null) throw new System.InvalidOperationException("No compatible Unity shader is available for runtime materials.");
        var m = new Material(shader) { name=n, color=c };
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        return m;
    }

    void CreateLightingAndSky()
    {
        // Colours, fog and sun direction are driven every frame by ApplyAtmosphere for the current biome.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        sun = new GameObject("Biome sunlight").AddComponent<Light>();
        sun.type=LightType.Directional; sun.shadows=LightShadows.Soft; sun.shadowStrength=.7f; sun.shadowBias=.03f; sun.shadowNormalBias=.3f;
        QualitySettings.shadowDistance=70;
    }

    void CreateOcean()
    {
        movingWorld = new GameObject("Endless scenery and pickups").transform;
        // One ocean for every biome; ApplyAtmosphere retints it (turquoise, glacial, lava-dark, storm grey).
        var waterShader = tropicalWaterShader ? tropicalWaterShader : Shader.Find("JetskiRunner/TropicalWater");
        waterSurfaceMaterial = waterShader ? new Material(waterShader) : sea;
        TropicalWaterSurface.Create(waterSurfaceMaterial, -.42f);
    }

    void CreatePlayer()
    {
        player=new GameObject("Player Jetski").transform; player.position=new Vector3(0,.13f,0);
        player.gameObject.AddComponent<Rigidbody>().isKinematic=true;
        player.gameObject.AddComponent<CapsuleCollider>().radius=.78f;
        if (riderPrefab)
        {
            var model=Instantiate(riderPrefab,player);
            // Pirate Escape prefabs are already turned to face +Z and authored with the waterline at y=0.
            model.transform.localPosition=new Vector3(0,WaterY-.15f,0);
            model.transform.localRotation=Quaternion.identity;
            model.transform.localScale=Vector3.one;
            rider=model.GetComponentInChildren<PirateRider>();
            return;
        }
        CreateHullMesh("Shaped low-poly jetski hull",player,charcoal);
        CreateDeckMesh("Turquoise upper deck",player,cyan);
        CreateDeckMesh("Orange nose stripe",player,orange,.58f,.93f,.67f);
        Piece("Padded seat",PrimitiveType.Capsule,player,new Vector3(0,.28f,-.48f),new Vector3(.34f,.12f,.58f),charcoal);
        Piece("Rider jacket",PrimitiveType.Capsule,player,new Vector3(0,.68f,-.13f),new Vector3(.32f,.42f,.28f),orange).transform.localRotation=Quaternion.Euler(34,0,0);
        Piece("Life vest center",PrimitiveType.Cube,player,new Vector3(0,.68f,-.105f),new Vector3(.30f,.31f,.30f),charcoal);
        Piece("Vest back panel",PrimitiveType.Cube,player,new Vector3(0,.72f,-.31f),new Vector3(.38f,.25f,.07f),orange);
        Piece("Rider helmet",PrimitiveType.Sphere,player,new Vector3(0,1.22f,.12f),new Vector3(.30f,.34f,.29f),skin);
        Piece("Helmet crown",PrimitiveType.Sphere,player,new Vector3(0,1.36f,.10f),new Vector3(.31f,.22f,.30f),charcoal);
        Piece("Helmet orange stripe",PrimitiveType.Cube,player,new Vector3(0,1.37f,.16f),new Vector3(.08f,.12f,.28f),orange);
        CreateLimb("Left forearm",player,new Vector3(-.27f,.84f,-.04f),new Vector3(-.34f,.55f,.43f),skin,.105f);
        CreateLimb("Right forearm",player,new Vector3(.27f,.84f,-.04f),new Vector3(.34f,.55f,.43f),skin,.105f);
        CreateLimb("Left leg",player,new Vector3(-.19f,.43f,-.28f),new Vector3(-.31f,.22f,-.70f),charcoal,.145f);
        CreateLimb("Right leg",player,new Vector3(.19f,.43f,-.28f),new Vector3(.31f,.22f,-.70f),charcoal,.145f);
        Piece("Handlebar",PrimitiveType.Cylinder,player,new Vector3(0,.54f,.45f),new Vector3(.045f,.045f,.42f),charcoal).transform.localRotation=Quaternion.Euler(0,0,90);
        Piece("Left handle grip",PrimitiveType.Sphere,player,new Vector3(-.34f,.55f,.43f),new Vector3(.10f,.08f,.09f),orange);
        Piece("Right handle grip",PrimitiveType.Sphere,player,new Vector3(.34f,.55f,.43f),new Vector3(.10f,.08f,.09f),orange);
        Piece("Rear jet nozzle",PrimitiveType.Cylinder,player,new Vector3(0,-.12f,-1.04f),new Vector3(.22f,.22f,.10f),cyan).transform.localRotation=Quaternion.Euler(90,0,0);
        for(int i=0;i<16;i++)
        {
            float x=Random.Range(-.78f,.78f), z=-1.2f-i*.3f;
            var spray=CreatePrimitive("Jetski spray",PrimitiveType.Sphere,movingWorld,new Vector3(x,-.15f,z),new Vector3(Random.Range(.11f,.28f),.09f,Random.Range(.22f,.48f)),foam);
            spray.transform.rotation=Quaternion.Euler(0,Random.Range(-18,18),0);
        }
    }

    void CreateHullMesh(string objectName,Transform parent,Material material)
    {
        float[] z={-1.18f,-.88f,-.42f,.25f,.78f,1.12f};
        float[] w={.42f,.61f,.64f,.55f,.42f,.18f};
        var vertices=new List<Vector3>(); var triangles=new List<int>();
        for(int i=0;i<z.Length;i++)
        {
            float y=i<2?-.08f:-.12f, bottom=-.27f;
            vertices.Add(new Vector3(-w[i],y,z[i])); vertices.Add(new Vector3(-w[i]*.83f,bottom,z[i]));
            vertices.Add(new Vector3(0,bottom-.025f,z[i])); vertices.Add(new Vector3(w[i]*.83f,bottom,z[i]));
            vertices.Add(new Vector3(w[i],y,z[i]));
        }
        for(int i=0;i<z.Length-1;i++) for(int side=0;side<4;side++)
        {
            int a=i*5+side,b=(i+1)*5+side,c=(i+1)*5+side+1,d=i*5+side+1;
            triangles.Add(a);triangles.Add(b);triangles.Add(c); triangles.Add(a);triangles.Add(c);triangles.Add(d);
        }
        var mesh=new Mesh{name="Jetski angular hull mesh"}; mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        var go=new GameObject(objectName);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
    }

    void CreateDeckMesh(string objectName,Transform parent,Material material,float from=.72f,float to=-.92f,float widthScale=.82f)
    {
        var verts=new[]{new Vector3(-.43f,.055f,from),new Vector3(.43f,.055f,from),new Vector3(.55f,.055f,.10f),new Vector3(.46f,.055f,to),new Vector3(-.46f,.055f,to),new Vector3(-.55f,.055f,.10f)};
        for(int i=0;i<verts.Length;i++) verts[i]=new Vector3(verts[i].x*widthScale,verts[i].y,verts[i].z);
        var mesh=new Mesh{name=objectName+" mesh"};mesh.vertices=verts;mesh.triangles=new[]{0,2,1,0,3,2,0,4,3,0,5,4};mesh.RecalculateNormals();
        var go=new GameObject(objectName);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
    }

    void CreateLimb(string name,Transform parent,Vector3 start,Vector3 end,Material material,float radius)
    {
        Vector3 delta=end-start;var limb=Piece(name,PrimitiveType.Capsule,parent,(start+end)*.5f,new Vector3(radius,delta.magnitude*.5f+radius,radius),material);
        limb.transform.localRotation=Quaternion.FromToRotation(Vector3.up,delta.normalized);
    }

    // Low chase camera like the Blender gameplay preview: rider in the bottom third, horizon about a third down.
    static readonly Vector3 CameraOffset=new Vector3(0,3.7f,-7.9f);
    void CreateCamera()
    {
        gameCamera=new GameObject("Portrait chase camera").AddComponent<Camera>();
        gameCamera.tag="MainCamera"; gameCamera.fieldOfView=AtmosphereOf(CurrentBiome).fov; gameCamera.nearClipPlane=.1f; gameCamera.farClipPlane=620;
        gameCamera.clearFlags=CameraClearFlags.SolidColor;
        gameCamera.transform.position=CameraOffset; gameCamera.transform.rotation=Quaternion.Euler(10,0,0);
        gameCamera.aspect=9f/16f;
        gameCamera.gameObject.AddComponent<AudioListener>();
        // depth powers the water's contact foam and the soft particles
        gameCamera.depthTextureMode|=DepthTextureMode.Depth;
        // post-processing costs too much on the low quality tiers
        if(QualitySettings.GetQualityLevel()>=3) postFx=RunnerPostFX.Attach(gameCamera,postFxShader?postFxShader:Shader.Find("JetskiRunner/PostFX"));
        var skyShaderToUse=skyShader?skyShader:Shader.Find("JetskiRunner/SkyGradient");
        if(skyShaderToUse)
        {
            var dome=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name="Sky dome"; Destroy(dome.GetComponent<Collider>());
            dome.transform.SetParent(gameCamera.transform,false);
            dome.transform.localScale=Vector3.one*1100f;
            skyMaterial=new Material(skyShaderToUse);
            var r=dome.GetComponent<Renderer>(); r.sharedMaterial=skyMaterial; r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows=false;
        }
    }

    void FollowCamera()
    {
        var target=CameraOffset+new Vector3(player.position.x*.78f,JumpOffset*.35f,0);
        gameCamera.transform.position=Vector3.Lerp(gameCamera.transform.position,target,1f-Mathf.Exp(-7f*Time.deltaTime));
        // short dip + shake on splash-down
        if(cameraKick>0)
        {
            cameraKick=Mathf.MoveTowards(cameraKick,0f,Time.deltaTime*2.2f);
            gameCamera.transform.position+=new Vector3(Mathf.Sin(Time.time*55f)*.06f,-.22f-Mathf.Sin(Time.time*47f)*.05f,0)*cameraKick;
        }
        gameCamera.fieldOfView=Mathf.Lerp(gameCamera.fieldOfView,atmosphere.fov+(boostLeft>0?7:0),Time.deltaTime*3f);
    }

    // White spray fans off both sides of the hull plus a rooster tail, as in the gameplay render.
    void CreateSpray()
    {
        var mat=splashMaterial?splashMaterial:sprayMaterial;
        if(!mat) return;
        if(splashMaterial)
        {
            // Hull spray: low wide sheets peeling off both chines, a tall rooster tail and fine bow mist.
            sprays.Add(MakeSpray("Left spray sheet",new Vector3(-.6f,-.28f,-.1f),new Vector3(-1f,.5f,-.7f),55,5.2f,.75f,mat,.45f,.7f,22,.95f));
            sprays.Add(MakeSpray("Right spray sheet",new Vector3(.6f,-.28f,-.1f),new Vector3(1f,.5f,-.7f),55,5.2f,.75f,mat,.45f,.7f,22,.95f));
            sprays.Add(MakeSpray("Rooster tail",new Vector3(0,-.25f,-1.35f),new Vector3(0,.85f,-1f),45,6.2f,1.05f,mat,.6f,1f,9,.9f));
            sprays.Add(MakeSpray("Bow mist",new Vector3(0,-.3f,.9f),new Vector3(0,.35f,-1f),30,2.2f,.45f,mat,.3f,.55f,55,.45f));
            sprays.Add(MakeSpray("Wake droplets",new Vector3(0,-.25f,-1.2f),new Vector3(0,.6f,-1f),70,4f,.22f,mat,.5f,.9f,35,1f));
        }
        else
        {
            sprays.Add(MakeSpray("Left spray fan",new Vector3(-.55f,-.25f,-.2f),new Vector3(-1f,.42f,-.55f),200,6.5f,.32f,mat));
            sprays.Add(MakeSpray("Right spray fan",new Vector3(.55f,-.25f,-.2f),new Vector3(1f,.42f,-.55f),200,6.5f,.32f,mat));
            sprays.Add(MakeSpray("Rooster tail",new Vector3(0,-.2f,-1.3f),new Vector3(0,.55f,-1f),140,4.5f,.38f,mat));
        }
        foreach(var s in sprays) sprayRates.Add(s.emission.rateOverTime.constant);
        CreateSplashSystems();
    }
    ParticleSystem MakeSpray(string name,Vector3 local,Vector3 direction,float rate,float speed,float size,Material material,
        float minLife=.45f,float maxLife=.8f,float coneAngle=14,float alpha=.85f)
    {
        var go=new GameObject(name); go.transform.SetParent(player,false);
        go.transform.localPosition=local; go.transform.localRotation=Quaternion.LookRotation(direction.normalized);
        var ps=go.AddComponent<ParticleSystem>();
        ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;
        main.startLifetime=new ParticleSystem.MinMaxCurve(minLife,maxLife);
        main.startSpeed=new ParticleSystem.MinMaxCurve(speed*.6f,speed);
        main.startSize=new ParticleSystem.MinMaxCurve(size*.35f,size);
        main.startRotation=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2f);
        main.startColor=new Color(1,1,1,alpha);
        main.gravityModifier=1.4f;
        main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.maxParticles=900;
        var emission=ps.emission; emission.rateOverTime=rate;
        var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Cone; shape.angle=coneAngle; shape.radius=.12f;
        var velocity=ps.velocityOverLifetime; velocity.enabled=true; velocity.space=ParticleSystemSimulationSpace.World;
        velocity.x=new ParticleSystem.MinMaxCurve(0); velocity.y=new ParticleSystem.MinMaxCurve(0); velocity.z=new ParticleSystem.MinMaxCurve(-forwardSpeed);
        var sizeLife=ps.sizeOverLifetime; sizeLife.enabled=true; sizeLife.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.6f,1,1.6f));
        var fade=ps.colorOverLifetime; fade.enabled=true;
        var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(.9f,0),new GradientAlphaKey(.6f,.5f),new GradientAlphaKey(0,1)});
        fade.color=gradient;
        var renderer=go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=material;
        ps.Play();
        return ps;
    }

    // ---------- one-shot splashes (takeoff, landing, carving, impacts) ----------

    void CreateSplashSystems()
    {
        if(!splashMaterial) return;
        splashDrops=MakeBurstSystem("Splash droplets",splashMaterial,1.5f,false);
        if(foamRingMaterial) splashRings=MakeBurstSystem("Splash foam rings",foamRingMaterial,0f,true);
    }

    static ParticleSystem MakeBurstSystem(string name,Material material,float gravity,bool flatOnWater)
    {
        var ps=new GameObject(name).AddComponent<ParticleSystem>();
        ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;
        main.simulationSpace=ParticleSystemSimulationSpace.World; main.gravityModifier=gravity;
        main.maxParticles=flatOnWater?60:1500; main.playOnAwake=false; main.loop=false;
        var emission=ps.emission; emission.enabled=false;
        var shape=ps.shape; shape.enabled=false;
        var sizeLife=ps.sizeOverLifetime; sizeLife.enabled=true;
        sizeLife.size=new ParticleSystem.MinMaxCurve(1,flatOnWater?AnimationCurve.EaseInOut(0,.35f,1,1.6f):new AnimationCurve(new Keyframe(0,.5f),new Keyframe(.25f,1f),new Keyframe(1,.7f)));
        var fade=ps.colorOverLifetime; fade.enabled=true;
        var g=new Gradient();
        g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                  flatOnWater?new[]{new GradientAlphaKey(.9f,0),new GradientAlphaKey(.6f,.45f),new GradientAlphaKey(0,1)}
                             :new[]{new GradientAlphaKey(1f,0),new GradientAlphaKey(.85f,.6f),new GradientAlphaKey(0,1)});
        fade.color=g;
        var r=ps.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial=material;
        if(flatOnWater){ r.renderMode=ParticleSystemRenderMode.HorizontalBillboard; r.sortingFudge=10; }
        return ps;
    }

    // drift: how fast the water itself moves past the camera, so splashes stay where they hit the water
    void EmitDrops(Vector3 origin,int count,Vector3 baseVelocity,float spread,float upMin,float upMax,float sizeMin,float sizeMax,float drift)
    {
        if(!splashDrops) return;
        var p=new ParticleSystem.EmitParams();
        for(int i=0;i<count;i++)
        {
            var dir=Random.insideUnitCircle*spread;
            p.position=origin+new Vector3(Random.Range(-.25f,.25f),0,Random.Range(-.25f,.25f));
            p.velocity=baseVelocity+new Vector3(dir.x,Random.Range(upMin,upMax),dir.y-drift);
            p.startSize=Random.Range(sizeMin,sizeMax);
            p.startLifetime=Random.Range(.55f,1.05f);
            p.rotation=Random.Range(0f,360f);
            p.startColor=new Color(1,1,1,Random.Range(.75f,1f));
            splashDrops.Emit(p,1);
        }
    }

    void EmitRing(Vector3 origin,float size,float drift)
    {
        if(!splashRings) return;
        var p=new ParticleSystem.EmitParams
        {
            position=new Vector3(origin.x,WaterY+.04f,origin.z), velocity=new Vector3(0,0,-drift),
            startSize=size, startLifetime=.9f, rotation=Random.Range(0f,360f), startColor=Color.white,
        };
        splashRings.Emit(p,1);
    }

    Vector3 WaterAt(Vector3 p,float forward=0f)=>new(p.x,WaterY+.05f,p.z+forward);

    void TakeoffSplash(float height)
    {
        float k=Mathf.Clamp(height/1.7f,.8f,1.4f), drift=CurrentSpeed;
        EmitDrops(WaterAt(player.position,-.8f),Mathf.RoundToInt(28*k),new Vector3(0,0,-1.5f),3.2f,2.5f,5.5f*k,.35f,.85f,drift);
        EmitRing(WaterAt(player.position,-.4f),2.2f*k,drift);
    }

    // Splash-down: a crown thrown outwards, a tall central plume, two spreading rings and a camera dip.
    void LandingSplash(float fallSpeed)
    {
        float k=Mathf.Clamp(fallSpeed/9f,.6f,1.8f), drift=CurrentSpeed;
        var at=WaterAt(player.position,.2f);
        if(splashDrops)
        {
            int crown=Mathf.RoundToInt(46*k);
            var p=new ParticleSystem.EmitParams();
            for(int i=0;i<crown;i++)
            {
                float a=i/(float)crown*Mathf.PI*2f+Random.Range(-.1f,.1f), out_=Random.Range(2.8f,4.6f)*k;
                p.position=at+new Vector3(Mathf.Cos(a)*.7f,0,Mathf.Sin(a)*1.1f);
                p.velocity=new Vector3(Mathf.Cos(a)*out_,Random.Range(3.2f,5.8f)*Mathf.Sqrt(k),Mathf.Sin(a)*out_*.6f-drift);
                p.startSize=Random.Range(.45f,1.05f)*Mathf.Sqrt(k); p.startLifetime=Random.Range(.6f,1.05f);
                p.rotation=Random.Range(0f,360f); p.startColor=new Color(1,1,1,Random.Range(.8f,1f));
                splashDrops.Emit(p,1);
            }
        }
        // central plume just behind the hull so it frames the rider instead of covering them
        EmitDrops(at+Vector3.back*.9f,Mathf.RoundToInt(10*k),Vector3.zero,.8f,4.5f*k,7f*k,.4f,.8f,drift);
        EmitRing(at,3.4f*k,drift);
        EmitRing(at+Vector3.back*.6f,2f*k,drift);
        cameraKick=Mathf.Max(cameraKick,.35f*k);
        audioFx?.Splash(k);
    }

    void CarveSplash(int step)
    {
        float drift=CurrentSpeed, k=Mathf.Lerp(.85f,1.4f,SpeedProgress);
        var side=WaterAt(player.position,-.3f)+Vector3.right*(-step*.55f);
        EmitDrops(side,Mathf.RoundToInt(18*k),new Vector3(-step*3.8f*k,0,-1f),1.4f,2f,4.2f,.4f,.9f,drift);
    }

    void ImpactSplash(Vector3 at,float scale,float? driftOverride=null)
    {
        float drift=driftOverride??CurrentSpeed;
        EmitDrops(WaterAt(at),Mathf.RoundToInt(40*scale),Vector3.zero,4f*scale,3f,7.5f*scale,.5f,1.2f,drift);
        EmitRing(WaterAt(at),3f*scale,drift);
        audioFx?.Splash(scale);
    }

    // Storm rain and volcanic embers follow the camera; ApplyAtmosphere sets their emission per biome.
    void CreateWeather()
    {
        if(!sprayMaterial) return;
        rain=MakeWeather("Storm rain",new Vector3(0,9,12),new Vector3(22,1,30),new Color(.78f,.84f,.94f,.8f),.07f,.12f,new Vector3(-1.5f,-24,-8),true);
        embers=MakeWeather("Volcanic embers",new Vector3(0,-.5f,18),new Vector3(30,1,40),new Color(1f,.48f,.12f,.95f),.08f,.16f,new Vector3(0,2.4f,-3),false);
    }
    ParticleSystem MakeWeather(string name,Vector3 local,Vector3 box,Color color,float minSize,float maxSize,Vector3 velocity,bool streaks)
    {
        var go=new GameObject(name); go.transform.SetParent(gameCamera.transform,false); go.transform.localPosition=local;
        go.transform.rotation=Quaternion.identity;
        var ps=go.AddComponent<ParticleSystem>();
        ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;
        main.startLifetime=streaks?new ParticleSystem.MinMaxCurve(.5f,.8f):new ParticleSystem.MinMaxCurve(2.5f,4f);
        main.startSpeed=0; main.startSize=new ParticleSystem.MinMaxCurve(minSize,maxSize); main.startColor=color;
        main.simulationSpace=ParticleSystemSimulationSpace.World; main.maxParticles=streaks?2500:400;
        var emission=ps.emission; emission.rateOverTime=0;
        var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Box; shape.scale=box;
        var vel=ps.velocityOverLifetime; vel.enabled=true; vel.space=ParticleSystemSimulationSpace.World;
        vel.x=new ParticleSystem.MinMaxCurve(velocity.x,velocity.x); vel.y=new ParticleSystem.MinMaxCurve(velocity.y*.8f,velocity.y); vel.z=new ParticleSystem.MinMaxCurve(velocity.z,velocity.z); // all TwoConstants: mixed modes are rejected
        if(!streaks){ var noise=ps.noise; noise.enabled=true; noise.strength=.8f; noise.frequency=.4f; }
        var renderer=go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=sprayMaterial;
        if(streaks){ renderer.renderMode=ParticleSystemRenderMode.Stretch; renderer.velocityScale=.07f; renderer.lengthScale=2f; }
        ps.Play();
        return ps;
    }

    void CreateShore()
    {
        scenery=new TropicalScenery(Make,sideRockPrefab,dockBarrelPrefab,pirateShipPrefab,BiomeCount);
        scenery.BuildBackdrop();
        SpawnShoreUpTo(ShoreAhead);
        SpawnCaveUpTo(ShoreAhead);
    }

    const float ShoreAhead=190f;
    // Shore chunks take the biome of the spot they spawn at, so the next biome is visible ahead before the player arrives.
    void SpawnShoreUpTo(float limit)
    {
        if(scenery==null) return;
        for(int s=0;s<2;s++)
            while(nextShoreAt[s]<limit)
            {
                var biome=BiomeAt(nextShoreAt[s]);
                var chunk=scenery.SpawnShore(movingWorld,s==0?-1:1,nextShoreAt[s],(int)biome,BiomeNames[(int)biome]);
                if(chunk) spawned.Add(chunk);
                nextShoreAt[s]+=Random.Range(17f,27f);
            }
    }

    // Cave passage: ice tunnel segments laid end to end over the whole cave biome, with a glacier at each mouth.
    void SpawnCaveUpTo(float limit)
    {
        if(!iceTunnelPrefab || scenery==null) return;
        float step=iceTunnelLength;
        while(nextCaveAt<limit)
        {
            float zoneStart=Mathf.Floor(nextCaveAt/biomeLength+1e-4f)*biomeLength, zoneEnd=zoneStart+biomeLength;
            if(BiomeAt(zoneStart+1f)!=EnvironmentPreset.IceCave){ nextCaveAt=zoneEnd; continue; }
            if(nextCaveAt+step>zoneEnd+.01f){ nextCaveAt=zoneEnd; continue; }
            bool first=nextCaveAt<=zoneStart+.01f, last=nextCaveAt+2f*step>zoneEnd+.01f;
            if(first) spawned.Add(scenery.SpawnGlacierMouth(movingWorld,zoneStart,true));
            SpawnTunnelSegment(nextCaveAt);
            if(last) spawned.Add(scenery.SpawnGlacierMouth(movingWorld,nextCaveAt+step,false));
            nextCaveAt+=step;
        }
    }

    void SpawnTunnelSegment(float start)
    {
        var segment=Instantiate(iceTunnelPrefab,movingWorld);
        segment.name="Cave passage | ice tunnel segment";
        if(!tunnelMeasured)
        {
            // the model's geometry sits behind its pivot; measure once so segments start exactly at 'start'
            segment.transform.localPosition=Vector3.zero;
            var r=segment.GetComponentsInChildren<Renderer>();
            float min=float.MaxValue; foreach(var x in r) min=Mathf.Min(min,movingWorld.InverseTransformPoint(x.bounds.min).z);
            tunnelMinZ=r.Length>0?min:0f; tunnelMeasured=true;
        }
        segment.transform.localPosition=new Vector3(0,WaterY,start-tunnelMinZ);
        // the shared ocean (retinted glacial teal) replaces the segment's own water so the wake foam keeps working
        foreach(var t in segment.GetComponentsInChildren<Transform>(true))
            if(t.name.StartsWith("Ice_Tunnel_Water")) foreach(var rr in t.GetComponents<Renderer>()) rr.enabled=false;
        spawned.Add(segment);
    }

    // Pirate ships work like lane-blocking traffic: some lie at anchor, others sail head-on down a lane.
    void CreateShipRow(int freeLane,float z)
    {
        int movingLane=distanceTravelled>120f && Random.value<.55f ? (freeLane+Random.Range(1,3))%3 : -1;
        // A sailing ship sweeps its lane all the way to the player. It used to delete whatever was
        // in the way, which made obstacles and coins vanish in plain sight; now it only sails into a clear lane.
        if(movingLane>=0 && !LaneClear(movingLane,z+25f+shipLength)) movingLane=(movingLane==(freeLane+1)%3?(freeLane+2)%3:(freeLane+1)%3);
        if(movingLane>=0 && !LaneClear(movingLane,z+25f+shipLength)) movingLane=-1;
        for(int laneIndex=0;laneIndex<3;laneIndex++)
        {
            if(laneIndex==freeLane) continue;
            if(laneIndex==movingLane){ SpawnShip(laneIndex,z+Random.Range(10f,25f),true); continue; }
            if(Random.value<.35f) continue;
            int convoy=Random.value<.4f?2:1;
            for(int i=0;i<convoy;i++) SpawnShip(laneIndex,z+i*shipLength,false);
        }
        CreateCoinRow(freeLane,z-2f,4);
    }

    // True when nothing (pickup, obstacle, ramp or ship) sits in the lane between the player and 'untilZ'.
    bool LaneClear(int laneIndex,float untilZ)
    {
        float x=(laneIndex-1)*laneWidth;
        foreach(var go in spawned)
            if(go && go.GetComponent<RunnerTrigger>() && Mathf.Abs(go.transform.localPosition.x-x)<laneWidth*.5f
               && go.transform.position.z>-2f && go.transform.localPosition.z<untilZ)
                return false;
        return true;
    }

    void SpawnShip(int laneIndex,float z,bool sailing)
    {
        var ship=SpawnAsset(pirateShipPrefab,laneIndex,z,TriggerKind.Ship);
        ship.name=sailing?"Pirate ship | sailing head-on":"Pirate ship | at anchor";
        // The prefab's bow points +Z; every ship turns its bow to the player (anchored ones used to be
        // flipped at random, which read as ships sitting backwards).
        var trigger=ship.GetComponent<RunnerTrigger>();
        trigger.Yaw=180f+(sailing?0f:Random.Range(-8f,8f));
        ship.transform.localRotation=Quaternion.Euler(0,trigger.Yaw,0);
        trigger.SailSpeed=sailing?shipSailSpeed:0f;
    }

    void CreateCoinRow(int laneIndex,float z,int count)
    {
        for(int i=0;i<count;i++)
        {
            GameObject coin;
            if (goldPrefab)
            {
                coin=Instantiate(goldPrefab,movingWorld);
                coin.name="Gold pickup | Blender anchor coin";
                coin.transform.localPosition=new Vector3((laneIndex-1)*laneWidth,.60f,z+i*3.6f);
                coin.transform.localRotation=Quaternion.Euler(0,0,0);
            }
            else
            {
                coin=CreatePrimitive("Gold pickup",PrimitiveType.Cylinder,movingWorld,new Vector3((laneIndex-1)*laneWidth,.48f,z+i*2.25f),new Vector3(.68f,.14f,.68f),gold);
                coin.transform.rotation=Quaternion.Euler(90,0,0);
            }
            coin.AddComponent<SphereCollider>().isTrigger=true;
            coin.AddComponent<RunnerTrigger>().Init(this,TriggerKind.Coin);
            spawned.Add(coin);
        }
    }
    void CreateZigzagRow(int start,float z)
    { for(int i=0;i<5;i++) CreateCoinRow((start+i)%3,z+i*1.8f,1); }

    GameObject SpawnAsset(GameObject prefab,int laneIndex,float z,TriggerKind kind)
    {
        var go=Instantiate(prefab,movingWorld);
        go.transform.localPosition=new Vector3((laneIndex-1)*laneWidth,WaterY,z);
        go.transform.localRotation=Quaternion.identity;
        AddBoundsTrigger(go,kind>=TriggerKind.Obstacle ? .75f : 1f);
        go.AddComponent<RunnerTrigger>().Init(this,kind);
        spawned.Add(go);
        return go;
    }

    // Box trigger fitted to the visible mesh, shrunk a little for obstacles so near-misses feel fair.
    static void AddBoundsTrigger(GameObject go,float shrink)
    {
        var renderers=go.GetComponentsInChildren<Renderer>();
        if(renderers.Length==0){ go.AddComponent<SphereCollider>().isTrigger=true; return; }
        var bounds=renderers[0].bounds;
        foreach(var r in renderers) bounds.Encapsulate(r.bounds);
        var box=go.AddComponent<BoxCollider>(); box.isTrigger=true;
        box.center=go.transform.InverseTransformPoint(bounds.center);
        var scale=go.transform.lossyScale;
        box.size=new Vector3(bounds.size.x/scale.x*shrink,bounds.size.y/scale.y,bounds.size.z/scale.z*shrink);
    }

    void CreateObstacleRow(int freeLane,float z)
    {
        for(int laneIndex=0;laneIndex<3;laneIndex++)
        {
            if(laneIndex==freeLane || Random.value<.35f) continue;
            var prefab=obstaclePrefabs[Random.Range(0,obstaclePrefabs.Length)];
            SpawnAsset(prefab,laneIndex,z+Random.Range(-1.5f,1.5f),TriggerKind.Obstacle).transform.localRotation=Quaternion.Euler(0,Random.Range(-20f,20f),0);
        }
        CreateCoinRow(freeLane,z-3f,3);
    }

    void CreateRampRow(int laneIndex,float z)
    {
        SpawnAsset(rampPrefab,laneIndex,z,TriggerKind.Ramp);
        // Coins follow the jump arc so a ramp is always worth taking.
        float flight=CurrentSpeed*jumpDuration;
        for(int i=1;i<=4;i++)
        {
            float t=i/5f;
            CreateCoinRow(laneIndex,z+1.5f+t*flight,1);
            var coin=spawned[spawned.Count-1].transform;
            coin.localPosition=new Vector3(coin.localPosition.x,.6f+4f*jumpHeight*t*(1f-t),z+1.5f+t*flight);
        }
        int other=(laneIndex+Random.Range(1,3))%3;
        SpawnAsset(obstaclePrefabs[Random.Range(0,obstaclePrefabs.Length)],other,z+flight*.5f,TriggerKind.Obstacle);
    }

    void CreateFuelPickup(int laneIndex,float z)
    {
        var can=SpawnAsset(fuelPrefab,laneIndex,z,TriggerKind.Fuel);
        can.transform.localPosition+=Vector3.up*.75f;
    }

    void CleanupPassed()
    {
        for(int i=spawned.Count-1;i>=0;i--)
        {
            var go=spawned[i];
            if(go==null){ spawned.RemoveAt(i); continue; }
            if(go.transform.position.z<-30f){ Destroy(go); spawned.RemoveAt(i); }
        }
    }

    GameObject CreatePrimitive(string name,PrimitiveType type,Transform parent,Vector3 position,Vector3 scale,Material material)
    {
        var go=GameObject.CreatePrimitive(type); go.name=name; go.transform.SetParent(parent,false);
        go.transform.localPosition=position; go.transform.localScale=scale;
        go.GetComponent<Renderer>().sharedMaterial=material;
        var collider=go.GetComponent<Collider>(); if(collider!=null && name!="Gold pickup") Destroy(collider);
        return go;
    }
    GameObject Piece(string name,PrimitiveType type,Transform parent,Vector3 pos,Vector3 scale,Material mat)
    { return CreatePrimitive(name,type,parent,pos,scale,mat); }

    void RecycleWater()
    {
        foreach(var t in recycledWater) if(t!=null && t.position.z < -16f) t.position += Vector3.forward*160f;
    }
    public void Collect(GameObject pickup) { AddCoin(); Destroy(pickup); }

    void Crash()
    {
        ImpactSplash(player.position+Vector3.forward*1.2f,1.6f,0f); cameraKick=.5f;
        crashed=true; crashTime=Time.time; duckLeft=0;
        if(rider) rider.Fall();
        audioFx?.Crash();
        FinishRun();
    }

    void OnTrigger(RunnerTrigger trigger)
    {
        if(crashed) return;
        switch(trigger.Kind)
        {
            case TriggerKind.Coin: Collect(trigger.gameObject); break;
            case TriggerKind.Fuel: boostLeft=BoostTime; audioFx?.PowerUp(); Destroy(trigger.gameObject); break;
            case TriggerKind.Magnet: case TriggerKind.Shield: case TriggerKind.DoubleCoins: Pickup(trigger); break;
            case TriggerKind.Ramp: if(jumpTime<0){ duckLeft=0; StartJump(jumpDuration,jumpHeight); } break;
            case TriggerKind.Obstacle:
                if(JumpOffset>.7f) return;           // cleared it in the air
                if(boostLeft>0){ ImpactSplash(trigger.transform.position,1f); trigger.Knock(player.position.x); if(rider) rider.Hit(); return; } // boost smashes through
                if(AbsorbHit(trigger) || TryStumble(trigger)) return;
                Crash();
                break;
            case TriggerKind.Overhead:                 // hanging sail / icicles: only ducking gets under
                if(Ducking) return;
                if(boostLeft>0){ ImpactSplash(trigger.transform.position,1f); trigger.Knock(player.position.x); return; }
                if(AbsorbHit(trigger) || TryStumble(trigger)) return;
                Crash();
                break;
            case TriggerKind.Strike:                   // lightning: nothing saves you but the shield
                if(AbsorbHit(trigger)) return;
                Crash();
                break;
            case TriggerKind.Ship:                     // too big to jump or smash: change lanes
                if(AbsorbHit(trigger) || TryStumble(trigger)) return;
                Crash();
                break;
        }
    }

    // order matters: everything from Ramp on is a hazard (revive clears them), from Obstacle on shrinks its trigger
    enum TriggerKind { Coin, Fuel, Magnet, Shield, DoubleCoins, Ramp, Obstacle, Overhead, Strike, Ship }

    sealed partial class RunnerTrigger:MonoBehaviour
    {
        JetskiRunnerGame game;
        public TriggerKind Kind { get; private set; }
        public void Init(JetskiRunnerGame owner,TriggerKind kind){ game=owner; Kind=kind; }
        public float SailSpeed { get; set; }
        // Stored heading: reading it back from localEulerAngles after adding roll/pitch can flip it.
        public float Yaw { get; set; }
        Vector3 knockVelocity, knockSpin;
        float knockedFor=-1f;

        // Boost smash: tumble up and away instead of blinking out of existence.
        public void Knock(float playerX)
        {
            if(knockedFor>=0) return;
            knockedFor=0f;
            foreach(var c in GetComponents<Collider>()) c.enabled=false;
            float side=transform.position.x>=playerX?1f:-1f;
            knockVelocity=new Vector3(side*Random.Range(5f,8f),Random.Range(7f,10f),Random.Range(6f,12f));
            knockSpin=new Vector3(Random.Range(-540f,540f),Random.Range(-360f,360f),Random.Range(-540f,540f));
        }

        void Update()
        {
            if(knockedFor>=0)
            {
                knockedFor+=Time.deltaTime;
                knockVelocity+=Physics.gravity*Time.deltaTime;
                transform.position+=knockVelocity*Time.deltaTime;
                transform.Rotate(knockSpin*Time.deltaTime,Space.World);
                if(knockedFor>1.6f) Destroy(gameObject);
                return;
            }
            UpdateHazard();
            if(Kind==TriggerKind.Coin) transform.Rotate(0,32f*Time.deltaTime,0,Space.World);
            else if(Kind==TriggerKind.Fuel) transform.Rotate(0,60f*Time.deltaTime,0,Space.World);
            else if(Kind>=TriggerKind.Magnet && Kind<=TriggerKind.DoubleCoins)
            {
                transform.Rotate(0,90f*Time.deltaTime,0,Space.World);
                var p=transform.localPosition; p.y=WaterY+1.25f+Mathf.Sin(Time.time*3f)*.15f; transform.localPosition=p;
            }
            else if(Kind==TriggerKind.Ship && !game.crashed)
            {
                var p=transform.localPosition;
                p.z-=SailSpeed*Time.deltaTime;
                p.y=WaterY+Mathf.Sin(Time.time*1.3f+p.z)*.12f;
                transform.localPosition=p;
                transform.localRotation=Quaternion.Euler(Mathf.Sin(Time.time*1.1f+p.z)*1.5f,Yaw,Mathf.Sin(Time.time*.9f+p.x)*2.5f);
            }
        }
        void OnTriggerEnter(Collider other)
        { if(knockedFor<0 && (other.transform==game.player||other.transform.IsChildOf(game.player))) game.OnTrigger(this); }
    }
}
