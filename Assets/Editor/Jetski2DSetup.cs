#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Jetski2DSetup
{
    [MenuItem("Jetski Runner/Build Illustrated Runner")]
    public static void Build()
    {
        string[] names = { "TropicalOcean", "JetskiPlayer", "PirateShip", "AnchorCoin", "Barrel", "SeaRocks", "FuelCan" };
        string[] paths = {
            "Assets/Art/Backgrounds/TropicalOcean.png",
            "Assets/Art/Sprites/JetskiPlayer.png",
            "Assets/Art/Sprites/PirateShip.png",
            "Assets/Art/Sprites/AnchorCoin.png",
            "Assets/Art/Sprites/Barrel.png",
            "Assets/Art/Sprites/SeaRocks.png",
            "Assets/Art/Sprites/FuelCan.png"
        };
        foreach (string path in paths)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new System.IO.FileNotFoundException("Missing artwork", path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera");
        cam.tag = "MainCamera";
        cam.AddComponent<Camera>();
        cam.AddComponent<AudioListener>();
        var runner = new GameObject("Illustrated Jetski Runner").AddComponent<JetskiRunner2D>();
        runner.ocean = AssetDatabase.LoadAssetAtPath<Sprite>(paths[0]);
        runner.rider = AssetDatabase.LoadAssetAtPath<Sprite>(paths[1]);
        runner.pirateShip = AssetDatabase.LoadAssetAtPath<Sprite>(paths[2]);
        runner.coin = AssetDatabase.LoadAssetAtPath<Sprite>(paths[3]);
        runner.barrel = AssetDatabase.LoadAssetAtPath<Sprite>(paths[4]);
        runner.rocks = AssetDatabase.LoadAssetAtPath<Sprite>(paths[5]);
        runner.fuel = AssetDatabase.LoadAssetAtPath<Sprite>(paths[6]);
        const string scenePath = "Assets/Scenes/IllustratedOceanRun.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
        EditorSceneManager.OpenScene(scenePath);
        Debug.Log("Illustrated runner ready. Open IllustratedOceanRun and press Play.");
    }

    public static void BuildPreview()
    {
        Build();
        System.IO.Directory.CreateDirectory("Build/WindowsPreview");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/IllustratedOceanRun.unity" },
            locationPathName = "Build/WindowsPreview/JetskiRunner.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception("Preview build failed: " + report.summary.result);
    }
}
#endif
