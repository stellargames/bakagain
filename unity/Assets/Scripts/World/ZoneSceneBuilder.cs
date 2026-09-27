namespace BakAgain.World {
    using BakAgain.CutScenes;
    using BakAgain.ResourceManagement;
    using BakAgain.ResourceManagement.Converters;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.World.Converters;
    using BakAgain.World.Rendering;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Config;
    using GameData.Resources.Image;
    using GameData.Resources.Palette;
    using GameData.Resources.World;
    using Microsoft.Extensions.Logging;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using UnityEngine;
    using Color = UnityEngine.Color;

    /// <summary>
    /// Builds a Unity scene graph from BaK zone data (TBL + WLD + sprites + textures).
    /// Uses Addressables system for resource loading.
    /// </summary>
    public class ZoneSceneBuilder {
        /// <summary>Zone draw distance in Unity units, used as the fog end distance. Applies to
        /// every zone — the RMP supplies the fog colour, not the range.</summary>
        private const float DefaultFogMaxDistance = 640f;

        private readonly ILogger<ZoneSceneBuilder> _logger;
        private readonly WorldRenderModeService _renderModeService;
        private readonly IResourceProviderService _resourceProvider;
        private readonly IResourceCache _resourceCache;
        private readonly BakAgain.Core.GameSession _gameSession;
        private readonly GroundBagService _groundBags;
        private readonly DoorVisualService _doorVisuals;

        public ZoneSceneBuilder(
            ILogger<ZoneSceneBuilder> logger,
            WorldRenderModeService renderModeService,
            IResourceProviderService resourceProvider,
            IResourceCache resourceCache,
            BakAgain.Core.GameSession gameSession = null,
            GroundBagService groundBags = null,
            DoorVisualService doorVisuals = null) {
            _logger = logger;
            _renderModeService = renderModeService;
            _resourceProvider = resourceProvider;
            _resourceCache = resourceCache;
            // Optional so the model/zone debug harnesses can build a zone with no session behind
            // them; without both, a zone simply shows no dropped bags.
            _gameSession = gameSession;
            _groundBags = groundBags;
            _doorVisuals = doorVisuals;
        }

        /// <summary>
        /// Collision geometry for the zone built by the last <see cref="BuildZoneAsync"/> call — the
        /// GID polygons of every placement, plus the FILTER.DAT candidate gate. Consumed by
        /// <see cref="PartyMovement"/>; null when FILTER/TBL data was unavailable.
        /// </summary>
        public Collision.ProximityWorld Collision { get; private set; }

        /// <summary>
        /// The render context the zone was built with — materials, palette and fog.
        /// </summary>
        /// <remarks>
        /// Exposed so anything added to the world AFTER the build draws through the same one.
        /// A second context would give creatures their own materials and a subtly different
        /// palette, which reads as an art bug rather than as two contexts.
        /// </remarks>
        public WorldEntityRenderContext RenderContext { get; private set; }

        /// <summary>The zone's ordered RMP blocks, kept from the fog load for the render context.</summary>
        private List<Dictionary<byte, byte>> _fadeRamp;

        /// <summary>Chunk names (<c>Tzzxxyy</c>) the last build loaded, in zone-map order.</summary>
        public IReadOnlyList<string> LoadedChunks { get; private set; } = System.Array.Empty<string>();

        /// <summary>
        /// Which tiles are active, keyed off the party's own — TASK-130 option (b).
        /// </summary>
        /// <remarks>
        /// Populated as the zone is built, one registration per tile. Every tile starts ACTIVE, so
        /// a caller that never applies residency gets the behaviour that existed before it.
        /// </remarks>
        public TileResidency Residency { get; private set; } = new TileResidency();

        /// <summary>
        /// Build the complete zone scene. Returns the root GameObject (caller owns lifetime).
        /// </summary>
        public async UniTask<GameObject> BuildZoneAsync(int zoneNumber) {
            _logger.LogInformation("Building zone {Zone} scene...", zoneNumber);
            string zoneId = $"Z{zoneNumber:D2}";

            // Use the zone root as the owner for resource tracking
            var root = new GameObject($"Zone_{zoneId}");

            try {
                // Load zone data from Addressables
                var tbl = await LoadTblAsync(zoneId, root);
                var palette = await LoadPaletteAsync(zoneId, root);
                var unityPalette = palette.Colors.ToUnity();
                var profile = _renderModeService.CreateProfile();

                // Build fog data from RMP
                var fogData = await LoadAndBuildFogAsync(zoneId, unityPalette, root);

                // Build scene hierarchy
                var terrainParent = CreateChild(root, "Terrain");
                var entitiesParent = CreateChild(root, "Entities");
                var skyParent = CreateChild(root, "Sky");
                var envParent = CreateChild(root, "Environment");

                // The zone definition is loaded up here rather than with the sky colours below,
                // because whether the zone is underground decides which DETECT range block the
                // entities are stamped with — and that has to be known before they are built.
                var zoneDef = await _resourceProvider.LoadAssetAsync<ZoneDefinition>(
                    $"{zoneId}DEF.DAT", root);

                // Data-driven interactability range table (DETECT.DAT) — carried on the render
                // context for the collider decision (Task 6).
                var detect = await LoadDetectAsync(root);
                bool underground = zoneDef?.IsUnderground ?? false;

                // Single shared render context — terrain/polygon materials, fog and sprites. The
                // model debug viewer assembles the same context and calls the same
                // WorldEntityBuilder, so the two render through one path rather than two.
                var renderCtx = WorldEntityRenderContext.Create(
                    unityPalette, profile, detect, underground);

                // Slot-bitmap texturing: the extractor bakes each textured face's resource key
                // directly (see WorldEntityRenderContext.EnsureSlotTexturesAsync), so the loader
                // just needs the shared resource cache. Sprite billboards load from the same baked
                // key via ResourceProvider (WorldEntityRenderContext.LoadSpriteTextureAsync).
                renderCtx.SlotLoader = new SlotBitmapTextureLoader(_resourceCache);
                renderCtx.ResourceProvider = _resourceProvider;
                renderCtx.FadeRamp = _fadeRamp;
                RenderContext = renderCtx;

                // *** BEFORE ANY MESH IS BUILT. *** Z##.DAT's pen remap is baked per face into the
                // meshes below, so it has to be in the context before the entities are made — this
                // used to sit further down beside MapFillColor, where every mesh had already been
                // built and the map palette reached nothing. ToPenTable is identity with the
                // changes applied, so a zone that remaps nothing (Z10-Z12 ship empty tables) yields
                // a map palette equal to the world's and map mode changes no colour there.
                var appearance = await _resourceProvider.LoadAssetAsync<ZoneAppearance>(
                    $"Z{zoneNumber:D2}.DAT", root);
                if (appearance != null) {
                    byte[] penTable = appearance.ToPenTable();
                    var mapPalette = new Color[unityPalette.Length];
                    for (var i = 0; i < mapPalette.Length; i++) {
                        byte drawnAs = i < penTable.Length ? penTable[i] : (byte)i;
                        mapPalette[i] = drawnAs < unityPalette.Length
                            ? unityPalette[drawnAs]
                            : unityPalette[i];
                    }
                    renderCtx.MapPalette = mapPalette;
                }

                var modelLoader = new WorldModelLoader(tbl, renderCtx);

                // The dungeon automap draws the SAME placements from the zone's map model table
                // (Z##M.TBL — record table slot 2). The original swaps the table under the renderer
                // for the duration of the pass; we cannot swap a table under a live scene graph, so
                // the map models are built alongside the world ones and the two sets are swapped by
                // activation. Only the three underground zones ship a Z##M.TBL.
                ZoneTable mapTbl = underground
                    ? await _resourceProvider.LoadAssetAsync<ZoneTable>($"{zoneId}M.TBL", root)
                    : null;
                WorldModelLoader mapModelLoader = mapTbl != null
                    ? new WorldModelLoader(mapTbl, renderCtx)
                    : null;
                GameObject automapParent = mapTbl != null ? CreateChild(root, "Automap") : null;
                DungeonAutomapView automap = automapParent != null
                    ? root.AddComponent<DungeonAutomapView>()
                    : null;

                var zoneTeardown = root.AddComponent<ZoneTeardown>();
                // Order matters: the loader destroys its template GameObjects first, then the
                // context frees the materials/meshes/textures those templates referenced. Neither
                // is reclaimed by destroying the zone root — Unity only frees a Material/Mesh/
                // Texture2D on an explicit Destroy.
                zoneTeardown.OnDestroyed = () => {
                    // The bag spawner holds this zone's render context and model loader, so it has
                    // to let go before they are disposed.
                    _groundBags?.ClearZone(renderCtx);
                    _doorVisuals?.ClearZone(renderCtx);
                    modelLoader.Dispose();
                    mapModelLoader?.Dispose();
                    renderCtx.Dispose();
                };

                // De-indexed entity lookup (reference #1): resolve each placement by its stable
                // content key (WorldItem.EntityKey → ZoneTableEntry.Key) instead of the raw TypeId
                // positional index. The key survives a mod reordering/extending the zone table; the
                // index does not. See docs/re-notes/reference-inventory.md #1.
                var entryByKey = new Dictionary<string, ZoneTableEntry>(tbl.Entries.Count);
                foreach (var e in tbl.Entries) entryByKey[e.Key] = e;

                // Build world tiles
                var wldFiles = await GetWldFilesAsync(zoneNumber, root);
                LoadedChunks = wldFiles;

                // Collision candidates are the same placements the renderer walks, so they are
                // collected in this loop rather than by re-reading the WLDs (spec §2.5: the
                // collision candidate list *is* the render candidate list).
                var proximityRecords = new List<Collision.ProximityRecord>();

                Residency = new TileResidency();

                foreach (string wldFile in wldFiles) {
                    var tile = await LoadWldAsync(wldFile, root);
                    string tileName = Path.GetFileNameWithoutExtension(wldFile);
                    var tileTerrainParent = CreateChild(terrainParent, tileName);
                    var tileEntityParent = CreateChild(entitiesParent, tileName);
                    var tileAutomapParent = automapParent != null
                        ? CreateChild(automapParent, tileName)
                        : null;

                    // The dungeon automap addresses a mark as tile + index-in-tile, so the record
                    // has to carry where it was read from. The index counts EVERY item in the file,
                    // including ones we skip below for having no zone-table entry — the original
                    // indexes its own list the same way (see ProximityRecord.TileX).
                    Hotspots.HotspotService.TryParseChunkCoords(tileName, out int tileCx, out int tileCy);
                    // The chunk name's coordinates ARE the tile coordinates WorldTileCache.TileOf
                    // computes from a world position — HotspotService looks its triggers up by
                    // Floor(worldX, 64000) against these same numbers — so residency can key off
                    // the party's position with no second mapping.
                    //
                    // *** THE AUTOMAP TILE IS DELIBERATELY NOT REGISTERED. *** DungeonAutomapView
                    // already owns that subtree's visibility, and at two levels: it deactivates the
                    // whole Automap root while travelling and, when shown, activates each placement
                    // by whether it has been SEEN. Residency toggling the per-tile parents
                    // underneath would fight both — a tile outside the ring would stay hidden on the
                    // map however much of it the party had explored, which is the opposite of what a
                    // map is for. Residency owns what is DRAWN IN THE WORLD; the map owns itself.
                    Residency.Register(tileCx, tileCy, tileTerrainParent, tileEntityParent);
                    int indexInTile = -1;

                    foreach (var item in tile.Items) {
                        indexInTile++;
                        if (!entryByKey.TryGetValue(item.EntityKey, out var entry)) continue;

                        // A door is built in the shape its FLAG says, not the shape the WLD names.
                        // worlddoor_load_door_records reads DOOR_OPEN(id) at zone load and swaps
                        // the shape id (step 4), which is what makes a door left open render open
                        // when the party comes back. Shut and open are two different models
                        // (0x5c / 0x5d), not one animated one.
                        int typeId = item.TypeId;
                        if (entry.Behavior == "door") {
                            typeId = DoorShapeFor(item, tbl, ref entry) ?? typeId;
                        }

                        // The proximity record is the raw BaK-space placement (the original's
                        // WorldObject read through a collision lens) — never the Unity transform.
                        proximityRecords.Add(new Collision.ProximityRecord(entry,
                            (int)item.Position.X, (int)item.Position.Y, (int)item.Position.Z,
                            item.Rotation.Z, (byte)tileCx, (byte)tileCy, indexInTile));

                        Vector3 worldPos = BakCoordinateConverter.ConvertPosition(
                            item.Position.X, item.Position.Y, item.Position.Z);
                        Quaternion worldRot = BakCoordinateConverter.ConvertRotation(
                            item.Rotation.X, item.Rotation.Y, item.Rotation.Z);

                        // Classification, mesh conversion and material assignment all live in the
                        // shared WorldEntityBuilder — the single owner of how a world entity renders.
                        await WorldEntityBuilder.Build(entry, typeId, worldPos, worldRot,
                            tileTerrainParent.transform, tileEntityParent.transform, renderCtx,
                            modelLoader, item.Rotation.Z);

                        // Same placement, same shape id (including the door's open/shut swap above —
                        // the automap picks the door shape the same way every other pass does), but
                        // the map table's model. A null map entry means the automap deliberately
                        // omits this entity, so it is skipped rather than treated as a missing asset.
                        if (mapModelLoader != null && (uint)typeId < mapTbl.Entries.Count) {
                            ZoneTableEntry mapEntry = mapTbl.Entries[typeId];
                            if (mapEntry != null && mapEntry.Name != "null") {
                                GameObject mapped = await WorldEntityBuilder.Build(
                                    mapEntry, typeId, worldPos, worldRot,
                                    tileAutomapParent.transform, tileAutomapParent.transform,
                                    renderCtx, mapModelLoader, item.Rotation.Z);
                                automap.Add((byte)tileCx, (byte)tileCy, indexInTile, mapped);
                            }
                        }
                    }
                }

                // Dropped bags are not in any WLD — they are runtime state in the save's container
                // records — so they are spawned from the session after the authored tiles are up,
                // and the same context is handed to the spawner so a later drop can build one too.
                await SpawnGroundBagsAsync(zoneNumber, tbl, entitiesParent, renderCtx, modelLoader);

                // FILTER.DAT gates which placements are collision candidates at all. Terrain kinds
                // are marked "always" in every detail block, so the graphics detail slider changes
                // what you see and never what you can walk on (spec §2.5, acceptance #22).
                var filter = await _resourceProvider.LoadAssetAsync<FilterData>("FILTER.DAT", root);
                // The automap stands in for the world's terrain and entities while it is up, so it
                // needs to know what to hide. Done after the tile loop, when both roots are fully
                // populated.
                if (automap != null) {
                    automap.Initialize(automapParent, terrainParent, entitiesParent);
                    _logger.LogInformation(
                        "Zone {Zone} automap: {Mapped} of {Placements} placements have a map model.",
                        zoneNumber, automap.PlacementCount, proximityRecords.Count);
                }

                Collision = new Collision.ProximityWorld(proximityRecords, filter);

                // Doors are re-shaped in place when they open, and a door's shape IS its collision,
                // so the context they are re-shaped from carries the collision world. *** IT HAS TO
                // BE PUBLISHED AFTER Collision EXISTS. *** This call sat inside SpawnGroundBagsAsync,
                // which runs above — so doors were handed a null collision world and an opened door
                // left the doorway impassable, the very thing the swap exists to fix. It also sat
                // past that method's `_groundBags == null` early return, so a zone with no bag
                // spawner gave its doors no context at all.
                _doorVisuals?.SetZone(tbl, renderCtx, modelLoader, entitiesParent.transform, Collision);

                // The dungeon automap is drawn from what the party has walked past, and the
                // original only allocates that table underground — so handing the scan a table is
                // itself the gate. Above ground (or with no session) the scan records nothing.
                if (_gameSession != null && zoneDef != null) {
                    Collision.EnableAutomapRecording(_gameSession.AutomapVisits,
                        (byte)zoneNumber, zoneDef.ZoneLocation);
                }

                _logger.LogInformation("Zone {Zone} collision: {Records} of {Placements} placements carry GID polygons.",
                    zoneNumber, Collision.RecordCount, proximityRecords.Count);

                // Configure environment — sky band + horizon mountains, mirroring the original's
                // drawHorizonSkyAndGround (ovr138 @0x460c7): SkyColor/GroundColor are DEF pen
                // indices resolved through the zone palette (they shift with time of day because
                // the day/night system mutates the palette, not the index); the Z##H.BMX mountain
                // panels are blitted across the horizon offset by camera heading. The resolved
                // colours + horizon are handed to InGameState (camera owner) via ZoneEnvironment.
                var env = root.AddComponent<ZoneEnvironment>();
                if (zoneDef != null) {
                    env.SkyColor = unityPalette[zoneDef.SkyColor];
                    env.GroundColor = unityPalette[zoneDef.GroundColor];
                }

                // The overhead map's flat fill is a DIFFERENT pen from either of those, and it comes
                // from a different file: Z##.DAT's ground pen, the one the sky renderer swaps below
                // the horizon. Looking straight down, everything is ground — so that is what the map
                // clears to. Underground ships an empty file (pen 0) and never renders this way.
                if (appearance != null) {
                    env.MapFillColor = unityPalette[appearance.GroundPen];
                }

                // *** THE FLAG IS THE GATE, NOT THE FILE'S ABSENCE. *** ZoneFlags.NoHorizon is a
                // LOAD gate in the original (resource_loadZoneDataFiles @0x733b8): a zone that sets
                // it never reads Z##H.BMX at all. Relying on the load failing gave the same result
                // only because the shipped underground zones happen to have no such file — a mod
                // that added one would paint mountains in a cave, which the original could not do.
                Texture2D[] horizonPanels =
                    zoneDef != null && (zoneDef.Flags & ZoneFlags.NoHorizon) != 0
                        ? null
                        : await LoadHorizonPanelsAsync(zoneId, unityPalette, root);
                if (horizonPanels is { Length: >= 4 }) {
                    var horizonGo = CreateChild(skyParent, "Horizon");
                    var horizon = horizonGo.AddComponent<HorizonRenderer>();
                    horizon.Initialize(horizonPanels);
                    env.Horizon = horizon;
                    // Bitmap-horizon mode (word_dseg_1E76 bit clear): the visible sky comes from the
                    // panel's own sky band, NOT the DEF skyColor pen (that's the flat/underground
                    // source). Clear the camera to it so the band is coherent with the mountains.
                    env.SkyColor = horizon.PanelSkyColor;
                    // Initialize stitches the four quadrants into one wrap texture and keeps only
                    // that; the source panels are ours and are dead from here.
                    foreach (var panel in horizonPanels) {
                        if (panel != null) Object.Destroy(panel);
                    }
                }

                profile.ConfigureSky(skyParent, null);
                // *** THE HAZE RANGE IS THE ZONE'S OWN. *** Z##DEF.DAT carries where the original's
                // sprite fog starts and where it reaches its last rung; a fixed 320..640 left an
                // eleven-cell pine at full colour where the original had faded it grey (TASK-434).
                // Sprites only: the terrain keeps the wide fog, as the original remaps sprites alone.
                (float spriteFogStart, float spriteFogEnd) = (0f, 0f);
                if (zoneDef != null) {
                    (uint start, uint end) = GameData.Resources.World.ZoneFog.Range(zoneDef);
                    (spriteFogStart, spriteFogEnd) = (start / Converters.BakCoordinateConverter.WorldScale,
                        end / Converters.BakCoordinateConverter.WorldScale);
                    // *** OUTDOORS THE SPRITE HAZE HAS TO REACH AS FAR AS THE TERRAIN'S. *** The
                    // shipped range ends at 250 units outdoors while the ground fades over
                    // 320..640, and the original got away with that because it also CULLED sprites
                    // at about the same 250 — the ramp ended exactly where the tree stopped being
                    // drawn. We no longer cull by distance (decision 0003), so that ramp saturated
                    // in open view: measured on Z01, mid-distance pines were flat brown cut-outs
                    // standing against fully saturated green hills BEHIND them, haze running
                    // backwards. Stretching the far end to the terrain's keeps a tree fading at the
                    // same rate as the ground it stands on. Underground keeps its own tight range
                    // (Z10: 30..64) — there the short fade IS the dungeon's darkness, and the wide
                    // terrain fog never bites in a corridor.
                    if (!zoneDef.IsUnderground) {
                        spriteFogEnd = Mathf.Max(spriteFogEnd, fogData.maxDist);
                    }
                }
                profile.ConfigureFog(fogData.fogColor, fogData.maxDist, spriteFogStart, spriteFogEnd);
                profile.ConfigureLighting(envParent, Color.white * 0.8f);

                // Add fog controller

                int entityCount = entitiesParent.transform.childCount;
                _logger.LogInformation("Zone {Zone} built: {Tiles} tiles, entities parent has {Children} tile groups",
                    zoneId, wldFiles.Count, entityCount);

                return root;
            } catch (System.Exception ex) {
                _logger.LogError(ex, "Failed to build zone {Zone}", zoneId);
                // Clean up resources on failure
                _resourceProvider.ReleaseAssets(root);
                Object.Destroy(root);
                return null;
            }
        }

        /// <summary>
        /// Publish this zone's build context to <see cref="GroundBagService"/> and spawn an entity
        /// for every ground bag the session already holds here, so piles dropped in an earlier
        /// visit (or in a loaded save) are back on the ground.
        /// </summary>
        /// <summary>
        /// The shape a door should be built in, or null to keep the one the WLD names.
        /// </summary>
        /// <remarks>
        /// <b>The door's identity is not in the WLD.</b> A WLD record only says "a door shape
        /// stands here"; the variant and lock live on the fixed-object placement record at that
        /// world position, which is why this resolves a container to find out which door it is.
        ///
        /// <para>Returns null — leaving the authored shape — whenever there is no placement record
        /// or the flag already agrees, so a zone with no door data builds exactly as before.</para>
        /// </remarks>
        private int? DoorShapeFor(WorldItem item, ZoneTable tbl, ref ZoneTableEntry entry) {
            if (_gameSession == null) {
                return null;
            }

            GameData.Resources.Data.SaveGameContainerData placement = _gameSession.GetContainerAt(
                _gameSession.CurrentZone, (int)item.Position.X, (int)item.Position.Y);
            int? variant = GameData.Resources.Data.FixedObjectAccess.DoorVariant(placement);
            if (variant == null) {
                return null;
            }

            bool isOpen = _gameSession.GetGlobalValue(
                GameData.Resources.World.DoorMechanics.OpenFlagBase + variant.Value) == 1;
            int wanted = GameData.Resources.World.DoorMechanics.ShapeFor(isOpen);
            if (wanted == item.TypeId) {
                return null;
            }

            foreach (var e in tbl.Entries) {
                if (e.Index == wanted) {
                    entry = e;

                    return wanted;
                }
            }

            return null;   // the zone has no entry for the other shape: keep what was authored
        }

        private async UniTask SpawnGroundBagsAsync(int zoneNumber, ZoneTable tbl,
            GameObject entitiesParent, WorldEntityRenderContext renderCtx, WorldModelLoader modelLoader) {
            if (_groundBags == null) {
                return;
            }

            int bagTypeId = GameData.Resources.Inventory.GroundContainerPool.BagWorldItemId;
            ZoneTableEntry bagEntry = null;
            foreach (var e in tbl.Entries) {
                if (e.Index == bagTypeId) {
                    bagEntry = e;
                    break;
                }
            }

            var bagParent = CreateChild(entitiesParent, "DroppedBags");
            _groundBags.SetZone(zoneNumber, bagEntry, bagTypeId, renderCtx, modelLoader,
                bagParent.transform);

            if (_gameSession == null) {
                return;
            }
            if (bagEntry != null) {
                foreach (var bag in _gameSession.GroundBagsInZone(zoneNumber)) {
                    await _groundBags.SpawnAsync(bag);
                }
            }

            // RES_SELF_SPAWN loot (ACTSPAWN.C:83): no WLD placement, so the record itself is what
            // puts the object in the world, drawn with the row its WorldItemId names (TASK-557).
            foreach (var loot in _gameSession.SelfSpawnedLootInZone(zoneNumber)) {
                ZoneTableEntry lootEntry = null;
                foreach (var e in tbl.Entries) {
                    if (e.Index == loot.WorldItemId) {
                        lootEntry = e;
                        break;
                    }
                }
                if (lootEntry == null) {
                    _logger.LogWarning(
                        "Zone {Zone} has no TBL entry {TypeId} for self-spawned loot at ({X}, {Y}).",
                        zoneNumber, loot.WorldItemId, loot.X, loot.Y);
                    continue;
                }
                await _groundBags.SpawnAsync(loot, lootEntry, loot.WorldItemId, "SelfSpawn");
            }
        }

        #region Addressables Resource Loading Helpers

        private async UniTask<ZoneTable> LoadTblAsync(string zoneId, GameObject owner) {
            var address = $"{zoneId}.TBL";
            var tbl = await _resourceProvider.LoadAssetAsync<ZoneTable>(address, owner);
            if (tbl == null) {
                throw new System.InvalidOperationException($"Failed to load TBL for zone {zoneId}");
            }

            return tbl;
        }

        private async UniTask<DetectData> LoadDetectAsync(GameObject owner) {
            var detect = await _resourceProvider.LoadAssetAsync<DetectData>("DETECT.DAT", owner);
            if (detect == null) {
                throw new System.InvalidOperationException("Failed to load DETECT.DAT");
            }

            return detect;
        }

        private async UniTask<PaletteResource> LoadPaletteAsync(string zoneId, GameObject owner) {
            var address = $"{zoneId}.PAL";
            var palette = await _resourceProvider.LoadAssetAsync<PaletteResource>(address, owner);
            if (palette == null) {
                throw new System.InvalidOperationException($"Failed to load PAL for zone {zoneId}");
            }

            return palette;
        }

        private async UniTask<WorldTile> LoadWldAsync(string wldPath, GameObject owner) {
            var fileName = Path.GetFileNameWithoutExtension(wldPath);
            var address = $"{fileName}.WLD";
            var wld = await _resourceProvider.LoadAssetAsync<WorldTile>(address, owner);
            if (wld == null) {
                throw new System.InvalidOperationException($"Failed to load WLD: {wldPath}");
            }

            return wld;
        }

        private async UniTask<List<string>> GetWldFilesAsync(int zoneNumber, GameObject owner) {
            // Load the zone map bitmap — each set bit indicates a tile exists at (x,y)
            // The original game uses ZxxMAP.DAT (a 50x8-byte bitmap for a 50x50 grid)
            string mapAddress = $"Z{zoneNumber:D2}MAP.DAT";
            var zoneMap = await _resourceProvider.LoadAssetAsync<ZoneMap>(mapAddress, owner);

            var wldFiles = new List<string>();
            if (zoneMap == null) {
                _logger.LogWarning("No zone map {Map} found, cannot determine WLD tiles", mapAddress);
                return wldFiles;
            }

            // WLD filenames are Tzzxxyy.WLD where zz=zone, xx=x, yy=y (all 2-digit decimal)
            for (int y = 0; y < ZoneMap.Height; y++) {
                for (int x = 0; x < ZoneMap.Width; x++) {
                    if (zoneMap.IsTileInZone(x, y)) {
                        wldFiles.Add($"T{zoneNumber:D2}{x:D2}{y:D2}");
                    }
                }
            }

            _logger.LogDebug("Zone map {Map} has {Count} WLD tiles", mapAddress, wldFiles.Count);
            return wldFiles;
        }

        private async UniTask<(Color fogColor, float maxDist)> LoadAndBuildFogAsync(
            string zoneId, Color[] palette, GameObject owner) {
            try {
                var address = $"{zoneId}.RMP";
                var rmp = await _resourceProvider.LoadAssetAsync<RemapResource>(address, owner);

                if (rmp == null) {
                    _logger.LogWarning("No RMP fog data for {Zone}, using defaults", zoneId);
                    return (Color.gray, DefaultFogMaxDistance);
                }

                var blocks = new List<Dictionary<byte, byte>>();
                foreach (var kvp in rmp.Mappings.OrderBy(k => k.Key))
                    blocks.Add(kvp.Value);

                // *** THE SAME BLOCKS SERVE THE HIT FLASH. *** The original selects one with
                // (index << 8) + 0xA66 for both distance and spriteHitDir, so keeping the list is
                // what lets a struck combatant be redrawn one rung further into the fog instead of
                // being moved. Held in a field rather than assigned to RenderContext here: the fog
                // is loaded BEFORE the context is created, so assigning it here would silently do
                // nothing. See HitReaction.
                _fadeRamp = blocks;

                var rampData = FogRampBuilder.BuildFogRamp(blocks, palette);
                return (rampData.FogColor, DefaultFogMaxDistance);
            } catch (System.Exception ex) {
                _logger.LogWarning(ex, "No RMP fog data for {Zone}, using defaults", zoneId);
                return (Color.gray, DefaultFogMaxDistance);
            }
        }

        /// <summary>
        /// Load the zone's horizon mountain panels (<c>Z##H.BMX</c>, 4 × 90° quadrant images).
        /// Opaque (sky colour baked into the panel top), so index 0 is NOT treated as transparent.
        /// Returns null for zones without a horizon BMX (e.g. underground).
        /// </summary>
        private async UniTask<Texture2D[]> LoadHorizonPanelsAsync(string zoneId, Color[] palette, GameObject owner) {
            try {
                var address = $"{zoneId}H.BMX";
                var bitmapResource = await _resourceProvider.LoadAssetAsync<ImageSet>(address, owner);
                if (bitmapResource?.Images == null) {
                    _logger.LogDebug("No horizon BMX {Addr} for zone {Zone}", address, zoneId);
                    return null;
                }

                var panels = new List<Texture2D>();
                foreach (var img in bitmapResource.Images) {
                    if (img.BitMapData == null) continue;
                    panels.Add(img.ToTexture2D(palette, transparentIndex0: false));
                }
                _logger.LogDebug("Loaded {Count} horizon panels for zone {Zone}", panels.Count, zoneId);
                return panels.ToArray();
            } catch (System.Exception ex) {
                _logger.LogDebug(ex, "Failed to load horizon BMX for zone {Zone}", zoneId);
                return null;
            }
        }

        #endregion

        #region GameObject Creation Helpers

        private static GameObject CreateChild(GameObject parent, string name) {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            return go;
        }

        #endregion
    }
}