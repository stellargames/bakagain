namespace BakAgain.Tests.PlayMode.CutScenes {
    using System.Collections.Generic;
    using BakAgain.CutScenes;
    using BakAgain.Graphics;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// The cutscene palette: which cycle windows survive, and what a rotation does.
    ///
    /// <para>These are the rules a rewrite would quietly break — the cycle list is REPLACED rather
    /// than appended to, a one-entry window is dropped, and a negative offset is normalised rather
    /// than reversed. None of them were covered.</para>
    /// </summary>
    public class CutsceneStatePaletteTests {
        private GameObject _host;
        private CutsceneState _state;

        // The palette texture is 256x1, so anything handed to CutsceneState must be a full
        // palette — a short one throws inside Texture2D.SetPixels. Only the pure RotateSlice tests
        // can use small ramps.
        private const int FullPalette = 256;

        private static Color[] Ramp(int n) {
            var c = new Color[n];
            for (var i = 0; i < n; i++) {
                // Distinct and order-revealing: index i is recoverable from r.
                c[i] = new Color(i / 255f, 0f, 0f, 1f);
            }
            return c;
        }

        private static int IndexOf(Color c) => Mathf.RoundToInt(c.r * 255f);

        [SetUp]
        public void SetUp() {
            _host = new GameObject("CutsceneStateHost");
            RawImage raw = _host.AddComponent<RawImage>();
            raw.texture = new Texture2D(8, 8);
            _state = new CutsceneState(new UiImage(raw), Ramp(FullPalette));
        }

        [TearDown]
        public void TearDown() {
            _state?.Dispose();
            if (_host != null) {
                Object.DestroyImmediate(_host);
            }
        }

        // --- RotateSlice: pure, and the piece every cycle rule rests on --------------------------

        [Test]
        public void RotateSlice_MovesTheWindowAndLeavesTheRestAlone() {
            Color[] p = Ramp(8);
            CutsceneState.RotateSlice(p, 2, 5, 1);

            // Outside [2..5] untouched.
            Assert.AreEqual(0, IndexOf(p[0]));
            Assert.AreEqual(1, IndexOf(p[1]));
            Assert.AreEqual(6, IndexOf(p[6]));
            Assert.AreEqual(7, IndexOf(p[7]));
            // Inside, rotated one step toward LOWER indices: 2,3,4,5 -> 3,4,5,2.
            Assert.AreEqual(3, IndexOf(p[2]));
            Assert.AreEqual(4, IndexOf(p[3]));
            Assert.AreEqual(5, IndexOf(p[4]));
            Assert.AreEqual(2, IndexOf(p[5]));
        }

        [Test]
        public void RotateSlice_ANegativeOffsetIsNormalisedNotReversed() {
            // palette_cycle_add does target = count + target, so -1 means "rotate by count-1" —
            // which lands one step the OTHER way. Reversing instead would look right on a symmetric
            // window and wrong everywhere else.
            Color[] byMinusOne = Ramp(8);
            Color[] byCountMinusOne = Ramp(8);
            CutsceneState.RotateSlice(byMinusOne, 2, 5, -1);
            CutsceneState.RotateSlice(byCountMinusOne, 2, 5, 3);   // count = 4

            CollectionAssert.AreEqual(byCountMinusOne, byMinusOne);
            Assert.AreEqual(5, IndexOf(byMinusOne[2]), "one step toward higher indices");
        }

        [Test]
        public void RotateSlice_AWholeTurnIsANoOp() {
            Color[] p = Ramp(8);
            CutsceneState.RotateSlice(p, 2, 5, 4);   // count = 4
            CollectionAssert.AreEqual(Ramp(8), p);
        }

        [Test]
        public void RotateSlice_AWindowNarrowerThanTwoDoesNothing() {
            Color[] p = Ramp(8);
            CutsceneState.RotateSlice(p, 3, 3, 1);
            CollectionAssert.AreEqual(Ramp(8), p);
        }

        [Test]
        public void RotateSlice_ClampsRatherThanThrowingOnAnOutOfRangeWindow() {
            Color[] p = Ramp(4);
            Assert.DoesNotThrow(() => CutsceneState.RotateSlice(p, -5, 99, 1));
            // Clamped to the whole array, so every entry moved.
            Assert.AreEqual(1, IndexOf(p[0]));
        }

        [Test]
        public void RotateSlice_ToleratesANullPalette() {
            Assert.DoesNotThrow(() => CutsceneState.RotateSlice(null, 0, 3, 1));
        }

        // --- the cycle list -----------------------------------------------------------------------

        private static CutsceneState.PaletteCycle Cycle(int start, int end, int direction) =>
            new CutsceneState.PaletteCycle { Start = start, End = end, Direction = direction };

        [Test]
        public void WithNoCyclesAdvanceDoesNothingAndSaysSo() {
            Assert.IsFalse(_state.HasActivePaletteCycles);
            Assert.IsFalse(_state.AdvancePaletteCycles());
        }

        [Test]
        public void AWindowNarrowerThanTwoEntriesIsDropped() {
            // palette_cycle_add rejects count <= 1, so a single-entry window never becomes a cycle.
            _state.SetPaletteCycles(new List<CutsceneState.PaletteCycle> { Cycle(3, 3, 1) });
            Assert.IsFalse(_state.HasActivePaletteCycles);
        }

        [Test]
        public void AWindowWithNoDirectionIsDropped() {
            _state.SetPaletteCycles(new List<CutsceneState.PaletteCycle> { Cycle(2, 6, 0) });
            Assert.IsFalse(_state.HasActivePaletteCycles);
        }

        [Test]
        public void SettingCyclesREPLACESTheListRatherThanAppending() {
            // *** Faithful, and easy to get wrong. *** The original's 0x2402 calls
            // palette_cycle_add(-1, 0, 0) first, which clears the list, before adding its bands. An
            // appending implementation would accumulate windows across every command.
            _state.SetPaletteCycles(new List<CutsceneState.PaletteCycle> { Cycle(2, 6, 1) });
            Assert.IsTrue(_state.HasActivePaletteCycles);

            _state.SetPaletteCycles(new List<CutsceneState.PaletteCycle> { Cycle(3, 3, 1) });
            Assert.IsFalse(_state.HasActivePaletteCycles,
                "the replacement dropped the only valid window, so nothing should remain");
        }

        [Test]
        public void SettingCyclesToNullClearsThem() {
            _state.SetPaletteCycles(new List<CutsceneState.PaletteCycle> { Cycle(2, 6, 1) });
            _state.SetPaletteCycles(null);
            Assert.IsFalse(_state.HasActivePaletteCycles);
            Assert.IsFalse(_state.AdvancePaletteCycles());
        }

        [Test]
        public void AdvancingDoesNotMutateTheSlotsOwnPaletteArray() {
            // *** The reason the snapshot exists. *** PaletteSlots may hold a shared or cached
            // resource array; rotating it in place would corrupt the palette for everything else
            // holding the same reference.
            Color[] slotPalette = Ramp(FullPalette);
            _state.SetPalette(slotPalette);
            _state.SetPaletteCycles(new List<CutsceneState.PaletteCycle> { Cycle(0, 7, 1) });

            Assert.IsTrue(_state.AdvancePaletteCycles());

            CollectionAssert.AreEqual(Ramp(FullPalette), slotPalette,
                "the array handed to SetPalette must be untouched");
        }

        [Test]
        public void LoadingAPaletteWhileACycleRunsRebasesTheCycleOnIt() {
            // *** THE ONE THAT MADE EVERY GOODS STORE UNREADABLE. *** SHOP3's init script arms its
            // cycle BEFORE it loads SHOP3.PAL, so the snapshot the rotation works on was the palette
            // the location came FROM. Without a re-base the next tick uploads that snapshot over the
            // freshly-loaded palette: the load lands and is undone a frame later, and Romney's Port
            // Exchange drew its shelves in exactly the right places painted in the town's outdoor
            // palette (TASK-427).
            //
            // The original has no snapshot to go stale: palette_cycle_tick (PALDRV.C:159) rotates
            // g_graphics_context.pPaletteScratchBuf in place, and loading a palette writes that same
            // buffer.
            _state.SetPalette(Ramp(FullPalette));
            _state.SetPaletteCycles(new List<CutsceneState.PaletteCycle> { Cycle(0, 7, 1) });

            // A second palette, distinguishable from the ramp at every index.
            var loaded = new Color[FullPalette];
            for (var i = 0; i < FullPalette; i++) {
                loaded[i] = new Color(0f, i / 255f, 0f, 1f);
            }
            _state.SetPalette(loaded);
            Assert.IsTrue(_state.AdvancePaletteCycles());

            // Read what the shader is sampling, not what a slot holds.
            Color[] uploaded = _state.PaletteTexture.GetPixels();
            Assert.That(uploaded[200].g, Is.EqualTo(200 / 255f).Within(0.01f),
                "outside the cycle window the uploaded palette must be the one just loaded");
            Assert.That(uploaded[200].r, Is.EqualTo(0f).Within(0.01f),
                "and not the palette that was current when the cycle was armed");
            // Inside the window it is still the NEW palette, rotated: 0..7 -> 1..7,0.
            Assert.That(uploaded[0].g, Is.EqualTo(1 / 255f).Within(0.01f),
                "the window rotates the new palette, it does not fall back to the old one");
        }

        [Test]
        public void ReSelectingTheCurrentPaletteSlotIsANoOp() {
            // The setter returns early on an unchanged value, so it does not re-upload. Worth
            // pinning: a rewrite that dropped the guard would push the palette texture every frame
            // a script re-selects the slot it is already on, which scripts do.
            _state.PaletteSlots[4] = Ramp(FullPalette);
            _state.CurrentPaletteSlot = 4;
            Assert.AreEqual(4, _state.CurrentPaletteSlot);

            Assert.DoesNotThrow(() => _state.CurrentPaletteSlot = 4);
            Assert.AreEqual(4, _state.CurrentPaletteSlot);
        }

        [Test]
        public void SelectingASlotThatHoldsNoPaletteStillMovesTheSelection() {
            // TryGetValue fails and the upload is skipped, but the slot number still changes — so a
            // later SetPalette lands in the slot the script asked for.
            _state.CurrentPaletteSlot = 5;
            Assert.AreEqual(5, _state.CurrentPaletteSlot);

            Color[] p = Ramp(FullPalette);
            _state.SetPalette(p);
            Assert.AreSame(p, _state.PaletteSlots[5]);
        }
    }
}
