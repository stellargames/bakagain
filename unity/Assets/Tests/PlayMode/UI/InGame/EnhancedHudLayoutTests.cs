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
    /// <remarks>Run at 16:9 desktop, the owner's 20:9 phone (SM-A525F) and 4:3 (TASK-939/940).</remarks>
    [RequiresShippedGameData]
    [TestFixture(1920u, 1080u)]
    [TestFixture(2400u, 1080u)]
    [TestFixture(1024u, 768u)]
    public class EnhancedHudLayoutTests {
        private readonly uint _width, _height;

        public EnhancedHudLayoutTests(uint width, uint height) {
            _width = width;
            _height = height;
        }

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
        private VisualElement _stage;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _gameView = GameViewResolutionScope.Force(_width, _height);
            yield return null;
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                AssetDatabase.GUIDToAssetPath("e1ac2bea2a13f4a4abb255bc71d7cc98"));
            Assert.NotNull(settings?.themeStyleSheet, "the shared PanelSettings and its theme");
            _host = new GameObject("~EnhancedHudHost");
            UIDocument document = _host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            _root = document.rootVisualElement;
            VisualElement stage = _stage = CanonicalStage.GetOrCreate(_root,
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
            // SpellEffectCaptionView's plaque and rune row, inline as it builds them.
            stage.Add(new VisualElement {
                name = SpellEffectCaptionView.PlaqueName,
                style = {
                    position = Position.Absolute, left = 640, top = 10, width = 320, height = 48,
                    backgroundImage = Background.FromTexture2D(Texture2D.whiteTexture),
                },
            });
            stage.Add(new VisualElement {
                name = SpellEffectCaptionView.SymbolsName,
                style = {
                    position = Position.Absolute, left = 800, top = 20,
                    translate = new StyleTranslate(new Translate(Length.Percent(-50f), 0)),
                },
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
            VisualElement bar = _root.Q(SpellEffectCaptionView.PlaqueName);
            VisualElement runes = _root.Q(SpellEffectCaptionView.SymbolsName);
            VisualElement stage = bar.parent;
            StyleBackground parchment = bar.style.backgroundImage;
            StyleTranslate runesTranslate = runes.style.translate;

            var hud = new EnhancedHudLayout(_root, session: null, clock: null, statusFontFraction: 0.024f, EnhancedHudLayout.Travel);
            Assert.IsTrue(hud.Apply());
            yield return null;
            yield return null;
            Assert.AreSame(compass.parent, bar.parent, "the spell bar in the top strip");
            Assert.Less(bar.worldBound.xMax, compass.worldBound.xMin + 1f, "left of the compass");
            Assert.AreEqual(StyleKeyword.Null, bar.style.backgroundImage.keyword, "the parchment is off");
            Assert.Greater(bar.resolvedStyle.backgroundColor.a, 0f, "the theme's translucent bar instead");
            Assert.Less(bar.resolvedStyle.backgroundColor.a, 1f);
            Assert.AreSame(bar, runes.parent, "the runes ride on the bar");
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
            Assert.AreSame(stage, bar.parent);
            Assert.AreSame(stage, runes.parent);
            Assert.AreEqual(parchment, bar.style.backgroundImage, "the parchment is back");
            Assert.AreEqual(runesTranslate, runes.style.translate);
            Assert.IsFalse(head.ClassListContains("enhanced-hud__portrait"));
            Assert.AreEqual(DisplayStyle.Flex, _root.Q("imagebutton_72").resolvedStyle.display);
            Assert.IsNull(_root.Q("enhanced-hud"), "the container is gone");
        }

        [UnityTest]
        public IEnumerator AMissingElementMovesNothing() {
            _root.Q("imagebutton_48").name = "renamed-for-test";
            VisualElement head = _root.Q("hotspot_2");
            VisualElement parent = head.parent;

            BakAgain.Tests.TestSupport.ExpectedLog.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("EnhancedHud"));
            var hud = new EnhancedHudLayout(_root, session: null, clock: null, statusFontFraction: 0.024f, EnhancedHudLayout.Travel);
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
            var hud = new EnhancedHudLayout(_root, session: null, clock: null, statusFontFraction: 0.024f, EnhancedHudLayout.Travel);
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

        private static readonly string[] Portraits = { "hotspot_2", "hotspot_3", "hotspot_4" };
        private static readonly string[] Buttons =
            { "toggle_19", "imagebutton_50", "imagebutton_46", "imagebutton_48", "imagebutton_18", "imagebutton_24" };

        private IEnumerator ApplyHud() {
            var hud = new EnhancedHudLayout(_root, session: null, clock: null, statusFontFraction: 0.024f, EnhancedHudLayout.Travel);
            Assert.IsTrue(hud.Apply());
            yield return null;
            yield return null;
        }

        /// <summary>
        /// TASK-939 (owner 2026-10-22): the left edge in quarters of the window height, a portrait in
        /// each of the first three, so none sits at mid-height where a phone's camera hole is.
        /// </summary>
        [UnityTest]
        public IEnumerator APortraitSitsInEachOfTheTopThreeQuarters() {
            yield return ApplyHud();
            float h = _root.panel.visualTree.layout.height;
            for (int i = 0; i < Portraits.Length; i++) {
                Rect b = _root.Q(Portraits[i]).worldBound;
                Assert.AreEqual((i + 0.5f) * h / 4f, b.center.y, 1f, $"{Portraits[i]} centred in quarter {i + 1}");
                Assert.GreaterOrEqual(b.yMin, i * h / 4f, Portraits[i]);
                Assert.LessOrEqual(b.yMax, (i + 1) * h / 4f, Portraits[i]);
            }
            Assert.Less(_root.Q(Portraits[1]).worldBound.yMax, h / 2f, "clear of mid-height");
            Assert.Greater(_root.Q(Portraits[2]).worldBound.yMin, h / 2f, "clear of mid-height");
        }

        /// <summary>
        /// TASK-940 (owner 2026-10-22): the buttons evenly spaced over the whole height and sized from
        /// their share, aspect kept, and a press re-applying the native-size face leaves that alone.
        /// </summary>
        [UnityTest]
        public IEnumerator TheButtonsSpreadEvenlyOverTheWholeHeightSizedFromTheirShare() {
            yield return ApplyHud();
            float h = _root.panel.visualTree.layout.height;
            float share = h / Buttons.Length;
            var before = new Rect[Buttons.Length];
            for (int i = 0; i < Buttons.Length; i++) {
                Rect b = before[i] = _root.Q(Buttons[i]).worldBound;
                Assert.AreEqual((i + 0.5f) * share, b.center.y, 1f, $"{Buttons[i]} centred in its sixth");
                Assert.Greater(b.height, 0.75f * share, $"{Buttons[i]} sized from its share");
                Assert.LessOrEqual(b.height, share, $"{Buttons[i]} inside its share");
                Assert.AreEqual(170f / 174f, b.width / b.height, 0.01f, $"{Buttons[i]} keeps its aspect");
            }
            // What MenuIconLoader does on every press: the face again, inline, at native size.
            Sprite face = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);
            foreach (string n in Buttons) _root.Q(n).SetBackgroundSpriteNativeSizeTopLeft(face);
            yield return null;
            yield return null;
            for (int i = 0; i < Buttons.Length; i++) {
                Rect b = _root.Q(Buttons[i]).worldBound;
                Assert.AreEqual(before[i].height, b.height, 0.5f, $"{Buttons[i]}: a press keeps the size");
                Assert.AreEqual(before[i].center.y, b.center.y, 0.5f, $"{Buttons[i]}: and the place");
            }
            Object.DestroyImmediate(face);
        }

        /// <summary>
        /// TASK-939/940 on a phone: the four touch pads in the left edge's fourth quarter, clear of
        /// the portraits and of the button column, which has the right edge to itself.
        /// </summary>
        [UnityTest]
        public IEnumerator OnATouchScreenThePadsSitInTheFourthQuarterClearOfPortraitsAndButtons() {
            yield return ApplyHud();
            var view = new TouchControlsView(new BakAgain.UI.InputCore.TouchInputState(),
                new BakAgain.UI.InputCore.FakePointer { IsPresent = false, CanPointOverride = true }, new TouchControlsLayout());
            view.PadBandTopOverride = () => new EnhancedTravelLayout().TouchPadBandTop;
            try {
                view.Build(_root, _stage);
                yield return null;
                view.Refresh(inFight: false);
                yield return null;
                yield return null;
                float h = _root.panel.visualTree.layout.height;
                string[] pads = { "touchpad_72", "touchpad_80", "touchpad_75", "touchpad_77" };
                if (_stage.layout.x < new TouchControlsLayout().MinBarWidth) {
                    Assert.AreEqual(DisplayStyle.None, _root.Q("touch-left").resolvedStyle.display, "4:3: no side bars, no pads");
                    yield break;
                }
                var others = new List<string>(Portraits);
                others.AddRange(Buttons);
                foreach (string p in pads) {
                    Rect r = _root.Q(p).worldBound;
                    Assert.IsTrue(_root.Q("touch-left").Contains(_root.Q(p)), $"{p} on the left");
                    Assert.GreaterOrEqual(r.yMin, 0.75f * h - 0.5f, $"{p} in the fourth quarter");
                    Assert.LessOrEqual(r.yMax, h + 0.5f, $"{p} inside the window");
                    foreach (string o in others) {
                        Assert.IsFalse(r.Overlaps(_root.Q(o).worldBound), $"{p} overlaps {o}");
                    }
                }
            } finally {
                view.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator StatusLineTemplateReadsDayAndHour() {
            yield return null;
            Assert.AreEqual("Day 12 · 14:00", GameData.Resources.Text.UiTemplates.Format(
                GameData.Resources.Text.UiTemplates.EnhancedStatusKey, ("day", 12), ("hour", 14)));
        }
    }
}
