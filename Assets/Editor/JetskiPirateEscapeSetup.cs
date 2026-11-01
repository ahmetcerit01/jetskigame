#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Imports the "Jetski Pirate Escape" Blender pack (Assets/Models/PirateEscape) into game-ready prefabs
// and wires them into every runner scene. Blender exports bow = +Y, which lands on Unity -Z, so every
// model is wrapped in a root that turns it 180° to face the run direction.
public static class JetskiPirateEscapeSetup
{
    const string ModelDir = "Assets/Models/PirateEscape/";
    const string PrefabDir = "Assets/Prefabs/PirateEscape/";
    const string MaterialDir = "Assets/Materials/PirateEscape/";
    static readonly string[] Scenes = { "Assets/Scenes/OceanRun.unity", "Assets/Scenes/PirateCove.unity", "Assets/Scenes/IceCave.unity" };

    // Blender materials that were procedural (wood, rock, cloth) arrive as plain grey; give them their look back.
    static readonly Dictionary<string, Color> ColorOverrides = new()
    {
        { "Barrel_Lid_Planks", new Color(.55f,.33f,.16f) }, { "Barrel_Staves_Oak", new Color(.45f,.24f,.10f) },
        { "Log_Bark", new Color(.27f,.16f,.08f) }, { "Log_End_Grain", new Color(.78f,.58f,.36f) },
        { "Ramp_Deck_Planks", new Color(.66f,.46f,.27f) }, { "Ramp_Beam_Wood", new Color(.36f,.21f,.10f) },
        { "Ship_Spar_Wood", new Color(.33f,.19f,.09f) }, { "Ship_Hull_Planks", new Color(.30f,.16f,.07f) },
        { "Ship_Deck_Planks", new Color(.55f,.37f,.20f) }, { "Rock_Wet_Basalt", new Color(.23f,.25f,.27f) },
        { "Rider_LifeVest", new Color(1f,.30f,.06f) }, { "Rider_Boardshorts", new Color(.08f,.20f,.55f) },
        { "Rider_Rashguard", new Color(.10f,.13f,.18f) }, { "Ocean_Glacial", new Color(.02f,.22f,.30f) },
    };

    [MenuItem("Jetski Runner/Apply Pirate Escape Assets")]
    public static void Apply()
    {
        AssetDatabase.Refresh();
        System.IO.Directory.CreateDirectory(PrefabDir);
        System.IO.Directory.CreateDirectory(MaterialDir);
        AssetDatabase.Refresh();

        var player = MakePrefab("PirateEscapeJetski", .8f, "Jetski", "Jetski_Rider");
        var coin = MakePrefab("GoldCoin", .85f, "Gold_Coin");
        var fuel = MakePrefab("BoostFuelCan", .9f, "Boost_Fuel_Can");
        var ramp = MakePrefab("JumpRamp", .7f, "Jump_Ramp");
        var ship = MakePrefab("PirateShip", .45f, "Pirate_Ship");
        var tunnel = MakePrefab("IceTunnelSegment", 1f, "Ice_Tunnel_Segment");
        var obstacles = new[]
        {
            MakePrefab("FloatingBarrel", .85f, "Floating_Barrel"),
            MakePrefab("FloatingLog", .7f, "Floating_Log"),
            MakePrefab("SeaMine", .75f, "Sea_Mine"),
            MakePrefab("SeaRock", .5f, "Sea_Rock_Foam"),
        };
        AssetDatabase.SaveAssets();

        foreach (var path in Scenes)
        {
            var scene = EditorSceneManager.OpenScene(path);
            var game = Object.FindFirstObjectByType<JetskiRunnerGame>();
            if (!game) throw new System.Exception(path + " has no JetskiRunnerGame");
            var so = new SerializedObject(game);
            so.FindProperty("tropicalWaterShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/TropicalWater.shader");
            so.FindProperty("riderPrefab").objectReferenceValue = player;
            so.FindProperty("goldPrefab").objectReferenceValue = coin;
            so.FindProperty("fuelPrefab").objectReferenceValue = fuel;
            so.FindProperty("rampPrefab").objectReferenceValue = ramp;
            so.FindProperty("pirateShipPrefab").objectReferenceValue = ship;
            so.FindProperty("iceTunnelPrefab").objectReferenceValue = tunnel;
            so.FindProperty("sideRockPrefab").objectReferenceValue = obstacles[3];
            so.FindProperty("dockBarrelPrefab").objectReferenceValue = obstacles[0];
            so.FindProperty("skyShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/SkyGradient.shader");
            so.FindProperty("sprayMaterial").objectReferenceValue = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
            so.FindProperty("laneWidth").floatValue = 2.9f;
            var list = so.FindProperty("obstaclePrefabs");
            list.arraySize = obstacles.Length;
            for (int i = 0; i < obstacles.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = obstacles[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        var buildScenes = new EditorBuildSettingsScene[Scenes.Length];
        for (int i = 0; i < Scenes.Length; i++) buildScenes[i] = new EditorBuildSettingsScene(Scenes[i], true);
        EditorBuildSettings.scenes = buildScenes;
        Debug.Log("Pirate Escape assets applied to " + Scenes.Length + " scenes");
    }

    public static void BuildPreview()
    {
        Apply();
        System.IO.Directory.CreateDirectory("Build/PirateEscapePreview");
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = Scenes,
            locationPathName = "Build/PirateEscapePreview/JetskiPirateEscape.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception("Pirate Escape preview failed: " + result.summary.result);
    }

    static GameObject MakePrefab(string name, float scale, params string[] models)
    {
        var root = new GameObject(name);
        var pivot = new GameObject("Model").transform;
        pivot.SetParent(root.transform, false);
        pivot.localRotation = Quaternion.Euler(0, 180, 0);
        pivot.localScale = Vector3.one * scale;
        foreach (var model in models)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + model + ".fbx");
            if (!source) throw new System.Exception("Missing model " + ModelDir + model + ".fbx");
            var instance = Object.Instantiate(source, pivot, false);
            instance.name = model;
        }
        foreach (var light in root.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(light.gameObject);
        foreach (var camera in root.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(camera.gameObject);
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            bool mist = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] && materials[i].name.Contains("Mist")) mist = true;
                materials[i] = ConvertMaterial(materials[i]);
            }
            if (mist) { Object.DestroyImmediate(renderer.gameObject); continue; }
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + name + ".prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static Material ConvertMaterial(Material imported)
    {
        string key = imported ? imported.name : "Default";
        string path = MaterialDir + key + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
        Color color = ColorOverrides.TryGetValue(key, out var c) ? c : imported ? imported.color : Color.grey;
        color.a = 1;
        bool metal = key.Contains("Gold") || key.Contains("Steel") || key.Contains("Iron") || key.Contains("Buckle");
        bool glow = key.Contains("Glow") || key.Contains("Bolt");
        mat.color = color;
        mat.SetFloat("_Metallic", key.Contains("Gold") ? .7f : metal ? .55f : 0f);
        mat.SetFloat("_Glossiness", key.Contains("Gold") || key.Contains("Paint") || key.Contains("Ice") || key.Contains("Glass") ? .72f : .35f);
        if (glow) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", color * .9f); }
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
#endif
