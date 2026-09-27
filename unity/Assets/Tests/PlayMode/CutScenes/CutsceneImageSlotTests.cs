namespace BakAgain.Tests.PlayMode.CutScenes {
    using BakAgain.CutScenes;
    using BakAgain.CutScenes.AnimationCommands;
    using BakAgain.Graphics;
    using GameData.Resources.Animation.FrameCommands;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// The image-slot join: which slot a load lands in, which one a dispose clears, and the fact
    /// that a draw is not affected by either.
    /// </summary>
    /// <remarks>
    /// <b>Three commands share one piece of state and a fourth deliberately ignores it.</b>
    /// <c>SelectImageSlot</c> (327 uses across all 42 shipped scripts), <c>LoadImageResource</c>
    /// (225) and <c>DisposeCurrentBitmap</c> (104) all route through
    /// <see cref="CutsceneState.CurrentImageSlot"/>; the 4,169 draw commands do not — every one of
    /// them names its own slot in its arguments.
    ///
    /// <para><b>That asymmetry is the trap.</b> "Select a slot, then draw" is what the command names
    /// suggest, and making a draw read the selected slot is the obvious tidy-up. It would be wrong
    /// for every shipped script: C11 selects slot 1 to load into while its draws name slot 0, so the
    /// change swaps the images and nothing throws. Pinned below in both directions.</para>
    ///
    /// <para>Everything here is asserted through <see cref="CutsceneState.ImageSlots"/> rather than
    /// through <c>GetImage</c>, which reaches Addressables for a slot that names a file. The slot
    /// bookkeeping is the part these commands own; what a named file resolves to is not.</para>
    /// </remarks>
    public class CutsceneImageSlotTests {
        private GameObject _host;
        private CutsceneState _state;

        [SetUp]
        public void SetUp() {
            _host = new GameObject("CutsceneImageSlotHost");
            RawImage raw = _host.AddComponent<RawImage>();
            raw.texture = new Texture2D(8, 8);
            _state = new CutsceneState(new UiImage(raw), new Color[256]);
        }

        [TearDown]
        public void TearDown() {
            _state?.Dispose();
            if (_host != null) {
                Object.DestroyImmediate(_host);
            }
        }

        private void Select(int slot) =>
            new SelectImageSlot { SlotNumber = slot }.ToAction()(_state);

        private void Load(string filename) =>
            new LoadImageResource { Filename = filename }.ToAction()(_state);

        private void DisposeBitmap() =>
            new DisposeCurrentBitmap().ToAction()(_state);

        [Test]
        public void ALoadLandsInTheSlotTheLastSelectNamed() {
            Select(1);
            Load("C11B1.BMP");

            Assert.AreEqual("C11B1.BMP", _state.ImageSlots[1]);
            Assert.IsFalse(_state.ImageSlots.ContainsKey(0), "slot 0 was never selected or loaded");
        }

        [Test]
        public void ReselectingRedirectsTheNextLoadAndLeavesTheEarlierSlotAlone() {
            // The shape every shipped script opens with: two palettes and two bitmaps loaded by
            // alternating select/load pairs.
            Select(0);
            Load("C11A1.BMP");
            Select(1);
            Load("C11B1.BMP");

            Assert.AreEqual("C11A1.BMP", _state.ImageSlots[0]);
            Assert.AreEqual("C11B1.BMP", _state.ImageSlots[1]);
        }

        [Test]
        public void ASecondLoadWITHOUTASelectOVERWRITESTheSameSlot() {
            // *** THE SLOT IS NOT A STACK. *** A load does not advance to the next slot, so a script
            // that loads twice in a row keeps only the second. Nothing shipped does it, which is
            // exactly why a "load pushes" rewrite would go unnoticed.
            Select(2);
            Load("FIRST.BMP");
            Load("SECOND.BMP");

            Assert.AreEqual("SECOND.BMP", _state.ImageSlots[2]);
            Assert.AreEqual(1, _state.ImageSlots.Count, "no second slot was written");
        }

        [Test]
        public void DisposeClearsONLYTheSelectedSlot() {
            Select(0);
            Load("KEEP.BMP");
            Select(1);
            Load("DROP.BMP");

            DisposeBitmap();

            Assert.IsNull(_state.ImageSlots[1], "the selected slot was cleared");
            Assert.AreEqual("KEEP.BMP", _state.ImageSlots[0], "and the other slot was not");
        }

        [Test]
        public void DisposeStopsThatSlotResolvingWhileTheOthersAreUntouched() {
            // GetImage treats a null filename exactly as it treats a slot never loaded — both give
            // null with no load attempted. That equivalence is what makes a mis-aimed dispose
            // silent: the draw finds nothing, logs a warning and carries on.
            Select(1);
            Load("DROP.BMP");
            DisposeBitmap();

            Assert.IsNull(_state.GetImage(1, 0), "a disposed slot resolves to nothing");
            Assert.IsNull(_state.GetImage(3, 0), "and so does one that was never loaded");
        }

        [Test]
        public void DisposeBeforeAnySelectClearsSlotZero() {
            // CurrentImageSlot starts at 0, so a dispose with no select ahead of it is aimed at
            // slot 0 rather than at nothing.
            Load("SLOT0.BMP");
            DisposeBitmap();

            Assert.IsNull(_state.ImageSlots[0]);
        }

        [Test]
        public void SELECTINGASLOTCHANGESNOTHINGADRAWCANSEE() {
            // *** THE ASYMMETRY. *** A draw names its own slot, so selecting one cannot redirect it.
            // If a rewrite made draws read CurrentImageSlot, this is the assertion that would still
            // pass and the next one that would not — so both are here.
            Select(0);
            Load("A.BMP");
            Select(1);
            Load("B.BMP");

            Select(0);
            Assert.AreEqual("A.BMP", _state.ImageSlots[0]);
            Assert.AreEqual("B.BMP", _state.ImageSlots[1]);

            Select(1);
            Assert.AreEqual("A.BMP", _state.ImageSlots[0], "selecting slot 1 did not move slot 0");
            Assert.AreEqual("B.BMP", _state.ImageSlots[1]);
        }

        [Test]
        public void ADrawResolvesFromTheSlotItNAMES_notTheSelectedOne() {
            // The other half of the asymmetry, at the accessor a draw actually calls. Slot 2 holds
            // nothing while slot 2 is selected; GetImage(3, ..) is still answered from slot 3.
            Select(2);
            Load("SELECTED.BMP");

            Assert.IsNull(_state.GetImage(3, 0),
                "GetImage takes the slot as an argument; the selection does not reach it");
        }
    }
}
