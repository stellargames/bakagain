namespace BakAgain.UI {
    using BakAgain.ResourceManagement.Converters;
    using GameData.Resources.Font;
    using System.IO;
    using TMPro;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.TextCore.LowLevel;
    using UnityEngine.TextCore.Text;
    using UnityEngine.UIElements;

    /// <summary>
    /// The game's text fonts, built at runtime from the player's own GAME.FNT and BOOK.FNT (via
    /// <see cref="FntTrueType"/>) instead of shipping Dynamix's glyphs as project assets.
    /// </summary>
    /// <remarks>
    /// Atlas settings are the ones the replaced assets used (Game SDF: 90 pt, padding 18,
    /// SDFAA_HINTED; Book SDF: 90 pt, padding 9, SDFAA).
    /// </remarks>
    public static class GameFonts {
        /// <summary>GAME.FNT for UI Toolkit: every screen, dialog and caption. Null until installed.</summary>
        public static FontAsset Game { get; private set; }

        /// <summary>
        /// GAME.FNT itself — its cell height and the shape of its pixels, which is what line
        /// heights are measured in. Builds the fonts if nothing has yet.
        /// </summary>
        public static FontResource GameFont {
            get {
                Build();
                return _gameFont;
            }
        }

        private static FontResource _gameFont;

        /// <summary>BOOK.FNT itself, for the book's measurements. Builds the fonts if nothing has yet.</summary>
        public static FontResource BookFont {
            get {
                Build();
                return _bookFont;
            }
        }

        private static FontResource _bookFont;

        /// <summary>BOOK.FNT for the TextMesh Pro book view. Null until installed.</summary>
        public static TMP_FontAsset Book { get; private set; }

        /// <summary>
        /// Build the fonts (once) and make GAME the default font of <paramref name="panelSettings"/>,
        /// which every UIDocument shares. The theme sets no font of its own, so this is what all
        /// UI text renders in.
        /// </summary>
        /// <remarks>
        /// On the text settings rather than as a style on the panel's root: the panel is recreated
        /// whenever its last document goes away (the loading screen is released right after this
        /// runs), and a style set on it goes with it. The PanelSettings asset outlives that, so the
        /// runtime text settings are taken off it again on quit — otherwise the Editor could save
        /// the asset pointing at an object that only existed during play.
        /// </remarks>
        public static void Install(PanelSettings panelSettings) {
            Build();
            PanelTextSettings original = panelSettings.textSettings;
            var runtime = ScriptableObject.CreateInstance<PanelTextSettings>();
            runtime.defaultFontAsset = Game;
            panelSettings.textSettings = runtime;
            Application.quitting += () => panelSettings.textSettings = original;
        }

        /// <summary>
        /// Whether <see cref="Game"/> is Enhanced mode's book font (Alegreya Medium) rather than
        /// GAME.FNT — decided once, when the fonts are built, which is why the option says "(restart)".
        /// <see cref="GameFont"/> stays GAME.FNT either way: line pitch is the original's.
        /// </summary>
        public static bool IsBookFont { get; private set; }

        /// <summary>Build <see cref="Game"/> and <see cref="Book"/> once, from the loaded game data.</summary>
        public static void Build() {
            if (Game == null) {
                Build(BakAgain.Core.GameOptions.IsOn(BakAgain.Core.EnhancedFeature.BookFont));
            }
        }

        /// <summary>Build the fonts now, with or without the book font — what <see cref="Build()"/> does once.</summary>
        /// <remarks>
        /// Each kind of asset is made once and reused by a later call (tests switch back and forth):
        /// a rebuild must not destroy an asset, because labels already drawn hold on to it, and
        /// making a fresh set on every call leaked one per switch.
        /// </remarks>
        internal static void Build(bool bookFont) {
            string dir = Path.Combine(Application.temporaryCachePath, "Fonts");
            Directory.CreateDirectory(dir);
            _gameFont = Addressables.LoadAssetAsync<FontResource>("GAME.FNT").WaitForCompletion();
            if (bookFont && _alegreya == null) {
                _alegreya = LoadBookFont();
            }
            // A missing bundled font is a broken build, not a reason to draw nothing: GAME.FNT then.
            IsBookFont = bookFont && _alegreya != null;
            // From a Font, not from the file path: UI Toolkit's text generator renders from the
            // asset's source Font and draws nothing ("FontAsset is invalid. Please assign a Source
            // Font File") for an asset made straight from a path.
            if (!IsBookFont && _gameFntAsset == null) {
                _gameFntAsset = FontAsset.CreateFontAsset(TrueType(dir, _gameFont, "Game"), 90, 18,
                    GlyphRenderMode.SDFAA_HINTED, 1024, 1024, UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic, true);
            }
            Game = IsBookFont ? _alegreya : _gameFntAsset;
            _bookFont = Addressables.LoadAssetAsync<FontResource>("BOOK.FNT").WaitForCompletion();
            Book ??= TMP_FontAsset.CreateFontAsset(TrueType(dir, _bookFont, "Book"), 90, 9,
                GlyphRenderMode.SDFAA, 1024, 1024, TMPro.AtlasPopulationMode.Dynamic, true);
        }

        private static FontAsset _gameFntAsset;
        private static FontAsset _alegreya;

        /// <summary>
        /// Alegreya Medium, with its Medium Italic as the face <c>&lt;i&gt;</c> switches to at the
        /// text's regular weight (400 → slot 4) instead of a slanted Medium. Null if the bundled
        /// fonts are missing.
        /// </summary>
        private static FontAsset LoadBookFont() {
            Font medium = Resources.Load<Font>("Fonts/Alegreya/Alegreya-Medium");
            Font italic = Resources.Load<Font>("Fonts/Alegreya/Alegreya-MediumItalic");
            if (medium == null) {
                return null;
            }
            FontAsset Make(Font font) => FontAsset.CreateFontAsset(font, 90, 9,
                GlyphRenderMode.SDFAA, 1024, 1024, UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic, true);
            FontAsset regular = Make(medium);
            if (italic != null) {
                regular.fontWeightTable[4].italicTypeface = Make(italic);
            }
            return regular;
        }

        /// <summary>
        /// Each character's advance in the book font at <paramref name="em"/> canonical px, for a
        /// prepared dialog string — what <see cref="GameTextWrapper"/> wraps with when
        /// <see cref="IsBookFont"/>. Italic runs (the DDX control codes, decoded as
        /// <see cref="DialogTextFormatter"/> styles them) measure in the italic face; the codes
        /// themselves and line breaks have no width.
        /// </summary>
        /// <remarks>Glyph advances only, no kerning. Checked against the text engine over every
        /// narrative and spoken line in English and German: the drawn line matches the sum to
        /// within rounding (kerning mostly tightens it), and no wrapped line ends past its box.</remarks>
        internal static float[] BookAdvances(string text, float em) {
            var advances = new float[text?.Length ?? 0];
            if (advances.Length == 0) {
                return advances;
            }
            var italic = new bool[text.Length];
            foreach (GameData.Resources.Dialog.DialogTextRuns.Run run in
                     GameData.Resources.Dialog.DialogTextRuns.Decode(text, 0, text.Length, 0)) {
                for (int i = run.Start; i < run.Start + run.Length; i++) {
                    italic[i] = run.Italic;
                }
            }
            FontAsset regular = Game;
            FontAsset oblique = regular.fontWeightTable[4].italicTypeface ?? regular;
            regular.TryAddCharacters(text, out string _);
            oblique.TryAddCharacters(text, out string _);
            for (int i = 0; i < text.Length; i++) {
                char c = text[i];
                if (c == '\n' || (c >= '\uE000' && c <= '\uE00F')) {
                    continue;
                }
                FontAsset face = italic[i] ? oblique : regular;
                advances[i] = face.characterLookupTable.TryGetValue(c, out UnityEngine.TextCore.Text.Character character)
                    ? character.glyph.metrics.horizontalAdvance * face.faceInfo.scale * em / face.faceInfo.pointSize
                    : em / 2f; // ponytail: a letter the face lacks is drawn as nothing; half an em keeps the wrap conservative
            }
            return advances;
        }

        private static Font TrueType(string dir, FontResource fnt, string family) {
            string path = Path.Combine(dir, family + ".ttf");
            File.WriteAllBytes(path, FntTrueType.Build(fnt, family));

            // A name with a directory in it makes Unity load the font from that file.
            return new Font(path);
        }
    }
}
