namespace ResourceExtraction.Extractors;

using GameData.Resources.Config;
using GameData.Resources.World;
using ResourceExtraction.Imaging;
using System.IO;

/// <summary>
/// Parses START.DAT — ten little-endian int16 with no header, count or terminator, so the read
/// ORDER below is the whole format. It mirrors <c>LoadSTART.DAT</c> (ovr129 @0x41620) call for call.
/// See <see cref="StartData"/> for what each value means and who consumes it.
///
/// <para>Signed, not unsigned: two of the ten are negative (the combat camera's pitches), and
/// reading them as u16 turns -2112 into 63424 — a value that would look like a plausible angle and
/// tilt the camera the wrong way.</para>
/// </summary>
public class StartDataExtractor : ExtractorBase<StartData> {
    public override StartData Extract(string id, Stream resourceStream) {
        using var reader = new BinaryReader(resourceStream);

        var start = new StartData(id) {
            // Heights, so they join the square world (WorldUp, TASK-764).
            CombatCameraHeightAboveGround = (short)WorldUp.FromOriginal(reader.ReadInt16()),
            CombatCameraHeightUnderground = (short)WorldUp.FromOriginal(reader.ReadInt16()),
            // Pitches turn with the world's heights (WorldUp.PitchFromOriginal, TASK-764).
            CombatCameraPitchAboveGround = WorldUp.PitchFromOriginal(reader.ReadInt16()),
            CombatCameraPitchUnderground = WorldUp.PitchFromOriginal(reader.ReadInt16()),
            CombatGridCellSize = reader.ReadInt16(),
            // Screen coordinates, so they cross into canonical space here and the original's
            // 320x200 stops at this boundary.
            ViewportX = AspectCorrection.ScaleVgaX(reader.ReadInt16()),
            ViewportY = AspectCorrection.ScaleVgaY(reader.ReadInt16()),
            ViewportWidth = AspectCorrection.ScaleVgaX(reader.ReadInt16()),
            ViewportHeight = AspectCorrection.ScaleVgaY(reader.ReadInt16()),
            ProjectionShift = reader.ReadInt16(),
        };
        // The projection's focal length: 1 << shift VGA pixels, which are 5 canonical units ACROSS.
        // One square-pixel number, so the camera needs neither the shift nor the 6:5 (TASK-764).
        start.FocalLength = AspectCorrection.ScaleVgaX(1 << start.ProjectionShift);
        return start;
    }
}
