namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.ResourceManagement;
    using BakAgain.UI;
    using GameData.Resources.Text;
    using GameData.Setup;
    using NUnit.Framework;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>What a desktop Browse pick does (TASK-1014): a folder, a zip, an installer.</summary>
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
        public void ACopyFailureOnTheDesktopNeverSaysPhone() {
            Assert.That(MetaMenu.ImportErrorText("io"), Does.Not.Contain("phone"));
            Assert.That(MetaMenu.ImportErrorText("too-large"), Is.EqualTo(Text(UiTemplates.BootZipTooLarge)));
        }
    }
}
