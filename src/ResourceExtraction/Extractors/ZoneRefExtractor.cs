namespace ResourceExtraction.Extractors;

using GameData.Resources.World;
using System.IO;
using System.Text;

public class ZoneRefExtractor : ExtractorBase<ZoneRef>
{
    public override ZoneRef Extract(string id, Stream resourceStream)
    {
        using var reader = new BinaryReader(resourceStream, Cp437Encoding.Instance);
        byte numTiles = reader.ReadByte();
        var zoneRef = new ZoneRef(id);
        for (int i = 0; i < numTiles; i++)
        {
            zoneRef.Tiles.Add(new TileCoordinate
            {
                X = reader.ReadByte(),
                Y = reader.ReadByte()
            });
        }
        return zoneRef;
    }
}
