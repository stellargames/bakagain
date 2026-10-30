namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.World;
    using GameData.Resources.World;
    using NUnit.Framework;

    /// <summary>
    /// What the fight hides, and — more importantly — what it must not (TASK-600, TASK-603).
    /// </summary>
    /// <remarks>
    /// Each clause here was established by driving both games, and the numbers in TASK-600's notes
    /// are what these cases restate. Read them before changing an expectation: clause 3 reads as an
    /// arbitrary special case until you know that the mine corridor is a hollow shell AROUND the
    /// camera, which no depth test can tell from an obstacle in front of it.
    /// </remarks>
    public class ArenaSceneryCullTests {
        // The arena's near edge, in camera-forward units. Any number does; the clauses are relative.
        private const float ArenaDepth = 50f;

        [Test]
        public void SomethingInFrontOfTheArenaIsHidden() {
            // The control the other three cases are measured against: without this passing, a
            // predicate that answers "keep" to everything would satisfy them all.
            Assert.IsTrue(WorldRuntime.StandsBetweenCameraAndArena(
                WorldEntityType.Landscape, ArenaDepth - 1f, ArenaDepth));
        }

        [Test]
        public void SomethingBehindTheCameraIsKept() {
            // Depth <= 0 is behind the eye. It cannot obscure a combatant, and the party turns to
            // face the arena when a fight starts, so most of the zone lands here.
            Assert.IsFalse(WorldRuntime.StandsBetweenCameraAndArena(
                WorldEntityType.Landscape, -1f, ArenaDepth));
            Assert.IsFalse(WorldRuntime.StandsBetweenCameraAndArena(
                WorldEntityType.Landscape, 0f, ArenaDepth),
                "exactly at the eye is not in front of it");
        }

        [Test]
        public void SomethingNoNearerThanTheArenaIsKept() {
            // At or beyond the nearest cell a combatant can stand on: scenery the fight is staged
            // in front of, which is the backdrop and must stay drawn.
            Assert.IsFalse(WorldRuntime.StandsBetweenCameraAndArena(
                WorldEntityType.Landscape, ArenaDepth, ArenaDepth),
                "level with the near edge is not in front of it");
            Assert.IsFalse(WorldRuntime.StandsBetweenCameraAndArena(
                WorldEntityType.Landscape, ArenaDepth + 1f, ArenaDepth));
        }

        [Test]
        public void TheMineCorridorIsKeptEvenWhenItMeasuresAsInFront() {
            // The regression this file exists for. Six m_halwp1 pieces measured 9..45 units in
            // front of the camera in Z10 and were culled on exactly that reading, emptying the room
            // and leaving the combatants on black. The depth is right; the conclusion is not.
            Assert.IsFalse(WorldRuntime.StandsBetweenCameraAndArena(
                WorldEntityType.MineCorridor, 9f, ArenaDepth));
            Assert.IsFalse(WorldRuntime.StandsBetweenCameraAndArena(
                WorldEntityType.MineCorridor, 45f, ArenaDepth));
        }

        [Test]
        public void AFlatSurfaceIsKeptEvenWhenItMeasuresAsInFront() {
            // Owner's phone round 4: the trap puzzle fought on black. Two 640-unit ground tiles and a
            // road measured in front of the near edge (their pivots do) and were culled; their surface
            // runs under the whole board, so nothing of it stands between the camera and a fighter.
            foreach (WorldEntityType flat in new[] { WorldEntityType.Ground, WorldEntityType.Road, WorldEntityType.Water }) {
                Assert.IsFalse(WorldRuntime.StandsBetweenCameraAndArena(flat, ArenaDepth - 1f, ArenaDepth), flat.ToString());
            }
            // A bridge is not flat (bridge1..4: Z 0..480, railings and sides), so in front it still goes.
            Assert.IsTrue(WorldRuntime.StandsBetweenCameraAndArena(WorldEntityType.Bridge, ArenaDepth - 1f, ArenaDepth));
        }
    }
}
