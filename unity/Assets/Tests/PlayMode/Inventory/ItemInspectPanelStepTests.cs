namespace BakAgain.Tests.PlayMode.Inventory {
    using BakAgain.UI.Inventory;
    using NUnit.Framework;

    /// <summary>
    /// Pins the icon-travel easing from <c>invinspect_animate_item_move</c> @0x5A0CB: each frame
    /// advances a twelfth of the remaining distance plus one pixel in the direction of travel.
    /// </summary>
    public class ItemInspectPanelStepTests {
        [Test]
        public void AtTarget_DoesNotMove() {
            Assert.AreEqual(58, ItemInspectPanel.StepToward(58, 58));
        }

        [Test]
        public void MovesATwelfthPlusOne_Forwards() {
            // 120 away: 120/12 + 1 = 11.
            Assert.AreEqual(11, ItemInspectPanel.StepToward(0, 120));
        }

        [Test]
        public void MovesATwelfthPlusOne_Backwards() {
            Assert.AreEqual(-11, ItemInspectPanel.StepToward(0, -120));
        }

        /// <summary>The +/-1 is what stops it stalling: integer division alone yields 0 once the
        /// gap is under 12, so the icon would never arrive.</summary>
        [Test]
        public void ShortDistances_StillAdvance() {
            for (int gap = 1; gap < 12; gap++) {
                int next = ItemInspectPanel.StepToward(0, gap);
                Assert.Greater(next, 0, $"gap {gap} must still move");
            }
        }

        /// <summary>
        /// Under 12px the division contributes nothing, so the step is the bare +/-1 — a 5px gap
        /// closes over five frames rather than in one. (An earlier version of this test asserted it
        /// landed in a single step, which the original does not do.)
        /// </summary>
        [Test]
        public void ShortGap_ClosesOnePixelPerFrame() {
            Assert.AreEqual(1, ItemInspectPanel.StepToward(0, 5));
            Assert.AreEqual(-1, ItemInspectPanel.StepToward(0, -5));
            Assert.AreEqual(1, ItemInspectPanel.StepToward(0, 1), "a 1px gap lands exactly");
        }

        /// <summary>
        /// delta/12 + 1 &lt;= delta for every delta >= 1, so the original can't overshoot; this pins
        /// that property rather than the clamp guarding it.
        /// </summary>
        [Test]
        public void NeverOvershoots_AtAnyDistance() {
            for (int gap = 1; gap <= 300; gap++) {
                Assert.LessOrEqual(ItemInspectPanel.StepToward(0, gap), gap, $"forward gap {gap}");
                Assert.GreaterOrEqual(ItemInspectPanel.StepToward(0, -gap), -gap, $"back gap {gap}");
            }
        }

        [Test]
        public void Converges_FromATypicalGridDistance() {
            // The far corner of the general grid to the inspect centre is ~200 VGA px.
            int x = 260, frames = 0;
            while (x != 58 && frames < 200) {
                x = ItemInspectPanel.StepToward(x, 58);
                frames++;
            }
            Assert.AreEqual(58, x, "must reach the target");
            Assert.Less(frames, 60, "and inside a second at 60fps");
            Assert.Greater(frames, 5, "but not instantly — it's an easing, not a jump");
        }
    }
}
