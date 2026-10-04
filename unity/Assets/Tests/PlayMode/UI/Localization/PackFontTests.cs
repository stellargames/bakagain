namespace BakAgain.Tests.PlayMode.UI.Localization {
    using BakAgain.ResourceManagement;
    using BakAgain.ResourceManagement.Converters;
    using GameData.Resources.Font;
    using NUnit.Framework;
    using System.IO;
    using UnityEngine;

    /// <summary>
    /// A language pack's BDF letters reach the player's GAME.FNT and the font Unity draws with
    /// (TASK-778).
    /// </summary>
    public class PackFontTests {
        private const string Bdf = "STARTFONT 2.1\nCHARS 1\nSTARTCHAR adieresis\nENCODING 228\nDWIDTH 5 0\n"
            + "BBX 4 6 0 0\nBITMAP\n90\n00\n60\n90\n90\n70\nENDCHAR\nENDFONT\n";

        private string _dir;
        private string _savedLanguage;
        private string _savedOverridePath;
        private FontResource _font;
        private System.Collections.Generic.HashSet<int> _extrasBefore;

        [SetUp]
        public void SetUp() {
            _savedLanguage = BakResourceSettings.Language;
            _savedOverridePath = BakResourceSettings.OverridePath;
            _dir = Path.Combine(Path.GetTempPath(), "bak-lang-" + System.Guid.NewGuid().ToString("N"));
            string po = LanguagePacks.PathFor(_dir, "xx");
            Directory.CreateDirectory(Path.GetDirectoryName(po));
            File.WriteAllText(po, "msgid \"\"\nmsgstr \"\"\n\"Language: xx\\n\"\n\nmsgctxt \"k\"\nmsgid \"a\"\nmsgstr \"ä\"\n");
            string bdf = LanguagePacks.FontPathFor(_dir, "xx", "GAME.FNT");
            Directory.CreateDirectory(Path.GetDirectoryName(bdf));
            File.WriteAllText(bdf, Bdf);
            BakResourceSettings.OverridePath = _dir;
            BakResourceSettings.Language = "xx";
            LanguagePacks.Reload();
        }

        [TearDown]
        public void TearDown() {
            // The FontResource is the Addressables-cached one; leave it as other tests expect it.
            if (_font != null && _extrasBefore != null) {
                foreach (int c in System.Linq.Enumerable.ToList(_font.ExtraGlyphs.Keys)) {
                    if (!_extrasBefore.Contains(c)) {
                        _font.ExtraGlyphs.Remove(c);
                    }
                }
            }
            BakResourceSettings.Language = _savedLanguage;
            BakResourceSettings.OverridePath = _savedOverridePath;
            LanguagePacks.Reload();
            Directory.Delete(_dir, true);
        }

        private void LoadGameFont() {
            try {
                _font = UnityEngine.AddressableAssets.Addressables
                    .LoadAssetAsync<FontResource>("GAME.FNT").WaitForCompletion();
            } catch (System.Exception) {
                _font = null;
            }
            if (_font == null) {
                Assert.Ignore("No game data to extract from.");
            }
            _extrasBefore = new System.Collections.Generic.HashSet<int>(_font.ExtraGlyphs.Keys);
        }

        [Test]
        public void ALetterThePackDoesNotDrawIsComposed_AndReachesTheTrueType() {
            File.Delete(LanguagePacks.FontPathFor(_dir, "xx", "GAME.FNT"));
            File.AppendAllText(LanguagePacks.PathFor(_dir, "xx"), "\nmsgctxt \"k2\"\nmsgid \"b\"\nmsgstr \"Café\"\n");
            LanguagePacks.Reload();
            LoadGameFont();
            _font.ExtraGlyphs.Remove('é');   // in case an earlier test composed it on the cached font

            LanguagePacks.MergeFont(_font);

            Assert.IsNotNull(_font.GlyphFor('é'), "é composed from e");
            string path = Path.Combine(_dir, "Composed.ttf");
            File.WriteAllBytes(path, FntTrueType.Build(_font, "Composed"));
            UnityEngine.TextCore.Text.FontAsset asset = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(
                new Font(path), 90, 18, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA_HINTED, 1024, 1024,
                UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic, true);
            Assert.IsTrue(asset.HasCharacter('é', false, true));
        }

        [Test]
        public void ThePacksLetterIsInTheGameFontAndItsTrueType() {
            LoadGameFont();

            // Loaded fresh, the provider has merged it already; an earlier test may have cached the
            // font first, which is why the merge is also called here — it is idempotent.
            LanguagePacks.MergeFont(_font);

            FontGlyph glyph = _font.GlyphFor('ä');
            Assert.IsNotNull(glyph, "the pack's ä is in GAME.FNT");
            Assert.AreEqual(5, glyph.Width);

            string path = Path.Combine(_dir, "PackProbe.ttf");
            File.WriteAllBytes(path, FntTrueType.Build(_font, "PackProbe"));
            // Through the same TextCore asset GameFonts gives UI Toolkit — the thing that draws.
            UnityEngine.TextCore.Text.FontAsset asset = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(
                new Font(path), 90, 18, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA_HINTED, 1024, 1024,
                UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic, true);
            Assert.IsTrue(asset.HasCharacter('A', false, true) && asset.HasCharacter('~', false, true),
                "the TrueType still maps the original's ASCII run");
            Assert.IsTrue(asset.HasCharacter('ä', false, true), "and maps ä");
            Assert.IsFalse(asset.HasCharacter('ö', false, true), "and nothing it does not have");
        }
    }
}
