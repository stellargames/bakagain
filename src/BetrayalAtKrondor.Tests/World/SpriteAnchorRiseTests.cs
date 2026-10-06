namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// A sprite face's anchor is a projected model VERTEX, not the entity's origin — renderSprite2
/// (0x23031): <c>yPos = proj(VertexIndex).y - AnchorY * si</c>. COMBAT.TBL's whirlwind (entry 4,
/// <c>spell3</c>) has AnchorY 0 and its vertex at Z 462, so the image hangs from 462 units up and
/// stands on the floor; ignoring the vertex buried it (TASK-117).
/// </summary>
public class SpriteAnchorRiseTests {
    private static TableDatInfo Dat(int vertexZ, byte vertexIndex, out SpriteBMeshFace face) {
        face = new SpriteBMeshFace { VertexIndex = vertexIndex };
        var dat = new TableDatInfo();
        dat.Lods.Add(new LodLevel {
            VertexPools = new List<List<Position3DInt>> { new() { new Position3DInt { Z = vertexZ } } },
            Meshes = new List<MeshRecord> {
                new() { VertexPoolIndex = 0, MeshFaces = new List<MeshFaceRecord> { face } },
            },
        });
        return dat;
    }

    [Fact]
    public void TheWhirlwindHangsFromItsRaisedVertex() {
        TableDatInfo dat = Dat(462, 0, out SpriteBMeshFace face);
        Assert.Equal(462, dat.SpriteAnchorRise(face));
    }

    [Fact]
    public void AFaceWithNoVertexSitsOnTheOrigin() {
        TableDatInfo dat = Dat(462, 255, out SpriteBMeshFace face);
        Assert.Equal(0, dat.SpriteAnchorRise(face));
    }
}
