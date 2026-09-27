namespace BakAgain.Tests.PlayMode.UI.Inventory {
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
            InputLayerStack inputStack = null) {
            _go = new GameObject("InventoryMenuUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;

            var menu = _go.AddComponent<InventoryMenu>();
            menu.Construct(session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                new NoOpDialogs(), inputStack, bags);
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

            yield return menu.HideAsync().ToCoroutine();

            Assert.AreEqual(SaveGameContainerType.Free, bag.ContainerType,
                "an emptied self-spawning bag returns to the zone's pool");
            Assert.IsNull(session.GetLiveContainerAt(1, BagX, BagY),
                "the spot must stop answering as a container");
            CollectionAssert.Contains(bags.Despawned, bag, "its world entity must be removed");
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

            yield return menu.HideAsync().ToCoroutine();

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

            yield return menu.HideAsync().ToCoroutine();

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

        /// <summary>Opening a member's own inventory settles nothing — there is no container to
        /// settle, and a party inventory must never be handed to the pool.</summary>
        [UnityTest]
        public IEnumerator LeavingAMemberView_SettlesNothing() {
            GameSession session = SessionWith(FreeSlot());
            var bags = new RecordingBagSpawner();
            InventoryMenu menu = BuildMenu(session, bags);

            yield return menu.HideAsync().ToCoroutine();

            CollectionAssert.IsEmpty(bags.Despawned);
        }
    }
}
