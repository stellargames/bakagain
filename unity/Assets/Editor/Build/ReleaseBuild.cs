namespace BakAgain.Editor.Build {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEditor.AddressableAssets.Settings;
    using UnityEditor.Build.Reporting;
    using UnityEngine;

    /// <summary>
    /// The release build the CI runs for every platform: <c>BakAgain.Editor.Build.ReleaseBuild.Run</c>,
    /// called by GameCI's unity-builder as its <c>buildMethod</c> for the active build target.
    /// </summary>
    /// <remarks>
    /// <para>Exists because the default builder does not build Addressables content here
    /// (<c>m_BuildAddressablesWithPlayerBuild</c> defers to per-user preferences), and a player
    /// without it cannot load a single prefab.</para>
    /// <para>Reads the arguments GameCI passes: <c>-customBuildPath</c>, <c>-buildVersion</c>
    /// (the tag, shown on the Preferences screen) and, for Android, <c>-androidVersionCode</c> and
    /// the keystore quartet. Exits non-zero on any failure so the workflow fails with it.</para>
    /// </remarks>
    public static class ReleaseBuild {
        public static void Run() {
            Dictionary<string, string> args = ParseArgs(Environment.GetCommandLineArgs());
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            try {
                if (args.TryGetValue("buildVersion", out string version)) {
                    PlayerSettings.bundleVersion = version;
                }
                if (target == BuildTarget.Android) {
                    ConfigureAndroid(args);
                }

                AddressableAssetSettings.CleanPlayerContent();
                AddressableAssetSettings.BuildPlayerContent(out var addressables);
                if (!string.IsNullOrEmpty(addressables.Error)) {
                    Fail("Addressables: " + addressables.Error);
                }

                if (!args.TryGetValue("customBuildPath", out string path)) {
                    Fail("-customBuildPath is required.");
                }
                if (target == BuildTarget.StandaloneLinux64 && Path.GetExtension(path) == "") {
                    path += ".x86_64"; // the name the release notes and website give
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = Array.ConvertAll(Array.FindAll(EditorBuildSettings.scenes, s => s.enabled), s => s.path),
                    locationPathName = path,
                    target = target,
                    targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                    options = BuildOptions.None,
                });
                Debug.Log($"ReleaseBuild: {target} {PlayerSettings.bundleVersion} -> {path}: {report.summary.result}, "
                    + $"{report.summary.totalErrors} errors, {report.summary.totalSize} bytes");
                if (report.summary.result != BuildResult.Succeeded) {
                    Fail("player build " + report.summary.result);
                }
            } catch (Exception e) {
                Debug.LogException(e);
                Fail(e.Message);
            }
            EditorApplication.Exit(0);
        }

        private static void ConfigureAndroid(Dictionary<string, string> args) {
            EditorUserBuildSettings.buildAppBundle = false; // an APK players can sideload
            if (args.TryGetValue("androidVersionCode", out string code) && int.TryParse(code, out int versionCode)) {
                PlayerSettings.Android.bundleVersionCode = versionCode;
            }
            if (args.TryGetValue("androidKeystoreName", out string keystore) && !string.IsNullOrEmpty(keystore)) {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystore;
                PlayerSettings.Android.keystorePass = args.GetValueOrDefault("androidKeystorePass");
                PlayerSettings.Android.keyaliasName = args.GetValueOrDefault("androidKeyaliasName");
                PlayerSettings.Android.keyaliasPass = args.GetValueOrDefault("androidKeyaliasPass");
            }
        }

        private static Dictionary<string, string> ParseArgs(string[] argv) {
            var args = new Dictionary<string, string>();
            for (int i = 0; i < argv.Length; i++) {
                if (argv[i].StartsWith("-") && i + 1 < argv.Length && !argv[i + 1].StartsWith("-")) {
                    args[argv[i].TrimStart('-')] = argv[i + 1];
                }
            }
            return args;
        }

        private static void Fail(string reason) {
            Debug.LogError("ReleaseBuild FAILED: " + reason);
            EditorApplication.Exit(1);
        }
    }
}
