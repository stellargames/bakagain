namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using BakAgain.Graphics;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    public class ContentsChapterOverlayTests {
        private static Sprite MakeSprite() =>
            Sprite.Create(new Texture2D(2, 2), new Rect(0, 0, 2, 2), Vector2.zero);

        // The test that proves the CONT2 reveal covers the *stage's* real size rather than the
        // fixed Canonical.Width/Height constant: an unmistakable, non-canonical 800×600 Contain
        // stage's reveal image must resolve to 800x600 (minus the locked-region crop), not
        // 1600x1200.
        [UnityTest]
        public IEnumerator RevealCont2_CoversTheStagesRealSize_NotHardcodedCanonicalSize() {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var stageHost = new GameObject("StageHost");
            // Created inactive so the ContentsChapterOverlay/UserInterfaceLoader RequireComponent
            // pair never runs Awake/OnEnable (which would try to load a real REQ via Addressables) —
            // this test drives AddCont2OverlayAsync directly via the RevealCont2ForTest test seam.
            var overlayHost = new GameObject("OverlayHost");
            overlayHost.SetActive(false);
            try {
                UIDocument stageDoc = stageHost.AddComponent<UIDocument>();
                stageDoc.panelSettings = settings;
                var frame = new DesignFrame { Width = 800, Height = 600, Fit = LayoutFit.Contain };
                VisualElement stage = CanonicalStage.GetOrCreate(stageDoc.rootVisualElement, frame);

                var overlay = overlayHost.AddComponent<ContentsChapterOverlay>();
                var resources = new FakeResourceProviderService();
                resources.Register("CONT2.SCX", MakeSprite());

                const float lockedTop = 200f; // arbitrary row boundary within the 600-tall stage
                yield return overlay.RevealCont2ForTest(stage, resources, lockedTop).ToCoroutine();

                yield return null;
                yield return null;

                Assert.Greater(stage.resolvedStyle.width, 0f, "sanity: stage should have resolved a real size");
                Assert.AreEqual(800f, stage.resolvedStyle.width, 0.5f, "sanity: stage is the 800-wide frame we built");
                Assert.AreEqual(600f, stage.resolvedStyle.height, 0.5f, "sanity: stage is the 600-tall frame we built");

                VisualElement clip = stage.Q("cont2_reveal");
                Assert.IsNotNull(clip, "the CONT2 reveal container should have been added to the stage");
                Assert.AreEqual(1, clip.childCount);
                VisualElement img = clip[0];

                // The image must cover the stage's actual full size...
                Assert.AreEqual(stage.resolvedStyle.width, img.resolvedStyle.width, 0.5f,
                    "reveal image width must match the stage's real width, not a hardcoded constant");
                Assert.AreEqual(stage.resolvedStyle.height, img.resolvedStyle.height, 0.5f,
                    "reveal image height must match the stage's real height, not a hardcoded constant");

                // ...which for this 800x600 stage is unmistakably NOT 1600x1200 — proving the old
                // Canonical.Width/Height-sized version would have failed this test.
                Assert.AreNotEqual(Canonical.Width, img.resolvedStyle.width);
                Assert.AreNotEqual(Canonical.Height, img.resolvedStyle.height);
            } finally {
                Object.DestroyImmediate(overlayHost);
                Object.DestroyImmediate(stageHost);
                Object.DestroyImmediate(settings);
            }
        }
    }
}
