namespace BakAgain.CutScenes {
    using Core;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation;
    using GameData.Resources.Palette;
    using Microsoft.Extensions.Logging;
    using ResourceManagement.Converters;
    using System.Collections.Generic;
    using System.Linq;

    public class CutscenePresenter : ICutscenePresenter {
        private readonly ILogger<CutscenePresenter> _logger;
        private readonly IResourceCache _resourceCache;
        private readonly ICutscenePlayerFactory _playerFactory;
        private readonly BakAgain.Core.GameSession _session;
        private CutscenePlayer _currentPlayer;

        public CutscenePresenter(ILogger<CutscenePresenter> logger, IResourceCache resourceCache,
            ICutscenePlayerFactory playerFactory, BakAgain.Core.GameSession session) {
            _logger = logger;
            _resourceCache = resourceCache;
            _playerFactory = playerFactory;
            _session = session;
        }

        /// <summary>
        /// The chapter an ADS script's <c>IF CHAPTER</c> conditions are evaluated against.
        /// </summary>
        /// <remarks>
        /// <b>This was hardcoded to 1, which silently mis-selected scenes.</b> Two shipped scripts
        /// branch on it — G_MISC and TVRN2 — so with a constant 1 every <c>IF CHAPTER &gt;= n</c>
        /// (n &gt; 1) was false and every <c>IF CHAPTER &lt;= n</c> was true: a chapter-4 party
        /// walking into a tavern got the chapter-1 scene, with nothing logged.
        ///
        /// <para>Falls back to 1 before a game is loaded (the attract loop and the debug cutscene
        /// entry both play scenes with no session), which is the value that was there before and is
        /// correct for the intro.</para>
        /// </remarks>
        private int ChapterNumber => _session is { IsActive: true } ? _session.Chapter : 1;

        /// <summary>
        /// One cutscene playback's setup, owned as a unit so both entry points get it identically.
        /// </summary>
        /// <remarks>
        /// <b>It exists to hold the LIFETIMES, which is why it is not a plain helper method.</b> The
        /// setup creates two <c>IDisposable</c>s that must outlive the call that made them, so a
        /// helper with <c>using var</c> inside would dispose them before playback ever started.
        ///
        /// <para><b>Dispose order is player, then state</b> — the order the two <c>using var</c>
        /// declarations produced, since C# disposes them in reverse. Reversing it would tear down
        /// the state a player is still holding.</para>
        ///
        /// <para><c>_currentPlayer = null</c> moved in here from each entry point's
        /// <c>finally</c>. That ran before the <c>using</c> disposals, and it still does: this
        /// clears the field first and disposes afterwards.</para>
        /// </remarks>
        private sealed class CutsceneSession : System.IDisposable {
            private readonly CutscenePresenter _owner;
            private bool _disposed;

            internal CutsceneSession(CutscenePresenter owner, Cutscene resource, CutsceneState state,
                CutscenePlayer player, Dictionary<int, int> tags) {
                _owner = owner;
                Resource = resource;
                State = state;
                Player = player;
                Tags = tags;
            }

            public Cutscene Resource { get; }

            public CutsceneState State { get; }

            public CutscenePlayer Player { get; }

            /// <summary>Scene number to start frame, from the one pre-process walk.</summary>
            public Dictionary<int, int> Tags { get; }

            public void Dispose() {
                if (_disposed) {
                    return;
                }
                _disposed = true;
                _owner._currentPlayer = null;
                Player?.Dispose();
                State?.Dispose();
            }
        }

        /// <summary>
        /// Everything both entry points do before they diverge on how they pick scripts.
        /// </summary>
        /// <remarks>
        /// The two shared eight setup steps — load, <c>Show</c>, palette, state, player and
        /// <c>_currentPlayer</c>, the pre-process walk, the image-slot reset — written out twice.
        /// The tag map alone was already shared because two copies of it would make the same
        /// cutscene start in different places; the rest had the same exposure and had simply not
        /// caused a visible failure yet.
        ///
        /// <para>Returns null when the cutscene will not load, having shown nothing:
        /// <c>view.Show()</c> deliberately follows the null check, so a failed load leaves the
        /// screen as it was rather than flashing an empty canvas.</para>
        ///
        /// <para><b>The catch is not decoration.</b> Pre-processing runs after both disposables
        /// exist, and it awaits per-frame work that can throw. Previously the entry point's
        /// <c>using var</c>s covered that; now that they are owned here, an exception between
        /// creating them and handing them back has to dispose them or the playback leaks a state
        /// and a player.</para>
        /// </remarks>
        private async UniTask<CutsceneSession> BeginAsync(string cutsceneName, ICutsceneView view,
            string caller) {
            Cutscene resource = await LoadCutsceneResource(cutsceneName);
            if (resource == null) {
                ConditionalLoggingExtensions.LogError(_logger,
                    "{Caller}: LoadCutsceneResource returned null for '{CutsceneName}'. Aborting.",
                    caller, cutsceneName);
                return null;
            }
            _logger.LogDebug("{Caller}: Cutscene '{CutsceneName}' loaded successfully.", caller, cutsceneName);

            view.Show();

            var defaultPalette = await _resourceCache.GetOrLoadAsync<PaletteResource>("options.pal");
            var state = new CutsceneState(view.Canvas, defaultPalette.Colors.ToUnity());
            CutscenePlayer player = null;
            try {
                player = _playerFactory.Create(state);
                _currentPlayer = player;
                _logger.LogDebug("{Caller}: state and player ready; pre-processing frames.", caller);

                Dictionary<int, int> tags = await PreProcessAndMapTagsAsync(resource, player);

                state.CurrentImageSlot = 0;
                state.ImageSlots.Clear();
                return new CutsceneSession(this, resource, state, player, tags);
            } catch {
                _currentPlayer = null;
                player?.Dispose();
                state.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Walks every frame once: pre-processes its commands and records where each TAG starts.
        /// </summary>
        /// <remarks>
        /// Both entry points need the same pass and had their own copy of it. They must stay
        /// identical — the tag map is what turns a script's scene number into a frame index, so two
        /// copies drifting would make the same cutscene start in different places depending on
        /// which entry point played it.
        /// </remarks>
        private async UniTask<Dictionary<int, int>> PreProcessAndMapTagsAsync(
            Cutscene cutsceneResource, CutscenePlayer player) {
            foreach (var frame in cutsceneResource.Frames) {
                await player.PreProcessSceneAsync(frame.Commands);
            }

            return MapTagsToFrames(cutsceneResource.Frames);
        }

        /// <summary>
        /// Where each tag starts, by frame index — the pure half of
        /// <see cref="PreProcessAndMapTagsAsync"/>.
        /// </summary>
        /// <remarks>
        /// <b>A repeated tag keeps the LAST frame that carries it</b>, because the walk assigns
        /// rather than adds. Nothing in the shipped files repeats one, so this is a decision about
        /// mod-authored data rather than a reproduction — but it is the existing behaviour and is
        /// pinned so a "first wins" rewrite cannot slip in unnoticed.
        ///
        /// <para>Internal so it can be tested without a player: the walk it comes from also awaits
        /// per-frame pre-processing, which needs the whole cutscene stack standing up.</para>
        /// </remarks>
        internal static Dictionary<int, int> MapTagsToFrames(IReadOnlyList<Frame> frames) {
            var tags = new Dictionary<int, int>();
            if (frames == null) {
                return tags;
            }
            for (var index = 0; index < frames.Count; index++) {
                if (frames[index]?.Tag is { } tag) {
                    tags[tag] = index;
                }
            }

            return tags;
        }

        public async UniTask<bool> PlayCutsceneAsync(string cutsceneName, ICutsceneView view, int animationNumber = 0, bool attractMode = false) {
            _logger.LogDebug("PlayCutsceneAsync: Loading cutscene '{CutsceneName}' with animation number {AnimationNumber}.", cutsceneName, animationNumber);

            using CutsceneSession session = await BeginAsync(cutsceneName, view, nameof(PlayCutsceneAsync));
            if (session == null) {
                return false;
            }

            // Named locals over the session so the playback bodies below read exactly as they did
            // when each entry point owned its own setup — the duplication was in the setup, and only
            // the setup moved.
            Cutscene cutsceneResource = session.Resource;
            CutsceneState cutsceneState = session.State;
            CutscenePlayer player = session.Player;
            Dictionary<int, int> tags = session.Tags;

            {
                var script = cutsceneResource.Scripts[animationNumber];
                _logger.LogDebug("PlayCutsceneAsync: AnimatorScript for animation number {AnimationNumber} obtained.", animationNumber);

                int chapterNumber = ChapterNumber;
                var playedScenes = new HashSet<int>();
                _logger.LogDebug("PlayCutsceneAsync: Processing script with ScriptProcessor.");
                var commands = ScriptProcessor.Process(script.Script, chapterNumber, playedScenes);
                _logger.LogDebug("PlayCutsceneAsync: ScriptProcessor finished. Starting command execution loop.");
                HashSet<int> background = ScriptProcessor.StoppedScenes(script.Script);

                foreach (var (action, sceneNumber) in commands) {
                    _logger.LogDebug("PlayCutsceneAsync: Executing command: Action={Action}, SceneNumber={SceneNumber}.", action, sceneNumber);
                    if (background.Contains(sceneNumber)) {
                        // A loop the original ran beside another scene and later STOPped: armed, so
                        // it counts as played, but never waited on (ScriptProcessor.StoppedScenes).
                        playedScenes.Add(sceneNumber);
                        continue;
                    }

                    if (!tags.TryGetValue(sceneNumber, out var startFrame)) {
                        ConditionalLoggingExtensions.LogError(_logger, "PlayCutsceneAsync: SceneNumber {SceneNumber} not found in tags. Skipping command.", sceneNumber);
                        continue;
                    }
                    _logger.LogDebug("PlayCutsceneAsync: Start frame for scene {SceneNumber} is {StartFrame}.", sceneNumber, startFrame);

                    switch (action) {
                        case CutsceneAction.Continue:   // a rewind-then-start; see ScriptProcessor.Plays
                        case CutsceneAction.Start:
                            _logger.LogDebug("PlayCutsceneAsync: Calling player.PlayCutScene for scene {SceneNumber} from frame {StartFrame}.", sceneNumber, startFrame);
                            var notCancelled = await player.PlayCutScene(cutsceneResource.Frames, startFrame, attractMode);
                            _logger.LogDebug("PlayCutsceneAsync: player.PlayCutScene for scene {SceneNumber} returned {NotCancelled}.", sceneNumber, notCancelled);

                            if (notCancelled) {
                                playedScenes.Add(sceneNumber);
                            } else {
                                _logger.LogInformation("PlayCutsceneAsync: Cutscene playback cancelled for scene {SceneNumber}. Exiting playback loop.", sceneNumber);
                                view.Hide();
                                // Normal cutscenes treat a cancel as "skip this scene, continue the
                                // flow" (return true). The intro attract cutscene treats ANY input as
                                // "exit to the menu" — return false so IntroCutsceneState breaks out.
                                return !attractMode;
                            }
                            break;
                        default:
                            ConditionalLoggingExtensions.LogWarning(_logger, "PlayCutsceneAsync: Unknown CutsceneAction '{Action}' for scene {SceneNumber}. Skipping.", action, sceneNumber);
                            break;
                    }
                }
                _logger.LogDebug("PlayCutsceneAsync: Command execution loop finished.");

                view.Hide();
                return true;
            }
        }

        // Debug/preview path: plays the named ADS tags in order (rather than a
        // single indexed animation) and, when holdRenderingAfter is set, keeps the
        // final frame on screen with palette cycling running until the player
        // cancels. Used to verify the temple symbol shimmer (INIT then SYM1).
        public async UniTask<bool> PlayCutsceneTagsAsync(string cutsceneName, ICutsceneView view, IReadOnlyList<string> tags,
            bool holdRenderingAfter = false, System.Threading.CancellationToken holdUntil = default,
            string backdrop = null, System.Action onHeld = null) {
            using CutsceneSession session = await BeginAsync(cutsceneName, view, nameof(PlayCutsceneTagsAsync));
            if (session == null) {
                return false;
            }

            Cutscene cutsceneResource = session.Resource;
            CutsceneState cutsceneState = session.State;
            CutscenePlayer player = session.Player;
            Dictionary<int, int> sceneTags = session.Tags;

            {
                await ApplyBackdrop(backdrop, cutsceneState);

                int chapterNumber = ChapterNumber;
                var playedScenes = new HashSet<int>();

                foreach (var tag in tags) {
                    var script = cutsceneResource.Scripts.FirstOrDefault(s => string.Equals(s.Tag, tag, System.StringComparison.OrdinalIgnoreCase));
                    if (script == null) {
                        ConditionalLoggingExtensions.LogError(_logger, "PlayCutsceneTagsAsync: ADS tag '{Tag}' not found in '{CutsceneName}'. Skipping.", tag, cutsceneName);
                        continue;
                    }

                    var commands = ScriptProcessor.Process(script.Script, chapterNumber, playedScenes);
                    HashSet<int> background = ScriptProcessor.StoppedScenes(script.Script);
                    foreach (var (action, sceneNumber) in commands) {
                        if (background.Contains(sceneNumber)) {
                            playedScenes.Add(sceneNumber);   // armed, as the original arms it; see StoppedScenes
                            continue;
                        }
                        if (!sceneTags.TryGetValue(sceneNumber, out var startFrame)) {
                            ConditionalLoggingExtensions.LogError(_logger, "PlayCutsceneTagsAsync: SceneNumber {SceneNumber} not found in frame tags. Skipping.", sceneNumber);
                            continue;
                        }
                        if (!ScriptProcessor.Plays(action)) {
                            ConditionalLoggingExtensions.LogWarning(_logger, "PlayCutsceneTagsAsync: Unsupported CutsceneAction '{Action}' for scene {SceneNumber}. Skipping.", action, sceneNumber);
                            continue;
                        }

                        var notCancelled = await player.PlayCutScene(cutsceneResource.Frames, startFrame);
                        if (notCancelled) {
                            playedScenes.Add(sceneNumber);
                        } else {
                            view.Hide();
                            return true;
                        }
                    }
                }

                if (holdRenderingAfter) {
                    // The scripts are done and the last frame is about to be held: anything the
                    // caller wants ON the held picture has to wait until now, because each script
                    // clears the dialog panel as it ends (CutscenePlayer's own cleanup). A location
                    // showing its description any earlier watches it disappear.
                    onHeld?.Invoke();

                    // A caller that supplies a token owns its own input layer, so hold without
                    // pushing ours — otherwise the Exclusive cutscene layer eats its clicks.
                    if (holdUntil.CanBeCanceled) {
                        await player.HoldRenderingWithoutInputAsync(holdUntil);
                    } else {
                        await player.HoldRenderingAsync();
                    }
                }

                view.Hide();
                return true;
            }
        }

        /// <summary>
        /// Lays a full-screen image into the background buffer, under everything the scene draws.
        /// </summary>
        /// <remarks>
        /// <b>An interactive location is drawn on top of a backdrop, not on an empty screen.</b> The
        /// original re-blits a whole buffer over the working one before every scene replay
        /// (<c>playAnimationScene</c>), and for a location that buffer holds <c>DIALOG.SCX</c> — so
        /// the town picture covers the top and the dialogue panel shows in the band below it. Without
        /// it a location renders with an empty strip under the picture.
        ///
        /// <para>Promoting it to the background buffer is what makes it survive: every frame's
        /// <c>PrepareBuffers</c> copies that buffer back under the drawing, which is the same
        /// per-replay restore the original gets from its blit. This is exactly what the
        /// <c>LoadScreenResource</c> frame command does — the backdrop just has no frame command to
        /// carry it, because the scene files assume it is already on screen.</para>
        /// </remarks>
        private async UniTask ApplyBackdrop(string backdrop, CutsceneState cutsceneState) {
            if (string.IsNullOrWhiteSpace(backdrop)) {
                return;
            }

            var image = await _resourceCache.GetOrLoadAsync<ResourceManagement.Models.IndexedTexture>(backdrop);
            if (image == null) {
                ConditionalLoggingExtensions.LogError(_logger, "Backdrop {Backdrop} did not load.", backdrop);
                return;
            }

            BakAgain.Graphics.Drawing.DrawImage(image, 0, 0, BakAgain.Graphics.Orientation.Normal,
                UnityEngine.Vector2.one, cutsceneState);
            cutsceneState.CopyBuffer(cutsceneState.CurrentDrawBufferIndex, cutsceneState.BackgroundBufferIndex);
        }

        private async UniTask<Cutscene> LoadCutsceneResource(string cutSceneName) {
            var ads = await _resourceCache.GetOrLoadAsync<AnimatorResource>($"{cutSceneName}.ADS");
            if (ads == null) {
                ConditionalLoggingExtensions.LogError(_logger, "Failed to load animator resource for cutscene {CutsceneName}", cutSceneName);
                return null;
            }

            var filename = ads.ResourceFiles.Values.First();
            var animationResource = await _resourceCache.GetOrLoadAsync<AnimationResource>(filename);
            if (animationResource == null) {
                ConditionalLoggingExtensions.LogError(_logger, "Failed to load animation resource {FileName}", filename);
                return null;
            }

            return new Cutscene(ads.Animations, animationResource.Frames);
        }

        public void Cancel() {
            _currentPlayer?.Cancel();
        }
    }

}
