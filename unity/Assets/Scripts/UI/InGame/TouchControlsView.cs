namespace BakAgain.UI.InGame {
    using System.Collections.Generic;
    using BakAgain.UI.InputCore;
    using GameData.Resources.Layout;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The Android touch aids (spec 2026-09-29-android-touch-aids-design.md in the private
    /// workspace): controls drawn in the pillarbox side bars only, each feeding a path the mouse
    /// already uses.
    /// </summary>
    /// <remarks>
    /// <para><b>Nothing here moves.</b> Pad buttons are named <c>touchpad_{actionId}</c>, which
    /// ClassicMovementDriver picks exactly like the REQ compass arrows, so a held pad repeats with the
    /// original's dead time (a finger is heard through UI Toolkit pointer events — see RegisterHold).
    /// A long-press anywhere is the right-click; this view tracks the finger for it (TouchDown /
    /// TouchTarget). Thrust and Swing raise <see cref="MeleeRequested"/> for the combat targeting to
    /// resolve through the mouse's click path; the grid button is the original's G key.</para>
    ///
    /// <para><b>Temporary variants.</b> The owner is comparing them on a phone; the losers and the
    /// cycle button go once one is chosen. ponytail: flat stone-coloured fills, not the original's
    /// BICONS sprites — swap them in (UiElement.IconKeyForCombined) for the variant that stays.</para>
    /// </remarks>
    public sealed class TouchControlsView : System.IDisposable {
        private const int Forward = 72, Back = 80, TurnLeft = 75, TurnRight = 77;
        private static readonly int[] PadIds = { Forward, Back, TurnLeft, TurnRight };

        private readonly TouchInputState _state;
        private readonly IPointer _pointer;
        private readonly TouchControlsLayout _layout;
        private readonly Dictionary<int, VisualElement> _pads = new Dictionary<int, VisualElement>();
        private readonly List<VisualElement> _arrowZones = new List<VisualElement>();
        private readonly Dictionary<VisualElement, System.Action> _holdUnregister =
            new Dictionary<VisualElement, System.Action>();
        private Label _diag;
        private VisualElement _root, _stage, _left, _right, _grid, _thrust, _swing, _move, _cycle;
        private CursorContext _lastContext;
        private bool _lastAwaiting;
        private VisualElement _panelRoot;
        private bool _inFight;
        private bool _wasTouch;

        public TouchControlsView(TouchInputState state, IPointer pointer, TouchControlsLayout layout) {
            _state = state;
            _pointer = pointer;
            _layout = layout;
        }

        /// <summary>Thrust (true) or Swing (false) was asked for from the side bar.</summary>
        public event System.Action<bool> MeleeRequested;

        /// <summary>C3: the Move / Cast-here button — the ground click on the cursor's cell.</summary>
        public event System.Action MoveRequested;

        private bool IsTouch => _pointer != null && _pointer.CanPoint && !_pointer.IsPresent;

        /// <param name="documentRoot">The screen's document root: the side bars are its area outside <paramref name="stage"/>.</param>
        /// <param name="stage">The canonical 4:3 stage (CanonicalStage), untouched.</param>
        public void Build(VisualElement documentRoot, VisualElement stage) {
            _root = documentRoot;
            _stage = stage;
            _left = Bar("touch-left");
            _right = Bar("touch-right");
            foreach (int id in PadIds) {
                _pads[id] = Button($"touchpad_{id}", Glyph(id), null);
                RegisterHold(_pads[id], id, reqArrow: false);
            }
            _grid = Button("touch-grid", "#", _state.RequestGridToggle);
            _thrust = Button("touch-thrust", "Thrust", () => MeleeRequested?.Invoke(true));
            _swing = Button("touch-swing", "Swing", () => MeleeRequested?.Invoke(false));
            _move = Button("touch-move", "Move", () => MoveRequested?.Invoke());
            _cycle = Button("touch-cycle", "↻", Cycle);
            _right.Add(_grid);
            _right.Add(_thrust);
            _right.Add(_swing);
            _right.Add(_move);
            _right.Add(_cycle);
            _root.Add(_left);
            _root.Add(_right);
            if (Debug.isDebugBuild) {
                // Temporary (2026-09-30): the owner's phone saw no held finger through the polled
                // pointer. This readout names what the device reports; remove with the variant choice.
                _diag = new Label { name = "touch-diag", pickingMode = PickingMode.Ignore };
                _diag.style.position = Position.Absolute;
                _diag.style.left = 8;
                _diag.style.top = 8;
                _diag.style.fontSize = 22;
                _diag.style.color = new Color(1f, 1f, 0.6f);
                _diag.style.whiteSpace = WhiteSpace.Normal;
                _left.Add(_diag);
            }
            _wasTouch = IsTouch;
            TrackPresses(_root.panel);
            _state.Changed += Layout;
            _root.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            Layout();
        }

        /// <summary>Per frame: follows the fight state and the active pointer.</summary>
        public void Refresh(bool inFight) {
            UpdateDiagnostic();
            bool touch = IsTouch;
            if (inFight == _inFight && touch == _wasTouch
                && _state.CursorContext == _lastContext && _state.AwaitingTarget == _lastAwaiting) {
                return;
            }
            _lastContext = _state.CursorContext;
            _lastAwaiting = _state.AwaitingTarget;
            _inFight = inFight;
            _wasTouch = touch;
            Layout();
        }

        // A finger on a pad or arrow, reported by UI Toolkit's own pointer events — the path the
        // owner's phone proved works (Examine) where the polled pointer never saw a held finger.
        // Touch only: a mouse on the desktop's arrows keeps its old behaviour exactly.
        private void RegisterHold(VisualElement e, int id, bool reqArrow) {
            if (_holdUnregister.ContainsKey(e)) {
                return;
            }
            EventCallback<PointerDownEvent> down = evt => {
                if (evt.pointerType != UnityEngine.UIElements.PointerType.touch) {
                    return;
                }
                _state.PressHold(id, reqArrow);
                if (!reqArrow) {
                    e.CapturePointer(evt.pointerId);   // a REQ arrow's Clickable captures it already
                }
            };
            EventCallback<PointerUpEvent> up = _ => _state.ReleaseHold(id);
            EventCallback<PointerCancelEvent> cancel = _ => _state.ReleaseHold(id);
            EventCallback<PointerCaptureOutEvent> lost = _ => _state.ReleaseHold(id);
            // TrickleDown: a REQ arrow's Clickable handles the press first in the bubble phase and
            // stops it there, so a bubble-phase listener never heard a held arrow (owner's phone,
            // 2026-09-30). The trickle-down phase reaches this element before the Clickable does.
            e.RegisterCallback(down, TrickleDown.TrickleDown);
            e.RegisterCallback(up, TrickleDown.TrickleDown);
            e.RegisterCallback(cancel, TrickleDown.TrickleDown);
            e.RegisterCallback(lost);
            _holdUnregister[e] = () => {
                e.UnregisterCallback(down, TrickleDown.TrickleDown);
                e.UnregisterCallback(up, TrickleDown.TrickleDown);
                e.UnregisterCallback(cancel, TrickleDown.TrickleDown);
                e.UnregisterCallback(lost);
                _state.ReleaseHold(id);
            };
        }

        private void RegisterReqArrows() {
            foreach (int id in PadIds) {
                VisualElement arrow = _root.Q($"imagebutton_{id}");
                if (arrow != null) {
                    RegisterHold(arrow, id, reqArrow: true);
                }
            }
        }

        private void UpdateDiagnostic() {
            if (_diag == null || _root?.panel == null) {
                return;
            }
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var pointer = UnityEngine.InputSystem.Pointer.current;
            var touchscreen = UnityEngine.InputSystem.Touchscreen.current;
            Vector2 sp = _pointer?.ScreenPosition ?? Vector2.zero;
            Vector2 pp = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(sp.x, Screen.height - sp.y));
            VisualElement picked = _root.panel.Pick(pp);
            _diag.text = $"mouse: {(mouse == null ? "none" : mouse.name)}\n"
                + $"pointer: {(pointer == null ? "none" : pointer.GetType().Name + " " + pointer.name)}\n"
                + $"touch press: {touchscreen?.primaryTouch.press.isPressed}\n"
                + $"Primary.IsDown: {_pointer?.Primary.IsDown}\n"
                + $"pos {sp:F0} of {Screen.width}x{Screen.height}\n"
                + $"pick: {picked?.name}\n"
                + $"held (UI events): {_state.HeldTouchAction}";
        }

        private void Cycle() {
            if (_inFight) {
                _state.CycleCombat();
            } else {
                _state.CycleTravel();
            }
        }

        /// <summary>A finger is down anywhere on the panel (UI Toolkit's events, not the polled pointer).</summary>
        public bool TouchDown { get; private set; }

        /// <summary>Where that finger is, in panel coordinates.</summary>
        public Vector2 TouchPosition { get; private set; }

        /// <summary>The element the finger went down on.</summary>
        public VisualElement TouchTarget { get; private set; }

        /// <summary>Counts presses, so a hold detector restarts on every new one even if a release was missed.</summary>
        public int PressSerial { get; private set; }

        private VisualElement _releaseWatch;

        // Panel-wide (every document shares the panel), so a press on a screen raised over this one
        // still drops a stale long-press suppression.
        private void TrackPresses(IPanel panel) {
            _panelRoot = panel?.visualTree;
            _panelRoot?.RegisterCallback<PointerDownEvent>(OnPressDown, TrickleDown.TrickleDown);
            _panelRoot?.RegisterCallback<PointerMoveEvent>(OnPressMove, TrickleDown.TrickleDown);
            _panelRoot?.RegisterCallback<PointerUpEvent>(OnPressUp, TrickleDown.TrickleDown);
            _panelRoot?.RegisterCallback<PointerCancelEvent>(OnPressCancel, TrickleDown.TrickleDown);
        }

        private void OnPressDown(PointerDownEvent e) {
            if (e.pointerType != UnityEngine.UIElements.PointerType.touch) {
                return;
            }
            TouchDown = true;
            TouchPosition = e.position;
            TouchTarget = e.target as VisualElement;
            PressSerial++;
            WatchReleaseOn(TouchTarget);
            _state.OnTouchPressStarted();
        }

        private void OnPressMove(PointerMoveEvent e) {
            if (e.pointerType == UnityEngine.UIElements.PointerType.touch) {
                TouchPosition = e.position;
            }
        }

        private void OnPressUp(PointerUpEvent e) {
            if (e.pointerType == UnityEngine.UIElements.PointerType.touch) {
                TouchDown = false;
            }
        }

        private void OnPressCancel(PointerCancelEvent e) => TouchDown = false;

        // A Clickable captures the pointer on down, and UI Toolkit then sends the release to that
        // element alone: the panel-level listener never heard a finger lift (emulator logcat,
        // 2026-10-01). So the release is also watched on the pressed element itself.
        private void WatchReleaseOn(VisualElement target) {
            UnwatchRelease();
            _releaseWatch = target;
            _releaseWatch?.RegisterCallback<PointerUpEvent>(OnTargetRelease, TrickleDown.TrickleDown);
            _releaseWatch?.RegisterCallback<PointerCancelEvent>(OnTargetCancel, TrickleDown.TrickleDown);
            _releaseWatch?.RegisterCallback<PointerCaptureOutEvent>(OnTargetCaptureOut);
        }

        private void UnwatchRelease() {
            _releaseWatch?.UnregisterCallback<PointerUpEvent>(OnTargetRelease, TrickleDown.TrickleDown);
            _releaseWatch?.UnregisterCallback<PointerCancelEvent>(OnTargetCancel, TrickleDown.TrickleDown);
            _releaseWatch?.UnregisterCallback<PointerCaptureOutEvent>(OnTargetCaptureOut);
            _releaseWatch = null;
        }

        private void OnTargetRelease(PointerUpEvent e) => TouchDown = false;

        private void OnTargetCancel(PointerCancelEvent e) => TouchDown = false;

        private void OnTargetCaptureOut(PointerCaptureOutEvent e) => TouchDown = false;

        private void OnGeometry(GeometryChangedEvent _) => Layout();

        private void Layout() {
            if (_root == null) {
                return;
            }
            ClearArrowZones();
            if (IsTouch) {
                RegisterReqArrows();
            }
            float h = _root.layout.height;
            float leftW = _stage.layout.x;
            float rightX = _stage.layout.xMax;
            float rightW = _root.layout.width - rightX;
            bool bars = !float.IsNaN(leftW) && !float.IsNaN(rightW)
                && leftW >= _layout.MinBarWidth && rightW >= _layout.MinBarWidth;
            bool travel = !_inFight;
            TouchTravelVariant v = _state.Travel;
            bool cursorFight = _inFight && _state.Combat == TouchCombatVariant.Cursor;
            if (IsTouch && travel && v == TouchTravelVariant.Minimal) {
                AddArrowZones();
            }
            bool show = IsTouch && bars;
            Place(_left, 0, 0, leftW, h, show);
            Place(_right, rightX, 0, rightW, h, show);
            if (!show) {
                return;
            }

            // Pads: T1 all four in the left bar; T2 turning left, walking right; T3 none.
            foreach (KeyValuePair<int, VisualElement> kv in _pads) {
                bool walk = kv.Key == Forward || kv.Key == Back;
                // In a C3 fight the pad moves the combat cursor: always the four-way thumb layout.
                bool split = v == TouchTravelVariant.SplitPads && !cursorFight;
                VisualElement bar = split && walk ? _right : _left;
                if (kv.Value.parent != bar) {
                    bar.Add(kv.Value);
                }
                float barW = bar == _left ? leftW : rightW;
                float cell = barW * _layout.PadSize / 3f;
                var centre = new Vector2(barW / 2f, h * _layout.PadCentreY);
                Vector2 off = kv.Key switch {
                    Forward => new Vector2(0, -cell),
                    Back => new Vector2(0, cell),
                    TurnLeft => new Vector2(-cell, 0),
                    _ => new Vector2(cell, 0),
                };
                Place(kv.Value, centre.x + off.x - cell / 2f, centre.y + off.y - cell / 2f, cell, cell,
                    (travel && v != TouchTravelVariant.Minimal) || cursorFight);
            }

            float bw = rightW * _layout.ButtonWidth;
            float bh = h * _layout.ButtonHeight;
            float bx = (rightW - bw) / 2f;
            // C1/C2: Thrust and Swing whenever a fight is on. C3: the buttons for the cell under
            // the cursor — Thrust/Swing on a combatant, Move on ground — renamed while a spell or
            // item waits for a target, since the same clicks then cast it.
            bool onTarget = !cursorFight || _state.CursorContext == CursorContext.Target;
            bool onGround = cursorFight && _state.CursorContext == CursorContext.Ground;
            bool casting = cursorFight && _state.AwaitingTarget;
            SetLabel(_thrust, casting ? "Cast" : "Thrust");
            SetLabel(_move, casting ? "Cast here" : "Move");
            Place(_thrust, bx, h * _layout.ThrustY, bw, bh, _inFight && onTarget);
            Place(_swing, bx, h * _layout.SwingY, bw, bh, _inFight && onTarget && !casting);
            Place(_move, bx, h * _layout.ThrustY, bw, bh, onGround);
            // The cycle and grid buttons: small squares in the bar's top corner (owner, 2026-10-01:
            // the grid toggle should be much more unobtrusive).
            float cs = rightW * _layout.CycleSize;
            float m = rightW * _layout.CycleMargin;
            Place(_cycle, rightW - cs - m, m, cs, cs, true);
            Place(_grid, rightW - 2 * (cs + m), m, cs, cs, _inFight);
        }

        // T3: invisible, larger touch areas behind the four REQ arrows, inside the stage. ALL of them
        // go before the FIRST arrow: REQ_MAIN orders the arrows 75, 72, 80, 77, so a zone placed just
        // before its own arrow sat above the earlier ones and stole taps on their art.
        private void AddArrowZones() {
            VisualElement panel = null;
            int behind = int.MaxValue;
            foreach (int id in PadIds) {
                VisualElement arrow = _root.Q($"imagebutton_{id}");
                if (arrow?.parent != null) {
                    panel = arrow.parent;
                    behind = System.Math.Min(behind, panel.IndexOf(arrow));
                }
            }
            if (panel == null) {
                return;
            }
            foreach (int id in PadIds) {
                VisualElement arrow = _root.Q($"imagebutton_{id}");
                if (arrow?.parent != panel) {
                    continue;
                }
                Rect r = arrow.layout;
                float g = _layout.ArrowTouchGrow;
                var zone = new VisualElement { name = $"touchpad_{id}", pickingMode = PickingMode.Position };
                zone.style.position = Position.Absolute;
                zone.style.left = r.center.x - r.width * g / 2f;
                zone.style.top = r.center.y - r.height * g / 2f;
                zone.style.width = r.width * g;
                zone.style.height = r.height * g;
                panel.Insert(behind++, zone);
                RegisterHold(zone, id, reqArrow: false);
                _arrowZones.Add(zone);
            }
        }

        private void ClearArrowZones() {
            foreach (VisualElement z in _arrowZones) {
                if (_holdUnregister.TryGetValue(z, out System.Action undo)) {
                    undo();
                    _holdUnregister.Remove(z);
                }
                z.RemoveFromHierarchy();
            }
            _arrowZones.Clear();
        }

        private static VisualElement Bar(string name) {
            var bar = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            bar.style.position = Position.Absolute;
            return bar;
        }

        private static VisualElement Button(string name, string label, System.Action onClick) {
            var b = new VisualElement { name = name, pickingMode = PickingMode.Position };
            b.AddToClassList("touch-button");
            b.style.position = Position.Absolute;
            b.Add(new Label(label) { pickingMode = PickingMode.Ignore });
            if (onClick != null) {
                b.RegisterCallback<ClickEvent>(_ => onClick());
            }
            return b;
        }

        private static void SetLabel(VisualElement button, string text) {
            Label label = button.Q<Label>();
            if (label != null) {
                label.text = text;
            }
        }

        private static string Glyph(int id) => id switch {
            Forward => "▲",
            Back => "▼",
            TurnLeft => "◀",
            _ => "▶",
        };

        private static void Place(VisualElement e, float x, float y, float w, float h, bool visible) {
            e.style.left = x;
            e.style.top = y;
            e.style.width = w;
            e.style.height = h;
            e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Dispose() {
            if (_state != null) {
                _state.Changed -= Layout;
            }
            _root?.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
            _panelRoot?.UnregisterCallback<PointerDownEvent>(OnPressDown, TrickleDown.TrickleDown);
            _panelRoot?.UnregisterCallback<PointerMoveEvent>(OnPressMove, TrickleDown.TrickleDown);
            _panelRoot?.UnregisterCallback<PointerUpEvent>(OnPressUp, TrickleDown.TrickleDown);
            _panelRoot?.UnregisterCallback<PointerCancelEvent>(OnPressCancel, TrickleDown.TrickleDown);
            _panelRoot = null;
            UnwatchRelease();
            ClearArrowZones();
            foreach (System.Action undo in _holdUnregister.Values) {
                undo();
            }
            _holdUnregister.Clear();
            _left?.RemoveFromHierarchy();
            _right?.RemoveFromHierarchy();
            _root = null;
        }
    }
}
