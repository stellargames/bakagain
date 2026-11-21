namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using BakAgain.Tests.TestSupport;
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.UI.InputCore;
    using BakAgain.UI.Inventory;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Object;
    using GameData.Resources.World;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// Leaving the loot screen settles the container it was opened on — the Unity counterpart of
    /// <c>actorspawn_destroy_and_persist</c>, which every world-container handler calls the moment
    /// <c>cmbinv_inventory_screen_run</c> returns (WCURSOR.C:213/252/1054). A self-spawning bag
    /// looted dry there goes back to RES_FREE and out of the visible pool; anything else is left
    /// exactly as it is.
    ///
    /// <para>Regression guard: the release existed (<c>GroundContainerPool.ReleaseIfEmpty</c>) but
    /// only the discard path called it, so a bag you emptied by picking its contents up stayed on
    /// the ground forever — un-openable (the loot gate needs items), respawned by every zone build,
    /// written into saves, and holding its pool slot hostage.</para>
    /// </summary>
    public class InventoryMenuExitSettleTests {
        private const int BagX = 669600;
        private const int BagY = 1064800;

        private GameObject _go;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        /// <summary>Records what the screen asks the world to remove, so the test can tell "the
        /// record was freed" from "the thing the player is looking at went away".</summary>
        private sealed class RecordingBagSpawner : BakAgain.World.IGroundBagSpawner {
            public readonly List<RuntimeContainer> Despawned = new List<RuntimeContainer>();
            public readonly List<RuntimeContainer> Spawned = new List<RuntimeContainer>();

            public UniTask SpawnAsync(RuntimeContainer bag) {
                Spawned.Add(bag);
                return UniTask.CompletedTask;
            }

            public void Despawn(RuntimeContainer bag) => Despawned.Add(bag);
        }

        private static SaveGameContainerData FreeSlot() =>
            new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 255, minChapter: 0, maxChapter: 10,
                    worldItemId: 0, x: 0, y: 0, actorNumber: 0),
                SaveGameContainerType.Free, numberOfItems: 0, capacity: 20,
                dataTypes: SaveGameContainerDataType.Timestamp | SaveGameContainerDataType.SelfSpawn,
                items: new SaveGameInventoryItemData[20],
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: 0, globalStateIndex: null);

        private static SaveGameContainerData Corpse() =>
            new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 1, minChapter: 0, maxChapter: 10,
                    worldItemId: 195, x: BagX, y: BagY, actorNumber: 0),
                SaveGameContainerType.Corpse, numberOfItems: 0, capacity: 4,
                dataTypes: SaveGameContainerDataType.Timestamp,
                items: new SaveGameInventoryItemData[4],
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: 0, globalStateIndex: null);

        private static GameSession SessionWith(params SaveGameContainerData[] containers) {
            var session = new GameSession();
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(1, 0, containers),
            }), chapter: 1);
            session.CurrentZone = 1;
            return session;
        }

        // An InventoryMenu wired to a real UIDocument (so ScreenBase's fade has a root to work on)
        // with the inert collaborators from InventoryMenuStubs.cs. The fade is zeroed so HideAsync
        // completes inside the test's own frame.
        private InventoryMenu BuildMenu(GameSession session, BakAgain.World.IGroundBagSpawner bags,
            InputLayerStack inputStack = null, NoOpDialogs dialogs = null) {
            _go = new GameObject("InventoryMenuUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            document.panelSettings = _panelSettings;

            var menu = _go.AddComponent<InventoryMenu>();
            menu.Construct(session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                dialogs ?? new NoOpDialogs(), inputStack, bags);
            typeof(BakAgain.UI.Navigation.ScreenBase)
                .GetField("_fadeSeconds", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .SetValue(menu, 0f);
            return menu;
        }

        /// <summary>The bug this file exists for: take everything out of a dropped bag, leave the
        /// screen, and the bag must be gone — record freed, spot empty, entity despawned.</summary>
        [UnityTest]
        public IEnumerator LeavingTheScreen_FreesAndDespawnsABagLootedEmpty() {
            GameSession session = SessionWith(FreeSlot());
            GroundDropTarget target = session.ResolveGroundDropTarget(1, BagX, BagY);
            RuntimeContainer bag = target.Container;
            bag.Items.Add(new RuntimeItem(48, 1, 0));

            var bags = new RecordingBagSpawner();
            InventoryMenu menu = BuildMenu(session, bags);
            menu.SetContainer(bag, WorldEntityType.Bag);

            bag.Items.Clear(); // the player picked the pile up

            yield return ScreenLeave.LeaveAsync(menu).ToCoroutine();

            Assert.AreEqual(SaveGameContainerType.Free, bag.ContainerType,
                "an emptied self-spawning bag returns to the zone's pool");
            Assert.IsNull(session.GetLiveContainerAt(1, BagX, BagY),
                "the spot must stop answering as a container");
            CollectionAssert.Contains(bags.Despawned, bag, "its world entity must be removed");
        }

        /// <summary>Exit or Escape with an item still examined ends the examine too. It used to stay
        /// open across the close, so the pack reopened with the bag for the key ring and, in the
        /// Enhanced layout, "Back to the pack" under a pack already shown.</summary>
        [UnityTest]
        public IEnumerator LeavingTheScreen_EndsAnOpenExamine() {
            GameSession session = SessionWith(FreeSlot());
            InventoryMenu menu = BuildMenu(session, new RecordingBagSpawner());
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            System.Reflection.FieldInfo open = typeof(InventoryMenu).GetField("_inspectOpen", flags);
            System.Reflection.FieldInfo slot = typeof(InventoryMenu).GetField("_inspectSlot", flags);
            open.SetValue(menu, true); // as ShowItemDescriptionAsync leaves it while the examine is up
            slot.SetValue(menu, 2);

            yield return ScreenLeave.LeaveAsync(menu).ToCoroutine();

            Assert.IsFalse((bool)open.GetValue(menu), "the examine must not outlive the screen");
            Assert.AreEqual(-1, (int)slot.GetValue(menu));
        }

        /// <summary>The next shop opens at the top of its shelf: a scroller left from the last visit
        /// was read back over SetShop's reset on the first render.</summary>
        [UnityTest]
        public IEnumerator LeavingTheScreen_ForgetsTheShelfScroll() {
            GameSession session = SessionWith(FreeSlot());
            InventoryMenu menu = BuildMenu(session, new RecordingBagSpawner());
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            System.Reflection.FieldInfo shelf = typeof(InventoryMenu).GetField("_shelf", flags);
            shelf.SetValue(menu, new ShelfScroller(new VisualElement(), new VisualElement(), 5, 2, offset: 300f));

            yield return ScreenLeave.LeaveAsync(menu).ToCoroutine();

            Assert.IsNull(shelf.GetValue(menu), "the shelf must not outlive the screen");
        }

        /// <summary>A bag you only partly emptied is still a pile: it stays claimed and on screen.</summary>
        [UnityTest]
        public IEnumerator LeavingTheScreen_KeepsABagThatStillHoldsSomething() {
            GameSession session = SessionWith(FreeSlot());
            GroundDropTarget target = session.ResolveGroundDropTarget(1, BagX, BagY);
            RuntimeContainer bag = target.Container;
            bag.Items.Add(new RuntimeItem(48, 1, 0));
            bag.Items.Add(new RuntimeItem(90, 1, 0));

            var bags = new RecordingBagSpawner();
            InventoryMenu menu = BuildMenu(session, bags);
            menu.SetContainer(bag, WorldEntityType.Bag);

            bag.Items.RemoveAt(0);

            yield return ScreenLeave.LeaveAsync(menu).ToCoroutine();

            Assert.AreEqual(SaveGameContainerType.Bag, bag.ContainerType);
            Assert.AreSame(bag, session.GetLiveContainerAt(1, BagX, BagY));
            CollectionAssert.IsEmpty(bags.Despawned);
        }

        /// <summary>An emptied corpse is not self-spawning, so the original leaves the record alone —
        /// the body stays lying there. Only <c>flags &amp; 0x80</c> records are freed.</summary>
        [UnityTest]
        public IEnumerator LeavingTheScreen_LeavesAnEmptiedCorpseWhereItIs() {
            GameSession session = SessionWith(Corpse());
            RuntimeContainer corpse = session.GetLiveContainerAt(1, BagX, BagY);
            Assert.NotNull(corpse, "fixture: the corpse must be at the test spot");

            var bags = new RecordingBagSpawner();
            InventoryMenu menu = BuildMenu(session, bags);
            menu.SetContainer(corpse, WorldEntityType.Corpse);

            corpse.Items.Clear();

            yield return ScreenLeave.LeaveAsync(menu).ToCoroutine();

            Assert.AreEqual(SaveGameContainerType.Corpse, corpse.ContainerType);
            Assert.AreSame(corpse, session.GetLiveContainerAt(1, BagX, BagY));
            CollectionAssert.IsEmpty(bags.Despawned);
        }

        /// <summary>TASK-507: discarding a STACK waits on the quantity picker. The bag used to be
        /// settled as the picker opened, when it was still empty, so it was freed and the stack then
        /// moved into a Free record nothing indexes: gone from the pack and from the world. The
        /// original runs the picker inside cmbinv_actor_pickup_item and settles after.</summary>
        [UnityTest]
        [Timeout(15000)]
        public IEnumerator DiscardingAStack_KeepsTheBagUntilThePickerHasAnswered() =>
            UniTask.ToCoroutine(async () => {
                const byte RationsId = 72;
                GameSession session = SessionWith(FreeSlot());
                session.SetObjectInfo(new ObjectInfoSet("O", new List<ObjectInfo> {
                    new ObjectInfo("r") {
                        Number = RationsId, Name = "Rations", InventorySlots = 1,
                        ObjectType = GameData.ObjectType.Food, Flags = ObjectFlags.B8000,
                    },
                }));
                session.PositionX = BagX;
                session.PositionY = BagY;

                var bags = new RecordingBagSpawner();
                var stack = new InputLayerStack();
                InventoryMenu menu = BuildMenu(session, bags, stack);
                var pack = new RuntimeContainer { Capacity = 24, ContainerType = SaveGameContainerType.Inventory };
                pack.Items.Add(new RuntimeItem(RationsId, 6, 0));
                const System.Reflection.BindingFlags Private =
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                typeof(InventoryMenu).GetField("_displayed", Private).SetValue(menu, pack);
                typeof(InventoryMenu).GetField("_stage", Private).SetValue(menu, _go.GetComponent<UIDocument>().rootVisualElement);

                typeof(InventoryMenu).GetMethod("DiscardToGround", Private).Invoke(menu, new object[] { 0 });
                Assert.IsNotNull(stack.Top, "fixture: a countable stack must raise the picker");

                stack.DispatchIntent(UiIntent.Activate()); // accept the full stack
                await UniTask.Yield();
                await UniTask.Yield();

                RuntimeContainer bag = session.GetLiveContainerAt(1, BagX, BagY);
                Assert.IsNotNull(bag, "the bag must still stand at the party's spot");
                Assert.AreEqual(SaveGameContainerType.Bag, bag.ContainerType);
                Assert.AreEqual(1, bag.Items.Count);
                Assert.AreEqual(RationsId, bag.Items[0].ObjectId);
                Assert.AreEqual(6, bag.Items[0].Variable);
                CollectionAssert.IsEmpty(pack.Items, "the stack left the pack");
                CollectionAssert.Contains(bags.Spawned, bag, "and appeared in the world");
            });

        /// <summary>The spare-bag line is read before the item moves: dialog_play_record blocks, and
        /// only then does the pickup (and a stack's quantity picker) run (CMBINV.C:1064-1072). The
        /// port fired the line and opened the picker under it (TASK-1129 row 3).</summary>
        [UnityTest]
        [Timeout(15000)]
        public IEnumerator TheSpareBagLineIsReadBeforeTheQuantityPicker() =>
            UniTask.ToCoroutine(async () => {
                const byte RationsId = 72;
                GameSession session = SessionWith(FreeSlot());
                session.SetObjectInfo(new ObjectInfoSet("O", new List<ObjectInfo> {
                    new ObjectInfo("r") {
                        Number = RationsId, Name = "Rations", InventorySlots = 1,
                        ObjectType = GameData.ObjectType.Food, Flags = ObjectFlags.B8000,
                    },
                }));
                session.PositionX = BagX;
                session.PositionY = BagY;
                var line = new HeldLineDialogs();
                var stack = new InputLayerStack();
                InventoryMenu menu = BuildMenu(session, new RecordingBagSpawner(), stack, line);
                var pack = new RuntimeContainer { Capacity = 24, ContainerType = SaveGameContainerType.Inventory };
                pack.Items.Add(new RuntimeItem(RationsId, 6, 0));
                const System.Reflection.BindingFlags Private =
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                typeof(InventoryMenu).GetField("_displayed", Private).SetValue(menu, pack);
                typeof(InventoryMenu).GetField("_stage", Private).SetValue(menu, _go.GetComponent<UIDocument>().rootVisualElement);

                typeof(InventoryMenu).GetMethod("DiscardToGround", Private).Invoke(menu, new object[] { 0 });
                await UniTask.Yield();
                Assert.IsTrue(line.Shown, "the control: the spare-bag line is up");
                Assert.IsNull(stack.Top, "no picker while the line is read");

                line.Dismiss();
                await UniTask.Yield();
                await UniTask.Yield();
                Assert.IsNotNull(stack.Top, "the picker after it");
                stack.DispatchIntent(UiIntent.Activate());
                await UniTask.Yield();
            });

        /// <summary>After a discard the dropped bag is the screen's other inventory: the window shows the
        /// bag and opens it, and the next drop on the window goes straight into that bag
        /// (g_other_inventory_actor, CMBINV.C:1064-1072, 385-392). The port kept the keyring
        /// (TASK-1129 row 2).</summary>
        [UnityTest]
        [Timeout(15000)]
        public IEnumerator ADiscardMakesTheBagTheWindowsInventory() =>
            UniTask.ToCoroutine(async () => {
                const byte A = 48, B = 49;
                GameSession session = SessionWith(FreeSlot());
                session.SetObjectInfo(new ObjectInfoSet("O", new List<ObjectInfo> {
                    new ObjectInfo("a") { Number = A, Name = "A", InventorySlots = 1, ObjectType = GameData.ObjectType.Misc },
                    new ObjectInfo("b") { Number = B, Name = "B", InventorySlots = 1, ObjectType = GameData.ObjectType.Misc },
                }));
                session.PositionX = BagX;
                session.PositionY = BagY;
                InventoryMenu menu = BuildMenu(session, new RecordingBagSpawner(), new InputLayerStack());
                var pack = new RuntimeContainer { Capacity = 24, ContainerType = SaveGameContainerType.Inventory };
                pack.Items.Add(new RuntimeItem(A, 1, 0));
                pack.Items.Add(new RuntimeItem(B, 1, 0));
                const System.Reflection.BindingFlags Private =
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                typeof(InventoryMenu).GetField("_displayed", Private).SetValue(menu, pack);
                typeof(InventoryMenu).GetField("_stage", Private).SetValue(menu, _go.GetComponent<UIDocument>().rootVisualElement);

                typeof(InventoryMenu).GetMethod("DiscardToGround", Private).Invoke(menu, new object[] { 0 });
                await UniTask.Yield();
                await UniTask.Yield();
                RuntimeContainer bag = session.GetLiveContainerAt(1, BagX, BagY);
                Assert.IsNotNull(bag, "the control: the bag stands");

                Assert.AreSame(bag, typeof(InventoryMenu).GetField("_container", Private).GetValue(menu),
                    "the window's inventory is now the bag");
                Assert.AreEqual(ContainerImage.IndexForWorldEntityType(WorldEntityType.Bag),
                    typeof(InventoryMenu).GetProperty("EffectiveContainerImageIndex", Private).GetValue(menu),
                    "and the window shows it");
            });

        private sealed class HeldLineDialogs : NoOpDialogs {
            private readonly UniTaskCompletionSource<int> _held = new();
            public bool Shown;
            public override UniTask<int> ShowById(int id, System.Threading.CancellationToken ct = default) {
                Shown = true;
                return _held.Task;
            }
            public void Dismiss() => _held.TrySetResult(0);
        }

        /// <summary>
        /// A refused drop is read before the grid comes back: the original shows the line blocking
        /// with the item still lifted and returns after it (INVENTOR.C:741-751; TASK-1129 row 4). The
        /// result is therefore awaitable and does not finish while the line is up.
        /// </summary>
        [Test]
        public void ARefusalIsReadBeforeTheDropFinishes() {
            var dialogs = new HeldLineDialogs();
            InventoryMenu menu = BuildMenu(SessionWith(FreeSlot()), new RecordingBagSpawner(), dialogs: dialogs);
            object shown = typeof(InventoryMenu).GetMethod("ShowTransferResult",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(menu, new object[] { GameData.Resources.Inventory.InventoryTransfer.Result.MustKeepEquipped, 0 });
            Assert.IsInstanceOf<UniTask>(shown, "the result can be waited on");
            var task = (UniTask)shown;
            Assert.IsTrue(dialogs.Shown, "control: the line is up");
            Assert.AreEqual(UniTaskStatus.Pending, task.Status, "the drop is not over while it is read");
            dialogs.Dismiss();
            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status, "and ends when it is dismissed");
        }

        /// <summary>
        /// The Classic screen fades in only once its page is built and drawn: the original builds
        /// the page and draws the inventory, then fades the palette in (CMBINV.C:273-279). The port
        /// faded the chrome in ~110 ms before the grid and icons appeared (TASK-1129 row 11).
        /// </summary>
        [UnityTest]
        public IEnumerator TheClassicScreenWaitsForItsPageBeforeFadingIn() => UniTask.ToCoroutine(async () => {
            var stash = BakAgain.Tests.EnhancedPrefsStash.Take();
            try {
                InventoryMenu menu = BuildMenu(SessionWith(FreeSlot()), new RecordingBagSpawner());
                var loader = _go.AddComponent<BakAgain.ResourceManagement.Loaders.UserInterfaceLoader>();
                loader.enabled = false;
                var any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                typeof(InventoryMenu).GetField("_ui", any).SetValue(menu, loader);
                var build = (UniTask)typeof(InventoryMenu).GetMethod("BuildWhileHiddenAsync", any).Invoke(menu, null);
                await UniTask.Yield();
                Assert.AreEqual(UniTaskStatus.Pending, build.Status, "no fade while the page is still building");
                typeof(BakAgain.ResourceManagement.Loaders.UserInterfaceLoader).GetProperty("IsBuilt")
                    .SetValue(loader, true);
                await UniTask.Yield();
                await UniTask.Yield();
                Assert.AreEqual(UniTaskStatus.Succeeded, build.Status, "built: the fade may start");
            } finally {
                stash.Restore();
            }
        });

        /// <summary>Opening a member's own inventory settles nothing — there is no container to
        /// settle, and a party inventory must never be handed to the pool.</summary>
        [UnityTest]
        public IEnumerator LeavingAMemberView_SettlesNothing() {
            GameSession session = SessionWith(FreeSlot());
            var bags = new RecordingBagSpawner();
            InventoryMenu menu = BuildMenu(session, bags);

            yield return ScreenLeave.LeaveAsync(menu).ToCoroutine();

            CollectionAssert.IsEmpty(bags.Despawned);
        }
    }
}
