namespace BakAgain.UI {
    using BakAgain.Core;
    using BakAgain.Core.States;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using VContainer;

    /// <summary>
    /// In-game menu (REQ_OPT1 / OPTIONS1.SCX). A separate full-screen screen from the title
    /// <see cref="MainMenu"/> — a distinct type so DI resolves the two prefabs independently and
    /// <see cref="InGame.InGameScreen"/> can push it onto the <see cref="IScreenNavigator"/>.
    /// Forwards to an InGame-mode <see cref="MenuActionHandler"/>; shares no code with
    /// <see cref="MainMenu"/> beyond that. Faithful to UI_showMainMenu's openedFromGame != 0 path.
    /// </summary>
    public sealed class InGameMenu : ScreenBase, IActionHandler, BakAgain.UI.InputCore.IUnmatchedScancodeHandler {
        private BakAgain.UI.InputCore.InputLayerStack _inputStack;
        private BakAgain.ResourceManagement.IResourceProviderService _bannerResources;

        [Inject]
        public void ConstructBanner(BakAgain.UI.InputCore.InputLayerStack inputStack,
            BakAgain.ResourceManagement.IResourceProviderService resources) {
            _inputStack = inputStack;
            _bannerResources = resources;
        }

        /// <summary>V shows the version banner (MAINMENU.C:289-291) — TASK-804.</summary>
        public bool OnUnmatchedScancode(int scancode) {
            if (scancode != GameData.Resources.Menu.VersionBanner.Key) {
                return false;
            }
            VersionBannerView.ShowAsync(GetComponent<UnityEngine.UIElements.UIDocument>()?.rootVisualElement,
                _bannerResources, _inputStack, this).Forget();
            return true;
        }

        /// <summary>Cancel — the one exit that goes back where it came from. REQ_OPT1 action 18.</summary>
        private const int ActionCancel = 18;

        private MenuActionHandler _handler;
        private BakAgain.Audio.MidiPlaybackManager _midi;
        private BakAgain.ResourceManagement.IResourceProviderService _resources;
        private int _trackBeforeMenu = GameData.Resources.Audio.MusicPlayback.NoTrack;

        [Inject]
        public void Construct(IDialogManager dialogManager, PreferencesMenu preferencesMenu,
            LoadGameMenu loadGameMenu, SaveGameMenu saveGameMenu, ContentsMenu contentsMenu,
            BakAgain.Core.Services.IGameFlow flow, IScreenNavigator navigator,
            BakAgain.Audio.MidiPlaybackManager midi,
            BakAgain.ResourceManagement.IResourceProviderService resources) {
            _handler = new MenuActionHandler(MenuMode.InGame, dialogManager,
                preferencesMenu, loadGameMenu, saveGameMenu, contentsMenu, flow,
                navigator, LogManager.LoggerFactory.CreateLogger<InGameMenu>());
            _midi = midi;
            _resources = resources;
            _navigator = navigator;
            saveGameMenu.Saved += ResumeAfterSave;
            preferencesMenu.Applied = ResumeAfterPreferences;
        }

        /// <summary>REQ_OPT1 action 25: Preferences.</summary>
        private const int ActionPreferences = 25;

        /// <summary>The last action taken here opened Preferences (the title menu shares that screen).</summary>
        private bool _openedPreferences;

        /// <summary>
        /// Preferences confirmed from this menu goes straight back to the world, like a save:
        /// <c>mainmenu_save_prefs_menu_run</c> returning 1 (OK) ends the in-game menu with result 0
        /// (MAINMENU.C:240-243). Its Cancel returns 0 and the menu is shown again.
        /// </summary>
        private bool ResumeAfterPreferences() {
            if (!_openedPreferences) {
                return false;
            }
            _openedPreferences = false;
            RestoreWorldTrack();
            _navigator.Pop(2).Forget();
            return true;
        }

        private IScreenNavigator _navigator;

        /// <summary>
        /// A save from this menu goes straight back to the world: <c>mainmenu_save_save_game_dialog</c>
        /// returning non-zero ends the menu with result 0 (MAINMENU.C:227-230), the same exit as Cancel,
        /// so the world's track comes back too. Both screens go in one pop, so this menu is not shown again.
        /// </summary>
        private void ResumeAfterSave() {
            RestoreWorldTrack();
            _navigator.Pop(2).Forget();
        }

        /// <summary>
        /// Starts the menu's theme and remembers what the world was playing.
        /// </summary>
        /// <remarks>
        /// The saved value is <c>audio_music_play</c>'s return — the reason that function returns
        /// anything at all. <c>UI_showMainMenu</c> @0x6de78 opens with <c>audio_music_play(1015)</c>
        /// and keeps the result to hand back on the way out. The track is the same 1015 the title
        /// screen uses: <c>openedFromGame</c> chooses REQ_OPT1 over REQ_OPT0, not the music.
        /// </remarks>
        protected override void OnAfterShow() {
            if (_midi == null || _resources == null) {
                return;
            }
            SaveAndPlayAsync().Forget();
        }

        private async UniTaskVoid SaveAndPlayAsync() {
            int previous = await _midi.PlayTrackAsync(
                GameData.Resources.Audio.MusicSelection.MainMenuTrack, _resources, owner: this);
            // Re-shown after a sub-screen (a cancelled Save, Restore, Preferences) the menu's own track
            // is already playing; the original asks once, on entry (MAINMENU.C:108), so keep that answer.
            if (previous != GameData.Resources.Audio.MusicSelection.MainMenuTrack) {
                _trackBeforeMenu = previous;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Only Cancel puts the world's music back.</b> New game, load, contents and quit all
        /// leave the menu theme playing, because each destination sets its own as it comes up — the
        /// original returns before its restore call for exactly those four
        /// (<see cref="GameData.Resources.Audio.MusicSelection.RestoresPreviousTrack"/>). Restoring
        /// on them would put the world track back for the moment the destination takes to load.
        /// </remarks>
        public void PrimaryAction(int menuEntryActionId) {
            _openedPreferences = menuEntryActionId == ActionPreferences;
            if (menuEntryActionId == ActionCancel) {
                RestoreWorldTrack();
            }

            _handler.Primary(menuEntryActionId);
        }

        private void RestoreWorldTrack() {
            if (_midi != null && _resources != null
                && GameData.Resources.Audio.MusicSelection.RestoresPreviousTrack(
                    GameData.Resources.Audio.MusicSelection.MenuExit.Resume)) {
                _midi.PlayTrackAsync(_trackBeforeMenu, _resources, owner: this).Forget();
            }
        }

        public Awaitable SecondaryAction(int menuEntryActionId) => _handler.Secondary(menuEntryActionId);
    }
}
