namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.Tests.Editor.World;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Config;
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
    /// A record flagged <c>AutoAdvanceTimer</c> (0x40) closes by itself, even at the text speed that
    /// otherwise waits for the player (TASK-912).
    /// </summary>
    /// <remarks>
    /// <c>dialog_wait_for_acknowledge</c> (DIALOG.C:211-220) sets the deadline for a 0x40 record
    /// BEFORE it looks at the text speed: <c>(chars * 12 + 150) * scale / 100 / (chapter / 5 + 3)</c>
    /// timer ticks. Measured in the original on the zone 1 -> 2 crossing (t910.G01/SAVE02, Yes):
    /// dial_z27:1203, "The trip was dull...", closed with no input after 902 ticks at text speed 0,
    /// where the port waited for a key. Drives the real <see cref="DialogManager"/> narrative branch.
    /// </remarks>
    public class DialogAutoAdvanceTimerTests {
        private const int DialogId = 2700099;

        // 17 drawn characters (the tab is not drawn): (17 * 12 + 150) / 3 = 118 ticks = ~2.0 s.
        private const string Text = "\tThe trip was dull.";

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
        public IEnumerator AnAutoAdvanceRecordClosesItselfAtTheWaitForThePlayerSpeed() {
            UniTask<int> showing = Show(DialogEntryFlags.Legacy10 | DialogEntryFlags.AutoAdvanceTimer);
            float start = Time.realtimeSinceStartup;
            float deadline = start + 10f;
            while (showing.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline) {
                yield return null;
            }
            float held = Time.realtimeSinceStartup - start;

            Assert.AreNotEqual(UniTaskStatus.Pending, showing.Status,
                "the 0x40 record closes itself with no input, as the original's does at text speed 0");
            Assert.That(held, Is.GreaterThan(1.5f),
                "and it is read first: 118 ticks of the 59.17 Hz timer, about two seconds");
        }

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator WithoutTheFlagTheSameRecordWaitsForThePlayer() {
            // The control: at Slow a plain record has no deadline (DIALOG.C:227-231).
            UniTask<int> showing = Show(DialogEntryFlags.Legacy10);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline) {
                yield return null;
            }

            Assert.AreEqual(UniTaskStatus.Pending, showing.Status, "a plain record still waits for a key at Slow");
        }

        private UniTask<int> Show(DialogEntryFlags flags) {
            _go = new GameObject("DialogAutoAdvanceUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;
            document.sortingOrder = 10;   // the modal-dialog tier, as DialogOverlay sits

            var session = new GameSession();
            var executor = new DialogExecutor(NullLogger<DialogExecutor>.Instance, session, clock: null, provider: null);
            executor.UseResourceLoader(new OneDialogLoader(Record(flags)));

            var prefs = new FakePreferencesService();
            Assert.AreEqual(TextSpeed.Slow, prefs.Current.TextSpeed, "the setting the original was measured at");

            var manager = _go.AddComponent<DialogManager>();
            manager.Construct(cursorManager: null, stack: null, menuSound: null,
                gameSession: session, executor: executor, preferences: prefs);
            return manager.ShowById(DialogId);
        }

        private static Dialog Record(DialogEntryFlags flags) {
            var entry = new DialogEntry {
                Offset = 100,
                Id = DialogId,
                Text = Text,
                Flags = flags,
                Actions = new List<DialogActionBase>(),
                Branches = new List<DialogBranchBase>(),
            };
            var dialog = new Dialog("dial_z27");
            dialog.Entries.Add(entry);
            entry.Key = ContentKey.ForBase("ddx:dial_z27", entry.Offset);
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
