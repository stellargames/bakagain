namespace BakAgain.UI.InputCore {
    /// <summary>Settable ICheatInput for tests.</summary>
    public sealed class FakeCheatInput : ICheatInput {
        public bool RevealRareCredits { get; set; }
        public bool CheatChordHeld { get; set; }
        public bool CheatKeyPressed { get; set; }
        public bool CheatKeyHeld { get; set; }
        public bool SkipChapterKeyPressed { get; set; }
    }
}
