namespace BakAgain.Tests.World {
    using GameData.Resources.World;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// Road following (WORLDMOV.C, spec §3.5). The cases below pin the parts that look like details
    /// and are not: diagonals move on both axes rather than by trig, a diagonal probe needs three
    /// samples rather than one, and a second continuation is a fork the party refuses to guess at.
    /// </summary>
    public class RoadTravelTests {
        private const int Cell = RoadTravel.CellSize;   // 1600
        private const ushort North = 0x0000;            // 0
        private const ushort NorthEast = 0x2000;        // 45
        private const ushort East = 0x4000;             // 90
        private const ushort SouthEast = 0x6000;        // 135
        private const ushort South = 0x8000;            // 180
        private const ushort NorthWest = 0xE000;        // 315

        /// <summary>Road everywhere.</summary>
        private static System.Func<int, int, bool> AllRoad() => (_, _) => true;

        /// <summary>Road only at the listed positions.</summary>
        private static System.Func<int, int, bool> RoadAt(params (int X, int Y)[] cells) {
            var set = new HashSet<(int, int)>();
            foreach ((int x, int y) in cells) {
                set.Add((x, y));
            }
            return (x, y) => set.Contains((x, y));
        }

        // ---- kinds and headings ----------------------------------------------------------

        [Test]
        public void OnlyRoadAndBridgeAreTravellable() {
            Assert.IsTrue(RoadTravel.IsRoadKind(1));
            Assert.IsTrue(RoadTravel.IsRoadKind(2));
            Assert.IsFalse(RoadTravel.IsRoadKind(0));
            Assert.IsFalse(RoadTravel.IsRoadKind(3));
        }

        [Test]
        public void OnlyTheEightCompassHeadingsCanTravel() {
            Assert.IsTrue(RoadTravel.IsCompassHeading(North));
            Assert.IsTrue(RoadTravel.IsCompassHeading(NorthEast));
            Assert.IsTrue(RoadTravel.IsCompassHeading(0xE000));
            // One degree off the lattice — which is exactly what the wall-slide pivot leaves behind.
            Assert.IsFalse(RoadTravel.IsCompassHeading(0x2001));
        }

        [Test]
        public void SnappingToTheStrideBringsABumpedHeadingBackWithinReachOfTheCompass() {
            // 910 is a real measurement, not a made-up number: the port's pivot left the party there
            // after one blocked step on 2026-09-13 where the original turned a clean 2048. From 910
            // the reachable set by turning is 910 + 2048k, which contains no compass heading at all,
            // so the road sweep could never fire again.
            const int Stride = 0x800;
            Assert.AreEqual(0, RoadTravel.SnapToStride(910, Stride));
            Assert.AreEqual(Stride, RoadTravel.SnapToStride(1200, Stride), "past the half-stride, round up");

            // A party that never bumped is already on the lattice, and snapping must not move it.
            Assert.AreEqual(North, RoadTravel.SnapToStride(North, Stride));
            Assert.AreEqual(NorthEast, RoadTravel.SnapToStride(NorthEast, Stride));

            // And the wrap has to land on 0 rather than on 65536-truncated-to-something.
            Assert.AreEqual(0, RoadTravel.SnapToStride(65535, Stride));
        }

        // ---- axis offsets ----------------------------------------------------------------

        [Test]
        public void AnOrthogonalStepMovesOneAxis() {
            Assert.AreEqual((0, Cell), RoadTravel.AxisOffset(North, Cell));
            Assert.AreEqual((-Cell, 0), RoadTravel.AxisOffset(East, Cell));
            Assert.AreEqual((0, -Cell), RoadTravel.AxisOffset(South, Cell));
        }

        [Test]
        public void ADiagonalStepMovesTheFullDeltaOnBothAxesRatherThanResolvingByTrig() {
            // 1.41x the ground of an orthogonal step. Using sin/cos here would drift off the integer
            // lattice the whole system depends on.
            Assert.AreEqual((-Cell, Cell), RoadTravel.AxisOffset(NorthEast, Cell));
            Assert.AreEqual((-Cell, -Cell), RoadTravel.AxisOffset(SouthEast, Cell));
            Assert.AreEqual((Cell, Cell), RoadTravel.AxisOffset(NorthWest, Cell));
        }

        // ---- lattice lines ---------------------------------------------------------------

        [Test]
        public void MovingAlongYRequiresStandingOnAColumnCentre() {
            Assert.IsTrue(RoadTravel.IsOnLatticeLine(North, 800, 1234));
            Assert.IsTrue(RoadTravel.IsOnLatticeLine(South, 800 + (3 * Cell), 0));
            Assert.IsFalse(RoadTravel.IsOnLatticeLine(North, 801, 1234));
        }

        [Test]
        public void MovingAlongXRequiresStandingOnARowCentre() {
            Assert.IsTrue(RoadTravel.IsOnLatticeLine(East, 1234, 800));
            Assert.IsFalse(RoadTravel.IsOnLatticeLine(East, 1234, 799));
        }

        [Test]
        public void TheTwoDiagonalFamiliesPreserveDifferentInvariants() {
            // 135/315 keep (x-y) — equal residues.
            Assert.IsTrue(RoadTravel.IsOnLatticeLine(NorthWest, 400, 400));
            Assert.IsFalse(RoadTravel.IsOnLatticeLine(NorthWest, 400, 401));
            // 45/225 keep (x+y) — residues summing to a full cell.
            Assert.IsTrue(RoadTravel.IsOnLatticeLine(NorthEast, 400, 1200));
            Assert.IsFalse(RoadTravel.IsOnLatticeLine(NorthEast, 400, 1201));
        }

        [Test]
        public void TheOriginCornerIsAcceptedOnTheFortyFiveFamily() {
            Assert.IsTrue(RoadTravel.IsOnLatticeLine(NorthEast, 0, 0));
            Assert.IsTrue(RoadTravel.IsOnLatticeLine(NorthEast, Cell * 2, Cell * 5));
        }

        [Test]
        public void TheLatticeHoldsWestAndSouthOfTheOriginToo() {
            // C# remainder follows the dividend's sign, so a naive % would fail every negative case.
            Assert.IsTrue(RoadTravel.IsOnLatticeLine(North, -Cell + 800, 0));
            Assert.IsTrue(RoadTravel.IsOnLatticeLine(East, 0, -Cell + 800));
            Assert.IsTrue(RoadTravel.IsOnLatticeLine(NorthWest, -Cell + 400, -Cell + 400));
        }

        // ---- probing ---------------------------------------------------------------------

        [Test]
        public void AnOrthogonalProbeNeedsOnlyTheCellAhead() {
            var probes = 0;
            bool ok = RoadTravel.ProbeAdjacentCell(800, 800, North, (_, _) => {
                probes++;
                return true;
            });

            Assert.IsTrue(ok);
            Assert.AreEqual(1, probes);
        }

        [Test]
        public void ADiagonalProbeNeedsThreeSamples() {
            var probes = 0;
            bool ok = RoadTravel.ProbeAdjacentCell(800, 800, NorthEast, (_, _) => {
                probes++;
                return true;
            });

            Assert.IsTrue(ok);
            Assert.AreEqual(3, probes);
        }

        [Test]
        public void ADiagonalIsRefusedWhenOnlyTheCellAheadIsRoad() {
            // Cutting the corner of a bend: the cell ahead is road but the shoulder samples are not,
            // and the original refuses. Keeping only the first sample would let the party cut across.
            bool ok = RoadTravel.ProbeAdjacentCell(800, 800, NorthEast,
                RoadAt((800 - Cell, 800 + Cell)));

            Assert.IsFalse(ok);
        }

        [Test]
        public void AProbeFailsWhenTheCellAheadIsNotRoad() {
            Assert.IsFalse(RoadTravel.ProbeAdjacentCell(800, 800, North, (_, _) => false));
        }

        // ---- continuation sweep ----------------------------------------------------------

        [Test]
        public void StraightOnWinsImmediately() {
            RoadSweep result = RoadTravel.FindContinuation(800, 800, North, false, AllRoad(), out ushort target);

            Assert.AreEqual(RoadSweep.Turn, result);
            Assert.AreEqual(North, target);
        }

        [Test]
        public void ARoadWithNoContinuationStopsTravel() {
            RoadSweep result = RoadTravel.FindContinuation(800, 800, North, false, (_, _) => false, out _);

            Assert.AreEqual(RoadSweep.None, result);
        }

        [Test]
        public void ASingleBendTurnsThePartyToFollowIt() {
            // Nothing straight ahead; road only to the east.
            RoadSweep result = RoadTravel.FindContinuation(
                800, 800, North, false, RoadAt((800 - Cell, 800)), out ushort target);

            Assert.AreEqual(RoadSweep.Turn, result);
            Assert.AreEqual(East, target);
        }

        [Test]
        public void TwoContinuationsAreAForkAndThePartyRefusesToPick() {
            // Road both east and west: the sweep finds a second hit and gives up rather than guessing.
            RoadSweep result = RoadTravel.FindContinuation(
                800, 800, North, false,
                RoadAt((800 - Cell, 800), (800 + Cell, 800)), out _);

            Assert.AreEqual(RoadSweep.Fork, result);
        }

        [Test]
        public void AHeadingOffTheCompassCannotSweepAtAll() {
            RoadSweep result = RoadTravel.FindContinuation(800, 800, 0x2001, false, AllRoad(), out _);

            Assert.AreEqual(RoadSweep.None, result);
        }

        [Test]
        public void TravellingBackwardSweepsFromTheReversedHeading() {
            RoadSweep result = RoadTravel.FindContinuation(800, 800, North, true, AllRoad(), out ushort target);

            Assert.AreEqual(RoadSweep.Turn, result);
            Assert.AreEqual(North, target); // reversed to sweep, then folded back by the offset
        }

        // ---- cell centres and ticks ------------------------------------------------------

        [Test]
        public void OnlyExactCellCentresCountAsABendPoint() {
            Assert.IsTrue(RoadTravel.IsOnCellCentre(800, 800));
            Assert.IsTrue(RoadTravel.IsOnCellCentre(800 + Cell, 800 + (4 * Cell)));
            Assert.IsFalse(RoadTravel.IsOnCellCentre(800, 801));
        }

        [Test]
        public void StepTicksDivideTheCellByTheStepSize() {
            Assert.AreEqual(16, RoadTravel.TicksPerStep(100));
            Assert.AreEqual(8, RoadTravel.TicksPerStep(200));
            Assert.AreEqual(0, RoadTravel.TicksPerStep(0));
        }
    

        // ---- engaging --------------------------------------------------------------------

        [Test]
        public void EngagingSAMPLESTHECELLCENTRE_NotWhereThePartyStands() {
            // TASK-554. `worldmove_prox_find_near_pos` (WORLDMOV.C:634-646) writes the party's own
            // cell centre into the position and queries THERE first; the raw position is never
            // asked about. So a party standing anywhere inside a road cell engages, and this fake
            // answers road at the centre ONLY -- if the gate ever samples the raw position again,
            // this fails.
            const int centre = 800;                       // the cell containing 0..1599
            bool IsRoadAt(int x, int y) => x == centre && y == centre;

            foreach ((int x, int y) in new[] { (800, 800), (10, 10), (1590, 10), (10, 1590), (1590, 1590) }) {
                Assert.IsTrue(RoadTravel.TryEngage(x, y, IsRoadAt, out int sx, out int sy),
                    $"standing at ({x},{y}) is inside the road cell and must engage");
                Assert.AreEqual((centre, centre), (sx, sy), "engaging snaps to the cell centre");
            }
        }

        [Test]
        public void EngagingSnapsToTheCentreOfTheCellYouAreStandingIn() {
            bool ok = RoadTravel.TryEngage(1234, 567, AllRoad(), out int x, out int y);

            Assert.IsTrue(ok);
            Assert.AreEqual(800, x);
            Assert.AreEqual(800, y);
        }

        [Test]
        public void EngagingStillRefusesWhenNoNearbyCellCentreIsRoad() {
            // The control: without it the case above is satisfied by a TryEngage that says yes to
            // everything.
            Assert.IsFalse(RoadTravel.TryEngage(800, 800, (x, y) => false, out _, out _));
        }



        [Test]
        public void TheCurrentCellIsAlwaysTriedFirst() {
            // Road under the party and also to the east; the party's own cell wins.
            bool ok = RoadTravel.TryEngage(1500, 1500, RoadAt((800, 800), (800 + Cell, 800)),
                out int x, out int y);

            Assert.IsTrue(ok);
            Assert.AreEqual(800, x);
            Assert.AreEqual(800, y);
        }

        [Test]
        public void TheSnapReachesOnlyTowardTheQuadrantThePartyStandsIn() {
            // Party in the NE quarter of its cell (x,y both past the centre). A road cell to the
            // south-west is NOT reachable, while the same road to the north-east is.
            var southWest = RoadAt((800 - Cell, 800 - Cell));
            var northEast = RoadAt((800 + Cell, 800 + Cell));

            Assert.IsFalse(RoadTravel.TryEngage(1200, 1200, southWest, out _, out _));

            Assert.IsTrue(RoadTravel.TryEngage(1200, 1200, northEast, out int x, out int y));
            Assert.AreEqual(800 + Cell, x);
            Assert.AreEqual(800 + Cell, y);
        }

        [Test]
        public void AndMirrorsThatForTheOppositeQuadrant() {
            // Party in the SW quarter: now the south-west neighbour is the reachable one.
            Assert.IsTrue(RoadTravel.TryEngage(400, 400, RoadAt((800 - Cell, 800 - Cell)),
                out int x, out int y));
            Assert.AreEqual(800 - Cell, x);
            Assert.AreEqual(800 - Cell, y);
        }

        [Test]
        public void EngagingFailsAndLeavesThePartyPutWhenNothingNearbyIsRoad() {
            bool ok = RoadTravel.TryEngage(1234, 567, (_, _) => false, out int x, out int y);

            Assert.IsFalse(ok);
            Assert.AreEqual(1234, x);
            Assert.AreEqual(567, y);
        }

        [Test]
        public void CellCentresAreFoundWestAndSouthOfTheOriginToo() {
            Assert.AreEqual(-Cell + 800, RoadTravel.CellCentre(-100));
            Assert.AreEqual(800, RoadTravel.CellCentre(0));
        }
    }
}
