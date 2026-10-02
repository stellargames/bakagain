namespace BakAgain.UI.InGame {
    using System;
    using UnityEngine;

    /// <summary>
    /// C3, the cursor variant of the Android touch aids (owner, 2026-10-01): a cell cursor on the
    /// 8x13 arena that the side-bar pad moves, with context buttons acting on the cell under it.
    /// </summary>
    /// <remarks>
    /// A pad direction picks the neighbour that lies that way ON SCREEN, using the projection the
    /// arena is drawn with, so up is "away" whichever way the camera faces the grid.
    /// </remarks>
    public sealed class CombatCursor {
        private const int Columns = 8;
        private const int Rows = 13;
        private static readonly (int, int)[] Neighbours = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        private readonly Func<int, int, Vector2?> _cellOnScreen;

        /// <param name="cellOnScreen">Where a cell's centre lands on screen, or null if it does not.</param>
        public CombatCursor(Func<int, int, Vector2?> cellOnScreen) {
            _cellOnScreen = cellOnScreen;
        }

        public (int Column, int Row) Cell { get; set; }

        /// <summary>Moves one cell in a screen direction (up = (0,1)); stays put at the grid's edge.</summary>
        public void Step(Vector2 screenDirection) {
            Vector2? here = _cellOnScreen(Cell.Column, Cell.Row);
            if (!here.HasValue) {
                return;
            }
            (int, int)? best = null;
            float bestDot = 0.5f;   // anything within ~60 degrees of the asked direction
            foreach ((int dc, int dr) in Neighbours) {
                int c = Cell.Column + dc;
                int r = Cell.Row + dr;
                if (c < 0 || c >= Columns || r < 0 || r >= Rows) {
                    continue;
                }
                Vector2? there = _cellOnScreen(c, r);
                if (!there.HasValue) {
                    continue;
                }
                float dot = Vector2.Dot((there.Value - here.Value).normalized, screenDirection.normalized);
                if (dot > bestDot) {
                    bestDot = dot;
                    best = (c, r);
                }
            }
            if (best.HasValue) {
                Cell = best.Value;
            }
        }
    }
}
