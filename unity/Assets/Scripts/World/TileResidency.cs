namespace BakAgain.World {
    using System.Collections.Generic;
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// Keeps only the tiles around the party active — TASK-130, option (b).
    /// </summary>
    /// <remarks>
    /// <b>The original's rule without the original's storage.</b> <c>WorldTileCache</c> keeps nine
    /// tiles: the party's and the eight around it. Those nine SLOTS are a 16-bit memory budget —
    /// nine fixed partitions of one allocation — and this deliberately does not reproduce them.
    /// What it keeps is <see cref="WorldTileCache.IsResident"/>, which is the game-facing half:
    /// which tiles the engine is ever willing to draw.
    ///
    /// <para><b>It toggles parents that already exist rather than streaming anything.</b>
    /// <see cref="ZoneSceneBuilder"/> already groups every tile's terrain, entities and automap
    /// models under per-tile GameObjects, so residency is <c>SetActive</c> on those and nothing
    /// more. Measured on zone 6 (2026-08-30): 40 tiles, 14,437 mesh renderers all enabled at once
    /// and 39.8 fps; nine tiles is roughly 2,650 of them.</para>
    ///
    /// <para><b>It does NOT reproduce the original's stale-terrain behaviour.</b> There, crossing
    /// into an uncached tile does nothing at all — <c>CheckAndLoadNewTile</c> returns — which is
    /// invisible only because the ring is maintained ahead of the party. Every tile here is already
    /// built, so a crossing can always show the right ground.</para>
    /// </remarks>
    public sealed class TileResidency {
        private readonly Dictionary<(int X, int Y), List<GameObject>> _byTile = new();
        private (int X, int Y)? _appliedFor;

        /// <summary>Tiles this zone registered.</summary>
        public int TileCount => _byTile.Count;

        /// <summary>The tile the last <see cref="Apply"/> centred on, if any.</summary>
        public (int X, int Y)? Centre => _appliedFor;

        /// <summary>Put a tile's root objects under residency control.</summary>
        /// <remarks>
        /// Called once per tile per parent as the zone is built. Nulls are skipped so a zone with no
        /// automap registers two roots per tile rather than needing a special case.
        /// </remarks>
        public void Register(int tileX, int tileY, params GameObject[] roots) {
            if (roots == null) {
                return;
            }

            if (!_byTile.TryGetValue((tileX, tileY), out List<GameObject> list)) {
                list = new List<GameObject>();
                _byTile[(tileX, tileY)] = list;
            }
            foreach (GameObject root in roots) {
                if (root != null) {
                    list.Add(root);
                }
            }
        }

        /// <summary>
        /// Activate the nine tiles around the party's world position and deactivate the rest.
        /// </summary>
        /// <returns>Whether anything changed — false when the party has not left its tile.</returns>
        /// <remarks>
        /// <b>Early-outs on the tile, not on the position.</b> The party moves every frame and
        /// crosses a tile boundary rarely, so comparing the derived tile is what keeps this off the
        /// per-frame cost; comparing positions would re-walk every tile for a step of 100 units.
        /// </remarks>
        public bool Apply(long partyWorldX, long partyWorldY) {
            var tile = (X: WorldTileCache.TileOf(partyWorldX), Y: WorldTileCache.TileOf(partyWorldY));
            if (_appliedFor is { } was && was == tile) {
                return false;
            }

            _appliedFor = tile;
            foreach (KeyValuePair<(int X, int Y), List<GameObject>> entry in _byTile) {
                bool resident = WorldTileCache.IsResident(entry.Key.X, entry.Key.Y, tile.X, tile.Y);
                foreach (GameObject root in entry.Value) {
                    if (root != null && root.activeSelf != resident) {
                        root.SetActive(resident);
                    }
                }
            }
            return true;
        }

        /// <summary>Turn every tile back on — the state a zone is built in.</summary>
        /// <remarks>
        /// For anything that needs the whole zone at once: the overhead map, a screenshot, or a
        /// caller that has not established where the party is. Clears the centre so the next
        /// <see cref="Apply"/> is not skipped by the early-out.
        /// </remarks>
        public void ShowAll() {
            _appliedFor = null;
            foreach (List<GameObject> roots in _byTile.Values) {
                foreach (GameObject root in roots) {
                    if (root != null && !root.activeSelf) {
                        root.SetActive(true);
                    }
                }
            }
        }
    }
}
