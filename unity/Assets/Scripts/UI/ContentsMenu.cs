namespace BakAgain.UI {
    using BakAgain.Core;
    using BakAgain.Core.States;
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using System;
    using UnityEngine;
    using VContainer;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Contents (table-of-contents) screen controller. Exit is a normal REQ button; chapter marking +
    /// clicking is delegated to <see cref="ContentsChapterOverlay"/> (the REQ chapter TextButtons are
    /// Visible=false/unpickable). A reached-chapter click runs the <see cref="Core.Services.ChapterScenesPlayer"/>
    /// (Full = re-watch all the chapter's scenes); the player pushes the cutscene/book screens over this
    /// one and pops them, so this screen is simply re-shown afterward. A navigator-managed screen:
    /// pushed by <see cref="MenuActionHandler"/>; Exit pops.
    /// </summary>
    [RequireComponent(typeof(ContentsChapterOverlay))]
    public class ContentsMenu : BakAgain.UI.Navigation.ScreenBase, IActionHandler {
        private const int ButtonExit = 1;
        private const int HelpChapterArea = 329;   // right-click help DDX
        private const int HelpExit = 330;

        private ILogger _logger;
        private IDialogManager _dialogManager;
        private GameSession _session;
        private BakAgain.Core.Services.ChapterScenesPlayer _scenesPlayer;
        private BakAgain.UI.Navigation.IScreenNavigator _navigator;
        private ContentsChapterOverlay _overlay;

        private void Awake() {
            _logger = LogManager.LoggerFactory.CreateLogger<ContentsMenu>();
            _overlay = GetComponent<ContentsChapterOverlay>();
        }

        [Inject]
        public void Construct(IDialogManager dialogManager, GameSession session,
            BakAgain.Core.Services.ChapterScenesPlayer scenesPlayer, IResourceProviderService resources,
            BakAgain.UI.Navigation.IScreenNavigator navigator) {
            _dialogManager = dialogManager ?? throw new ArgumentNullException(nameof(dialogManager));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _scenesPlayer = scenesPlayer ?? throw new ArgumentNullException(nameof(scenesPlayer));
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            // Resolve + supply the sibling overlay here (it is not [Inject]-ed itself). VContainer
            // injects this prefab while it is still INACTIVE (the Contents screen starts hidden), so
            // Awake — which also sets _overlay — has NOT run yet at Construct time. GetComponent works
            // on an inactive GameObject, so resolve it here rather than relying on Awake having run.
            _overlay = GetComponent<ContentsChapterOverlay>();
            _overlay.Init(resources ?? throw new ArgumentNullException(nameof(resources)));
        }

        private void OnEnable() => _overlay.ChapterClicked += OnChapterClicked;
        private void OnDisable() => _overlay.ChapterClicked -= OnChapterClicked;

        // Mark reached chapters before the panel shows (the overlay draws on enable).
        protected override UniTask OnBeforeShowAsync() {
            _overlay.Configure(CurrentChapter);
            return UniTask.CompletedTask;
        }

        private void Close() => _navigator.Pop().Forget();

        public void PrimaryAction(int actionId) {
            if (actionId == ButtonExit) {
                Close();
            } else {
                _logger.LogDebug("Unhandled contents action {ActionId}", actionId);
            }
        }

        public async Awaitable SecondaryAction(int actionId) {
            // Exit has help 330; chapter click areas raise help 329 via their own right-click (below).
            if (actionId == ButtonExit) {
                await _dialogManager.ShowById(HelpExit).AsTask();
            }
        }

        private int CurrentChapter =>
            _session?.IsActive == true ? Math.Max(_session.Chapter, 1) : 1;

        // A reached chapter was clicked: replay its scenes via the awaited player. The player pushes
        // the cutscene/book screens over this one and pops them when done — Contents (and whatever
        // is beneath it) is simply re-shown by the navigator; no state round-trip, no continuation.
        private void OnChapterClicked(int chapter) {
            // Faithful to showTableOfContents @ 0x210aa: always replay the chapter's start (intro +
            // start book = part 1); replay the end scenes (part 2) ONLY once you've moved PAST that
            // chapter. The original calls playChapterAnimationsAndBook(N, 1) unconditionally and adds
            // (N, 2) only when currentChapter > N — so the chapter you're still in shows just its start.
            ChapterScenesMode mode = CurrentChapter > chapter
                ? ChapterScenesMode.Full        // past chapter: intro + start + end
                : ChapterScenesMode.StartOnly;   // current chapter: intro + start only
            _scenesPlayer.PlayAsync(chapter, mode).Forget();
        }
    }
}
