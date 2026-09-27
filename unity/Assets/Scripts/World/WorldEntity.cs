namespace BakAgain.World {
    using GameData.Resources.Data;
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// Attached to every placed world object. Stores TBL metadata for the entity
    /// so that render profiles and game systems can query what this object is.
    /// </summary>
    public class WorldEntity : MonoBehaviour {
        [Header("TBL Identity")]
        public int TypeId;
        public string EntityName;

        [Header("TBL Properties")]
        public WorldEntityType EntityType;
        /// <summary>
        /// The placement's raw BaK Z rotation, as the WLD stores it — 0x10000 to the full turn.
        /// </summary>
        /// <remarks>
        /// <b>Kept because the transform cannot give it back.</b> The builder converts it into a
        /// Quaternion and keeps nothing, so a rule that reads the ORIGINAL's rotation value has no
        /// source at all once the entity is built. <see cref="GameData.Resources.World.PitRopeCrossing.AxisOf"/>
        /// is one: it tests four exact equalities, and recovering them from a float quaternion means
        /// re-deriving an integer the WLD already stated.
        ///
        /// <para>The proximity records carry the same value for collision, so this is not the only
        /// copy — but they are keyed by tile and index, and an interaction handler is handed the
        /// entity.</para>
        /// </remarks>
        public int RotationZ;

        public bool IsUnbounded;
        public bool IsDepthSorted;
        public byte DrawPriority;

        [Header("Rendering")]
        public bool IsSprite;
        public int SpriteIndex = -1;

        [Header("Interaction")]
        // Semantic dispatch key (null/empty = non-interactable) + its data-driven profile,
        // both copied from the ZoneTableEntry at build time. InteractionProfile is a plugin
        // reference type, not Unity-serializable — runtime-only, set by WorldEntityBuilder.
        [System.NonSerialized] public string Behavior;
        [System.NonSerialized] public InteractionProfile Interaction;

        // The entity's own world extent — TBL Extent << VertexScale, the original's radius << shift.
        // Subtracted from a centre-to-centre distance so a big thing is found from further out than
        // a small one; the locator spells' list scans use it (LocatorMap.Marks). 0 = no extent.
        [System.NonSerialized] public float WorldExtent;

        // DETECT.DAT detection range (game/fine units) for this entity's zone location; 0 = no gate.
        // The click only registers within this range (renderAndDetectVisibleItems' distance <= DetectRanges[type]).
        [System.NonSerialized] public float DetectionRange;
    }
}
