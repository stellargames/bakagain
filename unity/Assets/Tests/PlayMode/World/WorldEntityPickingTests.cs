namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.World;
    using GameData.Resources.Config;
    using GameData.Resources.World;
    using NUnit.Framework;
    using UnityEngine;

    public class WorldEntityPickingTests {
        private static DetectData Detect(WorldEntityType interactable) {
            var d = new DetectData("DETECT.DAT");
            var above = new DetectLocationRanges { Location = "Aboveground" };
            above.DetectRanges[(byte)interactable] = 7000;
            d.Locations.Add(above);
            d.Locations.Add(new DetectLocationRanges { Location = "Underground" });
            return d;
        }

        private static GameObject Meshed(string name) {
            var go = new GameObject(name);
            var mf = go.AddComponent<MeshFilter>();
            var m = new Mesh { vertices = new[] { new Vector3(-0.5f,-0.5f,-0.5f), new Vector3(0.5f,0.5f,0.5f) } };
            m.RecalculateBounds();
            mf.sharedMesh = m;
            go.AddComponent<MeshRenderer>();
            return go;
        }

        [Test]
        public void InteractableType_GetsColliderOnWorldInteractableLayer() {
            var detect = Detect(WorldEntityType.Container);
            var go = Meshed("chest");
            try {
                WorldEntityBuilder.AddPickColliderIfInteractable(go, WorldEntityType.Container, detect, underground: false);
                Assert.IsNotNull(go.GetComponent<BoxCollider>(), "interactable type gets a collider");
                Assert.AreEqual(LayerMask.NameToLayer(WorldInteractionLayers.WorldInteractableLayerName), go.layer);
            } finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void NonInteractableType_GetsNoCollider() {
            var detect = Detect(WorldEntityType.Container);            // only Container interactable
            var go = Meshed("tree");
            try {
                WorldEntityBuilder.AddPickColliderIfInteractable(go, (WorldEntityType)3, detect, underground: false);
                Assert.IsNull(go.GetComponent<BoxCollider>(), "decorative type gets no collider");
            } finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void InteractableEntity_StampsDetectionRangeFromDetectData() {
            var detect = Detect(WorldEntityType.Container);          // Container range 7000 in the helper
            var go = new GameObject("chest");
            try {
                var mf = go.AddComponent<MeshFilter>();
                var m = new Mesh { vertices = new[] { new Vector3(-0.5f,-0.5f,-0.5f), new Vector3(0.5f,0.5f,0.5f) } };
                m.RecalculateBounds(); mf.sharedMesh = m; go.AddComponent<MeshRenderer>();
                var we = go.AddComponent<WorldEntity>();
                we.EntityType = WorldEntityType.Container;
                WorldEntityBuilder.AddPickColliderIfInteractable(go, WorldEntityType.Container, detect, underground: false);
                WorldEntityBuilder.StampDetectionRange(we, WorldEntityType.Container, detect, underground: false);
                Assert.AreEqual(7000f, we.DetectionRange, 0.5f);
            } finally { Object.DestroyImmediate(go); }
        }
    }
}
