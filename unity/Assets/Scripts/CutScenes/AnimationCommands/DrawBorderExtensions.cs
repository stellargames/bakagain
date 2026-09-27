namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class DrawBorderExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(DrawBorderExtensions));

        public static Func<CutsceneState, Awaitable> ToAction(this DrawBorder args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                // The mirror of FillArea: 0xa114 clears bGfx_fill_enabled before the same
                // draw_rect_filled, so only the outline is drawn and it uses bGfx_outline_color —
                // p0, our FOREGROUND colour. Invisible in the shipped scripts, where every
                // DrawBorder runs under a SetColors whose two pens are equal, and wrong anyway.
                Drawing.DrawBorder(args, cutsceneState.ForegroundColorIndex, cutsceneState);

                return AwaitableUtility.Completed;
            };
        }
    }
}