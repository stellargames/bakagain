namespace BakAgain.ResourceManagement {
    using GameData.Resources.Font;
    using GameData.Resources.Text;
    using System;
    using System.IO;
    using UnityEngine;

    /// <summary>
    /// The active language pack (TASK-773): a gettext PO file at
    /// <c>&lt;OverridePath&gt;/Lang/&lt;locale&gt;/&lt;locale&gt;.po</c>, read once at start.
    /// </summary>
    /// <remarks>
    /// <b>Applied where resources load</b> (<see cref="BakResourceProvider"/>, the UI string loader),
    /// so every screen sees translated text without knowing a pack exists. No pack, an unreadable one
    /// or the <c>en</c> setting all mean the original English — a broken pack must never stop the
    /// game starting.
    /// </remarks>
    public static class LanguagePacks {
        private static LanguagePack _current;

        /// <summary>The pack in effect for this session.</summary>
        public static LanguagePack Current => _current ??= Load(BakResourceSettings.Language, BakResourceSettings.OverridePath);

        /// <summary>Forget the loaded pack, so the next read of <see cref="Current"/> re-reads the
        /// setting and the file.</summary>
        public static void Reload() => _current = null;

        /// <summary>The pack file a locale is read from, under the override folder.</summary>
        public static string PathFor(string overridePath, string locale) =>
            Path.Combine(overridePath ?? string.Empty, "Lang", locale, locale + ".po");

        /// <summary>The pack's pixel font for a game font: <c>fonts/&lt;GAME|BOOK&gt;.bdf</c> beside the PO file.</summary>
        public static string FontPathFor(string overridePath, string locale, string fontId) =>
            Path.Combine(overridePath ?? string.Empty, "Lang", locale, "fonts",
                Path.GetFileNameWithoutExtension(fontId) + ".bdf");

        /// <summary>
        /// Merge the active pack's BDF for <paramref name="font"/>, if it has one, so the letters
        /// its translation uses can be drawn and measured (TASK-778). Like the text, a broken font
        /// file is a warning, never a failed start.
        /// </summary>
        public static void MergeFont(FontResource font) {
            if (Current == LanguagePack.English || font.PixelFormat != FontPixelFormat.Monochrome) {
                return;
            }
            string path = FontPathFor(BakResourceSettings.OverridePath, BakResourceSettings.Language, font.Id);
            if (!File.Exists(path)) {
                return;
            }
            try {
                using var reader = new StreamReader(path);
                var clipped = ResourceExtraction.Text.BdfFont.MergeInto(font, reader);
                Debug.Log($"Language pack font {path}: {font.ExtraGlyphs.Count} glyphs merged into {font.Id}.");
                if (clipped.Count > 0) {
                    Debug.LogWarning($"{path}: ink outside {font.Id}'s {font.Height}-row cell was clipped for "
                        + string.Join(", ", System.Linq.Enumerable.Select(clipped, c => $"U+{c:X4}")) + ".");
                }
            } catch (Exception e) {
                Debug.LogWarning($"Language pack font {path} could not be read ({e.Message}); its letters will not draw.");
            }
        }

        private static LanguagePack Load(string locale, string overridePath) {
            if (string.IsNullOrEmpty(locale) || locale == LanguagePack.English.Locale
                || string.IsNullOrEmpty(overridePath)) {
                return LanguagePack.English;
            }
            string path = PathFor(overridePath, locale);
            if (!File.Exists(path)) {
                Debug.LogWarning($"Language '{locale}' selected but no pack at {path}; using English.");
                return LanguagePack.English;
            }
            try {
                using var reader = new StreamReader(path);
                LanguagePack pack = ResourceExtraction.Text.PoLanguagePack.Read(reader);
                Debug.Log($"Language pack '{locale}': {pack.TranslatedCount} translated strings from {path}.");
                return pack;
            } catch (Exception e) {
                Debug.LogWarning($"Language pack at {path} could not be read ({e.Message}); using English.");
                return LanguagePack.English;
            }
        }
    }
}
