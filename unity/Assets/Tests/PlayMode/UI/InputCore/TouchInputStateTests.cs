namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;

    /// <summary>The Android touch aids' shared state (spec 2026-09-29-android-touch-aids-design.md).</summary>
    public class TouchInputStateTests {

        /// <summary>Final review #4: a held compass arrow is hold-to-walk; its long-press must not open its help text.</summary>
        [Test]
        public void ALongPressNeverAppliesToTheMovementArrows() {
            foreach (int id in new[] { 72, 75, 77, 80 }) {
                Assert.IsFalse(TouchInputState.LongPressApplies(id), $"arrow {id}");
            }
            Assert.IsTrue(TouchInputState.LongPressApplies(2), "a portrait");
            Assert.IsTrue(TouchInputState.LongPressApplies(192), "the world view");
        }

        [Test]
        public void ALongPressSwallowsItsOwnRelease() {
            var s = new TouchInputState() { SuppressSelectFor = 4 };
            Assert.AreEqual(SelectRoute.Swallow, s.TakeSelectRoute(4), "the long-pressed portrait's release");
            Assert.AreEqual(SelectRoute.Primary, s.TakeSelectRoute(4));
        }

        /// <summary>
        /// Found on the emulator (twice): a long-press on a portrait opens the character sheet — on
        /// its own panel — before the finger lifts, so the portrait's release click never comes, and
        /// the leftover suppression ate the sheet's Exit. Only the long-pressed action's own select
        /// is swallowed; any other select drops the suppression.
        /// </summary>
        [Test]
        public void AStaleSuppressionNeverEatsAnotherButton() {
            var s = new TouchInputState() { SuppressSelectFor = 4 };
            Assert.AreEqual(SelectRoute.Primary, s.TakeSelectRoute(24), "the sheet's Exit goes through");
            Assert.IsNull(s.SuppressSelectFor, "and the suppression is gone");
        }

        /// <summary>
        /// Found on the emulator: a long-press on a portrait opened the character sheet before the
        /// finger lifted, so its release never became a select, and a leftover suppression ate the
        /// sheet's Exit. Any new press (UI Toolkit's own PointerDown, seen panel-wide) drops it.
        /// </summary>
        [Test]
        public void ANewPressDropsAStaleSuppression() {
            var s = new TouchInputState() { SuppressSelectFor = 4 };
            s.OnTouchPressStarted();
            Assert.AreEqual(SelectRoute.Primary, s.TakeSelectRoute(4));
        }

        [Test]
        public void AFightEdgeForgetsAnOldPreview() {
            var s = new TouchInputState() { CombatHoverScreenPoint = new UnityEngine.Vector2(1, 2) };
            s.ForgetCombatPreview();
            Assert.IsNull(s.CombatHoverScreenPoint);
        }

        [Test]
        public void TheGridButtonIsAOneShotToggle() {
            var s = new TouchInputState();
            Assert.IsFalse(s.TakeGridToggle());
            s.RequestGridToggle();
            Assert.IsTrue(s.TakeGridToggle(), "one press, one toggle — like the G key's edge");
            Assert.IsFalse(s.TakeGridToggle());
        }

    }
}
