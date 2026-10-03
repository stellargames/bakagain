namespace BakAgain.Tests.PlayMode.UI.InGame {
    using System.Collections;
    using BakAgain.Core;
    using BakAgain.Tests.Editor.World;
    using BakAgain.UI.InGame;
    using BakAgain.UI.InputCore;
    using BakAgain.World;
    using GameData.Resources.Config;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The touch aids' pad buttons are named <c>touchpad_{actionId}</c> so ClassicMovementDriver's
    /// per-frame pick — and with it the original's hold-to-repeat — drives them exactly like the
    /// REQ compass arrows (spec 2026-09-29-android-touch-aids-design.md).
    /// </summary>
    public class TouchPadMovementTests {
        [UnityTest]
        public IEnumerator AHeldTouchPadTurnsLikeTheCompassArrow() {
            var host = new GameObject("TouchPadDoc");
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            var pad = new VisualElement {
                name = "touchpad_77", pickingMode = PickingMode.Position,
                style = { position = Position.Absolute, left = 0, top = 0, width = 200, height = 200 },
            };
            doc.rootVisualElement.Add(pad);
            yield return null;
            yield return null;   // layout

            // The driver maps screen (bottom-left) -> panel with ScreenToPanel(Screen.height - y);
            // invert it through the panel's own scale (screen px per panel point).
            Rect rootBound = doc.rootVisualElement.worldBound;
            float scale = Screen.width / rootBound.width;
            Vector2 centre = pad.worldBound.center * scale;
            var pointer = new FakePointer { IsPresent = false, CanPointOverride = true };
            pointer.ScreenPosition = new Vector2(centre.x, Screen.height - centre.y);
            pointer.Primary.SetDown(true, pressedThisFrame: true);

            var session = new GameSession();
            var prefs = new FakePreferencesService();
            prefs.Current.TurnSize = TurnSize.Medium;
            var cam = new GameObject("cam").AddComponent<Camera>();
            var movement = new PartyMovement(session, new MovementData("MOVEMENT.DAT") {
                StepDistances = new[] { 100, 200, 400 }, TurnAngles = new[] { 0x1000, 0x2000, 0x4000 },
                SecondsPerStep = new[] { 60, 120, 240 },
            }, prefs, cam, cameraHeightZ: 0, cameraPitch: 0);
            var driver = new ClassicMovementDriver(doc, movement, new FakeGameplayInput(), pointer, () => 1f / 60f);
            try {
                driver.Tick(active: true);
                // Left is +0x2000 at the Medium preset (ClassicMovementDriverTests), so right is -0x2000.
                Assert.AreEqual((short)-0x2000, session.Rotation, "a held touchpad_77 turns right at once");
            } finally {
                Object.DestroyImmediate(cam.gameObject);
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator AMouseClickOnACompassArrowTakesOneStep_ItsOwnClick() {
            // The arrow's Clickable calls PrimaryAction on release; stepping on the press frame too
            // made every mouse click two steps. Measured on the zone-1 road (t739, heading 135):
            // one click walked 2264 per axis where the original walks 1132 (TASK-739).
            var host = new GameObject("ArrowDoc");
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            var arrow = new VisualElement {
                name = "imagebutton_75", pickingMode = PickingMode.Position,
                style = { position = Position.Absolute, left = 0, top = 0, width = 200, height = 200 },
            };
            doc.rootVisualElement.Add(arrow);
            yield return null;
            yield return null;   // layout

            Rect rootBound = doc.rootVisualElement.worldBound;
            float scale = Screen.width / rootBound.width;
            Vector2 centre = arrow.worldBound.center * scale;
            var pointer = new FakePointer { IsPresent = true, CanPointOverride = true };
            pointer.ScreenPosition = new Vector2(centre.x, Screen.height - centre.y);
            pointer.Primary.SetDown(true, pressedThisFrame: true);

            var session = new GameSession();
            var prefs = new FakePreferencesService();
            prefs.Current.TurnSize = TurnSize.Medium;
            var cam = new GameObject("cam").AddComponent<Camera>();
            var movement = new PartyMovement(session, new MovementData("MOVEMENT.DAT") {
                StepDistances = new[] { 100, 200, 400 }, TurnAngles = new[] { 0x1000, 0x2000, 0x4000 },
                SecondsPerStep = new[] { 60, 120, 240 },
            }, prefs, cam, cameraHeightZ: 0, cameraPitch: 0);
            var driver = new ClassicMovementDriver(doc, movement, new FakeGameplayInput(), pointer, () => 1f / 60f);
            try {
                driver.Tick(active: true);
                Assert.AreEqual((short)0, session.Rotation, "the press leaves the first turn to the click");
                for (int i = 0; i < 25; i++) {   // 0.42 s held: one repeat after the 0.38 s dead time
                    pointer.Primary.SetDown(true, pressedThisFrame: false);
                    driver.Tick(active: true);
                }
                Assert.AreEqual((short)0x2000, session.Rotation, "a held arrow still repeats");
            } finally {
                Object.DestroyImmediate(cam.gameObject);
                Object.DestroyImmediate(host);
            }
        }
    }
}
