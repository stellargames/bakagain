namespace BakAgain.Graphics {
    using JetBrains.Annotations;
    using UnityEngine;
    using UnityEngine.UI;
    using Image = UnityEngine.UIElements.Image;

    // @TODO: get rid of this and just use textures?
    // Leave it for now, until we're sure we don't need it.

    public class UiImage {
        private readonly Image _image;
        private readonly RawImage _rawImage;

        public UiImage(Image canvas) {
            _image = canvas;
            SupportsFading = false;
        }

        public UiImage(RawImage rawImage) {
            _rawImage = rawImage;
            SupportsFading = true;
        }

        [CanBeNull]
        public Texture2D Texture => _rawImage?.texture as Texture2D ?? _image?.image as Texture2D;

        public bool SupportsFading { get; }
    }
}