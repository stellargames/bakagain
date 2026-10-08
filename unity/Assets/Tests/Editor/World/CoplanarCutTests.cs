namespace BakAgain.Tests.Editor.World {
    using BakAgain.World.Converters;
    using GameData.Resources.World;
    using NUnit.Framework;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// A depth-sorted model's later coplanar overlay is cut out of the face it paints over, so the
    /// two never share depth (the original painted them in order with no depth buffer; we z-fought).
    /// </summary>
    public class CoplanarCutTests {
        private const byte Base = 10, Overlay = 12;
        private static readonly Color BaseColour = Color.red, OverlayColour = Color.green;

        /// <summary>A 100x100 BaK base quad on Z=0 and an overlay quad on the same plane, painted
        /// after it. <paramref name="reverseOverlay"/> winds the overlay the other way.</summary>
        private static TableDatInfo BaseAndOverlay(int x0, int y0, int x1, int y1,
            bool reverseOverlay = false, byte flags = MaterialKey.NoRuntimeFlags, int cornerZ = 0) {
            var verts = new List<Position3DInt> {
                new() { X = 0, Y = 0, Z = 0 }, new() { X = 100, Y = 0, Z = 0 },
                new() { X = 100, Y = 100, Z = 0 }, new() { X = 0, Y = 100, Z = 0 },
                new() { X = x0, Y = y0, Z = 0 }, new() { X = x1, Y = y0, Z = 0 },
                new() { X = x1, Y = y1, Z = cornerZ }, new() { X = x0, Y = y1, Z = 0 },
            };
            var overlay = reverseOverlay ? new List<int> { 7, 6, 5, 4 } : new List<int> { 4, 5, 6, 7 };
            var faces = new List<PolygonFace> {
                new() { VgaColor = Base, Flags = 1, VertexIndices = new List<int> { 0, 1, 2, 3 } },
                new() { VgaColor = Overlay, Flags = 1, VertexIndices = overlay },
            };
            return new TableDatInfo {
                EntityFlags = 0x40,
                Lods = new List<LodLevel> {
                    new() {
                        VertexPools = new List<List<Position3DInt>> { verts },
                        Meshes = new List<MeshRecord> {
                            new() {
                                VertexPoolIndex = 0, VertexCount = (byte)verts.Count,
                                RuntimeFlagsIndex = flags,
                                MeshFaces = new List<MeshFaceRecord> { new PolygonMeshFace { Faces = faces } },
                            },
                        },
                    },
                },
            };
        }

        private static TblMeshConverter.TerrainMeshData Convert(TableDatInfo dat) {
            var palette = new Color[256];
            palette[Base] = BaseColour;
            palette[Overlay] = OverlayColour;
            return TblMeshConverter.ConvertDepthSortedEntity(dat, palette, "test");
        }

        /// <summary>Sum of triangle areas in the colour, and the sign of their normals' Y.</summary>
        private static (float Area, int MinY, int MaxY) Measure(TblMeshConverter.TerrainMeshData d, Color colour) {
            float area = 0f;
            int minSign = 1, maxSign = -1;
            foreach (var tris in d.SubMeshTriangles)
                for (int i = 0; i < tris.Length; i += 3) {
                    if (d.Colors[tris[i]] != colour) continue;
                    Vector3 a = d.Vertices[tris[i]], b = d.Vertices[tris[i + 1]], c = d.Vertices[tris[i + 2]];
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    area += cross.magnitude / 2f;
                    int s = cross.y > 0 ? 1 : -1;
                    minSign = Mathf.Min(minSign, s);
                    maxSign = Mathf.Max(maxSign, s);
                }
            return (area, minSign, maxSign);
        }

        // 100 BaK = 1 Unity unit, so the base is 1.0 and a 20x20 overlay is 0.04.

        [Test]
        public void ALaterOverlayIsCutOutOfTheBase() {
            var d = Convert(BaseAndOverlay(40, 40, 60, 60));
            var baseM = Measure(d, BaseColour);
            Assert.AreEqual(1f - 0.04f, baseM.Area, 1e-4f);
            Assert.AreEqual(0.04f, Measure(d, OverlayColour).Area, 1e-4f, "the overlay is untouched");
            Assert.AreEqual(baseM.MinY, baseM.MaxY, "every base piece keeps one facing");
            var uncut = Measure(Convert(BaseAndOverlay(40, 40, 60, 60, flags: 3)), BaseColour);
            Assert.AreEqual(uncut.MinY, baseM.MinY, "and it is the base's own facing");
            foreach (var v in d.Vertices) Assert.AreEqual(0f, v.y, 1e-6f, "pieces stay on the base plane");
        }

        [Test]
        public void EveryOverlayCornerIsAlsoABaseVertex_NoTJunctions() {
            // The ring piece below a middle hole spans the full width; the overlay's corners sit
            // inside its top edge. Without them as vertices the rasteriser leaves pinholes there.
            var d = Convert(BaseAndOverlay(40, 40, 60, 60));
            var baseVerts = new List<Vector3>();
            var overlayVerts = new List<Vector3>();
            for (int i = 0; i < d.Vertices.Length; i++)
                (d.Colors[i] == BaseColour ? baseVerts : overlayVerts).Add(d.Vertices[i]);
            foreach (var o in overlayVerts)
                Assert.IsTrue(baseVerts.Exists(b => (b - o).sqrMagnitude < 1e-10f), $"overlay corner {o} is a base vertex");
        }

        [Test]
        public void AnOverlayCoveringTheWholeBaseRemovesIt() {
            Assert.AreEqual(0f, Measure(Convert(BaseAndOverlay(-10, -10, 110, 110)), BaseColour).Area, 1e-6f);
        }

        [Test]
        public void AnOverlayFacingTheOtherWayDoesNotCut() {
            // Single-sided and back-to-back: no viewpoint sees both, so cutting would open a hole.
            var d = Convert(BaseAndOverlay(40, 40, 60, 60, reverseOverlay: true));
            Assert.AreEqual(1f, Measure(d, BaseColour).Area, 1e-4f);
        }

        [Test]
        public void AFlaggedMeshIsNotCut() {
            // A flip-book frame is not always drawn, so it may not remove anything from what is.
            var d = Convert(BaseAndOverlay(40, 40, 60, 60, flags: 3));
            Assert.AreEqual(1f, Measure(d, BaseColour).Area, 1e-4f);
        }

        [Test]
        public void ATexturedBaseIsNotCut() {
            var dat = BaseAndOverlay(40, 40, 60, 60);
            ((PolygonMeshFace)dat.Lods[0].Meshes[0].MeshFaces[0]).Faces[0].TextureBitmap = "Z01SLOT0.BMX#0";
            var d = Convert(dat);
            // A textured face is drawn FlatFill-keyed but keeps its vertex colour.
            Assert.AreEqual(1f, Measure(d, BaseColour).Area, 1e-4f);
        }

        [Test]
        public void ATiltedNearCoplanarOverlayDoesNotCut() {
            // A bridge plank tilting a few BaK through its deck: the hole edge would sit on the
            // deck's plane and the plank below it, so cutting opens a crack. It keeps the nudge.
            var d = Convert(BaseAndOverlay(40, 40, 60, 60, cornerZ: 4));
            Assert.AreEqual(1f, Measure(d, BaseColour).Area, 1e-4f);
        }

        [Test]
        public void AnEarlierFaceDoesNotCutALaterOne() {
            // Paint order is the rule: only what is painted AFTER covers.
            var d = Convert(BaseAndOverlay(40, 40, 60, 60));
            Assert.AreEqual(0.04f, Measure(d, OverlayColour).Area, 1e-4f);
        }
    }
}
