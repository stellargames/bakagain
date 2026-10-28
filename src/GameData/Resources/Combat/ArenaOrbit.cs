namespace GameData.Resources.Combat;

using System;
using GameData.Resources.Layout;

/// <summary>
/// The Enhanced combat camera's pose: an eye orbiting the board's centre (TASK-972 phase 1).
/// Engine-free and in the party-relative frame of <see cref="CombatArenaPlacement"/> (X across,
/// Y away, before the party's heading), in world units; the Unity driver rotates and places it.
/// Yaw 0 looks along +Y; yaw grows toward +X.
/// </summary>
public sealed class ArenaOrbit {
    private readonly EnhancedCombatLayout _layout;
    private readonly (double X, double Y) _centre;
    private (double X, double Y) _focus;
    private readonly double _startYaw, _startPitch, _startZoom;
    private double _startDistance;
    private double _zoom;

    /// <param name="distance">The whole-board fit: zoom 1, and the farthest the camera may go.</param>
    /// <param name="startZoom">Where the fight opens and Reset returns to, as a factor of <paramref name="distance"/>.</param>
    public ArenaOrbit(EnhancedCombatLayout layout, (double X, double Y) boardCentre,
        double startYawDegrees, double startPitchDegrees, double distance, double startZoom = 1) {
        _layout = layout;
        _centre = _focus = boardCentre;
        _startYaw = YawDegrees = startYawDegrees;
        // Clamped here, not only on Drag: an out-of-range start would otherwise jump on the first drag.
        _startPitch = PitchDegrees = Math.Clamp(startPitchDegrees, layout.PitchMinDegrees, layout.PitchMaxDegrees);
        _startDistance = distance;
        _zoom = _startZoom = Math.Clamp(startZoom, layout.ZoomMin, layout.ZoomMax);
    }

    public double YawDegrees { get; private set; }
    public double PitchDegrees { get; private set; }
    public double Distance => _startDistance * _zoom;
    /// <summary>
    /// What the camera turns around: the board's centre at the farthest zoom (the whole board in view),
    /// leaning toward <see cref="Focus"/> as it comes closer, onto it at the closest -- so a close view
    /// shows the acting hero rather than the board's empty middle.
    /// </summary>
    public (double X, double Y) Centre {
        get {
            double t = _layout.ZoomMax > _layout.ZoomMin
                ? Math.Clamp((_layout.ZoomMax - _zoom) / (_layout.ZoomMax - _layout.ZoomMin), 0, 1) : 0;
            return t == 0 ? _centre : (_centre.X + ((_focus.X - _centre.X) * t), _centre.Y + ((_focus.Y - _centre.Y) * t));
        }
    }

    /// <summary>The middle of the playable board: what the camera turns around at the farthest zoom.</summary>
    public (double X, double Y) BoardMiddle => _centre;

    /// <summary>
    /// The whole-board distance for the pose as it is now: zoom 1, and the farthest the camera may go. It moves
    /// with the yaw, the pitch and the window, so the driver refits it after each of them; the zoom factor stays.
    /// </summary>
    public void Refit(double wholeBoardDistance) {
        if (wholeBoardDistance > 0) {
            _startDistance = wholeBoardDistance;
        }
    }

    /// <summary>The point a close view centres on (the acting hero's cell), in the board's frame.</summary>
    public void Focus((double X, double Y) point) => _focus = point;

    /// <summary>The middle of the PLAYABLE cells: an underground fight's blocked rows are not the board.</summary>
    public static (double X, double Y) BoardCentre(int cellSize, bool underground) {
        int lastRow = (underground ? CombatGrid.UndergroundPlayableRows : CombatGrid.Height) - 1;
        (int a0, int y0) = CombatArenaPlacement.CellOffset(0, 0, cellSize);
        (int a1, int y1) = CombatArenaPlacement.CellOffset(CombatGrid.Width - 1, lastRow, cellSize);
        return ((a0 + a1) / 2.0, (y0 + y1) / 2.0);
    }

    /// <summary>The outer corners of the PLAYABLE cells, in the same frame as <see cref="BoardCentre"/>.</summary>
    public static (double X, double Y)[] BoardCorners(int cellSize, bool underground) {
        int lastRow = (underground ? CombatGrid.UndergroundPlayableRows : CombatGrid.Height) - 1;
        double half = cellSize / 2.0;
        (int a0, int y0) = CombatArenaPlacement.CellOffset(0, 0, cellSize);
        (int a1, int y1) = CombatArenaPlacement.CellOffset(CombatGrid.Width - 1, lastRow, cellSize);
        return new[] { (a0 - half, y0 - half), (a1 + half, y0 - half), (a0 - half, y1 + half), (a1 + half, y1 + half) };
    }

    /// <summary>The pose that sees the board from an eye at (<paramref name="eyeX"/>, <paramref name="eyeY"/>,
    /// <paramref name="eyeHeight"/>), the board's centre taken at ground level.</summary>
    public static ArenaOrbit From(EnhancedCombatLayout layout, (double X, double Y) boardCentre,
        double eyeX, double eyeY, double eyeHeight) {
        double dx = boardCentre.X - eyeX, dy = boardCentre.Y - eyeY;
        double horizontal = Math.Sqrt((dx * dx) + (dy * dy));
        return new ArenaOrbit(layout, boardCentre,
            Math.Atan2(dx, dy) * 180 / Math.PI,
            Math.Atan2(eyeHeight, horizontal) * 180 / Math.PI,
            Math.Sqrt((horizontal * horizontal) + (eyeHeight * eyeHeight)));
    }

    public void Drag(double dxPixels, double dyPixels) {
        YawDegrees += dxPixels * _layout.OrbitDegreesPerPixel;
        PitchDegrees = Math.Clamp(PitchDegrees + (dyPixels * _layout.OrbitDegreesPerPixel),
            _layout.PitchMinDegrees, _layout.PitchMaxDegrees);
    }

    /// <summary>Multiplies the distance by <paramref name="factor"/>, within the layout's zoom limits.</summary>
    public void Zoom(double factor) => _zoom = Math.Clamp(_zoom * factor, _layout.ZoomMin, _layout.ZoomMax);

    public void Reset() {
        YawDegrees = _startYaw;
        PitchDegrees = _startPitch;
        _zoom = _startZoom;
    }

    public void TurnQuarter(int direction) => YawDegrees += 90 * Math.Sign(direction);

    public (double X, double Y, double Height) EyePosition() {
        double yaw = YawDegrees * Math.PI / 180, pitch = PitchDegrees * Math.PI / 180;
        double horizontal = Distance * Math.Cos(pitch);
        (double cx, double cy) = Centre;
        return (cx - (horizontal * Math.Sin(yaw)), cy - (horizontal * Math.Cos(yaw)),
            Distance * Math.Sin(pitch));
    }
}
