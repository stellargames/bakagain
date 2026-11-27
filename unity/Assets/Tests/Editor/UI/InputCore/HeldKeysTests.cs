namespace BakAgain.Tests.Editor.UI.InputCore {
    using System.Collections.Generic;
    using BakAgain.UI.InputCore;
    using NUnit.Framework;

    /// <summary>
    /// TASK-1151: during a blocking animation the original reads no input, but the BIOS keyboard
    /// buffer keeps the keys and the screen's loop reads them once the animation is over.
    /// </summary>
    public class HeldKeysTests {
        [Test]
        public void AKeyPressedWhileHeldActsOnceReleased() {
            int cancels = 0;
            bool held = true;
            var layer = new NavigableLayer("inventory", CaptureMode.Exclusive, new List<NavWidget>(),
                () => cancels++, null) { HoldsKeys = () => held };

            Assert.IsTrue(layer.HandleIntent(UiIntent.Cancel()), "the key is taken, not passed on");
            Assert.AreEqual(0, cancels, "nothing happens during the animation");

            held = false;
            layer.ReleaseHeldKeys();
            Assert.AreEqual(1, cancels, "and it acts when the animation is over");

            layer.ReleaseHeldKeys();
            Assert.AreEqual(1, cancels, "once");
        }

        [Test]
        public void DroppedKeysNeverAct() {
            // The examine fly-in: the original's examine wait starts with the keys of the fly-in
            // gone (measured 2026-11-27: Escape sent mid fly-in left the examine up).
            int cancels = 0;
            bool held = true;
            var layer = new NavigableLayer("inventory", CaptureMode.Exclusive, new List<NavWidget>(),
                () => cancels++, null) { HoldsKeys = () => held };
            layer.HandleIntent(UiIntent.Cancel());

            layer.DropHeldKeys();
            held = false;
            layer.ReleaseHeldKeys();

            Assert.AreEqual(0, cancels);
        }

        [Test]
        public void TheBufferHoldsFifteenKeys() {
            int cancels = 0;
            bool held = true;
            var layer = new NavigableLayer("inventory", CaptureMode.Exclusive, new List<NavWidget>(),
                () => cancels++, null) { HoldsKeys = () => held };
            for (int i = 0; i < 20; i++) {
                layer.HandleIntent(UiIntent.Cancel());
            }

            held = false;
            layer.ReleaseHeldKeys();

            Assert.AreEqual(NavigableLayer.HeldKeyCapacity, cancels);
        }

        [Test]
        public void AReplayedKeyThatStartsAnotherAnimationLeavesTheRestWaiting() {
            int cancels = 0;
            bool held = true;
            NavigableLayer layer = null;
            layer = new NavigableLayer("inventory", CaptureMode.Exclusive, new List<NavWidget>(),
                () => { cancels++; held = true; }, null) { HoldsKeys = () => held };
            layer.HandleIntent(UiIntent.Cancel());
            layer.HandleIntent(UiIntent.Cancel());

            held = false;
            layer.ReleaseHeldKeys();
            Assert.AreEqual(1, cancels);

            held = false;
            layer.ReleaseHeldKeys();
            Assert.AreEqual(2, cancels);
        }
    }
}
