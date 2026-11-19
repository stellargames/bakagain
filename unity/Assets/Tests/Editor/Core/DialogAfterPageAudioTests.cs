namespace BakAgain.Tests.Editor.Core {
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using GameData.Resources.Dialog;
    using GameData.Resources.Dialog.Actions;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using NUnit.Framework;

    /// <summary>
    /// A line's "after" sound (op 12, pass 3) plays when its page is dismissed, not while it is
    /// resolved: DIALOG.C:1445 runs pass 3 after the wait. The port played it before the page was
    /// drawn (TASK-1113 row 3; 53 shipped, e.g. DIAL_Z20 @39196).
    /// </summary>
    public class DialogAfterPageAudioTests {
        private readonly List<int> _cues = new();

        [SetUp]
        public void SetUp() {
            _cues.Clear();
            BakAgain.Audio.MenuSoundService.CueRequested += _cues.Add;
        }

        [TearDown]
        public void TearDown() => BakAgain.Audio.MenuSoundService.CueRequested -= _cues.Add;

        private static DialogEntry Line(string text) => new DialogEntry {
            Text = text,
            Actions = new List<DialogActionBase> {
                new PlayAudioAction { AudioId = 41, Timing = PlayAudioTiming.AfterActions },
            },
        };

        [Test]
        public void AShownLinesAfterSoundWaitsForThePage() {
            var session = new GameSession();
            var executor = new DialogExecutor(new NullLogger<DialogExecutor>(), session, new GameClock(session));
            DialogEntry line = Line("A page with words on it.");

            executor.ApplyEntryGameplayActionsForTest(line);
            CollectionAssert.IsEmpty(_cues, "not while the line resolves");

            executor.PlayAfterPageAudio(line);
            CollectionAssert.AreEqual(new[] { 41 }, _cues, "when its page is dismissed");
        }

        [Test]
        public void ATextlessRoutersAfterSoundPlaysAtOnce() {
            var session = new GameSession();
            var executor = new DialogExecutor(new NullLogger<DialogExecutor>(), session, new GameClock(session));

            executor.ApplyEntryGameplayActionsForTest(Line(null));

            CollectionAssert.AreEqual(new[] { 41 }, _cues, "no page to wait for");
        }
    }
}
