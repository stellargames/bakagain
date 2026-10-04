namespace BakAgain.ResourceManagement {
    using System;
    using UnityEngine;

    [Serializable]
    public static class BakResourceSettings {
        private const string EnableOverridesKey = "enable overrides";
        private const string OverridesDirectoryKey = "overrides directory";
        private const string OriginalGamePathKey = "original game path";

        public static bool OverrideEnabled {
            get => PlayerPrefs.GetInt(EnableOverridesKey, 0) == 1;
            set {
                PlayerPrefs.SetInt(EnableOverridesKey, value ? 1 : 0);
                Save();
            }
        }

        public static string OverridePath {
            get => PlayerPrefs.GetString(OverridesDirectoryKey);
            set {
                PlayerPrefs.SetString(OverridesDirectoryKey, value);
                Save();
            }
        }

        public static string GamePath {
            get => PlayerPrefs.GetString(OriginalGamePathKey);
            set {
                PlayerPrefs.SetString(OriginalGamePathKey, value);
                Save();
            }
        }

        private static GameData.Resources.Config.ResourceConfig _resourceConfig;

        /// <summary>
        /// The game folder's RESOURCE.CFG, read once — the original parses it at startup
        /// (CFGPARSE.C). A missing file reads as the defaults.
        /// </summary>
        public static GameData.Resources.Config.ResourceConfig ResourceConfig =>
            _resourceConfig ??= GameData.Resources.Config.ResourceConfig.Parse(ReadGameFile("RESOURCE.CFG"));

        /// <summary>A file in the game folder, matched without regard to case (DOS names it in capitals).</summary>
        private static string ReadGameFile(string name) {
            try {
                string dir = GamePath;
                if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) {
                    return null;
                }
                foreach (string file in System.IO.Directory.EnumerateFiles(dir)) {
                    if (string.Equals(System.IO.Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase)) {
                        return System.IO.File.ReadAllText(file);
                    }
                }
            } catch (System.IO.IOException) {
            } catch (UnauthorizedAccessException) {
            }
            return null;
        }

        public static void Save() {
            PlayerPrefs.Save();
        }
    }
}