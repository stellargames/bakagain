namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.Tests.TestSupport;
    using System;
    using System.Collections;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using BakAgain.UI.Navigation;
    using BakAgain.UI.Puzzle;
    using BakAgain.World;
    using BakAgain.World.Converters;
    using BakAgain.World.Interaction;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Scene;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using Stubs = BakAgain.Tests.PlayMode.UI.Inventory;

    /// <summary>
    /// A solved puzzle chest goes from the riddle to its loot through black, with no frame of the
    /// world between them (TASK-973).
    /// </summary>
    /// <remarks>
    /// The original fades the riddle to black as the cipher screen's solved path ends
    /// (CIPHER.C:205-209), and the inventory screen then fades the loot in from black
    /// (CMBINV.C:232-240, :269-271). The port closed the riddle straight onto the world and pushed
    /// the loot with two screen fades, so the world showed between them.
    /// </remarks>
    public class SolvedPuzzleChestGoesThroughBlackTests {
        private const int ChestX = 724000;
        private const int ChestY = 730400;
        private const int Puzzle = 54;   // a shipped wordlock (BARROW, DIAL_Z17)

        private static readonly InteractionProfile ChestProfile = new() {
            ActionableContainerTypes = new[] { SaveGameContainerType.Chest },
            OpensLoot = true, HasLock = true,
        };

        /// <summary>The travel HUD under the riddle: what showing it means is the world is on screen.</summary>
        private sealed class Hud : IScreen {
            public UniTask ShowAsync() => UniTask.CompletedTask;
            public UniTask HideAsync() => UniTask.CompletedTask;
        }

        /// <summary>A black cover that is either up or not, and notes what was under it as it lifted.</summary>
        private sealed class Cover : IScreenFade {
            public bool Up;
            public Func<bool> LootUp;
            public bool LootUpWhenLifted;

            public async UniTask FadeOutAsync() {
                await UniTask.Yield();
                Up = true;
            }

            public UniTask FadeInAsync() {
                LootUpWhenLifted = LootUp();
                Up = false;
                return UniTask.CompletedTask;
            }
        }

        private GameObject _menuGo;
        private GameObject _puzzleGo;
        private GameObject _entityGo;
        private PanelSettings _panelSettings;

        [OneTimeSetUp]
        public void NeedsTheGameData() => ShippedGameData.RequireOrIgnore();   // the riddle is a DDX record

        [TearDown]
        public void TearDown() {
            if (_entityGo != null) { UnityEngine.Object.DestroyImmediate(_entityGo); }
            if (_puzzleGo != null) { UnityEngine.Object.DestroyImmediate(_puzzleGo); }
            if (_menuGo != null) { UnityEngine.Object.DestroyImmediate(_menuGo); }
            if (_panelSettings != null) { UnityEngine.Object.DestroyImmediate(_panelSettings); }
        }

        [UnityTest]
        public IEnumerator ASolvedRiddleGoesToBlackAndTheLootComesUpOutOfIt() => UniTask.ToCoroutine(async () => {
            var session = new GameSession();
            var chest = new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 7, minChapter: 0, maxChapter: 10,
                    worldItemId: 162, x: ChestX, y: ChestY, actorNumber: 0),
                SaveGameContainerType.Chest, numberOfItems: 0, capacity: 4,
                dataTypes: SaveGameContainerDataType.Lock,
                items: Array.Empty<SaveGameInventoryItemData>(),
                lockData: new SaveGameContainerLockData(0, 0, Puzzle, 0),
                dialogData: null, shopData: null, encounterData: null, timestamp: 0,
                globalStateIndex: null);
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(7, 0, new[] { chest }),
            }), chapter: 1);
            session.CurrentZone = 7;
            session.SetActorInventoryForTest(0,
                new RuntimeContainer { Capacity = 4, ContainerType = SaveGameContainerType.Inventory });
            session.SetActiveParty(1, new byte[] { 0 });

            var navigator = new ScreenNavigator();
            await navigator.ResetTo(new Hud());

            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _menuGo = new GameObject("InventoryMenuUnderTest");
            _menuGo.AddComponent<UIDocument>().panelSettings = _panelSettings;
            _menuGo.SetActive(false);
            var menu = _menuGo.AddComponent<InventoryMenu>();
            menu.Construct(session, new Stubs.NoOpResources(), navigator,
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<
                        BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                new Stubs.NoOpDialogs());

            _puzzleGo = new GameObject("PuzzleUnderTest");
            _puzzleGo.SetActive(false);
            var puzzle = _puzzleGo.AddComponent<PuzzleScreen>();

            _entityGo = new GameObject("chest");
            var entity = _entityGo.AddComponent<WorldEntity>();
            entity.Behavior = "container";
            entity.EntityType = GameData.Resources.World.WorldEntityType.Container;
            entity.Interaction = ChestProfile;
            entity.transform.position = BakCoordinateConverter.ConvertPosition(ChestX, ChestY, 0);

            bool LootUp() {
                VisualElement r = _menuGo.GetComponent<UIDocument>().rootVisualElement;   // a new one per enable
                return _menuGo.activeSelf && r != null && r.style.opacity.value >= 1f;
            }
            var cover = new Cover { LootUp = LootUp };

            var handler = new ContainerInteractionHandler(session, new Stubs.NoOpDialogs(), menu, navigator,
                new PuzzleService(puzzle), clock: null, explode: null, fade: () => cover);
            handler.HandleAsync(entity, isPrimary: true).Forget();

            float deadline = Time.realtimeSinceStartup + 10f;
            while (!_puzzleGo.activeSelf && Time.realtimeSinceStartup < deadline) {
                await UniTask.Yield();
            }
            Assert.IsTrue(_puzzleGo.activeSelf, "control: the riddle is up");

            // Spell the answer and leave by Exit: the screen answers IsSolved, as a solve does once
            // its bolts have run.
            const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
            var cipher = (CipherPuzzle)typeof(PuzzleScreen).GetField("_puzzle", Private).GetValue(puzzle);
            var rows = (int[])typeof(PuzzleScreen).GetField("_rows", Private).GetValue(puzzle);
            for (int c = 0; c < cipher.Width; c++) {
                for (int r = 0; r < cipher.DialRows.Count; r++) {
                    if (cipher.LetterAt(c, r) == cipher.Target[c]) { rows[c] = r; break; }
                }
            }
            Assert.IsTrue(puzzle.IsSolved, "control: the dials spell " + cipher.Target);
            puzzle.PrimaryAction(18);   // Exit

            // Every frame from the answer until well after the loot is up: something covers the
            // world -- the riddle, the black, or the loot fully faded in.
            int worldFrames = 0;
            deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline) {
                if (!_puzzleGo.activeSelf && !cover.Up && !LootUp()) { worldFrames++; }
                await UniTask.Yield();
            }

            Assert.AreEqual(0, worldFrames, "frames that showed the world between the riddle and the loot");
            Assert.IsFalse(_puzzleGo.activeSelf, "the riddle is closed");
            Assert.IsTrue(cover.LootUpWhenLifted, "the black lifts onto the loot, not onto the world");
            Assert.IsFalse(cover.Up, "and it does lift");
            Assert.AreSame(menu, navigator.Current, "the loot screen is up");
            Assert.AreEqual(SaveGameContainerType.Chest, menu.DisplayedContainer?.ContainerType,
                "showing the chest");

            await navigator.Pop();
        });
    }
}
