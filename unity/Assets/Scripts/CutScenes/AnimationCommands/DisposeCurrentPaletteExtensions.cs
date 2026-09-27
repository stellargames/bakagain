namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class DisposeCurrentPaletteExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(DisposeCurrentPaletteExtensions));

        public static Func<CutsceneState, Awaitable> ToAction(this DisposeCurrentPalette args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.SetPalette(null);

                return AwaitableUtility.Completed;
            };
        }
    }
}