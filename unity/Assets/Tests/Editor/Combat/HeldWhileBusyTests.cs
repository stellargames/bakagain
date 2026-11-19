namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.World.Encounters;
    using NUnit.Framework;

    /// <summary>
    /// A grid toggle pressed during an attack waits for it: the original reads no input while the
    /// attack runs (COMBAT.C:2361-2362), so the key is read once it ends. Redrawing mid-attack cut
    /// the swing and snapped the corpse to its last frame (TASK-1100 D9).
    /// </summary>
    public class HeldWhileBusyTests {
        [Test]
        public void APressDuringTheAttackFiresWhenItEnds() {
            var held = new HeldWhileBusy();
            Assert.IsFalse(held.Poll(pressed: true, busy: true), "not mid-attack");
            Assert.IsFalse(held.Poll(pressed: false, busy: true));
            Assert.IsTrue(held.Poll(pressed: false, busy: false), "once the arena is idle");
            Assert.IsFalse(held.Poll(pressed: false, busy: false), "once");
        }

        [Test]
        public void AnIdlePressFiresAtOnce() {
            Assert.IsTrue(new HeldWhileBusy().Poll(pressed: true, busy: false));
        }
    }
}
