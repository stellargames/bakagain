namespace BakAgain.UI.InputCore {
    // Backspace/Delete/Left/Right/Home/End for caret editing inside a text field. Only meaningful for
    // screens where WantsText is true (SAVE); a screen with no text field (LOAD) can no-op every case.
    public enum EditKey { Backspace, Delete, Left, Right, Home, End }

    // Implemented by type-2 ("InteractiveScreen") REQ controllers — SAVE/LOAD — that own arrows + Enter
    // outright: no first-letter accelerator matching, no keyboard focus-warp (see
    // .superpowers/sdd/task-3-brief.md). ScreenInputLayer routes UiIntents (MoveFocus/Activate/Cancel)
    // to this interface's methods instead of doing NavigableLayer-style widget navigation. Typed text
    // does NOT arrive as a UiIntent: per the task-2 spike decision (decision P,
    // .superpowers/sdd/task-2-input-routing-decision.md), the module is pointer-only and never dispatches
    // keyboard/text to the UITK panel, so InputAdapter owns text directly via Keyboard.onTextInput and
    // calls OnText on this interface when WantsText is true — there is no double-insert to guard against.
    public interface IScreenInput {
        // True routes typed printable characters to OnText and Backspace/Delete/Left/Right/Home/End to
        // OnEdit instead of focus movement (SAVE). False (LOAD) means this screen has no text field —
        // OnText/OnEdit are never called by the adapter, and arrow/Tab intents still reach OnDirection/
        // OnTab as normal list/button navigation.
        bool WantsText { get; }

        // Up/Down always mean list/field navigation; Left/Right too — the screen decides whether that's
        // a caret move (WantsText) or a column/picker move (LOAD). ctrl mirrors the same keyboard
        // modifier InputAdapter reads for Shift+Tab. Return true if consumed.
        bool OnDirection(NavDirection dir, bool ctrl);

        // Tab / Shift+Tab.
        bool OnTab(bool shift);

        // A typed printable character. Only ever called when WantsText is true.
        void OnText(char c);

        // Only ever called when WantsText is true. Return true if consumed.
        bool OnEdit(EditKey key);

        // Enter.
        void OnSubmit();

        // Esc.
        void OnCancel();
    }
}
