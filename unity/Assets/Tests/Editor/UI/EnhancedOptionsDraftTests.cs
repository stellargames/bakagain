namespace BakAgain.Tests.Editor.UI {
    using BakAgain.Core;
    using BakAgain.UI;
    using NUnit.Framework;
    using UnityEngine;

    public class EnhancedOptionsDraftTests {
        [SetUp, TearDown]
        public void Clear() {
            PlayerPrefs.DeleteKey("enhanced");
            foreach (EnhancedFeature f in System.Enum.GetValues(typeof(EnhancedFeature)))
                PlayerPrefs.DeleteKey($"enhanced {f}");
        }

        [Test]
        public void EditsAreNotSavedUntilCommit() {
            EnhancedOptionsDraft draft = EnhancedOptionsDraft.Load();
            draft.Master = true;
            draft[EnhancedFeature.PortraitRings] = false;
            Assert.IsFalse(GameOptions.Enhanced, "cancel must be able to drop it");

            draft.Commit();

            Assert.IsTrue(GameOptions.Enhanced);
            Assert.IsFalse(GameOptions.GetFeature(EnhancedFeature.PortraitRings));
            Assert.IsTrue(GameOptions.GetFeature(EnhancedFeature.MouseLook));
        }
    }
}
