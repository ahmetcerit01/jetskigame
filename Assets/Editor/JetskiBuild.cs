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

    // Release (frame times) and development (render counters) players that run RunnerBenchmark; kept apart from the real build.
    public static void MacBenchmark()
    {
        UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64ARM64;
        var defines = new[] { "JETSKI_BENCHMARK" };
        Build(BuildTarget.StandaloneOSX, "Build/MacBenchmark/Release/JetskiRunner.app", BuildOptions.None, defines);
        Build(BuildTarget.StandaloneOSX, "Build/MacBenchmark/Dev/JetskiRunner.app", BuildOptions.Development, defines);
    }

    // Xcode projects of the same benchmark players; signing and the bundle id are given to xcodebuild, not stored in the project.
    public static void IOSBenchmark()
    {
        var defines = new[] { "JETSKI_BENCHMARK" };
        Build(BuildTarget.iOS, "Build/iOSBenchmark/Release", BuildOptions.None, defines);
        Build(BuildTarget.iOS, "Build/iOSBenchmark/Dev", BuildOptions.Development, defines);
    }

    // Xcode project of the real game for a device.
    public static void IOS() => Build(BuildTarget.iOS, "Build/iOS", BuildOptions.None, null);

    static void Build(BuildTarget target, string path, BuildOptions options = BuildOptions.None, string[] defines = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = path,
            target = target,
            options = options,
            extraScriptingDefines = defines,
        });
        var s = report.summary;
        Debug.Log($"JETSKI BUILD {target}: {s.result}, {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors -> {path}");
        if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
    }
}
#endif
