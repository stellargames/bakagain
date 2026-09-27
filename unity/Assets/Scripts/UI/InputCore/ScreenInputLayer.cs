namespace BakAgain.UI.InputCore {
    using System;

    // The IInputLayer for a type-2 (InteractiveScreen) REQ menu — SAVE/LOAD. Exclusive, like
    // NavigableLayer, but deliberately does NOT do NavigableLayer's job: no focus-warp, no widget list,
    // no first-letter accelerator matching. Every intent is handed straight to the screen's own
    // IScreenInput, which owns arrows + Tab + Enter + Esc outright. Typed text bypasses this layer
    // entirely — InputAdapter forwards Keyboard.onTextInput straight to IScreenInput.OnText when
    // Screen.WantsText is true (task-2 decision P) — so HandleIntent never needs an Accelerator case.
    public sealed class ScreenInputLayer : IInputLayer {
        private readonly IScreenInput _screen;

        public ScreenInputLayer(string id, IScreenInput screen) {
            Id = id;
            _screen = screen ?? throw new ArgumentNullException(nameof(screen));
        }

        public string Id { get; }
        public CaptureMode CaptureMode => CaptureMode.Exclusive;
        public bool WantsFocus => true;

        // Exposed so InputAdapter can recognise "the top layer is a type-2 screen" and (a) suppress the
        // A-Z accelerator drive, (b) route text/edit keys straight to Screen instead of through intents.
        public IScreenInput Screen => _screen;

        public bool HandleIntent(UiIntent intent) {
            switch (intent.Kind) {
                case UiIntentKind.MoveFocus: return HandleMove(intent.Direction);
                case UiIntentKind.Activate:
                    _screen.OnSubmit();
                    return true;
                case UiIntentKind.Cancel:
                    _screen.OnCancel();
                    return true;
                case UiIntentKind.Accelerator:
                    // *** A LETTER IS A SHORTCUT ON A SCREEN THAT IS NOT TYPING. *** Type-2 screens
                    // still do no first-letter MATCHING against widget labels, which is what the
                    // note here used to say and is still true. But a screen with WantsText false has
                    // no text channel at all, and in the original a letter on such a page is simply
                    // its scancode arriving at menupage_run -- which is how M closes the map it
                    // opened (MAP.C:398, and see GameData TravelHotkeys). A screen that IS typing
                    // keeps every letter as text, so SAVE and LOAD are untouched.
                    if (!_screen.WantsText) {
                        _screen.OnText(intent.Character);
                        return true;
                    }
                    return false;
                default:
                    // Skip: full-frame ActionLayers only.
                    return false;
            }
        }

        private bool HandleMove(NavDirection dir) {
            switch (dir) {
                case NavDirection.Up:
                case NavDirection.Down:
                case NavDirection.Left:
                case NavDirection.Right:
                // PageUp/PageDown and First/Last (Home/End) reach the screen too: the overhead map
                // zooms with them. SAVE/LOAD return false for a direction they don't use, which is
                // what this layer did for them before, so nothing there changes.
                case NavDirection.PageUp:
                case NavDirection.PageDown:
                case NavDirection.First:
                case NavDirection.Last:
                    // ctrl comes from the same live keyboard read InputAdapter uses for shift; exposed
                    // statically rather than threaded through UiIntent (see InputAdapter.CtrlHeld).
                    return _screen.OnDirection(dir, InputAdapter.CtrlHeld);
                case NavDirection.Next:
                    return _screen.OnTab(shift: false);
                case NavDirection.Previous:
                    return _screen.OnTab(shift: true);
                default:
                    return false;
            }
        }

        public void OnPushed() { }
        public void OnPopped() { }
        public void OnActiveChanged(bool isActive) { }
    }
}
