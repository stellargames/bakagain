namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using GameData.Resources.Character;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// During a fight the pack stays on whoever is acting.
    /// </summary>
    /// <remarks>
    /// <c>CMBINV.C:344</c> gates the portrait-switch arm on
    /// <c>(target != memberIdx) &amp;&amp; (g_wInCombatMode == 0)</c>, so the original refuses it
    /// outright mid-combat. The port had no combat gate, so a player could open the acting
    /// character's pack and then rummage through anyone's (TASK-592).
    ///
    /// <para>SelectMember now has three modes — party, lock and combat — which is why the
    /// out-of-combat case is asserted alongside: a gate that refused every switch would pass a test
    /// that only checked the refusal.</para>
    /// </remarks>
    [TestFixture]
    public class CombatInventoryStaysOnTheActorTests {
        private const int PortraitSlot2Action = 3;   // REQ_INV's second portrait

        private GameObject _go;
        private PanelSettings _panelSettings;
        private GameSession _session;
        private InventoryMenu _menu;

        [SetUp]
        public void SetUp() {
            _go = new GameObject("CombatInventoryUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;

            _menu = _go.AddComponent<InventoryMenu>();
            _session = new GameSession();
            _menu.Construct(_session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<
                        BakAgain.Core.Services.DialogExecutor>(),
                    _session, new BakAgain.Core.Services.GameClock(_session)),
                new NoOpDialogs());

            _session.SetActiveParty(3, new byte[] { 0, 1, 2 });
            for (var slot = 0; slot < 3; slot++) {
                _session.SetActorInventoryForTest(slot, new RuntimeContainer {
                    Capacity = 4,
                    ContainerType = SaveGameContainerType.Inventory,
                });
            }
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        [Test]
        public void APortraitClickDuringAFightDoesNotSwitchMember() {
            _menu.SetMember(0);
            RuntimeContainer actingPack = _menu.DisplayedContainer;
            _menu.InCombat = true;

            _menu.PrimaryAction(PortraitSlot2Action);

            Assert.That(_menu.DisplayedContainer, Is.SameAs(actingPack));
            Assert.That(_menu.DisplayedContainer,
                Is.Not.SameAs(_session.GetActorInventory(1)));
        }

        [Test]
        public void OutOfAFightTheSameClickStillSwitches() {
            // The control. Without it a gate that refused every switch would look correct.
            _menu.SetMember(0);
            _menu.InCombat = false;

            _menu.PrimaryAction(PortraitSlot2Action);

            Assert.That(_menu.DisplayedContainer, Is.SameAs(_session.GetActorInventory(1)));
        }

        [Test]
        public void TheGateIsCombat_NotWhetherTheScreenWasOpenedFromOne() {
            // InCombat is cleared when the screen resets, so a pack opened in a fight and re-opened
            // from travel must switch again rather than staying stuck on the last acting member.
            _menu.SetMember(0);
            _menu.InCombat = true;
            _menu.PrimaryAction(PortraitSlot2Action);
            Assert.That(_menu.DisplayedContainer, Is.SameAs(_session.GetActorInventory(0)));

            _menu.InCombat = false;
            _menu.PrimaryAction(PortraitSlot2Action);

            Assert.That(_menu.DisplayedContainer, Is.SameAs(_session.GetActorInventory(1)));
        }
    }
}
