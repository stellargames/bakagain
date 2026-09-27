namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class LoadImageResourceExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(LoadImageResourceExtensions)); 

        public static Func<CutsceneState, Awaitable> ToAction(this LoadImageResource args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.SetCurrentImageName(args.Filename);

                return AwaitableUtility.Completed;
            };
        }
    }
}