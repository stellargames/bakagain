namespace BakAgain.ResourceManagement {
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

        /// <summary>The pack file a locale is read from, under the override folder.</summary>
        public static string PathFor(string overridePath, string locale) =>
            Path.Combine(overridePath ?? string.Empty, "Lang", locale, locale + ".po");

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
