namespace BetrayalAtKrondor.Tests.Data;

using System;
using System.IO;
using System.Linq;
using System.Text;
using global::GameData.Resources.Data;
using global::GameData.Resources.World;
using global::ResourceExtraction.Extractors;
using Xunit;

/// <summary>
/// The chest explosion is a TBL model of its own: shape 0xb6 overworld, 0x8e underground
/// (canassa R3D/ACTOR/ACTOROVL.C, actoroverlay_render_actor), drawn over the chest with its sprite
/// table swapped to BOOM. Its sprite faces size the frames — the first one is the small spark.
/// </summary>
public class ChestExplosionShapeTests {
    static ChestExplosionShapeTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Theory]
    [InlineData("Z02.TBL", false)]
    [InlineData("Z10.TBL", true)]
    public void TheExplosionShapeIsTheBoomModel(string tbl, bool underground) {
        string? dir = FindOriginalGameDir();
        if (dir == null) {
            return; // shipped data absent (CI)
        }
        using FileStream stream = File.OpenRead(Path.Combine(dir, tbl));
        ZoneTable table = new ZoneTableExtractor().Extract(tbl, stream);

        ZoneTableEntry boom = table.Entries[ChestTrap.ExplosionShape(underground)];
        Assert.Equal("boom", boom.Name);
        var faces = boom.Dat.Lods[0].Meshes[0].MeshFaces.OfType<SpriteBMeshFace>().ToArray();
        Assert.Equal(3, faces.Length);
        Assert.True(faces[0].SizeScale < faces[1].SizeScale, "frame 0 is the small spark");
    }

    private static string? FindOriginalGameDir() {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir)) {
            string candidate = Path.Combine(dir, "OriginalGame");
            if (File.Exists(Path.Combine(candidate, "Z01.TBL"))) {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
