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

            Invoke("RunTransfer", 0, target, false);

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
            Invoke("RunTransfer", 99, new RuntimeContainer { Capacity = 4 }, false);

            Assert.That(_session.DialogKeyObjectId, Is.EqualTo(Sword));
        }

        private void SetField(string name, object value) =>
            typeof(InventoryMenu).GetField(name, Flags).SetValue(_menu, value);

        private void Invoke(string method, params object[] args) =>
            typeof(InventoryMenu).GetMethod(method, Flags).Invoke(_menu, args);

        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    }
}
