namespace ResourceExtraction.Text;

using Karambolo.PO;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// Writes the translator's template — a gettext POT — from the player's own English (TASK-783).
/// </summary>
/// <remarks>
/// Generated on the player's machine because the English is the game's text and cannot be
/// committed. One entry per <see cref="TextEntry"/>: <c>msgctxt</c> is the key, <c>msgid</c> the
/// English, and a <c>#:</c> reference names the resource it came from. Poedit, Weblate and Crowdin
/// open it as it is, and their own statistics are the coverage report.
/// </remarks>
public static class PotTemplate {
    /// <summary>A conversion <see cref="GameData.Resources.Text.CFormat"/> fills.</summary>
    private static readonly Regex PrintfConversion = new Regex(@"%(\d+\$)?[lFh]*[dscu]");

    public static void Write(IEnumerable<TextEntry> entries, TextWriter writer) {
        var catalog = new POCatalog { Encoding = "UTF-8", Language = string.Empty };
        foreach (TextEntry entry in entries) {
            var comments = new List<POComment> {
                new POReferenceComment { References = new List<POSourceReference> { new(entry.Source, 0) } },
            };
            if (GameData.Resources.Text.UiTemplates.IsTemplate(entry.Key)) {
                // Weblate's flag for ICU MessageFormat: it then checks placeholders and plurals.
                comments.Add(new POFlagsComment { Flags = new HashSet<string> { "icu-message-format" } });
            } else if (PrintfConversion.IsMatch(entry.Text)) {
                // gettext's own flag: the tools then check a translation keeps the conversions,
                // and accept them reordered as %2$d (CFormat reads both).
                comments.Add(new POFlagsComment { Flags = new HashSet<string> { "c-format" } });
            }
            catalog.Add(new POSingularEntry(new POKey(entry.Text, contextId: entry.Key)) {
                Translation = string.Empty,
                Comments = comments,
            });
        }
        // The header declares UTF-8; the writer's own encoding is the caller's business (a file is
        // written as UTF-8, a test reads a string).
        new POGenerator(new POGeneratorSettings { IgnoreLongLines = true, IgnoreEncoding = true })
            .Generate(writer, catalog);
    }
}
