namespace BakAgain.UI.InputCore {
    /// <summary>Niche cheat/debug holds, abstracted so no view reads Keyboard.current. Deliberately
    /// NOT folded into IGameplayInput — interface segregation (a rare Easter-egg hold isn't gameplay
    /// movement input).</summary>
    public interface ICheatInput {
        bool RevealRareCredits { get; } // 'N' held — CreditsView's Sparkle/rare-credit reveal cheat

        /// <summary>CHEAT CENTRAL's modifiers: right Shift + Alt, no left Shift, no Ctrl (WORLDLP.C:380).</summary>
        bool CheatChordHeld { get; }

        /// <summary>The backquote key (DOS scancode 0x29) went down this frame.</summary>
        bool CheatKeyPressed { get; }

        /// <summary>The backquote key is held — the map screen waits on it (MAP.C:426).</summary>
        bool CheatKeyHeld { get; }

        /// <summary>'N' went down this frame — the chest menu's chapter skip (scancode 0x31).</summary>
        bool SkipChapterKeyPressed { get; }
    }
}
