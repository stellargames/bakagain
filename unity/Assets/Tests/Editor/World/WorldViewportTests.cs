namespace BakAgain.Tests.Editor.World {
    using BakAgain.Graphics;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using BakAgain.World;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    [RequiresShippedGameData]
    public class WorldViewportTests {
        private const float Tolerance = 0.001f;

        [Test]
        public void CanonicalRect_IsReVerifiedViewportScaledToCanonical() {
            // RE-verified 2026-05-31: setupRenderView (0x23d18) + REQ_MAIN.DAT ClickArea
            // (ActionId 192) give VGA (13, 11, 294, 101); canonical = ×5 x/width, ×6 y/height.
            Area rect = new WorldViewport().CanonicalRect;
            Assert.AreEqual(65, rect.X);      // 13 × 5
            Assert.AreEqual(66, rect.Y);      // 11 × 6
            Assert.AreEqual(1470, rect.Width);  // 294 × 5
            Assert.AreEqual(606, rect.Height);  // 101 × 6
        }

        [Test]
        public void ViewportAspect_IsTrueDisplayedAspect() {
            // 1470/606 ≈ 2.426 — square-pixel canonical space already carries the 6:5
            // VGA pixel aspect, unlike the old non-square 294/101 ≈ 2.911.
            Assert.AreEqual(1470f / 606f, new WorldViewport().ViewportAspect, Tolerance);
        }

        // --- ToScreenRect now takes the stage's already-resolved screen rect instead of
        // re-deriving Contain from a window size. The tests below feed it the exact stage rect
        // CanonicalStage would have produced for the old scenarios, so the numbers pin the same
        // geometry as before the refactor (see the faithfulness test for the explicit proof).
        //
        // "The same geometry as before" holds AT AND ABOVE 4:3, which is every aspect the two
        // faithfulness tests below cover and every aspect anyone plays at. It does NOT hold below
        // 4:3, and that divergence is deliberate — see
        // ToScreenRect_ContainStageBelowFourThree_TracksTheStageWithNoInventedLetterbox for the one
        // case where the deleted min() math and the stage-rect mapping genuinely disagree.

        [Test]
        public void ToScreenRect_AtCanonicalSize_MatchesRectWithTopLeftFlippedToBottomLeft() {
            // A 1600×1200 window: the Contain stage exactly fills it (no letterbox), so the
            // stage rect equals the window. Screen rect equals the canonical rect with the Y axis
            // flipped: canonical top y=66, height=606 → bottom-left y = 1200 - (66 + 606) = 528.
            Rect r = new WorldViewport().ToScreenRect(new Rect(0f, 0f, 1600f, 1200f));
            Assert.AreEqual(65f, r.x, Tolerance);
            Assert.AreEqual(528f, r.y, Tolerance);
            Assert.AreEqual(1470f, r.width, Tolerance);
            Assert.AreEqual(606f, r.height, Tolerance);
        }

        [Test]
        public void ToScreenRect_ScalesLinearlyWithWindow() {
            // 3200×2400 is exactly 4:3 (same as the canonical frame), so the Contain stage again
            // fills the whole window with no letterbox — every dimension doubles (linear stretch).
            Rect r = new WorldViewport().ToScreenRect(new Rect(0f, 0f, 3200f, 2400f));
            Assert.AreEqual(130f, r.x, Tolerance);
            Assert.AreEqual(1056f, r.y, Tolerance); // 2400 - (66 + 606) * 2
            Assert.AreEqual(2940f, r.width, Tolerance);
            Assert.AreEqual(1212f, r.height, Tolerance);
        }

        [Test]
        public void ToScreenRect_PillarboxesOnWiderThanFourThree() {
            // 3200×1800 (16:9) window: CanonicalStage's Contain box is the largest centered 4:3
            // area — uniform scale = min(3200/1600, 1800/1200) = 1.5, stage 2400×1800 centered
            // → x offset (3200-2400)/2 = 400, no y offset. The viewport maps proportionally into
            // that stage rect, not the raw window.
            Rect r = new WorldViewport().ToScreenRect(new Rect(400f, 0f, 2400f, 1800f));
            Assert.AreEqual(400f + 65f * 1.5f, r.x, Tolerance);
            Assert.AreEqual(1800f - (66f + 606f) * 1.5f, r.y, Tolerance);
            Assert.AreEqual(1470f * 1.5f, r.width, Tolerance);
            Assert.AreEqual(606f * 1.5f, r.height, Tolerance);
        }

        [Test]
        public void RenderTextureSize_RoundsAndClampsToAtLeastOne() {
            Vector2Int size = new WorldViewport().RenderTextureSize(new Rect(0f, 0f, 1600f, 1200f));
            Assert.AreEqual(1470, size.x);
            Assert.AreEqual(606, size.y);

            // Degenerate stage rect must not produce a zero-sized texture.
            Vector2Int tiny = new WorldViewport().RenderTextureSize(new Rect(0f, 0f, 1f, 1f));
            Assert.GreaterOrEqual(tiny.x, 1);
            Assert.GreaterOrEqual(tiny.y, 1);
        }

        // --- Phase 5 task 3: map through the stage instead of re-deriving Contain ------------
        //
        // Faithfulness gate: under Contain, ToScreenRect(stageScreenRect) must reproduce exactly
        // what the OLD Vector2-screenSize implementation returned — proving the stage-rect mapping
        // is a genuine no-op for the shipped (Contain, 1600×1200) case. The expected numbers below
        // were captured by running the pre-refactor WorldViewport.ToScreenRect(new Vector2(1920,
        // 1080)) directly (via the live Editor, not hand-derived from the new formula) and are
        // pinned here as literals: Rect(298.5, 475.2, 1323, 545.4).
        //
        // The stage rect a real CanonicalStage produces for a Contain (1600×1200) frame in a
        // 1920×1080 window is (240, 0, 1440, 1080): uniform scale = min(1920/1600, 1080/1200) =
        // 0.9, stage 1440×1080 centered → x offset (1920-1440)/2 = 240, no y offset (height is the
        // binding dimension). Verified independently against a live UIDocument using this
        // project's own PanelSettings (ScaleWithScreenSize, match-height): worldBound for a
        // 1600×1200 stage at a 1920×1080 window reports logical (266.67, 0, 1600, 1200), which
        // scales by Screen.height/panelLogicalHeight = 1080/1200 = 0.9 to physical (240, 0, 1440,
        // 1080) — matching this figure exactly.
        [Test]
        public void ToScreenRect_ContainStage_MatchesPreRefactorFormula() {
            var containStageRect = new Rect(240f, 0f, 1440f, 1080f);
            Rect r = new WorldViewport().ToScreenRect(containStageRect);
            Assert.AreEqual(298.5f, r.x, Tolerance);
            Assert.AreEqual(475.2f, r.y, Tolerance);
            Assert.AreEqual(1323f, r.width, Tolerance);
            Assert.AreEqual(545.4f, r.height, Tolerance);
        }

        // Reflow: under Fill the stage spans the whole window, so sx (1920/1600=1.2) and sy
        // (1080/1200=0.9) are no longer equal — the viewport visibly widens relative to Contain
        // (same stage/window size) instead of staying pillarboxed. This is the behaviour the
        // whole task exists to enable.
        [Test]
        public void ToScreenRect_FillStage_ReflowsIndependentlyInXAndY() {
            var fillStageRect = new Rect(0f, 0f, 1920f, 1080f);
            Rect r = new WorldViewport().ToScreenRect(fillStageRect);
            Assert.AreEqual(78f, r.x, Tolerance);
            Assert.AreEqual(475.2f, r.y, Tolerance);
            Assert.AreEqual(1764f, r.width, Tolerance);
            Assert.AreEqual(545.4f, r.height, Tolerance);
        }

        /// <summary>
        /// The one place phase 5's "Contain is unchanged" claim needs a caveat, pinned so it can
        /// never drift back silently.
        ///
        /// <para><b>Below 4:3 the old and new formulas genuinely disagree, and the new one is
        /// right.</b> The deleted math was <c>min(w/1600, h/1200)</c> plus a centring offset on BOTH
        /// axes — a letterbox as well as a pillarbox. <c>CanonicalStage</c> never letterboxes: the
        /// shared <c>PanelSettings</c> uses match-HEIGHT scaling, so a Contain stage's logical height
        /// is always exactly 1200 and it spans the window vertically at every aspect, overflowing
        /// horizontally when the window is narrower than 4:3 (CanonicalStage.cs:26-31). The old
        /// min() therefore hit-tested against a vertical letterbox that was never drawn.</para>
        ///
        /// <para>The stage rect fed in is the same 1280x1024 case
        /// <c>CanonicalStageTests.ScreenRect_ContainStage_AtNonSixteenByNineResolution_MatchesHandDerivedPixels</c>
        /// measures against a live UIDocument, taken here in its exact (un-snapped) form so this
        /// stays pure arithmetic: scale = 1024/1200, the 1600x1200 logical stage overflows the
        /// 1500-logical-px-wide panel by 50 logical px on each side. That test reports the same box
        /// as (-43, 0, 1366, 1024) after UI Toolkit's device-pixel snapping.</para>
        ///
        /// <para>The old formula would have returned left 52, bottom 454.4, width 1176, height
        /// 484.8 here — a ~39 px horizontal and ~32 px vertical error against where the viewport is
        /// actually drawn. Nothing visible moves; this is a latent hit-test bug being fixed.</para>
        /// </summary>
        [Test]
        public void ToScreenRect_ContainStageBelowFourThree_TracksTheStageWithNoInventedLetterbox() {
            const float scale = 1024f / 1200f;            // match-height: Screen.height / 1200
            var containStageRect = new Rect(-50f * scale, 0f, 1600f * scale, 1024f);

            Rect r = new WorldViewport().ToScreenRect(containStageRect);

            Assert.AreEqual(12.8f, r.x, Tolerance, "left: 52 here means the old min() letterbox is back");
            Assert.AreEqual(450.56f, r.y, Tolerance, "bottom: 454.4 here means the old min() letterbox is back");
            Assert.AreEqual(1254.4f, r.width, Tolerance, "width: 1176 here means the old min() letterbox is back");
            Assert.AreEqual(517.12f, r.height, Tolerance, "height: 484.8 here means the old min() letterbox is back");
        }

        /// <summary>
        /// The same sub-4:3 divergence, asserted END TO END through a real
        /// <see cref="CanonicalStage"/> at a real 1280x1024 window — and this is the version the OLD
        /// implementation actually fails.
        ///
        /// <para>The pure-math test above cannot be falsified by "put the min() math back", because
        /// the stage rect it is handed is 4:3-shaped by construction, which makes
        /// <c>min(w/1600, h/1200)</c> and the per-axis scales the same number. The whole difference
        /// between the two implementations is <b>what they measure against</b>: the old one took the
        /// WINDOW (1280x1024, a 5:4 box), the new one takes the STAGE (1365x1024, still 4:3 because
        /// match-height keeps it so). Only a test that starts from the window can tell them
        /// apart.</para>
        ///
        /// <para>Old, from the window: scale = min(1280/1600, 1024/1200) = 0.8, offsetY =
        /// (1024 - 960)/2 = 32 → left 52, bottom 454.4, width 1176, height 484.8. New, from the
        /// stage: 12.8 / 450.56 / 1254.4 / 517.12. The tolerance is 1 px because the live stage rect
        /// carries UI Toolkit's device-pixel snapping, which the pure-math test above deliberately
        /// does not.</para>
        /// </summary>
        [UnityTest]
        public IEnumerator ToScreenRect_ThroughARealStageBelowFourThree_DiffersFromTheOldWindowMath() {
            GameViewResolutionScope gameView =
                GameViewResolutionScope.Force(1280, 1024);
            yield return null;

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
            settings.referenceResolution = new Vector2Int(Canonical.Width, Canonical.Height);
            var host = new GameObject("WorldViewportSubFourThreeHost");
            try {
                UIDocument document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                VisualElement stage = CanonicalStage.GetOrCreate(document.rootVisualElement,
                    new DesignFrame { Width = Canonical.Width, Height = Canonical.Height, Fit = LayoutFit.Contain });
                yield return null;
                yield return null;

                Assert.AreEqual(1280, Screen.width, "sanity: forced sub-4:3 resolution should be active");
                Assert.AreEqual(1024, Screen.height, "sanity: forced sub-4:3 resolution should be active");

                Rect stageRect = CanonicalStage.ScreenRect(stage, out bool isFallback);
                Assert.IsFalse(isFallback, "the stage must be resolved, else this measures the fallback instead");

                Rect r = new WorldViewport().ToScreenRect(stageRect);

                Assert.AreEqual(12.8f, r.x, 1f, "52 here is the old window-based min() letterbox");
                Assert.AreEqual(450.56f, r.y, 1f, "454.4 here is the old window-based min() letterbox");
                Assert.AreEqual(1254.4f, r.width, 1f, "1176 here is the old window-based min() letterbox");
                Assert.AreEqual(517.12f, r.height, 1f, "484.8 here is the old window-based min() letterbox");
            } finally {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
                gameView.Restore();
            }
            yield return null;
        }

        // Picking: a pointer at the exact centre of the Fill-mapped viewport must normalise to
        // (0.5, 0.5) in viewport space — the same (pointer - r.min) / r.size WorldPicker.Pick uses
        // to build its camera ray, exercised here against the real Fill-reflowed rect (not a stub
        // that hands back a fixed full-screen box, which would make this pass regardless of the
        // mapping).
        [Test]
        public void ToScreenRect_FillStage_PointerAtViewportCentreNormalisesToHalf() {
            var fillStageRect = new Rect(0f, 0f, 1920f, 1080f);
            Rect r = new WorldViewport().ToScreenRect(fillStageRect);
            Vector2 pointerAtCentre = r.center;
            Vector2 normalised = (pointerAtCentre - r.min) / r.size;
            Assert.AreEqual(0.5f, normalised.x, Tolerance);
            Assert.AreEqual(0.5f, normalised.y, Tolerance);
        }
    }
}
