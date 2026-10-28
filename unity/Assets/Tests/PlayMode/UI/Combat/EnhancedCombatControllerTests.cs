namespace BakAgain.Tests.PlayMode.UI.Combat {
    using System.Collections;
    using BakAgain.Core;
    using BakAgain.Tests.Editor.World.Hotspots;
    using BakAgain.UI.Combat;
    using BakAgain.UI.Combat.Enhanced;
    using BakAgain.UI.InGame;
    using BakAgain.World.Hotspots;
    using GameData.Resources.Combat;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using State = BakAgain.UI.Combat.Enhanced.EnhancedCombatController.TurnState;

    /// <summary>
    /// Enhanced combat's turn states and input (TASK-972, phase 1 task 9): one path per action. Every
    /// act goes through HotspotService's own click and command paths, so the assertions read the
    /// fight itself (pools, the turn, what was inspected).
    /// </summary>
    [TestFixture]
    public class EnhancedCombatControllerTests {
        private bool _enhanced, _combat;
        private bool _busy;
        private GameObject _go, _menuGo;

        [SetUp]
        public void SetUp() {
            _enhanced = GameOptions.Enhanced;
            _combat = GameOptions.GetFeature(EnhancedFeature.Combat);
            GameOptions.Enhanced = true;
            GameOptions.SetFeature(EnhancedFeature.Combat, true);
            // The arena "plays" the party's act until released, so the enemies' replies cannot touch
            // the pools before the assertions read them.
            _busy = true;
        }

        [TearDown]
        public void TearDown() {
            GameOptions.Enhanced = _enhanced;
            GameOptions.SetFeature(EnhancedFeature.Combat, _combat);
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_menuGo != null) { Object.DestroyImmediate(_menuGo); }
        }

        // The hero at (4,5); foe A beside him at (5,5), foe B (when asked for) far away at (7,1).
        private (HotspotService S, Combatant Hero, EnhancedCombatController C) Fight(bool phone = false, int enemies = 1, int roll = 0) {
            var (s, hero) = EnhancedBoardMarksTests.PartyTurn(enemies: enemies, roll: roll, arenaBusy: () => _busy,
                dialogs: new BakAgain.Tests.PlayMode.UI.Inventory.NoOpDialogs());
            if (enemies > 1) {
                EnhancedBoardMarksTests.Place(s, hero, (4, 5), 5, (0, 5, 5), (1, 7, 1));
            } else {
                EnhancedBoardMarksTests.Place(s, hero, (4, 5), 5, (0, 5, 5));
            }
            return (s, hero, new EnhancedCombatController(s, s.Combat, null, new EnhancedCombatLayout(), null, phone));
        }

        private static int Pool(Combatant c) => c.Health + c.Stamina;

        private static Combatant Foe(HotspotService s, int i) => s.Combat.Encounter.Enemies[i];

        private static void PointAtFoe(HotspotService s, EnhancedCombatController c, int i) =>
            c.Point(Foe(s, i).X, Foe(s, i).Y, i, false);

        private static bool Spent(Combatant hero) => !hero.CanAct(strict: true);

        [Test]
        public void MouseHoverForecasts_OneClickThrusts() {
            var (s, hero, c) = Fight();
            int pool = Pool(Foe(s, 0));

            PointAtFoe(s, c, 0);

            Assert.AreEqual(State.Forecast, c.State);
            Assert.AreEqual(ForecastAction.Thrust, c.Current().Action, "the default on a foe");
            Assert.IsNotNull(c.Model(2).Alternative, "the Swing column beside it");
            Assert.AreEqual(pool, Pool(Foe(s, 0)), "a hover spends nothing");
            Assert.IsFalse(Spent(hero));

            c.Primary(shift: false);

            Assert.Less(Pool(Foe(s, 0)), pool, "one click thrusts");
            Assert.IsTrue(Spent(hero));
            Assert.IsNull(c.Model(2).Forecast, "Confirm is empty once acted");
        }

        [Test]
        public void ShiftClickSwings_CostsOneStamina() {
            var (s, hero, c) = Fight(roll: 99);
            int stamina = hero.Stamina;
            PointAtFoe(s, c, 0);
            Assert.AreEqual(stamina, hero.Stamina);

            c.Primary(shift: true);

            Assert.AreEqual(stamina - MeleeExchange.SwingCost, hero.Stamina, "a swing is billed whether it lands or not");
            Assert.IsTrue(Spent(hero));
        }

        [Test]
        public void RightClickForecastsInspect_ASecondRightClickInspects_ALeftClickThrusts() {
            var (s, hero, c) = Fight();
            int pool = Pool(Foe(s, 0));
            PointAtFoe(s, c, 0);

            c.Secondary();
            Assert.AreEqual(ForecastAction.Inspect, c.Current().Action);
            Assert.AreEqual(0, c.Current().TargetSlot);
            Assert.IsFalse(Spent(hero), "the first right click only forecasts");
            Assert.IsFalse(s.Combat.Knowledge.Inspected(0));

            c.Secondary();
            Assert.IsTrue(s.Combat.Knowledge.Inspected(0), "the second right click inspects");
            Assert.IsTrue(Spent(hero));
            Assert.AreEqual(pool, Pool(Foe(s, 0)), "an inspect strikes nothing");

            // A left click on a foe with the Inspect forecast up thrusts: a left click never inspects.
            var (s2, hero2, c2) = Fight();
            int pool2 = Pool(Foe(s2, 0));
            PointAtFoe(s2, c2, 0);
            c2.Secondary();
            c2.Primary(shift: false);
            Assert.Less(Pool(Foe(s2, 0)), pool2);
            Assert.IsFalse(s2.Combat.Knowledge.Inspected(0));
            Assert.IsTrue(Spent(hero2));
        }

        [Test]
        public void RightClickOnAnotherFoeMovesTheInspectForecast_NothingSpent() {
            var (s, hero, c) = Fight(enemies: 2);
            PointAtFoe(s, c, 0);
            c.Secondary();
            Assert.AreEqual(0, c.Current().TargetSlot);

            PointAtFoe(s, c, 1);
            c.Secondary();

            Assert.AreEqual(ForecastAction.Inspect, c.Current().Action);
            Assert.AreEqual(1, c.Current().TargetSlot, "B's forecast replaced A's");
            Assert.IsFalse(Spent(hero), "nothing spent");
            Assert.IsFalse(s.Combat.Knowledge.Inspected(0));
            Assert.IsFalse(s.Combat.Knowledge.Inspected(1));
        }

        [Test]
        public void AStoneKeyForecastsFirst_TheSecondPressActs() {
            var (s, hero, c) = Fight();

            Assert.IsTrue(c.Key(CombatCommands.DefendId));
            Assert.AreEqual(ForecastAction.Defend, c.Current().Action);
            Assert.IsFalse(Spent(hero), "the first press forecasts");

            Assert.IsTrue(c.Key(CombatCommands.EscapeId), "Esc backs out of the forecast");
            Assert.AreEqual(State.Ready, c.State);
            Assert.IsFalse(c.Key(CombatCommands.EscapeId), "with nothing up, Esc is the Classic Escape");

            c.Key(CombatCommands.DefendId);
            c.Key(CombatCommands.DefendId);
            Assert.IsTrue(Spent(hero), "the second press acts");
            Assert.IsTrue((hero.Flags & CombatantFlags.Parry) != 0, "and it was Defend");
        }

        [Test]
        public void EnterActsAStoneKeysForecast_AStoneClickActsAtOnce() {
            var (s, hero, c) = Fight();
            c.Key(CombatCommands.DefendId);
            c.Key(0x1c);
            Assert.IsTrue(Spent(hero), "Enter acts the forecast");

            var (s2, hero2, c2) = Fight();
            c2.Stone(CombatCommands.Command.Defend, fromKey: false);
            Assert.IsTrue(Spent(hero2), "a desktop stone click acts");
        }

        [Test]
        public void LeftClickOnYourselfDoesNothing() {
            var (s, hero, c) = Fight();
            c.Point(hero.X, hero.Y, hero.PartySlot, true);

            Assert.IsNull(c.Current());
            c.Primary(shift: false);

            Assert.IsFalse(Spent(hero));
            Assert.AreEqual(State.Ready, c.State);
        }

        [Test]
        public void PhoneTapForecasts_OnlyConfirmActs_ReTapDoesNothing() {
            var (s, hero, c) = Fight(phone: true);
            int pool = Pool(Foe(s, 0));
            PointAtFoe(s, c, 0);
            Assert.IsNull(c.Current(), "a finger has no hover");

            c.Primary(false);
            Assert.AreEqual(ForecastAction.Thrust, c.Current().Action, "a tap forecasts");
            c.Switch();
            Assert.AreEqual(ForecastAction.Swing, c.Current().Action, "the peek's switch");
            c.Primary(false);
            Assert.AreEqual(ForecastAction.Swing, c.Current().Action, "a re-tap changes nothing");
            Assert.IsFalse(Spent(hero), "and acts on nothing");
            Assert.AreEqual(pool, Pool(Foe(s, 0)));

            c.Confirm();
            Assert.IsTrue(Spent(hero), "Confirm acts");
        }

        [Test]
        public void PhoneCancelIsTheOnlyWayBack_EmptyGroundDoesNothing() {
            var (s, hero, c) = Fight(phone: true);
            PointAtFoe(s, c, 0);
            c.Primary(false);

            c.Point(null, null, null, false);   // off the board
            c.Primary(false);
            Assert.AreEqual(ForecastAction.Thrust, c.Current().Action, "tapping nothing keeps the forecast");
            c.Point(0, 0, null, false);         // a walled-off corner: no walk arrives
            c.Primary(false);
            Assert.AreEqual(ForecastAction.Thrust, c.Current().Action, "nor does ground out of reach");

            c.Cancel();
            Assert.IsNull(c.Current());
            Assert.AreEqual(State.Ready, c.State);
            Assert.IsFalse(Spent(hero));
        }

        [Test]
        public void PhoneLongPressOnAFoeForecastsInspect() {
            var (s, hero, c) = Fight(phone: true);
            PointAtFoe(s, c, 0);

            c.LongPress();
            Assert.AreEqual(ForecastAction.Inspect, c.Current().Action);
            Assert.IsFalse(Spent(hero));

            c.Confirm();
            Assert.IsTrue(s.Combat.Knowledge.Inspected(0));
            Assert.IsTrue(Spent(hero));
        }

        [Test]
        public void AHandoffClearsAnOpenForecast() {
            var (s, hero, c) = Fight();
            PointAtFoe(s, c, 0);
            Assert.IsNotNull(c.Model(2).Forecast);

            // The turn ends by another path (here the Classic command): the settle hands off.
            s.IssueCombatCommand(CombatCommands.Command.Defend, CombatCommands.DefendId);

            Assert.IsNull(c.Model(2).Forecast, "the forecast is cleared, Confirm empty");
            Assert.AreNotEqual(State.Forecast, c.State);
        }

        [UnityTest]
        public IEnumerator AnInspectFillsTheCardAndItStaysForTheFight() {
            var (s, hero, c) = Fight();
            PointAtFoe(s, c, 0);
            c.Secondary();
            c.Secondary();
            CombatHudModel inspected = c.Model(2);
            Assert.IsTrue(inspected.Foes[0].Inspected);
            Assert.IsTrue(System.Linq.Enumerable.Any(inspected.Foes[0].Rows, r => r.Known), "the card has values");

            // The enemies act and the turn comes back.
            _busy = false;
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until && !(s.Combat.Encounter.Current?.CanAct(strict: true) == true
                && s.Combat.Encounter.Current.IsPartyMember)) {
                yield return null;
            }
            Assert.IsTrue(s.Combat.Encounter.Current.IsPartyMember, "the next party turn came");

            PointAtFoe(s, c, 0);
            CombatHudModel later = c.Model(2);
            Assert.AreEqual(0, later.CardFoe, "pointing at A shows its card");
            Assert.IsTrue(later.Foes[0].Inspected, "it stays for the fight");
            Assert.IsTrue(System.Linq.Enumerable.Any(later.Foes[0].Rows, r => r.Known));
        }

        [Test]
        public void ClassicCombatClicksUnchanged() {
            GameOptions.SetFeature(EnhancedFeature.Combat, false);
            var (s, hero, c) = Fight(roll: 99);
            _go = new GameObject("ClassicControl");
            _go.SetActive(false);
            var screen = _go.AddComponent<InGameScreen>();
            screen.SetInCombatPredicate(() => true);
            screen.CombatController = c;

            Assert.IsFalse(screen.EnhancedCombatClick(isPrimary: false, Vector2.zero), "the world click keeps the Classic path");
            _menuGo = new GameObject("ClassicMenu");   // active: the menu's Awake makes its logger
            var menu = _menuGo.AddComponent<CombatMenu>();
            CombatCommands.Command? issued = null;
            menu.CommandIssued += (command, _) => issued = command;
            menu.KeyIntercept = key => screen.EnhancedCombatActive && c.Key(key);
            menu.PrimaryAction(CombatCommands.DefendId);
            Assert.AreEqual(CombatCommands.Command.Defend, issued, "D is the Classic Defend at once");

            int stamina = hero.Stamina;
            s.ResolveCombatTargetClick(0, partyMember: false, isPrimary: false);
            Assert.AreEqual(stamina - MeleeExchange.SwingCost, hero.Stamina, "the right click still Swings");

            // Control: with Enhanced combat on the same click and key go to the controller.
            GameOptions.SetFeature(EnhancedFeature.Combat, true);
            issued = null;
            Assert.IsTrue(screen.EnhancedCombatClick(isPrimary: false, Vector2.zero));
            menu.PrimaryAction(CombatCommands.DefendId);
            Assert.IsNull(issued);
        }
    }
}
