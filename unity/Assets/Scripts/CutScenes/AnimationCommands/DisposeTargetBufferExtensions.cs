namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class DisposeTargetBufferExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(DisposeTargetBufferExtensions));

        public static Func<CutsceneState, Awaitable> ToAction(this DisposeTargetBuffer args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.TargetBufferIndexed.DiscardContents();
                // Reset to the canonical full frame — Areas[] are canonical-space
                // (CopyArea scales them by ScaleFromOriginal), not buffer px.
                cutsceneState.Areas[cutsceneState.TargetBufferIndex] = new Area(0, 0, Canonical.Width, Canonical.Height);

                return AwaitableUtility.Completed;
            };
        }
    }
}