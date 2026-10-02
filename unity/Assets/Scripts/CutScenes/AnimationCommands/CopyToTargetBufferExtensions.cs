namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class CopyToTargetBufferExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(CopyToTargetBufferExtensions));

        /// Save an area of the current page into the target slot (0x4214).
        public static Func<CutsceneState, Awaitable> ToAction(this CopyToTargetBuffer args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.SaveRect(cutsceneState.TargetBufferIndex, args);

                return AwaitableUtility.Completed;
            };
        }
    }
}