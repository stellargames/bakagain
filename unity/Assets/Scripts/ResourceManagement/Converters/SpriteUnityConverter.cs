namespace BakAgain.ResourceManagement.Converters {
    using GameData.Resources;
    using GameData.Resources.Image;
    using UnityEngine;

    internal class SpriteUnityConverter : UnityConverterBase<Sprite> {

        public override Sprite Convert(IResource resource, string subResourceId) {

            var image = resource as ImageResource;
            // Strip sub-resource suffixes ([N] or #N) to get the base filename for palette lookup
            string baseId = resource.Id.Split('[', '#')[0];
            // Forward the sub-image index so PaletteMapping can override the palette for a specific
            // BMX frame (e.g. the Contents Exit icon BICONS1/2 #66 -> CONTENTS.PAL). -1 = whole image.
            int subImage = ParseSubImageIndex(resource.Id);
            // The requested key — not resource.Id, which is the extractor's "NAME.BMX[0]" and never
            // carries the suffix — is where a host palette is named.
            string host = GameData.PaletteMapping.HostPaletteOf(subResourceId ?? string.Empty);
            Color[] colors = host == null
                ? LoadPalette(baseId, subImage)
                : LoadPaletteOver(baseId, subImage, host);
            // BMX sub-images use palette index 0 as transparent; SCX backgrounds do not
            bool isSubImage = image is BmImage;
            Texture2D texture = ConvertToTexture(image, colors, transparentIndex0: isSubImage);

            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }

        // Extract the sub-image index from a resource id like "BICONS1.BMX#66" or "BICONS1.BMX[66]".
        // Returns -1 when there is no sub-resource suffix (the whole image).
        private static int ParseSubImageIndex(string id) {
            int sep = id.IndexOfAny(new[] { '#', '[' });
            if (sep < 0) {
                return -1;
            }
            string rest = id.Substring(sep + 1).TrimEnd(']');
            return int.TryParse(rest, out int n) ? n : -1;
        }
    }
}