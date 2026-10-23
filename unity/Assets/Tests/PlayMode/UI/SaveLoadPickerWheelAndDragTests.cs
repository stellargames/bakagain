namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using BakAgain.UI.InputCore;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// TASK-907: the Save and Restore dialogs' two lists scroll with the mouse wheel and follow a drag
    /// on their scrollbars, driven through the real REQ-built dialog.
    /// <para>The drag is the original's: a press held on the scrollbar track sets the list's CURRENT ROW
    /// from the cursor's place on the track (widget_hit_test region 3 -> g_nScrollTrackOffset,
    /// widget_scrollbar_update, canassa UI/WIDGET.C:120-131 and 220-248, re-run every poll while the
    /// button is held, UI/MENUPAGE.C:331-342), and listwidget_set_selection centres it
    /// (UI/LISTWDG.C:207-227). The wheel is a port addition (a DOS mouse has none): one notch moves the
    /// current row one, like the arrows.</para>
    /// </summary>
    [RequiresShippedGameData]
    public class SaveLoadPickerWheelAndDragTests {
        private const int Directories = 197;
        private const int Games = 196;
        private const int DirectoryCount = 9;
        private const int GameCount = 12;

        private EnhancedPrefsStash _stash;
        private ScreenBase _screen;
        private object _realSaves;
        private string _lastDirectory;
        private string _lastGame;

        private static readonly FieldInfo SavesField =
            typeof(SaveLoadMenuBase).GetField("_saves", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo LastDirectory =
            typeof(SaveLoadMenuBase).GetField("s_lastDirectory", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo LastGame =
            typeof(SaveLoadMenuBase).GetField("s_lastGame", BindingFlags.NonPublic | BindingFlags.Static);

        [SetUp]
        public void SetUp() {
            _stash = EnhancedPrefsStash.Take();
            _lastDirectory = (string)LastDirectory.GetValue(null);
            _lastGame = (string)LastGame.GetValue(null);
            LastDirectory.SetValue(null, null);
            LastGame.SetValue(null, null);
        }

        [TearDown]
        public void TearDown() {
            if (_screen != null) {
                _screen.gameObject.SetActive(false);
                SavesField.SetValue(_screen, _realSaves);
            }
            LastDirectory.SetValue(null, _lastDirectory);
            LastGame.SetValue(null, _lastGame);
            _stash.Restore();
        }

        [UnityTest]
        public IEnumerator WheelAndDrag_ScrollBothLists_ArrowsKeysAndClicksUnchanged(
            [Values(false, true)] bool save, [Values(false, true)] bool enhanced) => UniTask.ToCoroutine(async () => {
            GameOptions.Enhanced = enhanced;
            var scope = Object.FindAnyObjectByType<VContainer.Unity.LifetimeScope>();
            Assert.IsNotNull(scope, "the test scene's container");
            _screen = (ScreenBase)scope.Container.Resolve(save ? typeof(SaveGameMenu) : typeof(LoadGameMenu));
            if (_screen.gameObject.activeSelf) {
                _screen.gameObject.SetActive(false);
            }
            _realSaves = SavesField.GetValue(_screen);
            SavesField.SetValue(_screen, new FakeSaves());
            var picker = (IFilePickerSource)_screen;

            await _screen.ShowAsync(fade: false);
            var loader = _screen.GetComponent<UserInterfaceLoader>();
            VisualElement root = _screen.GetComponent<UIDocument>().rootVisualElement;
            float deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline
                   && !(loader.IsBuilt && Rows(root, Games).Count > 0
                        && root.Q($"filepicker_{Games}_track")?.contentRect.height > 0f
                        && root.Q($"filepicker_{Directories}_track")?.contentRect.height > 0f)) {
                await UniTask.Yield();
            }
            Assert.That(Rows(root, Games).Count, Is.EqualTo(5), "the real dialog built its Games list");
            Assert.That(picker.GetSelectedIndex(Directories), Is.EqualTo(0));
            Assert.That(picker.GetSelectedIndex(Games), Is.EqualTo(0));

            // --- #1 The wheel: one notch = one row, clamped at both ends, per list. ---
            VisualElement gamesRows = root.Q($"filepicker_{Games}_rows");
            Wheel(gamesRows, +1);
            Assert.That(picker.GetSelectedIndex(Games), Is.EqualTo(1), "one notch down = one row");
            Wheel(gamesRows, +1);
            Wheel(gamesRows, +1);
            Assert.That(picker.GetSelectedIndex(Games), Is.EqualTo(3));
            for (var i = 0; i < 6; i++) Wheel(gamesRows, -1);
            Assert.That(picker.GetSelectedIndex(Games), Is.EqualTo(0), "clamped at the top");
            for (var i = 0; i < 20; i++) Wheel(gamesRows, +1);
            Assert.That(picker.GetSelectedIndex(Games), Is.EqualTo(GameCount - 1), "clamped at the bottom");
            Assert.That(FirstRow(root, Games), Is.EqualTo("D0 game 7"), "the window followed the row");
            Assert.That(picker.GetSelectedIndex(Directories), Is.EqualTo(0), "the other list did not move");

            Wheel(root.Q($"filepicker_{Directories}_rows"), +1);
            Assert.That(picker.GetSelectedIndex(Directories), Is.EqualTo(1), "the wheel over the directories");
            Assert.That(FirstRow(root, Games), Is.EqualTo("D1 game 0"), "...lists that directory's games");
            Wheel(root.Q($"filepicker_{Directories}_bar"), +1);
            Assert.That(picker.GetSelectedIndex(Directories), Is.EqualTo(2), "over the scrollbar too");

            // --- #2 The drag: the row follows the pointer down the track, clamped, and the thumb with it. ---
            VisualElement track = root.Q($"filepicker_{Games}_track");
            VisualElement thumb = root.Q($"filepicker_{Games}_thumb");
            Rect t = track.worldBound;
            float x = t.center.x;
            Send<PointerDownEvent>(thumb, EventType.MouseDown, thumb.worldBound.center);
            Assert.That(picker.GetSelectedIndex(Games), Is.EqualTo(0), "a press ON the thumb keeps its row");
            // The thumb's centre for row 6 of 0..11: the row under the pointer is the row the thumb shows.
            float travel = track.contentRect.height - thumb.resolvedStyle.height;
            Vector2 row6 = track.LocalToWorld(new Vector2(0f,
                track.resolvedStyle.borderTopWidth + thumb.resolvedStyle.height / 2f + travel * 6f / 11f));
            Send<PointerMoveEvent>(track, EventType.MouseDrag, new Vector2(x, row6.y));
            Assert.That(picker.GetSelectedIndex(Games), Is.EqualTo(6), "half way down = the middle row");
            Assert.That(FirstRow(root, Games), Is.EqualTo("D2 game 4"),
                "centred, as listwidget_set_selection does (LISTWDG.C:222)");
            Send<PointerMoveEvent>(track, EventType.MouseDrag, new Vector2(x, t.yMax + 400f));
            Assert.That(picker.GetSelectedIndex(Games), Is.EqualTo(GameCount - 1), "past the end: clamped");
            Assert.That(thumb.style.top.value.value, Is.EqualTo(travel).Within(0.5f), "the thumb at the bottom");
            Send<PointerMoveEvent>(track, EventType.MouseDrag, new Vector2(x, t.yMin - 400f));
            Assert.That(picker.GetSelectedIndex(Games), Is.EqualTo(0), "past the top: clamped");
            Assert.That(thumb.style.top.value.value, Is.EqualTo(0f).Within(0.5f), "the thumb at the top");
            Send<PointerUpEvent>(track, EventType.MouseUp, new Vector2(x, t.yMin - 400f));
            Send<PointerMoveEvent>(track, EventType.MouseMove, new Vector2(x, t.yMax));
            Assert.That(picker.GetSelectedIndex(Games), Is.EqualTo(0), "released: a bare move does nothing");

            // A press on the track (not the thumb) takes the row there at once (WIDGET.C:220 on the press).
            Send<PointerDownEvent>(track, EventType.MouseDown, new Vector2(x, t.yMax - 1f));
            Send<PointerUpEvent>(track, EventType.MouseUp, new Vector2(x, t.yMax - 1f));
            Assert.That(picker.GetSelectedIndex(Games), Is.EqualTo(GameCount - 1), "a click low on the track");

            VisualElement dirTrack = root.Q($"filepicker_{Directories}_track");
            Rect d = dirTrack.worldBound;
            Send<PointerDownEvent>(root.Q($"filepicker_{Directories}_thumb"), EventType.MouseDown,
                root.Q($"filepicker_{Directories}_thumb").worldBound.center);
            Send<PointerMoveEvent>(dirTrack, EventType.MouseDrag, new Vector2(d.center.x, d.yMax + 50f));
            Send<PointerUpEvent>(dirTrack, EventType.MouseUp, new Vector2(d.center.x, d.yMax + 50f));
            Assert.That(picker.GetSelectedIndex(Directories), Is.EqualTo(DirectoryCount - 1), "the directories' thumb");
            Assert.That(FirstRow(root, Games), Is.EqualTo("D8 game 0"), "...lists that directory's games");

            // --- #3 Controls: the arrows, Up/Down (Ctrl: directories) and a row click as before. ---
            VisualElement downArrow = root.Q($"filepicker_{Games}_bar").Q(className: "file-picker-arrow--down");
            Send<PointerDownEvent>(downArrow, EventType.MouseDown, downArrow.worldBound.center);
            Send<PointerUpEvent>(downArrow, EventType.MouseUp, downArrow.worldBound.center);
            Assert.That(picker.GetSelectedIndex(Games), Is.EqualTo(1), "the down arrow");
            var input = (IScreenInput)_screen;
            input.OnDirection(NavDirection.Down, false);
            Assert.That(picker.GetSelectedIndex(Games), Is.EqualTo(2), "Down");
            input.OnDirection(NavDirection.Up, true);
            Assert.That(picker.GetSelectedIndex(Directories), Is.EqualTo(DirectoryCount - 2), "Ctrl+Up");
            VisualElement row = Rows(root, Games)[3];
            using (ClickEvent click = ClickEvent.GetPooled()) {
                click.target = row;
                row.SendEvent(click);
            }
            Assert.That(picker.GetSelectedIndex(Games), Is.EqualTo(3), "a row click");
        });

        private static List<VisualElement> Rows(VisualElement root, int action) =>
            root.Query(className: "file-picker-row").Where(r => r.parent?.name == $"filepicker_{action}_rows").ToList();

        private static string FirstRow(VisualElement root, int action) =>
            Rows(root, action)[0].Q<Label>("caption").text;

        private static void Wheel(VisualElement target, int notches) {
            var imgui = new Event {
                type = EventType.ScrollWheel, delta = new Vector2(0f, 3f * notches),
                mousePosition = target.worldBound.center,
            };
            using (WheelEvent evt = WheelEvent.GetPooled(imgui)) {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        private static void Send<T>(VisualElement target, EventType type, Vector2 at)
            where T : PointerEventBase<T>, new() {
            var imgui = new Event { type = type, mousePosition = at, button = 0, clickCount = 1 };
            using (T evt = PointerEventBase<T>.GetPooled(imgui)) {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        /// <summary>Nine directories of twelve games each, in memory: the lists overflow their five rows.</summary>
        private sealed class FakeSaves : ISaveGameDirectoryService {
            public string SaveRoot => Path.Combine(Path.GetTempPath(), "t907-none");
            public UniTask EnsureRootAsync() => UniTask.CompletedTask;
            public UniTask<string> EnsureDefaultDirectoryAsync() => UniTask.FromResult("D0.G01");
            public int LowestFreeSlotIndex(string directoryName) => -1;
            public int LowestFreeDirectoryNumber() => -1;
            public UniTask<string> CreateDirectoryAsync(string baseName) => UniTask.FromResult<string>(null);
            public bool IsValidDirectoryName(string name) => true;

            public UniTask<IReadOnlyList<SaveDirectoryInfo>> ListDirectoriesAsync() {
                var list = new List<SaveDirectoryInfo>();
                for (var i = 0; i < DirectoryCount; i++) {
                    list.Add(new SaveDirectoryInfo { Name = $"D{i}.G{i + 1:D2}", DisplayName = $"D{i}", Number = i + 1 });
                }
                return UniTask.FromResult<IReadOnlyList<SaveDirectoryInfo>>(list);
            }

            public UniTask<IReadOnlyList<SaveSlotInfo>> ListSlotsAsync(string directoryName) {
                string dir = directoryName.Split('.')[0];
                var list = new List<SaveSlotInfo>();
                for (var i = 0; i < GameCount; i++) {
                    list.Add(new SaveSlotInfo {
                        FileName = $"SAVE{i:D2}.GAM", DisplayName = $"{dir} game {i}", SlotIndex = i, IsValid = true,
                        FullPath = Path.Combine(SaveRoot, directoryName, $"SAVE{i:D2}.GAM"), ChapterNumber = 1,
                    });
                }
                return UniTask.FromResult<IReadOnlyList<SaveSlotInfo>>(list);
            }

            public UniTask<string> EnsureDirectoryAsync(string directoryName) => UniTask.FromResult<string>(null);
            public string SlotFullPath(string directoryName, int slotIndex) => null;
            public UniTask<bool> WriteSlotAsync(string fullPath, byte[] bytes) => UniTask.FromResult(false);
            public UniTask DeleteSlotAsync(string fullPath) => UniTask.CompletedTask;
            public UniTask DeleteDirectoryAsync(string directoryName) => UniTask.CompletedTask;
        }
    }
}
