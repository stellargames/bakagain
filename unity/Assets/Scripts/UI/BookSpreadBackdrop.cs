namespace BakAgain.UI {
    using BakAgain.ResourceManagement.Models;
    using GameData.Resources.Layout;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.UI;
    using UnityEngine.UIElements;

    /// <summary>
    /// The open book around a full-screen parchment (spec 2026-10-11-book-spread-pillars-design.md in
    /// the private workspace): a table in the pillars, the facing page across the parchment's straight
    /// edge, and the binding's shade over both pages. Not in the original, which filled the screen;
    /// drawn in both modes (owner, 2026-10-11).
    /// </summary>
    /// <remarks>
    /// <para><b>Shown by whatever draws the page, for as long as that surface is on screen.</b> Each
    /// owner calls <see cref="Show(VisualElement, Texture2D)"/> with the element (or uGUI image) the
    /// page is drawn on. An entry is dropped as soon as its surface leaves the screen, so a screen
    /// closed by any route cannot leave the book over the world. The latest live surface wins, and the
    /// one below comes back when it goes.</para>
    ///
    /// <para><b>The spine is read from the page</b> (<see cref="PageSpine"/>): straight edge = bound
    /// edge. Two straight edges is a picture, which draws nothing and still hides any page below it.</para>
    /// </remarks>
    public sealed class BookSpreadBackdrop : MonoBehaviour {
        // Under every UI surface (the shared panel and the cutscene canvas are 0), so the table and the
        // facing page show only in the pillars and through the page's torn notches.
        private const int BackSortingOrder = -100;

        // Over BookView's canvas (10), the highest surface a page is drawn on: the shade lies ON the page.
        private const int FrontSortingOrder = 11;

        private static readonly BookSpreadLayout Layout = new();
        // ponytail: keyed by texture and never pruned — a few bytes per parchment ever shown; prune on
        // texture destroy if mods ever stream hundreds of pages.
        private static readonly Dictionary<Texture2D, SpineSide> Spines = new();
        // ponytail: one GPU-only copy per distinct page ever shown (a handful of SCX screens, ~8 MB each
        // at 1600x1200); drop them on a screen change if a mod ships dozens of page images.
        private static readonly Dictionary<Texture2D, Texture2D> Untorn = new();
        private static BookSpreadBackdrop _instance;
        private static readonly Vector3[] Corners = new Vector3[4];   // GetWorldCorners' out array, reused every frame

        private readonly List<(object Surface, Texture2D Page, Texture2D Facing)> _shown = new();
        private Canvas _back;
        private Canvas _front;
        private RawImage _table;
        private RawImage _facing;
        private RawImage _shade;
        private RawImage _sheetShadow;
        private Vector2Int _sheetShadowSize;
        private Texture2D _dialogPage;
        private (Rect Stage, Texture2D Page, Texture2D Facing, int Width, int Height) _placed;

        /// <summary>
        /// A loose sheet lying on the table, not a page in a book (the full map): the table around it
        /// and its shadow, no facing page, no binding. Never turned like a page.
        /// </summary>
        public static void ShowSheet(Graphic surface) {
            if (surface == null) {
                return;
            }
            BookSpreadBackdrop backdrop = Instance;
            backdrop._shown.RemoveAll(e => e.Surface == (object)surface);
            backdrop._shown.Add((surface, null, null));
            backdrop.Refresh();
        }

        /// <summary>Show the book around <paramref name="page"/>, drawn on a UI Toolkit stage.</summary>
        public static void Show(VisualElement surface, Texture2D page) => ShowCore(surface, page);

        /// <summary>Show the book around <paramref name="page"/>, drawn on a uGUI image.</summary>
        /// <param name="facingPage">
        /// The page across the binding, for a book whose pages are not cut like DIALOG.SCX (the book's
        /// own parchment, 1280x960, torn 0-9 px from its edges against DIALOG.SCX's 12-18). Drawn
        /// mirrored and level, so the two pages' tops and bottoms line up. Null: DIALOG.SCX, a little lower.
        /// </param>
        public static void Show(Graphic surface, Texture2D page, Texture2D facingPage = null) =>
            ShowCore(surface, page, facingPage);

        /// <summary>A cutscene screen: its pixels, whichever form it was loaded in.</summary>
        public static void Show(Graphic surface, IndexedTexture page) =>
            ShowCore(surface, page == null ? null : page.DirectTexture ? page.DirectTexture : page.IndexTexture);

        /// <summary>
        /// <paramref name="page"/> with its tears see-through, for an owner that draws it on a screen of
        /// its own: the tears are the palette's black, opaque in the loaded image, and the table belongs
        /// in them. Only black joined to the border is cleared (<see cref="PageSpine.TornAway"/>), so
        /// ink stays. Anything that is not a page comes back unchanged.
        /// </summary>
        /// <remarks>
        /// <b>Only for a screen with nothing live beneath it but the table</b> (the navigator's REQ
        /// screens, credits). A dialog's parchment lies over the travel screen and a book over the
        /// cutscene that opened it, so their tears would show those instead; they keep their black.
        /// </remarks>
        public static Texture2D WithTearsOpen(Texture2D page) => page == null ? null : PageToDraw(page);

        internal static bool Exists => _instance != null;

        internal static BookSpreadBackdrop Instance {
            get {
                if (_instance == null) {
                    var host = new GameObject(nameof(BookSpreadBackdrop));
                    DontDestroyOnLoad(host);
                    _instance = host.AddComponent<BookSpreadBackdrop>();
                }
                return _instance;
            }
        }

        /// <summary>The spine of the page the book is drawn around; <see cref="SpineSide.NotAPage"/> when nothing is drawn.</summary>
        internal SpineSide Spine { get; private set; }

        internal bool Drawn => _back.enabled;
        internal RectTransform FacingPage => _facing.rectTransform;
        internal RawImage FacingImage => _facing;
        /// <summary>The latest live surface a page is drawn on, a book or not; null when none is.</summary>
        internal object Surface { get; private set; }
        /// <summary>Where the page is, in screen pixels, while <see cref="Drawn"/>.</summary>
        internal Rect Stage { get; private set; }
        internal RectTransform Shade => _shade.rectTransform;
        internal RawImage SheetShadow => _sheetShadow;

        private static void ShowCore(object surface, Texture2D page, Texture2D facing = null) {
            if (surface == null || page == null) {
                return;
            }
            BookSpreadBackdrop backdrop = Instance;
            backdrop._shown.RemoveAll(e => e.Surface == surface);
            backdrop._shown.Add((surface, page, facing));
            backdrop.Refresh();
        }

        private static Texture2D PageToDraw(Texture2D page) {
            if (SpineOf(page) == SpineSide.NotAPage) {
                return page;
            }
            if (!Untorn.TryGetValue(page, out Texture2D copy)) {
                Color32[] pixels = page.GetPixels32();
                var dark = new bool[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) {
                    dark[i] = !IsPaper(pixels[i]);
                }
                bool[] torn = PageSpine.TornAway(dark, page.width, page.height);
                for (int i = 0; i < pixels.Length; i++) {
                    if (torn[i]) {
                        pixels[i].a = 0;
                    }
                }
                copy = new Texture2D(page.width, page.height, TextureFormat.RGBA32, false) {
                    name = page.name, filterMode = page.filterMode, wrapMode = page.wrapMode,
                };
                copy.SetPixels32(pixels);
                copy.Apply(false, makeNoLongerReadable: true);
                Untorn[page] = copy;
            }
            return copy;
        }

        private void Awake() {
            _back = NewCanvas("Back", BackSortingOrder);
            _table = NewImage(_back, null);
            _sheetShadow = NewImage(_back, null);
            _facing = NewImage(_back, null);
            _front = NewCanvas("Front", FrontSortingOrder);
            _shade = NewImage(_front, null);
            _back.enabled = _front.enabled = false;
            // Async, as the facing page below: Awake runs on the first Show, usually inside another load's callback.
            LoadInto(_table, "BookSpread/table", TextureWrapMode.Mirror);   // alternate copies mirrored: no seam
            LoadInto(_shade, "BookSpread/crease", TextureWrapMode.Clamp);   // the page turn samples past the band: clear there, not smeared
        }

        private void LoadInto(RawImage image, string address, TextureWrapMode wrap) {
            Addressables.LoadAssetAsync<Texture2D>(address).Completed += loaded => {
                if (this == null || loaded.Result == null) {
                    return;
                }
                loaded.Result.wrapMode = wrap;
                image.texture = loaded.Result;
                image.enabled = true;
                image.gameObject.name = loaded.Result.name;
                _placed = default;   // re-place: the table's tiling depends on its size
            };
        }

        // The plain parchment every dialog is drawn on is the facing page: no page art is shipped.
        // Loaded in Start, not Awake: the first Show usually comes from inside another load's Completed
        // callback (BackgroundImageLoader), and the sprite converter's own WaitForCompletion throws
        // there ("Reentering the Update method is not allowed").
        private void Start() {
            Addressables.LoadAssetAsync<Sprite>(GameData.Resources.Dialog.DialogBackdrop.Resource).Completed += loaded => {
                if (this == null || loaded.Result == null) {
                    return;
                }
                _dialogPage = loaded.Result.texture;
                _placed = default;   // re-place: the facing page has arrived
            };
        }

        private void LateUpdate() => Refresh();

        private void Refresh() {
            _shown.RemoveAll(e => !IsOnScreen(e.Surface));
            (object surface, Texture2D page, Texture2D ownFacing) = _shown.Count > 0 ? _shown[_shown.Count - 1] : (null, null, null);
            Surface = surface;
            _sheetShadow.enabled = false;
            if (surface != null && page == null) {
                ShowAsSheet(surface);
                return;
            }
            Spine = page != null ? SpineOf(page) : SpineSide.NotAPage;
            if (Spine == SpineSide.NotAPage || !TryStageRect(surface, out Rect stage)) {
                Spine = SpineSide.NotAPage;
                _back.enabled = _front.enabled = false;
                return;
            }
            Stage = stage;
            Texture2D facing = ownFacing != null ? ownFacing : _dialogPage;
            _back.enabled = true;
            _front.enabled = _shade.texture != null;
            _facing.enabled = facing != null;
            if (_placed != (stage, page, facing, Screen.width, Screen.height)) {
                _placed = (stage, page, facing, Screen.width, Screen.height);
                Place(stage, facing, drop: ownFacing != null ? 0f : Layout.FacingPageDrop);
            }
        }

        private void Place(Rect stage, Texture2D facing, float drop) {
            SetRect(_table, new Rect(0, 0, Screen.width, Screen.height));
            if (_table.texture != null) {
                float tileWidth = Screen.height * (float)_table.texture.width / _table.texture.height;
                _table.uvRect = new Rect(0, 0, Screen.width / tileWidth, 1);
            }
            bool left = Spine == SpineSide.Left;
            float spineX = left ? stage.xMin : stage.xMax;
            SetRect(_facing, new Rect(left ? stage.xMin - stage.width : stage.xMax,
                stage.yMin - drop * stage.height, stage.width, stage.height));
            if (facing != null) {
                // Its tears open onto the table, which is on the same canvas.
                _facing.texture = PageToDraw(facing);
                // Its straight edge goes against the binding: mirrored when it is bound on the same side as the shown page.
                bool mirror = SpineOf(facing) == Spine;
                _facing.uvRect = mirror ? new Rect(1, 0, -1, 1) : new Rect(0, 0, 1, 1);
            }
            float shadeWidth = Layout.ShadeWidth * stage.height;
            SetRect(_shade, new Rect(spineX - shadeWidth / 2, stage.yMin, shadeWidth, stage.height));
            _shade.color = new Color(1, 1, 1, Layout.ShadeOpacity);
        }

        // The table, and the sheet's shadow on it. Not a page: no spine, so nothing turns it.
        private void ShowAsSheet(object surface) {
            Spine = SpineSide.NotAPage;
            _front.enabled = false;
            if (!TryStageRect(surface, out Rect stage)) {
                _back.enabled = false;
                return;
            }
            Stage = stage;
            _back.enabled = true;
            _facing.enabled = false;
            _sheetShadow.enabled = true;
            if (_placed == (stage, null, null, Screen.width, Screen.height)) {
                return;
            }
            _placed = (stage, null, null, Screen.width, Screen.height);
            Place(stage, facing: null, drop: 0);
            float reach = Layout.SheetShadowWidth * stage.height;
            var area = new Rect(stage.xMin - reach, stage.yMin - reach - Layout.SheetShadowDrop * stage.height,
                stage.width + 2 * reach, stage.height + 2 * reach);
            SetRect(_sheetShadow, area);
            var size = new Vector2Int(256, Mathf.Max(8, Mathf.RoundToInt(256 * area.height / area.width)));
            if (size != _sheetShadowSize) {
                _sheetShadowSize = size;
                if (_sheetShadow.texture != null) {
                    Destroy(_sheetShadow.texture);
                }
                _sheetShadow.texture = SheetShadowTexture(size, reach * size.x / area.width, Layout.SheetShadowOpacity);
            }
        }

        // A rectangle's soft shadow: opaque inside, fading to nothing over `reach` texels outside it,
        // round at the corners.
        private static Texture2D SheetShadowTexture(Vector2Int size, float reach, float opacity) {
            var pixels = new Color32[size.x * size.y];
            for (int y = 0; y < size.y; y++) {
                float dy = Mathf.Max(0, Mathf.Max(reach - y, y - (size.y - 1 - reach)));
                for (int x = 0; x < size.x; x++) {
                    float dx = Mathf.Max(0, Mathf.Max(reach - x, x - (size.x - 1 - reach)));
                    float fade = 1 - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / reach);
                    pixels[y * size.x + x] = new Color32(0, 0, 0, (byte)(255 * opacity * fade * fade));
                }
            }
            var texture = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false) {
                name = "SheetShadow", wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, makeNoLongerReadable: true);
            return texture;
        }

        private static bool IsOnScreen(object surface) => surface switch {
            VisualElement v => v.panel != null && v.resolvedStyle.display != DisplayStyle.None,
            Graphic g => g != null && g.isActiveAndEnabled && g.canvas != null && g.canvas.isActiveAndEnabled,
            _ => false,
        };

        // The page's rectangle in screen pixels, bottom-left origin (the overlay canvases' space).
        private static bool TryStageRect(object surface, out Rect stage) {
            if (surface is Graphic g) {
                g.rectTransform.GetWorldCorners(Corners);
                Vector3[] corners = Corners;
                stage = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
            } else {
                var v = (VisualElement)surface;
                float panelHeight = v.panel.visualTree.layout.height;
                Rect w = v.worldBound;
                float scale = Screen.height / panelHeight;
                stage = new Rect(w.x * scale, Screen.height - w.yMax * scale, w.width * scale, w.height * scale);
            }
            return stage.width > 0 && stage.height > 0 && !float.IsNaN(stage.x) && !float.IsNaN(stage.y);
        }

        private static SpineSide SpineOf(Texture2D page) {
            if (!Spines.TryGetValue(page, out SpineSide spine)) {
                Spines[page] = spine = page.isReadable
                    ? PageSpine.Of(Opaque(page, 0), Opaque(page, page.width - 1))
                    : SpineSide.NotAPage;
            }
            return spine;
        }

        // Paper, row by row, down one column. A torn-away pixel is palette index 0: transparent in an
        // indexed cutscene texture, opaque exact black in a loaded sprite (measured: BLANK.SCX and
        // BOOK.SCX load their tears as 0,0,0). Only EXACT black: the travel frame's edge is a dark
        // brown, and a "dark" threshold read it as torn.
        private static bool[] Opaque(Texture2D page, int x) {
            Color[] column = page.GetPixels(x, 0, 1, page.height);
            var paper = new bool[column.Length];
            for (int y = 0; y < column.Length; y++) {
                paper[y] = IsPaper(column[y]);
            }
            return paper;
        }

        private static bool IsPaper(Color c) => c.a >= 0.5f && (c.r > 0f || c.g > 0f || c.b > 0f);

        private Canvas NewCanvas(string name, int sortingOrder) {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            var canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            return canvas;
        }

        private static RawImage NewImage(Canvas canvas, Texture texture) {
            var host = new GameObject(texture != null ? texture.name : "missing");
            host.transform.SetParent(canvas.transform, false);
            var image = host.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            image.enabled = texture != null;   // a RawImage with no texture draws white
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            return image;
        }

        private static void SetRect(Graphic graphic, Rect rect) {
            graphic.rectTransform.anchoredPosition = rect.position;
            graphic.rectTransform.sizeDelta = rect.size;
        }
    }
}
