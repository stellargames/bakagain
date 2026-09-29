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

        private (CombatTouchTargeting t, TouchInputState s, List<(int, bool, bool)> hits) Make() {
            var s = new TouchInputState(new MemPrefs());
            var hits = new List<(int, bool, bool)>();
            _groundClicks = 0;
            var t = new CombatTouchTargeting(s, p => EnemyAt(p),
                (slot, party, primary) => hits.Add((slot, party, primary)), 60f, () => _groundClicks++);
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
            t.Tap(new Vector2(590, 500));   // 40 px outside the box, inside the 60 px snap ring
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

        [Test]
        public void TappingEmptyGroundClearsAndIsAGroundClick() {
            var (t, s, hits) = Make();
            t.Tap(new Vector2(500, 500));
            t.Tap(new Vector2(100, 100));
            Assert.IsNull(s.CombatHoverScreenPoint);
            Assert.IsEmpty(hits);
            Assert.AreEqual(1, _groundClicks,
                "a ground tap is still the mouse's ground click: combat movement, ground spells, summons (final review #1)");
        }
    }
}
