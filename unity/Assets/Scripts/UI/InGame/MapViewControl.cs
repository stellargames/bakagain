namespace BakAgain.UI.InGame {
    using System;
    using System.Collections.Generic;
    using GameData.Resources.Layout;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The overhead map's pointer controls: drag to pan, pinch to zoom, twist or right-drag to turn,
    /// double-click or double-tap to put the view back on the party.
    /// </summary>
    /// <remarks>
    /// <b>An addition, not a port</b> — the original map follows the party and nothing else. View
    /// state only: nothing here is saved, the zoom itself still goes through the screen's one zoom
    /// path (so it stays on the zone's steps), and a step or turn of the party recentres the view.
    /// The pan is limited so the party never leaves the view.
    ///
    /// <para>The gestures land on a transparent surface over the world view, behind every REQ
    /// widget, so a press on a button or portrait is never a drag.</para>
    /// </remarks>
    public sealed class MapViewControl {
        private const int FullTurn = 0x10000;

        private readonly EnhancedMapLayout _layout;
        private readonly Action<int, Vector2> _zoomStep;
        private readonly Dictionary<int, Vector2> _pointers = new();
        private VisualElement _surface;
        private Camera _camera;
        private bool _turning;
        private float _pinchBase;
        private float _twistLast;

        /// <param name="zoomStep">One zoom step about a panel point: +1 lifts the camera (out), -1 lowers it (in).</param>
        public MapViewControl(EnhancedMapLayout layout, Action<int, Vector2> zoomStep) {
            _layout = layout;
            _zoomStep = zoomStep;
        }

        /// <summary>The view centre's offset from the party, in Unity XZ.</summary>
        public Vector2 Pan { get; private set; }

        /// <summary>Yaw added to the map's own, in engine units.</summary>
        public int Turn { get; private set; }

        public void Recentre() {
            Pan = Vector2.zero;
            Turn = 0;
        }

        public void Attach(VisualElement viewport, Camera camera) {
            _camera = camera;
            _surface = new VisualElement {
                name = "BakMapGestures",
                pickingMode = PickingMode.Position,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };
            viewport.Add(_surface);
            _surface.RegisterCallback<PointerDownEvent>(OnDown);
            _surface.RegisterCallback<PointerMoveEvent>(OnMove);
            _surface.RegisterCallback<PointerUpEvent>(OnUp);
            _surface.RegisterCallback<PointerCancelEvent>(e => Release(e.pointerId));
        }

        public void Detach() {
            _surface?.RemoveFromHierarchy();
            _surface = null;
            _pointers.Clear();
        }

        /// <summary>
        /// Keeps the ground under <paramref name="panelPoint"/> where it is while the camera goes from
        /// <paramref name="fromHeight"/> to <paramref name="toHeight"/>.
        /// </summary>
        public void ZoomAbout(Vector2 panelPoint, float fromHeight, float toHeight) {
            if (_surface == null || _camera == null || fromHeight <= 0f) {
                return;
            }
            Rect b = _surface.worldBound;
            if (b.width <= 0f || !b.Contains(panelPoint)) {
                return;
            }
            (Vector2 right, Vector2 up, float halfW, float halfH) = Axes(fromHeight);
            float u = (panelPoint.x - b.center.x) / (b.width / 2f);
            float v = (b.center.y - panelPoint.y) / (b.height / 2f);
            Pan += (right * (u * halfW) + up * (v * halfH)) * (1f - toHeight / fromHeight);
        }

        /// <summary>Holds the party inside the view at <paramref name="height"/>; call after the camera is turned.</summary>
        public void Clamp(float height) {
            if (_camera == null || Pan == Vector2.zero) {
                return;
            }
            (Vector2 right, Vector2 up, float halfW, float halfH) = Axes(height);
            float f = _layout.PanLimitFraction;
            float a = Mathf.Clamp(Vector2.Dot(Pan, right), -halfW * f, halfW * f);
            float b = Mathf.Clamp(Vector2.Dot(Pan, up), -halfH * f, halfH * f);
            Pan = right * a + up * b;
        }

        // The screen's right and up on the ground, and the ground's half-extent, for a camera looking
        // straight down from height.
        private (Vector2 Right, Vector2 Up, float HalfW, float HalfH) Axes(float height) {
            Transform t = _camera.transform;
            float halfH = height * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return (new Vector2(t.right.x, t.right.z).normalized, new Vector2(t.up.x, t.up.z).normalized,
                halfH * _camera.aspect, halfH);
        }

        private void OnDown(PointerDownEvent e) {
            if (e.clickCount == 2) {
                Recentre();
            }
            _pointers[e.pointerId] = e.position;
            _turning = e.pointerType == UnityEngine.UIElements.PointerType.mouse && e.button == 1;
            _surface.CapturePointer(e.pointerId);
            if (_pointers.Count == 2) {
                (float d, float angle, _) = Pair();
                _pinchBase = d;
                _twistLast = angle;
            }
            e.StopPropagation();
        }

        private void OnMove(PointerMoveEvent e) {
            if (!_pointers.TryGetValue(e.pointerId, out Vector2 last) || _camera == null) {
                return;
            }
            if (_pointers.Count == 1) {
                Vector2 delta = (Vector2)e.position - last;
                _pointers[e.pointerId] = e.position;
                if (_turning) {
                    TurnBy(delta.x / Mathf.Max(1f, _surface.worldBound.width) * _layout.TurnPerViewWidth * 360f);
                } else {
                    DragBy(delta);
                }
                return;
            }

            (float _, float _, Vector2 midBefore) = Pair();
            _pointers[e.pointerId] = e.position;
            (float d, float angle, Vector2 mid) = Pair();
            DragBy(mid - midBefore);
            TurnBy(Mathf.DeltaAngle(_twistLast, angle));
            _twistLast = angle;
            if (_pinchBase > 0f && d / _pinchBase >= _layout.PinchStepRatio) {
                _zoomStep(-1, mid);
                _pinchBase = d;
            } else if (_pinchBase > 0f && _pinchBase / Mathf.Max(1f, d) >= _layout.PinchStepRatio) {
                _zoomStep(+1, mid);
                _pinchBase = d;
            }
        }

        private void OnUp(PointerUpEvent e) => Release(e.pointerId);

        private void Release(int pointerId) {
            _pointers.Remove(pointerId);
            _surface?.ReleasePointer(pointerId);
            _turning = false;
            _pinchBase = 0f;
        }

        // The ground follows the pointer: the camera moves the other way.
        private void DragBy(Vector2 panelDelta) {
            float h = _surface.worldBound.height;
            if (h <= 0f) {
                return;
            }
            (Vector2 right, Vector2 up, _, float halfH) = Axes(_camera.transform.position.y);
            float perPx = 2f * halfH / h;
            Pan += (-right * panelDelta.x + up * panelDelta.y) * perPx;
        }

        // Panel degrees, clockwise positive (panel y runs down).
        private void TurnBy(float degrees) {
            Turn = (Turn - Mathf.RoundToInt(degrees / 360f * FullTurn)) & (FullTurn - 1);
        }

        private (float Distance, float AngleDegrees, Vector2 Mid) Pair() {
            using Dictionary<int, Vector2>.ValueCollection.Enumerator it = _pointers.Values.GetEnumerator();
            it.MoveNext();
            Vector2 a = it.Current;
            it.MoveNext();
            Vector2 b = it.Current;
            Vector2 d = b - a;
            return (d.magnitude, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, (a + b) / 2f);
        }
    }
}
