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
            // The "viewport" is the whole stage rect it's handed — matches the old fixed
            // full-screen-Vector2 stub's behaviour now that ToScreenRect maps through the stage.
            public Rect ToScreenRect(Rect stageScreenRect) => stageScreenRect;
            public Vector2Int RenderTextureSize(Rect stageScreenRect) =>
                new Vector2Int((int)stageScreenRect.width, (int)stageScreenRect.height);
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
