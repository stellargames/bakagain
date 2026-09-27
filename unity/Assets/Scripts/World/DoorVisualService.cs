namespace BakAgain.World {
    using BakAgain.Core;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.World;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Re-shapes a door when it opens or shuts — the runtime half of
    /// <c>worlddoor_load_door_records</c>'s shape swap.
    /// </summary>
    /// <remarks>
    /// <b>Shut and open are two different MODELS</b> (0x5c / 0x5d), not one model animated, so
    /// changing a door's state means building the other one. The zone builder already picks the
    /// right shape at load time; this is what keeps a door honest when the player opens it without
    /// leaving the zone.
    ///
    /// <para>It holds the zone's render context and model loader the way
    /// <see cref="GroundBagService"/> does, for the same reason: both build a world entity after
    /// the zone is already on screen, and both must let go at teardown because those objects die
    /// with the zone root.</para>
    /// </remarks>
    public sealed class DoorVisualService {
        private readonly ILogger _logger;
        private ZoneContext _zone;

        public DoorVisualService() =>
            _logger = Microsoft.Extensions.Logging.LoggerFactoryExtensions
                .CreateLogger<DoorVisualService>(LogManager.LoggerFactory);

        private sealed class ZoneContext {
            public ZoneTable Table;
            public WorldEntityRenderContext RenderContext;
            public WorldModelLoader ModelLoader;
            public Transform Parent;
            public Collision.ProximityWorld Collision;
        }

        /// <summary>Publish the zone that is now on screen.</summary>
        public void SetZone(ZoneTable table, WorldEntityRenderContext renderContext,
            WorldModelLoader modelLoader, Transform parent,
            Collision.ProximityWorld collision = null) =>
            _zone = new ZoneContext {
                Table = table,
                RenderContext = renderContext,
                ModelLoader = modelLoader,
                Parent = parent,
                Collision = collision,
            };

        /// <summary>Revoke it — the context dies with the zone root.</summary>
        /// <remarks>
        /// <b>Only if it is still THAT zone's.</b> The root is torn down by <c>Object.Destroy</c>,
        /// which runs at the end of the frame, and a zone rebuilt from cache publishes its own
        /// context before then. An unconditional clear wiped the NEW zone's context, so every door
        /// opened afterwards flipped its flag and never gained the doorway's floor (Cavall Run,
        /// 2026-09-23: an open door the party could not walk through).
        /// </remarks>
        internal bool HasZone => _zone != null;

        public void ClearZone(WorldEntityRenderContext owner) {
            if (_zone != null && _zone.RenderContext == owner) {
                _zone = null;
            }
        }

        /// <summary>
        /// Swings the panel — the eight-frame loop at <c>handle_Door</c> @0x77adf.
        /// </summary>
        /// <remarks>
        /// <b>This is the whole visible animation.</b> The shape swap beside it changes no pixels
        /// (0x5c and 0x5d are the same geometry with and without a collision region); what the
        /// player sees is the flagged mesh's face changing, and its eight faces are a panel
        /// rotating about a fixed hinge edge.
        ///
        /// <para>Opening runs the frames forward and closing runs them back, and the door is left
        /// standing on the last one — which is why a door loaded from a save shows its state
        /// without animating (<see cref="DoorMechanics.SeedState"/> seeds frame 7 or 0).</para>
        ///
        /// <para>The original redraws the whole world per frame and so has no timer; here a frame
        /// is a frame, which is the same pacing without the full repaint.</para>
        /// </remarks>
        public async UniTask SwingAsync(GameObject door, bool open) {
            WorldMeshFrames frames = door == null ? null : door.GetComponent<WorldMeshFrames>();
            if (frames == null || frames.FrameCount <= 1) {
                return;   // nothing animated on this entity
            }

            for (var step = 0; step < frames.FrameCount; step++) {
                frames.SetFrame(open ? step : frames.FrameCount - 1 - step);
                await UniTask.Yield();
            }
        }

        /// <summary>Puts a door straight onto the frame its state calls for, with no animation.</summary>
        public static void SeedFrame(GameObject door, bool open) {
            WorldMeshFrames frames = door == null ? null : door.GetComponent<WorldMeshFrames>();
            if (frames != null && frames.FrameCount > 1) {
                frames.SetFrame(open ? frames.FrameCount - 1 : 0);
            }
        }

        /// <summary>
        /// Rebuild <paramref name="door"/> in the shape <paramref name="open"/> calls for.
        /// </summary>
        /// <returns>The new entity, or null when nothing had to change or nothing could be built.</returns>
        public async UniTask<GameObject> SwapAsync(WorldEntity door, bool open) {
            int wanted = DoorMechanics.ShapeFor(open);
            if (_zone == null || door == null || door.TypeId == wanted) {
                return null;   // already the right shape, or no zone to build into
            }

            ZoneTableEntry entry = null;
            foreach (ZoneTableEntry e in _zone.Table.Entries) {
                if (e.Index == wanted) {
                    entry = e;
                    break;
                }
            }
            if (entry == null) {
                _logger?.LogWarning("Zone table has no entry {Shape} — a door cannot change shape.",
                    wanted);

                return null;
            }

            Transform old = door.transform;

            // *** THE COLLISION WORLD FIRST, AND WHATEVER THE RENDER DOES. *** The open shape's GID
            // region is the floor of the doorway (ProximityWorld.SetEntryAt), so this — not the
            // model — is what lets the party walk through. It follows the flag like the model does,
            // which is why a build that fails below still leaves an open door passable: a door that
            // looks shut and lets you through is recoverable, one that looks open and does not is
            // the bug this whole swap exists to prevent.
            if (_zone.Collision != null) {
                Vector3 p = old.position;
                (int bakX, int bakY) = BakCoordinateConverter.ToBakXY(p);
                _zone.Collision.SetEntryAt(bakX, bakY, (ushort)door.RotationZ,
                    (int)Mathf.Round(p.y * BakCoordinateConverter.WorldScale), entry);
            }
            GameObject built = await WorldEntityBuilder.Build(
                entry, wanted, old.localPosition, old.localRotation,
                _zone.Parent, old.parent != null ? old.parent : _zone.Parent,
                _zone.RenderContext, _zone.ModelLoader);
            if (built == null) {
                return null;   // not renderable: leave the door as it was rather than deleting it
            }

            built.name = door.name;
            // Destroyed only once the replacement exists, so a failed build cannot leave a hole
            // where a door used to be.
            Object.Destroy(door.gameObject);

            return built;
        }
    }
}
