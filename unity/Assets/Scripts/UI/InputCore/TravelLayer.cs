namespace BakAgain.UI.InputCore {
    using System;
    using System.Collections.Generic;
    using UnityEngine.UIElements;

    // The in-game travel surface's input layer: a Passive base gameplay layer and a PEER of
    // NavigableLayer, deliberately NOT a NavigableLayer — on this surface arrows mean MOVE, not
    // focus-navigate, so it never does focus nav. It sits on the stack so (a) a modal above makes
    // its buttons inert (OnActiveChanged) and (b) movement can be gated on "travel is the resolved
    // input target" (TravelLayerHost.IsInputActive). Movement itself is a separate ClassicMovementDriver,
    // not handled here. Design: docs/superpowers/specs/2026-07-02-ingame-inputcore-cutover-design.md.
    public sealed class TravelLayer : IInputLayer {
        private readonly IReadOnlyList<NavWidget> _widgets;
        private readonly Action _onCancel;  // Esc -> open the game/options menu

        // A letter the HUD has a button for. REQ_MAIN's action ids ARE DOS scancodes, so the
        // original needs no mapping at all -- see GameData TravelHotkeys. Optional: a harness with
        // no HUD passes nothing and letters keep being swallowed as before.
        private readonly Action<char> _onAccelerator;

        public TravelLayer(string id, IReadOnlyList<NavWidget> widgets, Action onCancel,
            Action<char> onAccelerator = null) {
            Id = id;
            _widgets = widgets;
            _onCancel = onCancel;
            _onAccelerator = onAccelerator;
        }

        public string Id { get; }
        public CaptureMode CaptureMode => CaptureMode.Passive;
        public bool WantsFocus => true;

        public bool HandleIntent(UiIntent intent) {
            switch (intent.Kind) {
                case UiIntentKind.Cancel:
                    _onCancel?.Invoke();
                    return true;
                // *** A LETTER IS A BUTTON PRESS HERE. *** REQ_MAIN's action ids are the DOS
                // scancodes of the keys that press them, so M is the map, E is encamp and so on --
                // the original dispatches the scancode straight into menupage_run and needs no
                // table (TASK-584). Still consumed either way, so the travel surface owns the key
                // whether or not the HUD has a button for it.
                case UiIntentKind.Accelerator:
                    _onAccelerator?.Invoke(intent.Character);
                    return true;
                // Arrows are MOVEMENT here (handled by the gated ClassicMovementDriver, not as focus-nav);
                // Enter has no travel meaning. Consume so nothing reinterprets these.
                case UiIntentKind.MoveFocus:
                case UiIntentKind.Activate:
                    return true;
                default:
                    return false; // Skip is for full-frame ActionLayers
            }
        }

        public void OnPushed() { }
        public void OnPopped() { }

        // Only widget pickability — the movement gate is TravelLayerHost.IsInputActive. An Exclusive
        // layer above (a true modal over the HUD) makes the travel buttons inert; restored on close.
        public void OnActiveChanged(bool isActive) {
            foreach (NavWidget w in _widgets) {
                if (w.Element == null) {
                    continue;
                }
                w.Element.focusable = isActive;
                w.Element.pickingMode = isActive ? PickingMode.Position : PickingMode.Ignore;
            }
        }
    }
}
