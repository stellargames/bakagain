namespace BakAgain.ResourceManagement.Models {
    using BakAgain.Graphics;
    using BakAgain.Utility;
    using GameData.Resources.Image;
    using JetBrains.Annotations;
    using System;
    using UnityEngine;

    /// <summary>
    /// Represents an image that can be either an indexed color palette image or a regular RGBA texture.
    /// Provides a unified interface for both formats, optimized for shader consumption.
    /// </summary>
    public class IndexedTexture : IDisposable {
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsIndexed { get; }
        public Vector2 Scale { get; set; }

        /// <summary>
        /// Natural drawn width in canonical 1600×1200 px. <see cref="Scale"/> is a
        /// fraction of the full frame (original bitmaps: width/320; override
        /// metadata: authored fraction), so this is the size a draw command with
        /// no resize produces — use it to convert command sizes (canonical px)
        /// into unitless resize factors regardless of the bitmap's resolution.
        /// </summary>
        public float CanonicalWidth => Canonical.Width * Scale.x;

        /// <summary>Natural drawn height in canonical px — see <see cref="CanonicalWidth"/>.</summary>
        public float CanonicalHeight => Canonical.Height * Scale.y;


        [CanBeNull]
        public Texture2D IndexTexture { get; private set; }

        public Texture2D DirectTexture { get; private set; }

        private const FilterMode FilterMode = UnityEngine.FilterMode.Point;
        private const TextureWrapMode WrapMode = TextureWrapMode.Clamp;

        /// <summary>
        /// Creates an IndexedTexture from a regular Texture2D
        /// </summary>
        public IndexedTexture(Texture2D texture, Vector2 scale) {
            if (!texture)
                throw new ArgumentNullException(nameof(texture));

            Width = texture.width;
            Height = texture.height;
            IsIndexed = false;
            DirectTexture = texture;
            Scale = scale;
        }

        /// <summary>
        /// Creates an indexed texture with the specified dimensions, palette, and index data
        /// </summary>
        public IndexedTexture(ImageResource image) {
            if (image.BitMapData == null)
                throw new ArgumentNullException(nameof(image.BitMapData));

            Width = image.Width;
            Height = image.Height;
            IsIndexed = true;
            Scale = new Vector2((float)image.ScaleX, (float)image.ScaleY);

            IndexTexture = CreateIndexTexture(image);
        }

        private IndexedTexture(IndexedTexture original) {
            Width = original.Width;
            Height = original.Height;
            IsIndexed = original.IsIndexed;
            DirectTexture = original.DirectTexture;
            IndexTexture = original.IndexTexture;
            Scale = original.Scale;
        }

        private Texture2D CreateIndexTexture(ImageResource image) {
            var indexTexture = new Texture2D(
                Width,
                Height,
                TextureFormat.ARGB32,
                false,
                true
            ) {
                filterMode = FilterMode,
                wrapMode = WrapMode
            };

            var colorData = new Color[Width * Height];
            int index = 0;

            if (image is BmImage bmImage && bmImage.Flags.HasFlag(ImageFlags.ReversedRowColumn)) {
                // Row-major order
                for (int x = 0; x < Width; x++) {
                    for (int y = Height - 1; y >= 0; y--) {
                        byte rawIndex = image.BitMapData[index++];
                        colorData[y * Width + x] = new Color(rawIndex / 255f, 0, 0, rawIndex == 0 ? 0 : 1);
                    }
                }
            } else {
                // Column-major order
                for (int y = Height - 1; y >= 0; y--) {
                    for (int x = 0; x < Width; x++) {
                        byte rawIndex = image.BitMapData[index++];
                        colorData[y * Width + x] = new Color(rawIndex / 255f, 0, 0, rawIndex == 0 ? 0 : 1);
                    }
                }
            }

            indexTexture.name = image.Id;
            indexTexture.SetPixels(colorData);
            indexTexture.Apply();

            return indexTexture;
        }

        /// <summary>
        /// Gets the texture that should be used for rendering.
        /// For indexed textures, this requires a shader that samples both index and palette textures.
        /// For direct textures, this can be used directly.
        /// </summary>
        public Texture2D GetRenderTexture() {
            return IsIndexed ? IndexTexture : DirectTexture;
        }

        /// <summary>
        /// Destroys the index texture this instance created. The direct texture is
        /// <em>not</em> destroyed: it is supplied by the caller (or aliased through
        /// <see cref="WithTexture"/>) and its lifetime is owned elsewhere.
        /// </summary>
        public void Dispose() {
            if (IsIndexed) {
                UnityObjectUtil.Destroy(IndexTexture);
                IndexTexture = null;
            }
        }

        public IndexedTexture WithTexture(Texture2D newTexture) {
            if (!newTexture)
                throw new ArgumentNullException(nameof(newTexture));

            return new IndexedTexture(this) {
                Scale = Scale * new Vector2((float)newTexture.width / Width, (float)newTexture.height / Height),
                Width = newTexture.width,
                Height = newTexture.height,
                DirectTexture = IsIndexed ? null : newTexture,
                IndexTexture = IsIndexed ? newTexture : null
            };
        }
    }
}