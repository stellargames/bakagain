namespace BakAgain.Tests.Editor.Core {
    using System.Collections.Generic;
    using BakAgain.Core;
    using GameData.Resources.Content;
    using GameData.Resources.Object;
    using NUnit.Framework;

    /// <summary>
    /// The item catalog reaching gameplay through the additive registry — TASK-259.
    /// </summary>
    /// <remarks>
    /// <b>The registry's whole claim is that the original game is not privileged</b>: OBJINFO.DAT is
    /// the priority-0 source and a mod is a later one. That claim is only worth anything if the
    /// merge is on the path a running game actually uses, which until now it was not — five models
    /// sat unconsumed while every lookup read the archive directly.
    ///
    /// <para>The counterpart in GameData (<c>ObjectInfoRegistryTests</c>) proves the merge rule.
    /// These prove the SESSION uses it, which is the part that was missing.</para>
    /// </remarks>
    public class GameSessionItemCatalogTests {
        private const int Sword = 17;

        private static ObjectInfoSet Archive(params (int Number, string Name)[] items) {
            var list = new List<ObjectInfo>();
            foreach ((int number, string name) in items) {
                list.Add(new ObjectInfo("OBJINFO.DAT") { Number = number, Name = name });
            }

            return new ObjectInfoSet("OBJINFO.DAT", list);
        }

        private static IContentSource<ObjectInfo> Mod(string key, ObjectInfo item) =>
            new ListContentSource<ObjectInfo>("testmod",
                new[] { new ContentEntry<ObjectInfo>(key, item) });

        [Test]
        public void ABaseOnlyMergeAnswersExactlyWhatTheDirectLoadDid() {
            // *** THE NO-REGRESSION INVARIANT, ASSERTED ON THE SESSION RATHER THAN THE RULE. ***
            // Every gameplay lookup goes through GameSession.ObjectInfo, so this is where "putting
            // the registry in the path changed nothing" has to hold.
            ObjectInfoSet archive = Archive((0, "knife"), (Sword, "sword"), (42, "shield"));
            var session = new GameSession();

            session.SetObjectInfo(archive);

            Assert.AreEqual(archive.Items.Count, session.ObjectInfo.Items.Count);
            foreach (ObjectInfo item in archive.Items) {
                Assert.AreSame(archive.GetById(item.Number), session.ObjectInfo.GetById(item.Number),
                    $"item {item.Number} must be the same instance the archive holds");
            }

            Assert.IsNull(session.ObjectInfo.GetById(999), "and an absent id stays absent");
        }

        [Test]
        public void AModOverrideReachesTheNumericLookupEveryConsumerUses() {
            // The point of the whole exercise: a later source replacing base:objinfo:17 changes what
            // GetById(17) answers, without a single consumer knowing the registry exists.
            ObjectInfoSet archive = Archive((0, "knife"), (Sword, "sword"));
            var replacement = new ObjectInfo("mod") { Number = Sword, Name = "flaming sword" };
            var session = new GameSession();

            session.SetItemCatalog(GameSession.CatalogOf(new List<IContentSource<ObjectInfo>> {
                new ObjectInfoContentSource(archive),
                Mod(ContentKey.ForBase(ObjectInfoContentSource.Catalog, Sword), replacement),
            }));

            Assert.AreSame(replacement, session.ObjectInfo.GetById(Sword));
            Assert.AreEqual("knife", session.ObjectInfo.GetById(0).Name, "and nothing else moves");
            Assert.AreEqual(2, session.ObjectInfo.Items.Count,
                "an override replaces a slot, it does not add one");
        }

        [Test]
        public void TheOverrideIsRecordedWithBothItsSources() {
            // Provenance is the diagnostic that tells a modder WHICH mod won a key. It is only
            // reachable through ItemCatalog — the numeric view cannot carry it.
            ObjectInfoSet archive = Archive((Sword, "sword"));
            var session = new GameSession();
            string key = ContentKey.ForBase(ObjectInfoContentSource.Catalog, Sword);

            session.SetItemCatalog(GameSession.CatalogOf(new List<IContentSource<ObjectInfo>> {
                new ObjectInfoContentSource(archive),
                Mod(key, new ObjectInfo("mod") { Number = Sword, Name = "flaming sword" }),
            }));

            Assert.AreEqual(1, session.ItemCatalog.Merged.Overrides.Count);
            KeyOverride recorded = session.ItemCatalog.Merged.Overrides[0];
            Assert.AreEqual(key, recorded.Key);
            Assert.AreEqual("testmod", session.ItemCatalog.Merged.Provenance[key]);
        }

        [Test]
        public void AModADDEDItemIsAbsentFromTheNumericViewAndPresentInTheCatalog() {
            // *** THE DOCUMENTED CEILING, ASSERTED SO IT STAYS DOCUMENTED. *** An added item has no
            // numeric slot to occupy, so it cannot appear in an ObjectInfoSet and is reachable only
            // by string key. Anyone who later wants mod-added items in inventory has to widen the
            // lookup, and this test is where they will find out.
            ObjectInfoSet archive = Archive((0, "knife"));
            var added = new ObjectInfo("mod") { Number = 200, Name = "antidote" };
            var session = new GameSession();

            session.SetItemCatalog(GameSession.CatalogOf(new List<IContentSource<ObjectInfo>> {
                new ObjectInfoContentSource(archive),
                Mod("testmod:antidote", added),
            }));

            Assert.IsNull(session.ObjectInfo.GetById(200));
            Assert.AreEqual(1, session.ObjectInfo.Items.Count);
            Assert.IsTrue(session.ItemCatalog.Merged.TryGet("testmod:antidote", out ObjectInfo found));
            Assert.AreSame(added, found);
        }
    }
}
