namespace BakAgain.UI.Cheats {
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Scene;
    using GameData.Resources.World;

    /// <summary>The map screen's cipher chest, REQ_CHET — <c>townscene_chest_open_with_cipher</c> (TOWNSCN.C:752).</summary>
    public sealed class ChestCheatScreen : CheatCentralScreen {
        /// <summary>The screen fade the solved cipher goes to black through (TASK-978).</summary>
        [VContainer.Inject] internal BakAgain.UI.Navigation.IScreenFade Fade;

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
            // *** THROUGH BLACK (TASK-978). *** The cipher's solved path fades to black and puts the
            // palette back at once on the black screen (CIPHER.C:205-213); the line and the menu are
            // drawn on it (TOWNSCN.C:769-773). The menu goes up under the black, which then cuts away;
            // the line, a full-screen one, opens over the menu (the cover sits above every dialog).
            BakAgain.UI.Navigation.IScreenFade fade = Fade;
            if (puzzle == null || !await Puzzles.RunAsync(puzzle,
                    fade == null ? null : () => fade.FadeOutAsync(BakAgain.UI.Navigation.ScreenFade.CipherFadeOutSeconds))) {
                await Dialogs.ShowById(CheatCentral.ChestFailed);
                return;
            }
            try {
                await Navigator.PushUnfaded(this);   // returns once it is up
            } finally {
                if (fade != null) {
                    await fade.FadeInAsync(0f);
                }
            }
            await Dialogs.ShowById(CheatCentral.ChestOpened);
            await Navigator.WhenOffStackAsync(this);
        }
    }
}
