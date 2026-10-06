namespace BakAgain.Core {
    /// <summary>
    /// The line a bug report needs: which build, and where its log is. Shown on the Preferences
    /// screen.
    /// </summary>
    public static class BuildInfo {
        /// <param name="version">The player's version — the release tag, stamped into
        /// <c>bundleVersion</c> by the release build.</param>
        /// <param name="logPath">Unity's player log (<c>Application.consoleLogPath</c>). It carries
        /// everything the game logs. Empty on Android, where the log goes to logcat.</param>
        public static string Describe(string version, string logPath) {
            string log = string.IsNullOrEmpty(logPath) ? "adb logcat -s Unity" : logPath;
            return GameData.Resources.Text.UiTemplates.Format(GameData.Resources.Text.UiTemplates.BuildInfoKey,
                ("version", version), ("log", log));
        }
    }
}
