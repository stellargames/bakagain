namespace BakAgain.UI {
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using UnityEngine.UIElements;

    /// <summary>
    /// A <see cref="VisualElement"/> whose background image is loaded from an
    /// Addressables key — e.g. a game-archive sub-resource like
    /// <c>"BICONS1.BMX#0"</c>. Stock UXML can only reference project assets by
    /// path/GUID (resolved at import), so it cannot point at archive images that
    /// exist only at runtime. This control carries the key as the <c>address</c>
    /// UXML attribute and loads it itself, which also lets
    /// <c>VisualTreeUxmlExporter</c> round-trip these images (it reads
    /// <see cref="address"/> back out).
    /// </summary>
    [UxmlElement]
    public partial class ArchiveImage : VisualElement {
        private string _address;
        private bool _sizeToImage = true;
        private AsyncOperationHandle<Sprite> _handle;
        private bool _handleValid;

        /// <summary>The Addressables key to load the background sprite from.</summary>
        [UxmlAttribute]
        public string address {
            get => _address;
            set {
                if (_address == value) {
                    return;
                }
                _address = value;
                Reload();
            }
        }

        /// <summary>When true (default), the element resizes to the sprite's native
        /// pixel dimensions on load — matching the original engine, which blits
        /// icons at their own size.</summary>
        [UxmlAttribute]
        public bool sizeToImage {
            get => _sizeToImage;
            set => _sizeToImage = value;
        }

        public ArchiveImage() {
            // Load lazily / re-load if re-attached after a detach released the handle
            // (e.g. the editor's UIDocument.OnValidate wipe + rebuild).
            RegisterCallback<AttachToPanelEvent>(_ => {
                if (!_handleValid && !string.IsNullOrEmpty(_address)) {
                    Reload();
                }
            });
            RegisterCallback<DetachFromPanelEvent>(_ => Release());
        }

        public ArchiveImage(string address) : this() {
            this.address = address;
        }

        private void Reload() {
            Release();
            if (string.IsNullOrEmpty(_address)) {
                style.backgroundImage = new StyleBackground();
                return;
            }
            _handle = Addressables.LoadAssetAsync<Sprite>(_address);
            _handleValid = true;
            _handle.Completed += op => {
                if (!_handleValid) {
                    return; // released (detached) while loading
                }
                if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null) {
                    Debug.LogError($"ArchiveImage: failed to load sprite '{_address}'.");
                    return;
                }
                Apply(op.Result);
            };
        }

        private void Apply(Sprite sprite) {
            style.backgroundImage = Background.FromSprite(sprite);
            style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            if (_sizeToImage) {
                style.width = sprite.rect.width;
                style.height = sprite.rect.height;
            }
        }

        private void Release() {
            if (_handleValid && _handle.IsValid()) {
                Addressables.Release(_handle);
            }
            _handleValid = false;
        }
    }
}
