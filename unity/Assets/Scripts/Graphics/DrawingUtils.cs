namespace BakAgain.Graphics
{
    using GameData.Resources.Animation;
    using UnityEngine;

    public static class DrawingUtils
    {
        /// <summary>The four edge rectangles of a border, in top-left-origin pixel space.</summary>
        public readonly struct BorderRects
        {
            public BorderRects(Rect top, Rect bottom, Rect left, Rect right)
            {
                Top = top;
                Bottom = bottom;
                Left = left;
                Right = right;
            }

            public Rect Top { get; }
            public Rect Bottom { get; }
            public Rect Left { get; }
            public Rect Right { get; }
        }

        /// <summary>
        /// Computes the four edge rectangles of the original's 1-VGA-px border, scaled to the
        /// current buffer resolution. The area is given in canonical 1600×1200 coordinates;
        /// <paramref name="scale"/> is <c>CutsceneState.ScaleFromOriginal</c> (buffer px per
        /// canonical px). A 1 VGA px line is 5 canonical px wide / 6 tall, so thickness is
        /// 5×/6× the scale — preserving the original's line weight — and never drops below 1px.
        /// Rectangles are in top-left-origin space to match <c>GL.LoadPixelMatrix(0, w, h, 0)</c>.
        /// </summary>
        public static BorderRects GetBorderRects(IArea area, Vector2 scale)
        {
            // scale arrives as float32 (e.g. 1.2f ≈ 1.20000005), so a coordinate
            // landing exactly on a pixel boundary (1525 × 1.2 = 1830.0) would be
            // nudged across it by floor/ceil. Snap in double, rounded to 1/1000 px,
            // to absorb that representation noise before extending the outer rect.
            int x1 = FloorScaled(area.X, scale.x);
            int y1 = FloorScaled(area.Y, scale.y);
            int x2 = CeilScaled(area.X + area.Width, scale.x);
            int y2 = CeilScaled(area.Y + area.Height, scale.y);

            int width = x2 - x1;
            int height = y2 - y1;
            int thicknessX = Mathf.Max(1, Mathf.RoundToInt(Canonical.VgaScaleX * scale.x));
            int thicknessY = Mathf.Max(1, Mathf.RoundToInt(Canonical.VgaScaleY * scale.y));

            return new BorderRects(
                top: new Rect(x1, y1, width, thicknessY),
                bottom: new Rect(x1, y2 - thicknessY, width, thicknessY),
                left: new Rect(x1, y1, thicknessX, height),
                right: new Rect(x2 - thicknessX, y1, thicknessX, height));
        }

        // Scale in double precision and round away float32 noise before snapping,
        // so coordinates exactly on a pixel boundary don't drift across it.
        private static int FloorScaled(int value, float scale) =>
            (int)System.Math.Floor(System.Math.Round(value * (double)scale, 3));

        private static int CeilScaled(int value, float scale) =>
            (int)System.Math.Ceiling(System.Math.Round(value * (double)scale, 3));

        /// <summary>
        /// Calculates source and destination rectangles for drawing an image, handling clipping and orientation.
        /// </summary>
        /// <param name="x">X position in original game coordinates</param>
        /// <param name="y">Y position in original game coordinates</param>
        /// <param name="width">Width in original game coordinates</param>
        /// <param name="height">Height in original game coordinates</param>
        /// <param name="clipArea">Clipping area in (xMin, yMin, xMax, yMax) format, bounds are inclusive</param>
        /// <param name="orientation">Image orientation (flipped horizontally/vertically)</param>
        /// <returns>Tuple of (sourceRect, destinationRect) where sourceRect is in UV coordinates (0-1) and destinationRect is in pixel coordinates</returns>
        public static (Rect sourceRect, Rect destinationRect) CalculateRects(float x, float y, float width, float height, Rect clipArea, Orientation orientation)
        {
            // clipArea is in (xMin,yMin,xMax,yMax) format with inclusive bounds
            float destX = Mathf.Max(x, clipArea.x);
            float destY = Mathf.Max(y, clipArea.y);
            float xOffset = destX - x;
            float yOffset = destY - y;
            
            // Add 1 to xMax/yMax since bounds are inclusive
            float destWidth = Mathf.Min(width - xOffset, (clipArea.width + 1) - destX);
            float destHeight = Mathf.Min(height - yOffset, (clipArea.height + 1) - destY);
            var destinationRect = new Rect(destX, destY, destWidth, destHeight);

            float sourceX, sourceWidth;
            if (orientation.HasFlag(Orientation.FlippedHorizontally))
            {
                sourceX = 1f - xOffset / width;
                sourceWidth = -destWidth / width;
            }
            else
            {
                sourceX = xOffset / width;
                sourceWidth = destWidth / width;
            }

            float sourceY, sourceHeight;
            if (orientation.HasFlag(Orientation.FlippedVertically))
            {
                sourceY = (yOffset + destHeight) / height;
                sourceHeight = -destHeight / height;
            }
            else
            {
                sourceY = 1f - (yOffset + destHeight) / height;
                sourceHeight = destHeight / height;
            }

            var sourceRect = new Rect(sourceX, sourceY, sourceWidth, sourceHeight);

            return (sourceRect, destinationRect);
        }
    }
}
