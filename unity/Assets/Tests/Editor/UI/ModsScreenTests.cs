namespace BakAgain.Tests.Editor.UI {
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using GameData.Mods;
    using NUnit.Framework;
    using System;
    using System.IO;
    using System.Linq;

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
        public void Draft_ProblemsSurface() {
            Mod("aa");
            Mod("bb", ",\"dependencies\":{\"zz\":\">=1.0.0\"}");
            ModLayers.Initialize();
            ModsDraft draft = ModsDraft.Load();

            ModProblem problem = draft.ProblemsFor("bb").Single();
            Assert.AreEqual(ModProblemKind.MissingDependency, problem.Kind);
            Assert.AreEqual("Needs zz", ModsScreen.ProblemText(problem));
            CollectionAssert.IsEmpty(draft.ProblemsFor("aa"));
        }

        [Test]
        public void Draft_InstallFromZip_GoesToTop() {
            Mod("aa");
            Order("aa");
            string source = Path.Combine(Path.GetTempPath(), "bakagain-modsrc-" + Guid.NewGuid().ToString("N"));
            try {
                Directory.CreateDirectory(source);
                File.WriteAllText(Path.Combine(source, "mod.json"), Manifest("nn"));
                string zip = ModArchive.Pack(source, source);
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
