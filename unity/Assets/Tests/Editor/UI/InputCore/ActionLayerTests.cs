namespace BakAgain.Tests.Editor.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;

    public class ActionLayerTests {
        [Test]
        public void Activate_And_Skip_BothInvokeOnActivate() {
            int activated = 0;
            var layer = new ActionLayer("cut", () => activated++, () => { });
            Assert.IsTrue(layer.HandleIntent(UiIntent.Activate()));
            Assert.IsTrue(layer.HandleIntent(UiIntent.Skip()));
            Assert.AreEqual(2, activated, "cutscene skip rides both Activate and Skip");
        }

        [Test]
        public void Cancel_InvokesOnCancel() {
            int cancelled = 0;
            var layer = new ActionLayer("cut", () => { }, () => cancelled++);
            Assert.IsTrue(layer.HandleIntent(UiIntent.Cancel()));
            Assert.AreEqual(1, cancelled);
        }

        [Test]
        public void Move_RoutesToOnMove_WhenProvided() {
            NavDirection? moved = null;
            var layer = new ActionLayer("book", () => { }, () => { }, d => moved = d);
            Assert.IsTrue(layer.HandleIntent(UiIntent.Move(NavDirection.Left)));
            Assert.AreEqual(NavDirection.Left, moved);
        }

        [Test]
        public void Move_IsIgnored_WhenNoMoveHandler() {
            var layer = new ActionLayer("cut", () => { }, () => { });
            Assert.IsFalse(layer.HandleIntent(UiIntent.Move(NavDirection.Next)),
                "a layer with no page-turn handler doesn't consume MoveFocus");
        }

        [Test]
        public void AnyIntent_Activates_WhenAttractFlagSet() {
            // The intro attract layer must exit on ANY input (PlayIntro exits on any key/click), incl.
            // first-letter accelerators and arrows that a normal action layer would ignore.
            int activated = 0;
            var layer = new ActionLayer("intro", () => activated++, () => { },
                onMove: null, anyIntentActivates: true);
            Assert.IsTrue(layer.HandleIntent(UiIntent.Accelerator('x')), "accelerator exits in attract mode");
            Assert.IsTrue(layer.HandleIntent(UiIntent.Move(NavDirection.Up)), "arrow exits in attract mode");
            Assert.AreEqual(2, activated, "any input maps to activate (exit) in attract mode");
        }

        [Test]
        public void IsAlwaysExclusive() {
            var layer = new ActionLayer("cut", () => { }, () => { });
            Assert.AreEqual(CaptureMode.Exclusive, layer.CaptureMode);
        }
    }
}
