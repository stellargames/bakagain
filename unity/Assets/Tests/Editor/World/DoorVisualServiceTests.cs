namespace BakAgain.Tests.Editor.World {
    using BakAgain.World;
    using BakAgain.World.Rendering;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// The zone root is destroyed at the END of the frame, after a zone rebuilt from cache has
    /// already published its context. The old root's teardown must not revoke the new context, or
    /// every door opened afterwards stays without a doorway floor (Cavall Run, 2026-09-23).
    /// </summary>
    public class DoorVisualServiceTests {
        [Test]
        public void AStaleTeardownDoesNotRevokeTheNewZone() {
            var oldZone = WorldEntityRenderContext.Create(new Color[256], new ClassicRenderProfile());
            var newZone = WorldEntityRenderContext.Create(new Color[256], new ClassicRenderProfile());
            var doors = new DoorVisualService();

            doors.SetZone(null, oldZone, null, null);
            doors.SetZone(null, newZone, null, null);
            doors.ClearZone(oldZone);
            Assert.IsTrue(doors.HasZone, "the old root's late teardown wiped the new zone's context");

            doors.ClearZone(newZone);
            Assert.IsFalse(doors.HasZone, "control: the zone's own teardown still revokes it");

            oldZone.Dispose();
            newZone.Dispose();
        }
    }
}
