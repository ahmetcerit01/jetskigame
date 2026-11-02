#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class JetskiMeshyIntegration
{
    const string CharacterModel = "Assets/Models/Meshy/MainCharacter/Bandana_Buccaneer.fbx";
    const string JetskiModel = "Assets/Models/Meshy/MainJetski/Crimson_Wave_Runner.fbx";
    const string PrefabDir = "Assets/Prefabs/Meshy/";
    static readonly string[] Scenes =
    {
        "Assets/Scenes/OceanRun.unity",
        "Assets/Scenes/PirateCove.unity",
        "Assets/Scenes/IceCave.unity"
    };

    [MenuItem("Jetski Runner/Build and Apply Meshy Models")]
    public static void BuildAndApply()
    {
        AssetDatabase.Refresh();
        EnsureFolder("Assets/Prefabs/Meshy");
        var character = BuildNormalizedPrefab("BandanaBuccaneer", CharacterModel, 1.45f, false);
        var jetski = BuildNormalizedPrefab("CrimsonWaveRunner", JetskiModel, 2.8f, true);
        var combo = BuildPlayerPrefab(character, jetski);
        AssetDatabase.SaveAssets();

        foreach (var path in Scenes)
        {
            var scene = EditorSceneManager.OpenScene(path);
            var game = UnityEngine.Object.FindFirstObjectByType<JetskiRunnerGame>();
            if (!game) throw new Exception(path + " has no JetskiRunnerGame");
            var so = new SerializedObject(game);
            var playerProperty = so.FindProperty("riderPrefab");
            if (playerProperty == null) throw new Exception("JetskiRunnerGame.riderPrefab was not found");
            playerProperty.objectReferenceValue = combo;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        var buildScenes = new EditorBuildSettingsScene[Scenes.Length];
        for (int i = 0; i < Scenes.Length; i++) buildScenes[i] = new EditorBuildSettingsScene(Scenes[i], true);
        EditorBuildSettings.scenes = buildScenes;
        Debug.Log("Meshy character and jetski prefabs created and applied to " + Scenes.Length + " runner scenes.");
    }

    static GameObject BuildNormalizedPrefab(string name, string modelPath, float targetDimension, bool orientLongAxisForward)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (!source) throw new Exception("Missing imported model: " + modelPath);

        var root = new GameObject(name);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);

        Bounds bounds = GetBounds(model);
        if (orientLongAxisForward && bounds.size.x > bounds.size.z)
        {
            model.transform.localRotation = Quaternion.Euler(0, -90, 0);
            bounds = GetBounds(model);
        }
        float dimension = orientLongAxisForward ? Mathf.Max(bounds.size.x, bounds.size.z) : bounds.size.y;
        if (dimension < .001f) throw new Exception("Invalid model dimensions: " + name);
        Debug.Log(name + " source bounds: " + bounds.size.ToString("F3"));
        float scale = targetDimension / dimension;
        model.transform.localScale = Vector3.one * scale;
        model.transform.localPosition = new Vector3(0, -bounds.min.y * scale, 0);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + name + ".prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject BuildPlayerPrefab(GameObject characterPrefab, GameObject jetskiPrefab)
    {
        var root = new GameObject("MeshyPirateJetskiPlayer");
        var jetski = (GameObject)PrefabUtility.InstantiatePrefab(jetskiPrefab);
        jetski.name = "Crimson Wave Runner";
        jetski.transform.SetParent(root.transform, false);

        var character = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab);
        character.name = "Bandana Buccaneer (static until rigged)";
        character.transform.SetParent(root.transform, false);
        character.transform.localPosition = new Vector3(0, .52f, -.28f);
        character.transform.localScale = Vector3.one * .82f;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "MeshyPirateJetskiPlayer.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static Bounds GetBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new Exception("Model has no renderers: " + root.name);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static void EnsureFolder(string path)
    {
        string current = "Assets";
        foreach (var part in path.Substring("Assets/".Length).Split('/'))
        {
            string next = current + "/" + part;
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part);
            current = next;
        }
    }
}
#endif
