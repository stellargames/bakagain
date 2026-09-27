namespace BakAgain.UI.InputCore {
    /// <summary>Niche cheat/debug holds, abstracted so no view reads Keyboard.current. Deliberately
    /// NOT folded into IGameplayInput — interface segregation (a rare Easter-egg hold isn't gameplay
    /// movement input).</summary>
    public interface ICheatInput {
        bool RevealRareCredits { get; } // 'N' held — CreditsView's Sparkle/rare-credit reveal cheat
    }
}
