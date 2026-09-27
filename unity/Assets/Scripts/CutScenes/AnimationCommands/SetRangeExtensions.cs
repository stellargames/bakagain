namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger; 

    public static class SetRangeExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(SetRangeExtensions)); 

        public static Func<CutsceneState, Awaitable> ToAction(this SetRange1 args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.Range1 = (args.Start, args.End);

                return AwaitableUtility.Completed;
            };
        }

        public static Func<CutsceneState, Awaitable> ToAction(this SetRange2 args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.Range2 = (args.Start, args.End);

                return AwaitableUtility.Completed;
            };
        }

        public static Func<CutsceneState, Awaitable> ToAction(this SetRange3 args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                cutsceneState.Range3 = (args.Start, args.End);

                return AwaitableUtility.Completed;
            };
        }

        public static Func<CutsceneState, Awaitable> ToAction(this StartPaletteCycle args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                // ONLY THE SIGN MATTERS: the original computes si = p1 / abs(p1) and passes
                // that as the rotation amount, so Step 7 and Step 1 cycle identically. Step 0
                // is forced to 1 there, which is why >= 0 maps to forward rather than to "off".
                // abs(Step) is stored in a global that nothing ever reads.
                int direction = args.Step >= 0 ? 1 : -1;
                var cycles = new List<CutsceneState.PaletteCycle>(3);
                if ((args.Range & Ranges.Range1) != 0) {
                    cycles.Add(new CutsceneState.PaletteCycle {
                        Start = cutsceneState.Range1.Start, End = cutsceneState.Range1.End, Direction = direction
                    });
                }
                if ((args.Range & Ranges.Range2) != 0) {
                    cycles.Add(new CutsceneState.PaletteCycle {
                        Start = cutsceneState.Range2.Start, End = cutsceneState.Range2.End, Direction = direction
                    });
                }
                if ((args.Range & Ranges.Range3) != 0) {
                    cycles.Add(new CutsceneState.PaletteCycle {
                        Start = cutsceneState.Range3.Start, End = cutsceneState.Range3.End, Direction = direction
                    });
                }
                cutsceneState.SetPaletteCycles(cycles);

                return AwaitableUtility.Completed;
            };
        }
    }
}