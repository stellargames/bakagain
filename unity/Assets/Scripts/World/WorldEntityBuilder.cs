namespace BakAgain.World {
    using BakAgain.ResourceManagement;
    using BakAgain.ResourceManagement.Converters;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.ResourceManagement.Models;
    using BakAgain.World.Converters;
    using BakAgain.World.Rendering;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Config;
    using GameData.Resources.Image;
    using GameData.Resources.Palette;
    using GameData.Resources.World;
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Rendering;
    using Color = UnityEngine.Color;

    /// <summary>
    /// Shared per-zone rendering inputs (palette, materials, fog, sprites) consumed by
    /// <see cref="WorldEntityBuilder"/>. Assembling these once and reusing them keeps the
    /// in-game zone renderer (<see cref="ZoneSceneBuilder"/>) and the model debug viewer
    /// (<c>ModelDebugState</c>) on a single rendering path instead of two divergent ones.
    /// </summary>
    public sealed class WorldEntityRenderContext : System.IDisposable {
        public Color[] Palette;

        // The same palette read through Z##.DAT's pen remap: MapPalette[p] is the colour pen p is
        // DRAWN AS while the overhead map is up. Baked per face into the mesh so the map's flatter
        // colours cost one global uniform to switch on and nothing at all while travelling.
        // Null (and identical to Palette for the three zones that ship an empty table) means the
        // map simply looks like the world, which is what those zones do.
        public Color[] MapPalette;
        public IWorldRenderProfile Profile;
        public Dictionary<TerrainPen, Material> TerrainMaterials;

        // Per-(pen, DrawPriority) painter's-order material cache (DrawPriority 8=ground/bottom,
        // 7=road/path, 6=river/top). See docs/superpowers/specs/2026-06-29-world-render-draw-order-design.md.
        public readonly Dictionary<(TerrainPen, byte), Material> LayeredTerrainCache = new();

        // The provider used to load sprite billboard images (index-0 transparent). Set alongside
        // SlotLoader. Null for the debug viewer (which only lists polygon-geometry models — never
        // sprites) — LoadSpriteTextureAsync null-guards it.
        public IResourceProviderService ResourceProvider;

        // DETECT.DAT — data-driven interactability range table (Task 6 reads it). Null for the
        // debug viewer, which doesn't need interaction colliders.
        public DetectData Detect;

        // Whether the zone is underground, from Z##DEF.DAT's first field (ZoneDefinition.IsUnderground).
        // Selects which block of DETECT.DAT's interaction ranges entities are stamped with.
        public bool Underground;

        // --- Slot-bitmap texturing (World-Model Slot-Bitmap Texturing) ---
        // The loader that resolves a baked resource key → Texture2D, and the shared cache of
        // already-loaded slot textures (keyed by the same resource key baked onto each face by the
        // extractor). Populated once per zone by the ctx-construction site (ZoneSceneBuilder /
        // ModelDebugState). All null/empty for callers that don't texture (SlotLoader null ⇒ no
        // slots load ⇒ pure vertex-coloured behaviour, byte-identical to before).
        public SlotBitmapTextureLoader SlotLoader;
        public readonly Dictionary<string, Texture2D> SlotTextures = new();

        // Per-key billboard-sprite texture cache, mirroring SlotTextures: many placements share one
        // billboard bitmap, so one Texture2D is built per distinct key per zone and reused (the old
        // LoadSlotSpritesAsync built ~one per slot image per zone and shared them; loading per-key
        // per-placement instead would allocate N textures for N placements).
        private readonly Dictionary<string, Texture2D> _spriteTextures = new();

        // --- Ownership -----------------------------------------------------------------------
        // Unity never reclaims a Material/Mesh/Texture2D without an explicit Destroy: destroying the
        // zone root frees the GameObjects but NOT the assets they reference through sharedMesh /
        // sharedMaterials. Everything this context CREATES is tracked below and destroyed in
        // Dispose (wired to ZoneTeardown). SlotTextures are deliberately absent: those are
        // sprite.texture handles borrowed from the shared IResourceCache, and destroying one would
        // corrupt the cache for every later consumer.
        private readonly Dictionary<Texture2D, Material> _slotMaterials = new();
        private readonly Dictionary<Texture2D, Material> _spriteMaterials = new();
        private readonly List<Mesh> _trackedMeshes = new();

        /// <summary>One slot material per distinct bitmap, not per sub-mesh.</summary>
        public Material GetSlotMaterial(Texture2D bitmap) {
            if (_slotMaterials.TryGetValue(bitmap, out var mat) && mat != null) return mat;
            mat = Profile.CreateSlotMaterial(bitmap);
            _slotMaterials[bitmap] = mat;
            return mat;
        }

        /// <summary>One sprite material per distinct billboard texture, not per placement.</summary>
        public Material GetSpriteMaterial(Texture2D spriteTexture) {
            if (_spriteMaterials.TryGetValue(spriteTexture, out var mat) && mat != null) return mat;
            mat = Profile.CreateSpriteMaterial(spriteTexture);
            _spriteMaterials[spriteTexture] = mat;
            return mat;
        }

        /// <summary>Register a runtime-built mesh so the zone teardown can free it.</summary>
        public void TrackMesh(Mesh mesh) {
            if (mesh != null) _trackedMeshes.Add(mesh);
        }

        /// <summary>Destroy every asset this context created. Idempotent.</summary>
        public void Dispose() {
            if (TerrainMaterials != null) {
                foreach (var m in TerrainMaterials.Values) DestroyAsset(m);
                TerrainMaterials.Clear();
            }
            foreach (var m in LayeredTerrainCache.Values) DestroyAsset(m);
            foreach (var m in _slotMaterials.Values) DestroyAsset(m);
            foreach (var m in _spriteMaterials.Values) DestroyAsset(m);
            foreach (var t in _spriteTextures.Values) DestroyAsset(t);
            foreach (var m in _trackedMeshes) DestroyAsset(m);

            LayeredTerrainCache.Clear();
            _slotMaterials.Clear();
            _spriteMaterials.Clear();
            _spriteTextures.Clear();
            _trackedMeshes.Clear();
            SlotTextures.Clear();   // borrowed handles: drop the references, don't destroy
        }

        private static void DestroyAsset(UnityEngine.Object o) {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        /// <summary>
        /// Ensure every distinct texture key referenced by the just-converted mesh's
        /// <paramref name="keys"/> has its texture loaded into <see cref="SlotTextures"/>. Called
        /// after the (sync) mesh conversion and before the (sync) material assignment — this is where
        /// the async texture load fits between the two sync phases. No-op when no loader is wired.
        /// </summary>
        public async UniTask EnsureSlotTexturesAsync(MaterialKey[] keys) {
            if (SlotLoader == null || keys == null) return;
            foreach (var key in keys) {
                if (key.TextureKey is { } k && !SlotTextures.ContainsKey(k)) {
                    SlotTextures[k] = await SlotLoader.LoadAsync(k);
                }
            }
        }

        /// <summary>Load a billboard sprite texture from its baked "Z##SLOT#.BMX#i" key, honouring
        /// index-0 transparency (matches the old LoadSlotSpritesAsync path). Returns null if the key
        /// is null/empty or the image can't be loaded. Deduped per key via <see cref="_spriteTextures"/>.
        /// Note the intentional divergence from the opaque polygon path (which resolves through
        /// IResourceCache.GetOrLoadAsync&lt;Sprite&gt;(key)): sprites go via
        /// ResourceProvider.LoadAssetAsync&lt;ImageSet&gt; + a manual '#' index parse +
        /// ToTexture2D(transparentIndex0: true) because a billboard needs index-0 transparency and an
        /// owner-scoped resource handle — deliberate, not an oversight.</summary>
        /// <summary>
        /// Builds (and caches) the texture for one billboard frame.
        /// </summary>
        /// <param name="resourceKey">
        /// <c>&lt;stem&gt;#&lt;frame&gt;</c>, where the stem may carry a creature colour set as
        /// <c>_CS&lt;n&gt;</c> — <c>MOR1_CS2#7</c>. The suffix is not part of any filename; the
        /// original loads <c>CS&lt;n&gt;.DAT</c> separately.
        /// </param>
        /// <remarks>
        /// <b>The recolour is done to the PALETTE, not to the image.</b> The original remaps the
        /// loaded image's indices and then colours them normally; drawing pixel <c>p</c> as
        /// <c>Palette[lut[p]]</c> is the same picture, and it leaves the <see cref="ImageSet"/>
        /// alone — which matters because that set comes from the shared resource cache, so
        /// <c>CreatureColorSet.Apply</c>'s in-place edit would recolour it for every later consumer
        /// too. Index 0 survives as transparent because every shipped table maps 0 to 0.
        ///
        /// <para><b>The cache key is the whole resource key, colour set included</b>, so a mordel
        /// and its recoloured twin do not share one texture.</para>
        /// </remarks>
        /// <summary>
        /// The zone's remap blocks, ordered — <c>Z##.RMP</c>, the same list the fog ramp is built
        /// from.
        /// </summary>
        /// <remarks>
        /// <b>It is the fade ramp AND the hit flash.</b> The original selects a block with
        /// <c>(index &lt;&lt; 8) + 0xA66</c> and uses it for both: distance for the world, and
        /// <c>spriteHitDir</c> for a struck combatant. So a recoil is a creature briefly redrawn one
        /// rung further into its own zone's fog — see <c>HitReaction</c>.
        /// </remarks>
        public IReadOnlyList<Dictionary<byte, byte>> FadeRamp;

        public async UniTask<Texture2D> LoadSpriteTextureAsync(string resourceKey, GameObject owner,
            int fadeRung = 0) {
            if (string.IsNullOrEmpty(resourceKey) || ResourceProvider == null) return null;
            // A faded variant is a DIFFERENT TEXTURE, which is why no shader work and no property
            // block are needed: GetSpriteMaterial already caches one material per texture, so the
            // tinted sprite gets its own for free.
            string cacheKey = fadeRung > 0 ? $"{resourceKey}~{fadeRung}" : resourceKey;
            if (_spriteTextures.TryGetValue(cacheKey, out var cached) && cached != null) return cached;
            int hash = resourceKey.IndexOf('#');
            if (hash < 0 || !int.TryParse(resourceKey.Substring(hash + 1), out int localIndex)) return null;
            (string stem, int colorSet) =
                ResourceExtraction.Imaging.CreatureColorSet.ParseVariantKey(resourceKey.Substring(0, hash));
            string setName = stem.EndsWith(".BMX", StringComparison.OrdinalIgnoreCase) ? stem : stem + ".BMX";
            var set = await ResourceProvider.LoadAssetAsync<ImageSet>(setName, owner);
            if (set?.Images == null || localIndex < 0 || localIndex >= set.Images.Count) return null;
            var img = set.Images[localIndex];
            if (img.BitMapData == null) return null;
            Color[] palette = Faded(await RemappedPaletteAsync(colorSet, owner), fadeRung);
            var tex = img.ToTexture2D(palette, transparentIndex0: true);
            _spriteTextures[cacheKey] = tex;
            return tex;
        }

        /// <summary>The palette seen through fade rung <paramref name="rung"/> of the zone's RMP.</summary>
        /// <remarks>
        /// <b>An index-to-index substitution, not a colour transform.</b> The block says which pen
        /// each pen becomes; everything it does not name keeps its own colour, which is why the low
        /// rungs barely change anything (30 entries in Z01's block 0) and the high ones remap almost
        /// the whole palette (141).
        /// </remarks>
        private Color[] Faded(Color[] palette, int rung) {
            if (rung <= 0 || palette == null || FadeRamp == null || rung >= FadeRamp.Count) {
                return palette;
            }
            Dictionary<byte, byte> block = FadeRamp[rung];
            if (block == null || block.Count == 0) {
                return palette;
            }
            var faded = new Color[palette.Length];
            for (int i = 0; i < faded.Length; i++) {
                faded[i] = block.TryGetValue((byte)i, out byte to) && to < palette.Length
                    ? palette[to]
                    : palette[i];
            }
            return faded;
        }

        // Palettes already permuted for a colour set, so a fight full of mordels builds one.
        private readonly Dictionary<int, Color[]> _remappedPalettes = new();

        /// <summary>The zone palette, permuted through CS&lt;n&gt;.DAT — or the zone palette itself.</summary>
        private async UniTask<Color[]> RemappedPaletteAsync(int colorSet, GameObject owner) {
            if (colorSet < 0 || Palette == null || ResourceProvider == null) {
                return Palette;
            }
            if (_remappedPalettes.TryGetValue(colorSet, out Color[] hit)) {
                return hit;
            }
            var table = await ResourceProvider.LoadAssetAsync<ColorRemapTable>($"CS{colorSet}.DAT", owner);
            if (table?.Lut == null || table.Lut.Length != ColorRemapTable.Entries) {
                // Better the zone's colours than none: the creature is still recognisable, where a
                // null palette draws nothing at all.
                _remappedPalettes[colorSet] = Palette;
                return Palette;
            }
            var remapped = new Color[Palette.Length];
            for (int i = 0; i < remapped.Length; i++) {
                byte to = table.Lut[i];
                remapped[i] = to < Palette.Length ? Palette[to] : Palette[i];
            }
            _remappedPalettes[colorSet] = remapped;
            return remapped;
        }

        /// <summary>
        /// Assemble the shared materials from a render profile + already-loaded fog. Both the zone
        /// builder and the debug viewer call this so terrain/polygon materials (and the pen textures
        /// behind them) are created identically.
        /// </summary>
        public static WorldEntityRenderContext Create(
            Color[] palette, IWorldRenderProfile profile,
            DetectData detect = null, bool underground = false, int zone = 1) {
            var penTextures = TerrainPenTextures.LoadAll(zone);
            return new WorldEntityRenderContext {
                Palette = palette,
                Profile = profile,
                TerrainMaterials = profile.CreateTerrainMaterials(penTextures),
                Detect = detect,
                Underground = underground,
            };
        }
    }

    /// <summary>
    /// Builds the Unity GameObject for a single world entity using the game's exact
    /// classification → conversion → material rules. This is the single owner of "how a world
    /// entity is rendered": <see cref="ZoneSceneBuilder"/> calls it for every zone item and the
    /// model debug viewer calls it for the one model on screen, so the viewer always shows
    /// exactly what the game shows.
    /// </summary>
    public static class WorldEntityBuilder {
        /// <summary>
        /// Build one entity's GameObject. Terrain goes under <paramref name="terrainParent"/>,
        /// everything else under <paramref name="entityParent"/> (pass the same transform for both
        /// when the split doesn't matter, e.g. the debug viewer). Returns null for non-renderable
        /// entities (empty, or a sprite whose bitmap isn't loaded).
        /// </summary>
        public static async UniTask<GameObject> Build(ZoneTableEntry entry, int typeId,
            Vector3 position, Quaternion rotation,
            Transform terrainParent, Transform entityParent,
            WorldEntityRenderContext ctx, WorldModelLoader loader, int rotationZ = 0) {
            var dat = entry.Dat;
            bool hasPolygons = TblMeshConverter.HasPolygonGeometry(dat);
            int spriteIdx = TblMeshConverter.FindSpriteBitmapIndex(dat);
            var spriteFace = TblMeshConverter.FindSpriteBFace(dat);
            bool hasSprite = spriteFace != null && !string.IsNullOrEmpty(spriteFace.TextureBitmap);

            WorldEntityKind kind = WorldEntityClassifier.Classify(
                dat.IsUnbounded, dat.IsDepthSorted, dat.EntityType, hasPolygons, hasSprite);

            switch (kind) {
                case WorldEntityKind.Terrain: {
                    var tmd = TblMeshConverter.ConvertTerrainEntity(dat, ctx.Palette, ctx.MapPalette);
                    var mesh = TblMeshConverter.CreateTerrainMesh(tmd, entry.Name);
                    ctx.TrackMesh(mesh);
                    await ctx.EnsureSlotTexturesAsync(tmd.Keys);
                    var go = WorldMeshObjects.BuildTerrain(entry.Name, mesh, tmd.Keys, dat.DrawPriority, ctx, terrainParent);
                    go.transform.localPosition = position;
                    go.transform.localRotation = rotation;
                    AttachWorldEntity(go, typeId, entry, false, spriteIdx, rotationZ);
                    return go;
                }
                case WorldEntityKind.Sprite when spriteFace != null: {
                    // Sprite billboard — textured from the baked "Z##SLOT#.BMX#i" key, sized/anchored
                    // from the decoded SpriteBMeshFace.
                    var spriteTex = await ctx.LoadSpriteTextureAsync(spriteFace.TextureBitmap, entityParent.gameObject);
                    if (spriteTex == null) return null;   // unavailable bitmap ⇒ non-renderable (as before)
                    // Entity world extent (== engine dword_3803) drives the billboard size; see
                    // TblSpriteConverter.BuildBillboard. Extent ships pre-shifted by VertexScale.
                    int entityExtentBak = Mathf.Abs(dat.Extent);
                    var spriteMat = ctx.GetSpriteMaterial(spriteTex);
                    var go = CreateSpriteObject(entry.Name, spriteTex, spriteFace,
                        entityExtentBak, spriteMat, entityParent, position);
                    ctx.TrackMesh(go.GetComponent<MeshFilter>()?.sharedMesh);
                    AttachWorldEntity(go, typeId, entry, true, spriteIdx, rotationZ);
                    AddPickColliderIfInteractable(go, entry.Dat.EntityType, ctx.Detect, ctx.Underground);
                    StampDetectionRange(go.GetComponent<WorldEntity>(), entry.Dat.EntityType, ctx.Detect, ctx.Underground);
                    return go;
                }
                case WorldEntityKind.PolygonEntity: {
                    var model = await loader.GetAsync(typeId);
                    var go = UnityEngine.Object.Instantiate(model.Visual, entityParent);
                    go.name = entry.Name;
                    go.SetActive(true);
                    // The template is built asynchronously and handed out from a cache, so a clone
                    // can be taken while it is still being filled in — half of zone 10's doors came
                    // up with an empty frame map that way. Copy it here, where the template is
                    // certainly finished.
                    go.GetComponent<WorldMeshFrames>()
                        ?.CopyFrom(model.Visual.GetComponent<WorldMeshFrames>());
                    go.transform.localPosition = position;
                    go.transform.localRotation = rotation;
                    AttachWorldEntity(go, model.Metadata, false, spriteIdx, rotationZ);
                    var we = go.GetComponent<WorldEntity>();
                    we.Behavior = entry.Behavior;
                    we.Interaction = entry.Interaction;
                    AddPickColliderIfInteractable(go, entry.Dat.EntityType, ctx.Detect, ctx.Underground);
                    StampDetectionRange(we, entry.Dat.EntityType, ctx.Detect, ctx.Underground);
                    return go;
                }
                default:
                    // Sprite with unavailable bitmap and empty entities (encounter markers, etc.).
                    return null;
            }
        }

        private static GameObject CreateSpriteObject(string name, Texture2D spriteTex,
            SpriteBMeshFace spriteFace, int entityExtentBak, Material material, Transform parent, Vector3 position) {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.localPosition = position;

            // Size from the decoded SizeScale (texture dims only set aspect) and pivot at the
            // decoded anchor hotspot — matches renderSprite2 (0x23031). Falls back to a
            // bottom-center, texture-sized quad when no SpriteBMeshFace is available.
            Mesh quad;
            if (spriteFace != null) {
                var billboard = TblSpriteConverter.BuildBillboard(
                    spriteFace, entityExtentBak, spriteTex.width, spriteTex.height);
                quad = billboard.mesh;
                go.transform.localScale = billboard.localScale;
            } else {
                quad = TblSpriteConverter.CreateBillboardQuad();
                go.transform.localScale = new Vector3(spriteTex.width / 44f, spriteTex.height / 44f, 1f);
            }

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            go.AddComponent<BillboardSprite>();

            return go;
        }

        /// <summary>Add a WorldInteractable pick collider (sized from the object's mesh bounds) ONLY
        /// when DETECT.DAT marks this entity type interactable in this location — the Unity-standard,
        /// data-driven pick decision. Decorative types get no collider.</summary>
        public static void AddPickColliderIfInteractable(GameObject go, WorldEntityType type,
            DetectData detect, bool underground) {
            if (detect == null || !detect.IsInteractable(type, underground)) {
                return;
            }
            Bounds b = ComputeLocalBounds(go);
            AddInteractionCollider(go, b.center, b.size);
        }

        /// <summary>Copy the DETECT.DAT detection range for this type+location onto the entity, so the
        /// pick path can gate the click on it.</summary>
        // The table lookup (which location block, bounds on both the block and the type index) is
        // DetectData's, and IsInteractable — used by AddPickColliderIfInteractable right above —
        // goes through the same accessor. This used to re-derive it, so the two could disagree
        // about what counts as in range.
        public static void StampDetectionRange(WorldEntity entity, WorldEntityType type, DetectData detect, bool underground) {
            if (entity == null || detect == null) {
                return;
            }
            entity.DetectionRange = detect.GetRange(type, underground);
        }

        private static Bounds ComputeLocalBounds(GameObject go) {
            // Union of the object's mesh bounds in local space (billboard quad for sprites, the model
            // mesh for 3D). Falls back to a unit box if no mesh is found.
            var filters = go.GetComponentsInChildren<MeshFilter>();
            Bounds b = new Bounds(Vector3.zero, Vector3.one);
            bool seeded = false;
            foreach (var f in filters) {
                if (f.sharedMesh == null) { continue; }
                if (!seeded) { b = f.sharedMesh.bounds; seeded = true; }
                else { b.Encapsulate(f.sharedMesh.bounds); }
            }
            return b;
        }

        /// <summary>
        /// Adds the picking collider + WorldInteractable layer to an interactive world entity.
        /// center/size are the local-space bounds of the object's mesh.
        /// </summary>
        public static void AddInteractionCollider(GameObject go, Vector3 center, Vector3 size) {
            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            box.isTrigger = false;
            int layer = LayerMask.NameToLayer(WorldInteractionLayers.WorldInteractableLayerName);
            if (layer >= 0) {
                go.layer = layer;
            }
        }

        private static void AttachWorldEntity(GameObject go, int typeId,
            ZoneTableEntry entry, bool isSprite, int spriteIndex, int rotationZ) {
            var entity = go.AddComponent<WorldEntity>();
            entity.TypeId = typeId;
            entity.EntityName = entry.Name;
            entity.EntityType = entry.Dat.EntityType;
            entity.IsUnbounded = entry.Dat.IsUnbounded;
            entity.IsDepthSorted = entry.Dat.IsDepthSorted;
            entity.DrawPriority = entry.Dat.DrawPriority;
            entity.IsSprite = isSprite;
            entity.SpriteIndex = spriteIndex;
            entity.Behavior = entry.Behavior;
            entity.Interaction = entry.Interaction;
            entity.RotationZ = rotationZ;
            // Extent is stored PRE-SHIFTED (ZoneTable.Extent, 2026-07-20), so it is already the
            // original's radius << shift. Shifting by VertexScale here would double it and find
            // things from twice the distance.
            entity.WorldExtent = entry.Dat.Extent;
        }

        private static void AttachWorldEntity(GameObject go, WorldModelMetadata meta, bool isSprite,
            int spriteIndex, int rotationZ) {
            var entity = go.AddComponent<WorldEntity>();
            entity.TypeId = meta.TypeId;
            entity.EntityName = meta.Name;
            entity.EntityType = meta.EntityType;
            entity.IsUnbounded = meta.IsUnbounded;
            entity.IsDepthSorted = meta.IsDepthSorted;
            entity.DrawPriority = meta.DrawPriority;
            entity.IsSprite = isSprite;
            entity.SpriteIndex = spriteIndex;
            entity.RotationZ = rotationZ;
        }
    }
}
