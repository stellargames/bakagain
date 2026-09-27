namespace BakAgain.ResourceManagement.Models {
    using GameData.Resources.World;

    /// <summary>
    /// Source-agnostic, engine-independent metadata for a world model. Phase 1 carries the fields
    /// the WorldEntity component needs; Phase 2 extends it with the Gid collision footprint and
    /// extent/bbox when the .glb override + metadata sidecar land.
    /// </summary>
    public sealed class WorldModelMetadata {
        public int TypeId;
        public string Name;
        public WorldEntityType EntityType;
        public bool IsUnbounded;
        public bool IsDepthSorted;
        public byte DrawPriority;

        public static WorldModelMetadata FromEntry(ZoneTableEntry entry, int typeId) {
            var dat = entry.Dat;
            return new WorldModelMetadata {
                TypeId = typeId, Name = entry.Name,
                EntityType = dat.EntityType, IsUnbounded = dat.IsUnbounded, IsDepthSorted = dat.IsDepthSorted,
                DrawPriority = dat.DrawPriority,
            };
        }
    }
}
