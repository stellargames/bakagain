using BakAgain.World;
using NUnit.Framework;
using UnityEngine;

namespace BakAgain.Tests.PlayMode.World {
    public class WorldEntityColliderTests {
        [Test]
        public void SpriteEntity_GetsBoxColliderOnInteractableLayer() {
            var go = new GameObject("sprite-entity");
            try {
                // AttachWorldEntity is the choke point; simulate what it does for a sprite.
                WorldEntityBuilder.AddInteractionCollider(go, Vector3.zero, new Vector3(1f, 2f, 0.1f));

                Assert.IsNotNull(go.GetComponent<BoxCollider>(), "sprite entity must have a BoxCollider");
                Assert.AreEqual(LayerMask.NameToLayer(WorldInteractionLayers.WorldInteractableLayerName),
                    go.layer, "must sit on the WorldInteractable layer");
            } finally {
                Object.DestroyImmediate(go);
            }
        }
    }
}
