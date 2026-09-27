namespace BakAgain.ResourceManagement.Converters {
    using GameData;
    using GameData.Resources;
    using GameData.Resources.Dialog;
    using GameData.Resources.Image;
    using GameData.Resources.Palette;
    using ResourceExtraction.Extractors;
    using System;
    using System.IO;
    using System.Linq;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using UnityEngine.ResourceManagement.ResourceProviders;
    using Color = GameData.Resources.Palette.Color;

    internal abstract class UnityConverterBase<T> {
        // public abstract T Convert(ProvideHandle key, Stream stream);

        protected static ImageResource LoadImageResource(ProvideHandle provideHandle, Stream stream) {
            ImageResource image;

            if (provideHandle.Location.PrimaryKey.ToLower().EndsWith(".scx")) {
                // Load SCX file
                var extractor = new ScreenExtractor();
                image = extractor.Extract(provideHandle.Location.PrimaryKey, stream);
                if (image.BitMapData == null || image.BitMapData.Length < image.Width * image.Height) {
                    throw new Exception("Not enough data in SCX file for it's dimensions");
                }
            } else {
                // Load BMX file
                var extractor = new BitmapExtractor();
                ImageSet imageSet = extractor.Extract(provideHandle.Location.PrimaryKey, stream);
                string internalId = provideHandle.ResourceManager.TransformInternalId(provideHandle.Location);
                image = imageSet.Images[int.Parse(internalId)];

                if (image.BitMapData == null || image.BitMapData.Length < image.Width * image.Height) {
                    throw new Exception("Not enough data in BMX file for it's dimensions");
                }
            }

            return image;
        }

        protected static UnityEngine.Color[] LoadPalette(string key, int subImage = -1) {
            // Load palette. subImage lets PaletteMapping override the palette for a specific BMX
            // sub-image (e.g. the Contents Exit icon BICONS1/2 #66 -> CONTENTS.PAL); -1 = no sub-image.
            string paletteKey = PaletteMapping.GetPaletteFor(key, subImage);
            if (paletteKey == null && key.Length > 4) {
                paletteKey = key[..^4] + ".PAL";
            }
            AsyncOperationHandle<PaletteResource> paletteOp = Addressables.LoadAssetAsync<PaletteResource>(paletteKey);
            Color[] colors = paletteOp.WaitForCompletion().Colors;
            const float divider = byte.MaxValue;

            return colors
                .Select(color => new UnityEngine.Color(color.R / divider, color.G / divider, color.B / divider, color.A / divider))
                .ToArray();
        }

        /// <summary>
        /// The image's own palette with a host screen's palette showing through everywhere the
        /// image's does not define a colour.
        /// </summary>
        /// <remarks>
        /// <b>An actor portrait's palette is partial on purpose.</b> <c>ACT###.PAL</c> defines only
        /// <see cref="ActorFaceCache.FaceRangeFirst"/>..<c>FaceRangeEnd</c>, and
        /// <c>askabout_actor_spr_blit_pal_swap</c> (canassa <c>SRC/DIALOG/ASKABOUT.C:155-167</c>)
        /// fills every index outside it from the palette the screen has installed. 47 of the 53
        /// shipped <c>ACT*.PAL</c> files are zero out there because the game overwrites it anyway —
        /// so reading the file alone paints the portrait's surround black, which is what
        /// <c>TASK-363</c> reported. On the character sheet the host is <c>INVENTOR.PAL</c>, whose
        /// 0x0b-0x0f run is the sheet's dithered browns, and the surround is the frame the player
        /// sees.
        /// </remarks>
        protected static UnityEngine.Color[] LoadPaletteOver(string key, int subImage, string hostPaletteKey) {
            UnityEngine.Color[] own = LoadPalette(key, subImage);
            UnityEngine.Color[] host = LoadPalette(hostPaletteKey);
            var merged = (UnityEngine.Color[])own.Clone();
            for (int i = 0; i < merged.Length; i++) {
                if (i < ActorFaceCache.FaceRangeFirst || i >= ActorFaceCache.FaceRangeEnd) {
                    if (i < host.Length) {
                        merged[i] = host[i];
                    }
                }
            }

            return merged;
        }

        protected static Texture2D ConvertToTexture(ImageResource image, UnityEngine.Color[] colors, bool transparentIndex0 = false) {
            return ImageConverter.ConvertToTexture(image, colors, transparentIndex0);
        }

        public abstract T Convert(IResource resource, string subResourceId);
    }
}