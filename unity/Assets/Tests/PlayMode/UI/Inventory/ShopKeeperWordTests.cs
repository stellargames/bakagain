namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using NUnit.Framework;
    using System.Reflection;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Which word the person behind the counter gets. A dialog that names them says "tavernkeeper"
    /// for an establishment that sells beds and "shopkeeper" otherwise; the flag behind that choice
    /// is the shop screen's to write, and its lifetime is the open screen.
    /// </summary>
    [TestFixture]
    public class ShopKeeperWordTests {
        private GameObject _go;
        private PanelSettings _panelSettings;
        private GameSession _session;
        private InventoryMenu _menu;

        [SetUp]
        public void SetUp() {
            _go = new GameObject("ShopKeeperWordUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            document.panelSettings = _panelSettings;

            _menu = _go.AddComponent<InventoryMenu>();
            _session = new GameSession();
            _menu.Construct(_session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<
                        BakAgain.Core.Services.DialogExecutor>(),
                    _session, new BakAgain.Core.Services.GameClock(_session)),
                new NoOpDialogs());
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        [Test]
        public void AnEstablishmentThatSellsBedsHasATavernkeeper() {
            Assert.That(_menu.SetShop(Stock(), Block(innCostPerNight: 12)), Is.True);

            Assert.That(_session.OpenShopRunsAnInn, Is.True);
        }

        [Test]
        public void AShopThatChargesNothingForABedIsStillAShop() {
            // The nightly COST is the discriminator, not the rest hours: a place that states hours
            // and charges nothing is a shop, which only the disassembly settles.
            Assert.That(_menu.SetShop(Stock(), Block(innCostPerNight: 0)), Is.True);

            Assert.That(_session.OpenShopRunsAnInn, Is.False);
        }

        [Test]
        public void OpeningOnSomethingElseClearsTheLastTavernsAnswer() {
            _menu.SetShop(Stock(), Block(innCostPerNight: 12));

            _menu.SetContainer(Stock(), GameData.Resources.World.WorldEntityType.Container);

            // Left standing, a corpse or a chest would name a keeper who runs an inn — and it would
            // read perfectly. The original clears the flag as the first act of the screen's setup.
            Assert.That(_session.OpenShopRunsAnInn, Is.False);
        }

        /// <summary>
        /// TASK-1152 row 7, a deliberate difference (owner 2026-11-27): a sold stack merges with the
        /// shelf's at once. The original merges only when the screen opens (CMBINV.C:245), so the same
        /// stackable sold twice in one visit shows as two cells there.
        /// </summary>
        [Test]
        public void ASoldStackMergesOnTheShelfAtOnce() {
            _session.SetObjectInfo(new GameData.Resources.Object.ObjectInfoSet("O",
                new System.Collections.Generic.List<GameData.Resources.Object.ObjectInfo> {
                    new("quarrel") { Number = 12, Name = "Quarrels", InventorySlots = 1, MaxAmount = 30,
                        Flags = (GameData.Resources.Object.ObjectFlags)0x800 },
                }));
            RuntimeContainer shop = Stock();
            const BindingFlags any = BindingFlags.NonPublic | BindingFlags.Instance;
            MethodInfo render = typeof(InventoryMenu).GetMethod("RenderCurrent", any);
            Assert.That(_menu.SetShop(shop, Block(innCostPerNight: 0)), Is.True);
            render.Invoke(_menu, null);

            shop.Items.Add(new RuntimeItem(12, 3, 0x2));   // sold, then sold again
            shop.Items.Add(new RuntimeItem(12, 4, 0x2));
            render.Invoke(_menu, null);

            Assert.That(shop.Items.Count, Is.EqualTo(1));
        }

        private static RuntimeContainer Stock() {
            var stock = new RuntimeContainer {
                Capacity = 24,
                ContainerType = SaveGameContainerType.FixedWorldItem,
            };
            stock.Items.Add(new RuntimeItem(12, 1, 0));

            return stock;
        }

        /// <summary>A shop block whose only interesting byte is the nightly rate.</summary>
        private static SaveGameContainerShopData Block(byte innCostPerNight) =>
            new SaveGameContainerShopData(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, innCostPerNight, 0, 0,
                (GameData.ShopItemCategories)0);
    }
}
