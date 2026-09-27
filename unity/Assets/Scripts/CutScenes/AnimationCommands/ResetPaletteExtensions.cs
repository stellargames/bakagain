namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public static class ResetPaletteExtensions {
        private static readonly ILogger Logger =
            LogManager.LoggerFactory.CreateLogger(nameof(ResetPaletteExtensions));

        /// <summary>
        /// Re-applies the current palette and cancels any running palette cycle — TTM 0x0400.
        /// </summary>
        /// <remarks>
        /// <b>It re-applies the CURRENT palette, not the default one.</b> The original
        /// (<c>anim_executeFrameFunctions</c>, loc_ovr153_D2D @0x534ad) resets the palette range
        /// and then calls <c>SetPaletteOrDefault(anim_pCurrentPalette)</c> — the palette the scene
        /// has loaded, which is usually not the default. Reading the name as "go back to the
        /// default palette" would recolour the scene.
        ///
        /// <para>What it actually undoes is the two things that write the DAC out from under the
        /// loaded palette: a fade, and a running cycle. A fade leaves its final colours in the
        /// slot, so re-uploading the slot is enough to put it back; a cycle keeps rotating and has
        /// to be stopped, which is what clearing the windows does.</para>
        /// </remarks>
        public static Func<CutsceneState, Awaitable> ToAction(this ResetPalette args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                // Order matters: stop the cycle before re-uploading, or the next cycle tick would
                // immediately rotate the palette we just restored.
                cutsceneState.SetPaletteCycles(null);
                cutsceneState.SetPalette(cutsceneState.CurrentPalette);

                return AwaitableUtility.Completed;
            };
        }
    }
}
