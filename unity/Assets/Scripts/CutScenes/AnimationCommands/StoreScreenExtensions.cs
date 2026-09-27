namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class StoreScreenExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(StoreScreenExtensions)); 

        /// Copy buffer B to buffer C
        public static Func<CutsceneState, Awaitable> ToAction(this StoreScreen args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.CopyBuffer(cutsceneState.CurrentDrawBufferIndex, cutsceneState.BackgroundBufferIndex);

                return AwaitableUtility.Completed;
            };
        }
    }
}