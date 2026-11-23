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

        /// <summary>TASK-944 (owner): an item dropped on the side-by-side pack panel lands in the pack;
        /// it does not fly into the portrait. A drop on the portrait itself still flies (the original's).</summary>
        [UnityTest]
        public IEnumerator ADropOnThePackPanelDoesNotFly_ADropOnThePortraitDoes() {
            // Member 1's portrait (hotspot_3), sized: the flight needs somewhere to fly to.
            VisualElement head = _root.Q("hotspot_3");
            _root.Add(head);   // out of the REQ stage, which the Enhanced layout hides
            head.style.position = Position.Absolute;
            head.style.left = 1300; head.style.top = 900; head.style.width = 100; head.style.height = 100;
            yield return null;
            _root.panel.GetType().GetMethod("ValidateLayout", BindingFlags.Public | Any)?.Invoke(_root.panel, null);
            object moved = GameData.Resources.Inventory.InventoryTransfer.Result.Moved;
            Set("_resources", new IconStillLoading());   // a flyer waits for its icon; NoOp has none at once

            void Drop(bool onPackPanel) {
                Set("_dropPortraitSlot", 1);
                Set("_dropObject", new ObjectInfo("dagger") { Number = Dagger, Name = "Rusty Dagger", InventorySlots = 1 });
                Set("_dropStagePos", Vector2.zero);
                Set("_dropOnPackPanel", onPackPanel);
                Call("ShowTransferResult", moved, 0);
            }

            Drop(onPackPanel: false);
            Assert.IsNotNull(_root.Q("drop_flight"), "control: a portrait drop flies");
            _root.Q("drop_flight").RemoveFromHierarchy();

            Drop(onPackPanel: true);
            Assert.IsNull(_root.Q("drop_flight"), "a drop on the pack panel does not fly");
        }

        /// <summary>
        /// A purchase flies the item into the buyer's portrait and a sale flies it into the shop's
        /// window: in the original both are the ordinary drop, whose flight plays 0x3c when the other
        /// side is a shop (INVENTOR.C:752-755, 779-786). The port only played the sound (TASK-1099).
        /// </summary>
        [UnityTest]
        public IEnumerator ABuyFliesToThePortrait_ASaleFliesToTheShopWindow([Values(true, false)] bool sale) {
            const int window = 32;
            VisualElement head = _root.Q("hotspot_3");
            _root.Add(head);
            head.style.position = Position.Absolute;
            head.style.left = 1300; head.style.top = 900; head.style.width = 100; head.style.height = 100;
            VisualElement shop = _root.Q($"hotspot_{window}") ?? new VisualElement { name = $"hotspot_{window}" };
            _root.Add(shop);
            shop.style.position = Position.Absolute;
            shop.style.left = 200; shop.style.top = 900; shop.style.width = 100; shop.style.height = 100;
            yield return null;
            _root.panel.GetType().GetMethod("ValidateLayout", BindingFlags.Public | Any)?.Invoke(_root.panel, null);
            Set("_resources", new IconStillLoading());

            Set("_dropPortraitSlot", sale ? -1 : 1);
            Set("_dropObject", new ObjectInfo("dagger") { Number = Dagger, Name = "Rusty Dagger", InventorySlots = 1 });
            Set("_dropStagePos", Vector2.zero);
            Set("_dropOnContainerWindow", sale);
            Call("FinishShopTransfer", sale);

            Assert.IsNotNull(_root.Q("drop_flight"), sale ? "the sale flies to the shop's window" : "the purchase flies to the portrait");
            _root.Q("drop_flight")?.RemoveFromHierarchy();
        }

        /// <summary>
        /// A put-back into the chest's window flies there, with the drop's sound: INVENTOR.C:779-786
        /// flies the icon to the window on every transfer that moved. The port flew only to a
        /// portrait, so a put-back was silent and nothing moved (TASK-1099).
        /// </summary>
        [UnityTest]
        public IEnumerator APutBackIntoTheContainerWindowFlies() {
            const int window = 32;   // InventoryMenu.WindowContainerImage, the container window
            VisualElement chest = _root.Q($"hotspot_{window}") ?? new VisualElement { name = $"hotspot_{window}" };
            _root.Add(chest);
            chest.style.position = Position.Absolute;
            chest.style.left = 200; chest.style.top = 900; chest.style.width = 100; chest.style.height = 100;
            yield return null;
            _root.panel.GetType().GetMethod("ValidateLayout", BindingFlags.Public | Any)?.Invoke(_root.panel, null);
            Set("_resources", new IconStillLoading());

            Set("_dropPortraitSlot", -1);
            Set("_dropObject", new ObjectInfo("dagger") { Number = Dagger, Name = "Rusty Dagger", InventorySlots = 1 });
            Set("_dropStagePos", Vector2.zero);
            Set("_dropOnContainerWindow", true);
            Call("ShowTransferResult", GameData.Resources.Inventory.InventoryTransfer.Result.Moved, 0);

            Assert.IsNotNull(_root.Q("drop_flight"), "the put-back flies to the chest's window");
            _root.Q("drop_flight")?.RemoveFromHierarchy();
        }

        /// <summary>
        /// The flight takes no input: the original runs its 15 steps in a blocking loop
        /// (INVENTOR.C:565-604, invUi_animateItemFlight @0x56e7f), so a click during it is not read.
        /// The port's flight ran on the panel scheduler and every press was taken (TASK-1097 row 18).
        /// </summary>
        [UnityTest]
        public IEnumerator NoInputIsTakenWhileAnItemIsInFlight() {
            const int window = 32;
            VisualElement chest = _root.Q($"hotspot_{window}") ?? new VisualElement { name = $"hotspot_{window}" };
            _root.Add(chest);
            chest.style.position = Position.Absolute;
            chest.style.left = 200; chest.style.top = 900; chest.style.width = 100; chest.style.height = 100;
            yield return null;
            _root.panel.GetType().GetMethod("ValidateLayout", BindingFlags.Public | Any)?.Invoke(_root.panel, null);
            Set("_resources", new IconStillLoading());
            PropertyInfo blocked = typeof(InventoryMenu).GetProperty("InputHeldByAFlight", Any);
            Assert.IsNotNull(blocked, "the menu says when a flight holds its input");
            Assert.IsFalse((bool)blocked.GetValue(_menu), "control: nothing in the air");

            Set("_dropPortraitSlot", -1);
            Set("_dropObject", new ObjectInfo("dagger") { Number = Dagger, Name = "Rusty Dagger", InventorySlots = 1 });
            Set("_dropStagePos", Vector2.zero);
            Set("_dropOnContainerWindow", true);
            Call("ShowTransferResult", GameData.Resources.Inventory.InventoryTransfer.Result.Moved, 0);

            Assert.IsTrue((bool)blocked.GetValue(_menu), "the item is in the air: no input");
            _root.Q("drop_flight")?.RemoveFromHierarchy();
            Assert.IsFalse((bool)blocked.GetValue(_menu), "landed: input again");
        }

        /// <summary>
        /// A give draws the red ring (HEADS frame 8) on the target head for the whole flight
        /// (INVENTOR.C:144-151 from the loop at :573) and takes it away with the icon (TASK-1129 row 5).
        /// </summary>
        [UnityTest]
        public IEnumerator AGiveRingsTheTargetHeadWhileTheItemFlies() {
            const int target = 3; // portrait 1
            VisualElement head = _root.Q($"hotspot_{target}") ?? new VisualElement { name = $"hotspot_{target}" };
            _root.Add(head);
            head.style.position = Position.Absolute;
            head.style.left = 400; head.style.top = 900; head.style.width = 100; head.style.height = 100;
            yield return null;
            _root.panel.GetType().GetMethod("ValidateLayout", BindingFlags.Public | Any)?.Invoke(_root.panel, null);
            Set("_resources", new IconStillLoading());

            Set("_dropPortraitSlot", 1);
            Set("_dropObject", new ObjectInfo("dagger") { Number = Dagger, Name = "Rusty Dagger", InventorySlots = 1 });
            Set("_dropStagePos", Vector2.zero);
            Set("_dropOnContainerWindow", false);
            Call("FlyDroppedItemTo", target, null);

            Assert.IsNotNull(head.Q("flight_ring"), "the target head is ringed while the item flies");
            _root.Q("drop_flight")?.RemoveFromHierarchy();
            head.Q("flight_ring")?.RemoveFromHierarchy();
        }

        /// <summary>
        /// Keyboard/gamepad activation and the right-click are held during a drop flight too: the
        /// original's flight is a blocking loop (INVENTOR.C:565-604). Only the pointer and the
        /// buttons were gated (TASK-1129 row 12).
        /// </summary>
        [Test]
        public void KeyboardActivationWaitsForTheFlightToLand() {
            var flyer = new VisualElement { name = "drop_flight" };
            _root.Add(flyer);
            Call("ActivateSlot", 0);
            Assert.AreEqual(-1, (int)typeof(InventoryMenu).GetField("_selectedSlot", Any).GetValue(_menu),
                "the item is in the air: Enter selects nothing");
            flyer.RemoveFromHierarchy();
            Call("ActivateSlot", 0);
            Assert.AreEqual(0, (int)typeof(InventoryMenu).GetField("_selectedSlot", Any).GetValue(_menu),
                "control: landed, Enter selects");
        }

        /// <summary>
        /// The hover ring steps once per 9 timer ticks, about 38 ms: invui_portrait_refresh_animated
        /// busy-waits g_nFrameTickCountdown and re-arms it to 9 (INVENTOR.C:152-154). The port stepped
        /// every 0.11 s, about three times slower (TASK-1129 row 6).
        /// </summary>
        [Test]
        public void TheHoverRingStepsEveryNineTicks() {
            long ms = (long)typeof(InventoryMenu).GetField("PulseStepMs", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Assert.That(ms, Is.InRange(37, 39));
        }

        /// <summary>
        /// While an item is examined, any button only ends the examine: the original waits in
        /// dialog_poll_arrow_or_button (DIALOG.C:155-175, INVINSP.C:424-426). A portrait click
        /// switched packs, a right-click on one opened the sheet (TASK-1129 row 10).
        /// </summary>
        [Test]
        public void APortraitClickDuringAnExamineOnlyEndsIt() {
            Set("_inspectOpen", true);
            _menu.PrimaryAction(3);
            Assert.AreSame(_chest, typeof(InventoryMenu).GetField("_displayed", Any).GetValue(_menu),
                "the pack shown is unchanged");
            Assert.IsFalse((bool)typeof(InventoryMenu).GetField("_inspectOpen", Any).GetValue(_menu),
                "the click ended the examine");

            Set("_inspectOpen", true);
            _menu.SecondaryAction(3);
            Assert.IsFalse((bool)typeof(InventoryMenu).GetField("_inspectOpen", Any).GetValue(_menu),
                "a right-click on a portrait ends it too, rather than opening the sheet");

            Set("_inspectOpen", true);
            Call("OnSecondaryPressed", Vector2.zero);
            Assert.IsFalse((bool)typeof(InventoryMenu).GetField("_inspectOpen", Any).GetValue(_menu),
                "and so does a right-click anywhere");
        }

        /// <summary>
        /// The inspect fly-in takes no input either: the original flies the icon in a blocking loop and
        /// reads nothing until the description is up (INVINSP.C:347-348, input from :402). Exit during
        /// the port's fly-in closed the screen and the description then showed over travel
        /// (TASK-1129 row 9).
        /// </summary>
        [Test]
        public void NoInputIsTakenWhileTheInspectViewOpens() {
            PropertyInfo blocked = typeof(InventoryMenu).GetProperty("InputHeldByAFlight", Any);
            Assert.IsFalse((bool)blocked.GetValue(_menu), "control: nothing opening");
            Set("_descriptionOpen", true);
            Set("_inspectOpen", false);
            Assert.IsTrue((bool)blocked.GetValue(_menu), "resolving or flying in: no input");
            Set("_inspectOpen", true);
            Assert.IsFalse((bool)blocked.GetValue(_menu), "the description is up: its click is read again");
            Set("_descriptionOpen", false);
            Set("_inspectOpen", false);
        }

        /// <summary>TASK-944 (owner): the loot shows its item count against the container's capacity,
        /// which binds long before its 28 squares (a body holds 2-8 items).</summary>
        [Test]
        public void TheLootShowsItsItemCountAgainstItsCapacity() {
            _chest.Capacity = 4;
            _chest.Items.Add(new RuntimeItem(Rock, 1, 0));
            Call("RenderLootPeer");
            Label count = _root.Q<Label>("loot_count");
            Assert.IsNotNull(count, "a count on the loot side");
            Assert.AreEqual("2/4", count.text);

            _chest.Items.Add(new RuntimeItem(Rock, 1, 0));
            _chest.Items.Add(new RuntimeItem(Rock, 1, 0));
            Call("RenderLootPeer");
            Assert.AreEqual("4/4", _root.Q<Label>("loot_count").text, "full: the rule refuses a fifth");
        }

        private sealed class IconStillLoading : BakAgain.ResourceManagement.IResourceProviderService {
            public Cysharp.Threading.Tasks.UniTask<T> LoadAssetAsync<T>(object key, object owner) where T : class =>
                new Cysharp.Threading.Tasks.UniTaskCompletionSource<T>().Task;
            public void ReleaseAssets(object owner) { }
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
        public void ShareAllHandsArmourToWhoeverWearsTheWorst_NotTheEmptiestPack() {
            // TASK-1048: a 53% armour went to Gorath (wearing 67%, most room) while Locklear wore 17%.
            const ushort worn = (ushort)GameData.ItemFlags.Equipped;
            _chest.Items.Clear();
            _chest.Items.Add(new RuntimeItem(Armour, 53, 0));
            _packs[0].Items.Add(new RuntimeItem(Armour, 17, worn));
            _packs[0].Items.Add(new RuntimeItem(Rock, 1, 0));
            _packs[1].Items.Add(new RuntimeItem(Armour, 50, worn));
            _packs[1].Items.Add(new RuntimeItem(Rock, 1, 0));
            _packs[2].Items.Add(new RuntimeItem(Armour, 67, worn));

            Call("ShareAll");

            Assert.IsEmpty(_chest.Items);
            Assert.IsNotNull(_packs[0].Items.Find(i => i.ObjectId == Armour && i.Variable == 53), "to the 17% wearer");
        }

        [Test]
        public void ShareAllShowsOnTheLootScreenAndSharesTheLootAcrossTheParty() {
            _chest.Items.Add(new RuntimeItem(Rations, 6, 0));
            _chest.Items.Add(new RuntimeItem(53, 3, 0)); // three sovereigns
            _packs[0].Items.Add(new RuntimeItem(Rock, 1, 0));
            int gold = _session.PartyGold;

            Call("ShareAll");

            Assert.IsEmpty(_chest.Items);
            Assert.AreEqual(gold + 30, _session.PartyGold, "gold to the purse");
            Assert.AreEqual(Dagger, _packs[1].Items.Find(i => i.ObjectId != Rations).ObjectId,
                "the dagger to the emptiest pack, first in roster order");
            int rations = 0;
            foreach (RuntimeContainer p in _packs) rations += p.Items.Find(i => i.ObjectId == Rations)?.Variable ?? 0;
            Assert.AreEqual(6, rations, "the stack shared");
            Assert.AreEqual(DisplayStyle.Flex, _layout.GetAll.style.display.value, "the stone shows on the loot screen");
        }

        /// <summary>TASK-1075 (owner): "Get all" fills the member whose pack is shown, as far as they have
        /// room; the rest stays in the container. "Share all" is the old spread across the party.</summary>
        [Test]
        public void GetAllFillsTheShownMember_WhatDoesNotFitStays() {
            Set("_lootMember", 1);
            _chest.Items.Add(new RuntimeItem(Rations, 6, 0));
            _chest.Items.Add(new RuntimeItem(53, 3, 0)); // three sovereigns
            for (int i = 0; i < 18; i++) _packs[1].Items.Add(new RuntimeItem(Rock, 1, 0));
            _chest.Items.Add(new RuntimeItem(Rock, 1, 0));
            int gold = _session.PartyGold;
            _layout.ShowGetAll(true);
            Assert.AreEqual(DisplayStyle.Flex, _layout.ShareAll.style.display.value, "Share all shows beside it");

            Call("TakeAll");

            Assert.AreEqual(gold + 30, _session.PartyGold, "gold to the purse");
            Assert.IsEmpty(_packs[0].Items, "nothing to the others");
            Assert.IsEmpty(_packs[2].Items);
            Assert.AreEqual(20, _packs[1].Items.Count, "the shown member filled to the 20-slot budget");
            Assert.AreEqual(1, _chest.Items.Count, "what does not fit stays in the chest");
        }
    }
}
