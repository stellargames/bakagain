namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public static class SelectFontSlotExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(SelectFontSlotExtensions));

        /// <summary>
        /// Makes a font slot active — <b>deliberately nothing here</b>, because nothing in the
        /// shipped scripts ever draws with it.
        /// </summary>
        /// <remarks>
        /// TTM opcode <c>0x1071</c> stores the slot on the anim node and activates that slot's font
        /// handle. The only consumer is the TTM text-drawing opcode range (<c>0xa204</c> and up),
        /// which re-activates <c>pAhFont[slot]</c> and calls <c>font_draw_text_ds</c>.
        ///
        /// <para><b>Not one of the 42 shipped scripts contains a text-drawing command.</b> A census
        /// of every command in <c>generated/TTM</c> finds the whole font story is two occurrences of
        /// this command and two of <see cref="LoadFontResource"/>, in C21 and C42, both selecting
        /// slot 0 and both loading GAME.FNT. So the selected slot has no reader.</para>
        ///
        /// <para>The cutscene text a player actually sees comes from <c>DialogCommand</c>, which
        /// goes to the dialog surface and draws in GAME.FNT — the same font these two commands ask
        /// for. Our output therefore already matches whatever they were for.</para>
        ///
        /// <para><b>This used to throw</b>, which the frame processor caught and logged as an error,
        /// so those two cutscenes each reported a failure while playing correctly. A no-op with the
        /// reason written down beats a red line for a command with nothing to do.</para>
        /// </remarks>
        public static Func<CutsceneState, Awaitable> ToAction(this SelectFontSlot args) {
            return _ => {
                Logger.LogDebug("Running frame command: {Args}", args);
                return AwaitableUtility.Completed;
            };
        }
    }
}
