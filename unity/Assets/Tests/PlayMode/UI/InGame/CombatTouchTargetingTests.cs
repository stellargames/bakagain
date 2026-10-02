namespace BakAgain.Tests.PlayMode.UI.InGame {
    using System.Collections.Generic;
    using BakAgain.UI.InGame;
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// The side bar's Thrust/Swing: an attack on the cell the combat cursor previews, through the
    /// mouse's click path (spec 2026-09-29-android-touch-aids-design.md).
    /// </summary>
    public class CombatTouchTargetingTests {
        // One enemy (roster slot 2) occupying the 100x100 box around (500, 500).
        private static (int, bool)? EnemyAt(Vector2 p) =>
            Mathf.Abs(p.x - 500) <= 50 && Mathf.Abs(p.y - 500) <= 50 ? (2, false) : ((int, bool)?)null;

        private static (CombatTouchTargeting t, TouchInputState s, List<(int, bool, bool)> hits) Make() {
            var s = new TouchInputState();
            var hits = new List<(int, bool, bool)>();
            var t = new CombatTouchTargeting(s, p => EnemyAt(p), (slot, party, primary) => hits.Add((slot, party, primary)));
            return (t, s, hits);
        }

        [Test]
        public void ThrustIsTheLeftClickOnThePreviewedTarget() {
            var (t, s, hits) = Make();
            s.CombatHoverScreenPoint = new Vector2(500, 500);
            t.Melee(thrust: true);
            CollectionAssert.AreEqual(new[] { (2, false, true) }, hits);
            Assert.IsNull(s.CombatHoverScreenPoint, "an attack ends the preview");
        }

        [Test]
        public void SwingIsTheRightClick() {
            var (t, s, hits) = Make();
            s.CombatHoverScreenPoint = new Vector2(500, 500);
            t.Melee(thrust: false);
            CollectionAssert.AreEqual(new[] { (2, false, false) }, hits);
        }

        [Test]
        public void MeleeWithNothingPreviewedDoesNothing() {
            var (t, s, hits) = Make();
            t.Melee(thrust: true);
            s.CombatHoverScreenPoint = new Vector2(100, 100);   // bare ground
            t.Melee(thrust: true);
            Assert.IsEmpty(hits);
        }
    }
}
