namespace BakAgain.Core {
    using UnityEngine;

    /// <summary>
    /// The player's persisted display options — the Unity home for what <c>LoadConfig</c> reads out
    /// of the original's config block.
    /// </summary>
    /// <remarks>
    /// These are OPTIONS, not save state: they belong to the player and outlive any one game, which
    /// is why they sit in <see cref="PlayerPrefs"/> next to
    /// <see cref="BakAgain.World.Rendering.WorldRenderModeService"/> rather than in
    /// <see cref="GameSession"/>. Putting one in the save would make it travel with a shared save
    /// file and reset itself when the player loaded someone else's.
    /// </remarks>
    public static class GameOptions {
        private const string NorthUpMapKey = "map north up";

        /// <summary>
        /// Whether the overhead map holds north at the top instead of turning with the party —
        /// <c>bool_NonRotatingMap</c>, toggled by 'N' on the map screen (CD build only).
        /// </summary>
        /// <remarks>
        /// Off by default, matching the original's own default: the map turns with the party unless
        /// the player says otherwise. What the flag then changes is split across two places on
        /// purpose — see <c>LocalMapScreen.MapRendersWithYaw</c> for the camera and
        /// <c>OverheadMapMarker.IconIndexFor</c> for the marker — because the heading goes into
        /// exactly one of them.
        /// </remarks>
        public static bool NorthUpMap {
            get => PlayerPrefs.GetInt(NorthUpMapKey, 0) == 1;
            set {
                PlayerPrefs.SetInt(NorthUpMapKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}
