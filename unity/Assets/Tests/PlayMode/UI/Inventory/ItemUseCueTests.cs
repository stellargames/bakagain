namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using GameData;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// An item that its own use spends still makes its use sound (forum p=50: the scroll, the Herbal
    /// Pack, the last Aventurine). The original plays <c>wUse_sfx</c> from the record it already holds
    /// and only then removes the item (ITEMUSE.C:491-494); the port looked the sound up in the slot
    /// after the use, where the next item had moved up.
    /// </summary>
    public class ItemUseCueTests {
        private const int LightBowstringId = 77, LightCrossbowId = 30, BowstringCue = 22;
        private const int ShopCue = 0x3c; // BUY: the shop's item flight, INVENTOR.C:565

        private readonly List<int> _cues = new();
        private GameObject _go;
        private PanelSettings _panelSettings;

        [SetUp]
        public void SetUp() {
            _cues.Clear();
            BakAgain.Audio.MenuSoundService.CueRequested += _cues.Add;
        }

        [TearDown]
        public void TearDown() {
            BakAgain.Audio.MenuSoundService.CueRequested -= _cues.Add;
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        /// <summary>
        /// A purchase sounds (forum p=50, "no coin noises"). The original's buy is the ordinary drop
        /// on a portrait, whose item flight plays BUY when the other side is a shop (INVENTOR.C:565,
        /// :754).
        /// </summary>
        [Test]
        public void ABuyPlaysTheShopSound() {
            InventoryMenu menu = Build(out GameSession session, out RuntimeContainer pack);
            session.PartyGold = 1000;
            var shop = new RuntimeContainer { Capacity = 20, ContainerType = SaveGameContainerType.FixedWorldItem };
            var forSale = new RuntimeItem(LightCrossbowId, 50, 0);
            shop.Items.Add(forSale);
            typeof(InventoryMenu).GetField("_displayed", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(menu, shop);

            var buy = (Cysharp.Threading.Tasks.UniTask)typeof(InventoryMenu)
                .GetMethod("CompletePurchaseAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(menu, new object[] {
                    forSale, session.ObjectInfo.GetById(LightCrossbowId), pack, 0, 100L,
                });

            Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded, buy.Status, "control: nothing asked, so the buy ends at once");
            Assert.AreEqual(900, session.PartyGold, "control: the crossbow was paid for");
            Assert.That(_cues, Is.EqualTo(new[] { ShopCue }));
        }

        private sealed class RecordingDialogs : NoOpDialogs {
            public readonly List<int> Shown = new();
            public override Cysharp.Threading.Tasks.UniTask<int> ShowById(int id,
                System.Threading.CancellationToken ct = default) {
                Shown.Add(id);
                return base.ShowById(id, ct);
            }
        }

        /// <summary>
        /// No room, no price: the original checks the buyer's room before it names one and
        /// refuses with 0x1b7748 at Var 0 = 0 (SHOP.C:128-131). The port quoted the price first and
        /// only refused after the offer was accepted (TASK-1093).
        /// </summary>
        [Test]
        public void ABuyWithNoRoomIsRefusedBeforeThePriceIsNamed() {
            var dialogs = new RecordingDialogs();
            InventoryMenu menu = Build(out GameSession session, out RuntimeContainer pack, dialogs);
            pack.Capacity = 1;
            pack.Items.Add(new RuntimeItem(LightBowstringId, 1, 0));   // full by count
            session.PartyGold = 1000;
            var shop = new RuntimeContainer { Capacity = 20, ContainerType = SaveGameContainerType.FixedWorldItem };
            shop.Items.Add(new RuntimeItem(LightCrossbowId, 50, 0));
            const BindingFlags any = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(InventoryMenu).GetField("_displayed", any).SetValue(menu, shop);

            var buy = (Cysharp.Threading.Tasks.UniTask)typeof(InventoryMenu)
                .GetMethod("BuyTransactionAsync", any).Invoke(menu, new object[] { 0, pack, 0 });

            Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded, buy.Status, "control: the buy ends at once");
            Assert.AreEqual(1000, session.PartyGold, "control: nothing was paid");
            CollectionAssert.DoesNotContain(dialogs.Shown, 1800023, "the offer (ddx 1800023) must not be made to a buyer with no room");
        }

        private InventoryMenu Build(out GameSession session, out RuntimeContainer pack,
            BakAgain.UI.IDialogManager dialogs = null) {
            session = new GameSession();
            session.SetObjectInfo(new ObjectInfoSet("O", new List<ObjectInfo> {
                new ObjectInfo("bowstring") {
                    Number = LightBowstringId, Name = "Light Bowstring", InventorySlots = 1,
                    ObjectType = ObjectType.BowString, Flags = ObjectFlags.ConsumedOnUse,
                    SoundId = BowstringCue,
                },
                new ObjectInfo("crossbow") {
                    Number = LightCrossbowId, Name = "Light Crossbow", InventorySlots = 2,
                    ObjectType = ObjectType.Crossbow,
                },
            }));
            pack = new RuntimeContainer { Capacity = 20, ContainerType = SaveGameContainerType.Inventory };

            _go = new GameObject("ItemUseCueUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            document.panelSettings = _panelSettings;
            var menu = _go.AddComponent<InventoryMenu>();
            menu.Construct(session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                dialogs ?? new NoOpDialogs());
            const BindingFlags any = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(InventoryMenu).GetField("_displayed", any).SetValue(menu, pack);
            VisualElement stage = document.rootVisualElement;
            typeof(InventoryMenu).GetField("_stage", any).SetValue(menu, stage);
            // The crossbow's cell is where the bowstring is dropped.
            stage.Add(new VisualElement {
                name = "item_slot_1",
                style = { position = Position.Absolute, left = 0, top = 0, width = 2000, height = 2000 },
            });
            stage.panel.GetType().GetMethod("ValidateLayout",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(stage.panel, null);
            return menu;
        }

        [Test]
        public void ABowstringSpentOnACrossbowStillMakesItsSound() {
            InventoryMenu menu = Build(out _, out RuntimeContainer pack);
            pack.Items.Add(new RuntimeItem(LightBowstringId, 1, 0));
            pack.Items.Add(new RuntimeItem(LightCrossbowId, 50, 0));
            const BindingFlags any = BindingFlags.NonPublic | BindingFlags.Instance;

            bool used = (bool)typeof(InventoryMenu).GetMethod("UseOnItemDrop", any)
                .Invoke(menu, new object[] { 0, Vector2.zero });

            Assert.IsTrue(used, "control: the drop is a use on the crossbow");
            Assert.AreEqual(1, pack.Items.Count, "control: the bowstring was spent");
            Assert.That(_cues, Is.EqualTo(new[] { BowstringCue }),
                "the spent bowstring's own sound, not the sound of whatever moved into its slot");
        }
    }
}
