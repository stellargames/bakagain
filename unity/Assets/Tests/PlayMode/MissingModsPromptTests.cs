namespace BakAgain.Tests.PlayMode {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using BakAgain.Tests.PlayMode.UI.Inventory;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using GameData.Mods;
    using NUnit.Framework;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using UnityEngine;

    /// <summary>
    /// Saves record the active mods in a sidecar, and restoring a save whose mods are not active asks
    /// first (mod system spec 4.7). The restore is driven through <see cref="LoadGameMenu"/>'s own
    /// Restore button with the prompt's choice arms, on inactive objects: the REQ build is not tested.
    /// </summary>
    public class MissingModsPromptTests {
        private const int ButtonRestore = 193;

        private TempOverrideDirectory _dev;
        private EnhancedPrefsStash _enhanced;
        private string _mods;
        private string _saveDir;
        private readonly List<GameObject> _objects = new();

        [SetUp]
        public void SetUp() {
            _dev = new TempOverrideDirectory();
            _enhanced = EnhancedPrefsStash.Take();
            GameOptions.Enhanced = true;
            _mods = ModLayers.ModsRoot;
            _saveDir = _dev.Root + "-saves";
            Directory.CreateDirectory(_saveDir);
        }

        [TearDown]
        public void TearDown() {
            foreach (GameObject go in _objects) Object.DestroyImmediate(go);
            _objects.Clear();
            if (Directory.Exists(_saveDir)) Directory.Delete(_saveDir, true);
            _enhanced.Restore();
            _dev.Dispose();
        }

        [Test]
        public async Task Save_WritesSidecar_WithActiveMods() {
            InstallMod("here-mod");
            ModLayers.Initialize();
            (SaveGameService service, TempSaves saves) = SaveService();

            Assert.That(await service.SaveAsync("SAVES.G01", 1, "my save"), Is.True);

            IReadOnlyList<SavedMod> saved = SaveModList.Read(saves.SlotFullPath("SAVES.G01", 1));
            Assert.That(saved.Select(m => $"{m.Id} {m.Version}"), Is.EqualTo(new[] { "here-mod 1.0.0" }));
        }

        [Test]
        public async Task Save_SidecarWriteFails_StaleSidecarGoesAndTheSaveStands() {
            InstallMod("here-mod");
            ModLayers.Initialize();
            (SaveGameService service, TempSaves saves) = SaveService();
            string slot = saves.SlotFullPath("SAVES.G01", 1);
            Directory.CreateDirectory(Path.GetDirectoryName(slot)!);
            SaveModList.Write(slot, ModLayers.Current.Active); // the previous save's list
            Assert.That(File.Exists(SaveModList.SidecarPath(slot)));
            Directory.CreateDirectory(SaveModList.SidecarPath(slot) + ".tmp"); // the write cannot take its temp file

            Assert.That(await service.SaveAsync("SAVES.G01", 1, "my save"), Is.True, "the save itself is not lost");

            Assert.That(File.Exists(slot));
            Assert.That(File.Exists(SaveModList.SidecarPath(slot)), Is.False, "a stale list would be taken for this save's");
        }

        private (SaveGameService, TempSaves) SaveService() {
            var session = new GameSession();
            session.Initialize(new SaveGameBuilder().WithBackingBody().Build(), GameSessionSource.NewGame);
            var saves = new TempSaves(_saveDir);
            return (new SaveGameService(session, saves, new NullLogger<SaveGameService>(), new GameClock(session),
                new FakeResourceProviderService()), saves);
        }

        [Test]
        public void Load_MissingMod_PromptShown_CancelStays() {
            ModLayers.Initialize();
            Harness h = Restore(Sidecar("gone-mod"));

            Assert.That(h.Navigator.Pushed, Is.EqualTo(new IScreen[] { h.Prompt }));
            Assert.That(h.Prompt.Title, Is.EqualTo("This save was made with mods that are not active:"));
            // A missing mod has no manifest to name it, so the line names its saved id.
            Assert.That(h.Prompt.Lines, Is.EqualTo(new[] { "gone-mod 1.0.0" }));

            h.Prompt.Cancel();

            Assert.That(h.Navigator.Pops, Is.EqualTo(1), "the prompt closes, back to the restore screen");
            Assert.That(h.Flow.Loaded, Is.Empty);
        }

        [Test]
        public void Load_MissingMod_LoadAnyway_Loads() {
            ModLayers.Initialize();
            string save = Sidecar("gone-mod");
            Harness h = Restore(save);

            h.Prompt.LoadAnyway();

            Assert.That(h.Navigator.Pops, Is.EqualTo(1));
            Assert.That(h.Flow.Loaded, Is.EqualTo(new[] { save }));
        }

        [Test]
        public void Load_MajorVersion_PromptSaysMismatch() {
            InstallMod("here-mod", "2.0.0");
            ModLayers.Initialize();
            Harness h = Restore(Sidecar("here-mod"));

            Assert.That(h.Navigator.Pushed, Is.EqualTo(new IScreen[] { h.Prompt }));
            // The mod is active, only at another major version: not "not active".
            Assert.That(h.Prompt.Title, Is.EqualTo("This save was made with different versions of these mods:"));
            Assert.That(h.Prompt.Lines, Is.EqualTo(new[] { "here-mod 1.0.0 (installed: 2.0.0)" }));
        }

        [Test]
        public void Prompt_ClosedTwice_PopsOnce() {
            ModLayers.Initialize();
            string save = Sidecar("gone-mod");
            Harness h = Restore(save);

            // A double click, or a key and a click in one frame: the second must not pop the restore screen.
            h.Prompt.LoadAnyway();
            h.Prompt.Cancel();

            Assert.That(h.Navigator.Pops, Is.EqualTo(1));
            Assert.That(h.Flow.Loaded, Is.EqualTo(new[] { save }));
        }

        [Test]
        public void Prompt_HiddenWithoutAChoice_ResolvesAsCancel() {
            ModLayers.Initialize();
            Harness h = Restore(Sidecar("gone-mod"));
            Assert.That(h.Prompt.Result.Status, Is.EqualTo(UniTaskStatus.Pending));

            typeof(MissingModsScreen).GetMethod("OnClosing", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(h.Prompt, null);

            Assert.That(h.Prompt.Result.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(h.Prompt.Result.GetAwaiter().GetResult(), Is.False, "Cancel, not a restore left waiting");
            Assert.That(h.Flow.Loaded, Is.Empty);
        }

        [Test]
        public void Load_NoIssues_NoPrompt() {
            InstallMod("here-mod");
            ModLayers.Initialize();
            string save = Sidecar("here-mod");
            Harness h = Restore(save);

            Assert.That(h.Navigator.Pushed, Is.Empty);
            Assert.That(h.Flow.Loaded, Is.EqualTo(new[] { save }));
        }

        private void InstallMod(string id, string version = "1.0.0") {
            string folder = Path.Combine(_mods, id);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "mod.json"),
                $"{{\"schema\":1,\"id\":\"{id}\",\"name\":\"{id}\",\"version\":\"{version}\"}}");
        }

        /// <summary>A save slot whose sidecar records one Enhanced mod; returns the slot's path.</summary>
        private string Sidecar(string id) {
            string save = Path.Combine(_saveDir, "SAVE01.GAM");
            File.WriteAllText(SaveModList.SidecarPath(save),
                $"{{\"mods\":[{{\"id\":\"{id}\",\"version\":\"1.0.0\",\"modes\":[\"enhanced\"]}}]}}");
            return save;
        }

        private sealed class Harness {
            public LoadGameMenu Menu;
            public MissingModsScreen Prompt;
            public RecordingNavigator Navigator;
            public RecordingFlow Flow;
        }

        /// <summary>Selects <paramref name="save"/> on the restore screen and presses Restore.</summary>
        private Harness Restore(string save) {
            var h = new Harness { Navigator = new RecordingNavigator(), Flow = new RecordingFlow() };
            h.Prompt = Inactive<MissingModsScreen>();
            h.Prompt.Construct(h.Navigator, null, null);
            h.Menu = Inactive<LoadGameMenu>();
            h.Menu.Construct(new TempSaves(_saveDir), new NoOpDialogs(), h.Flow, h.Navigator, h.Prompt);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(LoadGameMenu).GetMethod("Awake", flags)!.Invoke(h.Menu, null); // the logger; the GameObject stays inactive
            typeof(SaveLoadMenuBase).GetField("_slots", flags)!.SetValue(h.Menu, new List<SaveSlotInfo> {
                new() { FileName = "SAVE01.GAM", FullPath = save, DisplayName = "my save", ChapterNumber = 1, IsValid = true },
            });
            typeof(SaveLoadMenuBase).GetField("_selectedSlot", flags)!.SetValue(h.Menu, 0);
            h.Menu.PrimaryAction(ButtonRestore);
            return h;
        }

        private T Inactive<T>() where T : Component {
            var go = new GameObject(typeof(T).Name);
            go.SetActive(false);
            _objects.Add(go);
            return go.AddComponent<T>();
        }

        private sealed class RecordingNavigator : IScreenNavigator {
            public readonly List<IScreen> Pushed = new();
            public int Pops;

            public IScreen Current => Pushed.LastOrDefault();
            public UniTask ResetTo(IScreen root) => UniTask.CompletedTask;

            public UniTask Push(IScreen screen) {
                Pushed.Add(screen);
                return UniTask.CompletedTask;
            }

            public UniTask PushAndWaitAsync(IScreen screen) => Push(screen);

            public UniTask Pop() {
                Pops++;
                return UniTask.CompletedTask;
            }

            public UniTask Replace(IScreen screen) => UniTask.CompletedTask;
            public UniTask Clear() => UniTask.CompletedTask;
        }

        private sealed class RecordingFlow : IGameFlow {
            public readonly List<string> Loaded = new();

            public UniTask<bool> LoadSave(string path) {
                Loaded.Add(path);
                return UniTask.FromResult(true);
            }

            public UniTask Boot() => UniTask.CompletedTask;
            public UniTask ShowMainMenu() => UniTask.CompletedTask;
            public UniTask StartNewGame(bool playChapterScenes = true) => UniTask.CompletedTask;
            public UniTask GoToChapter(int chapter) => UniTask.CompletedTask;
            public UniTask EnterWorld() => UniTask.CompletedTask;

            public UniTask<GameData.Resources.Location.ZoneTransitionKind> TransitionTo(GameData.Resources.Location.Location destination) =>
                UniTask.FromResult(default(GameData.Resources.Location.ZoneTransitionKind));
        }

        /// <summary>Slots under a temp folder; only what saving and restoring touch.</summary>
        private sealed class TempSaves : ISaveGameDirectoryService {
            public TempSaves(string root) => SaveRoot = root;

            public string SaveRoot { get; }
            public UniTask EnsureRootAsync() => UniTask.CompletedTask;
            public UniTask<string> EnsureDefaultDirectoryAsync() => UniTask.FromResult("SAVES.G01");
            public int LowestFreeSlotIndex(string directoryName) => 1;
            public int LowestFreeDirectoryNumber() => 1;
            public UniTask<string> CreateDirectoryAsync(string baseName) => UniTask.FromResult(baseName + ".G01");
            public bool IsValidDirectoryName(string name) => true;

            public UniTask<IReadOnlyList<SaveDirectoryInfo>> ListDirectoriesAsync() =>
                UniTask.FromResult<IReadOnlyList<SaveDirectoryInfo>>(new List<SaveDirectoryInfo>());

            public UniTask<IReadOnlyList<SaveSlotInfo>> ListSlotsAsync(string directoryName) =>
                UniTask.FromResult<IReadOnlyList<SaveSlotInfo>>(new List<SaveSlotInfo>());

            public UniTask<string> EnsureDirectoryAsync(string directoryName) =>
                UniTask.FromResult(Path.Combine(SaveRoot, directoryName));

            public string SlotFullPath(string directoryName, int slotIndex) =>
                Path.Combine(SaveRoot, directoryName, $"SAVE{slotIndex:D2}.GAM");

            public UniTask<bool> WriteSlotAsync(string fullPath, byte[] bytes) {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllBytes(fullPath, bytes);
                return UniTask.FromResult(true);
            }

            public UniTask DeleteSlotAsync(string fullPath) => UniTask.CompletedTask;
            public UniTask DeleteDirectoryAsync(string directoryName) => UniTask.CompletedTask;
        }
    }
}
