namespace BakAgain.World {
    using System.Collections.Generic;
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// The dungeon automap's entity set: the same placements the world has, built from the zone's
    /// <b>map</b> model table, of which only the ones the party has walked past are shown.
    ///
    /// <para>Faithful to <c>renderDungeonAutomap</c> (canassa <c>worldframe_render_chapter_full</c>).
    /// The original is not a 2D map — it runs the ordinary 3D renderer with the record table swapped
    /// to slot 2 and draws only entities whose bit is set in <see cref="EncounterVisitTable"/>. We
    /// cannot swap a table under a live scene graph, so the map models are built alongside the world
    /// ones at zone load and the two sets are swapped by activation. Underground zones only — see
    /// <see cref="LocalMapScreen.DrawsDungeonAutomap"/>.</para>
    ///
    /// <para>There is no party icon: the centred blit at the end of the original's automap is
    /// <c>#ifndef V102CD</c> and we are the CD build.</para>
    /// </summary>
    public sealed class DungeonAutomapView : MonoBehaviour {
        private readonly List<Placement> _placements = new();
        private GameObject _mapRoot;
        private GameObject[] _worldRoots = System.Array.Empty<GameObject>();

        private readonly struct Placement {
            public readonly byte TileX;
            public readonly byte TileY;
            public readonly int IndexInTile;
            public readonly GameObject Instance;

            public Placement(byte tileX, byte tileY, int indexInTile, GameObject instance) {
                TileX = tileX;
                TileY = tileY;
                IndexInTile = indexInTile;
                Instance = instance;
            }
        }

        /// <summary>How many placements have a map model at all.</summary>
        /// <remarks>
        /// Fewer than the zone has placements, and that is correct: the map table is a simplified
        /// variant with deliberate gaps, so some entities never appear on the automap however often
        /// the party walks past them. See <see cref="LocalMapScreen.AutomapModelTableSlot"/>.
        /// </remarks>
        public int PlacementCount => _placements.Count;

        /// <summary>How many were shown by the last <see cref="Show"/> — i.e. visited AND mapped.</summary>
        public int LastShownCount { get; private set; }

        public bool IsShowing { get; private set; }

        /// <summary>
        /// Hand over the built map-entity root and the world roots it stands in for. Called once by
        /// the zone builder; the map root starts inactive so travel pays nothing for it.
        /// </summary>
        public void Initialize(GameObject mapRoot, params GameObject[] worldRoots) {
            _mapRoot = mapRoot;
            _worldRoots = worldRoots ?? System.Array.Empty<GameObject>();
            if (_mapRoot != null) {
                _mapRoot.SetActive(false);
            }
        }

        /// <summary>Register one built map entity against the placement it stands for.</summary>
        public void Add(byte tileX, byte tileY, int indexInTile, GameObject instance) {
            if (instance == null || indexInTile < 0) {
                return;
            }
            _placements.Add(new Placement(tileX, tileY, indexInTile, instance));
        }

        /// <summary>
        /// Swap the world out for the automap and reveal what has been visited. Returns how many
        /// entities were shown, which is the number the map screen is actually drawing.
        /// </summary>
        public int Show(EncounterVisitTable visits, byte zone) {
            if (_mapRoot == null) {
                return 0;
            }

            var shown = 0;
            foreach (Placement placement in _placements) {
                bool seen = visits != null
                    && visits.HasSeen(zone, placement.TileX, placement.TileY, placement.IndexInTile);
                if (placement.Instance != null) {
                    placement.Instance.SetActive(seen);
                }
                if (seen) {
                    shown++;
                }
            }

            SetWorldActive(false);
            _mapRoot.SetActive(true);
            IsShowing = true;
            LastShownCount = shown;
            return shown;
        }

        /// <summary>Put the world back. Safe to call when the map was never shown.</summary>
        public void Hide() {
            if (_mapRoot != null) {
                _mapRoot.SetActive(false);
            }
            SetWorldActive(true);
            IsShowing = false;
        }

        private void SetWorldActive(bool active) {
            foreach (GameObject root in _worldRoots) {
                if (root != null) {
                    root.SetActive(active);
                }
            }
        }
    }
}
