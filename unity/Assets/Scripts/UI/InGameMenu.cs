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
    public sealed class InGameMenu : ScreenBase, IActionHandler {
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
            _trackBeforeMenu = await _midi.PlayTrackAsync(
                GameData.Resources.Audio.MusicSelection.MainMenuTrack, _resources, owner: this);
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
            if (menuEntryActionId == ActionCancel && _midi != null && _resources != null
                && GameData.Resources.Audio.MusicSelection.RestoresPreviousTrack(
                    GameData.Resources.Audio.MusicSelection.MenuExit.Resume)) {
                _midi.PlayTrackAsync(_trackBeforeMenu, _resources, owner: this).Forget();
            }

            _handler.Primary(menuEntryActionId);
        }

        public Awaitable SecondaryAction(int menuEntryActionId) => _handler.Secondary(menuEntryActionId);
    }
}
