namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Core.DI;
    using BakAgain.UI;
    using GameData.Setup;
    using NUnit.Framework;
    using System.IO;
    using UnityEngine;

    /// <summary>The setup is checked at every start, not only the first (TASK-1014).</summary>
    public class SetupStartupCheckTests {
        private string _dir;
        private GameObject _go;

        [SetUp]
        public void SetUp() {
            _dir = Path.Combine(Path.GetTempPath(), "bak-setup-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) Object.DestroyImmediate(_go);
            Directory.Delete(_dir, true);
        }

        /// <summary>The four files, KRONDOR.001 a one-entry archive whose header chains to its end.</summary>
        private void WriteGameFiles(bool lowerCase) {
            string N(string name) => lowerCase ? name.ToLowerInvariant() : name;
            var archive = new byte[13 + 4 + 1];
            archive[13] = 1;
            File.WriteAllBytes(Path.Combine(_dir, N("KRONDOR.001")), archive);
            foreach (string name in new[] { "FRP.SX", "STARTUP.GAM", "RESOURCE.CFG" }) {
                File.WriteAllBytes(Path.Combine(_dir, N(name)), new byte[] { 1 });
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AnExistingGoodSetupSkipsTheWizard(bool lowerCase) {
            WriteGameFiles(lowerCase);

            Assert.That(GameInitializationService.NeedsSetup(_dir, out _), Is.False);
        }

        [Test]
        public void AMissingStartupGamOpensTheWizardOnCheck() {
            WriteGameFiles(false);
            File.Delete(Path.Combine(_dir, "STARTUP.GAM"));

            Assert.That(GameInitializationService.NeedsSetup(_dir, out GameFilesReport report), Is.True);
            MetaMenu view = SetupWizardViewTests.Spawn(out _go);
            view.Show(report);

            Assert.That(view.Flow.Step, Is.EqualTo(SetupStep.Check));
            Assert.That(view.Flow.Report.Files["STARTUP.GAM"], Is.EqualTo(GameFileState.Missing));
        }

        [Test]
        public void NoStoredPathOpensTheWizardOnWelcome() {
            Assert.That(GameInitializationService.NeedsSetup("", out GameFilesReport report), Is.True);
            Assert.That(report, Is.Null);
        }
    }
}
