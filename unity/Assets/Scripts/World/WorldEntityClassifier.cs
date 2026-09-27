namespace BakAgain.World {
    using GameData.Resources.World;

    /// <summary>
    /// Semantic kind of a world entity, derived from its TBL/DAT flags and geometry.
    /// Decides how <see cref="ZoneSceneBuilder"/> renders a zone item.
    /// </summary>
    public enum WorldEntityKind {
        /// <summary>Not rendered (empty entity, encounter marker, sprite with no geometry, …).</summary>
        None,

        /// <summary>Ground/terrain polygon — multi-submesh, one submesh per pen.</summary>
        Terrain,

        /// <summary>Camera-facing sprite billboard.</summary>
        Sprite,

        /// <summary>Non-terrain polygon mesh (buildings, fences, …).</summary>
        PolygonEntity
    }

    /// <summary>
    /// Pure classification of a world entity from its DAT entity flags/type plus two
    /// geometry facts (whether it has polygon geometry and whether it references a
    /// sprite bitmap). Kept free of UnityEngine types so it is unit-testable in
    /// isolation. Encodes the branch logic that previously lived inline in
    /// <see cref="ZoneSceneBuilder"/>.
    /// </summary>
    public static class WorldEntityClassifier {
        public static WorldEntityKind Classify(bool isUnbounded, bool isDepthSorted,
            WorldEntityType entityType, bool hasPolygons, bool hasSprite) {
            // Terrain gate was `entityFlags == 0` (whole byte); for all shipped data
            // (flags ∈ {0x00,0x40,0x60}) that is exactly "neither known flag set". See the plan's
            // terrain-gate equivalence note.
            bool noFlags = !isUnbounded && !isDepthSorted;
            bool isTerrain = noFlags && ((byte)entityType is 0 or 1 or 3);
            if (isTerrain && hasPolygons) {
                return WorldEntityKind.Terrain;
            }

            bool isSprite = hasSprite && !hasPolygons && isUnbounded && isDepthSorted;
            if (isSprite) {
                return WorldEntityKind.Sprite;
            }

            if (hasPolygons) {
                return WorldEntityKind.PolygonEntity;
            }

            return WorldEntityKind.None;
        }
    }
}
