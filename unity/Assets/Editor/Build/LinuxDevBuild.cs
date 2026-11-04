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
    /// </remarks>
    public static class LinuxDevBuild {
        public const string OutputDirectory = "Builds/Linux";

        // Since 6000.6 a build started before the switch's script reload fails inside Addressables
        // ("Unable to build with the current configuration": the Linux build window extension reports
        // its button disabled until then; a delayCall still runs too early). So a switch ends this call
        // and the build resumes from the load hook. A platform switch always changes the platform
        // defines, so it always recompiles and reloads.
        const string PendingKey = "BakAgain.LinuxDevBuild.RestoreTarget";

        [InitializeOnLoadMethod]
        static void ResumeAfterReload() {
            if (SessionState.GetInt(PendingKey, -1) >= 0) EditorApplication.delayCall += Resume;
        }

        static void Resume() {
            int restore = SessionState.GetInt(PendingKey, -1);
            if (restore < 0) return;
            SessionState.EraseInt(PendingKey);
            Build((BuildTarget)restore);
        }

        [MenuItem("BaK/Build/Linux Dev")]
        public static void Run() {
            Directory.CreateDirectory(OutputDirectory);
            string resultPath = Path.Combine(OutputDirectory, "result.txt");
            File.WriteAllText(resultPath, "RUNNING\n");

            BuildTarget before = EditorUserBuildSettings.activeBuildTarget;
            if (before != BuildTarget.StandaloneLinux64) {
                SessionState.SetInt(PendingKey, (int)before);
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64);
                return;
            }
            Build(before);
        }

        static void Build(BuildTarget before) {
            string resultPath = Path.Combine(OutputDirectory, "result.txt");
            BuildTargetGroup beforeGroup = BuildPipeline.GetBuildTargetGroup(before);
            try {
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
                if (EditorUserBuildSettings.activeBuildTarget != before) {
                    EditorUserBuildSettings.SwitchActiveBuildTarget(beforeGroup, before);
                }
            }
        }
    }
}
