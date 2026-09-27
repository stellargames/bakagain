namespace BakAgain.Tests.Editor.World.Collision {
    using BakAgain.World.Collision;
    using GameData.Resources.World;
    using NUnit.Framework;

    /// <summary>
    /// The point-in-polygon / ground-height / distance primitives of the original proximity
    /// system (canassa WORLD/ZONE/PROXIM.C, R3D/CORE/DISTDIR.ASM). See
    /// docs/specs/collision-system.md §2.3 / §2.4 / §1.
    /// </summary>
    public class ProximityMathTests {
        // Axis-aligned square of half-extent `half`, edge normals pointing OUTWARD — the shape the
        // shipped `ground` entity uses (Z01 entry 0: Dx/Dy = ±125 at the four ±16000 anchors).
        private static GidRegion Square(int half, short baseElevation = 0) {
            var region = new GidRegion { BaseElevation = baseElevation };
            region.Subedges.Add(new GidSubedge { Dx = 0, Dy = 125, AnchorX = (short)-half, AnchorY = (short)half });
            region.Subedges.Add(new GidSubedge { Dx = 125, Dy = 0, AnchorX = (short)half, AnchorY = (short)half });
            region.Subedges.Add(new GidSubedge { Dx = 0, Dy = -125, AnchorX = (short)half, AnchorY = (short)-half });
            region.Subedges.Add(new GidSubedge { Dx = -125, Dy = 0, AnchorX = (short)-half, AnchorY = (short)-half });
            return region;
        }

        [Test]
        public void RegionContains_PointWellInside_IsTrue() {
            Assert.IsTrue(ProximityMath.RegionContains(Square(1000), 10, -20));
        }

        [Test]
        public void RegionContains_PointBeyondOneEdge_IsFalse() {
            Assert.IsFalse(ProximityMath.RegionContains(Square(1000), 10, 1200));
        }

        [Test]
        public void RegionContains_PointOnEdge_IsTrue() {
            // side == 0 counts as inside (`if (side > 0) return 0`), and the zero delta short-circuits
            // the sign shortcut — this is the branch that keeps adjacent polygons seamless.
            Assert.IsTrue(ProximityMath.RegionContains(Square(1000), 1000, 0));
        }

        [Test]
        public void RegionContains_RegionWithNoEdges_IsFalse() {
            Assert.IsFalse(ProximityMath.RegionContains(new GidRegion(), 0, 0));
        }

        [Test]
        public void RegionContains_DiagonalEdge_UsesFullDotProductWhenSignsDisagree() {
            // Single edge, normal (100,100) through the origin: the half-plane x+y <= 0.
            var region = new GidRegion();
            region.Subedges.Add(new GidSubedge { Dx = 100, Dy = 100, AnchorX = 0, AnchorY = 0 });
            Assert.IsTrue(ProximityMath.RegionContains(region, -300, 100), "x+y = -200 is inside");
            Assert.IsFalse(ProximityMath.RegionContains(region, 300, -100), "x+y = +200 is outside");
        }

        [Test]
        public void RegionHeight_FlatRegion_IsBaseElevationOnly() {
            Assert.AreEqual(250, ProximityMath.RegionHeight(Square(1000, 250), 500, -500));
        }

        [Test]
        public void RegionHeight_SlopedRegion_InterpolatesFromTheReferenceVertex() {
            // Real shipped data: Z01 entry 115 `bridge1`, region 1 (the ramp).
            var ramp = Square(400, 180);
            ramp.SlopeShift = 3;
            ramp.Slope = new GidSlopePlane { A = 0, B = 77, AnchorX = -400, AnchorY = 800 };

            // At the reference vertex the gradient term vanishes.
            Assert.AreEqual(180, ProximityMath.RegionHeight(ramp, 0, 800));
            // 800 units downhill: accum = 77 * (800 - 0) = 61600; (61600 * 3) >> 12 = 45.
            Assert.AreEqual(225, ProximityMath.RegionHeight(ramp, 0, 0));
        }

        [Test]
        public void OctagonalDistance_IsMaxPlusThreeEighthsOfMin() {
            // distdir_octagonal_distance: max(|dx|,|dy|) + 3*min/8, z ignored.
            Assert.AreEqual(512, ProximityMath.OctagonalDistance(-300, 400));
            Assert.AreEqual(0, ProximityMath.OctagonalDistance(0, 0));
            Assert.AreEqual(1000, ProximityMath.OctagonalDistance(1000, 0));
        }

        [Test]
        public void Rotate_ByZero_IsIdentity() {
            Assert.AreEqual((1234, -567), ProximityMath.Rotate(1234, -567, 0));
        }

        [Test]
        public void Rotate_ByQuarterCircle_MapsXOntoY() {
            // 0x4000 = 90 degrees in BaK angle space.
            var (x, y) = ProximityMath.Rotate(1000, 0, 0x4000);
            Assert.AreEqual(0, x, 1);
            Assert.AreEqual(1000, y, 1);
        }
    }
}
