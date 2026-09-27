namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.World;
    using GameData.Resources.Data;
    using NUnit.Framework;
    using UnityEngine;

    public class WorldEntityInteractionTests {
        [Test]
        public void WorldEntity_HoldsBehaviorAndProfile() {
            var go = new GameObject("e");
            try {
                var e = go.AddComponent<WorldEntity>();
                e.Behavior = "container";
                e.Interaction = new InteractionProfile { OpensLoot = true };
                Assert.AreEqual("container", e.Behavior);
                Assert.IsTrue(e.Interaction.OpensLoot);
            } finally {
                Object.DestroyImmediate(go);
            }
        }
    }
}
