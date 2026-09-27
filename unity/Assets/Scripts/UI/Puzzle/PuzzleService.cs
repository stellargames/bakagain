namespace BakAgain.UI.Puzzle {
    using BakAgain.Core;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Dialog;
    using GameData.Resources.Dialog.Actions;
    using GameData.Resources.Scene;
    using Microsoft.Extensions.Logging;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Finds a puzzle by its id and runs it — the service half of
    /// <c>UI_RunCipherPuzzle</c> @0x78c60.
    /// </summary>
    /// <remarks>
    /// <b>A puzzle is not a file.</b> Its table lives in the TAIL of a DDX record, keyed
    /// <see cref="CipherPuzzle.DialogKeyFor"/> — <c>(puzzleId - 1) + 0x19f0a1</c>, which for the
    /// shipped puzzle chests lands in DIAL_Z17. So loading one means loading a dialog and reading
    /// the text off an entry, not opening a puzzle resource that does not exist.
    /// </remarks>
    public sealed class PuzzleService {
        private readonly PuzzleScreen _screen;
        private readonly ILogger _logger;
        private readonly DialogResourceLoader _dialogs;

        public PuzzleService(PuzzleScreen screen) {
            _screen = screen;
            _logger = LoggerFactoryExtensions.CreateLogger<PuzzleService>(LogManager.LoggerFactory);
            // The DDX address derivation is already this loader's job — deriving it again here
            // would be a second place to fix when the naming changes.
            _dialogs = new DialogResourceLoader(_logger);
        }

        /// <summary>The puzzle with this id, or null when nothing carries its table.</summary>
        public async UniTask<CipherPuzzle> LoadAsync(int puzzleId) {
            var key = (int)CipherPuzzle.DialogKeyFor(puzzleId);
            Dialog dialog = await _dialogs.LoadDialogAsync(key);
            if (dialog == null) {
                return null;   // the loader has already said which file failed
            }

            foreach (DialogEntry entry in dialog.Entries) {
                if (entry.Id != key || string.IsNullOrEmpty(entry.Text)) {
                    continue;
                }

                // The riddle's box is the ENTRY's own resize rect, not a constant on the screen —
                // dialog_getDialogArea (0x485bc) uses an entry's resize in place of the style's
                // area, so this is where a puzzle says where its text goes.
                _screen?.SetTextArea(AreaOf(entry));

                return CipherPuzzle.Parse(entry.Text);
            }

            Microsoft.Extensions.Logging.LoggerExtensions.LogError(_logger,
                "Puzzle {Id}: no entry {Key} in the dialog that holds it.", puzzleId, key);

            return null;
        }

        /// <summary>The entry's own resize rect, or null when it does not carry one.</summary>
        private static GameData.Resources.Layout.LayoutHint AreaOf(DialogEntry entry) {
            foreach (DialogActionBase action in entry.Actions) {
                if (action is ResizeDialogAction resize) {
                    return resize.ToLayoutHint();
                }
            }

            return null;
        }

        /// <summary>Runs the screen; true when the player solved it.</summary>
        public UniTask<bool> RunAsync(CipherPuzzle puzzle) =>
            _screen == null ? UniTask.FromResult(false) : _screen.RunAsync(puzzle);
    }
}
