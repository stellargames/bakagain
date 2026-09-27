namespace BakAgain.Tests.Editor.World {
    using BakAgain.World;
    using BakAgain.World.Rendering;
    using NUnit.Framework;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// Pins ownership and reuse for the per-zone render context. Unity never reclaims a
    /// Material/Mesh/Texture2D without an explicit Destroy, so anything the context creates it must
    /// also destroy — and anything it merely borrows it must NOT.
    /// </summary>
    public class WorldEntityRenderContextLifetimeTests {
        private static WorldEntityRenderContext NewContext() =>
            WorldEntityRenderContext.Create(new Color[256], new ClassicRenderProfile());

        private static Texture2D NewTexture(string name) => new Texture2D(2, 2) { name = name };

        [Test]
        public void SlotMaterial_IsReusedPerTexture() {
            var ctx = NewContext();
            var tex = NewTexture("slot");

            var a = ctx.GetSlotMaterial(tex);
            var b = ctx.GetSlotMaterial(tex);

            // One material per distinct bitmap, not per sub-mesh: a fresh Material per call both
            // leaks and breaks SRP batching.
            Assert.AreSame(a, b);
            ctx.Dispose();
            UnityEngine.Object.Destroy(tex);
        }

        [Test]
        public void SpriteMaterial_IsReusedPerTexture() {
            var ctx = NewContext();
            var tex = NewTexture("sprite");

            var a = ctx.GetSpriteMaterial(tex);
            var b = ctx.GetSpriteMaterial(tex);

            // Sprite materials were previously created per PLACEMENT — every tree in the zone.
            Assert.AreSame(a, b);
            ctx.Dispose();
            UnityEngine.Object.Destroy(tex);
        }

        // The destruction assertions must yield a frame: these run under the PlayMode runner, where
        // Application.isPlaying is true and Object.Destroy is deferred to end of frame. Asserting
        // immediately after Dispose would read the pre-destroy state and pass (or fail) vacuously.

        [UnityTest]
        public IEnumerator Dispose_DestroysMaterialsTheContextCreated() {
            var ctx = NewContext();
            var tex = NewTexture("slot");
            var slotMat = ctx.GetSlotMaterial(tex);
            var spriteMat = ctx.GetSpriteMaterial(tex);

            ctx.Dispose();
            yield return null;

            Assert.IsTrue(slotMat == null, "slot material should be destroyed");
            Assert.IsTrue(spriteMat == null, "sprite material should be destroyed");
            UnityEngine.Object.Destroy(tex);
        }

        [UnityTest]
        public IEnumerator Dispose_DestroysTrackedMeshes() {
            var ctx = NewContext();
            var mesh = new Mesh { name = "tracked" };
            ctx.TrackMesh(mesh);

            ctx.Dispose();
            yield return null;

            Assert.IsTrue(mesh == null, "tracked mesh should be destroyed");
        }

        [UnityTest]
        public IEnumerator Dispose_LeavesBorrowedSlotTexturesAlone() {
            var ctx = NewContext();
            var borrowed = NewTexture("borrowed");
            // SlotTextures hold sprite.texture handed out by the shared IResourceCache — the context
            // borrows them. Destroying one would corrupt the cache for every later consumer.
            ctx.SlotTextures["Z01SLOT3.BMX#8"] = borrowed;

            ctx.Dispose();
            yield return null;

            Assert.IsFalse(borrowed == null, "cache-owned slot texture must not be destroyed");
            UnityEngine.Object.Destroy(borrowed);
        }
    }
}
