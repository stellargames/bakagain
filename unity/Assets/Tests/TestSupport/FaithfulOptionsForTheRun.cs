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

        [OneTimeSetUp]
        public void Clear() {
            TestSupport.GameViewResolutionScope.OpenOnceWithoutGraphics();
            _language = BakResourceSettings.Language;
            BakResourceSettings.Language = "en";
            LanguagePacks.Reload();
            _stash = EnhancedPrefsStash.Take();
            // The fonts outlive a test run: the Editor does not reload the domain on Play.
            if (BakAgain.UI.GameFonts.IsBookFont) {
                BakAgain.UI.GameFonts.Build(bookFont: false);
            }
        }

        [OneTimeTearDown]
        public void Restore() {
            BakResourceSettings.Language = _language;
            LanguagePacks.Reload();
            _stash?.Restore();
            bool book = BakAgain.Core.GameOptions.IsOn(BakAgain.Core.EnhancedFeature.BookFont);
            if (BakAgain.UI.GameFonts.Game != null && BakAgain.UI.GameFonts.IsBookFont != book) {
                BakAgain.UI.GameFonts.Build(book);
            }
        }
    }
}
