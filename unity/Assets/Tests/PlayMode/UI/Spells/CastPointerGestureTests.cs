namespace BakAgain.Tests.PlayMode.UI.Spells {
    using BakAgain.UI.InputCore;
    using BakAgain.UI.Spells;
    using NUnit.Framework;

    /// <summary>
    /// How the cast screen reads a pointer (TASK-816): a mouse previews on hover and picks on the
    /// press; a finger previews while it is down and picks where it LIFTS (owner, 2026-10-05).
    /// </summary>
    public class CastPointerGestureTests {
        private static FakePointer Mouse() => new FakePointer { IsPresent = true };
        private static FakePointer Finger() => new FakePointer { IsPresent = false, CanPointOverride = true };

        [Test]
        public void AMouseTracksAlwaysAndPicksOnThePress() {
            FakePointer p = Mouse();
            Assert.IsTrue(CastScreen.Tracks(p));
            Assert.IsFalse(CastScreen.Picks(p));
            p.Primary.SetDown(true, pressedThisFrame: true);
            Assert.IsTrue(CastScreen.Picks(p));
        }

        [Test]
        public void AFingerPreviewsWhileDownAndDoesNotPickOnThePress() {
            FakePointer p = Finger();
            Assert.IsFalse(CastScreen.Tracks(p), "no finger, nothing to follow");
            p.Primary.SetDown(true, pressedThisFrame: true);
            Assert.IsTrue(CastScreen.Tracks(p));
            Assert.IsFalse(CastScreen.Picks(p), "touching the ring must not cast");
            p.Primary.SetDown(true);
            Assert.IsTrue(CastScreen.Tracks(p));
            Assert.IsFalse(CastScreen.Picks(p));
        }

        [Test]
        public void AFingerPicksWhereItLifts() {
            FakePointer p = Finger();
            p.Primary.SetDown(false, releasedThisFrame: true);
            Assert.IsTrue(CastScreen.Tracks(p), "the lift's own position still counts");
            Assert.IsTrue(CastScreen.Picks(p));
        }
    }
}
