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

    /// <summary>"Cancel" — the party picker's last row; no EXE entry carries it alone.</summary>
    public const string PartyPickerCancel = "port:template:party_picker_cancel";

    /// <summary>"GoodBye" — the ask-about menu's farewell, a literal in ASKABOUT.C:310.</summary>
    public const string Farewell = "port:template:farewell";

    /// <summary>"OK" — the button of a dialog record with no branches.</summary>
    public const string Ok = "port:template:ok";

    /// <summary>"Give: <c>{n}</c>", with " (All)" at the maximum — INVINSP.C:90-98, from the
    /// EXE's own pieces. <c>{max}</c> is <c>yes</c> or <c>no</c>.</summary>
    public const string QuantityGive = "port:template:quantity_give";

    // The boot screens, shown before the game data is found (TASK-775, decision 5: one system).
    public const string BootLoading = "port:template:boot_loading";
    public const string BootDataFiles = "port:template:boot_data_files";
    public const string BootEnableOverrides = "port:template:boot_enable_overrides";
    public const string BootOverrideDirectory = "port:template:boot_override_directory";
    public const string BootGameDirectory = "port:template:boot_game_directory";
    public const string BootContinue = "port:template:boot_continue";

    // The touch controls' own words; the original had no touch screen.
    public const string TouchMove = "port:template:touch_move";
    public const string TouchCast = "port:template:touch_cast";
    public const string TouchCastHere = "port:template:touch_cast_here";

    /// <summary>"<c>{n}</c>%" — every percentage the port builds; "94 %" in a language that spaces it.</summary>
    public const string PercentKey = "port:template:percent";

    /// <summary><paramref name="n"/> as a percentage, through <see cref="PercentKey"/>.</summary>
    public static string Percent(object n) => Format(PercentKey, ("n", n));

    /// <summary>The Preferences screen's language button (TASK-782): "Language: <c>{name}</c>".</summary>
    public const string LanguageChoiceKey = "port:template:language_choice";

    /// <summary>Once a different language is chosen: "<c>{name}</c> (restart)" — it applies when the
    /// game next starts, and the shorter wording still fits the button.</summary>
    public const string LanguageChoicePendingKey = "port:template:language_choice_pending";

    /// <summary>
    /// An amount in prose (TASK-777): <c>{case}</c> is <c>sovereigns</c>, <c>royals</c> or <c>both</c>,
    /// <c>{s}</c> and <c>{r}</c> the counts, each a CLDR plural in the pack's own language. English
    /// is never formatted from it — the original's quirks ("0 sovereign") stay in MoneyFormatter —
    /// it is the translator's reference.
    /// </summary>
    public const string MoneyProse = "port:template:money_prose";

    /// <summary>Whether <paramref name="catalog"/> is English, whose grammar is the original's code.</summary>
    public static bool IsEnglish(UiStringCatalog catalog) =>
        string.IsNullOrEmpty(catalog.Locale) || catalog.Locale == "en" || catalog.Locale.StartsWith("en-", StringComparison.Ordinal);

    /// <summary><paramref name="pattern"/> — a translator's own MessageFormat text — filled in, in the
    /// ambient catalog's language; the text itself when it will not parse.</summary>
    public static string FormatPattern(string pattern, params (string Name, object Value)[] args) {
        var values = new Dictionary<string, object?>();
        foreach ((string name, object value) in args) {
            values[name] = value;
        }
        try {
            return Formatter.FormatMessage(pattern, values, Culture(UiStrings.Catalog.Locale));
        } catch (Exception) {
            return pattern;
        }
    }

    /// <summary>CHEAT CENTRAL's subtitle.</summary>
    public const string CheatCentralSubtitle = "port:template:cheat_central_subtitle";

    private static readonly (string Key, Func<UiStringCatalog, string> English)[] Templates = {
        (AskedAbout, c => "{name}" + Literal(c.Get("base:uistring:dialog.asked_about_suffix"))),
        (QuarrelsRemaining, c => "{count} " + Literal(c.Get("base:uistring:combat.quarrels_remaining"))),
        (CurrentOfMax, c => "{current}" + Literal(c.Get("base:uistring:encamp.current_of_max_separator")) + "{max}"),
        // Port text with no EXE source: the English lives here, and a pack translates it by key.
        (PartyPickerCancel, _ => "Cancel"),
        (MoneyProse, _ => "{case, select, royals {{r, plural, one {# royal} other {# royals}}} "
            + "sovereigns {{s, plural, one {# sovereign} other {# sovereigns}}} "
            + "other {{s, plural, one {# sovereign} other {# sovereigns}} and {r, plural, one {# royal} other {# royals}}}}"),
        (LanguageChoiceKey, _ => "Language: {name}"),
        (LanguageChoicePendingKey, _ => "{name} (restart)"),
        (PercentKey, _ => "{n}%"),
        (TouchMove, _ => "Move"),
        (TouchCast, _ => "Cast"),
        (TouchCastHere, _ => "Cast here"),
        (BootLoading, _ => Literal("Loading Betrayal at Krondor... please wait.")),
        (BootDataFiles, _ => Literal("This game requires the original data files of the \"Betrayal at Krondor\" game. "
            + "If you do not have a copy of that game you can buy one at "
            + "<a href=\"https://www.gog.com/game/betrayal_at_krondor\"><u>GOG</u> (https://www.gog.com/game/betrayal_at_krondor)</a>"
            + "\n\nIt is possible to customize/mod/override all the game data by placing files in an override folder.")),
        (BootEnableOverrides, _ => "Enable overrides"),
        (BootOverrideDirectory, _ => "Override directory"),
        (BootGameDirectory, _ => "Game files directory"),
        (BootContinue, _ => "Continue"),
        (Farewell, _ => "GoodBye"),
        (Ok, _ => "OK"),
        (QuantityGive, c => Literal(c.Get("base:uistring:quantity.give_prefix")) + "{n}{max, select, yes {"
            + Literal(c.Get("base:uistring:quantity.all_suffix")) + "} other {}}"),
        (CheatCentralSubtitle, _ => Literal("Enjoy with caution...")),
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
