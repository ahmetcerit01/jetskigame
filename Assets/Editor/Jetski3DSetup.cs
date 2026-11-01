#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Jetski3DSetup
{
    [MenuItem("Jetski Runner/Activate 3D Ocean Prototype")]
    public static void Activate()
    {
        const string path = "Assets/Scenes/OceanRun.unity";
        var scene = EditorSceneManager.OpenScene(path);
        var game = Object.FindFirstObjectByType<JetskiRunnerGame>();
        if (!game) throw new System.Exception("OceanRun scene has no JetskiRunnerGame");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/TropicalWater.shader");
        if (!shader) throw new System.Exception("TropicalWater shader missing");
        var serialized = new SerializedObject(game);
        serialized.FindProperty("tropicalWaterShader").objectReferenceValue = shader;
        var coin = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/AnchorCoin.fbx");
        if (!coin) throw new System.Exception("Blender anchor coin FBX missing");
        serialized.FindProperty("goldPrefab").objectReferenceValue = MakePrefab(coin,"AnchorCoin");
        var rider=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/JetskiRider.fbx");
        if(!rider) throw new System.Exception("JetskiRider FBX missing");
        serialized.FindProperty("riderPrefab").objectReferenceValue = MakePrefab(rider,"JetskiRider");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
        Debug.Log("3D ocean prototype active: " + path);
    }

    public static void BuildPreview()
    {
        Activate();
        System.IO.Directory.CreateDirectory("Build/ThreeDPreview");
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/OceanRun.unity" },
            locationPathName = "Build/ThreeDPreview/JetskiRunner3D.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception("3D preview failed: " + result.summary.result);
    }
    static GameObject MakePrefab(GameObject source,string name)
    {
        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        System.IO.Directory.CreateDirectory("Assets/Materials");
        AssetDatabase.Refresh();
        var root=Object.Instantiate(source);
        foreach(var renderer in root.GetComponentsInChildren<Renderer>())
        {
            var materials=renderer.sharedMaterials;
            for(int i=0;i<materials.Length;i++)
            {
                string key=materials[i] ? materials[i].name : "Hull";
                Color color=new Color(.025f,.045f,.065f);
                if(key.Contains("Turquoise")) color=new Color(.015f,.72f,.76f);
                else if(key.Contains("Orange")) color=new Color(1,.24f,.015f);
                else if(key.Contains("Skin")) color=new Color(.66f,.32f,.13f);
                else if(key.Contains("Hair")) color=new Color(.095f,.035f,.012f);
                else if(key.Contains("Silver")) color=new Color(.42f,.52f,.56f);
                else if(key.Contains("Seat")) color=new Color(.065f,.075f,.09f);
                else if(key.Contains("Gold")) color=key.Contains("highlight") ? new Color(1,.78f,.08f) : key.Contains("inset") ? new Color(.70f,.27f,.015f) : new Color(1,.48f,.02f);
                string path="Assets/Materials/"+key.Replace(" ","_")+".mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!mat) { mat=new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat,path); }
                mat.color=color; mat.SetFloat("_Metallic",key.Contains("Gold") ? .45f : .05f); mat.SetFloat("_Glossiness",.4f);
                EditorUtility.SetDirty(mat); materials[i]=mat;
            }
            renderer.sharedMaterials=materials;
        }
        var prefab=PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/"+name+".prefab");
        Object.DestroyImmediate(root); AssetDatabase.SaveAssets(); return prefab;
    }
}
#endif
