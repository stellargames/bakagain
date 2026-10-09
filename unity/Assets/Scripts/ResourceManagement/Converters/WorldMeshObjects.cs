namespace BakAgain.ResourceManagement.Converters {
    using BakAgain.World;
    using BakAgain.World.Converters;
    using BakAgain.World.Rendering;
    using UnityEngine;
    using UnityEngine.Rendering;

    /// <summary>
    /// Shared factory helper for building per-pen textured polygon GameObjects (terrain and the
    /// depth-sorted models — houses/landscape — which are prepared as per-pen meshes upstream).
    /// Extracted from <c>WorldEntityBuilder</c> so both the world builder (instance placement)
    /// and <see cref="BakAgain.ResourceManagement.Converters.TblModelConverter"/> (disabled
    /// template creation) share one code path. Call sites set position/rotation on the returned
    /// object; these helpers always leave the object at the origin (template convention).
    /// </summary>
    public static class WorldMeshObjects {
        private static readonly int OffsetFactorId = Shader.PropertyToID("_OffsetFactor");
        private static readonly int OffsetUnitsId = Shader.PropertyToID("_OffsetUnits");

        /// <summary>
        /// Builds a per-pen layered terrain/polygon GameObject with one sub-mesh per pen, each
        /// assigned a render-queue-adjusted + depth-offset material variant. When
        /// <paramref name="parent"/> is null the object is left unparented (template use-case).
        /// Position and rotation are set to identity; call-sites that place instances must set
        /// them after this call returns.
        /// </summary>
        public static GameObject BuildTerrain(string name, Mesh mesh,
            MaterialKey[] keys, byte drawPriority, WorldEntityRenderContext ctx,
            Transform parent = null) {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            // Build material array parallel with sub-mesh indices. A slot-textured key gets a
            // per-bitmap textured material (texture preloaded into ctx.SlotTextures by
            // WorldEntityRenderContext.EnsureSlotTexturesAsync); a plain pen key gets the layered
            // per-pen material at this entity's DrawPriority layer.
            // *** ONLY ONE FRAME OF A FLIP-BOOK MESH IS DRAWN AT A TIME. ***
            // A flagged mesh's extra MeshFaces are animation positions, not extra geometry — the
            // original draws face (runtimeFlag % frameCount), so a door shows ONE of its eight
            // panels. They all reach the mesh as sub-meshes so WorldMeshFrames can pick between
            // them; leaving every one lit would stack all eight on top of each other.
            var matArray = new Material[keys.Length];
            var frameOfSubMesh = new int[keys.Length];
            var animated = false;
            for (int i = 0; i < keys.Length; i++) {
                matArray[i] = GetMaterialForKey(keys[i], drawPriority, ctx);
                // An unflagged mesh is drawn unconditionally in the original, so it must not blink
                // while a neighbouring panel moves.
                frameOfSubMesh[i] = keys[i].RuntimeFlags == MaterialKey.NoRuntimeFlags
                    ? WorldMeshFrames.AlwaysLit
                    : keys[i].Frame;
                animated |= keys[i].Frame > 0;
            }

            if (animated) {
                var frames = go.AddComponent<WorldMeshFrames>();
                frames.Initialise(matArray, frameOfSubMesh);
                frames.SetFrame(0);   // the resting position, until something animates it
            } else {
                renderer.sharedMaterials = matArray;
            }
            return go;
        }

        /// <summary>Resolve the material for one sub-mesh key: textured slot-bitmap quad → a slot
        /// material built from the preloaded texture; otherwise the layered per-pen terrain material.</summary>
        private static Material GetMaterialForKey(MaterialKey key, byte drawPriority,
            WorldEntityRenderContext ctx) {
            if (key.TextureKey is { } k) {
                // Cache-miss fallback. Relies on the invariant that the SAME Keys array is passed to
                // EnsureSlotTexturesAsync (which preloads every TextureKey into SlotTextures) and to
                // this material assignment — so for current callers this branch is unreachable; it's a
                // defensive fallback should a key ever reach here unloaded.
                if (!ctx.SlotTextures.TryGetValue(k, out var tex) || tex == null)
                    return GetLayeredTerrainMaterial(key.Pen, drawPriority, ctx);
                return ctx.GetSlotMaterial(tex);
            }
            return GetLayeredTerrainMaterial(key.Pen, drawPriority, ctx);
        }

        /// <summary>
        /// Paint layer from DrawPriority, lowest paints first. The original documents the byte as
        /// "8=ground/base, 7=road/path, 6=river, 0=non-terrain object; higher = painted first/under",
        /// so the order is 8 → 7 → 6 → 0. Only 0/6/7/8 occur in shipped data (2606/77/256/62
        /// entities across all zones).
        ///
        /// The previous formula, <c>(dp >= 6 &amp;&amp; dp &lt;= 8) ? (8 - dp) : 0</c>, collapsed everything
        /// outside 6..8 onto layer 0 — the SAME layer as ground, with no depth bias — so DrawPriority-0
        /// geometry coplanar with the terrain under it had nothing breaking the tie. That is the
        /// waterfall base (fall1 shares exact planes at Y=0/12/24 with the zero*/one* landscape
        /// blocks it sits among); fixed and confirmed in-game 2026-07-20.
        ///
        /// KNOWN RISK, not yet observed: this also moves DrawPriority-0 landscape blocks ABOVE
        /// rivers (layer 3 vs 2), inverting their previous order. If a river tile is ever coplanar
        /// with a landscape block, the block will now win and the river will disappear. Watch for
        /// missing/z-fighting rivers and roads; if that shows up, the layering needs to distinguish
        /// landscape relief (EntityType 4, depth-sorted) from objects standing on terrain.
        /// </summary>
        private const int ObjectLayer = 3;

        private static int PaintLayer(byte drawPriority) {
            switch (drawPriority) {
                case 8: return 0;   // ground / base
                case 7: return 1;   // road, path, field
                case 6: return 2;   // river
                default: return ObjectLayer;  // 0 = non-terrain object / landscape relief
            }
        }

        /// <summary>
        /// Cached material variant for (pen, DrawPriority): the base per-pen material re-queued and
        /// depth-offset so coplanar surfaces paint in the original's painter's order without
        /// Z-fighting. See <see cref="PaintLayer"/> for the ordering.
        ///
        /// Both offset terms matter: <c>_OffsetUnits</c> is a constant bias, <c>_OffsetFactor</c>
        /// scales with the polygon's depth slope and is what separates large near-coplanar planes at
        /// grazing angles (a river sheet over ground). Dropping the factor term made rivers z-fight
        /// the ground immediately — verified 2026-07-20, do not remove it. Objects are the
        /// exception (see below).
        /// </summary>
        public static Material GetLayeredTerrainMaterial(TerrainPen pen, byte drawPriority,
            WorldEntityRenderContext ctx) {
            var key = (pen, drawPriority);
            if (ctx.LayeredTerrainCache.TryGetValue(key, out var mat)) return mat;
            if (!ctx.TerrainMaterials.TryGetValue(pen, out var baseMat))
                ctx.TerrainMaterials.TryGetValue(TerrainPen.FlatFill, out baseMat);
            mat = new Material(baseMat);
            int layer = PaintLayer(drawPriority);
            mat.renderQueue = (int)RenderQueue.Geometry + layer;
            if (layer > 0) { // overlays bias toward camera to win the coplanar depth test
                mat.SetFloat(OffsetUnitsId, -layer);
                // The slope-scaled term only for the terrain overlays (road/river sheets lying on
                // ground). It is per POLYGON: on an object a wall seen edge-on has a huge depth slope
                // and was pulled far past the roof above it — the gable pierced the house roof along
                // its top edge (2026-10-09). Objects keep the constant term, which moves a whole
                // model alike and still orders DrawPriority 0 over the terrain layers.
                mat.SetFloat(OffsetFactorId, layer == ObjectLayer ? 0f : -layer);
            }
            ctx.LayeredTerrainCache[key] = mat;
            return mat;
        }
    }
}
