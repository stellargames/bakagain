namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.Core;
    using BakAgain.World.Scenes;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Scene;
    using NUnit.Framework;
    using System.Reflection;
    using UnityEngine;

    /// <summary>
    /// What the chapter-ending hotspot does.
    /// </summary>
    /// <remarks>
    /// <b>Action code 15 raises the world-loop exit request; leaving the scene is only the second
    /// half.</b> <c>TOWNSCN.C:569</c> is two lines — <c>nWorldLoopExitRequest = 1; exitFlag = 1;</c>
    /// — and the port had the exit without the request, so the click read as an ordinary way out
    /// and the chapter never turned.
    ///
    /// <para><b>GDS6A is the only scene in the shipped data carrying code 15</b>, and its
    /// <c>ActionDialogId</c> is 0, so there is no dialog behind it that could raise the request
    /// instead. If this arm does not write it, nothing does — which is why the omission was
    /// invisible: every other chapter transition comes from a dialog action.</para>
    /// </remarks>
    [TestFixture]
    public class EndChapterHotspotTests {
        private GameObject _go;
        private GameSession _session;
        private LocationScreen _screen;

        [SetUp]
        public void SetUp() {
            _go = new GameObject("EndChapterHotspotUnderTest");
            _screen = _go.AddComponent<LocationScreen>();
            _session = new GameSession();
            // Only the session matters to this arm; the rest of the screen's collaborators are not
            // reached before it returns.
            _screen.Construct(null, null, _session, null, null, null, null, null, null, null);
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        [Test]
        public void SceneDescription_UsesTheSceneItIsGiven_NotTheBoundOne() {
            // The entry animation's hold fires before ShowHotspotsAsync binds the screen, so a
            // description that reads _scene describes the location the party just LEFT: arriving in
            // Romney printed LaMut's text over Romney's square (2026-09-09). Passing null must still
            // fall back to the bound scene, which the two in-scene callers rely on.
            var dialogs = new ResolveRecordingDialogs();
            _screen.Construct(dialogs, null, _session, null, null, null, null, null, null, null);

            _screen.ShowSceneDescriptionAsync(new GdsScene("GDSTEST.DAT") { SceneDialogId = 4242 }).Forget();

            Assert.That(dialogs.LastResolved, Is.EqualTo(4242));
        }

        /// <summary>Records the id the description asked for; nothing else is reached.</summary>
        private sealed class ResolveRecordingDialogs : ClearCountingDialogs {
            public int LastResolved { get; private set; } = -1;
            public override UniTask<GameData.Resources.Dialog.DialogPlay> ResolveById(int id,
                System.Threading.CancellationToken cancellationToken = default) {
                LastResolved = id;
                return UniTask.FromResult<GameData.Resources.Dialog.DialogPlay>(null);
            }
        }

        [Test]
        public void ClearDescription_TakesTheDescriptionPanelDown() {
            // The description is shown with DisplayEntry, which leaves the panel up until something
            // clears it. Nothing did, so walking out of LaMut left the tavern's description painted
            // across the travel HUD for the rest of the session (2026-09-09). LocationScenePlayer
            // .Hide() calls this before deactivating the screen.
            var dialogs = new ClearCountingDialogs();
            _screen.Construct(dialogs, null, _session, null, null, null, null, null, null, null);

            _screen.ClearDescription();

            Assert.That(dialogs.Cleared, Is.EqualTo(1));
        }

        /// <summary>Counts <c>ClearDialog</c>; every other member is unreachable from this test.</summary>
        private class ClearCountingDialogs : BakAgain.UI.IDialogManager {
            public int Cleared { get; private set; }
            public void ClearDialog() => Cleared++;
            public void LetClicksThroughPanel() { }

            public UniTask ShowEntry(GameData.Resources.Dialog.DialogEntry entry,
                System.Threading.CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask ShowEntry(GameData.Resources.Dialog.DialogPlay play,
                System.Threading.CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask DisplayEntry(GameData.Resources.Dialog.DialogEntry entry,
                System.Threading.CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask DisplayEntry(GameData.Resources.Dialog.DialogPlay play,
                System.Threading.CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask<UnityEngine.UIElements.VisualElement> BuildStyledBoxAsync(
                GameData.Resources.Dialog.DialogEntry entry, UnityEngine.UIElements.VisualElement host)
                => UniTask.FromResult<UnityEngine.UIElements.VisualElement>(null);
            public virtual UniTask<GameData.Resources.Dialog.DialogPlay> ResolveById(int id,
                System.Threading.CancellationToken cancellationToken = default)
                => UniTask.FromResult<GameData.Resources.Dialog.DialogPlay>(null);
            public void SetActivePalette(Color[] palette) { }
            public UniTask<int> ShowById(int id,
                System.Threading.CancellationToken cancellationToken = default) => UniTask.FromResult(-1);
            public UniTask<bool> ShowConfirmById(int id,
                System.Threading.CancellationToken cancellationToken = default) => UniTask.FromResult(false);
            public UniTask<int> ShowChoiceById(int id,
                System.Threading.CancellationToken cancellationToken = default) => UniTask.FromResult(-1);
            public UniTask<int> ShowChoiceIndexById(int id,
                System.Threading.CancellationToken cancellationToken = default) => UniTask.FromResult(-1);
        }

        [Test]
        public void TheChapterEndHotspotRaisesTheWorldLoopExitRequest() {
            Assert.That(_session.ChapterTransitionPending, Is.EqualTo(0), "precondition");

            Dispatch(EndChapterCode);

            Assert.That(_session.ChapterTransitionPending,
                Is.EqualTo(GameData.Resources.GameState.ChapterTransition.AdvanceRequest));
        }

        [Test]
        public void AnOrdinaryExitDoesNotEndTheChapter() {
            // The arm next door: code 2 is dialog-only and leaves the request alone. Without this,
            // a fix that set the request unconditionally would look correct.
            Dispatch(DialogOnlyCode);

            Assert.That(_session.ChapterTransitionPending, Is.EqualTo(0));
        }

        /// <summary>Code 15 — the only action code that ends a chapter.</summary>
        private const int EndChapterCode = 15;

        /// <summary>Code 2 — shows its dialog and stays put.</summary>
        private const int DialogOnlyCode = 2;

        /// <summary>
        /// Runs one action code through the screen's own dispatch.
        /// </summary>
        /// <remarks>
        /// The EndChapter arm has no await before it writes, so the returned task has already run
        /// past the assignment by the time Invoke returns.
        /// </remarks>
        private void Dispatch(int actionCode) {
            MethodInfo dispatch = typeof(LocationScreen).GetMethod(
                "Dispatch", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(dispatch, Is.Not.Null, "LocationScreen.Dispatch was renamed");

            var hotspot = new GdsHotspot { ActionCode = actionCode };
            _ = (UniTask)dispatch.Invoke(_screen, new object[] { hotspot, 0, actionCode });
        }
    }
}
