namespace BakAgain.CutScenes
{
    using BakAgain.Core;
    using BakAgain.ResourceManagement.Models;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation.FrameCommands;
    using GameData.Resources.Audio;
    using GameData.Resources.Dialog;
    using GameData.Resources.Palette;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using UnityEngine;
    using UnityEngine.AddressableAssets;

    public class CutsceneFrameProcessor : ICutsceneFrameProcessor
    {
        private readonly ILogger<CutsceneFrameProcessor> _logger;
        private readonly IResourceCache _resourceCache;

        public CutsceneFrameProcessor(ILogger<CutsceneFrameProcessor> logger, IResourceCache resourceCache)
        {
            _logger = logger;
            _resourceCache = resourceCache;
        }

        /// <summary>
        /// Draws one frame: every command in order, then the three render passes.
        /// </summary>
        /// <remarks>
        /// <b>The runtime and Editor entry points differ only in what they do AFTERWARDS</b> — the
        /// runtime holds for the frame's duration, the Editor yields once so the Studio can repaint.
        /// They used to carry a byte-identical copy of this each, which is how a fix lands in one
        /// and not the other; the preload walk beside them drifted exactly that way.
        ///
        /// <para>A command that throws is logged and the frame carries on. Cancellation is not a
        /// failure and is rethrown, so a skipped cutscene does not log ~40 errors on its way out.</para>
        /// </remarks>
        private async UniTask RunCommandsAsync(IEnumerable<FrameCommand> commands, CutsceneState state, CancellationToken cancellationToken)
        {
            state.PrepareBuffers();

            foreach (var frameCommand in commands.Select(AnimationCommandMap.GetAction).Where(func => func != null))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await frameCommand(state);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Exception during frame command execution");
                }
            }
            cancellationToken.ThrowIfCancellationRequested();

            state.RenderIndexed();
            state.RenderDirect();
            state.RenderOutput();
        }

        public async UniTask ProcessFrameRuntimeAsync(IEnumerable<FrameCommand> commands, CutsceneState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double segmentStartTimeReal = Time.realtimeSinceStartupAsDouble;

            await RunCommandsAsync(commands, state, cancellationToken);

#if UNITY_EDITOR
            // The frame is drawn and not yet held -- the same point in the loop the emulator-side
            // override captures at (seg020:0x06FA). Off unless CutsceneFrameCapture.Begin was called.
            CutsceneFrameCapture.Capture(state.OutputBuffer);
#endif

            double segmentEndTimeReal = Time.realtimeSinceStartupAsDouble;
            double processingTimeSeconds = segmentEndTimeReal - segmentStartTimeReal;

            float targetTotalDurationSeconds = CutsceneTiming.FrameHoldSeconds(state.FramesDuration);

            float waitTimeSeconds =
                CutsceneTiming.WaitAfterProcessing(targetTotalDurationSeconds, processingTimeSeconds);

            if (Application.isPlaying)
            {
                if (CutsceneTiming.HoldsAtAll(waitTimeSeconds))
                {
                    if (state.HasActivePaletteCycles)
                    {
                        await HoldWithPaletteCyclesAsync(state, waitTimeSeconds, cancellationToken);
                    }
                    else
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(waitTimeSeconds), cancellationToken: cancellationToken);
                    }
                }
                else
                {
                    await UniTask.Yield(cancellationToken);
                }
            }
        }

        // VGA palette colour-cycling is timer-driven and independent of frame advancement, so it
        // must keep rotating during a frame's hold. The interval and the accumulate-don't-restart
        // rule both live on CutsceneTiming now, with the reasoning that goes with them.

        private static async UniTask HoldWithPaletteCyclesAsync(CutsceneState state, float waitTimeSeconds, CancellationToken cancellationToken)
        {
            double holdStart = Time.realtimeSinceStartupAsDouble;
            double nextStep = CutsceneTiming.PaletteCycleStepSeconds;
            while (true)
            {
                double elapsed = Time.realtimeSinceStartupAsDouble - holdStart;
                if (elapsed >= waitTimeSeconds)
                {
                    break;
                }
                if (elapsed >= nextStep)
                {
                    if (state.AdvancePaletteCycles())
                    {
                        state.RenderIndexed();
                        state.RenderOutput();
                    }
                    nextStep = CutsceneTiming.NextCycleStep(nextStep);
                }
                await UniTask.Yield(cancellationToken);
            }
        }

        // Holds the last rendered frame on screen indefinitely while keeping any
        // active palette cycles shimmering. Runs until cancelled — used to preview
        // a static-with-shimmer scene (e.g. the temple symbols).
        public async UniTask HoldPaletteCyclesAsync(CutsceneState state, CancellationToken cancellationToken)
        {
            double holdStart = Time.realtimeSinceStartupAsDouble;
            double nextStep = CutsceneTiming.PaletteCycleStepSeconds;
            while (!cancellationToken.IsCancellationRequested)
            {
                double elapsed = Time.realtimeSinceStartupAsDouble - holdStart;
                if (elapsed >= nextStep)
                {
                    if (state.AdvancePaletteCycles())
                    {
                        state.RenderIndexed();
                        state.RenderOutput();
                    }
                    nextStep = CutsceneTiming.NextCycleStep(nextStep);
                }
                await UniTask.Yield(cancellationToken);
            }
        }

        public async UniTask ProcessFrameEditorAsync(IEnumerable<FrameCommand> commands, CutsceneState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await RunCommandsAsync(commands, state, cancellationToken);

            await UniTask.Yield(cancellationToken); // Yield to allow UI to update
        }

        /// <summary>
        /// Preloads a resource a frame names, tolerating one the game does not contain.
        /// </summary>
        /// <remarks>
        /// <b>The shipped data names resources that are not in the game.</b> <c>g_misc</c> asks for
        /// <c>G_BKLIAL.PAL</c>, which no archive entry provides — the neighbouring <c>G_BKLIBR.PAL</c>
        /// loads fine, so it is one dangling name rather than a broken loader. The original must
        /// shrug that off, because the scene it belongs to ships and is reachable from four world
        /// triggers.
        ///
        /// <para>Letting the load throw took the whole animation with it: GDS40K rendered nothing and
        /// reported only "error animating". Preloading is an optimisation, so a name that resolves to
        /// nothing is worth a line in the log and no more.</para>
        /// </remarks>
        private async UniTask PreloadOptional<T>(string filename, CutsceneState state, IResourceCache cache) where T : class {
            if (string.IsNullOrEmpty(filename) || state.Resources.ContainsKey(filename)) {
                return;
            }

            T resource;
            try {
                resource = await cache.GetOrLoadAsync<T>(filename);
            } catch (System.Exception e) {
                BakAgain.Core.ConditionalLoggingExtensions.LogWarning(_logger,
                    "Frame names {Filename}, which this game does not contain ({Reason}); skipped.",
                    filename, e.GetType().Name);
                return;
            }

            if (resource != null) {
                state.Resources.Add(filename, resource);
            }
        }

        /// <summary>
        /// Loads what a frame's commands will ask for, before the frame is drawn.
        /// </summary>
        public UniTask PreloadFrameResourcesAsync(IEnumerable<FrameCommand> commands, CutsceneState state) =>
            PreloadWalkAsync(commands, state, _resourceCache);

        /// <summary>
        /// The Cutscene Studio's preload — <b>the same walk</b>, driven to completion.
        /// </summary>
        /// <remarks>
        /// The Studio builds its frame list from a synchronous UI callback and cannot await, which is
        /// the only reason this entry point exists. It used to be a hand-copied second walk, and the
        /// copies drifted: the dangling-name tolerance that <c>G_BKLIAL.PAL</c> forced onto the
        /// runtime walk never reached this one, so a scene that played fine in the game still took
        /// the Studio down. Sharing the walk is what stops that happening again — the nine tests over
        /// <see cref="PreloadWalkAsync"/> now cover both callers.
        ///
        /// <para><see cref="BlockingCache"/> completes every load synchronously, so the whole walk
        /// finishes before the await ever suspends and <c>GetResult</c> cannot deadlock.</para>
        /// </remarks>
        public void PreloadFrameResourcesEditor(IEnumerable<FrameCommand> commands, CutsceneState state) =>
            PreloadWalkAsync(commands, state, BlockingCache).GetAwaiter().GetResult();

        private static readonly IResourceCache BlockingCache = new AddressablesBlockingCache();

        /// <summary>Addressables driven with <c>WaitForCompletion</c>, for the synchronous Editor walk.</summary>
        /// <remarks>
        /// Deliberately keeps no handles: it replaces a walk that called <c>Addressables</c> inline
        /// and released nothing, so it retains exactly the previous behaviour. The Studio is an
        /// authoring tool with an Editor-lifetime asset set — if that ever needs releasing, the fix
        /// is to hand it the real <see cref="ResourceCache"/>, not to grow this.
        /// </remarks>
        private sealed class AddressablesBlockingCache : IResourceCache {
            public UniTask<T> GetOrLoadAsync<T>(string key) where T : class =>
                UniTask.FromResult(Addressables.LoadAssetAsync<T>(key).WaitForCompletion());

            public void Clear() { }
        }

        private async UniTask PreloadWalkAsync(IEnumerable<FrameCommand> commands, CutsceneState state, IResourceCache cache)
        {
            bool hasDialog = false;
            foreach (FrameCommand frameCommand in commands)
            {
                switch (frameCommand)
                {
                    case SelectImageSlot selectImageSlot:
                        state.CurrentImageSlot = selectImageSlot.SlotNumber;
                        break;
                    case LoadImageResource loadImage:
                        if (loadImage.Filename != null)
                        {
                            state.ImageSlots[state.CurrentImageSlot] = loadImage.Filename;
                        }
                        break;
                    case DisposeCurrentBitmap:
                        // Empties the slot, as the runtime command does. Without this the walk kept
                        // pairing later draws with a bitmap the script had already thrown away:
                        // C51.TTM loads C51B_JNL.BMP (two images) into slot 1, disposes it, and jumps;
                        // the draws of images 2..7 that follow in the file are reached from elsewhere,
                        // with C51A_PTR.BMP in that slot. Six not-found errors per chapter-5 start
                        // (TASK-634).
                        state.SetCurrentImageName(null);
                        break;
                    case DrawImageBase drawImage:
                        await PreLoadImageResourceAsync(state, drawImage.ImageSlot, drawImage.ImageNumber, cache);
                        break;
                    case LoadPaletteResource loadPalette:
                        await PreloadOptional<PaletteResource>(loadPalette.Filename, state, cache);
                        break;
                    case LoadScreenResource loadScreen:
                        await PreloadOptional<IndexedTexture>(loadScreen.Filename, state, cache);
                        break;
                    case DialogCommand dialogCommand:
                        hasDialog = true;
                        if (dialogCommand.Dialog16Id == 0 && dialogCommand.Arg2 is >= 0 and <= 20)
                        {
                            await PreLoadImageResourceAsync(state, 0, 0, cache);
                        }
                        else if (dialogCommand.Dialog16Id > 0)
                        {
                            int dialogId = GameData.Resources.Animation.CutsceneDialogCommand.DialogIdFor(dialogCommand.Dialog16Id);
                            string ddxKey = GameData.Resources.Animation.CutsceneDialogCommand.DdxKeyFor(dialogId);
                            if (!state.Resources.ContainsKey(ddxKey))
                            {
                                var dialog = await cache.GetOrLoadAsync<Dialog>(ddxKey);
                                if (dialog != null)
                                    state.Resources.Add(ddxKey, dialog);
                            }
                        }
                        break;
                    case LoadSound loadSound:
                        var soundIdStr = loadSound.SoundId.ToString();
                        if (!state.Resources.ContainsKey(soundIdStr))
                        {
                            var loadSoundResource = await cache.GetOrLoadAsync<AudioResource>(loadSound.SoundId.ToString());
                            if(loadSoundResource != null)
                                state.Resources.Add(soundIdStr, loadSoundResource);
                        }
                        break;
                    case PlaySound playSound:
                        var playSoundIdStr = playSound.SoundId.ToString();
                        if (!state.Resources.ContainsKey(playSoundIdStr))
                        {
                            var playSoundResource = await cache.GetOrLoadAsync<AudioResource>(playSound.SoundId.ToString());
                            if(playSoundResource != null)
                                state.Resources.Add(playSoundIdStr, playSoundResource);
                        }
                        break;
                }
            }
            if (hasDialog)
            {
                var dialogBackground = await cache.GetOrLoadAsync<IndexedTexture>("DIALOG.SCX");
                if(dialogBackground != null && !state.Resources.ContainsKey("DIALOG.SCX"))
                    state.Resources.Add("DIALOG.SCX", dialogBackground);
            }
        }

        private async UniTask PreLoadImageResourceAsync(CutsceneState state, int imageSlot, int imageNumber, IResourceCache cache)
        {
            // Guarded exactly as the editor variant above is. Preloading walks EVERY frame's commands
            // before playback has assigned any image slot, so a frame that names a slot nothing has
            // filled yet is normal — the indexer here threw KeyNotFoundException instead, which
            // escaped as an unobserved UniTask exception and killed the whole cutscene with no
            // message tying it to a scene. Skipping is safe: preloading is an optimisation, and the
            // image is loaded on demand once the slot is really set.
            if (!state.ImageSlots.TryGetValue(imageSlot, out string imageDirName) || imageDirName == null) {
                _logger.LogDebug("Preloading image for runtime skipped: slot {ImageSlot} not assigned yet ({ImageNumber}).",
                    imageSlot, imageNumber);
                return;
            }

            string imageKey = $"{imageDirName}#{imageNumber}";
            if (state.Resources.ContainsKey(imageKey))
            {
                return;
            }
            _logger.LogDebug("Preloading image for runtime: {ImageSlot} {ImageNumber} [{ImageKey}]", imageSlot, imageNumber, imageKey);
            IndexedTexture indexedTexture;
            try {
                indexedTexture = await cache.GetOrLoadAsync<IndexedTexture>(imageKey);
            }
            catch (System.Exception) {
                // Same reasoning as the unassigned-slot guard above, one case later. The walk pairs a
                // DrawImage's index with whatever file the slot holds when the walk reaches it, but
                // playback jumps between frames, so the slot that is current at the moment a draw
                // really executes need not be the one the linear walk saw. An index the paired file
                // does not have is therefore an artefact of walking, not a broken script — C21 draws
                // indices up to 13 across several slot files and preloading paired index 4 with
                // C21B1.BMX, which has four images. That killed the chapter-2 cutscene outright.
                // Catch is deliberately BROAD. Preloading is an optimisation and the image is
                // loaded on demand later, so no failure here is worth surfacing -- and the failure
                // arrives as a different type depending on where it is detected: the provider now
                // reports an out-of-range sub-index by returning null (see BakResourceProvider),
                // which Addressables completes as a plain Exception, where it used to escape as
                // IndexOutOfRangeException. Catching only the latter left the former unhandled and
                // logged as an Exception, which is TASK-294 wearing a new hat.
                _logger.LogDebug("Preloading image for runtime skipped: {ImageKey} could not be paired by the walk.", imageKey);
                return;
            }
            if(indexedTexture != null)
                state.Resources.Add(imageKey, indexedTexture);
        }
    }
}
