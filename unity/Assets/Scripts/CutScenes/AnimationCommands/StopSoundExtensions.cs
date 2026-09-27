namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class StopSoundExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(StopSoundExtensions)); 

        /// <summary>
        /// Requests the MidiPlaybackManager to stop the sound associated with the given SoundId.
        /// </summary>
        public static Func<CutsceneState, Awaitable> ToAction(this StopSound args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                if (!cutsceneState.MidiPlayer) {
                    Logger.LogError("MidiPlaybackManager is not available in CutsceneState. Cannot stop sound ID {SoundId}.", args.SoundId);
                    return AwaitableUtility.Completed;
                }

                try {
                    cutsceneState.MidiPlayer.StopSound(args.SoundId.ToString());
                    Logger.LogInformation("Requested to stop sound with ID: {SoundId}", args.SoundId);
                } catch (Exception ex) {
                    Logger.LogError(ex, "Error trying to stop sound ID {SoundId} via MidiPlaybackManager.", args.SoundId);
                }

                return AwaitableUtility.Completed;
            };
        }
    }
}