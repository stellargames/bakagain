namespace BakAgain.Tests {
    using NUnit.Framework;

    /// <summary>
    /// Every test run starts from a fresh player's options — Enhanced off — whatever the owner has
    /// set in the Editor, and puts them back afterwards. Without it, turning the book font on in the
    /// Editor turned every GAME.FNT metric test red, because the fonts are built from the options.
    /// </summary>
    /// <remarks>In the root test namespace, so NUnit runs it once around every test under it.</remarks>
    [SetUpFixture]
    public class FaithfulOptionsForTheRun {
        private EnhancedPrefsStash _stash;

        [OneTimeSetUp]
        public void Clear() {
            _stash = EnhancedPrefsStash.Take();
            // The fonts outlive a test run: the Editor does not reload the domain on Play.
            if (BakAgain.UI.GameFonts.IsBookFont) {
                BakAgain.UI.GameFonts.Build(bookFont: false);
            }
        }

        [OneTimeTearDown]
        public void Restore() {
            _stash?.Restore();
            bool book = BakAgain.Core.GameOptions.IsOn(BakAgain.Core.EnhancedFeature.BookFont);
            if (BakAgain.UI.GameFonts.Game != null && BakAgain.UI.GameFonts.IsBookFont != book) {
                BakAgain.UI.GameFonts.Build(book);
            }
        }
    }
}
