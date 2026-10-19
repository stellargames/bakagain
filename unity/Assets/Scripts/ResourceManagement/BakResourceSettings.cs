namespace BakAgain.ResourceManagement {
    using System;
    using UnityEngine;

    [Serializable]
    public static class BakResourceSettings {
        private const string EnableOverridesKey = "enable overrides";
        private const string OverridesDirectoryKey = "overrides directory";
        private const string OriginalGamePathKey = "original game path";
        private const string LanguageKey = "language";

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

        private const string PendingKey = "next start";
        private const string PendingGamePathKey = "next start: " + OriginalGamePathKey;
        private const string PendingEnableOverridesKey = "next start: " + EnableOverridesKey;
        private const string PendingOverridesDirectoryKey = "next start: " + OverridesDirectoryKey;

        /// <summary>
        /// The folders the Enhanced settings chose "for the next start". They are kept aside rather than
        /// written over the settings: the running game read its folders at start, and a loader that
        /// reads them again later (a restored game's mod items, a language pack's images) would
        /// otherwise mix the old folder with the new one. <see cref="ApplyPending"/> makes them the
        /// settings, first thing at the next start.
        /// </summary>
        public static void SetForNextStart(string gamePath, bool overrideEnabled, string overridePath) {
            if (gamePath == GamePath && overrideEnabled == OverrideEnabled && overridePath == OverridePath) {
                PlayerPrefs.DeleteKey(PendingKey); // back to what is running: nothing waits
            } else {
                PlayerPrefs.SetString(PendingGamePathKey, gamePath ?? string.Empty);
                PlayerPrefs.SetInt(PendingEnableOverridesKey, overrideEnabled ? 1 : 0);
                PlayerPrefs.SetString(PendingOverridesDirectoryKey, overridePath ?? string.Empty);
                PlayerPrefs.SetInt(PendingKey, 1);
            }
            Save();
        }

        /// <summary>The folders the next start will use: the pending choice, or the settings.</summary>
        public static (string GamePath, bool OverrideEnabled, string OverridePath) NextStart =>
            PlayerPrefs.GetInt(PendingKey, 0) == 1
                ? (PlayerPrefs.GetString(PendingGamePathKey), PlayerPrefs.GetInt(PendingEnableOverridesKey, 0) == 1,
                    PlayerPrefs.GetString(PendingOverridesDirectoryKey))
                : (GamePath, OverrideEnabled, OverridePath);

        /// <summary>At start, before anything reads a folder: a pending choice becomes the settings.</summary>
        public static void ApplyPending() {
            if (PlayerPrefs.GetInt(PendingKey, 0) != 1) {
                return;
            }
            (string game, bool modsOn, string mods) = NextStart;
            PlayerPrefs.SetString(OriginalGamePathKey, game);
            PlayerPrefs.SetInt(EnableOverridesKey, modsOn ? 1 : 0);
            PlayerPrefs.SetString(OverridesDirectoryKey, mods);
            PlayerPrefs.DeleteKey(PendingKey);
            Save();
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

        /// <summary>
        /// The active language's locale (<c>en</c> = the original's English). A pack for any other
        /// locale is read from <c>&lt;OverridePath&gt;/Lang/&lt;locale&gt;/&lt;locale&gt;.po</c>; it takes
        /// effect on the next start (TASK-773).
        /// </summary>
        public static string Language {
            get => PlayerPrefs.GetString(LanguageKey, "en");
            set {
                PlayerPrefs.SetString(LanguageKey, value);
                Save();
            }
        }

        public static void Save() {
            PlayerPrefs.Save();
        }
    }
}