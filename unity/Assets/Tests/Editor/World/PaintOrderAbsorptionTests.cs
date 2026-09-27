namespace BakAgain.Tests.Editor.World {
    using BakAgain.World.Converters;
    using GameData.Resources.World;
    using NUnit.Framework;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// The converter never displaces a vertex. Coplanar grouping still assigns paint-order ranks
    /// for the shader's depth-bias tie-break, but geometry is emitted exactly as authored.
    ///
    /// Earlier versions projected near-coplanar faces onto their group plane to make the depth tie
    /// exact. That destroyed authored separations: measured on Z01 fall1 (2026-07-20), the two
    /// topmost river faces are hinged on a shared edge with their free vertex 0.37-0.49 BaK clear
    /// of the rock, and flattening collapsed that to 0.000 — leaving the pair to be separated by
    /// the rank bias alone, which is ~10x smaller and invisible at travel distance.
    /// </summary>
    public class PaintOrderAbsorptionTests {
        private const byte SingleSided = 1;

        /// <summary>Base quad on Z=0 plus a second quad at <paramref name="cornerZ"/> on one corner.</summary>
        private static TableDatInfo TwoQuads(int secondZ, int secondCornerZ) {
            var verts = new List<Position3DInt> {
                // base quad, Z = 0
                new() { X = 0, Y = 0, Z = 0 }, new() { X = 1000, Y = 0, Z = 0 },
                new() { X = 1000, Y = 1000, Z = 0 }, new() { X = 0, Y = 1000, Z = 0 },
                // second quad — three corners at secondZ, one at secondCornerZ
                new() { X = 0, Y = 0, Z = secondZ }, new() { X = 1000, Y = 0, Z = secondZ },
                new() { X = 1000, Y = 1000, Z = secondZ }, new() { X = 0, Y = 1000, Z = secondCornerZ },
            };
            var faces = new List<PolygonFace> {
                new() { VgaColor = 0, Flags = SingleSided, VertexIndices = new List<int> { 0, 1, 2, 3 } },
                new() { VgaColor = 0, Flags = SingleSided, VertexIndices = new List<int> { 4, 5, 6, 7 } },
            };
            return new TableDatInfo {
                EntityFlags = 0x40,
                EntityType = (WorldEntityType)4,
                Lods = new List<LodLevel> {
                    new() {
                        VertexPools = new List<List<Position3DInt>> { verts },
                        Meshes = new List<MeshRecord> {
                            new() {
                                VertexPoolIndex = 0, VertexCount = (byte)verts.Count,
                                MeshFaces = new List<MeshFaceRecord> {
                                    new PolygonMeshFace { FaceCount = (ushort)faces.Count, Faces = faces },
                                },
                            },
                        },
                    },
                },
            };
        }

        private static TblMeshConverter.TerrainMeshData Convert(TableDatInfo dat) =>
            TblMeshConverter.ConvertDepthSortedEntity(dat, new Color[256], "test");

        /// <summary>Unity Y of the second face's vertices (BaK Z maps to Unity Y).</summary>
        private static float SecondFaceMaxY(TblMeshConverter.TerrainMeshData d) {
            float max = float.MinValue;
            // Vertices are duplicated per face in emit order: face 0 first, then face 1.
            for (int i = d.Vertices.Length / 2; i < d.Vertices.Length; i++)
                max = Mathf.Max(max, d.Vertices[i].y);
            return max;
        }

        [Test]
        public void Authored_offset_on_one_side_survives_conversion() {
            // Second quad a clean 2 BaK above the base — inside the grouping tolerance (10 BaK) but
            // entirely on one side, so it is a real separation, not a tie.
            var d = Convert(TwoQuads(secondZ: 2, secondCornerZ: 2));

            Assert.AreEqual(0.02f, SecondFaceMaxY(d), 1e-4f,
                "a face offset clear of the plane must keep its geometry, not be flattened onto it");
        }

        [Test]
        public void Face_piercing_the_plane_also_keeps_its_geometry() {
            // Three corners 2 BaK below, one 2 BaK above → straddles its base. Previously this was
            // projected flat to make the depth tie exact; now nothing is moved at all, and the
            // authored crossing is left for the depth buffer.
            var d = Convert(TwoQuads(secondZ: -2, secondCornerZ: 2));

            Assert.AreEqual(0.02f, SecondFaceMaxY(d), 1e-4f,
                "no vertex is displaced, whichever side of the plane it is on");
        }

        [Test]
        public void Exactly_coplanar_faces_still_share_a_group_and_get_distinct_ranks() {
            var d = Convert(TwoQuads(secondZ: 0, secondCornerZ: 0));

            float lo = float.MaxValue, hi = float.MinValue;
            foreach (float p in d.PaintOrder) { lo = Mathf.Min(lo, p); hi = Mathf.Max(hi, p); }
            Assert.AreEqual(0f, lo, "the first coplanar face keeps rank 0");
            Assert.AreEqual(1f, hi, "a genuinely tied face still gets a distinct rank for the bias");
        }
    }
}
