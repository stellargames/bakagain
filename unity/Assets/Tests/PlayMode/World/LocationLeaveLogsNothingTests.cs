namespace BakAgain.Tests.PlayMode.World {
    using System.Collections.Generic;
    using BakAgain.Core;
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
        private readonly List<string> _warnings = new();

        private void OnLog(string message, string stack, LogType type) {
            if (type == LogType.Warning && message.Contains("has no hotspot")) _warnings.Add(message);
        }

        [SetUp]
        public void SetUp() => Application.logMessageReceived += OnLog;

        [TearDown]
        public void TearDown() {
            Application.logMessageReceived -= OnLog;
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        [Test]
        public void LeaveSceneIsNotLookedUpAsAHotspot_AnUnknownIdStillWarns() {
            _go = new GameObject("LocationLeaveUnderTest");
            var screen = _go.AddComponent<LocationScreen>();
            screen.Construct(null, null, new GameSession(), null, null, null, null, null, null, null);
            screen.PrimaryAction(GdsActionDispatch.LeaveSceneActionId);
            Assert.IsEmpty(_warnings, "Esc (action 1) is the way out, not a missing hotspot");
            screen.PrimaryAction(999);
            Assert.AreEqual(1, _warnings.Count, "control: an id with no hotspot still warns");
        }
    }
}
