namespace BakAgain.UI.Inventory {
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The Enhanced shop shelf's scroll arithmetic (TASK-890, owner 2026-10-18): the whole stock in
    /// one strip instead of the original's six-item pages, shrunk just enough that a slice of the
    /// next row shows below the last whole one. Offsets are in the viewport's own units.
    /// </summary>
    public static class ShelfScroll {
        /// <summary>How much of the next row shows when the stock does not fit.</summary>
        public const float PeekRows = 0.35f;

        /// <summary>One More press moves this share of the viewport, so no row goes by unseen.</summary>
        public const float PageShare = 0.9f;

        public static float PeekScale(int totalRows, int visibleRows) =>
            totalRows > visibleRows && visibleRows > 0 ? visibleRows / (visibleRows + PeekRows) : 1f;

        public static float Clamp(float offset, float content, float viewport) =>
            Mathf.Clamp(offset, 0f, Mathf.Max(0f, content - viewport));

        /// <summary>More: down a page, stopping at the end; from the end, back to the top (the
        /// original's Next also wraps, <see cref="GameData.Resources.Shop.ShopPaging.Turned"/>).</summary>
        public static float Paged(float offset, float content, float viewport) {
            float max = Mathf.Max(0f, content - viewport);
            return offset >= max - 0.5f ? 0f : Mathf.Min(offset + viewport * PageShare, max);
        }

        /// <summary>The nearest offset at which [top, bottom] is inside the viewport.</summary>
        public static float Revealing(float offset, float top, float bottom, float viewport) =>
            top < offset ? top : bottom > offset + viewport ? bottom - viewport : offset;
    }

    /// <summary>
    /// Drives one rendered shelf: the clipping viewport (the renderer's grid host) and the strip of
    /// cells inside it. Drag, wheel, coast after a fling, More, and keyboard focus all move the
    /// same offset.
    /// </summary>
    public sealed class ShelfScroller {
        private const float CoastDecay = 0.9f;   // velocity kept per 16 ms step
        private const float CoastStop = 30f;     // units/s below which the coast ends

        private readonly VisualElement _viewport;
        private readonly VisualElement _content;
        private readonly float _scale;
        private readonly float _rowsRatio;
        private float _velocity;
        private IVisualElementScheduledItem _coast;

        public ShelfScroller(VisualElement viewport, VisualElement content, int totalRows, int visibleRows, float offset) {
            _viewport = viewport;
            _content = content;
            _scale = ShelfScroll.PeekScale(totalRows, visibleRows);
            _rowsRatio = visibleRows > 0 ? (float)totalRows / visibleRows : 1f;
            viewport.style.overflow = Overflow.Hidden;
            content.style.position = Position.Absolute;
            content.style.left = 0;
            content.style.top = 0;
            content.style.width = Length.Percent(100);
            content.style.height = Length.Percent(100 * _rowsRatio);
            content.style.transformOrigin = new TransformOrigin(Length.Percent(50), 0);
            content.style.scale = new Scale(new Vector2(_scale, _scale));
            Offset = offset;
            viewport.RegisterCallback<GeometryChangedEvent>(_ => Apply(Offset));
        }

        public float Offset { get; private set; }

        public bool Scrolls => _rowsRatio > 1f;

        private float ViewportHeight => _viewport.layout.height;

        private float ContentHeight => ViewportHeight * _rowsRatio * _scale;

        public bool Contains(Vector2 world) => _viewport.worldBound.Contains(world);

        public void ScrollBy(float delta) => Apply(Offset + delta);

        public void Page() {
            StopCoast();
            Apply(ShelfScroll.Paged(Offset, ContentHeight, ViewportHeight));
        }

        /// <summary>Brings a focused cell (keyboard or gamepad) into view.</summary>
        public void Reveal(VisualElement cell) {
            float top = cell.layout.y * _scale, bottom = cell.layout.yMax * _scale;
            Apply(ShelfScroll.Revealing(Offset, top, bottom, ViewportHeight));
        }

        /// <summary>Lets go with a speed (viewport units per second): the strip coasts to a stop.</summary>
        public void Fling(float velocity) {
            StopCoast();
            _velocity = velocity;
            _coast = _viewport.schedule.Execute(() => {
                float before = Offset;
                ScrollBy(_velocity * 0.016f);
                _velocity *= CoastDecay;
                if (Mathf.Abs(_velocity) < CoastStop || Mathf.Approximately(before, Offset)) StopCoast();
            }).Every(16);
        }

        private float _dragStartOffset, _dragStartY, _lastY, _lastTime;

        /// <summary>Viewport units per world unit (the sheet the shelf sits on is scaled).</summary>
        private float UnitsPerWorld {
            get {
                float w = _viewport.worldBound.height, h = ViewportHeight;
                return w > 0f && h > 0f ? h / w : 1f;
            }
        }

        public void BeginDrag(float worldY) {
            StopCoast();
            _dragStartOffset = Offset;
            _dragStartY = _lastY = worldY;
            _lastTime = Time.unscaledTime;
            _velocity = 0f;
        }

        /// <summary>The strip follows the pointer: dragging up shows what is below.</summary>
        public void DragTo(float worldY) {
            float now = Time.unscaledTime, dt = now - _lastTime;
            if (dt > 0.001f) _velocity = -(worldY - _lastY) * UnitsPerWorld / dt;
            _lastY = worldY;
            _lastTime = now;
            Apply(_dragStartOffset - (worldY - _dragStartY) * UnitsPerWorld);
        }

        public void EndDrag() {
            if (Time.unscaledTime - _lastTime < 0.1f && Mathf.Abs(_velocity) > CoastStop) Fling(_velocity);
        }

        /// <summary>A wheel notch, in world units of travel.</summary>
        public void Wheel(float worldDelta) {
            StopCoast();
            ScrollBy(worldDelta * UnitsPerWorld);
        }

        public void StopCoast() {
            _coast?.Pause();
            _coast = null;
        }

        private void Apply(float offset) {
            float h = ViewportHeight;
            Offset = float.IsNaN(h) || h <= 0f ? Mathf.Max(0f, offset) : ShelfScroll.Clamp(offset, ContentHeight, h);
            _content.style.translate = new Translate(0, -Offset);
        }
    }
}
