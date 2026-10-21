namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.UI;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

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
            Assert.AreEqual(new GameData.Resources.Layout.EnhancedFontLayout().EmPerLinePitch * GameFontText.LinePitchPx(GameFontText.DialogLineGapVgaRows), GameFontText.FontSizePx, 0.001f);
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

        [Test]
        public void On_TheBookIsBookHand_WithBookFntBehindIt() {
            GameFonts.Build(bookFont: true);

            Assert.AreEqual("BookHand-Regular", GameFonts.Book.sourceFontFile.name);
            CollectionAssert.IsNotEmpty(GameFonts.Book.fallbackFontAssetTable, "a letter Book Hand lacks draws in BOOK.FNT");
        }

        [Test]
        public void Off_TheBookIsBookFnt() {
            GameFonts.Build(bookFont: false);

            Assert.AreNotEqual("BookHand-Regular", GameFonts.Book.sourceFontFile.name);
        }

        /// <summary>
        /// Leaving Play destroys what the session made, but the statics survive into the next one
        /// when it starts without a domain reload (the MCP test runner's PlayMode runs, or Enter Play
        /// Mode options): a destroyed BOOK.FNT asset is built again, and Book Hand falls back to the
        /// new one (TASK-918).
        /// </summary>
        [Test]
        public void ADestroyedBookFntAsset_IsBuiltAgain() {
            GameFonts.Build(bookFont: false);
            TMPro.TMP_FontAsset stale = GameFonts.Book;
            Object.DestroyImmediate(stale.sourceFontFile);
            Object.DestroyImmediate(stale);

            GameFonts.Build(bookFont: false);
            Assert.IsTrue(GameFonts.Book != null, "BOOK.FNT's asset is built again");
            Assert.AreNotEqual("BookHand-Regular", GameFonts.Book.sourceFontFile.name);

            GameFonts.Build(bookFont: true);
            Assert.IsTrue(GameFonts.Book.fallbackFontAssetTable.TrueForAll(f => f != null),
                "Book Hand falls back to the live BOOK.FNT asset, not the destroyed one");
        }

        /// <summary>
        /// A language pack's own BOOK font can redraw a letter at its own width (the German pack's ß
        /// is 8 columns, Book Hand's 10). The wrap measures the merged font, so Book Hand follows it.
        /// </summary>
        [Test]
        public void APackFontsWidthWins_OverBookHandsOwnAdvance() {
            GameData.Resources.Font.FontResource fnt = GameFonts.BookFont;
            bool had = fnt.ExtraGlyphs.TryGetValue('ß', out GameData.Resources.Font.FontGlyph previous);
            fnt.ExtraGlyphs['ß'] = new GameData.Resources.Font.FontGlyph { Width = 8 };
            try {
                GameFonts.Build(bookFont: true);
                TMPro.TMP_FontAsset hand = GameFonts.Book;
                Assert.IsTrue(hand.HasCharacter('ß', false, true));
                float columns = hand.characterLookupTable['ß'].glyph.metrics.horizontalAdvance
                    / hand.faceInfo.pointSize * (fnt.Height + 1);
                Assert.AreEqual(8f, columns, 0.1f);
            } finally {
                if (had) {
                    fnt.ExtraGlyphs['ß'] = previous;
                } else {
                    fnt.ExtraGlyphs.Remove('ß');
                }
                GameFonts.Build(bookFont: true);
            }
        }

        /// <summary>
        /// The book wraps on BOOK.FNT's widths, so Book Hand must advance exactly as far for every
        /// character, the language packs' letters included: TMP wraps a justified line that comes out
        /// wider than its box.
        /// </summary>
        [Test]
        public void BookHandAdvancesEqualBookFntWidths_PackLettersIncluded() {
            GameFonts.Build(bookFont: true);
            GameData.Resources.Font.FontResource fnt = GameFonts.BookFont;
            var before = new HashSet<int>(fnt.ExtraGlyphs.Keys);
            try {
                // Composed before the build, as LanguagePacks.MergeFont runs before GameFonts.Build.
                GameData.Resources.Font.GlyphSynthesis.AddComposed(fnt, "ÄÖÜßäöüëèéï".Select(c => (int)c));
                GameFonts.Build(bookFont: true);
                TMPro.TMP_FontAsset hand = GameFonts.Book;
                // An em is BOOK.FNT's Height + 1 columns, in both fonts (FntTrueType).
                float columnsPerEm = fnt.Height + 1;
                foreach (var (code, glyph) in fnt.AllGlyphs()) {
                    if (code < 0x20 || code == 0x7F) {
                        continue;
                    }
                    char c = (char)code;
                    Assert.IsTrue(hand.HasCharacter(c, false, true), $"'{c}' is missing");
                    float columns = hand.characterLookupTable[c].glyph.metrics.horizontalAdvance
                        / hand.faceInfo.pointSize * columnsPerEm;
                    Assert.AreEqual(glyph.Width, columns, 0.1f, $"'{c}'");
                }
            } finally {
                foreach (int added in fnt.ExtraGlyphs.Keys.Where(k => !before.Contains(k)).ToList()) {
                    fnt.ExtraGlyphs.Remove(added);
                }
                GameFonts.Build(bookFont: true);
            }
        }
        // The survey's tightest real lines (TASK: Enhanced font report): all-caps German, a long
        // German narration, slurred English, and the Gorath narration everyone looks at first.
        private static readonly string[] Hardest = {
            "\tUh, ist schon in Ordnung.\n\tSCHON IN ORDNUNG. ENTSCHULDIGT, DAß WIR EUCH BELÄSTIGT HABEN.",
            "\tTage vergingen.\n\tSie fühlten sich unbehaglich, da ihnen der Hohlweg ein Gefühl des Ausgesetzseins gab. Sie reisten schnell bei Nacht und ruhten tagsüber unter felsigen Überhängen, wobei sie ständig auf die Schritte von Meuchelmördern lauschten. Schließlich, mit wunden Füßen und der Straße überdrüssig, erreichten sie die Küste des Bitteren Meeres.",
            "\tYa nu...Zaa unturducktur key urdn skeywers...trite to sulit tome but...UHRUP...I no crumiminal...oh gles mr...AAAGH. O Ghats! Imna be sich! Ha- ho!",
            Gorath,
        };

        private const string Gorath = "\tGorath seemed distant.\n\tThough the moredhel warrior didn't appear grieved about killing the assassin that had followed them from the Northlands, his eyes had a baleful look in them that seemed something between hatred and rage. Several times he glanced back at the corpse that lay behind them in the dust, his thoughts unguessable from his expression...";

        [Test]
        public void On_AWrappedLineNeverRunsPastItsBox_MeasuredByTheTextEngine() {
            GameFonts.Build(bookFont: true);
            float em = GameFontText.FontSizePx;
            foreach (float width in new[] { 1370f, 1446f }) {      // the narrative and spoken bodies
                foreach (string raw in Hardest) {
                    string text = DialogTextFormatter.Prepare(raw, centered: false);
                    List<GameTextWrapper.Line> lines = GameTextWrapper.Wrap(text, width, GameFonts.BookAdvances(text, em));
                    foreach (GameTextWrapper.Line line in lines) {
                        string rich = DialogTextFormatter.Format(text, line.Start, line.End, new Color[256], 0);
                        var label = new Label(rich);
                        GameFontText.Apply(label);
                        label.style.unityFontDefinition = new StyleFontDefinition(GameFonts.Game);
                        float measured = label.MeasureTextSize(rich, 100000f, VisualElement.MeasureMode.AtMost,
                            0f, VisualElement.MeasureMode.Undefined).x;
                        Assert.That(measured, Is.LessThanOrEqualTo(width + 0.5f), $"\"{rich}\" in a {width} px box");
                    }
                }
            }
        }

        [Test]
        public void Off_TheBreaksAreTheOriginals() {
            GameFonts.Build(bookFont: false);
            string text = DialogTextFormatter.Prepare(Gorath, centered: false);
            var got = new List<string>();
            foreach (GameTextWrapper.Line line in GameTextWrapper.Wrap(text, 274)) {   // 1370 / 5
                got.Add(text.Substring(line.Start, line.Length));
            }
            CollectionAssert.AreEqual(new[] {
                "     Gorath seemed distant.",
                "     Though the moredhel warrior didn't appear grieved",
                "about killing the assassin that had followed them from the",
                "Northlands, his eyes had a baleful look in them that seemed",
                "something between hatred and rage. Several times he",
                "glanced back at the corpse that lay behind them in the dust,",
                "his thoughts unguessable from his expression...",
            }, got);
        }

        private static readonly string[] Captions = { "OK", "Restore Game", "Start New Game" };

        private static IEnumerator CaptionSizes(bool bookFont, List<float> sizes) {
            GameFonts.Build(bookFont);
            var host = new GameObject("CaptionScreenUnderTest");
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            try {
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                document.rootVisualElement.style.unityFontDefinition = new StyleFontDefinition(GameFonts.Game);
                var captions = new List<Label>();
                for (int i = 0; i < Captions.Length; i++) {
                    var button = new Button();
                    button.style.position = Position.Absolute;
                    button.style.top = i * 80;
                    button.style.width = 260;
                    button.style.height = 70;
                    document.rootVisualElement.Add(button);
                    captions.Add(GameFontText.Caption(button, Captions[i]));
                }
                for (int f = 0; f < 6; f++) {
                    yield return null;
                }
                foreach (Label c in captions) {
                    sizes.Add(c.resolvedStyle.fontSize);
                }
            } finally {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
            }
        }

        [UnityTest]
        public IEnumerator EveryCaptionOnAScreenTakesTheSmallestFit([Values] bool bookFont) {
            var sizes = new List<float>();
            yield return CaptionSizes(bookFont, sizes);
            Assert.That(sizes[2], Is.LessThan(GameFontText.FontSizePx - 1f), "the long caption had to shrink");
            Assert.That(sizes[0], Is.EqualTo(sizes[2]).Within(0.6f), "and the short one shrinks with it");
            Assert.That(sizes[1], Is.EqualTo(sizes[2]).Within(0.6f));
        }

        [Test]
        public void ASizeChosenForGameFntIsScaledToTheBookFont() {
            GameFonts.Build(bookFont: false);
            float gameFnt = GameFontText.FontSizePx;
            Assert.AreEqual(48f, GameFontText.InDrawnFont(48f), 0.001f, "GAME.FNT: unchanged");

            GameFonts.Build(bookFont: true);
            Assert.AreEqual(48f * GameFontText.FontSizePx / gameFnt, GameFontText.InDrawnFont(48f), 0.001f);
        }
    }
}
