namespace ResourceExtraction.Text;

using GameData.Resources.Text;
using Karambolo.PO;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Reads a language pack from a gettext PO file (TASK-773) — the standard format, through an
/// existing parser (Karambolo.PO), so translators use Poedit/Weblate/Crowdin and we keep no format of
/// our own.
/// </summary>
/// <remarks>
/// gettext's runtime rules: the entry's <c>msgctxt</c> is its <see cref="TextKey"/>, an empty
/// <c>msgstr</c> is "not translated yet", and a <c>#, fuzzy</c> entry is a translator's unreviewed
/// guess and is not used. The locale is the header's <c>Language</c>.
/// </remarks>
public static class PoLanguagePack {
    public static LanguagePack Read(TextReader reader, string? locale = null) {
        POParseResult result = new POParser(new POParserSettings()).Parse(reader);
        if (!result.Success) {
            throw new InvalidDataException("Not a readable PO file: " + string.Join("; ",
                result.Diagnostics.Select(d => d.ToString())));
        }

        POCatalog catalog = result.Catalog;
        var text = new Dictionary<string, string>();
        foreach (IPOEntry entry in catalog) {
            if (string.IsNullOrEmpty(entry.Key.ContextId) || IsFuzzy(entry)) {
                continue;
            }
            string translation = entry.Count > 0 ? entry[0] : string.Empty;
            if (!string.IsNullOrEmpty(translation)) {
                text[entry.Key.ContextId] = translation;
            }
        }
        // The pack's own folder names it (Lang/<locale>/); the header's Language is a PO editor's
        // guess ("nl_NL", or absent), so it only stands in when the caller has no locale.
        return new LanguagePack(locale ?? catalog.Language ?? string.Empty, text, Capitals(catalog));
    }

    /// <summary>
    /// The pack's own illuminated capitals (TASK-827): an extension header, <c>X-Drop-Caps: E=19, W=20</c>,
    /// naming the letter each added BMX/BOOK/&lt;n&gt;.png draws. Unparseable pairs are skipped.
    /// </summary>
    private static Dictionary<char, int> Capitals(POCatalog catalog) {
        var capitals = new Dictionary<char, int>();
        if (catalog.Headers == null || !catalog.Headers.TryGetValue("X-Drop-Caps", out string? value) || value == null) {
            return capitals;
        }
        foreach (string pair in value.Split(',')) {
            string[] parts = pair.Split('=');
            if (parts.Length == 2 && parts[0].Trim().Length == 1 && int.TryParse(parts[1].Trim(), out int picture)) {
                capitals[parts[0].Trim()[0]] = picture;
            }
        }
        return capitals;
    }

    private static bool IsFuzzy(IPOEntry entry) =>
        entry.Comments?.OfType<POFlagsComment>().Any(c => c.Flags.Contains("fuzzy")) == true;
}
