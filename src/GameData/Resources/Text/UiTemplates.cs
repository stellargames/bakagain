namespace GameData.Resources.Text;

using Jeffijoe.MessageFormat;
using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Sentences the port assembles from pieces, as ICU MessageFormat templates (TASK-776) — so a
/// translation can put the name after the verb, or pluralise a count, which concatenation cannot.
/// </summary>
/// <remarks>
/// <b>The English is still the original's pieces.</b> Each template is composed from the
/// catalog's own EXE strings, quoted so they read as literal text, which keeps English output
/// byte-identical and the game's text out of the repository. The composed templates are catalog
/// entries under <c>port:template:</c>, so a language pack translates them and the POT lists them.
/// Formatting is jeffijoe's MessageFormat.NET (MIT): CLDR plural rules for the catalog's locale.
/// </remarks>
public static class UiTemplates {
    /// <summary>"<c>{name}</c> asked about:" — the keyword prompt's heading (<c>KeywordPrompt</c>).</summary>
    public const string AskedAbout = "port:template:asked_about";

    /// <summary>"<c>{count}</c> quarrels remaining" — the shoot panel's no-target line.</summary>
    public const string QuarrelsRemaining = "port:template:quarrels_remaining";

    /// <summary>"<c>{current}</c> of <c>{max}</c>" — the camp screen's health and stamina.</summary>
    public const string CurrentOfMax = "port:template:current_of_max";

    private static readonly (string Key, Func<UiStringCatalog, string> English)[] Templates = {
        (AskedAbout, c => "{name}" + Literal(c.Get("base:uistring:dialog.asked_about_suffix"))),
        (QuarrelsRemaining, c => "{count} " + Literal(c.Get("base:uistring:combat.quarrels_remaining"))),
        (CurrentOfMax, c => "{current}" + Literal(c.Get("base:uistring:encamp.current_of_max_separator")) + "{max}"),
    };

    /// <summary>Whether a catalog key is one of these templates (its text is MessageFormat, not plain).</summary>
    public static bool IsTemplate(string key) => key.StartsWith("port:template:", StringComparison.Ordinal);

    private static readonly MessageFormatter Formatter = new MessageFormatter(true, CultureInfo.InvariantCulture, null);

    /// <summary>The templates' English, composed from <paramref name="exe"/>'s pieces.</summary>
    public static IEnumerable<KeyValuePair<string, string>> EnglishFor(UiStringCatalog exe) {
        foreach ((string key, Func<UiStringCatalog, string> english) in Templates) {
            yield return new KeyValuePair<string, string>(key, english(exe));
        }
    }

    /// <summary><paramref name="key"/>'s template from the ambient catalog, filled in.</summary>
    public static string Format(string key, params (string Name, object Value)[] args) =>
        Format(UiStrings.Catalog, key, args);

    /// <summary><paramref name="key"/>'s template from <paramref name="catalog"/>, filled in.</summary>
    /// <remarks>A catalog without the template (a bare mod catalog) composes it from its own
    /// pieces; a template that will not parse shows as its raw text rather than throwing.</remarks>
    public static string Format(UiStringCatalog catalog, string key, params (string Name, object Value)[] args) {
        if (!catalog.TryGet(key, out string template) || string.IsNullOrEmpty(template)) {
            template = Array.Find(Templates, t => t.Key == key).English?.Invoke(catalog) ?? string.Empty;
        }
        var values = new Dictionary<string, object?>();
        foreach ((string name, object value) in args) {
            values[name] = value;
        }
        try {
            return Formatter.FormatMessage(template, values, Culture(catalog.Locale));
        } catch (Exception) {
            return template;
        }
    }

    /// <summary>Text as a MessageFormat literal: apostrophes doubled, braces quoted.</summary>
    private static string Literal(string text) =>
        text.Replace("'", "''").Replace("{", "'{'").Replace("}", "'}'");

    private static CultureInfo Culture(string locale) {
        try {
            return CultureInfo.GetCultureInfo(string.IsNullOrEmpty(locale) ? "en" : locale);
        } catch (CultureNotFoundException) {
            return CultureInfo.InvariantCulture;
        }
    }
}
