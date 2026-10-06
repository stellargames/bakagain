namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using BakAgain.Tests.TestSupport;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// A portrait click while a lock is up says WHO IS PICKING. It does not open that member's pack.
    /// </summary>
    /// <remarks>
    /// CMBINV.C has two portrait arms. The party-inventory one (:331-345) reassigns <c>actor</c>,
    /// so the grid becomes that member's pack; the shop/picklock one (:409-423) does
    /// <c>memberIdx = target;</c> and nothing else. We ran the first arm in both modes, so clicking
    /// a portrait at a lock swapped the party's picklocks and keys off the screen for that member's
    /// belongings — and, because the attempt was judged against the party's best regardless, the
    /// click could not change who picked either.
    /// </remarks>
    [TestFixture]
    public class LockPickerSelectionTests {
        // Measures text with the game font, which is built from the player's own files (TASK-662).
        [NUnit.Framework.OneTimeSetUp]
        public void RequireShippedGameData() => ShippedGameData.RequireOrIgnore();

        private const int Picker = 1;        // portrait slot the player clicks
        private const int PortraitSlot2 = 3; // the REQ action id for that portrait

        private GameObject _go;
        private PanelSettings _panelSettings;
        private GameSession _session;
        private InventoryMenu _menu;

        [SetUp]
        public void SetUp() {
            _go = new GameObject("LockPickerSelectionUnderTest");
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

            // Three members; member 2 is the best picker, so the screen opens on portrait 2 and a
            // click on portrait 1 is a real change.
            _session.SetActiveParty(3, new byte[] { 0, 1, 2 });
            GiveLockPicking(character: 0, value: 10);
            GiveLockPicking(character: 1, value: 30);
            GiveLockPicking(character: 2, value: 70);
            GiveEveryoneAPack();
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        [Test]
        public void TheScreenOpensOnThePartysBestPicker() {
            Assert.That(_menu.SetLock(20), Is.True);

            Assert.That(_menu.ActivePortraitSlot(), Is.EqualTo(2));
        }

        [Test]
        public void ClickingAPortraitHandsTheJobToThatMember() {
            _menu.SetLock(20);

            _menu.PrimaryAction(PortraitSlot2);

            Assert.That(_menu.ActivePortraitSlot(), Is.EqualTo(Picker));
        }

        [Test]
        public void ClickingAPortraitDoesNotOpenThatMembersInventory() {
            _menu.SetLock(20);
            RuntimeContainer workingSet = _menu.DisplayedContainer;

            _menu.PrimaryAction(PortraitSlot2);

            // The defect: this was that member's pack, which takes the tools being dragged onto the
            // lock off the screen they are dragged from.
            Assert.That(_menu.DisplayedContainer, Is.SameAs(workingSet));
            Assert.That(_menu.DisplayedContainer,
                Is.Not.SameAs(_session.GetActorInventory(Picker)));
        }

        [Test]
        public void OutsideALockAPortraitStillOpensThatMembersInventory() {
            // The other arm, unchanged — one rule must not have been swapped for the other.
            _menu.PrimaryAction(PortraitSlot2);

            Assert.That(_menu.DisplayedContainer,
                Is.SameAs(_session.GetActorInventory(Picker)));
        }

        private void GiveLockPicking(int character, byte value) {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 10, Max = 99 };
            }
            stats[(int)ActorAttribute.LockPicking] = new ActorStat { Base = value, Max = 99 };
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = 99, Max = 99 };
            _session.SetActorStatsForTest(character, stats);
        }

        /// <summary>A pack each, the first holding the picklocks that make SetLock accept.</summary>
        private void GiveEveryoneAPack() {
            for (var slot = 0; slot < 3; slot++) {
                var pack = new RuntimeContainer {
                    Capacity = 4,
                    ContainerType = SaveGameContainerType.Inventory,
                };
                if (slot == 0) {
                    pack.Items.Add(new RuntimeItem((byte)LockPicking.LockpickObjectId, 5, 0));
                }
                _session.SetActorInventoryForTest(slot, pack);
            }
        }
    }
}
