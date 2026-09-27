namespace BakAgain.Tests.Editor.CutScenes {
    using BakAgain.CutScenes;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// VGA palette colour-cycling — <c>palette_cycle_tick</c> (SRC/GFX/DRIVER/PALDRV.C) driven by
    /// TTM opcode 0x2402. The direction is the thing: rotating the wrong way makes water, fire and
    /// torchlight run backwards, which looks plausible enough to ship unnoticed.
    /// </summary>
    public class PaletteCycleTests {
        // Colours whose red channel is the index, so a rotation is readable as a sequence.
        private static Color[] Ramp(int length) {
            var palette = new Color[length];

            for (var i = 0; i < length; i++) {
                palette[i] = new Color(i, 0, 0);
            }

            return palette;
        }

        private static int[] Reds(Color[] palette) {
            var reds = new int[palette.Length];

            for (var i = 0; i < palette.Length; i++) {
                reds[i] = (int)palette[i].r;
            }

            return reds;
        }

        [Test]
        public void APositiveOffsetMovesColoursTowardLowerIndices() {
            // The original's tick reads the saved copy at start+target and writes it to start.
            Color[] palette = Ramp(5);

            CutsceneState.RotateSlice(palette, 0, 4, 1);

            Assert.AreEqual(new[] { 1, 2, 3, 4, 0 }, Reds(palette));
        }

        [Test]
        public void ANegativeOffsetComesOutAsOneStepTheOtherWay() {
            // palette_cycle_add normalises it (target = count + target) rather than reversing.
            Color[] palette = Ramp(5);

            CutsceneState.RotateSlice(palette, 0, 4, -1);

            Assert.AreEqual(new[] { 4, 0, 1, 2, 3 }, Reds(palette));
        }

        [Test]
        public void MinusOneIsExactlyTheSameAsCountMinusOne() {
            // Which is what "normalised, not reversed" means arithmetically.
            Color[] normalised = Ramp(5);
            Color[] negative = Ramp(5);

            CutsceneState.RotateSlice(normalised, 0, 4, 4);
            CutsceneState.RotateSlice(negative, 0, 4, -1);

            Assert.AreEqual(Reds(normalised), Reds(negative));
        }

        [Test]
        public void OnlyTheWindowMoves() {
            Color[] palette = Ramp(8);

            CutsceneState.RotateSlice(palette, 2, 5, 1);

            Assert.AreEqual(new[] { 0, 1, 3, 4, 5, 2, 6, 7 }, Reds(palette));
        }

        [Test]
        public void TheWindowIsInclusiveAtBothEnds() {
            // count = end - start + 1, from ttmscript_pal_cycle_band_add.
            Color[] palette = Ramp(4);

            CutsceneState.RotateSlice(palette, 1, 2, 1);

            Assert.AreEqual(new[] { 0, 2, 1, 3 }, Reds(palette));
        }

        [Test]
        public void AFullTurnLeavesThePaletteWhereItStarted() {
            Color[] palette = Ramp(6);

            for (var step = 0; step < 6; step++) {
                CutsceneState.RotateSlice(palette, 0, 5, 1);
            }

            Assert.AreEqual(Reds(Ramp(6)), Reds(palette));
        }

        [Test]
        public void AWindowOfOneEntryOrNoneIsLeftAlone() {
            // palette_cycle_add rejects count <= 1.
            Color[] palette = Ramp(4);

            CutsceneState.RotateSlice(palette, 2, 2, 1);
            CutsceneState.RotateSlice(palette, 3, 1, 1);

            Assert.AreEqual(Reds(Ramp(4)), Reds(palette));
        }

        [Test]
        public void AnOffsetOfZeroDoesNothing() {
            Color[] palette = Ramp(4);

            CutsceneState.RotateSlice(palette, 0, 3, 0);

            Assert.AreEqual(Reds(Ramp(4)), Reds(palette));
        }

        [Test]
        public void OutOfRangeBoundsAreClampedRatherThanThrowing() {
            Color[] palette = Ramp(4);

            Assert.DoesNotThrow(() => CutsceneState.RotateSlice(palette, -5, 99, 1));
            Assert.AreEqual(new[] { 1, 2, 3, 0 }, Reds(palette));
        }

        [Test]
        public void ANullPaletteIsNotAnError() {
            Assert.DoesNotThrow(() => CutsceneState.RotateSlice(null, 0, 3, 1));
        }
    }
}
