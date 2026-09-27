namespace BakAgain.World {
    using GameData.Resources.Config;
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// Drops the entities that are never drawn at all — the identity half of <c>proxscan_run</c>
    /// (canassa <c>SRC/R3D/VIS/PROXSCAN.C:72-115</c>).
    /// </summary>
    /// <remarks>
    /// <b>THE DISTANCE HALF IS DELIBERATELY NOT HERE — see
    /// <c>docs/decisions/0003-view-distance-is-ours.md</c>.</b> The original also culls each entity
    /// at a per-kind FILTER.DAT distance, and this class used to reproduce it. Owner's rule,
    /// 2026-09-20: the port's draw distance is its own, because those thresholds were cut for a
    /// 320x200 software renderer and reproduced here they pop scenery in and out in clear air.
    /// Measured on the overworld: most kinds were switched off between 187 and 312 units while the
    /// terrain fog does not even START until 320 and the haze only reaches full at 640. Nothing was
    /// hiding the change, so the player watched trees and buildings blink. What bounds the view now
    /// is <see cref="TileResidency"/>'s nine-tile ring (640 units to the nearest edge) with the fog
    /// saturating at the same 640 — so the edge of the world is already solid haze. Do not
    /// "restore" the thresholds here.
    ///
    /// <para><b>The DUNGEON AUTOMAP is the second consumer, and the one that hid the damage.</b>
    /// <see cref="DungeonAutomapView"/>'s map models are built into the same zone root and each
    /// hangs off a <see cref="WorldEntity"/>, so the old per-move pass culled them exactly like
    /// scenery — and their kinds are the cheap ones: 14, 15 and 23 all sit at a 100-unit threshold.
    /// Measured in zone 10 on 2026-09-20, of the 113 features the party had actually explored the
    /// gate hid <b>97</b>, leaving a stub of corridor that followed the party around. The map read
    /// as "the port doesn't remember where you have been" when the visit record was complete and
    /// correct the whole time and simply was not being drawn. If a distance cull is ever wanted
    /// again, exclude the automap set or it will silently eat the explored map.</para>
    ///
    /// <para><b>What remains is not a distance rule and must stay.</b> Two exclusions in
    /// <see cref="ProximityScan.JoinsVisibleList"/> are about WHAT a thing is, not how far off it
    /// is: <see cref="ProximityScan.NeverRenderedKind"/> (the <c>db1..db8</c> records, which have no
    /// art and are dropped outright) and a FILTER.DAT threshold of
    /// <see cref="ProximityScan.DisabledThreshold"/> (the kind is switched off at this detail
    /// level). Dropping those along with the distance test would have drawn the scan's own
    /// bookkeeping records into the world. The shipped table has no -1 at any level, so that arm is
    /// for mods.</para>
    ///
    /// <para><b>Renderers and colliders, not <c>SetActive</c>.</b> Two things find entities with
    /// <c>FindObjectsByType</c>/<c>GetComponentsInChildren</c>, which skip inactive objects: the
    /// locator map's dot pass and <c>WorldRuntime</c>'s arena cull. The locator is supposed to show
    /// marks right across the zone, so deactivating an entity would silently empty it. The collider
    /// goes with the renderer because the original's hit test walks the draw list too, so something
    /// it never draws cannot be clicked either.</para>
    ///
    /// <para><b>Runs once per zone.</b> Kind and threshold do not change as the party walks, so
    /// unlike the old distance gate there is nothing to refresh on movement.</para>
    /// </remarks>
    public sealed class WorldEntityVisibility : MonoBehaviour {
        /// <summary>
        /// Hides this zone's never-drawn entities. Call once, after the zone root is populated.
        /// </summary>
        /// <param name="filter">FILTER.DAT, or null to leave everything drawn.</param>
        /// <param name="detailLevel">The block <c>config.levelOfDetail</c> selects.</param>
        /// <returns>How many entities were hidden.</returns>
        public int Apply(FilterData filter, int detailLevel) {
            int[] thresholds = filter?.DrawDistancesFor(detailLevel);
            var hidden = 0;
            foreach (WorldEntity entity in GetComponentsInChildren<WorldEntity>(true)) {
                var kind = (byte)entity.EntityType;
                // Out of the table's range keeps the entity drawn: an unknown kind should not make
                // scenery vanish, the same way ProximityWorld keeps it rather than lose collision.
                int threshold = thresholds != null && kind < thresholds.Length
                    ? thresholds[kind]
                    : ProximityScan.AlwaysVisibleThreshold;
                if (kind != ProximityScan.NeverRenderedKind
                    && threshold != ProximityScan.DisabledThreshold) {
                    continue;
                }

                foreach (Renderer r in entity.GetComponentsInChildren<Renderer>(true)) {
                    r.enabled = false;
                }
                foreach (Collider c in entity.GetComponentsInChildren<Collider>(true)) {
                    c.enabled = false;
                }
                hidden++;
            }
            return hidden;
        }
    }
}
