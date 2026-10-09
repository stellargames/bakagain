namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using NUnit.Framework;

    /// <summary>
    /// Enhanced mode's book font: <see cref="GameFonts.Game"/> becomes Alegreya Medium (with its
    /// italic for &lt;i&gt;) sized from the dialog line pitch; off, it is GAME.FNT exactly as before.
    /// </summary>
    [RequiresShippedGameData]
    public class BookFontTests {
        [SetUp]
        public void SetUp() {
            using (new IsolatedResourceLocators()) {
                ResourceManagementInitializer.InitializeResourceManagement();
                GameFonts.Build();
            }
        }

        // Back to what the player's own options say, as a fresh launch would build.
        [TearDown]
        public void TearDown() => GameFonts.Build(GameOptions.IsOn(EnhancedFeature.BookFont));

        [Test]
        public void On_TheGameFontIsAlegreyaWithItsItalic_SizedFromTheLinePitch() {
            GameFonts.Build(bookFont: true);

            Assert.IsTrue(GameFonts.IsBookFont);
            Assert.AreEqual("Alegreya-Medium", GameFonts.Game.sourceFontFile.name);
            Assert.AreEqual("Alegreya-MediumItalic", GameFonts.Game.fontWeightTable[4].italicTypeface?.sourceFontFile.name,
                "<i> at the regular weight draws the real italic");
            Assert.IsTrue(GameFonts.Game.HasCharacter('ä', false, true), "German letters are in the face");
            Assert.AreEqual(1.04f * GameFontText.LinePitchPx(GameFontText.DialogLineGapVgaRows), GameFontText.FontSizePx, 0.001f);
            Assert.AreEqual(10, (int)GameFonts.GameFont.Height, "line pitch is still GAME.FNT's cell");
        }

        [Test]
        public void Off_TheGameFontIsGameFnt() {
            GameFonts.Build(bookFont: false);

            Assert.IsFalse(GameFonts.IsBookFont);
            Assert.AreNotEqual("Alegreya-Medium", GameFonts.Game.sourceFontFile.name);
            GameData.Resources.Font.FontResource font = GameFonts.GameFont;
            Assert.AreEqual((float)((font.Height + 1) * font.PixelWidth), GameFontText.FontSizePx, 0.001f);
        }
    }
}
