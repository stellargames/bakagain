namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using NUnit.Framework;

    public class BuildInfoTests {
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
