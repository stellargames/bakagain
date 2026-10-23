namespace BakAgain.Tests.PlayMode.UI.Spells {
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using BakAgain.UI.Spells;
    using GameData.Resources.Character;
    using GameData.Resources.Layout;
    using GameData.Resources.Spells;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// TASK-954: the Enhanced cast screen is pushed over a fight, and the combat HUD (tier -1) stays up
    /// under it. Every box of the Enhanced layout ignored picking and the REQ stage it replaces is
    /// hidden, so a click on the screen's empty bottom strip fell through to the HUD: live in
    /// Main.unity, Rest, Rest, Inspect and Auto-resolve all acted from under the cast screen. The
    /// original's cast screen covers the HUD and nothing under it can be clicked.
    /// </summary>
    public class EnhancedCastBlocksTheHudTests {
        private EnhancedPrefsStash _stash;
        private GameObject _hud, _cast;
        private VisualElement _hudRoot, _castRoot, _castStage;

        // generated/REQ/COMBAT.json: the combat HUD's buttons (panel at 0,0), name -> (x, y, w, h).
        private static readonly (string Name, int X, int Y, int W, int H)[] Combat = {
            ("imagebutton_46", 1000, 786, 160, 156), ("imagebutton_33", 1185, 786, 160, 156),
            ("imagebutton_32", 1365, 786, 160, 156), ("imagebutton_22", 75, 822, 250, 270),
            ("imagebutton_30", 1365, 990, 160, 156), ("imagebutton_47", 1000, 990, 160, 156),
            ("imagebutton_19", 1185, 990, 160, 156),
        };

        [UnitySetUp]
        public IEnumerator SetUp() {
            _stash = EnhancedPrefsStash.Take();
            GameOptions.Enhanced = true;
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                AssetDatabase.GUIDToAssetPath("e1ac2bea2a13f4a4abb255bc71d7cc98"));
            Assert.NotNull(settings?.themeStyleSheet, "the shared PanelSettings and its theme");

            _hudRoot = Document(settings, "~CombatHud", -1, out _hud);
            VisualElement hudStage = CanonicalStage.GetOrCreate(_hudRoot,
                new DesignFrame { Width = Canonical.Width, Height = Canonical.Height, Fit = LayoutFit.Contain });
            foreach ((string name, int x, int y, int w, int h) in Combat) {
                hudStage.Add(new VisualElement {
                    name = name, pickingMode = PickingMode.Position,
                    style = { position = Position.Absolute, left = x, top = y, width = w, height = h },
                });
            }

            _castRoot = Document(settings, "~CastScreen", 0, out _cast);
            VisualElement castStage = _castStage = CanonicalStage.GetOrCreate(_castRoot,
                new DesignFrame { Width = Canonical.Width, Height = Canonical.Height, Fit = LayoutFit.Contain });
            var names = new List<string> { "imagebutton_1" };
            for (int i = 0; i < CastRingLayout.CategoryCount; i++) names.Add($"imagebutton_{CastMenuSelection.FirstSchoolActionId + i}");
            for (int i = 0; i < 3; i++) names.Add($"hotspot_{CastMenuSelection.FirstPartySlotActionId + i}");
            foreach (string n in names) castStage.Add(new VisualElement { name = n, pickingMode = PickingMode.Position });
            yield return null;
        }

        private static VisualElement Document(PanelSettings settings, string name, int order, out GameObject host) {
            host = new GameObject(name);
            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.sortingOrder = order;
            return document.rootVisualElement;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            Object.DestroyImmediate(_cast);
            Object.DestroyImmediate(_hud);
            _stash.Restore();
            yield return null;
        }

        private static GameSession Party() {
            var session = new GameSession();
            session.SetActiveParty(3, new byte[] { 0, 1, 2 });
            for (int id = 0; id < 3; id++) {
                var stats = new ActorStat[16];
                for (int i = 0; i < stats.Length; i++) stats[i] = new ActorStat { Base = 30, Max = 40 };
                session.SetActorStatsForTest(id, stats);
            }
            return session;
        }

        private static IReadOnlyList<RingPosition> Ring() =>
            Enumerable.Range(0, 30).Select(i => new RingPosition {
                X = 400 + (int)(300 * System.Math.Cos(i * 0.21)), Y = 600 + (int)(300 * System.Math.Sin(i * 0.21)),
            }).ToList();

        [UnityTest]
        public IEnumerator NoClickOnTheEnhancedCastScreenReachesTheCombatHudBeneath() {
            IPanel panel = _hudRoot.panel;
            Assert.AreSame(panel, _castRoot.panel, "one shared panel, as in the game");
            VisualElement rest = _hudRoot.Q("imagebutton_19");
            // Control: with nothing applied the faithful stage is empty here, so the HUD takes the click.
            Assert.AreSame(rest, panel.Pick(rest.worldBound.center), "the HUD's Rest is reachable before the layout");

            var layout = new EnhancedCastLayout(_castRoot, Party());
            Assert.IsTrue(layout.Apply(_castStage, Ring(), new HashSet<int>(), combat: true));
            yield return null;
            yield return null;

            foreach ((string name, _, _, _, _) in Combat) {
                VisualElement button = _hudRoot.Q(name);
                VisualElement picked = panel.Pick(button.worldBound.center);
                Assert.IsTrue(picked != null && _castRoot.Contains(picked),
                    $"a click at the HUD's {name} is the cast screen's (picked {picked?.name ?? "nothing"})");
            }
            // The screen's own controls still take their clicks.
            foreach (string own in new[] { "imagebutton_1", $"imagebutton_{CastMenuSelection.FirstSchoolActionId}" }) {
                VisualElement e = _castRoot.Q(own);
                Assert.AreSame(e, panel.Pick(e.worldBound.center), $"{own} takes its own click");
            }
            layout.Revert();
        }
    }
}
