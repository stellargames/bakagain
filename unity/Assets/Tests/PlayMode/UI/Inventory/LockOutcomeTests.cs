namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using NUnit.Framework;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// When the picklock screen's answer becomes available.
    /// </summary>
    /// <remarks>
    /// <b><c>IScreenNavigator.Push</c> returns once the screen is SHOWN, not once it closes.</b>
    /// Three world handlers read <c>LockOpened</c> on the line after awaiting the push — a frame or
    /// two after the screen appeared and long before the player had touched the lock — so it was
    /// always false. A chest the party picked open stayed locked, a door stayed shut, a crossing
    /// stayed barred; the screen looked right and said the right thing, and nothing downstream of
    /// it ever ran.
    ///
    /// <para>These pin the timing rather than the outcome: the answer must not be available while
    /// the screen is still up, and leaving by Exit must answer rather than hang the caller.</para>
    /// </remarks>
    [TestFixture]
    public class LockOutcomeTests {
        private GameObject _go;
        private PanelSettings _panelSettings;
        private GameSession _session;
        private InventoryMenu _menu;

        [SetUp]
        public void SetUp() {
            _go = new GameObject("LockOutcomeUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;

            _menu = _go.AddComponent<InventoryMenu>();
            _session = new GameSession();
            _menu.Construct(_session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<
                        BakAgain.Core.Services.DialogExecutor>(),
                    _session, new BakAgain.Core.Services.GameClock(_session)),
                new NoOpDialogs());
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        [UnityTest]
        public IEnumerator TheAnswerIsNotAvailableWhileTheScreenIsStillUp() {
            GivenSomethingToTryWith();
            Assert.That(_menu.SetLock(20), Is.True);

            UniTask<bool> outcome = _menu.LockOutcomeAsync();
            yield return null;
            yield return null;

            // The whole defect in one assertion: this used to be answerable immediately, which is
            // what let a caller read "not opened" a frame after the lock appeared on screen.
            Assert.That(outcome.Status, Is.EqualTo(UniTaskStatus.Pending));
        }

        [UnityTest]
        public IEnumerator LeavingTheLockAloneAnswersFalseRatherThanHangingTheCaller() {
            GivenSomethingToTryWith();
            _menu.SetLock(20);
            UniTask<bool> outcome = _menu.LockOutcomeAsync();

            yield return _menu.HideAsync().ToCoroutine();

            Assert.That(outcome.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(outcome.GetAwaiter().GetResult(), Is.False);
        }

        [Test]
        public void AScreenThatWasNeverShowingALockAnswersFalse() {
            // The handlers call this unconditionally after a push; with no lock in play it must
            // return an already-completed false rather than a null task.
            UniTask<bool> outcome = _menu.LockOutcomeAsync();

            Assert.That(outcome.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(outcome.GetAwaiter().GetResult(), Is.False);
        }

        /// <summary>SetLock refuses outright when the working set comes out empty.</summary>
        private void GivenSomethingToTryWith() {
            var pack = new RuntimeContainer {
                Capacity = 4,
                ContainerType = SaveGameContainerType.Inventory,
            };
            pack.Items.Add(new RuntimeItem(
                (byte)GameData.Resources.Character.LockPicking.LockpickObjectId, 5, 0));
            _session.SetActorInventoryForTest(0, pack);
        }
    }
}
