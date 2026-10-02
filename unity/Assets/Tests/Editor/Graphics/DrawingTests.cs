namespace BakAgain.Tests.Editor.Graphics {
    using BakAgain.CutScenes;
    using BakAgain.CutScenes.AnimationCommands;
    using BakAgain.CutScenes.Extensions;
    using BakAgain.Graphics;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UI;

    public class DrawingTests {
        private CutsceneState _cutsceneState;
        private RenderTexture _indexedBuffer;
        private RenderTexture _directBuffer;
        private RawImage _rawImage;
        private UiImage _uiImage;

        [SetUp]
        public void Setup() {
            // Create UI components
            var gameObject = new GameObject("TestImage");
            _rawImage = gameObject.AddComponent<RawImage>();
            _rawImage.texture = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
            _uiImage = new UiImage(_rawImage);

            // Create default palette (256 colors)
            var defaultPalette = new Color[256];
            for (int i = 0; i < 256; i++) {
                float grey = i / (float)byte.MaxValue;
                defaultPalette[i] = new Color(grey, grey, grey, 255);
            }

            // Setup cutscene state
            _cutsceneState = new CutsceneState(_uiImage, defaultPalette);

            // Store references to the current buffers for testing
            _indexedBuffer = (RenderTexture)_cutsceneState.GetIndexedBuffer(_cutsceneState.CurrentDrawBufferIndex);
            _directBuffer = (RenderTexture)_cutsceneState.GetDirectBuffer(_cutsceneState.CurrentDrawBufferIndex);
        }

        [TearDown]
        public void TearDown() {
            // Clean up resources
            Object.DestroyImmediate(_indexedBuffer);
            Object.DestroyImmediate(_directBuffer);
            if (_rawImage != null) {
                Object.DestroyImmediate(_rawImage.gameObject);
            }
            _cutsceneState.Dispose();
        }

        [Test]
        public void DrawBorder_DoesNotThrowAndLeavesNoActiveRenderTexture() {
            // Smoke test for the GL/RenderTexture plumbing. The border geometry itself
            // (scaling from original 320x200 coords to buffer resolution) is verified
            // deterministically in DrawingUtilsTests.GetBorderRects_*; exact pixel
            // readback here is platform-Y-flip sensitive and not asserted.
            var area = new Area(14, 10, 291, 103);

            Assert.DoesNotThrow(() => Drawing.DrawBorder(area, 250, _cutsceneState));
            Assert.IsNull(RenderTexture.active, "DrawBorder must reset RenderTexture.active");
        }

        /// <summary>
        /// One pixel out of a render target.
        /// </summary>
        /// <remarks>
        /// The existing tests here assert only that drawing does not throw and leaves no render
        /// texture bound, which passes whatever the pixels say. Reading them back is what turns
        /// these into tests of the DRAWING rather than of the plumbing.
        /// </remarks>
        private static Color ReadPixel(RenderTexture target, int x, int y) {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var probe = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            probe.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
            probe.Apply();
            Color pixel = probe.GetPixel(0, 0);
            RenderTexture.active = previous;
            Object.DestroyImmediate(probe);
            return pixel;
        }

        private static Area SourceArea() => new Area(10, 10, 20, 20);

        /// <summary>
        /// A point comfortably inside the area once it has been scaled — in the BUFFER's coordinates.
        /// </summary>
        /// <remarks>
        /// <b>The y is inverted, and reading the un-inverted one finds nothing.</b> CopyToArea places
        /// the block at <c>area.InverseY(destination.height)</c>, and ReadPixels is bottom-left too,
        /// so both ends agree — but the AREA is top-left, so a test that reads at the area's own y
        /// samples empty buffer and reports 0 for everything. Computed here rather than by calling
        /// InverseY, so these tests do not depend on the very helper they would be sampling through.
        /// </remarks>
        private (int X, int Y) InsideScaled() {
            Area scaled = SourceArea().Scale<Area>(_cutsceneState.ScaleFromOriginal);
            int bottomUpY = _directBuffer.height - scaled.Y - scaled.Height;
            return (scaled.X + scaled.Width / 2, bottomUpY + scaled.Height / 2);
        }

        [Test]
        public void FillArea_Direct_WritesThePALETTEColourIntoTheDirectBuffer() {
            const int index = 200;
            Drawing.FillArea(SourceArea(), index, _cutsceneState, isIndexed: false);

            (int x, int y) = InsideScaled();
            Color pixel = ReadPixel(_directBuffer, x, y);

            Assert.AreEqual(_cutsceneState.CurrentPalette[index].r, pixel.r, 0.01f,
                "direct mode resolves the index through the palette");
            Assert.AreEqual(1f, pixel.a, 0.01f, "and it is opaque");
        }

        [Test]
        public void FillArea_Indexed_WritesTheINDEXItselfIntoTheRedChannel() {
            // Indexed mode stores index/255 in red and resolves it in a shader later, so the buffer
            // holds the INDEX, not a colour. Chosen so the index and the palette colour differ:
            // a greyscale palette makes index 200 -> 0.784 in both, which would not discriminate.
            const int index = 200;
            Drawing.FillArea(SourceArea(), index, _cutsceneState, isIndexed: true);

            (int x, int y) = InsideScaled();
            Color pixel = ReadPixel(_indexedBuffer, x, y);

            Assert.AreEqual(index / 255f, pixel.r, 0.01f);
        }

        [Test]
        public void FillArea_Indexed_ALSOClearsTheDirectBufferUnderneath() {
            // *** The half of the split that is easy to miss. *** An indexed fill writes TRANSPARENT
            // over the same area of the direct buffer. Without it, anything drawn directly there
            // earlier keeps showing through beneath the indexed pixels — which reads as a stray
            // sprite rather than as a fill that did too little.
            const int directIndex = 200;
            Drawing.FillArea(SourceArea(), directIndex, _cutsceneState, isIndexed: false);

            (int x, int y) = InsideScaled();
            Assert.AreEqual(1f, ReadPixel(_directBuffer, x, y).a, 0.01f, "the direct fill landed");

            Drawing.FillArea(SourceArea(), 5, _cutsceneState, isIndexed: true);

            Assert.AreEqual(0f, ReadPixel(_directBuffer, x, y).a, 0.01f,
                "and the indexed fill cleared it");
        }

        [Test]
        public void FillArea_Direct_LeavesTheIndexedBufferAlone() {
            // The mirror of the case above: direct mode touches ONE buffer. If it wrote both, an
            // indexed shader pass would find a stale index wherever anything was drawn directly.
            Color before = ReadPixel(_indexedBuffer, InsideScaled().X, InsideScaled().Y);

            Drawing.FillArea(SourceArea(), 200, _cutsceneState, isIndexed: false);

            Color after = ReadPixel(_indexedBuffer, InsideScaled().X, InsideScaled().Y);
            Assert.AreEqual(before.r, after.r, 0.01f);
            Assert.AreEqual(before.a, after.a, 0.01f);
        }

        private static Texture2D SolidTexture(int w, int h, Color color) {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true) {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i] = color;
            }
            t.SetPixels(pixels);
            t.Apply();
            return t;
        }

        [Test]
        public void CopyToArea_PutsTheWholeSourceAtTheAREAsPosition() {
            // CopyToArea takes the source from (0,0) and places it AT the area — the area describes
            // the DESTINATION, not a region of the source. CopyArea is the one that means a region.
            var area = new Area(40, 30, 16, 12);
            Texture2D source = SolidTexture(area.Width, area.Height, Color.red);

            Drawing.CopyToArea(source, _directBuffer, area);

            int bottomUpY = _directBuffer.height - area.Y - area.Height;
            Color inside = ReadPixel(_directBuffer, area.X + area.Width / 2, bottomUpY + area.Height / 2);
            Color outside = ReadPixel(_directBuffer, area.X - 4, bottomUpY + area.Height / 2);

            Assert.AreEqual(1f, inside.r, 0.01f, "the block landed at the area");
            Assert.AreEqual(0f, outside.r, 0.01f, "and did not spill to its left");
            Object.DestroyImmediate(source);
        }

        [Test]
        public void CopyArea_CopiesAREGIONAtTheSAMEPositionInBothTextures() {
            // Unlike CopyToArea, both the read and the write use area.X / InverseY — it moves a
            // region between buffers without moving it on screen. Confusing the two puts the block
            // at the origin instead of where it belongs.
            // *** FillArea SCALES its area; CopyArea does NOT. *** So the region to copy is the
            // SCALED one — using the source coordinates here reads empty buffer, which is how this
            // test failed first. The two take their areas in different spaces, which is worth
            // knowing before mixing them.
            var source = new Area(60, 50, 20, 16);
            Area area = source.Scale<Area>(_cutsceneState.ScaleFromOriginal);
            int bottomUpY = _directBuffer.height - area.Y - area.Height;

            Drawing.FillArea(source, 200, _cutsceneState, isIndexed: false);
            Assert.AreEqual(1f, ReadPixel(_directBuffer, area.X + 4, bottomUpY + 4).a, 0.01f,
                "the source region has something in it");

            Drawing.CopyArea(_directBuffer, _indexedBuffer, area);

            Assert.AreEqual(1f, ReadPixel(_indexedBuffer, area.X + 4, bottomUpY + 4).a, 0.01f,
                "and it arrived at the SAME coordinates");
        }

        /// <summary>
        /// Whether any pixel in a bottom-up rect satisfies <paramref name="match"/>.
        /// </summary>
        /// <remarks>
        /// A border edge is one or two pixels thick at this fixture's scale, so naming an exact
        /// pixel is fragile in a way that has nothing to do with what is being tested. Scanning a
        /// band answers "did the edge get drawn" without depending on the rounding.
        /// </remarks>
        private static bool AnyPixel(RenderTexture target, int x, int y, int w, int h,
            System.Func<Color, bool> match) {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var probe = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            probe.ReadPixels(new Rect(x, y, w, h), 0, 0);
            probe.Apply();
            Color[] pixels = probe.GetPixels();
            RenderTexture.active = previous;
            Object.DestroyImmediate(probe);
            foreach (Color c in pixels) {
                if (match(c)) {
                    return true;
                }
            }
            return false;
        }

        [Test]
        public void DrawBorder_PutsTheINDEXOnTheEdgesAndLeavesTheInteriorAlone() {
            // *** The assertion that separates a border from a fill. *** Both write the same index
            // into the same buffer; only the interior tells them apart, and "does not throw" cannot.
            var source = new Area(40, 30, 60, 40);
            Area scaled = source.Scale<Area>(_cutsceneState.ScaleFromOriginal);
            int bottomUpY = _indexedBuffer.height - scaled.Y - scaled.Height;
            const int index = 200;

            Drawing.DrawBorder(source, index, _cutsceneState);

            bool onTheEdge = AnyPixel(_indexedBuffer, scaled.X, bottomUpY, scaled.Width, 3,
                c => Mathf.Abs(c.r - index / 255f) < 0.01f);
            Assert.IsTrue(onTheEdge, "an edge carries the index");

            Color middle = ReadPixel(_indexedBuffer,
                scaled.X + scaled.Width / 2, bottomUpY + scaled.Height / 2);
            Assert.AreEqual(0f, middle.r, 0.01f, "and the interior is untouched — this is a border");
        }

        [Test]
        public void DrawBorder_PunchesTheHoleOnlyWhereTheBorderIs() {
            // *** THE TEST THAT FOUND TASK-204, back with the fix. *** The direct buffer gets a hole
            // over the edges so the indexed border survives compositing — the same job FillArea's
            // indexed path does with a raw copy. It failed while the hole was drawn through
            // Unlit/Transparent, which BLENDS by source alpha and so cannot clear anything.
            //
            // The interior half matters just as much: a hole that covered it would erase whatever
            // the border is drawn around.
            var source = new Area(40, 30, 60, 40);
            Area scaled = source.Scale<Area>(_cutsceneState.ScaleFromOriginal);
            int bottomUpY = _directBuffer.height - scaled.Y - scaled.Height;

            Drawing.FillArea(source, 200, _cutsceneState, isIndexed: false);
            Assert.AreEqual(1f,
                ReadPixel(_directBuffer, scaled.X + scaled.Width / 2, bottomUpY + scaled.Height / 2).a,
                0.01f, "the interior starts opaque");

            Drawing.DrawBorder(source, 200, _cutsceneState);

            bool holeOnEdge = AnyPixel(_directBuffer, scaled.X, bottomUpY, scaled.Width, 3,
                c => c.a < 0.01f);
            Assert.IsTrue(holeOnEdge, "the edge was punched through");
            Assert.AreEqual(1f,
                ReadPixel(_directBuffer, scaled.X + scaled.Width / 2, bottomUpY + scaled.Height / 2).a,
                0.01f, "and the interior survived");
        }

        [Test]
        public void CutsceneStateCopyArea_CopiesBOTHBuffers() {
            // *** The assertion that matters. *** CutsceneState.CopyArea copies the indexed AND the
            // direct buffer; an implementation that did only one would look right for whichever kind
            // of content the scene happened to use and silently lose the other — a cutscene that is
            // correct until the frame that mixes them.
            var source = new Area(40, 30, 40, 30);
            Area scaled = source.Scale<Area>(_cutsceneState.ScaleFromOriginal);
            int bottomUpY = _directBuffer.height - scaled.Y - scaled.Height;
            int from = _cutsceneState.CurrentDrawBufferIndex;
            int to = from == 0 ? 1 : 0;

            // One fill of each kind, so both buffers have something to lose.
            Drawing.FillArea(source, 200, _cutsceneState, isIndexed: false);
            Drawing.FillArea(new Area(source.X, source.Y + 40, 40, 20), 30, _cutsceneState,
                isIndexed: true);

            _cutsceneState.CopyArea(from, to, new Area(0, 0, 320, 200));

            var toDirect = (RenderTexture)_cutsceneState.GetDirectBuffer(to);
            var toIndexed = (RenderTexture)_cutsceneState.GetIndexedBuffer(to);

            Assert.AreEqual(1f,
                ReadPixel(toDirect, scaled.X + scaled.Width / 2, bottomUpY + scaled.Height / 2).a,
                0.01f, "the DIRECT content arrived");

            Area indexedScaled = new Area(source.X, source.Y + 40, 40, 20)
                .Scale<Area>(_cutsceneState.ScaleFromOriginal);
            int indexedBottomUpY = toIndexed.height - indexedScaled.Y - indexedScaled.Height;
            Assert.AreEqual(30 / 255f,
                ReadPixel(toIndexed, indexedScaled.X + indexedScaled.Width / 2,
                    indexedBottomUpY + indexedScaled.Height / 2).r,
                0.01f, "and the INDEXED content did too");
        }

        [Test]
        public void CutsceneStateCopyArea_ScalesTheAreaUnlikeTheRawPrimitive() {
            // The command's area is in original 320x200 space; Drawing.CopyArea's is in buffer
            // space. Feeding one to the other unscaled copies the wrong rectangle — a whole class of
            // "the frame is nearly right" that this pins.
            var source = new Area(40, 30, 40, 30);
            Area scaled = source.Scale<Area>(_cutsceneState.ScaleFromOriginal);
            int from = _cutsceneState.CurrentDrawBufferIndex;
            int to = from == 0 ? 1 : 0;

            Drawing.FillArea(source, 200, _cutsceneState, isIndexed: false);
            _cutsceneState.CopyArea(from, to, source);

            var toDirect = (RenderTexture)_cutsceneState.GetDirectBuffer(to);
            int bottomUpY = toDirect.height - scaled.Y - scaled.Height;

            Assert.AreEqual(1f,
                ReadPixel(toDirect, scaled.X + scaled.Width / 2, bottomUpY + scaled.Height / 2).a,
                0.01f, "the copy covered the SCALED rectangle");
        }

        // *** Saved rects are the script's OWN slots, not screen pages. *** 0x1121 picks a slot in
        // pFreemem[12] (structs.h:1228), 0x4214 saves the rect into fresh memory there, 0xa601
        // pastes it back at its own position and 0xc0 frees it (TTM.C:282-288, 464-490). The port
        // aliased slot N to screen buffer N, so C61's slot 1 WAS the draw buffer: the save copied
        // it onto itself, the scene cleared it, and every later Makala/Pug shot restored black.

        private bool Covered(Area area) {
            Area scaled = area.Scale<Area>(_cutsceneState.ScaleFromOriginal);
            int bottomUpY = _directBuffer.height - scaled.Y - scaled.Height;
            return ReadPixel(_directBuffer, scaled.X + scaled.Width / 2, bottomUpY + scaled.Height / 2).a > 0.5f;
        }

        private void SaveInto(int slot, Area area) {
            new GameData.Resources.Animation.FrameCommands.SetTargetBuffer { BufferNumber = slot }
                .ToAction()(_cutsceneState);
            new GameData.Resources.Animation.FrameCommands.CopyToTargetBuffer {
                X = area.X, Y = area.Y, Width = area.Width, Height = area.Height
            }.ToAction()(_cutsceneState);
        }

        private void Restore(int slot) =>
            new GameData.Resources.Animation.FrameCommands.DrawAreaFromBuffer { BufferNumber = slot }
                .ToAction()(_cutsceneState);

        [Test]
        public void ASavedRectComesBackAfterTheDrawBufferIsCleared_EvenFromSlotOne() {
            var area = new Area(40, 30, 60, 40);
            Drawing.FillArea(area, 200, _cutsceneState, isIndexed: false);
            SaveInto(_cutsceneState.CurrentDrawBufferIndex, area);   // C61 frame 31

            Drawing.FillArea(area, 30, _cutsceneState, isIndexed: true);   // clears the direct pixels
            Assert.IsFalse(Covered(area), "the draw buffer no longer has it");

            Restore(_cutsceneState.CurrentDrawBufferIndex);
            Assert.IsTrue(Covered(area), "and the slot put it back, in the same place");
        }

        [Test]
        public void ASavedRectSurvivesAStoreScreen() {
            // Slot 2 is not the background page: StoreScreen overwriting that page leaves it intact.
            var area = new Area(40, 30, 60, 40);
            Drawing.FillArea(area, 200, _cutsceneState, isIndexed: false);
            SaveInto(_cutsceneState.BackgroundBufferIndex, area);

            Drawing.FillArea(area, 30, _cutsceneState, isIndexed: true);
            new GameData.Resources.Animation.FrameCommands.StoreScreen().ToAction()(_cutsceneState);

            Restore(_cutsceneState.BackgroundBufferIndex);
            Assert.IsTrue(Covered(area));
        }

        [Test]
        public void RestoringAnEmptySlotDrawsNothing() {
            // `if (!*pfreemem) break;` (TTM.C:283-284).
            var area = new Area(40, 30, 60, 40);
            Drawing.FillArea(area, 200, _cutsceneState, isIndexed: false);

            Restore(3);

            Assert.IsTrue(Covered(area), "the frame is untouched");
        }

        [Test]
        public void DisposeTargetBuffer_EmptiesTheSlot() {
            var area = new Area(40, 30, 60, 40);
            Drawing.FillArea(area, 200, _cutsceneState, isIndexed: false);
            SaveInto(3, area);
            Assert.AreEqual(60, _cutsceneState.SavedRectArea(3).Width, "the slot keeps its rect");

            new GameData.Resources.Animation.FrameCommands.DisposeTargetBuffer().ToAction()(_cutsceneState);
            Drawing.FillArea(area, 30, _cutsceneState, isIndexed: true);
            Restore(3);

            Assert.IsNull(_cutsceneState.SavedRectArea(3));
            Assert.IsFalse(Covered(area), "a freed slot restores nothing");
        }

        [Test]
        public void TheStateCommandsSetExactlyWhatTheyName() {
            // One-liners, but each is a slot index that silently changes which buffer or palette
            // every LATER command touches — a wrong one is invisible until a frame or two on.
            new GameData.Resources.Animation.FrameCommands.SetTargetBuffer { BufferNumber = 3 }
                .ToAction()(_cutsceneState);
            Assert.AreEqual(3, _cutsceneState.TargetBufferIndex);

            new GameData.Resources.Animation.FrameCommands.SelectPaletteSlot { SlotNumber = 2 }
                .ToAction()(_cutsceneState);
            Assert.AreEqual(2, _cutsceneState.CurrentPaletteSlot);

            new GameData.Resources.Animation.FrameCommands.SetClipArea {
                X = 10, Y = 20, Width = 30, Height = 40
            }.ToAction()(_cutsceneState);
            Assert.AreEqual(new Rect(10, 20, 30, 40), _cutsceneState.ClipArea);
        }

        [Test]
        public void StoreArea_SavesIntoNoSlot_UnlikeItsNeighbour() {
            // *** FAITHFUL, not an oversight. *** 0x4214 saves a rect into a slot; 0x4204 is a
            // straight page-to-page blit that keeps nothing (TTM.C:658-661).
            new GameData.Resources.Animation.FrameCommands.StoreArea {
                X = 40, Y = 30, Width = 60, Height = 40
            }.ToAction()(_cutsceneState);

            for (int slot = 0; slot < CutsceneState.SavedRectSlotCount; slot++) {
                Assert.IsNull(_cutsceneState.SavedRectArea(slot));
            }
        }
    }
}
