using BakAgain.Graphics;
using BakAgain.World;
using GameData.Resources.World;
using NUnit.Framework;
using UnityEngine;

namespace BakAgain.Tests.PlayMode.World {
    public class WorldPickerTests {
        private sealed class FullViewport : IWorldViewport {
            public Area CanonicalRect => new Area(0, 0, 1600, 1200);
            public float ViewportAspect => 1600f / 1200f;
            public int FocalLength => 2560;
            // The "viewport" is the whole stage rect it's handed — matches the old fixed
            // full-screen-Vector2 stub's behaviour now that ToScreenRect maps through the stage.
            public Rect ToScreenRect(Rect stageScreenRect) => stageScreenRect;
            public Vector2Int RenderTextureSize(Rect stageScreenRect) =>
                new Vector2Int((int)stageScreenRect.width, (int)stageScreenRect.height);
        }

        /// <summary>
        /// The touch aids' combat cursor needs a cell's place on SCREEN. The arena camera renders into
        /// a texture shown in the viewport, so Camera.WorldToScreenPoint answers in the texture's
        /// space; found live, 2026-10-01 (a cursor point off the battlefield). The inverse of
        /// PickGroundPoint round-trips.
        /// </summary>
        [Test]
        public void ScreenPointOfGround_IsTheInverseOfTheGroundPick() {
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 10, -10);
            cam.transform.LookAt(Vector3.zero);
            var stage = new Rect(300, 50, 800, 600);   // a viewport that is NOT the whole screen
            try {
                var floor = new Vector3(2.5f, 0f, 1.5f);
                Vector2? screen = WorldPicker.ScreenPointOfGround(cam, new FullViewport(), floor, stage);
                Assert.IsTrue(screen.HasValue);
                Assert.IsTrue(stage.Contains(screen.Value), "inside the viewport's own rect");
                Vector3? back = WorldPicker.PickGroundPoint(cam, new FullViewport(), screen.Value, stage);
                Assert.IsTrue(back.HasValue);
                Assert.Less(Vector3.Distance(floor, back.Value), 0.01f);
            } finally {
                Object.DestroyImmediate(cam.gameObject);
            }
        }

        [Test]
        public void Pick_ReturnsEntityUnderPointer() {
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 0, -10);
            cam.transform.rotation = Quaternion.identity; // looks +Z
            int layer = LayerMask.NameToLayer(WorldInteractionLayers.WorldInteractableLayerName);

            var target = new GameObject("corpse");
            target.layer = layer;
            target.transform.position = Vector3.zero;
            target.AddComponent<BoxCollider>().size = new Vector3(4, 4, 1);
            var we = target.AddComponent<WorldEntity>();
            we.EntityType = WorldEntityType.Corpse;
            Physics.SyncTransforms();

            var stageRect = new Rect(0, 0, 800, 600);
            var pointer = new Vector2(400, 300); // dead center → viewport (0.5,0.5) → ray hits the target

            WorldEntity hit = WorldPicker.Pick(cam, new FullViewport(), pointer, stageRect);

            Assert.IsNotNull(hit);
            Assert.AreEqual(WorldEntityType.Corpse, hit.EntityType);

            Object.DestroyImmediate(target);
            Object.DestroyImmediate(cam.gameObject);
        }

        [Test]
        public void Pick_ReturnsNullOutsideViewport() {
            var cam = new GameObject("cam").AddComponent<Camera>();
            WorldEntity hit = WorldPicker.Pick(cam, new FullViewport(), new Vector2(-5, -5), new Rect(0, 0, 800, 600));
            Assert.IsNull(hit);
            Object.DestroyImmediate(cam.gameObject);
        }
    }
}
