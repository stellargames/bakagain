namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    /// <summary>
    /// TTM <c>0x4204</c> — blit a rectangle to the other page.
    /// </summary>
    /// <remarks>
    /// <b>It records NO area, and that asymmetry with CopyToTargetBuffer is faithful.</b> The two
    /// neighbouring commands are different operations in the original:
    /// <list type="bullet">
    ///   <item><c>0x4214</c> (CopyToTargetBuffer) allocates a buffer, saves the rect into it, AND
    ///     stores the rect's coordinates in <c>pRectSrcX/Y</c> and <c>pRectDstX/Y</c> under the
    ///     slot — geometry kept alongside the pixels, which is what our <c>Areas[]</c> models.</item>
    ///   <item><c>0x4204</c> (this) is <c>adscript_rndr_blit_other_page</c>: a straight page-to-page
    ///     blit (VGA page 2 -&gt; page 1) that keeps nothing.</item>
    /// </list>
    /// So a reader who "fixes" this to record an area is un-porting it. Established 2026-08-25 by
    /// reading both handlers, after the difference was noticed and left open as unexplained.
    ///
    /// <para><b>One condition is NOT modelled:</b> the original also gates this on
    /// <c>g_nVgaRenderMode &gt;= 3</c>, so on a lower render mode the blit does not happen at all.
    /// We have no render-mode concept, so ours always blits.</para>
    /// </remarks>
    public static class StoreAreaExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(StoreAreaExtensions)); 

        /// Copy area from buffer B to buffer C
        public static Func<CutsceneState, Awaitable> ToAction(this StoreArea args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.CopyArea(cutsceneState.CurrentDrawBufferIndex, cutsceneState.BackgroundBufferIndex, args);

                return AwaitableUtility.Completed;
            };
        }
    }
}