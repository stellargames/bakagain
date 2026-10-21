namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using BakAgain.World.Encounters;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Leaving Play destroys the objects the session made, but a static cache lives on into a session
    /// entered without a domain reload (the PlayMode test runner's, or Enter Play Mode options). Each
    /// static that holds one makes it again when it is destroyed (TASK-918; GameFonts in BookFontTests).
    /// </summary>
    public class DestroyedStaticCacheTests {
        [Test]
        public void GlowCloudMaterials_AreMadeAgainOnceDestroyed() {
            Object.DestroyImmediate(GlowCloud.DotMaterial);
            Object.DestroyImmediate(GlowCloud.LineMaterial);

            Assert.IsTrue(GlowCloud.DotMaterial != null, "dot");
            Assert.IsTrue(GlowCloud.LineMaterial != null, "line");
        }

        [Test, RequiresShippedGameData]
        public void DialogPanelTile_IsBakedAgainOnceDestroyed() {
            using (new IsolatedResourceLocators()) {
                ResourceManagementInitializer.InitializeResourceManagement();
                Texture2D tile = DialogPanelBuilder.GetPanelTile();
                Assume.That(tile != null, "OPTIONS0.SCX bakes a tile");
                Object.DestroyImmediate(tile);

                Assert.IsTrue(DialogPanelBuilder.GetPanelTile() != null);
            }
        }
    }
}
