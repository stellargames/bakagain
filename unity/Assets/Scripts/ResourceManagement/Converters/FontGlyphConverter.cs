namespace BakAgain.ResourceManagement.Converters {
    using GameData.Resources.Font;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Turns a <c>.FNT</c> glyph into a sprite.
    /// </summary>
    /// <remarks>
    /// <b>For the fonts whose glyphs are pictures rather than letters.</b> SPELL.FNT's symbols and
    /// PUZZLE.FNT's shapes are indexed from character zero and drawn as images; text fonts go
    /// through the TMP assets instead. Nothing here is a substitute for those — it is the only way
    /// to get at the symbols, which no other resource carries.
    ///
    /// <para>Each glyph pixel becomes the block the font declares (<see cref="FontResource.PixelWidth"/>
    /// x <see cref="FontResource.PixelHeight"/> canonical units, 5 x 6 for the original's fonts), so
    /// the sprite lands at canonical scale next to everything around it (TASK-765).</para>
    /// </summary>
    public static class FontGlyphConverter {
        /// <summary>The glyph as a sprite, or null when there is nothing to draw.</summary>
        /// <param name="ink">
        /// The colour a MONOCHROME glyph's pixels take. A PALETTED one ignores it and uses its own
        /// indices — the symbols carry their own shading, and painting them a flat colour throws
        /// that away.
        /// </param>
        /// <param name="palette">Resolves a paletted glyph's indices; ignored by a monochrome one.</param>
        public static Sprite ToSprite(FontResource font, FontGlyph glyph, Color ink,
            GameData.Resources.Palette.PaletteResource palette = null) {
            if (font == null || glyph == null || glyph.Width <= 0 || glyph.Rows.Count == 0) {
                return null;
            }

            int pixelW = System.Math.Max(1, (int)System.Math.Round(font.PixelWidth));
            int pixelH = System.Math.Max(1, (int)System.Math.Round(font.PixelHeight));
            int width = glyph.Width * pixelW;
            int height = glyph.Rows.Count * pixelH;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false) {
                filterMode = FilterMode.Point,   // the original's pixels, not a blur of them
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[width * height];
            Color32 lit = ink;
            for (var y = 0; y < height; y++) {
                // Texture rows run bottom-up; the glyph's run top-down.
                int glyphRow = glyph.Rows.Count - 1 - (y / pixelH);
                for (var x = 0; x < width; x++) {
                    int index = glyph.PixelAt(x / pixelW, glyphRow);
                    if (index == 0) {
                        continue;
                    }
                    pixels[(y * width) + x] = glyph.PixelFormat == FontPixelFormat.Paletted
                        ? BakAgain.Graphics.PaletteColors.ResolvePen(palette, index, ink)
                        : lit;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false);

            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f, extrude: 0, meshType: SpriteMeshType.FullRect);
        }

        /// <summary>Every glyph of a font, by its character code.</summary>
        public static Dictionary<int, Sprite> ToSprites(FontResource font, Color ink,
            GameData.Resources.Palette.PaletteResource palette = null) {
            var sprites = new Dictionary<int, Sprite>();
            if (font?.Glyphs == null) {
                return sprites;
            }
            for (var i = 0; i < font.Glyphs.Count; i++) {
                Sprite sprite = ToSprite(font, font.Glyphs[i], ink, palette);
                if (sprite != null) {
                    sprites[font.FirstCharacter + i] = sprite;
                }
            }

            return sprites;
        }
    }
}
