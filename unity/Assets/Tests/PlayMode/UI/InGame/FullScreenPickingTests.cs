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
    /// Enhanced full-screen (spec 2026-10-08 §2): the world is DRAWN in one rect and PICKED through
    /// another unless both come from the same <see cref="FullScreenViewport"/>. Narrow on purpose —
    /// no PlayMode fixture boots the travel screen with a world — so it builds the two halves the
    /// travel screen wires together (a <see cref="WorldViewportView"/> on a real panel and
    /// <see cref="WorldPicker"/>) around one adapter, and checks that a ground point drawn at a pixel
    /// is the ground point picked at that pixel, faithful and full-window, and after a rehost.
    /// </summary>
    public class FullScreenPickingTests {
        [UnityTest]
        public IEnumerator AGroundPointRoundTripsThroughTheRectTheWorldIsDrawnIn([Values(false, true)] bool fullScreen) {
            GameViewResolutionScope gameView = GameViewResolutionScope.Force(1920, 1080);
            yield return null;

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
            settings.referenceResolution = new Vector2Int(Canonical.Width, Canonical.Height);
            var host = new GameObject("FullScreenPickingHost");
            var camGo = new GameObject("FullScreenPickingCamera");
            WorldViewportView view = null;
            try {
                Camera cam = camGo.AddComponent<Camera>();
                cam.transform.position = new Vector3(0f, 10f, -10f);
                cam.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
                cam.fieldOfView = 40f;

                UIDocument document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                VisualElement root = document.rootVisualElement;
                VisualElement stage = CanonicalStage.GetOrCreate(root,
                    new DesignFrame { Width = Canonical.Width, Height = Canonical.Height, Fit = LayoutFit.Contain });
                var inner = new WorldViewport();
                Area c = inner.CanonicalRect;
                stage.Add(new VisualElement {
                    name = "hotspot_192",
                    style = { position = Position.Absolute, left = c.X, top = c.Y, width = c.Width, height = c.Height },
                });

                bool active = fullScreen;
                var fs = new FullScreenViewport(inner, () => active, () => new Vector2(Screen.width, Screen.height));
                view = new WorldViewportView(fs, NullLogger.Instance);
                view.SetWorldCamera(cam);
                view.Attach(root);
                yield return null;
                yield return null;
                view.Tick();   // the stage has laid out: size the texture from it
                yield return null;
                AssertDrawnRectIsPickedRect(view, fs, cam, stage, root, active);

                // The other host, as a fight starting or the option changing would flip it.
                active = !active;
                view.Rehost();
                yield return null;
                yield return null;
                view.Tick();
                yield return null;
                AssertDrawnRectIsPickedRect(view, fs, cam, stage, root, active);
            } finally {
                view?.Dispose();
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
                gameView.Restore();
            }
            yield return null;
        }

        /// <summary>
        /// Full-window, a click anywhere no REQ widget covers must reach the world through the one
        /// existing path — the ClickArea 192 element, whose loader handlers dispatch the click,
        /// right-click, long-press and hover — while the widgets still win where they are.
        /// <see cref="IPanel.Pick"/> is what the event system targets a pointer event with.
        /// </summary>
        [UnityTest]
        public IEnumerator AClickOutsideTheOldViewportReachesTheWorldOnlyWhenFullScreen() {
            GameViewResolutionScope gameView = GameViewResolutionScope.Force(1920, 1080);
            yield return null;

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
            settings.referenceResolution = new Vector2Int(Canonical.Width, Canonical.Height);
            var host = new GameObject("FullScreenClickHost");
            var camGo = new GameObject("FullScreenClickCamera");
            WorldViewportView view = null;
            try {
                Camera cam = camGo.AddComponent<Camera>();
                cam.transform.position = new Vector3(0f, 10f, -10f);
                cam.transform.rotation = Quaternion.Euler(30f, 0f, 0f);

                UIDocument document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                VisualElement root = document.rootVisualElement;
                VisualElement stage = CanonicalStage.GetOrCreate(root,
                    new DesignFrame { Width = Canonical.Width, Height = Canonical.Height, Fit = LayoutFit.Contain });
                var inner = new WorldViewport();
                Area c = inner.CanonicalRect;
                // REQ_MAIN order: the buttons come BEFORE ClickArea 192, so a stretched 192 left in
                // place would cover them.
                var button = new VisualElement {
                    name = "imagebutton_50",
                    style = { position = Position.Absolute, left = 1000, top = 990, width = 170, height = 174 },
                };
                stage.Add(button);
                var area = new VisualElement {
                    name = "hotspot_192",
                    style = { position = Position.Absolute, left = c.X, top = c.Y, width = c.Width, height = c.Height },
                };
                stage.Add(area);

                bool active = true;
                var fs = new FullScreenViewport(inner, () => active, () => new Vector2(Screen.width, Screen.height));
                view = new WorldViewportView(fs, NullLogger.Instance);
                view.SetWorldCamera(cam);
                view.Attach(root);
                yield return null;
                view.Tick();
                yield return null;
                yield return null;

                // A side-bar point (left of the 4:3 stage) and one below the old frame, mid-stage.
                var sideBar = new Vector2(60f, Screen.height * 0.5f);
                var belowFrame = new Vector2(Screen.width * 0.5f, Screen.height * 0.3f);
                Rect stageRect = CanonicalStage.ScreenRect(stage, out _);
                Assert.IsFalse(inner.ToScreenRect(stageRect).Contains(sideBar), "outside the old viewport");
                Assert.IsFalse(inner.ToScreenRect(stageRect).Contains(belowFrame), "outside the old viewport");
                foreach (Vector2 p in new[] { sideBar, belowFrame }) {
                    Assert.AreSame(area, PickAt(root, p), $"the world's click area takes {p}");
                    Assert.IsTrue(WorldPicker.PickGroundPoint(cam, fs, p + new Vector2(0f, -100f), stageRect).HasValue
                        || WorldPicker.PickGroundPoint(cam, fs, p, stageRect).HasValue,
                        $"and the world pick maps it ({p})");
                }
                Vector2 buttonCentre = ScreenPointOf(root, button);
                Assert.AreSame(button, PickAt(root, buttonCentre), "a REQ widget still wins over the world");

                // Back to the frame: the click area is the REQ's rect again, in its REQ place.
                active = false;
                view.Rehost();
                yield return null;
                yield return null;
                Assert.AreNotSame(area, PickAt(root, sideBar), "faithful: the side bar is not the world");
                Assert.AreEqual(c.X, area.resolvedStyle.left, 1f);   // layout snaps to device pixels
                Assert.AreEqual(c.Width, area.resolvedStyle.width, 1f);
                Assert.AreEqual(1, stage.IndexOf(area), "restored to its REQ sibling index");
            } finally {
                view?.Dispose();
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
                gameView.Restore();
            }
            yield return null;
        }

        /// <summary>
        /// A fight can hang its painted backdrop (or a floating number) off the world element in
        /// the same frame it starts, before Update rehosts the world into the frame. The rehost
        /// must carry those children over, or the backdrop leaves the tree for good.
        /// </summary>
        [Test]
        public void ARehostCarriesTheWorldElementsChildrenAcross() {
            var camGo = new GameObject("FullScreenRehostCamera");
            WorldViewportView view = null;
            try {
                var root = new VisualElement();
                root.Add(new VisualElement { name = "hotspot_192" });
                bool active = true;
                var fs = new FullScreenViewport(new WorldViewport(), () => active, () => new Vector2(640, 360));
                view = new WorldViewportView(fs, NullLogger.Instance);
                view.SetWorldCamera(camGo.AddComponent<Camera>());
                view.Attach(root);
                VisualElement before = view.Element;
                var backdrop = new VisualElement { name = "CombatBackdrop" };
                before.Add(backdrop);

                active = false;   // the fight has started
                view.Rehost();

                Assert.AreNotSame(before, view.Element, "the world moved host");
                Assert.AreSame(view.Element, backdrop.parent, "the backdrop moved with it");
                Assert.AreEqual("hotspot_192", view.Element.parent.name);
            } finally {
                view?.Dispose();
                Object.DestroyImmediate(camGo);
            }
        }

        /// <summary>
        /// A view disposed while full-screen (the travel screen hidden under a menu) must give the
        /// frame back: the next view starts knowing nothing of the hidden tint.
        /// </summary>
        [Test]
        public void DisposingAFullScreenViewShowsTheFrameAgain() {
            var camGo = new GameObject("FullScreenFrameCamera");
            try {
                var root = new VisualElement();
                VisualElement stage = CanonicalStage.GetOrCreate(root,
                    new DesignFrame { Width = Canonical.Width, Height = Canonical.Height, Fit = LayoutFit.Contain });
                stage.Add(new VisualElement { name = "hotspot_192" });
                var fs = new FullScreenViewport(new WorldViewport(), () => true, () => new Vector2(640, 360));
                var view = new WorldViewportView(fs, NullLogger.Instance);
                view.SetWorldCamera(camGo.AddComponent<Camera>());
                view.Attach(root);
                Assert.AreEqual(Color.clear, stage.style.unityBackgroundImageTintColor.value, "hidden while full-screen");

                view.Dispose();

                Assert.AreEqual(StyleKeyword.Null, stage.style.unityBackgroundImageTintColor.keyword,
                    "the frame's tint is the stylesheet's again");
            } finally {
                Object.DestroyImmediate(camGo);
            }
        }

        private static VisualElement PickAt(VisualElement root, Vector2 screen) =>
            root.panel.Pick(RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y)));

        private static Vector2 ScreenPointOf(VisualElement root, VisualElement e) {
            float scale = Screen.height / root.panel.visualTree.layout.height;
            Vector2 c = e.worldBound.center * scale;
            return new Vector2(c.x, Screen.height - c.y);
        }

        private static void AssertDrawnRectIsPickedRect(WorldViewportView view, FullScreenViewport fs,
            Camera cam, VisualElement stage, VisualElement root, bool active) {
            Rect stageRect = CanonicalStage.ScreenRect(stage, out bool fallback);
            Assert.IsFalse(fallback, "the stage must have laid out");

            // Where the world is DRAWN: the element's on-screen box, bottom-left origin.
            Rect wb = view.Element.worldBound;
            float scale = Screen.height / root.panel.visualTree.layout.height;
            var drawn = new Rect(wb.x * scale, Screen.height - (wb.y + wb.height) * scale,
                wb.width * scale, wb.height * scale);
            Rect picked = fs.ToScreenRect(stageRect);
            Assert.AreEqual(picked.x, drawn.x, 1f, $"active={active}");
            Assert.AreEqual(picked.y, drawn.y, 1f, $"active={active}");
            Assert.AreEqual(picked.width, drawn.width, 1f, $"active={active}");
            Assert.AreEqual(picked.height, drawn.height, 1f, $"active={active}");
            if (active) {
                Assert.AreEqual(new Rect(0, 0, Screen.width, Screen.height), picked, "full-window");
                Assert.AreEqual(Color.clear, stage.resolvedStyle.unityBackgroundImageTintColor, "frame hidden");
            } else {
                Assert.AreNotEqual(Color.clear, stage.resolvedStyle.unityBackgroundImageTintColor, "frame shown");
            }

            // The texture is the drawn box's shape, so the camera's aspect is the box's.
            Assert.AreEqual(fs.RenderTextureSize(stageRect), new Vector2Int(cam.targetTexture.width, cam.targetTexture.height));
            Assert.AreEqual(drawn.width / drawn.height, cam.aspect, 0.01f);

            // A floor point drawn at a pixel of the texture, mapped through the DRAWN box, picks back.
            Vector2 start = picked.center - new Vector2(0f, picked.height * 0.25f);
            Vector3? ground = WorldPicker.PickGroundPoint(cam, fs, start, stageRect);
            Assert.IsTrue(ground.HasValue, "the start pixel hits the floor");
            Vector3 vp = cam.WorldToViewportPoint(ground.Value);
            Vector2 drawnPixel = drawn.min + new Vector2(vp.x, vp.y) * drawn.size;
            Vector3? back = WorldPicker.PickGroundPoint(cam, fs, drawnPixel, stageRect);
            Assert.IsTrue(back.HasValue);
            Assert.Less(Vector3.Distance(ground.Value, back.Value), 0.05f, $"active={active}");
            Vector2? screen = WorldPicker.ScreenPointOfGround(cam, fs, ground.Value, stageRect);
            Assert.Less(Vector2.Distance(screen.Value, drawnPixel), 1f, $"active={active}");
        }
    }
}
