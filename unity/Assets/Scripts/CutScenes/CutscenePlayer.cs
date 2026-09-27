namespace BakAgain.CutScenes {
    using BakAgain.Audio;
    using BakAgain.Core;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation;
    using GameData.Resources.Animation.FrameCommands;
    using GameData.Resources.Audio;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using AudioType = GameData.Resources.Audio.AudioType;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public class CutscenePlayer : IDisposable {
        private readonly CutsceneState _cutsceneState;
        private readonly BakAgain.UI.InputCore.InputLayerStack _stack;
        private readonly ICutsceneFrameProcessor _frameProcessor;
        private readonly IDialogManager _dialogManager;
        private CancellationTokenSource _cancellation;
        private bool _skipRequested;
        private readonly MidiPlaybackManager _midiPlayer;
        private readonly ILogger _logger;
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(CutscenePlayer));

        // Shows a chapter book and completes when it is closed; null where no book can be shown.
        private readonly Func<string, UniTask> _showBook;

        public CutscenePlayer(CutsceneState cutsceneState, BakAgain.UI.InputCore.InputLayerStack stack, MidiPlaybackManager midiPlayer, ICutsceneFrameProcessor frameProcessor, IDialogManager dialogManager, Func<string, UniTask> showBook = null) {
            _showBook = showBook;
            _cutsceneState = cutsceneState;
            _stack = stack;
            _midiPlayer = midiPlayer;
            _cutsceneState.MidiPlayer = midiPlayer;
            _frameProcessor = frameProcessor;
            _dialogManager = dialogManager;
            _logger = Logger;
        }

        public void Cancel() {
            _cancellation?.Cancel();
        }

        public async UniTask<bool> PlayCutScene(List<Frame> frames, int startFrame = 0, bool attractMode = false) {
            _cancellation = new CancellationTokenSource();
            _skipRequested = false;
            // Push an Exclusive skip layer. Normal mode: Activate/Skip → skip the scene, Cancel →
            // cancel. ATTRACT mode (the intro): ANY input — Activate/Skip OR Cancel — exits to the
            // menu, so Activate maps to cancel too (PlayIntro @ 0x20bbc: any key/click → skip credits →
            // menu). The stack guarantees only the top layer gets intents, so a mid-scene dialog
            // (which pushes its OWN Exclusive layer above) is dismissed without reaching the cutscene.
            System.Action onActivate = attractMode
                ? () => _cancellation.Cancel()
                : () => _skipRequested = true;
            var skipLayer = new BakAgain.UI.InputCore.ActionLayer(
                "cutscene", onActivate, () => _cancellation.Cancel(),
                onMove: null, anyIntentActivates: attractMode);
            _stack.Push(skipLayer);

            try {
                for (int index = startFrame; index < frames.Count; index++) {
                    Frame frame = frames[index];
                    await _frameProcessor.ProcessFrameRuntimeAsync(frame.Commands, _cutsceneState, _cancellation.Token);

                    // Start any audio that was requested during the frame
                    while (_cutsceneState.RequestedAudio.Count > 0) {
                        if (_cutsceneState.RequestedAudio.Dequeue() is { } audioResource) {
                            StartAudio(audioResource);
                        }
                    }

                    // Process any dialog requests from the frame
                    await ProcessDialogRequests();

                    if (_cutsceneState.EndScene || _skipRequested) {
                        _cutsceneState.EndScene = false;
                        _skipRequested = false;

                        return true;
                    }
                    if (_cutsceneState.GotoTag != 0) {
                        int target = FrameSequence.IndexOfTag(frames, _cutsceneState.GotoTag);
                        if (target == FrameSequence.NotFound) {
                            // Was silent: the scene simply carried on from wherever it was, which
                            // looks like the jump was never asked for.
                            Logger.LogWarning(
                                "Cutscene asked to jump to tag {Tag}, which no frame carries; "
                                + "continuing from frame {Index}.", _cutsceneState.GotoTag, index);
                        } else {
                            // *** ONE BEFORE, BECAUSE THE LOOP'S OWN index++ RUNS NEXT. *** A
                            // tagged frame is an ordinary frame that happens to be labelled, so the
                            // jump has to EXECUTE it. Assigning the target directly stepped past it
                            // and dropped everything that frame was going to do — a scene missing a
                            // step rather than a jump visibly going wrong.
                            index = target - 1;
                        }
                        _cutsceneState.GotoTag = 0;
                    }
                }
            } catch (OperationCanceledException) {
                return false;
            } finally {
                // Tear down any panel left up by a final NarrativeAutoAdvance
                // dialog — DisplayEntry intentionally returns without
                // disposing the panel, so the cutscene owns the cleanup on
                // its own exit (natural end, EndScene/skip early-return, or
                // cancellation).
                _dialogManager?.ClearDialog();
                _stack.Remove(skipLayer);
            }

            return true;
        }

        // Holds the final rendered frame on screen while any active palette cycles
        // keep shimmering, until the player cancels (ESC) or interacts. Used by the
        // debug temple preview to verify colour-cycling without the scene ending.
        public async UniTask HoldRenderingAsync() {
            _cancellation = new CancellationTokenSource();
            _skipRequested = false;
            var skipLayer = new BakAgain.UI.InputCore.ActionLayer(
                "cutscene-hold", () => _skipRequested = true, () => _cancellation.Cancel());
            _stack.Push(skipLayer);
            try {
                await HoldPaletteCyclesAsync(_cancellation.Token);
            } finally {
                _stack.Remove(skipLayer);
            }
        }

        /// <summary>
        /// Holds the final frame — palette cycles still running — <b>without taking input</b>, for a
        /// caller that owns its own input layer.
        ///
        /// <para><see cref="HoldRenderingAsync"/> pushes an <c>ActionLayer</c>, which is Exclusive:
        /// nothing underneath it can be clicked. That is right for a cutscene, whose only
        /// interaction is "skip", and wrong for an interactive location, whose whole point is that
        /// the held picture is clickable. A GDS scene plays its entry animation, holds the last
        /// frame through this, and lets its own hotspot layer own the clicks — one owner, as
        /// InputCore requires, rather than two layers racing for the same click.</para>
        ///
        /// <para>Cancellation is the caller's: pass the token that ends when the screen closes.</para>
        /// </summary>
        public UniTask HoldRenderingWithoutInputAsync(CancellationToken cancellationToken) =>
            HoldPaletteCyclesAsync(cancellationToken);

        private async UniTask HoldPaletteCyclesAsync(CancellationToken cancellationToken) {
            try {
                await _frameProcessor.HoldPaletteCyclesAsync(_cutsceneState, cancellationToken);
            } catch (OperationCanceledException) {
                // Cancelled by the player or the owning screen — expected exit path.
            }
        }

        private async UniTask ProcessDialogRequests() {
            while (_cutsceneState.RequestedDialogs.Count > 0) {
                CutsceneDialogRequest request = _cutsceneState.RequestedDialogs.Dequeue();
                if (request.Book != null) {
                    // The scene waits for the book, as the original's gmain_play_chapter_intro does.
                    if (_showBook != null) {
                        await _showBook(request.Book);
                    }

                    continue;
                }
                if (request.Clear) {
                    _dialogManager?.ClearDialog();

                    continue;
                }
                if (request.Entry == null || _dialogManager == null) {
                    continue;
                }

                // Push the live cutscene palette (if any) before rendering, so
                // DialogManager resolves text/chrome pens against it.
                if (request.Palette != null) {
                    _dialogManager.SetActivePalette(request.Palette);
                }

                // ShowEntry awaits the player dismiss; DisplayEntry renders and returns once the panel
                // is on-screen (auto-advance path). The dialog pushes its OWN Exclusive layer above the
                // cutscene's skip layer, so the dismiss routes to the dialog — it never reaches the
                // cutscene skip layer. No _skipRequested cleanup needed (the old IInputHandler hack).
                if (request.WaitForInput) {
                    await _dialogManager.ShowEntry(request.Entry, _cancellation.Token);
                } else {
                    await _dialogManager.DisplayEntry(request.Entry, _cancellation.Token);
                }
            }
        }

        private void StartAudio(AudioResource audioResource) {
            _logger.LogInformation("Starting audio: {AudioResourceName} ({AudioType})", audioResource.Name, audioResource.AudioType);
            if (audioResource.Variants.Count == 0) {
                _logger.LogError("AudioResource {AudioResourceName} has no variants", audioResource.Name);

                return;
            }

            AudioDataResource variant = audioResource.Variants[0];
            if (variant?.MidiData == null) {
                _logger.LogError("AudioResource variant for {AudioResourceName} has no MIDI data", audioResource.Name);

                return;
            }

            byte[] midiData = variant.MidiData;
            if (midiData == null || midiData.Length == 0) {
                _logger.LogError("MIDI data is null or empty for {AudioResourceName}", audioResource.Name);

                return;
            }

            switch (audioResource.AudioType) {
                case AudioType.Music:
                    _midiPlayer.PlaySong(audioResource);

                    break;
                case AudioType.SoundEffect:
                    _midiPlayer.PlaySfx(audioResource);

                    break;
                default:
                    _logger.LogWarning("Unsupported audio type: {AudioType} for {AudioResourceName}", audioResource.AudioType, audioResource.Name);

                    break;
            }
        }

        public UniTask PreProcessSceneAsync(List<FrameCommand> frameCommands) {
            return _frameProcessor.PreloadFrameResourcesAsync(frameCommands, _cutsceneState);
        }

        public void Dispose() {
            _cancellation?.Cancel();

            // Drop any still-queued sound requests (per-cutscene one-shots) so they can't start late.
            // Deliberately do NOT stop the playing music here. The original never stops the intro song:
            // PlayIntro (@0x20bbc) and its only caller _main (@0x21645) contain no song start/stop — the
            // music is an INTRO.ADS audio track that plays continuously through the animation, the
            // credits, and the attract loop. Songs simply replace one another via PlaySong (which does
            // stop-then-play), so a cutscene's music persists until the next song plays. Force-stopping
            // here is what cut the intro theme off before the credits.
            _cutsceneState?.RequestedAudio?.Clear();
            _cutsceneState?.Dispose();
        }
    }
}
