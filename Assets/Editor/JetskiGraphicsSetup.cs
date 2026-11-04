#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// References the post-processing shader and an emissive material from every runner scene, so both survive
// shader stripping in player builds.
//   Unity -batchmode -quit -projectPath . -executeMethod JetskiGraphicsSetup.Apply
public static class JetskiGraphicsSetup
{
    static readonly string[] Scenes = { "Assets/Scenes/OceanRun.unity", "Assets/Scenes/PirateCove.unity", "Assets/Scenes/IceCave.unity" };

    [MenuItem("Jetski Runner/Apply Graphics Setup")]
    public static void Apply()
    {
        var shader = Shader.Find("JetskiRunner/PostFX");
        if (!shader) throw new System.Exception("JetskiRunner/PostFX shader not found");
        Directory.CreateDirectory("Assets/Materials/FX");
        const string templatePath = "Assets/Materials/FX/EmissiveTemplate.mat";
        var template = AssetDatabase.LoadAssetAtPath<Material>(templatePath);
        if (!template) { template = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(template, templatePath); }
        template.EnableKeyword("_EMISSION");
        template.SetColor("_EmissionColor", Color.white);
        template.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        EditorUtility.SetDirty(template);
        AssetDatabase.SaveAssets();
        var shore = CookShoreModels();
        var tunnel = CookIceTunnel();
        KeepEmission(templatePath); KeepEmission("Assets/Materials/Shore/32-ice-crystals.mat");
        foreach (var path in Scenes)
        {
            if (!File.Exists(path)) continue;
            var scene = EditorSceneManager.OpenScene(path);
            var game = Object.FindFirstObjectByType<JetskiRunnerGame>();
            if (!game) continue;
            var so = new SerializedObject(game);
            so.FindProperty("postFxShader").objectReferenceValue = shader;
            if (tunnel) so.FindProperty("iceTunnelPrefab").objectReferenceValue = tunnel;
            var shoreList = so.FindProperty("shoreModels");
            shoreList.arraySize = shore.Count;
            for (int i = 0; i < shore.Count; i++) shoreList.GetArrayElementAtIndex(i).objectReferenceValue = shore[i];
            Debug.Log("Shore models wired: " + shore.Count);
            so.FindProperty("emissiveTemplate").objectReferenceValue = template;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene);
        }
        Debug.Log("GRAPHICS SETUP applied");
    }

    // The keyword has to be on the saved asset, or shader stripping drops the variant from player builds.
    static void KeepEmission(string path)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) return;
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        mat.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssetIfDirty(mat);
        Debug.Log("EMISSION KEEP " + path + ": " + string.Join(",", mat.shaderKeywords));
    }

    // The hollow, exactly tiling tunnel shell modelled by Tools/Blender/build_ice_tunnel.py.
    static GameObject CookIceTunnel()
    {
        const string glb = "Assets/Models/IceCave/IceTunnel.glb";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(glb);
        if (!model) return null;
        Directory.CreateDirectory("Assets/Prefabs/IceCave"); Directory.CreateDirectory("Assets/Materials/IceCave");
        var color = Texture("Assets/Art/IceCave/IceTunnel_color.png", 2048, false);
        var normal = Texture("Assets/Art/IceCave/IceTunnel_normal.png", 2048, true);
        const string matPath = "Assets/Materials/IceCave/IceTunnel.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (!mat) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, matPath); }
        mat.SetTexture("_MainTex", color); mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP");
        mat.SetFloat("_Glossiness", .62f); mat.SetFloat("_Metallic", 0f); mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        inst.name = "IceTunnel";
        var prefab = PrefabUtility.SaveAsPrefabAsset(inst, "Assets/Prefabs/IceCave/IceTunnel.prefab");
        Object.DestroyImmediate(inst);
        return prefab;
    }

    // A prefab per shore model: the GLB's mesh with a Standard material on compressed textures. Big scenery gets
    // more texture than small props, which never fill the screen.
    static List<GameObject> CookShoreModels()
    {
        const string prefabDir = "Assets/Prefabs/Meshy/Shore/", matDir = "Assets/Materials/Shore/", texDir = "Assets/Art/Shore/";
        Directory.CreateDirectory(prefabDir); Directory.CreateDirectory(matDir);
        var cooked = new List<GameObject>();
        foreach (var glb in Directory.GetFiles("Assets/Models/Meshy/Shore", "*.glb"))
        {
            string name = Path.GetFileNameWithoutExtension(glb);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(glb);
            var size = model.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.size;
            bool big = Mathf.Max(size.x, size.y, size.z) >= 4f;
            var color = Texture(texDir + name + "_color.jpg", big ? 2048 : 1024, false);
            var normal = Texture(texDir + name + "_normal.jpg", big ? 1024 : 512, true);
            string matPath = matDir + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_MainTex", color); mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP");
            bool ice = name.Contains("ice") || name.Contains("icicle") || name.Contains("glacier");
            mat.SetFloat("_Glossiness", ice ? .55f : .15f); mat.SetFloat("_Metallic", 0f); mat.enableInstancing = true;
            if (name.Contains("crystals"))
            {
                mat.EnableKeyword("_EMISSION"); mat.SetTexture("_EmissionMap", color); mat.SetColor("_EmissionColor", new Color(.75f, .75f, .75f));
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            EditorUtility.SetDirty(mat);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = name;
            cooked.Add(PrefabUtility.SaveAsPrefabAsset(inst, prefabDir + name + ".prefab"));
            Object.DestroyImmediate(inst);
        }
        AssetDatabase.SaveAssets();
        return cooked;
    }

    static Texture2D Texture(string path, int maxSize, bool normalMap)
    {
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.maxTextureSize = maxSize;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.anisoLevel = 2;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
#endif
