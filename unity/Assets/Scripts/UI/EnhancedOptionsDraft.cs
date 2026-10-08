namespace BakAgain.UI {
    using System.Collections.Generic;
    using BakAgain.Core;

    /// <summary>The Preferences screen's working copy of the Enhanced options: saved on OK only.</summary>
    public sealed class EnhancedOptionsDraft {
        private readonly Dictionary<EnhancedFeature, bool> _features = new();

        public bool Master { get; set; }

        public bool this[EnhancedFeature feature] {
            get => _features[feature];
            set => _features[feature] = value;
        }

        public static EnhancedOptionsDraft Load() {
            var draft = new EnhancedOptionsDraft { Master = GameOptions.Enhanced };
            foreach (EnhancedFeature f in System.Enum.GetValues(typeof(EnhancedFeature)))
                draft._features[f] = GameOptions.GetFeature(f);
            return draft;
        }

        public void Commit() {
            GameOptions.Enhanced = Master;
            foreach (KeyValuePair<EnhancedFeature, bool> kv in _features)
                GameOptions.SetFeature(kv.Key, kv.Value);
        }
    }
}
