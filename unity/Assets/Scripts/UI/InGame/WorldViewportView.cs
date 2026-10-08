namespace BakAgain.UI.InGame {
    using BakAgain.Core;
    using BakAgain.World;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Renders the 3D world into the REQ_MAIN viewport region: a RenderTexture fed by the world
    /// camera, shown by a VisualElement that is an inset-0 child of the REQ hotspot_192 element
    /// (so its position/size come from REQ data, not hardcoded coords). Plain C#, owned by
    /// InGameScreen. Extracted from the former InGameHud.
    /// </summary>
    public sealed class WorldViewportView {
        private readonly IWorldViewport _viewport;
        private readonly ILogger _logger;

        // *** WHO IS CURRENTLY SHOWING THE WORLD. *** There is one world camera and it renders to
        // one texture, so two views (the travel viewport and an overlay's inset) cannot both display
        // it. The last view to allocate wins and the other freezes on its last frame — which is what
        // the original does when it shrinks the render viewport. Tracked here rather than handed
        // between screens because it is a property of displaying the world, and both sides of the
        // handover live in this class.
        private static WorldViewportView _cameraOwner;

        private VisualElement _element;
        private Rect? _canonicalOverride;
        private VisualElement _panelRoot;
        private VisualElement _stage;
        private bool _loggedStageFallback;
        private RenderTexture _rt;
        private Vector2Int _rtSize;
        private Camera _camera;
        private VisualElement _host;

        /// <summary>Raised after every render-texture (re)allocation, the first one included — the
        /// camera's aspect has just followed the new texture, so a lens that depends on it is stale.</summary>
        public event System.Action RenderTextureChanged;

        public WorldViewportView(IWorldViewport viewport, ILogger logger) {
            _viewport = viewport;
            _logger = logger;
        }

        public void SetWorldCamera(Camera camera) => _camera = camera;

        /// <summary>The element the world is shown in, for anything drawn over it.</summary>
        /// <remarks>
        /// Null until <see cref="Attach"/> has found a host. A caller that adds children (the
        /// overhead map's party marker, the locator's dots) gets the viewport rect for free and
        /// cannot drift from the hole the world is rendered into.
        /// </remarks>
        public VisualElement Element => _element;

        /// <summary>Create the viewport element as an inset-0 child of hotspot_192 and allocate
        /// the RenderTexture. panelRoot is used to find hotspot_192, and to resolve the stage
        /// (lazily — see <see cref="CanonicalStage.FindOrCached"/>). The document root is what this
        /// wants; the stage resolves identically, and an element from inside it resolves loudly —
        /// see <see cref="CanonicalStage"/>'s "What callers may hand in".</summary>
        /// <param name="panelRoot">The screen's document root.</param>
        /// <param name="canonicalOverride">
        /// An explicit canonical rect to show the world in, for a screen whose world view is not the
        /// travel viewport — the locator spells shrink it to the REQ_CMAP inset
        /// (<see cref="GameData.Resources.Spells.FieldSpells.LocatorViewport"/>). Null takes the
        /// screen's own hole: ClickArea 192 if the REQ has one, else the canonical travel rect.
        /// </param>
        public void Attach(VisualElement panelRoot, Rect? canonicalOverride = null) {
            _panelRoot = panelRoot;
            _canonicalOverride = canonicalOverride;
            // An override wins outright rather than falling back: a REQ that happens to carry a
            // ClickArea 192 would otherwise silently put the world back in the travel viewport.
            bool fullWindow = canonicalOverride == null && _viewport is FullScreenViewport { Active: true };
            VisualElement host = canonicalOverride is { } inset
                ? HostAtRect(panelRoot, inset)
                : fullWindow
                    ? FullWindowHost(panelRoot)
                    : panelRoot?.Q(name: "hotspot_192") ?? CanonicalViewportHost(panelRoot);
            HideFrame(panelRoot, fullWindow);
            if (fullWindow) {
                StretchClickArea(panelRoot);
            }
            _host = host;
            if (host == null) {
                _logger.LogWarning("WorldViewportView: no host for the world view; not shown.");
                return;
            }
            _element = new VisualElement {
                name = "WorldViewport",
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };
            host.Add(_element);
            AllocateRenderTexture();
        }

        /// <summary>
        /// Rebuilds the world element in whichever host <see cref="Attach"/> picks now — Enhanced
        /// full-screen turning on or off, or a fight taking the world back into the frame.
        /// </summary>
        public void Rehost() {
            if (_panelRoot == null) {
                return;
            }
            Dispose();
            Attach(_panelRoot, _canonicalOverride);
        }

        /// <summary>Enhanced full-screen: the world fills the window, under the stage.</summary>
        /// <remarks>
        /// First child of the document root, so the stage (and every REQ widget on it) draws over it.
        /// </remarks>
        private static VisualElement FullWindowHost(VisualElement panelRoot) {
            if (panelRoot == null) {
                return null;
            }
            var host = new VisualElement {
                name = FullWindowHostName,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };
            panelRoot.Insert(0, host);

            return host;
        }

        private const string FullWindowHostName = "WorldViewportFullWindow";
        private bool _frameHidden;

        // *** THE WORLD'S CLICK AREA FOLLOWS THE WORLD. *** Clicks, right-clicks, touch long-presses
        // and hover reach the world only through the REQ's ClickArea 192 (the loader's own handlers
        // on that element dispatch to InGameScreen). Full-window, that element is stretched over
        // the whole window and sent to the back of its parent, so every point no other widget
        // covers takes the one existing path, and every REQ widget still wins over it. Restored
        // (rect and sibling index) whenever the world leaves the full window.
        private VisualElement _clickArea;
        private int _clickAreaIndex;
        private StyleLength _clickLeft, _clickTop, _clickWidth, _clickHeight;

        private void StretchClickArea(VisualElement panelRoot) {
            VisualElement area = panelRoot?.Q(name: "hotspot_192");
            if (area?.parent == null || _clickArea != null) {
                return;
            }
            _clickArea = area;
            _clickAreaIndex = area.parent.IndexOf(area);
            _clickLeft = area.style.left;
            _clickTop = area.style.top;
            _clickWidth = area.style.width;
            _clickHeight = area.style.height;
            area.SendToBack();
            FitClickArea();
        }

        // Per frame (Tick) as well as on attach: the window can resize, and before the first
        // layout pass there is nothing to measure.
        private void FitClickArea() {
            if (_clickArea?.panel == null) {
                return;
            }
            Rect parent = _clickArea.parent.worldBound;
            Rect window = _clickArea.panel.visualTree.layout;
            if (float.IsNaN(parent.x) || float.IsNaN(parent.y) || window.width <= 0f || window.height <= 0f) {
                return;
            }
            _clickArea.style.left = -parent.x;
            _clickArea.style.top = -parent.y;
            _clickArea.style.width = window.width;
            _clickArea.style.height = window.height;
        }

        private void RestoreClickArea() {
            if (_clickArea == null) {
                return;
            }
            _clickArea.style.left = _clickLeft;
            _clickArea.style.top = _clickTop;
            _clickArea.style.width = _clickWidth;
            _clickArea.style.height = _clickHeight;
            VisualElement parent = _clickArea.parent;
            if (parent != null) {
                parent.Insert(Mathf.Min(_clickAreaIndex, parent.childCount - 1), _clickArea);
            }
            _clickArea = null;
        }

        // FRAME.SCR is the stage's background image (set asynchronously by BackgroundImageLoader);
        // the tint is ours alone, so clearing it hides the frame whenever it arrives. Null restores
        // the stylesheet's tint, which is what every faithful path has always had.
        private void HideFrame(VisualElement panelRoot, bool hide) {
            // Faithful never touches the stage: only undo what full-screen did.
            if (!hide && !_frameHidden) {
                return;
            }
            _frameHidden = hide;
            VisualElement stage = CanonicalStage.FindOrCached(_stage, panelRoot);
            if (stage == null) {
                return;
            }
            _stage = stage;
            stage.style.unityBackgroundImageTintColor = hide ? new StyleColor(Color.clear) : new StyleColor(StyleKeyword.Null);
        }

        /// <summary>
        /// The host for a screen whose REQ carries no viewport element — REQ_MAP, which shows the
        /// same world through the same hole in the same chrome but has no ClickArea 192 of its own.
        /// </summary>
        /// <remarks>
        /// The rect is <see cref="IWorldViewport.CanonicalRect"/>, not a constant typed in here: the
        /// original's viewport is a global the map screen never re-points, so both screens are
        /// looking through the one rect <c>setupRenderView</c> installed. Placed on the stage so it
        /// lands in canonical space rather than against the aspect-dependent panel width.
        /// </remarks>
        private VisualElement CanonicalViewportHost(VisualElement panelRoot) {
            BakAgain.Graphics.Area rect = _viewport.CanonicalRect;

            return HostAtRect(panelRoot, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
        }

        /// <summary>Places the world-view host on the stage at a canonical rect.</summary>
        /// <remarks>
        /// On the stage rather than against the panel, so the rect lands in canonical space instead
        /// of against an aspect-dependent panel width.
        /// </remarks>
        private VisualElement HostAtRect(VisualElement panelRoot, Rect rect) {
            VisualElement stage = CanonicalStage.FindOrCached(_stage, panelRoot);
            if (stage == null) {
                return null;
            }
            _stage = stage;
            var host = new VisualElement {
                name = "WorldViewportHost",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = rect.x, top = rect.y,
                    width = rect.width, height = rect.height,
                },
            };
            stage.Add(host);

            return host;
        }

        /// <summary>Per-frame: reallocate the RT when the window size changes the cutout pixel size.</summary>
        public void Tick() {
            if (_element == null) {
                return;
            }
            FitClickArea();
            Vector2Int wanted = WantedRenderTextureSize();
            if (wanted != _rtSize) {
                AllocateRenderTexture();

                return;
            }
            // *** TAKE THE CAMERA BACK IF SOMETHING ELSE BORROWED IT. *** An overlay that shows the
            // world in its own rect (the locator spells' inset) points the one world camera at its
            // own texture, which correctly freezes this view on its last frame while it is up. When
            // it goes away it disables the camera and clears the target, and nothing else would ever
            // switch this view back on — reallocation only happens on a size change, and the window
            // has not resized. Self-healing here rather than in the overlay keeps camera ownership a
            // property of whoever is displaying the world, not a handshake between screens.
            // Only reclaim once the borrower has gone: without the ownership test this view wins
            // every frame (it ticks; an overlay does not), and the inset it borrowed the camera for
            // would show a texture nothing renders into.
            if (_cameraOwner == null && _camera != null && _rt != null
                && _camera.targetTexture != _rt) {
                _cameraOwner = this;
                _camera.targetTexture = _rt;
                _camera.enabled = true;
            }
        }

        // The stage's current on-screen rect (see CanonicalStage.ScreenRect) — pillarboxed under
        // Contain, full-window under Fill. Falls back to the canonical Contain box (logged once) if
        // the stage hasn't resolved a layout yet, rather than mapping the viewport against garbage.
        private Rect CurrentStageScreenRect() {
            Rect rect = CanonicalStage.ScreenRect(ResolveStage(), out bool isFallback);
            if (isFallback && !_loggedStageFallback) {
                _logger.LogWarning(
                    "WorldViewportView: stage not resolved (missing or pre-layout); falling back to "
                    + "the canonical Contain box for the world viewport mapping.");
                _loggedStageFallback = true;
            }
            return rect;
        }

        /// <summary>The render texture's pixel size for whichever rect the world is being shown in.</summary>
        /// <remarks>
        /// <b>An overridden rect has to size the RT too, not just place it.</b> Sizing from
        /// <see cref="IWorldViewport"/> always describes the TRAVEL viewport, so an inset would get a
        /// texture of the travel viewport's shape stretched into a differently-shaped box — the world
        /// squashed, and the camera's aspect (which follows the texture) wrong with it. Measured
        /// against the stage's own scale so it tracks window size exactly as the travel path does.
        /// </remarks>
        private Vector2Int WantedRenderTextureSize() {
            Rect stage = CurrentStageScreenRect();
            if (_canonicalOverride is not { } rect) {
                return _viewport.RenderTextureSize(stage);
            }
            float scale = stage.width / BakAgain.Graphics.Canonical.Width;

            return new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt(rect.width * scale)),
                Mathf.Max(1, Mathf.RoundToInt(rect.height * scale)));
        }

        // Re-resolved until found, never captured once in Attach: this view lives for the whole
        // travel session (Tick reallocates the RT on every window change), so a Find that ran
        // before the stage existed would pin the fallback box permanently — and AllocateRenderTexture
        // is called synchronously from Attach, before any layout. See CanonicalStage.FindOrCached.
        private VisualElement ResolveStage() => _stage = CanonicalStage.FindOrCached(_stage, _panelRoot);

        private void AllocateRenderTexture() {
            _rtSize = WantedRenderTextureSize();
            DetachRenderTexture();
            // 32-bit FLOAT depth, not the default 24-bit fixed point. The world's coplanar
            // tie-break (ClassicPolygon._PaintBias) is a per-rank NDC nudge of rank*bias/w², which
            // is sized against float depth: on a D24 fixed-point buffer one increment is 2^-24
            // (6e-8 NDC) everywhere, so the nudge (5.6e-10 at 300 u of travel distance) rounds away
            // to a hundredth of an increment and exactly-coplanar faces land in the same bucket —
            // the winner then flips with rasterisation order as the camera moves. Reversed-Z float
            // concentrates precision near the camera and keeps the nudge resolvable to fog range.
            // Symptom this fixes: fall1's two topmost river polygons z-fighting the rock beneath
            // them in game while looking correct in the close-up model viewer (2026-07-20).
            _rt = new RenderTexture(_rtSize.x, _rtSize.y,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB,
                UnityEngine.Experimental.Rendering.GraphicsFormat.D32_SFloat) { name = "WorldViewportRT" };
            _rt.Create();
            if (_camera != null) {
                _cameraOwner = this;
                _camera.targetTexture = _rt;
                // The camera's aspect is the render texture's own: the world is square (TASK-764), so
                // there is nothing to correct here. Unity derives it from the target texture.
                _camera.ResetAspect();
                // This view is now displaying the world, so let the camera render.
                _camera.enabled = true;
            } else {
                _logger.LogWarning("WorldViewportView: no world camera set; viewport will be empty.");
            }
            if (_element != null) {
                _element.style.backgroundImage = Background.FromRenderTexture(_rt);
            }
            RenderTextureChanged?.Invoke();
        }

        public void Dispose() {
            DetachRenderTexture();
            _element?.RemoveFromHierarchy();
            _element = null;
            RestoreClickArea();
            // The full-window host is ours; hotspot_192 and the stage are the REQ's and stay.
            if (_host?.name == FullWindowHostName) {
                _host.RemoveFromHierarchy();
            }
            _host = null;
        }

        private void DetachRenderTexture() {
            // The world is no longer being displayed (this view is going away, or the RT is being
            // reallocated). Stop the camera rendering: without a RenderTexture an enabled world camera
            // reverts to drawing full-screen straight to the display, bleeding the 3D world around
            // whatever Opaque screen (in-game menu's Preferences / Save / Restore / Contents) hid the
            // travel view. The camera object outlives this view (owned by WorldRuntime); the next
            // Attach re-enables it. Guarded so reallocation during travel re-enables cleanly.
            if (_camera != null && _camera.targetTexture == _rt) {
                _camera.enabled = false;
                _camera.targetTexture = null;
            }
            // Released whether or not this view held the target, so a view disposed out of order
            // cannot leave the camera owned by something that no longer exists.
            if (_cameraOwner == this) {
                _cameraOwner = null;
            }
            if (_rt != null) {
                _rt.Release();
                Object.Destroy(_rt);
                _rt = null;
            }
        }
    }
}
