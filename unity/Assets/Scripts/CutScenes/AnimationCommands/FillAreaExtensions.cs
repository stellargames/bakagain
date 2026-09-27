namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class FillAreaExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(FillAreaExtensions));

        public static Func<CutsceneState, Awaitable> ToAction(this FillArea args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                // *** THE INTERIOR TAKES THE SECOND PEN, NOT THE FIRST. *** TTM 0x2002 assigns
                // p0 to bGfx_outline_color (and bText_fg_color) and p1 to bGfx_fill_color; this
                // opcode is 0xa104, which sets bGfx_fill_enabled = 1 before draw_rect_filled, so
                // the interior is painted in bGfx_fill_color — p1, which our model calls the
                // BACKGROUND colour. Reading ForegroundColorIndex here painted 131 of the shipped
                // fills in pen 6 where the original uses pen 0.
                Drawing.FillArea(args, cutsceneState.BackgroundColorIndex, cutsceneState, true);

                return AwaitableUtility.Completed;
            };
        }
    }
}