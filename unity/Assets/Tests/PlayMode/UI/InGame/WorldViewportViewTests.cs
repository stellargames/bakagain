namespace BakAgain.Tests.PlayMode.UI.InGame {
    using BakAgain.Graphics;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using BakAgain.UI.InGame;
    using BakAgain.World;
    using GameData.Resources.Layout;
    using Microsoft.Extensions.Logging.Abstractions;
    using NUnit.Framework;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// Regression for the "full-screen world behind the in-game menu screens" bug: the world camera
    /// is confined to the travel viewport only by rendering into a RenderTexture that the travel
    /// screen's <see cref="WorldViewportView"/> hosts. When an Opaque screen (the in-game menu's
    /// Preferences / Restore / Save / Contents) hides the travel screen, that view is disposed — and
    /// a still-enabled world camera without a RenderTexture reverts to drawing full-screen straight
    /// to the display, bleeding the 3D world around those screens. The fix: the view enables the
    /// camera on Attach and disables it on Dispose, so the world renders only while the travel
    /// viewport is displaying it.
    /// </summary>
    public class WorldViewportViewTests {
        private sealed class StubViewport : IWorldViewport {
            public Area CanonicalRect => new Area(65, 66, 1470, 606);
            public float ViewportAspect => 1470f / 606f;
            public int FocalLength => 2560;
            public Rect ToScreenRect(Rect stageScreenRect) => stageScreenRect;
            // The stage rect the view last mapped through: Tick sizes the RT from it.
            public Rect LastStageScreenRect { get; private set; }
            public Vector2Int RenderTextureSize(Rect stageScreenRect) {
                LastStageScreenRect = stageScreenRect;
                return new Vector2Int(256, 128);
            }
        }

        private static VisualElement PanelWithHost() {
            var root = new VisualElement();
            root.Add(new VisualElement { name = "hotspot_192" });
            return root;
        }

        private static WorldViewportView NewView() =>
            new WorldViewportView(new StubViewport(), NullLogger.Instance);

        [Test]
        public void Attach_EnablesCameraAndRoutesItIntoTheViewportRenderTexture() {
            var camGo = new GameObject("TestWorldCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            try {
                WorldViewportView view = NewView();
                view.SetWorldCamera(cam);
                view.Attach(PanelWithHost());

                Assert.IsTrue(cam.enabled, "camera should render while the travel viewport displays it");
                Assert.IsNotNull(cam.targetTexture, "camera should render into the viewport RenderTexture, not the screen");

                view.Dispose();
            } finally {
                Object.DestroyImmediate(camGo);
            }
        }

        [Test]
        public void Dispose_DisablesCameraSoItDoesNotRenderFullScreenBehindMenus() {
            var camGo = new GameObject("TestWorldCamera");
            var cam = camGo.AddComponent<Camera>();
            try {
                WorldViewportView view = NewView();
                view.SetWorldCamera(cam);
                view.Attach(PanelWithHost());
                Assert.IsTrue(cam.enabled);

                view.Dispose();

                Assert.IsFalse(cam.enabled,
                    "camera must stop rendering when the travel viewport is gone, else the world bleeds full-screen behind Opaque menu screens");
                Assert.IsNull(cam.targetTexture, "the RenderTexture must be released on teardown");
            } finally {
                Object.DestroyImmediate(camGo);
            }
        }

        /// <summary>
        /// THE FENCE for the second half of the fallback finding: the stage must be RE-resolved, not
        /// captured once.
        ///
        /// <para><c>Attach</c> used to do <c>_stage = CanonicalStage.Find(panelRoot)</c> and never
        /// look again. Which component creates the stage is deliberately unordered (see
        /// <c>CanonicalStage</c>'s "Order-independence" section), so an <c>Attach</c> that ran first
        /// captured null — and this view lives for the whole travel session, so the world viewport
        /// would then be mapped through <c>ScreenRect</c>'s fallback box <i>permanently</i>, with a
        /// single logged warning to show for it. A transient one-frame approximation and a
        /// session-long wrong answer are indistinguishable in the log.</para>
        ///
        /// <para><b>The fixture is 1280x1024 for a reason: it makes all THREE possible answers
        /// different numbers</b>, so no two of them can cancel out (an earlier version at 1920x1080
        /// could not tell "resolved the Fill stage" from "raw-window fallback" — they are the same
        /// rect, and a paired mutant proved nothing).
        /// <list type="bullet">
        /// <item>resolved the real Contain stage: (-43, 0, 1366, 1024) — match-height scaling gives
        /// a full-height stage overflowing a narrower-than-4:3 window;</item>
        /// <item>the current Contain fallback box: (0, 32, 1280, 960) — uniform min() scale 0.8;</item>
        /// <item>the old raw-window fallback: (0, 0, 1280, 1024).</item>
        /// </list>
        /// The expected numbers are the ones
        /// <c>CanonicalStageTests.ScreenRect_ContainStage_AtNonSixteenByNineResolution_MatchesHandDerivedPixels</c>
        /// derives by hand and measures live for the same window.</para>
        /// </summary>
        [UnityTest]
        public IEnumerator AStageThatAppearsAfterAttach_IsPickedUp_NotPinnedToTheFallbackBox() {
            GameViewResolutionScope gameView =
                GameViewResolutionScope.Force(1280, 1024);
            yield return null;

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
            settings.referenceResolution = new Vector2Int(Canonical.Width, Canonical.Height);
            var host = new GameObject("WorldViewportViewLateStageHost");
            var viewport = new StubViewport();
            WorldViewportView view = null;
            try {
                UIDocument document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                VisualElement root = document.rootVisualElement;
                root.Add(new VisualElement { name = "hotspot_192" });

                // Attach BEFORE any stage exists — the ordering that used to pin the fallback.
                view = new WorldViewportView(viewport, NullLogger.Instance);
                view.Attach(root);

                // The stage arrives afterwards, as it does when the REQ loader wins the race.
                CanonicalStage.GetOrCreate(root,
                    new DesignFrame { Width = Canonical.Width, Height = Canonical.Height, Fit = LayoutFit.Contain });
                yield return null;
                yield return null;

                // Tick maps through the stage rect the view resolves NOW. See the three candidate
                // answers in the doc above.
                view.Tick();
                Rect resolved = viewport.LastStageScreenRect;
                Assert.AreEqual(-43f, resolved.x, 1f,
                    "0 here means the view never re-resolved the stage and is still on a fallback box "
                    + "(the Contain fallback is (0, 32, 1280, 960); the old raw-window one (0, 0, 1280, 1024))");
                Assert.AreEqual(0f, resolved.y, 1f);
                Assert.AreEqual(1366f, resolved.width, 1f,
                    "1280 here means the view is still mapping through a fallback, not the real stage");
                Assert.AreEqual(1024f, resolved.height, 1f);
            } finally {
                view?.Dispose();
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
                gameView.Restore();
            }
            yield return null;
        }

        /// <summary>
        /// TASK-65: <c>Attach</c>'s parameter changed meaning from the stage element to the panel
        /// root during phase 5, without changing its type — so a caller holding the stage still
        /// compiles. This pins that the wrong-but-plausible argument produces the RIGHT geometry
        /// rather than a silent, session-long fallback box. Same 1280x1024 fixture (and the same
        /// three mutually-distinguishable answers) as
        /// <see cref="AStageThatAppearsAfterAttach_IsPickedUp_NotPinnedToTheFallbackBox"/>: the real
        /// stage is (-43, 0, 1366, 1024), the Contain fallback (0, 32, 1280, 960).
        /// </summary>
        [UnityTest]
        public IEnumerator AViewHandedTheStageInsteadOfThePanelRoot_StillMapsThroughTheRealStage() {
            GameViewResolutionScope gameView =
                GameViewResolutionScope.Force(1280, 1024);
            yield return null;

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
            settings.referenceResolution = new Vector2Int(Canonical.Width, Canonical.Height);
            var host = new GameObject("WorldViewportViewStageArgHost");
            var viewport = new StubViewport();
            WorldViewportView view = null;
            try {
                UIDocument document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                VisualElement root = document.rootVisualElement;
                VisualElement stage = CanonicalStage.GetOrCreate(root,
                    new DesignFrame { Width = Canonical.Width, Height = Canonical.Height, Fit = LayoutFit.Contain });
                // hotspot_192 is a REQ widget, so in production it is a child of the stage — which
                // is why handing the stage in place of the panel root finds the host either way.
                stage.Add(new VisualElement { name = "hotspot_192" });

                view = new WorldViewportView(viewport, NullLogger.Instance);
                view.Attach(stage);   // the stage, where the panel root is expected
                yield return null;
                yield return null;

                view.Tick();
                Rect resolved = viewport.LastStageScreenRect;
                Assert.AreEqual(-43f, resolved.x, 1f,
                    "0 here means the view fell back to the canonical Contain box instead of resolving "
                    + "the stage it was handed");
                Assert.AreEqual(0f, resolved.y, 1f);
                Assert.AreEqual(1366f, resolved.width, 1f);
                Assert.AreEqual(1024f, resolved.height, 1f);
            } finally {
                view?.Dispose();
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
                gameView.Restore();
            }
            yield return null;
        }

        [Test]
        public void Reattach_ReEnablesCameraWhenTheTravelViewportReturns() {
            var camGo = new GameObject("TestWorldCamera");
            var cam = camGo.AddComponent<Camera>();
            try {
                WorldViewportView opened = NewView();
                opened.SetWorldCamera(cam);
                opened.Attach(PanelWithHost());
                opened.Dispose();                 // menu opened → travel hidden → camera off
                Assert.IsFalse(cam.enabled);

                WorldViewportView reopened = NewView();
                reopened.SetWorldCamera(cam);
                reopened.Attach(PanelWithHost());  // menu closed → travel shown again
                Assert.IsTrue(cam.enabled, "camera should render again once the travel viewport returns");
                Assert.IsNotNull(cam.targetTexture);

                reopened.Dispose();
            } finally {
                Object.DestroyImmediate(camGo);
            }
        }
    }
}
