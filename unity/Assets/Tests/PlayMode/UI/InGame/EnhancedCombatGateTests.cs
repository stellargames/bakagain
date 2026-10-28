namespace BakAgain.Tests.PlayMode.UI.InGame {
    using BakAgain.Core;
    using BakAgain.UI.InGame;
    using NUnit.Framework;
    using UnityEngine;

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
            Assert.That(BakAgain.World.WorldRuntime.ArenaUsesOriginalLens, Is.False,
                "the arena camera takes the travel lens");
        }

        [Test]
        public void WithEnhancedCombatOff_AFightRevertsAsToday() {
            GameOptions.SetFeature(EnhancedFeature.Combat, false);

            Assert.That(_hud.EnhancedCombatActive, Is.False);
            Assert.That(_hud.WantsFullScreen, Is.False);
            Assert.That(BakAgain.World.WorldRuntime.ArenaUsesOriginalLens, Is.True);
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
