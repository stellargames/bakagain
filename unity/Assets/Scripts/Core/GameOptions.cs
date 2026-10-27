namespace BakAgain.Core {
    using UnityEngine;

    /// <summary>
    /// The player's persisted display options — the Unity home for what <c>LoadConfig</c> reads out
    /// of the original's config block.
    /// </summary>
    /// <remarks>
    /// These are OPTIONS, not save state: they belong to the player and outlive any one game, which
    /// is why they sit in <see cref="PlayerPrefs"/> rather than in <see cref="GameSession"/>. Putting one in the save would make it travel with a shared save
    /// file and reset itself when the player loaded someone else's.
    /// </remarks>
    public static class GameOptions {
        private const string NorthUpMapKey = "map north up";

        /// <summary>
        /// Whether the overhead map holds north at the top instead of turning with the party —
        /// <c>bool_NonRotatingMap</c>, toggled by 'N' on the map screen (CD build only).
        /// </summary>
        /// <remarks>
        /// <b>Defaults to RESOURCE.CFG's <c>nonrotatingmap</c></b>, which the shipped CD install sets
        /// to 1 — so the original, as players ran it, held north up. A folder without the key falls
        /// back to north up as well (owner, 2026-10-05). The player's own toggle, once made, wins.
        /// What the flag then changes is split across two places on
        /// purpose — see <c>LocalMapScreen.MapRendersWithYaw</c> for the camera and
        /// <c>OverheadMapMarker.IconIndexFor</c> for the marker — because the heading goes into
        /// exactly one of them.
        /// </remarks>
        public static bool NorthUpMap {
            get => PlayerPrefs.HasKey(NorthUpMapKey)
                ? PlayerPrefs.GetInt(NorthUpMapKey, 0) == 1
                : BakAgain.ResourceManagement.BakResourceSettings.ResourceConfig.NonRotatingMap ?? true;
            set {
                PlayerPrefs.SetInt(NorthUpMapKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        private const string EnhancedKey = "enhanced";

        /// <summary>
        /// The Enhanced-mode master switch. Off by default: a new player gets the faithful game.
        /// Only exactly 1 reads as on, so a corrupt value falls back to faithful.
        /// </summary>
        public static bool Enhanced {
            get => PlayerPrefs.GetInt(EnhancedKey, 0) == 1;
            set => Store(EnhancedKey, value);
        }

        /// <summary>A feature's own switch; defaults on, so the master alone turns on the full set.</summary>
        public static bool GetFeature(EnhancedFeature feature) =>
            PlayerPrefs.GetInt(FeatureKey(feature), 1) == 1;

        public static void SetFeature(EnhancedFeature feature, bool on) => Store(FeatureKey(feature), on);

        /// <summary>The one check every Enhanced call site makes.</summary>
        public static bool IsOn(EnhancedFeature feature) => Enhanced && GetFeature(feature);

        // Built once: the lighting asks for two features every frame. Values are not cached — tests and
        // the settings screen write PlayerPrefs directly.
        private static readonly string[] FeatureKeys = System.Array.ConvertAll(
            (EnhancedFeature[])System.Enum.GetValues(typeof(EnhancedFeature)), f => $"{EnhancedKey} {f}");

        private static string FeatureKey(EnhancedFeature feature) => FeatureKeys[(int)feature];

        private const string ShadowsKey = "enhanced shadows";

        /// <summary>The player's shadow choice; until one is made, Sun on desktop and Blobs on phones,
        /// where a shadow map costs too much.</summary>
        public static ShadowMode Shadows {
            get {
                int v = PlayerPrefs.GetInt(ShadowsKey, -1);
                return v is >= (int)ShadowMode.Off and <= (int)ShadowMode.Sun ? (ShadowMode)v : DefaultShadows;
            }
            set {
                PlayerPrefs.SetInt(ShadowsKey, (int)value);
                PlayerPrefs.Save();
            }
        }

        public static ShadowMode DefaultShadows => Application.isMobilePlatform ? ShadowMode.Blobs : ShadowMode.Sun;

        /// <summary>The shadows to draw: the choice under the master switch, Off in the faithful game.</summary>
        public static ShadowMode ShadowsShown => Enhanced ? Shadows : ShadowMode.Off;

        private const string CheckForUpdatesKey = "check for updates";

        /// <summary>Whether the main menu asks bakagain.org for a newer release (TASK-1017). On by default.</summary>
        public static bool CheckForUpdates {
            get => PlayerPrefs.GetInt(CheckForUpdatesKey, 1) == 1;
            set => Store(CheckForUpdatesKey, value);
        }

        private static void Store(string key, bool value) {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
