namespace BakAgain.Tests.Editor.World.Hotspots {
    using System.Collections;
    using System.Reflection;
    using BakAgain.UI.Combat;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// The shoot menu gives way to the combat menu only after the shot has played: the original
    /// fires (COMBAT.C:2416, a blocking call through the whole animation) and then sets the menu slot
    /// back (:2435). The port swapped menus at the click (TASK-1124 #11).
    /// </summary>
    public class ShootMenuHoldsThroughTheShotTests {
        private GameObject _shootGo, _menuGo;

        [TearDown]
        public void TearDown() {
            if (_shootGo != null) Object.DestroyImmediate(_shootGo);
            if (_menuGo != null) Object.DestroyImmediate(_menuGo);
        }

        [UnityTest]
        public IEnumerator TheShootMenuStaysUpUntilTheArenaSettles() {
            _shootGo = new GameObject("shoot");
            ShootMenu shoot = _shootGo.AddComponent<ShootMenu>();
            _menuGo = new GameObject("combat");
            CombatMenu combat = _menuGo.AddComponent<CombatMenu>();
            _menuGo.SetActive(false);
            bool busy = true;
            var (service, _) = EnhancedBoardMarksTests.PartyTurn(arenaBusy: () => busy,
                shoot: () => shoot, combatMenu: () => combat);

            typeof(BakAgain.World.Hotspots.HotspotService)
                .GetMethod("EndTargetSelection", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(service, null);
            yield return null;
            yield return null;
            Assert.IsTrue(shoot.IsOpen, "the shot is still playing: the shoot menu stays");
            Assert.IsFalse(combat.IsOpen);

            busy = false;
            yield return null;
            yield return null;
            Assert.IsFalse(shoot.IsOpen, "settled: the shoot menu goes");
            Assert.IsTrue(combat.IsOpen, "and the combat menu comes back");
        }
    }
}
