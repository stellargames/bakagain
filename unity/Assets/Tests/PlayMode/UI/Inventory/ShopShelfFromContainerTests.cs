namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// A shelf opened as an ordinary container is still a shop.
    /// </summary>
    /// <remarks>
    /// <b>Shop-ness belongs to the container, not to the screen.</b> The original has no shop mode
    /// to be in: <c>cmbinv_transfer</c> branches on the container carrying a shop subrecord
    /// (<c>actorrec_get_subrecord(.., SUBREC_EVENT_STATE)</c>, CMBINV.C:787-792) and hands off to
    /// <c>shop_npc_transaction</c> there. Its one screen-wide flag, <c>g_bInventoryShopMode</c>, is
    /// the picklock screen's — PICKLOCK.C is its only writer.
    ///
    /// <para>That distinction is not academic here: location action codes 5, 6 and 8 open a
    /// shopkeeper's shelf through <see cref="InventoryMenu.SetContainer"/> (TOWNSCN.C:519/527/544,
    /// three identical arms), and a shelf that arrives with no shop attached hands its stock over
    /// free. Scavenger's Meet — the chapter-1 picklock supplier — is reached exactly that way.</para>
    /// </remarks>
    [TestFixture]
    public class ShopShelfFromContainerTests {
        private GameObject _go;
        private PanelSettings _panelSettings;
        private GameSession _session;
        private InventoryMenu _menu;

        [SetUp]
        public void SetUp() {
            _go = new GameObject("ShopShelfFromContainerUnderTest");
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
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        [Test]
        public void AContainerCarryingAShopBlockOpensAsAShop() {
            _menu.SetContainer(Shelf(), GameData.Resources.World.WorldEntityType.Container);

            // Without this the transfer out of the shelf is a plain loot pickup: no price is
            // quoted, no gold moves, and the player walks off with the stock.
            Assert.That(_menu.IsShopMode, Is.True);
        }

        [Test]
        public void TheShopBlockComesFromTheContainerItself() {
            // Proves the block was adopted, not merely the flag: the keeper's word is read off the
            // nightly rate, which only the container can supply on this path.
            _menu.SetContainer(Shelf(innCostPerNight: 12),
                GameData.Resources.World.WorldEntityType.Container);

            Assert.That(_session.OpenShopRunsAnInn, Is.True);
        }

        [Test]
        public void APlainChestIsNotAShop() {
            _menu.SetShop(Shelf(), Block(innCostPerNight: 0));

            _menu.SetContainer(Chest(), GameData.Resources.World.WorldEntityType.Container);

            // The other half of the rule, and the one the old code got right: a previous shop must
            // not leak into a corpse or a chest, or its contents acquire a price.
            Assert.That(_menu.IsShopMode, Is.False);
        }

        private static RuntimeContainer Chest() {
            var chest = new RuntimeContainer {
                Capacity = 24,
                ContainerType = SaveGameContainerType.FixedWorldItem,
            };
            chest.Items.Add(new RuntimeItem(12, 1, 0));

            return chest;
        }

        private static RuntimeContainer Shelf(byte innCostPerNight = 0) {
            RuntimeContainer shelf = Chest();
            shelf.IsShop = true;
            shelf.Shop = Block(innCostPerNight);

            return shelf;
        }

        /// <summary>A shop block whose only interesting byte is the nightly rate.</summary>
        private static SaveGameContainerShopData Block(byte innCostPerNight) =>
            new SaveGameContainerShopData(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, innCostPerNight, 0, 0,
                (GameData.ShopItemCategories)0);
    }
}
