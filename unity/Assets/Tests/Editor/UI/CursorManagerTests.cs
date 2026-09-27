namespace BakAgain.Tests.Editor.UI {
    using BakAgain.UI.Cursor;
    using NUnit.Framework;
    using UnityEngine;

    public class CursorManagerTests {
        [Test]
        public void WarpTo_SetsCanonicalPosition() {
            var arb = new CursorArbiter();
            arb.WarpTo(new Vector2(400, 300));
            Assert.AreEqual(new Vector2(400, 300), arb.Position);
        }

        [Test]
        public void PointerMove_AfterWarp_ReclaimsPosition() {
            var arb = new CursorArbiter();
            arb.WarpTo(new Vector2(400, 300));
            arb.OnPointerMoved(new Vector2(10, 10), new Vector2(800, 600)); // non-zero delta
            Assert.AreEqual(new Vector2(800, 600), arb.Position);
        }

        [Test]
        public void PointerNoMove_AfterWarp_KeepsWarpPosition() {
            var arb = new CursorArbiter();
            arb.WarpTo(new Vector2(400, 300));
            arb.OnPointerMoved(Vector2.zero, new Vector2(800, 600)); // zero delta -> ignored
            Assert.AreEqual(new Vector2(400, 300), arb.Position);
        }

        [Test]
        public void ComputeHotspot_Index0And1_AreTopLeft_Index2Plus_AreCentred() {
            Assert.AreEqual(Vector2.zero, CursorManager.ComputeHotspot(0, 50, 60));
            Assert.AreEqual(Vector2.zero, CursorManager.ComputeHotspot(1, 50, 60));
            Assert.AreEqual(new Vector2(25, 30), CursorManager.ComputeHotspot(2, 50, 60));
            Assert.AreEqual(new Vector2(37.5f, 45f), CursorManager.ComputeHotspot(7, 75, 90));
        }
    }
}
