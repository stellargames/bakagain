namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class CopyAreaBetweenBuffersExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(CopyAreaBetweenBuffersExtensions));

        /// Copy an area from the source buffer to the target buffer.
        public static Func<CutsceneState, Awaitable> ToAction(this CopyAreaBetweenBuffers args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.CopyArea(args.SourceBuffer, args.DestinationBuffer, args);

                return AwaitableUtility.Completed;
            };
        }
    }
}