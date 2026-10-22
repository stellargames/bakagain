namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using BakAgain.Tests.TestSupport;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using NUnit.Framework;
    using System.Reflection;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// A refused transfer names the item it refused — "For a moment he'd thought to discard his
    /// @1..." — and the name is resolved through the engine's <c>nEvtArgItemId</c>. Whoever is about
    /// to run a transfer has to stamp it, or the sentence prints with a hole in it.
    /// </summary>
    [TestFixture]
    public class InventoryTransferNamesTheItemTests {
        // Measures text with the game font, which is built from the player's own files (TASK-662).
        [NUnit.Framework.OneTimeSetUp]
        public void RequireShippedGameData() => ShippedGameData.RequireOrIgnore();

        private GameObject _go;
        private PanelSettings _panelSettings;
        private GameSession _session;
        private InventoryMenu _menu;
        private RuntimeContainer _displayed;

        private const byte Sword = 12;

        [SetUp]
        public void SetUp() {
            _go = new GameObject("InventoryTransferUnderTest");
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

            _displayed = new RuntimeContainer {
                Capacity = 24,
                ContainerType = SaveGameContainerType.Inventory,
            };
            _displayed.Items.Add(new RuntimeItem(Sword, 1, 0));
            SetField("_displayed", _displayed);
            SetField("_stage", document.rootVisualElement);
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        [Test]
        public void AnyTransferNamesTheItemBeforeItCanBeRefused() {
            var target = new RuntimeContainer {
                Capacity = 24,
                ContainerType = SaveGameContainerType.Inventory,
            };

            Invoke("RunTransfer", 0, target, false, false);

            // The stamp is on the SHARED path, so a refusal names the item whichever caller ran it
            // — the portrait drop, the container move, and any later one. It used to be stamped
            // only where discarding does it, which left every other refusal reading "...to discard
            // his , its bulk wearisome".
            Assert.That(_session.DialogKeyObjectId, Is.EqualTo(Sword));
        }

        [Test]
        public void AnEmptySlotLeavesTheLastNameAloneRatherThanBlankingIt() {
            _session.SetDialogKeyObjectId(Sword);

            // Out of range: nothing to name. Stamping a zero here would blank the name for whatever
            // message the caller is about to show instead.
            Invoke("RunTransfer", 99, new RuntimeContainer { Capacity = 4 }, false, false);

            Assert.That(_session.DialogKeyObjectId, Is.EqualTo(Sword));
        }

        // ---- which leaf of the "no room" record a refusal reads (TASK-944) --------------------
        // INVENTOR.C:749: a drop on a PORTRAIT that the member refuses sets Var 0 to whether the
        // SOURCE is a party member's pack (actor->bResidence == 1); INVENTOR.C:775: a drop on the
        // container window sets it to the DESTINATION's type. From a chest the portrait refusal is
        // "Owyn scowled. 'There isn't enough room...'", not "Owyn waved off the ...".

        private static RuntimeContainer FullPack() {
            var pack = new RuntimeContainer { Capacity = 24, ContainerType = SaveGameContainerType.Inventory };
            for (int i = 0; i < 20; i++) pack.Items.Add(new RuntimeItem(Sword, 1, 0));
            return pack;
        }

        private int? NoRoomVar0 => _session.GetGlobalValue(30000); // ItemInspectText.DescriptionGlobalKey

        [Test]
        public void APortraitRefusal_FromAChest_ReadsTheNotAMemberLeaf() {
            _displayed.ContainerType = SaveGameContainerType.Chest;
            Invoke("RunTransfer", 0, FullPack(), false, true);
            Assert.That(NoRoomVar0, Is.EqualTo(0));
        }

        [Test]
        public void APortraitRefusal_FromAMembersPack_ReadsTheMemberLeaf() {
            Invoke("RunTransfer", 0, FullPack(), false, true);
            Assert.That(NoRoomVar0, Is.EqualTo(1));
        }

        [Test]
        public void AContainerWindowRefusal_ReadsTheDestinationsType() {
            var body = new RuntimeContainer { Capacity = 1, ContainerType = SaveGameContainerType.Corpse };
            body.Items.Add(new RuntimeItem(Sword, 1, 0));
            Invoke("RunTransfer", 0, body, false, false);
            Assert.That(NoRoomVar0, Is.EqualTo((int)SaveGameContainerType.Corpse));
        }

        private void SetField(string name, object value) =>
            typeof(InventoryMenu).GetField(name, Flags).SetValue(_menu, value);

        private void Invoke(string method, params object[] args) =>
            typeof(InventoryMenu).GetMethod(method, Flags).Invoke(_menu, args);

        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    }
}
