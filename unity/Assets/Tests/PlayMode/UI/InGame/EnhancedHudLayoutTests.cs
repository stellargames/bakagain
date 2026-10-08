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
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// Enhanced travel HUD (spec 2026-10-08 §3). No fixture boots the travel screen with a world,
    /// so this builds REQ_MAIN's named elements at their REQ rects on the game's own PanelSettings
    /// (the real theme), with the world's click area stretched full-window by the real
    /// <see cref="WorldViewportView"/> — the arrangement the travel screen has in full-screen.
    /// </summary>
    public class EnhancedHudLayoutTests {
        // REQ_MAIN.json: name -> (x, y, w, h).
        private static readonly (string Name, int X, int Y, int W, int H)[] ReqMain = {
            ("imagebutton_75", 630, 732, 80, 49), ("imagebutton_72", 755, 678, 88, 48),
            ("imagebutton_80", 760, 798, 88, 48), ("imagebutton_77", 885, 732, 80, 49),
            ("toggle_19", 1000, 786, 170, 174), ("imagebutton_50", 1000, 990, 170, 174),
            ("imagebutton_46", 1185, 786, 170, 174), ("imagebutton_48", 1180, 990, 170, 174),
            ("imagebutton_18", 1365, 786, 170, 174), ("imagebutton_24", 1365, 990, 170, 174),
            ("hotspot_2", 70, 852, 265, 264), ("hotspot_3", 365, 852, 260, 264),
            ("hotspot_4", 660, 852, 260, 264),
        };

        private static readonly string[] Moved = {
            "hotspot_2", "hotspot_3", "hotspot_4", "toggle_19", "imagebutton_50", "imagebutton_46",
            "imagebutton_48", "imagebutton_18", "imagebutton_24" };

        private GameViewResolutionScope _gameView;
        private GameObject _host;
        private GameObject _camGo;
        private WorldViewportView _view;
        private VisualElement _root;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _gameView = GameViewResolutionScope.Force(1920, 1080);
            yield return null;
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                AssetDatabase.GUIDToAssetPath("e1ac2bea2a13f4a4abb255bc71d7cc98"));
            Assert.NotNull(settings?.themeStyleSheet, "the shared PanelSettings and its theme");
            _host = new GameObject("~EnhancedHudHost");
            UIDocument document = _host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            _root = document.rootVisualElement;
            VisualElement stage = CanonicalStage.GetOrCreate(_root,
                new DesignFrame { Width = Canonical.Width, Height = Canonical.Height, Fit = LayoutFit.Contain });
            foreach ((string name, int x, int y, int w, int h) in ReqMain) {
                var e = new VisualElement { name = name, style = { left = x, top = y, width = w, height = h } };
                e.AddToClassList("req-hitbox");
                stage.Add(e);
            }
            var inner = new WorldViewport();
            Area c = inner.CanonicalRect;
            stage.Add(new VisualElement {
                name = "hotspot_192",
                style = { position = Position.Absolute, left = c.X, top = c.Y, width = c.Width, height = c.Height },
            });
            // CompassView's window: inline absolute placement at the synthesized REQ rect.
            stage.Add(new VisualElement {
                name = CompassView.WindowName,
                style = { position = Position.Absolute, left = 720, top = 726, width = 155, height = 60 },
            });

            _camGo = new GameObject("~EnhancedHudCamera");
            var fs = new FullScreenViewport(inner, () => true, () => new Vector2(Screen.width, Screen.height));
            _view = new WorldViewportView(fs, NullLogger.Instance);
            _view.SetWorldCamera(_camGo.AddComponent<Camera>());
            _view.Attach(_root);
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _view?.Dispose();
            Object.DestroyImmediate(_camGo);
            Object.DestroyImmediate(_host);
            _gameView.Restore();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ApplyMovesEveryElementAndRevertPutsItBackExactly() {
            VisualElement head = _root.Q("hotspot_2");
            VisualElement compass = _root.Q(CompassView.WindowName);
            VisualElement oldParent = head.parent;
            int oldIndex = oldParent.IndexOf(head);
            StyleLength oldLeft = head.style.left;
            StyleLength oldWidth = head.style.width;
            StyleEnum<Position> compassPosition = compass.style.position;

            var hud = new EnhancedHudLayout(_root);
            Assert.IsTrue(hud.Apply());
            yield return null;
            yield return null;
            Assert.AreEqual("enhanced-hud", head.parent.parent.name, "in the HUD's portrait column");
            Assert.AreEqual(DisplayStyle.None, _root.Q("imagebutton_72").resolvedStyle.display);
            Assert.AreEqual("enhanced-hud", compass.parent.parent.name, "the compass in the top strip");

            hud.Revert();
            yield return null;
            Assert.IsFalse(hud.Applied);
            Assert.AreSame(oldParent, head.parent);
            Assert.AreEqual(oldIndex, oldParent.IndexOf(head), "its REQ sibling index");
            Assert.AreEqual(oldLeft, head.style.left);
            Assert.AreEqual(oldWidth, head.style.width);
            Assert.AreEqual(compassPosition, compass.style.position, "the compass's inline absolute is back");
            Assert.IsFalse(head.ClassListContains("enhanced-hud__portrait"));
            Assert.AreEqual(DisplayStyle.Flex, _root.Q("imagebutton_72").resolvedStyle.display);
            Assert.IsNull(_root.Q("enhanced-hud"), "the container is gone");
        }

        [UnityTest]
        public IEnumerator AMissingElementMovesNothing() {
            _root.Q("imagebutton_48").name = "renamed-for-test";
            VisualElement head = _root.Q("hotspot_2");
            VisualElement parent = head.parent;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("EnhancedHud"));
            var hud = new EnhancedHudLayout(_root);
            Assert.IsFalse(hud.Apply());
            Assert.IsFalse(hud.Apply(), "a retry (the compass builds late) stays quiet: one warning");
            Assert.AreSame(parent, head.parent);
            Assert.IsNull(_root.Q("enhanced-hud"));
            yield return null;
        }

        /// <summary>
        /// The HUD sits ABOVE the stretched world click area: a click on a moved portrait or button
        /// is picked as that element (whose loader handlers dispatch it), and a click between them
        /// still reaches the world. <see cref="IPanel.Pick"/> is what pointer events target with.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryMovedElementTakesItsOwnClickAboveTheWorld() {
            var hud = new EnhancedHudLayout(_root);
            Assert.IsTrue(hud.Apply());
            yield return null;
            yield return null;

            VisualElement world = _root.Q("hotspot_192");
            float width = _root.panel.visualTree.layout.width;
            float height = _root.panel.visualTree.layout.height;
            foreach (string name in Moved) {
                VisualElement e = _root.Q(name);
                Rect b = e.worldBound;
                Assert.Greater(b.width, 0f, name);
                Assert.AreSame(e, _root.panel.Pick(b.center), $"{name} takes its own click");
                if (name.StartsWith("hotspot_")) {
                    Assert.AreEqual(b.height, b.width, 1f, $"{name} is square");
                    Assert.Less(b.xMax, width * 0.15f, $"{name} in the left column");
                } else {
                    Assert.Greater(b.xMin, width * 0.85f, $"{name} in the right column");
                }
            }
            Assert.AreSame(world, _root.panel.Pick(new Vector2(width * 0.5f, height * 0.6f)),
                "the middle of the window is still the world");
            Rect compass = _root.Q(CompassView.WindowName).worldBound;
            Assert.AreEqual(width * 0.5f, compass.center.x, width * 0.1f, "the compass near top-centre");
            Assert.Less(compass.yMax, height * 0.15f);
        }

        [UnityTest]
        public IEnumerator StatusLineTemplateReadsDayAndHour() {
            yield return null;
            Assert.AreEqual("Day 12 · 14:00", GameData.Resources.Text.UiTemplates.Format(
                GameData.Resources.Text.UiTemplates.EnhancedStatusKey, ("day", 12), ("hour", 14)));
        }
    }
}
