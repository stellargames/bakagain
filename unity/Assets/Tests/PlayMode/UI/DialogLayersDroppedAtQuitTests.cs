namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.UI;
    using NUnit.Framework;

    /// <summary>
    /// Closing the game with a dialog up drops its input layers without a warning: the window is going away,
    /// not a panel torn down under a running show (build test harness, 2026-11-01: every run closed mid-dialog logged it).
    /// </summary>
    public class DialogLayersDroppedAtQuitTests {
        [Test]
        public void LayersDroppedWhileThePanelIsTornDown_Warn() =>
            Assert.IsTrue(DialogManager.WarnAboutDroppedLayers(dropped: 1, quitting: false));

        [Test]
        public void LayersDroppedWhileTheGameQuits_DoNotWarn() =>
            Assert.IsFalse(DialogManager.WarnAboutDroppedLayers(dropped: 1, quitting: true));

        [Test]
        public void NothingDropped_DoesNotWarn() =>
            Assert.IsFalse(DialogManager.WarnAboutDroppedLayers(dropped: 0, quitting: false));
    }
}
