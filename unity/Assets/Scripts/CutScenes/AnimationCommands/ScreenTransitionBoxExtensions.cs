namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Utility;
    using BakAgain.Graphics;
    using GameData.Resources.Animation;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The two box screen-transitions — TTM 0xA034 and 0xA094,
    /// <c>anim_screenTransitionEffect</c> @0x53ab5.
    /// </summary>
    /// <remarks>
    /// <b>A wipe needs both the old picture and the new one, and this engine already holds them.</b>
    /// Each frame starts by copying the background buffer into the draw buffer
    /// (<c>PrepareBuffers</c>) and the frame's commands then draw on top, so when a transition runs
    /// the background buffer still holds the image the viewer is looking at and the draw buffer
    /// holds the one about to replace it. That is the same pair the original wipes between, which
    /// is why no snapshot has to be taken here.
    ///
    /// <para>The reveal happens IN the background buffer — the old image with pieces of the new one
    /// copied over it — and that buffer is presented after each step. It therefore ends the wipe
    /// holding the completed new image, which is what the screen is showing and what the next
    /// frame should start from.</para>
    ///
    /// <para>Geometry and the per-step copy rule are <see cref="ScreenTransitionBox"/>'s; this only
    /// performs them and paces the steps.</para>
    /// </remarks>
    public static class ScreenTransitionBoxExtensions {
        private static readonly ILogger Logger =
            LogManager.LoggerFactory.CreateLogger(nameof(ScreenTransitionBoxExtensions));

        public static Func<CutsceneState, Awaitable> ToAction(this ScreenTransitionBoxOut args) =>
            Wipe(args, args.X, args.Y, args.Width, args.Height, boxOut: true);

        public static Func<CutsceneState, Awaitable> ToAction(this ScreenTransitionBoxIn args) =>
            Wipe(args, args.X, args.Y, args.Width, args.Height, boxOut: false);

        private static Func<CutsceneState, Awaitable> Wipe(FrameCommand command,
            int x, int y, int width, int height, bool boxOut) {
            return async cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", command);

                if (!Application.isPlaying) {
                    return;
                }

                int steps = ScreenTransitionBox.StepCount(width, height);
                int newImage = cutsceneState.CurrentDrawBufferIndex;
                int shown = cutsceneState.BackgroundBufferIndex;
                if (steps <= 0 || newImage == shown) {
                    // Nothing to wipe between — the caller drew straight onto the shown buffer.
                    return;
                }

                // *** THE ORIGINAL'S PACE IS ONE STEP PER LOGICAL FRAME. *** The step count comes
                // from the area's size, so a large wipe genuinely takes longer than a small one;
                // pacing by a fixed duration instead would make every transition the same length
                // and lose that.
                foreach (int step in Steps(boxOut, steps)) {
                    foreach ((int rx, int ry, int rw, int rh) in
                             Revealed(boxOut, x, y, width, height, step)) {
                        if (rw <= 0 || rh <= 0) {
                            continue;
                        }
                        cutsceneState.CopyArea(newImage, shown,
                            new Area(rx, ry, rw, rh));
                    }

                    Present(cutsceneState, shown);
                    await Awaitable.WaitForSecondsAsync(CutsceneTiming.FrameDurationSeconds);
                    if (cutsceneState.EndScene) {
                        break;
                    }
                }

                // Whatever the wipe left, the frame ends on the complete new image.
                cutsceneState.CopyBuffer(newImage, shown);
            };
        }

        /// <summary>Box-out counts up from the centre; box-in counts down from the edge.</summary>
        private static IEnumerable<int> Steps(bool boxOut, int steps) {
            if (boxOut) {
                for (int step = ScreenTransitionBox.BoxOutFirstStep; step <= steps; step++) {
                    yield return step;
                }
            } else {
                for (int step = steps; step >= 1; step--) {
                    yield return step;
                }
            }
        }

        private static IEnumerable<(int X, int Y, int Width, int Height)> Revealed(
            bool boxOut, int x, int y, int width, int height, int step) {
            if (boxOut) {
                yield return ScreenTransitionBox.RevealedByBoxOut(x, y, width, height, step);

                yield break;
            }

            foreach ((int X, int Y, int Width, int Height) strip in
                     ScreenTransitionBox.RevealedByBoxIn(x, y, width, height, step)) {
                yield return strip;
            }
        }

        /// <summary>
        /// Shows a buffer without disturbing which one the frame is drawing into.
        /// </summary>
        /// <remarks>
        /// The same idiom the fades use: blit through the indexed material into the output buffer
        /// and copy that to the canvas. Going via <c>RenderIndexed</c> would present the DRAW
        /// buffer, which during a wipe is the finished new image — the transition would be over
        /// before its first step.
        /// </remarks>
        private static void Present(CutsceneState cutsceneState, int buffer) {
            if (!cutsceneState.IndexedMaterial) {
                return;
            }
            Graphics.Blit(cutsceneState.GetIndexedBuffer(buffer), cutsceneState.OutputBuffer,
                cutsceneState.IndexedMaterial);
            cutsceneState.RenderOutput();
        }
    }
}
