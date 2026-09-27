namespace BakAgain.ResourceManagement.Converters {
    using GameData.Resources.Image;
    using UnityEngine;

    /// <summary>
    /// Core image conversion utilities. Provides the canonical implementation for converting
    /// BaK image resources to Unity textures. <b>Do not reimplement this logic elsewhere</b> —
    /// use <see cref="ImageConverter.ConvertToTexture"/> or the <see cref="ConverterExtensions.ToTexture2D"/>
    /// extension method.
    /// </summary>
    public static class ImageConverter {
        /// <summary>
        /// Converts an ImageResource to a Unity Texture2D using the provided palette.
        /// <para><b>DO NOT reimplement this logic elsewhere</b> — use this method or
        /// <see cref="ConverterExtensions.ToTexture2D"/></para>
        /// </summary>
        /// <param name="image">The image resource to convert.</param>
        /// <param name="colors">The Unity color palette (256 entries).</param>
        /// <param name="transparentIndex0">If true, palette index 0 will be fully transparent.</param>
        /// <returns>A new Texture2D containing the converted image data.</returns>
        public static Texture2D ConvertToTexture(ImageResource image, UnityEngine.Color[] colors, bool transparentIndex0 = false) {
            var texture = new Texture2D(
                image.Width,
                image.Height,
                TextureFormat.RGBA32,
                false,
                false) {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[image.Width * image.Height];
            int index = 0;

            // SCX files and BMX files with flag 0x20 set have the image data in column-major order
            if (image is BmImage bmImage && bmImage.Flags.HasFlag(ImageFlags.ReversedRowColumn)) {
                // the image is in row-major order
                for (int x = 0; x < image.Width; x++) {
                    for (int y = image.Height - 1; y >= 0; y--) {
                        pixels[y * image.Width + x] = SamplePixel(colors, image.BitMapData[index++], transparentIndex0);
                    }
                }
            } else {
                for (int y = image.Height - 1; y >= 0; y--) {
                    for (int x = 0; x < image.Width; x++) {
                        pixels[y * image.Width + x] = SamplePixel(colors, image.BitMapData[index++], transparentIndex0);
                    }
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();

            return texture;
        }

        private static Color SamplePixel(Color[] colors, byte colorIndex, bool transparentIndex0) {
            Color color = colors[colorIndex];
            if (transparentIndex0 && colorIndex == 0) {
                color.a = 0;
            }
            return color;
        }
    }
}
