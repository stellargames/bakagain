namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class LoadSoundExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(LoadSoundExtensions)); 

        /// Loads a sound from the sound file and stores it in a list.
        public static Func<CutsceneState, Awaitable> ToAction(this LoadSound args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                // Should have been preloaded in CutScenes.CutscenePlayer.PreProcessScene
                if (!cutsceneState.Resources.ContainsKey(args.SoundId.ToString())) {
                    throw new InvalidOperationException($"Sound ID {args.SoundId} not found in resources.");
                }

                return AwaitableUtility.Completed;
            };
        }
    }
}