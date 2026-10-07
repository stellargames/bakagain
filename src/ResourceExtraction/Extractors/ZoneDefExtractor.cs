namespace ResourceExtraction.Extractors;

using GameData.Resources.World;
using ResourceExtraction.Imaging;
using System.IO;
using System.Text;

public class ZoneDefExtractor : ExtractorBase<ZoneDefinition>
{
    public override ZoneDefinition Extract(string id, Stream resourceStream)
    {
        using var reader = new BinaryReader(resourceStream, Cp437Encoding.Instance);
        var def = new ZoneDefinition(id)
        {
            ZoneLocation = reader.ReadInt16(),
            ZonePointer = reader.ReadInt16(),
            // A height seen side-on, so it joins the square world (WorldUp). The map heights below
            // stay as they are: those cameras look straight down, so their Z is a viewing distance
            // that sets how much GROUND the map shows, and ground is not scaled.
            DefaultCameraZ = (uint)WorldUp.FromOriginal((int)reader.ReadUInt32()),
            // Turns with the world's heights, like the eye height above (TASK-764).
            DefaultCameraPitch = unchecked((ushort)WorldUp.PitchFromOriginal(reader.ReadUInt16())),
            Flags = (ZoneFlags)reader.ReadUInt16(),
            SkyColor = reader.ReadByte(),
            GroundColor = reader.ReadByte(),
            MapMinZ = reader.ReadUInt32(),
            CameraZPosition = reader.ReadUInt32(),
            MapMaxZ = reader.ReadUInt32(),
            MapZoomStep = reader.ReadUInt32(),
            RmpResourceCount = reader.ReadInt16(),
            SpriteFogDivisor = reader.ReadInt16(),
            SpriteFogNearDistance = reader.ReadUInt32(),
            Unused26 = reader.ReadUInt32(),
            PolygonFogDivisor = reader.ReadInt16(),
            PolygonFogNearDistance = reader.ReadUInt32(),
            FarClipDistance = reader.ReadUInt32()
        };
        // The zone's own focal length in canonical (square) units: 1 << shift VGA pixels, 5 canonical
        // units across each (TASK-764). See StartData.FocalLength.
        def.FocalLength = AspectCorrection.ScaleVgaX(1 << def.ViewZoomShift);
        return def;
    }
}
