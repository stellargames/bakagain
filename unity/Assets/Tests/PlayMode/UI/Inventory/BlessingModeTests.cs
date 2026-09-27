namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using GameData.Resources.Data;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The temple's blessing is a MODE on the ordinary inventory screen, not a screen of its own —
    /// the original sets <c>g_inventory_screen_mode = 2</c> and runs the same screen.
    /// </summary>
    [TestFixture]
    public class BlessingModeTests {
        private GameObject _go;
        private PanelSettings _panelSettings;
        private GameSession _session;
        private InventoryMenu _menu;

        [SetUp]
        public void SetUp() {
            _go = new GameObject("BlessingModeUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;

            _menu = _go.AddComponent<InventoryMenu>();
            _session = new GameSession();
            _session.Initialize(
                new BakAgain.Tests.Editor.Core.Fixtures.SaveGameBuilder()
                    .WithBackingBody().WithPartyActors()
                    .WithParty(new[] { "A", "B", "C" }, new byte[] { 0, 1, 2 }).Build(),
                GameSessionSource.NewGame);
            _menu.Construct(_session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<
                        BakAgain.Core.Services.DialogExecutor>(),
                    _session, new BakAgain.Core.Services.GameClock(_session)),
                new NoOpDialogs());

            // The blessing screen opens on a member's own pack, so the party needs one. An actor
            // inventory is a container owned by actor number (roster position + 1).
            _session.SetZoneContainersForTest(
                new SaveGameZoneContainerStateData(new[] {
                    new SaveGameZoneContainerEntryData(0, 0, new[] { MemberPack(actorNumber: 1) }),
                }),
                chapter: 1);
        }

        /// <summary>An actor-inventory container, which is how a party member's pack is stored.</summary>
        private static SaveGameContainerData MemberPack(int actorNumber) =>
            new SaveGameContainerData(
                new SaveGameContainerLocationData(0, 0, 9, 0, 0, 0, (short)actorNumber),
                SaveGameContainerType.Inventory, 0, 24, (SaveGameContainerDataType)0,
                new SaveGameInventoryItemData[0], null, null, null, null, null, null);

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        [Test]
        public void ATempleTurnsTheOrdinaryScreenIntoABlessingOne() {
            Assert.That(_menu.IsBlessingMode, Is.False);

            Assert.That(_menu.SetBlessing(Temple(), portraitSlot: 0), Is.True);

            Assert.That(_menu.IsBlessingMode, Is.True);
        }

        [Test]
        public void OpeningOnSomethingElseTakesTheOfferAway() {
            _menu.SetBlessing(Temple(), portraitSlot: 0);

            _menu.SetContainer(new GameData.Resources.Inventory.RuntimeContainer { Capacity = 4 },
                GameData.Resources.World.WorldEntityType.Container);

            // The screen is a singleton: a temple's offer left standing would let a player bless
            // things out of a chest halfway across the world.
            Assert.That(_menu.IsBlessingMode, Is.False);
        }

        [Test]
        public void WithoutATempleThereIsNoOffer() =>
            Assert.That(_menu.SetBlessing(null, portraitSlot: 0), Is.False);

        private static SaveGameContainerShopData Temple() =>
            new SaveGameContainerShopData(1, 25, 60, 3, 65, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                (GameData.ShopItemCategories)0);
    }
}
