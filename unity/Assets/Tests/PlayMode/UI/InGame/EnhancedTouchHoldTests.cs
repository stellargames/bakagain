namespace BakAgain.Tests.PlayMode.UI.InGame {
    using BakAgain.UI.InGame;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>Enhanced combat's long press (TASK-972): fed by the touch tracker's press serial, and
    /// the tap it eats is only its own press's release.</summary>
    public class EnhancedTouchHoldTests {
        private static readonly Vector2 At = new Vector2(400, 300);

        [Test]
        public void AStillFingerFiresOnce_ItsReleaseIsEaten_ALaterTapIsNot() {
            var hold = new EnhancedTouchHold(0.5f, 8f);
            Assert.IsFalse(hold.Tick(1, true, At, 0f));
            Assert.IsFalse(hold.Tick(1, true, At, 0.4f), "not yet");
            Assert.IsTrue(hold.Tick(1, true, At, 0.6f), "held still past the hold time");
            Assert.IsFalse(hold.Tick(1, true, At, 0.9f), "once per press");
            Assert.IsTrue(hold.TakeTap(1), "the long press's own release is not a tap");
            Assert.IsFalse(hold.TakeTap(2), "the next press's tap is a tap");
        }

        [Test]
        public void ASwallowNeverOutlivesItsPress() {
            // The long press's release landed off the board, so its tap never reached the board:
            // the next real tap (a new press) must still go through.
            var hold = new EnhancedTouchHold(0.5f, 8f);
            hold.Tick(1, true, At, 0f);
            Assert.IsTrue(hold.Tick(1, true, At, 0.6f));
            hold.Tick(1, false, At, 0.7f);
            hold.Tick(2, true, At, 1.0f);
            Assert.IsFalse(hold.TakeTap(2));
        }

        [Test]
        public void AMovingFingerIsNoLongPress_ANewPressStartsAfresh() {
            var hold = new EnhancedTouchHold(0.5f, 8f);
            hold.Tick(1, true, At, 0f);
            Assert.IsFalse(hold.Tick(1, true, At + new Vector2(20, 0), 0.6f), "moved past the drag threshold");
            // A release the tracker missed: the serial still says it is a new press.
            Assert.IsFalse(hold.Tick(2, true, At, 1.0f));
            Assert.IsTrue(hold.Tick(2, true, At, 1.6f));
        }
    }
}
