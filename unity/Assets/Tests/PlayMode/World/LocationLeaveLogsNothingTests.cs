namespace BakAgain.Tests.PlayMode.World {
    using System.Text.RegularExpressions;
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.World.Scenes;
    using GameData.Resources.Scene;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Esc on a town scene (action 1, Leave Scene) leaves it without a warning; an id with no hotspot
    /// still warns. Linux dev build, 2026-11-05: every Esc on LaMut logged "action id 1 has no hotspot".
    /// </summary>
    public class LocationLeaveLogsNothingTests {
        private GameObject _go;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        [Test]
        public void LeaveSceneIsNotLookedUpAsAHotspot_AnUnknownIdStillWarns() {
            _go = new GameObject("LocationLeaveUnderTest");
            var screen = _go.AddComponent<LocationScreen>();
            screen.Construct(null, null, new GameSession(), null, null, null, null, null, null, null);
            // Captured, not just counted: the control's warning is provoked on purpose and must not
            // reach the log (TASK-1004).
            using ExpectedLog.Capture warnings = ExpectedLog.CaptureAll(new Regex("has no hotspot"));
            screen.PrimaryAction(GdsActionDispatch.LeaveSceneActionId);
            Assert.IsEmpty(warnings.Lines, "Esc (action 1) is the way out, not a missing hotspot");
            screen.PrimaryAction(999);
            Assert.AreEqual(1, warnings.Lines.Count, "control: an id with no hotspot still warns");
        }
    }
}
