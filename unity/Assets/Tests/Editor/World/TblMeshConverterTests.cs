namespace BakAgain.Tests.Editor.World {
    using BakAgain.World.Converters;
    using GameData.Resources.World;
    using NUnit.Framework;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine;

    public class TblMeshConverterTests {
        // Wrap raw vertices + a single PolygonFace into the LOD/MeshRecord/MeshFace
        // hierarchy the converter expects. Mirrors the on-disk shape we care about for
        // these tests: one LOD with one VertexPool and one MeshRecord (VertexPoolIndex=0)
        // whose MeshFaces[0] is a PolygonMeshFace carrying the test's faces.
        //
        // Tests must set Flags explicitly on each PolygonFace because the cull-mode
        // bits 0-1 directly drive triangle output (0=double-sided, 1/3=single-sided,
        // 2=skip). Most tests use Flags=1 to exercise the simple single-sided path.
        private static TableDatInfo BuildEntity(
            List<Position3DInt> verts, List<PolygonFace> faces,
            byte entityFlags = 0x00, byte entityType = 0x00, byte drawPriority = 0x00) {
            return new TableDatInfo {
                EntityFlags = entityFlags,
                EntityType = (WorldEntityType)entityType,
                DrawPriority = drawPriority,
                Lods = new List<LodLevel> {
                    new() {
                        VertexPools = new List<List<Position3DInt>> { verts },
                        Meshes = new List<MeshRecord> {
                            new() {
                                VertexPoolIndex = 0,
                                VertexCount = (byte)verts.Count,
                                MeshFaces = new List<MeshFaceRecord> {
                                    new PolygonMeshFace {
                                        RenderType = 0,
                                        Faces = faces
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        // Total triangle indices across all per-pen sub-meshes. The live ConvertTerrainEntity
        // buckets geometry by pen, so the generic conversion specs below assert on the totals
        // (and on SubMeshTriangles[0] when a test deliberately uses a single pen).
        private static int TotalIndices(TblMeshConverter.TerrainMeshData data) {
            int n = 0;
            foreach (var sub in data.SubMeshTriangles) n += sub.Length;
            return n;
        }

        // ─── Generic conversion specs ───────────────────────────────────────────────
        // Triangulation / coordinate conversion / double-sided winding / skip / shared
        // vertex pools. Re-pointed from the removed ConvertPolygonEntity onto the live
        // ConvertTerrainEntity, which shares the identical per-face duplication + fan
        // triangulation + reversed-winding logic (it just additionally buckets by pen).

        [Test]
        public void ConvertTerrainEntity_SingleTriangle_ProducesCorrectGeometry() {
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 },
                    new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() {
                        VgaColor = 10,
                        Flags = 1,
                        VertexIndices = new List<int> { 0, 1, 2 }
                    }
                });

            var palette = new Color[256];
            palette[10] = new Color(1f, 0f, 0f, 1f); // red

            var result = TblMeshConverter.ConvertTerrainEntity(dat, palette);

            // 1 face with 3 verts → 3 duplicated vertices, 1 triangle = 3 indices
            Assert.AreEqual(3, result.Vertices.Length);
            Assert.AreEqual(3, TotalIndices(result));
            Assert.AreEqual(3, result.Colors.Length);
            // All duplicated vertices of the face carry palette color 10 (red)
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), result.Colors[0]);
        }

        [Test]
        public void ConvertTerrainEntity_Quad_TriangulatedIntoTwoTriangles() {
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 },
                    new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 100, Y = 100, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() {
                        VgaColor = 5,
                        Flags = 1,
                        VertexIndices = new List<int> { 0, 1, 2, 3 }
                    }
                });

            var palette = new Color[256];
            palette[5] = Color.blue;

            var result = TblMeshConverter.ConvertTerrainEntity(dat, palette);

            // Quad → 4 duplicated vertices, 2 triangles = 6 triangle indices
            Assert.AreEqual(4, result.Vertices.Length);
            Assert.AreEqual(6, TotalIndices(result));
        }

        [Test]
        public void ConvertTerrainEntity_VerticesConvertedThroughCoordinateSystem() {
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 100, Y = 200, Z = 300 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 0, Flags = 1, VertexIndices = new List<int> { 0, 0, 0 } }
                });

            var result = TblMeshConverter.ConvertTerrainEntity(dat, new Color[256]);

            // BaK (100,200,300) → Unity (1, 3, 2): WorldScale=100 with Y/Z swap (see
            // BakCoordinateConverter.ConvertPosition). All 3 duplicated verts are from index 0.
            Assert.AreEqual(1f, result.Vertices[0].x, 0.01f);
            Assert.AreEqual(3f, result.Vertices[0].y, 0.01f);
            Assert.AreEqual(2f, result.Vertices[0].z, 0.01f);
        }

        [Test]
        public void ConvertTerrainEntity_DoubleSidedFace_EmitsBothWindings() {
            // PolygonFace.Flags bits 0-1 == 0 → double-sided (e.g. corn leaves at Z01:193).
            // The converter must emit both front- and reverse-winding triangles so the face
            // renders from either side under default Cull Back materials.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 },
                    new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 5, Flags = 0x00, VertexIndices = new List<int> { 0, 1, 2 } }
                });

            var result = TblMeshConverter.ConvertTerrainEntity(dat, new Color[256]);

            // 3 front verts + 3 back-side duplicates, 2 triangles = 6 indices (one pen → one sub-mesh).
            Assert.AreEqual(6, result.Vertices.Length);
            Assert.AreEqual(6, TotalIndices(result));
            Assert.AreEqual(1, result.SubMeshTriangles.Length);
            // Back-side triangle uses indices [3,5,4] (reversed winding).
            var tris = result.SubMeshTriangles[0];
            Assert.AreEqual(3, tris[3]);
            Assert.AreEqual(5, tris[4]);
            Assert.AreEqual(4, tris[5]);
        }

        [Test]
        public void ConvertTerrainEntity_SkipFace_EmitsNoGeometry() {
            // PolygonFace.Flags bits 0-1 == 2 → skip (don't render).
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 },
                    new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 5, Flags = 0x02, VertexIndices = new List<int> { 0, 1, 2 } }
                });

            var result = TblMeshConverter.ConvertTerrainEntity(dat, new Color[256]);

            Assert.AreEqual(0, result.Vertices.Length);
            Assert.AreEqual(0, TotalIndices(result));
            Assert.AreEqual(0, result.SubMeshTriangles.Length);
        }

        [Test]
        public void ConvertTerrainEntity_MultipleMeshesShareVertexPool_RendersCorrectly() {
            // Models the inn/church pattern: many MeshRecords reference one shared VertexPool
            // via VertexPoolIndex. Each mesh has its own face list but consumes the same verts.
            var sharedPool = new List<Position3DInt> {
                new() { X = 0, Y = 0, Z = 0 },
                new() { X = 100, Y = 0, Z = 0 },
                new() { X = 0, Y = 100, Z = 0 },
                new() { X = 100, Y = 100, Z = 0 }
            };
            var dat = new TableDatInfo {
                Lods = new List<LodLevel> {
                    new() {
                        VertexPools = new List<List<Position3DInt>> { sharedPool },
                        Meshes = new List<MeshRecord> {
                            new() {
                                VertexPoolIndex = 0,
                                VertexCount = 4,
                                MeshFaces = new List<MeshFaceRecord> {
                                    new PolygonMeshFace {
                                        RenderType = 0,
                                        Faces = new List<PolygonFace> {
                                            new() { VgaColor = 1, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } }
                                        }
                                    }
                                }
                            },
                            new() {
                                VertexPoolIndex = 0,
                                VertexCount = 4,
                                MeshFaces = new List<MeshFaceRecord> {
                                    new PolygonMeshFace {
                                        RenderType = 0,
                                        Faces = new List<PolygonFace> {
                                            new() { VgaColor = 2, Flags = 1, VertexIndices = new List<int> { 1, 3, 2 } }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var result = TblMeshConverter.ConvertTerrainEntity(dat, new Color[256]);

            // Two single-sided triangles, each with 3 duplicated verts → 6 verts, 6 indices.
            Assert.AreEqual(6, result.Vertices.Length);
            Assert.AreEqual(6, TotalIndices(result));
        }

        // ─── Pen-bucketing specs ────────────────────────────────────────────────────

        [Test]
        public void ConvertTerrainEntity_PathPen_PlacedInPathSubMesh() {
            // VgaColor 0x02 = Path pen. ConvertTerrainEntity should bucket this face
            // into the TerrainPen.Path sub-mesh.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 },
                    new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 0x02, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } }
                },
                drawPriority: 8); // intentionally unrelated — should be ignored

            var result = TblMeshConverter.ConvertTerrainEntity(dat, new Color[256]);

            Assert.AreEqual(1, result.Keys.Length);
            Assert.AreEqual(TerrainPen.Path, result.Keys[0].Pen);
            Assert.AreEqual(3, result.SubMeshTriangles[0].Length); // 1 triangle = 3 indices
        }

        [Test]
        public void ConvertTerrainEntity_UnknownPen_BucketedAsFlatFill() {
            // Pens not in the known table (e.g. 0xA0) should be bucketed as FlatFill
            // so the renderer uses vertex color instead of a texture.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 },
                    new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 0xA0, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } }
                });

            var result = TblMeshConverter.ConvertTerrainEntity(dat, new Color[256]);

            Assert.AreEqual(1, result.Keys.Length);
            Assert.AreEqual(TerrainPen.FlatFill, result.Keys[0].Pen);
        }

        [Test]
        public void ConvertTerrainEntity_GroundPen_BucketedAsGround() {
            // Pen 0x00 = Ground terrain type.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 },
                    new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 0x00, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } }
                });

            var result = TblMeshConverter.ConvertTerrainEntity(dat, new Color[256]);

            Assert.AreEqual(1, result.Keys.Length);
            Assert.AreEqual(TerrainPen.Ground, result.Keys[0].Pen);
        }

        [Test]
        public void ConvertTerrainEntity_MultiplePens_ProducesMultipleSubMeshes() {
            // A terrain entity with Ground (0x00) + River (0x03) faces should produce
            // two sub-meshes, each with distinct pens.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 },
                    new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 },
                    new() { X = 100, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 0x00, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } },
                    new() { VgaColor = 0x03, Flags = 1, VertexIndices = new List<int> { 1, 3, 2 } }
                });

            var result = TblMeshConverter.ConvertTerrainEntity(dat, new Color[256]);

            Assert.AreEqual(2, result.Keys.Length);
            Assert.AreEqual(2, result.SubMeshTriangles.Length);
            // Both sub-meshes should have 3 indices (one triangle each)
            Assert.AreEqual(3, result.SubMeshTriangles[0].Length);
            Assert.AreEqual(3, result.SubMeshTriangles[1].Length);
            // Pens should include Ground and River (order depends on dictionary iteration)
            var penSet = new HashSet<TerrainPen>(result.Keys.Select(k => k.Pen));
            Assert.IsTrue(penSet.Contains(TerrainPen.Ground));
            Assert.IsTrue(penSet.Contains(TerrainPen.River));
        }

        // ─── Depth-sorted pen-bucketing specs ───────────────────────────────────────
        // Regression guard: EF_DEPTH_SORTED (0x40) governs draw ORDER, not shading. The
        // original decides textured-vs-flat per FACE (processEntityFaces @0x23a48), so
        // depth-sorted models must keep their textured pens — landscape models (landscp1-4)
        // are 0x40 yet their ground faces use GroundLod-range colours that must stay textured.

        [Test]
        public void ConvertDepthSortedEntity_GroundLodFace_KeepsTexturedPen() {
            // VgaColor 0xFF is in the GroundLod range (0xE0-0xFF / 0xF7-0xFF). A depth-sorted
            // model must NOT flatten this to FlatFill — that was the landscape texture regression.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 },
                    new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 0, Z = 100 } // horizontal ground face (constant Y)
                },
                new List<PolygonFace> {
                    new() { VgaColor = 0xFF, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } }
                },
                entityFlags: 0x40);

            var result = TblMeshConverter.ConvertDepthSortedEntity(dat, new Color[256], "TestEntity");

            Assert.AreEqual(1, result.Keys.Length);
            Assert.AreEqual(TerrainPen.GroundLod, result.Keys[0].Pen);
        }

        [Test]
        public void ConvertDepthSortedEntity_FlatColourFace_StaysFlatFill() {
            // A house wall colour (0xA0, not in any textured range) stays FlatFill / vertex-colour —
            // houses render exactly as before.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 },
                    new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 0xA0, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } }
                },
                entityFlags: 0x40);

            var result = TblMeshConverter.ConvertDepthSortedEntity(dat, new Color[256], "TestEntity");

            Assert.AreEqual(1, result.Keys.Length);
            Assert.AreEqual(TerrainPen.FlatFill, result.Keys[0].Pen);
        }

        [Test]
        public void ConvertDepthSortedEntity_MixedGroundAndWall_ProducesBothPens() {
            // Landscape shape: a textured ground face (GroundLod) + a flat wall face (FlatFill)
            // in one depth-sorted model → two sub-meshes with distinct pens.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 },
                    new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 0, Z = 100 },
                    new() { X = 0, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 0xFF, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } }, // ground → GroundLod
                    new() { VgaColor = 0xA0, Flags = 1, VertexIndices = new List<int> { 0, 1, 3 } }  // wall → FlatFill
                },
                entityFlags: 0x40);

            var result = TblMeshConverter.ConvertDepthSortedEntity(dat, new Color[256], "TestEntity");

            Assert.AreEqual(2, result.Keys.Length);
            var penSet = new HashSet<TerrainPen>(result.Keys.Select(k => k.Pen));
            Assert.IsTrue(penSet.Contains(TerrainPen.GroundLod));
            Assert.IsTrue(penSet.Contains(TerrainPen.FlatFill));
        }

        [Test]
        public void ConvertTerrainEntity_HorizonPen_BucketedAsHorizon1() {
            // Pen 0x08 = Horizon1. Should be in its own sub-mesh.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 },
                    new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 0x08, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } }
                });

            var result = TblMeshConverter.ConvertTerrainEntity(dat, new Color[256]);

            Assert.AreEqual(1, result.Keys.Length);
            Assert.AreEqual(TerrainPen.Horizon1, result.Keys[0].Pen);
        }

        [Test]
        public void ConvertDepthSortedEntity_CoplanarFaces_GetIncrementingRanks() {
            // Two single-sided triangular faces in the SAME plane (all verts at BaK Z=0). Ranks are
            // per-coplanar-group in emit order: face 0 → rank 0, face 1 → rank 1, so the later-painted
            // face wins the depth tie.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 }, new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 }, new() { X = 100, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } },
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 1, 3, 2 } }
                },
                entityFlags: 0x40);

            var result = TblMeshConverter.ConvertDepthSortedEntity(dat, new Color[256], "test");

            Assert.IsNotNull(result.PaintOrder);
            Assert.AreEqual(result.Vertices.Length, result.PaintOrder.Length);
            CollectionAssert.AreEqual(new float[] { 0, 0, 0, 1, 1, 1 }, result.PaintOrder);
        }

        [Test]
        public void ConvertTerrainEntity_LeavesPaintOrderZero() {
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 }, new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } }
                });

            var result = TblMeshConverter.ConvertTerrainEntity(dat, new Color[256]);

            Assert.IsNotNull(result.PaintOrder);
            Assert.AreEqual(result.Vertices.Length, result.PaintOrder.Length);
            CollectionAssert.AreEqual(new float[] { 0, 0, 0 }, result.PaintOrder);
        }

        [Test]
        public void ConvertDepthSortedEntity_DoubleSidedFace_BothWindingsShareRank() {
            // Both faces share the BaK Z=0 plane, so they form one coplanar group: face 0 single-sided
            // (rank 0, 3 verts); face 1 double-sided (rank 1) emits front + reversed verts — BOTH the
            // front and back windings of face 1 must carry rank 1 (catches a back-winding rank desync).
            // Expected order: {0,0,0, 1,1,1, 1,1,1}.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 }, new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 }, new() { X = 100, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } },
                    new() { VgaColor = 10, Flags = 0, VertexIndices = new List<int> { 1, 3, 2 } }
                },
                entityFlags: 0x40);

            var result = TblMeshConverter.ConvertDepthSortedEntity(dat, new Color[256], "test");

            Assert.AreEqual(result.Vertices.Length, result.PaintOrder.Length);
            CollectionAssert.AreEqual(new float[] { 0, 0, 0, 1, 1, 1, 1, 1, 1 }, result.PaintOrder);
        }

        [Test]
        public void ConvertDepthSortedEntity_DifferentPlanes_EachFaceRankZero() {
            // Regression guard for the v1 see-through-model bug: a model-global running rank biased
            // faces of DISTINCT planes against each other, letting rear faces bleed through front
            // walls at distance. Faces in different planes must each start their own group at rank 0.
            // Face 0 lies in the ground plane (BaK Z=0 → Unity y=0); face 1 stands vertical
            // (BaK Y=0 → Unity z=0).
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 }, new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 }, new() { X = 0, Y = 0, Z = 100 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } },
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 0, 1, 3 } }
                },
                entityFlags: 0x40);

            var result = TblMeshConverter.ConvertDepthSortedEntity(dat, new Color[256], "test");

            Assert.AreEqual(result.Vertices.Length, result.PaintOrder.Length);
            CollectionAssert.AreEqual(new float[] { 0, 0, 0, 0, 0, 0 }, result.PaintOrder);
        }

        [Test]
        public void ConvertDepthSortedEntity_OppositeWoundCoplanarFaces_ShareGroup() {
            // A decal wound opposite to its base (the exact pair the winding-orientation stopgap
            // failed on) must land in the SAME plane group: the normal's sign is canonicalized, so
            // the reversed face still gets the next rank instead of starting a new group at 0
            // (which would leave the pair z-fighting). Face 1 is face 0's triangle reversed.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 }, new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } },
                    new() { VgaColor = 12, Flags = 1, VertexIndices = new List<int> { 2, 1, 0 } }
                },
                entityFlags: 0x40);

            var result = TblMeshConverter.ConvertDepthSortedEntity(dat, new Color[256], "test");

            Assert.AreEqual(result.Vertices.Length, result.PaintOrder.Length);
            CollectionAssert.AreEqual(new float[] { 0, 0, 0, 1, 1, 1 }, result.PaintOrder);
        }

        [Test]
        public void ConvertDepthSortedEntity_NearCoplanarDecal_AbsorbedButGeometryPreserved() {
            // Bridge-plank case: a decal authored for the painter's algorithm tilts through its base
            // plane by a few BaK units (here ±2, within the grouping tolerance). It joins the base's
            // plane group so it gets rank 1 for the shader's depth-bias tie-break — but its geometry
            // is left exactly as authored. Projecting it flat (the old behaviour) destroyed authored
            // separations elsewhere; see PaintOrderAbsorptionTests.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 }, new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 100, Y = 100, Z = 0 }, new() { X = 0, Y = 100, Z = 0 },
                    new() { X = 10, Y = 10, Z = 2 }, new() { X = 90, Y = 10, Z = -2 },
                    new() { X = 90, Y = 90, Z = 2 }, new() { X = 10, Y = 90, Z = -2 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 0, 1, 2, 3 } },
                    new() { VgaColor = 12, Flags = 1, VertexIndices = new List<int> { 4, 5, 6, 7 } }
                },
                entityFlags: 0x40);

            var result = TblMeshConverter.ConvertDepthSortedEntity(dat, new Color[256], "test");

            CollectionAssert.AreEqual(new float[] { 0, 0, 0, 0, 1, 1, 1, 1 }, result.PaintOrder);
            // Decal verts (indices 4-7) keep their authored ±2 BaK offsets — nothing is moved.
            Assert.AreEqual(0.02f, result.Vertices[4].y, 1e-4f, "vertex 4 must not be moved");
            Assert.AreEqual(-0.02f, result.Vertices[5].y, 1e-4f, "vertex 5 must not be moved");
            Assert.AreEqual(0.02f, result.Vertices[6].y, 1e-4f, "vertex 6 must not be moved");
            Assert.AreEqual(-0.02f, result.Vertices[7].y, 1e-4f, "vertex 7 must not be moved");
        }

        [Test]
        public void ConvertDepthSortedEntity_PerpendicularFaceSkimmingPlane_NotAbsorbed() {
            // A perpendicular sliver whose verts all lie within snap distance of the base plane (a
            // 5-BaK-unit-tall fin) must NOT be pulled into the base's group: the normal check keeps
            // it out. It stays rank 0 in its own plane and its geometry is untouched.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 }, new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 },
                    new() { X = 0, Y = 0, Z = 5 } // fin apex, 5 BaK units above the plane
                },
                new List<PolygonFace> {
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } },
                    new() { VgaColor = 12, Flags = 1, VertexIndices = new List<int> { 0, 1, 3 } }
                },
                entityFlags: 0x40);

            var result = TblMeshConverter.ConvertDepthSortedEntity(dat, new Color[256], "test");

            CollectionAssert.AreEqual(new float[] { 0, 0, 0, 0, 0, 0 }, result.PaintOrder);
            Assert.AreEqual(0.05f, result.Vertices[5].y, 1e-5f, "fin apex must not be snapped");
        }

        [Test]
        public void ConvertDepthSortedEntity_TwistedFounderQuad_KeepsItsAuthoredTwist() {
            // Church-window case: a group's FOUNDER can itself be a twisted (non-planar) quad, whose
            // fan triangles tilt around the best-fit plane. This used to be planarized onto the Newell
            // plane at emission; the converter no longer moves any vertex, so the authored twist is
            // preserved and the tilt is left for the depth buffer to resolve on real geometry.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 2 }, new() { X = 100, Y = 0, Z = -2 },
                    new() { X = 100, Y = 100, Z = 2 }, new() { X = 0, Y = 100, Z = -2 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 0, 1, 2, 3 } }
                },
                entityFlags: 0x40);

            var result = TblMeshConverter.ConvertDepthSortedEntity(dat, new Color[256], "test");

            CollectionAssert.AreEqual(new float[] { 0, 0, 0, 0 }, result.PaintOrder);
            float[] expected = { 0.02f, -0.02f, 0.02f, -0.02f };
            for (int i = 0; i < 4; i++)
                Assert.AreEqual(expected[i], result.Vertices[i].y, 1e-4f, $"founder vertex {i} was moved");
        }

        [Test]
        public void ConvertDepthSortedEntity_JunctionVerticesAreNeverMoved() {
            // Face B is near-coplanar with base A and joins its group (rank 1). B's off-plane vertex
            // (pool 3) is ALSO used by perpendicular face C in another group — a structural junction.
            // Since the converter no longer moves vertices, both copies keep the authored height and
            // the junction cannot tear.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 }, new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 },
                    new() { X = 200, Y = 200, Z = 5 },
                    new() { X = 300, Y = 200, Z = 5 }, new() { X = 250, Y = 200, Z = 105 },
                    new() { X = 200, Y = 0, Z = -5 }, new() { X = 0, Y = 200, Z = -5 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } },
                    new() { VgaColor = 12, Flags = 1, VertexIndices = new List<int> { 6, 7, 3 } },
                    new() { VgaColor = 14, Flags = 1, VertexIndices = new List<int> { 3, 4, 5 } }
                },
                entityFlags: 0x40);

            var result = TblMeshConverter.ConvertDepthSortedEntity(dat, new Color[256], "test");

            // A founds group 0; B joins it (rank 1); C founds its own vertical group (rank 0).
            CollectionAssert.AreEqual(new float[] { 0, 0, 0, 1, 1, 1, 0, 0, 0 }, result.PaintOrder);
            // Both emitted copies of pool vertex 3 keep the authored height: no snap means a
            // junction shared between groups can no longer tear at all.
            Assert.AreEqual(0.05f, result.Vertices[5].y, 1e-5f, "B's copy must not move");
            Assert.AreEqual(0.05f, result.Vertices[6].y, 1e-5f, "C's copy must not move");
        }

        [Test]
        public void ConvertDepthSortedEntity_ParallelFaceBeyondSnapDistance_NotAbsorbed() {
            // A parallel surface 20 BaK units above the base (a genuine second layer, beyond the
            // 6-unit snap tolerance) keeps its own plane group and its own height.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 }, new() { X = 100, Y = 0, Z = 0 },
                    new() { X = 0, Y = 100, Z = 0 },
                    new() { X = 0, Y = 0, Z = 20 }, new() { X = 100, Y = 0, Z = 20 },
                    new() { X = 0, Y = 100, Z = 20 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 0, 1, 2 } },
                    new() { VgaColor = 12, Flags = 1, VertexIndices = new List<int> { 3, 4, 5 } }
                },
                entityFlags: 0x40);

            var result = TblMeshConverter.ConvertDepthSortedEntity(dat, new Color[256], "test");

            CollectionAssert.AreEqual(new float[] { 0, 0, 0, 0, 0, 0 }, result.PaintOrder);
            Assert.AreEqual(0.2f, result.Vertices[3].y, 1e-5f, "second layer must keep its height");
        }

        // ─── Line faces (two vertices) ──────────────────────────────────────────────
        // polygon_draw @0x1ce39 branches on the vertex count and the == 2 case falls through to
        // drawSegmentsFromCoordArrays — one segment — so a two-vertex face is a LINE the original
        // draws, not a degenerate polygon it skips. 61 ship, and dropping them left the trap
        // cannon's body floating above a detached base plate (TASK-327).

        [Test]
        public void ConvertTerrainEntity_TwoVertexFace_EmitsLineGeometry() {
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 },
                    new() { X = 0, Y = 1000, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 0, 1 } }
                });

            var palette = new Color[256];
            palette[10] = new Color(1f, 0f, 0f, 1f);

            var result = TblMeshConverter.ConvertTerrainEntity(dat, palette);

            // Two crossed ribbons, 4 verts each; both windings of both, so 8 triangles.
            Assert.AreEqual(8, result.Vertices.Length, "two crossed ribbons of 4 verts");
            Assert.AreEqual(24, TotalIndices(result), "8 triangles = 24 indices");
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), result.Colors[0], "line takes its pen colour");

            // The point of the fix: the geometry must actually SPAN the two endpoints, not sit
            // bunched at one of them. Asserting the span rather than the vertex count is what
            // catches a ribbon built off a zero-length or mis-indexed direction.
            //
            // Measured on the LONGEST axis rather than a named one: the converter maps BaK's Y to
            // Unity's z, so asserting on .y here reads only the ribbon's own width (0.068 = twice
            // the half-width) and fails a working implementation. It did.
            var vs = result.Vertices;
            float spanX = vs.Max(v => v.x) - vs.Min(v => v.x);
            float spanY = vs.Max(v => v.y) - vs.Min(v => v.y);
            float spanZ = vs.Max(v => v.z) - vs.Min(v => v.z);
            float longest = Mathf.Max(spanX, Mathf.Max(spanY, spanZ));
            Assert.Greater(longest, 0.5f, "ribbon must run the length of the line");
        }

        [Test]
        public void ConvertTerrainEntity_LineFaceMarkedSkip_EmitsNothing() {
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0, Y = 0, Z = 0 },
                    new() { X = 0, Y = 1000, Z = 0 }
                },
                new List<PolygonFace> {
                    // cull bits 2 = Skip, which a line must honour like any other face
                    new() { VgaColor = 10, Flags = 2, VertexIndices = new List<int> { 0, 1 } }
                });

            var result = TblMeshConverter.ConvertTerrainEntity(dat, new Color[256]);

            Assert.AreEqual(0, TotalIndices(result));
        }

        // ─── Concave faces (TASK-339) ───────────────────────────────────────────────
        // A fan from vertex 0 is valid only for CONVEX polygons; on a concave one it emits
        // triangles OUTSIDE the outline. 20 shipped faces are concave and 8 carry real area —
        // db8 in all six outdoor zones, and two catapult quads.

        [Test]
        public void ConvertTerrainEntity_ConcaveFace_EmitsNoTriangleOutsideTheOutline() {
            // A U in the ground plane (BaK Z=0). The notch spans 100<X<200 above Y=100 and is
            // OUTSIDE the polygon; a fan from vertex 0 covers part of it, ear clipping does not.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0,   Y = 0,   Z = 0 },
                    new() { X = 300, Y = 0,   Z = 0 },
                    new() { X = 300, Y = 300, Z = 0 },
                    new() { X = 200, Y = 300, Z = 0 },
                    new() { X = 200, Y = 100, Z = 0 },
                    new() { X = 100, Y = 100, Z = 0 },
                    new() { X = 100, Y = 300, Z = 0 },
                    new() { X = 0,   Y = 300, Z = 0 }
                },
                new List<PolygonFace> {
                    new() {
                        VgaColor = 10, Flags = 1,
                        VertexIndices = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 }
                    }
                });

            var result = TblMeshConverter.ConvertTerrainEntity(dat, new Color[256]);

            // Six triangles for eight vertices, whichever way they are cut.
            Assert.AreEqual(6 * 3, TotalIndices(result), "an 8-gon triangulates to 6 triangles");

            // The converter maps BaK (X,Y,Z) to Unity (X, Z, Y), so the face lies in the XZ plane
            // and the notch centre is (1.5, _, 1.5).
            var notch = new Vector2(1.5f, 1.5f);
            foreach (var sub in result.SubMeshTriangles) {
                for (int i = 0; i < sub.Length; i += 3) {
                    Vector3 a = result.Vertices[sub[i]];
                    Vector3 b = result.Vertices[sub[i + 1]];
                    Vector3 c = result.Vertices[sub[i + 2]];
                    Assert.IsFalse(
                        PointInTriangleXz(notch, a, b, c),
                        $"triangle ({a},{b},{c}) covers the notch at {notch}, which is outside the "
                        + "polygon — the face was fan-triangulated rather than ear-clipped");
                }
            }
        }

        [Test]
        public void ConvertTerrainEntity_ConvexFace_KeepsTheCheapFan() {
            // The fast path must stay exact: 9418 of 9426 shipped 4+ vertex faces are convex, and a
            // convex fan is both correct and cheaper than clipping.
            var dat = BuildEntity(
                new List<Position3DInt> {
                    new() { X = 0,   Y = 0,   Z = 0 },
                    new() { X = 200, Y = 0,   Z = 0 },
                    new() { X = 200, Y = 200, Z = 0 },
                    new() { X = 0,   Y = 200, Z = 0 }
                },
                new List<PolygonFace> {
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 0, 1, 2, 3 } }
                });

            var result = TblMeshConverter.ConvertTerrainEntity(dat, new Color[256]);

            Assert.AreEqual(2 * 3, TotalIndices(result));
            // Fan from vertex 0 == (0,1,2) then (0,2,3).
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0, 2, 3 }, result.SubMeshTriangles[0]);
        }

        private static bool PointInTriangleXz(Vector2 p, Vector3 a, Vector3 b, Vector3 c) {
            float Side(Vector2 u, Vector2 v) => ((v.x - u.x) * (p.y - u.y)) - ((v.y - u.y) * (p.x - u.x));
            var a2 = new Vector2(a.x, a.z);
            var b2 = new Vector2(b.x, b.z);
            var c2 = new Vector2(c.x, c.z);
            float d1 = Side(a2, b2), d2 = Side(b2, c2), d3 = Side(c2, a2);
            bool anyNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool anyPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(anyNeg && anyPos);
        }

        [Test]
        public void ConvertTerrainEntity_SingleVertexFace_StillEmitsNothing() {
            // < 2 is the original's `jl` arm: it draws nothing, and neither may we. Guards the
            // line path against widening the predicate too far.
            var dat = BuildEntity(
                new List<Position3DInt> { new() { X = 0, Y = 0, Z = 0 } },
                new List<PolygonFace> {
                    new() { VgaColor = 10, Flags = 1, VertexIndices = new List<int> { 0 } }
                });

            var result = TblMeshConverter.ConvertTerrainEntity(dat, new Color[256]);

            Assert.AreEqual(0, TotalIndices(result));
        }
    }
}
