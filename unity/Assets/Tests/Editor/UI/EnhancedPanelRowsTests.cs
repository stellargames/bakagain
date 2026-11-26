namespace BakAgain.Tests.Editor.UI {
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.Core;
    using BakAgain.UI;
    using NUnit.Framework;
    using Templates = GameData.Resources.Text.UiTemplates;

    /// <summary>The Enhanced settings book's pages (spec 2026-11-26-enhanced-settings-bookmarks): every
    /// feature on one page, the spec's bookmark order, and which settings Classic players keep.</summary>
    public class EnhancedPanelRowsTests {
        private static IEnumerable<SettingItem> AllItems =>
            EnhancedSettingsPages.Order.SelectMany(EnhancedSettingsPages.ItemsOn);

        [Test]
        public void EveryFeatureIsOnExactlyOnePage() {
            foreach (EnhancedFeature f in System.Enum.GetValues(typeof(EnhancedFeature))) {
                Assert.That(AllItems.Count(i => EnhancedSettingsPages.FeatureOf(i.Id) == f), Is.EqualTo(1),
                    $"{f} needs exactly one line, or the player can never switch it");
            }
        }

        [Test]
        public void GeneralIsFirstAndStartsWithTheEnhancedSwitch() {
            CollectionAssert.AreEqual(new[] {
                SettingsPage.General, SettingsPage.Display, SettingsPage.Gameplay, SettingsPage.Combat,
                SettingsPage.Audio, SettingsPage.Controls, SettingsPage.GameFiles,
            }, EnhancedSettingsPages.Order);
            Assert.AreEqual("enhanced", EnhancedSettingsPages.ItemsOn(SettingsPage.General)[0].Id);
        }

        [Test]
        public void UsableInClassicMatchesTheSpec() {
            CollectionAssert.AreEquivalent(new[] {
                    "check_updates", "combat_speed", "music_volume", "sound_volume", "restore_shop_buyback_bug",
                    "key_list", "game_folder", "installed_mods", "dev_overrides", "dev_folder", "enhanced",
                },
                AllItems.Where(i => i.UsableInClassic).Select(i => i.Id));
        }

        [Test]
        public void EveryItemHasAHintAndEveryPageATitle() {
            foreach (SettingItem i in AllItems) {
                Assert.That(Templates.Format(i.HintKey), Is.Not.Empty.And.Not.EqualTo(i.HintKey), i.Id);
            }
            foreach (SettingsPage p in EnhancedSettingsPages.Order) {
                Assert.That(Templates.Format(EnhancedSettingsPages.TitleKey(p)), Is.Not.Empty.And.Not.EqualTo(EnhancedSettingsPages.TitleKey(p)), p.ToString());
            }
        }

        [Test]
        public void IdsAreUnique() {
            List<string> ids = AllItems.Select(i => i.Id).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
        }
    }
}
