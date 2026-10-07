namespace BakAgain.World {
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.Audio;
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.World.Converters;
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Audio;
    using GameData.Resources.Combat;
    using GameData.Resources.Config;
    using GameData.Resources.Data;
    using GameData.Resources.World;
    using Microsoft.Extensions.Logging;
    using UnityEngine;

    /// <summary>
    /// The single owner of the heavyweight in-game world lifecycle (2026-07-12 architecture doc
    /// §3.1): builds the active zone scene + dedicated world camera + party movement from
    /// <see cref="GameSession"/>, and tears them down again. Only <see cref="GameFlow"/> sequences
    /// these calls (and wires <see cref="WorldCamera"/>/<see cref="Movement"/> into the travel
    /// screen before showing it); nothing else may build or destroy the world. (Extracted from the
    /// former InGameState's Enter/Exit body.)
    /// </summary>
    public sealed class WorldRuntime {
        private readonly ILogger<WorldRuntime> _logger;
        private readonly GameSession _gameSession;
        private readonly ZoneSceneBuilder _zoneSceneBuilder;
        private readonly IResourceProviderService _resources;
        private readonly IPreferencesService _preferences;
        private readonly MidiPlaybackManager _midi;
        private readonly GameClock _clock;
        private readonly BakAgain.UI.IDialogManager _dialogs;
        private readonly BakAgain.World.Scenes.LocationScenePlayer _locations;
        // Only ever used to hand HotspotService a LAZY IGameFlow: that interface depends on
        // this class, so anything eager here is a DI cycle.
        private readonly VContainer.IObjectResolver _resolver;

        // The zone track a fight interrupted, to come back to when it ends. PlayTrackAsync returns
        // the outgoing track, which is what MusicSelection.ForCombat's contract says the caller keeps.
        private int _trackBeforeCombat = GameData.Resources.Audio.MusicPlayback.NoTrack;

        // The G-key edge that flips the tactical overlay. Optional, so a container that does
        // not register it leaves the overlay simply un-toggleable rather than failing to build.
        private readonly BakAgain.UI.InputCore.ICombatOverlayInput _overlayInput;

        /// <summary>
        /// The original's <c>g_bCombatGridLinesEnabled</c> — the one flag the whole tactical
        /// overlay hangs off.
        /// </summary>
        /// <remarks>
        /// <b>Here rather than on <see cref="Encounters.ArenaGridOverlay"/> because the arena root
        /// is destroyed and rebuilt on every redraw</b>, taking that component with it. The
        /// original's is a global and survives a turn; a field on the overlay would switch itself
        /// off again the moment anything moved.
        /// </remarks>
        private bool _combatGridLinesEnabled;

        private Encounters.EncounterActorSpriteBuilder _encounterSprites;
        private Encounters.SpellVfx _spellVfx;

        /// <summary>
        /// Puts the chunk's encounter actors on screen.
        /// </summary>
        /// <remarks>
        /// <b>Failure here must not stop the world coming up</b> — the same rule the hotspot tables
        /// follow. A missing COMBAT.TBL or BNAMES.DAT means no monsters, not no zone.
        /// </remarks>
        private async UniTask DrawEncounterActorsAsync() {
            if (_hotspots == null || _zoneSceneBuilder?.RenderContext == null || _zoneRoot == null) {
                return;
            }

            _drawnChunk = (GameData.Resources.World.WorldPlacement.TileOf(_gameSession.PositionX),
                GameData.Resources.World.WorldPlacement.TileOf(_gameSession.PositionY));
            int generation = ++_encounterDrawGeneration;
            var placed = _hotspots.PlaceEncounterActors();
            if (placed.Count == 0) {
                return;
            }

            // *** EACH DRAW BUILDS INTO ITS OWN ROOT, AND ONLY THE LATEST ONE IS KEPT. *** Redraws are
            // fired and forgotten while the world build awaits its own draw, so two draws could find
            // no root, and both then built into the one the first created: every actor stood there
            // twice (SAVE17: one mordel, two objects).
            var root = new GameObject("EncounterActors");
            root.transform.SetParent(_zoneRoot.transform, worldPositionStays: false);
            _encounterSprites ??= new Encounters.EncounterActorSpriteBuilder(_resources, _logger);
            int drawn = await _encounterSprites.BuildAsync(
                placed, root.transform, _zoneSceneBuilder.RenderContext,
                _worldCamera, this);
            if (generation != _encounterDrawGeneration || root == null) {
                if (root != null) {
                    UnityEngine.Object.Destroy(root);
                }
                return;
            }
            if (_encounterActorsRoot != null) {
                UnityEngine.Object.Destroy(_encounterActorsRoot);
            }
            _encounterActorsRoot = root;
            root.SetActive(_encounterActorsVisible);
            _logger?.LogInformation(
                "Encounter actors: {Placed} placed, {Drawn} drawn.", placed.Count, drawn);
        }

        // The chunk the actors on screen were drawn for. A step onto another chunk redraws (see
        // AdvanceRoamingActors); kept here rather than read from HotspotService's placement cache,
        // which the camp check also fills without drawing anything.
        private (int x, int y)? _drawnChunk;

        // The chunk's own actors, under one root so a fight can take them off screen in one call.
        // They used to hang directly off the zone root, where there was no way to address them as a
        // group. Null until a chunk places any.
        private GameObject _encounterActorsRoot;

        // The latest draw; an older one that finishes after it throws its own root away.
        private int _encounterDrawGeneration;

        // Whether the actors are shown: a draw that lands while a fight hides them stays hidden.
        private bool _encounterActorsVisible = true;

        private async UniTask RedrawEncounterActorsAsync() {
            if (_encounterActorsRoot != null) {
                UnityEngine.Object.Destroy(_encounterActorsRoot);
                _encounterActorsRoot = null;
            }
            _hotspots?.InvalidatePlacement();
            await DrawEncounterActorsAsync();
        }

        /// <summary>
        /// Shows or hides the monsters the ZONE drew standing on the map.
        /// </summary>
        /// <remarks>
        /// <b>The creatures on the arena are the same ones standing on the map, so both being drawn
        /// means seeing each monster twice.</b> <see cref="DrawCombatantsAsync"/>'s remarks have said
        /// so since it was written; nothing acted on it, because the actors had no root to act on.
        ///
        /// <para>The original does not have this problem to solve: entering a fight calls
        /// <c>UnloadZone(1)</c> (IDA <c>combat_arena_mode_enter</c> @0x5f2c0), which frees the world
        /// tiles AND the zone shape table outright — the map's actors cannot be drawn because
        /// nothing of the map is left. We keep the zone loaded and hide them instead, which is the
        /// same picture without adopting a teardown we would only have to undo.</para>
        /// </remarks>
        private void ShowEncounterActors(bool visible) {
            _encounterActorsVisible = visible;
            if (_encounterActorsRoot != null) {
                _encounterActorsRoot.SetActive(visible);
            }
        }

        // The arena's own root, so the fight's actors can be dropped in one go without touching the
        // zone's. Null whenever no fight is on screen.
        private GameObject _arenaRoot;

        // What the arena's near-cull switched off, so leaving the fight can switch it back on.
        private readonly List<Renderer> _arenaCulledRenderers = new();
        private readonly List<Collider> _arenaCulledColliders = new();

        /// <summary>
        /// The underground room check: how many arena cells the view shows FLOOR at —
        /// <c>combatgrid_tiles_match_ctr_px</c> (CMBTGRID.C:343), which a fight needs 24 of.
        /// </summary>
        /// <remarks>
        /// <b>The original asks its framebuffer, so this asks the geometry the same question.</b> It
        /// renders the world from the arena's capture pose (height 800), then projects each cell's
        /// centre at ground level from the pose raised by 510 (WORLDHIT.C:556-559 leaves that view
        /// set up) and counts the cells whose pixel is floor colour. Here: project the same point
        /// from the raised pose, cast a ray through that pixel from the capture pose, and count it if
        /// the first surface hit faces up. The 30-row strip the original paints first
        /// (CMBTGRID.C:358) does not matter inside the view: world_render_view clears and redraws
        /// the viewport over it (WORLDHIT.C:512), and the measured counts agree.
        ///
        /// <para>Colliders are added to the meshes near the party only for the probe; the world has
        /// none of its own (only picking boxes, which are skipped).</para>
        /// </remarks>
        private int CountUndergroundFloorCells() {
            StartData start = _hotspots?.Start;
            int all = CombatGrid.Width * CombatGrid.Height;
            if (_worldCamera == null || _gameSession == null || _zoneRoot == null
                || !CombatArenaCamera.IsUsable(start, underground: true) || start.CombatGridCellSize <= 0) {
                return all;
            }

            int px = _gameSession.PositionX;
            int py = _gameSession.PositionY;
            int cellSize = start.CombatGridCellSize;
            int renderZ = CombatArenaCamera.HeightFor(start, underground: true)
                - CombatArenaCamera.UndergroundHeightRaise;
            Quaternion rotation = BakCoordinateConverter.ConvertRotation(
                (ushort)CombatArenaCamera.PitchFor(start, underground: true), 0,
                unchecked((ushort)_gameSession.Rotation));
            Vector3 renderEye = BakCoordinateConverter.ConvertPosition(px, py, renderZ);
            Vector3 projectEye = BakCoordinateConverter.ConvertPosition(px, py,
                renderZ + CombatArenaCamera.UndergroundHeightRaise);

            var probeObject = new GameObject("ArenaRoomProbe");
            Camera probe = probeObject.AddComponent<Camera>();
            probe.CopyFrom(_worldCamera);
            probe.enabled = false;
            // START.DAT's zoom, which g_active_window carries for travel and combat alike
            // (WorldProjection). Measured against the original at ambush 305: 94 of 104 cells, the
            // 10 this zoom puts off screen.
            probe.fieldOfView = TravelFov(null);
            probe.aspect = _worldCamera.aspect;

            // ponytail: colliders built per probe, one fight trigger at a time; cache per zone if it shows up in a profile.
            float reach = (CombatGroundCheck.ForwardOffset + (CombatGrid.Height + 4) * cellSize)
                / BakCoordinateConverter.WorldScale;
            var added = new HashSet<Collider>();
            foreach (MeshFilter mesh in _zoneRoot.GetComponentsInChildren<MeshFilter>()) {
                Renderer renderer = mesh.GetComponent<Renderer>();
                if (mesh.sharedMesh == null || renderer == null || !renderer.enabled
                    || renderer.bounds.SqrDistance(renderEye) > reach * reach) {
                    continue;
                }
                var collider = mesh.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh.sharedMesh;
                added.Add(collider);
            }
            Physics.SyncTransforms();

            int floor = 0, offScreen = 0, noHit = 0, notFloor = 0;
            try {
                for (var column = 0; column < CombatGrid.Width; column++) {
                    for (var row = 0; row < CombatGrid.Height; row++) {
                        (int across, int away) = (column * cellSize + (cellSize >> 1) - 0x4b0,
                            row * cellSize + (cellSize >> 1) + CombatGroundCheck.ForwardOffset);
                        var (dx, dy) = Collision.ProximityMath.Rotate(across, away, _gameSession.Rotation);
                        Vector3 point = BakCoordinateConverter.ConvertPosition(px + dx, py + dy, 0);

                        probe.transform.SetPositionAndRotation(projectEye, rotation);
                        Vector3 viewport = probe.WorldToViewportPoint(point);
                        if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f
                            || viewport.y > 1f) {
                            offScreen++;
                            continue;
                        }

                        probe.transform.SetPositionAndRotation(renderEye, rotation);
                        Ray ray = probe.ViewportPointToRay(viewport);
                        RaycastHit[] hits = Physics.RaycastAll(ray, probe.farClipPlane,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                        RaycastHit? first = null;
                        foreach (RaycastHit hit in hits) {
                            if (added.Contains(hit.collider) && (first == null || hit.distance < first.Value.distance)) {
                                first = hit;
                            }
                        }
                        if (!first.HasValue) {
                            noHit++;
                        } else if (first.Value.normal.y > 0.7f) {
                            floor++;
                        } else {
                            notFloor++;
                        }
                    }
                }
            } finally {
                foreach (Collider collider in added) {
                    Object.Destroy(collider);
                }
                Object.Destroy(probeObject);
            }
            _logger?.LogDebug($"Underground room probe: {floor} of {all} cells show floor "
                + $"(off screen {offScreen}, nothing hit {noHit}, "
                + $"not floor {notFloor}).");
            return floor;
        }

        /// <summary>The placements on one map tile, in file order (TASK-652).</summary>
        private IEnumerable<GameData.Resources.Combat.ArenaScenery.Placement> SceneryOnTile(int tileX, int tileY) {
            foreach (Collision.ProximityRecord r in _zoneSceneBuilder.Collision.Placements) {
                if (r.TileX == tileX && r.TileY == tileY && r.Entry?.Dat != null) {
                    yield return new GameData.Resources.Combat.ArenaScenery.Placement(
                        (int)r.Entry.Dat.EntityType, r.X, r.Y);
                }
            }
        }

        /// <summary>
        /// While a fight is on, the zone's objects standing on the arena are not drawn — a minimum
        /// draw distance for scenery, set just beyond the grid.
        /// </summary>
        /// <remarks>
        /// <b>Nothing may come between the camera and a combatant.</b> The original gets that for
        /// free: <c>combat_arena_mode_enter</c> opens with <c>zone_teardown(1)</c> and the fight is
        /// drawn over a still capture (<c>combat_captureArenaBackdrop</c> @0x2210b), so the zone's
        /// scenery survives as pixels with no depth. We keep the world in 3D, so the near objects
        /// have to go.
        ///
        /// <para><b>Drawing them BEHIND the actors is not the same thing and was tried first.</b>
        /// Lifting the actors out of the depth test hides the symptom and leaves a tree standing at
        /// their feet with the sprites pasted over it — and it would let an actor show through a
        /// hillside as well. Reverted 2026-09-09 on the owner's call.</para>
        ///
        /// <para>The radius comes from the grid itself — the farthest of the four corner cells plus
        /// a cell of margin — so it tracks <c>StartData.CombatGridCellSize</c> rather than repeating
        /// a number. The party does not move during a fight, so this runs once on entry.</para>
        ///
        /// <para><b>Colliders go with the renderers.</b> An invisible house that still swallows the
        /// pick ray is the same defect wearing a hat; combat movement is grid-based and does not
        /// consult these, so nothing else depends on them for the duration.</para>
        /// </remarks>
        private void SetArenaSceneryCull(bool on) {
            if (!on) {
                foreach (Renderer r in _arenaCulledRenderers) {
                    if (r != null) r.enabled = true;
                }
                foreach (Collider c in _arenaCulledColliders) {
                    if (c != null) c.enabled = true;
                }
                _arenaCulledRenderers.Clear();
                _arenaCulledColliders.Clear();

                return;
            }

            StartData start = _hotspots?.Start;
            if (_zoneRoot == null || _gameSession == null || start == null) {
                return;
            }
            int cellSize = start.CombatGridCellSize;
            if (cellSize <= 0 || _worldCamera == null) {
                return;
            }

            // Measured along the camera's own view axis, because that is what "between the camera
            // and a combatant" means. Depth is affine in position, so its minimum over the grid is
            // reached at one of the four corner cells -- the same four Reach() measures.
            Vector3 camPos = _worldCamera.transform.position;
            Vector3 camForward = _worldCamera.transform.forward;
            float arenaDepth = float.MaxValue;
            foreach ((int Column, int Row) corner in ArenaCornerCells) {
                float d = Vector3.Dot(
                    ArenaCellPosition(corner.Column, corner.Row, cellSize) - camPos, camForward);
                if (d < arenaDepth) {
                    arenaDepth = d;
                }
            }

            List<GameData.Resources.Combat.ArenaScenery.Placement> notInFight = SceneryMissingFromFight(cellSize);
            foreach (WorldEntity entity in _zoneRoot.GetComponentsInChildren<WorldEntity>()) {
                float depth = Vector3.Dot(entity.transform.position - camPos, camForward);
                if (!StandsBetweenCameraAndArena(entity.EntityType, depth, arenaDepth)
                    && !IsAmong(entity, notInFight)) {
                    continue;
                }
                foreach (Renderer r in entity.GetComponentsInChildren<Renderer>()) {
                    if (!r.enabled) continue;
                    r.enabled = false;
                    _arenaCulledRenderers.Add(r);
                }
                foreach (Collider c in entity.GetComponentsInChildren<Collider>()) {
                    if (!c.enabled) continue;
                    c.enabled = false;
                    _arenaCulledColliders.Add(c);
                }
            }
            _logger?.LogDebug(
                "Arena scenery cull: {Count} renderers hidden in front of the arena's near edge "
                + "at {Depth} units.",
                _arenaCulledRenderers.Count, arenaDepth);
        }

        /// <summary>
        /// The placements the original leaves out of the fight: arena scenery its backdrop drops and
        /// its combatant table does not re-add (<see cref="GameData.Resources.Combat.ArenaScenery.HiddenInFight"/>,
        /// TASK-790) — a tree in the wedge nearest the camera stood on the board and hid an opponent.
        /// </summary>
        private List<GameData.Resources.Combat.ArenaScenery.Placement> SceneryMissingFromFight(int cellSize) {
            var missing = new List<GameData.Resources.Combat.ArenaScenery.Placement>();
            if (_zoneIsUnderground || _zoneSceneBuilder?.Collision == null) {
                return missing; // the underground fight stands inside its corridor, see below
            }
            long px = _gameSession.PositionX, py = _gameSession.PositionY;
            int heading = _gameSession.Rotation;
            var kept = GameData.Resources.Combat.ArenaScenery.Kept(
                SceneryOnTile(GameData.Resources.World.WorldPlacement.TileOf(px),
                    GameData.Resources.World.WorldPlacement.TileOf(py)),
                px, py, heading, cellSize);
            foreach (Collision.ProximityRecord r in _zoneSceneBuilder.Collision.Placements) {
                if (r.Entry?.Dat == null) {
                    continue;
                }
                var placement = new GameData.Resources.Combat.ArenaScenery.Placement(
                    (int)r.Entry.Dat.EntityType, r.X, r.Y);
                if (GameData.Resources.Combat.ArenaScenery.HiddenInFight(placement, kept, px, py, heading, cellSize)) {
                    missing.Add(placement);
                }
            }
            return missing;
        }

        /// <summary>Whether a scene entity is one of <paramref name="placements"/>: same kind, same
        /// spot to within the round trip through Unity space.</summary>
        private static bool IsAmong(WorldEntity entity,
            List<GameData.Resources.Combat.ArenaScenery.Placement> placements) {
            if (placements.Count == 0) {
                return false;
            }
            (int x, int y) = BakCoordinateConverter.ToBakXY(entity.transform.position);
            int kind = (int)entity.EntityType;
            foreach (GameData.Resources.Combat.ArenaScenery.Placement p in placements) {
                if (p.Kind == kind && System.Math.Abs(p.X - x) <= 2 && System.Math.Abs(p.Y - y) <= 2) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether an entity at <paramref name="depth"/> along the camera's view axis can come
        /// between the camera and an arena whose nearest cell is at <paramref name="arenaDepth"/>.
        /// </summary>
        /// <remarks>
        /// <b>MineCorridor is never "between", whatever its depth says.</b> Underground the entities
        /// around the arena ARE the corridor the fight stands inside — hollow shells whose walls run
        /// beside the camera and whose floor is under it, so no position test can tell them from an
        /// obstacle. Culling them emptied the room and left the combatants on black (TASK-600: six
        /// m_halwp1 pieces at 9..45 units, renderer disabled, the tile itself untouched).
        /// MineCorridor names exactly that shell — 23 of Z10's entries carry it, no surface zone has
        /// one, and it is in the walkable set, which is how the party came to be standing inside it.
        ///
        /// <para>The other two clauses are geometry: behind the camera, or no nearer than the closest
        /// cell a combatant can stand on, and either way it cannot obscure one.</para>
        /// </remarks>
        internal static bool StandsBetweenCameraAndArena(
            GameData.Resources.World.WorldEntityType type, float depth, float arenaDepth) {
            if (type == GameData.Resources.World.WorldEntityType.MineCorridor) {
                return false;
            }

            return depth > 0f && depth < arenaDepth;
        }

        /// <summary>The grid's four corner cells — the extremes any affine measure of it reaches.</summary>
        private static readonly (int Column, int Row)[] ArenaCornerCells = {
            (0, 0), (CombatGrid.Width - 1, 0),
            (0, CombatGrid.Height - 1), (CombatGrid.Width - 1, CombatGrid.Height - 1),
        };

        /// <summary>
        /// Where an arena cell's centre lands in world space, by the same route that puts the
        /// combatants there — <see cref="CombatArenaPlacement.CellOffset"/> rotated by the party's
        /// heading and added to its position — so the cull cannot drift from the board it clears.
        /// </summary>
        private Vector3 ArenaCellPosition(int column, int row, int cellSize) {
            (int across, int away) = CombatArenaPlacement.CellOffset(column, row, cellSize);
            var (dx, dy) = Collision.ProximityMath.Rotate(across, away, _gameSession.Rotation);

            return _zoneRoot.transform.TransformPoint(
                Converters.BakCoordinateConverter.ConvertPosition(
                    _gameSession.PositionX + dx, _gameSession.PositionY + dy, 0));
        }

        // Which of START.DAT's two arena poses this zone takes. Read from Z##DEF.DAT's first field,
        // the same place movement and entity stamping read it, so they cannot disagree.
        private bool _zoneIsUnderground;

        // Z##DEF.DAT's default camera pitch, the one PartyMovement flies the walking eye at.
        private int _zoneDefaultCameraPitch;

        /// <summary>
        /// Puts the fight's enemies on the arena.
        /// </summary>
        /// <remarks>
        /// <b>Their own root, torn down whole.</b> Combatants come and go with the fight while the
        /// zone's encounter actors persist, so mixing them under one parent would mean deciding per
        /// object what survives — and the enemies drawn here are the SAME creatures the zone already
        /// drew standing on the map. Both are visible at once otherwise.
        ///
        /// <para><b>Failure here must not stop the fight</b>, the same rule the zone's actors follow:
        /// a missing COMBAT.TBL means no visible monsters, not a wedged encounter. The turn loop runs
        /// either way, which is what it did before anything was drawn at all.</para>
        /// </remarks>
        /// <summary>How long a move that was not a walk (a shove, a knock-back) takes to slide.</summary>
        /// <remarks>
        /// A walk is timed by the original's own rate instead — see
        /// <see cref="GameData.Resources.Combat.CombatWalk.StepFrames"/>. This is about one straight
        /// step at that rate.
        /// </remarks>
        private const float SlideSeconds = 0.18f;

        /// <summary>Gait frames advanced per cell crossed.</summary>
        /// <remarks>
        /// ponytail: a flat count per cell. Three is one half of
        /// <see cref="GameData.Resources.World.EncounterActorPose.Advance"/>'s ping-pong, so each step
        /// reads as a stride. The original resets the walk animation on every step
        /// (CMBTAI.C:56, :172) rather than running it on; that look was not asked for.
        /// </remarks>
        private const int GaitStepsPerCell = 3;

        /// <summary>Guards against a second redraw arriving mid-slide.</summary>
        private bool _sliding;

        /// <summary>
        /// Moves the sprites that changed cell to where they now stand, then rebuilds the arena.
        /// </summary>
        /// <remarks>
        /// <b>Before this, a combatant did not move — it appeared in the next cell.</b> Every redraw
        /// path destroys the arena and builds it again from the current grid, so a step and a
        /// teleport rendered identically. That is what made a walk cycle pointless: the frame would
        /// be the phase of a movement nobody was being shown. See TASK-103.
        ///
        /// <para><b>It slides EVERY sprite that moved, not the one that was clicked.</b> The seam is
        /// the redraw, which is also what an enemy's turn goes through, so monsters step for the same
        /// reason the party does and nothing has to tell this which actor acted.</para>
        ///
        /// <para><b>It only works because the move arm stopped redrawing twice.</b> The first attempt
        /// at this failed live with no interpolation at all: <c>ResolveMoveClick</c> called
        /// <c>RefreshCombatHud</c> — whose first line is <c>RedrawArena</c> — and then invoked the
        /// redraw again itself. The first rebuild had already put the sprite on its destination, so
        /// this found nothing to move. A second redraw is not just wasted work here; it is
        /// indistinguishable from the feature being broken.</para>
        ///
        /// <para><b>The rebuild still happens</b> — this only fills the gap before it. Anything the
        /// rebuild does that a transform cannot (a corpse appearing, a facing changing, a combatant
        /// leaving the field) is unaffected, which is why the slide can afford to know nothing but
        /// positions.</para>
        ///
        /// <para><b>Reentrancy is skipped rather than queued.</b> A redraw arriving mid-slide means
        /// the state moved on again; sliding to a position that is already stale would show a step
        /// that never happened, so the second caller goes straight to the rebuild.</para>
        /// </remarks>
        /// <summary>Whether the built zone is underground — the original's game mode 2.</summary>
        public bool Underground => _zoneSceneBuilder?.Underground ?? false;

        /// <summary>
        /// A trapped chest going off in the party's face — <c>WCURSOR.C:630-680</c>: BOOM.BMX frames
        /// 0, 1, 2, 1, 0 over the container, the world flashing toward pen 0x2C (w 48, 32, 48, 63),
        /// then two plain frames. Awaitable: the original holds the detonation line until it ends.
        /// </summary>
        /// <remarks>
        /// Sized by the three sprite faces of the shape the original draws it through (zone entry
        /// 0xB6, <c>boom</c>; 0x8E underground). ponytail: one
        /// frame is one combat frame (~59 ms) — the original's world frame is unpaced.
        /// </remarks>
        public async UniTask PlayChestExplosionAsync(int bakX, int bakY) {
            WorldEntityRenderContext ctx = _zoneSceneBuilder?.RenderContext;
            if (ctx == null || _zoneRoot == null) {
                return;
            }
            // *** THE BOOM IS A MODEL, AND ITS FACES SIZE IT. *** actoroverlay_render_actor
            // (ACTOROVL.C) draws shape 0xb6 (0x8e underground) over the chest with the sprite table
            // swapped to BOOM, frame = state - 1; each of its three sprite faces carries its own
            // SizeScale and anchor, so the spark (frame 0) is little more than half the burst. A
            // fixed width drew all three three-to-four times the original's size.
            ZoneTableEntry shape = _zoneSceneBuilder.Table?.Entries is { } entries
                && ChestTrap.ExplosionShape(_zoneSceneBuilder.Underground) is int id && id < entries.Count
                ? entries[id] : null;
            SpriteBMeshFace[] faces = shape?.Dat?.Lods.FirstOrDefault()?.Meshes.FirstOrDefault()?.MeshFaces.OfType<SpriteBMeshFace>().ToArray();
            if (faces == null || faces.Length < 3) {
                return;
            }
            var frames = new Texture2D[3];
            for (var i = 0; i < frames.Length; i++) {
                frames[i] = await ctx.LoadSpriteTextureAsync($"BOOM.BMX#{i}", _zoneRoot);
            }
            if (frames[0] == null) {
                return;
            }
            var boom = new GameObject("ChestExplosion");
            boom.transform.SetParent(_zoneRoot.transform, true);
            var filter = boom.AddComponent<MeshFilter>();
            var renderer = boom.AddComponent<MeshRenderer>();
            boom.AddComponent<BillboardSprite>();
            Vector3 ground = _zoneRoot.transform.TransformPoint(BakCoordinateConverter.ConvertPosition(bakX, bakY, 0));
            ground.y = Physics.Raycast(ground + Vector3.up * 1000f, Vector3.down, out RaycastHit hit, 2000f)
                ? hit.point.y : ground.y;
            boom.transform.position = ground;
            Color flash = ctx.Palette != null && ctx.Palette.Length > 0x2c ? ctx.Palette[0x2c] : Color.white;
            float frameSeconds = (float)GameData.Resources.Combat.SpellVisuals.FrameSeconds;
            (int Frame, int Weight)[] steps = { (0, 63), (1, 48), (2, 32), (1, 48), (0, 63) };
            foreach ((int f, int w) in steps) {
                Texture2D tex = frames[f] ?? frames[0];
                (Mesh mesh, Vector3 scale) = TblSpriteConverter.BuildBillboard(
                    faces[f], Mathf.Abs(shape.Dat.Extent), tex.width, tex.height, shape.Dat.SpriteAnchorRise(faces[f]));
                ctx.TrackMesh(mesh);
                filter.sharedMesh = mesh;
                renderer.sharedMaterial = ctx.GetSpriteMaterial(tex);
                boom.transform.localScale = scale;
                await UniTask.Delay(System.TimeSpan.FromSeconds(frameSeconds));
                Encounters.SpellVfx.SetWorldFlash(flash, 1f - w / 63f);
            }
            Object.Destroy(boom);
            Encounters.SpellVfx.SetWorldFlash(flash, 0f);
            await UniTask.Delay(System.TimeSpan.FromSeconds(frameSeconds * 2));
        }

        /// <summary>The arena's spell-visual player (TASK-117), built on first use.</summary>
        private Encounters.SpellVfx SpellVfx => _spellVfx ??= NewSpellVfx();

        private Encounters.SpellVfx NewSpellVfx() {
            Encounters.SpellVfx.LoadRemapsAsync(_resources, this).Forget();
            return CreateSpellVfx();
        }

        private Encounters.SpellVfx CreateSpellVfx() => new Encounters.SpellVfx(
            SpriteOf,
            () => _zoneRoot != null ? _zoneRoot.transform : null,
            () => _zoneSceneBuilder?.RenderContext?.Palette,
            () => _worldCamera,
            (id, parent) => _encounterSprites == null || parent == null
                ? UniTask.FromResult<GameObject>(null)
                : _encounterSprites.BuildEffectSpriteAsync(id, _gameSession.CurrentZone, parent,
                    _zoneSceneBuilder.RenderContext, this),
            () => BakCoordinateConverter.ConvertRotation(0, 0, unchecked((ushort)_gameSession.Rotation)),
            who => {
                var points = new List<Vector3>();
                foreach ((long x, long y) in _hotspots.CrystalRunThrough(who)) {
                    Vector3 p = _zoneRoot.transform.TransformPoint(BakCoordinateConverter.ConvertPosition((int)x, (int)y, 0));
                    // The arena floor is under the zone root; sample it rather than assume 0.
                    p.y = SpriteOf(who) is { } s ? s.position.y : p.y;
                    points.Add(p);
                }
                return points;
            });

        /// <summary>The live sprite drawn for a combatant — its standing sprite, else its body.</summary>
        /// <remarks>Looked up every time rather than cached: a redraw replaces every sprite, and an
        /// effect that outlives a turn has to follow its combatant onto the new one.</remarks>
        private Transform SpriteOf(GameData.Resources.Combat.Combatant who) {
            if (who == null || _arenaRoot == null || _hotspots == null) {
                return null;
            }
            foreach (Encounters.ArenaCombatant m in _arenaRoot.GetComponentsInChildren<Encounters.ArenaCombatant>()) {
                if (_hotspots.CombatantFor(m.RosterSlot, m.PartyMember) == who) {
                    return m.transform;
                }
            }
            foreach (Encounters.ArenaCorpse m in _arenaRoot.GetComponentsInChildren<Encounters.ArenaCorpse>()) {
                if (_hotspots.CombatantFor(m.RosterSlot, m.PartyMember) == who) {
                    return m.transform;
                }
            }
            return null;
        }

        /// <summary>
        /// A lingering spell's look on each combatant carrying one, and the shake of anyone standing
        /// in a held tile — the per-frame arms of <c>CACTOR.C:903-1076</c>.
        /// </summary>
        /// <remarks>
        /// <b>Keyed by the STATUS's spell</b>, as the original's renderer is: Hocho's Haven draws
        /// its box, Skin of the Dragon twinkles, Grief and Thoughts Like Clouds glow. Put on the
        /// fresh sprites every redraw, so an expired effect simply is not re-attached.
        /// </remarks>
        private void AttachLingeringSpellVisuals() {
            BakAgain.Combat.CombatRuntime combat = _hotspots?.Combat;
            if (combat?.Encounter == null || _arenaRoot == null) {
                return;
            }
            foreach (Encounters.ArenaCombatant marker in
                     _arenaRoot.GetComponentsInChildren<Encounters.ArenaCombatant>()) {
                GameData.Resources.Combat.Combatant c = _hotspots.CombatantFor(marker.RosterSlot, marker.PartyMember);
                if (c == null) {
                    continue;
                }
                // Wrath of Killian's held tile: kind 1 in the terrain word (CSPELL.C:1362, CACTOR.C:964).
                if (combat.Grid != null
                    && (int)combat.Grid.TerrainAt(c.X, c.Y) == 1
                    && combat.Grid.EffectTimerAt(c.X, c.Y) != GameData.Resources.Combat.CombatGrid.NoEffect) {
                    SpellVfx.Attach(marker.transform, default, shake: true);
                }
                foreach (GameData.Resources.Spells.ActiveSpellEffect effect in combat.Encounter.Effects.EffectsOf(c)) {
                    GameData.Resources.Spells.Spell spell = null;
                    combat.Spells?.Spells?.TryGetValue(effect.SpellNumber, out spell);
                    GameData.Resources.Combat.LingeringVisual look =
                        GameData.Resources.Combat.SpellVisuals.Lingering(spell);
                    if (look.Kind != GameData.Resources.Combat.LingeringVisualKind.None) {
                        SpellVfx.Attach(marker.transform, look);
                    }
                }
            }
        }

        /// <summary>
        /// The numbers struck combatants float — the damage, "miss" for a blow that did nothing, or a
        /// heal's gain (COMBAT.C:382-390, CSPELL.C:1217, CACTOR.C:973). Played once, on the redraw after the blow, like the swing.
        /// </summary>
        private void FloatDamageNumbers() {
            Color[] palette = _zoneSceneBuilder?.RenderContext?.Palette;
            BakAgain.UI.InGame.InGameScreen screen =
                Object.FindAnyObjectByType<BakAgain.UI.InGame.InGameScreen>();
            if (screen == null || palette == null) {
                return;
            }
            foreach (Encounters.ArenaCombatant marker in _arenaRoot.GetComponentsInChildren<Encounters.ArenaCombatant>()) {
                GameData.Resources.Combat.Combatant c = _hotspots.CombatantFor(marker.RosterSlot, marker.PartyMember);
                if (c?.DamageFloat is not { } dealt) {
                    continue;
                }
                c.DamageFloat = null;
                // Pens: the countdown steps BEFORE the pen is read (CACTOR.C:986-995), so a number
                // walks 0x81..0x88; "miss" carries value 1 and a NEGATIVE countdown, so the same
                // `0x88 - frames` walks it 0x8F..0x88. A heal's gain is negative and drawn as its
                // magnitude in `0xEF - frames` (CACTOR.C:992-995), walking 0xE8..0xEF.
                var pens = new List<Color>();
                for (int left = GameData.Resources.Combat.Combatant.DamageFloatFrames - 1; left >= 0; left--) {
                    int pen = dealt > 0 ? 0x88 - left : dealt < 0 ? 0xEF - left : 0x88 + left;
                    pens.Add(pen < palette.Length ? palette[pen] : Color.white);
                }
                Renderer r = marker.GetComponent<Renderer>();
                Vector3 top = r != null ? new Vector3(r.bounds.center.x, r.bounds.max.y, r.bounds.center.z)
                                        : marker.transform.position;
                screen.FloatText(top, dealt == 0 ? "miss" : System.Math.Abs(dealt).ToString(), pens);
            }
        }

        /// <summary>
        /// The spell tiles a cell-aimed spell leaves — Mirrorwall's wall (COMBAT.TBL 12, <c>mrwall</c>)
        /// and Gambit of the Eight's trap (13, <c>blob</c>), drawn on their cell every frame
        /// (CACTOR.C:1265-1272).
        /// </summary>
        private async UniTask DrawSpellTilesAsync() {
            foreach ((int shape, long worldX, long worldY) in _hotspots.PlaceSpellTiles()) {
                GameObject tile = await _encounterSprites.BuildEffectSpriteAsync(
                    shape, _gameSession.CurrentZone, _arenaRoot.transform,
                    _zoneSceneBuilder.RenderContext, this);
                if (tile != null) {
                    tile.transform.localPosition = BakCoordinateConverter.ConvertPosition((int)worldX, (int)worldY, 0);
                }
            }
        }

        /// <summary>Build one effect sprite and fly it between two arena positions.</summary>
        /// <remarks>
        /// Fire-and-forget for the same reason the collapse and the swing are: the arena has already
        /// been drawn and the model has already resolved, so nothing downstream is waiting on the
        /// picture. A failed build is silent — a missing bitmap must not cost the player their shot.
        /// </remarks>
        private async UniTaskVoid FlyEffectAsync(int effectId, Vector3 from, Vector3 to,
            int arcSteps, int arcDelta, float seconds = Encounters.ProjectileFlight.Seconds) {
            if (_arenaRoot == null || _zoneSceneBuilder?.RenderContext == null) {
                return;
            }
            GameObject sprite = await _encounterSprites.BuildEffectSpriteAsync(
                effectId, _gameSession.CurrentZone, _arenaRoot.transform,
                _zoneSceneBuilder.RenderContext, this);
            if (sprite == null) {
                return;
            }
            // *** A SPELL FLIES AT CHEST HEIGHT, NOT ALONG THE GROUND. *** world_rndr_ranged_attack_anim
            // starts every shape at z=200 except the growing whirlwind (4) and a flying creature
            // (WORLDHIT.C:641-645); the thrown rock keeps its own arc (ThrownRockFlight).
            if (effectId != GameData.Resources.Combat.CombatEffectSprite.Shot && effectId != 4) {
                Vector3 lift = Vector3.up * (GameData.Resources.Combat.SpellHeights.Flight / BakCoordinateConverter.WorldScale);
                from += lift;
                to += lift;
            }
            // *** WHAT THE SHOT FLIES OVER MATTERS FOR EXACTLY ONE SPELL. *** Flamecast passing over
            // a standing crystal collapses its chain (TASK-270); every other effect id reports and
            // is ignored. The sweep's per-cell memory is reset here, as the shot launches, so one
            // flight cannot inherit the previous one's last cell.
            _hotspots.BeginProjectileSweep();
            // *** ONLY THE THROWN ROCK ARCS. *** arcSteps is 0 for every other effect, which is the
            // model saying "flat flight" — see HotspotService.ArmRockArc.
            System.Collections.Generic.IReadOnlyList<GameData.Resources.Combat.ThrownRockFlight.Step> arc =
                arcSteps > 0
                    ? GameData.Resources.Combat.ThrownRockFlight.Arc(arcSteps, arcDelta)
                    : null;
            sprite.AddComponent<Encounters.ProjectileFlight>().Play(from, to, point => {
                if (_hotspots.ProjectilePassedOver(point, effectId)) {
                    // The collapse changed the puzzle's elements and ground, and the arena is drawn
                    // from those — so it has to be rebuilt rather than left showing a chain that no
                    // longer exists.
                    DrawCombatantsAsync().Forget();
                }
            }, arc, () => MenuSoundService.Instance?.Play(
                GameData.Resources.Combat.ThrownRockFlight.SkipCue), seconds);
        }

        /// <summary>A FRACTIONAL arena cell (cell units, .5 = a centre) in the arena root's space —
        /// <see cref="ArenaCellPosition"/> for a point that need not be a cell centre, such as where
        /// a missed shot leaves the board.</summary>
        private Vector3 ArenaCellLocal(float column, float row, float height) {
            int cellSize = _hotspots?.Combat?.ArenaCellSize ?? 300;
            (int across0, int away0) = CombatArenaPlacement.CellOffset(0, 0, cellSize);
            int across = (int)(across0 - cellSize / 2 + column * cellSize);
            int away = (int)(away0 - cellSize / 2 + row * cellSize);
            var (dx, dy) = Collision.ProximityMath.Rotate(across, away, _gameSession.Rotation);
            Vector3 p = BakCoordinateConverter.ConvertPosition(
                (int)(_gameSession.PositionX + dx), (int)(_gameSession.PositionY + dy), 0);
            p.y = height;
            return p;
        }

        /// <summary>
        /// Turns the acting combatant toward a floor point, re-aiming its live sprite.
        /// </summary>
        /// <remarks>
        /// <b>No arena rebuild.</b> <see cref="Encounters.DirectionalSprite"/> resolves its column
        /// from the facing every frame, so re-aiming is a <c>SetPose</c> on the sprite already on
        /// screen. Going through <see cref="DrawCombatantsAsync"/> instead would tear down and
        /// rebuild every combatant on each cursor move — and would reset the gait, which is the very
        /// twitch the original's already-facing early-out exists to avoid.
        ///
        /// <para>The seam answers null unless the facing actually changed, so this does nothing on
        /// the overwhelming majority of frames.</para>
        /// </remarks>
        private void FaceActingCombatantAt(Vector3 point) {
            GameData.Resources.Combat.Combatant turned =
                _hotspots?.TurnActingTowardCursor(point);
            if (turned == null || _arenaRoot == null) {
                return;
            }
            foreach (Encounters.ArenaCombatant marker in
                     _arenaRoot.GetComponentsInChildren<Encounters.ArenaCombatant>()) {
                if (_hotspots.CombatantFor(marker.RosterSlot, marker.PartyMember) != turned) {
                    continue;
                }
                var sprite = marker.GetComponent<Encounters.DirectionalSprite>();
                if (sprite != null) {
                    (long X, long Y, short _) = sprite.RecordedPose;
                    sprite.SetPose(X, Y, _hotspots.ArenaSpriteYawFor(turned));
                }
                return;
            }
        }

        /// <summary>
        /// Draws the trap puzzle's props into the arena — the crystals, diamonds, cannons and
        /// burnt-out crystals the fight is already being played against.
        /// </summary>
        /// <remarks>
        /// <b>The rules were live long before anything drew them.</b> A trap encounter blocked cells,
        /// fired cannons and played the crystal sound over an arena that showed none of it, which
        /// reads worse than "unimplemented": the fight behaves as though objects are there and the
        /// screen says they are not. TASK-321.
        ///
        /// <para>Every prop is a <c>COMBAT.TBL</c> row indexed by its own element id, so this reuses
        /// <see cref="Encounters.EncounterActorSpriteBuilder.BuildEffectSpriteAsync"/> unchanged —
        /// it already resolves a row to either a billboard or a solid, which is exactly the split
        /// here: 7 <c>redcry</c> and 8 <c>greencry</c> carry a sprite face, while 9/10/11
        /// (<c>mancrys</c>, <c>wirecrys</c>, <c>frpcnon</c>) are polygon solids. Its name says
        /// "effect" because a projectile was the first caller, not because it is limited to one.</para>
        ///
        /// <para><b>Rotation is applied after the build and only bites on the solids.</b> A billboard
        /// re-aims at the camera every frame, so setting a crystal's rotation is harmless rather than
        /// wrong; a cannon is a seven-faced solid and points where it is turned.</para>
        ///
        /// <para>Rebuilt with the rest of the arena on every redraw, so a crystal that collapses
        /// mid-fight disappears on the next turn without anything tracking it.</para>
        /// </remarks>
        private async UniTask DrawTrapPropsAsync() {
            if (_arenaRoot == null || _hotspots == null || _zoneSceneBuilder?.RenderContext == null) {
                return;
            }

            GameData.Resources.Combat.CombatGrid grid = _hotspots.Combat?.Puzzle?.Grid;
            BeginPropEmergence();

            var rising = new List<(Transform Prop, int GridX, int GridY)>();
            foreach ((int elementId, long worldX, long worldY, ushort facing, int gridX, int gridY)
                     in _hotspots.PlaceTrapProps()) {
                GameObject prop = await _encounterSprites.BuildEffectSpriteAsync(
                    elementId, _gameSession.CurrentZone, _arenaRoot.transform,
                    _zoneSceneBuilder.RenderContext, this);
                if (prop == null) {
                    continue;
                }
                prop.transform.localPosition = BakCoordinateConverter.ConvertPosition(
                    (int)worldX, (int)worldY, -_propEmergence.BurialAt(grid, gridX, gridY));
                prop.transform.localRotation =
                    BakCoordinateConverter.ConvertRotation(0, 0, facing);
                if (_propEmergence.Running) {
                    rising.Add((prop.transform, gridX, gridY));
                }
            }

            if (rising.Count > 0) {
                AnimatePropEmergenceAsync(rising, grid).Forget();
            }
        }

        /// <summary>
        /// Arm the props' emergence the first time this encounter's arena is drawn.
        /// </summary>
        /// <remarks>
        /// <b>Once per encounter, not once per redraw.</b> DrawCombatantsAsync runs every turn, and
        /// re-rolling there would re-bury the props whenever anybody moved. The original arms it in
        /// the arena-entry transition (<c>combat_arena_round_trans_show</c>) for the same reason.
        /// </remarks>
        private void BeginPropEmergence() {
            long encounter = _hotspots.FightingEncounter;
            if (_propEmergenceFor == encounter) {
                return;
            }
            _propEmergenceFor = encounter;
            _propEmergence.Begin(_hotspots.Combat?.Puzzle, n => UnityEngine.Random.Range(0, n));
        }

        /// <summary>
        /// Lift the props out of the ground, one cutscene frame per rendered frame.
        /// </summary>
        /// <remarks>
        /// <b>The buried part needs no clip.</b> The original clips its viewport at the projected
        /// ground point because it has no depth buffer; here the arena's opaque floor and the depth
        /// test hide whatever is below it, so the prop simply rises into view.
        ///
        /// <para><b>Bails when the arena is rebuilt.</b> The transforms belong to GameObjects the
        /// next redraw destroys, so the loop checks its own root is still the live one — otherwise a
        /// turn taken mid-animation would leave it writing to destroyed transforms.</para>
        /// </remarks>
        private async UniTaskVoid AnimatePropEmergenceAsync(
            List<(Transform Prop, int GridX, int GridY)> rising,
            GameData.Resources.Combat.CombatGrid grid) {
            GameObject owner = _arenaRoot;
            while (_propEmergence.Running && owner != null && ReferenceEquals(owner, _arenaRoot)) {
                await UniTask.Yield();
                _propEmergence.Advance(grid, Time.deltaTime);
                foreach ((Transform prop, int gridX, int gridY) in rising) {
                    if (prop == null) {
                        continue;
                    }
                    Vector3 p = prop.localPosition;
                    // BaK Z is Unity Y (ConvertPosition maps bakZ -> y), so the burial is a
                    // vertical offset and the plan position is left alone.
                    float buried = BakCoordinateConverter.ConvertPosition(
                        0, 0, -_propEmergence.BurialAt(grid, gridX, gridY)).y;
                    prop.localPosition = new Vector3(p.x, buried, p.z);
                }
            }
        }

        private readonly GameData.Resources.Combat.TrapPropEmergence _propEmergence = new();
        private long _propEmergenceFor = -1;

        /// <summary>
        /// Draw the arena's tactical grid — <c>combatgrid_draw_terrain_walls</c>.
        /// </summary>
        /// <remarks>
        /// <b>The colour is per-zone DATA, not a constant.</b> GRID.DAT holds one palette pen for
        /// each of the twelve zones (224 for most, 234/225/187/152/173 for the rest) so the grid sits
        /// against each zone's own combat backdrop. That table has been extracted since its format
        /// was reversed and nothing consumed it until this.
        ///
        /// <para>Pinned to the original by measurement: the lines in encounter 347's frames are RGB
        /// (52,130,69), which is pen 224 exactly, and they cover the whole viewport rather than
        /// outlining it — see CombatGridOutline.</para>
        ///
        /// <para>One mesh of <see cref="MeshTopology.Lines"/> for the whole grid rather than a
        /// renderer per segment: it is rebuilt with the arena on every redraw, and a hundred
        /// GameObjects a turn is the shape of thing that shows up later as a frame-time problem.</para>
        /// </remarks>
        private void DrawCombatGridLines() {
            if (_arenaRoot == null || _hotspots == null) {
                return;
            }

            // *** THE GRID IS BEHIND THE SAME FLAG THE OVERLAY IS, AND THIS DREW IT ALWAYS. ***
            // CACTOR.C:1334 is `if (g_bCombatGridLinesEnabled) { combatgrid_rndr_engagement_lines();
            // combatgrid_draw_terrain_walls(); }` — BOTH calls, one flag, the one G toggles. This
            // ports the second of them and ran unconditionally, so every fight showed a faint
            // permanent lattice the original only shows on demand. Confirmed by driving: with the
            // flag off the original's arena has no lattice at all, and `g` brings the whole grid up.
            if (!_combatGridLinesEnabled) {
                return;
            }
            if (_hotspots.CombatGridBorderPen is not int pen) {
                return;
            }
            Color[] palette = _zoneSceneBuilder?.RenderContext?.Palette;
            if (palette == null || pen < 0 || pen >= palette.Length) {
                return;
            }

            List<(long X1, long Y1, long X2, long Y2)> lines = _hotspots.PlaceCombatGridLines();
            if (lines.Count == 0) {
                return;
            }

            var vertices = new Vector3[lines.Count * 2];
            var indices = new int[lines.Count * 2];
            for (var i = 0; i < lines.Count; i++) {
                (long x1, long y1, long x2, long y2) = lines[i];
                vertices[i * 2] = BakCoordinateConverter.ConvertPosition((int)x1, (int)y1, 0);
                vertices[(i * 2) + 1] = BakCoordinateConverter.ConvertPosition((int)x2, (int)y2, 0);
                indices[i * 2] = i * 2;
                indices[(i * 2) + 1] = (i * 2) + 1;
            }

            var mesh = new Mesh { name = "CombatGridLines" };
            mesh.vertices = vertices;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("CombatGridLines");
            go.transform.SetParent(_arenaRoot.transform, worldPositionStays: false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GridLineMaterial(palette[pen]);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>The unlit line material, rebuilt when the zone's pen resolves to a new colour.</summary>
        private static Material GridLineMaterial(Color colour) {
            if (_gridLineMaterial == null) {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) {
                    return null;
                }
                _gridLineMaterial = new Material(shader) {
                    name = "BakCombatGridLines",
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            _gridLineMaterial.SetColor(BaseColorId, colour);
            return _gridLineMaterial;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static Material _gridLineMaterial;

        /// <summary>
        /// The points a moved sprite passes and the seconds each leg takes: every cell its walk
        /// stepped through, at the original's frames per step (CMBTAI.C:36-60, WORLDHIT.C:660-665),
        /// or one straight leg for a move that was not a walk.
        /// </summary>
        private (List<Vector3> Points, List<float> Seconds) RouteOf(
            GameData.Resources.Combat.Combatant who, Vector3 start, Vector3 end) {
            var points = new List<Vector3> { start };
            var seconds = new List<float>();
            List<(int X, int Y)> cells = who?.WalkedCells;
            int cellSize = _hotspots?.Start?.CombatGridCellSize ?? 0;
            if (cells is { Count: >= 2 } && cells[^1] == (who.X, who.Y) && cellSize > 0
                && _hotspots.CellCentreWorld(who.X, who.Y) is Vector3 endCentre) {
                // Offsets between cell centres, laid onto the sprite's own end point: a sprite
                // need not stand on its cell's exact centre.
                for (var i = 1; i < cells.Count; i++) {
                    Vector3? centre = _hotspots.CellCentreWorld(cells[i].X, cells[i].Y);
                    points.Add(i == cells.Count - 1 || !centre.HasValue ? end : end + (centre.Value - endCentre));
                    seconds.Add(GameData.Resources.Combat.CombatWalk.StepFrames(
                        cells[i].X - cells[i - 1].X, cells[i].Y - cells[i - 1].Y, cellSize)
                        * (float)GameData.Resources.Combat.SpellVisuals.FrameSeconds);
                }
                return (points, seconds);
            }
            points.Add(end);
            seconds.Add(SlideSeconds);
            return (points, seconds);
        }

        private static float Sum(List<float> values) {
            float sum = 0f;
            foreach (float v in values) {
                sum += v;
            }
            return sum;
        }

        /// <summary>Where <paramref name="elapsed"/> falls on a route: its leg, the fraction of that
        /// leg, and the legs crossed so far (leg + fraction).</summary>
        private static float Along(List<float> legs, float elapsed, out int leg, out float k) {
            for (leg = 0; leg < legs.Count - 1 && elapsed >= legs[leg]; leg++) {
                elapsed -= legs[leg];
            }
            k = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, legs[leg]));
            return leg + k;
        }

        // Consumed by every draw, whether or not anything slides: a path left behind would replay on
        // the next redraw.
        private void ForgetWalkedCells() {
            foreach (GameData.Resources.Combat.Combatant c in
                     _hotspots?.Combat?.Encounter?.AllCombatants()
                     ?? System.Linq.Enumerable.Empty<GameData.Resources.Combat.Combatant>()) {
                c?.WalkedCells.Clear();
            }
        }

        private async UniTask SlideThenRedrawAsync() {
            if (_sliding || _arenaRoot == null || _hotspots == null) {
                ForgetWalkedCells();
                await DrawCombatantsAsync();
                return;
            }
            // A whirlwind reaches the victim where it stood before the rules pushed it; the walk
            // back comes after (CSPELL.C:653-668). The flight anchors on these very sprites.
            // The hold is part of the slide: the arena stays busy, so the fight cannot move on and a
            // second redraw cannot start its own slide from the same markers.
            _sliding = true;
            try {
                while (_spellVfx != null && _spellVfx.HoldsMovers) {
                    await UniTask.Yield();
                }
            }
            finally {
                _sliding = false;
            }
            if (_arenaRoot == null) {
                ForgetWalkedCells();
                return;
            }

            var targets = new List<Transform>();
            var gaits = new List<Encounters.DirectionalSprite>();
            var movers = new List<GameData.Resources.Combat.Combatant>();
            var routes = new List<(List<Vector3> Points, List<float> Seconds)>();
            List<GameData.Resources.World.EncounterActorPlacement.Placed> placed =
                _hotspots.PlaceCombatants();

            foreach (Encounters.ArenaCombatant marker in
                     _arenaRoot.GetComponentsInChildren<Encounters.ArenaCombatant>()) {
                foreach (GameData.Resources.World.EncounterActorPlacement.Placed p in placed) {
                    // BOTH fields: a roster slot indexes the enemy roster for a monster and the
                    // party for a character, so slot 1 names two combatants in the same fight.
                    if (p.RosterSlot != marker.RosterSlot || p.PartyMember != marker.PartyMember) {
                        continue;
                    }
                    Vector3 end = BakCoordinateConverter.ConvertPosition(
                        (int)p.WorldX, (int)p.WorldY, 0);
                    Vector3 start = marker.transform.localPosition;
                    // A cell is 300 world units, so anything that stepped is far past this; the
                    // threshold only drops rounding on a combatant that stayed put.
                    if ((end - start).sqrMagnitude > 1f) {
                        GameData.Resources.Combat.Combatant who =
                            _hotspots.CombatantFor(marker.RosterSlot, marker.PartyMember);
                        targets.Add(marker.transform);
                        gaits.Add(marker.GetComponent<Encounters.DirectionalSprite>());
                        movers.Add(who);
                        routes.Add(RouteOf(who, start, end));
                    }
                    break;
                }
            }

            ForgetWalkedCells();

            if (targets.Count == 0) {
                await DrawCombatantsAsync();
                return;
            }

            float total = 0f;
            foreach ((List<Vector3> _, List<float> seconds) in routes) {
                total = Mathf.Max(total, Sum(seconds));
            }

            _sliding = true;
            try {
                var stepped = new int[targets.Count];
                // At most one of the original's frames per frame drawn here: the original renders
                // every frame of a walk, so a hitch (the redraw's own frame is a long one) must slow
                // the walk rather than skip the cells it stepped through.
                float maxStep = (float)GameData.Resources.Combat.SpellVisuals.FrameSeconds;
                for (var elapsed = 0f; elapsed < total; elapsed += Mathf.Min(Time.deltaTime, maxStep)) {
                    for (var i = 0; i < targets.Count; i++) {
                        // Cell by cell, each at the original's pace: segments-crossed is the
                        // index of the current cell plus how far into it the sprite is.
                        float crossed = Along(routes[i].Seconds, elapsed, out int seg, out float k);
                        if (targets[i] != null) {
                            targets[i].localPosition = Vector3.Lerp(routes[i].Points[seg], routes[i].Points[seg + 1], k);
                        }
                        // *** THE LEGS SWING WITH THE SLIDE, WHICH IS THE ORIGINAL'S COUPLING. ***
                        // animateCombatActorMove steps the creature's animation and its position in
                        // the same loop, handing the stepped frame to the slide as its sprite. An
                        // actor that is not moving is not animating, exactly as RoamingActor does in
                        // the world.
                        while (stepped[i] < (int)(crossed * GaitStepsPerCell)) {
                            gaits[i]?.AdvanceGait();
                            stepped[i]++;
                        }
                    }
                    // *** WRITTEN BACK EVERY STEP, NOT ONCE AT THE END. *** The rebuild that follows
                    // destroys these sprites, and a slide can also be cut short by a second redraw
                    // arriving; keeping the combatant current means whatever replaces the sprite
                    // resumes from the frame that was actually on screen.
                    for (var i = 0; i < gaits.Count; i++) {
                        if (gaits[i] != null && movers[i] != null) {
                            movers[i].GaitFrame = gaits[i].GaitFrame;
                            movers[i].GaitAdvancing = gaits[i].GaitAdvancing;
                        }
                    }
                    await UniTask.Yield();
                }
            }
            finally {
                _sliding = false;
            }

            await DrawCombatantsAsync();
        }

        /// <summary>Take the painted backdrop down — the fight that wanted it is over.</summary>
        /// <remarks>
        /// Separate from <see cref="ClearArena"/> because that also runs between turns, to drop the
        /// previous arena before building the next: clearing the backdrop there would flash the
        /// world through on every redraw.
        /// </remarks>
        private void ClearCombatBackdrop() {
            foreach (BakAgain.UI.InGame.InGameScreen screen in
                     Object.FindObjectsByType<BakAgain.UI.InGame.InGameScreen>(FindObjectsSortMode.None)) {
                screen.SetCombatBackdrop(null);
            }
        }

        // The underground fight's backdrop: the world photographed once from the capture pose, and
        // what was switched off so the arena camera no longer draws the world itself.
        private Texture2D _arenaBackdropTexture;
        private GameObject _arenaBackdropQuad;
        private readonly List<Renderer> _arenaBackdropHiddenRenderers = new();

        /// <summary>
        /// Underground, photographs the world once from the ORIGINAL's capture pose and shows that
        /// still behind the arena, in place of the live zone.
        /// </summary>
        /// <remarks>
        /// <b>The original draws an underground fight from TWO poses.</b>
        /// <c>combat_captureArenaBackdrop</c> @0x2210b renders the world into the still at
        /// <c>cameraHeightUnderground</c>; only afterwards (@0x222a2) does it add 510 and rebuild the
        /// view that the grid and the figures project through. Drawing the live corridor from the
        /// raised arena camera instead showed the corridor ahead, put the walls through the outer
        /// columns and buried the combatants standing there (TASK-653).
        ///
        /// <para><b>A render texture on a quad, not a URP overlay camera.</b> An overlay rebuilds its
        /// projection from the stack's pixel rect, which once dropped an explicit 6:5 camera aspect
        /// (gone since TASK-764) and spread the arena ~1.2x sideways; the arena camera is left
        /// exactly as it is and only its background changes. The quad's shader must
        /// not take fog — the underground haze blacked out <c>Unlit/Texture</c> at this depth.</para>
        ///
        /// <para><b>Once, like the original.</b> The camera does not move during a fight (only the
        /// sprites turn), so one still is the whole backdrop. The surface needs none of this: there
        /// is no raise, the two poses coincide, and TASK-392's scenery cull handles occlusion.</para>
        /// </remarks>
        private async UniTask CaptureUndergroundBackdropAsync() {
            ReleaseUndergroundBackdrop();
            StartData start = _hotspots?.Start;
            RenderTexture target = _worldCamera != null ? _worldCamera.targetTexture : null;
            if (!_zoneIsUnderground || target == null || _zoneRoot == null || _gameSession == null
                || !CombatArenaCamera.IsUsable(start, underground: true)) {
                return;
            }

            var captureTarget = new RenderTexture(target.width, target.height, 24, target.format) {
                name = "ArenaBackdropCapture",
            };
            var captureObject = new GameObject("ArenaBackdropCapture");
            Camera capture = captureObject.AddComponent<Camera>();
            capture.CopyFrom(_worldCamera);
            capture.transform.SetPositionAndRotation(
                BakCoordinateConverter.ConvertPosition(_gameSession.PositionX, _gameSession.PositionY,
                    CombatArenaCamera.HeightFor(start, underground: true)
                    - CombatArenaCamera.UndergroundHeightRaise),
                _worldCamera.transform.rotation);
            capture.targetTexture = captureTarget;
            capture.aspect = _worldCamera.aspect;
            capture.depth = _worldCamera.depth - 1f;
            // *** LET THE FRAME LOOP RENDER IT, AND WAIT FOR URP TO SAY SO. *** Measured
            // 2026-09-24: capture.Render() left the texture black under URP, while the same camera
            // drawn by an ordinary frame fills it; endCameraRendering is the signal, and the cap only
            // stops a world that never draws from hanging the fight's entry.
            bool rendered = false;
            void OnRendered(UnityEngine.Rendering.ScriptableRenderContext _, Camera cam) {
                if (cam == capture) rendered = true;
            }
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += OnRendered;
            try {
                for (int frames = 0; !rendered && frames < 120; frames++) {
                    await UniTask.NextFrame();
                }
            } finally {
                UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= OnRendered;
            }
            // *** OFF BEFORE DESTROY. *** Destroy is deferred to the end of the frame; left enabled,
            // the capture renders once more AFTER the zone is hidden below and overwrites the still
            // with black (measured: the texture was right when URP reported it, black a frame later).
            capture.enabled = false;
            capture.targetTexture = null;
            Object.Destroy(captureObject);

            // *** COPIED OUT AT ONCE. *** The render texture was right when URP reported it and
            // black by the time the fight was on screen, with no camera targeting it; the cause
            // was not found. A Texture2D is what the original's still is anyway — a buffer.
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = captureTarget;
            _arenaBackdropTexture = new Texture2D(captureTarget.width, captureTarget.height,
                TextureFormat.RGBA32, mipChain: false) { name = "ArenaBackdrop" };
            _arenaBackdropTexture.ReadPixels(
                new Rect(0, 0, captureTarget.width, captureTarget.height), 0, 0);
            _arenaBackdropTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            RenderTexture.active = previous;
            captureTarget.Release();
            Object.Destroy(captureTarget);

            if (_zoneRoot == null || _worldCamera == null) {
                ReleaseUndergroundBackdrop();   // the world went away while we waited
                return;
            }

            // The still replaces the zone for the arena camera. The arena root is built after this
            // and every turn after that, so none of its renderers can be in the list.
            foreach (Renderer r in _zoneRoot.GetComponentsInChildren<Renderer>()) {
                if (!r.enabled) continue;
                r.enabled = false;
                _arenaBackdropHiddenRenderers.Add(r);
            }

            // Filling the view at a fixed depth: height 2 d tan(fov/2), width that times the aspect.
            float depth = _worldCamera.farClipPlane * 0.9f;
            float height = 2f * depth * Mathf.Tan(_worldCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _arenaBackdropQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _arenaBackdropQuad.name = "ArenaBackdrop";
            Object.Destroy(_arenaBackdropQuad.GetComponent<Collider>());
            _arenaBackdropQuad.transform.SetParent(_worldCamera.transform, worldPositionStays: false);
            _arenaBackdropQuad.transform.localPosition = new Vector3(0f, 0f, depth);
            _arenaBackdropQuad.transform.localRotation = Quaternion.identity;
            _arenaBackdropQuad.transform.localScale =
                new Vector3(height * _worldCamera.aspect, height, 1f);
            _arenaBackdropQuad.GetComponent<MeshRenderer>().sharedMaterial =
                new Material(Shader.Find("Sprites/Default")) { mainTexture = _arenaBackdropTexture };
        }

        /// <summary>Puts the live zone back behind the arena camera. Safe to call when nothing is up.</summary>
        private void ReleaseUndergroundBackdrop() {
            foreach (Renderer r in _arenaBackdropHiddenRenderers) {
                if (r != null) r.enabled = true;
            }
            _arenaBackdropHiddenRenderers.Clear();
            if (_arenaBackdropQuad != null) {
                Object.Destroy(_arenaBackdropQuad.GetComponent<MeshRenderer>().sharedMaterial);
                Object.Destroy(_arenaBackdropQuad);
                _arenaBackdropQuad = null;
            }
            if (_arenaBackdropTexture != null) {
                Object.Destroy(_arenaBackdropTexture);
                _arenaBackdropTexture = null;
            }
        }

        private async UniTask DrawCombatantsAsync() {
            ClearArena();
            // *** DONE HERE, NOT AT WORLD BUILD. *** The travel screen owns the viewport click and
            // the fight owns what a body means, but the HUD is pushed AFTER BuildAsync — handing it
            // over there found no InGameScreen and silently wired nothing. A fight starting is the
            // first moment both exist; it is idempotent, so repeating it per redraw costs nothing.
            foreach (BakAgain.UI.InGame.InGameScreen screen in
                     Object.FindObjectsByType<BakAgain.UI.InGame.InGameScreen>(FindObjectsSortMode.None)) {
                screen.SetCorpseLootSeam(_hotspots.LootCorpse);
                _hotspots.InspectArmed = screen.InspectAtTouchCursor;
                screen.SetCombatTargetSeam(_hotspots.ResolveCombatTargetClick);
                screen.SetCombatantAtPointSeam(_hotspots.CombatantAtPoint);
                screen.SetCombatCellSeams(_hotspots.CellAtPoint,
                    (cell, always) => { _hotspots.CursorCell = cell; _hotspots.CursorCellAlwaysShown = always; },
                    _hotspots.CellCentreWorld, _hotspots.ActingCell, () => _hotspots.AwaitingCombatTarget,
                    _hotspots.CastAcceptedAtCell, _hotspots.MoveAcceptedAtCell);
                screen.SetCombatGroundSeam(_hotspots.ResolveCombatGroundClick);
                screen.SetCombatFaceSeam(FaceActingCombatantAt);
                screen.SetInCombatPredicate(() => _hotspots?.Combat?.InCombat ?? false);
                screen.SetCombatPanelSeam(_hotspots.CombatPanelContent);
                screen.SetActingHeadSeam(_hotspots.ActingPortraitHeadId);
                // One encounter fights against a painted backdrop instead of the world. Handed over
                // with the rest of the seams because this is the redraw the arena runs every turn,
                // and SetCombatBackdrop is idempotent for exactly that reason.
                screen.SetCombatBackdrop(GameData.Resources.Combat.CombatBackdrop.ImageFor(
                    _hotspots.FightingEncounter));
            }
            // The cast screen needs the same two, because in a fight it draws the caster panel
            // itself rather than the travel heads — the navigator deactivates InGameScreen while
            // the cast screen is up, so the panel beneath is gone (TASK-368). Same loop, same
            // idempotence, same reason: this redraw is the first moment both objects exist.
            foreach (BakAgain.UI.Spells.CastScreen cast in
                     Object.FindObjectsByType<BakAgain.UI.Spells.CastScreen>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                cast.SetCasterStatsSeam(_hotspots.Combat != null
                    ? _hotspots.Combat.ActorStatsFor
                    : (System.Func<GameData.Resources.Combat.Combatant,
                        (string Name, System.Collections.Generic.IReadOnlyList<int> Values)?>)null);
            }
            if (_hotspots == null || _zoneSceneBuilder?.RenderContext == null || _zoneRoot == null) {
                return;
            }
            _hotspots.SpellVisualSink ??= (visual, from, to) => SpellVfx.Enqueue(visual, from, to);

            var placed = _hotspots.PlaceCombatants();
            if (placed.Count == 0) {
                return;
            }

            _arenaRoot = new GameObject("CombatArena");
            _arenaRoot.transform.SetParent(_zoneRoot.transform, worldPositionStays: false);
            _encounterSprites ??= new Encounters.EncounterActorSpriteBuilder(_resources, _logger);
            int drawn = await _encounterSprites.BuildAsync(
                placed, _arenaRoot.transform, _zoneSceneBuilder.RenderContext, _worldCamera, this,
                arena: true);
            await DrawTrapPropsAsync();
            DrawCombatGridLines();
            // "Combatants", not "enemies": the party is placed through here too since
            // HotspotService.CreatureNumberFor learned to translate a character index.
            // *** THE LEGS CARRY OVER. *** These sprites are brand new — the arena was destroyed a
            // few lines up — so without this every step ends by snapping the walk cycle back to
            // frame 0, which is what the first version of the slide did and it read as a twitch at
            // the end of every move. The frame lives on the combatant precisely because it has to
            // outlive the GameObject; see Combatant.GaitFrame.
            foreach (Encounters.ArenaCombatant marker in
                     _arenaRoot.GetComponentsInChildren<Encounters.ArenaCombatant>()) {
                GameData.Resources.Combat.Combatant c =
                    _hotspots.CombatantFor(marker.RosterSlot, marker.PartyMember);
                if (c != null) {
                    Encounters.DirectionalSprite sprite =
                        marker.GetComponent<Encounters.DirectionalSprite>();
                    sprite?.SeedGait(c.GaitFrame, c.GaitAdvancing);
                    // *** THE RECOIL, DRAWN. *** A struck combatant is redrawn through its zone's
                    // own fade ramp for the two redraws the timer lasts — the creature steps a rung
                    // deeper into the fog rather than moving. HitReactionRemap is the block index
                    // the original puts in spriteHitDir.
                    sprite?.SetTintRung(
                        c.HitReactionTimer > 0 ? c.HitReactionRemap : 0);
                }
            }

            AttachLingeringSpellVisuals();
            await DrawSpellTilesAsync();
            FloatDamageNumbers();

            // *** A SWING ARMED BY THE MODEL IS PLAYED HERE. *** The attack resolves in one call
            // and the arena catches up on the next redraw, so the flag is what carries the event
            // across. Slot 3 is bitmap 1 — SpriteKeys[1] — which nothing else loads, so the swing
            // is the only reason that set is fetched at all.
            foreach (Encounters.ArenaCombatant marker in
                     _arenaRoot.GetComponentsInChildren<Encounters.ArenaCombatant>()) {
                GameData.Resources.Combat.Combatant swinging =
                    _hotspots.CombatantFor(marker.RosterSlot, marker.PartyMember);
                if (swinging == null || !swinging.SwingPending) {
                    continue;
                }
                swinging.SwingPending = false;
                var sprite = marker.GetComponent<Encounters.DirectionalSprite>();
                string attackSet = null;
                foreach (GameData.Resources.World.EncounterActorPlacement.Placed p in placed) {
                    if (p.RosterSlot == marker.RosterSlot && p.PartyMember == marker.PartyMember) {
                        attackSet = _encounterSprites.AttackSetFor(p.CreatureNumber);
                        break;
                    }
                }
                if (sprite == null || attackSet == null || marker.Face == null) {
                    continue;
                }
                marker.gameObject.AddComponent<Encounters.AttackSwing>().Play(
                    _zoneSceneBuilder.RenderContext, marker.Face, attackSet,
                    sprite.CurrentOctant, marker.Extent);
            }

            // *** A SHOT ARMED BY THE MODEL FLIES HERE. *** Same carry-across as the swing above:
            // the attack resolves in one call and the arena catches up on the next redraw. The
            // projectile is a COMBAT.TBL effect entry — a single fixed face, no pose and no octant —
            // and it flies between the two sprites this redraw has just placed, so neither endpoint
            // is computed twice.
            foreach (Encounters.ArenaCombatant marker in
                     _arenaRoot.GetComponentsInChildren<Encounters.ArenaCombatant>()) {
                GameData.Resources.Combat.Combatant shooter =
                    _hotspots.CombatantFor(marker.RosterSlot, marker.PartyMember);
                if (shooter == null || shooter.FlightEffectId == 0) {
                    continue;
                }
                int effect = shooter.FlightEffectId;
                int arcSteps = shooter.FlightArcSteps;
                int arcDelta = shooter.FlightArcDelta;
                (float X, float Y)? missEnd = shooter.FlightMissEnd;
                shooter.FlightEffectId = 0;
                shooter.FlightArcSteps = 0;
                shooter.FlightArcDelta = 0;
                shooter.FlightMissEnd = null;

                // *** A CLEAN MISS FLIES OFF, NOT AT THE TARGET. *** SpellProjectileMiss ended it
                // where the deflected heading left the board; fly there at the original's speed.
                if (missEnd is { } end) {
                    Vector3 from = marker.transform.localPosition;
                    Vector3 away = ArenaCellLocal(end.X, end.Y, from.y);
                    float worldUnits = Vector3.Distance(from, away) * BakCoordinateConverter.WorldScale;
                    float seconds = worldUnits / GameData.Resources.Combat.SpellProjectileMiss.StepUnits
                        * (float)GameData.Resources.Combat.SpellVisuals.FrameSeconds;
                    FlyEffectAsync(effect, from, away, 0, 0, seconds).Forget();
                    continue;
                }

                Transform to = null;
                foreach (Encounters.ArenaCombatant other in
                         _arenaRoot.GetComponentsInChildren<Encounters.ArenaCombatant>()) {
                    if (other.RosterSlot == shooter.FlightToSlot
                        && other.PartyMember == shooter.FlightToParty) {
                        to = other.transform;
                        break;
                    }
                }
                if (to == null) {
                    continue;
                }
                FlyEffectAsync(effect, marker.transform.localPosition, to.localPosition,
                    arcSteps, arcDelta).Forget();
            }

            // *** THE LIVE COMBATANTS BREATHE. *** The idle is the walk cycle played in place —
            // slot 0 asks for the same three frames — and its delay is re-rolled to 8..15 ticks
            // after every advance, so it is deliberately irregular. On the arena root because the
            // root is what a redraw replaces; the counters live on the combatants and survive it.
            _arenaRoot.AddComponent<Encounters.ArenaIdleAnimator>()
                .Bind(_hotspots.CombatantFor, null);

            // *** THE TACTICAL OVERLAY, ON THE SAME ROOT. *** The 8x13 grid and the crystal links
            // are one feature behind one flag in the original (renderCombatGridScene @0x5DEA8 calls
            // drawCombatGridOutline and crystalChain_drawLinks together), toggled with G. Riding on
            // the arena root means it is rebuilt with the arena rather than needing invalidation.
            _arenaRoot.AddComponent<Encounters.ArenaGridOverlay>()
                .Bind(_hotspots.ArenaOverlaySegments,
                    // The G key, or the touch aids' grid button.
                    () => (_overlayInput?.ToggleOverlayPressed ?? false)
                        | (BakAgain.UI.InputCore.TouchInputState.Instance?.TakeGridToggle() ?? false),
                    () => _combatGridLinesEnabled,
                    on => {
                        _combatGridLinesEnabled = on;
                        // The terrain-wall lines are drawn during the arena BUILD, not by the
                        // overlay, so flipping the flag has to rebuild for them to appear or go.
                        // DrawCombatantsAsync is the same path a turn takes, and it is idempotent.
                        DrawCombatantsAsync().Forget();
                    },
                    _worldCamera,
                    // The ring under the acting combatant, polled rather than built with the grid:
                    // the arena is drawn before the turn is decided.
                    _hotspots.ActingCellSegments);

            // *** THE COLLAPSE PLAYS ONCE, ON THE BODY THAT HAS JUST APPEARED. *** A corpse is
            // rebuilt on every redraw, so the "already shown" flag lives on the combatant; without
            // it the body would fall over again on each turn.
            foreach (Encounters.ArenaCorpse corpse in
                     _arenaRoot.GetComponentsInChildren<Encounters.ArenaCorpse>()) {
                GameData.Resources.Combat.Combatant dead =
                    _hotspots.CombatantFor(corpse.RosterSlot, corpse.PartyMember);
                if (dead == null || dead.DeathShown) {
                    continue;
                }
                dead.DeathShown = true;
                corpse.gameObject.AddComponent<Encounters.DeathCollapse>().Play(
                    _zoneSceneBuilder.RenderContext, corpse.Face, corpse.SetName,
                    corpse.CollapseLastFrame, corpse.Extent);
            }

            // *** THE RECOIL'S CLOCK IS THIS REDRAW, AND THE TICK COMES AFTER THE DRAW. ***
            // tickHitReactionTimers @0x61598 is called only from RenderWorldView, so a hit reaction
            // lasts two redraws of whatever animation is playing rather than two frames or a fixed
            // number of seconds. Ticking BEFORE the draw would spend one of the two on a frame
            // nobody saw: a timer of 2 would flash once. After the draw, both ticks are visible.
            _hotspots?.Combat?.TickHitReactions();

            _logger?.LogInformation("Arena: {Placed} combatants placed, {Drawn} drawn.",
                placed.Count, drawn);
        }

        /// <summary>Shows a corpse's contents on the inventory screen.</summary>
        /// <remarks>
        /// The same two lines <c>ContainerInteractionHandler.TryOpenLoot</c> uses for a world
        /// container — the difference between a chest and a body is which container was found, not
        /// how it is shown.
        /// </remarks>
        /// <summary>Opens a pack for looting; completes when the screen closes.</summary>
        private UniTask OpenCorpseLoot(GameData.Resources.Inventory.RuntimeContainer body) {
            if (body == null || _resolver == null) {
                return UniTask.CompletedTask;
            }
            var menu = (BakAgain.UI.Inventory.InventoryMenu)_resolver.Resolve(
                typeof(BakAgain.UI.Inventory.InventoryMenu));
            var nav = (BakAgain.UI.Navigation.IScreenNavigator)_resolver.Resolve(
                typeof(BakAgain.UI.Navigation.IScreenNavigator));
            if (menu == null || nav == null) {
                return UniTask.CompletedTask;
            }
            ExposeStash(body);
            menu.SetContainer(body, GameData.Resources.World.WorldEntityType.Corpse);
            return nav.PushAndWaitAsync(menu);
        }

        /// <summary>Turns the view to a speaker's bearing for a backdrop dialog.</summary>
        /// <remarks>
        /// <b>The walking eye must stop writing first</b>, exactly as the arena pose needs — every
        /// movement path re-syncs the camera, so an unsuspended turn is undone on the next frame
        /// and the dialog shows the player's own view after one blink.
        /// </remarks>
        private void TurnCameraToSpeaker(int speakerSlot) {
            if (_worldCamera == null || _gameSession == null) {
                return;
            }
            if (_partyMovement != null) {
                _partyMovement.CameraSuspended = true;
            }
            // The original writes the pitch too, as defaultCameraPitch — which is the pitch the
            // walking eye already carries, so stating it here changes nothing and matches the source.
            ushort yaw = GameData.Resources.Dialog.DialogBackdropCamera.YawFor(
                unchecked((ushort)_gameSession.Rotation), speakerSlot);
            _worldCamera.transform.rotation = BakCoordinateConverter.ConvertRotation(
                unchecked((ushort)_zoneDefaultCameraPitch), 0, yaw);
        }

        /// <summary>Puts the walking view back after a backdrop dialog.</summary>
        private void RestoreCameraAfterDialog() {
            if (_partyMovement == null) {
                return;
            }
            // Un-suspend FIRST or the restore is swallowed too — the same ordering ClearArena needs.
            _partyMovement.CameraSuspended = false;
            _partyMovement.SyncToCamera();
        }

        /// <summary>Drops the arena's sprites. <b>Does not touch the camera.</b></summary>
        /// <remarks>
        /// *** THIS IS ALSO THE TIDY-UP BEFORE A REBUILD, WHICH IS WHY IT RESTORES NOTHING. ***
        /// <see cref="DrawCombatantsAsync"/> calls it to drop a previous arena before building the
        /// new one, and the arena is now rebuilt every turn. Anything undone here is therefore undone
        /// one step after the fight set it, and again on every turn.
        ///
        /// <para>It used to put the walking camera back, so the arena pose survived for exactly one
        /// call: <c>PointCameraAtArena</c> raised the view and <c>DrawCombatantsAsync</c> lowered it
        /// again before a frame was drawn, and every fight was rendered from the travel eye. The
        /// party's own encounter actors were hidden and re-shown by the same mistake two commits
        /// earlier. <b>Restoring belongs to LEAVING the fight</b> — see the <c>showArena</c> false
        /// branch, which owns both.</para>
        /// </remarks>
        private void ClearArena() {
            if (_arenaRoot != null) {
                Object.Destroy(_arenaRoot);
                _arenaRoot = null;
            }
        }

        /// <summary>Puts the walking view back after a fight.</summary>
        /// <remarks>
        /// Ground-relative again, and re-sampled because the fight may have moved the party.
        /// Un-suspend FIRST or the restore is swallowed too.
        /// </remarks>
        private void RestoreWalkingCamera() {
            if (_partyMovement != null) {
                _partyMovement.CameraSuspended = false;
                _partyMovement.SyncToCamera();
            }
            // The arena pinned the camera to its own zoom; the walking view is the ZONE's again.
            if (_worldCamera != null && _zoneDef != null) {
                _worldCamera.fieldOfView = TravelFov(_zoneDef.FocalLength);
            }
        }

        /// <summary>
        /// Points the view at the arena: the party's place and heading, the fight's height and tilt.
        /// </summary>
        /// <remarks>
        /// <b>The height is ABSOLUTE, unlike the walking eye's.</b> The explore camera adds the zone
        /// default to the ground under the party; this one is a world z
        /// (<see cref="CombatArenaCamera.HeightIsAbsolute"/>). Adding the ground here would lift the
        /// fight by the terrain height — invisible on the flat, and badly wrong on a slope.
        ///
        /// <para><b>Only height and pitch change.</b> X, Y and yaw stay the party's, which is what
        /// puts <see cref="CombatArenaPlacement"/>'s offsets — stated relative to their heading — in
        /// front of the camera rather than behind it.</para>
        ///
        /// <para>The original uses a second camera entirely and we re-point the one we have. That is
        /// the engine-architecture half of the original, not its behaviour: nothing else draws while
        /// the fight is up, and the walking pose is restored wholesale by
        /// <see cref="ClearArena"/>.</para>
        /// </remarks>
        private void PointCameraAtArena() {
            if (_worldCamera == null || _gameSession == null) {
                return;
            }
            bool underground = _zoneIsUnderground;
            StartData start = _hotspots?.Start;
            if (!CombatArenaCamera.IsUsable(start, underground)) {
                _logger?.LogDebug("No arena camera pose in START.DAT; leaving the view where it is.");
                return;
            }

            // The walking eye must stop writing, or the next sync puts the view straight back.
            if (_partyMovement != null) {
                _partyMovement.CameraSuspended = true;
            }
            _worldCamera.transform.position = BakCoordinateConverter.ConvertPosition(
                _gameSession.PositionX, _gameSession.PositionY,
                CombatArenaCamera.HeightFor(start, underground));
            _worldCamera.transform.rotation = BakCoordinateConverter.ConvertRotation(
                (ushort)CombatArenaCamera.PitchFor(start, underground), 0,
                unchecked((ushort)_gameSession.Rotation));

            // *** THE ARENA DOES NOT INHERIT THE ZONE'S ZOOM. *** setupRenderView (@0x23d18) takes
            // the shift from the DESCRIPTOR it is handed — `1 << descriptor->field_0` — not from the
            // zone global, so an arena is free to carry its own. Measured from the original's own
            // grid it projects at ~32.4° horizontal, which is shift 9, while zones 10-12 ship 8.
            // Reusing the walking camera means we inherit the zone's, which halved the underground
            // arena on screen (TASK-604). RestoreWalkingCamera puts the zone's back.
            _worldCamera.fieldOfView = TravelFov(null);
        }

        /// <summary>
        /// Start or stop combat music.
        /// </summary>
        /// <remarks>
        /// <b>Combat music is its own preference, and OFF means silence rather than "leave the zone
        /// track playing"</b> — <see cref="MusicSelection.ForCombat"/> returns NoTrack, and playing
        /// that stops the music. Either way the outgoing track is kept and put back afterwards, so a
        /// fight in one of the few zones that has music does not lose it.
        ///
        /// <para>Lives here rather than in <c>HotspotService</c> because the track to come back to is
        /// the world's; the hotspot layer only says when a fight starts and ends.</para>
        /// </remarks>
        private void SetCombatMusic(bool entering) {
            if (_midi == null || _resources == null) {
                return;
            }

            if (!entering) {
                _midi.PlayTrackAsync(_trackBeforeCombat, _resources, owner: this).Forget();
                return;
            }

            int track = MusicSelection.ForCombat(
                UnityEngine.Random.Range(0, MusicSelection.CombatTracks.Length),
                _preferences?.Current?.CombatMusic ?? true);
            PlayCombatTrackAsync(track).Forget();
        }

        private async UniTaskVoid PlayCombatTrackAsync(int track) =>
            _trackBeforeCombat = await _midi.PlayTrackAsync(track, _resources, owner: this);

        /// <summary>
        /// The combat HUD, or null when the container has none.
        /// </summary>
        /// <remarks>
        /// <b>Resolve THROWS for an unregistered type, and this one's registration is conditional.</b>
        /// <c>RootLifetimeScope</c> only registers <c>CombatMenu</c> when its prefab reference is
        /// assigned, so a missing reference would otherwise turn "no HUD" into an exception thrown
        /// out of the middle of starting a fight — breaking combat entirely rather than degrading it.
        /// </remarks>
        private BakAgain.UI.Combat.CombatMenu ResolveCombatMenu() {
            try {
                return _resolver.Resolve(typeof(BakAgain.UI.Combat.CombatMenu))
                    as BakAgain.UI.Combat.CombatMenu;
            } catch (System.Exception e) {
                _logger?.LogWarning($"No combat HUD available; fighting without one: {e.Message}");
                return null;
            }
        }

        private MovementData _movementTable;

        /// <summary>
        /// A preferences apply: hand the resolved step distance to the encounter sweep.
        /// </summary>
        /// <remarks>
        /// <b>The RESOLVED distance, not the preference index.</b> <c>worldmove_dat_load</c> seeks
        /// <c>movement.dat</c> by the preference and the change detector compares what it finds, so
        /// the enum would make every change look like a step of one.
        ///
        /// <para><b>ALSO CALLED ON ZONE ENTRY, and that is not belt-and-braces.</b> The original runs
        /// the detector at the end of every zone setup — <c>worldmove_rgn_chap_trans_apply()</c> is
        /// the last statement of ZONE.C's world scene setup (:180) — and only additionally on a
        /// preferences apply (MAINMENU.C:1275). Hanging it on the preferences event alone leaves the
        /// baseline at whatever the SAVE was written with, because <c>Changed</c> fires while
        /// preferences load at boot, before any save has been read, and the hydration that follows
        /// overwrites the result.
        ///
        /// <para>Measured 2026-09-12 with <c>scripts/re/statediff.py</c>, which is what this comment
        /// used to get wrong: the shipped <c>dir.G01/SAVE01.GAM</c> carries a baseline of 1600/2048,
        /// the running preferences resolve to 400/1024, and after loading it the ORIGINAL's live
        /// <c>g_gameState</c> read 400/1024 while ours still read 1600/2048. With the baseline stuck
        /// high, <c>ResetsRoamers</c> answers false for every step size below 1600 — so raising the
        /// setting from Small to Medium, which resets the roamers in the original, did nothing
        /// here.</para>
        /// </remarks>
        /// <summary>
        /// Whether an encounter actor forbids making camp here — see
        /// <see cref="GameData.Resources.World.CampRefusal"/>.
        /// </summary>
        public bool CampIsWatched() => _hotspots?.CampIsWatched() ?? false;

        private void OnPreferencesChangedForRoamers() {
            if (_hotspots == null || _movementTable == null || _preferences?.Current == null) {
                return;
            }
            bool reset = _hotspots.OnStepSizeChanged(
                _movementTable.StepDistanceFor(_preferences.Current.StepSize),
                // The stride is resolved from the TURN preference, not the step one:
                // worldmove_dat_load seeks (combat_step_speed + 3) * 2 into movement.dat, which is
                // the TurnAngles third of the file.
                _movementTable.TurnAngleFor(_preferences.Current.TurnSize));
            // rgnenc_reset_and_save re-places the reset roamers at once (RGNENC.C:476). Not over a
            // fight: its actors are hidden, and leaving it redraws the chunk anyway.
            if (reset && _hotspots.Combat?.Encounter == null) {
                RedrawEncounterActorsAsync().Forget();
            }
        }

        /// <summary>The quarrel picker, or null when the container has none.</summary>
        /// <remarks>Conditionally registered and caught for the same reason the melee menu is.</remarks>
        private BakAgain.UI.Combat.ShootMenu ResolveShootMenu() {
            try {
                return _resolver.Resolve(typeof(BakAgain.UI.Combat.ShootMenu))
                    as BakAgain.UI.Combat.ShootMenu;
            } catch (System.Exception e) {
                _logger?.LogWarning($"No shoot menu available; Shoot will report it: {e.Message}");
                return null;
            }
        }

        /// <summary>The spell picker, or null when the container has none.</summary>
        /// <remarks>Conditionally registered and caught for the same reason both combat menus
        /// are — a fight without a cast screen must refuse Cast, not throw out of a turn.</remarks>
        private BakAgain.UI.Spells.CastScreen ResolveCastScreen() {
            try {
                return _resolver.Resolve(typeof(BakAgain.UI.Spells.CastScreen))
                    as BakAgain.UI.Spells.CastScreen;
            } catch (System.Exception e) {
                _logger?.LogWarning($"No cast screen available; Cast will report it: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Steps every roaming encounter actor in proportion to how far the party just moved.
        /// </summary>
        /// <remarks>
        /// <b>The cadence is a party-movement one, and getting it from the disassembly took
        /// correcting a note that had it wrong.</b> <c>updateRoamingEncounterActors</c> @0x7600b has
        /// exactly one caller, <c>drawMap(animate)</c> @0x21711, and only
        /// <c>approachWalkToScenePosition</c> @0x6dd52 passes 1 — <c>MainGameLoop</c>,
        /// <c>HandleMoveEvent</c> and <c>UI_Encamp</c> all pass 0. So roaming advances once per
        /// frame of an ANIMATED PARTY WALK: not per render frame, and never while the party stands
        /// still.
        ///
        /// <para><b>Both obvious wires are wrong.</b> Ticking from an Update would have monsters
        /// roaming while the party stands still, covering ground the original never lets them cover.
        /// Ticking once per keypress is the opposite error — monsters moving in lockstep with the
        /// player, and an authored patrol drifting off its waypoints between two steps.</para>
        ///
        /// <para><b>So the tick is metered by DISTANCE, not by steps.</b> A roaming step is exactly
        /// <c>CellSize / 4</c>, which is why the waypoints — which sit on the cell lattice — are
        /// reached by exact equality after four of them. Metering the party's movement in the same
        /// quarter-cells keeps a monster at walking pace regardless of the player's step-size
        /// preference, which a fixed ticks-per-step could not: that preference changes the party's
        /// step length live, so any constant would be right for one setting and wrong for the rest.
        /// The remainder is carried, so a run of short steps still adds up to a step rather than
        /// being repeatedly rounded away.</para>
        /// </remarks>
        /// <summary>
        /// Keep the nine tiles around the party active and the rest off — TASK-130 option (b).
        /// </summary>
        /// <remarks>
        /// <b>Measured before it was built, on zone 6 (2026-08-30):</b> 40 tiles, 14,437 mesh
        /// renderers all enabled at once, 39.8 fps against zone 1's 58.6 with 5,451. Nine tiles is
        /// roughly 2,650 renderers.
        ///
        /// <para>This keeps <see cref="WorldTileCache"/>'s notion of WHICH tiles matter and not its
        /// nine-slot storage, which is a 16-bit memory budget rather than a game rule — the same
        /// call TASK-105 made about the fifth shape-residency slot. It also does not reproduce the
        /// original's stale terrain on a crossing into an uncached tile: every tile here is already
        /// built, so a crossing always shows the right ground.</para>
        /// </remarks>
        private void ApplyTileResidency() {
            if (_zoneSceneBuilder?.Residency == null || _gameSession == null) {
                return;
            }

            _zoneSceneBuilder.Residency.Apply(_gameSession.PositionX, _gameSession.PositionY);
        }

        private void AdvanceRoamingActors() {
            // *** A NEW CHUNK GETS ITS OWN ACTORS. *** The original loads the next chunk's encounter
            // actors on every crossing (czone_resync_on_world_move -> czone_region_encounters_load,
            // CZONE.C:79-99). Nothing here did, so the monsters on screen stayed those of the chunk
            // the zone was loaded in (or the last fight's), and a new tile's roamers never appeared.
            var chunk = (GameData.Resources.World.WorldPlacement.TileOf(_gameSession.PositionX),
                GameData.Resources.World.WorldPlacement.TileOf(_gameSession.PositionY));
            if (_drawnChunk != chunk && _hotspots != null && _hotspots.Combat?.Encounter == null) {
                RedrawEncounterActorsAsync().Forget();
                return;
            }

            if (_encounterActorsRoot == null) {
                return;
            }

            long dx = _gameSession.PositionX - _roamingLastX;
            long dy = _gameSession.PositionY - _roamingLastY;
            _roamingLastX = _gameSession.PositionX;
            _roamingLastY = _gameSession.PositionY;

            // Chebyshev, matching the lattice the world moves on: a diagonal step moves the full
            // delta on both axes, so measuring it as longer than a straight one would run diagonal
            // travel faster than the original does.
            long moved = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
            if (moved <= 0) {
                return;     // a pivot is not a step; nothing roams
            }

            _roamingCarry += moved;
            int ticks = (int)(_roamingCarry / RoamingStepDistance);
            if (ticks <= 0) {
                return;
            }
            _roamingCarry -= (long)ticks * RoamingStepDistance;

            Encounters.RoamingActor[] actors =
                _encounterActorsRoot.GetComponentsInChildren<Encounters.RoamingActor>();
            System.Func<int, int, bool> isRoadAt = RoadPredicate();
            for (var i = 0; i < ticks; i++) {
                foreach (Encounters.RoamingActor actor in actors) {
                    actor.Step(isRoadAt);
                }
            }
        }

        /// <summary>
        /// The road test a <see cref="GameData.Resources.World.RoamingMovement.Pattern.RoadFollowing"/>
        /// actor probes with.
        /// </summary>
        /// <remarks>
        /// <b>It scans the PARTY'S candidate list, and that is faithful rather than a compromise.</b>
        /// <c>worldmove_probe_adjacent_cell</c> calls <c>proximity_scan_list</c> over
        /// <c>g_pwVisibleEntryOffsets</c> / <c>g_nVisibleEntryCount</c> — the visible set built once
        /// per frame around the party — so in the original a road-follower far from the party finds
        /// no road either and about-faces. Rebuilding the candidate list per actor would be a
        /// different game, and an O(records) sort per actor per tick besides.
        ///
        /// <para>The list is whatever the party's own move step last built (<c>BuildCandidates</c> in
        /// <see cref="PartyMovement"/>), which is the same once-per-step list the original shares.</para>
        /// </remarks>
        private System.Func<int, int, bool> RoadPredicate() {
            Collision.ProximityWorld collision = _zoneSceneBuilder?.Collision;
            if (collision == null) {
                return null;
            }
            return (x, y) => collision.TryScan(x, y, out int kind, out _)
                             && GameData.Resources.World.RoadTravel.IsRoadKind(kind);
        }

        /// <summary>One roaming step — a quarter of a cell, the relation the waypoints depend on.</summary>
        private const int RoamingStepDistance =
            GameData.Resources.World.RoadTravel.CellSize / 4;

        private long _roamingCarry;
        private long _roamingLastX;
        private long _roamingLastY;

        private GameObject _zoneRoot;
        private Camera _worldCamera;
        private PartyMovement _partyMovement;
        /// <summary>Whether a fight is on the field — see
        /// <see cref="Hotspots.HotspotService.FightInProgress"/>.</summary>
        public bool FightInProgress => _hotspots != null && _hotspots.FightInProgress;

        /// <summary>Spring a trapped container's ambush — see
        /// <see cref="Hotspots.HotspotService.FireTrapEncounterAt"/>. Sub-tile coordinates.</summary>
        public Hotspots.HotspotService.TrapDispatch FireTrapEncounterAt(int subX, int subY) =>
            _hotspots?.FireTrapEncounterAt(subX, subY) ?? Hotspots.HotspotService.TrapDispatch.Proceed;

        /// <summary>A click on a live encounter group in the world — see
        /// <see cref="Hotspots.HotspotService.HintEncounter"/>.</summary>
        public void HintEncounter(long encounterNumber, int creatureNumber, bool isPrimary) =>
            _hotspots?.HintEncounter(encounterNumber, creatureNumber, isPrimary);

        /// <summary>Cross a zone from a clicked tunnel's own hotspot key — see
        /// <see cref="Hotspots.HotspotService.FireZoneCrossingAt"/>. Sub-tile coordinates.</summary>
        public bool FireZoneCrossingAt(int subX, int subY) =>
            _hotspots != null && _hotspots.FireZoneCrossingAt(subX, subY);

        private Hotspots.HotspotService _hotspots;
        private GameData.Resources.World.ZoneDefinition _zoneDef;

        public WorldRuntime(
            ILogger<WorldRuntime> logger,
            GameSession gameSession,
            ZoneSceneBuilder zoneSceneBuilder,
            IResourceProviderService resources,
            IPreferencesService preferences,
            MidiPlaybackManager midi,
            GameClock clock,
            BakAgain.UI.IDialogManager dialogs = null,
            BakAgain.World.Scenes.LocationScenePlayer locations = null,
            VContainer.IObjectResolver resolver = null,
            WorldLightingService lighting = null,
            BakAgain.UI.InputCore.ICombatOverlayInput overlayInput = null) {
            _logger = logger;
            _gameSession = gameSession;
            _zoneSceneBuilder = zoneSceneBuilder;
            _resources = resources;
            _preferences = preferences;
            _midi = midi;
            _clock = clock;
            _dialogs = dialogs;
            _locations = locations;
            _resolver = resolver;
            _lighting = lighting;
            // Optional: a fight still runs without it, the overlay simply cannot be toggled.
            _overlayInput = overlayInput;
            if (_gameSession != null) {
                _gameSession.ExposeStashOnOpen = ExposeStash;
            }
        }

        /// <summary>
        /// Whether a container the party is opening was robbed while they were away —
        /// <c>actor_maybeEmptyStashByExposure</c> (0x5B148), TASK-249 and TASK-506.
        /// </summary>
        /// <remarks>
        /// <b>Decided at OPEN, once, not on a day tick.</b> Its only callers in the original are the
        /// five WCURSOR.C open handlers (body, container, lock, fixed object, corpse), each right before
        /// <c>cmbinv_inventory_screen_run</c>. So a stash left N days gets one roll at N times its
        /// score when looked at. A daily sweep rolled it N times at 1x..Nx and robbed caches nobody
        /// looked at, on the strength of canassa's name for the routine ("npc_prox_periodic").
        ///
        /// <para><b>The force-empty event is an OR</b> (global 0xdc54, set by the Banath temple
        /// guards' dialog 2200034): every non-exempt stash is emptied whatever the roll. Every rule
        /// lives in <see cref="GameData.Resources.World.StashDecay"/>; this is the plumbing.</para>
        ///
        /// <para>Emptying goes through <c>Items.Clear()</c> plus <c>Dirty</c>, so the write-back and
        /// the last-touch stamp happen the way a player's looting would.</para>
        /// </remarks>
        private void ExposeStash(GameData.Resources.Inventory.RuntimeContainer container) {
            if (container == null || _zoneSceneBuilder?.Collision == null || _gameSession == null) {
                return;
            }

            var scenery = new List<GameData.Resources.World.StashExposure.NearbyEntity>(
                _zoneSceneBuilder.Collision.RecordCount);
            foreach (Collision.ProximityRecord record in _zoneSceneBuilder.Collision.Records) {
                scenery.Add(new GameData.Resources.World.StashExposure.NearbyEntity(
                    (int)record.Entry.Dat.EntityType, record.X, record.Y));
            }

            GameData.Resources.World.StashDecay.Verdict verdict =
                GameData.Resources.World.StashDecay.Decide(container, (uint)_clock.Ticks,
                    inCombat: _hotspots?.FightInProgress ?? false, scenery,
                    roll: UnityEngine.Random.Range(0, GameData.Resources.World.StashExposure.RollRange),
                    forceEmptyEventSet:
                        (_gameSession.GetGlobalValue(GameData.Resources.World.StashExposure.ForceEmptyEvent) ?? 0) != 0);
            if (!verdict.Empties) {
                return;
            }
            container.Items.Clear();
            container.Dirty = true;
            _logger?.LogDebug("Stash exposure emptied a container on opening it (score {Score}).",
                verdict.Score);
        }

        private readonly WorldLightingService _lighting;

        public bool IsBuilt => _zoneRoot != null || _worldCamera != null;

        /// <summary>The built world camera, valid between <see cref="BuildAsync"/> and
        /// <see cref="TeardownAsync"/>. GameFlow routes it into the travel screen's viewport.</summary>
        public Camera WorldCamera => _worldCamera;

        /// <summary>The built party-movement controller (drives the camera + GameSession from nav
        /// input), valid between build and teardown.</summary>
        public PartyMovement Movement => _partyMovement;

        /// <summary>The current zone's Z##DEF.DAT, valid between build and teardown. Read by the
        /// overhead map for its zoom limits, which are per-zone.</summary>
        public GameData.Resources.World.ZoneDefinition ZoneDefinition => _zoneDef;

        /// <summary>The zone's environment (sky/ground/horizon backdrop), or null before the build.
        /// The overhead map switches it into map mode while it is up.</summary>
        public ZoneEnvironment Environment =>
            _zoneRoot != null ? _zoneRoot.GetComponent<ZoneEnvironment>() : null;

        /// <summary>The dungeon automap's entity set, or null above ground. The overhead map shows
        /// it in place of the world while it is up.</summary>
        public DungeonAutomapView Automap =>
            _zoneRoot != null ? _zoneRoot.GetComponent<DungeonAutomapView>() : null;

        /// <summary>Build the world for the session's current zone. The caller (GameFlow) then wires
        /// <see cref="WorldCamera"/>/<see cref="Movement"/> into the travel screen and shows it.</summary>
        public async UniTask BuildAsync() {
            LoggerExtensions.LogInformation(_logger, "Building world. Chapter {Chapter}, Zone {Zone}, Party: {Party}, Gold: {Gold}",
                _gameSession.Chapter,
                _gameSession.CurrentZone,
                string.Join(", ", _gameSession.PartyActorNames ?? System.Array.Empty<string>()),
                _gameSession.PartyGold);

            _zoneRoot = await _zoneSceneBuilder.BuildZoneAsync(_gameSession.CurrentZone);

            // Own a dedicated world camera (not Camera.main).
            _worldCamera = CreateWorldCamera();

            // Sky band + horizon mountains (drawHorizonSkyAndGround, ovr138 @0x460c7).
            (_zoneRoot != null ? _zoneRoot.GetComponent<ZoneEnvironment>() : null)?.ApplyToCamera(_worldCamera);

            // Load the movement table (MOVEMENT.DAT) and the zone definition (Z##DEF.DAT).
            //
            // The zone def carries the walking camera's eye height and pitch, and they ARE per-zone
            // (230/280 outdoors, 250/0 underground). START.DAT's larger pair is a DIFFERENT camera —
            // the combat arena's — so it must not be substituted here; see StartData's type doc.
            var movement = await _resources.LoadAssetAsync<MovementData>("MOVEMENT.DAT", owner: this);
            var zoneDef = await _resources.LoadAssetAsync<GameData.Resources.World.ZoneDefinition>(
                $"Z{_gameSession.CurrentZone:D2}DEF.DAT", owner: this);
            _zoneDef = zoneDef;

            // *** THE VIEW ZOOM IS PER-ZONE, AND THE DUNGEONS DIFFER. *** zone_load re-reads it from
            // Z##DEF.DAT on every zone change (ZONE.C:67-89); zones 1-9 ship 9 and zones 10-12 ship
            // 8, which is a factor of two in projected scale. Measured in Mac Mordain Cadal from the
            // same save on both sides: pinned at 9 our corridor wall stood twice as tall on screen
            // as the original's (TASK-441). CreateWorldCamera's value is the overworld default;
            // this is the zone's own, and it has to be applied after the def is loaded.
            if (_worldCamera != null) {
                _worldCamera.fieldOfView = TravelFov(zoneDef.FocalLength);
            }

            // Seed the overhead map's remembered height — but only on a CHANGE of zone, which is the
            // condition zone_load applies (nZoneId != nPrevZoneId). Seeding on every build would
            // reset the player's zoom every time the world is rebuilt for the same zone (returning
            // from a location scene), which the original does not do.
            if (_gameSession.MapCameraZone != _gameSession.CurrentZone) {
                _gameSession.MapCameraZone = _gameSession.CurrentZone;
                _gameSession.MapCameraZ = zoneDef.CameraZPosition;
            }

            // Zone music, decided from the definition we just read — zone_load_definition (ZONE.C)
            // does it at exactly this point. Only three cases get a track: underground, zone 6, and
            // the final chapter. EVERY OTHER ZONE IS SILENT — there is no travelling music in the
            // original, and what you hear out there is ambience and footsteps. This is also what
            // silences the chapter-intro theme as the world comes up, since silence is a request
            // like any other. Re-entering a zone whose track is already playing is a no-op, so
            // crossing a boundary back and forth does not stutter it.
            await _midi.PlayTrackAsync(
                MusicSelection.ForZone(zoneDef.ZoneLocation, _gameSession.CurrentZone, _gameSession.Chapter),
                _resources, owner: this);

            // Hotspots: the per-chunk trigger tables for the current chapter (docs/specs/
            // collision-system.md §3). Failing to load them must not stop the world coming up —
            // the party then simply walks over inert ground.
            _hotspots = new Hotspots.HotspotService(_logger, _resources, _gameSession, _dialogs, _locations,
                random: null,
                flowAccessor: _resolver == null
                    ? (System.Func<Core.Services.IGameFlow>)null
                    : () => (Core.Services.IGameFlow)_resolver.Resolve(typeof(Core.Services.IGameFlow)),
                combatMenuAccessor: _resolver == null
                    ? (System.Func<BakAgain.UI.Combat.CombatMenu>)null
                    : ResolveCombatMenu,
                setCombatMusic: SetCombatMusic,
                // The same overlay the navigator and a location scene use, so world, screen and
                // fight transitions darken through one surface rather than three.
                fade: _resolver?.Resolve(typeof(BakAgain.UI.Navigation.IScreenFade))
                    as BakAgain.UI.Navigation.IScreenFade,
                // Awaited by HotspotService.SwapArenaThroughFade so the fade holds until the board
                // is actually built — DrawCombatantsAsync spans frames, and revealing before it
                // finished would show an empty arena and pop the combatants in.
                showArena: async entering => {
                    if (entering) {
                        PointCameraAtArena();
                        // Before the arena is built, not after: the map's copy of these creatures
                        // must never be on screen at the same time as the arena's. Also before
                        // the underground still, so they are not photographed into it.
                        ShowEncounterActors(false);
                        await CaptureUndergroundBackdropAsync();
                        SetArenaSceneryCull(true);
                        await DrawCombatantsAsync();
                    } else {
                        ClearArena();
                        ClearCombatBackdrop();
                        ReleaseUndergroundBackdrop();
                        SetArenaSceneryCull(false);
                        RestoreWalkingCamera();
                        // *** THE RESTORE BELONGS TO LEAVING THE FIGHT, NOT TO ClearArena. ***
                        // DrawCombatantsAsync calls ClearArena itself to drop a previous arena
                        // before building the new one, so a restore living in there runs one step
                        // AFTER the hide above and silently cancels it — the map's monsters stayed
                        // on screen beside the arena's, which is the exact thing this prevents.
                        ShowEncounterActors(true);
                        // The fight rewrote the state block: its bodies are now kind 4 (TASK-534). The
                        // original frees the zone for a fight and reloads it after, so the map is
                        // re-read; rebuild the chunk's actors from the block the same way.
                        await RedrawEncounterActorsAsync();
                    }
                },
                // The same by-id player PartyMovement already gets — reusing it rather than adding
                // a second sound path for combat.
                playSfx: id => MenuSoundService.Instance?.Play(id),
                shootMenuAccessor: _resolver == null
                    ? (System.Func<BakAgain.UI.Combat.ShootMenu>)null
                    : ResolveShootMenu,
                castScreenAccessor: _resolver == null
                    ? (System.Func<BakAgain.UI.Spells.CastScreen>)null
                    : ResolveCastScreen,
                navigatorAccessor: _resolver == null
                    ? (System.Func<BakAgain.UI.Navigation.IScreenNavigator>)null
                    : () => _resolver.Resolve(typeof(BakAgain.UI.Navigation.IScreenNavigator))
                        as BakAgain.UI.Navigation.IScreenNavigator,
                // The ground under a prospective arena. Collision is built by BuildZoneAsync above,
                // so it is already there. Whether the check applies — it does not underground —
                // is the service's decision, made from the zone definition it loads itself.
                undergroundFloorCells: CountUndergroundFloorCells,
                sceneryOnTile: _zoneSceneBuilder.Collision == null
                    ? null
                    : (tx, ty) => SceneryOnTile(tx, ty),
                groundKindAt: _zoneSceneBuilder.Collision == null
                    ? (System.Func<int, int, int>)null
                    : (x, y) => _zoneSceneBuilder.Collision.TryScan(x, y, out int kind, out _)
                        ? kind
                        : -1,
                // The hotspot pass moves the party twice — the flight relocation and the underground
                // turn — and both write the SESSION. Without this the view stays where the fight
                // was. Deferred through the field because PartyMovement is built further down.
                resyncCamera: () => _partyMovement?.SyncToCamera(),
                // Each turn changes the board — someone moved, someone fell. Without this the
                // sprites stay exactly as the fight opened.
                redrawArena: () => SlideThenRedrawAsync().Forget(),
                arenaBusy: () => _sliding,
                // The loot screen is the travel HUD's, so the service asks rather than reaching for
                // it. Same shape as showArena: a callback into whoever owns the UI.
                openCorpseLoot: OpenCorpseLoot,
                // The acting fighter's pack (combat command 44). Lazily, like the cast and shoot
                // screens: prefab-backed singletons built at a different moment from this service.
                inventoryMenuAccessor: _resolver == null
                    ? (System.Func<BakAgain.UI.Inventory.InventoryMenu>)null
                    : () => _resolver.Resolve(typeof(BakAgain.UI.Inventory.InventoryMenu))
                        as BakAgain.UI.Inventory.InventoryMenu,
                // Shift on the same command raises the acting fighter's sheet instead (TASK-514).
                characterSheetAccessor: _resolver == null
                    ? (System.Func<BakAgain.UI.Character.CharacterSheetScreen>)null
                    : () => _resolver.Resolve(typeof(BakAgain.UI.Character.CharacterSheetScreen))
                        as BakAgain.UI.Character.CharacterSheetScreen,
                // Read through a lambda, not captured: the zone kind changes as the party travels,
                // and HotspotService outlives any one zone.
                zoneIsUnderground: () => _zoneIsUnderground);

            // *** A DIALOG CAN RE-ARM AN ENCOUNTER, AND IT CANNOT ASK FOR THE SERVICE DIRECTLY. ***
            // Sub-action 3 runs the same routine the eleven self-re-arming encounters do, but
            // HotspotService takes IDialogManager, which owns DialogExecutor — so a constructor
            // dependency would close a cycle. Handing over the method is the same lazy treatment
            // this constructor already gives IGameFlow, and it happens here because this is where
            // both halves exist at once.
            if (_resolver?.Resolve(typeof(Core.Services.DialogExecutor))
                is Core.Services.DialogExecutor dialogExecutor) {
                dialogExecutor.RearmEncounter = _hotspots.RearmEncounter;
                dialogExecutor.ResolveEncounters = _hotspots.ResolveEncounters;
            }

            await _hotspots.LoadZoneAsync(_gameSession.CurrentZone, _zoneSceneBuilder.LoadedChunks, owner: this);

            // Underground zones quarter the step distance and swap the DETECT range table. Both this
            // and ZoneSceneBuilder now read it from the same place — Z##DEF.DAT's first field — so
            // movement and entity stamping cannot disagree about which kind of zone this is.
            bool underground = zoneDef.IsUnderground;
            _zoneIsUnderground = underground;
            // Kept for the dialog backdrop's camera, which states the same pitch the walking eye uses.
            _zoneDefaultCameraPitch = zoneDef.DefaultCameraPitch;

            // Roaming actors advance with the party's movement and only with it — see
            // AdvanceRoamingActors for why this is a step signal rather than a per-frame one.
            _roamingCarry = 0;
            _roamingLastX = _gameSession.PositionX;
            _roamingLastY = _gameSession.PositionY;

            // *** THE ROAMING-ENCOUNTER RESET. *** Subscribed here rather than at construction
            // because it needs MOVEMENT.DAT to resolve a preference index into the distance the
            // original actually compares. Unsubscribe first: entering a second zone must not stack
            // a second handler that resets the roamers twice for one apply.
            _movementTable = movement;
            _preferences.Changed -= OnPreferencesChangedForRoamers;
            _preferences.Changed += OnPreferencesChangedForRoamers;

            // *** ZONE.C's SETUP TAIL, IN ITS ORDER (:168-181). ***
            //     if (nZoneId != nPrevZoneId) worldmove_step_tick_reset();
            //     worldmove_rgn_chap_trans_apply();
            //     g_gameState.nPrevZoneId = g_gameState.nZoneId;
            // A zone CHANGE re-arms the movement-cell boundary so the first step in the new zone
            // counts; a rebuild of the same zone leaves the counter where it was. The original's
            // else-arm — resuming a pending step — belongs to PartyMovement and is not duplicated
            // here.
            if (_gameSession.CurrentZone != _gameSession.PreviousZone) {
                (int count, int tick) = GameData.Resources.World.WorldStepTick.Reset();
                _gameSession.SubTileStepCount = count;
                _gameSession.TileBoundaryCrossed = tick;
            }
            // *** THE SEED GOES BEFORE THE ROAMING RESET, NOT AFTER IT. ***
            // CZONE.C:56 calls czone_region_encounters_load() -> rgnenc_load_encounter_actors()
            // right after the table wipe, and only then does ZONE.C:180's
            // worldmove_rgn_chap_trans_apply reach rgnenc_reset_and_save. Ours seeded from
            // DrawEncounterActorsAsync, seventy lines further down, so the reset ran against an
            // unseeded table, found nothing Roaming and wrote nothing — measured on dir.G01/SAVE07,
            // where the original's TEMP.GAM holds Unplaced at the party's ref-pair and ours held the
            // wiped value. The pass is cached per chunk, so the draw below takes this same list.
            _hotspots.PlaceEncounterActors();
            OnPreferencesChangedForRoamers();
            // *** AND THE STAMP, WHICH NOTHING USED TO DO. *** PreviousZone was loaded from the save,
            // written back, and never touched in between, so the test above could never see a zone
            // change and a save we wrote recorded a zone the party had not come from. TASK-456.
            _gameSession.PreviousZone = _gameSession.CurrentZone;

            _partyMovement = new PartyMovement(_gameSession, movement, _preferences, _worldCamera,
                (int)zoneDef.DefaultCameraZ, zoneDef.DefaultCameraPitch,
                collision: _zoneSceneBuilder.Collision,
                hotspotPass: _hotspots.ActivateAtPartyPosition,
                // Reuses the existing by-id SFX player rather than adding a second sound path.
                playSfx: id => MenuSoundService.Instance?.Play(id),
                underground: underground,
                clock: _clock,
                // The one thing the pit fall needs that movement does not already own.
                showDialog: id => _dialogs?.ShowById(id).Forget());
            _partyMovement.Stepped += AdvanceRoamingActors;

            // *** THE NEVER-DRAWN ENTITIES. *** proxscan_run drops the db1..db8 records (kind 7)
            // and anything FILTER.DAT switches off for this detail level; they have no art and
            // drawing them puts the scan's own bookkeeping in the world. Once per zone, because
            // neither kind nor threshold changes as the party walks. The DISTANCE half of that scan
            // is deliberately not reproduced -- see docs/decisions/0003-view-distance-is-ours.md
            // and the remarks on WorldEntityVisibility.
            if (_zoneRoot != null) {
                var filter = await _resources.LoadAssetAsync<GameData.Resources.Config.FilterData>(
                    "FILTER.DAT", owner: this);
                WorldEntityVisibility visibility = _zoneRoot.GetComponent<WorldEntityVisibility>()
                    ?? _zoneRoot.AddComponent<WorldEntityVisibility>();
                visibility.Apply(filter, _partyMovement.DetailLevel);
            }
            // *** RESIDENCY RIDES EVERY RELOCATION, NOT JUST A STEP. *** Subscribing to Stepped
            // covered walking and nothing else — a pit crossing, a pit fall, a snap and a blocked
            // step's rollback all move the party without one, and would have left the previous
            // ring showing until the next footfall. Relocated hangs off the camera sync, which is
            // the one place every position change already passes through.
            //
            // Still cheap: a tile is 64000 world units and a step is 100, so TileResidency.Apply
            // early-outs on the derived TILE and costs one integer compare until the party actually
            // crosses.
            _partyMovement.Relocated += ApplyTileResidency;
            // Once now, because the party is standing somewhere before it takes its first step and
            // a zone builds with every tile active.
            ApplyTileResidency();
            _partyMovement.SyncToCamera();

            // A backdrop dialog renders the world from the SPEAKER's bearing, not the player's —
            // see DialogBackdropCamera. Only the yaw moves: the original's height and pitch work out
            // to the walking eye the camera already has. Handed over rather than injected, because a
            // delegate is not resolvable from the container.
            (_dialogs as BakAgain.UI.DialogManager)?.SetBackdropCameraSeam(
                TurnCameraToSpeaker, RestoreCameraAfterDialog);


            // The chunk's encounter actors. After SyncToCamera because the facing each sprite is
            // built for is measured against the camera, and before the ambient driver only because
            // nothing here depends on sound.
            await DrawEncounterActorsAsync();

            // The world's ambient SFX. Built here because this is where the zone kind is known, and
            // fed the same by-id player PartyMovement uses rather than opening a second sound path.
            AmbientSoundDriver.Instance = new AmbientSoundDriver(
                n => UnityEngine.Random.Range(0, n),   // rnd(n) is [0, n)
                (soundId, _) => MenuSoundService.Instance?.Play(soundId)) {
                Underground = underground,
                Chapter = _gameSession.Chapter,
                Zone = _gameSession.CurrentZone,
                MoodFlagSet = (_gameSession.GetGlobalValue(AmbientSound.MoodFlag) ?? 0) != 0,
            };

            // The lighting has to know the zone kind before its first refresh: underground ignores
            // the clock entirely, so getting this after the fact would light a cave by the hour for
            // one frame.
            if (_lighting != null) {
                _lighting.Underground = underground;
                var env = _zoneRoot != null ? _zoneRoot.GetComponent<ZoneEnvironment>() : null;
                _lighting.BindSky(_worldCamera, env != null ? env.SkyColor : Color.black);
                _lighting.Refresh();
            }
        }

        /// <summary>Destroy the world. The caller cleared the navigator stack (hiding the travel
        /// screen, which releases the camera's RenderTexture) before calling this.</summary>
        public UniTask TeardownAsync() {
            // The driver is zone-scoped: leaving the world must stop the ambience, or a menu or a
            // fight would keep hearing the last zone's birds.
            AmbientSoundDriver.Instance = null;
            LoggerExtensions.LogInformation(_logger, "Tearing down world.");
            _resources.ReleaseAssets(this);
            if (_partyMovement != null) {
                _partyMovement.Stepped -= AdvanceRoamingActors;
                _partyMovement.Relocated -= ApplyTileResidency;
            }
            _partyMovement = null;
            _hotspots = null;

            ReleaseUndergroundBackdrop();
            if (_worldCamera != null) {
                Object.Destroy(_worldCamera.gameObject);
                _worldCamera = null;
            }
            if (_zoneRoot != null) {
                Object.Destroy(_zoneRoot);
                _zoneRoot = null;
            }
            return UniTask.CompletedTask;
        }

        private IWorldViewport _viewport;

        /// <summary>
        /// The world camera's vertical FOV through the travel viewport, at a zone's focal length, or
        /// START.DAT's (the travel and combat lens) when <paramref name="focalLength"/> is null.
        /// </summary>
        /// <remarks>
        /// The camera is square and takes the viewport's own aspect: the original's 6:5 pixel is in
        /// the world's heights, not here (WorldProjection, TASK-764).
        /// </remarks>
        private float TravelFov(int? focalLength) {
            _viewport ??= _resolver?.Resolve(typeof(IWorldViewport)) as IWorldViewport;
            if (_viewport == null) {
                return _worldCamera != null ? _worldCamera.fieldOfView : 60f;
            }
            return (float)GameData.Resources.World.WorldProjection.VerticalFovDegrees(
                _viewport.CanonicalRect.Height, focalLength ?? _viewport.FocalLength);
        }

        private Camera CreateWorldCamera() {
            var go = new GameObject("WorldCamera");
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 2000f;
            // The original's 3D viewport is a narrow, zoomed view — not the Unity default 60°.
            // Without this the world looks far too wide-angle. The map screens are NOT this lens —
            // see WorldProjection and ZoneEnvironment.SetOverheadMapMode.
            cam.fieldOfView = TravelFov(null);
            return cam;
        }
    }
}
