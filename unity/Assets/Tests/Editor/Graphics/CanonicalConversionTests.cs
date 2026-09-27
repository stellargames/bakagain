namespace BakAgain.Tests.Editor.Graphics {
    using System.Collections;
    using BakAgain.Graphics;
    using BakAgain.UI;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    public class CanonicalConversionTests {
        [Test]
        public void ScreenToCanonical_NoStage_IsIdentityTopLeftFlip() {
            // No resolved CanonicalStage yet (e.g. the very first frame) — best-effort fallback:
            // just the bottom-left -> top-left Y flip, no stage offset available.
            // Unity screen pos is bottom-left origin. Top-left canonical (0,0) == screen (0,1200).
            Vector2 c = CanonicalConversion.ScreenToCanonical(new Vector2(0, 1200), new Vector2(1600, 1200), null);
            Assert.AreEqual(0f, c.x, 0.01f);
            Assert.AreEqual(0f, c.y, 0.01f);
        }

        [Test]
        public void ScreenToCanonical_NoStage_BottomRightMapsToFlippedBottomRight() {
            Vector2 c = CanonicalConversion.ScreenToCanonical(new Vector2(1600, 0), new Vector2(1600, 1200), null);
            Assert.AreEqual(1600f, c.x, 0.01f);
            Assert.AreEqual(1200f, c.y, 0.01f);
        }

        // The test that proves the numbers genuinely come from the stage's real geometry rather
        // than the fixed Canonical.Width/Height constant: an unmistakable, non-canonical 800×600
        // Contain stage must be the frame the conversion measures against, not 1600x1200.
        [UnityTest]
        public IEnumerator ScreenToCanonical_UsesStagesRealGeometry_NotHardcodedCanonicalConstant() {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var host = new GameObject("CanonicalConversionTestHost");
            try {
                UIDocument document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                VisualElement root = document.rootVisualElement;
                var frame = new DesignFrame { Width = 800, Height = 600, Fit = LayoutFit.Contain };
                VisualElement stage = CanonicalStage.GetOrCreate(root, frame);

                yield return null;
                yield return null;
                yield return null;

                Assert.Greater(stage.worldBound.width, 0f, "sanity: the stage should have resolved a real size");

                Vector2 screenSize = new Vector2(Screen.width, Screen.height);
                var screenPos = new Vector2(123f, 45f); // arbitrary bottom-left-origin physical point

                Vector2 actual = CanonicalConversion.ScreenToCanonical(screenPos, screenSize, stage);

                // Oracle: Unity's own screen->panel conversion (the same idiom ClassicMovementDriver
                // uses), then subtract the stage's real resolved top-left. This is exactly "ask the
                // stage for its actual geometry" rather than re-deriving an assumed transform.
                var topLeftScreenPos = new Vector2(screenPos.x, screenSize.y - screenPos.y);
                Vector2 expectedPanelPos = RuntimePanelUtils.ScreenToPanel(root.panel, topLeftScreenPos);
                Vector2 expected = expectedPanelPos - stage.worldBound.position;

                Assert.AreEqual(expected.x, actual.x, 0.5f);
                Assert.AreEqual(expected.y, actual.y, 0.5f);

                // And prove this is NOT what the old hardcoded-1600x1200 formula would have produced
                // for the same inputs — the whole point of the fix.
                float oldScale = Mathf.Min(screenSize.x / Canonical.Width, screenSize.y / Canonical.Height);
                float oldOffsetX = (screenSize.x - Canonical.Width * oldScale) / 2f;
                float oldOffsetY = (screenSize.y - Canonical.Height * oldScale) / 2f;
                float oldX = (screenPos.x - oldOffsetX) / oldScale;
                float oldY = (screenSize.y - screenPos.y - oldOffsetY) / oldScale;

                bool differs = Mathf.Abs(oldX - actual.x) > 1f || Mathf.Abs(oldY - actual.y) > 1f;
                Assert.IsTrue(differs,
                    $"expected the fix to diverge from the old hardcoded-Canonical formula for a "
                    + $"non-canonical 800x600 stage; old=({oldX},{oldY}) actual=({actual.x},{actual.y})");
            } finally {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
            }
        }
    }
}
