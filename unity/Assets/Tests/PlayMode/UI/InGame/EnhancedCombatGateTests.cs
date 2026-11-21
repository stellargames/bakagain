namespace BakAgain.Tests.PlayMode.UI.InGame {
    using BakAgain.Core;
    using BakAgain.UI.InGame;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// Enhanced combat (TASK-972): with its switch on a fight keeps the full-screen view and the
    /// travel lens; with it off the fight reverts to the original's framed arena.
    /// </summary>
    [TestFixture]
    public class EnhancedCombatGateTests {
        private bool _enhanced, _fullScreen, _combat;
        private GameObject _go;
        private InGameScreen _hud;

        [SetUp]
        public void SetUp() {
            _enhanced = GameOptions.Enhanced;
            _fullScreen = GameOptions.GetFeature(EnhancedFeature.FullScreenTravel);
            _combat = GameOptions.GetFeature(EnhancedFeature.Combat);
            GameOptions.Enhanced = true;
            GameOptions.SetFeature(EnhancedFeature.FullScreenTravel, true);
            _go = new GameObject("EnhancedCombatGate");
            _go.SetActive(false);
            _hud = _go.AddComponent<InGameScreen>();
            _hud.SetInCombatPredicate(() => true);
        }

        [TearDown]
        public void TearDown() {
            GameOptions.Enhanced = _enhanced;
            GameOptions.SetFeature(EnhancedFeature.FullScreenTravel, _fullScreen);
            GameOptions.SetFeature(EnhancedFeature.Combat, _combat);
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        [Test]
        public void WithEnhancedCombatOn_AFightKeepsFullScreenAndTheTravelLens() {
            GameOptions.SetFeature(EnhancedFeature.Combat, true);

            Assert.That(_hud.EnhancedCombatActive, Is.True);
            Assert.That(_hud.WantsFullScreen, Is.True);
            Assert.That(World().ArenaUsesOriginalLens, Is.False,
                "the arena camera takes the travel lens");
        }

        [Test]
        public void WithEnhancedCombatOff_AFightRevertsAsToday() {
            GameOptions.SetFeature(EnhancedFeature.Combat, false);

            Assert.That(_hud.EnhancedCombatActive, Is.False);
            Assert.That(_hud.WantsFullScreen, Is.False);
            Assert.That(World().ArenaUsesOriginalLens, Is.True);
        }

        private BakAgain.World.WorldRuntime World() {
            var world = new BakAgain.World.WorldRuntime(null, new GameSession(), null, null, null, null, null);
            world.SetCombatScreenForTest(_hud);
            return world;
        }

        [Test]
        public void TheOrbitTheLensAndTheBoardMarksFollowTheHudsPerTurnLatch() {
            // One predicate for "Enhanced combat active" (TASK-972 task 9): switched off mid-turn,
            // camera, lens and marks stay Enhanced with the HUD until the next turn.
            object turn = "Owyn, round 1";
            _hud.CombatHudHost = new BakAgain.UI.Combat.Enhanced.EnhancedCombatHudHost(
                new UnityEngine.UIElements.VisualElement(), new GameData.Resources.Layout.EnhancedCombatLayout());
            _hud.SetEnhancedCombatSeams(_ => null, () => turn, () => null, _ => null);
            BakAgain.World.WorldRuntime world = World();
            GameOptions.SetFeature(EnhancedFeature.Combat, true);
            Assert.That(world.ArenaUsesOrbit, Is.True);

            GameOptions.SetFeature(EnhancedFeature.Combat, false);
            Assert.That(world.ArenaUsesOrbit, Is.True, "this turn stays Enhanced");
            Assert.That(world.ArenaUsesOriginalLens, Is.False);
            var arena = new GameObject("arena");
            var next = new GameObject("arena, next turn");
            try {
                Assert.That(world.AttachBoardMarks(arena), Is.Not.Null, "and so do the marks");
                turn = "Brigand A, round 1";
                Assert.That(world.ArenaUsesOrbit, Is.False, "the next turn is Classic");
                Assert.That(world.AttachBoardMarks(next), Is.Null);
            } finally {
                Object.DestroyImmediate(arena);
                Object.DestroyImmediate(next);
            }
        }

        // Owner's phone test, 2026-10-28: the HUD was up over the entry line, before the fight began.
        [Test]
        public void TheHudWaitsUntilTheFightIsUnderWay_TheClassicMenuIsUntouched([Values(true, false)] bool enhanced) {
            GameOptions.SetFeature(EnhancedFeature.Combat, enhanced);
            var root = new UnityEngine.UIElements.VisualElement();
            var classic = new UnityEngine.UIElements.VisualElement { name = "classic-combat-menu" };
            var host = new BakAgain.UI.Combat.Enhanced.EnhancedCombatHudHost(root,
                new GameData.Resources.Layout.EnhancedCombatLayout(), () => true);
            _hud.CombatHudHost = host;
            _hud.SetEnhancedCombatSeams(_ => null, () => "Owyn, round 1", () => classic, _ => null);
            bool underWay = false;
            _hud.SetFightUnderWayPredicate(() => underWay);

            _hud.SyncCombatHudForTest();
            Assert.IsFalse(host.Hud?.Shown ?? false, "the entry line: no combat HUD yet");

            underWay = true;
            _hud.SyncCombatHudForTest();
            if (enhanced) {
                Assert.IsTrue(host.Hud.Shown, "the first turn is handed out: the HUD is up");
            } else {
                Assert.IsNull(host.Hud, "Classic: no Enhanced HUD is ever built");
                Assert.AreEqual(UnityEngine.UIElements.StyleKeyword.Null, classic.style.display.keyword,
                    "and the Classic menu is untouched");
            }
        }

        [UnityTest]
        public System.Collections.IEnumerator AFightIsUnderWayOnceItsMenuOpens_NotWhenItStarts() {
            var menuObject = new GameObject("CombatMenuUnderTest");
            menuObject.SetActive(false);
            try {
                var menu = menuObject.AddComponent<BakAgain.UI.Combat.CombatMenu>();
                var (s, _) = BakAgain.Tests.Editor.World.Hotspots.EnhancedBoardMarksTests.PartyTurn(combatMenu: () => menu);
                Assert.IsTrue(s.FightInProgress);
                Assert.IsFalse(menu.IsOpen && s.FightUnderWay, "control");
                float until = Time.realtimeSinceStartup + 10f;
                while (Time.realtimeSinceStartup < until && !menu.IsOpen) {
                    Assert.IsFalse(s.FightUnderWay, "not before the menu opens");
                    yield return null;
                }
                Assert.IsTrue(menu.IsOpen, "the opening settle opened the menu");
                Assert.IsTrue(s.FightUnderWay, "now the fight is under way");
            } finally {
                Object.DestroyImmediate(menuObject);
            }
        }

        /// <summary>
        /// Forum p=54: the fight is over in the code (Combat.Leave) while its outcome line and the fade still show
        /// the arena. The Classic arena keeps its framed view until it is left; it jumped to full-screen, a 2x zoom.
        /// </summary>
        [Test]
        public void WithEnhancedCombatOff_TheArenaStaysFramedUntilItIsLeft() {
            GameOptions.SetFeature(EnhancedFeature.Combat, false);
            _hud.SetInCombatPredicate(() => false);
            _hud.SetArenaShownPredicate(() => true);

            Assert.That(_hud.WantsFullScreen, Is.False, "the arena is still on screen");

            _hud.SetArenaShownPredicate(() => false);
            Assert.That(_hud.WantsFullScreen, Is.True, "control: once the arena is gone, travel is full-screen again");
        }

        /// <summary>
        /// TASK-1096: the Enhanced arena is full-window, and with Full-Screen Travel off it shrank
        /// into the frame under the outcome line, because Enhanced combat ends at Combat.Leave.
        /// The original changes the view only under black, when the arena is left.
        /// </summary>
        [Test]
        public void WithEnhancedCombatOn_TheArenaStaysFullWindowUntilItIsLeft() {
            GameOptions.SetFeature(EnhancedFeature.Combat, true);
            GameOptions.SetFeature(EnhancedFeature.FullScreenTravel, false);
            _hud.SetInCombatPredicate(() => false);
            _hud.SetArenaShownPredicate(() => true);

            Assert.That(_hud.WantsFullScreen, Is.True, "the Enhanced arena is still on screen");

            _hud.SetArenaShownPredicate(() => false);
            Assert.That(_hud.WantsFullScreen, Is.False, "control: once it is gone, travel is framed");
        }

        /// <summary>
        /// TASK-1096: under the outcome line the Classic arena keeps its combat frame and the
        /// travel controls stay dead; the port showed the travel chrome and let the party walk.
        /// </summary>
        [Test]
        public void TheArenaOnScreenKeepsTheCombatChromeAndBlocksTravel() {
            GameOptions.SetFeature(EnhancedFeature.Combat, false);
            _hud.SetInCombatPredicate(() => false);
            _hud.SetArenaShownPredicate(() => true);

            Assert.That(_hud.TravelBlockedByTheArena, Is.True, "no walking while the arena shows");
            _hud.RefreshCombatChrome();
            Assert.That(_hud.CombatChromeShown, Is.True, "the combat frame stays up");

            _hud.SetArenaShownPredicate(() => false);
            _hud.RefreshCombatChrome();
            Assert.That(_hud.TravelBlockedByTheArena, Is.False, "control: travel once it is left");
            Assert.That(_hud.CombatChromeShown, Is.False);
        }

        /// <summary>
        /// TASK-1096 row 3: under the outcome line the original redraws the last actor's stat panel
        /// (COMBAT.C:2672, combat_arena_redraw_hud_bufs(0, 1)); the port put the travel portraits there.
        /// </summary>
        [Test]
        public void TheArenaOnScreenHoldsTheLastStatsPanel() {
            _hud.SetInCombatPredicate(() => false);
            _hud.SetArenaShownPredicate(() => true);
            Assert.That(_hud.HoldsTheLastStatsPanel, Is.True, "the fight is over, the arena still shows");

            _hud.SetArenaShownPredicate(() => false);
            Assert.That(_hud.HoldsTheLastStatsPanel, Is.False, "control: the travel heads once it is left");

            _hud.SetInCombatPredicate(() => true);
            Assert.That(_hud.HoldsTheLastStatsPanel, Is.False, "control: a running fight redraws its panel");
        }

        [Test]
        public void WithoutAFight_EnhancedCombatIsNotActive() {
            GameOptions.SetFeature(EnhancedFeature.Combat, true);
            _hud.SetInCombatPredicate(() => false);

            Assert.That(_hud.EnhancedCombatActive, Is.False);
            Assert.That(_hud.WantsFullScreen, Is.True);
        }
    }
}
