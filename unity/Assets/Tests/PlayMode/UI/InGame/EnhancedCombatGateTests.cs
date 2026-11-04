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

        [Test]
        public void WithoutAFight_EnhancedCombatIsNotActive() {
            GameOptions.SetFeature(EnhancedFeature.Combat, true);
            _hud.SetInCombatPredicate(() => false);

            Assert.That(_hud.EnhancedCombatActive, Is.False);
            Assert.That(_hud.WantsFullScreen, Is.True);
        }
    }
}
