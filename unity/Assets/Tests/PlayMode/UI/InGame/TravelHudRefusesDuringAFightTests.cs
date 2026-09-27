namespace BakAgain.Tests.PlayMode.UI.InGame {
    using BakAgain.UI.InGame;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// The travel HUD answers nothing while a fight is up — except the viewport.
    /// </summary>
    /// <remarks>
    /// The HUD stays on screen during a fight and the combat band painted over it is
    /// PickingMode.Ignore, so its hotspots stayed live underneath: a click on what looks like the
    /// combat stats readout went through to a party-portrait hotspot and opened that member's pack
    /// (TASK-590). The original never runs REQ_MAIN during a fight — COMBAT.C:118 loads
    /// combat.dat/shoot.dat and the fight loop polls those — so the page answering nothing is the
    /// faithful behaviour, not a special case.
    /// </remarks>
    [TestFixture]
    public class TravelHudRefusesDuringAFightTests {
        private const int PortraitSlot1 = 2;   // REQ_MAIN party portraits
        private const int Map = 50;
        private const int CastSpell = 46;
        private const int Options = 24;
        private const int WorldViewport = 192;

        private GameObject _go;
        private InGameScreen _hud;
        private bool _inCombat;

        [SetUp]
        public void SetUp() {
            _go = new GameObject("TravelHudUnderTest");
            _go.SetActive(false);   // no Awake/Update: this is the dispatch rule, not the screen
            _hud = _go.AddComponent<InGameScreen>();
            _hud.SetInCombatPredicate(() => _inCombat);
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        [Test]
        public void EveryHudEntryIsRefusedWhileAFightIsUp() {
            _inCombat = true;

            // The portraits are the reported click; the rest were the same bug unreported.
            foreach (int action in new[] {
                         PortraitSlot1, PortraitSlot1 + 1, PortraitSlot1 + 2,
                         Map, CastSpell, Options,
                     }) {
                Assert.That(_hud.RefusedDuringAFight(action), Is.True,
                    $"action {action} still answered during a fight");
            }
        }

        [Test]
        public void TheWorldViewportStaysLiveBecauseTheArenaIsClickedThroughIt() {
            _inCombat = true;

            Assert.That(_hud.RefusedDuringAFight(WorldViewport), Is.False);
        }

        [Test]
        public void OutOfAFightTheHudAnswersNormally() {
            // The positive control: without it this fixture would pass against a guard that
            // refuses everything always.
            _inCombat = false;

            Assert.That(_hud.RefusedDuringAFight(PortraitSlot1), Is.False);
            Assert.That(_hud.RefusedDuringAFight(Map), Is.False);
        }

        [Test]
        public void WithNoPredicateWiredNothingIsRefused() {
            // A bare harness (or a HUD built before the fight seam is wired) must not lock the
            // player out of their own buttons.
            var bare = new GameObject("BareHud");
            bare.SetActive(false);
            try {
                var hud = bare.AddComponent<InGameScreen>();

                Assert.That(hud.RefusedDuringAFight(PortraitSlot1), Is.False);
            } finally {
                Object.DestroyImmediate(bare);
            }
        }
    }
}
