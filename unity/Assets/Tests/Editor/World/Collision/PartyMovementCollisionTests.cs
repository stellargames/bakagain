namespace BakAgain.Tests.Editor.World.Collision {
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.World;
    using BakAgain.World.Collision;
    using BakAgain.World.Converters;
    using GameData.Resources.Config;
    using GameData.Resources.World;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Acceptance criteria 1-6 and 21 of docs/specs/collision-system.md §7 — the walkability gate,
    /// the terrain-follow eye height, the blocked-forward pivot sweep and the bump.
    /// </summary>
    public class PartyMovementCollisionTests {
        private const int Rock = 3;
        private const int Ground = 0;
        private const int Chest = 6;
        private const int ZoneCameraZ = 5000;

        private static MovementData Table() => new MovementData("MOVEMENT.DAT") {
            StepDistances = new[] { 100, 800, 1600 },
            TurnAngles = new[] { 0x1000, 0x2000, 0x4000 },
            SecondsPerStep = new[] { 60, 120, 240 },
        };

        private static GidRegion Square(int half, short baseElevation = 0) {
            var region = new GidRegion { BaseElevation = baseElevation };
            region.Subedges.Add(new GidSubedge { Dx = 0, Dy = 125, AnchorX = (short)-half, AnchorY = (short)half });
            region.Subedges.Add(new GidSubedge { Dx = 125, Dy = 0, AnchorX = (short)half, AnchorY = (short)half });
            region.Subedges.Add(new GidSubedge { Dx = 0, Dy = -125, AnchorX = (short)half, AnchorY = (short)-half });
            region.Subedges.Add(new GidSubedge { Dx = -125, Dy = 0, AnchorX = (short)-half, AnchorY = (short)-half });
            return region;
        }

        private static ZoneTableEntry Entry(int kind, GidRegion region, short radius) {
            var entry = new ZoneTableEntry {
                Dat = new TableDatInfo { EntityType = (WorldEntityType)(byte)kind },
                Gid = new TableGidInfo { XRadius = radius, YRadius = radius },
            };
            if (region != null) {
                entry.Gid.Regions.Add(region);
            }
            return entry;
        }

        private static FilterData AlwaysDraw() {
            var filter = new FilterData("FILTER.DAT");
            for (int level = 0; level < FilterData.DetailLevelCount; level++) {
                var block = new DetailLevelFilter { Level = level };
                for (int k = 0; k < FilterData.EntityTypeCount; k++) {
                    block.DrawDistances[k] = 1;
                }
                block.DrawDistances[7] = -1;
                filter.DetailLevels.Add(block);
            }
            return filter;
        }

        // A broad ground tile plus whatever obstacles the test adds. Overlaps are resolved by
        // distance from the party (vislist_sort), so the ground record sits at its tile centre —
        // 12000 units away, like a real 32000-unit `ground` placement — while obstacles sit right
        // next to the party. That is exactly how the shipped data resolves: a small terrain feature
        // out-ranks the big ground tile it overlaps.
        private static ProximityWorld WorldWith(short groundElevation, params (int kind, int x, int y, int half)[] obstacles) {
            var records = new List<ProximityRecord> {
                new ProximityRecord(Entry(Ground, Square(16000, groundElevation), 16000), 0, 12000, 0, 0),
            };
            foreach (var (kind, x, y, half) in obstacles) {
                records.Add(new ProximityRecord(Entry(kind, Square(half), (short)half), x, y, 0, 0));
            }
            return new ProximityWorld(records, AlwaysDraw());
        }

        private sealed class Rig {
            public PartyMovement Movement;
            public GameSession Session;
            public readonly List<int> Sounds = new();
        }

        private static Rig Build(ProximityWorld world, StepSize step = StepSize.Medium,
            TurnSize turn = TurnSize.Large, bool underground = false) {
            var rig = new Rig { Session = new GameSession { PositionX = 0, PositionY = 0, Rotation = 0 } };
            var prefs = new FakePreferencesService();
            prefs.Current.StepSize = step;
            prefs.Current.TurnSize = turn;
            rig.Movement = new PartyMovement(rig.Session, Table(), prefs, camera: null,
                cameraHeightZ: ZoneCameraZ, cameraPitch: 0, collision: world,
                playSfx: id => rig.Sounds.Add(id), underground: underground);
            return rig;
        }

        [Test]
        public void MoveForward_OnOpenGround_AdvancesAndTakesItsEyeHeightFromTheTerrain() {
            // Acceptance #1.
            var rig = Build(WorldWith(groundElevation: 40));
            rig.Movement.MoveForward();

            Assert.AreEqual(0, rig.Session.PositionX);
            Assert.AreEqual(800, rig.Session.PositionY);
            Assert.AreEqual(ZoneCameraZ + 40, rig.Session.PositionZ, "eye = zone DefaultCameraZ + sampled ground z");
            CollectionAssert.IsEmpty(rig.Sounds);
        }

        [Test]
        public void TheCameraFollowsTheGroundWithTheParty() {
            // *** ASSERTED THE OPPOSITE ONCE, AND THE PLAYER SAID NO. SETTLED 2026-08-26. ***
            // The original DOES follow the ground: worldmove_party_attempt_move (WORLDMOV.C:87-123)
            // lets the step routines write the scanned ground z into the camera position and then
            // adds the zone default on top — literally _cameraHeightZ + groundZ.
            //
            // The absolute z that started the argument (WORLDHIT.C:517-521) is a DIFFERENT camera,
            // the combat arena's. The claim that "nothing in the movement path writes the camera z"
            // came from a grep truncated by head -20 that cut WORLDMOV.C off the results.
            //
            // Ground elevation 40 is what makes this discriminating: with flat ground both readings
            // coincide and the test would pass either way.
            var session = new GameSession { PositionX = 0, PositionY = 0, Rotation = 0 };
            var prefs = new FakePreferencesService();
            prefs.Current.StepSize = StepSize.Medium;
            prefs.Current.TurnSize = TurnSize.Large;
            var camera = new GameObject("cam").AddComponent<Camera>();
            var movement = new PartyMovement(session, Table(), prefs, camera,
                cameraHeightZ: ZoneCameraZ, cameraPitch: 0,
                collision: WorldWith(groundElevation: 40));

            movement.MoveForward();

            Assert.AreEqual(ZoneCameraZ + 40, session.PositionZ, "the PARTY rises with the ground");
            Assert.AreEqual(
                BakCoordinateConverter.ConvertPosition(
                    session.PositionX, session.PositionY, ZoneCameraZ + 40),
                camera.transform.position,
                "and so does the camera, which is what looks right in our renderer");

            Object.DestroyImmediate(camera.gameObject);
        }

        // ---- the road sweep after a bump ------------------------------------------------

        private const int Road = 1;
        private const int Centre = RoadTravel.HalfCell;          // 800 — a cell centre
        private const int WestCell = Centre - RoadTravel.CellSize;

        /// <summary>
        /// A road running through the party's cell and bending WEST: north is not road, so a party
        /// facing north is refused and the sweep has to find the bend.
        /// </summary>
        private static Rig OnARoadBendingWest() {
            var rig = Build(
                WorldWith(groundElevation: 0,
                    (Road, Centre, Centre, 700),
                    (Road, WestCell, Centre, 700)),
                turn: TurnSize.Medium);
            rig.Session.PositionX = Centre;
            rig.Session.PositionY = Centre;
            Assert.IsTrue(rig.Movement.TryEngageTravel(), "the rig must actually be on road");
            return rig;
        }

        [Test]
        public void TheRoadSweepFindsTheBendFromAnExactCompassHeading() {
            // The control for the test below: this is what the original does and what the port has
            // always done. Measured live at (671200, 823200) — heading 0 refused, party turned.
            var rig = OnARoadBendingWest();
            rig.Session.Rotation = 0;

            rig.Movement.MoveForward();

            Assert.AreEqual(0x4000, (ushort)rig.Session.Rotation, "turned onto the bend");
            Assert.AreEqual(Centre, rig.Session.PositionX, "a refused travel step does not translate");
            Assert.AreEqual(Centre, rig.Session.PositionY);
        }

        [Test]
        public void TheRoadSweepStillFindsTheBendAfterABumpLeftTheHeadingOffTheLattice() {
            // *** ONE BLOCKED STEP USED TO END ROAD TRAVEL FOR GOOD. ***
            // The blocked-forward pivot probes by the degree (spec §10.1), so it leaves headings the
            // original could never hold — 38002 where the original turned to 36864, measured in a
            // 30-press lockstep walk on 2026-09-13. Every candidate the sweep tried was that heading
            // plus whole strides, so none of them was ever an exact compass heading and the sweep
            // could not fire again no matter how the player turned. 910 is that same half-degree
            // offset; the sweep must still reach the bend.
            var rig = OnARoadBendingWest();
            rig.Session.Rotation = 910;

            rig.Movement.MoveForward();

            Assert.AreEqual(0x4000, (ushort)rig.Session.Rotation, "snapped to the lattice, then swept");
            Assert.AreEqual(Centre, rig.Session.PositionX);
            Assert.AreEqual(Centre, rig.Session.PositionY);
        }

        [Test]
        public void TravellingRefusesAHeadingThatIsNotAnExactCompassValue() {
            // `worldmove_crossing_check_8dir` switches on the heading and its default is `return 0`,
            // so an off-compass heading is refused exactly like a wrong lattice line. Without the
            // test the port stepped along the TRUNCATED direction instead: measured live at
            // (671200, 823200), heading 64000 moved a clean +1600/+1600 while facing 4° off it.
            // Here west is road, so the refusal is followed by the sweep and the party lands on it.
            var rig = OnARoadBendingWest();
            rig.Session.Rotation = unchecked((short)(0x4000 + 300));   // 300 units past west

            rig.Movement.MoveForward();

            Assert.AreEqual(0x4000, (ushort)rig.Session.Rotation, "snapped onto west rather than stepping");
            Assert.AreEqual(Centre, rig.Session.PositionX, "and it did NOT travel on the truncated heading");
            Assert.AreEqual(Centre, rig.Session.PositionY);
        }

        // The pivot probes one degree at a time (PartyMovement.PivotSteps = 45 across 45 degrees),
        // so the i'th correction is 8192*i/45 in BaK angle space.
        private static short Correction(int degrees) => (short)(0x2000 * degrees / 45);

        // Geometry for the pivot tests. Party at the origin facing +Y, step 800.
        //
        //   * A rock dead ahead of half-extent 306 blocks every probe whose |dest.x| <= 306.
        //     dest.x = -sin(delta)*800, so the sweep clears it at 23 degrees (313) and not at
        //     22 (300) — six units of margin either side of the transition.
        //   * A second rock off to +X blocks the counter-clockwise probe at that same angle, so
        //     the clockwise side wins outright instead of the two opening together.
        private static ProximityWorld WallWithAnOpeningClockwise() =>
            WorldWith(0, (Rock, 0, 800, 306), (Rock, 600, 700, 400));

        [Test]
        public void MoveForward_IntoARockFace_TurnsOnlyAsFarAsItMustToClear() {
            // Acceptance #2 as amended by spec §10.1: the correction is the SMALLEST one that opens,
            // not a whole turn-stride. Here that is 23 degrees; 22 is still inside the rock.
            var rig = Build(WallWithAnOpeningClockwise());
            rig.Movement.MoveForward();

            Assert.AreEqual(0, rig.Session.PositionX, "a pivot never translates");
            Assert.AreEqual(0, rig.Session.PositionY, "a pivot never translates");
            Assert.AreEqual(Correction(23), rig.Session.Rotation, "turned just far enough to clear");
            CollectionAssert.IsEmpty(rig.Sounds, "a successful pivot is not a bump");
        }

        [Test]
        public void MoveForward_PivotCorrection_IgnoresTheTurnSizePreference() {
            // The DOS sweep widened by the player's turn-stride, so TurnSize changed where you ended
            // up. The correction is now the geometry's, not the preference's: same answer for the
            // 5.625-degree stride and the 22.5-degree one.
            var small = Build(WallWithAnOpeningClockwise(), turn: TurnSize.Small);
            var large = Build(WallWithAnOpeningClockwise(), turn: TurnSize.Large);
            small.Movement.MoveForward();
            large.Movement.MoveForward();

            Assert.AreEqual(Correction(23), small.Session.Rotation);
            Assert.AreEqual(large.Session.Rotation, small.Session.Rotation);
        }

        [Test]
        public void MoveForward_WhenTheOnlyOpeningIsBeyond45Degrees_StillPivots() {
            // *** THE SWEEP REACHES 90 DEGREES, NOT 45. *** This test asserted a bump until
            // TASK-416: worldmove_sweep_alt_headings runs max_iters = 0x4000 / g_nWorldGridStride,
            // and 0x4000 IS 90 degrees, so the widest probe is a quarter turn either way. The
            // 45-degree reading came from a spec note, not from the routine, and it left the party
            // stopping dead in gaps the original walks straight through — which is what the user
            // reported as "I do not think getting stuck like this happened in the original".
            //
            // 90 degrees is not an arbitrary cap either: at a quarter turn the destination is
            // directly abeam, so a wall AHEAD can never need more than that to clear. Anything
            // steeper is behind the party, which is why widening it further would be meaningless
            // rather than merely wrong — and why the bump path is fenced by the both-sides case
            // below instead of by a steeper opening.
            //
            // The wall ahead has half-extent 700, so it needs |dest.x| > 700 — 62 degrees — while a
            // rock off to +X keeps the two sides from ever opening together (which would bump for a
            // different reason).
            var world = WorldWith(0, (Rock, 0, 800, 700), (Rock, 600, 400, 500));
            var rig = Build(world);

            // The opening is real, so a bump here would be the sweep refusing it rather than there
            // being nothing to find.
            world.BuildCandidates(0, 0, (int)DetailLevel.High);
            Assert.IsTrue(
                world.ProbeWalkable(0, 0, unchecked((ushort)Correction(62)), 800, out _, out _),
                "precondition: 62 degrees clockwise is walkable");

            rig.Movement.MoveForward();

            Assert.AreEqual(0, rig.Session.PositionX, "a pivot never translates");
            Assert.AreEqual(0, rig.Session.PositionY, "a pivot never translates");
            Assert.AreEqual(Correction(62), rig.Session.Rotation, "turned as far as it took to clear");
            CollectionAssert.IsEmpty(rig.Sounds, "a successful pivot is not a bump");
        }

        [Test]
        public void MoveForward_WhenBothSweepsOpenAtTheSameAngle_BumpsInstead() {
            // Acceptance #3 (ambiguous case): rock ahead only, so +90 and -90 both open on the
            // first iteration and the sweep gives up.
            var rig = Build(WorldWith(0, (Rock, 0, 800, 300)));
            rig.Movement.MoveForward();

            Assert.AreEqual(0, rig.Session.PositionY);
            Assert.AreEqual((short)0, rig.Session.Rotation);
            CollectionAssert.AreEqual(new[] { 0x31 }, rig.Sounds);
        }

        [Test]
        public void MoveForward_InADeadEnd_DoesNotMoveOrTurnAndBumps() {
            // Acceptance #3: nothing open anywhere in the sweep. A 400-unit island of ground with
            // rock all round means every probe at 800 units lands off the polygon soup.
            var records = new List<ProximityRecord> {
                new ProximityRecord(Entry(Ground, Square(400), 400), 0, 0, 0, 0),
            };
            var rig = Build(new ProximityWorld(records, AlwaysDraw()));
            rig.Movement.MoveForward();

            Assert.AreEqual(0, rig.Session.PositionX);
            Assert.AreEqual(0, rig.Session.PositionY);
            Assert.AreEqual((short)0, rig.Session.Rotation);
            CollectionAssert.AreEqual(new[] { 0x31 }, rig.Sounds);
        }

        [Test]
        public void MoveBackward_IntoAWall_GetsNoPivotRescue() {
            // Acceptance #4: TryAlternativeMove is only reached for mode 1.
            var rig = Build(WorldWith(0, (Rock, 0, -800, 300)));
            rig.Movement.MoveBackward();

            Assert.AreEqual(0, rig.Session.PositionY);
            Assert.AreEqual((short)0, rig.Session.Rotation);
            CollectionAssert.AreEqual(new[] { 0x31 }, rig.Sounds);
        }

        [Test]
        public void MoveForward_ThroughAChest_IsNotBlocked() {
            // Acceptance #5: props carry no GID regions, so they are never a proximity hit —
            // the chest is in the candidate list and simply cannot be hit.
            var records = new List<ProximityRecord> {
                new ProximityRecord(Entry(Ground, Square(16000), 16000), 0, 12000, 0, 0),
                new ProximityRecord(Entry(Chest, region: null, radius: 400), 0, 800, 0, 0),
            };
            var rig = Build(new ProximityWorld(records, AlwaysDraw()));
            rig.Movement.MoveForward();

            Assert.AreEqual(800, rig.Session.PositionY);
        }

        [Test]
        public void MoveForward_Underground_QuartersTheStepDistance() {
            // Acceptance #6.
            var rig = Build(WorldWith(0), step: StepSize.Large, underground: true); // 1600 >> 2
            rig.Movement.MoveForward();

            Assert.AreEqual(400, rig.Session.PositionY);
        }

        [Test]
        public void MoveForward_OffTheEdgeOfThePolygonSoup_IsBlocked() {
            // Acceptance #21.
            var records = new List<ProximityRecord> {
                new ProximityRecord(Entry(Ground, Square(400), 400), 0, 0, 0, 0),
            };
            var rig = Build(new ProximityWorld(records, AlwaysDraw()));
            rig.Movement.MoveForward();

            Assert.AreEqual(0, rig.Session.PositionY);
        }

        [Test]
        public void Turn_IsNeverBlocked() {
            // Non-requirement #6: turning is unconditional even when every direction is walled.
            var records = new List<ProximityRecord>();
            var rig = Build(new ProximityWorld(records, AlwaysDraw()));
            rig.Movement.TurnLeft();
            Assert.AreEqual((short)0x4000, rig.Session.Rotation);
        }

        // ---- engaging follow-road (TASK-554) ---------------------------------------------

        // A road patch 600 units across, centred on cell (0,0)'s centre at (800,800), so it spans
        // 500..1100 on both axes. Every corner of that 1600-unit cell is therefore OUTSIDE the
        // patch and samples as ground — which is the whole point.
        private static ProximityWorld RoadAtTheCellCentre() =>
            WorldWith(groundElevation: 0, (Road, 800, 800, 300));

        [Test]
        public void FollowRoadEngagesFromANYWHEREInsideARoadCell() {
            // *** THE GATE SAMPLES THE CELL CENTRE, NOT WHERE THE PARTY STANDS. ***
            // worldmove_prox_find_near_pos (WORLDMOV.C:634-646) writes the party's own cell centre
            // into the position and queries THERE; the raw position is never asked about. The gate
            // used to scan the raw position for a road kind, so standing off-centre in a road cell
            // refused while the snap would have found the road at distance 0 — measured in the
            // field from four directions before anyone read the source.
            foreach ((int x, int y) in new[] { (800, 800), (60, 60), (1540, 60), (60, 1540), (1400, 200) }) {
                Rig rig = Build(RoadAtTheCellCentre());
                rig.Session.PositionX = x;
                rig.Session.PositionY = y;

                Assert.IsTrue(rig.Movement.CanEngageTravel(),
                    $"standing at ({x},{y}) is inside the road cell and must be able to engage");
            }
        }

        [Test]
        public void EngagingFromAnOffCentreSpotSnapsToTheCellCentre() {
            // The corner case above, driven all the way through rather than stopping at the gate:
            // a gate that says yes and a snap that then fails would be the same bug one step later.
            Rig rig = Build(RoadAtTheCellCentre());
            rig.Session.PositionX = 1400;
            rig.Session.PositionY = 200;

            Assert.IsTrue(rig.Movement.TryEngageTravel());
            Assert.AreEqual((800, 800), (rig.Session.PositionX, rig.Session.PositionY));
            Assert.IsTrue(rig.Movement.IsTravelling);
        }

        [Test]
        public void FollowRoadStillRefusesWhereNoNearbyCellCentreIsRoad() {
            // The control. Without it both tests above are satisfied by a gate that says yes to
            // everything, which is the failure mode opposite to the one being fixed.
            Rig rig = Build(WorldWith(groundElevation: 0));
            rig.Session.PositionX = 1400;
            rig.Session.PositionY = 200;

            Assert.IsFalse(rig.Movement.CanEngageTravel());
            Assert.IsFalse(rig.Movement.TryEngageTravel());
        }

        [Test]
        public void FollowRoadCannotBeEngagedTwice() {
            // The !alreadyTravelling half, which moved here when RoadTravel.CanEngage was deleted.
            Rig rig = Build(RoadAtTheCellCentre());
            rig.Session.PositionX = 800;
            rig.Session.PositionY = 800;

            Assert.IsTrue(rig.Movement.TryEngageTravel());
            Assert.IsFalse(rig.Movement.CanEngageTravel(),
                "already travelling: the travel control must not offer to engage again");
        }
    }
}
