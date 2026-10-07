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

    /// <param name="measure">The English's width in game-screen pixels (the game font), for an
    /// entry with a <see cref="TextEntry.Room"/>; null leaves the room out.</param>
    public static void Write(IEnumerable<TextEntry> entries, TextWriter writer, System.Func<string, int>? measure = null) {
        var catalog = new POCatalog { Encoding = "UTF-8", Language = string.Empty };
        foreach (TextEntry entry in entries) {
            var comments = new List<POComment> {
                new POReferenceComment { References = new List<POSourceReference> { new(entry.Source, 0) } },
            };
            if (entry.Room is int room && measure?.Invoke(entry.Text) is int english && english > 0) {
                // A budget in characters is what a translator can act on; it is the English's own
                // average glyph, so it is "about". The caption shrinks to 60% before it overflows.
                int fit = entry.Text.Length * room / english;
                comments.Add(new POExtractedComment {
                    Text = $"One line, {room} px wide. The English takes {english} px: about {fit} characters fit.",
                });
            }
            if (entry.Key.StartsWith(GameData.Resources.Text.LetterHotkeys.Prefix, System.StringComparison.Ordinal)) {
                comments.Add(new POExtractedComment {
                    Text = "The key that presses this letter's buttons: one character. Keys left empty stay English; a letter moved away stops working.",
                });
            }
            if (entry.Key.StartsWith(GameData.Resources.Dialog.GermanCaseCodes.Prefix, System.StringComparison.Ordinal)) {
                comments.Add(new POExtractedComment {
                    Text = "Grammar of this creature's or item's name, for dialog codes like @d @1as (see language-packs.md). "
                        + "gender: m, f, n, pl (a plural noun) or name (no article: a person, \"Annas Buch\"). def: the form after der/die/das. acc, dat: the accusative and dative. pl: the plural. "
                        + "Leave empty where the name itself is right, or in a language without these codes.",
                });
            }
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
