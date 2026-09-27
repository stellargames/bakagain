namespace BakAgain.World.Encounters {
    using BakAgain.ResourceManagement;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Creature;
    using GameData.Resources.World;
    using Microsoft.Extensions.Logging;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Draws the encounter actors a chunk placed — the visible end of
    /// <c>rgnenc_render_object</c>.
    /// </summary>
    /// <remarks>
    /// <b>An encounter actor is an ordinary world sprite whose shape lives in a different table.</b>
    /// The original draws it by setting <c>shapeId = g_nProximityTableCount + creatureNumber</c> and
    /// going through the same entity path as everything else; <c>ts_get_shape</c> walks the
    /// shape-table slots subtracting each one's count, and <c>zone_load</c> fills slot 0 from the
    /// zone's own table and slot 1 from <c>COMBAT.TBL</c>. So the creature's extent, anchor and
    /// sprite-size scale come from <c>COMBAT.TBL[creatureNumber]</c> and there is nothing here to
    /// invent — which is the whole reason this class is short.
    ///
    /// <para><b>The images come from the creature's own set, not from a baked key.</b> Every face in
    /// COMBAT.TBL carries a null <c>TextureBitmap</c>: the original swaps the image table to that
    /// creature's bitmaps for the duration of the draw
    /// (<c>worldrender_table_swap(0, combat_actor_rsrc_cache_val(creature, 0))</c>), and the face's
    /// <c>BitmapIndex</c> indexes into THAT. <see cref="CreatureBitmaps"/> holds the set names; the
    /// <c>, 0</c> in the original's call is why only the first of a creature's three sets is used
    /// here.</para>
    /// </remarks>
    public sealed class EncounterActorSpriteBuilder {
        private readonly IResourceProviderService _resources;
        private readonly Microsoft.Extensions.Logging.ILogger _logger;

        private ZoneTable _combat;
        private CreatureBitmaps _bitmaps;
        private readonly Dictionary<int, List<string>> _slotImageCounts = new Dictionary<int, List<string>>();

        public EncounterActorSpriteBuilder(IResourceProviderService resources, Microsoft.Extensions.Logging.ILogger logger = null) {
            _resources = resources;
            _logger = logger;
        }

        /// <summary>
        /// Builds one billboard per placed actor under <paramref name="parent"/>.
        /// </summary>
        /// <returns>How many were drawn.</returns>
        /// <remarks>
        /// <b>The facing is resolved ONCE, at build time.</b> Which sprite an actor shows depends on
        /// the angle between it and the camera, so a faithful draw re-picks the face every frame —
        /// that, and the walk cycle, belong with the actor-animation work rather than here. This
        /// puts the right creature at the right place and size, facing the camera it was built for.
        /// </remarks>
        /// <param name="arena">
        /// These placements are the CURRENT FIGHT's combatants rather than the actors standing on
        /// the map. Only they get the picking collider that target selection needs — the same
        /// creature drawn on the world map must stay unclickable, or a click meant for scenery would
        /// resolve as an attack on a monster the party has not engaged.
        /// </param>
        public async UniTask<int> BuildAsync(
            IReadOnlyList<EncounterActorPlacement.Placed> placed, Transform parent,
            WorldEntityRenderContext ctx, Camera camera, object owner, bool arena = false) {
            if (placed == null || placed.Count == 0 || parent == null || ctx == null) {
                return 0;
            }

            _combat ??= await _resources.LoadAssetAsync<ZoneTable>("COMBAT.TBL", owner);
            _bitmaps ??= await _resources.LoadAssetAsync<CreatureBitmaps>("BNAMES.DAT", owner);
            if (_combat == null || _bitmaps == null) {
                _logger?.LogWarning(
                    "Encounter actors not drawn: COMBAT.TBL or BNAMES.DAT unavailable.");
                return 0;
            }

            var drawn = 0;
            foreach (EncounterActorPlacement.Placed actor in placed) {
                if (await BuildOneAsync(actor, parent, ctx, camera, owner, arena)) {
                    drawn++;
                }
            }
            return drawn;
        }

        /// <summary>
        /// Builds the single billboard an effect sprite is — a projectile, not an actor.
        /// </summary>
        /// <remarks>
        /// <b>An effect entry carries exactly ONE mesh face.</b> Checked against the shipped table
        /// for <c>spell</c>, <c>jack</c>, <c>rock</c> and <c>spell5</c>: unlike a creature there is
        /// no pose set to choose from and no octant to resolve, so the picture is fixed and only its
        /// position moves.
        ///
        /// <para><b>The bitmap index is GLOBAL over the zone's slot chain.</b> COMBAT.TBL faces
        /// carry no baked <c>Z##SLOT#.BMX#i</c> key because the table belongs to no zone —
        /// <c>ZoneTableExtractor.ParseZoneNumber</c> returns null for it by name — so the
        /// concatenation the extractor does at build time is done here at draw time instead. It is
        /// the same walk <c>zoneSlotBitmapFromGlobalIndex</c> @0x22f25 does, which is the original's
        /// resolver for any table whose <c>bitmapCacheSlot</c> is -1 — and the loader sets that on
        /// every table it loads, so it is the normal path rather than a fallback.</para>
        /// </remarks>
        public async UniTask<GameObject> BuildEffectSpriteAsync(int effectId, int zoneNumber,
            Transform parent, WorldEntityRenderContext ctx, object owner) {
            if (parent == null || ctx == null) {
                return null;
            }
            _combat ??= await _resources.LoadAssetAsync<ZoneTable>("COMBAT.TBL", owner);
            if (_combat?.Entries == null || effectId < 0 || effectId >= _combat.Entries.Count) {
                return null;
            }

            ZoneTableEntry entry = _combat.Entries[effectId];
            SpriteBMeshFace face = FirstSpriteFace(entry);
            if (face == null) {
                // *** NOT EVERY EFFECT IS A BILLBOARD. *** Entry 3, `jack` — Bane of Black Slayers'
                // projectile — has no sprite face at all: it is a small flat-shaded SOLID, drawn as
                // six sides sharing one vertex pool.
                return await BuildEffectModelAsync(entry, parent, ctx);
            }

            string key = await SlotKeyAsync(zoneNumber, face.BitmapIndex, owner);
            if (key == null) {
                _logger?.LogDebug(
                    "Effect sprite {Effect} not drawn: bitmap {Index} is past zone {Zone}'s slots.",
                    entry.Name, face.BitmapIndex, zoneNumber);
                return null;
            }

            Texture2D tex = await ctx.LoadSpriteTextureAsync(key, parent.gameObject);
            if (tex == null) {
                return null;
            }

            (Mesh mesh, Vector3 localScale) = TblSpriteConverter.BuildBillboard(
                face, Mathf.Abs(entry.Dat.Extent), tex.width, tex.height);
            ctx.TrackMesh(mesh);

            var go = new GameObject($"Effect {entry.Name}");
            go.transform.SetParent(parent, false);
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = ctx.GetSpriteMaterial(tex);
            go.AddComponent<BillboardSprite>();
            return go;
        }

        /// <summary>Builds an effect entry that is a SOLID rather than a billboard.</summary>
        /// <remarks>
        /// <b>Flat-shaded, and that is what makes it short.</b> Every face of COMBAT.TBL entry 3
        /// carries <c>Flags 0x81</c> and a <c>VgaColor</c> with a null <c>TextureBitmap</c> — no
        /// <c>&amp;0x10</c>, so nothing here needs the slot-bitmap chain the sprite path resolves.
        /// The colours come straight from the zone palette, which is exactly what
        /// <see cref="TblMeshConverter.ConvertTerrainEntity"/> already does for world scenery.
        ///
        /// <para><b>No <c>BillboardSprite</c>.</b> A billboard turns to face the camera; this is a
        /// solid with six sides and its own sort normals, and turning it would be wrong rather than
        /// merely unnecessary.</para>
        ///
        /// <para>The six meshes are the solid's SIDES, not animation frames — they share
        /// <c>VertexPoolIndex 0</c>, index different vertices, and all carry
        /// <c>RuntimeFlagsIndex 255</c>, i.e. no runtime animation state. See TASK-289.</para>
        /// </remarks>
        private async UniTask<GameObject> BuildEffectModelAsync(
            ZoneTableEntry entry, Transform parent, WorldEntityRenderContext ctx) {
            if (!TblMeshConverter.HasPolygonGeometry(entry.Dat)) {
                _logger?.LogDebug(
                    "Effect {Effect} has neither a sprite face nor polygons; nothing flown.",
                    entry.Name);
                return null;
            }

            TblMeshConverter.TerrainMeshData tmd =
                TblMeshConverter.ConvertTerrainEntity(entry.Dat, ctx.Palette, ctx.MapPalette);
            Mesh mesh = TblMeshConverter.CreateTerrainMesh(tmd, entry.Name);
            if (mesh == null) {
                return null;
            }
            ctx.TrackMesh(mesh);
            await ctx.EnsureSlotTexturesAsync(tmd.Keys);
            return BakAgain.ResourceManagement.Converters.WorldMeshObjects.BuildTerrain(
                $"Effect {entry.Name}", mesh, tmd.Keys, entry.Dat.DrawPriority, ctx, parent);
        }

        /// <summary>The first sprite face in an entry's first LOD, or null if it has none.</summary>
        private static SpriteBMeshFace FirstSpriteFace(ZoneTableEntry entry) {
            if (entry?.Dat?.Lods == null || entry.Dat.Lods.Count == 0) {
                return null;
            }
            IReadOnlyList<MeshRecord> meshes = entry.Dat.Lods[0].Meshes;
            if (meshes == null) {
                return null;
            }
            foreach (MeshRecord mesh in meshes) {
                if (mesh?.MeshFaces == null) {
                    continue;
                }
                foreach (MeshFaceRecord mf in mesh.MeshFaces) {
                    if (mf is SpriteBMeshFace sprite) {
                        return sprite;
                    }
                }
            }
            return null;
        }

        /// <summary>The image set filling one bitmap slot while a fight is on screen.</summary>
        /// <remarks>
        /// <b>Slot 0 in the arena is FIGS.BMX, not the zone's.</b>
        /// <c>combat_arena_mode_enter</c> @0x5f2c0 opens a fight by loading <c>figs.bmx</c> and
        /// calling <c>fillBitmapSlot(0, figs)</c>, replacing <c>Z##SLOT0.BMX</c> for the duration —
        /// alongside <c>SwapTblDataSlots(0, 1)</c>, which is what makes COMBAT.TBL the table small
        /// entity ids resolve into.
        ///
        /// <para>That is the whole answer to "which picture does an effect sprite draw". Resolved
        /// against the zone's own slot 0, Flamecast's bitmap 2 comes out as a PINE TREE; resolved
        /// against FIGS it is a fireball, and <c>rock</c>'s 15 is a rock. FIGS holds exactly 16
        /// images and every effect entry's index is below 16, so in practice they all land in it —
        /// but the walk continues into the zone's higher slots because the original's does.</para>
        /// </remarks>
        private static string SetNameForSlot(int zone, int slot) =>
            slot == 0 ? "FIGS.BMX" : $"Z{zone:D2}SLOT{slot}.BMX";

        /// <summary>Resolve a global bitmap index into a zone slot resource key, or null if it is
        /// past the end of the chain.</summary>
        private async UniTask<string> SlotKeyAsync(int zone, int bitmapIndex, object owner) {
            if (bitmapIndex < 0) {
                return null;
            }
            if (!_slotImageCounts.TryGetValue(zone, out List<string> chain)) {
                chain = new List<string>();
                // Ascending until one is missing, exactly as resource_loadZoneDataFiles @0x7313b
                // does — the number of slot files is not recorded anywhere, it is discovered.
                for (var slot = 0; slot < 7; slot++) {
                    chain.Add(SetNameForSlot(zone, slot));
                }
                _slotImageCounts[zone] = chain;
            }

            int rest = bitmapIndex;
            foreach (string setName in chain) {
                GameData.Resources.Image.ImageSet set = null;
                try {
                    set = await _resources.LoadAssetAsync<GameData.Resources.Image.ImageSet>(
                        setName, owner);
                } catch (System.Exception) {
                    set = null;
                }
                if (set?.Images == null || set.Images.Count == 0) {
                    return null;   // the chain ends here, and the index is past it
                }
                if (rest < set.Images.Count) {
                    return $"{setName}#{rest}";
                }
                rest -= set.Images.Count;
            }
            return null;
        }

        private async UniTask<bool> BuildOneAsync(EncounterActorPlacement.Placed actor,
            Transform parent, WorldEntityRenderContext ctx, Camera camera, object owner,
            bool arena) {
            ZoneTableEntry entry = EntryFor(actor.CreatureNumber);
            SpriteBMeshFace face = FaceFor(entry, actor, camera, out bool mirrored,
                out MeshRecord meshSet, out int poseKind, out int column);
            if (face == null) {
                return false;
            }

            string setName = SetNameFor(actor.CreatureNumber);
            if (setName == null) {
                return false;
            }

            Texture2D tex = await ctx.LoadSpriteTextureAsync(
                $"{setName}#{face.BitmapIndex}", parent.gameObject);
            if (tex == null) {
                return false;
            }

            (Mesh mesh, Vector3 localScale) = TblSpriteConverter.BuildBillboard(
                face, Mathf.Abs(entry.Dat.Extent), tex.width, tex.height);

            var go = new GameObject($"Encounter {entry.Name} #{actor.RosterSlot}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = BakCoordinateConverter.ConvertPosition(
                (int)actor.WorldX, (int)actor.WorldY, 0);
            // Mirroring is a negative X scale, which is what the original's sprite-flip flag does:
            // the far half of the turn has no art of its own.
            go.transform.localScale = mirrored
                ? new Vector3(-localScale.x, localScale.y, localScale.z)
                : localScale;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            Material material = ctx.GetSpriteMaterial(tex);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<BillboardSprite>();

            // BillboardSprite turns the QUAD; this turns the PICTURE on it. Without it the octant
            // resolved on this line is the only one the actor ever shows, so walking behind a
            // mordel leaves it facing you.
            go.AddComponent<DirectionalSprite>().Bind(ctx, camera, meshSet, setName,
                entry.Dat.Extent, poseKind, actor.WorldX, actor.WorldY, actor.Facing,
                column, mesh, material, go.transform.localScale);

            // Both halves of a fight are clickable, and the marker says which question the click
            // is asking: a body is looted, a live combatant is aimed at. A creature drawn on the
            // WORLD map gets neither — it is not part of a fight, so `arena` gates the whole thing.
            if (actor.Downed) {
                ArenaCorpse corpse = go.AddComponent<ArenaCorpse>();
                corpse.RosterSlot = actor.RosterSlot;
                corpse.PartyMember = actor.PartyMember;
                corpse.EncounterNumber = actor.EncounterNumber;
                // *** THE CORPSE PICTURE IS THE DEATH ANIMATION'S LAST FRAME. *** The downed mesh's
                // columns for a mordel are bitmaps 18, 22 and 26, and those are exactly the fourth
                // frame of each four-frame death run (15..18, 19..22, 23..26). So the collapse is
                // this index counted back three, and it ENDS on the picture already drawn here —
                // which is why the hand-off needs no second sprite and nothing jumps. See TASK-285.
                corpse.CollapseLastFrame = face.BitmapIndex;
                corpse.SetName = setName;
                corpse.Face = face;
                corpse.Extent = entry.Dat.Extent;
                WorldEntityBuilder.AddInteractionCollider(go, mesh.bounds.center, mesh.bounds.size);
            } else if (arena) {
                ArenaCombatant marker = go.AddComponent<ArenaCombatant>();
                marker.RosterSlot = actor.RosterSlot;
                marker.PartyMember = actor.PartyMember;
                marker.Face = face;
                marker.Extent = entry.Dat.Extent;
                WorldEntityBuilder.AddInteractionCollider(go, mesh.bounds.center, mesh.bounds.size);
            }
            // An actor that walks a route needs identity on the transform, or the ported tick has
            // nothing to move: BuildAsync returns a count, so after this method there is no way back
            // from a GameObject to the Placed it came from. Same shape as ArenaCorpse above.
            else if (actor.Roams && GameData.Resources.World.RoamingMovement.Moves(actor.Pattern)) {
                go.AddComponent<RoamingActor>().Bind(actor);
            }
            ctx.TrackMesh(mesh);
            return true;
        }

        /// <summary>The creature's shape — <c>COMBAT.TBL</c> indexed by creature number.</summary>
        /// <remarks>
        /// Looked up by <see cref="ZoneTableEntry.Index"/> rather than by list position: the two
        /// agree in the shipped table, and relying on that is how an unrelated extractor change
        /// silently shifts every creature by one.
        /// </remarks>
        private ZoneTableEntry EntryFor(int creatureNumber) {
            foreach (ZoneTableEntry e in _combat.Entries) {
                if (e.Index == creatureNumber) {
                    return e;
                }
            }
            return null;
        }

        /// <summary>
        /// The face to draw: the actor's facing picks a column, and the mesh depends on whether it
        /// walks.
        /// </summary>
        /// <remarks>
        /// <b>Mesh 0 is the walk set and mesh 1 the standing one</b> — 15 faces against
        /// <see cref="EncounterActorPose"/>'s five columns of three frames, and 12 against its three
        /// standing columns. Bounds-checked rather than trusted: three wyverns and the spider ship
        /// <b>14</b> faces, one short of the rearmost walking column's last frame.
        /// </remarks>
        private static SpriteBMeshFace FaceFor(ZoneTableEntry entry,
            EncounterActorPlacement.Placed actor, Camera camera, out bool mirrored,
            out MeshRecord chosenMesh, out int chosenKind, out int chosenColumn) {
            mirrored = false;
            chosenMesh = null;
            chosenKind = 0;
            chosenColumn = -1;
            if (entry?.Dat?.Lods == null || entry.Dat.Lods.Count == 0) {
                return null;
            }

            // *** EVERY LIVING ACTOR DRAWS FROM THE WALK SET, ROAMING OR NOT. ***
            //
            // This used to pick the other kind for a stationary actor, on the reading that it was a
            // "standing" pose. It is not: kind 4 is the DOWNED pose (see
            // EncounterActorPose.DownedKind), so a stationary guard — and every enemy the combat
            // arena places — was drawn as a corpse. Measured side by side on one mordel: roaming
            // gave a 280x300 frame of it standing with a sword, stationary gave 240x114, a picture
            // of it dead under its cloak.
            //
            // Roaming still decides the FRAME within the set (a gait needs one, a standing figure
            // does not); it does not decide the set. The downed pose IS drawn now, but only for a
            // combatant whose death left a body — EncounterActorPlacement.Placed.Downed, a flag of
            // its own, exactly as this comment asked for (TASK-101).
            IReadOnlyList<MeshRecord> meshes = entry.Dat.Lods[0].Meshes;
            if (meshes == null || meshes.Count == 0) {
                return null;
            }

            // The one thing that picks the other set. Roaming decides the FRAME within a set; being
            // a body decides the set itself.
            int kind = actor.Downed
                ? EncounterActorPose.DownedKind
                : EncounterActorPose.WalkingKind;

            // *** THE KIND PICKS THE MESH, NOT A COLUMN. *** The two column tables overlap (walking
            // 0/3/6/9/12, downed 3/7/11), so reading a downed column out of the WALK mesh hands back
            // a walking frame and the corpse stands up again — measured, not guessed. A mesh names
            // the flags slot it draws from, so the lookup is by RuntimeFlagsIndex.
            int slot = EncounterActorPose.FlagsSlotFor(kind);
            MeshRecord mesh = null;
            for (int i = 0; i < meshes.Count; i++) {
                if (meshes[i] != null && meshes[i].RuntimeFlagsIndex == slot) {
                    mesh = meshes[i];
                    break;
                }
            }
            if (mesh == null) {
                // No such set for this creature. Falling back to the walk mesh would draw a live
                // body; leaving the field clear is the honest answer.
                return null;
            }

            int octant = OctantTowards(actor, camera);
            int column = EncounterActorPose.SpriteColumn(kind, octant, out mirrored);

            IReadOnlyList<MeshFaceRecord> faces = mesh.MeshFaces;
            if (faces == null || column < 0 || column >= faces.Count) {
                return null;
            }
            chosenMesh = mesh;
            chosenKind = kind;
            chosenColumn = column;
            return faces[column] as SpriteBMeshFace;
        }

        /// <summary>Which of the eight facings the actor presents to the camera right now.</summary>
        private static int OctantTowards(EncounterActorPlacement.Placed actor, Camera camera) {
            if (camera == null) {
                return 0;
            }
            Vector3 cam = camera.transform.position;
            // Back into BaK units and axes: ConvertPosition maps (x, y) -> (x, _, y) over the shared
            // world scale, so the inverse is the same two components scaled back up.
            long dx = (long)(cam.x * BakCoordinateConverter.WorldScale) - actor.WorldX;
            long dy = (long)(cam.z * BakCoordinateConverter.WorldScale) - actor.WorldY;
            ushort angle = GameData.Resources.Combat.ArenaFacing.HeadingTo(dx, dy);
            return EncounterActorPose.Octant(angle, actor.Facing);
        }

        /// <summary>
        /// The creature's bitmap set name, e.g. <c>MOR1.BMX</c>.
        /// </summary>
        /// <remarks>
        /// <b>The key is returned whole, <c>_CS&lt;n&gt;</c> suffix and all.</b> It is deliberately
        /// opaque here — <see cref="CreatureBitmaps"/> says so in as many words: a consumer sees
        /// "creature N uses these sprites" and never needs to know the colour-set mechanism exists.
        /// The loader parses it, because the loader is what has to act on it.
        ///
        /// <para>This used to strip the suffix and throw the number away, which is why a mordel and
        /// its recoloured twin drew identically (TASK-218).</para>
        /// </remarks>
        /// <summary>
        /// The sprite set a creature's SWING is drawn from — <c>SpriteKeys[1]</c>.
        /// </summary>
        /// <remarks>
        /// <b>The second of the three bitmaps, and nothing else in the port loads it.</b>
        /// <c>prepareCreatureAnimSet</c> @0x5ce0a maps animation slots 3..6 to bitmap 1, and slot 3
        /// is <c>combat_actor_play_anim_sprite3</c> — the swing <c>resolveSwingAttack</c> plays. The
        /// ordinary arena sprite is <see cref="SetNameFor"/>'s <c>SpriteKeys[0]</c>, which holds only
        /// the walk and the death.
        ///
        /// <para>Null when a creature has no second key, which leaves the swing unplayed rather than
        /// drawing the wrong art.</para>
        /// </remarks>
        public string AttackSetFor(int creatureNumber) {
            if (_bitmaps?.Creatures == null) {
                return null;
            }
            foreach (CreatureSprite c in _bitmaps.Creatures) {
                if (c.CreatureId == creatureNumber && c.SpriteKeys.Count > 1) {
                    return c.SpriteKeys[1];
                }
            }
            return null;
        }

        private string SetNameFor(int creatureNumber) {
            foreach (CreatureSprite c in _bitmaps.Creatures) {
                if (c.CreatureId != creatureNumber || c.SpriteKeys.Count == 0) {
                    continue;
                }
                return c.SpriteKeys[0];
            }
            return null;
        }
    }
}
