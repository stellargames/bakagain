namespace BakAgain.Tests.Editor.ResourceManagement {
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using GameData.Resources.Content;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// THE FENCE for the mod half of the item catalog (TASK-259).
    ///
    /// <para><b>What these stand on.</b> The registry could always merge an ordered list of
    /// sources; until <see cref="ModItemCatalogSource"/> there was never more than one entry in
    /// it, so every merge was the no-regression case and nothing proved the fold worked at all.
    /// These drive the real production path — <c>FromOverrides</c> reading real files from the
    /// real overrides directory, then <see cref="GameSession.CatalogOf(IReadOnlyList{IContentSource{ObjectInfo}})"/>
    /// folding them exactly as <c>GameStateLoader</c> does.</para>
    ///
    /// <para><b>Why Price is asserted in the first test and not just Name.</b> A document is a
    /// PARTIAL. Deserializing it standalone would produce an ObjectInfo whose every unmentioned
    /// field sits at its type default, and the merged item would still have the right Name — so a
    /// Name-only assertion passes with the merge baseline thrown away. Price is the field that
    /// tells the two apart, which is the whole reason OverrideJsonMerge exists.</para>
    /// </summary>
    public class ModItemCatalogSourceTests {
        private const int ShippedNumber = 3;
        private const int ShippedPrice = 250;

        private static ObjectInfoSet Shipped() =>
            new ObjectInfoSet("OBJINFO", new List<ObjectInfo> {
                new ObjectInfo("OBJINFO") {
                    Number = ShippedNumber, Name = "Shipped Blade", Price = ShippedPrice,
                },
            });

        private static ObjectInfoCatalog Fold(ObjectInfoSet baseline) {
            var sources = new List<IContentSource<ObjectInfo>> {
                new ObjectInfoContentSource(baseline),
            };
            IContentSource<ObjectInfo> mod = ModItemCatalogSource.FromOverrides(baseline);
            if (mod != null) {
                sources.Add(mod);
            }
            return GameSession.CatalogOf(sources);
        }

        /// <summary>THE FENCE. An override replaces the field it names and leaves the rest of the
        /// shipped item standing.</summary>
        [Test]
        public void AnOverride_ReplacesTheNamedField_AndKeepsEveryFieldItDoesNotMention() {
            using (var overrides = new TempOverrideDirectory()) {
                overrides.Write(ModItemCatalogSource.Directory, "blade.json",
                    "{\"Number\":3,\"Name\":\"Modded Blade\"}");

                ObjectInfo item = Fold(Shipped()).GetById(ShippedNumber);

                Assert.IsNotNull(item, "the override must not remove the item it addresses");
                Assert.AreEqual("Modded Blade", item.Name,
                    "the mod source is later in the ordered list, so it wins the key");
                Assert.AreEqual(ShippedPrice, item.Price,
                    "Price is unmentioned, so it must survive from the shipped entry; 0 here means "
                    + "the document was deserialized standalone instead of merged onto a baseline");
            }
        }

        /// <summary>Adding and overriding share one keyspace, so an unshipped Number is reachable
        /// through the same numeric surface every gameplay caller already uses.</summary>
        [Test]
        public void ANumberTheArchiveDoesNotHave_AddsAnItem_ReachableByTheNumericView() {
            using (var overrides = new TempOverrideDirectory()) {
                overrides.Write(ModItemCatalogSource.Directory, "new-item.json",
                    "{\"Number\":9001,\"Name\":\"Mod Item\",\"Price\":7}");

                ObjectInfoCatalog catalog = Fold(Shipped());

                ObjectInfo added = catalog.GetById(9001);
                Assert.IsNotNull(added, "a mod-added Number must occupy its own numeric slot");
                Assert.AreEqual("Mod Item", added.Name);
                Assert.AreEqual(7, added.Price);
                Assert.AreEqual("Shipped Blade", catalog.GetById(ShippedNumber).Name,
                    "adding an item must not disturb the ones the archive shipped");
            }
        }

        /// <summary>One hand-authored file with a stray comma must not cost the player the rest of
        /// the folder.</summary>
        [Test]
        public void AMalformedDocument_IsSkipped_AndItsNeighbourStillApplies() {
            using (var overrides = new TempOverrideDirectory()) {
                overrides.Write(ModItemCatalogSource.Directory, "aaa-broken.json", "{\"Number\":3,");
                overrides.Write(ModItemCatalogSource.Directory, "zzz-good.json",
                    "{\"Number\":3,\"Name\":\"Survivor\"}");

                Assert.AreEqual("Survivor", Fold(Shipped()).GetById(ShippedNumber).Name,
                    "a malformed file must be logged and skipped, not fail the whole catalog");
            }
        }

        /// <summary>Without a Number there is nothing to key on, and it cannot be inferred from the
        /// file name — so the document is skipped rather than guessed at.</summary>
        [Test]
        public void ADocumentWithNoNumber_IsSkipped_LeavingTheShippedItemIntact() {
            using (var overrides = new TempOverrideDirectory()) {
                overrides.Write(ModItemCatalogSource.Directory, "nameless.json",
                    "{\"Name\":\"Unaddressed\"}");

                Assert.AreEqual("Shipped Blade", Fold(Shipped()).GetById(ShippedNumber).Name);
            }
        }

        /// <summary>No authored documents means NO mod source at all — not an empty one, which
        /// would appear in provenance as having been consulted.</summary>
        [Test]
        public void AnEmptyOverridesFolder_SuppliesNoSource() {
            using (new TempOverrideDirectory()) {
                Assert.IsNull(ModItemCatalogSource.FromOverrides(Shipped()),
                    "with nothing authored, \"no mod installed\" and \"a mod that changes nothing\" "
                    + "must stay distinguishable to anything reading provenance");
            }
        }
    }
}
