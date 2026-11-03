#if UNITY_EDITOR
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
        foreach (var path in Scenes)
        {
            if (!File.Exists(path)) continue;
            var scene = EditorSceneManager.OpenScene(path);
            var game = Object.FindFirstObjectByType<JetskiRunnerGame>();
            if (!game) continue;
            var so = new SerializedObject(game);
            so.FindProperty("postFxShader").objectReferenceValue = shader;
            so.FindProperty("emissiveTemplate").objectReferenceValue = template;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene);
        }
        Debug.Log("GRAPHICS SETUP applied");
    }
}
#endif
