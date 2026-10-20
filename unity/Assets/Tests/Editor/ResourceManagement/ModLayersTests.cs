namespace BakAgain.Tests.Editor.ResourceManagement {
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using GameData.Mods;
    using GameData.Resources.Content;
    using GameData.Resources.Dialog;
    using GameData.Resources.Layout;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using UnityEngine;
    using UnityEngine.ResourceManagement.ResourceLocations;
    using UnityEngine.TestTools;

    /// <summary>
    /// Installed mods as override layers (mod system spec 4.4): the inbox install, the lookup order
    /// and the bottom-up merge of partial documents.
    /// </summary>
    public class ModLayersTests {
        private TempOverrideDirectory _dev;
        private EnhancedPrefsStash _enhanced;
        private string _mods;

        [SetUp]
        public void SetUp() {
            _dev = new TempOverrideDirectory();
            _enhanced = EnhancedPrefsStash.Take();
            GameOptions.Enhanced = true;
            ModLayers.Reset();
            _mods = Path.Combine(Path.GetTempPath(), "bakagain-mods-" + Guid.NewGuid().ToString("N"));
            ModLayers.ModsRootOverride = _mods;
        }

        [TearDown]
        public void TearDown() {
            ModLayers.Reset();
            if (Directory.Exists(_mods)) Directory.Delete(_mods, true);
            _enhanced.Restore();
            _dev.Dispose();
        }

        private static string Manifest(string id, string extra = "") =>
            $"{{\"schema\":1,\"id\":\"{id}\",\"name\":\"{id}\",\"version\":\"1.0.0\"{extra}}}";

        /// <summary>An installed mod folder; returns it.</summary>
        private string Mod(string id, params (string Path, string Text)[] files) {
            string folder = Path.Combine(_mods, id);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "mod.json"), Manifest(id));
            foreach ((string path, string text) in files) {
                string full = Path.Combine(folder, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, text);
            }
            return folder;
        }

        /// <summary>mods.json, highest priority first.</summary>
        private void Order(params string[] ids) =>
            ModStateFile.Write(Path.Combine(_mods, "mods.json"), ids.Select(id => new ModStateEntry(id, "1.0.0", true)));

        private static string Located(string key, Type type = null) {
            bool found = new OverrideResourceLocator().Locate(key, type ?? typeof(Sprite), out IList<IResourceLocation> locations);
            return found ? locations[0].InternalId : null;
        }

        [Test]
        public void Initialize_InboxZip_InstalledAtTopAndZipDeleted() {
            Mod("old-mod");
            Order("old-mod");
            string source = Path.Combine(Path.GetTempPath(), "bakagain-modsrc-" + Guid.NewGuid().ToString("N"));
            try {
                Directory.CreateDirectory(Path.Combine(source, "SCX"));
                File.WriteAllText(Path.Combine(source, "mod.json"), Manifest("new-mod"));
                File.WriteAllText(Path.Combine(source, "SCX", "MODTEST.png"), "png");
                string zip = ModArchive.Pack(source, _mods);

                ModLayers.Initialize();

                Assert.IsFalse(File.Exists(zip), "an installed zip is deleted");
                Assert.IsTrue(File.Exists(Path.Combine(_mods, "new-mod", "SCX", "MODTEST.png")));
                CollectionAssert.AreEqual(new[] { "new-mod", "old-mod" },
                    ModStateFile.Read(Path.Combine(_mods, "mods.json")).Select(e => e.Id).ToArray());
                CollectionAssert.AreEqual(new[] { "new-mod", "old-mod" },
                    ModLayers.Current.Active.Select(m => m.Manifest.Id).ToArray());
            } finally {
                Directory.Delete(source, true);
            }
        }

        [Test]
        public void Initialize_InboxReinstall_KeepsPositionAndState() {
            Mod("aa");
            Mod("bb");
            ModStateFile.Write(Path.Combine(_mods, "mods.json"),
                new[] { new ModStateEntry("aa", "1.0.0", true), new ModStateEntry("bb", "1.0.0", false) });
            string source = Path.Combine(Path.GetTempPath(), "bakagain-modsrc-" + Guid.NewGuid().ToString("N"));
            try {
                Directory.CreateDirectory(source);
                File.WriteAllText(Path.Combine(source, "mod.json"), Manifest("bb"));
                string zip = ModArchive.Pack(source, _mods);

                ModLayers.Initialize();

                Assert.IsFalse(File.Exists(zip), "an installed zip is deleted");
                var state = ModStateFile.Read(Path.Combine(_mods, "mods.json"));
                CollectionAssert.AreEqual(new[] { "aa", "bb" }, state.Select(e => e.Id).ToArray(), "a re-install keeps its place");
                Assert.IsFalse(state[1].Enabled, "and stays off");
            } finally {
                Directory.Delete(source, true);
            }
        }

        [Test]
        public void GameVersion_SkippedInTheEditorAndDevelopmentBuilds() {
            Assert.IsNull(ModLayers.GameVersionOf(devBuild: true, "0.2.0-beta"));
            Assert.AreEqual("0.2.0-beta", ModLayers.GameVersionOf(devBuild: false, "0.2.0-beta")?.ToString());
            Assert.IsNull(ModLayers.GameVersionOf(devBuild: false, "not a version"));
        }

        [Test]
        public void Initialize_BadInboxZip_RenamedFailed() {
            Directory.CreateDirectory(_mods);
            string zip = Path.Combine(_mods, "broken.zip");
            File.WriteAllText(zip, "not a zip");
            LogAssert.Expect(LogType.Error, new Regex("broken\\.zip"));

            ModLayers.Initialize();

            Assert.IsFalse(File.Exists(zip));
            Assert.IsTrue(File.Exists(zip + ".failed"));
            Assert.IsEmpty(ModLayers.Current.Active);
        }

        [Test]
        public void Initialize_CorruptStateFile_ContinuesWithEmptyState() {
            Mod("a-mod");
            File.WriteAllText(Path.Combine(_mods, "mods.json"), "{");
            LogAssert.Expect(LogType.Error, new Regex("mods\\.json"));

            Assert.DoesNotThrow(ModLayers.Initialize);

            CollectionAssert.AreEqual(new[] { "a-mod" }, ModLayers.Current.Active.Select(m => m.Manifest.Id).ToArray(),
                "with no readable state every installed mod is enabled");
        }

        [Test]
        public void Initialize_SkipsTmpAndOldFolders() {
            // What an interrupted install leaves beside an installed mod; the id pattern has no '.', so neither is a mod.
            Mod("a-mod");
            foreach (string leftover in new[] { "a-mod.tmp", "a-mod.old" }) {
                Directory.CreateDirectory(Path.Combine(_mods, leftover));
                File.WriteAllText(Path.Combine(_mods, leftover, "mod.json"), Manifest("a-mod"));
            }

            ModLayers.Initialize();

            CollectionAssert.AreEqual(new[] { Path.Combine(_mods, "a-mod") }, ModLayers.Current.Active.Select(m => m.Folder).ToArray());
        }

        [Test]
        public void Initialize_StrandedOld_ComesBackInstalled() {
            // An install interrupted between its two moves: the previous version is only <id>.old.
            Directory.CreateDirectory(Path.Combine(_mods, "a-mod.old"));
            File.WriteAllText(Path.Combine(_mods, "a-mod.old", "mod.json"), Manifest("a-mod"));

            ModLayers.Initialize();

            CollectionAssert.AreEqual(new[] { "a-mod" }, ModLayers.Current.Active.Select(m => m.Manifest.Id).ToArray());
            Assert.IsTrue(Directory.Exists(Path.Combine(_mods, "a-mod")));
        }

        [Test]
        public void Initialize_StateFileUnwritable_InstalledWithAWarning() {
            Directory.CreateDirectory(Path.Combine(_mods, "mods.json")); // a folder where the state file goes
            string source = Path.Combine(Path.GetTempPath(), "bakagain-modsrc-" + Guid.NewGuid().ToString("N"));
            try {
                Directory.CreateDirectory(source);
                File.WriteAllText(Path.Combine(source, "mod.json"), Manifest("new-mod"));
                string zip = ModArchive.Pack(source, _mods);
                LogAssert.Expect(LogType.Warning, new Regex("new-mod is installed, but the mod order and on/off state could not be saved"));

                ModLayers.Initialize(); // a "could not be installed" error would fail the test

                Assert.IsFalse(File.Exists(zip), "an installed zip is deleted");
                Assert.IsFalse(File.Exists(zip + ".failed"), "and not reported as a failed install");
                CollectionAssert.AreEqual(new[] { "new-mod" }, ModLayers.Current.Active.Select(m => m.Manifest.Id).ToArray());
            } finally {
                Directory.Delete(source, true);
            }
        }

        [Test]
        public void Locate_HigherModWins() {
            string lo = Mod("lo-mod", ("SCX/MODTEST.png", "png"));
            string hi = Mod("hi-mod", ("SCX/MODTEST.png", "png"));
            Order("hi-mod", "lo-mod");
            ModLayers.Initialize();

            Assert.AreEqual(Path.Combine(hi, "SCX", "MODTEST.png"), Located("MODTEST.SCX"));
            CollectionAssert.AreEqual(
                new[] { Path.Combine(lo, "SCX", "MODTEST.png"), Path.Combine(hi, "SCX", "MODTEST.png") },
                OverrideResourceLocator.FindAll("SCX", "MODTEST.png"), "FindAll lists the lowest priority first");
        }

        [Test]
        public void Locate_DevFolderBeatsMods() {
            Mod("a-mod", ("SCX/MODTEST.png", "png"));
            string dev = _dev.Write("SCX", "MODTEST.png", "png");
            ModLayers.Initialize();

            Assert.AreEqual(dev, Located("MODTEST.SCX"));
        }

        [Test]
        public void Locate_ClassicSkipsEnhancedOnlyMod() {
            Mod("a-mod", ("SCX/MODTEST.png", "png")); // modes default to ["enhanced"]
            GameOptions.Enhanced = false;
            ModLayers.Initialize();

            Assert.AreEqual(ModMode.Classic, ModLayers.Mode);
            Assert.IsNull(Located("MODTEST.SCX"));
        }

        [Test]
        public void Locate_EnhancedFindsEnhancedOnlyMod() {
            string mod = Mod("a-mod", ("SCX/MODTEST.png", "png")); // the control for the Classic case above
            ModLayers.Initialize();

            Assert.AreEqual(ModMode.Enhanced, ModLayers.Mode);
            Assert.AreEqual(Path.Combine(mod, "SCX", "MODTEST.png"), Located("MODTEST.SCX"));
        }

        [Test]
        public void AliasCache_ReadOncePerRoot_ResetForgetsIt() {
            string mod = Mod("a-mod", ("BMX/Z01SLOT0/3.png", "png"),
                ("aliases.json", "{\"BMX/Z02SLOT1/4\": \"BMX/Z01SLOT0/3\"}"));
            ModLayers.Initialize();
            string aliased = Path.Combine(mod, "BMX", "Z01SLOT0", "3.png");
            Assert.AreEqual(aliased, Located("Z02SLOT1.BMX#4"));

            File.WriteAllText(Path.Combine(mod, "aliases.json"), "{}");
            Assert.AreEqual(aliased, Located("Z02SLOT1.BMX#4"), "the second lookup is served from the cache");

            ModLayers.Reset();
            ModLayers.ModsRootOverride = _mods;
            ModLayers.Initialize();
            Assert.IsNull(Located("Z02SLOT1.BMX#4"), "a reset reads aliases.json again");
        }

        [Test]
        public void Locate_AliasResolvesWithinMod() {
            string mod = Mod("a-mod", ("BMX/Z01SLOT0/3.png", "png"),
                ("aliases.json", "{\"BMX/Z02SLOT1/4\": \"BMX/Z01SLOT0/3\"}"));
            ModLayers.Initialize();

            Assert.AreEqual(Path.Combine(mod, "BMX", "Z01SLOT0", "3.png"), Located("Z02SLOT1.BMX#4"));
            Assert.IsNull(Located("Z02SLOT1.BMX#5"), "only the aliased key resolves");
        }

        [Test]
        public void BaselineMerge_AppliesEveryLayerBottomUp() {
            Mod("lo-mod", ("DAT/DIALSTYL.json", "{\"Rows\":[null,null,{\"BodyTextPenColor\":13}]}"));
            Mod("hi-mod", ("DAT/DIALSTYL.json", "{\"Rows\":[null,null,{\"DefaultArea\":{\"Left\":\"13.75%\"}}]}"));
            Order("hi-mod", "lo-mod");
            ModLayers.Initialize();

            var table = (DialogStyleTable)OverrideResourceProvider.LoadOverrideObject(
                Located(DialogStyleTable.ResourceId, typeof(DialogStyleTable)), typeof(DialogStyleTable),
                DialogStyleTable.ResourceId);

            DialogStyle row = table.Get(2);
            Assert.AreEqual(13, row.BodyTextPenColor, "the lower mod's field");
            Assert.AreEqual(LayoutLength.Percent(13.75f), row.DefaultArea.Left, "the higher mod's field");
        }

        [Test]
        public void BaselineMerge_ABrokenLayerIsSkipped_TheOthersStillApply() {
            Mod("lo-mod", ("DAT/DIALSTYL.json", "{\"Rows\":[null,"));
            Mod("hi-mod", ("DAT/DIALSTYL.json", "{\"Rows\":[null,null,{\"DefaultArea\":{\"Left\":\"13.75%\"}}]}"));
            Order("hi-mod", "lo-mod");
            ModLayers.Initialize();
            LogAssert.Expect(LogType.Error, new Regex("lo-mod.*DIALSTYL\\.json skipped"));

            var table = (DialogStyleTable)OverrideResourceProvider.LoadOverrideObject(
                Located(DialogStyleTable.ResourceId, typeof(DialogStyleTable)), typeof(DialogStyleTable),
                DialogStyleTable.ResourceId);

            Assert.AreEqual(LayoutLength.Percent(13.75f), table.Get(2).DefaultArea.Left, "the higher mod's field");
            Assert.AreEqual(LayoutLength.Px(66f), table.Get(2).DefaultArea.Top, "the shipped value under it");
        }

        [Test]
        public void Initialize_FolderWithoutValidManifest_IsLoggedAndSkipped() {
            Directory.CreateDirectory(Path.Combine(_mods, "no-manifest"));
            LogAssert.Expect(LogType.Warning, new Regex("no-manifest has no valid mod\\.json"));

            ModLayers.Initialize();

            Assert.IsEmpty(ModLayers.Current.Active);
        }

        [Test]
        public void ItemSources_OnePerLayer_LowestFirst() {
            Mod("lo-mod", ("ObjectInfo/a.json", "{\"Number\":3,\"Name\":\"Low Blade\",\"Price\":5}"));
            Mod("hi-mod", ("ObjectInfo/a.json", "{\"Number\":3,\"Price\":9}"));
            Mod("no-items");
            Order("hi-mod", "no-items", "lo-mod");
            ModLayers.Initialize();
            var shipped = new ObjectInfoSet("OBJINFO", new List<ObjectInfo> {
                new ObjectInfo("OBJINFO") { Number = 3, Name = "Shipped Blade", Price = 250 },
            });

            IReadOnlyList<IContentSource<ObjectInfo>> layers = ModItemCatalogSource.FromLayers(shipped, null);

            CollectionAssert.AreEqual(new[] { "mod:lo-mod:objinfo", "mod:hi-mod:objinfo" },
                layers.Select(s => s.SourceName).ToArray(), "one source per layer with items, lowest first");
            var sources = new List<IContentSource<ObjectInfo>> { new ObjectInfoContentSource(shipped) };
            sources.AddRange(layers);
            ObjectInfo item = GameSession.CatalogOf(sources).GetById(3);
            Assert.AreEqual("Low Blade", item.Name, "the lower layer's field survives the higher one");
            Assert.AreEqual(9, item.Price);
        }
    }
}
