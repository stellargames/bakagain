namespace BakAgain.UI.InputCore {
    using System;
    using UnityEngine;
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.UI;
    using VContainer.Unity;

    // The device adapter for discrete UI intents (nav/activate/cancel/accelerator + text): translates the
    // player's key actions (SystemInputSource's remappable keys, TASK-1080) and the UI map's Click into
    // IUiCommands intents, which the
    // InputLayerStack routes to the top layer. (Positional/analog input — pointer position/clicks and
    // gameplay movement — is the separate SystemInputSource adapter behind IPointer/IGameplayInput.) Detaches the
    // scene InputSystemUIInputModule's navigate/submit/cancel so device nav/activate flows through here
    // (one owner); the module keeps only pointer (Point/Click) for positional UI Toolkit hit-testing.
    public sealed class InputAdapter : IStartable, ITickable, IDisposable {
        private readonly IUiCommands _commands;
        private DefaultInputActions _actions;
        // Pointer-click gesture tracking: a click fires Skip on RELEASE, and only if the same layer is
        // still on top as when the press began. This gives the click "capture" semantics at the intent
        // layer (no per-surface scrim): the menu revealed by a skip can't be hit by the same click's
        // release (Skip is the release), and a release whose press happened on a prior screen is ignored
        // (the top layer changed). Replaces the old per-screen press/release armed flags.
        private bool _clickArmed;
        private IInputLayer _clickArmedTop;
        private bool _moduleDetached;
        // The keyboard currently subscribed to onTextInput (decision P, task-2-input-routing-decision.md:
        // there is no native UITK text path — the scene module is pointer-only — so this adapter owns
        // text). Tracked (rather than subscribing once in Start) so a device hot-swap re-subscribes,
        // mirroring TryDetachModule's "keep trying" pattern for a resource that isn't guaranteed to
        // exist yet.
        private Keyboard _textInputKeyboard;
        // Whether the platform's text input is switched on (TASK-1058).
        private bool _textInputOn;

        // The DefaultInputActions is injected (DI-owned via SharedInputActions), not new'd. The scene
        // InputSystemUIInputModule keeps its own asset for pointer events.
        public InputAdapter(IUiCommands commands, SharedInputActions shared, SystemInputSource keys) {
            _commands = commands;
            _actions = shared?.Actions;
            _keys = keys;
        }

        // The keys (TASK-1080). Enter/Escape/the arrows used to come from the UI map's Submit, Cancel and
        // Navigate, whose bindings are usage paths a player cannot move one key of; the map now only clicks.
        private readonly SystemInputSource _keys;
        private readonly System.Action<InputAction.CallbackContext>[] _onNav = new System.Action<InputAction.CallbackContext>[4];

        private static readonly NavDirection[] NavDirections = { NavDirection.Up, NavDirection.Down, NavDirection.Left, NavDirection.Right };

        public void Start() {
            _actions.UI.Enable();
            _keys.Activate.performed += OnSubmit;
            _keys.CancelKey.performed += OnCancel;
            for (int i = 0; i < 4; i++) {
                NavDirection d = NavDirections[i];
                _onNav[i] = ctx => OnNavigate(ctx, d);
                _keys.NavArrows[i].performed += _onNav[i];
            }
            _actions.UI.Click.performed += OnClick;
            _actions.UI.Click.canceled += OnClick;
            TryDetachModule();
            EnsureTextInputSubscription();
        }

        // Ctrl-modifier state for ScreenInputLayer.OnDirection's ctrl argument. Mirrors the `shift` read
        // in Tick() below, but shift stays local there (it only picks Tab's Next/Previous); ctrl needs
        // to reach a different class (ScreenInputLayer), so it's exposed here as a live keyboard read
        // rather than threaded through UiIntent/IUiCommands (which every other layer would then have to
        // ignore).
        public static bool CtrlHeld {
            get {
                Keyboard kb = Keyboard.current;
                return kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
            }
        }

        // (Re-)subscribe to the live Keyboard's onTextInput so InputAdapter — not UI Toolkit's panel,
        // which the pointer-only module never feeds keyboard/text into (task-2 decision P) — owns typed
        // characters. Keyboard.current can be null before a device is seen, or can change on hot-swap;
        // called from Start and every Tick so both cases self-heal, same as TryDetachModule.
        private void EnsureTextInputSubscription() {
            Keyboard kb = Keyboard.current;
            if (ReferenceEquals(kb, _textInputKeyboard)) {
                return;
            }
            if (_textInputKeyboard != null) {
                _textInputKeyboard.onTextInput -= OnTextInput;
            }
            _textInputKeyboard = kb;
            if (kb != null) {
                kb.onTextInput += OnTextInput;
            }
        }

        // The onTextInput text channel (decision P): forward each character straight to the top layer's
        // IScreenInput when it WantsText (SAVE). Every other layer (NavigableLayer, ActionLayer, a
        // non-text ScreenInputLayer like LOAD) ignores this entirely — type-0 behavior is untouched,
        // since this is a brand new event source, not a change to an existing one.
        private void OnTextInput(char c) {
            // onTextInput can surface control characters on some platforms/IMEs (e.g. \b, \r); those are
            // handled by the dedicated edit-key path below, not as typed text — forwarding them here
            // would double-drive the same key (single-owner invariant).
            if (char.IsControl(c)) {
                return;
            }
            if (_commands.TopLayer is ScreenInputLayer screenLayer && screenLayer.Screen.WantsText) {
                screenLayer.Screen.OnText(c);
            }
        }

        // Strip the scene UI module to pointer-only so device navigation/activation has a single owner
        // (this adapter). Leaving the module's move/submit also live would double-drive UITK focus. The
        // module may not exist yet at Start (scene/order), so this is retried each Tick until it's found.
        private void TryDetachModule() {
            InputSystemUIInputModule module =
                UnityEngine.Object.FindAnyObjectByType<InputSystemUIInputModule>();
            if (module == null) {
                return;
            }
            module.move = null;
            module.submit = null;
            module.cancel = null;
            _moduleDetached = true;
        }

        private void OnSubmit(InputAction.CallbackContext _) => _commands.Activate();
        private void OnCancel(InputAction.CallbackContext _) => _commands.Cancel();

        // A pointer click (mouse / tap) is a Skip intent for full-frame ActionLayers (cutscene,
        // credits, narrative dialog). Fire on the RELEASE, and only if the top layer is unchanged since
        // the press, so the gesture behaves as if captured by whatever was on top when it began:
        //   - the menu revealed by a skip can't be activated by the same click (Skip IS the release,
        //     and the menu appears after it);
        //   - a release whose press landed on a prior screen is ignored (top layer changed).
        // NavigableLayer ignores Skip and menu clicks are handled positionally by the module's own
        // Click, so this never double-fires on menus.
        private void OnClick(InputAction.CallbackContext ctx) {
            if (ctx.ReadValueAsButton()) {
                _clickArmed = true;
                _clickArmedTop = _commands.TopLayer;
                return;
            }
            if (_clickArmed && ReferenceEquals(_commands.TopLayer, _clickArmedTop)) {
                _commands.Skip();
            }
            _clickArmed = false;
        }

        // One MoveFocus per press of a direction key (the arrows, the dpad or the stick by default).
        // A text-wanting type-2 screen (SAVE) types a key that prints a character: moved onto a letter, the
        // direction would otherwise type it AND move the Games picker / caret out from under it.
        private void OnNavigate(InputAction.CallbackContext ctx, NavDirection direction) {
            if (_commands.TopLayer is ScreenInputLayer screenLayer && screenLayer.Screen.WantsText
                && ctx.control is UnityEngine.InputSystem.Controls.KeyControl key && Prints(key)) {
                return;
            }
            _commands.MoveFocus(direction);
        }

        private static bool Prints(UnityEngine.InputSystem.Controls.KeyControl key) =>
            key.keyCode is >= Key.A and <= Key.Z or >= Key.Digit1 and <= Key.Digit0 or Key.Space
            || key.displayName is { Length: 1 };

        // The polled keys (Tab, Backspace, the keypad, the page keys, the hotkeys, the Skip). First match per frame wins.
        public void Tick() {
            // The scene UI module may not have existed at Start — keep trying until it's stripped.
            if (!_moduleDetached) {
                TryDetachModule();
            }
            // Keyboard.current can appear/change after Start (device not yet seen, or hot-swap).
            EnsureTextInputSubscription();
            // Disarm a click whose press-context is gone: if the top layer changed since the press, the
            // pending release must not skip (belt-and-suspenders with the release-time guard in OnClick).
            if (_clickArmed && !ReferenceEquals(_commands.TopLayer, _clickArmedTop)) {
                _clickArmed = false;
            }
            Keyboard kb = Keyboard.current;
            if (kb == null) {
                return;
            }
            // A type-2 (InteractiveScreen) screen owns arrows + Enter outright: no first-letter
            // accelerators, and (when it has a text field) Backspace/Delete/Home/End/numpad-Left/Right
            // are caret edits, not focus moves. Every branch below that reads these two is new — gated
            // on the top layer actually being a ScreenInputLayer — so type-0/NavigableLayer screens are
            // byte-identical to before this change.
            ScreenInputLayer screenLayer = _commands.TopLayer as ScreenInputLayer;
            bool wantsText = screenLayer != null && screenLayer.Screen.WantsText;
            // The Linux player sends no text at all until text input is switched on: X hands it the
            // characters and its SDL layer drops them while TEXTINPUT is off, and only the legacy
            // imeCompositionMode turns it on there (Keyboard.SetIMEEnabled does not; TASK-1058). On only
            // while a text field wants it, so elsewhere letters stay keys and no IME window can open.
            if (wantsText != _textInputOn) {
                UnityEngine.Input.imeCompositionMode = wantsText ? IMECompositionMode.On : IMECompositionMode.Auto;
                kb.SetIMEEnabled(wantsText);
                _textInputOn = wantsText;
            }
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (_keys.NextFocus.WasPressedThisFrame()) {
                _commands.MoveFocus(shift ? NavDirection.Previous : NavDirection.Next);
                return;
            }
            // Backspace = "go back" (Previous): the book page layer turns Previous/Left into a
            // backward page (the Left arrow already does so via the UI Navigate map); in menus it
            // mirrors Shift+Tab. Not in the UI action map, so poll it here. For a text-wanting type-2
            // screen, Backspace instead deletes the character before the caret (OnEdit), matching what
            // the key means in a text field rather than "go back a widget".
            if (_keys.PreviousFocus.WasPressedThisFrame()) {
                if (wantsText) {
                    screenLayer.Screen.OnEdit(EditKey.Backspace);
                } else {
                    _commands.MoveFocus(NavDirection.Previous);
                }
                return;
            }
            if (_keys.NavPad[0].WasPressedThisFrame()) { _commands.MoveFocus(NavDirection.Up); return; }
            if (_keys.NavPad[1].WasPressedThisFrame()) { _commands.MoveFocus(NavDirection.Down); return; }
            // numpad4/6 mirror the Left/Right arrows for focus/picker nav, but a text-wanting type-2
            // screen treats them as caret movement (OnEdit) instead — same rationale as Backspace above.
            if (_keys.NavPad[2].WasPressedThisFrame()) {
                if (wantsText) {
                    screenLayer.Screen.OnEdit(EditKey.Left);
                } else {
                    _commands.MoveFocus(NavDirection.Left);
                }
                return;
            }
            if (_keys.NavPad[3].WasPressedThisFrame()) {
                if (wantsText) {
                    screenLayer.Screen.OnEdit(EditKey.Right);
                } else {
                    _commands.MoveFocus(NavDirection.Right);
                }
                return;
            }
            // PgUp/PgDn are the coarse step (menu_pollInput scancodes 0x49/0x51 — the quantity
            // picker's ±5, INVINSP.C:138-162). Not in the UI action map, so polled here like Tab.
            // Only a layer that has a coarse step acts on them; the rest ignore the direction (see
            // NavDirection), so this is purely additive. Gated out of a text-wanting type-2 screen
            // for the same reason Backspace is: there the key belongs to the field, not to nav.
            if (!wantsText) {
                if (_keys.PageUp.WasPressedThisFrame()) { _commands.MoveFocus(NavDirection.PageUp); return; }
                if (_keys.PageDown.WasPressedThisFrame()) { _commands.MoveFocus(NavDirection.PageDown); return; }
            }
            // Delete/Home/End are caret editing in a text field, so they go to OnEdit there. On a
            // type-2 screen with NO text field they mean nothing to a caret, and the overhead map
            // wants them as its five-step zoom (scancodes 0x47/0x4f), so they arrive as the First/Last
            // directions instead. Delete has no such reading and stays text-only.
            if (wantsText) {
                if (kb.deleteKey.wasPressedThisFrame) { screenLayer.Screen.OnEdit(EditKey.Delete); return; }
                if (_keys.First.WasPressedThisFrame()) { screenLayer.Screen.OnEdit(EditKey.Home); return; }
                if (_keys.Last.WasPressedThisFrame()) { screenLayer.Screen.OnEdit(EditKey.End); return; }
            } else if (screenLayer != null) {
                if (_keys.First.WasPressedThisFrame()) { _commands.MoveFocus(NavDirection.First); return; }
                if (_keys.Last.WasPressedThisFrame()) { _commands.MoveFocus(NavDirection.Last); return; }
            }
            // A-Z: a SCREEN THAT IS TYPING never gets this — SAVE's letters arrive as typed text
            // via onTextInput, and handing them here too would double every keystroke.
            //
            // *** A type-2 screen that is NOT typing DOES get it. *** This used to gate on any
            // ScreenInputLayer at all, on the grounds that such a screen "owns arrows + Enter" and
            // does no first-letter matching. The matching part is still true and still nobody's job
            // here -- but a letter on a non-typing page is a BUTTON PRESS in the original, because
            // REQ action ids are DOS scancodes (GameData TravelHotkeys). That is how M closes the
            // map it opened, and with the old gate the keystroke never left this method (TASK-584).
            // ScreenInputLayer decides what to do with it; LOAD's OnText is empty, so LOAD is
            // unchanged.
            if (wantsText) {
                return;
            }
            // A hotkey is its letter's action: by default the key that prints the letter, else the US
            // position (SystemInputSource.LetterPath), or wherever the player moved it. Then the language
            // pack's letter map (TASK-784: "Kaart" opens on K, and M goes dead).
            for (int i = 0; i < 26; i++) {
                if (_keys.Letters[i].WasPressedThisFrame()) {
                    char letter = GameData.Resources.Text.LetterHotkeys.Resolve(
                        BakAgain.ResourceManagement.LanguagePacks.Current, (char)('a' + i));
                    if (letter is >= 'a' and <= 'z') {
                        _commands.Accelerator(letter);
                    }
                    return;
                }
            }
            // A digit on a REQ page is a button press like a letter: '1'..'0' are scancodes 2..11,
            // the portraits' and the Contents chapters' ids — on menus (TASK-796), the travel HUD and
            // the map (TASK-798). Over a full-frame dialog it stays the Skip below, which pages it on.
            // (wantsText returned above, so a typing screen never gets here.)
            if (_commands.TopLayer is NavigableLayer || _commands.TopLayer is TravelLayer
                || _commands.TopLayer is ScreenInputLayer) {
                for (int i = 0; i < 10; i++) {
                    if (_keys.Digits[i].WasPressedThisFrame()) {
                        _commands.Accelerator(SystemInputSource.DigitChar(i));
                        return;
                    }
                }
            }
            // *** EVERY OTHER KEY IS A SKIP. *** dialog_poll_arrow_or_button (DIALOG.C:161-171) hands
            // its caller EVERY scancode it reads and discards only the four arrows and
            // NumLock/ScrollLock, so in the original a space, a digit, F1 or keypad 5 all page a
            // dialog on. Nothing above claimed this key, so it arrives as Skip — the one intent
            // full-frame ActionLayers (cutscene, credits, narrative dialog) act on and every menu
            // layer explicitly ignores, which is what makes it safe to fire from the shared adapter.
            // Measured before this existed: only letters (Accelerator) and Enter (Submit) advanced a
            // dialog; Space, Digit1, Period, F1 and Numpad5 all left it on the same page.
            if (!kb.anyKey.wasPressedThisFrame) {
                return;
            }
            foreach (UnityEngine.InputSystem.Controls.KeyControl key in kb.allKeys) {
                if (key.wasPressedThisFrame && !ClaimedBeforeSkip(key)) {
                    _commands.Skip(key.keyCode switch {
                        Key.Space => ' ', Key.Comma => ',', Key.Period => '.', _ => '\0',
                    });
                    return;
                }
            }
        }

        // Keys the catch-all above must NOT re-send: the ones the event-driven actions already turned into
        // another intent (Activate, Cancel and the direction keys, wherever the player put them), and the
        // ones the original never sees — DIALOG.C:168 drops NumLock/ScrollLock by name, and a bare
        // modifier leaves no scancode in the BIOS buffer at all.
        private bool ClaimedBeforeSkip(UnityEngine.InputSystem.Controls.KeyControl control) {
            if (SystemInputSource.Binds(_keys.Activate, control) || SystemInputSource.Binds(_keys.CancelKey, control)) {
                return true;
            }
            foreach (InputAction nav in _keys.NavArrows) {
                if (SystemInputSource.Binds(nav, control)) {
                    return true;
                }
            }
            switch (control.keyCode) {
                case Key.LeftShift:
                case Key.RightShift:
                case Key.LeftCtrl:
                case Key.RightCtrl:
                case Key.LeftAlt:
                case Key.RightAlt:
                case Key.LeftMeta:
                case Key.RightMeta:
                case Key.NumLock:
                case Key.ScrollLock:
                case Key.CapsLock:
                case Key.None:
                    return true;
                default:
                    return false;
            }
        }

        public void Dispose() {
            if (_keys.Activate != null) {
                _keys.Activate.performed -= OnSubmit;
                _keys.CancelKey.performed -= OnCancel;
                for (int i = 0; i < 4; i++) {
                    if (_onNav[i] != null) _keys.NavArrows[i].performed -= _onNav[i];
                }
            }
            if (_textInputKeyboard != null) {
                _textInputKeyboard.onTextInput -= OnTextInput;
                _textInputKeyboard = null;
            }
            if (_actions == null) {
                return;
            }
            _actions.UI.Click.performed -= OnClick;
            _actions.UI.Click.canceled -= OnClick;
            // The DefaultInputActions is shared (DI-owned): unsubscribe our
            // handlers but don't dispose the instance here.
            _actions = null;
        }
    }
}
