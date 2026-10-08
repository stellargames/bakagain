namespace BakAgain.Tests {
    using System.Collections.Generic;
    using BakAgain.Core;
    using UnityEngine;

    /// <summary>
    /// The Enhanced-mode keys are the owner's real, persisted PlayerPrefs: a test that clears them
    /// stashes them first and puts them back after, so a test run never changes the player's options.
    /// </summary>
    public sealed class EnhancedPrefsStash {
        private readonly Dictionary<string, int?> _saved = new();

        public static IEnumerable<string> Keys {
            get {
                yield return "enhanced";
                foreach (EnhancedFeature f in System.Enum.GetValues(typeof(EnhancedFeature)))
                    yield return $"enhanced {f}";
            }
        }

        /// <summary>Remembers every key (present or not) and clears them for the test.</summary>
        public static EnhancedPrefsStash Take() {
            var stash = new EnhancedPrefsStash();
            foreach (string k in Keys) {
                stash._saved[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : null;
                PlayerPrefs.DeleteKey(k);
            }
            return stash;
        }

        public void Restore() {
            foreach (KeyValuePair<string, int?> kv in _saved) {
                if (kv.Value.HasValue) PlayerPrefs.SetInt(kv.Key, kv.Value.Value);
                else PlayerPrefs.DeleteKey(kv.Key);
            }
            PlayerPrefs.Save();
        }
    }
}
