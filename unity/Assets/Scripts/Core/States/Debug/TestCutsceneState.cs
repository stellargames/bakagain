namespace BakAgain.Core.States.Debug {
    using BakAgain.CutScenes;
    using Cysharp.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    public class TestCutsceneState {
        private readonly ILogger<TestCutsceneState> _logger;
        private readonly ICutscenePresenter _presenter;
        private readonly ICutsceneView _view;

        public TestCutsceneState(ILogger<TestCutsceneState> logger, ICutscenePresenter presenter, ICutsceneView view) {
            _logger = logger;
            _presenter = presenter;
            _view = view;
        }

        public async UniTask RunAsync() {
            _logger.LogInformation("Entering TestCutsceneState.");

            if (_presenter == null || _view == null) {
                _logger.LogError("ICutscenePresenter or ICutsceneView was not injected. Cannot play cutscene.");
                return;
            }

            // Replays forever (the old state re-entered itself) — a debug dead-end by design.
            while (true) {
                _logger.LogInformation("Playing Test cutscene.");
                // Play the temple init + first symbol, then hold the frame on screen so
                // the palette colour-cycling (shimmer) keeps running until ESC/click.
                bool cutsceneComplete = await _presenter.PlayCutsceneTagsAsync("G_MISC", _view, new []{"CULLICH BG", "DISPLAY"}, true);

                if (cutsceneComplete) {
                    _logger.LogInformation("Test cutscene finished.");
                } else {
                    _logger.LogWarning("Test cutscene did not complete successfully (was cancelled).");
                }
            }
        }
    }
}
