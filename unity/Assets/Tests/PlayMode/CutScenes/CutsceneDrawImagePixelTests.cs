namespace BakAgain.Tests.PlayMode.CutScenes {
    using BakAgain.CutScenes;
    using BakAgain.CutScenes.AnimationCommands;
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement.Models;
    using GameData.Resources.Animation.FrameCommands;
    using GameData.Resources.Image;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// That a <c>DrawImage</c> actually puts its pixels in the buffer.
    /// </summary>
    /// <remarks>
    /// <b>This is the one drawing primitive nothing looked at.</b> Fills, borders and buffer copies
    /// are pixel-asserted elsewhere (<c>CutscenePenSelectionTests</c>,
    /// <c>CutsceneBufferRoutingTests</c>); rect arithmetic is covered by <c>DrawingClipTests</c>; and
    /// <c>CutscenePreloadWalkTests</c> walks <c>DrawImage</c> commands without ever rendering one.
    /// So "the command ran" was checked and "the image arrived" was not — which is exactly the shape
    /// of TASK-163, where scenes drew blank and every other test stayed green.
    ///
    /// <para><b>No shipped data and no Addressables.</b> <c>CutsceneState.GetImage</c> consults its
    /// own <c>Resources</c> set before it loads anything, so a synthetic image registered under the
    /// slot's key is returned by the real lookup. The image is INDEXED, like the shipped ones, which
    /// is what lets the assertion name a palette index rather than a colour.</para>
    ///
    /// <para>The palette makes each index recoverable from the red channel, the same trick the pen
    /// tests use, so a sampled pixel says which index was drawn there.</para>
    /// </remarks>
    public class CutsceneDrawImagePixelTests {
        private const int FullPalette = 256;

        /// <summary>The index the synthetic image is filled with — arbitrary, but not 0.</summary>
        /// <remarks>
        /// Zero would be indistinguishable from an empty buffer, which is the very failure this
        /// test exists to catch.
        /// </remarks>
        private const int ImagePen = 200;

        private const int ImageSize = 16;
        private const int Slot = 1;
        private const string SlotFile = "SYNTHETIC.BMX";

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
            _host = new GameObject("CutsceneDrawImageHost");
            RawImage raw = _host.AddComponent<RawImage>();
            raw.texture = new Texture2D(64, 64);
            _state = new CutsceneState(new UiImage(raw), Palette());

            var image = new BmImage("synthetic") {
                Width = ImageSize,
                Height = ImageSize,
                // *** SCALE IS THE IMAGE'S SIZE AS A FRACTION OF THE CANONICAL SCREEN, NOT A
                // MULTIPLIER. *** Shipped images bear this out: FIGS#2 is 160x138 with ScaleX 0.1
                // and ScaleY 0.115, which is 160/1600 and 138/1200. Passing 1 here made a 16x16
                // image fill the entire buffer, and the "leaves the rest alone" assertion below
                // caught it on the first run -- which is the reason to write the complement.
                ScaleX = ImageSize / (double)Canonical.Width,
                ScaleY = ImageSize / (double)Canonical.Height,
                BitMapData = Fill(ImageSize * ImageSize, ImagePen),
            };
            _state.ImageSlots[Slot] = SlotFile;
            _state.Resources.Add($"{SlotFile}#0", new IndexedTexture(image));
        }

        [TearDown]
        public void TearDown() {
            _state?.Dispose();
            if (_host != null) {
                Object.DestroyImmediate(_host);
            }
        }

        private static byte[] Fill(int count, int value) {
            var data = new byte[count];
            for (var i = 0; i < count; i++) {
                data[i] = (byte)value;
            }
            return data;
        }

        /// <summary>How many pixels of the buffer carry <paramref name="pen"/>, and how many there are.</summary>
        /// <remarks>
        /// <b>Counted over the whole buffer rather than sampled at a computed point.</b> The first
        /// version of this test asserted a pen at one corner and failed because cutscene Y runs down
        /// from the top while a <c>RenderTexture</c>'s runs up from the bottom — a coordinate
        /// convention this test has no business encoding. What it is actually for is "did the image
        /// arrive", and a count answers that without taking a position on where the origin is.
        /// </remarks>
        private static (int Matching, int Total) PenCount(Texture source, int pen) {
            var rt = (RenderTexture)source;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var probe = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            probe.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            probe.Apply();
            RenderTexture.active = previous;

            Color32[] pixels = probe.GetPixels32();
            Object.DestroyImmediate(probe);

            var matching = 0;
            foreach (Color32 p in pixels) {
                if (p.r == pen) {
                    matching++;
                }
            }
            return (matching, pixels.Length);
        }

        /// <summary>The image's own lookup finds the synthetic entry, so the draw has something to draw.</summary>
        /// <remarks>
        /// Asserted separately because a null image makes <c>DrawImage</c> log and return quietly —
        /// so without this, a broken fixture and a broken draw would look identical.
        /// </remarks>
        [Test]
        public void TheSlotResolvesToTheSyntheticImage() {
            IndexedTexture found = _state.GetImage(Slot, 0);
            Assert.IsNotNull(found, "the state's own Resources lookup should find the registered key");
            Assert.AreEqual(ImageSize, found.Width);
        }

        [Test]
        public void ADrawnImagePutsItsPixelsInTheBuffer() {
            new DrawImage { ImageSlot = Slot, ImageNumber = 0, X = 0, Y = 0 }.ToAction()(_state);

            (int matching, int _) = PenCount(_state.CurrentIndexedBuffer, ImagePen);
            Assert.Greater(matching, 0,
                "a DrawImage should leave the image's index somewhere in the buffer");
        }

        /// <summary>And it does NOT paint the whole buffer — the draw is bounded by the image.</summary>
        /// <remarks>
        /// The complement of the assertion above: a draw that filled everything would satisfy
        /// "pixels arrived" while being just as wrong as one that drew nothing.
        /// </remarks>
        [Test]
        public void ADrawnImageLeavesTheRestOfTheBufferAlone() {
            new DrawImage { ImageSlot = Slot, ImageNumber = 0, X = 0, Y = 0 }.ToAction()(_state);

            (int matching, int total) = PenCount(_state.CurrentIndexedBuffer, ImagePen);
            Assert.Less(matching, total,
                "a 16x16 image must not paint the whole buffer");
        }
    }
}
