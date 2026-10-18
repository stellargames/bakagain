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

    /// <summary>The main menu's V banner: "Version <c>{version}</c>" — the port's own version where
    /// the original printed its EXE's "Version 1.02 CD" (VersionBanner).</summary>
    public const string VersionBannerKey = "port:template:version_banner";

    /// <summary>The Preferences screen's bug-report line: "BaK-Again <c>{version}</c>   Log: <c>{log}</c>".</summary>
    public const string BuildInfoKey = "port:template:build_info";

    /// <summary>"<c>{n}</c>%" — every percentage the port builds; "94 %" in a language that spaces it.</summary>
    public const string PercentKey = "port:template:percent";

    /// <summary><paramref name="n"/> as a percentage, through <see cref="PercentKey"/>.</summary>
    /// <remarks>The combat panels redraw every frame, so the untranslated "{n}%" with a whole number
    /// skips MessageFormat: same text, no allocation of a value table per call.</remarks>
    public static string Percent(object n) =>
        n is int whole && (!UiStrings.Catalog.TryGet(PercentKey, out string template) || template == EnglishPercent)
            ? whole.ToString(CultureInfo.InvariantCulture) + "%"
            : Format(PercentKey, ("n", n));

    private const string EnglishPercent = "{n}%";

    /// <summary>The Preferences screen's language button (TASK-782): "Language: <c>{name}</c>".</summary>
    public const string LanguageChoiceKey = "port:template:language_choice";

    /// <summary>Once a different language is chosen: "<c>{name}</c> (restart)" — it applies when the
    /// game next starts, and the shorter wording still fits the button.</summary>
    public const string LanguageChoicePendingKey = "port:template:language_choice_pending";

    /// <summary>A pack its header marks <c>X-Status: beta</c>, named on the language button:
    /// "<c>{name}</c> (beta)" — playable, not yet reviewed.</summary>
    public const string LanguageBetaKey = "port:template:language_beta";

    /// <summary>The Preferences screen's Enhanced button: "Enhanced: <c>{state}</c>".</summary>
    public const string EnhancedButtonKey = "port:template:enhanced_button";
    public const string EnhancedStateOn = "port:template:enhanced_state_on";
    public const string EnhancedStateOff = "port:template:enhanced_state_off";
    public const string EnhancedFeatureFullScreen = "port:template:enhanced_feature_full_screen";
    public const string EnhancedFeatureMouseLook = "port:template:enhanced_feature_mouse_look";
    public const string EnhancedFeatureRings = "port:template:enhanced_feature_rings";
    public const string EnhancedFeatureBookFont = "port:template:enhanced_feature_book_font";
    public const string EnhancedFeaturePageTurn = "port:template:enhanced_feature_page_turn";
    public const string EnhancedFeatureInventory = "port:template:enhanced_feature_inventory";
    public const string EnhancedFeatureSky = "port:template:enhanced_feature_sky";
    public const string EnhancedFeatureLighting = "port:template:enhanced_feature_lighting";
    public const string EnhancedFeatureCasting = "port:template:enhanced_feature_casting";

    /// <summary>The Enhanced settings screen: its title and headings, the game-files and mods folders
    /// (the start-up prompt's fields, kept here too), and Save/Cancel.</summary>
    public const string EnhancedSettingsTitle = "port:template:enhanced_settings_title";
    public const string EnhancedFeaturesHeading = "port:template:enhanced_features_heading";
    public const string EnhancedFoldersHeading = "port:template:enhanced_folders_heading";
    public const string EnhancedGameFolder = "port:template:enhanced_game_folder";
    public const string EnhancedMods = "port:template:enhanced_mods";
    public const string EnhancedModsFolder = "port:template:enhanced_mods_folder";
    public const string EnhancedBrowse = "port:template:enhanced_browse";
    public const string EnhancedNotSet = "port:template:enhanced_not_set";
    public const string EnhancedBadGameFolder = "port:template:enhanced_bad_game_folder";
    public const string EnhancedSave = "port:template:enhanced_save";
    public const string EnhancedCancel = "port:template:enhanced_cancel";

    /// <summary>The Enhanced cast screen's Exit stone; the faithful button carries the word in its image.</summary>
    public const string EnhancedCastExit = "port:template:enhanced_cast_exit";

    /// <summary>The Enhanced cast screen's hint line, for picking a spell and for choosing its power,
    /// by mouse and by finger; and the "of {max}" under the power readout in the ring.</summary>
    public const string EnhancedCastHintPick = "port:template:enhanced_cast_hint_pick";
    public const string EnhancedCastHintPickTouch = "port:template:enhanced_cast_hint_pick_touch";
    public const string EnhancedCastHintPower = "port:template:enhanced_cast_hint_power";
    public const string EnhancedCastHintPowerTouch = "port:template:enhanced_cast_hint_power_touch";
    public const string EnhancedCastPowerOf = "port:template:enhanced_cast_power_of";

    /// <summary>The Enhanced cast screen's Cast and Back tablets, shown while a power is chosen, and the
    /// caster's two pools under their portrait.</summary>
    public const string EnhancedCastCast = "port:template:enhanced_cast_cast";
    public const string EnhancedCastBack = "port:template:enhanced_cast_back";
    public const string EnhancedCastHealth = "port:template:enhanced_cast_health";
    public const string EnhancedCastStamina = "port:template:enhanced_cast_stamina";

    /// <summary>The Enhanced inventory's buttons and labels. Labels name people, never items: a name
    /// is what examining an item reveals.</summary>
    public const string EnhancedInvGiveTo = "port:template:enhanced_inv_give_to";
    public const string EnhancedInvTakes = "port:template:enhanced_inv_takes";
    public const string EnhancedInvBuyFor = "port:template:enhanced_inv_buy_for";
    public const string EnhancedInvHasIt = "port:template:enhanced_inv_has_it";
    public const string EnhancedInvBack = "port:template:enhanced_inv_back";

    /// <summary>An Enhanced caption whose choice waits for the next start (the font is bound at
    /// start): "<c>{text}</c> (restart)", as <see cref="LanguageChoicePendingKey"/>.</summary>
    public const string EnhancedPendingKey = "port:template:enhanced_pending";

    /// <summary>The Enhanced travel HUD's status line: "Day <c>{day}</c> · <c>{hour}</c>:00".</summary>
    public const string EnhancedStatusKey = "port:template:enhanced_status";

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

    /// <summary>"<c>{using}</c>, <c>{state}</c>" — an item's status line when it is both in use and
    /// broken or repairable; INVINSP.C:377-383 strcat's the ", " (TASK-775).</summary>
    public const string ItemUsingAndState = "port:template:item_using_and_state";

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
        (LanguageBetaKey, _ => "{name} (beta)"),
        (EnhancedButtonKey, _ => "Enhanced: {state}"),
        (EnhancedStateOn, _ => "On"),
        (EnhancedStateOff, _ => "Off"),
        (EnhancedFeatureFullScreen, _ => "Full-screen travel"),
        (EnhancedFeatureMouseLook, _ => "Mouse and touch look"),
        (EnhancedFeatureRings, _ => "Health rings"),
        (EnhancedFeatureBookFont, _ => "Book font"),
        (EnhancedFeaturePageTurn, _ => "Turning pages"),
        (EnhancedFeatureInventory, _ => "Inventory"),
        (EnhancedFeatureSky, _ => "Sky"),
        (EnhancedFeatureLighting, _ => "Lighting"),
        (EnhancedFeatureCasting, _ => "Casting"),
        (EnhancedSettingsTitle, _ => "Enhanced"),
        (EnhancedFeaturesHeading, _ => "Features"),
        (EnhancedFoldersHeading, _ => "Game files and mods"),
        (EnhancedGameFolder, _ => "Game files"),
        (EnhancedMods, _ => "Mods"),
        (EnhancedModsFolder, _ => "Mods folder"),
        (EnhancedBrowse, _ => "Browse"),
        (EnhancedNotSet, _ => "not set"),
        (EnhancedBadGameFolder, _ => "That folder does not hold the game's files."),
        (EnhancedSave, _ => "Save"),
        (EnhancedCancel, _ => "Cancel"),
        (EnhancedCastExit, _ => "Exit"),
        (EnhancedCastHintPick, _ => "Point at a symbol to read it; click it to cast."),
        (EnhancedCastHintPickTouch, _ => "Touch a symbol to read it; lift your finger on it to cast."),
        (EnhancedCastHintPower, _ => "Point at a lit gem for the power, then click it or Cast. Back returns to the spells."),
        (EnhancedCastHintPowerTouch, _ => "Slide along the lit gems for the power, then Cast. Back returns to the spells."),
        (EnhancedCastPowerOf, _ => "of {max}"),
        (EnhancedCastCast, _ => "Cast"),
        (EnhancedCastBack, _ => "Back"),
        (EnhancedCastHealth, _ => "Health {current}/{max}"),
        (EnhancedCastStamina, _ => "Stamina {current}/{max}"),
        (EnhancedInvGiveTo, _ => "Give to {name}"),
        (EnhancedInvTakes, _ => "{name} takes it"),
        (EnhancedInvBuyFor, _ => "Buy for {name}"),
        (EnhancedInvHasIt, _ => "Has it"),
        (EnhancedInvBack, _ => "Back to the pack"),
        (EnhancedPendingKey, _ => "{text} (restart)"),
        (EnhancedStatusKey, _ => "Day {day} · {hour}:00"),
        (PercentKey, _ => EnglishPercent),
        (TouchMove, _ => "Move"),
        (TouchCast, _ => "Cast"),
        (TouchCastHere, _ => "Cast here"),
        (VersionBannerKey, _ => "Version {version}"),
        (BuildInfoKey, _ => "BaK-Again {version}   Log: {log}"),
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
        (ItemUsingAndState, _ => "{using}, {state}"),
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
            // gettext writes pt_BR, .NET knows pt-BR.
            return CultureInfo.GetCultureInfo(string.IsNullOrEmpty(locale) ? "en" : locale.Replace('_', '-'));
        } catch (CultureNotFoundException) {
            return CultureInfo.InvariantCulture;
        }
    }
}
