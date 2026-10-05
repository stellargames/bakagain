namespace GameData.Resources.Text;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// The languages a player can choose (TASK-782): English, every installed pack, and the pseudo
/// language in development builds.
/// </summary>
public static class LanguageChoice {
    /// <summary>English first, then <paramref name="installed"/> pack locales in order.</summary>
    public static IReadOnlyList<string> Available(IEnumerable<string> installed, bool development) {
        var all = new List<string> { LanguagePack.English.Locale };
        all.AddRange(installed
            .Where(l => !string.IsNullOrEmpty(l) && l != LanguagePack.English.Locale && l != PseudoLocalization.Locale)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase));
        if (development) {
            all.Add(PseudoLocalization.Locale);
        }
        return all;
    }

    /// <summary>The language after <paramref name="current"/>, wrapping; English for one not listed.</summary>
    public static string Next(IReadOnlyList<string> available, string current) {
        int at = -1;
        for (int i = 0; i < available.Count; i++) {
            if (string.Equals(available[i], current, StringComparison.OrdinalIgnoreCase)) {
                at = i;
            }
        }
        return at < 0 ? available[0] : available[(at + 1) % available.Count];
    }

    /// <summary>A language's name in that language ("Nederlands"), or its code when unknown.</summary>
    public static string DisplayName(string locale) {
        if (locale == PseudoLocalization.Locale) {
            return "Pseudo";
        }
        try {
            CultureInfo culture = CultureInfo.GetCultureInfo(locale);
            if (culture.CultureTypes.HasFlag(CultureTypes.UserCustomCulture) || culture.ThreeLetterISOLanguageName == "ivl"
                || string.IsNullOrEmpty(culture.NativeName) || culture.NativeName == locale) {
                return locale;
            }
            string name = culture.NativeName;
            return char.ToUpper(name[0], culture) + name.Substring(1);
        } catch (CultureNotFoundException) {
            return locale;
        }
    }
}
