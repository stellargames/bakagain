namespace BakAgain.Tests.PlayMode.UI.InGame {
    using BakAgain.UI.InGame;
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// A press the screen above closed on is not also a click on the travel viewport.
    /// </summary>
    /// <remarks>
    /// TASK-935: the cast screen commits a spell on the press and pops in that frame, so the press
    /// reached the travel HUD rebuilt under it and its release clicked the arena — River Song summoned
    /// on the cell under the spell's symbol. The original's menu acts on a release only for a press
    /// anchored on the same entry (MENUPAGE.C menupage_input_poll).
    /// </remarks>
    [TestFixture]
    public class TravelHudIgnoresThePressOfTheScreenAboveTests {
        private GameObject _go;
        private InGameScreen _hud;
        private FakePointer _pointer;

        [SetUp]
        public void SetUp() {
            _go = new GameObject("TravelHudUnderTest");
            _go.SetActive(false);   // no Awake/Update: this is the click rule, not the screen
            _hud = _go.AddComponent<InGameScreen>();
            _pointer = new FakePointer();
            _hud.Construct(null, null, null, null, null, null, null, null, null, null, null, null, null,
                _pointer, null, null, null, null, null);
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        [Test]
        public void ShownUnderAHeldPress_TheViewportIgnoresThatClickOnly() {
            _pointer.Primary.SetDown(true, pressedThisFrame: true);

            _hud.NoteRevealUnderAPress();

            Assert.IsFalse(_hud.TakesViewportClick(), "the cast screen's press is not an arena click");
            Assert.IsTrue(_hud.TakesViewportClick(), "the next click is the player's own");
        }

        [Test]
        public void ShownWithTheButtonUp_TheFirstClickCounts() {
            // The control: a guard that ate every first click would pass the test above.
            _pointer.Primary.SetDown(false);

            _hud.NoteRevealUnderAPress();

            Assert.IsTrue(_hud.TakesViewportClick());
        }
    }
}
