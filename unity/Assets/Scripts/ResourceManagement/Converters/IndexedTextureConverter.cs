namespace BakAgain.ResourceManagement.Converters {
    using BakAgain.ResourceManagement.Models;
    using GameData.Resources;
    using GameData.Resources.Image;

    internal class IndexedTextureConverter : UnityConverterBase<IndexedTexture> {
        public override IndexedTexture Convert(IResource resource, string subResourceId) {
            if (resource is ImageSet imageSet) {
                return new IndexedTexture(imageSet.Images[int.Parse(subResourceId)]);
            }
            if (resource is BackgroundImage backgroundImage) {
                return new IndexedTexture(backgroundImage);
            }
            if (resource is ImageResource imageResource) {
                return new IndexedTexture(imageResource);
            }

            return null;
        }
    }
}
