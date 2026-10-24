namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.World;
    using GameData.Resources.World;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// The kind-7 records (<c>db1..db8</c>, LaMut's buildings among them) are drawn on the overhead map
    /// and nowhere else: the travel scan skips kind 7 (<c>proxscan_run</c>, PROXSCAN.C:90) while the
    /// map's full frame (<c>world_render_frame_full</c>, WORLDHIT.C:192) draws from
    /// <c>proxscan_encounter_records</c>, which admits it (PROXSCAN.C:220). The port hid them for the
    /// whole zone, so LaMut's map was empty (owner, 2026-10-19).
    /// </summary>
    public class MapOnlyPatchesTests {
        private GameObject _zone;
        private Camera _camera;

        [TearDown]
        public void TearDown() {
            Object.DestroyImmediate(_zone);
            if (_camera != null) Object.DestroyImmediate(_camera.gameObject);
            Shader.SetGlobalFloat("_MapMode", 0f);
        }

        private Renderer Entity(WorldEntityType type) {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.transform.SetParent(_zone.transform, false);
            go.AddComponent<WorldEntity>().EntityType = type;
            return go.GetComponent<Renderer>();
        }

        [Test]
        public void APatchIsHiddenOnTheRoadAndDrawnOnTheMap() {
            _zone = new GameObject("Zone_Test");
            ZoneEnvironment env = _zone.AddComponent<ZoneEnvironment>();
            Renderer patch = Entity((WorldEntityType)ProximityScan.NeverRenderedKind);
            Renderer tree = Entity(WorldEntityType.Decoration);
            _camera = new GameObject("Cam").AddComponent<Camera>();

            _zone.AddComponent<WorldEntityVisibility>().Apply(null, 0);
            Assert.IsFalse(patch.enabled, "not in the travel view");
            Assert.IsTrue(tree.enabled);

            env.SetOverheadMapMode(_camera, true, 1000f, 600f, 9);
            Assert.IsTrue(patch.enabled, "drawn on the map");
            Assert.IsFalse(patch.GetComponent<Collider>().enabled, "but still not clickable");

            env.SetOverheadMapMode(_camera, false, 1000f, 600f, 9);
            Assert.IsFalse(patch.enabled, "and gone again when the map closes");
        }

        /// <summary>
        /// The open-edge filler (genmtn) is the map's too: only <c>world_render_frame_full</c> draws
        /// it (WORLDHIT.C:215-224), the frame the overhead map renders through (TASK-993).
        /// </summary>
        [Test]
        public void TheOpenEdgeFillerIsDrawnOnlyOnTheMap() {
            _zone = new GameObject("Zone_Test");
            ZoneEnvironment env = _zone.AddComponent<ZoneEnvironment>();
            var filler = GameObject.CreatePrimitive(PrimitiveType.Quad);
            filler.transform.SetParent(_zone.transform, false);
            Renderer ground = filler.GetComponent<Renderer>();
            _camera = new GameObject("Cam").AddComponent<Camera>();

            WorldEntityVisibility visibility = _zone.AddComponent<WorldEntityVisibility>();
            visibility.Apply(null, 0);
            visibility.AddMapOnly(filler);
            Assert.IsFalse(ground.enabled, "not in the travel view");

            env.SetOverheadMapMode(_camera, true, 1000f, 600f, 9);
            Assert.IsTrue(ground.enabled, "drawn on the map");

            env.SetOverheadMapMode(_camera, false, 1000f, 600f, 9);
            Assert.IsFalse(ground.enabled);
        }

        /// <summary>
        /// Zone 1 around tile (10,12), where dir:1:1 stands: the original recorded exactly these four
        /// open tiles, in this order (g_abOpenZoneTileX/Y read live, TASK-993).
        /// </summary>
        [Test]
        public void TheOpenTilesAroundTheRoadAreTheOnesTheOriginalRecords() {
            var map = new ZoneMap("Z01MAP.DAT");
            foreach ((int x, int y) in new[] { (9, 13), (9, 14), (9, 15), (10, 10), (10, 11), (10, 12),
                         (10, 13), (10, 14), (10, 15), (10, 16), (11, 11), (11, 16), (11, 17) }) {
                map.Tiles[x][y] = true;
            }

            Assert.That(ZoneSceneBuilder.OpenNeighbours(map, 10, 12),
                Is.EqualTo(new[] { (11, 13), (9, 12), (11, 12), (9, 11) }));
        }

        [Test]
        public void ATileWithNoOpenNeighbourRecordsNone() {
            var map = new ZoneMap("Z99MAP.DAT");
            for (var x = 4; x <= 6; x++) for (var y = 4; y <= 6; y++) map.Tiles[x][y] = true;

            Assert.That(ZoneSceneBuilder.OpenNeighbours(map, 5, 5), Is.Empty);
        }
    }
}
