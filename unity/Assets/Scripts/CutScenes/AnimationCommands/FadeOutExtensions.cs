namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using GameData.Resources.Animation;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public static class FadeOutExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(FadeOutExtensions));

        /// Fade the image to a specified color.
        public static Func<CutsceneState, Awaitable> ToAction(this FadeOut args) {
            return async cutsceneState => {
                Logger.LogDebug("Running frame command: {Args}", args);

                if (!Application.isPlaying)
                    return;

                // The speed argument indexes the original's step table, and the fade is that many
                // palette writes long — see FadeRamp, which also explains why a bigger speed is a
                // SLOWER fade and why speed 0 is a cut rather than a fast ramp. This used to be an
                // inlined switch, duplicated verbatim in FadeIn.
                float totalFadeDurationSeconds =
                    CutsceneTiming.FadeDurationSeconds(FadeRamp.PaletteWrites(args.Speed));

                // Create a temporary palette for lerping
                Color[] originalPalette = cutsceneState.CurrentPalette;
                if (originalPalette == null || originalPalette.Length == 0) {
                    Logger.LogError("Cannot fade with null or empty palette");

                    return;
                }

                var tempPalette = new Color[originalPalette.Length];
                Array.Copy(originalPalette, tempPalette, originalPalette.Length); // Initial copy

                // The range rules live in GameData.PaletteFadeRange — they were byte-identical
                // here and in the other fade handler, which is the duplication this test task
                // exists to remove.
                GameData.Resources.Animation.PaletteFadeRange.Range fadeRange =
                    GameData.Resources.Animation.PaletteFadeRange.Resolve(
                        args.Start, args.Length, originalPalette.Length);
                int startIndex = fadeRange.Start;
                int length = fadeRange.Length;
                if (fadeRange.Clamped) {
                    Logger.LogWarning("Fade length exceeds palette size, clamping to {Length}", length);
                }
                if (!fadeRange.Valid) {
                    Logger.LogError("Invalid fade start index: {StartIndex}", args.Start);

                    return;
                }

                // Get the target color
                if (args.Color >= originalPalette.Length) {
                    Logger.LogError("Target color index {Color} out of range for current palette (size {Size})", args.Color, originalPalette.Length);

                    return;
                }
                Color targetColor = originalPalette[args.Color];
                var targetVec = new Vector4(targetColor.r, targetColor.g, targetColor.b, targetColor.a);

                double fadeStartTime = Time.realtimeSinceStartupAsDouble;
                float elapsedTime = 0f;

                // Perform the fade by lerping the palette over time
                while (elapsedTime < totalFadeDurationSeconds) {
                    elapsedTime = (float)(Time.realtimeSinceStartupAsDouble - fadeStartTime);
                    float progress = (totalFadeDurationSeconds > 0) ? Mathf.Clamp01(elapsedTime / totalFadeDurationSeconds) : 1.0f;

                    for (int i = 0; i < length; i++) {
                        int paletteIndex = startIndex + i;
                        Color originalColor = originalPalette[paletteIndex];
                        tempPalette[paletteIndex] = Color.Lerp(originalColor, targetColor, progress);
                    }

                    // Update the palette and render
                    cutsceneState.SetPalette(tempPalette);
                    // *** BUFFER 0, NOT THE CURRENT BUFFER — AND THE ORIGINAL COMMENT WAS RIGHT. ***
                    // This line used to carry "Not using RenderIndexed() here because for some
                    // reason that does not work. Which is weird." It does not work, and the reason
                    // is not weird: buffer 0 is the frame's STARTING SNAPSHOT, taken by
                    // PrepareBuffers, while CurrentIndexedBuffer is what the frame has drawn so far.
                    // A fade has to ramp the image as it stood when the frame began — INTRO frame 21
                    // blacks the "presents" text out with FillArea BEFORE reaching its FadeOut, so
                    // fading the current buffer fades something already erased and the text just
                    // vanishes. Frame 19 does the same to the logo.
                    Graphics.Blit(cutsceneState.GetIndexedBuffer(0), cutsceneState.OutputBuffer,
                        cutsceneState.IndexedMaterial);
                    cutsceneState.RenderOutput();

                    // Don't wait for the next frame if we're in the cutscene editor, only when playing
                    if (Application.isPlaying) {
                        await Awaitable.NextFrameAsync();
                    }
                }

                // Restore the original palette. The screen keeps the last faded frame; the STATE
                // returns to normal for the next command. Verified against revision 013e39b, where
                // fading worked and this restore was present.
                cutsceneState.SetPalette(originalPalette);
            };
        }
    }
}