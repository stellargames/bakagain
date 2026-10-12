namespace BakAgain.Book {
    using System;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Layout;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// The Enhanced page turn (TASK-872): the page on screen lifts from its outer edge, curls over the
    /// spine and lands as the facing page, uncovering the next page beneath. Not in the original, which
    /// cuts straight to the next page (BOOKVIEW.C:171-190).
    /// </summary>
    /// <remarks>
    /// The sheet's front is a screen grab of the outgoing page; its back is the facing page the
    /// <see cref="BookSpreadBackdrop"/> draws, sampled where each point lands, so the sheet comes to
    /// rest looking exactly like what was already there. The bend is a strip of columns from the spine,
    /// each rotated a little further than the one before it, so the outer edge leads.
    ///
    /// <para>Turned by the book between its pages, and by the screen navigator between two menu
    /// screens drawn as bound pages (<see cref="TryTurnAsync"/>). From the grab until the turn starts,
    /// the grab covers the whole screen, so the next screen can lay out unseen.</para>
    /// </remarks>
    public sealed class BookPageCurl : MonoBehaviour {
        private const float Seconds = 0.55f;
        private const float MaxStep = 1 / 30f;
        private const int Segments = 48;
        // How far (radians) the outer edge runs ahead of the spine at mid-turn: the curl.
        private const float Lead = 1.5f;
        // The page grows this much per page-width of lift: a little perspective.
        private const float Lift = 0.35f;
        // The shade of a face seen edge-on; flat faces are unshaded so the turn starts and ends seamlessly.
        private const float EdgeOnShade = 0.3f;
        // The shadow the lifted sheet casts on the page beneath: its width (share of the page) and darkness at mid-turn.
        private const float ShadowWidth = 0.15f;
        private const float ShadowOpacity = 0.6f;
        // Over BookView (10) and the binding's shade (11): the lifted sheet is above the crease.
        private const int SortingOrder = 12;

        // How long the next screen gets to draw its page before the turn gives up and cuts.
        private const float NextPageTimeout = 1f;
        // A cutscene loads its resources first, so it gets longer to reach its first fade-in.
        private const float CutsceneFadeInTimeout = 1.5f;
        // A grab held by a navigator Clear is let go if no transition takes it up in this time.
        private const int HeldTimeoutMs = 2000;

        private static BookPageCurl _instance;

        private Canvas _canvas;
        private RawImage _frozen;
        private CurlSheet _front;
        private CurlSheet _back;
        private CurlSheet _shadow;
        private CurlSheet _crease;
        private Texture2D _shot;
        private Rect _stage;
        private bool _spineLeft;
        private Rect _facingUv;
        // Where the sheet comes to rest, so it lands exactly on what is drawn there: the facing page
        // sits this much lower (DIALOG.SCX's drop), under the binding's shade.
        private float _facingDrop;
        private Rect _creaseRect;
        private float _creaseOpacity;
        private static readonly Vector3[] Corners = new Vector3[4];
        private SpineSide _spine;
        private object _outgoing;
        private bool _held;
        private int _holdGeneration;
        // Set while a cutscene is being turned to: its first fade-in answers whether it lands on a page.
        private UniTaskCompletionSource<bool> _awaitingFadeIn;

        /// <summary>The one turn, made on first use and kept across scenes.</summary>
        public static BookPageCurl Instance {
            get {
                if (_instance == null) {
                    var host = new GameObject("PageCurl");
                    DontDestroyOnLoad(host);
                    _instance = host.AddComponent<BookPageCurl>();
                }
                return _instance;
            }
        }

        internal static bool Exists => _instance != null;

        /// <summary>
        /// Enhanced: if the page on screen and <paramref name="incoming"/> are both bound pages, grabs
        /// the page, runs <paramref name="change"/> under the grab and turns the page over onto the next
        /// one. False, having done nothing, when this is not a page-to-page change: the caller makes it.
        /// </summary>
        /// <remarks>
        /// Whether the incoming screen is a page is known only once it has drawn one, so a screen that
        /// shows an SCX background is a candidate (<see cref="BackgroundImageLoader"/>), and one that
        /// turns out not to be bound the same way, or not to draw in time, is cut to.
        /// </remarks>
        public static async UniTask<bool> TryTurnAsync(object incoming, Func<UniTask> change) {
            bool page = incoming is Component screen && screen.TryGetComponent(out ResourceManagement.Loaders.BackgroundImageLoader _);
            bool cutscene = incoming is CutScenes.CutSceneView;
            bool held = Exists && _instance._held;
            if (held) {
                _instance._held = false;
            }
            if (!Core.GameOptions.IsOn(Core.EnhancedFeature.PageTurn) || (!page && !cutscene)) {
                if (held) {
                    _instance.Abandon();
                }
                return false;
            }
            BookPageCurl curl = Instance;
            if (!held) {
                if (!await curl.CaptureAsync()) {
                    return false;
                }
                curl._outgoing = BookSpreadBackdrop.Instance.Surface;
            }
            object outgoing = curl._outgoing;
            if (cutscene) {
                curl._awaitingFadeIn = new UniTaskCompletionSource<bool>();
            }
            try {
                await change();
            } catch {
                curl._awaitingFadeIn = null;
                curl.Abandon();
                throw;
            }
            if (cutscene) {
                // Not awaited: the cutscene plays only once the push that showed it has returned.
                curl.TurnOntoCutsceneAsync().Forget();
                return true;
            }
            if (await curl.NextPageAsync(outgoing)) {
                await curl.PlayAsync(() => false);
            } else {
                curl.Abandon();
            }
            return true;
        }

        /// <summary>
        /// Grabs the page as it is on screen, at the end of this frame, and holds the grab over the
        /// screen until the turn. False when no open book is drawn around it: nothing to turn over.
        /// </summary>
        public async UniTask<bool> CaptureAsync() {
            BookSpreadBackdrop backdrop = BookSpreadBackdrop.Exists ? BookSpreadBackdrop.Instance : null;
            if (backdrop == null || !backdrop.Drawn || backdrop.Spine == SpineSide.NotAPage
                || backdrop.FacingImage.texture == null) {
                return false;
            }
            await UniTask.WaitForEndOfFrame(this);
            _stage = backdrop.Stage;
            _spine = backdrop.Spine;
            _spineLeft = _spine == SpineSide.Left;
            Build();
            _back.Texture = backdrop.FacingImage.texture;
            _facingUv = backdrop.FacingImage.uvRect;
            _facingDrop = ScreenRect(backdrop.FacingPage).yMin - _stage.yMin;
            var shade = backdrop.Shade.GetComponent<RawImage>();
            _creaseRect = ScreenRect(backdrop.Shade);
            _creaseOpacity = shade.enabled && shade.texture != null ? shade.color.a : 0;
            _crease.Texture = shade.texture;
            if (shade.texture != null) {
                shade.texture.wrapMode = TextureWrapMode.Clamp;   // clear beyond the band, not its edge smeared
            }
            _crease.V0 = (_stage.yMin + _facingDrop - _creaseRect.yMin) / _creaseRect.height;
            _crease.V1 = _crease.V0 + _stage.height / _creaseRect.height;
            ReleaseShot();
            // Read back now, at this frame's end: the render-texture grab is deferred, and by the time
            // it runs the next page is already drawn. ponytail: one CPU read-back per turn (~8 MB at
            // 1080p); a camera render into a texture avoids the stall if a phone ever stutters here.
            // An sRGB texture: the screen holds sRGB values, which a linear texture would show too light.
            _shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, mipChain: false, linear: false);
            _shot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, recalculateMipMaps: false);
            _shot.Apply(false);
            _front.Texture = _shot;
            (_front.V0, _front.V1) = (_stage.yMin / Screen.height, _stage.yMax / Screen.height);
            _frozen.texture = _shot;
            _frozen.enabled = true;
            _front.Columns.Clear();
            _back.Columns.Clear();
            _shadow.Columns.Clear();
            _crease.Columns.Clear();
            _canvas.enabled = true;
            return true;
        }

        /// <summary>
        /// Enhanced: grabs the page on screen and holds it while the screens are cleared, for the
        /// transition that follows to turn (New Game clears the menu before pushing the chapter's
        /// cutscene). Let go after <see cref="HeldTimeoutMs"/> if nothing takes it up.
        /// </summary>
        public static async UniTask HoldAsync() {
            if (!Core.GameOptions.IsOn(Core.EnhancedFeature.PageTurn)) {
                return;
            }
            BookPageCurl curl = Instance;
            if (curl._held || !await curl.CaptureAsync()) {
                return;
            }
            curl._outgoing = BookSpreadBackdrop.Instance.Surface;
            curl._held = true;
            curl.LetGoUnlessTakenAsync(++curl._holdGeneration).Forget();
        }

        private async UniTaskVoid LetGoUnlessTakenAsync(int generation) {
            await UniTask.Delay(HeldTimeoutMs, ignoreTimeScale: true);
            if (this != null && _held && _holdGeneration == generation) {
                _held = false;
                Abandon();
            }
        }

        /// <summary>
        /// A cutscene's fade-in, asked whether a turn is waiting for it. True: the page it fades in
        /// is bound like the grabbed one, so it is shown at once and the turn reveals it instead.
        /// False: the fade runs, after the grab has gone.
        /// </summary>
        public static bool TakeFadeIn() {
            if (!Exists || _instance._awaitingFadeIn == null) {
                return false;
            }
            UniTaskCompletionSource<bool> waiting = _instance._awaitingFadeIn;
            _instance._awaitingFadeIn = null;
            bool lands = _instance.LandsOnPage(_instance._outgoing);
            if (!lands) {
                _instance.Abandon();
            }
            waiting.TrySetResult(lands);
            return lands;
        }

        // The grab holds until the cutscene's first fade-in says whether it fades in a page.
        private async UniTaskVoid TurnOntoCutsceneAsync() {
            UniTaskCompletionSource<bool> waiting = _awaitingFadeIn;
            float waited = 0;
            while (waiting.Task.Status == UniTaskStatus.Pending && waited < CutsceneFadeInTimeout) {
                await UniTask.Yield();
                waited += Time.unscaledDeltaTime;
                // A cutscene that draws something else first is not going to be turned to.
                object surface = BookSpreadBackdrop.Instance.Surface;
                if (surface != null && !ReferenceEquals(surface, _outgoing) && !LandsOnPage(_outgoing)) {
                    break;
                }
            }
            if (waiting.Task.Status == UniTaskStatus.Pending) {
                _awaitingFadeIn = null;
                waiting.TrySetResult(false);
                Abandon();
                return;
            }
            if (await waiting.Task) {
                await PlayAsync(() => false);
            }
        }

        // The page now drawn is bound like the grabbed one and in the same place.
        private bool LandsOnPage(object outgoing) {
            BookSpreadBackdrop backdrop = BookSpreadBackdrop.Instance;
            object surface = backdrop.Surface;
            Rect stage = backdrop.Stage;
            return surface != null && !ReferenceEquals(surface, outgoing)
                && backdrop.Drawn && backdrop.Spine == _spine
                && Mathf.Abs(stage.xMin - _stage.xMin) < 1 && Mathf.Abs(stage.width - _stage.width) < 1
                && Mathf.Abs(stage.yMin - _stage.yMin) < 1 && Mathf.Abs(stage.height - _stage.height) < 1;
        }

        // The next screen has drawn its page, bound like the grabbed one and in the same place.
        private async UniTask<bool> NextPageAsync(object outgoing) {
            float waited = 0;
            while (waited < NextPageTimeout) {
                await UniTask.Yield();
                waited += Time.unscaledDeltaTime;
                BookSpreadBackdrop backdrop = BookSpreadBackdrop.Instance;
                object surface = backdrop.Surface;
                if (surface != null && !ReferenceEquals(surface, outgoing)) {
                    return LandsOnPage(outgoing);
                }
            }
            return false;
        }

        // No turn after all: take the grab down, showing the next screen as it is.
        private void Abandon() {
            _canvas.enabled = false;
            _frozen.enabled = false;
            ReleaseShot();
        }

        /// <summary>Turns the grabbed page over the spine; <paramref name="skip"/> ends it at once.</summary>
        public async UniTask PlayAsync(Func<bool> skip) {
            _canvas.enabled = true;
            _frozen.enabled = false;
            try {
                // Stepped per frame, a slow frame counting as 1/30 s at most: the frames right after the
                // grab can stall (its read-back and upload), and a clock would skip the lift entirely.
                for (float t = 0; t < 1 && !skip(); t += Mathf.Min(Time.unscaledDeltaTime, MaxStep) / Seconds) {
                    Pose(t);
                    await UniTask.Yield();
                }
            } finally {
                if (_canvas != null) {
                    Abandon();
                }
            }
        }

        private void Pose(float t) {
            float eased = t * t * (3 - 2 * t);
            float turn = Mathf.PI * eased;
            float lead = Lead * Mathf.Sin(Mathf.PI * t);
            float width = _stage.width, step = width / Segments;
            float spineX = _spineLeft ? _stage.xMin : _stage.xMax;
            float dir = _spineLeft ? 1 : -1;
            _front.Columns.Clear();
            _back.Columns.Clear();
            _crease.Columns.Clear();
            float x = 0, z = 0;
            for (int i = 0; i <= Segments; i++) {
                float s = i * step;
                bool frontUp = true;
                if (i > 0) {
                    float mid = Angle(turn, lead, s - step / 2, width);
                    x += Mathf.Cos(mid) * step;
                    z += Mathf.Sin(mid) * step;
                    frontUp = Mathf.Cos(mid) >= 0;
                }
                float angle = Angle(turn, lead, s, width);
                float grow = 1 + Lift * z / width;
                float screenX = spineX + dir * x;
                // 0 lying on the page, 1 lying on the facing side: settles to the facing page's height.
                float over = (1 - Mathf.Cos(angle)) / 2;
                float centre = _stage.center.y + _facingDrop * over;
                float bottom = centre - _stage.height / 2 * grow;
                float top = centre + _stage.height / 2 * grow;
                float shade = Mathf.Lerp(EdgeOnShade, 1, Mathf.Abs(Mathf.Cos(angle)));
                float frontU = (spineX + dir * s) / Screen.width;
                float facingLocal = _spineLeft ? 1 - s / width : s / width;
                float backU = _facingUv.x + facingLocal * _facingUv.width;
                _front.Columns.Add((screenX, bottom, top, frontU, shade, frontUp));
                _back.Columns.Add((screenX, bottom, top, backU, shade, !frontUp));
                float creaseU = (spineX - dir * s - _creaseRect.xMin) / _creaseRect.width;
                _crease.Columns.Add((screenX, bottom, top, creaseU, _creaseOpacity * over, !frontUp));
            }
            _front.SetVerticesDirty();
            _back.SetVerticesDirty();
            _crease.SetVerticesDirty();

            // Beyond the sheet's reach on the page side, fading out, strongest while the sheet stands highest.
            float reach = 0;
            foreach (var column in _front.Columns) {
                reach = Mathf.Max(reach, dir * (column.X - spineX));
            }
            float shadowWidth = ShadowWidth * width * Mathf.Sin(Mathf.PI * t);
            _shadow.Columns.Clear();
            if (reach > 0 && reach < width) {
                float far = Mathf.Min(reach + shadowWidth, width);
                float opacity = ShadowOpacity * Mathf.Sin(Mathf.PI * t);
                _shadow.Columns.Add((spineX + dir * reach, _stage.yMin, _stage.yMax, 0, opacity, true));
                _shadow.Columns.Add((spineX + dir * far, _stage.yMin, _stage.yMax, 0, 0, true));
            }
            _shadow.SetVerticesDirty();
        }

        // A point's turn about the spine, s along the page from it: the outer edge leads.
        private static float Angle(float turn, float lead, float s, float width) =>
            Mathf.Clamp(turn + lead * s / width, 0, Mathf.PI);

        private void Build() {
            if (_canvas != null) {
                return;
            }
            // On this root object: under BookView's canvas it would be a nested canvas, in that canvas's space.
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;
            _canvas.enabled = false;
            var frozenHost = new GameObject("Grab", typeof(RectTransform));
            frozenHost.transform.SetParent(_canvas.transform, false);
            _frozen = frozenHost.AddComponent<RawImage>();
            _frozen.raycastTarget = false;
            _frozen.enabled = false;
            RectTransform full = _frozen.rectTransform;
            full.anchorMin = Vector2.zero;
            full.anchorMax = Vector2.one;
            full.offsetMin = full.offsetMax = Vector2.zero;
            // Shadow under all; front before back: the back is always the outer, higher part of the sheet.
            _shadow = NewSheet("Shadow");
            _shadow.Shadow = true;
            _front = NewSheet("Front");
            _back = NewSheet("Back");
            // The binding's shade on the back as it settles: under our canvas, it would show only once the sheet is gone.
            _crease = NewSheet("Crease");
            _crease.Shadow = true;
        }

        private CurlSheet NewSheet(string name) {
            var host = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            host.transform.SetParent(_canvas.transform, false);
            var sheet = host.AddComponent<CurlSheet>();
            sheet.raycastTarget = false;
            RectTransform rect = sheet.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;   // local space = screen pixels
            rect.anchoredPosition = Vector2.zero;
            return sheet;
        }

        private static Rect ScreenRect(RectTransform rect) {
            rect.GetWorldCorners(Corners);
            return Rect.MinMaxRect(Corners[0].x, Corners[0].y, Corners[2].x, Corners[2].y);
        }

        private void ReleaseShot() {
            if (_shot != null) {
                Destroy(_shot);
                _shot = null;
            }
        }

        private void OnDestroy() => ReleaseShot();
    }
}
