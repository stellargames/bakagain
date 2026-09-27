namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class EndSceneExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(EndSceneExtensions));

        public static Func<CutsceneState, Awaitable> ToAction(this EndScene args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                // EndScene (TTM 0x0110) marks the end of the current scene — the
                // ADS layer decides what plays next. Without this, PlayCutScene
                // runs past the scene boundary into the rest of the frame list
                // (e.g. TEMPLE's SYM1 bleeding into all the other symbols).
                cutsceneState.EndScene = true;

                return AwaitableUtility.Completed;
            };
        }
    }
}