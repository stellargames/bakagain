namespace BakAgain.Tests.PlayMode.UI.Combat {
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI.Combat.Enhanced;
    using BakAgain.UI.InGame;
    using GameData.Resources.Combat;
    using GameData.Resources.Layout;
    using GameData.Resources.Text;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>Shared models for the HUD tests: Owyn's turn against three brigands (frames 01/02/14).</summary>
    internal static class HudModels {
        public static readonly string[] Log = {
            "C shoots Locklear: hit 5", "Locklear shoots A: hit 6", "Owyn defends",
            "A swings at Owyn: miss", "Gorath defends", "C shoots Locklear: hit 12",
        };

        public static IReadOnlyDictionary<CombatCommands.Command, ActionAvailability> Stones(
            string castReason = "foe beside Owyn", string restWarning = "no heal: poisoned") =>
            new Dictionary<CombatCommands.Command, ActionAvailability> {
                [CombatCommands.Command.Cast] = new(false, false, UiTemplates.EnhancedAvailFoeAdjacent, castReason),
                [CombatCommands.Command.Shoot] = new(false, false, UiTemplates.EnhancedAvailNoCrossbow, "no crossbow"),
                [CombatCommands.Command.BackOrRetreat] = ActionAvailability.On,
                [CombatCommands.Command.Defend] = ActionAvailability.On,
                [CombatCommands.Command.CharacterScreen] = ActionAvailability.On,
                [CombatCommands.Command.Rest] = new(true, true, UiTemplates.EnhancedAvailNoHealCondition, restWarning),
                [CombatCommands.Command.AutoResolve] = ActionAvailability.On,
            };

        public static HeroCardModel Owyn => new("Owyn", 0, new[] { "Poisoned" }, 17, 40, 4, 40, 2, 4, 6, 12,
            new KitItemModel("Quarterstaff", 92, "Thrust 5+Str · Swing 7+Str"), new KitItemModel("Leather", 80, "Armour 2"));

        public static IReadOnlyList<PartyCoinModel> Party => new[] {
            new PartyCoinModel(1, "Owyn", 0, RingVariant.Active, 17, 40, 4, 40),
            new PartyCoinModel(2, "Gorath", 1, RingVariant.Acted, 38, 40, 20, 40),
            new PartyCoinModel(3, "Locklear", 2, RingVariant.Down, 0, 35, 0, 35),
        };

        private static IReadOnlyList<FoeStatRow> Rows(bool known) {
            string[] labels = { "Health", "Stamina", "Missile", "Cast", "Speed", "Strength", "Melee", "Defense" };
            return labels.Select((l, i) => new FoeStatRow(l, l.Substring(0, 2).ToUpperInvariant(),
                known && i == 0 ? "16" : "?", known && i == 0)).ToList();
        }

        public static IReadOnlyList<FoeRowModel> Foes => new[] {
            new FoeRowModel(0, 'A', "Brigand", true, true, 6, false, false, false, false, Rows(false)),
            new FoeRowModel(1, 'B', "Brigand", true, false, 0, false, false, false, false, Rows(false)),
            new FoeRowModel(2, 'C', "Archer", false, false, 0, true, false, false, false, Rows(false)),
        };

        public static CombatForecast Swing => new(ForecastAction.Swing, 0, new[] { (3, 2) }, 1, null, 3, 5, null,
            1, 0, 0, true, true, false, "");

        public static CombatForecast Thrust => new(ForecastAction.Thrust, 0, new[] { (3, 2) }, 2, null, 1, 2, null,
            0, 0, 0, true, true, false, "");

        // A Thrust that walks three cells first (start included in the path).
        public static CombatForecast WalkingThrust => new(ForecastAction.Thrust, 0, new[] { (0, 0), (1, 1), (2, 2), (3, 2) }, 2, null,
            1, 2, null, 0, 0, 0, true, true, false, "");

        public static CombatHudModel WalkingForecast() =>
            new(1, Owyn, Party, WalkingThrust, Log, Foes, 0, Stones(), false, "Owyn's turn", Swing, 2);

        public static CombatHudModel Idle(IReadOnlyDictionary<CombatCommands.Command, ActionAvailability> stones = null) =>
            new(1, Owyn, Party, null, Log, Foes, null, stones ?? Stones(), false, "Owyn's turn", null, 2);

        public static CombatHudModel Forecast() =>
            new(1, Owyn, Party, Thrust, Log, Foes, 0, Stones(), false, "Owyn's turn", Swing, 2);

        public static CombatHudModel SwingForecast() =>
            new(1, Owyn, Party, Swing, Log, Foes, 0, Stones(), false, "Owyn's turn", Thrust, 2);

        public static CombatHudModel TheirTurn() =>
            new(0, new HeroCardModel("Brigand A", -1, new string[0], 0, 0, 0, 0, 0, 0, 0, 0, null, null, true, 'A'),
                Party, null, Log, Foes, null, new Dictionary<CombatCommands.Command, ActionAvailability>(), true,
                "Brigand A's turn");

        /// <summary>The game's own PanelSettings, so the HUD is laid out with the real theme.</summary>
        public static VisualElement Root(out GameObject host) {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                AssetDatabase.GUIDToAssetPath("e1ac2bea2a13f4a4abb255bc71d7cc98"));
            Assert.NotNull(settings?.themeStyleSheet, "the shared PanelSettings and its theme");
            host = new GameObject("~EnhancedCombatHudHost");
            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            // A bare document's root only wraps its content; InGameScreen's covers the window.
            VisualElement root = document.rootVisualElement;
            DesktopCombatHud.Fill(root);
            return root;
        }

        /// <summary>Whether a pick at this panel point lands on the HUD (true) or passes to what is under it.</summary>
        public static bool PickHitsHud(VisualElement hudRoot, Vector2 point) {
            for (VisualElement v = hudRoot.panel.Pick(point); v != null; v = v.parent) {
                if (v == hudRoot) return true;
            }
            return false;
        }

        public static bool Visible(VisualElement e) {
            for (VisualElement v = e; v != null; v = v.parent) {
                if (v.resolvedStyle.display == DisplayStyle.None || v.resolvedStyle.visibility == Visibility.Hidden) {
                    return false;
                }
            }
            return e.worldBound.width > 0.5f && e.worldBound.height > 0.5f;
        }
    }

    /// <summary>
    /// The desktop band (TASK-972 phase 1, Task 8) at the three desktop shapes: five slots that never
    /// change shape, the ledger's log under a forecast, the foe slot's list and card, and reasons only
    /// in a hover tooltip (owner, 2026-10-28).
    /// </summary>
    [TestFixture(1920u, 1080u)]
    [TestFixture(1600u, 900u)]
    [TestFixture(2400u, 1080u)]
    public class EnhancedCombatDesktopHudTests {
        private readonly uint _width, _height;
        private GameViewResolutionScope _gameView;
        private GameObject _host;
        private DesktopCombatHud _hud;
        private readonly EnhancedCombatLayout _layout = new();

        public EnhancedCombatDesktopHudTests(uint width, uint height) {
            _width = width;
            _height = height;
        }

        [UnitySetUp]
        public IEnumerator SetUp() {
            _gameView = GameViewResolutionScope.Force(_width, _height);
            yield return null;
            _hud = new DesktopCombatHud(HudModels.Root(out _host), _layout);
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            Object.DestroyImmediate(_host);
            _gameView.Restore();
            yield return null;
        }

        private Rect[] Slots() => Enumerable.Range(0, 4).Select(i => _hud.Root.Q("ec-desk-slot-" + i).worldBound).ToArray();

        [UnityTest]
        public IEnumerator DesktopBandSlotsKeepTheirRectsInEveryState() {
            _hud.Show(HudModels.Idle());
            yield return null;
            yield return null;
            Rect[] idle = Slots();
            VisualElement band = _hud.Root.Q("ec-desk-band");
            Assert.AreEqual(_layout.BandHeight, band.worldBound.height / _hud.Root.worldBound.height, 0.01f,
                $"the band is BandHeight of the window (root {_hud.Root.worldBound}, parent {_hud.Root.parent?.worldBound}, band {band.worldBound}, panel {_hud.Root.panel?.visualTree.worldBound})");
            for (int i = 1; i < 4; i++) {
                Assert.Greater(idle[i].xMin, idle[i - 1].xMin, $"slot {i} is right of slot {i - 1}");
                float expected = _layout.BandSlotWidths[i] / _layout.BandSlotWidths[0];
                Assert.AreEqual(expected, idle[i].width / idle[0].width, 0.05f, $"slot {i}'s share of the band");
            }

            _hud.Show(HudModels.Forecast());
            yield return null;
            yield return null;
            Rect[] forecast = Slots();
            _hud.Show(HudModels.TheirTurn());
            yield return null;
            yield return null;
            Rect[] theirs = Slots();
            for (int i = 0; i < 4; i++) {
                Assert.AreEqual(idle[i], forecast[i], $"slot {i} under a forecast");
                Assert.AreEqual(idle[i], theirs[i], $"slot {i} on their turn");
            }
            Assert.AreEqual(DisplayStyle.Flex, _hud.Root.Q("ec-desk-party").resolvedStyle.display,
                "on their turn the hero slot is the party");
        }

        // Owner, 2026-11-01: the foe card stands above the stones, as wide as their slot, and a party member
        // under the mouse shows their own card there.
        [UnityTest]
        public IEnumerator TheFoeCardStandsOverTheStones_AndAPartyMemberGetsACardThere() {
            _hud.Show(HudModels.Idle());
            yield return null;
            yield return null;
            Rect foes = _hud.Root.Q("ec-desk-foes").worldBound;
            Rect stones = _hud.Root.Q("ec-desk-slot-3").worldBound;
            Assert.AreEqual(stones.xMin, foes.xMin, 1f, "over the stones' slot");
            Assert.AreEqual(stones.width, foes.width, 1f, "as wide");
            Assert.AreEqual(_hud.Root.Q("ec-desk-band").worldBound.yMin, foes.yMax, 1f, "standing on the band");
            Assert.AreEqual(DisplayStyle.Flex, _hud.Root.Q("ec-desk-foelist").resolvedStyle.display, "the foe list when idle");
            // As tall as what it lists: every foe's row inside the card (a zero-basis list collapsed to its header).
            List<VisualElement> rows = _hud.Root.Query(className: "ec-desk-foe").ToList();
            Assert.IsNotEmpty(rows);
            foreach (VisualElement row in rows) {
                Assert.Greater(row.worldBound.height, 0f, "a foe row has height");
                Assert.GreaterOrEqual(row.worldBound.yMin, foes.yMin - 1f, "inside the card");
                Assert.LessOrEqual(row.worldBound.yMax, foes.yMax + 1f, "inside the card");
            }

            _hud.Show(HudModels.Idle() with { CardHero = HudModels.Owyn });
            yield return null;
            Assert.AreEqual(DisplayStyle.None, _hud.Root.Q("ec-desk-foelist").resolvedStyle.display);
            Assert.AreEqual("Owyn", _hud.Root.Q<Label>("ec-desk-foecard-name").text);
            List<string> values = _hud.Root.Q("ec-desk-foecard-rows").Query<Label>(className: "ec-desk-foecard__value")
                .ToList().Select(l => l.text).ToList();
            CollectionAssert.IsSubsetOf(new[] { "17/40", "4/40" }, values, "health and stamina");
            // Weapon and armour on their own full-width lines, so a long name is not cut to half the card.
            List<string> kit = _hud.Root.Q("ec-desk-foecard-kit").Query<Label>(className: "ec-desk-pair__value")
                .ToList().Select(l => l.text).ToList();
            CollectionAssert.AreEqual(new[] { "92%", "80%" }, kit, "the kit's condition");
        }

        [UnityTest]
        public IEnumerator DesktopLedgerKeepsTheLastTwoLogLinesUnderAForecast() {
            _hud.Show(HudModels.Forecast());
            yield return null;
            string[] footer = _hud.Root.Q("ec-desk-ledger-footer").Query<Label>().ToList()
                .Select(l => System.Text.RegularExpressions.Regex.Replace(l.text, "<[^>]+>", "")).ToArray();
            CollectionAssert.AreEqual(HudModels.Log.Skip(HudModels.Log.Length - _layout.LedgerLogLines).ToArray(), footer);
            // Owner, 2026-11-01: each party member's name has its own colour in the log.
            string last = _hud.Root.Q("ec-desk-ledger-footer").Query<Label>().ToList().Last().text;
            StringAssert.Contains("<color=" + _layout.PartyLogColours[2] + ">Locklear</color>", last, "Locklear is third in the party");
            Assert.IsNotNull(_hud.Root.Q("ec-desk-forecast"), "the forecast table is the ledger's body");
            StringAssert.Contains("Owyn", _hud.Root.Q<Label>("ec-desk-ends-turn").text, "the red ends-turn chip");

            _hud.Show(HudModels.Idle());
            yield return null;
            Assert.IsNull(_hud.Root.Q("ec-desk-forecast"));
            Assert.AreEqual(HudModels.Log.Length, _hud.Root.Q("ec-desk-ledger-body").Query<Label>().ToList().Count,
                "without a forecast the ledger is the log");
        }

        // Real fight-log lines are long: the assessment alone is a paragraph.
        private static readonly string[] LongLog = {
            "moredhel warrior A thrusts at Gorath: hit 18",
            "Owyn studied his opponent. While it was possible he could be grossly misjudging some of the moredhel warrior's abilities, he was absolutely convinced about certain facts.",
            "Locklear thrusts at moredhel warrior A: hit 13",
        };

        // DIAL_Z00 record 303 as LogDialogLine writes it (whitespace collapsed), seen overlapping live (task 11).
        private const string RetreatNarration = "They were trapped. With the sound of his heart pounding in his ears, Gorath "
            + "forced himself to pause... forced himself to think clearly for a moment; an action that lasted mere seconds but "
            + "felt like lifetimes. Taking stock of their situation he quickly realized escape would be futile. Retreat, for "
            + "the moment at least, was out of the question.";

        private static void AssertNoOverlapInside(VisualElement box, Rect slot, string what) {
            List<Label> lines = box.Query<Label>(className: "ec-desk-log").ToList();
            Assert.IsNotEmpty(lines, what);
            for (int i = 1; i < lines.Count; i++) {
                Assert.GreaterOrEqual(lines[i].worldBound.yMin, lines[i - 1].worldBound.yMax - 0.5f,
                    $"{what}: line {i} does not overlap line {i - 1}");
            }
            Label last = lines[lines.Count - 1];
            Assert.LessOrEqual(last.worldBound.yMax, slot.yMax + 0.5f, $"{what}: the newest line stays inside the ledger");
        }

        [UnityTest]
        public IEnumerator DesktopLedgerLogLinesNeverOverlap_TheNewestStaysInsideTheSlot() {
            Rect slot = default;
            CombatHudModel forecast = HudModels.Forecast() with { LogLines = LongLog };
            _hud.Show(forecast);
            yield return null;
            yield return null;
            slot = _hud.Root.Q("ec-desk-slot-2").worldBound;
            AssertNoOverlapInside(_hud.Root.Q("ec-desk-ledger-footer"), slot, "under a forecast");
            foreach (Label l in _hud.Root.Q("ec-desk-ledger-footer").Query<Label>().ToList()) {
                Assert.AreEqual(WhiteSpace.NoWrap, l.resolvedStyle.whiteSpace, "one row per footer line");
            }

            var many = new List<string>();
            for (int i = 0; i < 4; i++) many.AddRange(LongLog);
            // A line taller than the whole ledger (task 11 saw it with the retreat narration): its height was
            // capped at the body's, so its last rows drew under the next line.
            many.Add(RetreatNarration + " " + RetreatNarration);
            many.Add("moredhel warrior A swings at Gorath: hit 20");
            _hud.Show(HudModels.Idle() with { LogLines = many });
            yield return null;
            yield return null;
            VisualElement body = _hud.Root.Q("ec-desk-ledger-body");
            AssertNoOverlapInside(body, slot, "idle");
            foreach (Label l in body.Query<Label>(className: "ec-desk-log").ToList()) {
                float needs = l.MeasureTextSize(l.text, l.resolvedStyle.width, VisualElement.MeasureMode.Exactly,
                    0f, VisualElement.MeasureMode.Undefined).y;
                Assert.GreaterOrEqual(l.resolvedStyle.height, needs - 0.5f, "a line is as tall as its wrapped text: " + l.text.Substring(0, 20));
            }
            Label newest = body.Query<Label>(className: "ec-desk-log").ToList().Last();
            Assert.LessOrEqual(newest.worldBound.yMax, body.worldBound.yMax + 0.5f, "the newest idle line is the one kept in view");
        }

        [UnityTest]
        public IEnumerator DesktopInspectForecastNamesItsVerb() {
            var inspect = new CombatForecast(ForecastAction.Inspect, 0, new (int, int)[0], 0, null, 0, 0, null,
                0, 0, 0, true, true, false, "");
            _hud.Show(HudModels.Idle() with { Forecast = inspect, CardFoe = 0 });
            yield return null;
            string title = _hud.Root.Q<Label>("ec-desk-ledger-title").text;
            StringAssert.StartsWith(UiTemplates.Format(UiTemplates.HudInspect), title, "the verb first");
            StringAssert.Contains("Brigand A", title, "then whom");

            // A long name is what gets cut, never the red chip.
            var foes = HudModels.Foes.Select(f => f with { Name = "moredhel warrior of the northern wastes" }).ToList();
            _hud.Show(HudModels.Idle() with { Forecast = inspect, CardFoe = 0, Foes = foes });
            yield return null;
            yield return null;
            Label chip = _hud.Root.Q<Label>("ec-desk-ends-turn");
            Vector2 need = chip.MeasureTextSize(chip.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            Assert.GreaterOrEqual(chip.contentRect.width + 1f, need.x, "the ends-turn chip keeps its whole text");
            Assert.LessOrEqual(chip.worldBound.xMax, _hud.Root.Q("ec-desk-slot-2").worldBound.xMax + 0.5f);
        }

        [UnityTest]
        public IEnumerator DesktopFoeSlotIsTheListWhenNothingIsHovered_TheCardWhenItIs() {
            _hud.Show(HudModels.Idle());
            yield return null;
            Assert.IsTrue(HudModels.Visible(_hud.Root.Q("ec-desk-foelist")));
            Assert.IsFalse(HudModels.Visible(_hud.Root.Q("ec-desk-foecard")));
            Assert.AreEqual(3, _hud.Root.Q("ec-desk-foe-rows").childCount, "one lettered row per foe");

            _hud.Show(HudModels.Forecast());   // CardFoe = A
            yield return null;
            Assert.IsFalse(HudModels.Visible(_hud.Root.Q("ec-desk-foelist")));
            Assert.IsTrue(HudModels.Visible(_hud.Root.Q("ec-desk-foecard")));
            List<Label> values = _hud.Root.Q("ec-desk-foecard-rows").Query<Label>(className: "ec-desk-foecard__value").ToList();
            Assert.AreEqual(8, values.Count, "the 8-row card");
            Assert.IsTrue(values.All(v => v.text == "?"), "\"?\" until known");
            Assert.AreEqual(3, _hud.Root.Q("ec-desk-foe-discs").childCount, "the letter discs keep the overview");
        }

        [UnityTest]
        public IEnumerator DesktopADisabledStoneShowsItsReasonOnlyInAHoverTooltip_AWarnedOneToo() {
            const string cast = "foe beside Owyn";
            const string rest = "no heal: poisoned";
            _hud.Show(HudModels.Idle(HudModels.Stones(cast, rest)));
            yield return null;
            yield return null;
            VisualElement castStone = _hud.Root.Q("ec-stone-Cast");
            VisualElement restStone = _hud.Root.Q("ec-stone-Rest");
            Assert.IsTrue(castStone.ClassListContains("ec-stone--off"), "an unavailable stone is greyed");
            Assert.IsTrue(HudModels.Visible(restStone.Q("ec-stone-warn")), "a warned stone carries the amber mark");
            Assert.IsFalse(HudModels.Visible(castStone.Q("ec-stone-warn")));
            Assert.IsFalse(Shows(cast) || Shows(rest), "no reason is printed without a hover");

            _hud.HoverStone(CombatCommands.Command.Cast, true);
            yield return null;
            Assert.IsTrue(Shows(cast), "hovering Cast shows its reason");
            _hud.HoverStone(CombatCommands.Command.Cast, false);
            yield return null;
            Assert.IsFalse(Shows(cast));

            _hud.HoverStone(CombatCommands.Command.Rest, true);
            yield return null;
            Assert.IsTrue(Shows(rest), "hovering Rest shows its warning");
            _hud.HoverStone(CombatCommands.Command.Rest, false);
            yield return null;
            Assert.IsFalse(Shows(rest));
        }

        private bool Shows(string text) =>
            _hud.Root.Query<Label>().ToList().Any(l => HudModels.Visible(l) && l.text.Contains(text));

        [UnityTest]
        public IEnumerator DesktopBoardClicksPassThrough_TheBandTakesItsOwn() {
            _hud.Show(HudModels.Idle());
            yield return null;
            yield return null;
            Rect root = _hud.Root.worldBound;
            Rect band = _hud.Root.Q("ec-desk-band").worldBound;
            Assert.IsFalse(HudModels.PickHitsHud(_hud.Root, new Vector2(root.center.x, band.yMin / 2)),
                "the board above the band is clicked through the HUD");
            Assert.IsTrue(HudModels.PickHitsHud(_hud.Root, band.center), "the band takes its own clicks");
            Assert.IsTrue(HudModels.PickHitsHud(_hud.Root, new Vector2(band.xMin + 2, band.yMin + 2)),
                "even between the slots");
        }

        [UnityTest]
        public IEnumerator DesktopShowsTheRound_AndDrawsTheActedAndDownBadges() {
            _hud.Show(HudModels.Idle());
            yield return null;
            Assert.AreEqual("round 2", _hud.Root.Q<Label>("ec-desk-banner-round").text);
            Assert.AreEqual("round 2", _hud.Root.Q<Label>("ec-desk-ledger-round").text);
            List<VisualElement> badges = _hud.Root.Q("ec-desk-coins").Query("coin-badge").ToList();
            CollectionAssert.AreEqual(new object[] { RingVariant.Active, RingVariant.Acted, RingVariant.Down },
                badges.Select(b => b.userData).ToArray(), "each coin knows its variant");
            yield return null;
            // Owner, 2026-11-01: a downed member's portrait dims, with no red cross; the coins stand in a column.
            Assert.AreEqual(DisplayStyle.Flex, badges[1].resolvedStyle.display, "acted: the tick");
            Assert.AreEqual(DisplayStyle.None, badges[2].resolvedStyle.display, "down: no badge");
            Assert.Less(badges[2].parent.resolvedStyle.opacity, 0.5f, "down: dimmed");
            List<VisualElement> coins = _hud.Root.Q("ec-desk-coins").Children().ToList();
            Assert.AreEqual(coins[0].worldBound.center.x, coins[2].worldBound.center.x, 1f, "one column");
            Assert.Less(coins[0].worldBound.yMax, coins[1].worldBound.yMin + 1f, "stacked");
        }

        [UnityTest]
        public IEnumerator DesktopTheForecastSaysAThrustWalksFirst() {
            _hud.Show(HudModels.WalkingForecast());
            yield return null;
            yield return null;
            StringAssert.Contains("Walks 3, then thrusts", _hud.Root.Q<Label>("ec-desk-ledger-title").text);
        }

        [UnityTest]
        public IEnumerator DesktopFoeListCarriesNoSwordMarks() {
            _hud.Show(HudModels.Idle());
            yield return null;
            yield return null;
            Assert.IsNull(_hud.Root.Q(className: "ec-marks"), "no blades in the foe list (owner, 2026-10-29)");
        }

        [UnityTest]
        public IEnumerator DesktopBodyTextIsAtLeastTheMinimum() {
            _hud.Show(HudModels.Forecast());
            yield return null;
            yield return null;
            // 16 px at 1080p is the floor; the panel is 1200 units high.
            float minUnits = 16f / 1080f * _hud.Root.worldBound.height;
            foreach (Label l in _hud.Root.Query<Label>().ToList().Where(l => HudModels.Visible(l) && l.text.Length > 0)) {
                Assert.GreaterOrEqual(l.resolvedStyle.fontSize, minUnits - 0.01f, $"'{l.text}'");
            }
        }
    }

    /// <summary>The phone docks at 844x390 pt (TASK-972 phase 1, Task 8).</summary>
    public class EnhancedCombatPhoneHudTests {
        private const uint Width = 844, Height = 390;
        private GameViewResolutionScope _gameView;
        private GameObject _host;
        private PhoneCombatHud _hud;
        private readonly EnhancedCombatLayout _layout = new();

        // An iPhone in landscape: 47 pt off both sides for the notch and the rounded corners, 21 at the bottom.
        private static readonly Rect Safe = new(47, 21, Width - 94, Height - 21);

        [UnitySetUp]
        public IEnumerator SetUp() {
            _gameView = GameViewResolutionScope.Force(Width, Height);
            yield return null;
            _hud = new PhoneCombatHud(HudModels.Root(out _host), _layout, null,
                () => Safe, () => new Vector2(Width, Height), () => 1f);
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            Object.DestroyImmediate(_host);
            _gameView.Restore();
            yield return null;
        }

        private IEnumerator Settle() {
            yield return null;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator PhoneTargetsAreAtLeast44Points_TextAtLeast11() {
            foreach (CombatHudModel m in new[] { HudModels.Idle(), HudModels.Forecast() }) {
                _hud.Show(m);
                _hud.ShowSheet(m.Forecast == null);
                yield return Settle();
                float scale = _hud.Scale;
                Assert.AreEqual(_hud.Root.worldBound.height / Height, scale, 0.01f, "panel units per point");
                List<VisualElement> all = _hud.Root.Query<VisualElement>().ToList();
                int targets = 0;
                foreach (VisualElement e in all.Where(e => e.pickingMode == PickingMode.Position && HudModels.Visible(e)
                             && e.enabledInHierarchy && e != _hud.Root)) {
                    targets++;
                    Assert.GreaterOrEqual(e.worldBound.width / scale, 44f - 0.5f, $"{e.name ?? e.GetType().Name} width");
                    Assert.GreaterOrEqual(e.worldBound.height / scale, 44f - 0.5f, $"{e.name ?? e.GetType().Name} height");
                }
                Assert.Greater(targets, 10, "the walk found the targets");
                foreach (TextElement t in all.OfType<TextElement>().Where(t => HudModels.Visible(t) && !string.IsNullOrEmpty(t.text))) {
                    Assert.GreaterOrEqual(t.resolvedStyle.fontSize / scale, _layout.MinTextPt - 0.05f, $"'{t.text}'");
                }
            }
        }

        [UnityTest]
        public IEnumerator PhoneNothingInTheUnsafeArea() {
            _hud.Show(HudModels.Forecast());
            yield return Settle();
            float scale = _hud.Scale;
            var safe = new Rect(Safe.xMin * scale, (Height - Safe.yMax) * scale, Safe.width * scale, Safe.height * scale);
            VisualElement root = _hud.Root.Q("ec-phone-safe");
            var checkedOnes = new List<VisualElement>(root.Children());
            checkedOnes.AddRange(_hud.Root.Query<VisualElement>().ToList().Where(e => e.pickingMode == PickingMode.Position));
            foreach (VisualElement e in checkedOnes.Where(HudModels.Visible)) {
                Rect r = e.worldBound;
                Assert.GreaterOrEqual(r.xMin, safe.xMin - 0.5f, $"{e.name} left");
                Assert.LessOrEqual(r.xMax, safe.xMax + 0.5f, $"{e.name} right");
                Assert.GreaterOrEqual(r.yMin, safe.yMin - 0.5f, $"{e.name} top");
                Assert.LessOrEqual(r.yMax, safe.yMax + 0.5f, $"{e.name} bottom");
            }
            // The docks do not overlap one another.
            Rect confirm = _hud.Root.Q("ec-phone-confirm").worldBound;
            Rect peek = _hud.Root.Q("ec-phone-peek").worldBound;
            Rect grid = _hud.Root.Q("ec-phone-grid").worldBound;
            Assert.LessOrEqual(confirm.xMax, peek.xMin + 0.5f);
            Assert.LessOrEqual(peek.xMax, grid.xMin + 0.5f);
        }

        [UnityTest]
        public IEnumerator PhoneRebuildsDoNotGrowTheSizeRegistry() {
            _hud.Show(HudModels.Forecast());
            _hud.ShowSheet(true);
            yield return Settle();
            int first = _hud.RegisteredCount;
            for (int i = 0; i < 20; i++) {
                _hud.Show(i % 2 == 0 ? HudModels.Forecast() : HudModels.Idle());
            }
            _hud.Show(HudModels.Forecast());
            yield return Settle();
            Assert.AreEqual(first, _hud.RegisteredCount, "rebuilding the peek and sheet must not leak registrations");
        }

        // Owner's phone test, 2026-10-28, item 6: the sheet's portrait flickered -- it was a new image on
        // every 4 Hz refresh, loading its picture again each time.
        [UnityTest]
        public IEnumerator PhoneTheSheetsPortraitIsKeptAcrossRefreshes() {
            _hud.Show(HudModels.Idle());
            _hud.ShowSheet(true);
            yield return Settle();
            // By its class, which the old sheet's portrait had too: the old code fails on the refresh, not the lookup.
            VisualElement head = _hud.Root.Q(className: "ec-phone-sheet__head");
            Assert.IsNotNull(head, "the sheet has a portrait");
            string address = ((BakAgain.UI.ArchiveImage)head).address;
            for (int i = 0; i < 5; i++) {
                _hud.Show(HudModels.Idle());
            }
            yield return Settle();
            Assert.AreSame(head, _hud.Root.Q(className: "ec-phone-sheet__head"), "the same portrait, not a new one per refresh");
            Assert.AreEqual(address, ((BakAgain.UI.ArchiveImage)head).address, "its picture not reassigned");

            // Review M1: into a foe's turn the open sheet shows no hero, and no stale face either.
            _hud.Show(HudModels.TheirTurn());
            Assert.IsNull(((BakAgain.UI.ArchiveImage)head).address, "no hero, no face");
        }

        // Item 8: Thrust / Swing were hard to switch -- a press held across a refresh lost its button.
        [UnityTest]
        public IEnumerator PhoneTheSwitchButtonsSurviveARefresh_SoAHeldPressStillSwitches() {
            _hud.Show(HudModels.Forecast());
            yield return Settle();
            Button swing = _hud.Root.Q<Button>("ec-phone-switch-Swing");
            Assert.IsNotNull(swing);
            ForecastAction? pressed = null;
            _hud.SwitchPressed += a => pressed = a;
            _hud.Show(HudModels.Forecast());   // the 4 Hz refresh, mid-press
            _hud.Show(HudModels.Forecast());
            Assert.AreSame(swing, _hud.Root.Q<Button>("ec-phone-switch-Swing"), "the button pressed is still there");
            using (var click = NavigationSubmitEvent.GetPooled()) {
                click.target = swing;
                swing.SendEvent(click);
            }
            Assert.AreEqual(ForecastAction.Swing, pressed, "and its release switches");
            Assert.GreaterOrEqual(swing.worldBound.height / _hud.Scale, 44f - 0.5f, "a 44 pt target");
        }

        // Item 7: the sheet was a fixed 500 pt with its four numbers spread across it.
        [UnityTest]
        public IEnumerator PhoneTheSheetIsAsWideAsItsContent_UpToItsCap_ItsTextLargerThanTheDocks() {
            _hud.Show(HudModels.Idle());
            _hud.ShowSheet(true);
            yield return Settle();
            VisualElement sheet = _hud.Root.Q("ec-phone-sheet");
            float scale = _hud.Scale;
            Assert.LessOrEqual(sheet.worldBound.width / scale, _layout.PhoneSheetWidthPt + 0.5f, "capped");
            Assert.Less(sheet.worldBound.width / scale, 400f, "not the old 500 pt");
            // Every line of the sheet, the weapon's numbers line too (review M5), at the sheet's own size.
            foreach (Label l in sheet.Query<Label>().ToList().Where(l => HudModels.Visible(l) && !string.IsNullOrEmpty(l.text))) {
                Assert.GreaterOrEqual(l.resolvedStyle.fontSize / scale, _layout.PhoneSheetTextPt - 0.05f, $"'{l.text}'");
            }
        }

        // Owner, 2026-10-29: the crossed-blade reach marks go, on the chips as on the board's plates.
        // Owner, 2026-10-29: the side bar's "#" sat under the sentence box; the toggle lives in the camera column now.
        [UnityTest]
        public IEnumerator PhoneTheGridToggleIsInTheCameraColumn_ReachableWhereItIs_AndToggles() {
            BakAgain.UI.InputCore.TouchInputState keep = BakAgain.UI.InputCore.TouchInputState.Instance;
            BakAgain.UI.InputCore.TouchInputState.Instance = new BakAgain.UI.InputCore.TouchInputState();
            try {
                _hud.Show(HudModels.Idle());
                yield return Settle();
                VisualElement toggle = _hud.Root.Q("ec-cam-grid");
                Assert.IsNotNull(toggle, "under Reset");
                Assert.IsTrue(_hud.Root.Q("ec-phone-cam").Contains(toggle), "in the camera column");
                VisualElement picked = _hud.Root.panel.Pick(toggle.worldBound.center);
                Assert.IsTrue(picked == toggle || toggle.Contains(picked), "nothing lies over it: a tap reaches it");
                Assert.GreaterOrEqual(toggle.worldBound.height / _hud.Scale, 44f - 0.5f, "a 44 pt target");
                using (var click = NavigationSubmitEvent.GetPooled()) {
                    click.target = toggle;
                    toggle.SendEvent(click);
                }
                Assert.IsTrue(BakAgain.UI.InputCore.TouchInputState.Instance.TakeGridToggle(), "it toggles the grid, as the G key does");
            } finally {
                BakAgain.UI.InputCore.TouchInputState.Instance = keep;
            }
        }

        // Owner, 2026-10-29: "explain the move+attack combo".
        [UnityTest]
        public IEnumerator PhoneASentenceSaysAThrustWalksFirst_NotWhenTheFoeIsBeside() {
            _hud.Show(HudModels.WalkingForecast());
            yield return Settle();
            StringAssert.StartsWith("Walks 3, then thrusts", _hud.Root.Q<Label>("ec-phone-sentence-text").text);
            _hud.Show(HudModels.Forecast());   // beside the foe: no walk
            yield return Settle();
            StringAssert.DoesNotContain("Walks", _hud.Root.Q<Label>("ec-phone-sentence-text").text);
        }

        [UnityTest]
        public IEnumerator PhoneFoeChipsCarryNoSwordMarks() {
            _hud.Show(HudModels.Forecast());
            yield return Settle();
            Assert.IsNull(_hud.Root.Q(className: "ec-marks"), "no blades on the foe chips");
            Assert.IsNull(_hud.Root.Q(className: "ec-mark"));
        }

        [UnityTest]
        public IEnumerator PhoneDocksTakeTheirTaps_TheFreeBoardPassesThrough() {
            _hud.Show(HudModels.Forecast());
            yield return Settle();
            foreach (string dock in new[] { "ec-phone-peek", "ec-phone-grid", "ec-phone-top", "ec-phone-sentence" }) {
                Rect r = _hud.Root.Q(dock).worldBound;
                Assert.IsTrue(HudModels.PickHitsHud(_hud.Root, r.center), $"{dock} takes a tap at its centre");
            }
            Rect grid = _hud.Root.Q("ec-phone-grid").worldBound;
            Rect tile = _hud.Root.Q("ec-tile-AutoResolve").worldBound;
            Assert.IsTrue(HudModels.PickHitsHud(_hud.Root, new Vector2(tile.xMax + 1, grid.center.y)),
                "a gap between grid tiles is not the board");
            Rect top = _hud.Root.Q("ec-phone-top").worldBound;
            Rect peek = _hud.Root.Q("ec-phone-peek").worldBound;
            Assert.IsFalse(HudModels.PickHitsHud(_hud.Root, new Vector2(peek.center.x, (top.yMax + peek.yMin) / 2)),
                "the free board between the docks is tapped through the HUD");
        }

        [UnityTest]
        public IEnumerator PhoneShowsTheRound() {
            _hud.Show(HudModels.Idle());
            yield return Settle();
            StringAssert.Contains("round 2", _hud.Root.Q<Label>("ec-phone-sentence-ends").text);
        }

        [UnityTest]
        public IEnumerator PhoneConfirmIsEmptyWithoutAForecast_LabelledWithOne() {
            _hud.Show(HudModels.Idle());
            yield return Settle();
            Button confirm = _hud.Root.Q<Button>("ec-phone-confirm");
            Assert.IsFalse(confirm.enabledSelf, "nothing to commit");
            Assert.AreEqual(UiTemplates.Format(UiTemplates.HudConfirmEmpty), _hud.Root.Q<Label>("ec-phone-confirm-detail").text);

            int pressed = 0;
            _hud.ConfirmPressed += () => pressed++;
            _hud.Show(HudModels.SwingForecast());
            yield return Settle();
            Assert.IsTrue(confirm.enabledSelf);
            Assert.AreEqual("Swing ▶", _hud.Root.Q<Label>("ec-phone-confirm-title").text);
            Assert.AreEqual("−1 St · ends turn", _hud.Root.Q<Label>("ec-phone-confirm-detail").text);
            using (var click = NavigationSubmitEvent.GetPooled()) {
                click.target = confirm;
                confirm.SendEvent(click);
            }
            Assert.AreEqual(1, pressed, "Confirm raises its event");
        }
    }

    /// <summary>Which HUD is up in a fight, and the Classic control (TASK-972 phase 1, Task 8).</summary>
    public class EnhancedCombatHudSwitchTests {
        private GameObject _host;
        private VisualElement _root;
        private VisualElement _classic;
        private EnhancedCombatHudHost _hudHost;
        private bool _enhanced, _fullScreen, _combat;

        [SetUp]
        public void SetUp() {
            _enhanced = GameOptions.Enhanced;
            _fullScreen = GameOptions.GetFeature(EnhancedFeature.FullScreenTravel);
            _combat = GameOptions.GetFeature(EnhancedFeature.Combat);
            _root = HudModels.Root(out _host);
            _classic = new VisualElement { name = "classic-combat-menu" };
            _hudHost = new EnhancedCombatHudHost(_root, new EnhancedCombatLayout(), () => false);
        }

        [TearDown]
        public void TearDown() {
            GameOptions.Enhanced = _enhanced;
            GameOptions.SetFeature(EnhancedFeature.FullScreenTravel, _fullScreen);
            GameOptions.SetFeature(EnhancedFeature.Combat, _combat);
            Object.DestroyImmediate(_host);
        }

        [Test]
        public void TurningEnhancedOffMidFightRevertsAtTheNextTurn() {
            object owyn = "Owyn, round 2", brigand = "Brigand A, round 2";
            _hudHost.Sync(true, true, owyn, () => HudModels.Idle(), _classic);
            Assert.IsTrue(_hudHost.Active);
            Assert.IsTrue(_hudHost.Hud.Shown, "the Enhanced HUD is up");
            Assert.AreEqual(DisplayStyle.None, _classic.style.display.value, "the Classic menu is hidden");

            _hudHost.Sync(false, true, owyn, () => HudModels.Idle(), _classic);
            Assert.IsTrue(_hudHost.Active, "switched off mid-turn: this turn stays Enhanced");
            Assert.IsTrue(_hudHost.Hud.Shown);

            _hudHost.Sync(false, true, brigand, () => HudModels.TheirTurn(), _classic);
            Assert.IsFalse(_hudHost.Active, "the next turn is Classic");
            Assert.IsFalse(_hudHost.Hud.Shown);
            Assert.AreEqual(StyleKeyword.Null, _classic.style.display.keyword, "the Classic menu is back");
        }

        [Test]
        public void ClassicCombatHudUnchanged() {
            _hudHost.Sync(false, true, "Owyn, round 1", () => HudModels.Idle(), _classic);
            Assert.IsFalse(_hudHost.Active);
            Assert.IsNull(_hudHost.Hud, "no Enhanced HUD is ever built");
            Assert.AreEqual(StyleKeyword.Null, _classic.style.display.keyword, "the Classic menu is untouched");
            Assert.IsNull(_root.Q("ec-desk"));

            // Out of a fight nothing is up either, whatever the switch says.
            _hudHost.Sync(true, false, null, () => null, _classic);
            Assert.IsFalse(_hudHost.Active);
            Assert.IsNull(_hudHost.Hud);
        }

        [Test]
        public void EnhancedCombatImpliesTheFullScreenViewEvenWithFullScreenTravelOff() {
            GameOptions.Enhanced = true;
            GameOptions.SetFeature(EnhancedFeature.FullScreenTravel, false);
            GameOptions.SetFeature(EnhancedFeature.Combat, true);
            var go = new GameObject("EnhancedCombatFullScreen");
            go.SetActive(false);
            try {
                InGameScreen screen = go.AddComponent<InGameScreen>();
                screen.SetInCombatPredicate(() => true);
                Assert.IsTrue(screen.WantsFullScreen, "a fight with Enhanced combat is full-screen");
                GameOptions.SetFeature(EnhancedFeature.Combat, false);
                Assert.IsFalse(screen.WantsFullScreen, "Classic combat keeps the framed arena");
                screen.SetInCombatPredicate(() => false);
                Assert.IsFalse(screen.WantsFullScreen, "travel follows its own switch");
            } finally {
                Object.DestroyImmediate(go);
            }
        }
    }
}
