namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The owner's Enhanced inventory list of 2026-11-01 (TASK-1019): no label for a take from loot,
    /// no "back" hint, the hovered item's name in the bar, a double-click on loot that takes, and
    /// "Get all". Each runs through the menu's own path with the Enhanced layout applied.
    /// </summary>
    public class EnhancedInventoryPolishTests {
        private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly string[] Chrome =
            { "hotspot_2", "hotspot_3", "hotspot_4", "hotspot_32", "hotspot_34", "imagebutton_22", "imagebutton_1" };
        private static readonly Rect ItemBox = new(65f, 66f, 1470f, 726f);
        private const byte Dagger = 90, Rock = 91, Rations = 92, Armour = 93;

        private GameObject _go;
        private PanelSettings _panelSettings;
        private VisualElement _root;
        private GameSession _session;
        private InventoryMenu _menu;
        private EnhancedInventoryLayout _layout;
        private RuntimeContainer _chest;
        private readonly RuntimeContainer[] _packs = new RuntimeContainer[3];

        [SetUp]
        public void SetUp() {
            _session = new GameSession();
            _session.SetObjectInfo(new ObjectInfoSet("O", new List<ObjectInfo> {
                new("dagger") { Number = Dagger, Name = "Rusty Dagger", InventorySlots = 1, MaxAmount = 1 },
                new("rock") { Number = Rock, Name = "Rock", InventorySlots = 1, MaxAmount = 1 },
                new("rations") { Number = Rations, Name = "Rations", InventorySlots = 1, MaxAmount = 30, Flags = (ObjectFlags)0x800 },
                new("armour") { Number = Armour, Name = "Leather", InventorySlots = 1, MaxAmount = 1, ObjectType = GameData.ObjectType.Armor },
            }));
            _session.SetActiveParty(3, new byte[] { 0, 1, 2 });
            for (int i = 0; i < 3; i++) {
                _packs[i] = new RuntimeContainer { Capacity = 24, ContainerType = SaveGameContainerType.Inventory };
                _session.SetActorInventoryForTest(i, _packs[i]);
            }

            _go = new GameObject("EnhancedInventoryUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            document.panelSettings = _panelSettings;
            _menu = _go.AddComponent<InventoryMenu>();
            _menu.Construct(_session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    _session, new BakAgain.Core.Services.GameClock(_session)),
                new NoOpDialogs());
            _root = document.rootVisualElement;
            var reqStage = new VisualElement { name = "BakStage", style = { position = Position.Absolute, width = 1600, height = 1200 } };
            _root.Add(reqStage);
            foreach (string n in Chrome) reqStage.Add(new VisualElement { name = n });
            _layout = new EnhancedInventoryLayout(_root, _session);
            Assert.IsTrue(_layout.Apply(reqStage, ItemBox));
            Set("_enhanced", _layout);

            _chest = new RuntimeContainer { Capacity = 28, ContainerType = SaveGameContainerType.Chest };
            _chest.Items.Add(new RuntimeItem(Dagger, 1, 0));
            Set("_container", _chest);
            Set("_displayed", _chest);
            // One cell over the whole panel, on top, as in InventoryPointerTests: stage-local is panel space.
            _root.Add(new VisualElement {
                name = "item_slot_0",
                style = { position = Position.Absolute, left = 0, top = 0, width = 2000, height = 2000 },
            });
            Set("_stage", _root);
            Call("EnsureStageInput");
            _root.panel.GetType().GetMethod("ValidateLayout", BindingFlags.Public | Any)?.Invoke(_root.panel, null);
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) Object.DestroyImmediate(_go);
            if (_panelSettings != null) Object.DestroyImmediate(_panelSettings);
        }

        private void Set(string field, object v) => typeof(InventoryMenu).GetField(field, Any).SetValue(_menu, v);
        private object Call(string method, params object[] args) => typeof(InventoryMenu).GetMethod(method, Any).Invoke(_menu, args);
        private string Hint => _root.Q<Label>(className: "enhanced-inv__hint").text;
        private Label NameLabel(int portrait) => _layout.Portrait(portrait).Q<Label>(className: "enhanced-inv__name");

        private void Send<T>(EventType type, int clickCount = 1) where T : PointerEventBase<T>, new() {
            var imgui = new Event { type = type, mousePosition = Vector2.zero, button = 0, clickCount = clickCount };
            using T evt = PointerEventBase<T>.GetPooled(imgui);
            evt.target = _root;
            _root.SendEvent(evt);
        }

        private void Click(int clickCount) {
            Send<PointerDownEvent>(EventType.MouseDown, clickCount);
            Send<PointerUpEvent>(EventType.MouseUp, clickCount);
        }

        [Test]
        public void DraggingLootOverAMemberShowsNoLabel_AGiveStillSaysGiveTo() {
            Set("_dragSlot", 0);
            Call("ShowHoverLabel", 1);
            Assert.AreEqual(DisplayStyle.None, NameLabel(1).style.display.value, "a take from loot says nothing");

            // The control: from a pack that is not the hovered member's, the hand-over is labelled.
            var pack = new RuntimeContainer { Capacity = 24, ContainerType = SaveGameContainerType.Inventory };
            pack.Items.Add(new RuntimeItem(Dagger, 1, 0));
            Set("_displayed", pack);
            Set("_hoverLabelSlot", -2);
            Call("ShowHoverLabel", 1);
            Assert.AreEqual(DisplayStyle.Flex, NameLabel(1).style.display.value);
            StringAssert.StartsWith("Give to", NameLabel(1).text);
        }

        [Test]
        public void TheExamineViewLeavesTheBarHintEmpty() {
            _layout.SetHint("something");
            Set("_inspectOpen", true);
            Call("SyncEnhancedChrome");
            Assert.AreEqual(string.Empty, Hint, "no 'Back to the pack'");
        }

        [UnityTest]
        public IEnumerator AWheelScrollRenamesTheItemNowUnderTheStillPointer() {
            // TASK-1045: the wheel moves the list under a pointer that does not move, so no
            // PointerMoveEvent comes; the bar kept the name of the item that had been there.
            _layout.SetHint("Quarrels");
            var imgui = new Event { type = EventType.ScrollWheel, delta = new Vector2(0f, 3f), mousePosition = Vector2.zero };
            using (WheelEvent evt = WheelEvent.GetPooled(imgui)) {
                evt.target = _root;
                _root.SendEvent(evt);
            }
            yield return null;
            yield return null;
            Assert.AreEqual("Rusty Dagger", Hint);
        }

        [Test]
        public void TheMouseOverAnItemNamesItInTheBar_AndNotWhileExamining() {
            Send<PointerMoveEvent>(EventType.MouseMove);
            Assert.AreEqual("Rusty Dagger", Hint);

            Send<PointerLeaveEvent>(EventType.MouseMove);
            Assert.AreEqual(string.Empty, Hint);

            Set("_inspectOpen", true);
            Send<PointerMoveEvent>(EventType.MouseMove);
            Assert.AreEqual(string.Empty, Hint);
        }

        [Test]
        public void ADoubleClickOnLootTakesItIntoThePackBesideIt() {
            Assert.IsTrue((bool)typeof(InventoryMenu).GetProperty("SideBySide", Any).GetValue(_menu),
                "the fixture is the side-by-side loot");
            Set("_lootMember", 1);

            Click(1);
            Assert.AreEqual(1, _chest.Items.Count, "one click only selects");
            Click(2);

            Assert.AreEqual(0, _chest.Items.Count);
            Assert.AreEqual(Dagger, _packs[1].Items[0].ObjectId, "into the member whose pack is shown");
            Assert.IsEmpty(_packs[0].Items);
        }

        [Test]
        public void AShopDoubleClickBuysForTheFirstMemberWithRoom_OrTheFirstMember() {
            for (int i = 0; i < 20; i++) _packs[0].Items.Add(new RuntimeItem(Rock, 1, 0));
            MethodInfo buyer = typeof(InventoryMenu).GetMethod("BuyerFor", Any);
            Assert.AreEqual(1, buyer.Invoke(_menu, new object[] { new RuntimeItem(Dagger, 1, 0) }));

            for (int i = 0; i < 20; i++) {
                _packs[1].Items.Add(new RuntimeItem(Rock, 1, 0));
                _packs[2].Items.Add(new RuntimeItem(Rock, 1, 0));
            }
            Assert.AreEqual(0, buyer.Invoke(_menu, new object[] { new RuntimeItem(Dagger, 1, 0) }),
                "nobody has room: the first member, whose refusal plays");
        }

        [Test]
        public void GetAllHandsArmourToWhoeverWearsTheWorst_NotTheEmptiestPack() {
            // TASK-1048: a 53% armour went to Gorath (wearing 67%, most room) while Locklear wore 17%.
            const ushort worn = (ushort)GameData.ItemFlags.Equipped;
            _chest.Items.Clear();
            _chest.Items.Add(new RuntimeItem(Armour, 53, 0));
            _packs[0].Items.Add(new RuntimeItem(Armour, 17, worn));
            _packs[0].Items.Add(new RuntimeItem(Rock, 1, 0));
            _packs[1].Items.Add(new RuntimeItem(Armour, 50, worn));
            _packs[1].Items.Add(new RuntimeItem(Rock, 1, 0));
            _packs[2].Items.Add(new RuntimeItem(Armour, 67, worn));

            Call("TakeAll");

            Assert.IsEmpty(_chest.Items);
            Assert.IsNotNull(_packs[0].Items.Find(i => i.ObjectId == Armour && i.Variable == 53), "to the 17% wearer");
        }

        [Test]
        public void GetAllShowsOnTheLootScreenAndSharesTheLootAcrossTheParty() {
            _chest.Items.Add(new RuntimeItem(Rations, 6, 0));
            _chest.Items.Add(new RuntimeItem(53, 3, 0)); // three sovereigns
            _packs[0].Items.Add(new RuntimeItem(Rock, 1, 0));
            int gold = _session.PartyGold;

            Call("TakeAll");

            Assert.IsEmpty(_chest.Items);
            Assert.AreEqual(gold + 30, _session.PartyGold, "gold to the purse");
            Assert.AreEqual(Dagger, _packs[1].Items.Find(i => i.ObjectId != Rations).ObjectId,
                "the dagger to the emptiest pack, first in roster order");
            int rations = 0;
            foreach (RuntimeContainer p in _packs) rations += p.Items.Find(i => i.ObjectId == Rations)?.Variable ?? 0;
            Assert.AreEqual(6, rations, "the stack shared");
            Assert.AreEqual(DisplayStyle.Flex, _layout.GetAll.style.display.value, "the tablet shows on the loot screen");
        }
    }
}
