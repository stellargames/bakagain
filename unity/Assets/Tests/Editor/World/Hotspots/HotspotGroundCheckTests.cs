namespace BakAgain.Tests.Editor.World.Hotspots {
    using BakAgain.Core;
    using BakAgain.World;
    using BakAgain.World.Collision;
    using BakAgain.World.Hotspots;
    using GameData.Resources.Combat;
    using GameData.Resources.Config;

    using NUnit.Framework;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The sweep behind <see cref="HotspotService.EnoughGroundToFight"/> —
    /// <c>combatgrid_plr_prox_sweep_cnt</c>.
    /// </summary>
    public class HotspotGroundCheckTests {
        private const int CellSize = 300;

        private static StartData Start() =>
            new StartData("START.DAT") { CombatGridCellSize = CellSize };

        private static readonly List<int> Resyncs = new List<int>();

        private static (HotspotService Service, GameSession Session, List<(int X, int Y)> Sampled)
            Build(Func<int, int, int> ground) {
            var sampled = new List<(int X, int Y)>();
            var session = new GameSession { PositionX = 0, PositionY = 0, Rotation = 0 };
            Resyncs.Clear();
            var service = new HotspotService(null, null, session, null,
                groundKindAt: (x, y) => {
                    sampled.Add((x, y));
                    return ground(x, y);
                },
                resyncCamera: () => Resyncs.Add(1));
            service.SetZoneForTest(1, start: Start());
            return (service, session, sampled);
        }

        // The first `open` cells the sweep asks about are open ground; the rest are nothing.
        private static Func<int, int, int> FirstNOpen(int open) {
            var seen = 0;
            return (x, y) => seen++ < open ? (int)GameData.Resources.World.WorldEntityType.Ground : -1;
        }

        [Test]
        public void OpenCountryHasRoomAndAVoidDoesNot() {
            var (openService, _, _) = Build((x, y) => (int)GameData.Resources.World.WorldEntityType.Ground);
            Assert.IsTrue(openService.EnoughGroundToFight(null));

            var (emptyService, _, _) = Build((x, y) => -1);
            Assert.IsFalse(emptyService.EnoughGroundToFight(null), "nothing underfoot is not open ground");
        }

        [Test]
        public void WaterIsNotRoomToFightEvenThoughItCoversEveryCell() {
            var (service, _, _) = Build((x, y) => (int)GameData.Resources.World.WorldEntityType.Water);
            Assert.IsFalse(service.EnoughGroundToFight(null));
        }

        [Test]
        public void TheBarIsExactlyTwentyFourCells() {
            Assert.IsFalse(Build(FirstNOpen(CombatGroundCheck.MinimumOpenCells - 1))
                .Service.EnoughGroundToFight(null));
            Assert.IsTrue(Build(FirstNOpen(CombatGroundCheck.MinimumOpenCells))
                .Service.EnoughGroundToFight(null));
        }

        [Test]
        public void EveryCellOfTheFootprintIsSampledOnce() {
            var (service, _, sampled) = Build((x, y) => -1);
            service.EnoughGroundToFight(null);

            Assert.AreEqual(CombatGroundCheck.SampledCells, sampled.Count);
            CollectionAssert.AllItemsAreUnique(sampled);
        }

        [Test]
        public void TheFootprintSitsInFrontOfThePartyAndIsCentredOnThem() {
            var (service, _, sampled) = Build((x, y) => -1);
            service.EnoughGroundToFight(null);   // heading 0 — forward is +Y

            var minX = int.MaxValue;
            var maxX = int.MinValue;
            foreach ((int x, int y) in sampled) {
                Assert.Greater(y, 0, "every cell is ahead of the party, none under or behind them");
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
            }

            Assert.AreEqual(0, minX + maxX, "and it is centred on their line of sight");
        }

        [Test]
        public void TheFootprintTURNSWithTheParty() {
            // The discriminating case: an arena laid out in world axes rather than the party's would
            // sample the same points whatever way they face, and would be wrong for three of the
            // four cardinal headings.
            var (unturned, _, straight) = Build((x, y) => -1);
            unturned.EnoughGroundToFight(null);

            var (service, session, turned) = Build((x, y) => -1);
            session.Rotation = unchecked((short)0x4000);   // a quarter turn
            service.EnoughGroundToFight(null);

            CollectionAssert.AreNotEqual(straight, turned);

            // A quarter turn takes (x, y) to (-y, x). Stated as the transform rather than as a
            // compass direction: heading 0x4000 moves the party along -X in this convention, so
            // calling it "east" would be a guess, and a guess about which way +X points is exactly
            // what got the encounter landing codes mis-described.
            Assert.AreEqual(-straight[0].Y, turned[0].X);
            Assert.AreEqual(straight[0].X, turned[0].Y);
        }

        [Test]
        public void TheSweepUsesTheSameRotationThePartysOwnStepDoes() {
            // Asserted rather than assumed: the arena's forward axis and the party's forward axis
            // have to be the same one, or the footprint is laid out beside them.
            //
            // *** EXACTLY, NOT WITHIN A UNIT. *** This allowed a tolerance of 1 and said why:
            // "Rotate is Q14 fixed point and truncates, StepDelta rounds a double". That was a real
            // disagreement inside the port, and the tolerance absorbed it instead of reporting it —
            // StepDelta was the one that was wrong. The original's step is
            // worldmove_vec2_rotate_add_q14 (WORLDMOV.C:463): a Q14 multiply and an ARITHMETIC
            // shift, which floors. StepDelta now does the same, so the two agree by construction
            // and this can demand equality (TASK-421).
            foreach (ushort heading in new ushort[] { 0, 0x2000, 0x4000, 0x8000, 0xC000 }) {
                var (rx, ry) = ProximityMath.Rotate(0, 5000, heading);
                var (sx, sy) = MovementMath.StepDelta(heading, 5000);

                Assert.AreEqual(sx, rx, $"heading {heading:X4} x");
                Assert.AreEqual(sy, ry, $"heading {heading:X4} y");
            }
        }

        // ---- underground: the party is turned, and the sweep does not run -------------------------

        [Test]
        public void UndergroundTheSweepDoesNotRunAtAll() {
            // The original answers the ground question down there by counting floor pixels and
            // never sweeps ground kinds (CMBTGRID.C:379-387); the kind sweep would refuse nearly
            // every mine fight. With no floor count supplied, as here, the check abstains.
            var (service, _, sampled) = Build((x, y) => -1);   // a void: would refuse above ground
            service.SetZoneForTest(1, start: Start(), underground: true);

            Assert.IsTrue(service.EnoughGroundToFight(null));
            CollectionAssert.IsEmpty(sampled, "and nothing was sampled");
        }

        [Test]
        public void AboveGroundTheSameVoidStillRefuses() {
            // The other half: the skip is about the ZONE KIND, not about the seam being unusable.
            var (service, _, _) = Build((x, y) => -1);
            service.SetZoneForTest(1, start: Start(), underground: false);

            Assert.IsFalse(service.EnoughGroundToFight(null));
        }

        [Test]
        public void UndergroundThePartyIsTurnedToFaceTheEncounter() {
            var (service, session, _) = Build((x, y) => -1);
            service.SetZoneForTest(1, start: Start(), underground: true);
            session.PositionX = (int)GameData.Resources.World.WorldPlacement.CornerOf(0, 32);
            session.PositionY = (int)GameData.Resources.World.WorldPlacement.CornerOf(0, 39);
            session.Rotation = 0;

            // A box below them in Y, so they should be turned around to a half turn.
            service.EnoughGroundToFight(new GameData.Resources.World.TileEventTrigger {
                StartX = 30, EndY = 34, EndX = 34, StartY = 30,
            });

            Assert.AreEqual(unchecked((short)0x8000), session.Rotation);
            Assert.AreEqual(1, Resyncs.Count, "and the view is turned with them, not just the state");
        }

        [Test]
        public void AboveGroundThePartyIsNOTTurned() {
            // The turn is the underground arm's, and turning the party outdoors would spin the view
            // on every ambush.
            var (service, session, _) = Build((x, y) =>
                (int)GameData.Resources.World.WorldEntityType.Ground);
            service.SetZoneForTest(1, start: Start(), underground: false);
            session.PositionX = (int)GameData.Resources.World.WorldPlacement.CornerOf(0, 32);
            session.PositionY = (int)GameData.Resources.World.WorldPlacement.CornerOf(0, 39);
            session.Rotation = 0x1234;

            service.EnoughGroundToFight(new GameData.Resources.World.TileEventTrigger {
                StartX = 30, EndY = 34, EndX = 34, StartY = 30,
            });

            Assert.AreEqual(0x1234, session.Rotation);
            CollectionAssert.IsEmpty(Resyncs, "nothing moved, so nothing to push out");
        }

        [Test]
        public void AHostWithNoWorldToSweepDoesNotRefuseFights() {
            // The default the interface asks for. Underground this is what is passed, and in a test
            // rig it is what everything else assumes.
            var service = new HotspotService(null, null, new GameSession(), null);
            service.SetZoneForTest(1, start: Start());

            Assert.IsTrue(service.EnoughGroundToFight(null));
        }

        [Test]
        public void WithoutSTARTDATTheCheckAbstainsRatherThanRefusing() {
            var service = new HotspotService(null, null, new GameSession(), null,
                groundKindAt: (x, y) => -1);
            service.SetZoneForTest(1);   // no START.DAT

            Assert.IsTrue(service.EnoughGroundToFight(null));
        }
    }
}
