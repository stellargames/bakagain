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

        /// <summary>Build <see cref="Game"/> and <see cref="Book"/> once, from the loaded game data.</summary>
        public static void Build() {
            if (Game != null) {
                return;
            }
            string dir = Path.Combine(Application.temporaryCachePath, "Fonts");
            Directory.CreateDirectory(dir);
            // From a Font, not from the file path: UI Toolkit's text generator renders from the
            // asset's source Font and draws nothing ("FontAsset is invalid. Please assign a Source
            // Font File") for an asset made straight from a path.
            Game = FontAsset.CreateFontAsset(LoadTrueType(dir, "GAME.FNT", "Game"), 90, 18,
                GlyphRenderMode.SDFAA_HINTED, 1024, 1024, UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic, true);
            Book = TMP_FontAsset.CreateFontAsset(LoadTrueType(dir, "BOOK.FNT", "Book"), 90, 9,
                GlyphRenderMode.SDFAA, 1024, 1024, TMPro.AtlasPopulationMode.Dynamic, true);
        }

        private static Font LoadTrueType(string dir, string fntKey, string family) {
            FontResource fnt = Addressables.LoadAssetAsync<FontResource>(fntKey).WaitForCompletion();
            string path = Path.Combine(dir, family + ".ttf");
            File.WriteAllBytes(path, FntTrueType.Build(fnt, family));

            // A name with a directory in it makes Unity load the font from that file.
            return new Font(path);
        }
    }
}
