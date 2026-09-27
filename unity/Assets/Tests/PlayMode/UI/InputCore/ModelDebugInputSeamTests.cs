namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;

    public class ModelDebugInputSeamTests {
        [Test]
        public void FakeModelDebugInput_ExposesSetValues() {
            var d = new FakeModelDebugInput {
                PrevModel = true, NextZone = true, RotateRight = true
            };

            Assert.IsTrue(d.PrevModel);
            Assert.IsFalse(d.NextModel);
            Assert.IsFalse(d.PrevZone);
            Assert.IsTrue(d.NextZone);
            Assert.IsFalse(d.RotateLeft);
            Assert.IsTrue(d.RotateRight);
        }
    }
}
