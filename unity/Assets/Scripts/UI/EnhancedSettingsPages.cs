namespace BakAgain.UI {
    using System.Collections.Generic;
    using BakAgain.Core;
    using T = GameData.Resources.Text.UiTemplates;

    /// <summary>A page of the Enhanced settings book, one per bookmark.</summary>
    public enum SettingsPage { General, Display, Gameplay, Combat, Audio, Controls, GameFiles }

    /// <summary>How a setting is drawn: a check box, a heading over radio buttons, a slider, a folder
    /// line, a button that opens something, or the key list.</summary>
    public enum SettingKind { Check, Choice, Slider, Path, Action, KeyList }

    /// <summary>One setting on a page. <paramref name="Id"/> names its element and ties it to the draft
    /// (<see cref="EnhancedSettingsScreen"/>).</summary>
    public sealed record SettingItem(string Id, SettingKind Kind, string LabelKey, string HintKey,
        bool UsableInClassic, string[] ChoiceKeys = null);

    /// <summary>
    /// What is on each page of the Enhanced settings book, in the bookmarks' order (spec
    /// 2026-11-26-enhanced-settings-bookmarks). Data only: the screen maps each id to the draft.
    /// </summary>
    public static class EnhancedSettingsPages {
        public static IReadOnlyList<SettingsPage> Order { get; } = new[] {
            SettingsPage.General, SettingsPage.Display, SettingsPage.Gameplay, SettingsPage.Combat,
            SettingsPage.Audio, SettingsPage.Controls, SettingsPage.GameFiles,
        };

        private static SettingItem Feature(string id, string label, string hint) =>
            new(id, SettingKind.Check, label, hint, UsableInClassic: false);

        private static readonly Dictionary<SettingsPage, SettingItem[]> Pages = new() {
            [SettingsPage.General] = new[] {
                new SettingItem("enhanced", SettingKind.Check, T.SettingsEnhanced, T.HintEnhanced, true),
                new SettingItem("check_updates", SettingKind.Check, T.EnhancedCheckForUpdates, T.HintCheckUpdates, true),
                // Its choices are the installed language packs, known only at run time (the screen lists them).
                new SettingItem("language", SettingKind.Choice, T.SettingsLanguageHeading, T.HintLanguage, true),
            },
            [SettingsPage.Display] = new[] {
                Feature("full_screen_travel", T.EnhancedFeatureFullScreen, T.HintFullScreenTravel),
                Feature("sky", T.EnhancedFeatureSky, T.HintSky),
                Feature("lighting", T.EnhancedFeatureLighting, T.HintLighting),
                Feature("health_rings", T.EnhancedFeatureRings, T.HintHealthRings),
                Feature("book_font", T.EnhancedFeatureBookFont, T.HintBookFont),
                Feature("turning_pages", T.EnhancedFeaturePageTurn, T.HintTurningPages),
                new SettingItem("shadows", SettingKind.Choice, T.SettingsShadowsHeading, T.HintShadows, false,
                    new[] { T.EnhancedStateOff, T.EnhancedShadowsBlobs, T.EnhancedShadowsSun }),
            },
            [SettingsPage.Gameplay] = new[] {
                Feature("continuous_walking", T.EnhancedFeatureContinuousMovement, T.HintContinuousWalking),
                Feature("inventory", T.EnhancedFeatureInventory, T.HintInventory),
                Feature("casting", T.EnhancedFeatureCasting, T.HintCasting),
                new SettingItem("restore_shop_buyback_bug", SettingKind.Check, T.RestoreShopBuybackBug, T.HintRestoreShopBuybackBug, true),
            },
            [SettingsPage.Combat] = new[] {
                Feature("enhanced_combat", T.EnhancedFeatureCombat, T.HintEnhancedCombat),
                new SettingItem("combat_speed", SettingKind.Choice, T.SettingsCombatSpeedHeading, T.HintCombatSpeed, true,
                    new[] { T.CombatSpeedSlow, T.CombatSpeedNormal, T.CombatSpeedFast, T.CombatSpeedUltra }),
            },
            [SettingsPage.Audio] = new[] {
                new SettingItem("music_volume", SettingKind.Slider, T.SettingsMusicVolume, T.HintMusicVolume, true),
                new SettingItem("sound_volume", SettingKind.Slider, T.SettingsSoundVolume, T.HintSoundVolume, true),
            },
            [SettingsPage.Controls] = new[] {
                Feature("mouse_look", T.EnhancedFeatureMouseLook, T.HintMouseLook),
                new SettingItem("key_list", SettingKind.KeyList, T.ControlsTitle, T.HintKeyList, true),
            },
            [SettingsPage.GameFiles] = new[] {
                new SettingItem("game_folder", SettingKind.Path, T.EnhancedGameFolder, T.HintGameFolder, true),
                new SettingItem("installed_mods", SettingKind.Action, T.ModsInstalled, T.HintInstalledMods, true),
                new SettingItem("dev_overrides", SettingKind.Check, T.EnhancedMods, T.HintDevOverrides, true),
                new SettingItem("dev_folder", SettingKind.Path, T.EnhancedModsFolder, T.HintDevFolder, true),
            },
        };

        private static readonly Dictionary<string, EnhancedFeature> Features = new() {
            ["full_screen_travel"] = EnhancedFeature.FullScreenTravel, ["mouse_look"] = EnhancedFeature.MouseLook,
            ["health_rings"] = EnhancedFeature.PortraitRings, ["book_font"] = EnhancedFeature.BookFont,
            ["turning_pages"] = EnhancedFeature.PageTurn, ["inventory"] = EnhancedFeature.Inventory,
            ["sky"] = EnhancedFeature.Sky, ["lighting"] = EnhancedFeature.Lighting, ["casting"] = EnhancedFeature.Casting,
            ["continuous_walking"] = EnhancedFeature.ContinuousMovement, ["enhanced_combat"] = EnhancedFeature.Combat,
        };

        public static IReadOnlyList<SettingItem> ItemsOn(SettingsPage page) => Pages[page];

        /// <summary>The Enhanced feature a check line switches, or null for one that is not a feature.</summary>
        public static EnhancedFeature? FeatureOf(string id) => Features.TryGetValue(id, out EnhancedFeature f) ? f : null;

        public static string TitleKey(SettingsPage page) => page switch {
            SettingsPage.General => T.SettingsPageGeneral,
            SettingsPage.Display => T.SettingsPageDisplay,
            SettingsPage.Gameplay => T.SettingsPageGameplay,
            SettingsPage.Combat => T.SettingsPageCombat,
            SettingsPage.Audio => T.SettingsPageAudio,
            SettingsPage.Controls => T.SettingsPageControls,
            _ => T.SettingsPageGameFiles,
        };
    }
}
