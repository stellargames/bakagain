namespace BakAgain.Tests.PlayMode.UI.InGame {
    using System.Collections;
    using BakAgain.Graphics;
    using BakAgain.UI;
    using BakAgain.UI.InGame;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// TASK-957: during a fight nothing of the travel HUD below the world viewport can be picked.
    /// </summary>
    /// <remarks>
    /// The travel HUD (tier -2) stays up under the combat HUD (tier -1), and the combat REQ covers only
    /// its own stones. With the capability stone blank (an enemy adjacent; COMBAT.DAT ships entry 14
    /// Disabled, so it takes no pick) a click at canonical (1080,864) reached REQ_MAIN's follow-road
    /// toggle and played its press sound; the portraits and the edges of map, bookmark and options
    /// were reachable the same way. TASK-590 already refuses the actions, but the pick, the hover face
    /// and the sound still happened. The original never polls REQ_MAIN in a fight (COMBAT.C:118, the
    /// loop runs menupage_run(g_combat_menu) at :1736), and a click on a gated entry hits nothing
    /// (MENUPAGE.C:446-458), so there is no sound either.
    /// </remarks>
    public class TravelHudUnderTheCombatHudTakesNoClickTests {
        // generated/REQ/REQ_MAIN.json, panel at (0,0): name -> (x, y, w, h). The viewport's bottom is the
        // split the combat frame (CFRAME.SCX) is laid from.
        private static readonly (string Name, int X, int Y, int W, int H)[] Travel = {
            ("imagebutton_75", 630, 732, 80, 49), ("imagebutton_72", 755, 678, 88, 48),
            ("imagebutton_80", 760, 798, 88, 48), ("imagebutton_77", 885, 732, 80, 49),
            ("toggle_19", 1000, 786, 170, 174), ("imagebutton_50", 1000, 990, 170, 174),
            ("imagebutton_46", 1185, 786, 170, 174), ("imagebutton_48", 1180, 990, 170, 174),
            ("imagebutton_18", 1365, 786, 170, 174), ("imagebutton_24", 1365, 990, 170, 174),
            ("hotspot_2", 70, 852, 265, 264), ("hotspot_3", 365, 852, 260, 264),
            ("hotspot_4", 660, 852, 260, 264), ("hotspot_1000", 720, 726, 155, 60),
        };
        private const int ViewportX = 65, ViewportY = 66, ViewportW = 1470, ViewportH = 606;

        private GameObject _doc, _hudHost;
        private VisualElement _root, _stage;
        private InGameScreen _hud;
        private bool _inCombat;

        [UnitySetUp]
        public IEnumerator SetUp() {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                AssetDatabase.GUIDToAssetPath("e1ac2bea2a13f4a4abb255bc71d7cc98"));
            Assert.NotNull(settings, "the shared PanelSettings");
            _doc = new GameObject("~TravelHud");
            UIDocument document = _doc.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.sortingOrder = -2;
            _root = document.rootVisualElement;
            _stage = CanonicalStage.GetOrCreate(_root,
                new DesignFrame { Width = Canonical.Width, Height = Canonical.Height, Fit = LayoutFit.Contain });
            _stage.Add(Box("hotspot_192", ViewportX, ViewportY, ViewportW, ViewportH));
            foreach ((string name, int x, int y, int w, int h) in Travel) {
                _stage.Add(Box(name, x, y, w, h));
            }

            _hudHost = new GameObject("TravelHudUnderTest");
            _hudHost.SetActive(false);   // no Awake/Update: the chrome is driven by hand below
            _hud = _hudHost.AddComponent<InGameScreen>();
            _hud.SetInCombatPredicate(() => _inCombat);
            _hud.AttachCombatGuard(_stage, ViewportY + ViewportH);
            yield return null;
        }

        private static VisualElement Box(string name, int x, int y, int w, int h) => new VisualElement {
            name = name, pickingMode = PickingMode.Position,
            style = { position = Position.Absolute, left = x, top = y, width = w, height = h },
        };

        [UnityTearDown]
        public IEnumerator TearDown() {
            Object.DestroyImmediate(_hudHost);
            Object.DestroyImmediate(_doc);
            yield return null;
        }

        private VisualElement PickAt(float canonicalX, float canonicalY) {
            Rect box = _stage.worldBound;
            float scale = box.width / Canonical.Width;
            return _root.panel.Pick(new Vector2(box.x + canonicalX * scale, box.y + canonicalY * scale));
        }

        [UnityTest]
        public IEnumerator InAFight_NoTravelEntryBelowTheViewportTakesAPick_AndTheArenaStillDoes() {
            // Control: out of a fight the travel HUD takes its own clicks.
            _inCombat = false;
            _hud.RefreshCombatChrome();
            yield return null;
            Assert.AreEqual("toggle_19", PickAt(1080, 864)?.name, "travel: the follow-road toggle");

            _inCombat = true;
            _hud.RefreshCombatChrome();
            yield return null;
            Assert.AreNotEqual("toggle_19", PickAt(1080, 864)?.name,
                "the reported click: under the blank capability stone");
            foreach ((string name, int x, int y, int w, int h) in Travel) {
                // The centre and the four corners, a pixel in: the combat stones are smaller than the
                // travel ones, so the travel edges are what a near miss reaches.
                foreach ((float px, float py) in new[] {
                             (x + w / 2f, y + h / 2f), (x + 1f, y + 1f), (x + w - 1f, y + 1f),
                             (x + 1f, y + h - 1f), (x + w - 1f, y + h - 1f),
                         }) {
                    VisualElement picked = PickAt(px, py);
                    Assert.IsFalse(picked != null && picked.name == name,
                        $"in a fight a click at ({px},{py}) reached the travel HUD's {name}");
                }
            }
            Assert.AreEqual("hotspot_192", PickAt(800, 400)?.name, "the arena is clicked through the viewport");

            // And the fight's end hands the HUD back.
            _inCombat = false;
            _hud.RefreshCombatChrome();
            yield return null;
            Assert.AreEqual("toggle_19", PickAt(1080, 864)?.name, "after the fight: the toggle again");
        }
    }
}
