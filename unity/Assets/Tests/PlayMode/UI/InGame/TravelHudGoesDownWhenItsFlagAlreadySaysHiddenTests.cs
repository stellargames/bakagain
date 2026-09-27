namespace BakAgain.Tests.PlayMode.UI.InGame {
    using BakAgain.UI.InGame;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Hiding the travel HUD keys on whether it is ON SCREEN, not on its <c>IsVisible</c> flag.
    /// </summary>
    /// <remarks>
    /// The two disagree for a whole fade: <see cref="InGameScreen.HideAsync"/> clears
    /// <c>_visible</c> before awaiting the fade and only deactivates the GameObject after it. A
    /// screen caught in that window answers <c>IsVisible == false</c> while still painting, and the
    /// old early-return meant a second hide could not take it down at all —
    /// <c>LocationScenePlayer.HideTravelScreenAsync</c> asked and nothing happened, so REQ_MAIN's
    /// six round buttons stayed over the location description (TASK-620).
    ///
    /// <para>Measured in Romney 2026-09-23: <c>active=True IsVisible=False</c>, and in that state
    /// the location screen (sortingOrder -3) draws behind the HUD (-2).</para>
    /// </remarks>
    [TestFixture]
    public class TravelHudGoesDownWhenItsFlagAlreadySaysHiddenTests {
        private GameObject _go;
        private InGameScreen _hud;

        [SetUp]
        public void SetUp() {
            // Built inactive so AddComponent does not run Awake yet, matching the sibling fixture.
            _go = new GameObject("StrayTravelHud");
            _go.SetActive(false);
            _hud = _go.AddComponent<InGameScreen>();
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        [Test]
        public void AHudLeftActiveWhileItsFlagReadsHiddenIsStillTakenDown() {
            // Awake deactivates the screen again ("start hidden"), and it only runs once — so the
            // second activation is the real stray state: on screen, with _visible never set.
            _go.SetActive(true);
            _go.SetActive(true);
            Assume.That(_go.activeSelf, Is.True, "the stray state is an ACTIVE GameObject");
            Assume.That(_hud.IsVisible, Is.False, "...whose flag already says hidden");

            _hud.HideAsync();

            Assert.That(_go.activeSelf, Is.False,
                "a hide must take the HUD off screen even when IsVisible already reads false — "
                + "otherwise nothing can stop it painting over the location (TASK-620)");
        }

        [Test]
        public void AHudThatIsAlreadyOffScreenIsLeftAlone() {
            // The control: without it the test above passes for a HideAsync that deactivates
            // unconditionally, which would be a different bug (tearing down a screen nobody raised).
            _go.SetActive(true);   // Awake -> inactive
            Assume.That(_go.activeSelf, Is.False);

            _hud.HideAsync();

            Assert.That(_go.activeSelf, Is.False);
            Assert.That(_hud.IsVisible, Is.False);
        }
    }
}
