namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.UI.InGame;
    using NUnit.Framework;

    /// <summary>
    /// A damage number is placed in the frame it is created, before UI Toolkit has laid it out.
    /// Its width then reads NaN, and NaN carried into left/top put "50"/"40" at the viewport's corner
    /// for that first frame (TASK-117, seen on Evil Seek).
    /// </summary>
    public class DamageFloatPlacementTests {
        [Test]
        public void AnUnlaidOutLabelIsCentredOnItsMeasuredWidth() {
            float left = InGameScreen.FloatLeft(0.5f, 1000f, float.NaN, measured: 40f);
            Assert.AreEqual(480f, left, 0.01f);
        }

        [Test]
        public void ALaidOutLabelUsesItsOwnWidth() {
            Assert.AreEqual(475f, InGameScreen.FloatLeft(0.5f, 1000f, 50f, measured: 40f), 0.01f);
        }
    }
}
