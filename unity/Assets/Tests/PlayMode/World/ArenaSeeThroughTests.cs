namespace BakAgain.Tests.PlayMode.World {
    using System.Collections.Generic;
    using BakAgain.World.Encounters;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>Enhanced combat's see-through (TASK-972): a unit in front of the focused cell fades.</summary>
    public class ArenaSeeThroughTests {
        private readonly List<GameObject> _made = new();
        private readonly EnhancedCombatLayout _layout = new();
        private Camera _cam;

        private sealed class FullViewport : BakAgain.World.IWorldViewport {
            public BakAgain.Graphics.Area CanonicalRect => new BakAgain.Graphics.Area(0, 0, 1600, 1200);
            public float ViewportAspect => 1600f / 1200f;
            public int FocalLength => 2560;
            public Rect ToScreenRect(Rect stageScreenRect) => stageScreenRect;
            public Vector2Int RenderTextureSize(Rect stageScreenRect) =>
                new Vector2Int((int)stageScreenRect.width, (int)stageScreenRect.height);
        }

        [SetUp]
        public void SetUp() {
            var c = new GameObject("cam");
            _made.Add(c);
            _cam = c.AddComponent<Camera>();
            c.transform.position = new Vector3(0, 10, -10);
            c.transform.LookAt(Vector3.zero);
            _cam.aspect = 1f;
        }

        [TearDown]
        public void TearDown() {
            foreach (GameObject g in _made) { Object.DestroyImmediate(g); }
        }

        private ArenaCombatant Unit(Vector3 at, float height) {
            var go = new GameObject("unit");
            _made.Add(go);
            go.transform.position = at;
            var mesh = new Mesh();
            mesh.SetVertices(new[] { new Vector3(-1, 0, 0), new Vector3(-1, height, 0), new Vector3(1, height, 0), new Vector3(1, 0, 0) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            return go.AddComponent<ArenaCombatant>();
        }

        private static Vector3? Centre((int Col, int Row) _) => Vector3.zero;

        // Stand-in grid: one cell per world unit, the focus cell (0, 0) at the origin.
        private static (int Col, int Row)? CellAt(Vector3 p) => (Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));

        [Test]
        public void AUnitInFrontOfTheFocusCellFades_OneBehindDoesNot() {
            ArenaCombatant front = Unit(new Vector3(0, 0, -1), 2f);
            ArenaCombatant behind = Unit(new Vector3(0, 0, 1), 12f);   // covers the point on screen, but farther
            ArenaSeeThrough.Apply(_cam, (0, 0), new[] { front, behind }, _layout, Centre, CellAt);
            Assert.AreEqual(_layout.SeeThroughAlpha, ArenaSeeThrough.AlphaOf(front.GetComponent<Renderer>()), 1e-4f);
            Assert.AreEqual(1f, ArenaSeeThrough.AlphaOf(behind.GetComponent<Renderer>()), 1e-4f);
        }

        [Test]
        public void ANearerUnitThatDoesNotCoverTheFocusPointDoesNotFade() {
            ArenaCombatant aside = Unit(new Vector3(6, 0, -1), 2f);
            ArenaSeeThrough.Apply(_cam, (0, 0), new[] { aside }, _layout, Centre, CellAt);
            Assert.AreEqual(1f, ArenaSeeThrough.AlphaOf(aside.GetComponent<Renderer>()), 1e-4f);
        }

        [Test]
        public void NoFocusNoFade_AndTheFadeIsRestoredWhenTheFocusClears() {
            ArenaCombatant front = Unit(new Vector3(0, 0, -1), 2f);
            ArenaSeeThrough.Apply(_cam, null, new[] { front }, _layout, Centre, CellAt);
            Assert.AreEqual(1f, ArenaSeeThrough.AlphaOf(front.GetComponent<Renderer>()), 1e-4f);
            ArenaSeeThrough.Apply(_cam, (0, 0), new[] { front }, _layout, Centre, CellAt);
            Assert.Less(ArenaSeeThrough.AlphaOf(front.GetComponent<Renderer>()), 1f);
            ArenaSeeThrough.Apply(_cam, null, new[] { front }, _layout, Centre, CellAt);
            Assert.AreEqual(1f, ArenaSeeThrough.AlphaOf(front.GetComponent<Renderer>()), 1e-4f);
        }

        [Test]
        public void ThePointerOnAUnitPicksTheUnit_ThePickBehindAFadedUnitHitsTheCell() {
            // The arena picks the CELL under the pointer (a floor plane, no collider), then asks who
            // stands on it: a faded unit in front cannot swallow the pick behind it, and fading adds no collider.
            ArenaCombatant front = Unit(new Vector3(0, 0, -1), 2f);
            var box = front.gameObject.AddComponent<BoxCollider>();
            ArenaSeeThrough.Apply(_cam, (0, 0), new[] { front }, _layout, Centre, CellAt);
            Assert.IsTrue(box.enabled, "fading leaves the collider alone");
            Vector3? floor = BakAgain.World.WorldPicker.PickGroundPoint(_cam, new FullViewport(),
                new Vector2(_cam.pixelWidth / 2f, _cam.pixelHeight / 2f), new Rect(0, 0, _cam.pixelWidth, _cam.pixelHeight));
            Assert.IsTrue(floor.HasValue);
            Assert.AreEqual(0f, floor.Value.magnitude, 1e-3f, "the pointer through the faded unit lands on the focus cell");
        }

        [Test]
        public void TheDriverFadesWhileTheLatchIsEnhancedAndRestoresWhenItGoesClassic() {
            var root = new GameObject("arena");
            _made.Add(root);
            ArenaCombatant front = Unit(new Vector3(0, 0, -1), 2f);
            front.transform.SetParent(root.transform, true);
            bool on = true;
            root.AddComponent<ArenaSeeThroughDriver>().Bind(() => on, () => (0, 0), () => _cam, Centre, CellAt, _layout);
            root.SendMessage("LateUpdate");
            Assert.Less(ArenaSeeThrough.AlphaOf(front.GetComponent<Renderer>()), 1f);
            on = false;
            root.SendMessage("LateUpdate");
            Assert.AreEqual(1f, ArenaSeeThrough.AlphaOf(front.GetComponent<Renderer>()), 1e-4f);
        }

        [Test]
        public void TheOccupantOfTheFocusCellNeverFades_EvenOffCentreAndNearerTheCamera() {
            // Pivot 0.3 nearer the camera than the cell centre (an idle sway, a slide): still the subject.
            ArenaCombatant onCell = Unit(new Vector3(0, 0, -0.3f), 12f);
            ArenaCombatant front = Unit(new Vector3(0, 0, -1), 2f);
            ArenaSeeThrough.Apply(_cam, (0, 0), new[] { onCell, front }, _layout, Centre, CellAt);
            Assert.AreEqual(1f, ArenaSeeThrough.AlphaOf(onCell.GetComponent<Renderer>()), 1e-4f);
            Assert.AreEqual(_layout.SeeThroughAlpha, ArenaSeeThrough.AlphaOf(front.GetComponent<Renderer>()), 1e-4f);
        }
    }
}
