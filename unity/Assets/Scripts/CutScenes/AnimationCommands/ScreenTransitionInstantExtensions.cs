namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The two "instant" screen transitions — TTM 0xA014 and 0xA0B5.
    /// </summary>
    /// <remarks>
    /// <b>These do nothing, and that is the faithful behaviour rather than a gap.</b>
    /// <c>anim_screenTransitionEffect</c> @0x53ab5 redirects the draw target to buffer A on entry
    /// and switches on the opcode; the A014/A0B5 arm restores the draw buffer and returns without
    /// copying anything. The new frame simply appears — there is no wipe to reproduce.
    ///
    /// <para><b>Mapped to an explicit no-op rather than left unmapped.</b> An unmapped command is
    /// dropped by <c>CutsceneFrameProcessor</c>'s null filter, which looks identical — in the code
    /// and in the logs — to a command nobody has implemented yet. Dispatching to a documented
    /// no-op keeps the debug log showing the scene's real command stream, and keeps "we checked,
    /// it does nothing" distinguishable from "we have not looked".</para>
    ///
    /// <para>0xA0B5 carries a fifth argument that the engine reads from the script and ignores, so
    /// the two arms are identical in effect.</para>
    /// </remarks>
    public static class ScreenTransitionInstantExtensions {
        private static readonly ILogger Logger =
            LogManager.LoggerFactory.CreateLogger(nameof(ScreenTransitionInstantExtensions));

        public static Func<CutsceneState, Awaitable> ToAction(this ScreenTransitionInstant args) =>
            NoVisibleTransition(args);

        public static Func<CutsceneState, Awaitable> ToAction(this ScreenTransitionInstant5 args) =>
            NoVisibleTransition(args);

        private static Func<CutsceneState, Awaitable> NoVisibleTransition(FrameCommand args) {
            return _ => {
                Logger.LogDebug("Running frame command: {Args} (instant style — no visible wipe)", args);

                return AwaitableUtility.Completed;
            };
        }
    }
}
