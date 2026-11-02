#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds game-ready prefabs from the Meshy GLB props (Assets/Models/Meshy/Props, imported by glTFast)
// and swaps them into the runner scenes. Each prop is turned to face +Z, scaled to a real-world size
// and dropped so its waterline sits at y = 0, the convention JetskiRunnerGame.SpawnAsset expects.
public static class JetskiMeshyProps
{
    const string ModelDir = "Assets/Models/Meshy/Props/";
    const string PrefabDir = "Assets/Prefabs/Meshy/Props/";
    static readonly string[] Scenes = { "Assets/Scenes/OceanRun.unity", "Assets/Scenes/PirateCove.unity", "Assets/Scenes/IceCave.unity" };

    enum Fit { Length, Height, Diameter }

    // name, model, fit axis, size (m), yaw (deg), waterline (fraction of height below y=0)
    static readonly (string name, string model, Fit fit, float size, float yaw, float waterline)[] Props =
    {
        ("PirateShip_Black", "Pirate_Ship_Black.glb", Fit.Length, 8.0f, -90f, .18f),
        ("Barrel",           "Barrel.glb",            Fit.Height, 1.15f,  0f, .30f),
        ("StarCoin",         "Star_Coin.glb",         Fit.Diameter, .85f, 0f, 0f),
    };

    [MenuItem("Jetski Runner/Apply Meshy Props")]
    public static void Apply()
    {
        Directory.CreateDirectory(PrefabDir);
        AssetDatabase.Refresh();
        var built = new Dictionary<string, GameObject>();
        foreach (var p in Props) built[p.name] = BuildPrefab(p.name, p.model, p.fit, p.size, p.yaw, p.waterline);
        AssetDatabase.SaveAssets();

        foreach (var path in Scenes)
        {
            if (!File.Exists(path)) continue;
            var scene = EditorSceneManager.OpenScene(path);
            var game = Object.FindFirstObjectByType<JetskiRunnerGame>();
            if (!game) continue;
            var so = new SerializedObject(game);
            so.FindProperty("pirateShipPrefab").objectReferenceValue = built["PirateShip_Black"];
            so.FindProperty("goldPrefab").objectReferenceValue = built["StarCoin"];
            so.FindProperty("dockBarrelPrefab").objectReferenceValue = built["Barrel"];
            so.FindProperty("shipLength").floatValue = 8.5f;
            // Replace the Blender barrel in the obstacle pool, keep the other obstacles for now.
            var list = so.FindProperty("obstaclePrefabs");
            bool replaced = false;
            for (int i = 0; i < list.arraySize; i++)
            {
                var el = list.GetArrayElementAtIndex(i);
                if (el.objectReferenceValue && el.objectReferenceValue.name.Contains("Barrel")) { el.objectReferenceValue = built["Barrel"]; replaced = true; }
            }
            if (!replaced) { list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = built["Barrel"]; }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        Debug.Log("MESHY PROPS applied: " + string.Join(", ", built.Keys));
    }

    static GameObject BuildPrefab(string name, string model, Fit fit, float size, float yaw, float waterline)
    {
        string path = ModelDir + model;
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!source) throw new System.Exception("Missing or not imported (is glTFast installed?): " + path);

        var root = new GameObject(name);
        var pivot = new GameObject("Model").transform;
        pivot.SetParent(root.transform, false);
        var inst = Object.Instantiate(source, pivot, false);
        inst.name = Path.GetFileNameWithoutExtension(model);
        inst.transform.localRotation = Quaternion.Euler(0, yaw, 0) * inst.transform.localRotation;

        var b = Bounds(inst, pivot);
        float measure = fit == Fit.Height ? b.size.y : fit == Fit.Length ? Mathf.Max(b.size.x, b.size.z) : Mathf.Max(b.size.x, b.size.y);
        // Floating props sit on the waterline; coins spin around their own centre.
        inst.transform.localPosition -= fit == Fit.Diameter ? b.center : new Vector3(b.center.x, b.min.y + waterline * b.size.y, b.center.z);
        pivot.localScale = Vector3.one * (size / Mathf.Max(.0001f, measure));

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
        }
        foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + name + ".prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static Bounds Bounds(GameObject go, Transform space)
    {
        bool any = false; var b = new Bounds();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.sharedMesh) continue;
            foreach (var v in mf.sharedMesh.vertices)
            {
                var p = space.InverseTransformPoint(mf.transform.TransformPoint(v));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }
        if (!any) throw new System.Exception(go.name + " has no mesh");
        return b;
    }

    // Batchmode: apply, then render every prop on a water plane next to the rider for scale.
    //   Unity -batchmode -projectPath . -executeMethod JetskiMeshyProps.ApplyAndCapture -captureDir <dir>
    public static void ApplyAndCapture()
    {
        Apply();
        string dir = "Logs/PropCapture";
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-captureDir") dir = args[i + 1];
        Directory.CreateDirectory(dir);

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.25f; sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(45, -35, 0);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.65f, .72f, .8f);
        var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.transform.localScale = new Vector3(4, 1, 4);
        water.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(.06f, .55f, .74f) };

        float x = -6f;
        var rider = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Meshy/MeshyPirateJetski.prefab");
        if (rider) Object.Instantiate(rider, new Vector3(x, 0, 0), Quaternion.identity);
        x += 3f;
        foreach (var p in Props)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + p.name + ".prefab");
            var go = Object.Instantiate(prefab, new Vector3(x + (p.name.Contains("Ship") ? 4f : 0f), p.name.Contains("Coin") ? .6f : 0f, 0), Quaternion.identity);
            x += p.name.Contains("Ship") ? 9.5f : 2.2f;
        }

        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.74f, .88f, .94f); cam.fieldOfView = 30;
        var rt = new RenderTexture(1600, 800, 24); cam.targetTexture = rt;
        (string tag, Vector3 pos, Vector3 look)[] views =
        {
            ("lineup", new Vector3(3f, 4.5f, -17f), new Vector3(3f, 1.2f, 0)),
            ("behind", new Vector3(3f, 3.2f, -9f), new Vector3(3f, 1f, 8f)),
        };
        foreach (var v in views)
        {
            cam.transform.position = v.pos; cam.transform.LookAt(v.look); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, "props_" + v.tag + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        RenderTexture.active = null;
        Debug.Log("MESHY PROPS captured to " + Path.GetFullPath(dir));
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
#endif
