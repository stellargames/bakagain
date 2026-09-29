namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>A still finger is a right-click in split-pad and minimal travel (spec 2026-09-29-android-touch-aids-design.md).</summary>
    public class TouchHoldDetectorTests {
        [Test]
        public void FiresOnceAfterAStillHold() {
            var d = new TouchHoldDetector(0.5f, 20f);
            Assert.IsFalse(d.Tick(true, Vector2.zero, 0f));
            Assert.IsFalse(d.Tick(true, new Vector2(5, 5), 0.4f));
            Assert.IsTrue(d.Tick(true, new Vector2(5, 5), 0.51f));
            Assert.IsFalse(d.Tick(true, new Vector2(5, 5), 0.9f), "once per press");
        }

        [Test]
        public void MovingBeyondTheSlopCancels() {
            var d = new TouchHoldDetector(0.5f, 20f);
            d.Tick(true, Vector2.zero, 0f);
            d.Tick(true, new Vector2(30, 0), 0.2f);
            Assert.IsFalse(d.Tick(true, new Vector2(30, 0), 0.6f));
        }

        [Test]
        public void ReleasingResets() {
            var d = new TouchHoldDetector(0.5f, 20f);
            d.Tick(true, Vector2.zero, 0f);
            d.Tick(false, Vector2.zero, 0.3f);
            d.Tick(true, Vector2.zero, 0.4f);
            Assert.IsFalse(d.Tick(true, Vector2.zero, 0.8f), "a new press starts its own clock");
            Assert.IsTrue(d.Tick(true, Vector2.zero, 0.91f));
        }
    }
}
