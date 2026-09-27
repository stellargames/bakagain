namespace BakAgain.Tests.PlayMode.CutScenes {
    using BakAgain.CutScenes;
    using BakAgain.CutScenes.AnimationCommands;
    using BakAgain.Graphics;
    using GameData.Resources.Animation.FrameCommands;
    using NUnit.Framework;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Which palette window a cycle actually cycles — the join between <c>SetRange1/2/3</c> and
    /// <c>StartPaletteCycle</c>, which nothing asserted.
    ///
    /// <para><b>The rotation primitive is well covered and this is not it.</b> <c>RotateSlice</c> has
    /// tests at two levels. What had none is the part that decides WHICH window it is handed: three
    /// commands store bounds in three slots, and a fourth picks slots by bitmask. Swap two and the
    /// shipped scripts still cycle a window, just the wrong one — water shimmering where the fire
    /// should, which no exception and no green "does not throw" would ever catch.</para>
    ///
    /// <para>Shipped usage: 18 SetRange1, 5 SetRange2, 3 SetRange3 and 32 StartPaletteCycle across
    /// the 42 TTM scripts, so this is a live join and not a theoretical one.</para>
    /// </summary>
    public class PaletteCycleRangeTests {
        private GameObject _host;
        private CutsceneState _state;

        private static readonly (int Start, int End) One = (10, 20);
        private static readonly (int Start, int End) Two = (30, 40);
        private static readonly (int Start, int End) Three = (50, 60);

        [SetUp]
        public void SetUp() {
            _host = new GameObject("PaletteCycleRangeHost");
            RawImage raw = _host.AddComponent<RawImage>();
            raw.texture = new Texture2D(64, 64);
            _state = new CutsceneState(new UiImage(raw), new Color[256]);

            new SetRange1 { Start = One.Start, End = One.End }.ToAction()(_state);
            new SetRange2 { Start = Two.Start, End = Two.End }.ToAction()(_state);
            new SetRange3 { Start = Three.Start, End = Three.End }.ToAction()(_state);
        }

        [TearDown]
        public void TearDown() {
            _state?.Dispose();
            if (_host != null) {
                UnityEngine.Object.DestroyImmediate(_host);
            }
        }

        private IReadOnlyList<CutsceneState.PaletteCycle> Cycle(Ranges range, int step) {
            new StartPaletteCycle { Range = range, Step = step }.ToAction()(_state);
            return _state.ActivePaletteCyclesForTest;
        }

        /// <summary>Three commands, three slots — a swap here is the whole bug this file guards.
        /// </summary>
        [Test]
        public void EachSetRangeCommandLandsInItsOwnSlot() {
            Assert.AreEqual(One, _state.Range1);
            Assert.AreEqual(Two, _state.Range2);
            Assert.AreEqual(Three, _state.Range3);
        }

        [Test]
        public void ACycleTakesItsBoundsFromTheRangeTheBitmaskNAMES() {
            IReadOnlyList<CutsceneState.PaletteCycle> cycles = Cycle(Ranges.Range2, 1);

            Assert.AreEqual(1, cycles.Count, "one bit was set, so one window cycles");
            Assert.AreEqual(Two.Start, cycles[0].Start, "Range2's window, not Range1's");
            Assert.AreEqual(Two.End, cycles[0].End);
        }

        [Test]
        public void TwoBitsCycleTwoWindows_AndSkipTheOneBetweenThem() {
            IReadOnlyList<CutsceneState.PaletteCycle> cycles =
                Cycle(Ranges.Range1 | Ranges.Range3, 1);

            Assert.AreEqual(2, cycles.Count);
            Assert.AreEqual(One.Start, cycles[0].Start);
            Assert.AreEqual(Three.Start, cycles[1].Start,
                "Range3 follows Range1; Range2 was not named and must not appear");
        }

        /// <summary>
        /// <b>Only the SIGN of Step is used</b> — the handler computes <c>si = p1 / abs(p1)</c>, so 7
        /// and 1 cycle identically and the magnitude is inert.
        /// </summary>
        [TestCase(1, 1)]
        [TestCase(7, 1)]
        [TestCase(-1, -1)]
        [TestCase(-5, -1)]
        public void OnlyTheSignOfStepSurvives(int step, int expectedDirection) {
            Assert.AreEqual(expectedDirection, Cycle(Ranges.Range1, step)[0].Direction);
        }

        /// <summary>
        /// <b>Step 0 means FORWARD, not "off".</b> The original forces a zero to 1 before taking the
        /// sign, so reading it as "no cycling" silently stops an animation the script asked for.
        /// </summary>
        [Test]
        public void AStepOfZeroCyclesFORWARD_RatherThanNotAtAll() {
            IReadOnlyList<CutsceneState.PaletteCycle> cycles = Cycle(Ranges.Range1, 0);

            Assert.AreEqual(1, cycles.Count, "zero is a step, not an instruction to stop");
            Assert.AreEqual(1, cycles[0].Direction);
        }

        /// <summary>A second command REPLACES the running cycles — the handler resets the list with
        /// <c>palette_cycle_add(-1, 0, 0)</c> before adding. Accumulating instead would leave the
        /// first window cycling for the rest of the scene.</summary>
        [Test]
        public void AStartREPLACESTheCyclesAlreadyRunning() {
            Cycle(Ranges.Range1 | Ranges.Range2, 1);

            IReadOnlyList<CutsceneState.PaletteCycle> cycles = Cycle(Ranges.Range3, 1);

            Assert.AreEqual(1, cycles.Count, "the previous two must be gone, not appended to");
            Assert.AreEqual(Three.Start, cycles[0].Start);
        }
        // ------------------------------------------------- ResetPalette / DisposeCurrentPalette

        [Test]
        public void ResetPaletteSTOPSAnyRunningCycle() {
            // TTM 0x0400. The command's whole reason to exist is undoing the two things that write
            // the DAC out from under the loaded palette — a fade, and a cycle. A port that only
            // re-uploaded the palette would leave the cycle rotating and the picture would keep
            // shimmering after the script asked it to stop.
            Assert.AreEqual(2, Cycle(Ranges.Range1 | Ranges.Range3, 1).Count, "cycles are running");

            new ResetPalette().ToAction()(_state);

            Assert.IsEmpty(_state.ActivePaletteCyclesForTest, "the cycle must be cancelled");
        }

        [Test]
        public void ResetPaletteKeepsTheCURRENTPaletteRatherThanRestoringADefault() {
            // *** The name invites the wrong reading. *** The original re-applies
            // anim_pCurrentPalette — the palette the SCENE loaded, which is usually not the default.
            // "Reset" meaning "go back to the default" would recolour every scene that uses it.
            var loaded = new Color[256];
            for (var i = 0; i < loaded.Length; i++) {
                loaded[i] = new Color(i / 255f, 0f, 0f, 1f);
            }
            _state.SetPalette(loaded);

            new ResetPalette().ToAction()(_state);

            Assert.AreSame(loaded, _state.CurrentPalette,
                "the scene's own palette must survive a reset");
        }

        [Test]
        public void DisposeCurrentPaletteLeavesItNULL_NotAnEmptyArray() {
            // Both fade handlers guard with `palette == null || palette.Length == 0`, so either
            // shape survives them — but everything else reads the slot directly. Pinning NULL keeps
            // the two readings from drifting: a port that disposed to Array.Empty<Color>() would
            // pass the fades and hand a zero-length palette to every other command.
            new DisposeCurrentPalette().ToAction()(_state);

            Assert.IsNull(_state.CurrentPalette);
        }

        [Test]
        public void ResetAfterDisposeDoesNotThrow() {
            // A real sequence in the shipped scripts: dispose the palette at the end of a scene,
            // then reset on the way into the next one. Reset re-applies whatever is in the slot, so
            // after a dispose it re-applies null and must tolerate it.
            new DisposeCurrentPalette().ToAction()(_state);

            Assert.DoesNotThrow(() => new ResetPalette().ToAction()(_state));
            Assert.IsNull(_state.CurrentPalette);
        }

    }
}
