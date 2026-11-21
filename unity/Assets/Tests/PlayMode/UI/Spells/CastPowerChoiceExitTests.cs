namespace BakAgain.Tests.PlayMode.UI.Spells {
    using System.Reflection;
    using BakAgain.UI.InputCore;
    using BakAgain.UI.Spells;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// While a power is being chosen the original reads only the ring, the click and Escape
    /// (UI_SelectSpellCost, CSPELL.C:2010-2079; menupage_run is not called), so a click on Exit and
    /// the M key do nothing there. Escape still closes. The port closed on all three (TASK-1128 row 5).
    /// </summary>
    public class CastPowerChoiceExitTests {
        private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject _go;
        private BakAgain.Tests.EnhancedPrefsStash _stash;

        private sealed class CountingNavigator : BakAgain.UI.Navigation.IScreenNavigator {
            public int Pops;
            public BakAgain.UI.Navigation.IScreen Current => null;
            public UniTask ResetTo(BakAgain.UI.Navigation.IScreen root) => UniTask.CompletedTask;
            public UniTask Push(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;
            public UniTask PushAndWaitAsync(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;
            public UniTask Pop() { Pops++; return UniTask.CompletedTask; }
            public UniTask Replace(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;
            public UniTask Clear() => UniTask.CompletedTask;
        }

        [SetUp]
        public void SetUp() => _stash = BakAgain.Tests.EnhancedPrefsStash.Take();

        [TearDown]
        public void TearDown() {
            if (_go != null) Object.DestroyImmediate(_go);
            _stash.Restore();
        }

        private CastScreen ChoosingAPower(FakePointer pointer, CountingNavigator nav) {
            _go = new GameObject("CastScreenUnderTest");
            _go.SetActive(false);
            var screen = _go.AddComponent<CastScreen>();
            screen.Construct(null, pointer, nav, null, new BakAgain.Core.GameSession(), null);
            typeof(CastScreen).GetField("_sliderSpell", Any).SetValue(screen, 3);
            return screen;
        }

        [Test]
        public void AClickOnExitDuringThePowerChoiceDoesNothing() {
            var pointer = new FakePointer();
            var nav = new CountingNavigator();
            CastScreen screen = ChoosingAPower(pointer, nav);
            pointer.Primary.SetDown(false, releasedThisFrame: true);   // the click that hit Exit

            screen.PrimaryAction(1);

            Assert.AreEqual(0, nav.Pops, "the spellbook stays");
            Assert.AreEqual(3, (int)typeof(CastScreen).GetField("_sliderSpell", Any).GetValue(screen), "and the choice goes on");
        }

        [Test]
        public void MDuringThePowerChoiceDoesNothing() {
            var nav = new CountingNavigator();
            CastScreen screen = ChoosingAPower(new FakePointer(), nav);
            Assert.IsTrue(screen.OnUnmatchedScancode(0x32), "M is still this screen's key");
            Assert.AreEqual(0, nav.Pops, "but it does not close the spellbook mid-choice");
        }

        [Test]
        public void EscapeDuringThePowerChoiceStillCloses() {
            var nav = new CountingNavigator();
            CastScreen screen = ChoosingAPower(new FakePointer(), nav);   // no button: a key
            screen.PrimaryAction(1);
            Assert.AreEqual(1, nav.Pops);
        }
    }
}
