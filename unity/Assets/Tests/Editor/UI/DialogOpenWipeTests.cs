namespace BakAgain.Tests.Editor.UI {
    using BakAgain.UI;
    using GameData.Resources.Dialog;
    using NUnit.Framework;
    using UnityEngine;

    public class DialogOpenWipeTests {
        [Test]
        public void ShouldPlay_TrueWhenFlagClear_FalseWhenSet() {
            Assert.IsTrue(DialogOpenWipe.ShouldPlay(DialogEntryFlags.None));
            Assert.IsTrue(DialogOpenWipe.ShouldPlay(DialogEntryFlags.CenterText | DialogEntryFlags.Legacy10));
            Assert.IsFalse(DialogOpenWipe.ShouldPlay(DialogEntryFlags.SkipOpenWipe));
            Assert.IsFalse(DialogOpenWipe.ShouldPlay(DialogEntryFlags.CenterText | DialogEntryFlags.SkipOpenWipe));
        }

        [Test]
        public void StepGeometry_AtZero_IsZeroSizeCentred_PanelPinnedFromCentre() {
            var full = new Rect(100f, 200f, 400f, 300f); // centre (300, 350)
            var (mask, offset) = DialogOpenWipe.StepGeometry(full, 0f);
            Assert.AreEqual(new Rect(300f, 350f, 0f, 0f), mask);
            // panel offset within the (zero-size, centred) mask pins it back to full's top-left
            Assert.AreEqual(new Vector2(-200f, -150f), offset);
        }

        [Test]
        public void StepGeometry_AtOne_IsFullRect_ZeroOffset() {
            var full = new Rect(100f, 200f, 400f, 300f);
            var (mask, offset) = DialogOpenWipe.StepGeometry(full, 1f);
            Assert.AreEqual(full, mask);
            Assert.AreEqual(Vector2.zero, offset);
        }

        [Test]
        public void StepGeometry_AtHalf_IsHalfSizeCentred() {
            var full = new Rect(100f, 200f, 400f, 300f); // centre (300, 350)
            var (mask, offset) = DialogOpenWipe.StepGeometry(full, 0.5f);
            Assert.AreEqual(new Rect(200f, 275f, 200f, 150f), mask); // centred, half size
            Assert.AreEqual(new Vector2(-100f, -75f), offset);       // full.xy - mask.xy
        }

        [Test]
        public void StepGeometry_ClampsTBelowZeroAndAboveOne() {
            var full = new Rect(0f, 0f, 100f, 100f);
            Assert.AreEqual(new Rect(50f, 50f, 0f, 0f), DialogOpenWipe.StepGeometry(full, -1f).mask);
            Assert.AreEqual(full, DialogOpenWipe.StepGeometry(full, 2f).mask);
        }

        [Test]
        public void StepGeometry_IsUnchanged_ForTheShippedPxRect() {
            // Faithfulness pin: row 2's shipped area. The wipe's maths must not move.
            // centre = (65 + 1470/2, 66 + 606/2) = (800, 369); at t=0.5 the mask is half-size
            // and CENTRED on that point, i.e. mask.xy = centre - halfMaskSize/2, not centre
            // itself (that would be the t=0 point, pinned separately by
            // StepGeometry_AtZero_IsZeroSizeCentred_PanelPinnedFromCentre above).
            var canonical = new Rect(65f, 66f, 1470f, 606f);
            (Rect mask, Vector2 offset) = DialogOpenWipe.StepGeometry(canonical, 0.5f);
            Assert.AreEqual(432.5f, mask.x, 0.001f);
            Assert.AreEqual(217.5f, mask.y, 0.001f);
            Assert.AreEqual(735f, mask.width, 0.001f);
            Assert.AreEqual(303f, mask.height, 0.001f);
            Assert.AreEqual(-367.5f, offset.x, 0.001f);
            Assert.AreEqual(-151.5f, offset.y, 0.001f);
        }
    }
}
