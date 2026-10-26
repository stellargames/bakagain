namespace BakAgain.Tests.Editor.UI {
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using GameData.Mods;
    using NUnit.Framework;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using UnityEngine;
    using Templates = GameData.Resources.Text.UiTemplates;

    /// <summary>The Mods screen's draft (mod system spec 4.6): nothing changes on disk until Save.</summary>
    public class ModsScreenTests {
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

        private string Mod(string id, string extra = "") {
            string folder = Path.Combine(_mods, id);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "mod.json"), Manifest(id, extra));
            return folder;
        }

        private string StatePath => Path.Combine(_mods, "mods.json");

        private void Order(params string[] ids) =>
            ModStateFile.Write(StatePath, ids.Select(id => new ModStateEntry(id, "1.0.0", true)));

        private static string[] Ids(ModsDraft draft) => draft.Mods.Select(e => e.Mod.Manifest.Id).ToArray();

        [Test]
        public void Draft_ToggleMoveSave_WritesStateInOrder() {
            Mod("aa");
            Mod("bb");
            Mod("cc");
            Order("aa", "bb", "cc");
            ModLayers.Initialize();
            ModsDraft draft = ModsDraft.Load();
            Assert.IsFalse(draft.Changed, "a fresh draft is the running state");

            draft.Toggle("bb");
            draft.MoveDown("aa");
            draft.MoveUp("cc");
            draft.MoveUp("bb"); // already at the top: stays
            CollectionAssert.AreEqual(new[] { "bb", "cc", "aa" }, Ids(draft));
            Assert.IsTrue(draft.Changed);

            draft.Save();

            var state = ModStateFile.Read(StatePath);
            CollectionAssert.AreEqual(new[] { "bb", "cc", "aa" }, state.Select(e => e.Id).ToArray());
            CollectionAssert.AreEqual(new[] { false, true, true }, state.Select(e => e.Enabled).ToArray());
            Assert.AreEqual("1.0.0", state[0].Version);
        }

        [Test]
        public void Draft_Uninstall_DeletesFolderOnSave() {
            string aa = Mod("aa");
            Mod("bb");
            Order("aa", "bb");
            ModsDraft draft = ModsDraft.Load();

            draft.Uninstall("aa");
            CollectionAssert.AreEqual(new[] { "bb" }, Ids(draft));
            Assert.IsTrue(Directory.Exists(aa), "nothing is deleted before Save");

            draft.Save();

            Assert.IsFalse(Directory.Exists(aa));
            CollectionAssert.AreEqual(new[] { "bb" }, ModStateFile.Read(StatePath).Select(e => e.Id).ToArray());
        }

        [Test]
        public void Draft_Uninstall_DeletesAnInstallsLeftoversToo() {
            string aa = Mod("aa");
            Directory.CreateDirectory(aa + ".old"); // a delete that failed after an earlier re-install
            Directory.CreateDirectory(aa + ".tmp");
            Order("aa");
            ModsDraft draft = ModsDraft.Load();

            draft.Uninstall("aa");
            draft.Save();

            Assert.IsFalse(Directory.Exists(aa));
            Assert.IsFalse(Directory.Exists(aa + ".old"), "or the next start would put it back");
            Assert.IsFalse(Directory.Exists(aa + ".tmp"));
        }

        [Test]
        public void Draft_UninstallDeleteFails_StaysDisabled() {
            string aa = Mod("aa");
            Mod("bb");
            Order("aa", "bb");
            ModsDraft draft = ModsDraft.Load();
            draft.DeleteFolder = _ => throw new IOException("locked");

            draft.Uninstall("aa");
            Assert.Throws<IOException>(draft.Save);

            Assert.IsTrue(Directory.Exists(aa));
            var state = ModStateFile.Read(StatePath);
            CollectionAssert.AreEqual(new[] { "bb", "aa" }, state.Select(e => e.Id).ToArray());
            Assert.IsFalse(state[1].Enabled, "a mod the player uninstalled must not start enabled");
            ModsDraft next = ModsDraft.Load();
            Assert.IsFalse(next.Mods.Single(e => e.Mod.Manifest.Id == "aa").Enabled);
        }

        [Test]
        public void Draft_Move_StepsOverHiddenLanguageMods() {
            Mod("lang-xx", ",\"kinds\":[\"language\"]");
            Mod("aa");
            Mod("bb");
            Order("lang-xx", "aa", "bb");
            ModsDraft draft = ModsDraft.Load();

            draft.MoveUp("aa"); // the top listed mod: the language pack above it is not on the screen
            CollectionAssert.AreEqual(new[] { "lang-xx", "aa", "bb" }, Ids(draft));
            draft.MoveUp("bb");
            CollectionAssert.AreEqual(new[] { "lang-xx", "bb", "aa" }, Ids(draft));

            Order("aa", "lang-xx", "bb");
            draft = ModsDraft.Load();
            draft.MoveDown("aa");
            CollectionAssert.AreEqual(new[] { "bb", "lang-xx", "aa" }, Ids(draft), "swaps with the next listed mod");
        }

        [Test]
        public void Draft_Cancel_WritesNothing() {
            string aa = Mod("aa");
            Mod("bb");
            Order("aa", "bb");
            string before = File.ReadAllText(StatePath);
            ModsDraft draft = ModsDraft.Load();

            draft.Toggle("bb");
            draft.MoveDown("aa");
            draft.Uninstall("aa");
            // Cancel drops the draft without saving it.

            Assert.AreEqual(before, File.ReadAllText(StatePath));
            Assert.IsTrue(Directory.Exists(aa));
        }

        [Test]
        public void Draft_Uninstall_NeverDeletesOutsideTheModsFolder() {
            Mod("aa");
            Order("aa");
            string outside = Path.Combine(Path.GetTempPath(), "bakagain-outside-" + Guid.NewGuid().ToString("N"));
            string foreign = Path.Combine(outside, "aa"); // a mod folder's name, but not directly in the mods folder
            string notAMod = Path.Combine(_mods, "Not A Mod"); // directly in it, but not a mod folder's name
            Directory.CreateDirectory(foreign);
            Directory.CreateDirectory(notAMod);
            try {
                ModsDraft draft = ModsDraft.Load();
                var mods = (List<ModsDraft.Entry>)typeof(ModsDraft).GetField("_mods", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(draft);
                mods.Add(new ModsDraft.Entry(new InstalledMod(ModManifest.Parse(Manifest("bb"), out _), foreign), true));
                mods.Add(new ModsDraft.Entry(new InstalledMod(ModManifest.Parse(Manifest("cc"), out _), notAMod), true));
                draft.Uninstall("bb");
                draft.Uninstall("cc");

                draft.Save();

                Assert.IsTrue(Directory.Exists(foreign));
                Assert.IsTrue(Directory.Exists(notAMod));
            } finally {
                Directory.Delete(outside, true);
            }
        }

        [Test]
        public void ClipTo_CutsToTheViewport_AndEmptiesARowOutOfView() {
            var viewport = new Rect(0, 100, 500, 300);

            Assert.AreEqual(new Rect(0, 100, 500, 50), ModsScreen.ClipTo(new Rect(0, 50, 500, 100), viewport), "partly in view: its visible part");
            Assert.AreEqual(new Rect(10, 150, 200, 66), ModsScreen.ClipTo(new Rect(10, 150, 200, 66), viewport));

            Rect below = ModsScreen.ClipTo(new Rect(10, 450, 200, 66), viewport); // under the viewport, over Install or Save
            Assert.IsFalse(below.Contains(new Vector2(100, 480)), "contains no point");
            Assert.AreEqual(new Vector2(110, 483), below.center, "but keeps its place for the arrow keys");
        }

        [Test]
        public void InstallFailed_ReadsWithALocalizedPrefix() {
            Assert.AreEqual("Could not install: mod.json is missing",
                Templates.Format(Templates.ModsInstallFailed, ("reason", "mod.json is missing")));
        }

        [Test]
        public void Draft_ProblemsSurface() {
            Mod("aa");
            Mod("bb", ",\"dependencies\":{\"zz\":\">=1.0.0\"}");
            BakAgain.Tests.TestSupport.ExpectedLog.Expect(UnityEngine.LogType.Warning, "Mod bb not active \\(MissingDependency\\)");
            ModLayers.Initialize();
            ModsDraft draft = ModsDraft.Load();

            ModProblem problem = draft.ProblemsFor("bb").Single();
            Assert.AreEqual(ModProblemKind.MissingDependency, problem.Kind);
            Assert.AreEqual("Needs zz", ModsScreen.ProblemText(problem));
            CollectionAssert.IsEmpty(draft.ProblemsFor("aa"));
        }

        [Test]
        public void Draft_ReinstallFromZip_KeepsPositionAndState() {
            Mod("aa");
            Mod("bb");
            Order("aa", "bb");
            string source = Path.Combine(Path.GetTempPath(), "bakagain-modsrc-" + Guid.NewGuid().ToString("N"));
            try {
                string folder = Path.Combine(source, "bb"); // packed beside it: a mod's own folder is refused
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "mod.json"), Manifest("bb"));
                string zip = ModArchive.Pack(folder, source);
                ModsDraft draft = ModsDraft.Load();
                draft.Toggle("bb");

                draft.Install(zip);

                CollectionAssert.AreEqual(new[] { "aa", "bb" }, Ids(draft), "a re-install keeps its place");
                Assert.IsFalse(draft.Mods[1].Enabled, "and its on/off state");
            } finally {
                Directory.Delete(source, true);
            }
        }

        [Test]
        public void Draft_InstallFromZip_GoesToTop() {
            Mod("aa");
            Order("aa");
            string source = Path.Combine(Path.GetTempPath(), "bakagain-modsrc-" + Guid.NewGuid().ToString("N"));
            try {
                string folder = Path.Combine(source, "nn"); // packed beside it: a mod's own folder is refused
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "mod.json"), Manifest("nn"));
                string zip = ModArchive.Pack(folder, source);
                ModsDraft draft = ModsDraft.Load();

                draft.Install(zip);

                CollectionAssert.AreEqual(new[] { "nn", "aa" }, Ids(draft));
                Assert.IsTrue(draft.Mods[0].Enabled);
                Assert.IsTrue(File.Exists(Path.Combine(_mods, "nn", "mod.json")));
                draft.Save();
                CollectionAssert.AreEqual(new[] { "nn", "aa" }, ModStateFile.Read(StatePath).Select(e => e.Id).ToArray());
            } finally {
                Directory.Delete(source, true);
            }
        }
    }
}
