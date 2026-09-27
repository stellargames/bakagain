namespace BakAgain.ResourceManagement.Loaders {
    using System;
    using System.Collections.Generic;
    using BakAgain.UI;
    using BakAgain.UI.InputCore;
    using GameData.Resources.Menu;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Renders and drives the REQ file-picker widgets (the Restore/Save Game
    /// directory + saves lists): the scrollbar chrome, windowed rows, selection
    /// highlight, thumb, arrow/keyboard navigation, and double-click activation.
    /// Reproduces the original engine's filePicker_* (ovr142) + menu_type_2
    /// (0x2bd70) rendering. Extracted from <see cref="UserInterfaceLoader"/>, which
    /// delegates its <c>AddFilePicker</c>/<c>RefreshFilePicker</c>/
    /// <c>TryMovePickerSelection</c> here. All visual styling lives in
    /// ClassicTheme.tss (.file-picker-*); this only builds structure and reads its
    /// data from the screen's <see cref="IFilePickerSource"/>.
    /// </summary>
    internal sealed class FilePickerRenderer {
        private readonly IFilePickerSource _source;

        // View by ActionId, kept so Refresh is a pure redraw. Each view owns the
        // clipped rows host + scrollbar thumb.
        private readonly Dictionary<int, FilePickerView> _pickers = new();
        // First visible row per picker (scroll offset), so the arrows and a redraw
        // keep their place. Mirrors the engine's per-picker scroll.
        private readonly Dictionary<int, int> _scroll = new();
        // The picker the keyboard cursor keys drive — the last one interacted with,
        // defaulting to the last-built picker (the Games list on REQ_LOAD).
        private int _activePicker = -1;

        // Manual double-click detection: a single click rebuilds the row list
        // (Populate), destroying the clicked label — which resets UITK's
        // ClickEvent.clickCount — so track the last click by (picker, index, time).
        private const float DoubleClickSeconds = 0.4f;
        private int _lastClickPicker = -1;
        private int _lastClickIndex = -1;
        private float _lastClickTime = -1f;

        public FilePickerRenderer(IFilePickerSource source) {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        // Per-picker view state kept so Refresh is a pure redraw.
        private sealed class FilePickerView {
            public VisualElement RowsHost;
            public VisualElement Track;
            public VisualElement Thumb;
            public int VisibleRows;
            public float ThumbFraction; // 0..1 from the selection; -> thumb.top after layout
        }

        /// <summary>Build a file-picker widget for <paramref name="menuEntry"/> into
        /// <paramref name="parent"/> (the canonical stage).</summary>
        public void Add(UiElement menuEntry, VisualElement parent) {
            int actionId = menuEntry.ActionId;
            Rect rect = _source.GetPickerRect(actionId);

            // Outer widget: list well (flex-grows) + fixed-width scrollbar. Only the
            // data-driven rect is inline; layout/visuals come from the USS class.
            var widget = new VisualElement { name = $"filepicker_{actionId}" };
            widget.AddToClassList("file-picker");
            widget.style.left = rect.x;
            widget.style.top = rect.y;
            widget.style.width = rect.width;
            widget.style.height = rect.height;
            if (!menuEntry.Visible) {
                widget.AddToClassList("req-hidden");
            }

            var listBox = new VisualElement { name = $"filepicker_{actionId}_list" };
            listBox.AddToClassList("file-picker-well");
            listBox.AddToClassList("bevel-raised");

            var rowsHost = new VisualElement { name = $"filepicker_{actionId}_rows" };
            rowsHost.AddToClassList("file-picker-rows");
            listBox.Add(rowsHost);
            widget.Add(listBox);

            var view = new FilePickerView {
                RowsHost = rowsHost,
                VisibleRows = Mathf.Max(1, _source.GetVisibleRows(actionId)),
            };
            BuildScrollbar(widget, actionId, view);

            parent.Add(widget);
            _pickers[actionId] = view;
            _activePicker = actionId; // last-built picker is the default keyboard target
            Populate(actionId);
        }

        /// <summary>Re-render a file picker after its underlying data changed (e.g.
        /// the left-pane directory selection produced a new file list in the right
        /// pane). Selection highlight is recomputed from the source.</summary>
        public void Refresh(int actionId) {
            if (_pickers.ContainsKey(actionId)) {
                Populate(actionId);
            }
        }

        /// <summary>Keyboard cursor-key hook: move the active picker's selection.
        /// Returns true when consumed (Up/Down over a live picker) so the caller —
        /// the NavigableLayer — does not also move button focus. Left/Right/Tab fall
        /// through to normal widget navigation.</summary>
        public bool TryMoveSelection(NavDirection dir) {
            if (_pickers.Count == 0) {
                return false;
            }
            int actionId = _pickers.ContainsKey(_activePicker) ? _activePicker : FirstPickerActionId();
            if (actionId < 0) {
                return false;
            }
            switch (dir) {
                case NavDirection.Up:
                    MoveSelection(actionId, -1);
                    return true;
                case NavDirection.Down:
                    MoveSelection(actionId, +1);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Drop all picker state on screen teardown.</summary>
        public void Clear() {
            _pickers.Clear();
            _scroll.Clear();
            _activePicker = -1;
        }

        // The scrollbar column: up-arrow button, a flex-grow track holding the
        // thumb, and a down-arrow button (sprite flipped). The arrows move the
        // selection, not just the view (menu_type_2 @ 0x2bd70).
        private void BuildScrollbar(VisualElement widget, int actionId, FilePickerView view) {
            var bar = new VisualElement { name = $"filepicker_{actionId}_bar" };
            bar.AddToClassList("file-picker-scrollbar");
            bar.AddToClassList("bevel-raised");

            VisualElement up = MakeArrowButton(down: false, () => MoveSelection(actionId, -1));

            var track = new VisualElement { name = $"filepicker_{actionId}_track" };
            track.AddToClassList("file-picker-track");
            var thumb = new VisualElement { name = $"filepicker_{actionId}_thumb" };
            thumb.AddToClassList("file-picker-thumb");
            thumb.AddToClassList("bevel-raised");
            track.Add(thumb);

            VisualElement down = MakeArrowButton(down: true, () => MoveSelection(actionId, +1));

            bar.Add(up);
            bar.Add(track);
            bar.Add(down);
            widget.Add(bar);

            view.Track = track;
            view.Thumb = thumb;
            // Position the thumb once the track has a resolved height (and on resize).
            track.RegisterCallback<GeometryChangedEvent>(_ => PositionThumb(view));
        }

        private VisualElement MakeArrowButton(bool down, Action onClick) {
            var btn = new VisualElement();
            btn.AddToClassList("file-picker-arrow");
            btn.AddToClassList(down ? "file-picker-arrow--down" : "file-picker-arrow--up");
            btn.AddToClassList("bevel-raised");
            // The red scroll arrow is BICONS combined-index 0 (BICONS1.BMX#0) — the
            // up arrow; the down button reuses it vertically flipped (the engine's
            // sub_seg029_A9 flip flag). An ArchiveImage loads it by archive key and
            // carries that key as a UXML attribute, so the export round-trips it.
            var glyph = new ArchiveImage("BICONS1.BMX#0") { pickingMode = PickingMode.Ignore };
            glyph.AddToClassList("file-picker-arrow-glyph");
            if (down) {
                glyph.AddToClassList("file-picker-arrow-glyph--flip");
            }
            btn.Add(glyph);
            btn.AddManipulator(new Clickable(_ => onClick()));
            return btn;
        }

        private int FirstPickerActionId() {
            foreach (int k in _pickers.Keys) {
                return k;
            }
            return -1;
        }

        // Scrollbar arrows move the SELECTION up/down (not just the view), then
        // scroll the window so the newly-selected row stays visible.
        private void MoveSelection(int actionId, int delta) {
            int count = _source.GetItemCount(actionId);
            if (count == 0) {
                return;
            }
            int cur = _source.GetSelectedIndex(actionId);
            int next = cur < 0 ? (delta > 0 ? 0 : count - 1) : Mathf.Clamp(cur + delta, 0, count - 1);
            if (next == cur) {
                return;
            }
            _source.OnItemSelected(actionId, next);
            EnsureRowVisible(actionId, next);
            Populate(actionId);
        }

        // Adjust the scroll offset so row `index` is within the visible window.
        private void EnsureRowVisible(int actionId, int index) {
            if (!_pickers.TryGetValue(actionId, out FilePickerView view)) {
                return;
            }
            int visible = Mathf.Max(1, view.VisibleRows);
            int offset = _scroll.TryGetValue(actionId, out int o) ? o : 0;
            if (index < offset) {
                offset = index;
            } else if (index >= offset + visible) {
                offset = index - visible + 1;
            }
            _scroll[actionId] = offset;
        }

        private void Populate(int actionId) {
            if (!_pickers.TryGetValue(actionId, out FilePickerView view)) {
                return;
            }
            VisualElement host = view.RowsHost;
            host.Clear();

            int count = _source.GetItemCount(actionId);
            int visible = Mathf.Max(1, view.VisibleRows);
            int maxOffset = Mathf.Max(0, count - visible);
            int offset = Mathf.Clamp(_scroll.TryGetValue(actionId, out int o) ? o : 0, 0, maxOffset);
            _scroll[actionId] = offset;
            int selected = _source.GetSelectedIndex(actionId);

            for (int row = 0; row < visible; row++) {
                int index = offset + row;
                if (index >= count) {
                    break;
                }
                bool isSelected = index == selected;
                var label = new Label();
                label.AddToClassList("file-picker-row");
                // Same shape as a text button: this element carries the background and (when
                // selected) the bevel, so the text goes in a child that can be stretched without
                // taking them with it. Left-anchored, because a row is middle-LEFT aligned.
                BakAgain.UI.GameFontText.Caption(
                    label, _source.GetItemLabel(actionId, index), BakAgain.UI.GameFontText.AnchorX.Left);
                // Selected row: same background, distinguished by the bevel box +
                // cream (pen 0x0A) text with a pen-1 drop shadow (the .--selected
                // and .bevel-raised classes).
                if (isSelected) {
                    label.AddToClassList("file-picker-row--selected");
                    label.AddToClassList("bevel-raised");
                }
                int captured = index;
                // Single click selects; a second click on the same row within the
                // double-click window activates it (select + Restore for a save row) —
                // the engine synthesises Restore on a double-click.
                label.RegisterCallback<ClickEvent>(_ => {
                    _activePicker = actionId;
                    float now = Time.unscaledTime;
                    bool doubleClick = _lastClickPicker == actionId && _lastClickIndex == captured
                        && now - _lastClickTime <= DoubleClickSeconds;
                    _lastClickPicker = actionId;
                    _lastClickIndex = captured;
                    _lastClickTime = now;
                    if (doubleClick) {
                        _lastClickIndex = -1; // consume, so a third click starts fresh
                        _source.OnItemActivated(actionId, captured);
                    } else {
                        _source.OnItemSelected(actionId, captured);
                        Populate(actionId);
                    }
                });
                host.Add(label);
            }

            UpdateThumb(view, count, selected);
        }

        // The thumb is a fixed-size handle that always shows and tracks the SELECTION:
        // first item selected → top of the track, last item → bottom. Only the
        // per-item step depends on the item count.
        private static void UpdateThumb(FilePickerView view, int count, int selected) {
            view.ThumbFraction = (count <= 1 || selected < 0)
                ? 0f
                : Mathf.Clamp01((float)selected / (count - 1));
            PositionThumb(view);
        }

        // Place the thumb from its fraction, reading the track/thumb sizes from the
        // resolved (USS-driven) layout. No-op until the track has been laid out —
        // the track's GeometryChangedEvent calls this again once it has a height.
        private static void PositionThumb(FilePickerView view) {
            if (view.Thumb == null || view.Track == null) {
                return;
            }
            float trackHeight = view.Track.contentRect.height;
            if (float.IsNaN(trackHeight) || trackHeight <= 0f) {
                return;
            }
            float travel = Mathf.Max(0f, trackHeight - view.Thumb.resolvedStyle.height);
            view.Thumb.style.top = view.ThumbFraction * travel;
        }
    }
}
