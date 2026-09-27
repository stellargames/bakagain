namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.Tests.TestSupport;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using BakAgain.World;
    using BakAgain.World.Converters;
    using BakAgain.World.Interaction;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using Stubs = BakAgain.Tests.PlayMode.UI.Inventory;

    /// <summary>
    /// A chest says its own message once it has been looted, not before and not never.
    /// </summary>
    /// <remarks>
    /// The chest click (canassa <c>INPUT/WCURSOR.C</c>, the <c>bHandled</c> arm) runs the inventory
    /// screen and then plays SUBREC_INTERACT_MSG as <c>deferred_msg</c> in its cleanup (:702-706).
    /// The port never did, so Moraeulf's VICTORY chest never said "the Waani isn't here", never set
    /// 8110, and Squire Phillip never offered the Waani: chapter 7 could not be finished.
    /// </remarks>
    public class ChestSpeaksAfterItIsLootedTests {
        private const int ChestX = 724000;
        private const int ChestY = 730400;
        private const int VictoryChestMessage = 1900100;

        private static readonly InteractionProfile ChestProfile = new() {
            ActionableContainerTypes = new[] { SaveGameContainerType.Chest },
            OpensLoot = true, HasLock = true,
        };

        private sealed class RecordingDialogs : Stubs.NoOpDialogs {
            public readonly List<int> Shown = new();
            public override UniTask<int> ShowById(int id, System.Threading.CancellationToken ct = default) {
                Shown.Add(id);
                return UniTask.FromResult(0);
            }
        }

        /// <summary>A navigator whose screen stays up until the test closes it.</summary>
        private sealed class GatedNavigator : BakAgain.UI.Navigation.IScreenNavigator {
            public readonly UniTaskCompletionSource Closed = new();
            public BakAgain.UI.Navigation.IScreen Current => null;
            public UniTask ResetTo(BakAgain.UI.Navigation.IScreen root) => UniTask.CompletedTask;
            public UniTask Push(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;
            public UniTask PushAndWaitAsync(BakAgain.UI.Navigation.IScreen screen) => Closed.Task;
            public UniTask Pop() => UniTask.CompletedTask;
            public UniTask Replace(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;
            public UniTask Clear() => UniTask.CompletedTask;
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
        public IEnumerator AnOpenChestSpeaksWhenItsLootScreenCloses() => UniTask.ToCoroutine(async () => {
            var session = new GameSession();
            var chest = new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 7, minChapter: 0, maxChapter: 10,
                    worldItemId: 162, x: ChestX, y: ChestY, actorNumber: 0),
                SaveGameContainerType.Chest, numberOfItems: 0, capacity: 4,
                dataTypes: SaveGameContainerDataType.Dialog,
                items: Array.Empty<SaveGameInventoryItemData>(),
                lockData: null,
                dialogData: new SaveGameContainerDialogData(0, 0, VictoryChestMessage),
                shopData: null, encounterData: null, timestamp: 0, globalStateIndex: null);
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(7, 0, new[] { chest }),
            }), chapter: 7);
            session.CurrentZone = 7;

            _menuGo = new GameObject("InventoryMenuUnderTest");
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _menuGo.AddComponent<UIDocument>().panelSettings = _panelSettings;
            var menu = _menuGo.AddComponent<InventoryMenu>();
            menu.Construct(session, new Stubs.NoOpResources(), new Stubs.NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<
                        BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                new Stubs.NoOpDialogs());

            _entityGo = new GameObject("chest");
            var entity = _entityGo.AddComponent<WorldEntity>();
            entity.Behavior = "container";
            entity.EntityType = GameData.Resources.World.WorldEntityType.Container;
            entity.Interaction = ChestProfile;
            entity.transform.position = BakCoordinateConverter.ConvertPosition(ChestX, ChestY, 0);

            var dialogs = new RecordingDialogs();
            var navigator = new GatedNavigator();
            var handler = new ContainerInteractionHandler(session, dialogs, menu, navigator,
                puzzles: null, clock: null, explode: null);
            handler.HandleAsync(entity, isPrimary: true).Forget();
            await UniTask.Yield();

            Assert.IsFalse(dialogs.Shown.Contains(VictoryChestMessage),
                "control: the message waits for the loot screen -- it is DEFERRED, not a prompt");

            navigator.Closed.TrySetResult();
            await UniTask.Yield();
            await UniTask.Yield();

            Assert.IsTrue(dialogs.Shown.Contains(VictoryChestMessage),
                "the chest says its own message once the loot screen closes (WCURSOR.C:702-706)");
        });
    }
}
