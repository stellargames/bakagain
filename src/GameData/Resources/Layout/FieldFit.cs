namespace GameData.Resources.Layout;

/// <summary>
/// Fits a box of the design frame into a field on screen: one uniform scale (the smaller of the two
/// axis ratios, so nothing is cut off) and the offset that centres the scaled box in the field. The
/// Enhanced inventory draws the faithful item area this way (spec 2026-10-13 "The sheet").
/// </summary>
public static class FieldFit
{
    /// <returns>The scale, and where the frame's origin lands, so that frame point p is drawn at
    /// <c>(X + p.x * Scale, Y + p.y * Scale)</c>.</returns>
    public static (float Scale, float X, float Y) Fit(
        float boxX, float boxY, float boxWidth, float boxHeight,
        float fieldX, float fieldY, float fieldWidth, float fieldHeight)
    {
        if (boxWidth <= 0f || boxHeight <= 0f || fieldWidth <= 0f || fieldHeight <= 0f)
        {
            return (1f, fieldX - boxX, fieldY - boxY);
        }
        float scale = System.Math.Min(fieldWidth / boxWidth, fieldHeight / boxHeight);
        return (scale,
            fieldX + (fieldWidth - boxWidth * scale) / 2f - boxX * scale,
            fieldY + (fieldHeight - boxHeight * scale) / 2f - boxY * scale);
    }
}
