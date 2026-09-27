namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class SetClipAreaExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(SetClipAreaExtensions)); 

        /// Sets the clip area for the current frame. Draw actions are limited to this area.
        public static Func<CutsceneState, Awaitable> ToAction(this SetClipArea args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.ClipArea = new Rect(args.X, args.Y, args.Width, args.Height);

                return AwaitableUtility.Completed;
            };
        }
    }
}