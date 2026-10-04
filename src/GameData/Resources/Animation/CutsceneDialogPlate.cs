namespace GameData.Resources.Animation;

/// <summary>
/// The window a cutscene's dialog-clear draws over DIALOG.SCX, in canonical units (TASK-765).
/// </summary>
/// <remarks>
/// A flat border slightly larger than the window, then the window filled from the background
/// buffer: the original's VGA rects (14, 10, 291, 103) and (15, 11, 289, 102), at 5 x 6.
/// </remarks>
public static class CutsceneDialogPlate {
    /// <summary>The border rect (x, y, width, height).</summary>
    public static (int X, int Y, int Width, int Height) Border => (14 * 5, 10 * 6, 291 * 5, 103 * 6);

    /// <summary>The window rect inside it.</summary>
    public static (int X, int Y, int Width, int Height) Window => (15 * 5, 11 * 6, 289 * 5, 102 * 6);
}
