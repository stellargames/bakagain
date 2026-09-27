namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.Tests.TestSupport;
    using System.Collections;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
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
    /// A building whose message opens the inventory opens it on the building's OWN container.
    /// </summary>
    /// <remarks>
    /// WCURSOR.C:340/346/370 — <c>cmbinv_inventory_screen_run(actor, 0, 0)</c>, with <c>actor</c>
    /// the clicked object's record. Tyr-Sog's inn is the case in hand: Geoffrey's "What would you
    /// like?" opened a member's pack because the screen was pushed unbound, and no food could be
    /// bought (TASK-555). The menu starts on a member's pack here for exactly that reason.
    /// </remarks>
    public class BuildingOpensItsOwnContainerTests {
        private const int InnX = 996023;
        private const int InnY = 1133591;

        private sealed class RecordingNavigator : BakAgain.UI.Navigation.IScreenNavigator {
            public BakAgain.UI.Navigation.IScreen Pushed;
            public BakAgain.UI.Navigation.IScreen Current => Pushed;
            public UniTask ResetTo(BakAgain.UI.Navigation.IScreen root) => UniTask.CompletedTask;
            public UniTask Push(BakAgain.UI.Navigation.IScreen screen) {
                Pushed = screen;
                return UniTask.CompletedTask;
            }
            public UniTask PushAndWaitAsync(BakAgain.UI.Navigation.IScreen screen) => Push(screen);
            public UniTask Pop() => UniTask.CompletedTask;
            public UniTask Replace(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;
            public UniTask Clear() => UniTask.CompletedTask;
        }

        private static SaveGameContainerData Inn() {
            var items = new SaveGameInventoryItemData[6];
            items[0] = new SaveGameInventoryItemData(72, 7, 0); // Rations x7
            return new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 1, minChapter: 0, maxChapter: 10,
                    worldItemId: 0, x: InnX, y: InnY, actorNumber: 0),
                SaveGameContainerType.FixedWorldItem, numberOfItems: 1, capacity: 6,
                dataTypes: SaveGameContainerDataType.Dialog | SaveGameContainerDataType.Shop
                    | SaveGameContainerDataType.Timestamp,
                items: items,
                lockData: null,
                dialogData: new SaveGameContainerDialogData(examineMessageIndex: 3,
                    flags: (byte)GameData.Resources.World.FixedObjectClick.OpensInventoryFlag,
                    dialogId: 3100287),
                shopData: new SaveGameContainerShopData(0, 10, 0, 10, 40, 30, 0, 0, 0, 0, 0, 0, 0, 0,
                    GameData
.ShopItemCategories.Miscellaneous),
                encounterData: null, timestamp: 0, globalStateIndex: null);
        }

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator AFlagTwoBuildingOpensTheInventoryOnItsOwnShop() => UniTask.ToCoroutine(async () => {
            var session = new GameSession();
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(1, 0, new[] { Inn() }),
            }), chapter: 1);
            session.CurrentZone = 1;

            var menuGo = new GameObject("InventoryMenuUnderTest");
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            var entity = new GameObject("inn").AddComponent<WorldEntity>();
            try {
                menuGo.AddComponent<UIDocument>().panelSettings = panelSettings;
                var menu = menuGo.AddComponent<InventoryMenu>();
                menu.Construct(session, new Stubs.NoOpResources(), new Stubs.NoOpNavigator(),
                    new BakAgain.Core.Services.DialogExecutor(
                        new Microsoft.Extensions.Logging.Abstractions.NullLogger<
                            BakAgain.Core.Services.DialogExecutor>(),
                        session, new BakAgain.Core.Services.GameClock(session)),
                    new Stubs.NoOpDialogs());

                entity.Behavior = "building";
                entity.EntityType = GameData.Resources.World.WorldEntityType.Building;
                entity.Interaction = new InteractionProfile();
                entity.transform.position = BakCoordinateConverter.ConvertPosition(InnX, InnY, 0);

                var navigator = new RecordingNavigator();
                var handler = new BuildingInteractionHandler(session, new Stubs.NoOpDialogs(), menu,
                    navigator);

                await handler.HandleAsync(entity, isPrimary: true);

                Assert.AreSame(menu, navigator.Pushed, "the click opens the inventory");
                var bound = (RuntimeContainer)typeof(InventoryMenu)
                    .GetField("_container", System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance)
                    .GetValue(menu);
                Assert.IsNotNull(bound, "the screen is bound to a container, not a member's pack");
                Assert.AreSame(session.GetLiveContainerAt(1, InnX, InnY), bound,
                    "the container is the clicked building's own record");
                Assert.IsTrue(bound.IsShop, "a container with a shop subrecord opens as its shop");
            } finally {
                Object.DestroyImmediate(entity.gameObject);
                Object.DestroyImmediate(menuGo);
                Object.DestroyImmediate(panelSettings);
            }
        });

        private const int DwellingX = 991176;
        private const int DwellingY = 677660;
        private const int DwellingMessage = 3100267;   // "...assured that they were alone, went inside"

        private sealed class RecordingDialogs : Stubs.NoOpDialogs {
            public readonly System.Collections.Generic.List<int> Shown = new();
            public override UniTask<int> ShowById(int id, System.Threading.CancellationToken ct = default) {
                Shown.Add(id);
                return UniTask.FromResult(0);
            }
        }

        private static SaveGameContainerData Dwelling() =>
            new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 9, minChapter: 0, maxChapter: 10,
                    worldItemId: 125, x: DwellingX, y: DwellingY, actorNumber: 0),
                SaveGameContainerType.FixedWorldItem, numberOfItems: 0, capacity: 6,
                dataTypes: SaveGameContainerDataType.Dialog | SaveGameContainerDataType.Encounter,
                items: System.Array.Empty<SaveGameInventoryItemData>(),
                lockData: null,
                dialogData: new SaveGameContainerDialogData(examineMessageIndex: 0,
                    flags: (byte)GameData.Resources.World.FixedObjectClick.OpensInventoryFlag,
                    dialogId: DwellingMessage),
                shopData: null,
                encounterData: new SaveGameContainerEncounterData(0, 0, 0, 0, 1, 19, 24),
                timestamp: null, globalStateIndex: null);

        // WCURSOR.C:297-356: a building's message plays AFTER its hotspot dispatch (no shipped record
        // sets MessageFirstFlag), and a dispatch that springs an ambush ends the click before it.
        // The Crystal Grove's Cup dwelling said "finally assured that they were alone" and then
        // attacked; the original opens straight on the ambush's "The door burst open".
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator AnAmbushSpringsBeforeTheBuildingSpeaks() => UniTask.ToCoroutine(async () => {
            foreach (var (trap, speaks) in new[] {
                         (BakAgain.World.Hotspots.HotspotService.TrapDispatch.Blocks, false),
                         (BakAgain.World.Hotspots.HotspotService.TrapDispatch.Proceed, true) }) {
                var session = new GameSession();
                session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                    new SaveGameZoneContainerEntryData(9, 0, new[] { Dwelling() }),
                }), chapter: 8);
                session.CurrentZone = 9;
                session.PositionX = DwellingX;
                session.PositionY = DwellingY + 1200;
                var entity = new GameObject("dwelling").AddComponent<WorldEntity>();
                try {
                    entity.Behavior = "building";
                    entity.EntityType = GameData.Resources.World.WorldEntityType.Building;
                    entity.Interaction = new InteractionProfile();
                    entity.transform.position = BakCoordinateConverter.ConvertPosition(DwellingX, DwellingY, 0);
                    var dialogs = new RecordingDialogs();
                    var handler = new BuildingInteractionHandler(session, dialogs, null,
                        new RecordingNavigator(), fireTrap: (x, y) => trap);

                    await handler.HandleAsync(entity, isPrimary: true);

                    Assert.AreEqual(speaks, dialogs.Shown.Contains(DwellingMessage),
                        trap + ": the message plays only when the hotspot lets the click go on");
                } finally {
                    Object.DestroyImmediate(entity.gameObject);
                }
            }
        });
    }
}
