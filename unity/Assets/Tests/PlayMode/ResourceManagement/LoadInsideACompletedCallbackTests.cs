namespace BakAgain.Tests.PlayMode.ResourceManagement {
    using System.Collections;
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using UnityEngine.TestTools;

    /// <summary>
    /// A game sprite asked for from inside another load's Completed callback is built, palette and
    /// all. That callback runs inside Addressables' own <c>ResourceManager.Update</c>, so the sprite
    /// converter's palette load runs there too; a <c>WaitForCompletion</c> at that point re-enters
    /// <c>Update</c>, which Addressables 4.x refuses outright ("Reentering the Update method is not
    /// allowed") and 2.x refused in this case already (TASK-883).
    /// </summary>
    public class LoadInsideACompletedCallbackTests {
        [UnityTest]
        public IEnumerator ASpriteLoadedInsideAnotherLoadsCallbackIsBuilt() {
            ShippedGameData.RequireOrIgnore();
            // A new language-pack instance empties BakResourceProvider's caches, so the sprite
            // below is converted now (palettes loaded) rather than handed out from an earlier test.
            ResourceManagementInitializer.InitializeResourceManagement();

            AsyncOperationHandle<Sprite> outer = Addressables.LoadAssetAsync<Sprite>("BICONS1.BMX#2");
            AsyncOperationHandle<Sprite> inner = default;
            bool ran = false;
            // Registered on a finished operation, so Addressables defers it to its next Update.
            outer.Completed += _ => {
                // The host palette (@INVENTOR.PAL) makes the converter load two palettes.
                inner = Addressables.LoadAssetAsync<Sprite>("BICONS1.BMX#3@INVENTOR.PAL");
                ran = true;
            };
            for (int frame = 0; frame < 10 && !(ran && inner.IsDone); frame++) {
                yield return null;
            }

            try {
                Assert.IsTrue(ran, "the Completed callback never ran");
                Assert.AreEqual(AsyncOperationStatus.Succeeded, inner.Status, "the sprite failed to load inside the callback");
                Assert.IsNotNull(inner.Result, "no sprite");
            } finally {
                if (inner.IsValid()) {
                    inner.Release();
                }
                outer.Release();
            }
        }
    }
}
