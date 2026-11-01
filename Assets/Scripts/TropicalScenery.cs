using System.Collections.Generic;
using UnityEngine;

// Procedural low-poly shoreline for the tropical runs: hazy backdrop mountains and clouds that stay on the
// horizon, plus recyclable shore chunks (palm islands, docks, rocky cliffs) that scroll past both sides.
public sealed class TropicalScenery
{
    readonly System.Func<string, Color, float, float, Material> make;
    readonly GameObject rockPrefab, barrelPrefab;
    readonly Material sand, grass, grassDark, trunk, frond, frondDark, coconut, cliff, mountain, cloud, plank, crate, flag;
    readonly Mesh frondMesh, trunkMesh;
    readonly List<GameObject> templates = new();
    readonly Transform templateRoot;

    public TropicalScenery(System.Func<string, Color, float, float, Material> make, GameObject rockPrefab, GameObject barrelPrefab, bool cove)
    {
        this.make = make; this.rockPrefab = rockPrefab; this.barrelPrefab = barrelPrefab;
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
        if (cloud.HasProperty("_EmissionColor")) { cloud.EnableKeyword("_EMISSION"); cloud.SetColor("_EmissionColor", new Color(.5f, .52f, .55f)); }
        plank = make("Dock planks", new Color(.52f, .34f, .18f), 0, .2f);
        crate = make("Cargo crate", new Color(.72f, .52f, .3f), 0, .2f);
        flag = make("Red flag", new Color(.86f, .12f, .08f), 0, .25f);
        frondMesh = BuildFrondMesh();
        trunkMesh = BuildTrunkMesh();

        templateRoot = new GameObject("Shore chunk templates").transform;
        templateRoot.gameObject.SetActive(false);
        for (int i = 0; i < 3; i++) templates.Add(BuildPalmIsland(i));
        templates.Add(BuildDock());
        if (cove) templates.Add(BuildDock());
        templates.Add(BuildCliff(0)); templates.Add(BuildCliff(1));
    }

    // Shore chunk for one side (-1 left, +1 right); authored for the right side and mirrored for the left.
    public GameObject SpawnShore(Transform parent, int side, float z)
    {
        var chunk = Object.Instantiate(templates[Random.Range(0, templates.Count)], parent);
        chunk.transform.localPosition = new Vector3(side * Random.Range(9.5f, 12f), -.42f, z);
        chunk.transform.localRotation = Quaternion.Euler(0, side < 0 ? 180 : 0, 0);
        chunk.transform.localScale = Vector3.one * Random.Range(.9f, 1.15f);
        return chunk;
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
    }

    GameObject BuildPalmIsland(int variant)
    {
        var root = Chunk("Palm island");
        float radius = 4.2f + variant * .9f;
        Solid("Sand bank", FacetedHill(radius, .9f, 12, 2, 11 + variant), sand, root.transform).transform.localPosition = new Vector3(1.5f, -.3f, 0);
        Solid("Grass mound", FacetedHill(radius * .62f, 1.6f + variant * .4f, 9, 3, 21 + variant), variant == 1 ? grassDark : grass, root.transform).transform.localPosition = new Vector3(2.4f, .1f, .6f);
        int palms = 2 + variant;
        for (int i = 0; i < palms; i++)
            AddPalm(root.transform, new Vector3(Random.Range(-.5f, 3.2f), .5f, Random.Range(-radius * .6f, radius * .6f)), Random.Range(4.2f, 6.2f));
        if (rockPrefab && variant != 1)
        {
            var rock = Object.Instantiate(rockPrefab, root.transform);
            rock.transform.localPosition = new Vector3(-1.6f, 0, -radius * .7f);
            rock.transform.localScale = Vector3.one * 1.3f;
        }
        return root;
    }

    GameObject BuildDock()
    {
        var root = Chunk("Wooden dock");
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
        Primitive("Flag", PrimitiveType.Cube, root.transform, new Vector3(1.6f, 4.8f, -1.75f), new Vector3(.04f, .65f, 1f), flag);
        AddPalm(root.transform, new Vector3(5f, .6f, 1.5f), 5.6f);
        AddPalm(root.transform, new Vector3(6.2f, .6f, -1.6f), 4.8f);
        return root;
    }

    GameObject BuildCliff(int variant)
    {
        var root = Chunk("Rocky cliff");
        Solid("Cliff", FacetedHill(4.5f + variant, 7f + variant * 3f, 8, 4, 61 + variant), cliff, root.transform).transform.localPosition = new Vector3(3.5f, -.5f, 0);
        Solid("Cliff shoulder", FacetedHill(3f, 4f, 7, 3, 71 + variant), cliff, root.transform).transform.localPosition = new Vector3(1f, -.5f, 3.2f);
        Solid("Cliff grass", FacetedHill(2.6f, 1.2f, 8, 2, 81 + variant), grass, root.transform).transform.localPosition = new Vector3(4f, 6f + variant * 3f, 0);
        AddPalm(root.transform, new Vector3(3.6f, 6.4f + variant * 3f, .4f), 4.2f);
        if (rockPrefab)
        {
            var rock = Object.Instantiate(rockPrefab, root.transform);
            rock.transform.localPosition = new Vector3(-1.4f, 0, -2.5f);
        }
        return root;
    }

    void AddPalm(Transform parent, Vector3 basePos, float height)
    {
        var palm = new GameObject("Palm tree").transform;
        palm.SetParent(parent, false);
        palm.localPosition = basePos;
        palm.localRotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
        float lean = Random.Range(-14f, -4f);
        var trunkGo = Solid("Palm trunk", trunkMesh, trunk, palm);
        trunkGo.transform.localScale = new Vector3(1, height / 5f, 1);
        trunkGo.transform.localRotation = Quaternion.Euler(lean, 0, 0);
        Vector3 top = trunkGo.transform.localRotation * new Vector3(0, 5f * height / 5f, .9f * height / 5f);
        for (int i = 0; i < 8; i++)
        {
            var leaf = Solid("Palm frond", frondMesh, i % 2 == 0 ? frond : frondDark, palm);
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

    GameObject Solid(string name, Mesh mesh, Material material, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material material)
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
