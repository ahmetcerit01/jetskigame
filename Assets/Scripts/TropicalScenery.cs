using System.Collections.Generic;
using UnityEngine;

// Procedural low-poly shoreline for every biome of the run: hazy backdrop mountains, clouds and a volcano that
// stay on the horizon (recoloured per biome), plus shore chunk templates per biome that scroll past both sides.
public sealed class TropicalScenery
{
    readonly System.Func<string, Color, float, float, Material> make;
    readonly GameObject rockPrefab, barrelPrefab, shipPrefab;
    readonly Material sand, grass, grassDark, trunk, frond, frondDark, coconut, cliff, mountain, cloud, plank, crate, flag;
    readonly Material stone, flagBlack, basalt, lava, ash, charred, stormRock, stormGrass, stormFrond, lighthouseWhite, lighthouseRed, lamp, glacier;
    readonly Mesh frondMesh, trunkMesh;
    readonly List<GameObject>[] templates;
    readonly Transform templateRoot;
    Transform volcano;
    readonly Color cloudEmission = new(.5f, .52f, .55f);

    public TropicalScenery(System.Func<string, Color, float, float, Material> make, GameObject rockPrefab, GameObject barrelPrefab, GameObject shipPrefab, int biomeCount)
    {
        this.make = make; this.rockPrefab = rockPrefab; this.barrelPrefab = barrelPrefab; this.shipPrefab = shipPrefab;
        sand = make("Beach sand", new Color(.84f, .74f, .53f), 0, .1f);
        grass = make("Island grass", new Color(.29f, .52f, .21f), 0, .15f);
        grassDark = make("Island grass shade", new Color(.22f, .46f, .2f), 0, .2f);
        trunk = make("Palm trunk", new Color(.45f, .3f, .17f), 0, .15f);
        frond = make("Palm frond", new Color(.27f, .62f, .2f), 0, .3f);
        frondDark = make("Palm frond dark", new Color(.16f, .45f, .16f), 0, .3f);
        coconut = make("Coconut", new Color(.3f, .19f, .08f), 0, .3f);
        cliff = make("Cliff rock", new Color(.31f, .34f, .35f), 0, .12f);
        mountain = make("Hazy mountain", new Color(.43f, .56f, .54f), 0, .05f);
        cloud = make("Cloud", new Color(1f, 1f, 1f), 0, 0f);
        Glow(cloud, cloudEmission);
        plank = make("Dock planks", new Color(.52f, .34f, .18f), 0, .2f);
        crate = make("Cargo crate", new Color(.72f, .52f, .3f), 0, .2f);
        flag = make("Red flag", new Color(.86f, .12f, .08f), 0, .25f);
        stone = make("Harbor stone", new Color(.55f, .52f, .47f), 0, .1f);
        flagBlack = make("Pirate flag", new Color(.04f, .04f, .05f), 0, .2f);
        basalt = make("Basalt", new Color(.11f, .09f, .09f), 0, .25f);
        lava = Glow(make("Lava", new Color(1f, .36f, .05f), 0, .6f), new Color(1.9f, .55f, .08f));
        ash = make("Volcanic ash", new Color(.25f, .22f, .21f), 0, .05f);
        charred = make("Charred wood", new Color(.07f, .05f, .04f), 0, .1f);
        stormRock = make("Storm rock", new Color(.2f, .23f, .25f), 0, .35f);
        stormGrass = make("Storm grass", new Color(.13f, .27f, .17f), 0, .3f);
        stormFrond = make("Storm frond", new Color(.1f, .3f, .15f), 0, .35f);
        lighthouseWhite = make("Lighthouse white", new Color(.85f, .86f, .84f), 0, .3f);
        lighthouseRed = make("Lighthouse red", new Color(.7f, .1f, .08f), 0, .3f);
        lamp = Glow(make("Lighthouse lamp", new Color(1f, .93f, .6f), 0, .8f), new Color(2.2f, 1.9f, 1.1f));
        glacier = make("Glacier ice", new Color(.42f, .74f, .86f), .1f, .7f);
        frondMesh = BuildFrondMesh();
        trunkMesh = BuildTrunkMesh();

        templateRoot = new GameObject("Shore chunk templates").transform;
        templateRoot.gameObject.SetActive(false);
        templates = new List<GameObject>[biomeCount];
        for (int i = 0; i < biomeCount; i++) templates[i] = new List<GameObject>();
        // 0 Tropical island
        for (int i = 0; i < 3; i++) templates[0].Add(BuildPalmIsland(i, false));
        templates[0].Add(BuildDock(false)); templates[0].Add(BuildCliff(0)); templates[0].Add(BuildCliff(1));
        // 1 Pirate harbor
        templates[1].Add(BuildDock(true)); templates[1].Add(BuildDock(true)); templates[1].Add(BuildHarborTower());
        if (shipPrefab) templates[1].Add(BuildShipwreck());
        templates[1].Add(BuildCliff(0));
        // 2 Cave passage: walls come from the tunnel segments, no shore chunks.
        // 3 Volcanic zone
        templates[3].Add(BuildBasaltCliff(0)); templates[3].Add(BuildBasaltCliff(1)); templates[3].Add(BuildLavaCone(0));
        templates[3].Add(BuildLavaCone(1)); templates[3].Add(BuildAshIsland());
        // 4 Storm
        templates[4].Add(BuildStormCliff(0)); templates[4].Add(BuildStormCliff(1)); templates[4].Add(BuildLighthouse());
        templates[4].Add(BuildPalmIsland(1, true)); templates[4].Add(BuildPalmIsland(2, true));
    }

    static Material Glow(Material m, Color emission)
    {
        if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission); }
        return m;
    }

    // Shore chunk for one side (-1 left, +1 right); authored for the right side and mirrored for the left.
    // Returns null for biomes without shore chunks.
    public GameObject SpawnShore(Transform parent, int side, float z, int biome, string biomeName)
    {
        var set = templates[biome];
        if (set.Count == 0) return null;
        var template = set[Random.Range(0, set.Count)];
        var chunk = Object.Instantiate(template, parent);
        chunk.name = "Shore | " + biomeName + " | " + template.name;
        chunk.transform.localPosition = new Vector3(side * Random.Range(9.5f, 12f), -.42f, z);
        chunk.transform.localRotation = Quaternion.Euler(0, side < 0 ? 180 : 0, 0);
        chunk.transform.localScale = Vector3.one * Random.Range(.9f, 1.15f);
        return chunk;
    }

    // Glacier mountain the cave tunnel cuts through; placed at the entrance and exit of every cave passage.
    public GameObject SpawnGlacierMouth(Transform parent, float z, bool entrance)
    {
        var root = new GameObject(entrance ? "Cave passage | glacier entrance" : "Cave passage | glacier exit").transform;
        root.SetParent(parent, false);
        root.localPosition = new Vector3(0, -.42f, z);
        float dir = entrance ? 1 : -1;
        for (int side = -1; side <= 1; side += 2)
        {
            Solid("Glacier flank", FacetedHill(8f, 17f, 9, 4, 300 + side), glacier, root).transform.localPosition = new Vector3(side * 12.5f, -.5f, dir * 6f);
            Solid("Glacier shoulder", FacetedHill(6f, 11f, 8, 3, 310 + side), glacier, root).transform.localPosition = new Vector3(side * 9.5f, -.5f, dir * 1.5f);
        }
        Solid("Glacier crown", FacetedHill(10f, 9f, 10, 3, 320), glacier, root).transform.localPosition = new Vector3(0, 6.2f, dir * 7f);
        return root.gameObject;
    }

    public void BuildBackdrop()
    {
        var root = new GameObject("Horizon backdrop").transform;
        var rng = new System.Random(77);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 6; i++)
            {
                float z = 150 + i * 42 + R(-10, 10);
                var m = Solid("Backdrop mountain", FacetedHill(R(18, 30), R(28, 60), 9, 5, rng.Next()), mountain, root);
                m.transform.position = new Vector3(side * R(38, 75 + i * 10), -1, z);
            }
        for (int i = 0; i < 4; i++)
        {
            var m = Solid("Far mountain", FacetedHill(R(30, 45), R(30, 50), 10, 5, rng.Next()), mountain, root);
            m.transform.position = new Vector3(R(-60, 60), -2, R(330, 380));
        }
        for (int i = 0; i < 9; i++)
        {
            var c = new GameObject("Cloud").transform; c.SetParent(root, false);
            c.position = new Vector3(R(-140, 140), R(32, 70), R(260, 380));
            int puffs = rng.Next(4, 8);
            for (int p = 0; p < puffs; p++)
            {
                float s = R(10, 20) * (1 - Mathf.Abs(p - puffs * .5f) / puffs);
                var puff = Primitive("Cloud puff", PrimitiveType.Sphere, c, new Vector3((p - puffs * .5f) * 7.5f, R(-1, 4), R(-3, 3)), new Vector3(s * 1.4f, s, s), cloud);
                puff.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        // Volcano: sinks below the horizon outside the volcanic zone and rises out of the haze inside it.
        volcano = new GameObject("Backdrop volcano").transform; volcano.SetParent(root, false);
        volcano.position = new Vector3(-26, -90, 300);
        Solid("Volcano cone", FacetedHill(70, 78, 12, 5, 501), basalt, volcano);
        Solid("Volcano crater glow", FacetedHill(9, 6, 9, 2, 502), lava, volcano).transform.localPosition = new Vector3(0, 74, 0);
        for (int i = 0; i < 3; i++)
        {
            var stream = Solid("Lava stream", FacetedHill(4, 30, 6, 3, 510 + i), lava, volcano).transform;
            stream.localPosition = new Vector3(-10 + i * 10, 38, -32 + i * 4);
            stream.localRotation = Quaternion.Euler(-48, i * 12 - 12, 0);
        }
        for (int i = 0; i < 6; i++)
        {
            var smoke = Primitive("Volcano smoke", PrimitiveType.Sphere, volcano, new Vector3(i * 6 - 6, 84 + i * 9, i * 3), Vector3.one * (16 + i * 5), ash);
            smoke.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        volcano.gameObject.SetActive(false);
    }

    public void SetAtmosphere(Color mountainColor, Color cloudColor, float volcanoWeight)
    {
        mountain.color = mountainColor;
        cloud.color = cloudColor;
        if (cloud.HasProperty("_EmissionColor")) cloud.SetColor("_EmissionColor", cloudEmission * cloudColor.grayscale);
        if (!volcano) return;
        volcano.gameObject.SetActive(volcanoWeight > .01f);
        var p = volcano.position; p.y = Mathf.Lerp(-90f, -2f, Mathf.SmoothStep(0, 1, volcanoWeight)); volcano.position = p;
    }

    // ---------- tropical island ----------

    GameObject BuildPalmIsland(int variant, bool storm)
    {
        var root = Chunk(storm ? "Windswept island" : "Palm island");
        float radius = 4.2f + variant * .9f;
        Solid("Sand bank", FacetedHill(radius, .9f, 12, 2, 11 + variant), storm ? ash : sand, root.transform).transform.localPosition = new Vector3(1.5f, -.3f, 0);
        Solid("Grass mound", FacetedHill(radius * .62f, 1.6f + variant * .4f, 9, 3, 21 + variant), storm ? stormGrass : variant == 1 ? grassDark : grass, root.transform).transform.localPosition = new Vector3(2.4f, .1f, .6f);
        int palms = 2 + variant;
        for (int i = 0; i < palms; i++)
            AddPalm(root.transform, new Vector3(Random.Range(-.5f, 3.2f), .5f, Random.Range(-radius * .6f, radius * .6f)), Random.Range(4.2f, 6.2f),
                storm ? stormFrond : frond, storm ? stormGrass : frondDark, storm ? -26f : -9f);
        if (rockPrefab && variant != 1)
        {
            var rock = Object.Instantiate(rockPrefab, root.transform);
            rock.transform.localPosition = new Vector3(-1.6f, 0, -radius * .7f);
            rock.transform.localScale = Vector3.one * 1.3f;
        }
        return root;
    }

    GameObject BuildDock(bool pirate)
    {
        var root = Chunk(pirate ? "Pirate pier" : "Wooden dock");
        Solid("Dock island", FacetedHill(5.5f, 1.1f, 12, 2, 41), sand, root.transform).transform.localPosition = new Vector3(4.5f, -.3f, 0);
        Primitive("Dock deck", PrimitiveType.Cube, root.transform, new Vector3(-.6f, 1.1f, 0), new Vector3(5.2f, .2f, 3.2f), plank);
        for (int x = 0; x < 3; x++) for (int z = -1; z <= 1; z += 2)
            Primitive("Dock post", PrimitiveType.Cylinder, root.transform, new Vector3(-2.9f + x * 2.3f, .3f, z * 1.45f), new Vector3(.22f, .9f, .22f), trunk);
        Primitive("Crate", PrimitiveType.Cube, root.transform, new Vector3(-.4f, 1.55f, .7f), new Vector3(.75f, .7f, .75f), crate).transform.localRotation = Quaternion.Euler(0, 12, 0);
        Primitive("Crate", PrimitiveType.Cube, root.transform, new Vector3(.4f, 1.5f, .9f), new Vector3(.62f, .6f, .62f), crate);
        Primitive("Crate", PrimitiveType.Cube, root.transform, new Vector3(-.1f, 2.15f, .8f), new Vector3(.55f, .5f, .55f), crate).transform.localRotation = Quaternion.Euler(0, -20, 0);
        if (barrelPrefab)
        {
            var barrel = Object.Instantiate(barrelPrefab, root.transform);
            barrel.transform.localPosition = new Vector3(-2.2f, 1.6f, -.8f);
            barrel.transform.localScale = Vector3.one * .7f;
        }
        Primitive("Flag pole", PrimitiveType.Cylinder, root.transform, new Vector3(1.6f, 3.2f, -1.2f), new Vector3(.08f, 2.1f, .08f), trunk);
        Primitive("Flag", PrimitiveType.Cube, root.transform, new Vector3(1.6f, 4.8f, -1.75f), new Vector3(.04f, .65f, 1f), pirate ? flagBlack : flag);
        if (pirate)
        {
            // harbor warehouse instead of palms
            Primitive("Warehouse", PrimitiveType.Cube, root.transform, new Vector3(5f, 1.8f, .2f), new Vector3(3.6f, 2.6f, 4.2f), plank);
            Primitive("Warehouse roof", PrimitiveType.Cube, root.transform, new Vector3(5f, 3.3f, .2f), new Vector3(4f, .35f, 4.6f), charred).transform.localRotation = Quaternion.Euler(0, 0, 8);
            Primitive("Lantern", PrimitiveType.Sphere, root.transform, new Vector3(-2.9f, 1.55f, 1.45f), Vector3.one * .25f, lamp);
        }
        else
        {
            AddPalm(root.transform, new Vector3(5f, .6f, 1.5f), 5.6f, frond, frondDark, -9f);
            AddPalm(root.transform, new Vector3(6.2f, .6f, -1.6f), 4.8f, frond, frondDark, -9f);
        }
        return root;
    }

    GameObject BuildCliff(int variant)
    {
        var root = Chunk("Rocky cliff");
        Solid("Cliff", FacetedHill(4.5f + variant, 7f + variant * 3f, 8, 4, 61 + variant), cliff, root.transform).transform.localPosition = new Vector3(3.5f, -.5f, 0);
        Solid("Cliff shoulder", FacetedHill(3f, 4f, 7, 3, 71 + variant), cliff, root.transform).transform.localPosition = new Vector3(1f, -.5f, 3.2f);
        Solid("Cliff grass", FacetedHill(2.6f, 1.2f, 8, 2, 81 + variant), grass, root.transform).transform.localPosition = new Vector3(4f, 6f + variant * 3f, 0);
        AddPalm(root.transform, new Vector3(3.6f, 6.4f + variant * 3f, .4f), 4.2f, frond, frondDark, -9f);
        if (rockPrefab)
        {
            var rock = Object.Instantiate(rockPrefab, root.transform);
            rock.transform.localPosition = new Vector3(-1.4f, 0, -2.5f);
        }
        return root;
    }

    // ---------- pirate harbor ----------

    GameObject BuildHarborTower()
    {
        var root = Chunk("Harbor watchtower");
        Solid("Tower rocks", FacetedHill(4.5f, 1.8f, 10, 2, 91), cliff, root.transform).transform.localPosition = new Vector3(3f, -.4f, 0);
        Primitive("Tower", PrimitiveType.Cylinder, root.transform, new Vector3(3f, 4f, 0), new Vector3(2.6f, 3.4f, 2.6f), stone);
        Primitive("Tower top", PrimitiveType.Cylinder, root.transform, new Vector3(3f, 7.6f, 0), new Vector3(3.3f, .35f, 3.3f), stone);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4;
            Primitive("Battlement", PrimitiveType.Cube, root.transform, new Vector3(3f + Mathf.Cos(a) * 1.45f, 8.2f, Mathf.Sin(a) * 1.45f), new Vector3(.5f, .6f, .5f), stone);
        }
        Primitive("Flag pole", PrimitiveType.Cylinder, root.transform, new Vector3(3f, 9.6f, 0), new Vector3(.08f, 1.8f, .08f), trunk);
        Primitive("Pirate flag", PrimitiveType.Cube, root.transform, new Vector3(3f, 10.7f, -.6f), new Vector3(.04f, .8f, 1.2f), flagBlack);
        Primitive("Cannon", PrimitiveType.Cylinder, root.transform, new Vector3(1.2f, 8.1f, 0), new Vector3(.32f, .8f, .32f), charred).transform.localRotation = Quaternion.Euler(0, 0, 80);
        return root;
    }

    GameObject BuildShipwreck()
    {
        var root = Chunk("Shipwreck");
        Solid("Sand bar", FacetedHill(6f, .7f, 12, 2, 101), sand, root.transform).transform.localPosition = new Vector3(3f, -.35f, 0);
        var wreck = Object.Instantiate(shipPrefab, root.transform);
        wreck.name = "Wrecked ship";
        wreck.transform.localPosition = new Vector3(3.5f, -.9f, 0);
        wreck.transform.localRotation = Quaternion.Euler(4, 35, 24);
        return root;
    }

    // ---------- volcanic zone ----------

    GameObject BuildBasaltCliff(int variant)
    {
        var root = Chunk("Basalt cliff");
        Solid("Basalt", FacetedHill(4.8f + variant, 8f + variant * 3f, 7, 4, 121 + variant), basalt, root.transform).transform.localPosition = new Vector3(3.5f, -.5f, 0);
        Solid("Basalt shoulder", FacetedHill(3f, 4.5f, 6, 3, 131 + variant), basalt, root.transform).transform.localPosition = new Vector3(1f, -.5f, -3f);
        var vein = Solid("Lava vein", FacetedHill(1.1f, 6.5f + variant * 2.5f, 5, 3, 141 + variant), lava, root.transform).transform;
        vein.localPosition = new Vector3(1.7f, -.4f, .8f); vein.localRotation = Quaternion.Euler(0, 0, 14);
        Solid("Lava pool", FacetedHill(1.9f, .15f, 9, 1, 151 + variant), lava, root.transform).transform.localPosition = new Vector3(.4f, -.38f, 1.4f);
        return root;
    }

    GameObject BuildLavaCone(int variant)
    {
        var root = Chunk("Lava cone");
        Solid("Ash slope", FacetedHill(5.5f, 1f, 10, 2, 161 + variant), ash, root.transform).transform.localPosition = new Vector3(3f, -.35f, 0);
        Solid("Cone", FacetedHill(3.6f + variant, 5.5f + variant * 2f, 8, 4, 171 + variant), basalt, root.transform).transform.localPosition = new Vector3(3.5f, 0, 0);
        Solid("Crater glow", FacetedHill(1.1f, .9f, 7, 2, 181 + variant), lava, root.transform).transform.localPosition = new Vector3(3.5f, 5.1f + variant * 2f, 0);
        for (int i = 0; i < 3; i++)
            Solid("Lava rock", FacetedHill(.7f, .9f, 6, 2, 191 + i), i == 1 ? lava : basalt, root.transform).transform.localPosition = new Vector3(-.4f + i * .8f, -.3f, -3f + i * 2.4f);
        return root;
    }

    GameObject BuildAshIsland()
    {
        var root = Chunk("Ash island");
        Solid("Ash bank", FacetedHill(5f, 1f, 11, 2, 201), ash, root.transform).transform.localPosition = new Vector3(2f, -.3f, 0);
        for (int i = 0; i < 3; i++)
        {
            var dead = Solid("Charred trunk", trunkMesh, charred, root.transform).transform;
            dead.localPosition = new Vector3(1.5f + i * 1.2f, .4f, -2f + i * 1.8f);
            dead.localScale = new Vector3(1, .7f + i * .15f, 1);
            dead.localRotation = Quaternion.Euler(-12 - i * 6, i * 70, 0);
        }
        Solid("Lava crack", FacetedHill(1.4f, .12f, 8, 1, 211), lava, root.transform).transform.localPosition = new Vector3(.6f, -.25f, 1.6f);
        return root;
    }

    // ---------- storm ----------

    GameObject BuildStormCliff(int variant)
    {
        var root = Chunk("Storm cliff");
        Solid("Sea stack", FacetedHill(4.6f + variant, 9f + variant * 3f, 7, 4, 221 + variant), stormRock, root.transform).transform.localPosition = new Vector3(3.5f, -.5f, 0);
        Solid("Stack shoulder", FacetedHill(2.8f, 4.5f, 6, 3, 231 + variant), stormRock, root.transform).transform.localPosition = new Vector3(.9f, -.5f, 3f);
        Solid("Stack moss", FacetedHill(2.4f, 1f, 8, 2, 241 + variant), stormGrass, root.transform).transform.localPosition = new Vector3(4f, 7.8f + variant * 3f, 0);
        if (rockPrefab)
        {
            var rock = Object.Instantiate(rockPrefab, root.transform);
            rock.transform.localPosition = new Vector3(-1.2f, 0, -2.6f);
            rock.transform.localScale = Vector3.one * 1.4f;
        }
        return root;
    }

    GameObject BuildLighthouse()
    {
        var root = Chunk("Lighthouse");
        Solid("Lighthouse rocks", FacetedHill(4.2f, 2.2f, 9, 2, 251), stormRock, root.transform).transform.localPosition = new Vector3(3f, -.5f, 0);
        for (int i = 0; i < 4; i++)
            Primitive("Lighthouse band", PrimitiveType.Cylinder, root.transform, new Vector3(3f, 2.6f + i * 1.7f, 0), new Vector3(1.7f - i * .14f, .85f, 1.7f - i * .14f), i % 2 == 0 ? lighthouseWhite : lighthouseRed);
        Primitive("Lamp room", PrimitiveType.Cylinder, root.transform, new Vector3(3f, 9.3f, 0), new Vector3(1.2f, .55f, 1.2f), lamp);
        Primitive("Lamp roof", PrimitiveType.Sphere, root.transform, new Vector3(3f, 10f, 0), new Vector3(1.4f, .9f, 1.4f), lighthouseRed);
        return root;
    }

    // ---------- shared pieces ----------

    void AddPalm(Transform parent, Vector3 basePos, float height, Material leafA, Material leafB, float meanLean)
    {
        var palm = new GameObject("Palm tree").transform;
        palm.SetParent(parent, false);
        palm.localPosition = basePos;
        palm.localRotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
        float lean = meanLean + Random.Range(-5f, 5f);
        var trunkGo = Solid("Palm trunk", trunkMesh, trunk, palm);
        trunkGo.transform.localScale = new Vector3(1, height / 5f, 1);
        trunkGo.transform.localRotation = Quaternion.Euler(lean, 0, 0);
        Vector3 top = trunkGo.transform.localRotation * new Vector3(0, 5f * height / 5f, .9f * height / 5f);
        for (int i = 0; i < 8; i++)
        {
            var leaf = Solid("Palm frond", frondMesh, i % 2 == 0 ? leafA : leafB, palm);
            leaf.transform.localPosition = top;
            leaf.transform.localRotation = Quaternion.Euler(Random.Range(-12f, 6f), i * 45 + Random.Range(-10f, 10f), 0);
            leaf.transform.localScale = Vector3.one * Random.Range(.85f, 1.15f);
        }
        for (int i = 0; i < 3; i++)
            Primitive("Coconut", PrimitiveType.Sphere, palm, top + new Vector3(Mathf.Cos(i * 2.1f) * .2f, -.25f, Mathf.Sin(i * 2.1f) * .2f), Vector3.one * .26f, coconut);
    }

    GameObject Chunk(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(templateRoot, false);
        return go;
    }

    static GameObject Solid(string name, Mesh mesh, Material material, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    // Faceted dome/peak: rings of jittered vertices narrowing to an apex, flat shaded.
    static Mesh FacetedHill(float radius, float height, int sides, int rings, int seed)
    {
        var rng = new System.Random(seed);
        float J(float a) => ((float)rng.NextDouble() - .5f) * 2f * a;
        var points = new List<Vector3[]>();
        for (int r = 0; r < rings; r++)
        {
            float k = r / (float)rings;
            float ringRadius = radius * Mathf.Pow(1 - k, .75f);
            var ring = new Vector3[sides];
            for (int s = 0; s < sides; s++)
            {
                float a = (s + J(.25f)) / sides * Mathf.PI * 2;
                float rr = ringRadius * (1 + J(.18f));
                ring[s] = new Vector3(Mathf.Cos(a) * rr, height * k + J(height * .06f) * (r > 0 ? 1 : 0), Mathf.Sin(a) * rr);
            }
            points.Add(ring);
        }
        var apex = new Vector3(J(radius * .12f), height, J(radius * .12f));
        var verts = new List<Vector3>(); var tris = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = verts.Count; verts.Add(a); verts.Add(b); verts.Add(c); tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < sides; s++)
            {
                int n = (s + 1) % sides;
                var a = points[r][s]; var b = points[r][n];
                if (r == rings - 1) { Tri(a, apex, b); continue; }
                var c = points[r + 1][s]; var d = points[r + 1][n];
                Tri(a, c, b); Tri(b, c, d);
            }
        var mesh = new Mesh { name = "Faceted hill" };
        mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // Arching, drooping, V-folded leaf strip; both faces so it reads from any angle.
    static Mesh BuildFrondMesh()
    {
        const int segments = 7; const float length = 2.7f;
        var verts = new List<Vector3>(); var tris = new List<int>();
        Vector3 Spine(float s) => new Vector3(0, Mathf.Sin(s * Mathf.PI * .55f) * .55f - s * s * 1.5f, s * length);
        float Width(float s) => .42f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, .12f + s));
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(a); verts.Add(c); verts.Add(b);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i + 3); tris.Add(i + 4); tris.Add(i + 5);
        }
        for (int i = 0; i < segments; i++)
        {
            float s0 = i / (float)segments, s1 = (i + 1) / (float)segments;
            Vector3 c0 = Spine(s0), c1 = Spine(s1);
            float w0 = Width(s0), w1 = i == segments - 1 ? 0 : Width(s1);
            Vector3 l0 = c0 + new Vector3(-w0, -w0 * .35f, 0), r0 = c0 + new Vector3(w0, -w0 * .35f, 0);
            Vector3 l1 = c1 + new Vector3(-w1, -w1 * .35f, 0), r1 = c1 + new Vector3(w1, -w1 * .35f, 0);
            Tri(c0, c1, l0); Tri(l0, c1, l1); Tri(c0, r0, c1); Tri(r0, r1, c1);
        }
        var mesh = new Mesh { name = "Palm frond" };
        mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // Tapered six-sided trunk that bends forward towards the top (5 m tall at scale 1).
    static Mesh BuildTrunkMesh()
    {
        const int sides = 6, rings = 8;
        var verts = new List<Vector3>(); var tris = new List<int>();
        Vector3 Point(int r, int s)
        {
            float k = r / (float)(rings - 1);
            float radius = Mathf.Lerp(.24f, .13f, k) * (r % 2 == 0 ? 1.08f : 1f);
            float a = s / (float)sides * Mathf.PI * 2;
            return new Vector3(Mathf.Cos(a) * radius, k * 5f, Mathf.Sin(a) * radius + k * k * .9f);
        }
        void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = verts.Count; verts.Add(a); verts.Add(b); verts.Add(c); tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); }
        for (int r = 0; r < rings - 1; r++)
            for (int s = 0; s < sides; s++)
            {
                int n = (s + 1) % sides;
                Tri(Point(r, s), Point(r + 1, s), Point(r, n));
                Tri(Point(r, n), Point(r + 1, s), Point(r + 1, n));
            }
        var mesh = new Mesh { name = "Palm trunk" };
        mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
}
