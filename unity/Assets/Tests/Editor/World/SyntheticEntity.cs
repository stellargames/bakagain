namespace BakAgain.Tests.Editor.World {
    using GameData.Resources.World;
    using System.Collections.Generic;

    internal static class SyntheticEntity {
        /// <summary>A depth-sorted (EntityFlags 0x40) entity with one quad in one mesh/pool.</summary>
        public static ZoneTableEntry DepthSortedQuad(string name) {
            var dat = new TableDatInfo {
                EntityFlags = 0x40, EntityType = (WorldEntityType)2, DrawPriority = 0, VertexScale = 0,
                Lods = new List<LodLevel> { new() {
                    VertexPools = new List<List<Position3DInt>> { new() {
                        new(){X=0,Y=0,Z=0}, new(){X=100,Y=0,Z=0}, new(){X=100,Y=100,Z=0}, new(){X=0,Y=100,Z=0} } },
                    Meshes = new List<MeshRecord> { new() { VertexPoolIndex = 0, VertexCount = 4,
                        MeshFaces = new List<MeshFaceRecord> { new PolygonMeshFace { RenderType = 0,
                            Faces = new List<PolygonFace> { new(){ VgaColor=5, Flags=1, VertexIndices=new List<int>{0,1,2,3} } } } } } } } } };
            return new ZoneTableEntry { Index = 7, Name = name, Dat = dat };
        }

        /// <summary>A per-pen (EntityFlags 0x00) entity with one quad in one mesh/pool.
        /// Non-zero DrawPriority (7) lets metadata tests discriminate real mapping from C#
        /// default-zero values. VertexScale (3) is set purely as on-disk provenance — consumers
        /// must not apply it, since the extractor bakes it into the pool coordinates.</summary>
        public static ZoneTableEntry PerPenPolygon(string name) {
            var dat = new TableDatInfo {
                EntityFlags = 0x00, EntityType = (WorldEntityType)2, DrawPriority = 7, VertexScale = 3,
                Lods = new List<LodLevel> { new() {
                    VertexPools = new List<List<Position3DInt>> { new() {
                        new(){X=0,Y=0,Z=0}, new(){X=100,Y=0,Z=0}, new(){X=100,Y=100,Z=0}, new(){X=0,Y=100,Z=0} } },
                    Meshes = new List<MeshRecord> { new() { VertexPoolIndex = 0, VertexCount = 4,
                        MeshFaces = new List<MeshFaceRecord> { new PolygonMeshFace { RenderType = 0,
                            Faces = new List<PolygonFace> { new(){ VgaColor=5, Flags=1, VertexIndices=new List<int>{0,1,2,3} } } } } } } } } };
            return new ZoneTableEntry { Index = 7, Name = name, Dat = dat };
        }
    }
}
