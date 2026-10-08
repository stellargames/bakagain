namespace BakAgain.Tests.Editor.UI {
    using BakAgain.Core;
    using BakAgain.UI;
    using NUnit.Framework;
    using UnityEngine;

    public class EnhancedOptionsDraftTests {
        private EnhancedPrefsStash _stash;

        [SetUp]
        public void SetUp() => _stash = EnhancedPrefsStash.Take();

        [TearDown]
        public void TearDown() => _stash.Restore();

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

        [Test]
        public void TheHealthRingsRowNeedsFullScreenTravel() {
            var draft = new EnhancedOptionsDraft { Master = true };
            foreach (EnhancedFeature f in System.Enum.GetValues(typeof(EnhancedFeature))) draft[f] = true;
            Assert.IsTrue(PreferencesMenu.EnhancedRowEnabled(draft, EnhancedFeature.PortraitRings));

            draft[EnhancedFeature.FullScreenTravel] = false;
            Assert.IsFalse(PreferencesMenu.EnhancedRowEnabled(draft, EnhancedFeature.PortraitRings), "rings only draw full-screen");
            Assert.IsTrue(PreferencesMenu.EnhancedRowEnabled(draft, EnhancedFeature.MouseLook), "the others do not care");
            Assert.IsTrue(PreferencesMenu.EnhancedRowEnabled(draft, EnhancedFeature.FullScreenTravel));

            draft.Master = false;
            draft[EnhancedFeature.FullScreenTravel] = true;
            Assert.IsFalse(PreferencesMenu.EnhancedRowEnabled(draft, EnhancedFeature.FullScreenTravel), "the master gates all");
        }
    }
}
