namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using GameData.Resources.Audio;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class PlaySoundExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(PlaySoundExtensions)); 

        /// Plays a sound
        public static Func<CutsceneState, Awaitable> ToAction(this PlaySound args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                string resourceKey = args.SoundId.ToString();

                // *** A SONG IS NEVER PRELOADED, AND THAT IS FAITHFUL. *** The preload walk has a
                // case for LoadSound only, and LoadSound is audio_sfx_register — which REFUSES ids
                // at or above MusicPlayback.FirstMusicId (0x3e9). So you do not load a track, and
                // 19 of the 42 shipped scripts play one without loading it: every chapter cutscene.
                //
                // This used to throw for exactly those, and CutsceneFrameProcessor catches and logs
                // each command's exception — so the failure was silent and the chapter cutscenes
                // simply had no music. The original has no such gate: PlaySound is `audio_play(p0)`,
                // which routes on the id and plays.
                //
                // Falling back to a direct load is the same fix GetImage carries for the same shape
                // of miss, and for the same reason: preloading is an optimisation, not a contract.
                if (!cutsceneState.Resources.ContainsKey(resourceKey)) {
                    AudioResource fetched = null;
                    try {
                        fetched = UnityEngine.AddressableAssets.Addressables
                            .LoadAssetAsync<AudioResource>(resourceKey).WaitForCompletion();
                    } catch (Exception e) {
                        Logger.LogWarning(e, "Could not load sound {SoundId} on demand.", args.SoundId);
                    }
                    if (fetched == null) {
                        // Silent rather than fatal: audio_play on an id the driver has nothing for
                        // simply makes no noise.
                        Logger.LogWarning("Sound {SoundId} is not available; nothing played.", args.SoundId);

                        return AwaitableUtility.Completed;
                    }
                    cutsceneState.Resources.Add(resourceKey, fetched);
                }
                AudioResource soundResource = cutsceneState.Resources.Get<AudioResource>(resourceKey);
                cutsceneState.RequestedAudio.Enqueue(soundResource);

                return AwaitableUtility.Completed;
            };
        }
    }
}