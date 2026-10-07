namespace BakAgain.Core.States.Debug {
    using BakAgain.CutScenes;
    using Cysharp.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using System.Collections.Generic;

    /// <summary>
    /// Plays one named cutscene and then HOLDS the last frame, so it can be captured and compared
    /// against the original.
    ///
    /// <para>It exists because every other way into the cutscene player leaves something else owning
    /// the screen: <see cref="TestCutsceneState"/> replays G_MISC in a <c>while (true)</c> and
    /// overdraws whatever else you start, and the Main Menu / world entries render their own screen
    /// on top. Both look like "our renderer drew nothing" and are really "something drew after us".
    /// This entry draws the scene and then stops, which is the whole point.</para>
    ///
    /// <para>Configure it before choosing the entry — the fields are static because the choice is
    /// made from outside the container (Editor CLI / MCP), before this instance is resolved:</para>
    /// <code>
    /// PlayCutsceneState.SceneName = "C21";
    /// PlayCutsceneState.Tags = null;            // null/empty = the whole scene
    /// DebugStart.Select("PlayCutsceneState");
    /// </code>
    /// </summary>
    public class PlayCutsceneState {
        /// <summary>Cutscene to play, e.g. "C21" (chapter 2 part 1) or "CHAPTER2" (its title card).</summary>
        public static string SceneName = "C21";

        /// <summary>Tags to play, in order. Null or empty plays the whole scene.</summary>
        public static IReadOnlyList<string> Tags;

        private readonly ILogger<PlayCutsceneState> _logger;
        private readonly CutscenePresenter _presenter;
        private readonly ICutsceneView _view;

        public PlayCutsceneState(ILogger<PlayCutsceneState> logger, CutscenePresenter presenter, ICutsceneView view) {
            _logger = logger;
            _presenter = presenter;
            _view = view;
        }

        public async UniTask RunAsync() {
            if (_presenter == null || _view == null) {
                _logger.LogError("CutscenePresenter or ICutsceneView was not injected. Cannot play a cutscene.");
                return;
            }

            string scene = string.IsNullOrEmpty(SceneName) ? "C21" : SceneName;
            IReadOnlyList<string> tags = Tags;
            bool played;

            if (tags == null || tags.Count == 0) {
                _logger.LogInformation("PlayCutsceneState: playing {Scene} in full.", scene);
                played = await _presenter.PlayCutsceneAsync(scene, _view);
            } else {
                _logger.LogInformation("PlayCutsceneState: playing {Scene} tags [{Tags}].", scene, string.Join(", ", tags));
                played = await _presenter.PlayCutsceneTagsAsync(scene, _view, tags, holdRenderingAfter: true);
            }

            _logger.LogInformation("PlayCutsceneState: {Scene} {Outcome}; holding the frame.", scene,
                played ? "finished" : "did not complete (cancelled)");

            // Hold. Yielding — rather than replaying, or handing back to another entry — is what
            // keeps the final frame on screen for a capture. A debug dead-end by design.
            while (true) {
                await UniTask.Yield();
            }
        }
    }
}
