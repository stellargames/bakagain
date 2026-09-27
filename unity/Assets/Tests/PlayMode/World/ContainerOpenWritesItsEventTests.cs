namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.Tests.TestSupport;
    using System;
    using System.Collections;
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
    /// Opening a container writes its game-state event id, through the handler the click uses.
    /// </summary>
    /// <remarks>
    /// <c>cmbinv_inventory_screen_run</c> (canassa <c>SRC/SCREENS/CMBINV.C</c>) writes the
    /// container's <c>wGame_state_event_id</c> as the screen comes up, and that field is our
    /// <see cref="SaveGameContainerEncounterData.GlobalDataKey2"/>. Nothing in the port wrote it, so
    /// six shipped containers never set their story flag — Brother Jeremy withholds Thiful's Bird
    /// Migrations until 56012 is set, which is the chapter-1 case. TASK-560.
    ///
    /// <para><b>This drives the production path, not the write.</b> It goes through
    /// <c>HandleAsync -&gt; Decide -&gt; TryOpenLoot -&gt; WriteOpenEvent</c> — the same chain a
    /// click runs — rather than calling the writer directly, which would prove only that a private
    /// method assigns a global.</para>
    /// </remarks>
    public class ContainerOpenWritesItsEventTests {
        private const int ChestX = 882511;
        private const int ChestY = 656147;
        private const int JeremysBoxFlag = 56012;
        private const int GateOnlyKey = 8086;

        private static readonly InteractionProfile ChestProfile = new() {
            ActionableContainerTypes = new[] { SaveGameContainerType.Chest },
            OpensLoot = true, HasLock = true,
        };

        private static SaveGameContainerData Chest(SaveGameContainerEncounterData encounter) =>
            new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 1, minChapter: 0, maxChapter: 10,
                    worldItemId: 162, x: ChestX, y: ChestY, actorNumber: 0),
                SaveGameContainerType.Chest, numberOfItems: 0, capacity: 4,
                dataTypes: SaveGameContainerDataType.Encounter,
                items: Array.Empty<SaveGameInventoryItemData>(),
                lockData: null, dialogData: null, shopData: null,
                encounterData: encounter, timestamp: 0, globalStateIndex: null);

        private GameObject _menuGo;
        private GameObject _entityGo;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            // Qualified: `using System;` and `using UnityEngine;` together make a bare `Object`
            // ambiguous (CS0104).
            if (_entityGo != null) { UnityEngine.Object.DestroyImmediate(_entityGo); }
            if (_menuGo != null) { UnityEngine.Object.DestroyImmediate(_menuGo); }
            if (_panelSettings != null) { UnityEngine.Object.DestroyImmediate(_panelSettings); }
        }

        private GameSession Open(SaveGameContainerEncounterData encounter) {
            var session = new GameSession();
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(1, 0, new[] { Chest(encounter) }),
            }), chapter: 1);
            session.CurrentZone = 1;

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

            _entityGo = new GameObject("box");
            var entity = _entityGo.AddComponent<WorldEntity>();
            entity.Behavior = "container";
            entity.EntityType = GameData.Resources.World.WorldEntityType.Container;
            entity.Interaction = ChestProfile;
            entity.transform.position = BakCoordinateConverter.ConvertPosition(ChestX, ChestY, 0);

            // Both are null-guarded on this path: the chest carries no puzzle id and no spell timer
            // is consulted, so neither collaborator has to be built for an ordinary open.
            var handler = new ContainerInteractionHandler(session, new Stubs.NoOpDialogs(), menu,
                new Stubs.NoOpNavigator(), puzzles: null, clock: null, explode: null);
            handler.HandleAsync(entity, isPrimary: true).Forget();
            return session;
        }

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator OpeningAContainerSetsItsGameStateEventId() => UniTask.ToCoroutine(async () => {
            GameSession session = Open(new SaveGameContainerEncounterData(0, JeremysBoxFlag, 0, 0, 0, 0, 0));
            await UniTask.Yield();

            Assert.AreEqual(1, session.GetGlobalValue(JeremysBoxFlag) ?? 0,
                "opening the chest writes its wGame_state_event_id, as cmbinv_inventory_screen_run does");
        });

        /// <summary>
        /// Key1 is a gate, not the open event, so a container carrying only Key1 sets nothing.
        /// </summary>
        /// <remarks>
        /// The control that gives the test above its meaning: the model documents Key2 by inheriting
        /// Key1's "the global whose value GATES this encounter", and a zone-7 ScriptedLoot carries
        /// Key1 8086 with no Key2. Reading the wrong word would set the wrong flag and still look
        /// like a pass.
        /// </remarks>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator AContainerWithOnlyAGateKeySetsNothing() => UniTask.ToCoroutine(async () => {
            GameSession session = Open(new SaveGameContainerEncounterData(GateOnlyKey, 0, 0, 0, 0, 0, 0));
            await UniTask.Yield();

            Assert.AreEqual(0, session.GetGlobalValue(GateOnlyKey) ?? 0,
                "Key1 gates the encounter; opening must not write it");
        });
    }
}
