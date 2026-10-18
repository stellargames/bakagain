namespace BakAgain.Tests.Editor.UI {
    using System.Linq;
    using BakAgain.Core;
    using BakAgain.UI;
    using NUnit.Framework;

    /// <summary>A feature with no row on the Enhanced panel can never be switched off by the player.</summary>
    public class EnhancedPanelRowsTests {
        [Test]
        public void EveryFeatureHasARow() {
            var rows = EnhancedSettingsScreen.FeatureRows.Select(r => r.Feature).ToList();
            foreach (EnhancedFeature f in System.Enum.GetValues(typeof(EnhancedFeature))) {
                Assert.That(rows, Has.Exactly(1).EqualTo(f), $"{f} needs exactly one row");
            }
        }
    }
}
