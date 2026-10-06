namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using NUnit.Framework;

    public class BuildInfoTests {
        // The line goes through the ambient catalog (port:template:build_info), and an Editor that
        // last ran a language pack keeps that catalog; the English is what these pin.
        private GameData.Resources.Text.UiStringCatalog _saved;

        [SetUp]
        public void UseTheEnglishCatalog() {
            _saved = GameData.Resources.Text.UiStrings.Catalog;
            GameData.Resources.Text.UiStrings.Catalog = GameData.Resources.Text.UiStringCatalog.Embedded;
        }

        [TearDown]
        public void RestoreTheCatalog() => GameData.Resources.Text.UiStrings.Catalog = _saved;

        [Test]
        public void NamesTheVersionAndTheLogFile() {
            Assert.AreEqual("BaK-Again 0.1.0-alpha   Log: /home/p/.config/unity3d/S/B/Player.log",
                BuildInfo.Describe("0.1.0-alpha", "/home/p/.config/unity3d/S/B/Player.log"));
        }

        [Test]
        public void WithoutALogFile_PointsAtLogcat() {
            StringAssert.EndsWith("Log: adb logcat -s Unity", BuildInfo.Describe("0.1.0", ""));
            StringAssert.EndsWith("Log: adb logcat -s Unity", BuildInfo.Describe("0.1.0", null));
        }
    }
}
