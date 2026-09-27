namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class SelectPaletteSlotExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(SelectPaletteSlotExtensions)); 

        /// Makes the specified palette slot the active palette slot.
        public static Func<CutsceneState, Awaitable> ToAction(this SelectPaletteSlot args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.CurrentPaletteSlot = args.SlotNumber;

                return AwaitableUtility.Completed;
            };
        }
    }
}