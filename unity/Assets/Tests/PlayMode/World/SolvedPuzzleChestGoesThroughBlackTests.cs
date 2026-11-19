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
    using GameData.Resources.World;
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

            public Func<int> Track;
            public int TrackAtFadeOut = int.MinValue;

            public async UniTask FadeOutAsync() {
                if (Track != null) { TrackAtFadeOut = Track(); }
                await UniTask.Yield();
                Up = true;
            }

            public UniTask FadeInAsync() {
                LootUpWhenLifted = LootUp();
                Up = false;
                return UniTask.CompletedTask;
            }

            // The ramp each direction asked for (TASK-978); NaN = the world fade's own.
            public float OutSeconds = float.NaN, InSeconds = float.NaN;

            public UniTask FadeOutAsync(float seconds) {
                OutSeconds = seconds;
                return FadeOutAsync();
            }

            public UniTask FadeInAsync(float seconds) {
                InSeconds = seconds;
                return FadeInAsync();
            }
        }

        private GameObject _menuGo;
        private GameObject _puzzleGo;
        private GameObject _musicGo;
        private GameObject _entityGo;
        private PanelSettings _panelSettings;

        [OneTimeSetUp]
        public void NeedsTheGameData() => ShippedGameData.RequireOrIgnore();   // the riddle is a DDX record

        [TearDown]
        public void TearDown() {
            if (_entityGo != null) { UnityEngine.Object.DestroyImmediate(_entityGo); }
            if (_puzzleGo != null) { UnityEngine.Object.DestroyImmediate(_puzzleGo); }
            if (_musicGo != null) { UnityEngine.Object.DestroyImmediate(_musicGo); }
            if (_menuGo != null) { UnityEngine.Object.DestroyImmediate(_menuGo); }
            if (_panelSettings != null) { UnityEngine.Object.DestroyImmediate(_panelSettings); }
        }

        [UnityTest]
        public IEnumerator ASolvedRiddleGoesToBlackAndTheLootComesUpOutOfIt() => SolveAndOpen(withMusic: false);

        /// <summary>
        /// The world's track comes back last, after the riddle has gone to black and been torn down:
        /// CIPHER.C:205-229 fades at :208 and calls audio_music_play(nTrack) at :229. The port put it
        /// back before the fade (TASK-1104 row 11).
        /// </summary>
        [UnityTest]
        public IEnumerator TheWorldsTrackComesBackAfterTheRiddleGoesToBlack() => SolveAndOpen(withMusic: true);

        private const int WorldTrack = 1005;

        private sealed class BareSongs : BakAgain.ResourceManagement.IResourceProviderService {
            public UniTask<T> LoadAssetAsync<T>(object key, object owner) where T : class =>
                UniTask.FromResult(new GameData.Resources.Audio.AudioResource(key.ToString()) as T);
            public void ReleaseAssets(object owner) { }
        }

        private IEnumerator SolveAndOpen(bool withMusic) => UniTask.ToCoroutine(async () => {
            using var notes = BakAgain.Tests.TestSupport.ExpectedLog.CaptureAll(
                new System.Text.RegularExpressions.Regex("does not contain variant"));
            BakAgain.Audio.MidiPlaybackManager midi = null;
            var songs = new BareSongs();
            if (withMusic) {
                midi = BakAgain.Audio.MidiPlaybackManager.Instance;
                if (midi == null) {
                    _musicGo = new GameObject("Music");
                    midi = _musicGo.AddComponent<BakAgain.Audio.MidiPlaybackManager>();
                    await UniTask.Yield();
                }
                midi.StopMusic();
                await midi.PlayTrackAsync(WorldTrack, songs);
                Assert.AreEqual(WorldTrack, midi.CurrentTrack, "control: the world's track");
            }
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

            _panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
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
            if (withMusic) {
                puzzle.Construct(null, songs, midi, session);
            }

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
            var cover = new Cover { LootUp = LootUp, Track = withMusic ? () => midi.CurrentTrack : null };

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
            Assert.AreEqual(ScreenFade.CipherFadeOutSeconds, cover.OutSeconds,
                "the riddle goes to black at the cipher's own ramp (CIPHER.C:207), not the world fade's");
            Assert.AreEqual(ScreenFade.InventoryFadeInSeconds, cover.InSeconds,
                "and the loot comes up at the inventory's (CMBINV.C:269-271)");
            Assert.AreSame(menu, navigator.Current, "the loot screen is up");
            Assert.AreEqual(SaveGameContainerType.Chest, menu.DisplayedContainer?.ContainerType,
                "showing the chest");
            if (withMusic) {
                Assert.AreEqual(GameData.Resources.Scene.CipherPuzzleLayout.MusicTrack, cover.TrackAtFadeOut,
                    "the riddle's own track still plays as it goes to black");
                Assert.AreEqual(WorldTrack, midi.CurrentTrack, "and the world's is back after");
                midi.StopMusic();
            }

            await navigator.Pop();
        });
            // TASK-978: the map's Cheat Central chest (TOWNSCN.C:761-773) runs the same cipher, whose solved
        // path goes to black (CIPHER.C:205-213: the fade, then the palette put back at once on a black
        // screen); the menu is drawn on it. The port closed the riddle onto the map.
        [UnityTest]
        public IEnumerator ASecondClickWhileTheChestIsBusyIsIgnored() => UniTask.ToCoroutine(async () => {
            // TASK-1005, owner 2026-10-26: a second click on the chest while its riddle was still
            // loading (the music takes ~1 s) started a second run of the same puzzle, whose Open
            // reset the resolved flag after the first run had dissolved to the readable face, so the
            // next turn of a wheel drew it in the alien script. The original's handler is modal.
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

            _puzzleGo = new GameObject("PuzzleUnderTest");
            _puzzleGo.SetActive(false);
            var puzzle = _puzzleGo.AddComponent<PuzzleScreen>();
            _entityGo = new GameObject("chest");
            var entity = _entityGo.AddComponent<WorldEntity>();
            entity.Behavior = "container";
            entity.EntityType = GameData.Resources.World.WorldEntityType.Container;
            entity.Interaction = ChestProfile;
            entity.transform.position = BakCoordinateConverter.ConvertPosition(ChestX, ChestY, 0);
            var handler = new ContainerInteractionHandler(session, new Stubs.NoOpDialogs(), null,
                new ScreenNavigator(), new PuzzleService(puzzle), clock: null, explode: null);

            handler.HandleAsync(entity, isPrimary: true).Forget();
            UniTask second = handler.HandleAsync(entity, isPrimary: true);
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!_puzzleGo.activeSelf && Time.realtimeSinceStartup < deadline) {
                await UniTask.Yield();
            }
            Assert.IsTrue(_puzzleGo.activeSelf, "control: the riddle is up");

            Assert.IsTrue(second.Status.IsCompleted(), "the second click started nothing of its own");
            puzzle.PrimaryAction(18);   // Exit
        });

        [UnityTest]
        public IEnumerator CheatCentralsChestGoesThroughBlackToItsMenu() => UniTask.ToCoroutine(async () => {
            var session = new GameSession();
            var chest = new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 0, minChapter: 0, maxChapter: 10,
                    worldItemId: 162, x: CheatCentral.ChestX, y: 1, actorNumber: 0),
                SaveGameContainerType.Chest, numberOfItems: 0, capacity: 4,
                dataTypes: SaveGameContainerDataType.Lock,
                items: Array.Empty<SaveGameInventoryItemData>(),
                lockData: new SaveGameContainerLockData(0, 0, Puzzle, 0),
                dialogData: null, shopData: null, encounterData: null, timestamp: 0,
                globalStateIndex: null);
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(0, 0, new[] { chest }),
            }), chapter: 1);

            var navigator = new ScreenNavigator();
            await navigator.ResetTo(new Hud());

            _puzzleGo = new GameObject("PuzzleUnderTest");
            _puzzleGo.SetActive(false);
            var puzzle = _puzzleGo.AddComponent<PuzzleScreen>();

            _panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            _menuGo = new GameObject("CheatCentralUnderTest");
            _menuGo.AddComponent<UIDocument>().panelSettings = _panelSettings;
            _menuGo.SetActive(false);
            var cheat = _menuGo.AddComponent<BakAgain.UI.Cheats.ChestCheatScreen>();
            cheat.Construct(session, new Stubs.NoOpDialogs(), navigator, null, null, new PuzzleService(puzzle), null);
            bool MenuUp() => _menuGo.activeSelf;
            var cover = new Cover { LootUp = MenuUp };
            cheat.Fade = cover;

            cheat.OpenWithCipherAsync().Forget();
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!_puzzleGo.activeSelf && Time.realtimeSinceStartup < deadline) {
                await UniTask.Yield();
            }
            Assert.IsTrue(_puzzleGo.activeSelf, "control: the riddle is up");

            const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
            var cipher = (CipherPuzzle)typeof(PuzzleScreen).GetField("_puzzle", Private).GetValue(puzzle);
            var rows = (int[])typeof(PuzzleScreen).GetField("_rows", Private).GetValue(puzzle);
            for (int c = 0; c < cipher.Width; c++) {
                for (int r = 0; r < cipher.DialRows.Count; r++) {
                    if (cipher.LetterAt(c, r) == cipher.Target[c]) { rows[c] = r; break; }
                }
            }
            puzzle.PrimaryAction(18);   // Exit, solved

            int mapFrames = 0;
            deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline) {
                if (!_puzzleGo.activeSelf && !cover.Up && !MenuUp()) { mapFrames++; }
                await UniTask.Yield();
            }

            Assert.AreEqual(0, mapFrames, "frames that showed the map between the riddle and the menu");
            Assert.AreEqual(ScreenFade.CipherFadeOutSeconds, cover.OutSeconds, "to black at the cipher's ramp");
            Assert.IsTrue(cover.LootUpWhenLifted, "the black lifts onto the menu");
            Assert.AreEqual(0f, cover.InSeconds, "as a cut: the cipher puts the palette back at once (CIPHER.C:211-213)");
            Assert.IsFalse(cover.Up);
            Assert.AreSame(cheat, navigator.Current, "Cheat Central is up");

            await navigator.Pop();
        });
    }
}
