namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI.Inventory;
    using Cysharp.Threading.Tasks;
    using GameData;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A combat item used from the pack closes it by itself (TASK-947).
    /// </summary>
    /// <remarks>
    /// <c>itemuse_dispatch_on_target</c> plays the "used" record and returns the item's id in a fight
    /// (ITEMUSE.C:384-385, 486-508), and <c>cmbinv_inventory_screen_run</c> loops only while its result
    /// is negative (CMBINV.C:445), so the screen ends there and the arena picks the target. Live with the
    /// Idol of Lassur: its text, then the arena's "Choose a target" with no pack. The port's pack stayed
    /// open until the player closed it.
    /// </remarks>
    public class CombatItemClosesThePackTests {
        [OneTimeSetUp]
        public void RequireShippedGameData() => ShippedGameData.RequireOrIgnore();

        private const int ButtonUse = 22;
        private const int Idol = 0x0c;
        private const int Pebble = 0x70;

        private sealed class CountingNavigator : BakAgain.UI.Navigation.IScreenNavigator {
            public int Pops;
            public BakAgain.UI.Navigation.IScreen Current => null;
            public UniTask ResetTo(BakAgain.UI.Navigation.IScreen root) => UniTask.CompletedTask;
            public UniTask Push(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;
            public UniTask PushAndWaitAsync(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;
            public UniTask Pop() { Pops++; return UniTask.CompletedTask; }
            public UniTask Replace(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;
            public UniTask Clear() => UniTask.CompletedTask;
        }

        private GameObject _go;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        private (InventoryMenu Menu, CountingNavigator Nav) PackWith(int objectId, ObjectType type) {
            var session = new GameSession();
            session.SetObjectInfo(new ObjectInfoSet("O", new List<ObjectInfo> {
                new ObjectInfo("item") { Number = objectId, ObjectType = type, InventorySlots = 1, MaxAmount = 1 },
            }));
            session.SetActiveParty(1, new byte[] { 0 });
            var pack = new RuntimeContainer { Capacity = 4, ContainerType = SaveGameContainerType.Inventory };
            pack.Items.Add(new RuntimeItem((byte)objectId, 1, 0));
            session.SetActorInventoryForTest(0, pack);

            _go = new GameObject("CombatPackUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            document.panelSettings = _panelSettings;
            var menu = _go.AddComponent<InventoryMenu>();
            var nav = new CountingNavigator();
            menu.Construct(session, new NoOpResources(), nav,
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                new NoOpDialogs());
            menu.SetMember(0);
            menu.InCombat = true;
            typeof(InventoryMenu).GetField("_selectedSlot", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(menu, 0);
            return (menu, nav);
        }

        [UnityTest]
        public IEnumerator UsingACombatItemHandsItToTheArenaAndClosesThePack() {
            (InventoryMenu menu, CountingNavigator nav) = PackWith(Idol, ObjectType.CombatItem);

            menu.PrimaryAction(ButtonUse);
            for (var frame = 0; frame < 30 && nav.Pops == 0; frame++) {
                yield return null;
            }

            Assert.AreEqual(Idol, menu.PendingCombatCommandId, "the arena gets the Idol");
            Assert.AreEqual(1, nav.Pops, "and the pack closes by itself");
        }

        [UnityTest]
        public IEnumerator UsingAnItemTheArenaDoesNotTakeLeavesThePackOpen() {
            // The control: a use that hands the arena nothing keeps the loop going (CMBINV.C:445).
            (InventoryMenu menu, CountingNavigator nav) = PackWith(Pebble, ObjectType.Misc);

            menu.PrimaryAction(ButtonUse);
            for (var frame = 0; frame < 30; frame++) {
                yield return null;
            }

            Assert.AreEqual(GameData.Resources.Combat.CombatItemUse.NoItemUsed, menu.PendingCombatCommandId);
            Assert.AreEqual(0, nav.Pops, "the pack stays open");
        }
    }
}
