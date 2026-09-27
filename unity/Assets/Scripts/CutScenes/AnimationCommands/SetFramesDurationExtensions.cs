namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class SetFramesDurationExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(SetFramesDurationExtensions)); 

        public static Func<CutsceneState, Awaitable> ToAction(this SetFramesDuration args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.FramesDuration = args.Amount;

                return AwaitableUtility.Completed;
            };
        }
    }
}