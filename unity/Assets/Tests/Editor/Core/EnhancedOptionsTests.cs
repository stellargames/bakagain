namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>Enhanced mode is the master AND the feature; master off by default, features on.</summary>
    public class EnhancedOptionsTests {
        private EnhancedPrefsStash _stash;

        [SetUp]
        public void SetUp() => _stash = EnhancedPrefsStash.Take();

        [TearDown]
        public void TearDown() => _stash.Restore();

        [Test]
        public void TheStashPutsThePlayersOwnValuesBack() {
            PlayerPrefs.SetInt("enhanced", 1);
            EnhancedPrefsStash inner = EnhancedPrefsStash.Take();
            Assert.IsFalse(PlayerPrefs.HasKey("enhanced"), "cleared for the test");
            PlayerPrefs.SetInt("enhanced", 0);
            PlayerPrefs.SetInt("enhanced MouseLook", 0);

            inner.Restore();

            Assert.AreEqual(1, PlayerPrefs.GetInt("enhanced", -1), "a present key comes back with its value");
            Assert.IsFalse(PlayerPrefs.HasKey("enhanced MouseLook"), "an absent key stays absent");
        }

        [Test]
        public void AFreshPlayerGetsTheFaithfulGame() {
            Assert.IsFalse(GameOptions.Enhanced);
            foreach (EnhancedFeature f in System.Enum.GetValues(typeof(EnhancedFeature))) {
                Assert.IsTrue(GameOptions.GetFeature(f), $"{f} defaults on");
                Assert.IsFalse(GameOptions.IsOn(f), $"{f} is off while the master is off");
            }
        }

        [Test]
        public void AFeatureIsOnOnlyWithTheMaster() {
            GameOptions.Enhanced = true;
            GameOptions.SetFeature(EnhancedFeature.MouseLook, false);
            Assert.IsTrue(GameOptions.IsOn(EnhancedFeature.FullScreenTravel));
            Assert.IsFalse(GameOptions.IsOn(EnhancedFeature.MouseLook));
        }

        [Test]
        public void AnUnexpectedStoredValueReadsAsOff() {
            PlayerPrefs.SetInt("enhanced", 7);
            Assert.IsFalse(GameOptions.Enhanced);
        }
    }
}
