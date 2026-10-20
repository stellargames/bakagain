namespace BakAgain.Tests.Editor.World {
    using BakAgain.Tests.TestSupport;
    using BakAgain.World.Rendering;
    using NUnit.Framework;
    using System;
    using System.IO;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// A mod's HD billboard frames (mod system spec 4.2, HD billboard extension): which files a frame
    /// key finds, the sun variants, the sidecar's variation, and mips that keep a cut-out's coverage.
    /// </summary>
    public class HdFramesTests {
        private TempOverrideDirectory _mods;

        [SetUp]
        public void SetUp() {
            _mods = new TempOverrideDirectory();
            HdFrames.ClearCache();
        }

        [TearDown]
        public void TearDown() {
            HdFrames.ClearCache();
            _mods.Dispose();
        }

        /// <summary>A white <paramref name="width"/> x <paramref name="height"/> PNG at <c>BMX/&lt;folder&gt;/&lt;file&gt;</c>.</summary>
        private void Png(string folder, string file, int width = 4, int height = 8) {
            var t = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var px = new Color32[width * height];
            Array.Fill(px, new Color32(255, 255, 255, 255));
            t.SetPixels32(px);
            byte[] png = t.EncodeToPNG();
            Object.DestroyImmediate(t);
            string dir = Path.Combine(_mods.Root, "BMX", folder);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, file), png);
        }

        [Test]
        public void TryLoad_NoModFile_False() {
            Assert.IsFalse(HdFrames.TryLoad("Z01SLOT0.BMX#3", out HdFrame frame));
            Assert.IsNull(frame);
        }

        [Test]
        public void TryLoad_PlainPng_NoSun() {
            Png("Z01SLOT0", "3.png");

            Assert.IsTrue(HdFrames.TryLoad("Z01SLOT0.BMX#3", out HdFrame frame));
            Assert.IsNotNull(frame.Base);
            Assert.IsFalse(frame.HasSun);
            Assert.IsNull(frame.Front);
            Assert.AreEqual(0f, frame.VarySize);
            Assert.AreEqual(0f, frame.VaryColor);
        }

        [Test]
        public void TryLoad_FourVariants_HasSun() {
            Png("Z01SLOT0", "3.png");
            foreach (string side in new[] { "front", "left", "right", "back" }) {
                Png("Z01SLOT0", $"3.{side}.png");
            }

            Assert.IsTrue(HdFrames.TryLoad("Z01SLOT0.BMX#3", out HdFrame frame));
            Assert.IsTrue(frame.HasSun);
            Assert.IsNotNull(frame.Front);
            Assert.IsNotNull(frame.Left);
            Assert.IsNotNull(frame.Right);
            Assert.IsNotNull(frame.Back);
        }

        [Test]
        public void TryLoad_ColourSetFolderPreferred() {
            Png("MOR1_CS2", "7.png", 4, 8);
            Png("MOR1", "7.png", 16, 16);

            Assert.IsTrue(HdFrames.TryLoad("MOR1_CS2#7", out HdFrame frame));
            Assert.AreEqual(4, frame.Base.width, "the colour set's own picture, not the shared one");
        }

        [Test]
        public void TryLoad_FallsBackToSharedFolder() {
            Png("MOR1", "7.png", 16, 16);

            Assert.IsTrue(HdFrames.TryLoad("MOR1_CS2#7", out HdFrame frame));
            Assert.AreEqual(16, frame.Base.width);
        }

        [Test]
        public void TryLoad_SidecarVariation() {
            Png("Z01SLOT0", "3.png");
            File.WriteAllText(Path.Combine(_mods.Root, "BMX", "Z01SLOT0", "3.json"),
                "{ \"ScaleX\": 1, \"ScaleY\": 1, \"VarySize\": 0.08, \"VaryColor\": 0.05 }");

            Assert.IsTrue(HdFrames.TryLoad("Z01SLOT0.BMX#3", out HdFrame frame));
            Assert.AreEqual(0.08f, frame.VarySize, 1e-6f);
            Assert.AreEqual(0.05f, frame.VaryColor, 1e-6f);
        }

        [Test]
        public void Mips_PreserveCoverage() {
            // Fine noise (r^2) under a coarse random envelope, its mean below the cutoff everywhere: plain
            // box-filtered mips lose the speckle that reaches the cutoff, the thinning a tree shows with
            // distance. The envelope (one value per 16x16 block) keeps the small mips' alphas spread out.
            const int size = 256, block = 16;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true);
            var px = new Color32[size * size];
            var random = new System.Random(1);
            var envelope = new double[(size / block) * (size / block)];
            for (int i = 0; i < envelope.Length; i++) envelope[i] = random.NextDouble();
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    double r = random.NextDouble();
                    double e = envelope[(y / block) * (size / block) + x / block];
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * Math.Min(1, 1.2 * e * r * r)));
                }
            }
            t.SetPixels32(px);
            t.Apply(updateMipmaps: true);

            HdFrames.PreserveCoverage(t, HdFrames.AlphaCutoff);

            float full = Coverage(t, 0);
            // Down to 16x16; below that one pixel is more than the 2% tolerance.
            for (int mip = 1; size >> mip >= block; mip++) {
                Assert.AreEqual(full, Coverage(t, mip), 0.02f, $"mip {mip}");
            }
            Object.DestroyImmediate(t);
        }

        private static float Coverage(Texture2D t, int mip) {
            Color32[] px = t.GetPixels32(mip);
            int n = 0;
            foreach (Color32 c in px) {
                if (c.a >= HdFrames.AlphaCutoff * 255) n++;
            }
            return n / (float)px.Length;
        }
    }
}
