#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;
using UnityEngine.SceneManagement;

public static class JetskiSceneSetup
{
    [MenuItem("Jetski Runner/Build Environment Scenes")]
    public static void CreateScene()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        var scenePaths = new[] { "Assets/Scenes/OceanRun.unity", "Assets/Scenes/PirateCove.unity", "Assets/Scenes/IceCave.unity" };
        for (int i=0;i<scenePaths.Length;i++)
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Jetski Runner Game");
            var game=root.AddComponent<JetskiRunnerGame>();
            var serialized=new SerializedObject(game);
            serialized.FindProperty("environment").enumValueIndex=i;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (!EditorSceneManager.SaveScene(scene,scenePaths[i])) throw new System.IO.IOException("Could not save " + scenePaths[i]);
            if(i==0) Selection.activeGameObject=root;
        }
        EditorBuildSettings.scenes=new[]
        {
            new EditorBuildSettingsScene(scenePaths[0],true),
            new EditorBuildSettingsScene(scenePaths[1],true),
            new EditorBuildSettingsScene(scenePaths[2],true)
        };
        Debug.Log("Built Ocean Run, Pirate Cove, and Ice Cave. Open OceanRun and press Play.");
    }

    public static void BuildWindowsPreview()
    {
        string[] scenes={"Assets/Scenes/OceanRun.unity","Assets/Scenes/PirateCove.unity","Assets/Scenes/IceCave.unity"};
        foreach(string scene in scenes) if(!File.Exists(scene)) throw new FileNotFoundException("Build the scenes first",scene);
        Directory.CreateDirectory("Build/WindowsPreview");
        var options=new BuildPlayerOptions
        {
            scenes=scenes,
            locationPathName="Build/WindowsPreview/JetskiRunner.exe",
            target=BuildTarget.StandaloneWindows64,
            options=BuildOptions.None
        };
        var report=BuildPipeline.BuildPlayer(options);
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception("Windows preview build failed: "+report.summary.result);
        Debug.Log("Windows preview built at Build/WindowsPreview/JetskiRunner.exe");
    }
}
#endif
