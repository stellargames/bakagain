namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>Enhanced mode is the master AND the feature; master off by default, features on.</summary>
    public class EnhancedOptionsTests {
        private static readonly string[] Keys = {
            "enhanced", "enhanced FullScreenTravel", "enhanced MouseLook", "enhanced PortraitRings" };

        [SetUp, TearDown]
        public void Clear() {
            foreach (string k in Keys) PlayerPrefs.DeleteKey(k);
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
