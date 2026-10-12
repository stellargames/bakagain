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

        private static readonly Vector3[] Corners = new Vector3[4];

        private Canvas _canvas;
        private CurlSheet _front;
        private CurlSheet _back;
        private CurlSheet _shadow;
        private Texture2D _shot;
        private Rect _stage;
        private bool _spineLeft;
        private Rect _facingUv;

        /// <summary>
        /// Grabs the page as it is on screen, at the end of this frame. False when no open book is
        /// drawn around it, so there is nothing to turn over.
        /// </summary>
        public async UniTask<bool> CaptureAsync(Graphic page) {
            BookSpreadBackdrop backdrop = BookSpreadBackdrop.Exists ? BookSpreadBackdrop.Instance : null;
            if (backdrop == null || !backdrop.Drawn || backdrop.Spine == SpineSide.NotAPage
                || backdrop.FacingImage.texture == null) {
                return false;
            }
            await UniTask.WaitForEndOfFrame(this);
            page.rectTransform.GetWorldCorners(Corners);
            _stage = Rect.MinMaxRect(Corners[0].x, Corners[0].y, Corners[2].x, Corners[2].y);
            _spineLeft = backdrop.Spine == SpineSide.Left;
            Build();
            _back.Texture = backdrop.FacingImage.texture;
            _facingUv = backdrop.FacingImage.uvRect;
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
            return true;
        }

        /// <summary>Turns the grabbed page over the spine; <paramref name="skip"/> ends it at once.</summary>
        public async UniTask PlayAsync(Func<bool> skip) {
            _canvas.enabled = true;
            try {
                // Stepped per frame, a slow frame counting as 1/30 s at most: the frames right after the
                // grab can stall (its read-back and upload), and a clock would skip the lift entirely.
                for (float t = 0; t < 1 && !skip(); t += Mathf.Min(Time.unscaledDeltaTime, MaxStep) / Seconds) {
                    Pose(t);
                    await UniTask.Yield();
                }
            } finally {
                if (_canvas != null) {
                    _canvas.enabled = false;
                }
                ReleaseShot();
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
                float bottom = _stage.center.y - _stage.height / 2 * grow;
                float top = _stage.center.y + _stage.height / 2 * grow;
                float shade = Mathf.Lerp(EdgeOnShade, 1, Mathf.Abs(Mathf.Cos(angle)));
                float frontU = (spineX + dir * s) / Screen.width;
                float facingLocal = _spineLeft ? 1 - s / width : s / width;
                float backU = _facingUv.x + facingLocal * _facingUv.width;
                _front.Columns.Add((screenX, bottom, top, frontU, shade, frontUp));
                _back.Columns.Add((screenX, bottom, top, backU, shade, !frontUp));
            }
            _front.SetVerticesDirty();
            _back.SetVerticesDirty();

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
            // A root object: under BookView's canvas it would be a nested canvas, in that canvas's space.
            var host = new GameObject("PageCurl");
            _canvas = host.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;
            _canvas.enabled = false;
            // Shadow under all; front before back: the back is always the outer, higher part of the sheet.
            _shadow = NewSheet("Shadow");
            _shadow.Shadow = true;
            _front = NewSheet("Front");
            _back = NewSheet("Back");
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

        private void ReleaseShot() {
            if (_shot != null) {
                Destroy(_shot);
                _shot = null;
            }
        }

        private void OnDestroy() {
            ReleaseShot();
            if (_canvas != null) {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
