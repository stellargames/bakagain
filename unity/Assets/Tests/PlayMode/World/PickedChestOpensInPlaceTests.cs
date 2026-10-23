namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.Tests.TestSupport;
    using System;
    using System.Collections;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using BakAgain.UI.Navigation;
    using BakAgain.World;
    using BakAgain.World.Converters;
    using BakAgain.World.Interaction;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using Stubs = BakAgain.Tests.PlayMode.UI.Inventory;

    /// <summary>
    /// A chest picked open turns the lock screen into its loot screen in place: no frame of the
    /// world between them (TASK-969).
    /// </summary>
    /// <remarks>
    /// The original runs the picklock screen as the inventory screen over a work actor, and on a
    /// successful drop with an owner (a chest, <c>picklock_screen_run(.., 0, pActor)</c>,
    /// WCURSOR.C:607) swaps that screen's actor for the chest and redraws, without leaving the loop
    /// or fading (CMBINV.C:289-295). The port popped the lock screen back to the travel HUD and then
    /// pushed the loot screen, so the world showed for about half a second between them.
    /// </remarks>
    public class PickedChestOpensInPlaceTests {
        private const int ChestX = 724000;
        private const int ChestY = 730400;
        private const int ChestMessage = 1900100;   // any interact message; no shipped picked chest has one

        private static readonly InteractionProfile ChestProfile = new() {
            ActionableContainerTypes = new[] { SaveGameContainerType.Chest },
            OpensLoot = true, HasLock = true,
        };

        /// <summary>Says yes to the lock prompt and notes every line shown; everything else is inert.</summary>
        private sealed class YesDialogs : Stubs.NoOpDialogs {
            public readonly System.Collections.Generic.List<int> Shown = new();
            public override UniTask<bool> ShowConfirmById(int id, System.Threading.CancellationToken ct = default) =>
                UniTask.FromResult(true);
            public override UniTask<int> ShowById(int id, System.Threading.CancellationToken ct = default) {
                Shown.Add(id);
                return UniTask.FromResult(0);
            }
        }

        /// <summary>The travel HUD under the lock: what showing it means is the world is on screen.</summary>
        private sealed class Hud : IScreen {
            public bool Shown;
            public UniTask ShowAsync() { Shown = true; return UniTask.CompletedTask; }
            public UniTask HideAsync() { Shown = false; return UniTask.CompletedTask; }
        }

        private GameObject _menuGo;
        private GameObject _entityGo;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_entityGo != null) { UnityEngine.Object.DestroyImmediate(_entityGo); }
            if (_menuGo != null) { UnityEngine.Object.DestroyImmediate(_menuGo); }
            if (_panelSettings != null) { UnityEngine.Object.DestroyImmediate(_panelSettings); }
        }

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator APickedChestBecomesItsLootWithNoFrameOfTheWorldBetween() => UniTask.ToCoroutine(async () => {
            var session = new GameSession();
            var chest = new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 7, minChapter: 0, maxChapter: 10,
                    worldItemId: 162, x: ChestX, y: ChestY, actorNumber: 0),
                SaveGameContainerType.Chest, numberOfItems: 0, capacity: 4,
                dataTypes: SaveGameContainerDataType.Lock | SaveGameContainerDataType.Dialog,
                items: Array.Empty<SaveGameInventoryItemData>(),
                lockData: new SaveGameContainerLockData(0, 17, 0, 0),
                dialogData: new SaveGameContainerDialogData(0, 0, ChestMessage), shopData: null, encounterData: null, timestamp: 0,
                globalStateIndex: null);
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(7, 0, new[] { chest }),
            }), chapter: 1);
            session.CurrentZone = 7;
            var pack = new RuntimeContainer { Capacity = 4, ContainerType = SaveGameContainerType.Inventory };
            pack.Items.Add(new RuntimeItem(
                (byte)GameData.Resources.Character.LockPicking.LockpickObjectId, 5, 0));
            session.SetActorInventoryForTest(0, pack);
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetObjectInfo(new GameData.Resources.Object.ObjectInfoSet("O",
                new System.Collections.Generic.List<GameData.Resources.Object.ObjectInfo> {
                    new GameData.Resources.Object.ObjectInfo("picklock") {
                        Number = GameData.Resources.Character.LockPicking.LockpickObjectId,
                        InventorySlots = 1, MaxAmount = 99,
                    },
                }));

            var navigator = new ScreenNavigator();
            var hud = new Hud();
            await navigator.ResetTo(hud);

            _menuGo = new GameObject("InventoryMenuUnderTest");
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _menuGo.AddComponent<UIDocument>().panelSettings = _panelSettings;
            _menuGo.SetActive(false);
            var menu = _menuGo.AddComponent<InventoryMenu>();
            menu.Construct(session, new Stubs.NoOpResources(), navigator,
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<
                        BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                new YesDialogs());

            _entityGo = new GameObject("chest");
            var entity = _entityGo.AddComponent<WorldEntity>();
            entity.Behavior = "container";
            entity.EntityType = GameData.Resources.World.WorldEntityType.Container;
            entity.Interaction = ChestProfile;
            entity.transform.position = BakCoordinateConverter.ConvertPosition(ChestX, ChestY, 0);

            var dialogs = new YesDialogs();
            var handler = new ContainerInteractionHandler(session, dialogs, menu, navigator,
                puzzles: null, clock: null, explode: null);
            handler.HandleAsync(entity, isPrimary: true).Forget();

            float deadline = Time.realtimeSinceStartup + 5f;
            while (!(menu.IsLockMode && _menuGo.activeSelf && !hud.Shown
                     && _menuGo.GetComponent<UIDocument>().rootVisualElement?.style.opacity.value >= 1f)
                   && Time.realtimeSinceStartup < deadline) {
                await UniTask.Yield();
            }
            Assert.IsTrue(menu.IsLockMode && _menuGo.activeSelf, "control: the lock screen is up (faded in)");
            Assert.IsFalse(hud.Shown, "control: the lock covers the world");

            // The drop's verdict, as InventoryMenu.DropOnLock hands it over.
            MethodInfo apply = typeof(InventoryMenu).GetMethod("ApplyLockOutcome",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Type result = apply.GetParameters()[1].ParameterType;
            apply.Invoke(menu, new object[] {
                true, Enum.Parse(result, "Opened"),
                GameData.Resources.Character.LockPicking.LockpickObjectId, 0,
            });

            // Every frame from the verdict until well after the loot is up (the verdict waits
            // PicklockDrop.VerdictDelaySeconds, a pop and a push take two 0.15 s fades each): the
            // world never shows, and the screen never goes down.
            // "Down" includes fading: ScreenBase fades a closing screen's root to 0 before it
            // deactivates and an opening one up from 0, and the world is seen through it.
            VisualElement root = _menuGo.GetComponent<UIDocument>().rootVisualElement;
            int worldFrames = 0, screenDownFrames = 0;
            deadline = Time.realtimeSinceStartup
                + (float)GameData.Resources.Character.PicklockDrop.VerdictDelaySeconds + 2f;
            while (Time.realtimeSinceStartup < deadline) {
                if (hud.Shown) { worldFrames++; }
                root = _menuGo.GetComponent<UIDocument>().rootVisualElement;   // a new one per enable
                if (!_menuGo.activeSelf || root == null || root.style.opacity.value < 1f) {
                    screenDownFrames++;
                }
                await UniTask.Yield();
            }

            Assert.AreEqual(0, worldFrames, "frames that showed the world between the lock and the loot");
            Assert.AreEqual(0, screenDownFrames, "frames with neither the lock nor the loot fully up");
            Assert.AreSame(menu, navigator.Current, "the loot screen is the one still on the stack");
            Assert.AreEqual(SaveGameContainerType.Chest, menu.DisplayedContainer?.ContainerType,
                "the screen now shows the chest");
            Assert.IsTrue(session.GetLiveContainerAt(7, ChestX, ChestY).Unlocked, "the chest is unlocked");

            await navigator.Pop();
            Assert.IsFalse(_menuGo.activeSelf, "Exit closes the loot as usual");

            // *** AND THE CHEST SAYS NOTHING (TASK-974). *** The lock arm leaves bHandled 0
            // (WCURSOR.C:606-607), so neither the second inventory run (:624) nor deferred_msg (:626-629,
            // :706-707) happens: a picked chest's message is not played.
            for (int i = 0; i < 10; i++) { await UniTask.Yield(); }
            Assert.IsFalse(dialogs.Shown.Contains(ChestMessage), "a picked chest plays no message after its loot");
        });
    }
}
