namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.World.Encounters;
    using GameData.Resources.Combat;
    using NUnit.Framework;

    /// <summary>
    /// A point on the arena floor back to the cell under it — the pick side of TASK-112.
    /// </summary>
    /// <remarks>
    /// Everything here is the inverse of the placement HotspotService already does, so the tests are
    /// written as round trips through that same forward chain rather than against expected numbers.
    /// A number would only restate the formula; a round trip catches a sign, an axis swap or a
    /// dropped party offset.
    /// </remarks>
    public class ArenaCellPickerTests {
        private const int Cell = 300;          // StartData.CombatGridCellSize as shipped
        private const int PartyX = 123456;     // deliberately not the origin
        private const int PartyY = -98765;

        /// <summary>Where the placement code would stand a combatant on this cell.</summary>
        private static (int X, int Y) Placed(int column, int row, int rotation) {
            (int across, int away) = CombatArenaPlacement.CellOffset(column, row, Cell);
            (int dx, int dy) = BakAgain.World.Collision.ProximityMath.Rotate(across, away, rotation);
            return (PartyX + dx, PartyY + dy);
        }

        [Test]
        public void EveryCellRoundTripsAtEveryQuarterTurn() {
            // 0, 90, 180, 270 in BaK angle space. A sign error survives one heading and not four.
            foreach (int rotation in new[] { 0, 16384, 32768, 49152 }) {
                for (var row = 0; row < CombatGrid.Height; row++) {
                    for (var column = 0; column < CombatGrid.Width; column++) {
                        (int x, int y) = Placed(column, row, rotation);
                        Assert.AreEqual((column, row),
                            ArenaCellPicker.CellAt(x, y, PartyX, PartyY, rotation, Cell),
                            $"cell ({column},{row}) at rotation {rotation}");
                    }
                }
            }
        }

        [Test]
        public void ItRoundTripsAtAnAWKWARDHeadingToo() {
            // Not a multiple of 90: this is where Q14 rounding actually bites, and a cell centre has
            // to survive it. 30 degrees.
            const int Rotation = 5461;
            for (var row = 0; row < CombatGrid.Height; row++) {
                for (var column = 0; column < CombatGrid.Width; column++) {
                    (int x, int y) = Placed(column, row, Rotation);
                    Assert.AreEqual((column, row),
                        ArenaCellPicker.CellAt(x, y, PartyX, PartyY, Rotation, Cell),
                        $"cell ({column},{row})");
                }
            }
        }

        [Test]
        public void RotatingTheWRONGWayIsNotHarmless() {
            // *** THE MISTAKE THIS CLASS EXISTS TO PREVENT. *** Un-rotating by +Rotation mirrors the
            // arena through the party. It is not a crash and not obviously wrong on screen — cells
            // still resolve — so this pins that the two directions genuinely disagree.
            const int Rotation = 16384;
            (int x, int y) = Placed(5, 9, Rotation);

            (int across, int away) = BakAgain.World.Collision.ProximityMath.Rotate(
                x - PartyX, y - PartyY, Rotation);
            Assert.AreNotEqual((5, 9), CombatArenaPlacement.CellAt(across, away, Cell),
                "the wrong sign must not accidentally agree");
            Assert.AreEqual((5, 9), ArenaCellPicker.CellAt(x, y, PartyX, PartyY, Rotation, Cell));
        }

        [Test]
        public void APointWellOutsideTheArenaIsNull() {
            const int Rotation = 16384;
            (int x, int y) = Placed(0, 0, Rotation);

            Assert.Null(ArenaCellPicker.CellAt(x + 100 * Cell, y, PartyX, PartyY, Rotation, Cell));
            Assert.Null(ArenaCellPicker.CellAt(x, y - 100 * Cell, PartyX, PartyY, Rotation, Cell));
        }

        [Test]
        public void ThePartyOffsetIsNOTOptional() {
            // Forgetting to subtract the party position leaves the arena parked at the world origin.
            // With the party far from it, every pick then misses.
            const int Rotation = 0;
            (int x, int y) = Placed(4, 6, Rotation);

            Assert.AreEqual((4, 6), ArenaCellPicker.CellAt(x, y, PartyX, PartyY, Rotation, Cell));
            Assert.Null(ArenaCellPicker.CellAt(x, y, 0, 0, Rotation, Cell),
                "the same point read against the origin is nowhere near the grid");
        }
    }
}
