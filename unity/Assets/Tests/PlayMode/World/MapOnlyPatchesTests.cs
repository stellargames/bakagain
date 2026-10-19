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
    }
}
