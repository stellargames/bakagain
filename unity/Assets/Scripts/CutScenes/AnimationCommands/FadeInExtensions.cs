namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using GameData.Resources.Animation;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public static class FadeInExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(FadeInExtensions));

        /// Fade in the current buffer.
        public static Func<CutsceneState, Awaitable> ToAction(this FadeIn args) {
            return async cutsceneState => {
                Logger.LogDebug("Running frame command: {Command}", args);

                if (!Application.isPlaying)
                    return;

                // Same table as FadeOut, and now the same source — see FadeRamp. Two copies of a
                // seven-way switch is exactly the drift the cutscene test task exists to remove.
                float totalFadeDurationSeconds =
                    CutsceneTiming.FadeDurationSeconds(FadeRamp.PaletteWrites(args.Speed));

                // Create a temporary palette for lerping
                Color[] currentPalette = cutsceneState.CurrentPalette;
                if (currentPalette == null || currentPalette.Length == 0) {
                    Logger.LogError("Cannot fade with null or empty palette");

                    return;
                }

                var tempPalette = new Color[currentPalette.Length];
                Array.Copy(currentPalette, tempPalette, currentPalette.Length); // Initial copy

                // The range rules live in GameData.PaletteFadeRange — they were byte-identical
                // here and in the other fade handler, which is the duplication this test task
                // exists to remove.
                GameData.Resources.Animation.PaletteFadeRange.Range fadeRange =
                    GameData.Resources.Animation.PaletteFadeRange.Resolve(
                        args.Start, args.Length, currentPalette.Length);
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
                if (args.Color >= currentPalette.Length) {
                    Logger.LogError("Target color index {Index} out of range for current palette (size {Size})", args.Color, currentPalette.Length);

                    return;
                }
                Color startColor = currentPalette[args.Color];

                // Pre-calculate color steps for each palette entry to avoid per-frame calculations
                var colorDeltas = new Vector4[length];
                var startVec = new Vector4(startColor.r, startColor.g, startColor.b, startColor.a);
                for (int i = 0; i < length; i++) {
                    int paletteIndex = startIndex + i;
                    Color currentColor = currentPalette[paletteIndex];
                    var currentVec = new Vector4(currentColor.r, currentColor.g, currentColor.b, currentColor.a);
                    colorDeltas[i] = currentVec - startVec;
                }

                double fadeStartTime = Time.realtimeSinceStartupAsDouble;
                float elapsedTime = 0f;

                // Perform the fade by lerping the palette over time
                while (elapsedTime < totalFadeDurationSeconds) {
                    elapsedTime = (float)(Time.realtimeSinceStartupAsDouble - fadeStartTime);
                    float progress = (totalFadeDurationSeconds > 0) ? Mathf.Clamp01(elapsedTime / totalFadeDurationSeconds) : 1.0f;

                    for (int i = 0; i < length; i++) {
                        int paletteIndex = startIndex + i;
                        Vector4 lerpedVec = startVec + colorDeltas[i] * progress;
                        var lerpedColor = new Color(lerpedVec.x, lerpedVec.y, lerpedVec.z, lerpedVec.w);
                        tempPalette[paletteIndex] = lerpedColor;
                    }

                    // Update the palette and render
                    cutsceneState.SetPalette(tempPalette);
                    cutsceneState.RenderIndexed();
                    cutsceneState.RenderOutput();

                    // Don't wait for the next frame if we're in the cutscene editor, only when playing
                    if (Application.isPlaying) {
                        await Awaitable.NextFrameAsync();
                    }
                }

                // Restore the original palette
                cutsceneState.SetPalette(currentPalette);
            };
        }
    }
}