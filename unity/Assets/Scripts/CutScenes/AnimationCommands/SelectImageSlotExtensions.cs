namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class SelectImageSlotExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(SelectImageSlotExtensions)); 

        public static Func<CutsceneState, Awaitable> ToAction(this SelectImageSlot args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.CurrentImageSlot = args.SlotNumber;

                return AwaitableUtility.Completed;
            };
        }
    }
}