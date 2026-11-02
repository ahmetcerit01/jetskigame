#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Desktop builds of the runner scenes.
//   Unity -batchmode -quit -projectPath . -executeMethod JetskiBuild.Windows
//   Unity -batchmode -quit -projectPath . -executeMethod JetskiBuild.Mac
public static class JetskiBuild
{
    static string[] Scenes => EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

    [MenuItem("Jetski Runner/Build/Windows (.exe)")]
    public static void Windows() => Build(BuildTarget.StandaloneWindows64, "Build/Windows/JetskiRunner.exe");

    [MenuItem("Jetski Runner/Build/macOS (.app)")]
    public static void Mac()
    {
        UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64ARM64;
        Build(BuildTarget.StandaloneOSX, "Build/Mac/JetskiRunner.app");
    }

    static void Build(BuildTarget target, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = path,
            target = target,
            options = BuildOptions.None,
        });
        var s = report.summary;
        Debug.Log($"JETSKI BUILD {target}: {s.result}, {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors -> {path}");
        if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
    }
}
#endif
