namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using GameData.Resources.Location;
    using GameData.Resources.World;
    using NUnit.Framework;
    using System;

    /// <summary>
    /// Landing the party somewhere — the state half of a teleport
    /// (<c>TeleportToLocation</c> @0x735bf).
    /// </summary>
    [TestFixture]
    public class GameSessionPlaceAtTests {
        private static Location Somewhere() => new Location {
            ZoneNumber = 9, X = 17, Y = 23, XOffset = 12, YOffset = 30, ZRotation = 512,
        };

        [Test]
        public void PlaceAt_SetsZoneTileAndFacing() {
            var session = new GameSession();

            session.PlaceAt(Somewhere());

            Assert.That(session.CurrentZone, Is.EqualTo(9));
            Assert.That(session.WorldX, Is.EqualTo(17));
            Assert.That(session.WorldY, Is.EqualTo(23));
            Assert.That(session.Rotation, Is.EqualTo(512));
        }

        [Test]
        public void PlaceAt_PutsTheFinePositionInTheMiddleOfTheSubCell() {
            var session = new GameSession();

            session.PlaceAt(Somewhere());

            // Same owner the town approach places arrivals with — not a second copy of
            // tile*64000 + offset*1600 + 800.
            Assert.That(session.PositionX, Is.EqualTo((int)WorldPlacement.CentreOf(17, 12)));
            Assert.That(session.PositionY, Is.EqualTo((int)WorldPlacement.CentreOf(23, 30)));
        }

        [Test]
        public void PlaceAt_LeavesZAlone() {
            // Ground height belongs to where you landed, not to the destination record: the zone
            // load and the next camera sync resolve it. Writing one here invents a number.
            var session = new GameSession { PositionZ = 4242 };

            session.PlaceAt(Somewhere());

            Assert.That(session.PositionZ, Is.EqualTo(4242));
        }

        [Test]
        public void PlaceAt_TileAndFinePositionAlwaysAgree() {
            // The failure this guards: a party rendered in one cell and colliding in another.
            var session = new GameSession();

            session.PlaceAt(Somewhere());

            Assert.That(session.PositionX / WorldPlacement.TileSize, Is.EqualTo((int)session.WorldX));
            Assert.That(session.PositionY / WorldPlacement.TileSize, Is.EqualTo((int)session.WorldY));
        }

        [Test]
        public void TileAndFinePositionStillAgree_AfterThePartyWALKS() {
            // *** THE HALF THE TEST ABOVE WAS MISSING. *** It asserts the invariant at the one
            // moment the two were assigned together, so it held while the party stood still and
            // said nothing about what happens once it moves — which is the only interesting case.
            //
            // Measured 2026-09-10: 29 of the 36 saves this port wrote during the chapter-1 run
            // recorded the tile the party ENTERED THE ZONE on, not the one it was standing in.
            // SAVE39 sat at tile (19,10) and stored (10,18). All 16 shipped original saves agree
            // with their own position, because the original keeps nPlayerTileX/Y in step
            // (CZONE.C:266) — so a port save was not interchangeable with one.
            var session = new GameSession();
            session.PlaceAt(Somewhere());
            byte tileOnArrival = session.WorldX;

            // Two whole tiles east and one north, the way walking gets there.
            session.PositionX += WorldPlacement.TileSize * 2;
            session.PositionY += WorldPlacement.TileSize;

            Assert.That(session.WorldX, Is.EqualTo((byte)(tileOnArrival + 2)),
                "the recorded tile did not follow the party");
            Assert.That(session.PositionX / WorldPlacement.TileSize, Is.EqualTo((int)session.WorldX));
            Assert.That(session.PositionY / WorldPlacement.TileSize, Is.EqualTo((int)session.WorldY));
        }

        [Test]
        public void TheTileIsDerived_SoASaveCannotDisagreeWithItsOwnPosition() {
            // A save whose stored tile disagrees with its stored position now loads at the tile the
            // position names. Hydration is exercised elsewhere; this pins the arithmetic that makes
            // the stored byte unreachable — sub-cell 39 is the last cell in a tile and must not
            // round up into the next one.
            var session = new GameSession {
                PositionX = (int)WorldPlacement.CentreOf(19, WorldPlacement.SubCellsPerTile - 1),
                PositionY = (int)WorldPlacement.CentreOf(10, 0)
            };

            Assert.That(session.WorldX, Is.EqualTo((byte)19));
            Assert.That(session.WorldY, Is.EqualTo((byte)10));
        }

        [Test]
        public void PlaceAt_RejectsNothingToPlaceAt() {
            var session = new GameSession();

            Assert.Throws<ArgumentNullException>(() => session.PlaceAt(null));
        }
    }
}
