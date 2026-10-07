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
    ///
    /// <para>An <see cref="BakAgain.UI.Navigation.IScreen"/>: the new-game/load flow calls <see cref="SetMarker"/> then
    /// pushes it (the chapter-description dialog renders over it as a tooltip) and pops it
    /// afterwards; the overhead map's Map button pushes the same screen.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class FullMapView : MonoBehaviour, BakAgain.UI.Navigation.IScreen {
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

        [Inject]
        public void Construct(IResourceProviderService resources,
            BakAgain.UI.InputCore.InputLayerStack inputStack, IDialogManager dialogs) {
            _resources = resources;
            _inputStack = inputStack;
            _dialogs = dialogs;
        }

        private BakAgain.UI.InputCore.InputLayerStack _inputStack;
        private IDialogManager _dialogs;
        private BakAgain.UI.InputCore.ActionLayer _layer;

        /// <summary>Right-click help on Exit — FMAP.C:207-209.</summary>
        private const int ExitHelpDialog = 0x80;

        /// <summary>
        /// The player's map takes the keyboard while it is up: Esc (scancode 1) and E (0x12) close
        /// it and every other key is read and dropped (fmap_screen_run, FMAP.C:206-216) — TASK-803.
        /// </summary>
        private void PushInputLayer() {
            PopInputLayer();
            if (_inputStack == null || _onExit == null) {
                return;
            }
            _layer = new BakAgain.UI.InputCore.ActionLayer("full-map",
                onActivate: null,
                onCancel: () => _onExit?.Invoke(),
                skipActivates: false,
                onAccelerator: c => {
                    if (GameData.Resources.World.KeyScancode.Of(c) == 0x12) {
                        _onExit?.Invoke();
                    }
                    return true;
                });
            _inputStack.Push(_layer);
        }

        private void PopInputLayer() {
            if (_layer != null) {
                _inputStack?.Remove(_layer);
                _layer = null;
            }
        }

        // ---- town names under the pointer (TASK-793) --------------------------------------------

        /// <summary>FMAP_TWN.DAT — the towns' names and positions, and the icon geometry.</summary>
        private GameData.Resources.Location.FullMapTowns _towns;

        /// <summary>The game font's glyphs in the map palette's pen 0, by character.</summary>
        private System.Collections.Generic.Dictionary<int, Sprite> _labelGlyphs;

        private RectTransform _labelRoot;
        private int _hoverTown = -1;
        private bool _hoverEnabled;

        /// <summary>
        /// Loads what the hover label needs, once: the town table, and the game font drawn in pen 0
        /// of FULLMAP.PAL — <c>fmap_screen_run</c> draws the name with text style 1 (no background
        /// fill) and ink 0 (SCREENS/FMAP.C).
        /// </summary>
        private async UniTask EnsureTownLabelsAsync() {
            if (_towns == null) {
                _towns = await _resources.LoadAssetAsync<GameData.Resources.Location.FullMapTowns>(
                    "FMAP_TWN.DAT", owner: this);
            }
            if (_labelGlyphs == null) {
                var palette = await _resources.LoadAssetAsync<GameData.Resources.Palette.PaletteResource>(
                    "FULLMAP.PAL", owner: this);
                Color ink = BakAgain.Graphics.PaletteColors.ResolvePen(palette, 0, Color.black);
                _labelGlyphs = ResourceManagement.Converters.FontGlyphConverter.ToSprites(GameFonts.GameFont, ink);
            }
            if (_labelRoot == null && backgroundImage != null) {
                var go = new GameObject("TownLabel", typeof(RectTransform));
                _labelRoot = go.GetComponent<RectTransform>();
                _labelRoot.SetParent(backgroundImage.transform, worldPositionStays: false);
                _labelRoot.anchorMin = Vector2.zero;
                _labelRoot.anchorMax = Vector2.one;
                _labelRoot.offsetMin = Vector2.zero;
                _labelRoot.offsetMax = Vector2.zero;
            }
        }

        /// <summary>
        /// The town under the pointer, re-tested every frame as the original's loop does, and its
        /// name drawn only when the town changes — <c>fmap_hit_test_cursor</c> and the label block
        /// of <c>fmap_screen_run</c> (SCREENS/FMAP.C).
        /// </summary>
        private void Update() {
            // The shared pointer seam (InputDriver), so a test or the driver's fake pointer reaches it.
            BakAgain.UI.InputCore.IPointer pointer = BakAgain.UI.InputCore.InputDriver.Pointer;
            PollExitButton(pointer);
            if (!_hoverEnabled || _towns == null || pointer == null || !pointer.CanPoint || backgroundImage == null) {
                return;
            }
            int town = -1;
            RectTransform map = backgroundImage.rectTransform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(map, pointer.ScreenPosition, null, out Vector2 local)
                && map.rect.width > 0f && map.rect.height > 0f) {
                // Canonical 1600x1200, top-left origin — the space the town table is in.
                int x = Mathf.FloorToInt((local.x - map.rect.xMin) / map.rect.width * BakAgain.Graphics.Canonical.Width);
                int y = Mathf.FloorToInt((map.rect.yMax - local.y) / map.rect.height * BakAgain.Graphics.Canonical.Height);
                town = _towns.TownAt(x, y);
            }
            if (town != _hoverTown) {
                _hoverTown = town;
                DrawTownLabel(town);
            }
        }

        /// <summary>
        /// Exit, polled like the town names: this canvas has no uGUI EventSystem, so the Button's
        /// own onClick never fires — a click on Exit did nothing and the player's map could only be
        /// left by keyboard (TASK-803). Left closes; right is the button's help (FMAP.C:206-210).
        /// </summary>
        private void PollExitButton(BakAgain.UI.InputCore.IPointer pointer) {
            if (_onExit == null || _exitButton == null || !_exitButton.gameObject.activeInHierarchy
                || pointer == null || !pointer.CanPoint
                || !RectTransformUtility.RectangleContainsScreenPoint(
                    _exitButton.rectTransform, pointer.ScreenPosition, null)) {
                return;
            }
            if (pointer.Primary.ReleasedThisFrame) {
                _onExit.Invoke();
            } else if (pointer.Secondary.ReleasedThisFrame) {
                _dialogs?.ShowById(ExitHelpDialog).Forget();
            }
        }

        /// <summary>The town's name in the game font's own pixels, placed in fractions of the map.</summary>
        private void DrawTownLabel(int town) {
            if (_labelRoot == null) {
                return;
            }
            for (int i = _labelRoot.childCount - 1; i >= 0; i--) {
                Destroy(_labelRoot.GetChild(i).gameObject);
            }
            if (town < 0 || _labelGlyphs == null) {
                return;
            }
            GameData.Resources.Font.FontResource font = GameFonts.GameFont;
            string name = _towns.Towns[town].Name;
            float pixelW = (float)font.PixelWidth, pixelH = (float)font.PixelHeight;
            int labelHeight = Mathf.RoundToInt((font.Height + 1) * pixelH);
            (int centreX, int top) = _towns.LabelPlacement(town, labelHeight);
            float width = 0f;
            foreach (char c in name) {
                width += (font.GlyphFor(c)?.Width ?? 0) * pixelW;
            }
            float x = centreX - width / 2f;
            const float w = BakAgain.Graphics.Canonical.Width, h = BakAgain.Graphics.Canonical.Height;
            foreach (char c in name) {
                GameData.Resources.Font.FontGlyph glyph = font.GlyphFor(c);
                float advance = (glyph?.Width ?? 0) * pixelW;
                if (glyph != null && _labelGlyphs.TryGetValue(c, out Sprite sprite) && sprite != null) {
                    var go = new GameObject(c.ToString(), typeof(RectTransform), typeof(Image));
                    var rt = go.GetComponent<RectTransform>();
                    rt.SetParent(_labelRoot, worldPositionStays: false);
                    float glyphH = glyph.Rows.Count * pixelH;
                    rt.anchorMin = new Vector2(x / w, 1f - (top + glyphH) / h);
                    rt.anchorMax = new Vector2((x + advance) / w, 1f - top / h);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    Image image = go.GetComponent<Image>();
                    image.sprite = sprite;
                    image.raycastTarget = false;
                }
                x += advance;
            }
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

        /// <summary>
        /// Set the party marker before the screen is pushed. When <paramref name="showMarker"/> is
        /// true the party icon (<paramref name="iconIndex"/> into <c>fmap_icn.bmx</c>) is placed at
        /// <paramref name="xPercent"/>/<paramref name="yPercent"/> of the map (0..100, from the
        /// top-left) — resolution-independent. When false the map shows without a marker (chapter 8).
        /// </summary>
        public void SetMarker(bool showMarker, float xPercent, float yPercent, int iconIndex) {
            _markerVisible = showMarker;
            _markerXPercent = xPercent;
            _markerYPercent = yPercent;
            _markerIconIndex = iconIndex;
        }

        private System.Action _onExit;
        private Image _exitButton;

        /// <summary>
        /// How the player leaves this map, or <c>null</c> when they cannot.
        /// </summary>
        /// <remarks>
        /// <b>The same view serves two opposite contracts.</b> As a loading screen it dismisses
        /// itself and must offer no way out; opened from the map screen it is an ordinary screen
        /// the player leaves through REQ_FMAP's one widget. Passing null in the first case and a
        /// pop in the second is what keeps them apart — and having neither is what made the
        /// player-opened map a dead end, with no button, no key and no click able to close it.
        ///
        /// <para>Set before pushing, like <see cref="SetMarker"/> — the typed pre-push setter
        /// pattern. Both callers set it explicitly, because it persists between shows.</para>
        /// </remarks>
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
            // Clicks are polled in PollExitButton; there is no EventSystem for onClick (TASK-803).
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
            // because the flow behind it dismisses it when the world is ready. The town names
            // belong to the same interactive loop (fmap_screen_run), so they come with it.
            if (_onExit != null) {
                await EnsureExitButtonAsync();
                await EnsureTownLabelsAsync();
            }
            _hoverTown = -1;
            DrawTownLabel(-1);
            _hoverEnabled = _onExit != null;
            PushInputLayer();
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
            _hoverEnabled = false;
            PopInputLayer();
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
        }
    }
}
