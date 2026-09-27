namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public static class StopSoundPlaybackExtensions {
        private static readonly ILogger Logger =
            LogManager.LoggerFactory.CreateLogger(nameof(StopSoundPlaybackExtensions));

        /// <summary>
        /// Stops a playing sound — TTM 0xC061.
        /// </summary>
        /// <remarks>
        /// <b>Not the same command as <see cref="StopSound"/> (0xC041), despite the names.</b>
        /// 0xC041 UNLOADS the loaded sound resource; this one stops PLAYBACK of the sound with the
        /// given id and clears its looping flag (<c>audio_stopSound</c> @0x3609f — for music it
        /// stops and frees the entry, for an effect it halts the active channel). A scene that
        /// stops a looping effect and later replays it uses this one, so folding the two together
        /// would unload a resource the scene still expects to have.
        ///
        /// <para>Our MidiPlaybackManager.StopSound is the playback-stopping call, which is why both
        /// commands reach it — the unload half of 0xC041 has no counterpart here, since the
        /// resource cache owns lifetime.</para>
        ///
        /// <para><b>And that shared mapping is FAITHFUL, not a convenient approximation</b> —
        /// checked 2026-09-01 because "0xC041 unloads rather than stops" reads like an argument
        /// that our 0xC041 should not stop anything. It should. <c>anim_handle_audio</c> @0x5280f
        /// dispatches 0xC041 to <c>audio_unload_soundEffect</c> → <c>audio_unload</c> @0x3581a,
        /// which calls <c>audio_stopSound</c> at 0x3588b <i>before</i> unlinking the entry. So the
        /// original stops the sound on both commands and additionally frees it on this one; the
        /// half we drop is the half we do not need. A port that made 0xC041 leave the sound playing
        /// would be diverging, not correcting.</para>
        /// </remarks>
        public static Func<CutsceneState, Awaitable> ToAction(this StopSoundPlayback args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                if (!cutsceneState.MidiPlayer) {
                    Logger.LogError(
                        "MidiPlaybackManager is not available in CutsceneState. Cannot stop playback of sound ID {SoundId}.",
                        args.SoundId);

                    return AwaitableUtility.Completed;
                }

                try {
                    cutsceneState.MidiPlayer.StopSound(args.SoundId.ToString());
                } catch (Exception ex) {
                    Logger.LogError(ex, "Error stopping playback of sound ID {SoundId}.", args.SoundId);
                }

                return AwaitableUtility.Completed;
            };
        }
    }
}
