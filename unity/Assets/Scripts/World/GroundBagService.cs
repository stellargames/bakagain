namespace BakAgain.World {
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Inventory;
    using GameData.Resources.World;
    using Microsoft.Extensions.Logging;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Spawns and despawns the "bag" entity that makes a discarded pile visible and clickable.
    ///
    /// <para>A dropped item lands in a <see cref="GameData.Resources.Data.SaveGameContainerType.Bag"/>
    /// container (see <see cref="GroundContainerPool"/>), and the container alone is invisible: world
    /// entities otherwise come only from a zone's WLD tiles, which are authored data and know nothing
    /// about runtime drops. This service is the one place that builds an entity for a container.</para>
    ///
    /// <para>It renders TBL entry <see cref="GroundContainerPool.BagWorldItemId"/> — the zone table's
    /// "bag", which already carries <c>Behavior = "container"</c> and an interaction profile whose
    /// actionable type is <c>Bag</c>, so <see cref="Interaction.ContainerInteractionHandler"/> loots
    /// it with no special-casing. The entity is built through the shared
    /// <see cref="WorldEntityBuilder"/> like every other world object.</para>
    /// </summary>
    public interface IGroundBagSpawner {
        /// <summary>Make <paramref name="bag"/> visible in the world. A no-op when no zone is built
        /// or the bag belongs to a different zone than the one on screen.</summary>
        UniTask SpawnAsync(RuntimeContainer bag);

        /// <summary>Remove a freed bag's entity. Safe to call for a bag that was never spawned.</summary>
        void Despawn(RuntimeContainer bag);
    }

    /// <summary>
    /// Session-scoped <see cref="IGroundBagSpawner"/>. The per-zone pieces it needs to build an
    /// entity (the zone table entry, render context, model loader, parent transform) only exist
    /// while a zone is built, so <see cref="ZoneSceneBuilder"/> hands them over at the end of a
    /// build and revokes them on teardown. Between those, spawning is a logged no-op rather than an
    /// error: dropping an item with no world on screen is legitimate and the bag will be built by
    /// the next zone build from the container state.
    /// </summary>
    public sealed class GroundBagService : IGroundBagSpawner {
        private readonly ILogger<GroundBagService> _logger;

        // The live zone's build context, or null between builds.
        private ZoneContext _zone;

        // Spawned entities, so a released bag can be found and destroyed again. Keyed by the
        // container instance because that is the identity the pool preserves across a claim.
        private readonly Dictionary<RuntimeContainer, GameObject> _spawned =
            new Dictionary<RuntimeContainer, GameObject>();

        public GroundBagService(ILogger<GroundBagService> logger) {
            _logger = logger;
        }

        private sealed class ZoneContext {
            public int ZoneNumber;
            public ZoneTableEntry BagEntry;
            public int BagTypeId;
            public WorldEntityRenderContext RenderContext;
            public WorldModelLoader ModelLoader;
            public Transform Parent;
        }

        /// <summary>
        /// Publish the zone that is now on screen. <paramref name="bagEntry"/> is the zone table's
        /// bag row; a zone whose table has no such row simply cannot show bags, which is reported
        /// once here rather than on every drop.
        /// </summary>
        public void SetZone(int zoneNumber, ZoneTableEntry bagEntry, int bagTypeId,
            WorldEntityRenderContext renderContext, WorldModelLoader modelLoader, Transform parent) {
            _spawned.Clear();
            if (bagEntry == null) {
                // Bags cannot be drawn, but the context still serves self-spawned loot, which is
                // built from each record's own table row (TASK-557).
                _logger.LogWarning(
                    "Zone {Zone} has no TBL entry {TypeId} (bag): discarded items will be invisible.",
                    zoneNumber, bagTypeId);
            }
            _zone = new ZoneContext {
                ZoneNumber = zoneNumber,
                BagEntry = bagEntry,
                BagTypeId = bagTypeId,
                RenderContext = renderContext,
                ModelLoader = modelLoader,
                Parent = parent,
            };
        }

        /// <summary>Revoke the zone context — its render context and model loader are destroyed with
        /// the zone root, so holding them past teardown would hand out dead references.</summary>
        /// <remarks>Only if it is still <paramref name="owner"/>'s: the teardown runs at the end of the
        /// frame and can land after the next zone published its context (see
        /// <c>DoorVisualService.ClearZone</c>).</remarks>
        public void ClearZone(WorldEntityRenderContext owner) {
            if (_zone == null || _zone.RenderContext != owner) {
                return;
            }
            _zone = null;
            _spawned.Clear();
        }

        public UniTask SpawnAsync(RuntimeContainer bag) =>
            _zone?.BagEntry == null
                ? UniTask.CompletedTask
                : SpawnAsync(bag, _zone.BagEntry, _zone.BagTypeId, "DroppedBag");

        /// <summary>
        /// Put a container record into the world as the object its table row describes — a bag with
        /// the bag row, or self-spawned loot with the row its <c>WorldItemId</c> names (TASK-557).
        /// </summary>
        public async UniTask SpawnAsync(RuntimeContainer container, ZoneTableEntry entry, int typeId,
            string namePrefix) {
            if (container == null || entry == null || _zone == null) {
                return;
            }
            if (container.Zone != _zone.ZoneNumber) {
                return; // a record in a zone we are not showing; the next build of that zone spawns it
            }
            if (_spawned.ContainsKey(container)) {
                return;
            }

            Vector3 position = BakCoordinateConverter.ConvertPosition(container.X, container.Y, 0);
            GameObject go = await WorldEntityBuilder.Build(
                entry, typeId, position, Quaternion.identity,
                _zone.Parent, _zone.Parent, _zone.RenderContext, _zone.ModelLoader);

            if (go == null) {
                _logger.LogWarning("{Prefix} entity at ({X}, {Y}) in zone {Zone} was not renderable.",
                    namePrefix, container.X, container.Y, container.Zone);
                return;
            }
            go.name = $"{namePrefix}_{container.X}_{container.Y}";
            _spawned[container] = go;
        }

        public void Despawn(RuntimeContainer bag) {
            if (bag == null || !_spawned.TryGetValue(bag, out GameObject go)) {
                return;
            }
            _spawned.Remove(bag);
            if (go != null) {
                Object.Destroy(go);
            }
        }
    }
}
