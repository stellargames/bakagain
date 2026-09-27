namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class SetColorsExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(SetColorsExtensions)); 

        public static Func<CutsceneState, Awaitable> ToAction(this SetColors args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.BackgroundColorIndex = args.BackgroundColor;
                cutsceneState.ForegroundColorIndex = args.ForegroundColor;

                return AwaitableUtility.Completed;
            };
        }
    }
}