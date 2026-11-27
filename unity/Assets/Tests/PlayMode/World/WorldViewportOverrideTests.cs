namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using BakAgain.World;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using System.Collections;
    using UnityEngine.TestTools;

    /// <summary>
    /// TASK-1083: a mod's START.DAT has to reach the travel viewport (its rect and focal length).
    /// The viewport used to open the game folder's archive itself, past the override layers.
    /// No shipped data needed: the override is the whole resource.
    /// </summary>
    public class WorldViewportOverrideTests {
        private const string StartOverrideJson = "{\"Id\":\"START.DAT\",\"Type\":\"DAT\","
            + "\"CombatCameraHeightAboveGround\":1229,\"CombatCameraHeightUnderground\":960,"
            + "\"CombatCameraPitchAboveGround\":-2519,\"CombatCameraPitchUnderground\":-3593,"
            + "\"CombatGridCellSize\":300,\"ViewportX\":100,\"ViewportY\":50,\"ViewportWidth\":1400,"
            + "\"ViewportHeight\":600,\"ProjectionShift\":9,\"FocalLength\":2000}";

        [UnityTest]
        public IEnumerator AStartDatOverrideReachesTheViewport() =>
            UniTask.ToCoroutine(async () => {
                using (var overrides = new TempOverrideDirectory())
                using (new IsolatedResourceLocators()) {
                    overrides.Write("DAT", "START.json", StartOverrideJson);
                    ResourceManagementInitializer.InitializeResourceManagement();

                    await WorldViewport.PreloadAsync();
                    var viewport = new WorldViewport();

                    Area rect = viewport.CanonicalRect;
                    Assert.AreEqual((100, 50, 1400, 600), (rect.X, rect.Y, rect.Width, rect.Height));
                    Assert.AreEqual(2000, viewport.FocalLength);
                }
            });
    }
}
