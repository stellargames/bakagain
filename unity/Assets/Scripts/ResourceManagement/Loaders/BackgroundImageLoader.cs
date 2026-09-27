namespace BakAgain.ResourceManagement.Loaders {
    using BakAgain.UI;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using UnityEngine.UIElements;

    /// <summary>
    /// Loads an SCX background image as the canonical stage's background. One
    /// of the three components that compose a REQ-driven screen (paired with
    /// <see cref="UserInterfaceLoader"/> and a screen-specific controller).
    /// The sprite stretches to the 1600×1200 stage, which holds the 4:3
    /// pillarboxed presentation (see <see cref="CanonicalStage"/>).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class BackgroundImageLoader : MonoBehaviour {
        [SerializeField]
        private string imageAddress = "OPTIONS0.SCX";

        private AsyncOperationHandle<Sprite> _handle;
        private bool _handleValid;
        private UIDocument _uiDocument;

        private void Awake() {
            _uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable() {
            // Defensive: a previous OnEnable's handle should already have been
            // released by OnDisable, but if anything left one dangling, drop it
            // before starting a fresh load.
            ReleaseHandle();
            _handle = Addressables.LoadAssetAsync<Sprite>(imageAddress);
            _handleValid = true;
            _handle.Completed += operationHandle => {
                // The screen may have been disabled while loading; OnDisable
                // already released the handle, so we mustn't apply the result.
                if (!_handleValid || _uiDocument == null || _uiDocument.rootVisualElement == null) {
                    return;
                }
                // GetOrCreate is shared with UserInterfaceLoader, and order-independent by
                // design (see CanonicalStage's "Order-independence" doc): whichever loader's
                // OnEnable completes first creates the stage, and a real frame from the other
                // loader always wins over this loader's fallback, regardless of which runs first.
                // The style survives the REQ loader's stage.Clear() (which only removes children).
                // FALLBACK: this loader only ever has a Sprite in hand, no design-frame-bearing
                // resource — deliberately not reaching into the sibling UserInterfaceLoader for its
                // Frame, which would coincidentally work here (they're siblings on every REQ
                // screen) but would make this loader depend on the other's load order/lifecycle for
                // something CanonicalStage.GetOrCreate already reconciles correctly on its own.
                VisualElement stage = CanonicalStage.GetOrCreate(_uiDocument.rootVisualElement, null);
                stage.style.backgroundImage = Background.FromSprite(operationHandle.Result);
                // SCX backgrounds are 320×200 source pixels — stretch to fill
                // the canonical 1600×1200 stage instead of rendering at native
                // sprite size in the top-left corner.
                stage.style.backgroundSize =
                    new BackgroundSize(Length.Percent(100), Length.Percent(100));
            };
        }

        private void OnDisable() {
            // Preferences (and any other screen that toggles active state)
            // re-enters OnEnable on every reopen — release the handle here so
            // each cycle doesn't leak a sprite into Addressables' refcount.
            ReleaseHandle();
            // Drop the now-stale background reference: once the handle is
            // released, the sprite asset may unload, leaving a dangling style.
            // The next OnEnable will overwrite it once the new load completes.
            // (Find rather than GetOrCreate — if the stage is already gone,
            // e.g. the REQ loader's OnDisable cleared the root, nothing to do.)
            VisualElement existingStage = _uiDocument != null
                ? CanonicalStage.Find(_uiDocument.rootVisualElement)
                : null;
            if (existingStage != null) {
                existingStage.style.backgroundImage = new StyleBackground();
            }
        }

        private void OnDestroy() {
            ReleaseHandle();
        }

        private void ReleaseHandle() {
            if (_handleValid && _handle.IsValid()) {
                Addressables.Release(_handle);
            }
            _handleValid = false;
        }
    }
}
