namespace BakAgain.Tests.PlayMode.CutScenes {
    using BakAgain.CutScenes.AnimationCommands;
    using BakAgain.CutScenes;
    using BakAgain.Graphics;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Which buffer a cutscene copy reads and which it writes.
    ///
    /// <para>The shipped scripts lean on this hard — 197 StoreScreen, 263 DrawAreaFromBuffer, 79
    /// StoreArea across the 42 TTM files — and none of it was covered. A swapped source and
    /// destination is the classic failure here and would show as the background eating the frame
    /// rather than the other way round.</para>
    /// </summary>
    public class CutsceneBufferRoutingTests {
        private const int FullPalette = 256;

        private GameObject _host;
        private CutsceneState _state;

        private static Color[] Palette() {
            var c = new Color[FullPalette];
            for (var i = 0; i < FullPalette; i++) {
                c[i] = new Color(i / 255f, 0f, 0f, 1f);
            }
            return c;
        }

        [SetUp]
        public void SetUp() {
            _host = new GameObject("CutsceneBufferHost");
            RawImage raw = _host.AddComponent<RawImage>();
            raw.texture = new Texture2D(64, 64);
            _state = new CutsceneState(new UiImage(raw), Palette());
        }

        [TearDown]
        public void TearDown() {
            _state?.Dispose();
            if (_host != null) {
                Object.DestroyImmediate(_host);
            }
        }

        private static void Fill(Texture target, Color colour) {
            var rt = (RenderTexture)target;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, colour);
            RenderTexture.active = previous;
        }

        private static Color Sample(Texture source) {
            var rt = (RenderTexture)source;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            probe.ReadPixels(new Rect(rt.width / 2, rt.height / 2, 1, 1), 0, 0);
            probe.Apply();
            RenderTexture.active = previous;
            Color c = probe.GetPixel(0, 0);
            Object.DestroyImmediate(probe);
            return c;
        }

        private const int DrawBuffer = 1;        // Buffer_B
        private const int BackgroundBuffer = 2;  // Buffer_C

        [Test]
        public void TheDrawBufferAndTheBackgroundBufferAreNotTheSameTexture() {
            // Everything below is meaningless if they alias, and a copy between them would look
            // like a success no matter which way round it ran.
            Assert.AreNotSame(_state.GetIndexedBuffer(DrawBuffer), _state.GetIndexedBuffer(BackgroundBuffer));
            Assert.AreNotSame(_state.GetDirectBuffer(DrawBuffer), _state.GetDirectBuffer(BackgroundBuffer));
        }

        [Test]
        public void TheDrawBufferIsBufferBAndTheBackgroundIsBufferC() {
            // CurrentDrawBufferIndex is assigned once, in Initialize, and nothing else writes it —
            // so it is a constant in practice, which is what makes the "buffer B to buffer C"
            // comments on StoreScreen and StoreArea accurate rather than merely usually-true.
            Assert.AreEqual(DrawBuffer, _state.CurrentDrawBufferIndex);
            Assert.AreEqual(BackgroundBuffer, _state.BackgroundBufferIndex);
        }

        [Test]
        public void CopyBufferMovesContentFromSourceToDestination_NotTheReverse() {
            Fill(_state.GetIndexedBuffer(DrawBuffer), Color.green);
            Fill(_state.GetIndexedBuffer(BackgroundBuffer), Color.red);

            _state.CopyBuffer(DrawBuffer, BackgroundBuffer);

            Assert.AreEqual(Color.green, Sample(_state.GetIndexedBuffer(BackgroundBuffer)),
                "the destination took the source's content");
            Assert.AreEqual(Color.green, Sample(_state.GetIndexedBuffer(DrawBuffer)),
                "and the source is unchanged");
        }

        [Test]
        public void CopyBufferCarriesTheDirectBufferToo_NotJustTheIndexedOne() {
            // Each slot is a PAIR — indexed and direct — and a copy that moved only one would leave
            // the two halves of a buffer describing different pictures.
            Fill(_state.GetDirectBuffer(DrawBuffer), Color.blue);
            Fill(_state.GetDirectBuffer(BackgroundBuffer), Color.red);

            _state.CopyBuffer(DrawBuffer, BackgroundBuffer);

            Assert.AreEqual(Color.blue, Sample(_state.GetDirectBuffer(BackgroundBuffer)));
        }

        [Test]
        public void StoreScreenPushesTheDrawnFrameIntoTheBackground() {
            // The direction that matters: the background is what gets restored at the start of the
            // next frame, so this is "keep what I just drew". Reversed, every StoreScreen would
            // wipe the frame with the old background instead.
            Fill(_state.GetIndexedBuffer(DrawBuffer), Color.green);
            Fill(_state.GetIndexedBuffer(BackgroundBuffer), Color.red);

            _state.CopyBuffer(_state.CurrentDrawBufferIndex, _state.BackgroundBufferIndex);

            Assert.AreEqual(Color.green, Sample(_state.GetIndexedBuffer(BackgroundBuffer)));
        }

        [Test]
        public void EveryOneOfTheFourBuffersIsDistinct() {
            // The shipped scripts use all four: CopyAreaBetweenBuffers moves 2->3, 3->2, 3->1 and
            // 1->0 across the 42 TTM files, so buffers A and X are not decoration.
            for (var a = 0; a < 4; a++) {
                for (int b = a + 1; b < 4; b++) {
                    Assert.AreNotSame(_state.GetIndexedBuffer(a), _state.GetIndexedBuffer(b),
                        $"buffers {a} and {b} alias");
                }
            }
        }

        [Test]
        public void ACopyIntoTheDrawBufferWorksToo_WhichTheScriptsDo() {
            // 3 -> 1 appears in the shipped data, so the draw buffer is a destination as well as a
            // source; nothing about it is write-only.
            Fill(_state.GetIndexedBuffer(3), Color.yellow);
            Fill(_state.GetIndexedBuffer(DrawBuffer), Color.red);

            _state.CopyBuffer(3, DrawBuffer);

            Assert.AreEqual(Color.yellow, Sample(_state.GetIndexedBuffer(DrawBuffer)));
        }
        [Test]
        public void ADrawAreaFromBufferLandsInTheDRAWBuffer_notInTheTargetBuffer() {
            // *** THE RESTORE HALF IGNORES SetTargetBuffer. *** CopyToTargetBuffer writes INTO the
            // target, so a script that stashed a region leaves the target index pointing at it;
            // DrawAreaFromBuffer's destination is the current draw buffer regardless, because
            // CopyArea(src) takes its destination from CurrentDrawBufferIndex. Making the restore
            // symmetric with the save — reading the target index at both ends — would copy the
            // buffer onto itself and the frame would simply never change.
            _state.TargetBufferIndex = 3;
            Fill(_state.GetIndexedBuffer(BackgroundBuffer), Color.green);
            Fill(_state.GetIndexedBuffer(DrawBuffer), Color.blue);
            Fill(_state.GetIndexedBuffer(3), Color.blue);

            new GameData.Resources.Animation.FrameCommands.DrawAreaFromBuffer {
                BufferNumber = BackgroundBuffer
            }.ToAction()(_state);

            Assert.AreEqual(Color.green, Sample(_state.GetIndexedBuffer(DrawBuffer)),
                "the destination is the draw buffer");
            Assert.AreEqual(Color.blue, Sample(_state.GetIndexedBuffer(3)),
                "and the target buffer was not written");
        }

    }
}
