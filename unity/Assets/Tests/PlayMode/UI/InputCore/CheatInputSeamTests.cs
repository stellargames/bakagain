namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;

    public class CheatInputSeamTests {
        [Test]
        public void FakeCheatInput_ExposesSetValue() {
            var cheat = new FakeCheatInput { RevealRareCredits = true };

            Assert.IsTrue(cheat.RevealRareCredits);
        }

        [Test]
        public void FakeCheatInput_DefaultsFalse() {
            var cheat = new FakeCheatInput();

            Assert.IsFalse(cheat.RevealRareCredits);
        }
    }
}
