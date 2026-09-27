namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public static class LoadFontResourceExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(LoadFontResourceExtensions));

        /// <summary>
        /// Loads a font into the active slot — <b>deliberately nothing here</b>. See
        /// <see cref="SelectFontSlotExtensions.ToAction"/> for the evidence; both commands share it.
        /// </summary>
        /// <remarks>
        /// TTM opcode <c>0xf04f</c> unloads whatever the node held, loads the named font and
        /// activates it. Every occurrence in the shipped data — both of them, in C21 and C42 —
        /// names <b>GAME.FNT</b>, which is the font our dialog surface already draws in, and no
        /// shipped script contains a text-drawing command to use it with.
        ///
        /// <para>Loading it anyway would put a second copy of a font we already have behind a slot
        /// nothing reads. If a text-drawing command is ever added to the frame set — mod-authored
        /// data could carry one — this is where the slot has to become real.</para>
        /// </remarks>
        public static Func<CutsceneState, Awaitable> ToAction(this LoadFontResource args) {
            return _ => {
                Logger.LogDebug("Running frame command: {Args}", args);
                return AwaitableUtility.Completed;
            };
        }
    }
}
