namespace BakAgain.UI.InputCore {
    using System;
    using System.Collections.Generic;
    using BakAgain.UI.Cursor;
    using UnityEngine;
    using UnityEngine.UIElements;

    // An IInputLayer over data-built focusable widgets (the spike's UITK-focus path). Intents arrive
    // from the InputAdapter and route here when this is the top resolved layer; focus moves via UI
    // Toolkit's focus controller (element.Focus()) and the software cursor warps onto the focused
    // widget. OnActiveChanged toggles focusable/pickingMode so an Exclusive layer above makes this one
    // inert (spike Q3). Replaces UserInterfaceLoader's Update()/_navEntries keyboard nav.
    public sealed class NavigableLayer : IInputLayer {
        private IReadOnlyList<NavWidget> _widgets;
        private readonly Action _onCancel;        // Esc / Cancel widget primary (REQ CancelActionId)
        private readonly ICursorManager _cursor;  // optional (null-safe): warp on keyboard nav
        // Optional first-refusal hook for cursor keys: a screen with a file picker
        // (Restore Game) consumes Up/Down to move the picker selection instead of
        // moving button focus. Returns true when it handled the direction.
        private readonly Func<NavDirection, bool> _trySelectionNav;
        private int _focusIndex = -1;
        // Set by SetWidgets, cleared by ResolveStaleFocus (or a real focus event). See SetWidgets
        // for why the cursor re-scan must be deferred rather than run at swap time.
        private bool _focusStale;
        // Elements we registered callbacks on, kept so a widget swap can unregister them: a
        // discarded cell that still reported FocusIn would drive _focusIndex into the new list.
        private readonly List<VisualElement> _registered = new List<VisualElement>();
        private readonly List<EventCallback<FocusInEvent>> _focusCallbacks =
            new List<EventCallback<FocusInEvent>>();
        // Last state OnActiveChanged was told. Cached because widgets can arrive AFTER that call
        // (SetWidgets), and they must adopt the state the layer is actually in.
        private bool _isActive = true;
        // True only while a keyboard-nav focus change is in flight (set around element.Focus() in
        // FocusWidget). Gates the cursor warp + synthetic hover in OnWidgetFocused, so a click/pointer
        // focus — which arrives via FocusInEvent with this false — only syncs the focus index and does
        // NOT move the cursor. Warping on click would yank the OS pointer (CursorManager.WarpTo now
        // drives the real mouse) to the widget centre mid-click, so PointerUp lands off the button.
        private bool _navWarp;

        public NavigableLayer(string id, CaptureMode captureMode, IReadOnlyList<NavWidget> widgets,
            Action onCancel, ICursorManager cursor, Func<NavDirection, bool> trySelectionNav = null,
            bool ambiguousLetterSelectsNothing = false) {
            _ambiguousLetterSelectsNothing = ambiguousLetterSelectsNothing;
            Id = id;
            CaptureMode = captureMode;
            _widgets = widgets;
            _onCancel = onCancel;
            _cursor = cursor;
            _trySelectionNav = trySelectionNav;
            // Mirror any focus change into our index via the single sync point, FocusInEvent (device
            // Navigate ends in element.Focus(); a pointer/click also focuses). The cursor warp itself
            // is gated to keyboard nav by _navWarp (see OnWidgetFocused) so a click doesn't warp.
            Register(_widgets);
        }

        // Stop UI Toolkit's built-in focus-ring navigation from acting; the InputAdapter owns nav.
        private static void SuppressNavigation<TEvent>(TEvent evt) where TEvent : EventBase<TEvent>, new() {
            evt.StopPropagation();
            evt.PreventDefault();
        }

        public string Id { get; }
        public CaptureMode CaptureMode { get; }
        public bool WantsFocus => true;

        public bool HandleIntent(UiIntent intent) {
            switch (intent.Kind) {
                case UiIntentKind.MoveFocus: return MoveFocus(intent.Direction);
                case UiIntentKind.Activate: return ActivateFocused();
                case UiIntentKind.Cancel:
                    if (_onCancel == null) return false;
                    _onCancel();
                    return true;
                case UiIntentKind.Accelerator: return Accelerate(intent.Character);
                default: return false; // Skip is for full-frame layers, not menus
            }
        }

        public void OnPushed() { }
        public void OnPopped() { }

        public void OnActiveChanged(bool isActive) {
            _isActive = isActive;
            foreach (NavWidget w in _widgets) {
                if (w.Element == null) continue;
                w.Element.focusable = isActive;
                w.Element.pickingMode = isActive ? PickingMode.Position : PickingMode.Ignore;
            }
        }

        /// <summary>
        /// Swap the widget list in place. The inventory grid rebuilds on nearly every action, and
        /// re-pushing a fresh layer would churn the input stack and fight MenuLayerHost's
        /// single-push model.
        ///
        /// <para>Focus re-resolves <b>by cursor position</b>, not by index. In the original, focus
        /// is the cursor — menupage_navigate warps it to the chosen entry (canassa
        /// MENUPAGE.C:312-315) — so a redraw cannot move it. An index would also be wrong on its
        /// own terms: the inventory re-sorts its container on every render, so slot indices are not
        /// stable across a rebuild.</para>
        ///
        /// <para>The re-scan itself is <b>deferred</b>, not run here: the inventory calls this as
        /// the last synchronous statement of its render, immediately after building brand-new cell
        /// elements that have not been through a UI Toolkit layout pass yet, so every live rect
        /// (<c>NavWidget.CanonicalRect</c>) still reads empty and nothing could ever match the
        /// cursor. <see cref="ResolveStaleFocus"/> runs the scan lazily, the next time an intent
        /// arrives — by then layout has happened. This must NOT defer the whole widget swap itself:
        /// an intent arriving in the same frame as the render has to see the new list.</para>
        /// </summary>
        public void SetWidgets(IReadOnlyList<NavWidget> widgets) {
            Unregister();
            _widgets = widgets ?? System.Array.Empty<NavWidget>();
            Register(_widgets);
            OnActiveChanged(_isActive); // widgets arriving under a modal must arrive inert
            _focusIndex = -1;
            _focusStale = true;
        }

        // Resolve the deferred focus re-scan. SetWidgets cannot do this eagerly: a screen swaps its
        // list immediately after building new elements, which have not been laid out yet, so every
        // live rect still reads empty and nothing would ever match the cursor. By the time an intent
        // arrives the layout pass has run.
        private void ResolveStaleFocus() {
            if (!_focusStale) {
                return;
            }
            _focusStale = false;
            _focusIndex = WidgetUnderCursor();
        }

        private int WidgetUnderCursor() {
            if (_cursor == null) {
                return -1;
            }
            Vector2 p = _cursor.CanonicalPosition;
            for (int i = 0; i < _widgets.Count; i++) {
                if (!CanFocus(_widgets[i])) continue; // never land focus on a hidden/unfocusable widget
                if (_widgets[i].CanonicalRect.Contains(p)) {
                    return i;
                }
            }
            return -1;
        }

        private void Register(IReadOnlyList<NavWidget> widgets) {
            for (int i = 0; i < widgets.Count; i++) {
                int idx = i;
                VisualElement el = widgets[i].Element;
                if (el == null) continue;
                EventCallback<FocusInEvent> focusIn = _ => OnWidgetFocused(idx);
                el.RegisterCallback(focusIn);
                // The InputAdapter is the single nav owner (Tab/arrows/Enter/Esc -> intents -> this layer).
                // But UI Toolkit's panel ALSO has built-in focus-ring navigation that turns the same real
                // key events into NavigationMove/Submit/Cancel and moves/activates focus a SECOND time —
                // so a real Tab skips an item (adapter move + panel move). Detaching the input *module's*
                // nav actions doesn't stop this; the panel's own handling does. Suppress it here so the
                // adapter's MoveFocus (element.Focus(), which doesn't raise these) is the only mover.
                el.RegisterCallback<NavigationMoveEvent>(SuppressNavigation, TrickleDown.TrickleDown);
                el.RegisterCallback<NavigationSubmitEvent>(SuppressNavigation, TrickleDown.TrickleDown);
                el.RegisterCallback<NavigationCancelEvent>(SuppressNavigation, TrickleDown.TrickleDown);
                _registered.Add(el);
                _focusCallbacks.Add(focusIn);
            }
        }

        private void Unregister() {
            for (int i = 0; i < _registered.Count; i++) {
                VisualElement el = _registered[i];
                el.UnregisterCallback(_focusCallbacks[i]);
                el.UnregisterCallback<NavigationMoveEvent>(SuppressNavigation, TrickleDown.TrickleDown);
                el.UnregisterCallback<NavigationSubmitEvent>(SuppressNavigation, TrickleDown.TrickleDown);
                el.UnregisterCallback<NavigationCancelEvent>(SuppressNavigation, TrickleDown.TrickleDown);
            }
            _registered.Clear();
            _focusCallbacks.Clear();
        }

        // Set an initial focus index (e.g. a dialog's default choice). navWarp:false — initial focus
        // should not move the cursor; only the user's Tab/arrow keys do.
        public void FocusIndex(int index) {
            if (index < 0 || index >= _widgets.Count) return;
            FocusWidget(index, navWarp: false);
        }

        // Move focus to a widget. An attached widget fires FocusInEvent — the single sync point that
        // updates the index (and, on keyboard nav, warps the cursor) — so we must NOT also call
        // OnWidgetFocused here or it double-fires. Only unattached widgets (panel == null: unit tests,
        // or before the panel goes live) need the explicit call, since FocusInEvent never fires for
        // them. navWarp is true only for keyboard nav (Tab/arrows/accelerator); it is reset after
        // Focus() so a later pointer/click focus (its own FocusInEvent) sees false and does not warp.
        private void FocusWidget(int index, bool navWarp) {
            VisualElement el = _widgets[index].Element;
            _navWarp = navWarp;
            el?.Focus();
            if (el == null || el.panel == null) {
                OnWidgetFocused(index);
            }
            _navWarp = false;
        }

        private bool MoveFocus(NavDirection dir) {
            ResolveStaleFocus();
            // A file-picker screen consumes Up/Down to move its list selection, so
            // cursor keys navigate the saves rather than the Restore/Cancel buttons
            // (which stay reachable via Tab / Left / Right).
            if (_trySelectionNav != null && _trySelectionNav(dir)) return true;
            if (_widgets.Count == 0) return false;
            int target = dir == NavDirection.Next || dir == NavDirection.Previous
                ? Cyclic(dir == NavDirection.Next ? 1 : -1)
                : Spatial(dir);
            if (target < 0) return false;
            FocusWidget(target, navWarp: true);
            return true;
        }

        // Whether a widget can actually take UI Toolkit focus. A hidden element refuses focus AND clears
        // the panel's current focus without firing FocusInEvent, so _focusIndex would never advance past
        // it — navigation would stall there forever and everything after it would be unreachable. Screens
        // hide live widgets at runtime (the inventory hides "More Info" while the grid is up, via the
        // loader's `req-hidden` class), so skipping them here is what keeps the entry list navigable.
        //
        // This also lands the original's own behaviour: its inventory page trims the live entry list to
        // the seven chrome entries plus one per item (canassa CMBINV.C:60), and "More Info" is not among
        // them — it becomes live only in the item-inspect view (INVINSP.C:407-417). Driving that off
        // visibility means each view gets the right list without a per-screen exception.
        //
        // Concretely: our grid-view chrome list is EIGHT live entries (the seven chrome hotspots plus
        // the hidden "More Info" button, which the loader always builds), where the original's is
        // seven — More Info simply isn't appended in that view. The two lists differ by that one
        // entry, but nothing here trims it out of the list; it is held out dynamically, by this
        // runtime hidden check, the same way the original's own inspect-view swap turns it on.
        private static bool CanFocus(NavWidget widget) {
            VisualElement el = widget.Element;
            if (el == null) return false;
            // Unattached (unit tests, or before the panel goes live): resolvedStyle never resolves, and
            // FocusWidget's explicit OnWidgetFocused fallback is what makes these "focused" anyway (see
            // FocusWidget) — so treat them as focusable rather than false-negative on missing layout state.
            if (el.panel == null) return true;
            return el.focusable
                && el.resolvedStyle.display != DisplayStyle.None
                && el.resolvedStyle.visibility != Visibility.Hidden;
        }

        private static int Wrap(int i, int n) => ((i % n) + n) % n;

        private int Cyclic(int step) {
            int n = _widgets.Count;
            int index = _focusIndex < 0 ? (step > 0 ? 0 : n - 1) : Wrap(_focusIndex + step, n);
            // Step over unfocusable widgets (hidden/unfocusable) in the same direction until one that can
            // take focus turns up. Bounded by n so a list that is entirely unfocusable terminates instead
            // of spinning forever — measured live: the inventory's hidden "More Info" button otherwise
            // stalled every subsequent Tab at the same index forever (see class doc / task-7).
            for (int i = 0; i < n; i++) {
                if (CanFocus(_widgets[index])) return index;
                index = Wrap(index + step, n);
            }
            return -1;
        }

        // Nearest widget in the pressed direction (ports UserInterfaceLoader.MoveSpatial): minimise the
        // perpendicular offset first (stay on the row/column), then the along-axis distance. Origin is
        // the focused widget centre, or the live cursor when nothing is focused.
        private int Spatial(NavDirection dir) {
            int dx = dir == NavDirection.Left ? -1 : dir == NavDirection.Right ? 1 : 0;
            int dy = dir == NavDirection.Up ? -1 : dir == NavDirection.Down ? 1 : 0;
            Vector2 origin = _focusIndex >= 0
                ? _widgets[_focusIndex].Center
                : (_cursor?.CanonicalPosition ?? Vector2.zero);
            int best = -1;
            float bestPerp = float.MaxValue;
            float bestAlong = float.MaxValue;
            for (int i = 0; i < _widgets.Count; i++) {
                if (i == _focusIndex) continue;
                if (!CanFocus(_widgets[i])) continue; // never consider an unfocusable widget as a candidate
                Vector2 c = _widgets[i].Center;
                float ox = c.x - origin.x;
                float oy = c.y - origin.y;
                if (dx != 0 && (ox == 0f || (int)Mathf.Sign(ox) != dx)) continue;
                if (dy != 0 && (oy == 0f || (int)Mathf.Sign(oy) != dy)) continue;
                float along = Mathf.Abs(dx != 0 ? ox : oy);
                float perp = Mathf.Abs(dx != 0 ? oy : ox);
                if (perp < bestPerp || (Mathf.Approximately(perp, bestPerp) && along < bestAlong)) {
                    bestPerp = perp;
                    bestAlong = along;
                    best = i;
                }
            }
            return best;
        }

        private bool ActivateFocused() {
            ResolveStaleFocus();
            if (_focusIndex < 0 || _focusIndex >= _widgets.Count) return false;
            if (!CanFocus(_widgets[_focusIndex])) return false; // never invoke a hidden/unfocusable widget
            _widgets[_focusIndex].Primary?.Invoke();
            return true;
        }

        // First-letter accelerator (menu_pollInput): a char matching a widget label's leading letter
        // focuses + activates it. First match wins on ties. A hidden/unfocusable widget is skipped —
        // it must never be focused OR invoked, same as WidgetUnderCursor/ActivateFocused.
        // The dialog choice menu's own scan (ASKABOUT.C:499-511): a letter that starts more than one label
        // selects nothing and the menu keeps waiting. Other menus poll through menu_pollInput and keep the
        // first match.
        private readonly bool _ambiguousLetterSelectsNothing;

        private bool Accelerate(char c) {
            char lower = char.ToLowerInvariant(c);
            if (_ambiguousLetterSelectsNothing) {
                int matches = 0;
                for (int j = 0; j < _widgets.Count; j++) {
                    string l = _widgets[j].Label;
                    if (CanFocus(_widgets[j]) && !string.IsNullOrEmpty(l) && char.ToLowerInvariant(l[0]) == lower) {
                        matches++;
                    }
                }
                if (matches > 1) {
                    return true;
                }
            }
            for (int i = 0; i < _widgets.Count; i++) {
                if (!CanFocus(_widgets[i])) continue;
                string label = _widgets[i].Label;
                if (string.IsNullOrEmpty(label)) continue;
                if (char.ToLowerInvariant(label[0]) == lower) {
                    FocusWidget(i, navWarp: true);
                    _widgets[i].Primary?.Invoke();
                    return true;
                }
            }
            return false;
        }

        private void OnWidgetFocused(int index) {
            // A real focus event supersedes a pending SetWidgets re-scan — it already knows the
            // answer ResolveStaleFocus would otherwise compute later.
            _focusStale = false;
            if (index == _focusIndex) {
                return; // already the active widget — don't re-warp or re-toggle its hover
            }
            // Always clear the previously-focused widget's highlight (it may be a stale keyboard
            // highlight when the user switches to the mouse).
            if (_focusIndex >= 0 && _focusIndex < _widgets.Count) {
                DispatchPointer(_widgets[_focusIndex].Element, PointerLeaveEvent.GetPooled());
            }
            _focusIndex = index;
            // Only KEYBOARD nav moves the cursor and drives the synthetic hover: the hover visuals (icon
            // swap, USS :hover, cursor shape) follow keyboard focus / the warped cursor, matching the
            // original's single highlighted "active item". A click/pointer focus (_navWarp false) skips
            // both — the cursor is already where the user clicked (warping it would yank the OS pointer
            // mid-click and break the click), and the real mouse PointerEnter already drives the hover.
            if (_navWarp) {
                _cursor?.WarpTo(_widgets[index].Center);
                DispatchPointer(_widgets[index].Element, PointerEnterEvent.GetPooled());
            }
        }

        // Send a pooled pointer event to an element (or dispose it if there's no element), so the
        // element's existing PointerEnter/Leave hover handlers fire from a focus change.
        private static void DispatchPointer(VisualElement element, EventBase pooledEvent) {
            // No panel (unattached test widgets, or before the panel goes live) -> nothing to hover.
            if (element?.panel == null) {
                pooledEvent.Dispose();
                return;
            }
            using (pooledEvent) {
                pooledEvent.target = element;
                element.SendEvent(pooledEvent);
            }
        }
    }
}
