using System.Collections;
using BakAgain.Graphics;
using BakAgain.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BakAgain.Tests.PlayMode.World {
    /// <summary>
    /// A billboard's pick collider and its damage-float origin follow the picture the sprite shader
    /// draws (SpritePass.hlsl: the quad's x along the WORLD camera's flattened right, y up), from
    /// every heading (TASK-945).
    /// </summary>
    public class BillboardSpriteTests {
        private sealed class FullViewport : IWorldViewport {
            public Area CanonicalRect => new Area(0, 0, 1600, 1200);
            public float ViewportAspect => 4f / 3f;
            public int FocalLength => 2560;
            public Rect ToScreenRect(Rect stageScreenRect) => stageScreenRect;
            public Vector2Int RenderTextureSize(Rect stageScreenRect) =>
                new Vector2Int((int)stageScreenRect.width, (int)stageScreenRect.height);
        }

        // World coordinates are thousands of units from the origin (zone 1 starts near (6700, 10600)),
        // which is where the idle UI camera's Camera.main sat the old facing.
        private static readonly Vector3 Pivot = new Vector3(6700f, 0f, 10600f);
        private static readonly Vector3 Scale = new Vector3(2f, 3f, 2f);

        private GameObject _sprite;
        private Camera _camera;
        private Camera _previousViewer;

        [SetUp]
        public void SetUp() {
            _previousViewer = BillboardSprite.Viewer;
            _camera = new GameObject("WorldCamera (test)").AddComponent<Camera>();
            _camera.fieldOfView = 40f;
            BillboardSprite.Viewer = _camera;

            // An anchor well left of centre (u = 0.2), like a sprite hung from its hotspot.
            var mesh = new Mesh {
                vertices = new[] {
                    new Vector3(-0.2f, 0f, 0f), new Vector3(0.8f, 0f, 0f),
                    new Vector3(0.8f, 1f, 0f), new Vector3(-0.2f, 1f, 0f),
                },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
            };
            BakAgain.World.Converters.TblSpriteConverter.CoverTurns(mesh);
            _sprite = new GameObject("sprite");
            _sprite.transform.position = Pivot;
            _sprite.transform.localScale = Scale;
            _sprite.AddComponent<MeshFilter>().sharedMesh = mesh;
            _sprite.AddComponent<MeshRenderer>();
            _sprite.AddComponent<BillboardSprite>();
            Bounds quad = GeometryUtility.CalculateBounds(mesh.vertices, Matrix4x4.identity);
            WorldEntityBuilder.AddInteractionCollider(_sprite, quad.center, quad.size);
            _sprite.AddComponent<WorldEntity>();
        }

        [TearDown]
        public void TearDown() {
            BillboardSprite.Viewer = _previousViewer;
            Object.DestroyImmediate(_sprite);
            Object.DestroyImmediate(_camera.gameObject);
        }

        /// <summary>Puts the world camera 8 units back from the sprite, eye height 2.3, looking along
        /// <paramref name="heading"/> degrees clockwise from +Z.</summary>
        private void View(float heading) {
            Vector3 f = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            _camera.transform.position = Pivot - f * 8f + Vector3.up * 2.3f;
            _camera.transform.rotation = Quaternion.LookRotation(Pivot + Vector3.up * 1.5f - _camera.transform.position);
        }

        /// <summary>Where the shader draws quad point (x, y): pivot + flat camera right * x + up * y.</summary>
        private Vector3 Drawn(float x, float y) {
            Vector3 right = _camera.transform.right;
            right.y = 0f;
            right.Normalize();
            return Pivot + right * (x * Scale.x) + Vector3.up * (y * Scale.y);
        }

        [UnityTest]
        public IEnumerator Pick_HitsTheDrawnPicture_FromEveryHeading() {
            var stage = new Rect(0, 0, 800, 600);
            var misses = new System.Collections.Generic.List<string>();
            for (int h = 0; h < 360; h += 45) {
                View(h);
                yield return null;   // a frame for the billboard to turn
                Physics.SyncTransforms();
                foreach (float x in new[] { -0.15f, 0.3f, 0.75f }) {
                    foreach (float y in new[] { 0.1f, 0.5f, 0.9f }) {
                        Vector3 vp = _camera.WorldToViewportPoint(Drawn(x, y));
                        var pointer = new Vector2(vp.x * stage.width, vp.y * stage.height);
                        if (WorldPicker.Pick(_camera, new FullViewport(), pointer, stage) == null) {
                            misses.Add($"{h} deg ({x},{y})");
                        }
                    }
                }
            }
            CollectionAssert.IsEmpty(misses, "clicks on the drawn sprite that pick nothing");
        }

        [UnityTest]
        public IEnumerator Pick_MissesBesideTheDrawnPicture() {
            // Control: the collider is the picture, not a column round the pivot.
            var stage = new Rect(0, 0, 800, 600);
            View(90f);
            yield return null;
            Physics.SyncTransforms();
            foreach (float x in new[] { -0.45f, 1.05f }) {
                Vector3 vp = _camera.WorldToViewportPoint(Drawn(x, 0.5f));
                var pointer = new Vector2(vp.x * stage.width, vp.y * stage.height);
                Assert.IsNull(WorldPicker.Pick(_camera, new FullViewport(), pointer, stage), $"x = {x}");
            }
        }

        [UnityTest]
        public IEnumerator DrawnTopCentre_IsAboveTheMiddleOfThePicture() {
            // CACTOR.C combat_actor_draw_float_damage: x = billboard left + width / 2, y = its top - 5.
            foreach (float h in new[] { 0f, 135f, 270f }) {
                View(h);
                yield return null;
                Vector3 got = BillboardSprite.DrawnTopCentre(_sprite.transform);
                Assert.Less(Vector3.Distance(Drawn(0.3f, 1f), got), 0.01f, $"heading {h}: {got}");
            }
        }

        [Test]
        public void DrawnTopCentre_IsRightOnTheSpriteItWasJustBuiltFor() {
            // The arena floats a number on the redraw that built the sprite, before any LateUpdate.
            View(200f);
            var fresh = Object.Instantiate(_sprite);
            try {
                Assert.Less(Vector3.Distance(Drawn(0.3f, 1f), BillboardSprite.DrawnTopCentre(fresh.transform)), 0.01f);
            } finally {
                Object.DestroyImmediate(fresh);
            }
        }
    }
}
