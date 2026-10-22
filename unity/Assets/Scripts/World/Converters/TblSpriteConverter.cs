namespace BakAgain.World.Converters {
    using BakAgain.Graphics;
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// Creates billboard quad meshes for TBL sprite entities.
    /// Sprite textures come from the concatenated SLOT BMX array.
    /// </summary>
    public static class TblSpriteConverter {

        /// <summary>
        /// Create a unit billboard quad mesh (1×1, bottom-center pivot at origin).
        /// Legacy fallback for sprites with no decoded anchor metadata.
        /// </summary>
        public static Mesh CreateBillboardQuad() {
            return CreateAnchoredQuad(0.5f, 1f);
        }

        /// <summary>
        /// Build a billboard for a decoded <see cref="SpriteBMeshFace"/>, sized to match the
        /// original engine exactly (renderSprite2 0x23031 + RenderWorldItem 0x2a95a).
        ///
        /// <para>The sprite's larger side IN THE ORIGINAL'S PIXELS is <c>SizeScale / 128</c> of the entity's own
        /// world extent (the texture's pixel dimensions only set the aspect ratio); the quad is
        /// pivoted at the decoded anchor hotspot (<see cref="SpriteBMeshFace.AnchorX"/>,
        /// <see cref="SpriteBMeshFace.AnchorY"/>) so the projected anchor sits at the placement
        /// position.</para>
        /// </summary>
        /// <param name="face">Decoded sprite-B face.</param>
        /// <param name="entityExtentBak">The entity's world extent in BaK units —
        /// <c>TableDatInfo.Extent</c>, which ships pre-shifted by VertexScale (== the engine's
        /// <c>dword_seg024_3803</c>). Converted to Unity units here via the shared world scale.</param>
        /// <param name="texW">Sprite texture width in pixels.</param>
        /// <param name="texH">Sprite texture height in pixels.</param>
        /// <param name="anchorRiseBak">How far above the object's origin the anchor VERTEX sits, in BaK
        /// z units (<see cref="TableDatInfo.SpriteAnchorRise"/>): renderSprite2 hangs the bitmap from a
        /// projected vertex, so a raised one lifts the whole quad. Baked into the mesh rather than the
        /// transform, because a flight sets the transform's position every frame.</param>
        /// <returns>Mesh (anchored unit quad) and the localScale to apply to the GameObject. Its z equals
        /// its x so the turn-covering bounds (<see cref="CoverTurns"/>) are round in the world.</returns>
        public static (Mesh mesh, Vector3 localScale) BuildBillboard(
            SpriteBMeshFace face, int entityExtentBak, int texW, int texH, int anchorRiseBak = 0) {
            // Anchor and size are RE knowledge (the anchor is in the source bitmap's pixels, and the
            // extent fits the larger ORIGINAL-pixel side); the model answers both for the canonical
            // texture, already in the square world (TASK-764). Converting to Unity units is ours.
            (double pivotU, double pivotV) = face.AnchorFraction(texW, texH);
            (double width, double height) = SpriteBMeshFace.BillboardWorldSize(
                SpriteBMeshFace.WorldExtentFor(face.SizeScale, entityExtentBak), texW, texH);

            var mesh = CreateAnchoredQuad((float)pivotU, (float)pivotV,
                height > 0 ? (float)(anchorRiseBak / height) : 0f);
            float worldWidth = (float)(width / BakCoordinateConverter.WorldScale);
            var localScale = new Vector3(worldWidth, (float)(height / BakCoordinateConverter.WorldScale), worldWidth);
            return (mesh, localScale);
        }

        /// <summary>
        /// Unit (1×1) billboard quad whose pivot is at the origin. <paramref name="pivotU"/> is the
        /// horizontal pivot as a fraction from the left edge; <paramref name="pivotV"/> is the
        /// vertical pivot as a fraction from the <b>top</b> edge. (0.5, 1) reproduces the old
        /// bottom-center quad.
        /// </summary>
        private static Mesh CreateAnchoredQuad(float pivotU, float pivotV, float rise = 0f) {
            float left = -pivotU;
            float right = 1f - pivotU;
            float top = pivotV + rise;   // world-up extent above the pivot
            float bottom = -(1f - pivotV) + rise;

            var mesh = new Mesh { name = "BillboardQuad" };
            mesh.vertices = new[] {
                new Vector3(left, bottom, 0f),
                new Vector3(right, bottom, 0f),
                new Vector3(right, top, 0f),
                new Vector3(left, top, 0f)
            };
            mesh.uv = new[] {
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(1, 1), new Vector2(0, 1)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            CoverTurns(mesh);
            return mesh;
        }

        /// <summary>
        /// Gives a billboard the bounds of every way the sprite shader can turn it. SpritePass.hlsl
        /// (BillboardWorldPos) swings the quad about its pivot to face the camera, and to the sun in the
        /// Enhanced shadow pass, so Unity must cull by a column as wide as the quad's longer arm, not by
        /// the quad as built: side-on, that is a sliver round the pivot, and a tree whose pivot had just
        /// left the screen vanished while part of it was still drawn (TASK-943). The column is in mesh
        /// space, so it is round in the world only while the transform's z scale equals its x scale.
        /// The mesh's bounds are therefore NOT the drawn quad: read the vertices for that.
        /// </summary>
        /// <param name="vary">The material's <c>_VarySize</c>: the shader scales the quad about its pivot
        /// by up to 1 +- this.</param>
        public static void CoverTurns(Mesh quad, float vary = 0f) {
            float arm = 0f, bottom = float.MaxValue, top = float.MinValue;
            foreach (Vector3 v in quad.vertices) {
                arm = Mathf.Max(arm, Mathf.Abs(v.x));
                bottom = Mathf.Min(bottom, v.y);
                top = Mathf.Max(top, v.y);
            }
            float lo = 1f - vary, hi = 1f + vary;
            arm *= hi;
            float min = Mathf.Min(bottom * lo, bottom * hi), max = Mathf.Max(top * lo, top * hi);
            quad.bounds = new Bounds(new Vector3(0f, (min + max) / 2f, 0f), new Vector3(2f * arm, max - min, 2f * arm));
        }
    }
}
