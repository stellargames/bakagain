namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using NUnit.Framework;

    /// <summary>The build test harness's <c>-bak-load &lt;dir&gt; &lt;slot&gt;</c> launch argument.</summary>
    public class BootLoadTests {
        [Test]
        public void TheFlagWithADirectoryAndASlot_Parses() {
            Assert.IsTrue(BootLoad.TryParse(new[] { "player", "-bak-hook", "-bak-load", "testkit.G01", "1" }, out string dir, out int slot));
            Assert.AreEqual("testkit.G01", dir);
            Assert.AreEqual(1, slot);
        }

        [TestCase("player", TestName = "NoFlag_DoesNotParse")]
        [TestCase("player -bak-load testkit.G01", TestName = "AMissingSlot_DoesNotParse")]
        [TestCase("player -bak-load testkit.G01 one", TestName = "ANonNumberSlot_DoesNotParse")]
        [TestCase("player -bak-load testkit.G01 -1", TestName = "ANegativeSlot_DoesNotParse")]
        [TestCase("player -bak-load testkit.G01 100", TestName = "ASlotPastTheTwoDigitFileName_DoesNotParse")]
        public void BadArguments(string commandLine) {
            Assert.IsFalse(BootLoad.TryParse(commandLine.Split(' '), out _, out _));
        }
    }
}
