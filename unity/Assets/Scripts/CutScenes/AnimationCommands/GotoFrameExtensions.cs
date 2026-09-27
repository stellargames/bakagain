namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class GotoFrameExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(GotoFrameExtensions)); 

        /// Set the specified frame as the next to execute.
        public static Func<CutsceneState, Awaitable> ToAction(this GotoFrame args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.GotoTag = args.NextFrame;

                return AwaitableUtility.Completed;
            };
        }
    }
}