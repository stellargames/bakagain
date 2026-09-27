namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class LoadSoundResourceExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(LoadSoundResourceExtensions)); 

        public static Func<CutsceneState, Awaitable> ToAction(this LoadSoundResource args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                if (args.Filename != "FRP.SX") {
                    throw new InvalidOperationException($"Only the FRP.SX sound resource is supported, but got {args.Filename}");
                }

                return AwaitableUtility.Completed;
            };
        }
    }
}