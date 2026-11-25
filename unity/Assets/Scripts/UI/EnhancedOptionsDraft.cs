namespace BakAgain.UI {
    using System.Collections.Generic;
    using BakAgain.Core;

    /// <summary>The Enhanced settings screen's working copy of the Enhanced options: saved on Save only.</summary>
    public sealed class EnhancedOptionsDraft {
        private readonly Dictionary<EnhancedFeature, bool> _features = new();

        public bool Master { get; set; }

        public ShadowMode Shadows { get; set; }

        public bool CheckForUpdates { get; set; }

        public bool RestoreShopBuybackBug { get; set; }

        public GameData.Resources.Combat.CombatSpeed CombatSpeed { get; set; }

        public bool this[EnhancedFeature feature] {
            get => _features[feature];
            set => _features[feature] = value;
        }

        public static EnhancedOptionsDraft Load() {
            var draft = new EnhancedOptionsDraft { Master = GameOptions.Enhanced, Shadows = GameOptions.Shadows,
                CheckForUpdates = GameOptions.CheckForUpdates,
                RestoreShopBuybackBug = GameOptions.RestoreShopBuybackBug, CombatSpeed = GameOptions.CombatSpeed };
            foreach (EnhancedFeature f in System.Enum.GetValues(typeof(EnhancedFeature)))
                draft._features[f] = GameOptions.GetFeature(f);
            return draft;
        }

        public void Commit() {
            GameOptions.Enhanced = Master;
            GameOptions.Shadows = Shadows;
            GameOptions.CheckForUpdates = CheckForUpdates;
            GameOptions.RestoreShopBuybackBug = RestoreShopBuybackBug;
            GameOptions.CombatSpeed = CombatSpeed;
            foreach (KeyValuePair<EnhancedFeature, bool> kv in _features)
                GameOptions.SetFeature(kv.Key, kv.Value);
        }
    }
}
