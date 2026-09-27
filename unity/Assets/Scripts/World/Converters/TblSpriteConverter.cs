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
        /// <para>The sprite's larger world dimension is <c>SizeScale / 128</c> of the entity's own
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
        /// <returns>Mesh (anchored unit quad) and the localScale to apply to the GameObject.</returns>
        public static (Mesh mesh, Vector3 localScale) BuildBillboard(
            SpriteBMeshFace face, int entityExtentBak, int texW, int texH) {
            // Anchor as a fraction of the bitmap: U from left, V from top (renderSprite2 subtracts
            // anchor*scale from the projected anchor-vertex screen position).
            // AnchorX/AnchorY are in ORIGINAL unscaled bitmap px, but texW/texH are the *canonical*
            // sprite texture dims — the BMX extraction upscales sprites by (VgaScaleX, VgaScaleY) =
            // (5, 6). Scale the anchor by the same factors so the pivot fraction matches the texture;
            // otherwise the hotspot lands ~1/5 (X) / ~1/6 (Y) of the way in and ground sprites sink
            // ~84% into the terrain (verified Z01 2026-06-18: AnchorY 90 of an unscaled-94px tree vs
            // texH 564 gave pivotV 0.16 instead of 0.96).
            float pivotU = texW > 0 ? Mathf.Clamp01((float)face.AnchorX * Canonical.VgaScaleX / texW) : 0.5f;
            float pivotV = texH > 0 ? Mathf.Clamp01((float)face.AnchorY * Canonical.VgaScaleY / texH) : 1f;

            // How big the sprite is in GAME units is RE knowledge (the 128ths fraction and the
            // "0 means 256" rule) and lives on the model. Converting to Unity units is ours: the
            // same BaK->Unity scale as mesh vertices, so billboards and polygon entities share one
            // coordinate space.
            float largerExtent =
                SpriteBMeshFace.WorldExtentFor(face.SizeScale, entityExtentBak)
                / BakCoordinateConverter.WorldScale;

            // Preserve the texture's pixel aspect: larger pixel axis maps to largerExtent.
            int maxDim = Mathf.Max(texW, texH);
            float perPixel = maxDim > 0 ? largerExtent / maxDim : 0f;

            var mesh = CreateAnchoredQuad(pivotU, pivotV);
            var localScale = new Vector3(texW * perPixel, texH * perPixel, 1f);
            return (mesh, localScale);
        }

        /// <summary>
        /// Unit (1×1) billboard quad whose pivot is at the origin. <paramref name="pivotU"/> is the
        /// horizontal pivot as a fraction from the left edge; <paramref name="pivotV"/> is the
        /// vertical pivot as a fraction from the <b>top</b> edge. (0.5, 1) reproduces the old
        /// bottom-center quad.
        /// </summary>
        private static Mesh CreateAnchoredQuad(float pivotU, float pivotV) {
            float left = -pivotU;
            float right = 1f - pivotU;
            float top = pivotV;          // world-up extent above the pivot
            float bottom = -(1f - pivotV);

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
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
