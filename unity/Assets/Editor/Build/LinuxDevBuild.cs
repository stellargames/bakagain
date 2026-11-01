namespace BakAgain.Editor.Build {
    using System.IO;
    using UnityEditor;
    using UnityEditor.AddressableAssets.Settings;
    using UnityEditor.Build.Reporting;
    using UnityEngine;

    /// <summary>
    /// Builds the Linux development player the build test harness drives:
    /// <c>BakAgain.Editor.Build.LinuxDevBuild.Run</c>.
    /// </summary>
    /// <remarks>
    /// Writes <c>Builds/Linux/BaK-Again.x86_64</c> and a one-line result to <c>Builds/Linux/result.txt</c>
    /// (first word <c>Succeeded</c>, or <c>FAILED</c>), then switches back to the target that was active.
    /// <para>Builds with Mono: the umachine's Editor has only the Mono Linux players installed, and the project's
    /// IL2CPP setting (for the release builds) disables the Linux build button. The setting is restored after.</para>
    /// </remarks>
    public static class LinuxDevBuild {
        public const string OutputDirectory = "Builds/Linux";

        [MenuItem("BaK/Build/Linux Dev")]
        public static void Run() {
            Directory.CreateDirectory(OutputDirectory);
            string resultPath = Path.Combine(OutputDirectory, "result.txt");
            File.WriteAllText(resultPath, "RUNNING\n");

            BuildTarget before = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup beforeGroup = BuildPipeline.GetBuildTargetGroup(before);
            var standalone = UnityEditor.Build.NamedBuildTarget.Standalone;
            ScriptingImplementation backend = PlayerSettings.GetScriptingBackend(standalone);
            try {
                PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
                if (before != BuildTarget.StandaloneLinux64) {
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64);
                }
                AddressableAssetSettings.CleanPlayerContent();
                AddressableAssetSettings.BuildPlayerContent(out var addressables);
                if (!string.IsNullOrEmpty(addressables.Error)) {
                    File.WriteAllText(resultPath, "FAILED addressables: " + addressables.Error + "\n");
                    return;
                }
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = System.Array.ConvertAll(
                        System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled), s => s.path),
                    locationPathName = Path.Combine(OutputDirectory, "BaK-Again.x86_64"),
                    target = BuildTarget.StandaloneLinux64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.Development,
                });
                string word = report.summary.result == BuildResult.Succeeded ? "Succeeded" : "FAILED " + report.summary.result;
                File.WriteAllText(resultPath, $"{word} errors={report.summary.totalErrors} "
                    + $"size={report.summary.totalSize} time={report.summary.totalTime}\n");
            } catch (System.Exception e) {
                File.WriteAllText(resultPath, "FAILED " + e + "\n");
                Debug.LogException(e);
            } finally {
                PlayerSettings.SetScriptingBackend(standalone, backend);
                AssetDatabase.SaveAssets(); // the build wrote Mono into ProjectSettings.asset; write the restore too
                if (EditorUserBuildSettings.activeBuildTarget != before) {
                    EditorUserBuildSettings.SwitchActiveBuildTarget(beforeGroup, before);
                }
            }
        }
    }
}
