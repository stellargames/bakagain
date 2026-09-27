namespace BakAgain.ResourceManagement.Converters {
    using Color = GameData.Resources.Palette.Color;
    using GameData.Resources.Image;

    public static class ConverterExtensions {
        public static UnityEngine.Color[] ToUnity(this Color[] colors) {
            var unityColors = new UnityEngine.Color[colors.Length];
            for (int index = 0; index < colors.Length; index++) {
                Color color = colors[index];
                const float divider = byte.MaxValue;
                unityColors[index] = new UnityEngine.Color(color.R / divider, color.G / divider, color.B / divider, color.A / divider);
            }

            return unityColors;
        }

        /// <summary>
        /// Converts an ImageResource to a Unity Texture2D using the provided palette.
        /// <para>Convenience extension method that delegates to <see cref="ImageConverter.ConvertToTexture"/>.</para>
        /// </summary>
        /// <param name="image">The image resource to convert.</param>
        /// <param name="colors">The Unity color palette (256 entries).</param>
        /// <param name="transparentIndex0">If true, palette index 0 will be fully transparent.</param>
        /// <returns>A new Texture2D containing the converted image data.</returns>
        public static UnityEngine.Texture2D ToTexture2D(this ImageResource image, UnityEngine.Color[] colors, bool transparentIndex0 = false) {
            return ImageConverter.ConvertToTexture(image, colors, transparentIndex0);
        }
    }
}
