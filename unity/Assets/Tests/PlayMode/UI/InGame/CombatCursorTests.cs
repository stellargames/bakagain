namespace BakAgain.Tests.PlayMode.UI.InGame {
    using BakAgain.UI.InGame;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// C3, the cursor variant (owner, 2026-10-01): the side-bar pad moves a cell cursor over the 8x13
    /// arena. A pad direction picks the neighbour that lies that way ON SCREEN, so it stays right
    /// whatever the arena's camera angle.
    /// </summary>
    public class CombatCursorTests {
        // A fake camera: columns run left-to-right, rows run up the screen (away from the viewer).
        private static Vector2? Screen(int col, int row) => new Vector2(100 + col * 50, 100 + row * 30);

        // The same arena seen from the other side: everything mirrored.
        private static Vector2? Flipped(int col, int row) => new Vector2(1000 - col * 50, 1000 - row * 30);

        [Test]
        public void PadDirectionsFollowTheScreen() {
            var c = new CombatCursor(Screen) { Cell = (3, 5) };
            c.Step(Vector2.up);
            Assert.AreEqual((3, 6), c.Cell, "up = away from the viewer");
            c.Step(Vector2.right);
            Assert.AreEqual((4, 6), c.Cell);
            c.Step(Vector2.down);
            c.Step(Vector2.left);
            Assert.AreEqual((3, 5), c.Cell);
        }

        [Test]
        public void AFlippedCameraFlipsTheGridDirection() {
            var c = new CombatCursor(Flipped) { Cell = (3, 5) };
            c.Step(Vector2.up);
            Assert.AreEqual((3, 4), c.Cell, "up on screen is a lower row from the other side");
        }

        [Test]
        public void TheCursorStaysOnTheGrid() {
            var c = new CombatCursor(Screen) { Cell = (0, 12) };
            c.Step(Vector2.left);
            c.Step(Vector2.up);
            Assert.AreEqual((0, 12), c.Cell, "8x13: columns 0..7, rows 0..12");
        }
    }
}
