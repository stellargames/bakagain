namespace BakAgain.ResourceManagement {
    using GameData.Resources.Font;
    using GameData.Resources.Text;
    using System;
    using System.IO;
    using System.Linq;
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
        public static LanguagePack Current => _current ??= Load(BakResourceSettings.Language);

        /// <summary>Forget the loaded pack, so the next read of <see cref="Current"/> re-reads the
        /// setting and the file.</summary>
        public static void Reload() => _current = null;

        /// <summary>
        /// A boot screen's text: shown before the game data — and so before the UI string catalog
        /// — is installed, it reads the embedded catalog through the active pack itself.
        /// </summary>
        public static string BootText(string key) =>
            UiTemplates.Format(UiStringCatalog.Embedded.TranslatedBy(Current), key);

        /// <summary>
        /// The locales with a pack installed: every <c>Lang/&lt;locale&gt;/&lt;locale&gt;.po</c> under the
        /// override folder (TASK-782) and under each language mod.
        /// </summary>
        public static System.Collections.Generic.IEnumerable<string> Installed() =>
            PackRoots().SelectMany(LocalesIn).Distinct();

        /// <summary>The folders a pack is looked for in: the override folder, then the language mods.</summary>
        private static System.Collections.Generic.IEnumerable<string> PackRoots() {
            if (!string.IsNullOrEmpty(BakResourceSettings.OverridePath)) {
                yield return BakResourceSettings.OverridePath;
            }
            foreach (string root in ModLayers.LanguageRoots()) {
                yield return root;
            }
        }

        /// <summary>
        /// The folder holding <paramref name="locale"/>'s pack, or null. The override folder wins over
        /// the mods, so a pack being worked on beats the installed one.
        /// </summary>
        public static string RootFor(string locale) =>
            PackRoots().FirstOrDefault(root => File.Exists(PathFor(root, locale)));

        private static System.Collections.Generic.IEnumerable<string> LocalesIn(string root) {
            string lang = Path.Combine(root, "Lang");
            if (!Directory.Exists(lang)) {
                yield break;
            }
            string[] dirs;
            try {
                dirs = Directory.GetDirectories(lang);
            } catch (Exception e) {
                // Scoped storage on Android can refuse a listing the folder check allowed; a menu
                // button must not throw over it.
                Debug.LogWarning($"Could not list language packs in {lang} ({e.Message}).");
                yield break;
            }
            foreach (string dir in dirs) {
                string locale = Path.GetFileName(dir);
                if (File.Exists(PathFor(root, locale))) {
                    yield return locale;
                }
            }
        }

        /// <summary>
        /// Whether a pack's header marks it <c>X-Status: beta</c> (playable, not yet reviewed); false
        /// when the file is missing or unreadable.
        /// </summary>
        public static bool IsBeta(string locale) {
            string root = RootFor(locale);
            if (root == null) {
                return false;
            }
            string path = PathFor(root, locale);
            try {
                using var reader = new StreamReader(path);
                return string.Equals(ResourceExtraction.Text.PoLanguagePack.HeaderField(reader, "X-Status"), "beta",
                    StringComparison.OrdinalIgnoreCase);
            } catch (Exception e) {
                Debug.LogWarning($"Could not read the header of {path} ({e.Message}).");
                return false;
            }
        }

        /// <summary>The pack file a locale is read from, under <paramref name="overridePath"/> (a pack root).</summary>
        public static string PathFor(string overridePath, string locale) =>
            Path.Combine(overridePath ?? string.Empty, "Lang", locale, locale + ".po");

        /// <summary>
        /// The active pack's folder, or null in English. It is searched for overrides before the mod
        /// folder, in the same layout (<c>SCX/&lt;NAME&gt;.png</c>, <c>BMX/&lt;NAME&gt;/&lt;i&gt;.png</c>, ...), so a
        /// pack can carry its own art (TASK-826).
        /// </summary>
        public static string ActiveFolder() {
            string root = Current == LanguagePack.English ? null : RootFor(Current.Locale);
            return root == null ? null : Path.Combine(root, "Lang", Current.Locale);
        }

        /// <summary>The pack's pixel font for a game font: <c>fonts/&lt;GAME|BOOK&gt;.bdf</c> beside the PO file.</summary>
        public static string FontPathFor(string overridePath, string locale, string fontId) =>
            Path.Combine(overridePath ?? string.Empty, "Lang", locale, "fonts",
                Path.GetFileNameWithoutExtension(fontId) + ".bdf");

        /// <summary>The active pack's pixel font for a game font, wherever the pack is installed.</summary>
        public static string ActiveFontPath(string fontId) => FontPathFor(RootFor(Current.Locale), Current.Locale, fontId);

        /// <summary>
        /// Give <paramref name="font"/> every letter the active pack's text uses (TASK-778): first the
        /// pack's own BDF, if it has one, then whatever is still missing composed from the font's
        /// own letters (<see cref="GlyphSynthesis"/>). Like the text, a broken font file is a
        /// warning, never a failed start.
        /// </summary>
        public static void MergeFont(FontResource font) {
            if (Current == LanguagePack.English || font.PixelFormat != FontPixelFormat.Monochrome) {
                return;
            }
            string path = ActiveFontPath(font.Id);
            if (File.Exists(path)) {
                try {
                    using var reader = new StreamReader(path);
                    var clipped = ResourceExtraction.Text.BdfFont.MergeInto(font, reader);
                    Debug.Log($"Language pack font {path}: {font.ExtraGlyphs.Count} glyphs merged into {font.Id}.");
                    if (clipped.Count > 0) {
                        Debug.LogWarning($"{path}: ink outside {font.Id}'s {font.Height}-row cell was clipped for "
                            + string.Join(", ", System.Linq.Enumerable.Select(clipped, c => $"U+{c:X4}")) + ".");
                    }
                } catch (Exception e) {
                    Debug.LogWarning($"Language pack font {path} could not be read ({e.Message}).");
                }
            }
            int before = font.ExtraGlyphs.Count;
            var missing = GlyphSynthesis.AddComposed(font, Current.Characters());
            Debug.Log($"{font.Id}: composed {font.ExtraGlyphs.Count - before} letters for '{Current.Locale}'.");
            if (missing.Count > 0) {
                Debug.LogWarning($"{font.Id} cannot draw "
                    + string.Join(" ", System.Linq.Enumerable.Select(missing, c => $"'{char.ConvertFromUtf32(c)}' U+{c:X4}"))
                    + $"; add them to the pack's fonts/{System.IO.Path.GetFileNameWithoutExtension(font.Id)}.bdf.");
            }
        }

        private static LanguagePack Load(string locale) {
            if (string.IsNullOrEmpty(locale) || locale == LanguagePack.English.Locale) {
                return LanguagePack.English;
            }
            if (locale == PseudoLocalization.Locale) {
                return LanguagePack.Pseudo; // built in: no file (TASK-780)
            }
            string root = RootFor(locale);
            string path = PathFor(root, locale);
            if (root == null) {
                Debug.LogWarning($"Language '{locale}' selected but no pack in the override folder or a language mod; using English.");
                return LanguagePack.English;
            }
            try {
                using var reader = new StreamReader(path);
                LanguagePack pack = ResourceExtraction.Text.PoLanguagePack.Read(reader, locale);
                Debug.Log($"Language pack '{locale}': {pack.TranslatedCount} translated strings from {path}.");
                return pack;
            } catch (Exception e) {
                Debug.LogWarning($"Language pack at {path} could not be read ({e.Message}); using English.");
                return LanguagePack.English;
            }
        }
    }
}
