namespace BakAgain.ResourceManagement {
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Xml.Linq;
    using UnityEngine;

    /// <summary>
    /// Brings a Linux player's settings over from the PlayerPrefs file older builds used (TASK-1025).
    /// </summary>
    /// <remarks>
    /// Players built with Unity 6000.5 and earlier wrote PlayerPrefs to unity3d/unknown/unknown/prefs;
    /// 6000.6 uses unity3d/StellarGameStudio/BaK-Again, as Unity documents (no release note mentions the
    /// change). Without this, a player updating from v0.5 meets the setup screen again. The old file is
    /// shared by every Unity game that had the fault, so only this game's keys come across.
    /// </remarks>
    public static class LegacyPrefs {
        private const string GamePathKey = "original game path";

        private static readonly HashSet<string> Keys = new() {
            GamePathKey, "enable overrides", "overrides directory", "language", "map north up", "enhanced",
            "check for updates",
        };

        private static bool IsOurs(string key) => Keys.Contains(key) || key.StartsWith("enhanced ", StringComparison.Ordinal);

#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ImportOnce() {
            if (PlayerPrefs.HasKey(GamePathKey)) {
                return;
            }
            string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(config)) {
                config = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".config");
            }
            string path = Path.Combine(config, "unity3d", "unknown", "unknown", "prefs");
            int count = 0;
            foreach (var (key, value) in Read(path)) {
                switch (value) {
                    case int i: PlayerPrefs.SetInt(key, i); break;
                    case float f: PlayerPrefs.SetFloat(key, f); break;
                    case string s: PlayerPrefs.SetString(key, s); break;
                }
                count++;
            }
            if (count > 0) {
                PlayerPrefs.Save();
                Debug.Log($"[LegacyPrefs] Imported {count} settings from {path}");
            }
        }
#endif

        /// <summary>This game's entries in a Unity Linux prefs file; empty when there is no file.</summary>
        public static IEnumerable<KeyValuePair<string, object>> Read(string path) {
            if (!File.Exists(path)) {
                yield break;
            }
            XElement root;
            try {
                root = XElement.Load(path);
            } catch (Exception e) when (e is System.Xml.XmlException || e is IOException) {
                Debug.LogWarning($"[LegacyPrefs] Could not read {path}: {e.Message}");
                yield break;
            }
            foreach (var pref in root.Elements("pref")) {
                string key = (string)pref.Attribute("name");
                if (key == null || !IsOurs(key)) {
                    continue;
                }
                string raw = pref.Value;
                switch ((string)pref.Attribute("type")) {
                    case "int" when int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i):
                        yield return new(key, i);
                        break;
                    case "float" when float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f):
                        yield return new(key, f);
                        break;
                    case "string" when TryDecode(raw, out string s):
                        yield return new(key, s);
                        break;
                }
            }
        }

        private static bool TryDecode(string base64, out string text) {
            try {
                text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                return true;
            } catch (FormatException) {
                text = null;
                return false;
            }
        }
    }
}
