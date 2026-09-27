namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class SetTargetBufferExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(SetTargetBufferExtensions)); 

        public static Func<CutsceneState, Awaitable> ToAction(this SetTargetBuffer args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.TargetBufferIndex = args.BufferNumber;

                return AwaitableUtility.Completed;
            };
        }
    }
}