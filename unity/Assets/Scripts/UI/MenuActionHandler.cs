namespace BakAgain.UI {
    using System;
    using BakAgain.Core;          // ConditionalLoggingExtensions (LogInformation/LogError)
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public enum MenuMode { Title, InGame }

    /// <summary>
    /// Shared button-dispatch for the two menu screens (title <see cref="MainMenu"/> /
    /// in-game <see cref="InGameMenu"/>). The screens are separate MonoBehaviours with their own
    /// prefabs (backgrounds + REQ); this class is the ONLY logic they share. REQ_OPT0 and REQ_OPT1
    /// use identical action ids (49/19/31/25/46/32/18), so one dispatch table serves both —
    /// <see cref="MenuMode"/> only gates the in-game-only buttons (Save 31 / Cancel 18) and the
    /// New-Game confirm. Faithful to UI_showMainMenu @ 0x6de78 + the choice switch @ 0x6e0d5.
    /// Sub-screens open via <see cref="IScreenNavigator"/> pushes — the stack remembers this menu
    /// and re-shows it on their Pop, whichever menu (title/in-game) they were opened from.
    /// </summary>
    public sealed class MenuActionHandler {
        private readonly MenuMode _mode;
        private readonly ILogger _logger;
        private readonly IDialogManager _dialogManager;
        private readonly PreferencesMenu _preferencesMenu;
        private readonly LoadGameMenu _loadGameMenu;
        private readonly SaveGameMenu _saveGameMenu;
        private readonly ContentsMenu _contentsMenu;
        private readonly BakAgain.Core.Services.IGameFlow _flow;
        private readonly IScreenNavigator _navigator;

        public MenuActionHandler(MenuMode mode, IDialogManager dialogManager,
            PreferencesMenu preferencesMenu, LoadGameMenu loadGameMenu, SaveGameMenu saveGameMenu,
            ContentsMenu contentsMenu, BakAgain.Core.Services.IGameFlow flow,
            IScreenNavigator navigator, ILogger logger) {
            _mode = mode;
            _dialogManager = dialogManager;
            _preferencesMenu = preferencesMenu;
            _loadGameMenu = loadGameMenu;
            _saveGameMenu = saveGameMenu;
            _contentsMenu = contentsMenu;
            _flow = flow;
            _navigator = navigator;
            _logger = logger;
        }

        public void Primary(int menuEntryActionId) {
            _logger.LogInformation("Primary action {MenuEntryActionId}", menuEntryActionId);
            switch (menuEntryActionId) {
                case 49:
                    // New Game. In-game confirms first via DDX 111 (dialog_ConfirmStartNewGame
                    // @ 0x6e278); the title menu does not.
                    _ = StartNewGame();
                    break;
                case 25:
                    _navigator.Push(_preferencesMenu).Forget();
                    break;
                case 19:
                    _navigator.Push(_loadGameMenu).Forget();   // Restore Game (id 19)
                    break;
                case 46:
                    _navigator.Push(_contentsMenu).Forget();   // Contents (id 46)
                    break;
                case 31 when _mode == MenuMode.InGame:
                    // Save Game — open the REQ_SAVE screen (dialog_SaveGame @0x6e7e8).
                    _navigator.Push(_saveGameMenu).Forget();
                    break;
                case 18 when _mode == MenuMode.InGame:
                    _navigator.Pop().Forget();                 // Cancel — return to game
                    break;
                case 32:
                    _ = ConfirmAndQuit();            // Quit — DDX 110 confirm (UI_showMainMenu 0x6e198)
                    break;
            }
        }

        private async Awaitable StartNewGame() {
            try {
                if (_mode == MenuMode.InGame &&
                    !await _dialogManager.ShowConfirmById(111).AsTask()) {
                    return; // player chose No — stay in the menu.
                }
                _logger.LogInformation("Starting new game...");
                // The flow leaves the current configuration itself (clears the stack, tears down
                // the world if built), plays the chapter scenes, hydrates, and enters the world.
                await _flow.StartNewGame();
            } catch (Exception e) {
                _logger.LogError(e, "Error starting new game");
            }
        }

        private async Awaitable ConfirmAndQuit() {
            // Fire-and-forget from Primary(), so observe faults here rather than leaving them on a
            // discarded Awaitable (mirrors StartNewGame's try/catch).
            try {
                if (!await _dialogManager.ShowConfirmById(110).AsTask()) {
                    return;
                }
                #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
                #else
                Application.Quit();
                #endif
            } catch (Exception e) {
                _logger.LogError(e, "Error during quit confirmation");
            }
        }

        public async Awaitable Secondary(int menuEntryActionId) {
            _logger.LogInformation("Secondary action {MenuEntryActionId}", menuEntryActionId);
            switch (menuEntryActionId) {
                case 25: await _dialogManager.ShowById(118).AsTask(); break; // Preferences help
                case 49: await _dialogManager.ShowById(115).AsTask(); break; // New Game help
                case 19: await _dialogManager.ShowById(116).AsTask(); break; // Restore help
                case 46: await _dialogManager.ShowById(119).AsTask(); break; // Contents help
                case 31: await _dialogManager.ShowById(117).AsTask(); break; // Save help
                case 32: await _dialogManager.ShowById(120).AsTask(); break; // Quit help
                case 18: await _dialogManager.ShowById(0x79).AsTask(); break; // Cancel help (MAINMENU.C:275-278)
            }
        }
    }
}
