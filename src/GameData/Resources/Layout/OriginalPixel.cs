namespace GameData.Resources.Layout;

/// <summary>
/// One pixel of the original's 320x200 screen, in canonical (square) units: 5 across, 6 down.
/// </summary>
/// <remarks>
/// <b>For the strokes the original draws one pixel wide</b> — outlines, rules, text shadows, frame
/// edges — which a faithful port draws as one of these, a rectangle rather than a square. Positions
/// and sizes are not converted with this at runtime: the extractor and the GameData models hand them
/// over canonical already (TASK-765). The book's EGA pixel is a different shape and is not this.
/// </remarks>
public static class OriginalPixel {
    public const int Width = 5;
    public const int Height = 6;
}
