namespace BakAgain.UI.FullMap {
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using System;
    using UnityEngine;
    using UnityEngine.UI;
    using VContainer;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// World-map "where are we now?" overlay shown between chapters. Renders
    /// FULLMAP.SCX aspect-fitted (no stretch — letter/pillar-boxed against black for
    /// non-matching screen ratios) and places the party icon from <c>fmap_icn.bmx</c>
    /// at the party's overworld position as a fraction of the displayed map.
    ///
    /// In the DOS engine this screen doubled as a load mask — STARTUP.GAM
    /// parsing was slow enough that the player naturally saw the map for ~1-2s.
    /// Modern hydration is near-instant, so <see cref="Show"/> enforces
    /// <see cref="minHoldSeconds"/> before returning to keep the pacing.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class FullMapView : MonoBehaviour, IFullMapView {
        [SerializeField]
        private Image backgroundImage;

        [SerializeField]
        private Image partyIcon;

        [Tooltip("Seconds for the fade-in transition.")]
        [SerializeField]
        private float fadeInSeconds = 0.5f;

        [Tooltip("Seconds for the fade-out transition.")]
        [SerializeField]
        private float fadeOutSeconds = 0.5f;

        [Tooltip("Minimum total time the map stays fully visible after fade-in completes, " +
                 "before Show() returns. The DOS engine spent this time loading; we don't, " +
                 "so we hold it deliberately to preserve pacing.")]
        [SerializeField]
        private float minHoldSeconds = 1.5f;

        private CanvasGroup _canvasGroup;
        private IResourceProviderService _resources;
        private ILogger _logger;
        private Sprite _backgroundSprite;
        private int _cachedIconIndex = -1;
        private AspectRatioFitter _backgroundFitter;
        private Image _letterboxBacking;
        private bool _layoutConfigured;
        private GameViewportRegistry _viewportRegistry;

        [Inject]
        public void Construct(IResourceProviderService resources, GameViewportRegistry viewportRegistry) {
            _resources = resources;
            _viewportRegistry = viewportRegistry;
        }

        // Screen-space rect of the displayed (aspect-fitted, possibly letter-boxed) map.
        // Registered with the GameViewportRegistry while the map is up so UI Toolkit overlays
        // — the chapter-description dialog shown on top — lay out against the map area rather
        // than the whole window, the same way CutSceneView anchors dialogs to its letterbox.
        private Rect GetMapScreenRect() {
            if (backgroundImage == null) {
                return new Rect(0f, 0f, Screen.width, Screen.height);
            }
            var corners = new Vector3[4];
            backgroundImage.rectTransform.GetWorldCorners(corners);
            float xMin = Mathf.Min(corners[0].x, corners[2].x);
            float yMin = Mathf.Min(corners[0].y, corners[2].y);
            float xMax = Mathf.Max(corners[0].x, corners[2].x);
            float yMax = Mathf.Max(corners[0].y, corners[2].y);
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private void Awake() {
            _canvasGroup = GetComponent<CanvasGroup>();
            _logger = LogManager.LoggerFactory.CreateLogger<FullMapView>();
            _canvasGroup.alpha = 0f;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// One-time runtime setup so the prefab doesn't have to encode any of it: a black
        /// full-screen backing for the letter/pillar bars, an <see cref="AspectRatioFitter"/>
        /// on the map so it fits without stretching, and re-parenting the party icon under the
        /// map so its percentage anchors track the (possibly letter-boxed) displayed map rect
        /// rather than the whole screen.
        /// </summary>
        private void EnsureLayout() {
            if (_layoutConfigured) {
                return;
            }
            _layoutConfigured = true;

            // Black backing behind everything, fills the screen → shows through the bars.
            var backingGo = new GameObject("LetterboxBacking", typeof(RectTransform), typeof(Image));
            var backingRt = backingGo.GetComponent<RectTransform>();
            backingRt.SetParent(transform, worldPositionStays: false);
            backingRt.anchorMin = Vector2.zero;
            backingRt.anchorMax = Vector2.one;
            backingRt.offsetMin = Vector2.zero;
            backingRt.offsetMax = Vector2.zero;
            backingRt.SetAsFirstSibling(); // draw behind the map + icon
            _letterboxBacking = backingGo.GetComponent<Image>();
            _letterboxBacking.color = Color.black;
            _letterboxBacking.raycastTarget = false;

            if (backgroundImage != null) {
                _backgroundFitter = backgroundImage.GetComponent<AspectRatioFitter>();
                if (_backgroundFitter == null) {
                    _backgroundFitter = backgroundImage.gameObject.AddComponent<AspectRatioFitter>();
                }
                _backgroundFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                backgroundImage.preserveAspect = false; // the rect already matches the sprite aspect

                // Anchor the icon to the map rect, not the screen, so letter-boxing can't
                // shift it off the map.
                if (partyIcon != null && partyIcon.transform.parent != backgroundImage.transform) {
                    partyIcon.transform.SetParent(backgroundImage.transform, worldPositionStays: false);
                }
            }
        }

        /// <summary>
        /// Positions and sizes the party icon on the displayed map. Position is
        /// (<paramref name="xPercent"/>, <paramref name="yPercent"/>) of the map (Y measured
        /// from the top). Size is the icon sprite's dimensions as a fraction of the map sprite's
        /// — both are square-pixel-corrected, so this reproduces the original on-map size with no
        /// DOS constants and scales the icon together with the (letter-boxed) map.
        /// </summary>
        private void PlaceIcon(float xPercent, float yPercent, Sprite iconSprite) {
            RectTransform rt = partyIcon.rectTransform;
            var centre = new Vector2(xPercent / 100f, 1f - yPercent / 100f);

            float halfW = 0f;
            float halfH = 0f;
            if (iconSprite != null && _backgroundSprite != null
                && _backgroundSprite.rect.width > 0f && _backgroundSprite.rect.height > 0f) {
                halfW = 0.5f * iconSprite.rect.width / _backgroundSprite.rect.width;
                halfH = 0.5f * iconSprite.rect.height / _backgroundSprite.rect.height;
            }

            rt.anchorMin = new Vector2(centre.x - halfW, centre.y - halfH);
            rt.anchorMax = new Vector2(centre.x + halfW, centre.y + halfH);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // Marker args, set via SetMarker before the screen is pushed (the typed pre-push
        // setter pattern — navigation design doc §3.2).
        private bool _markerVisible;
        private float _markerXPercent;
        private float _markerYPercent;
        private int _markerIconIndex;

        public void SetMarker(bool showMarker, float xPercent, float yPercent, int iconIndex) {
            _markerVisible = showMarker;
            _markerXPercent = xPercent;
            _markerYPercent = yPercent;
            _markerIconIndex = iconIndex;
        }

        private System.Action _onExit;
        private Image _exitButton;

        /// <inheritdoc/>
        public void SetExitAffordance(System.Action onExit) => _onExit = onExit;

        /// <summary>REQ_FMAP, which describes the one widget this screen has.</summary>
        private const string ExitLayoutAddress = "REQ_FMAP.DAT";

        /// <summary>
        /// Builds the Exit button the player leaves by, from REQ_FMAP rather than from constants.
        /// </summary>
        /// <remarks>
        /// The file holds exactly one entry — an ImageButton at (1385,1074,165x108) of the
        /// 1600x1200 frame, IconBase 119 — so its position, size and face all come from the data
        /// and nothing here needs to know a coordinate. Anchored under the map image in fractions
        /// of it, the same way <see cref="PlaceIcon"/> anchors the party marker, so letter-boxing
        /// moves the button with the map instead of leaving it on the bars.
        /// </remarks>
        private async UniTask EnsureExitButtonAsync() {
            if (_exitButton != null || backgroundImage == null) {
                return;
            }

            var layout = await _resources.LoadAssetAsync<GameData.Resources.Menu.UserInterface>(
                ExitLayoutAddress, owner: this);
            GameData.Resources.Menu.UiElement entry =
                layout?.MenuEntries is { Length: > 0 } entries ? entries[0] : null;
            if (entry == null || layout.Width <= 0 || layout.Height <= 0) {
                _logger.LogError("FullMap: {Layout} has no widget; the map would have no way out.",
                    ExitLayoutAddress);

                return;
            }

            var go = new GameObject("FullMapExit", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(backgroundImage.transform, worldPositionStays: false);
            // REQ coordinates are top-left origin; RectTransform anchors are bottom-left.
            float left = (float)entry.XPosition / layout.Width;
            float right = (float)(entry.XPosition + entry.Width) / layout.Width;
            float top = (float)entry.YPosition / layout.Height;
            float bottom = (float)(entry.YPosition + entry.Height) / layout.Height;
            rt.anchorMin = new Vector2(left, 1f - bottom);
            rt.anchorMax = new Vector2(right, 1f - top);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _exitButton = go.GetComponent<Image>();
            _exitButton.sprite = await _resources.LoadAssetAsync<Sprite>(
                GameData.Resources.Menu.UiElement.IconKeyForCombined(entry.IconBase), owner: this);
            _exitButton.raycastTarget = true;
            go.GetComponent<Button>().onClick.AddListener(() => _onExit?.Invoke());
        }

        // IScreen: shown/hidden only by the ScreenNavigator.
        public UniTask ShowAsync() => Show(_markerVisible, _markerXPercent, _markerYPercent, _markerIconIndex);

        public UniTask HideAsync() => Hide();

        private async UniTask Show(bool showMarker, float xPercent, float yPercent, int iconIndex) {
            _logger.LogInformation(
                "FullMap show: marker={ShowMarker} icon={Icon} at ({X}%,{Y}%)",
                showMarker, iconIndex, xPercent, yPercent);

            gameObject.SetActive(true);
            _canvasGroup.alpha = 0f;

            EnsureLayout();

            // Overlays (the chapter-description dialog) anchor to the displayed map, not the
            // whole window. Provider is lazy, so it reads the fitted rect once layout settles.
            _viewportRegistry?.SetProvider(GetMapScreenRect);

            if (_backgroundSprite == null) {
                _backgroundSprite = await _resources.LoadAssetAsync<Sprite>("FULLMAP.SCX", owner: this);
            }
            if (backgroundImage != null) {
                backgroundImage.sprite = _backgroundSprite;
                // Fit the map without stretching, using the corrected asset's own aspect ratio
                // (no DOS aspect baked in). FitInParent centres it and adds the letter/pillar bars.
                if (_backgroundFitter != null && _backgroundSprite != null && _backgroundSprite.rect.height > 0f) {
                    _backgroundFitter.aspectRatio = _backgroundSprite.rect.width / _backgroundSprite.rect.height;
                }
            }

            if (partyIcon != null) {
                partyIcon.gameObject.SetActive(showMarker);
                if (showMarker) {
                    Sprite iconSprite = partyIcon.sprite;
                    if (iconIndex != _cachedIconIndex) {
                        string iconKey = $"fmap_icn.bmx#{iconIndex}";
                        iconSprite = await _resources.LoadAssetAsync<Sprite>(iconKey, owner: this);
                        partyIcon.sprite = iconSprite;
                        _cachedIconIndex = iconIndex;
                    }
                    PlaceIcon(xPercent, yPercent, iconSprite);
                }
            }

            // Only the player-opened map gets a way out; as a loading screen this must offer none,
            // because the flow behind it dismisses it when the world is ready.
            if (_onExit != null) {
                await EnsureExitButtonAsync();
            }
            if (_exitButton != null) {
                _exitButton.gameObject.SetActive(_onExit != null);
            }

            float t0 = Time.unscaledTime;
            await FadeAsync(0f, 1f, fadeInSeconds);

            float elapsed = Time.unscaledTime - t0;
            if (elapsed < minHoldSeconds) {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(minHoldSeconds - elapsed),
                    DelayType.UnscaledDeltaTime);
            }
        }

        private async UniTask Hide() {
            // Stop providing the map viewport before fading — once we're hiding, overlays
            // should fall back to the default (full-window) rect rather than a vanishing map.
            _viewportRegistry?.SetProvider(null);
            if (!gameObject.activeSelf) {
                return;
            }
            await FadeAsync(_canvasGroup.alpha, 0f, fadeOutSeconds);
            gameObject.SetActive(false);
        }

        private async UniTask FadeAsync(float from, float to, float duration) {
            if (duration <= 0f) {
                _canvasGroup.alpha = to;
                return;
            }
            _canvasGroup.alpha = from;
            float elapsed = 0f;
            while (elapsed < duration) {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / duration);
                _canvasGroup.alpha = Mathf.Lerp(from, to, k);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            _canvasGroup.alpha = to;
        }

        private void OnDestroy() {
            _resources?.ReleaseAssets(this);
            _viewportRegistry?.SetProvider(null);
        }
    }
}
