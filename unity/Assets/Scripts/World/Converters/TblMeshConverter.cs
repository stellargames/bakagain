namespace BakAgain.World.Converters {
    using GameData.Resources.World;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Identifies a terrain sub-mesh's material. Either a vertex-coloured <see cref="TerrainPen"/>
    /// (<see cref="TextureKey"/> null) or a slot-bitmap textured quad (<see cref="TextureKey"/> set, and
    /// <see cref="Pen"/> forced to <see cref="TerrainPen.FlatFill"/>). Value-equality so it can key
    /// the triangle buckets. Hand-written (not a <c>record struct</c>) to stay within the Unity
    /// assembly's C# 9 language level.
    /// </summary>
    public readonly struct MaterialKey : System.IEquatable<MaterialKey> {
        public readonly TerrainPen Pen;
        /// <summary>The baked slot-bitmap resource key ("Z01SLOT3.BMX#8") for a textured quad, or
        /// null for a vertex-coloured pen face.</summary>
        public readonly string TextureKey;

        /// <summary>
        /// The source mesh record's <c>RuntimeFlagsIndex</c>, or <see cref="NoRuntimeFlags"/>.
        /// </summary>
        /// <remarks>
        /// <b>Part of the key so a flagged mesh keeps its own sub-mesh.</b> Grouping purely by
        /// material would merge a door's one flagged panel into the same sub-mesh as the 24 frame
        /// pieces that share its pen, and there would be no way to move it afterwards — which is
        /// what the swing needs (<c>renderShapeDispatcher</c> @0x2a7b2 thresholds a mesh in or out
        /// by this byte).
        ///
        /// <para>It does NOT affect which material a sub-mesh gets — <c>GetMaterialForKey</c> reads
        /// only the pen and the texture — so this splits buckets without changing how anything
        /// looks. In shipped data only 51 of 4491 meshes carry a real index at all, and only eight
        /// non-combat entities mix values (the doors, a catapult, a gate), so almost nothing splits.
        /// </para>
        /// </remarks>
        public readonly byte RuntimeFlags;

        /// <summary>The opt-out value 0xFF: this mesh is always drawn and never thresholded.</summary>
        public const byte NoRuntimeFlags = 0xFF;

        /// <summary>
        /// Which animation frame of a flagged mesh this sub-mesh is — the MeshFace ordinal.
        /// </summary>
        /// <remarks>
        /// Always 0 for an unflagged mesh, which has exactly one MeshFace. For a flagged one the
        /// runtime picks the frame to show as <c>flagByte % frameCount</c>, so the ordinal is how a
        /// component addresses "the third position of the door".
        /// </remarks>
        public readonly byte Frame;

        public MaterialKey(TerrainPen pen, string textureKey, byte runtimeFlags = NoRuntimeFlags,
            byte frame = 0) {
            Pen = pen;
            TextureKey = textureKey;
            RuntimeFlags = runtimeFlags;
            Frame = frame;
        }

        public bool Equals(MaterialKey other) =>
            Pen == other.Pen && TextureKey == other.TextureKey
            && RuntimeFlags == other.RuntimeFlags && Frame == other.Frame;
        public override bool Equals(object obj) => obj is MaterialKey o && Equals(o);
        public override int GetHashCode() =>
            System.HashCode.Combine(Pen, TextureKey, RuntimeFlags, Frame);
    }

    /// <summary>
    /// Converts TBL polygon entity data into Unity mesh arrays.
    /// Fan-triangulates faces with more than 3 vertices.
    /// Duplicates vertices per face so each face has independent colors and pen regions.
    /// </summary>
    public static class TblMeshConverter {

        /// <summary>
        /// Multi-submesh terrain mesh data. Each sub-mesh corresponds to a
        /// <see cref="MaterialKey"/> and gets its own material.
        /// </summary>
        public struct TerrainMeshData {
            public Vector3[] Vertices;
            public Color[] Colors;
            /// <summary>Per-vertex UVs, parallel with <see cref="Vertices"/>. Only meaningful for
            /// textured slot-bitmap quads; zero for vertex-coloured faces.</summary>
            public Vector2[] Uvs;
            /// <summary>Per-vertex paint-order rank (emit order <b>within the face's coplanar group</b>,
            /// see <see cref="PaintOrderAssigner"/>), parallel with <see cref="Vertices"/>. Written into
            /// UV1 for the paint-order depth bias. All vertices of a face share the face's rank; 0 for
            /// terrain and for faces alone in their plane (no bias). See ClassicPolygon._PaintBias.</summary>
            public float[] PaintOrder;
            /// <summary>Per-vertex overhead-map colour, parallel with <see cref="Vertices"/> and
            /// written into UV2 — what the face is drawn as while the map is up (Z##.DAT's pen
            /// remap, resolved through the zone palette). Equal to <see cref="Colors"/> where the
            /// zone remaps nothing, which is why map mode can be a single global switch.</summary>
            public Vector4[] MapColors;
            /// <summary>Per-submesh triangle index arrays, parallel with <see cref="Keys"/>.</summary>
            public int[][] SubMeshTriangles;
            /// <summary>The material key for each sub-mesh, parallel with <see cref="SubMeshTriangles"/>.</summary>
            public MaterialKey[] Keys;
        }


        /// <summary>
        /// Convert a TBL terrain entity into a multi-submesh mesh, one sub-mesh per
        /// <see cref="TerrainPen"/>. Faces with different pens end up in different sub-meshes
        /// so each can receive its own material. Vertices are shared across all sub-meshes
        /// (duplicated per-face as before for flat shading).
        /// </summary>
        /// <param name="mapPalette">
        /// The palette read through the zone's overhead-map pen remap, baked per face so map mode is
        /// a single global switch. Optional: null bakes each face's own colour, which is what a zone
        /// with no remap does anyway and what the model-viewer and mesh tests want.
        /// </param>
        public static TerrainMeshData ConvertTerrainEntity(TableDatInfo dat, Color[] palette,
            Color[] mapPalette = null,
            bool assignPaintOrder = false) {
            var dupVertices = new List<Vector3>();
            var dupColors = new List<Color>();
            var dupMapColors = new List<Vector4>();
            var dupUvs = new List<Vector2>();
            var dupPaintOrder = new List<float>();
            // Triangles bucketed by material key (pen, or slot-bitmap ref)
            var keyTriangles = new Dictionary<MaterialKey, List<int>>();

            if (dat.Lods.Count == 0) {
                return ToTerrainMeshData(dupVertices, dupColors, dupMapColors, dupUvs,
                    dupPaintOrder, keyTriangles);
            }

            var lod = dat.Lods[0];
            var unityPools = new Vector3[lod.VertexPools.Count][];
            var emitFaces = new List<(PolygonFace Face, Vector3[] Verts, int Pool, byte Flags, byte Frame)>();
            for (int meshIndex = 0; meshIndex < lod.Meshes.Count; meshIndex++) {
                var meshRec = lod.Meshes[meshIndex];
                if (meshRec.MeshFaces.Count == 0 || !(meshRec.MeshFaces[0] is PolygonMeshFace))
                    continue;
                if (meshRec.VertexPoolIndex < 0 || meshRec.VertexPoolIndex >= lod.VertexPools.Count)
                    continue;

                if (unityPools[meshRec.VertexPoolIndex] == null) {
                    var pool = lod.VertexPools[meshRec.VertexPoolIndex];
                    var pv = new Vector3[pool.Count];
                    for (int i = 0; i < pool.Count; i++) {
                        var v = pool[i];
                        // Pools ship pre-scaled (extractor bakes VertexScale),
                        // so this is a pure axis swap + world-scale divide.
                        pv[i] = BakCoordinateConverter.ConvertPosition(v.X, v.Y, v.Z);
                    }
                    unityPools[meshRec.VertexPoolIndex] = pv;
                }
                var meshVerts = unityPools[meshRec.VertexPoolIndex];

                // *** A MESH'S EXTRA MeshFace RECORDS ARE ANIMATION FRAMES. ***
                // renderShapeDispatcher @0x2a7b2 draws face (runtimeFlag % faceCount), so a flagged
                // mesh is a flip-book — the door's is eight quads, one per swing position. Reading
                // MeshFaces[0] and stopping (as this did) keeps frame 0 and silently drops the rest.
                //
                // Emitting them all is safe because the two populations coincide in shipped data:
                // of 4491 meshes, every one of the 4132 that opt out with 0xFF has exactly ONE
                // MeshFace, and all 359 with more than one are flagged. So no plain mesh gains
                // geometry here; only meshes that exist to be frame-selected do.
                for (var frame = 0; frame < meshRec.MeshFaces.Count; frame++) {
                    if (!(meshRec.MeshFaces[frame] is PolygonMeshFace framePoly))
                        continue;
                    foreach (var face in framePoly.Faces)
                        emitFaces.Add((face, meshVerts, meshRec.VertexPoolIndex,
                            meshRec.RuntimeFlagsIndex, (byte)frame));
                }
            }

            // Paint-order pass 1: classify every renderable face into its coplanar group. Geometry is
            // never modified — the rank only feeds the shader's depth-bias tie-break.
            PaintOrderAssigner assigner = null;
            int[] groupIds = null;
            float[] ranks = null;
            if (assignPaintOrder) {
                assigner = new PaintOrderAssigner();
                groupIds = new int[emitFaces.Count];
                ranks = new float[emitFaces.Count];
                for (int i = 0; i < emitFaces.Count; i++) {
                    var (face, verts, pool, _, _) = emitFaces[i];
                    groupIds[i] = -1;
                    if (IsRenderable(face, verts))
                        (groupIds[i], ranks[i]) = assigner.Classify(pool, face.VertexIndices, verts);
                }
            }

            // Pass 2: emit, with the per-face rank.
            for (int i = 0; i < emitFaces.Count; i++) {
                var (face, verts, pool, flags, frame) = emitFaces[i];
                float rank = (assigner != null && groupIds[i] >= 0) ? ranks[i] : 0f;
                AppendFace(face, verts, palette, mapPalette, dupVertices, dupColors,
                    dupMapColors, dupUvs, dupPaintOrder, rank, flags, frame, keyTriangles);
            }

            return ToTerrainMeshData(dupVertices, dupColors, dupMapColors, dupUvs, dupPaintOrder,
                keyTriangles);
        }

        /// <summary>Half-width of a line face's ribbon, in BaK units.</summary>
        /// <remarks>
        /// Calibrated live against the trap cannon: at 0.08 the strut measured 10 screen px in a
        /// 1280-wide game view, where the world viewport spans ~1265 px against VGA's ~294 of 320 —
        /// so one VGA pixel is ~4.3 screen px there, and 0.08 x 4.3/10 gives this.
        /// <para>ponytail: constant WORLD width, so a line thickens as you approach it. The
        /// original's is constant SCREEN width — <c>vga_draw_line</c> paints exactly one 320x200
        /// pixel however far away the object is — which needs the line expanded in clip space by a
        /// shader. That is the upgrade if the thickening ever reads wrong; this constant is only
        /// correct at the distance it was measured at.</para>
        /// </remarks>
        private const float LineHalfWidth = 0.034f;

        /// <summary>A face with exactly two vertices, which is a <b>line</b> and not a degenerate
        /// polygon.</summary>
        /// <remarks>
        /// <c>polygon_draw</c> @0x1ce39 branches on the vertex count as its first act — <c>cmp ax,2</c>,
        /// <c>jg</c> to the polygon fill path, <c>jl</c> to draw nothing, and the <b>== 2 case falls
        /// through</b> to <c>mov bp,1</c> + <c>drawSegmentsFromCoordArrays</c>, i.e. one segment, which
        /// reaches <c>clipLineToViewportAndDraw</c> @0x1a983 and the VGA vtable's <c>vga_draw_line</c>.
        /// So these are first-class geometry with their own arm in the renderer's entry branch, not
        /// something the original also skips.
        ///
        /// <para>61 such faces ship, across 15 objects — <c>catapult</c> alone has 19, <c>corn</c> 5 in
        /// each of six zones, <c>frpcnon</c> 5. Dropping them left the trap cannon's body floating
        /// above a detached base plate with its five struts missing (TASK-327).</para>
        ///
        /// <para><b>Do not discriminate these by flag.</b> Line faces carry <c>Flags 128</c> against the
        /// polygons' <c>129</c>, which looks like a bit-0 test, but <c>Flags=128</c> also appears on 359
        /// three-vertex and 325 four-vertex faces. The vertex count is the only reliable test.</para>
        /// </remarks>
        private static bool IsRenderableLine(PolygonFace face, Vector3[] meshVerts) {
            if (face.VertexIndices.Count != 2) return false;
            foreach (int vi in face.VertexIndices)
                if (vi < 0 || vi >= meshVerts.Length) return false;
            return face.CullMode != FaceCullMode.Skip;
        }

        /// <summary>The single emit predicate for <b>polygons</b>: <see cref="AppendFace"/> early-outs
        /// on it, and the paint-order pass classifies on it, so the two cannot drift apart. Lines go
        /// through <see cref="IsRenderableLine"/> instead and are deliberately kept out of the
        /// classifier — two vertices define no plane, so a line has no coplanar group and takes
        /// rank 0.</summary>
        private static bool IsRenderable(PolygonFace face, Vector3[] meshVerts) {
            if (face.VertexIndices.Count < 3) return false;
            foreach (int vi in face.VertexIndices)
                if (vi < 0 || vi >= meshVerts.Length) return false;
            return face.CullMode != FaceCullMode.Skip;
        }

        /// <summary>
        /// Emit one polygon face into the shared per-pen buckets: rejects non-renderable faces via
        /// <see cref="IsRenderable"/>, applies <see cref="PolygonFace.CullMode"/>, buckets by
        /// <see cref="TerrainPen"/> (textured pens
        /// keep their texture, others fall to FlatFill vertex colour), duplicates vertices per face and
        /// fan-triangulates (both windings when double-sided). Shared by the terrain converter and the
        /// depth-sorted path (which now delegates to <see cref="ConvertTerrainEntity"/>).
        /// </summary>
        private static void AppendFace(
            PolygonFace face, Vector3[] meshVerts, Color[] palette, Color[] mapPalette,
            List<Vector3> dupVertices, List<Color> dupColors, List<Vector4> dupMapColors,
            List<Vector2> dupUvs,
            List<float> dupPaintOrder, float rank, byte runtimeFlags, byte frame,
            Dictionary<MaterialKey, List<int>> keyTriangles) {

            if (IsRenderableLine(face, meshVerts)) {
                AppendLine(face, meshVerts, palette, mapPalette, dupVertices, dupColors,
                    dupMapColors, dupUvs, dupPaintOrder, rank, runtimeFlags, frame, keyTriangles);
                return;
            }
            if (!IsRenderable(face, meshVerts)) return;

            bool doubleSided = face.CullMode == FaceCullMode.DoubleSided;

            byte colorIdx = face.VgaColor;
            Color faceColor = colorIdx < palette.Length ? palette[colorIdx] : Color.magenta;
            // What this face is DRAWN AS on the overhead map. Falls back to its own colour, so a
            // zone with no remap (Z10-Z12) and any caller that does not supply one both render the
            // map exactly as the world — which is what those zones actually do.
            Color mapColor = mapPalette != null && colorIdx < mapPalette.Length
                ? mapPalette[colorIdx]
                : faceColor;
            TerrainPen pen = TerrainPenTextures.PenFromFaceColor(colorIdx);

            // The extractor baked a self-describing slot-bitmap key onto textured quads
            // (Flags&0x10 + 4 verts, resolved against the zone's slot bitmaps). A non-null key ⇒
            // textured face (FlatFill pen so no per-pen texture competes); else vertex-coloured.
            string textureKey = face.TextureBitmap;
            MaterialKey key = textureKey != null
                ? new MaterialKey(TerrainPen.FlatFill, textureKey, runtimeFlags, frame)
                : new MaterialKey(pen, null, runtimeFlags, frame);
            if (!keyTriangles.TryGetValue(key, out var tris))
                keyTriangles[key] = tris = new List<int>();

            bool textured = textureKey != null && face.VertexIndices.Count == 4;
            void PushUvs() {
                if (textured) {
                    // V flipped: BMX sprite texture origin vs quad winding put the bitmap
                    // upside-down; map the 4 quad verts to the bitmap's top edge first.
                    dupUvs.Add(new Vector2(0, 1)); dupUvs.Add(new Vector2(1, 1));
                    dupUvs.Add(new Vector2(1, 0)); dupUvs.Add(new Vector2(0, 0));
                } else {
                    for (int i = 0; i < face.VertexIndices.Count; i++) dupUvs.Add(Vector2.zero);
                }
            }

            int baseIndex = dupVertices.Count;
            for (int i = 0; i < face.VertexIndices.Count; i++) {
                dupVertices.Add(meshVerts[face.VertexIndices[i]]);
                dupColors.Add(faceColor); dupPaintOrder.Add(rank);
                dupMapColors.Add(mapColor);
            }
            PushUvs();
            List<int> local = Triangulate(face.VertexIndices, meshVerts);
            for (int i = 0; i < local.Count; i += 3) {
                tris.Add(baseIndex + local[i]);
                tris.Add(baseIndex + local[i + 1]);
                tris.Add(baseIndex + local[i + 2]);
            }
            if (doubleSided) {
                int backBase = dupVertices.Count;
                for (int i = 0; i < face.VertexIndices.Count; i++) {
                    dupVertices.Add(meshVerts[face.VertexIndices[i]]);
                    dupColors.Add(faceColor); dupPaintOrder.Add(rank);
                    dupMapColors.Add(mapColor);
                }
                PushUvs();
                for (int i = 0; i < local.Count; i += 3) {
                    tris.Add(backBase + local[i]);
                    tris.Add(backBase + local[i + 2]);
                    tris.Add(backBase + local[i + 1]);
                }
            }
        }

        /// <summary>
        /// Vertex-index triples for one face, in face-local indices — a fan when the polygon is
        /// convex, ear clipping when it is not.
        /// </summary>
        /// <remarks>
        /// <b>A fan from vertex 0 is only valid for a CONVEX polygon.</b> On a concave one it emits
        /// triangles that lie OUTSIDE the outline. Measured across generated/TBL: 9418 of 9426 faces
        /// with 4+ vertices are convex and keep the cheap fan; 20 are not, and 8 of those carry real
        /// area — <c>db8</c>, a 5-vertex face present in ALL SIX outdoor zones, and two
        /// <c>catapult</c> quads (TASK-339).
        ///
        /// <para>The spill is invisible TODAY, because on those objects it lands inside the region
        /// their sibling faces already paint. It stops being invisible the moment anything reasons
        /// per-face: a spilled triangle carries a facing that is not its own, so it would cull the
        /// wrong way once backface culling lands
        /// (docs/re-notes/2026-09-06-face-winding-is-consistent.md). Fixing it here is what makes
        /// that change safe.</para>
        ///
        /// <para>Ear clipping works in 2D, on the plane found by dropping the polygon normal's
        /// dominant axis — the projection with the largest area, so a face nearly parallel to one
        /// axis plane does not collapse. Winding is preserved, so the caller's front/back emission
        /// is unaffected.</para>
        /// </remarks>
        private static List<int> Triangulate(List<int> vertexIndices, Vector3[] meshVerts) {
            int n = vertexIndices.Count;
            var result = new List<int>(Mathf.Max(0, (n - 2) * 3));
            if (n < 3) {
                return result;
            }
            Vector3 normal = FaceNormal(vertexIndices, meshVerts);
            if (n == 3 || IsConvex(vertexIndices, meshVerts, normal)) {
                for (int i = 1; i < n - 1; i++) {
                    result.Add(0); result.Add(i); result.Add(i + 1);
                }
                return result;
            }
            EarClip(vertexIndices, meshVerts, normal, result);
            return result;
        }

        /// <summary>Newell's normal — correct for the non-planar faces the data contains.</summary>
        private static Vector3 FaceNormal(List<int> vertexIndices, Vector3[] meshVerts) {
            Vector3 n = Vector3.zero;
            int count = vertexIndices.Count;
            for (int i = 0; i < count; i++) {
                Vector3 a = meshVerts[vertexIndices[i]];
                Vector3 b = meshVerts[vertexIndices[(i + 1) % count]];
                n.x += (a.y - b.y) * (a.z + b.z);
                n.y += (a.z - b.z) * (a.x + b.x);
                n.z += (a.x - b.x) * (a.y + b.y);
            }
            return n;
        }

        private static bool IsConvex(List<int> vertexIndices, Vector3[] meshVerts, Vector3 normal) {
            int count = vertexIndices.Count;
            bool sawPositive = false, sawNegative = false;
            for (int i = 0; i < count; i++) {
                Vector3 a = meshVerts[vertexIndices[i]];
                Vector3 b = meshVerts[vertexIndices[(i + 1) % count]];
                Vector3 c = meshVerts[vertexIndices[(i + 2) % count]];
                float turn = Vector3.Dot(Vector3.Cross(b - a, c - b), normal);
                if (turn > 0f) sawPositive = true;
                else if (turn < 0f) sawNegative = true;
                if (sawPositive && sawNegative) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Project onto the plane that drops the normal's dominant axis.</summary>
        private static Vector2 Flatten(Vector3 v, int dropAxis) =>
            dropAxis == 0 ? new Vector2(v.y, v.z)
            : dropAxis == 1 ? new Vector2(v.z, v.x)
            : new Vector2(v.x, v.y);

        private static void EarClip(List<int> vertexIndices, Vector3[] meshVerts, Vector3 normal,
            List<int> result) {
            int count = vertexIndices.Count;
            Vector3 a3 = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            int dropAxis = a3.x >= a3.y && a3.x >= a3.z ? 0 : (a3.y >= a3.z ? 1 : 2);

            var flat = new Vector2[count];
            for (int i = 0; i < count; i++) {
                flat[i] = Flatten(meshVerts[vertexIndices[i]], dropAxis);
            }
            // Signed area fixes the handedness the projection may have flipped, so "convex corner"
            // below means the same thing whichever axis was dropped.
            float area2 = 0f;
            for (int i = 0; i < count; i++) {
                Vector2 p = flat[i], q = flat[(i + 1) % count];
                area2 += (p.x * q.y) - (q.x * p.y);
            }
            float sign = area2 >= 0f ? 1f : -1f;

            var remaining = new List<int>(count);
            for (int i = 0; i < count; i++) {
                remaining.Add(i);
            }

            int guard = count * count + 8;   // ponytail: O(n^2) is ample for n <= 14 here
            while (remaining.Count > 3 && guard-- > 0) {
                bool clipped = false;
                for (int k = 0; k < remaining.Count; k++) {
                    int i0 = remaining[(k + remaining.Count - 1) % remaining.Count];
                    int i1 = remaining[k];
                    int i2 = remaining[(k + 1) % remaining.Count];
                    if (!IsEar(flat, remaining, i0, i1, i2, sign)) {
                        continue;
                    }
                    result.Add(i0); result.Add(i1); result.Add(i2);
                    remaining.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) {
                    break;   // self-intersecting or degenerate — fall through to the fan below
                }
            }
            if (remaining.Count == 3) {
                result.Add(remaining[0]); result.Add(remaining[1]); result.Add(remaining[2]);
                return;
            }
            // Could not fully triangulate (a bow-tie, say). A fan is wrong but complete, which beats
            // dropping the face entirely.
            result.Clear();
            for (int i = 1; i < count - 1; i++) {
                result.Add(0); result.Add(i); result.Add(i + 1);
            }
        }

        private static bool IsEar(Vector2[] flat, List<int> remaining, int i0, int i1, int i2,
            float sign) {
            Vector2 a = flat[i0], b = flat[i1], c = flat[i2];
            float cross = ((b.x - a.x) * (c.y - b.y)) - ((b.y - a.y) * (c.x - b.x));
            if (cross * sign <= 0f) {
                return false;   // reflex corner, not an ear
            }
            foreach (int idx in remaining) {
                if (idx == i0 || idx == i1 || idx == i2) {
                    continue;
                }
                if (PointInTriangle(flat[idx], a, b, c, sign)) {
                    return false;
                }
            }
            return true;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float sign) {
            float d1 = (((b.x - a.x) * (p.y - a.y)) - ((b.y - a.y) * (p.x - a.x))) * sign;
            float d2 = (((c.x - b.x) * (p.y - b.y)) - ((c.y - b.y) * (p.x - b.x))) * sign;
            float d3 = (((a.x - c.x) * (p.y - c.y)) - ((a.y - c.y) * (p.x - c.x))) * sign;
            return d1 >= 0f && d2 >= 0f && d3 >= 0f;
        }

        /// <summary>Emit a two-vertex line face as a pair of crossed ribbons.</summary>
        /// <remarks>
        /// Reuses the polygon path's buckets wholesale — same pen, same vertex colours, same material
        /// key — so a line needs no new topology, submesh, shader or material. Two quads crossed at
        /// right angles rather than one, because a single ribbon disappears when viewed along its own
        /// normal, and these are struts and stalks seen from every angle as the party walks round them.
        /// Both windings are emitted: a line has no facing to cull against.
        /// <para>Cheap at this scale — 61 line faces in the whole corpus, so ~500 extra vertices
        /// across every zone put together.</para>
        /// </remarks>
        private static void AppendLine(
            PolygonFace face, Vector3[] meshVerts, Color[] palette, Color[] mapPalette,
            List<Vector3> dupVertices, List<Color> dupColors, List<Vector4> dupMapColors,
            List<Vector2> dupUvs,
            List<float> dupPaintOrder, float rank, byte runtimeFlags, byte frame,
            Dictionary<MaterialKey, List<int>> keyTriangles) {

            Vector3 a = meshVerts[face.VertexIndices[0]];
            Vector3 b = meshVerts[face.VertexIndices[1]];
            Vector3 dir = b - a;
            float len = dir.magnitude;
            if (len < 1e-5f) return;              // a zero-length line has no direction to widen along
            dir /= len;

            // Any two perpendiculars to the line. Cross with up first; if the line IS vertical that
            // degenerates, so fall back to forward.
            Vector3 across = Vector3.Cross(dir, Vector3.up);
            if (across.sqrMagnitude < 1e-6f) across = Vector3.Cross(dir, Vector3.forward);
            across = across.normalized * LineHalfWidth;
            Vector3 other = Vector3.Cross(dir, across).normalized * LineHalfWidth;

            byte colorIdx = face.VgaColor;
            Color faceColor = colorIdx < palette.Length ? palette[colorIdx] : Color.magenta;
            Color mapColor = mapPalette != null && colorIdx < mapPalette.Length
                ? mapPalette[colorIdx]
                : faceColor;
            MaterialKey key = new MaterialKey(
                TerrainPenTextures.PenFromFaceColor(colorIdx), null, runtimeFlags, frame);
            if (!keyTriangles.TryGetValue(key, out var tris))
                keyTriangles[key] = tris = new List<int>();

            void Ribbon(Vector3 off) {
                int bi = dupVertices.Count;
                dupVertices.Add(a - off); dupVertices.Add(a + off);
                dupVertices.Add(b + off); dupVertices.Add(b - off);
                for (int i = 0; i < 4; i++) {
                    dupColors.Add(faceColor); dupPaintOrder.Add(rank);
                    dupMapColors.Add(mapColor); dupUvs.Add(Vector2.zero);
                }
                tris.Add(bi); tris.Add(bi + 1); tris.Add(bi + 2);
                tris.Add(bi); tris.Add(bi + 2); tris.Add(bi + 3);
                tris.Add(bi); tris.Add(bi + 2); tris.Add(bi + 1);
                tris.Add(bi); tris.Add(bi + 3); tris.Add(bi + 2);
            }
            Ribbon(across);
            Ribbon(other);
        }

        /// <summary>
        /// Assigns paint-order ranks scoped to <b>coplanar face groups</b>. A face alone in its plane
        /// gets rank 0; faces sharing a plane get 0, 1, 2, … in emit order. The depth-bias tie-break
        /// (ClassicPolygon._PaintBias) therefore only ever separates faces that genuinely tie in depth —
        /// a model-global running rank (the first implementation) accumulated up to
        /// faceCount × _PaintBias of relative bias between <i>distinct</i> surfaces, which at distance
        /// exceeded their real NDC depth gap (∝ 1/z²) and let rear faces bleed through front walls
        /// ("partially transparent" models). Plane matching canonicalizes the normal's sign (dominant
        /// component positive) so opposite-wound decal/base pairs land in the same group.
        /// <para/>
        /// A joining face need only be <b>near</b>-coplanar (within <see cref="GroupDistance"/> of the
        /// plane, normal within <see cref="NormalDotTolerance"/>): TBL decals authored for the painter's
        /// algorithm sit a few BaK units off their base, or tilt through it (bridge-ramp planks ±3–5,
        /// fall1's stream-bed rock 6.5).
        /// <para/>
        /// Geometry is NEVER modified. An earlier version projected joining faces onto the group plane
        /// to make the tie exact, but that destroyed authored separations: fall1's two topmost river
        /// faces are hinged on a shared edge with their free vertex 0.37–0.49 BaK clear of the rock, and
        /// flattening that left the pair to be separated by the rank bias alone — ~10× less margin, and
        /// invisible at travel distance. A real offset resolves itself in the depth buffer (0.04 BaK is
        /// still ~10 float ulps at fog range on the D32F target); rank + _PaintBias then only has to
        /// break genuine exact ties. Not moving vertices also makes junction tears impossible, so the
        /// old shared-vertex clamp is gone with it (removed 2026-07-20).
        /// </summary>
        private sealed class PaintOrderAssigner {
            private struct PlaneGroup { public Vector3 Normal; public float Distance; public int FaceCount; }
            private readonly List<PlaneGroup> _groups = new();
            /// <summary>Same-plane normal tolerance (dot of canonicalized unit normals; ≈ 5.7°).
            /// Keeps perpendicular slivers whose verts skim a plane out of that plane's group.</summary>
            private const float NormalDotTolerance = 0.995f;
            /// <summary>Max distance (Unity units; 10 BaK units) of every face vertex from a group's
            /// plane for the face to share that plane's paint order. Sized from measured data
            /// (church ≤1, bridge planks 4.8, fall1 rock 6.5 BaK); real inter-surface offsets
            /// (double decks, floors) are an order of magnitude larger.</summary>
            private const float GroupDistance = 0.10f;

            /// <summary>
            /// Pass 1: assign the face to its coplanar group (founding one on its own Newell plane if
            /// none matches). Geometry is never modified.
            /// Returns (groupId, rank); groupId −1 for a degenerate face (no plane, no bias).
            /// </summary>
            public (int GroupId, float Rank) Classify(int poolIndex, List<int> indices, Vector3[] verts) {
                // Newell's method: robust polygon normal from the winding, any vertex count.
                Vector3 n = Vector3.zero;
                Vector3 centroid = Vector3.zero;
                int count = indices.Count;
                for (int i = 0; i < count; i++) {
                    Vector3 a = verts[indices[i]];
                    Vector3 b = verts[indices[(i + 1) % count]];
                    n.x += (a.y - b.y) * (a.z + b.z);
                    n.y += (a.z - b.z) * (a.x + b.x);
                    n.z += (a.x - b.x) * (a.y + b.y);
                    centroid += a;
                }
                if (n.sqrMagnitude < 1e-12f) return (-1, 0f); // degenerate: invisible, don't register
                n.Normalize();
                centroid /= count;
                // Canonical sign via the dominant component (magnitude ≥ 1/√3, so float noise
                // can't flip it): opposite windings of the same plane get the same normal.
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                float dominant = ax >= ay ? (ax >= az ? n.x : n.z) : (ay >= az ? n.y : n.z);
                if (dominant < 0f) n = -n;

                int groupId = -1;
                float rank = 0f;
                for (int i = 0; i < _groups.Count; i++) {
                    PlaneGroup g = _groups[i];
                    if (Vector3.Dot(n, g.Normal) < NormalDotTolerance) continue;
                    if (MaxPlaneDistance(indices, verts, g.Normal, g.Distance) > GroupDistance) continue;
                    rank = g.FaceCount;
                    g.FaceCount++;
                    _groups[i] = g;
                    groupId = i;
                    break;
                }
                if (groupId < 0) {
                    _groups.Add(new PlaneGroup {
                        Normal = n, Distance = Vector3.Dot(n, centroid), FaceCount = 1,
                    });
                    groupId = _groups.Count - 1;
                }

                return (groupId, rank);
            }

            private static float MaxPlaneDistance(List<int> indices, Vector3[] verts,
                Vector3 normal, float distance) {
                float maxAbs = 0f;
                foreach (int vi in indices) {
                    float dist = Mathf.Abs(Vector3.Dot(normal, verts[vi]) - distance);
                    if (dist > maxAbs) maxAbs = dist;
                }
                return maxAbs;
            }
        }

        /// <summary>
        /// Convert an EF_DEPTH_SORTED model (half-timbered houses, landscape, props). In the original
        /// (no Z-buffer) these were painted back-to-front by draw ORDER, which is what resolved their
        /// exactly-coplanar timber/plaster/window faces. Here the geometry renders through the shared
        /// per-pen path — identical to <see cref="ConvertTerrainEntity"/> — plus a per-coplanar-group
        /// paint-order rank (see <see cref="PaintOrderAssigner"/>) that the ClassicPolygon shader turns
        /// into a tiny depth-tie-break bias. <paramref name="entityName"/> is retained for signature
        /// stability and future per-model diagnostics.
        /// </summary>
        public static TerrainMeshData ConvertDepthSortedEntity(TableDatInfo dat, Color[] palette,
            string entityName, Color[] mapPalette = null) {
            _ = entityName;
            return ConvertTerrainEntity(dat, palette, mapPalette, assignPaintOrder: true);
        }

        private static TerrainMeshData ToTerrainMeshData(List<Vector3> verts, List<Color> colors,
            List<Vector4> mapColors, List<Vector2> uvs, List<float> paintOrder,
            Dictionary<MaterialKey, List<int>> keyTriangles) {
            var keys = new List<MaterialKey>();
            var subMeshTris = new List<int[]>();
            foreach (var kvp in keyTriangles) {
                if (kvp.Value.Count == 0) continue;
                keys.Add(kvp.Key);
                subMeshTris.Add(kvp.Value.ToArray());
            }
            return new TerrainMeshData {
                Vertices = verts.ToArray(),
                Colors = colors.ToArray(),
                Uvs = uvs.ToArray(),
                PaintOrder = paintOrder.ToArray(),
                MapColors = mapColors.ToArray(),
                SubMeshTriangles = subMeshTris.ToArray(),
                Keys = keys.ToArray(),
            };
        }

        /// <summary>Create a Unity Mesh with sub-meshes from TerrainMeshData.</summary>
        public static Mesh CreateTerrainMesh(TerrainMeshData data, string name = "TerrainMesh") {
            var mesh = new Mesh { name = name };
            if (data.Vertices.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = data.Vertices;
            mesh.colors = data.Colors;
            if (data.Uvs != null) mesh.uv = data.Uvs;
            if (data.PaintOrder != null && data.PaintOrder.Length == data.Vertices.Length) {
                var uv2 = new Vector2[data.PaintOrder.Length];
                for (int i = 0; i < uv2.Length; i++) uv2[i] = new Vector2(data.PaintOrder[i], 0f);
                mesh.uv2 = uv2;
            }
            // UV2 (mesh channel 2 — uv3): the overhead-map colour, as a float4 because a Color does
            // not fit the one per-vertex colour stream a Mesh has and this one must sit beside it,
            // not replace it. The shaders read it only while _MapMode is on.
            if (data.MapColors != null && data.MapColors.Length == data.Vertices.Length) {
                mesh.SetUVs(2, data.MapColors);
            }
            mesh.subMeshCount = data.SubMeshTriangles.Length;
            for (int i = 0; i < data.SubMeshTriangles.Length; i++) {
                mesh.SetTriangles(data.SubMeshTriangles[i], i);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }


        /// <summary>True if any LOD-0 mesh has a renderable PolygonMeshFace with faces.</summary>
        public static bool HasPolygonGeometry(TableDatInfo dat) {
            if (dat.Lods.Count == 0) 
                return false;
            foreach (var meshRec in dat.Lods[0].Meshes) {
                if (meshRec.MeshFaces.Count > 0
                    && meshRec.MeshFaces[0] is PolygonMeshFace poly
                    && poly.Faces.Count > 0) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns the bitmap index of the first <see cref="SpriteBMeshFace"/> found in
        /// LOD 0, or -1 if this entity is not sprite-based. Replaces the old flat
        /// <c>TableDatInfo.Sprite</c> field (removed in the new ZoneTable model).
        /// </summary>
        public static int FindSpriteBitmapIndex(TableDatInfo dat) {
            return FindSpriteBFace(dat)?.BitmapIndex ?? -1;
        }

        /// <summary>
        /// Returns the first <see cref="SpriteBMeshFace"/> in LOD 0, or <c>null</c> if this
        /// entity is not sprite-based. Exposes the billboard's anchor/size metadata
        /// (<see cref="SpriteBMeshFace.AnchorX"/>/<see cref="SpriteBMeshFace.AnchorY"/>/
        /// <see cref="SpriteBMeshFace.SizeScale"/>) decoded from renderSprite2 (0x23031),
        /// so the converter can size and anchor the quad like the original engine instead
        /// of guessing from texture dimensions.
        /// </summary>
        public static SpriteBMeshFace FindSpriteBFace(TableDatInfo dat) {
            if (dat.Lods.Count == 0) return null;
            foreach (var meshRec in dat.Lods[0].Meshes) {
                if (meshRec.MeshFaces.Count > 0 && meshRec.MeshFaces[0] is SpriteBMeshFace spriteB) {
                    return spriteB;
                }
            }
            return null;
        }

    }
}
