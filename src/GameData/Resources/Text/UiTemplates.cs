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

    // The game-files setup wizard (TASK-1014).
    public const string BootStepOf = "port:template:boot_step_of";
    public const string BootFrom = "port:template:boot_from";
    public const string BootFromFiles = "port:template:boot_from_files";
    public const string BootWelcomeTitle = "port:template:boot_welcome_title";
    public const string BootWelcomeBody = "port:template:boot_welcome_body";
    public const string BootNoGame = "port:template:boot_no_game";
    public const string BootFindFiles = "port:template:boot_find_files";
    public const string BootWhereLink = "port:template:boot_where_link";
    public const string BootBuyLink = "port:template:boot_buy_link";
    public const string BootStuckLink = "port:template:boot_stuck_link";
    public const string BootChooseFolderTitle = "port:template:boot_choose_folder_title";
    public const string BootChooseFolderBodyAndroid = "port:template:boot_choose_folder_body_android";
    public const string BootChooseFolderBodyShortAndroid = "port:template:boot_choose_folder_body_short_android";
    public const string BootDownloadNote = "port:template:boot_download_note";
    public const string BootChooseFolder = "port:template:boot_choose_folder";
    public const string BootPickOtherAndroid = "port:template:boot_pick_other_android";
    public const string BootPhoneHelpLink = "port:template:boot_phone_help_link";
    public const string BootFoundTitle = "port:template:boot_found_title";
    public const string BootFoundBody = "port:template:boot_found_body";
    public const string BootUseThis = "port:template:boot_use_this";
    public const string BootChooseAnother = "port:template:boot_choose_another";
    public const string BootChooseFolderBodyDesktop = "port:template:boot_choose_folder_body_desktop";
    public const string BootCaseNote = "port:template:boot_case_note";
    public const string BootBrowse = "port:template:boot_browse";
    public const string BootMissing = "port:template:boot_missing";
    public const string BootMissingHint = "port:template:boot_missing_hint";
    public const string BootFoundCount = "port:template:boot_found_count";
    public const string BootDamaged = "port:template:boot_damaged";
    public const string BootEmpty = "port:template:boot_empty";
    public const string BootDamagedInPlace = "port:template:boot_damaged_in_place";
    public const string BootOkCount = "port:template:boot_ok_count";
    public const string BootNotAZip = "port:template:boot_not_a_zip";
    public const string BootCopyErrorFound = "port:template:boot_copy_error_found";
    public const string BootInstallerUnreadable = "port:template:boot_installer_unreadable";
    public const string BootInstallerUnreadableAndroid = "port:template:boot_installer_unreadable_android";
    public const string BootNothingHere = "port:template:boot_nothing_here";
    public const string BootCopyFailed = "port:template:boot_copy_failed";
    public const string BootCopyFailedDesktop = "port:template:boot_copy_failed_desktop";
    public const string BootFolder = "port:template:boot_folder";
    public const string BootZipDamaged = "port:template:boot_zip_damaged";
    public const string BootZipTooLarge = "port:template:boot_zip_too_large";
    public const string BootFoundBodyOther = "port:template:boot_found_body_other";
    public const string BootPermission = "port:template:boot_permission";
    public const string BootCopying = "port:template:boot_copying";
    public const string BootCopyError = "port:template:boot_copy_error";
    public const string BootCopyErrorDesktop = "port:template:boot_copy_error_desktop";
    public const string BootNoFolderPicker = "port:template:boot_no_folder_picker";
    public const string BootNoFilePicker = "port:template:boot_no_file_picker";
    public const string BootChooseAgain = "port:template:boot_choose_again";
    public const string BootAllSet = "port:template:boot_all_set";
    public const string BootCopiedNote = "port:template:boot_copied_note";
    public const string BootPlay = "port:template:boot_play";
    public const string BootBack = "port:template:boot_back";
    public const string BootHelpTitleAndroid = "port:template:boot_help_title_android";
    public const string BootHelpTitleDesktop = "port:template:boot_help_title_desktop";
    public const string BootHelpBodyAndroid = "port:template:boot_help_body_android";
    public const string BootHelpBodyWindows = "port:template:boot_help_body_windows";
    public const string BootHelpBodyLinux = "port:template:boot_help_body_linux";
    public const string BootMoreHelpWeb = "port:template:boot_more_help_web";
    public const string BootStuckTitle = "port:template:boot_stuck_title";
    public const string BootStuckBody = "port:template:boot_stuck_body";
    public const string BootAskForum = "port:template:boot_ask_forum";
    public const string BootShareLog = "port:template:boot_share_log";
    public const string BootOpenLogFolder = "port:template:boot_open_log_folder";
    public const string BootCopied = "port:template:boot_copied";
    public const string BootVersionCopy = "port:template:boot_version_copy";
    public const string BootVersion = "port:template:boot_version";
    public const string BootExtracting = "port:template:boot_extracting";
    public const string BootInstallerDamaged = "port:template:boot_installer_damaged";

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
    public const string EnhancedFeatureContinuousMovement = "port:template:enhanced_feature_continuous_movement";

    /// <summary>The Enhanced settings' shadows row: "Shadows: <c>{state}</c>", Off (the On/Off "Off"),
    /// Blobs or Sun.</summary>
    public const string EnhancedShadowsKey = "port:template:enhanced_shadows";
    public const string EnhancedShadowsBlobs = "port:template:enhanced_shadows_blobs";
    public const string EnhancedShadowsSun = "port:template:enhanced_shadows_sun";

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

    /// <summary>The Mods screen (mod system spec 4.6): a row per installed mod, "[<c>{on}</c>] <c>{name}</c>
    /// <c>{version}</c>", its buttons and a line per problem (<c>{other}</c>: the mod or game version it
    /// names); and the missing-mods dialog on load, a line per mod the save was made with.</summary>
    public const string ModsTitle = "port:template:mods_title";
    public const string ModsInstalled = "port:template:mods_installed";
    public const string ModsRow = "port:template:mods_row";
    public const string ModsUp = "port:template:mods_up";
    public const string ModsDown = "port:template:mods_down";
    public const string ModsUninstall = "port:template:mods_uninstall";
    public const string ModsUninstallConfirm = "port:template:mods_uninstall_confirm";
    public const string ModsInstallFile = "port:template:mods_install_file";
    public const string ModsInboxHint = "port:template:mods_inbox_hint";
    public const string ModsInstallFailed = "port:template:mods_install_failed";
    public const string ModsProblemMissingDependency = "port:template:mods_problem_missing_dependency";
    public const string ModsProblemDependencyVersion = "port:template:mods_problem_dependency_version";
    public const string ModsProblemConflict = "port:template:mods_problem_conflict";
    public const string ModsProblemGameVersion = "port:template:mods_problem_game_version";
    public const string ModsNone = "port:template:mods_none";
    public const string ModsMissingTitle = "port:template:mods_missing_title";
    public const string ModsMismatchTitle = "port:template:mods_mismatch_title";
    public const string ModsMissingLine = "port:template:mods_missing_line";
    public const string ModsMajorLine = "port:template:mods_major_line";
    public const string ModsLoadAnyway = "port:template:mods_load_anyway";
    public const string ModsCancel = "port:template:mods_cancel";

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

    /// <summary>The caster's two lines on one, under the portrait: <c>{health}</c> and <c>{stamina}</c> are those templates.</summary>
    public const string EnhancedCastPool = "port:template:enhanced_cast_pool";

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

    /// <summary>The status line at a town: <c>{place}</c>, the town's name, before <c>{status}</c>, the line above.</summary>
    public const string EnhancedStatusAtKey = "port:template:enhanced_status_at";

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
        (EnhancedFeatureContinuousMovement, _ => "Continuous walking"),
        (EnhancedShadowsKey, _ => "Shadows: {state}"),
        (EnhancedShadowsBlobs, _ => "Blobs"),
        (EnhancedShadowsSun, _ => "Sun"),
        (EnhancedSettingsTitle, _ => "Enhanced"),
        (EnhancedFeaturesHeading, _ => "Features"),
        (EnhancedFoldersHeading, _ => "Game files and mods"),
        (EnhancedGameFolder, _ => "Game files"),
        (EnhancedMods, _ => "Dev overrides"),
        (EnhancedModsFolder, _ => "Dev overrides folder"),
        (EnhancedBrowse, _ => "Browse"),
        (EnhancedNotSet, _ => "not set"),
        (EnhancedBadGameFolder, _ => "That folder does not hold the game's files."),
        (EnhancedSave, _ => "Save"),
        (EnhancedCancel, _ => "Cancel"),
        (ModsTitle, _ => "Mods"),
        (ModsInstalled, _ => "Installed mods"),
        (ModsRow, _ => "[{on}] {name} {version}"),
        (ModsUp, _ => "Up"),
        (ModsDown, _ => "Down"),
        (ModsUninstall, _ => "Uninstall"),
        (ModsUninstallConfirm, _ => "Uninstall? press again"),
        (ModsInstallFile, _ => "Install from file…"),
        (ModsInstallFailed, _ => "Could not install: {reason}"),
        (ModsInboxHint, _ => Literal("Put mod zips in the game's mods folder; they install at the next start.")),
        (ModsProblemMissingDependency, _ => "Needs {other}"),
        (ModsProblemDependencyVersion, _ => "Needs {other}, wrong version"),
        (ModsProblemConflict, _ => "Conflicts with {other}"),
        (ModsProblemGameVersion, _ => "Needs game {other}"),
        (ModsNone, _ => "No mods installed."),
        (ModsMissingTitle, _ => "This save was made with mods that are not active:"),
        (ModsMismatchTitle, _ => "This save was made with different versions of these mods:"),
        (ModsMissingLine, _ => "{name} {version}"),
        (ModsMajorLine, _ => "{name} {version} (installed: {installed})"),
        (ModsLoadAnyway, _ => "Load anyway"),
        (ModsCancel, _ => "Cancel"),
        (EnhancedCastHintPick, _ => "Point at a symbol to read it; click it to cast."),
        (EnhancedCastHintPickTouch, _ => "Touch a symbol to read it; lift your finger on it to choose it, then Cast."),
        (EnhancedCastHintPower, _ => "Point at a lit gem for the power, then click it or Cast. Back returns to the spells."),
        (EnhancedCastHintPowerTouch, _ => "Slide along the lit gems for the power, then Cast. Back, or a tap off the ring, returns to the spells."),
        (EnhancedCastPowerOf, _ => "of {max}"),
        (EnhancedCastCast, _ => "Cast"),
        (EnhancedCastBack, _ => "Back"),
        (EnhancedCastHealth, _ => "Health {current}/{max}"),
        (EnhancedCastStamina, _ => "Stamina {current}/{max}"),
        (EnhancedCastPool, _ => "{health} · {stamina}"),
        (EnhancedInvGiveTo, _ => "Give to {name}"),
        (EnhancedInvTakes, _ => "{name} takes it"),
        (EnhancedInvBuyFor, _ => "Buy for {name}"),
        (EnhancedInvHasIt, _ => "Has it"),
        (EnhancedInvBack, _ => "Back to the pack"),
        (EnhancedPendingKey, _ => "{text} (restart)"),
        (EnhancedStatusKey, _ => "Day {day} · {hour}:00"),
        (EnhancedStatusAtKey, _ => "{place} · {status}"),
        (PercentKey, _ => EnglishPercent),
        (TouchMove, _ => "Move"),
        (TouchCast, _ => "Cast"),
        (TouchCastHere, _ => "Cast here"),
        (VersionBannerKey, _ => "Version {version}"),
        (BuildInfoKey, _ => "BaK-Again {version}   Log: {log}"),
        (BootLoading, _ => Literal("Loading Betrayal at Krondor... please wait.")),
        (BootStepOf, _ => "STEP {n} OF 3"),
        (BootFrom, _ => "from “{name}”"),
        (BootFromFiles, _ => Literal("from the selected files")),
        (BootWelcomeTitle, _ => Literal("Welcome to BaK-Again")),
        (BootWelcomeBody, _ => Literal("BaK-Again plays Betrayal at Krondor with the files from your own copy of the game. Point it at them once and you're ready.")),
        (BootNoGame, _ => Literal("Don't own the game? It's cheap on GOG.")),
        (BootFindFiles, _ => Literal("Find my game files")),
        (BootWhereLink, _ => Literal("Where do I get them?")),
        (BootBuyLink, _ => Literal("Buy on GOG")),
        (BootStuckLink, _ => Literal("Still stuck?")),
        (BootChooseFolderTitle, _ => Literal("Choose your game folder")),
        (BootChooseFolderBodyAndroid, _ => Literal("The game folder holds KRONDOR.001, FRP.SX and STARTUP.GAM. Choose the folder with those three files, then tap Use this folder and Allow.")),
        (BootChooseFolderBodyShortAndroid, _ => Literal("Choose the folder with KRONDOR.001, FRP.SX and STARTUP.GAM.")),
        (BootDownloadNote, _ => Literal("Android won't let an app use the Download folder itself, so put the files in a folder of their own, for example Download/BaK.")),
        (BootChooseFolder, _ => Literal("Choose folder")),
        (BootPickOtherAndroid, _ => Literal("Pick files, a .zip or the GOG installer")),
        (BootPhoneHelpLink, _ => Literal("How do I get them on my phone?")),
        (BootFoundTitle, _ => Literal("Found your game")),
        (BootFoundBody, _ => Literal("Betrayal at Krondor from GOG is installed on this PC.")),
        (BootUseThis, _ => Literal("Use this")),
        (BootChooseAnother, _ => Literal("Choose something else")),
        (BootChooseFolderBodyDesktop, _ => Literal("Choose the folder with your game files, a .zip, or the GOG installer.")),
        (BootCaseNote, _ => Literal("Upper or lower case doesn't matter.")),
        (BootBrowse, _ => Literal("Browse…")),
        (BootMissing, _ => "{file} is missing."),
        (BootMissingHint, _ => Literal("It sits in the same folder as KRONDOR.001 in your game. Copy it over too, then choose again.")),
        (BootFoundCount, _ => "Found {found} of 3."),
        (BootDamaged, _ => "{file}" + Literal(" is cut short. Copy it again; if that doesn't help, your copy is damaged: reinstall or download the game again from GOG.")),
        (BootEmpty, _ => "{file} is empty. Copy it again."),
        (BootDamagedInPlace, _ => "{file} is cut short: your copy is damaged. Reinstall or download the game again from GOG."),
        (BootOkCount, _ => "{ok, plural, one {# of 3 is OK.} other {# of 3 are OK.}}"),
        (BootNotAZip, _ => Literal("BaK-Again reads .zip files only. Unzip it first, then choose the folder.")),
        (BootCopyErrorFound, _ => Literal("Couldn't copy the files from that zip. Use this, or choose something else.")),
        (BootInstallerUnreadable, _ => Literal("This is the installer, not the game files. Install it first, then choose the folder it installed to.")),
        (BootInstallerUnreadableAndroid, _ => Literal("This version of the installer can't be read yet. Tap Still stuck? and ask on the forum, or install the game on a PC and copy its three files.")),
        (BootNothingHere, _ => Literal("No game files here.")),
        (BootCopyFailed, _ => Literal("Couldn't copy the files: the phone needs about ") + "{mb} MB free."),
        (BootCopyFailedDesktop, _ => Literal("Couldn't copy the files: this computer needs about ") + "{mb} MB free."),
        (BootFolder, _ => "Folder: {path}"),
        (BootZipDamaged, _ => Literal("This zip file is damaged. Download it again.")),
        (BootZipTooLarge, _ => Literal("This zip is too big to search. Unzip it and choose the folder with KRONDOR.001 in it.")),
        (BootFoundBodyOther, _ => Literal("Betrayal at Krondor is installed on this computer.")),
        (BootPermission, _ => Literal("The app isn't allowed to read that place. Choose it again and allow access, or use the other way to pick your files.")),
        (BootCopying, _ => Literal("Copying the files…")),
        (BootCopyError, _ => Literal("Couldn't copy the files. Choose them again; if it keeps failing, copy them to the phone first.")),
        (BootCopyErrorDesktop, _ => Literal("Couldn't copy the files. Choose them again.")),
        (BootNoFolderPicker, _ => Literal("This phone has no folder picker. Try “Pick files, a .zip or the GOG installer” instead.")),
        (BootNoFilePicker, _ => Literal("This phone has no file picker. Try Choose folder instead.")),
        (BootChooseAgain, _ => Literal("Choose again")),
        (BootAllSet, _ => Literal("All set")),
        (BootCopiedNote, _ => Literal("The files are copied into the app, so you can delete the originals if you like.")),
        (BootPlay, _ => Literal("Play")),
        (BootBack, _ => Literal("‹ Back")),
        (BootHelpTitleAndroid, _ => Literal("How do I get them on my phone?")),
        (BootHelpTitleDesktop, _ => Literal("Where do I get them?")),
        (BootHelpBodyAndroid, _ => Literal("1. With a USB cable\nConnect the phone to your PC. On the phone, swipe down from the top and tap the USB notification, then choose “File transfer”. On the PC, make a folder named BaK inside the phone's Download folder and copy KRONDOR.001, FRP.SX and STARTUP.GAM into it (on the PC they are in C:\\GOG Games\\Betrayal at Krondor, or wherever you installed the game). Then tap Choose folder, Download, BaK.\n\n2. With Google Drive, Dropbox or OneDrive\nUpload the three files, or a .zip with them, to your drive. Then tap “Pick files, a .zip or the GOG installer” and choose them from the drive.\n\n3. With a USB stick or SD card\nCopy the three files into a folder on it and plug it into the phone (many phones need a small USB adapter for a stick, and an SD card needs a card slot). Then tap Choose folder and pick that folder.\n\n4. With the GOG installer\nDownload the Windows installer (setup_betrayal_at_krondor….exe) from your GOG library, on a PC or in the phone's browser, and put it on the phone. Then tap “Pick files, a .zip or the GOG installer” and choose it.")),
        (BootHelpBodyWindows, _ => Literal("BaK-Again needs three files from your own copy of Betrayal at Krondor: KRONDOR.001, FRP.SX and STARTUP.GAM.\n\nIf you bought it on GOG, pick the installer itself, or the folder GOG installed the game to. By default that is C:\\GOG Games\\Betrayal at Krondor.\n\nIf you have the CD or an old DOS install, pick the folder you installed the game to, the one with KRONDOR.001 in it.")),
        (BootHelpBodyLinux, _ => Literal("BaK-Again needs three files from your own copy of Betrayal at Krondor: KRONDOR.001, FRP.SX and STARTUP.GAM. Upper or lower case doesn't matter.\n\nIf you bought it on GOG, pick the installer itself (no Wine needed), or the folder GOG installed the game to. With Heroic it is usually ~/Games/Heroic/Betrayal at Krondor.\n\nIf you have the CD or an old DOS install, pick the folder you installed the game to, the one with KRONDOR.001 in it.")),
        (BootMoreHelpWeb, _ => Literal("More help with pictures on bakagain.org")),
        (BootStuckTitle, _ => Literal("Still stuck?")),
        (BootStuckBody, _ => Literal("Ask on the forum. People there have set it up on the same kind of device. A log helps them see what went wrong.")),
        (BootAskForum, _ => Literal("Ask on the forum")),
        (BootShareLog, _ => Literal("Share log")),
        (BootOpenLogFolder, _ => Literal("Open log folder")),
        (BootCopied, _ => Literal("Copied")),
        (BootVersionCopy, _ => Literal("Tap or click the version to copy it.")),
        (BootVersion, _ => "BaK-Again {version}"),
        (BootExtracting, _ => "Extracting… {percent}%"),
        (BootInstallerDamaged, _ => Literal("This installer file is damaged. Download it again from your GOG library.")),
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
