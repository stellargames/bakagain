namespace BakAgain.Audio {
    using System;
    using BakAgain.Core;
    using BakAgain.CutScenes;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Audio;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    // Plays short UI SFX (button select / toggle flip) by sound id. Reuses MidiPlaybackManager.PlaySfx
    // (the audio owner) and IResourceCache (the session resource cache) — sounds live in FRP.SX, keyed
    // by numeric id string, the same path cutscenes use. Default menu select cue is sound_pound (id 83).
    // See docs/superpowers/specs/2026-06-24-ui-select-sounds-design.md.
    public sealed class MenuSoundService {
        // Composition seam: set once at container build by RootLifetimeScope, mirroring
        // UiDriver (and MidiPlaybackManager.Instance in this same layer). UserInterfaceLoader
        // lives as a sibling on REQ prefabs that VContainer's RegisterComponentInNewPrefab does
        // NOT inject, so it reads the service from here rather than reaching into the container.
        // Null until the container is built.
        public static MenuSoundService Instance { get; set; }

        // sound_pound — the default menu select/click cue (generated/SND/83_pound;
        // RE: sub_seg030_97F pushes sound_pound at 0x2cb12).
        public const int PoundSoundId = 83;

        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(MenuSoundService));
        private readonly MidiPlaybackManager _midi;
        private readonly IResourceCache _cache;

        public MenuSoundService(MidiPlaybackManager midi, IResourceCache cache) {
            _midi = midi;
            _cache = cache;
        }

        // Fire-and-forget: load (cached) the AudioResource for soundId and play it as a one-shot SFX.
        public void Play(int soundId) {
            PlayAsync(soundId, extraRepeats: 0).Forget();
        }

        /// <summary>
        /// Play a cue and then repeat it <paramref name="extraRepeats"/> more times, in sequence.
        /// </summary>
        /// <remarks>
        /// <b>The count is EXTRA repeats, matching the data</b>:
        /// <c>audio_sfx_play_n_times(int sfx_id, int extra_repeats, int blocking)</c>, so 0 is the
        /// ordinary single play and the overload above is exactly that.
        ///
        /// <para><b>Sequenced, not stacked.</b> Each repeat waits for the previous one to stop —
        /// three <c>Play</c> calls in a frame take three pool players and sound together, which is
        /// one louder strike rather than the Armorer's Hammer's three.</para>
        ///
        /// <para>The wait is capped so a cue that never reports finishing (a recycled pool player,
        /// a driver with no variant for this sound) cannot hang the sequence — it gives up on that
        /// repeat and moves on rather than waiting for ever.</para>
        /// </remarks>
        public void Play(int soundId, int extraRepeats) {
            PlayAsync(soundId, extraRepeats).Forget();
        }

        /// <summary>How long one repeat will wait for the previous play to finish.</summary>
        /// <remarks>
        /// Generous: the longest shipped SFX are well under this. It exists to bound the wait, not
        /// to time the sound.
        /// </remarks>
        private const int RepeatTimeoutMilliseconds = 4000;

        // Preload the default pound cue to avoid a load hitch on the first select. Best-effort
        // (fire-and-forget): a very fast first click can still race the preload and load on demand.
        // Safe to call repeatedly (IResourceCache memoizes). Call once a menu has built (after init).
        public void Warmup() {
            LoadAsync(PoundSoundId).Forget();
        }

        private async UniTaskVoid PlayAsync(int soundId, int extraRepeats) {
            AudioResource resource = await LoadAsync(soundId);
            if (resource == null) {
                return;
            }
            _midi.PlaySfx(resource);

            for (var repeat = 0; repeat < extraRepeats; repeat++) {
                if (!await WaitForQuietAsync(resource.Id)) {
                    return;
                }
                _midi.PlaySfx(resource);
            }
        }

        /// <summary>Waits until the cue stops, or gives up. False means "stop repeating".</summary>
        private async UniTask<bool> WaitForQuietAsync(string resourceId) {
            var waited = 0;
            const int step = 50;
            while (_midi.IsSfxPlaying(resourceId)) {
                await UniTask.Delay(step);
                waited += step;
                if (waited >= RepeatTimeoutMilliseconds) {
                    Logger.LogWarning(
                        "MenuSoundService: sound {SoundId} never reported finishing; dropping its "
                        + "remaining repeats.", resourceId);
                    return false;
                }
            }
            return true;
        }

        /// <summary>Stop a cue that was started to be held — the original's
        /// <c>audio_driver_stop(id)</c>.</summary>
        /// <remarks>
        /// <b>Only meaningful for a cue that is meant to be held.</b> Two spell effect arms start
        /// one and end it explicitly (<see cref="GameData.Resources.Spells.SpellEffectArmSound"/>),
        /// which a fire-and-forget play cannot express. Stopping a cue that already finished is a
        /// no-op, so this is safe to call unconditionally.
        /// </remarks>
        public void Stop(int soundId) {
            StopAsync(soundId).Forget();
        }

        private async UniTaskVoid StopAsync(int soundId) {
            AudioResource resource = await LoadAsync(soundId);
            if (resource != null) {
                _midi.StopSound(resource.Id);
            }
        }

        private async UniTask<AudioResource> LoadAsync(int soundId) {
            try {
                return await _cache.GetOrLoadAsync<AudioResource>(soundId.ToString());
            } catch (Exception e) {
                Logger.LogWarning("MenuSoundService: failed to load sound {SoundId}: {Message}", soundId, e.Message);
                return null;
            }
        }
    }
}
