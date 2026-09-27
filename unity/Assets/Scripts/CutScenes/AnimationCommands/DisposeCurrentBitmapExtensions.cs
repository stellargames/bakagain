namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class DisposeCurrentBitmapExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(DisposeCurrentBitmapExtensions));

        public static Func<CutsceneState, Awaitable> ToAction(this DisposeCurrentBitmap args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.SetCurrentImageName(null);

                return AwaitableUtility.Completed;
            };
        }
    }
}