namespace BakAgain.ResourceManagement.Converters {
    using GameData.Resources.Image;
    using GameData.Resources.Palette;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using Color = UnityEngine.Color;

    /// <summary>
    /// Bakes a seamless square tile out of a window of a 320x200 SCX background, from the
    /// player's own game data. The terrain pen textures and the dialog panel's wood fill are both
    /// made this way: they used to be baked by Editor tools into committed PNGs, which put original
    /// game art in the repository.
    /// </summary>
    /// <remarks>
    /// The window is sampled at native resolution; the per-row average is subtracted (removing the
    /// vertical lighting gradient so the tile repeats without a band); each output row takes a
    /// source row jittered by up to two rows and a random horizontal wrap-shift; a touch of noise
    /// breaks the palette banding. The random sequence is consumed in a fixed order, so a given
    /// seed reproduces the same tile.
    /// </remarks>
    public static class ScxTileBaker {
        private const int ScxWidth = 320;
        private const int ScxHeight = 200;

        /// <summary>
        /// The SCX as 320x200 RGBA pixels (bottom-up, Unity order), or null when it cannot be
        /// loaded. The image arrives in the extractor's canonical space (1600x1200, each DOS pixel a
        /// 5x6 block), so one pixel per block is taken.
        /// </summary>
        public static Color[] LoadPixels(string scxKey, string paletteKey) {
            // Without game data neither key exists, and loading one anyway logs an InvalidKeyException
            // as an Error; callers already fall back on null.
            if (!HasLocation(paletteKey) || !HasLocation(scxKey)) {
                Debug.LogWarning($"ScxTileBaker: {scxKey} / {paletteKey} not available.");

                return null;
            }
            PaletteResource palette = Addressables.LoadAssetAsync<PaletteResource>(paletteKey).WaitForCompletion();
            BackgroundImage scx = Addressables.LoadAssetAsync<BackgroundImage>(scxKey).WaitForCompletion();
            if (palette == null || scx == null) {
                Debug.LogWarning($"ScxTileBaker: could not load {scxKey} / {paletteKey}.");

                return null;
            }
            Texture2D texture = scx.ToTexture2D(palette.Colors.ToUnity());
            Color[] canonical = texture.GetPixels();
            int width = texture.width;
            int blockW = width / ScxWidth;
            int blockH = texture.height / ScxHeight;
            Object.Destroy(texture);

            var pixels = new Color[ScxWidth * ScxHeight];
            for (int y = 0; y < ScxHeight; y++) {
                for (int x = 0; x < ScxWidth; x++) {
                    pixels[y * ScxWidth + x] = canonical[y * blockH * width + x * blockW];
                }
            }

            return pixels;
        }

        private static bool HasLocation(string key) {
            var handle = Addressables.LoadResourceLocationsAsync(key);
            bool found = handle.WaitForCompletion() is { Count: > 0 };
            Addressables.Release(handle);

            return found;
        }

        /// <summary>
        /// A <paramref name="width"/>-square tile from the window whose top-left is
        /// (<paramref name="x0"/>, <paramref name="dosRow"/>) in DOS (top-down) coordinates.
        /// </summary>
        public static Texture2D Bake(Color[] scx, int x0, int dosRow, int width, int height,
            float noiseAmplitude, System.Random rng) {
            var window = new Color[height * width];
            for (int r = 0; r < height; r++) {
                int rowStart = ((ScxHeight - 1) - (dosRow + r)) * ScxWidth + x0;
                for (int x = 0; x < width; x++) {
                    window[r * width + x] = scx[rowStart + x];
                }
            }

            var rowAvg = new Vector3[height];
            Vector3 meanSum = Vector3.zero;
            for (int r = 0; r < height; r++) {
                Vector3 sum = Vector3.zero;
                for (int x = 0; x < width; x++) {
                    Color c = window[r * width + x];
                    sum += new Vector3(c.r, c.g, c.b);
                }
                rowAvg[r] = sum / width;
                meanSum += rowAvg[r];
            }
            Vector3 mean = meanSum / height;

            var pixels = new Color[width * width];
            for (int y = 0; y < width; y++) {
                int srcRow = Mathf.Clamp(y * height / width + rng.Next(-2, 3), 0, height - 1);
                int xShift = rng.Next(0, width);
                Vector3 gradient = rowAvg[srcRow];
                for (int x = 0; x < width; x++) {
                    Color c = window[srcRow * width + (x + xShift) % width];
                    float red = c.r - gradient.x + mean.x + ((float)rng.NextDouble() * 2f - 1f) * noiseAmplitude;
                    float green = c.g - gradient.y + mean.y + ((float)rng.NextDouble() * 2f - 1f) * noiseAmplitude;
                    float blue = c.b - gradient.z + mean.z + ((float)rng.NextDouble() * 2f - 1f) * noiseAmplitude;
                    pixels[y * width + x] = new Color(Mathf.Clamp01(red), Mathf.Clamp01(green), Mathf.Clamp01(blue), 1f);
                }
            }

            var tile = new Texture2D(width, width, TextureFormat.RGBA32, false) {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            tile.SetPixels(pixels);
            tile.Apply(false, false);

            return tile;
        }
    }
}
