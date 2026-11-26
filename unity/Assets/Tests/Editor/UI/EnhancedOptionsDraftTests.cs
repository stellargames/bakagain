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
        public void ShadowsDefaultByPlatformAndSaveOnCommit() {
            Assert.AreEqual(ShadowMode.Sun, GameOptions.Shadows, "desktop: the sun's shadows until the player chooses");
            Assert.AreEqual(ShadowMode.Off, GameOptions.ShadowsShown, "the faithful game has none");

            EnhancedOptionsDraft draft = EnhancedOptionsDraft.Load();
            draft.Shadows = ShadowMode.Blobs;
            Assert.AreEqual(ShadowMode.Sun, GameOptions.Shadows, "not saved before Save");

            draft.Master = true;
            draft.Commit();

            Assert.AreEqual(ShadowMode.Blobs, GameOptions.ShadowsShown);
        }

        [Test]
        public void CombatSpeedSetsTheArenaClockOnCommit() {
            Assert.AreEqual(GameData.Resources.Combat.CombatSpeed.Normal, GameOptions.CombatSpeed);
            Assert.AreEqual(50.0 / 3, GameOptions.ArenaFramesPerSecond, 1e-9);

            EnhancedOptionsDraft draft = EnhancedOptionsDraft.Load();
            draft.CombatSpeed = GameData.Resources.Combat.CombatSpeed.Fast;
            Assert.AreEqual(50.0 / 3, GameOptions.ArenaFramesPerSecond, 1e-9, "not before Save");
            draft.Commit();

            Assert.AreEqual(25.0, GameOptions.ArenaFramesPerSecond, 1e-9);
        }

        [Test]
        public void TheHealthRingsRowNeedsFullScreenTravel() {
            var draft = new EnhancedOptionsDraft { Master = true };
            foreach (EnhancedFeature f in System.Enum.GetValues(typeof(EnhancedFeature))) draft[f] = true;
            Assert.IsTrue(EnhancedSettingsScreen.EnhancedRowEnabled(draft, EnhancedFeature.PortraitRings));

            draft[EnhancedFeature.FullScreenTravel] = false;
            Assert.IsFalse(EnhancedSettingsScreen.EnhancedRowEnabled(draft, EnhancedFeature.PortraitRings), "rings only draw full-screen");
            Assert.IsTrue(EnhancedSettingsScreen.EnhancedRowEnabled(draft, EnhancedFeature.MouseLook), "the others do not care");
            Assert.IsTrue(EnhancedSettingsScreen.EnhancedRowEnabled(draft, EnhancedFeature.FullScreenTravel));

            draft.Master = false;
            draft[EnhancedFeature.FullScreenTravel] = true;
            Assert.IsFalse(EnhancedSettingsScreen.EnhancedRowEnabled(draft, EnhancedFeature.FullScreenTravel), "the master gates all");
        }

        [Test]
        public void TheMasterAndBookFontSayRestartWhileTheFontWouldChange() {
            Assume.That(GameFonts.IsBookFont, Is.False, "the run loads GAME.FNT");
            var draft = new EnhancedOptionsDraft { Master = true };
            foreach (EnhancedFeature f in System.Enum.GetValues(typeof(EnhancedFeature))) draft[f] = true;
            Assert.IsTrue(EnhancedSettingsScreen.FontChangePending(draft), "OK would load the book font at the next start");

            draft.Master = false;
            Assert.IsFalse(EnhancedSettingsScreen.FontChangePending(draft), "master off: GAME.FNT, as loaded");

            draft.Master = true;
            draft[EnhancedFeature.BookFont] = false;
            Assert.IsFalse(EnhancedSettingsScreen.FontChangePending(draft));
        }

        [Test]
        public void VolumesRoundTripThroughTheDraft() {
            Assert.AreEqual(1f, GameOptions.MusicVolume, "full until the player chooses");
            EnhancedOptionsDraft draft = EnhancedOptionsDraft.Load();
            draft.MusicVolume = 0.25f;
            draft.SoundVolume = 0.6f;
            Assert.AreEqual(1f, GameOptions.MusicVolume, "not saved before Save");

            draft.Commit();

            Assert.AreEqual(0.25f, GameOptions.MusicVolume, 0.001f);
            Assert.AreEqual(0.6f, GameOptions.SoundVolume, 0.001f);
            Assert.AreEqual(0.25f, EnhancedOptionsDraft.Load().MusicVolume, 0.001f);
            Assert.AreEqual(0.6f, EnhancedOptionsDraft.Load().SoundVolume, 0.001f);
        }
    }
}
