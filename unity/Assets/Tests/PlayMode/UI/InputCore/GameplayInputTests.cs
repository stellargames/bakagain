namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;

    public class GameplayInputTests {
        [Test]
        public void FakeGameplayInput_ExposesSetValues() {
            var g = new FakeGameplayInput { Move = new Vector2(0, 1), Run = true, Vertical = -1f };
            Assert.AreEqual(new Vector2(0, 1), g.Move);
            Assert.IsTrue(g.Run);
            Assert.AreEqual(-1f, g.Vertical);
        }
    }
}
