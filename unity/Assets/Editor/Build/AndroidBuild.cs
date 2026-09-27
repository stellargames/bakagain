namespace BakAgain.Editor.Build {
    using System.IO;
    using UnityEditor;
    using UnityEditor.AddressableAssets.Settings;
    using UnityEditor.Build.Reporting;
    using UnityEngine;

    /// <summary>
    /// Builds the Android player: <c>BakAgain.Editor.Build.AndroidBuild.Run</c>.
    /// </summary>
    /// <remarks>
    /// Writes <c>Builds/Android/BaK-Again.apk</c> and a one-line result to
    /// <c>Builds/Android/result.txt</c>. ARM64 only: the 6000.5 Android module ships no x86_64
    /// player, so an x86_64 emulator runs it through its ARM translation (API 36+ images; API 34's
    /// translator lacks the SEVL instruction Unity's mutex uses).
    /// </remarks>
    public static class AndroidBuild {
        public const string OutputDirectory = "Builds/Android";

        [MenuItem("BaK/Build/Android APK")]
        public static void Run() {
            Directory.CreateDirectory(OutputDirectory);
            string resultPath = Path.Combine(OutputDirectory, "result.txt");
            File.WriteAllText(resultPath, "RUNNING\n");

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            }

            try {
                AddressableAssetSettings.CleanPlayerContent();
                AddressableAssetSettings.BuildPlayerContent(out var addressables);
                if (!string.IsNullOrEmpty(addressables.Error)) {
                    File.WriteAllText(resultPath, "FAILED addressables: " + addressables.Error + "\n");
                    return;
                }

                var options = new BuildPlayerOptions {
                    scenes = System.Array.ConvertAll(
                        System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled), s => s.path),
                    locationPathName = Path.Combine(OutputDirectory, "BaK-Again.apk"),
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.Development,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                File.WriteAllText(resultPath, $"{report.summary.result} errors={report.summary.totalErrors} "
                    + $"size={report.summary.totalSize} time={report.summary.totalTime}\n");
            } catch (System.Exception e) {
                File.WriteAllText(resultPath, "FAILED " + e + "\n");
                Debug.LogException(e);
            }
        }
    }
}
