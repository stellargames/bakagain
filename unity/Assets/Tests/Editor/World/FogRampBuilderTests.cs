namespace BakAgain.Tests.Editor.World {
    using BakAgain.World.Converters;
    using NUnit.Framework;
    using System.Collections.Generic;
    using UnityEngine;

    public class FogRampBuilderTests {
        [Test]
        public void ExtractFogColor_AllRemapToSameIndex_ReturnsThatColor() {
            // RMP block where every index maps to index 128
            var remap = new Dictionary<byte, byte>();
            for (int i = 0; i < 256; i++)
                remap[(byte)i] = 128;

            var palette = new Color[256];
            palette[128] = new Color(0.5f, 0.5f, 0.5f, 1f);

            var fogColor = FogRampBuilder.ExtractFogColor(remap, palette);

            Assert.AreEqual(0.5f, fogColor.r, 0.05f);
            Assert.AreEqual(0.5f, fogColor.g, 0.05f);
            Assert.AreEqual(0.5f, fogColor.b, 0.05f);
        }

        [Test]
        public void ComputeFogIntensity_IdentityRemap_ReturnsZero() {
            // Identity remap = no fog
            var remap = new Dictionary<byte, byte>();
            for (int i = 0; i < 256; i++)
                remap[(byte)i] = (byte)i;

            var palette = new Color[256];
            for (int i = 0; i < 256; i++)
                palette[i] = new Color(i / 255f, 0, 0, 1);

            float intensity = FogRampBuilder.ComputeFogIntensity(remap, palette, Color.white);
            Assert.AreEqual(0f, intensity, 0.05f);
        }

        [Test]
        public void BuildFogRamp_MultipleBlocks_ProducesGradient() {
            var palette = new Color[256];
            palette[0] = Color.red;
            palette[128] = Color.gray;

            // Two blocks: first is identity (no fog), second maps everything to 128
            var blocks = new List<Dictionary<byte, byte>>();
            var identity = new Dictionary<byte, byte>();
            for (int i = 0; i < 256; i++) identity[(byte)i] = (byte)i;
            var maxFog = new Dictionary<byte, byte>();
            for (int i = 0; i < 256; i++) maxFog[(byte)i] = 128;

            blocks.Add(identity);
            blocks.Add(maxFog);

            var result = FogRampBuilder.BuildFogRamp(blocks, palette);

            Assert.IsNotNull(result.FogColor);
            Assert.AreEqual(2, result.Intensities.Length);
            Assert.AreEqual(0f, result.Intensities[0], 0.1f); // no fog at distance 0
            Assert.Greater(result.Intensities[1], 0.5f);       // strong fog at max distance
        }
    }
}
