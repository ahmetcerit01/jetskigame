#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Generates the water splash textures/materials (soft clumpy droplet, broken foam ring) and wires them into
// every runner scene's JetskiRunnerGame (splashMaterial, foamRingMaterial).
//   Unity -batchmode -quit -projectPath . -executeMethod JetskiSplashSetup.Apply
public static class JetskiSplashSetup
{
    const string TexDir = "Assets/Art/FX/";
    const string MatDir = "Assets/Materials/FX/";
    static readonly string[] Scenes = { "Assets/Scenes/OceanRun.unity", "Assets/Scenes/PirateCove.unity", "Assets/Scenes/IceCave.unity" };

    [MenuItem("Jetski Runner/Apply Water Splash FX")]
    public static void Apply()
    {
        Directory.CreateDirectory(TexDir); Directory.CreateDirectory(MatDir);
        var droplet = SaveTexture("SplashDroplet.png", Droplet(128));
        var ring = SaveTexture("SplashFoamRing.png", FoamRing(256));
        var dropMat = SaveMaterial("WaterSplash.mat", droplet, new Color(.93f, .98f, 1f, .9f));
        var ringMat = SaveMaterial("WaterFoamRing.mat", ring, new Color(.95f, 1f, 1f, .85f));

        foreach (var path in Scenes)
        {
            if (!File.Exists(path)) continue;
            var scene = EditorSceneManager.OpenScene(path);
            var game = Object.FindFirstObjectByType<JetskiRunnerGame>();
            if (!game) continue;
            var so = new SerializedObject(game);
            so.FindProperty("splashMaterial").objectReferenceValue = dropMat;
            so.FindProperty("foamRingMaterial").objectReferenceValue = ringMat;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("SPLASH FX applied");
    }

    // A soft white clump: a few overlapping blobs with a bright core, so a cluster reads as thrown water.
    static Texture2D Droplet(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var rnd = new System.Random(7);
        var blobs = new Vector3[6];
        for (int i = 0; i < blobs.Length; i++)
        {
            float a = (float)rnd.NextDouble() * Mathf.PI * 2, r = i == 0 ? 0 : .12f + (float)rnd.NextDouble() * .16f;
            blobs[i] = new Vector3(.5f + Mathf.Cos(a) * r, .5f + Mathf.Sin(a) * r, i == 0 ? .26f : .1f + (float)rnd.NextDouble() * .1f);
        }
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            var p = new Vector2((x + .5f) / n, (y + .5f) / n);
            // gaussian lobes summed (no hard rims, so clusters read as spray rather than bubbles)
            float alpha = 0f;
            foreach (var b in blobs)
            {
                float d = Vector2.Distance(p, new Vector2(b.x, b.y)) / b.z;
                alpha += Mathf.Exp(-d * d * 2.2f) * .55f;
            }
            float noise = Mathf.PerlinNoise(p.x * 5f + 1.3f, p.y * 5f + 2.1f) * .5f + .5f;
            alpha = Mathf.Clamp01(alpha * noise * 1.3f);
            float edge = Mathf.Clamp01(1f - Vector2.Distance(p, new Vector2(.5f, .5f)) * 2f);
            alpha *= Mathf.SmoothStep(0, 1, Mathf.Clamp01(edge * 2.5f));   // never touch the quad border
            float shade = Mathf.Lerp(.93f, 1f, alpha);                     // only a faint blue in the thin parts
            tex.SetPixel(x, y, new Color(shade * .96f, shade * .99f, shade, alpha));
        }
        tex.Apply();
        return tex;
    }

    // Broken foam ring seen from above, for the splash-down ring that spreads on the water.
    static Texture2D FoamRing(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            var p = new Vector2((x + .5f) / n - .5f, (y + .5f) / n - .5f) * 2f;
            float r = p.magnitude, ang = Mathf.Atan2(p.y, p.x);
            float ring = Mathf.Exp(-Mathf.Pow((r - .72f) / .13f, 2f));
            float inner = Mathf.Clamp01(1f - r / .7f) * .28f;             // faint churned foam inside
            float broken = Mathf.PerlinNoise(Mathf.Cos(ang) * 3f + 10f, Mathf.Sin(ang) * 3f + r * 6f);
            float speck = Mathf.PerlinNoise(p.x * 14f + 3f, p.y * 14f + 5f);
            float alpha = Mathf.Clamp01((ring * Mathf.SmoothStep(.25f, .7f, broken) + inner * speck) * 1.4f);
            alpha *= Mathf.Clamp01((1f - r) * 8f);
            tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
        }
        tex.Apply();
        return tex;
    }

    static Texture2D SaveTexture(string file, Texture2D tex)
    {
        string path = TexDir + file;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material SaveMaterial(string file, Texture2D tex, Color tint)
    {
        string path = MatDir + file;
        var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = shader;
        mat.SetTexture("_MainTex", tex);
        // the legacy particle shader doubles _TintColor, so 0.5 is neutral
        mat.SetColor("_TintColor", new Color(tint.r * .5f, tint.g * .5f, tint.b * .5f, tint.a * .5f));
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
#endif
