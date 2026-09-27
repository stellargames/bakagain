namespace BakAgain.Tests.UI {
    using BakAgain.Audio;
    using BakAgain.ResourceManagement.Loaders;
    using GameData.Resources.Menu;
    using NUnit.Framework;

    public class UserInterfaceLoaderSoundTests {
        // The collapse is what this fixture owns; which bit gates which edge is UiElement's, and
        // UiElementSoundTests covers that side.
        private static UiElement Element(int soundFlags, int clickSound) =>
            new UiElement { SoundFlags = soundFlags, ClickSound = clickSound };

        [Test]
        public void DefaultButton_playsPound() {
            // SoundFlags 0 = both cues in the original; collapsed to one default pound.
            Assert.AreEqual(MenuSoundService.PoundSoundId, UserInterfaceLoader.ResolveSelectSound(Element(0, 0)));
        }

        [Test]
        public void ClickSoundOverride_playsOverride() {
            Assert.AreEqual(18, UserInterfaceLoader.ResolveSelectSound(Element(0, 18))); // swoosh
        }

        [Test]
        public void ReleaseSuppressed_stillPlays() {
            // SoundFlags 2 (MainMenu etc.): release suppressed but the press cue plays — audible.
            Assert.AreEqual(MenuSoundService.PoundSoundId, UserInterfaceLoader.ResolveSelectSound(Element(2, 0)));
            Assert.AreEqual(18, UserInterfaceLoader.ResolveSelectSound(Element(2, 18)));
        }

        [Test]
        public void PressSuppressed_stillPlays() {
            // SoundFlags 1 (REQ_PUZL cells): press suppressed but the release cue plays — its ClickSound.
            Assert.AreEqual(MenuSoundService.PoundSoundId, UserInterfaceLoader.ResolveSelectSound(Element(1, 0)));
            Assert.AreEqual(18, UserInterfaceLoader.ResolveSelectSound(Element(1, 18)));
        }

        [Test]
        public void BothSuppressed_isSilent() {
            // SoundFlags 3 (inventory slots, world-viewport hotspot, GDS zones, file-picker rows):
            // both cues suppressed -> silent, even with a ClickSound override.
            Assert.IsNull(UserInterfaceLoader.ResolveSelectSound(Element(3, 0)));
            Assert.IsNull(UserInterfaceLoader.ResolveSelectSound(Element(3, 18)));
        }
    }
}
