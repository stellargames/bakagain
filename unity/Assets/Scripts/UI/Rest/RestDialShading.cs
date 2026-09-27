namespace BakAgain.UI.Rest {
    using GameData.Resources.Config;
    using GameData.Resources.Palette;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// A shaded copy of the rest dial's artwork — the colours the wedge's remap table produces.
    /// </summary>
    /// <remarks>
    /// <b>The wedge substitutes colours; it does not tint.</b> The original swaps the polygon
    /// filler's span callback for one that reads each pixel and writes
    /// <c>table[pixel]</c> back, where the table darkens palette entries 0..111 and leaves
    /// everything above alone — see <see cref="EncampShadow"/>. Reproducing that as a translucent
    /// black triangle darkens the moon and the stars along with the ground, and darkens uniformly
    /// where the palette often has nothing darker to offer.
    ///
    /// <para>So the shaded colours are computed once into a copy of the artwork, and the wedge is
    /// drawn as a textured triangle sampling that copy at the same place. No per-frame work and no
    /// reading back what is already on screen.</para>
    /// </remarks>
    public static class RestDialShading {
        /// <summary>
        /// The artwork as it looks under the wedge, or null when it cannot be computed.
        /// </summary>
        /// <remarks>
        /// <b>A pixel is matched back to its palette entry by exact colour.</b> Our textures came
        /// from that palette, so the match is the authored index rather than an approximation —
        /// and where the palette repeats a colour the FIRST entry wins, which is the same tie rule
        /// the original's own nearest-colour search uses.
        /// </remarks>
        public static Texture2D Build(Sprite source, PaletteResource palette) {
            if (source == null || palette?.Colors == null || palette.Colors.Length == 0) {
                return null;
            }

            Texture2D art = source.texture;
            if (art == null || !art.isReadable) {
                return null;
            }

            var indexOf = new Dictionary<Color32, int>(new ColorComparer());
            var entries = new List<(int R, int G, int B)>(palette.Colors.Length);
            for (var i = 0; i < palette.Colors.Length; i++) {
                var c = palette.Colors[i];
                entries.Add((c.R, c.G, c.B));
                Color32 rgba = ToRgba(c);
                if (!indexOf.ContainsKey(rgba)) {
                    indexOf[rgba] = i;
                }
            }

            // The palette's channels are 0..255 (the extractor widens VGA's six bits), so the
            // darkening has to be widened with them.
            int[] table = EncampShadow.Table(entries, EncampShadow.DarkeningFor(byte.MaxValue));
            Color32[] pixels = art.GetPixels32();
            for (var i = 0; i < pixels.Length; i++) {
                Color32 pixel = pixels[i];
                if (!indexOf.TryGetValue(pixel, out int entry)) {
                    // Not a palette colour — a converter artefact or an override's own art. Left
                    // alone rather than guessed at: the remap is defined over palette entries.
                    continue;
                }
                var shaded = ToRgba(palette.Colors[table[entry]]);
                shaded.a = pixel.a;
                pixels[i] = shaded;
            }

            var shadedArt = new Texture2D(art.width, art.height, TextureFormat.RGBA32,
                mipChain: false) {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            shadedArt.SetPixels32(pixels);
            shadedArt.Apply(updateMipmaps: false);

            return shadedArt;
        }

        /// <summary>A palette entry as a screen colour — already eight-bit off the extractor.</summary>
        private static Color32 ToRgba(GameData.Resources.Palette.Color c) =>
            new Color32(c.R, c.G, c.B, 255);

        /// <summary>Compares ignoring alpha, which the artwork carries and the palette does not.</summary>
        private sealed class ColorComparer : IEqualityComparer<Color32> {
            public bool Equals(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

            public int GetHashCode(Color32 c) => (c.r << 16) | (c.g << 8) | c.b;
        }
    }
}
