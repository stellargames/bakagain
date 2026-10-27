namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.ResourceManagement;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Text;
    using GameData.Setup;
    using NUnit.Framework;
    using System.Collections;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>What a pick does (TASK-1014): a desktop folder, zip or installer, and what the Android copy staged.</summary>
    public class SetupWizardPickTests {
        private static readonly string Original = Path.Combine(Application.persistentDataPath, "original");
        private static readonly string Stash = Original + "-stashed-by-test";
        private string _dir;
        private GameObject _go;
        private MetaMenu _view;

        [SetUp]
        public void SetUp() {
            _dir = Path.Combine(Path.GetTempPath(), "bak-pick-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
            if (Directory.Exists(Original)) Directory.Move(Original, Stash); // a real copy is never touched
            _view = SetupWizardViewTests.Spawn(out _go);
            _view.Show(null);
            _view.Flow.Next();
            _view.Render();
        }

        [TearDown]
        public void TearDown() {
            Object.DestroyImmediate(_go);
            Directory.Delete(_dir, true);
            if (Directory.Exists(Original)) Directory.Delete(Original, true);
            if (Directory.Exists(Stash)) Directory.Move(Stash, Original);
        }

        private VisualElement Root => _view.GetComponent<UIDocument>().rootVisualElement;

        private string Note => Root.Q<Label>(className: "setup-note")?.text;

        private static string Text(string key, params (string, object)[] args) => LanguagePacks.BootText(key, args);

        /// <summary>The three files; KRONDOR.001 is a one-entry archive whose header chains to its end.</summary>
        private static (string Name, byte[] Bytes)[] GameFiles() {
            var archive = new byte[13 + 4 + 1];
            archive[13] = 1;
            return new[] { ("KRONDOR.001", archive), ("FRP.SX", new byte[] { 1 }), ("STARTUP.GAM", new byte[] { 1 }) };
        }

        [Test]
        public void AZipWithTheFilesIsCopiedIntoTheApp() {
            string zip = Path.Combine(_dir, "bak.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create)) {
                foreach ((string name, byte[] bytes) in GameFiles()) {
                    using Stream s = archive.CreateEntry("BaK/" + name).Open();
                    s.Write(bytes, 0, bytes.Length);
                }
            }

            _view.UsePick(zip);

            Assert.That(_view.Flow.Step, Is.EqualTo(SetupStep.Check));
            Assert.That(_view.Flow.Report.AllOk, Is.True);
            Assert.That(GameFolder.Required.All(n => File.Exists(Path.Combine(Original, n))), Is.True);
        }

        [Test]
        public void AFolderIsUsedWhereItIs() {
            string game = Path.Combine(_dir, "GOG", "Betrayal at Krondor");
            Directory.CreateDirectory(game);
            foreach ((string name, byte[] bytes) in GameFiles()) File.WriteAllBytes(Path.Combine(game, name.ToLowerInvariant()), bytes);

            _view.UsePick(_dir);

            Assert.That(_view.Flow.Report.AllOk, Is.True);
            Assert.That(Directory.Exists(Original), Is.False, "nothing copied");
            Assert.That(Root.Query<Label>().ToList().Select(l => l.text), Has.Member(Text(UiTemplates.BootFolder, ("path", game))));
        }

        [Test]
        public void TheCdImageSaysItIsTheInstaller() {
            string gog = Path.Combine(_dir, "bak.gog");
            File.WriteAllBytes(gog, new byte[] { 1 });

            _view.UsePick(gog);

            Assert.That(_view.Flow.Step, Is.EqualTo(SetupStep.Find));
            Assert.That(Note, Is.EqualTo(Text(UiTemplates.BootInstallerUnreadable)));
        }

        [Test]
        public void ADamagedZipSaysSo() {
            string zip = Path.Combine(_dir, "bak.zip");
            File.WriteAllBytes(zip, new byte[] { 1, 2, 3, 4, 5 });

            _view.UsePick(zip);

            Assert.That(Note, Is.EqualTo(Text(UiTemplates.BootZipDamaged)));
        }

        [Test]
        public void AnAndroidPickOfTheFilesIsImportedUnderItsName() {
            foreach ((string name, byte[] bytes) in GameFiles()) File.WriteAllBytes(Path.Combine(_dir, name), bytes);

            _view.UseAndroidPick(new AndroidSetupPicker.PickResult("BaK", _dir, null));

            Assert.That(_view.Flow.Report.AllOk, Is.True);
            Assert.That(_view.Flow.SourceName, Is.EqualTo("BaK"));
            Assert.That(GameFolder.Required.All(n => File.Exists(Path.Combine(Original, n))), Is.True);
        }

        [Test]
        public void LooseAndroidFilesSayTheSelectedFilesNotTheFirstName() {
            foreach ((string name, byte[] bytes) in GameFiles()) File.WriteAllBytes(Path.Combine(_dir, name), bytes);

            _view.UseAndroidPick(new AndroidSetupPicker.PickResult("FRP.SX", _dir, null), looseFiles: true);
            _view.Render();

            string stepLine = Root.Q<Label>("StepLine").text;
            Assert.That(stepLine, Does.Contain(Text(UiTemplates.BootFromFiles)));
            Assert.That(stepLine, Does.Not.Contain("FRP.SX"));
        }

        [Test]
        public void AFindStepNoteComesRightUnderTheTitle() {
            string gog = Path.Combine(_dir, "bak.gog");
            File.WriteAllBytes(gog, new byte[] { 1 });

            _view.UsePick(gog);

            // The body scrolls; on a phone a note at its end showed one line above the button.
            Assert.That(Root.Q<ScrollView>("Body").contentContainer.ElementAt(1).ClassListContains("setup-note"), Is.True);
        }

        [Test]
        public void AnAndroidPickOfOneZipIsImportedAsAZip() {
            using (ZipArchive archive = ZipFile.Open(Path.Combine(_dir, "bak-files.zip"), ZipArchiveMode.Create)) {
                foreach ((string name, byte[] bytes) in GameFiles()) {
                    using Stream s = archive.CreateEntry(name).Open();
                    s.Write(bytes, 0, bytes.Length);
                }
            }

            _view.UseAndroidPick(new AndroidSetupPicker.PickResult("bak-files.zip", _dir, null));

            Assert.That(_view.Flow.Report.AllOk, Is.True);
            Assert.That(_view.Flow.SourceName, Is.EqualTo("bak-files.zip"));
        }

        [Test]
        public void AnAndroidCopyThatRanOutOfSpaceSaysSo() {
            _view.UseAndroidPick(new AndroidSetupPicker.PickResult("BaK", _dir, "space"));
            _view.Render();

            Assert.That(_view.Flow.Step, Is.EqualTo(SetupStep.Find));
            Assert.That(Note, Is.EqualTo(MetaMenu.ImportErrorText("space")));
        }

        [Test]
        public void AnInstallerCopyThatRanOutOfSpaceNamesWhatItNeeds() {
            _view.UseAndroidPick(new AndroidSetupPicker.PickResult("setup_betrayal_at_krondor_2.1.0.19.exe", _dir, "space", bytes: 114992104));
            _view.Render();

            // 109 MB of installer staged, then the 14 MB of game files beside it.
            Assert.That(Note, Is.EqualTo(MetaMenu.ImportErrorText("space", 123)));
            Assert.That(Note, Does.Contain("123"));
        }

        [Test]
        public void AFailedAndroidCopyImportsNothing() {
            // What a short copy can leave behind: all three names, the files cut short.
            foreach ((string name, byte[] bytes) in GameFiles()) File.WriteAllBytes(Path.Combine(_dir, name), bytes);

            _view.UseAndroidPick(new AndroidSetupPicker.PickResult("BaK", _dir, "io", new[] { "KRONDOR.001" }));
            _view.Render();

            Assert.That(_view.Flow.Step, Is.EqualTo(SetupStep.Find));
            Assert.That(Note, Is.EqualTo(Text(UiTemplates.BootCopyErrorDesktop))); // the Editor is the desktop
            Assert.That(Note, Does.Not.Contain("MB"), "a read failure is not a space problem");
            Assert.That(Directory.Exists(Original), Is.False, "nothing imported");
        }

        [Test]
        public void AnAndroidPickTheAppMayNotReadSaysSo() {
            _view.UseAndroidPick(new AndroidSetupPicker.PickResult("BaK", _dir, "permission"));
            _view.Render();

            Assert.That(_view.Flow.Step, Is.EqualTo(SetupStep.Find));
            Assert.That(Note, Is.EqualTo(Text(UiTemplates.BootPermission)));
            Assert.That(Directory.Exists(Original), Is.False);
        }

        [Test]
        public void OnlyResourceCfgFailingStillImportsTheGameFiles() {
            foreach ((string name, byte[] bytes) in GameFiles()) File.WriteAllBytes(Path.Combine(_dir, name), bytes);

            _view.UseAndroidPick(new AndroidSetupPicker.PickResult("BaK", _dir, "io", GameFolder.Required.ToArray()));

            Assert.That(_view.Flow.Step, Is.EqualTo(SetupStep.Check));
            Assert.That(_view.Flow.Report.AllOk, Is.True);
        }

        [Test]
        public void ADamagedFileUsedInPlaceIsCutShortNotFound() {
            foreach ((string name, byte[] bytes) in GameFiles()) File.WriteAllBytes(Path.Combine(_dir, name), bytes);
            byte[] cut = GameFiles()[0].Bytes;
            cut[13] = 100; // the entry claims more data than the file holds
            File.WriteAllBytes(Path.Combine(_dir, "KRONDOR.001"), cut);

            _view.UsePick(_dir);

            Assert.That(Root.Q<Label>(className: "setup-title").text, Is.EqualTo(Text(UiTemplates.BootOkCount, ("ok", 2))));
            Assert.That(Note, Is.EqualTo(Text(UiTemplates.BootDamagedInPlace, ("file", "KRONDOR.001"))));
            Assert.That(Note, Does.Not.Contain("Copy it again"), "nothing was copied");
        }

        [Test]
        public void ARarSaysToUnzipItFirst() {
            string rar = Path.Combine(_dir, "bak.rar");
            File.WriteAllBytes(rar, new byte[] { 1 });

            _view.UsePick(rar);

            Assert.That(Note, Is.EqualTo(Text(UiTemplates.BootNotAZip)));
        }

        [Test]
        public void AFailedZipUnderFoundYourGameNamesTheZip() {
            string zip = Path.Combine(_dir, "bak.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create)) {
                foreach ((string name, byte[] bytes) in GameFiles()) {
                    using Stream s = archive.CreateEntry(name).Open();
                    s.Write(bytes, 0, bytes.Length);
                }
            }
            _view.FoundForTest = _dir;
            File.WriteAllBytes(Original, new byte[] { 1 }); // a file where the copy's folder goes: the copy fails
            try {
                _view.UsePick(zip);
            } finally {
                File.Delete(Original);
            }

            Assert.That(Note, Is.EqualTo(Text(UiTemplates.BootCopyErrorFound)));
            Assert.That(Note, Does.Contain("zip").And.Not.Contain("Use this again"), "Use this never copies; the zip did");
        }

        /// <summary>GOG's setup_betrayal_at_krondor_2.1.0.19.exe, from $BAK_GOG_INSTALLER; the tests that need it pass without it.</summary>
        private static string InstallerPath() {
            string path = System.Environment.GetEnvironmentVariable("BAK_GOG_INSTALLER");
            return string.IsNullOrEmpty(path) || !File.Exists(path) ? null : path;
        }

        [UnityTest]
        public IEnumerator PickingTheInstallerImportsTheFiles() => UniTask.ToCoroutine(async () => {
            string installer = InstallerPath();
            if (installer == null) return;
            string kept = BakResourceSettings.GamePath;
            try {
                UniTask pick = _view.UsePick(installer);
                // Extracting runs off the main thread: the step says so, and nothing can be clicked meanwhile.
                Assert.That(Root.Query<Label>().ToList().Select(l => l.text), Has.Member(Text(UiTemplates.BootExtracting, ("percent", 0))));
                Assert.That(Root.Query<Button>().ToList().Where(b => b.enabledSelf), Is.Empty);
                await pick;

                Assert.That(_view.Flow.Step, Is.EqualTo(SetupStep.Check));
                Assert.That(_view.Flow.Report.AllOk, Is.True);
                Button play = Root.Query<Button>().ToList().Single(b => b.text == Text(UiTemplates.BootPlay));
                using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled()) {
                    submit.target = play;
                    play.SendEvent(submit);
                }
                Assert.That(BakResourceSettings.GamePath, Is.EqualTo(Original));
            } finally {
                BakResourceSettings.GamePath = kept;
            }
        });

        [UnityTest]
        public IEnumerator ARenamedFileThatIsNoInstallerSaysItIsTheInstaller() => UniTask.ToCoroutine(async () => {
            string exe = Path.Combine(_dir, "setup_betrayal_at_krondor_x.exe");
            using (ZipArchive archive = ZipFile.Open(exe, ZipArchiveMode.Create)) archive.CreateEntry("readme.txt");

            await _view.UsePick(exe);

            Assert.That(_view.Flow.Step, Is.EqualTo(SetupStep.Find));
            Assert.That(Note, Is.EqualTo(Text(UiTemplates.BootInstallerUnreadable)));
            Assert.That(Directory.Exists(Original) && Directory.EnumerateFileSystemEntries(Original).Any(), Is.False);
        });

        [UnityTest]
        public IEnumerator ADamagedInstallerSaysSo() => UniTask.ToCoroutine(async () => {
            string installer = InstallerPath();
            if (installer == null) return;
            string copy = Path.Combine(_dir, Path.GetFileName(installer));
            File.Copy(installer, copy);
            using (FileStream f = File.Open(copy, FileMode.Open)) {
                // Measured: KRONDOR.001's chunk starts at data offset 0x3B400 + 57304709; flip a bit 3 MB into it.
                f.Position = 0x3B400 + 57304709 + 3_000_000;
                int b = f.ReadByte();
                f.Position--;
                f.WriteByte((byte)(b ^ 0x10));
            }

            await _view.UsePick(copy);

            Assert.That(_view.Flow.Step, Is.EqualTo(SetupStep.Find));
            Assert.That(Note, Is.EqualTo(Text(UiTemplates.BootInstallerDamaged)));
        });

        [Test]
        public void ACopyFailureOnTheDesktopNeverSaysPhone() {
            Assert.That(MetaMenu.ImportErrorText("io"), Does.Not.Contain("phone"));
            Assert.That(MetaMenu.ImportErrorText("too-large"), Is.EqualTo(Text(UiTemplates.BootZipTooLarge)));
        }
    }
}
