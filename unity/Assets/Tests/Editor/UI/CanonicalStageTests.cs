namespace BakAgain.Tests.Editor.UI {
    using System.Collections;
    using System.Text.RegularExpressions;
    using BakAgain.Graphics;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    public class CanonicalStageTests {
        [Test]
        public void Contain_GivesAFixedBoxOfTheFramesDimensions_FlexCentered() {
            var root = new VisualElement();
            var frame = new DesignFrame { Width = 1600, Height = 1200, Fit = LayoutFit.Contain };

            VisualElement stage = CanonicalStage.GetOrCreate(root, frame);

            Assert.AreEqual(LengthUnit.Pixel, stage.style.width.value.unit);
            Assert.AreEqual(1600f, stage.style.width.value.value);
            Assert.AreEqual(LengthUnit.Pixel, stage.style.height.value.unit);
            Assert.AreEqual(1200f, stage.style.height.value.value);
            // Flex-centered within the document root.
            Assert.AreEqual(Align.Center, root.style.alignItems.value);
            Assert.AreEqual(Justify.Center, root.style.justifyContent.value);
        }

        [Test]
        public void Fill_GivesAStageSpanningItsParent_NotAFixedPixelSize() {
            var root = new VisualElement();
            var frame = new DesignFrame { Width = 1600, Height = 1200, Fit = LayoutFit.Fill };

            VisualElement stage = CanonicalStage.GetOrCreate(root, frame);

            Assert.AreEqual(LengthUnit.Percent, stage.style.width.value.unit);
            Assert.AreEqual(100f, stage.style.width.value.value);
            Assert.AreEqual(LengthUnit.Percent, stage.style.height.value.unit);
            Assert.AreEqual(100f, stage.style.height.value.value);
        }

        // The test that proves the numbers genuinely come from data rather than a constant:
        // an unmistakable, non-canonical 800×600 frame must produce an 800×600 stage.
        [Test]
        public void ArbitraryFrame_800x600_ProducesAn800x600Stage() {
            var root = new VisualElement();
            var frame = new DesignFrame { Width = 800, Height = 600, Fit = LayoutFit.Contain };

            VisualElement stage = CanonicalStage.GetOrCreate(root, frame);

            Assert.AreEqual(800f, stage.style.width.value.value);
            Assert.AreEqual(600f, stage.style.height.value.value);
        }

        // Confirms a real panel (not just a bare VisualElement's requested style) actually
        // resolves a Fill stage to the panel's own size, post-layout. Mirrors the
        // CreditsViewLayoutTests pattern: a runtime PanelSettings + UIDocument, then a couple of
        // yielded frames so Yoga runs before reading .layout (the resolved rect).
        [UnityTest]
        public IEnumerator Fill_ReallySpansTheAttachedPanel_NotJustStyleIntent() {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var host = new GameObject("CanonicalStageFillTestHost");
            try {
                UIDocument document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                VisualElement root = document.rootVisualElement;

                // Deliberately NOT the panel's own size — proves the stage is measured, not
                // asserted against its own requested Width/Height.
                var frame = new DesignFrame { Width = 800, Height = 600, Fit = LayoutFit.Fill };
                VisualElement stage = CanonicalStage.GetOrCreate(root, frame);

                yield return null;
                yield return null;

                Debug.Log(
                    $"[CanonicalStageTests] Fill stage resolved to {stage.layout.width}x{stage.layout.height} "
                    + $"against panel root {root.layout.width}x{root.layout.height}.");

                Assert.Greater(root.layout.width, 0f, "sanity: the attached panel should have a real resolved width");
                Assert.Greater(root.layout.height, 0f, "sanity: the attached panel should have a real resolved height");
                Assert.AreEqual(root.layout.width, stage.layout.width, 0.5f,
                    "a Fill stage must resolve to the panel's actual width, not the frame's 800");
                Assert.AreEqual(root.layout.height, stage.layout.height, 0.5f,
                    "a Fill stage must resolve to the panel's actual height, not the frame's 600");
            } finally {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void BothDimensionsZero_FallsBackToCanonicalDimensions_AndWarns() {
            var root = new VisualElement();
            var frame = new DesignFrame(); // default-constructed: Width = Height = 0

            LogAssert.Expect(LogType.Warning, new Regex("DesignFrame.*non-positive", RegexOptions.IgnoreCase));
            VisualElement stage = CanonicalStage.GetOrCreate(root, frame);

            Assert.AreEqual((float)Canonical.Width, stage.style.width.value.value);
            Assert.AreEqual((float)Canonical.Height, stage.style.height.value.value);
        }

        [Test]
        public void ZeroWidthOnly_FallsBackToCanonicalDimensions_AndWarns() {
            var root = new VisualElement();
            var frame = new DesignFrame { Width = 0, Height = 600, Fit = LayoutFit.Contain };

            LogAssert.Expect(LogType.Warning, new Regex("DesignFrame.*non-positive", RegexOptions.IgnoreCase));
            VisualElement stage = CanonicalStage.GetOrCreate(root, frame);

            Assert.AreEqual((float)Canonical.Width, stage.style.width.value.value);
            Assert.AreEqual((float)Canonical.Height, stage.style.height.value.value);
        }

        [Test]
        public void ZeroHeightOnly_FallsBackToCanonicalDimensions_AndWarns() {
            var root = new VisualElement();
            var frame = new DesignFrame { Width = 800, Height = 0, Fit = LayoutFit.Contain };

            LogAssert.Expect(LogType.Warning, new Regex("DesignFrame.*non-positive", RegexOptions.IgnoreCase));
            VisualElement stage = CanonicalStage.GetOrCreate(root, frame);

            Assert.AreEqual((float)Canonical.Width, stage.style.width.value.value);
            Assert.AreEqual((float)Canonical.Height, stage.style.height.value.value);
        }

        [Test]
        public void NegativeDimension_FallsBackToCanonicalDimensions_AndWarns() {
            var root = new VisualElement();
            var frame = new DesignFrame { Width = -100, Height = 600, Fit = LayoutFit.Contain };

            LogAssert.Expect(LogType.Warning, new Regex("DesignFrame.*non-positive", RegexOptions.IgnoreCase));
            VisualElement stage = CanonicalStage.GetOrCreate(root, frame);

            Assert.AreEqual((float)Canonical.Width, stage.style.width.value.value);
            Assert.AreEqual((float)Canonical.Height, stage.style.height.value.value);
        }

        // FINDING 2 regression: the non-positive-dimension guard is meaningless under Fill (the
        // dimensions are never consumed — the stage always spans its parent), so it must not fire.
        [Test]
        public void FillFrame_WithZeroDimensions_SpansAndDoesNotWarn() {
            var root = new VisualElement();
            var frame = new DesignFrame { Width = 0, Height = 0, Fit = LayoutFit.Fill };

            VisualElement stage = CanonicalStage.GetOrCreate(root, frame);

            Assert.AreEqual(LengthUnit.Percent, stage.style.width.value.unit);
            Assert.AreEqual(100f, stage.style.width.value.value);
            Assert.AreEqual(LengthUnit.Percent, stage.style.height.value.unit);
            Assert.AreEqual(100f, stage.style.height.value.value);
            LogAssert.NoUnexpectedReceived(); // no "falling back" warning — Fill never needed the dimensions
        }

        // FINDING 1 regression: BackgroundImageLoader (no resource, passes null -> the canonical
        // fallback) and UserInterfaceLoader (the real REQ frame) are siblings on every REQ screen
        // with no guaranteed completion order. Before the fix, GetOrCreate returned the cached
        // stage on any call after the first WITHOUT applying the caller's frame, so whichever
        // loader happened to finish first silently decided the stage forever. This is the case
        // that would have caught it: fallback creates the stage, then a real frame arrives and
        // must upgrade it — real beats fallback regardless of order.
        [Test]
        public void GetOrCreate_RealFrameAfterFallback_UpgradesTheStage() {
            var root = new VisualElement();
            CanonicalStage.GetOrCreate(root, null); // fallback first: 1600x1200/Contain

            var real = new DesignFrame { Width = 800, Height = 600, Fit = LayoutFit.Contain };
            VisualElement stage = CanonicalStage.GetOrCreate(root, real);

            Assert.AreEqual(800f, stage.style.width.value.value,
                "a real frame arriving after the fallback must upgrade the stage, not be ignored");
            Assert.AreEqual(600f, stage.style.height.value.value);
        }

        [Test]
        public void GetOrCreate_FallbackAfterRealFrame_KeepsTheRealFrame() {
            var root = new VisualElement();
            var real = new DesignFrame { Width = 800, Height = 600, Fit = LayoutFit.Contain };
            CanonicalStage.GetOrCreate(root, real); // real first

            VisualElement stage = CanonicalStage.GetOrCreate(root, null); // fallback arrives second

            Assert.AreEqual(800f, stage.style.width.value.value,
                "a later fallback call must never overwrite an already-real stage");
            Assert.AreEqual(600f, stage.style.height.value.value);
            LogAssert.NoUnexpectedReceived(); // ignoring a fallback is not a conflict — no warning
        }

        [Test]
        public void GetOrCreate_ConflictingRealFrames_KeepsTheFirstOneAndWarns() {
            var root = new VisualElement();
            var first = new DesignFrame { Width = 800, Height = 600, Fit = LayoutFit.Contain };
            CanonicalStage.GetOrCreate(root, first);

            var second = new DesignFrame { Width = 640, Height = 480, Fit = LayoutFit.Contain };
            LogAssert.Expect(LogType.Warning, new Regex("conflicting.*DesignFrame", RegexOptions.IgnoreCase));
            VisualElement stage = CanonicalStage.GetOrCreate(root, second);

            Assert.AreEqual(800f, stage.style.width.value.value,
                "two different real frames for one stage is a genuine data conflict — keep the first, don't silently switch");
            Assert.AreEqual(600f, stage.style.height.value.value);
        }

        // FINDING 1 regression: CanonicalStage.ScreenRect's real (non-fallback) worldBound ->
        // physical-pixel conversion had ZERO coverage — every call site (WorldViewportView,
        // WorldInteractionController) and every existing test that reaches this method
        // (WorldInteractionControllerTests, WorldPickerTests) passes `stage: null`, which only
        // ever exercises the fallback branch below. This is the first test to invoke ScreenRect
        // with a real, laid-out UIDocument panel.
        //
        // Deliberately a NON-16:9 window (1280x1024, not the previously-verified 1920x1080):
        // a formula that mixed up which axis drives the scale, or that dropped the scale
        // recovery altogether, can still land on plausible-looking numbers near a 16:9-ish
        // aspect close to the panel's own; a second, differently-shaped aspect is what forces a
        // wrong formula to visibly diverge. (See the fix report for why the "swap to a
        // width-based ratio" mutant specifically does NOT diverge here — it's a mathematical
        // identity of ScaleWithScreenSize's uniform scale factor, not a gap in this test; the
        // mutant actually used to falsify this test drops the scale conversion entirely.)
        //
        // Expected numbers, derived by hand from the panel scaler's definition (ScaleWithScreenSize,
        // MatchWidthOrHeight, match=1 == pure "match height" — the project's shared PanelSettings,
        // see the class doc on CanonicalStage) and cross-checked live against a running Editor panel:
        //   scale = Screen.height / referenceResolution.y = 1024 / 1200 = 0.85333...
        //   panel logical width  = Screen.width  / scale = 1280 / 0.85333... = 1500
        //   panel logical height = Screen.height / scale = 1200            (exact, by construction)
        // The Contain stage is a fixed 1600x1200 logical box, flex-centered in the narrower
        // 1500x1200 logical panel, so it overflows evenly on both sides:
        //   logical x = (1500 - 1600) / 2 = -50, width = 1600, y = 0, height = 1200
        // UI Toolkit then snaps that fixed-pixel box to the device-pixel grid (scaledPixelsPerPoint
        // reads exactly the same 0.85333... here), landing on (-50.390625, 0, 1600.78125, 1200) —
        // the nearest whole-*physical*-pixel box once scaled back, i.e. (-43, 0, 1366, 1200)
        // physical. Physical pixels (× scale, then Y-flip to Unity's bottom-left-origin
        // Screen-rect convention):
        //   x = -43, width = 1366
        //   height = 1200 * scale = 1024  (== Screen.height: overflowing only in x, the stage
        //                                   still exactly spans the window vertically)
        //   y = Screen.height - (0 + 1200) * scale = 1024 - 1024 = 0
        [UnityTest]
        public IEnumerator ScreenRect_ContainStage_AtNonSixteenByNineResolution_MatchesHandDerivedPixels() {
            GameViewResolutionScope gameView =
                GameViewResolutionScope.Force(1280, 1024);
            yield return null;

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
            settings.referenceResolution = new Vector2Int(1600, 1200);
            var host = new GameObject("CanonicalStageScreenRectTestHost");
            try {
                UIDocument document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                VisualElement root = document.rootVisualElement;

                var frame = new DesignFrame { Width = 1600, Height = 1200, Fit = LayoutFit.Contain };
                VisualElement stage = CanonicalStage.GetOrCreate(root, frame);

                yield return null;
                yield return null;

                // Sanity: prove the forced non-16:9 window is actually in effect before trusting
                // the hand-derived numbers below.
                Assert.AreEqual(1280, Screen.width, "sanity: forced non-16:9 resolution should be active");
                Assert.AreEqual(1024, Screen.height, "sanity: forced non-16:9 resolution should be active");

                Rect r = CanonicalStage.ScreenRect(stage, out bool isFallback);

                Assert.IsFalse(isFallback,
                    "a real, attached, laid-out stage must take the conversion branch, not the fallback");
                Assert.AreEqual(-43f, r.x, 0.5f);
                Assert.AreEqual(0f, r.y, 0.5f);
                Assert.AreEqual(1366f, r.width, 0.5f);
                Assert.AreEqual(1024f, r.height, 0.5f);
            } finally {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
                gameView.Restore();
            }
            yield return null;
        }

        /// <summary>
        /// THE FENCE for the pre-layout / missing-stage fallback: it must degrade to CONTAIN, not
        /// to Fill.
        ///
        /// <para>The guard used to return the raw window <c>(0, 0, Screen.width, Screen.height)</c>.
        /// <c>WorldViewport.ToScreenRect</c> maps the viewport proportionally into whatever box it
        /// is handed, so a full-window box IS Fill geometry — on a game whose every shipped resource
        /// is Contain. It is not a hypothetical window either: <c>WorldViewportView.Attach</c>
        /// allocates its RenderTexture synchronously, before the stage's first layout pass, so this
        /// branch runs on a normal show (the live phase-5 run logged exactly that warning once per
        /// session). At 1920x1080 it put the world pick rect at (78, 475.2, 1764, 545.4) instead of
        /// (298.5, 475.2, 1323, 545.4), and sized the RenderTexture 1764x545 instead of 1323x545.</para>
        ///
        /// <para>1920x1080 is forced rather than read, because the assertion has to be against
        /// literals: computing the expected box from <c>Screen</c> with the same min() the
        /// implementation uses would restate the code and pass either way.</para>
        /// </summary>
        [UnityTest]
        public IEnumerator ScreenRect_WithNoResolvedStage_FallsBackToTheContainBox_NotTheWholeWindow() {
            GameViewResolutionScope gameView =
                GameViewResolutionScope.Force(1920, 1080);
            yield return null;

            try {
                Assert.AreEqual(1920, Screen.width, "sanity: forced resolution should be active");
                Assert.AreEqual(1080, Screen.height, "sanity: forced resolution should be active");

                // Null stage: exactly what a consumer sees before the stage exists or before its
                // first layout pass (worldBound reads 0/NaN, same branch).
                Rect r = CanonicalStage.ScreenRect(null, out bool isFallback);

                Assert.IsTrue(isFallback, "a null stage must report the fallback so the caller can log it");
                // min(1920/1600, 1080/1200) = 0.9 → 1440x1080, centred: x = (1920-1440)/2 = 240, y = 0.
                Assert.AreEqual(240f, r.x, 0.001f,
                    "0 here means the fallback is the raw window, i.e. Fill geometry on a Contain game");
                Assert.AreEqual(0f, r.y, 0.001f);
                Assert.AreEqual(1440f, r.width, 0.001f,
                    "1920 here means the fallback is the raw window, i.e. Fill geometry on a Contain game");
                Assert.AreEqual(1080f, r.height, 0.001f);
            } finally {
                gameView.Restore();
            }
            yield return null;
        }

        // --- Which element a caller may hand the stage resolver (TASK-65) ------------------
        //
        // The concern: `WorldInteractionController`/`WorldViewportView` take "the panel root" as a
        // bare VisualElement, so a caller holding the STAGE passes it happily — same type, no
        // compile error, and the failure would be invisible (picking and RenderTexture sizing keep
        // working, just against the wrong box). There are three ways to get it wrong and they do
        // NOT behave alike; these tests pin which is which, so the distinction survives a refactor.

        /// <summary>
        /// Handing in the stage itself is harmless, because UQuery matches the element it is called
        /// on and not only its descendants — measured against the live Editor (6000.4.6f1), not
        /// assumed. Worth a test precisely because nothing at the call site says so: if a future
        /// UQuery change (or a hand-rolled replacement for <c>Q</c>) made the lookup
        /// descendants-only, every consumer handed a stage would silently fall back to the
        /// canonical Contain box for its whole session, which is exactly the class of failure a
        /// green build and a green suite cannot otherwise see.
        /// </summary>
        [Test]
        public void Find_HandedTheStageItself_ResolvesToThatStage() {
            var root = new VisualElement();
            VisualElement stage = CanonicalStage.GetOrCreate(
                root, new DesignFrame { Width = 800, Height = 600, Fit = LayoutFit.Contain });

            Assert.AreSame(stage, CanonicalStage.Find(stage),
                "a consumer handed the stage where the document root was expected must still resolve "
                + "the stage — otherwise it maps through the fallback box permanently");
            Assert.AreSame(stage, CanonicalStage.FindOrCached(null, stage),
                "FindOrCached is the memoised form of the same rule and must agree with it");
            LogAssert.NoUnexpectedReceived(); // it resolves correctly, so there is nothing to report
        }

        /// <summary>
        /// Handing in an element that lives INSIDE the stage (<c>hotspot_192</c>, the world viewport
        /// element, a REQ widget) is the one genuinely broken case: the stage is an ancestor, so a
        /// downward query cannot see it. Resolve it by climbing instead of returning null — the
        /// caller then gets correct geometry rather than a session-long fallback — and report the
        /// call site, since the code is still wrong and nothing else would ever say so.
        /// </summary>
        [Test]
        public void Find_HandedAnElementInsideTheStage_ResolvesTheEnclosingStage_AndReportsIt() {
            var root = new VisualElement();
            VisualElement stage = CanonicalStage.GetOrCreate(
                root, new DesignFrame { Width = 800, Height = 600, Fit = LayoutFit.Contain });
            var inner = new VisualElement { name = "hotspot_192" };
            stage.Add(inner);

            LogAssert.Expect(LogType.Error, new Regex("inside the stage", RegexOptions.IgnoreCase));

            Assert.AreSame(stage, CanonicalStage.Find(inner),
                "an element inside the stage must resolve the stage it is inside, not null");
            // Reported ONCE per stage, not once per call: these resolvers run per frame
            // (WorldViewportView.Tick), and an unbounded error log is its own failure mode on this
            // project's Editor. A second Expect above would be needed if this logged again.
            Assert.AreSame(stage, CanonicalStage.Find(inner));
        }

        /// <summary>
        /// The same misuse against <see cref="CanonicalStage.GetOrCreate"/> is worse than a wrong
        /// lookup: creating would nest a SECOND stage inside the first, so its children would be
        /// laid out through the frame transform twice. Reuse the enclosing stage instead.
        /// </summary>
        [Test]
        public void GetOrCreate_HandedAnElementInsideTheStage_ReusesIt_InsteadOfNestingASecondStage() {
            var root = new VisualElement();
            var frame = new DesignFrame { Width = 800, Height = 600, Fit = LayoutFit.Contain };
            VisualElement stage = CanonicalStage.GetOrCreate(root, frame);
            var inner = new VisualElement { name = "hotspot_192" };
            stage.Add(inner);

            LogAssert.Expect(LogType.Error, new Regex("inside the stage", RegexOptions.IgnoreCase));

            VisualElement resolved = CanonicalStage.GetOrCreate(inner, frame);

            Assert.AreSame(stage, resolved, "must return the stage it is already inside");
            Assert.AreEqual(0, inner.childCount, "a second stage must not be nested inside the first");
        }

        /// <summary>
        /// The mirror mix-up, on the measuring side: <see cref="CanonicalStage.ScreenRect"/> handed
        /// the document root would measure the whole panel — Fill geometry on a Contain game, the
        /// very degradation the fallback box exists to avoid — and hand back a plausible rect while
        /// doing it. So it resolves the stage from what it is given instead of measuring it.
        /// 1280x1024 again, where the three candidate answers are all different numbers: the real
        /// Contain stage is (-43, 0, 1366, 1024), the panel/window is (0, 0, 1280, 1024).
        /// </summary>
        [UnityTest]
        public IEnumerator ScreenRect_HandedTheDocumentRoot_MeasuresTheStage_NotTheWholePanel() {
            GameViewResolutionScope gameView =
                GameViewResolutionScope.Force(1280, 1024);
            yield return null;

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
            settings.referenceResolution = new Vector2Int(1600, 1200);
            var host = new GameObject("CanonicalStageScreenRectArgTestHost");
            try {
                UIDocument document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                VisualElement root = document.rootVisualElement;
                CanonicalStage.GetOrCreate(root,
                    new DesignFrame { Width = 1600, Height = 1200, Fit = LayoutFit.Contain });
                yield return null;
                yield return null;

                Rect r = CanonicalStage.ScreenRect(root, out bool isFallback);

                Assert.IsFalse(isFallback, "the stage exists and is laid out — this is not a fallback case");
                Assert.AreEqual(-43f, r.x, 0.5f,
                    "0 here means the panel itself was measured, i.e. Fill geometry on a Contain game");
                Assert.AreEqual(0f, r.y, 0.5f);
                Assert.AreEqual(1366f, r.width, 0.5f,
                    "1280 here means the panel itself was measured, not the stage");
                Assert.AreEqual(1024f, r.height, 0.5f);
            } finally {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
                gameView.Restore();
            }
            yield return null;
        }

        [Test]
        public void GetOrCreate_EqualFrame_IsANoOpAndDoesNotWarn() {
            var root = new VisualElement();
            CanonicalStage.GetOrCreate(root, new DesignFrame { Width = 800, Height = 600, Fit = LayoutFit.Contain });

            // A different DesignFrame instance, same values — must not be treated as a conflict.
            VisualElement stage = CanonicalStage.GetOrCreate(
                root, new DesignFrame { Width = 800, Height = 600, Fit = LayoutFit.Contain });

            Assert.AreEqual(800f, stage.style.width.value.value);
            Assert.AreEqual(600f, stage.style.height.value.value);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
