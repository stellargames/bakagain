namespace BakAgain.Tests {
    using BakAgain.ResourceManagement;
    using NUnit.Framework;

    /// <summary>
    /// Every test run starts from a fresh player's options — English (TASK-780), Enhanced off —
    /// whatever was last chosen in the Editor, and puts them back afterwards.
    /// </summary>
    /// <remarks>
    /// Both are PlayerPrefs the Editor shares with the test run: a session left in the pseudo language
    /// turned 23 text assertions red, and the book font turned every GAME.FNT metric test red (the
    /// fonts are built from the options). In the root test namespace, so NUnit runs it once around
    /// every test under it — a global-namespace fixture did not run for filtered runs. Tests that need
    /// a language or an option set it themselves and restore it.
    /// </remarks>
    [SetUpFixture]
    public class FaithfulOptionsForTheRun {
        private EnhancedPrefsStash _stash;
        private string _language;
        private (string Game, bool ModsOn, string Mods) _folders;
        private BakAgain.Audio.MenuSoundService _sounds;

        // The folder settings are the developer's real, persisted ones. A test that changes one puts
        // it back itself; this catches one that did not (TASK-949), says so, and restores it.
        private static (string Game, bool ModsOn, string Mods) Folders =>
            (BakResourceSettings.GamePath, BakResourceSettings.OverrideEnabled, BakResourceSettings.OverridePath);

        [OneTimeSetUp]
        public void Clear() {
            TestSupport.GameViewResolutionScope.OpenOnceWithoutGraphics();
            _folders = Folders;
            _language = BakResourceSettings.Language;
            BakResourceSettings.Language = "en";
            LanguagePacks.Reload();
            _stash = EnhancedPrefsStash.Take();
            // The root scope (VContainerSettings) builds on entering Play and publishes the real
            // sound service, so every PlayMode test that raises a cue would load it. Tests watch
            // MenuSoundService.CueRequested instead; a load without the game data (CI) makes
            // Addressables log an InvalidKeyException error into whichever test raised it.
            _sounds = BakAgain.Audio.MenuSoundService.Instance;
            BakAgain.Audio.MenuSoundService.Instance = null;
            // The fonts outlive a test run: the Editor does not reload the domain on Play.
            if (BakAgain.UI.GameFonts.IsBookFont) {
                BakAgain.UI.GameFonts.Build(bookFont: false);
            }
        }

        [OneTimeTearDown]
        public void Restore() {
            if (Folders != _folders) {
                UnityEngine.Debug.LogError($"A test left the folder settings changed: {Folders}, was {_folders}. Restored.");
                (BakResourceSettings.GamePath, BakResourceSettings.OverrideEnabled, BakResourceSettings.OverridePath) = _folders;
            }
            BakResourceSettings.Language = _language;
            LanguagePacks.Reload();
            _stash?.Restore();
            BakAgain.Audio.MenuSoundService.Instance = _sounds;
            bool book = BakAgain.Core.GameOptions.IsOn(BakAgain.Core.EnhancedFeature.BookFont);
            if (BakAgain.UI.GameFonts.Game != null && BakAgain.UI.GameFonts.IsBookFont != book) {
                BakAgain.UI.GameFonts.Build(book);
            }
        }
    }
}
