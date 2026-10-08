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
        private const byte SingleSided = 1, DoubleSided = 0;
        private static readonly Color BaseColour = Color.red, OverlayColour = Color.green;

        private struct Rect { public int X0, Y0, X1, Y1; public int CornerZ; public bool Reverse; public byte Flags; }

        private static Rect R(int x0, int y0, int x1, int y1) =>
            new Rect { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Flags = SingleSided };

        /// <summary>A 100x100 BaK base quad on Z=0 plus overlay quads on the same plane. The base is
        /// emitted first unless <paramref name="overlaysFirst"/>.</summary>
        private static TableDatInfo Build(IEnumerable<Rect> overlays, byte baseFlags = SingleSided,
            byte meshFlags = MaterialKey.NoRuntimeFlags, bool overlaysFirst = false) {
            var verts = new List<Position3DInt> {
                new() { X = 0, Y = 0, Z = 0 }, new() { X = 100, Y = 0, Z = 0 },
                new() { X = 100, Y = 100, Z = 0 }, new() { X = 0, Y = 100, Z = 0 },
            };
            var baseFace = new PolygonFace { VgaColor = Base, Flags = baseFlags, VertexIndices = new List<int> { 0, 1, 2, 3 } };
            var faces = new List<PolygonFace>();
            if (!overlaysFirst) faces.Add(baseFace);
            foreach (var o in overlays) {
                int b = verts.Count;
                verts.Add(new() { X = o.X0, Y = o.Y0, Z = 0 });
                verts.Add(new() { X = o.X1, Y = o.Y0, Z = 0 });
                verts.Add(new() { X = o.X1, Y = o.Y1, Z = o.CornerZ });
                verts.Add(new() { X = o.X0, Y = o.Y1, Z = 0 });
                var idx = o.Reverse ? new List<int> { b + 3, b + 2, b + 1, b } : new List<int> { b, b + 1, b + 2, b + 3 };
                faces.Add(new PolygonFace { VgaColor = Overlay, Flags = o.Flags, VertexIndices = idx });
            }
            if (overlaysFirst) faces.Add(baseFace);
            return new TableDatInfo {
                EntityFlags = 0x40,
                Lods = new List<LodLevel> {
                    new() {
                        VertexPools = new List<List<Position3DInt>> { verts },
                        Meshes = new List<MeshRecord> {
                            new() {
                                VertexPoolIndex = 0, VertexCount = (byte)verts.Count,
                                RuntimeFlagsIndex = meshFlags,
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

        /// <summary>Sum of triangle areas in the colour, and the sign range of their normals' Y.</summary>
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
            var d = Convert(Build(new[] { R(40, 40, 60, 60) }));
            var baseM = Measure(d, BaseColour);
            Assert.AreEqual(1f - 0.04f, baseM.Area, 1e-4f);
            Assert.AreEqual(0.04f, Measure(d, OverlayColour).Area, 1e-4f, "the overlay is untouched");
            Assert.AreEqual(baseM.MinY, baseM.MaxY, "every base piece keeps one facing");
            var uncut = Measure(Convert(Build(new[] { R(40, 40, 60, 60) }, meshFlags: 3)), BaseColour);
            Assert.AreEqual(uncut.MinY, baseM.MinY, "and it is the base's own facing");
            foreach (var v in d.Vertices) Assert.AreEqual(0f, v.y, 1e-6f, "pieces stay on the base plane");
        }

        [Test]
        public void EveryOverlayCornerIsAlsoABaseVertex_NoTJunctions() {
            // The ring piece below a middle hole spans the full width; the overlay's corners sit
            // inside its top edge. Without them as vertices the rasteriser leaves pinholes there.
            var d = Convert(Build(new[] { R(40, 40, 60, 60) }));
            var baseVerts = new List<Vector3>();
            var overlayVerts = new List<Vector3>();
            for (int i = 0; i < d.Vertices.Length; i++)
                (d.Colors[i] == BaseColour ? baseVerts : overlayVerts).Add(d.Vertices[i]);
            foreach (var o in overlayVerts)
                Assert.IsTrue(baseVerts.Exists(b => (b - o).sqrMagnitude < 1e-10f), $"overlay corner {o} is a base vertex");
        }

        [Test]
        public void TwoOverlays_NoBaseCornerLiesInsideAnotherBasePieceEdge() {
            // The clipper strips by EXTENDING hole edges: the second overlay's strip lines end on
            // internal edges of pieces the first one made, at points that are no outline's corner.
            var d = Convert(Build(new[] { R(20, 20, 40, 40), R(55, 50, 80, 75) }));
            var corners = new List<Vector3>();
            var edges = new List<(Vector3 A, Vector3 B)>();
            foreach (var tris in d.SubMeshTriangles)
                for (int i = 0; i < tris.Length; i += 3) {
                    if (d.Colors[tris[i]] != BaseColour) continue;
                    Vector3 a = d.Vertices[tris[i]], b = d.Vertices[tris[i + 1]], c = d.Vertices[tris[i + 2]];
                    if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) continue;   // collinear filler
                    corners.Add(a); corners.Add(b); corners.Add(c);
                    edges.Add((a, b)); edges.Add((b, c)); edges.Add((c, a));
                }
            Assert.Greater(edges.Count, 12, "the base was cut into several pieces");
            foreach (var p in corners)
                foreach (var (a, b) in edges) {
                    Vector3 ab = b - a;
                    float t = Vector3.Dot(p - a, ab) / ab.sqrMagnitude;
                    if (t <= 1e-4f || t >= 1 - 1e-4f) continue;
                    float off = (a + (ab * t) - p).magnitude;
                    Assert.Greater(off, 1e-5f, $"base corner {p} lies inside base edge {a}-{b}: a T-junction");
                }
        }

        [Test]
        public void AnOverlayCoveringTheWholeBaseRemovesIt() {
            Assert.AreEqual(0f, Measure(Convert(Build(new[] { R(-10, -10, 110, 110) })), BaseColour).Area, 1e-6f);
        }

        [Test]
        public void AnOverlayFacingTheOtherWayDoesNotCut() {
            // Single-sided and back-to-back: no viewpoint sees both, so cutting would open a hole.
            var o = R(40, 40, 60, 60); o.Reverse = true;
            Assert.AreEqual(1f, Measure(Convert(Build(new[] { o })), BaseColour).Area, 1e-4f);
        }

        [Test]
        public void ADoubleSidedOverlayCutsASingleSidedBase() {
            var o = R(40, 40, 60, 60); o.Flags = DoubleSided;
            Assert.AreEqual(0.96f, Measure(Convert(Build(new[] { o })), BaseColour).Area, 1e-4f);
        }

        [Test]
        public void ADoubleSidedBaseIsNotCutByASingleSidedOverlay() {
            // Seen from behind, the overlay is culled and the base must still be whole. Both
            // windings are emitted, so the whole base measures 2.0.
            var d = Convert(Build(new[] { R(40, 40, 60, 60) }, baseFlags: DoubleSided));
            Assert.AreEqual(2f, Measure(d, BaseColour).Area, 1e-4f);
        }

        [Test]
        public void AFlaggedMeshIsNotCut() {
            // A flip-book frame is not always drawn, so it may not remove anything from what is.
            Assert.AreEqual(1f, Measure(Convert(Build(new[] { R(40, 40, 60, 60) }, meshFlags: 3)), BaseColour).Area, 1e-4f);
        }

        [Test]
        public void ATexturedBaseIsNotCut() {
            var dat = Build(new[] { R(40, 40, 60, 60) });
            ((PolygonMeshFace)dat.Lods[0].Meshes[0].MeshFaces[0]).Faces[0].TextureBitmap = "Z01SLOT0.BMX#0";
            // A textured face is drawn FlatFill-keyed but keeps its vertex colour.
            Assert.AreEqual(1f, Measure(Convert(dat), BaseColour).Area, 1e-4f);
        }

        [Test]
        public void ATiltedNearCoplanarOverlayDoesNotCut() {
            // A bridge plank tilting a few BaK through its deck: the hole edge would sit on the
            // deck's plane and the plank below it, so cutting opens a crack. It keeps the nudge.
            var o = R(40, 40, 60, 60); o.CornerZ = 4;
            Assert.AreEqual(1f, Measure(Convert(Build(new[] { o })), BaseColour).Area, 1e-4f);
        }

        [Test]
        public void PaintOrderDecides_ABigFacePaintedLaterRemovesTheOverlayBeneathIt() {
            // Overlay emitted FIRST, base after: the base paints over it, so the overlay is gone
            // and the base is whole.
            var d = Convert(Build(new[] { R(40, 40, 60, 60) }, overlaysFirst: true));
            Assert.AreEqual(0f, Measure(d, OverlayColour).Area, 1e-6f);
            Assert.AreEqual(1f, Measure(d, BaseColour).Area, 1e-4f);
        }
    }
}
