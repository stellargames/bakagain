namespace BakAgain.World.Converters {
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine;

    /// <summary>
    /// Derives fog parameters from RMP palette remap tables.
    /// The original engine does per-pixel palette index remapping at each distance level.
    /// We approximate this as a smooth fog color + intensity curve for shader use.
    /// </summary>
    public static class FogRampBuilder {
        public struct FogRampData {
            public Color FogColor;
            public float[] Intensities; // one per RMP block, 0=no fog, 1=full fog
        }

        /// <summary>
        /// Build fog ramp from a list of RMP remap blocks.
        /// </summary>
        /// <param name="remapBlocks">Ordered list of remap dictionaries (near→far).</param>
        /// <param name="palette">Zone palette as Color[256].</param>
        public static FogRampData BuildFogRamp(
            List<Dictionary<byte, byte>> remapBlocks, Color[] palette) {

            // Use the last (most fogged) block to determine fog target color
            var fogColor = remapBlocks.Count > 0
                ? ExtractFogColor(remapBlocks[^1], palette)
                : Color.gray;

            var intensities = new float[remapBlocks.Count];
            for (int i = 0; i < remapBlocks.Count; i++) {
                intensities[i] = ComputeFogIntensity(remapBlocks[i], palette, fogColor);
            }

            return new FogRampData { FogColor = fogColor, Intensities = intensities };
        }

        /// <summary>
        /// Find the dominant target color in a remap block.
        /// Histogram the remapped palette colors and pick the most common.
        /// </summary>
        public static Color ExtractFogColor(Dictionary<byte, byte> remap, Color[] palette) {
            // Count how many source indices map to each target color
            var colorCounts = new Dictionary<int, (Color color, int count)>();
            foreach (var kvp in remap) {
                byte target = kvp.Value;
                if (target >= palette.Length) continue;
                Color c = palette[target];
                // Quantize to reduce near-duplicates (5-bit per channel)
                int key = ((int)(c.r * 31) << 10) | ((int)(c.g * 31) << 5) | (int)(c.b * 31);
                if (colorCounts.TryGetValue(key, out var existing))
                    colorCounts[key] = (existing.color, existing.count + 1);
                else
                    colorCounts[key] = (c, 1);
            }

            if (colorCounts.Count == 0) return Color.gray;

            return colorCounts.Values.OrderByDescending(x => x.count).First().color;
        }

        /// <summary>
        /// Compute average fog intensity for a remap block.
        /// Measures how far each remapped color moves toward the fog color,
        /// relative to its original distance from the fog color.
        /// </summary>
        public static float ComputeFogIntensity(
            Dictionary<byte, byte> remap, Color[] palette, Color fogColor) {

            float totalIntensity = 0;
            int count = 0;

            foreach (var kvp in remap) {
                byte src = kvp.Key;
                byte dst = kvp.Value;
                if (src >= palette.Length || dst >= palette.Length) continue;

                Color originalColor = palette[src];
                Color remappedColor = palette[dst];

                float originalDist = ColorDistance(originalColor, fogColor);
                if (originalDist < 0.01f) continue; // already at fog color

                float remappedDist = ColorDistance(remappedColor, fogColor);
                float intensity = 1f - (remappedDist / originalDist);
                intensity = Mathf.Clamp01(intensity);

                totalIntensity += intensity;
                count++;
            }

            return count > 0 ? totalIntensity / count : 0f;
        }

        private static float ColorDistance(Color a, Color b) {
            float dr = a.r - b.r;
            float dg = a.g - b.g;
            float db = a.b - b.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }
    }
}
