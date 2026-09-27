namespace BakAgain.World.Rendering {
    using UnityEngine;

    /// <summary>
    /// Holds the player's selected render mode and creates the corresponding profile.
    /// Mode is set once at startup via PlayerPrefs.
    /// </summary>
    public class WorldRenderModeService {
        private const string PrefKey = "WorldRenderMode";

        public WorldRenderMode CurrentMode { get; private set; }

        public WorldRenderModeService() {
            int saved = PlayerPrefs.GetInt(PrefKey, (int)WorldRenderMode.Classic);
            CurrentMode = (WorldRenderMode)saved;
        }

        public void SetMode(WorldRenderMode mode) {
            CurrentMode = mode;
            PlayerPrefs.SetInt(PrefKey, (int)mode);
            PlayerPrefs.Save();
        }

        public IWorldRenderProfile CreateProfile() {
            return CurrentMode switch {
                WorldRenderMode.Enhanced => new EnhancedRenderProfile(),
                _ => new ClassicRenderProfile()
            };
        }
    }
}
