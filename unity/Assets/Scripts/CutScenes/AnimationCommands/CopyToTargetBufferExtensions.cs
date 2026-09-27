namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class CopyToTargetBufferExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(CopyToTargetBufferExtensions));

        /// Copy an area from the screen (current renderTexture) to the target texture.
        public static Func<CutsceneState, Awaitable> ToAction(this CopyToTargetBuffer args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.CopyArea(cutsceneState.CurrentDrawBufferIndex, cutsceneState.TargetBufferIndex, args);
                cutsceneState.Areas[cutsceneState.TargetBufferIndex] = args;

                return AwaitableUtility.Completed;
            };
        }
    }
}