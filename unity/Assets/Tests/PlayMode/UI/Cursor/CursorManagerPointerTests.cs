namespace BakAgain.Tests.PlayMode.UI.Cursor {
    using System.Collections;
    using BakAgain.Graphics;
    using BakAgain.UI;
    using BakAgain.UI.Cursor;
    using BakAgain.UI.InputCore;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    public class CursorManagerPointerTests {
        [Test]
        public void PumpPointer_HidesCursorWhenNoPointerPresent() {
            var go = new UnityEngine.GameObject("cm");
            var cm = go.AddComponent<CursorManager>();
            var el = new VisualElement();
            cm.SetCursorElementForTest(el);                 // minimal test hook exposing _cursorElement
            cm.PumpPointer(new FakePointer { IsPresent = false });
            Assert.AreEqual(DisplayStyle.None, el.resolvedStyle.display);
            cm.PumpPointer(new FakePointer { IsPresent = true, ScreenPosition = new UnityEngine.Vector2(400, 300) });
            Assert.AreEqual(DisplayStyle.Flex, el.resolvedStyle.display);
            UnityEngine.Object.DestroyImmediate(go);
        }

        // The test that proves the cursor's draw origin genuinely comes from the active stage's
        // real resolved geometry rather than the fixed Canonical.Width/Height constant: an
        // unmistakable, non-canonical 800×600 Contain stage must be what StageOrigin() measures.
        [UnityTest]
        public IEnumerator PumpPointer_StageOrigin_ReadsTheActiveStagesRealGeometry_NotHardcodedCanonicalSize() {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var stageHost = new GameObject("StageHost");
            var cursorHost = new GameObject("CursorHost");
            try {
                UIDocument stageDoc = stageHost.AddComponent<UIDocument>();
                stageDoc.panelSettings = settings;
                var frame = new DesignFrame { Width = 800, Height = 600, Fit = LayoutFit.Contain };
                CanonicalStage.GetOrCreate(stageDoc.rootVisualElement, frame);

                // Cursor's own UIDocument shares the same PanelSettings — same runtime panel as the
                // stage host, exactly how the real game overlays background/REQ/cursor documents.
                UIDocument cursorDoc = cursorHost.AddComponent<UIDocument>();
                cursorDoc.panelSettings = settings;
                var cm = cursorHost.AddComponent<CursorManager>();
                var el = new VisualElement();
                cm.SetCursorElementForTest(el); // minimal test hook exposing _cursorElement

                yield return null;
                yield return null;
                yield return null;

                VisualElement panelTree = cursorDoc.rootVisualElement.panel.visualTree;
                VisualElement stage = CanonicalStage.Find(panelTree);
                Assert.IsNotNull(stage, "sanity: the stage should be findable from the shared panel");
                Assert.Greater(stage.worldBound.width, 0f, "sanity: the stage should have resolved a real size");

                cm.WarpTo(Vector2.zero); // pin the arbiter so the draw position is pure StageOrigin
                // Delta defaults to zero on a fresh FakePointer, so OnPointerMoved leaves the warp in place.
                cm.PumpPointer(new FakePointer { IsPresent = true });

                Assert.AreEqual(stage.worldBound.x, el.style.left.value.value, 0.5f);
                Assert.AreEqual(stage.worldBound.y, el.style.top.value.value, 0.5f);

                // And prove it's NOT the old hardcoded-1600x1200 pillarbox formula, which measured
                // the panel's own size against Canonical.Width/Height instead of the stage's real
                // 800x600 box — the whole point of the fix.
                float oldX = (panelTree.layout.width - Canonical.Width) / 2f;
                float oldY = (panelTree.layout.height - Canonical.Height) / 2f;
                bool differs = Mathf.Abs(oldX - el.style.left.value.value) > 1f
                    || Mathf.Abs(oldY - el.style.top.value.value) > 1f;
                Assert.IsTrue(differs,
                    $"expected the fix to diverge from the old hardcoded-Canonical origin; "
                    + $"old=({oldX},{oldY}) actual=({el.style.left.value.value},{el.style.top.value.value})");
            } finally {
                UnityEngine.Object.DestroyImmediate(cursorHost);
                UnityEngine.Object.DestroyImmediate(stageHost);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        // The test that proves FindActiveStage resolves the TOPMOST stage (by UIDocument
        // sortingOrder, which is the panel's real paint order — see CursorManager.FindActiveStage)
        // rather than whichever a depth-first Q() over the shared panel reaches first. A Contain
        // screen stage (tier 0) and a Fill dialog stage (tier 10) coexisting is the shipped
        // topology the moment any dialog opens over any screen — today Q() returns whichever comes
        // first depth-first, which is unrelated to what the user sees on top. Distinctive fits/sizes
        // so neither stage can pass by coincidence.
        [UnityTest]
        public IEnumerator PumpPointer_ResolvesAgainstTheTopmostStage_NotTheFirstOneInTheTree() {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var screenHost = new GameObject("ScreenHost");
            var dialogHost = new GameObject("DialogHost");
            var cursorHost = new GameObject("CursorHost");
            try {
                // screenStage is registered FIRST, so a depth-first Q() over the shared panel tree
                // finds it before dialogStage — that's the bug this test falsifies.
                VisualElement screenStage = MakeStage(screenHost, settings, sortingOrder: 0,
                    new DesignFrame { Width = 800, Height = 600, Fit = LayoutFit.Contain });
                VisualElement dialogStage = MakeStage(dialogHost, settings, sortingOrder: 10,
                    new DesignFrame { Width = 1920, Height = 1080, Fit = LayoutFit.Fill });

                // Cursor's own UIDocument shares the same PanelSettings — same runtime panel as
                // both stage hosts, exactly how the real game overlays background/REQ/dialog/cursor
                // documents.
                UIDocument cursorDoc = cursorHost.AddComponent<UIDocument>();
                cursorDoc.panelSettings = settings;
                var cm = cursorHost.AddComponent<CursorManager>();
                cm.SetCursorElementForTest(new VisualElement()); // minimal test hook

                yield return null;
                yield return null;
                yield return null;

                VisualElement active = cm.ResolveActiveStageForTest();

                Assert.AreSame(dialogStage, active,
                    "The cursor must resolve against the stage the user is actually looking at. With a "
                    + "Fill dialog over a Contain screen these are different coordinate spaces, so "
                    + "picking the wrong one warps the cursor on every Yes/No dialog.");
            } finally {
                UnityEngine.Object.DestroyImmediate(cursorHost);
                UnityEngine.Object.DestroyImmediate(dialogHost);
                UnityEngine.Object.DestroyImmediate(screenHost);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        // Fixture helper: a UIDocument on its own GameObject, sharing settings' runtime panel with
        // whatever else is under test, hosting a CanonicalStage built from a distinctive frame.
        private static VisualElement MakeStage(
            GameObject host, PanelSettings settings, int sortingOrder, DesignFrame frame) {
            UIDocument doc = host.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            doc.sortingOrder = sortingOrder;
            return CanonicalStage.GetOrCreate(doc.rootVisualElement, frame);
        }
    
        /// <summary>
        /// A touchscreen gets no software cursor, however answerable its presses are.
        /// </summary>
        /// <remarks>
        /// The regression this guards is a phantom cursor on a phone: <c>CanPoint</c> is true for
        /// touch, so a reader that switched to it "for consistency" would draw one. The cursor's
        /// question is <c>IsPresent</c> and only that (TASK-67).
        /// </remarks>
        [Test]
        public void PumpPointer_StillHidesTheCursorForATouchscreen() {
            var go = new UnityEngine.GameObject("cm-touch");
            var cm = go.AddComponent<CursorManager>();
            var el = new VisualElement();
            cm.SetCursorElementForTest(el);

            cm.PumpPointer(new FakePointer {
                IsPresent = false, CanPointOverride = true,
                ScreenPosition = new UnityEngine.Vector2(400, 300),
            });

            Assert.AreEqual(DisplayStyle.None, el.resolvedStyle.display);
            UnityEngine.Object.DestroyImmediate(go);
        }
}
}
