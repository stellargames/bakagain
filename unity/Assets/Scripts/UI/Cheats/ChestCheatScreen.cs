namespace BakAgain.UI.Cheats {
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Scene;
    using GameData.Resources.World;

    /// <summary>The map screen's cipher chest, REQ_CHET — <c>townscene_chest_open_with_cipher</c> (TOWNSCN.C:752).</summary>
    public sealed class ChestCheatScreen : CheatCentralScreen {
        protected override CheatCentral.Effect EffectFor(int actionId) =>
            CheatCentral.ChestEffect(actionId, Keys != null && Keys.CheatChordHeld, true, false, false);

        /// <summary>The chapter skip is a KEY, scancode 0x31 (N), not a REQ entry (TOWNSCN.C:794).</summary>
        private void Update() {
            if (Keys != null && Keys.SkipChapterKeyPressed) {
                PrimaryAction(0x31);
            }
        }

        protected override GameData.Resources.Inventory.RuntimeContainer ItemChest() =>
            Session.GetRuntimeContainerAt(0, CheatCentral.ChestX, Session.Chapter);

        protected override async UniTask<bool> ConfirmSkipAsync() =>
            await Dialogs.ShowById(CheatCentral.ChestSkipConfirm) == 0;

        /// <summary>
        /// The whole entry from the map: the chapter's cipher, then this menu (TOWNSCN.C:761-816).
        /// Nothing happens when the chapter's chest carries no cipher.
        /// </summary>
        public async UniTask OpenWithCipherAsync() {
            int puzzleId = Session.GetContainerAt(0, CheatCentral.ChestX, Session.Chapter)?.LockData?.PuzzleChest ?? 0;
            if (puzzleId <= 0 || Puzzles == null) {
                return;
            }
            await Dialogs.ShowById(CheatCentral.ChestPrompt);
            CipherPuzzle puzzle = await Puzzles.LoadAsync(puzzleId);
            if (puzzle == null || !await Puzzles.RunAsync(puzzle)) {
                await Dialogs.ShowById(CheatCentral.ChestFailed);
                return;
            }
            await Dialogs.ShowById(CheatCentral.ChestOpened);
            await Navigator.PushAndWaitAsync(this);
        }
    }
}
