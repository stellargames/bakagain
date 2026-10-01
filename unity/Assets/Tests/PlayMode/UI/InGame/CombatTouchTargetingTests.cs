namespace BakAgain.Tests.PlayMode.UI.InGame {
    using System.Collections.Generic;
    using BakAgain.UI.InGame;
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Touch combat targeting (spec 2026-09-29-android-touch-aids-design.md): the selection feeds the
    /// original's hover pick, and attacks go through the mouse's click path.
    /// </summary>
    public class CombatTouchTargetingTests {
        private sealed class MemPrefs : IPrefsStore {
            public int GetInt(string k, int f) => f;
            public void SetInt(string k, int v) { }
        }

        // One enemy (roster slot 2) occupying the 100x100 box around (500, 500).
        private static (int, bool)? EnemyAt(Vector2 p) =>
            Mathf.Abs(p.x - 500) <= 50 && Mathf.Abs(p.y - 500) <= 50 ? (2, false) : ((int, bool)?)null;

        private int _groundClicks;
        private (int, int)? _ringed;

        // Arena cells are 100x100 screen boxes in this fake; below y=50 and right of x=560 is off the grid.
        private static (int, int)? CellAt(Vector2 p) =>
            p.y < 50 || p.x >= 560 ? ((int, int)?)null : ((int)(p.x / 100), (int)(p.y / 100));

        private (CombatTouchTargeting t, TouchInputState s, List<(int, bool, bool)> hits) Make() {
            var s = new TouchInputState(new MemPrefs());
            var hits = new List<(int, bool, bool)>();
            _groundClicks = 0;
            _ringed = null;
            var t = new CombatTouchTargeting(s, p => EnemyAt(p),
                (slot, party, primary) => hits.Add((slot, party, primary)), 60f, () => _groundClicks++,
                CellAt, c => _ringed = c);
            return (t, s, hits);
        }

        [Test]
        public void FirstTapSelectsWithoutAttacking() {
            var (t, s, hits) = Make();
            t.Tap(new Vector2(510, 490));
            Assert.IsEmpty(hits);
            Assert.AreEqual(new Vector2(510, 490), s.CombatHoverScreenPoint, "the hover pick now reads the selection");
        }

        [Test]
        public void SecondTapOnTheSameTargetThrusts() {
            var (t, s, hits) = Make();
            t.Tap(new Vector2(510, 490));
            t.Tap(new Vector2(480, 520));
            CollectionAssert.AreEqual(new[] { (2, false, true) }, hits);
            Assert.IsNull(s.CombatHoverScreenPoint, "an attack ends the selection");
        }

        [Test]
        public void SwingButtonSwingsAtTheSelection() {
            var (t, s, hits) = Make();
            t.Tap(new Vector2(500, 500));
            t.Melee(thrust: false);
            CollectionAssert.AreEqual(new[] { (2, false, false) }, hits);
        }

        [Test]
        public void MeleeWithNothingSelectedDoesNothing() {
            var (t, s, hits) = Make();
            t.Melee(thrust: true);
            Assert.IsEmpty(hits);
        }

        [Test]
        public void ANearMissSnapsToTheTarget() {
            var (t, s, hits) = Make();
            t.Tap(new Vector2(590, 500));   // off the grid, 40 px from the target: inside the 60 px snap ring
            Assert.IsNotNull(s.CombatHoverScreenPoint);
            Assert.AreEqual(((int, bool)?)(2, false), EnemyAt(s.CombatHoverScreenPoint.Value), "snapped onto the target");
        }

        [Test]
        public void FingerHoverPreviewsAboveTheFingerAndThrustsOnLift() {
            var (t, s, hits) = Make();
            // Screen coords are bottom-left origin, so "above the finger" is +y.
            t.FingerMoved(new Vector2(500, 380), offsetPixels: 120f);
            Assert.AreEqual(new Vector2(500, 500), s.CombatHoverScreenPoint);
            Assert.IsEmpty(hits, "no attack while the finger is down");
            t.FingerLifted();
            CollectionAssert.AreEqual(new[] { (2, false, true) }, hits);
        }

        [Test]
        public void LiftingOverGroundIsAGroundClick() {
            var (t, s, hits) = Make();
            t.FingerMoved(new Vector2(100, 100), 120f);
            t.FingerLifted();
            Assert.IsEmpty(hits, "no attack over bare ground");
            Assert.AreEqual(1, _groundClicks, "the click goes on: move, cast on a cell, place a summon (final review #1)");
            Assert.IsNull(s.CombatHoverScreenPoint);
        }

        /// <summary>
        /// Owner, 2026-10-01: moving was one tap and "icky". Ground works like a target now: the first
        /// tap rings the cell, a tap on the ringed cell is the mouse's ground click (move, cast, summon).
        /// </summary>
        [Test]
        public void AGroundTapRingsTheCellAndASecondTapMovesThere() {
            var (t, s, hits) = Make();
            t.Tap(new Vector2(150, 150));
            Assert.AreEqual(((int, int)?)(1, 1), _ringed, "the cell is ringed");
            Assert.AreEqual(0, _groundClicks, "nothing moves yet");
            t.Tap(new Vector2(170, 130));   // same cell
            Assert.AreEqual(1, _groundClicks, "a tap on the ringed cell is the ground click");
            Assert.IsNull(_ringed, "and the ring goes");
            Assert.IsEmpty(hits);
        }

        /// <summary>
        /// Found live (2026-10-01): an empty cell next to Locklear could not be chosen — the snap
        /// pulled the tap onto him. On the grid, the cell under the finger is the answer, as the
        /// original's cursor pick is (TASK-589); snapping only rescues a tap that missed the grid.
        /// </summary>
        [Test]
        public void AnEmptyCellBesideATargetIsThatCellNotTheTarget() {
            var (t, s, hits) = Make();
            t.Tap(new Vector2(510, 440));   // cell (5,4): 10 px from the enemy, still an empty cell
            Assert.AreEqual(((int, int)?)(5, 4), _ringed, "the empty cell is ringed");
            Assert.IsNull(s.CombatHoverScreenPoint, "no target was selected");
        }

        [Test]
        public void TappingAnotherCellMovesTheRing() {
            var (t, s, hits) = Make();
            t.Tap(new Vector2(150, 150));
            t.Tap(new Vector2(250, 150));
            Assert.AreEqual(((int, int)?)(2, 1), _ringed);
            Assert.AreEqual(0, _groundClicks);
        }

        [Test]
        public void SelectingATargetDropsTheRingedCell() {
            var (t, s, hits) = Make();
            t.Tap(new Vector2(150, 150));
            t.Tap(new Vector2(500, 500));
            Assert.IsNull(_ringed);
            Assert.AreEqual(new Vector2(500, 500), s.CombatHoverScreenPoint);
        }

        [Test]
        public void ATapOffTheGridClearsEverything() {
            var (t, s, hits) = Make();
            t.Tap(new Vector2(150, 150));
            t.Tap(new Vector2(150, 10));
            Assert.IsNull(_ringed);
            Assert.AreEqual(0, _groundClicks);
        }
    }
}
