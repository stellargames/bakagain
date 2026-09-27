namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class DrawAreaFromBufferExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(DrawAreaFromBufferExtensions));

        public static Func<CutsceneState, Awaitable> ToAction(this DrawAreaFromBuffer args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.CopyArea(args.BufferNumber);

                return AwaitableUtility.Completed;
            };
        }
    }
}