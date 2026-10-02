namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class DisposeTargetBufferExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(DisposeTargetBufferExtensions));

        public static Func<CutsceneState, Awaitable> ToAction(this DisposeTargetBuffer args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.FreeRect(cutsceneState.TargetBufferIndex);

                return AwaitableUtility.Completed;
            };
        }
    }
}