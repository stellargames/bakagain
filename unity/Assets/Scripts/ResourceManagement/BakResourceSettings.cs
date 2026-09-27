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

        public static void Save() {
            PlayerPrefs.Save();
        }
    }
}