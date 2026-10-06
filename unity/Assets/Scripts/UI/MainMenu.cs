namespace BakAgain.UI {
    using BakAgain.Core;
    using BakAgain.Core.States;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using VContainer;

    /// <summary>
    /// Title-screen menu (REQ_OPT0 / OPTIONS0.SCX) — the Frontend root screen, shown via
    /// <see cref="IScreenNavigator.ResetTo"/> by <see cref="MainMenuState"/>. A thin
    /// <see cref="IActionHandler"/> that forwards button clicks to a Title-mode
    /// <see cref="MenuActionHandler"/> — the only logic it shares with the in-game
    /// <see cref="InGameMenu"/>.
    /// </summary>
    public sealed class MainMenu : ScreenBase, IActionHandler, BakAgain.UI.InputCore.IUnmatchedScancodeHandler {
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

        private MenuActionHandler _handler;
        private BakAgain.Audio.MidiPlaybackManager _midi;
        private BakAgain.ResourceManagement.IResourceProviderService _resources;

        [Inject]
        public void Construct(IDialogManager dialogManager, PreferencesMenu preferencesMenu,
            LoadGameMenu loadGameMenu, SaveGameMenu saveGameMenu, ContentsMenu contentsMenu,
            BakAgain.Core.Services.IGameFlow flow, IScreenNavigator navigator,
            BakAgain.Audio.MidiPlaybackManager midi,
            BakAgain.ResourceManagement.IResourceProviderService resources) {
            _handler = new MenuActionHandler(MenuMode.Title, dialogManager,
                preferencesMenu, loadGameMenu, saveGameMenu, contentsMenu, flow,
                navigator, LogManager.LoggerFactory.CreateLogger<MainMenu>());
            _midi = midi;
            _resources = resources;
        }

        /// <summary>
        /// Starts the menu's theme — <c>UI_showMainMenu</c> @0x6de78 opens with
        /// <c>audio_music_play(1015)</c>.
        /// </summary>
        /// <remarks>
        /// <b>No restore on the way out from here.</b> Every choice the title screen offers — new
        /// game, load, contents, quit — is a departure, and the original returns before its restore
        /// call for all four (<see cref="GameData.Resources.Audio.MusicSelection.RestoresPreviousTrack"/>).
        /// The saved track only matters for the in-game form of this menu, which can be cancelled
        /// back to the world; see <c>InGameMenu</c>.
        ///
        /// <para>Re-requesting the track already playing is a no-op, so coming back to the title
        /// from Contents does not restart the theme.</para>
        /// </remarks>
        protected override void OnAfterShow() {
            if (_midi == null || _resources == null) {
                return;
            }
            _midi.PlayTrackAsync(
                GameData.Resources.Audio.MusicSelection.MainMenuTrack, _resources, owner: this).Forget();
        }

        public void PrimaryAction(int menuEntryActionId) => _handler.Primary(menuEntryActionId);

        public Awaitable SecondaryAction(int menuEntryActionId) => _handler.Secondary(menuEntryActionId);
    }
}
