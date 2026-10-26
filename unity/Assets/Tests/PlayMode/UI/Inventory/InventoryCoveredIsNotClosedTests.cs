namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A character sheet opened over the pack COVERS it; it does not close it (TASK-925).
    /// </summary>
    /// <remarks>
    /// The original runs the sheet inside the inventory loop (canassa CMBINV.C:338, and the
    /// shop/picklock arm at :409-419), so nothing the visit set up — the shop, the lock, the temple's
    /// offer, the mender's running bill — is touched by it. The port's navigator hides a covered
    /// screen, and the pack's end-of-visit settlement used to run on that hide: back from the sheet,
    /// a shop's shelf was a plain grid and handed its stock out unpaid.
    ///
    /// <para>Each case drives a real <see cref="ScreenNavigator"/>: pack pushed, a screen pushed over
    /// it and popped, then the pack itself popped. The visit must survive the first pop and end on
    /// the second, exactly once.</para>
    /// </remarks>
    [BakAgain.Tests.TestSupport.RequiresShippedGameData]
    public class InventoryCoveredIsNotClosedTests {
        private GameObject _go;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        /// <summary>Stands in for the character sheet: all the navigator needs is a screen.</summary>
        private sealed class Cover : IScreen {
            public UniTask ShowAsync() => UniTask.CompletedTask;
            public UniTask HideAsync() => UniTask.CompletedTask;
        }

        private InventoryMenu Build(GameSession session, BakAgain.Core.Services.GameClock clock = null) {
            _go = new GameObject("InventoryCoveredUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            document.panelSettings = _panelSettings;
            var menu = _go.AddComponent<InventoryMenu>();
            menu.Construct(session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<
                        BakAgain.Core.Services.DialogExecutor>(),
                    session, clock ?? new BakAgain.Core.Services.GameClock(session)),
                new NoOpDialogs(), clock: clock);
            return menu;
        }

        /// <summary>Pack up, sheet over it, sheet closed. Returns the navigator still holding the pack.</summary>
        private static async UniTask<ScreenNavigator> CoverAndUncover(InventoryMenu menu) {
            var nav = new ScreenNavigator();
            await nav.PushUnfaded(menu);
            await nav.PushUnfaded(new Cover());
            await nav.PopUnfaded();
            Assert.AreSame(menu, nav.Current, "the pack is back on top");
            return nav;
        }

        [UnityTest]
        public IEnumerator AShopSurvivesTheSheetAndEndsWithThePack() => UniTask.ToCoroutine(async () => {
            InventoryMenu menu = Build(new GameSession());
            RuntimeContainer shelf = Shelf();
            Assert.That(menu.SetShop(shelf, shelf.Shop), Is.True);

            ScreenNavigator nav = await CoverAndUncover(menu);

            // The measured loss: `_shop` was null here, so the shelf drew unpriced and a drag out of
            // it moved the item with no price asked and no gold taken.
            Assert.That(menu.IsShopMode, Is.True, "still a shop after the sheet");
            Assert.AreSame(shelf, menu.DisplayedContainer, "still showing the shelf");

            await nav.PopUnfaded();
            Assert.That(menu.IsShopMode, Is.False, "leaving the pack ends the visit");
        });

        [UnityTest]
        public IEnumerator ALockKeepsItsCallerWaitingThroughTheSheet() => UniTask.ToCoroutine(async () => {
            var session = new GameSession();
            var pack = new RuntimeContainer { Capacity = 4, ContainerType = SaveGameContainerType.Inventory };
            pack.Items.Add(new RuntimeItem(
                (byte)GameData.Resources.Character.LockPicking.LockpickObjectId, 5, 0));
            session.SetActorInventoryForTest(0, pack);
            session.SetActiveParty(1, new byte[] { 0 });
            InventoryMenu menu = Build(session);
            Assert.That(menu.SetLock(20), Is.True);
            UniTask<bool> outcome = menu.LockOutcomeAsync();

            ScreenNavigator nav = await CoverAndUncover(menu);

            Assert.That(menu.IsLockMode, Is.True, "the lock is still on screen");
            Assert.That(outcome.Status, Is.EqualTo(UniTaskStatus.Pending),
                "the lock's caller is not answered while the player is still at the lock");

            await nav.PopUnfaded();
            Assert.That(outcome.Status, Is.EqualTo(UniTaskStatus.Succeeded), "answered on the way out");
            Assert.That(menu.IsLockMode, Is.False);
        });

        [UnityTest]
        public IEnumerator ABlessingSurvivesTheSheet() => UniTask.ToCoroutine(async () => {
            var session = new GameSession();
            session.Initialize(
                new BakAgain.Tests.Editor.Core.Fixtures.SaveGameBuilder()
                    .WithBackingBody().WithPartyActors()
                    .WithParty(new[] { "A", "B", "C" }, new byte[] { 0, 1, 2 }).Build(),
                GameSessionSource.NewGame);
            session.SetZoneContainersForTest(
                new SaveGameZoneContainerStateData(new[] {
                    new SaveGameZoneContainerEntryData(0, 0, new[] {
                        new SaveGameContainerData(
                            new SaveGameContainerLocationData(0, 0, 9, 0, 0, 0, 1),
                            SaveGameContainerType.Inventory, 0, 24, (SaveGameContainerDataType)0,
                            new SaveGameInventoryItemData[0], null, null, null, null, null, null),
                    }),
                }),
                chapter: 1);
            InventoryMenu menu = Build(session);
            Assert.That(menu.SetBlessing(Temple(), portraitSlot: 0), Is.True);

            ScreenNavigator nav = await CoverAndUncover(menu);

            Assert.That(menu.IsBlessingMode, Is.True, "the temple's offer is still open");

            await nav.PopUnfaded();
            Assert.That(menu.IsBlessingMode, Is.False);
        });

        [UnityTest]
        public IEnumerator TheMenderBillsHisHoursOnceOnTheWayOutNotUnderTheSheet() =>
            UniTask.ToCoroutine(async () => {
                var session = new GameSession();
                var clock = new BakAgain.Core.Services.GameClock(session);
                InventoryMenu menu = Build(session, clock);
                const int Mask = 1;
                // The mender IS the mode (SetRepair would need a hydrated party); the grid shows a pack.
                Field("_displayed").SetValue(menu,
                    new RuntimeContainer { Capacity = 24, ContainerType = SaveGameContainerType.Inventory });
                Field("_mender").SetValue(menu, Temple());
                Field("_menderTimeMask").SetValue(menu, Mask);
                long before = session.GameTimeIn2Seconds;

                ScreenNavigator nav = await CoverAndUncover(menu);

                Assert.That(session.GameTimeIn2Seconds, Is.EqualTo(before), "no hours under the sheet");
                Assert.That(menu.IsRepairMode, Is.True, "still at the mender");

                await nav.PopUnfaded();
                Assert.That(session.GameTimeIn2Seconds - before,
                    Is.EqualTo(GameData.Resources.Shop.ShopRepair.HoursFor(Mask) * BakAgain.Core.Services.GameClock.TicksPerHour),
                    "the visit's hours are paid once, on leaving");
            });

        private static FieldInfo Field(string name) =>
            typeof(InventoryMenu).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

        private static RuntimeContainer Shelf() {
            var shelf = new RuntimeContainer {
                Capacity = 24, ContainerType = SaveGameContainerType.FixedWorldItem, IsShop = true,
            };
            shelf.Items.Add(new RuntimeItem(12, 1, 0));
            shelf.Shop = new SaveGameContainerShopData(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                (GameData.ShopItemCategories)0);
            return shelf;
        }

        private static SaveGameContainerShopData Temple() =>
            new SaveGameContainerShopData(1, 25, 60, 3, 65, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                (GameData.ShopItemCategories)0);
    }
}
