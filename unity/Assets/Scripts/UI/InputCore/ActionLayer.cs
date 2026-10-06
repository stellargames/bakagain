namespace BakAgain.UI.InputCore {
    using System;

    // A non-navigable Exclusive input layer: full-frame content (cutscene, credits, book, a narrative
    // dialog) with no focusable widgets. Maps discrete intents to caller-supplied handlers. Replaces the
    // per-screen _cancelArmed/_interactArmed/_dismissArmed press-release bookkeeping — the stack
    // guarantees only the top layer receives intents, so the open-then-resolve fall-through is gone.
    public sealed class ActionLayer : IInputLayer {
        private readonly Action _onActivate;            // Enter / click / Skip (interact)
        private readonly Action _onCancel;              // Esc / Cancel
        private readonly Action<NavDirection> _onMove;  // optional (book page turn)
        private readonly Func<char, bool> _onAccelerator; // optional (letter key, see ctor doc)
        private readonly bool _anyIntentActivates;      // attract/dismiss-on-any-input (the intro)
        private readonly bool _skipActivates;           // pointer-click == activate (see ctor doc)

        /// <param name="skipActivates">Whether the Skip intent — which InputAdapter synthesises
        /// from EVERY pointer release — also activates. True for full-frame dismiss-on-click
        /// surfaces (cutscene, credits, narrative dialog). False for a layer whose surface has
        /// its own positional buttons (the quantity picker): there a click must mean only what
        /// the clicked element says, or every button press would also fire the layer's
        /// activate.</param>
        /// <param name="onAccelerator">Letter-key handler, returning whether it claimed the key.
        /// A full-frame surface can still have keyboard shortcuts even though it has no focusable
        /// widgets to first-letter-match against — the quantity picker's S (share) and G (give),
        /// which the original reads as raw scancodes (INVINSP.C:158-166). Null (the default)
        /// leaves Accelerator unhandled, exactly as before.</param>
        public ActionLayer(string id, Action onActivate, Action onCancel, Action<NavDirection> onMove = null,
            bool anyIntentActivates = false, bool skipActivates = true,
            Func<char, bool> onAccelerator = null) {
            Id = id;
            _onActivate = onActivate;
            _onCancel = onCancel;
            _onMove = onMove;
            _anyIntentActivates = anyIntentActivates;
            _skipActivates = skipActivates;
            _onAccelerator = onAccelerator;
        }

        public string Id { get; }
        public CaptureMode CaptureMode => CaptureMode.Exclusive;
        public bool WantsFocus => true;

        public bool HandleIntent(UiIntent intent) {
            switch (intent.Kind) {
                case UiIntentKind.Skip:
                    // A screen that reads one of these keys by scancode gets it first (TASK-809).
                    if (intent.Character != '\0' && _onAccelerator != null && _onAccelerator(intent.Character)) {
                        return true;
                    }
                    if (!_skipActivates) {
                        return true; // consumed: a positional click is the buttons' business
                    }
                    goto case UiIntentKind.Activate;
                case UiIntentKind.Activate:
                    _onActivate?.Invoke();
                    return true;
                case UiIntentKind.Cancel:
                    _onCancel?.Invoke();
                    return true;
                case UiIntentKind.MoveFocus:
                    if (_onMove != null) {
                        _onMove(intent.Direction);
                        return true;
                    }
                    break;
                case UiIntentKind.Accelerator:
                    // An unclaimed letter falls through to the _anyIntentActivates check below, so
                    // the intro still exits on any key.
                    if (_onAccelerator != null && _onAccelerator(intent.Character)) {
                        return true;
                    }
                    break;
            }
            // Attract/dismiss-on-any-input surfaces (the intro cutscene) treat EVERY other intent — a
            // first-letter accelerator, an arrow with no page handler — as an activate, matching
            // PlayIntro (@0x20bbc) where any key or click exits. Normal layers ignore these (false).
            if (_anyIntentActivates) {
                _onActivate?.Invoke();
                return true;
            }
            return false;
        }

        public void OnPushed() { }
        public void OnPopped() { }
        public void OnActiveChanged(bool isActive) { }
    }
}
