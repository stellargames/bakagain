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

        [Test]
        public void CopyToTargetBuffer_RecordsItsAreaUnderTheTARGETIndex() {
            // The two commands are a PAIR: CopyToTargetBuffer stashes the area under the target
            // slot, and DrawAreaFromBuffer later reads Areas[source] to know what to copy back. If
            // it recorded under the CURRENT index instead, the pair would still work whenever the
            // two happened to be equal and fail silently when they were not.
            var command = new GameData.Resources.Animation.FrameCommands.CopyToTargetBuffer {
                X = 40, Y = 30, Width = 60, Height = 40
            };

            command.ToAction()(_cutsceneState);

            GameData.Resources.Animation.IArea recorded =
                _cutsceneState.Areas[_cutsceneState.TargetBufferIndex];
            Assert.IsNotNull(recorded);
            Assert.AreEqual(40, recorded.X);
            Assert.AreEqual(30, recorded.Y);
            Assert.AreEqual(60, recorded.Width);
            Assert.AreEqual(40, recorded.Height);
        }

        [Test]
        public void TheTwoCommandsRoundTripAnAreaThroughTheTargetBuffer() {
            // *** The pair, end to end. *** Stash the current buffer's content, overwrite it, then
            // draw it back — and it lands in the same place, because the AREA travelled with the
            // buffer. Reading Areas[destination] instead of Areas[source] in the single-argument
            // CopyArea would copy the wrong rectangle here.
            var area = new Area(40, 30, 60, 40);
            Area scaled = area.Scale<Area>(_cutsceneState.ScaleFromOriginal);
            int bottomUpY = _directBuffer.height - scaled.Y - scaled.Height;
            int probeX = scaled.X + scaled.Width / 2;
            int probeY = bottomUpY + scaled.Height / 2;

            Drawing.FillArea(area, 200, _cutsceneState, isIndexed: false);
            Assert.AreEqual(1f, ReadPixel(_directBuffer, probeX, probeY).a, 0.01f);

            new GameData.Resources.Animation.FrameCommands.CopyToTargetBuffer {
                X = area.X, Y = area.Y, Width = area.Width, Height = area.Height
            }.ToAction()(_cutsceneState);

            // An indexed fill clears the direct buffer underneath, so this wipes what we stashed.
            Drawing.FillArea(area, 30, _cutsceneState, isIndexed: true);
            Assert.AreEqual(0f, ReadPixel(_directBuffer, probeX, probeY).a, 0.01f,
                "the current buffer no longer has it");

            new GameData.Resources.Animation.FrameCommands.DrawAreaFromBuffer {
                BufferNumber = _cutsceneState.TargetBufferIndex
            }.ToAction()(_cutsceneState);

            Assert.AreEqual(1f, ReadPixel(_directBuffer, probeX, probeY).a, 0.01f,
                "and drawing from the target buffer put it back, in the same place");
        }

        [Test]
        public void EveryBuffersAreaStartsAsTheWholeCANONICALScreen() {
            // *** The unit convention, pinned. *** Areas[] are CANONICAL (1600x1200), not the
            // original 320x200 — the extractors scale on the way out, so a shipped TTM FillArea
            // reads X=70 Width=1455. Anything that reads these as VGA coordinates produces
            // arithmetic that looks broken while the code is right.
            for (int i = 0; i < _cutsceneState.Areas.Length; i++) {
                GameData.Resources.Animation.IArea a = _cutsceneState.Areas[i];
                Assert.AreEqual(0, a.X);
                Assert.AreEqual(0, a.Y);
                Assert.AreEqual(BakAgain.Graphics.Canonical.Width, a.Width);
                Assert.AreEqual(BakAgain.Graphics.Canonical.Height, a.Height);
            }
        }

        [Test]
        public void DisposeTargetBuffer_PutsTheTargetsAreaBackToTheWholeScreen() {
            // The other end of CopyToTargetBuffer's narrowing. Without the reset, a later
            // DrawAreaFromBuffer on that slot would keep copying the last stashed rectangle rather
            // than the buffer — stale geometry outliving the content it described.
            new GameData.Resources.Animation.FrameCommands.CopyToTargetBuffer {
                X = 40, Y = 30, Width = 60, Height = 40
            }.ToAction()(_cutsceneState);
            Assert.AreEqual(60, _cutsceneState.Areas[_cutsceneState.TargetBufferIndex].Width);

            new GameData.Resources.Animation.FrameCommands.DisposeTargetBuffer().ToAction()(_cutsceneState);

            Assert.AreEqual(BakAgain.Graphics.Canonical.Width,
                _cutsceneState.Areas[_cutsceneState.TargetBufferIndex].Width);
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
        public void StoreArea_RecordsNoArea_UnlikeItsNeighbour() {
            // *** FAITHFUL, not an oversight. *** The two neighbouring commands are different
            // operations in the original: 0x4214 saves a rect AND stores its coordinates under the
            // slot, while 0x4204 is a straight page-to-page blit that keeps nothing. A reader who
            // "fixes" this to record an area would be un-porting it, which is why it is pinned.
            GameData.Resources.Animation.IArea before =
                _cutsceneState.Areas[_cutsceneState.BackgroundBufferIndex];

            new GameData.Resources.Animation.FrameCommands.StoreArea {
                X = 40, Y = 30, Width = 60, Height = 40
            }.ToAction()(_cutsceneState);

            GameData.Resources.Animation.IArea after =
                _cutsceneState.Areas[_cutsceneState.BackgroundBufferIndex];
            Assert.AreEqual(before.Width, after.Width, "the background slot's area is untouched");
            Assert.AreEqual(BakAgain.Graphics.Canonical.Width, after.Width,
                "and still the whole screen");
        }
    }
}
