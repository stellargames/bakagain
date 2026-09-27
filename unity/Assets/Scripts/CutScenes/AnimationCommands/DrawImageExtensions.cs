namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement.Models;
    using BakAgain.Utility;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public static class DrawImageExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(DrawImageExtensions));

        /// Draw an image to the screen.
        public static Func<CutsceneState, Awaitable> ToAction(this DrawImage args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Command}", args);

                IndexedTexture image = cutsceneState.GetImage(args.ImageSlot, args.ImageNumber);
                if (image == null) {
                    Logger.LogWarning("Image not found in slot {Slot} with number {Number}", args.ImageSlot, args.ImageNumber);

                    return AwaitableUtility.Completed;
                }
                Drawing.DrawImage(image, args.X, args.Y, Orientation.Normal, Vector2.one, cutsceneState);

                return AwaitableUtility.Completed;
            };
        }

        /// Draw an image to the screen flipped horizontally.
        public static Func<CutsceneState, Awaitable> ToAction(this DrawImageFlippedHorizontally args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Command}", args);

                IndexedTexture image = cutsceneState.GetImage(args.ImageSlot, args.ImageNumber);
                if (image == null) {
                    Logger.LogWarning("Image not found in slot {Slot} with number {Number}", args.ImageSlot, args.ImageNumber);

                    return AwaitableUtility.Completed;
                }
                Drawing.DrawImage(image, args.X, args.Y, Orientation.FlippedHorizontally, Vector3.one, cutsceneState);

                return AwaitableUtility.Completed;
            };
        }

        /// Draw an image to the screen flipped vertically.
        public static Func<CutsceneState, Awaitable> ToAction(this DrawImageFlippedVertically args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Command}", args);

                IndexedTexture image = cutsceneState.GetImage(args.ImageSlot, args.ImageNumber);
                if (image == null) {
                    Logger.LogWarning("Image not found in slot {Slot} with number {Number}", args.ImageSlot, args.ImageNumber);

                    return AwaitableUtility.Completed;
                }
                Drawing.DrawImage(image, args.X, args.Y, Orientation.FlippedVertically, Vector2.one, cutsceneState);

                return AwaitableUtility.Completed;
            };
        }

        /// Draw an image to the screen rotated 180 degrees.
        public static Func<CutsceneState, Awaitable> ToAction(this DrawImageRotated180 args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Command}", args);

                IndexedTexture image = cutsceneState.GetImage(args.ImageSlot, args.ImageNumber);
                if (image == null) {
                    Logger.LogWarning("Image not found in slot {Slot} with number {Number}", args.ImageSlot, args.ImageNumber);

                    return AwaitableUtility.Completed;
                }
                Drawing.DrawImage(image, args.X, args.Y, Orientation.Rotated180, Vector2.one, cutsceneState);

                return AwaitableUtility.Completed;
            };
        }

        /// Draw a scaled image to the screen with a specified size.
        public static Func<CutsceneState, Awaitable> ToAction(this DrawImageScaled args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Command}", args);

                IndexedTexture image = cutsceneState.GetImage(args.ImageSlot, args.ImageNumber);
                if (image == null) {
                    Logger.LogWarning("Image not found in slot {Slot} with number {Number}", args.ImageSlot, args.ImageNumber);

                    return AwaitableUtility.Completed;
                }
                // args.Width/Height are canonical px; image.Width/Height are raw bitmap px
                // (any resolution for overrides) — divide canonical by canonical.
                var scale = new Vector2(args.Width / image.CanonicalWidth, args.Height / image.CanonicalHeight);
                Drawing.DrawImage(image, args.X, args.Y, Orientation.Normal, scale, cutsceneState);

                return AwaitableUtility.Completed;
            };
        }

        /// Draw a scaled image to the screen flipped horizontally.
        public static Func<CutsceneState, Awaitable> ToAction(this DrawImageFlippedHorizontallyScaled args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Command}", args);

                IndexedTexture image = cutsceneState.GetImage(args.ImageSlot, args.ImageNumber);
                if (image == null) {
                    Logger.LogWarning("Image not found in slot {Slot} with number {Number}", args.ImageSlot, args.ImageNumber);

                    return AwaitableUtility.Completed;
                }
                // args.Width/Height are canonical px; image.Width/Height are raw bitmap px
                // (any resolution for overrides) — divide canonical by canonical.
                var scale = new Vector2(args.Width / image.CanonicalWidth, args.Height / image.CanonicalHeight);
                Drawing.DrawImage(image, args.X, args.Y, Orientation.FlippedHorizontally, scale, cutsceneState);

                return AwaitableUtility.Completed;
            };
        }

        /// Draw a scaled image to the screen flipped vertically.
        public static Func<CutsceneState, Awaitable> ToAction(this DrawImageFlippedVerticallyScaled args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Command}", args);

                IndexedTexture image = cutsceneState.GetImage(args.ImageSlot, args.ImageNumber);
                if (image == null) {
                    Logger.LogWarning("Image not found in slot {Slot} with number {Number}", args.ImageSlot, args.ImageNumber);

                    return AwaitableUtility.Completed;
                }
                // args.Width/Height are canonical px; image.Width/Height are raw bitmap px
                // (any resolution for overrides) — divide canonical by canonical.
                var scale = new Vector2(args.Width / image.CanonicalWidth, args.Height / image.CanonicalHeight);
                Drawing.DrawImage(image, args.X, args.Y, Orientation.FlippedVertically, scale, cutsceneState);

                return AwaitableUtility.Completed;
            };
        }

        /// Draw a scaled image to the screen rotated 180 degrees.
        public static Func<CutsceneState, Awaitable> ToAction(this DrawImageRotated180Scaled args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Command}", args);

                IndexedTexture image = cutsceneState.GetImage(args.ImageSlot, args.ImageNumber);
                if (image == null) {
                    Logger.LogWarning("Image not found in slot {Slot} with number {Number}", args.ImageSlot, args.ImageNumber);

                    return AwaitableUtility.Completed;
                }
                // args.Width/Height are canonical px; image.Width/Height are raw bitmap px
                // (any resolution for overrides) — divide canonical by canonical.
                var scale = new Vector2(args.Width / image.CanonicalWidth, args.Height / image.CanonicalHeight);
                Drawing.DrawImage(image, args.X, args.Y, Orientation.Rotated180, scale, cutsceneState);

                return AwaitableUtility.Completed;
            };
        }

        /// Draw a scaled image to the screen rotated freely.
        public static Func<CutsceneState, Awaitable> ToAction(this DrawImageRotated args) {
            return cutsceneState => {
                Logger.LogDebug("Running frame command: {Command}", args);

                IndexedTexture image = cutsceneState.GetImage(args.ImageSlot, args.ImageNumber);
                if (image == null) {
                    Logger.LogWarning("Image not found in slot {Slot} with number {Number}", args.ImageSlot, args.ImageNumber);

                    return AwaitableUtility.Completed;
                }
                // args.Width/Height are canonical px; image.Width/Height are raw bitmap px
                // (any resolution for overrides) — divide canonical by canonical.
                var scale = new Vector2(args.Width / image.CanonicalWidth, args.Height / image.CanonicalHeight);
                float rotation = args.AngleDegrees;
                Texture2D rotatedTexture = RotateTexture(image.GetRenderTexture(), rotation);
                IndexedTexture rotatedImage = image.WithTexture(rotatedTexture);
                // Position relative to original image center, in canonical px
                // (WithTexture adjusted Scale, so CanonicalWidth/Height track
                // the rotated bounding box).
                (int x, int y) = GameData.Resources.Animation.RotatedDraw.TopLeftFor(
                    args.X, args.Y, rotatedImage.CanonicalWidth, rotatedImage.CanonicalHeight,
                    scale.x, scale.y);

                Drawing.DrawImage(rotatedImage, x, y, Orientation.Normal, scale, cutsceneState);

                return AwaitableUtility.Completed;
            };
        }

        private static Texture2D RotateTexture(Texture2D originalTexture, float angle) {
            int originalWidth = originalTexture.width;
            int originalHeight = originalTexture.height;
            float angleRad = angle * Mathf.Deg2Rad;

            // Calculate cosine and sine of the angle
            float cos = Mathf.Cos(angleRad);
            float sin = Mathf.Sin(angleRad);

            // Box maths in GameData.RotatedDraw — pure, and tested against the real angles the
            // shipped scripts use. cos/sin above are still needed for the per-pixel inverse map.
            (int newWidth, int newHeight) =
                GameData.Resources.Animation.RotatedDraw.Bounds(originalWidth, originalHeight, angle);

            var rotatedTexture = new Texture2D(newWidth, newHeight, originalTexture.format, false, true) {filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp};

            var originalCenter = new Vector2(originalWidth / 2f, originalHeight / 2f);
            var newCenter = new Vector2(newWidth / 2f, newHeight / 2f);

            // Bulk pixel access (GetPixels32/SetPixels32 once) instead of per-pixel
            // GetPixel/SetPixel — same nearest-neighbour result, far less per-call overhead
            // on this per-rotated-draw path.
            Color32[] source = originalTexture.GetPixels32();
            var rotated = new Color32[newWidth * newHeight];
            var transparent = new Color32(0, 0, 0, 0);

            for (int y = 0; y < newHeight; y++) {
                for (int x = 0; x < newWidth; x++) {
                    // Calculate the coordinates in the original texture
                    Vector2 pos = new Vector2(x, y) - newCenter;

                    int srcX = Mathf.RoundToInt(cos * pos.x + sin * pos.y + originalCenter.x);
                    int srcY = Mathf.RoundToInt(-sin * pos.x + cos * pos.y + originalCenter.y);

                    rotated[y * newWidth + x] =
                        (srcX >= 0 && srcX < originalWidth && srcY >= 0 && srcY < originalHeight)
                            ? source[srcY * originalWidth + srcX]
                            : transparent;
                }
            }

            rotatedTexture.SetPixels32(rotated);
            rotatedTexture.Apply();

            return rotatedTexture;
        }
    }
}