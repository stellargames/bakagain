namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using System.Collections.Generic;
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
    /// A picked topic's answer is drawn without the topic grid under it (TASK-927: Sumani at LaMut's
    /// Blue Wheel Inn, the owner on a phone). The grid lives on the stage, beside the page's panel;
    /// it went only with the overlay's teardown, and a location leaves its pages drawn between
    /// records (TASK-914), so the grid stayed for the rest of the conversation.
    /// </summary>
    public class DialogKeywordGridGoesWithItsPageTests {
        private const int DialogId = 3000020;
        private const int Topic = 2;   // any keyword the session has asked about

        private GameObject _go;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_go != null) Object.DestroyImmediate(_go);
            if (_panelSettings != null) Object.DestroyImmediate(_panelSettings);
        }

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator TheAnswerToAPickedTopic_HasNoTopicGridUnderIt([Values(true, false)] bool inALocation) {
            var stack = new InputLayerStack();
            _go = new GameObject("DialogKeywordGridUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;
            document.sortingOrder = 10;
            var session = new GameSession();
            session.SetGlobalFlag(Topic, true);
            var executor = new DialogExecutor(NullLogger<DialogExecutor>.Instance, session, clock: null, provider: null);
            executor.UseResourceLoader(new OneDialogLoader(Conversation()));
            var manager = _go.AddComponent<DialogManager>();
            manager.Construct(cursorManager: null, stack: stack, menuSound: null,
                gameSession: session, executor: executor, preferences: new FakePreferencesService());

            // A location's own call leaves its pages drawn between records (TASK-914); the other is the
            // control, whose overlay is torn down after every page.
            UniTask<int> talking = inALocation
                ? manager.ShowChoiceIndexByIdLeavingLastPage(DialogId)
                : manager.ShowChoiceIndexById(DialogId);

            float deadline = Time.realtimeSinceStartup + 10f;
            while (Grid(document) == null) {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "the topic grid never came up");
                yield return null;
            }
            for (var i = 0; i < 5; i++) yield return null;

            stack.DispatchIntent(UiIntent.Activate());   // the first topic
            deadline = Time.realtimeSinceStartup + 10f;
            while (Body(manager)?.Contains("Steel") != true) {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "the answer never came up");
                yield return null;
            }
            yield return null;

            Assert.IsNull(Grid(document), "the answer is drawn with no topic grid under it");

            stack.DispatchIntent(UiIntent.Skip());
            for (var i = 0; i < 30 && talking.Status == UniTaskStatus.Pending; i++) yield return null;
        }

        private const string Answer = "Steel is steel, my lord.";

        private static VisualElement Grid(UIDocument document) => document.rootVisualElement?.Q("BakKeywordGrid");

        private static string Body(DialogManager manager) {
            var panel = (VisualElement)typeof(DialogManager)
                .GetField("_activePanel", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
            GameTextBlock body = panel?.Q<GameTextBlock>("BakDialogBody");
            return body == null ? null : string.Join(" ", System.Linq.Enumerable.Select(body.Lines, l => l.text));
        }

        // Sumani's shape: a topic menu (0x400) whose topic leads to a narrative answer.
        private static Dialog Conversation() {
            var menu = new DialogEntry {
                Offset = 100, Id = DialogId, Text = "Your will, lord?",
                Flags = DialogEntryFlags.ChoiceMenu,
                Actions = new List<DialogActionBase>(),
                Branches = new List<DialogBranchBase> { new KeywordChoiceBranch { Keyword = Topic, TargetOffset = 200 } },
            };
            var answer = new DialogEntry {
                Offset = 200, Id = DialogId + 1, Text = Answer,
                Actions = new List<DialogActionBase>(),
                Branches = new List<DialogBranchBase>(),
            };
            var dialog = new Dialog("dial_z30");
            dialog.Entries.Add(menu);
            dialog.Entries.Add(answer);
            menu.Key = ContentKey.ForBase("ddx:dial_z30", menu.Offset);
            answer.Key = ContentKey.ForBase("ddx:dial_z30", answer.Offset);
            return dialog;
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
