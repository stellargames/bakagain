namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.Tests.Editor.World;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Content;
    using GameData.Resources.Dialog;
    using GameData.Resources.Dialog.Actions;
    using GameData.Resources.Dialog.Branches;
    using GameData.Resources.Location;
    using Microsoft.Extensions.Logging.Abstractions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// One mouse click turns one page of a narrative, and the last page waits for another (TASK-919).
    /// </summary>
    /// <remarks>
    /// A click reaches a narrative page twice: as the Skip intent the input adapter raises on the
    /// release, and as the release on the page's scrim. Measured on 1300064 (two pages): the Skip
    /// turned the page and the scrim's release, dispatched after the turn, closed the last page in the
    /// next frame. The original acts on a press made during the wait: <c>dialog_wait_for_acknowledge</c>
    /// first waits for every button to be up (DIALOG.C:223, <c>dialog_input_wait_release</c>), then for
    /// a press, then for its release (:239-250), so one click is one page.
    /// </remarks>
    public class DialogOneClickTurnsOnePageTests {
        private const int DialogId = 1300064;

        private GameObject _go;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_go != null) {
                Object.DestroyImmediate(_go);
            }
            if (_panelSettings != null) {
                Object.DestroyImmediate(_panelSettings);
            }
        }

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator TheReleaseOfTheClickThatTurnedThePageDoesNotCloseTheNextOne() {
            var stack = new InputLayerStack();
            (DialogManager manager, UniTask<int> showing) = Show(stack);
            VisualElement scrim = null;
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((scrim = Scrim(manager)) == null || LinesRemaining(manager) <= 0) {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "the record never paged");
                yield return null;
            }
            // The page is read before it is clicked: the wait is armed a frame after the panel is up.
            for (var i = 0; i < 10; i++) {
                yield return null;
            }

            // The press, on page one.
            Send<PointerDownEvent>(scrim, EventType.MouseDown);
            yield return null;
            // The release: the adapter's Skip comes first (the Input System update), the page turns,
            // and the scrim's release is dispatched after it.
            stack.DispatchIntent(UiIntent.Skip());
            for (var i = 0; i < 3 && LinesRemaining(manager) > 0; i++) {
                yield return null;
            }
            Assume.That(LinesRemaining(manager), Is.Zero, "the click turned the page");
            Send<PointerUpEvent>(scrim, EventType.MouseUp);
            for (var i = 0; i < 10; i++) {
                yield return null;
            }

            Assert.AreEqual(UniTaskStatus.Pending, showing.Status,
                "the last page waits for a press of its own (DIALOG.C:223, :239-249)");

            // And a click of its own closes it.
            Send<PointerDownEvent>(scrim, EventType.MouseDown);
            yield return null;
            Send<PointerUpEvent>(scrim, EventType.MouseUp);
            for (var i = 0; i < 10 && showing.Status == UniTaskStatus.Pending; i++) {
                yield return null;
            }
            Assert.AreNotEqual(UniTaskStatus.Pending, showing.Status, "the next click closes it");
        }

        private (DialogManager, UniTask<int>) Show(InputLayerStack stack) {
            _go = new GameObject("DialogOneClickUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            document.panelSettings = _panelSettings;
            document.sortingOrder = 10;

            var session = new GameSession();
            var executor = new DialogExecutor(NullLogger<DialogExecutor>.Instance, session, clock: null, provider: null);
            executor.UseResourceLoader(new OneDialogLoader(Record()));
            var manager = _go.AddComponent<DialogManager>();
            manager.Construct(cursorManager: null, stack: stack, menuSound: null,
                gameSession: session, executor: executor, preferences: new FakePreferencesService());
            return (manager, manager.ShowById(DialogId));
        }

        // A full-screen narrative long enough to need a second page, as 1300064 (832 characters) does.
        private static Dialog Record() {
            string sentence = "As a courtesy, we are given some warning by the servants of the temple. ";
            var entry = new DialogEntry {
                Offset = 31300,
                Id = DialogId,
                Text = "\t" + string.Concat(Enumerable.Repeat(sentence, 12)),
                DialogType = DialogType.PlainFullScreen,
                Flags = DialogEntryFlags.Legacy10,
                Actions = new List<DialogActionBase>(),
                Branches = new List<DialogBranchBase>(),
            };
            var dialog = new Dialog("dial_z13");
            dialog.Entries.Add(entry);
            entry.Key = ContentKey.ForBase("ddx:dial_z13", entry.Offset);
            return dialog;
        }

        private static VisualElement Scrim(DialogManager manager) =>
            (VisualElement)typeof(DialogManager)
                .GetField("_modalScrim", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);

        private static int LinesRemaining(DialogManager manager) {
            var panel = (VisualElement)typeof(DialogManager)
                .GetField("_activePanel", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
            return panel?.Q<GameTextBlock>("BakDialogBody")?.LinesRemaining ?? -1;
        }

        private static void Send<T>(VisualElement target, EventType type) where T : PointerEventBase<T>, new() {
            var imgui = new Event { type = type, mousePosition = target.worldBound.center, button = 0, clickCount = 1 };
            using (T evt = PointerEventBase<T>.GetPooled(imgui)) {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        private sealed class OneDialogLoader : IDialogResourceLoader {
            private readonly Dialog _dialog;

            public OneDialogLoader(Dialog dialog) {
                _dialog = dialog;
            }

            public UniTask<Dialog> LoadDialogAsync(int dialogId) => UniTask.FromResult(_dialog);

            public UniTask<TeleportDestinationSet> GetTeleportDestinationsAsync() =>
                UniTask.FromResult(new TeleportDestinationSet("TELEPORT.DAT"));
        }
    }
}
