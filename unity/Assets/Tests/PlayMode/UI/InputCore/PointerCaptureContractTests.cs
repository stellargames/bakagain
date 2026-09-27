namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Characterisation tests for the UI Toolkit behaviour an event-driven drag depends on, so the
    /// decision to move off polled input (task-48) rests on measurement rather than assumption.
    ///
    /// <para>What is pinned here: a <see cref="PointerManipulator"/> that captures the pointer on
    /// press keeps receiving move and up events after the pointer leaves its target's bounds. That
    /// is the one property the current polled gesture provides by construction and an event-driven
    /// one does not — if it did not hold, a drag would silently stop the moment the cursor left the
    /// item cell.</para>
    ///
    /// <para>Events are synthesized and sent rather than injected at the device layer. That is
    /// already the house pattern (<c>ItemGridRendererTests.SimulateClick</c> in tests,
    /// <c>NavigableLayer.DispatchPointer</c> in production) and it sidesteps the device-injection
    /// flakiness that made this unmeasurable through <c>InputInjector</c> — the box's real pointer
    /// re-asserts (0,0) every frame and overwrites injected positions.</para>
    /// </summary>
    public class PointerCaptureContractTests {
        // The manipulator under test: the minimal shape an item-drag manipulator would take.
        private sealed class RecordingDragManipulator : PointerManipulator {
            public readonly List<string> Log = new List<string>();

            public RecordingDragManipulator(VisualElement target) { this.target = target; }

            protected override void RegisterCallbacksOnTarget() {
                target.RegisterCallback<PointerDownEvent>(OnDown);
                target.RegisterCallback<PointerMoveEvent>(OnMove);
                target.RegisterCallback<PointerUpEvent>(OnUp);
            }

            protected override void UnregisterCallbacksFromTarget() {
                target.UnregisterCallback<PointerDownEvent>(OnDown);
                target.UnregisterCallback<PointerMoveEvent>(OnMove);
                target.UnregisterCallback<PointerUpEvent>(OnUp);
            }

            private void OnDown(PointerDownEvent e) {
                target.CapturePointer(e.pointerId);
                Log.Add($"down captured={target.HasPointerCapture(e.pointerId)}");
            }

            private void OnMove(PointerMoveEvent e) =>
                Log.Add($"move inBounds={target.worldBound.Contains(new Vector2(e.position.x, e.position.y))}");

            private void OnUp(PointerUpEvent e) {
                Log.Add($"up captured={target.HasPointerCapture(e.pointerId)}");
                target.ReleasePointer(e.pointerId);
            }
        }

        private GameObject _go;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        // A live runtime panel with one small element; layout is forced so worldBound resolves
        // within this single-frame test (same trick as ItemGridRendererTests.AttachToRuntimePanel).
        private VisualElement BuildCell() {
            _go = new GameObject("PointerCaptureContract");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;

            var cell = new VisualElement {
                name = "cell",
                style = { position = Position.Absolute, left = 0, top = 0, width = 100, height = 100 },
            };
            document.rootVisualElement.Add(cell);

            MethodInfo validateLayout = document.rootVisualElement.panel.GetType()
                .GetMethod("ValidateLayout", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            validateLayout?.Invoke(document.rootVisualElement.panel, null);
            return cell;
        }

        private static void Send<T>(VisualElement target, Vector2 position, EventType type,
            bool leftButton, int clickCount = 1)
            where T : PointerEventBase<T>, new() {
            // clickCount must be set explicitly: a bare `new Event{...}` reports 0, and production
            // now reads it (the double-click rule uses UI Toolkit's count, not our own timer).
            var imgui = new Event {
                type = type, mousePosition = position, button = leftButton ? 0 : -1, clickCount = clickCount,
            };
            using (T evt = PointerEventBase<T>.GetPooled(imgui)) {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        [Test]
        public void CapturedPointer_KeepsDeliveringMovesAfterLeavingTheTargetBounds() {
            VisualElement cell = BuildCell();
            var drag = new RecordingDragManipulator(cell);
            cell.AddManipulator(drag);

            Vector2 inside = cell.worldBound.center;
            var outside = new Vector2(cell.worldBound.xMax + 500f, cell.worldBound.yMax + 500f);

            Send<PointerDownEvent>(cell, inside, EventType.MouseDown, leftButton: true);
            Send<PointerMoveEvent>(cell, outside, EventType.MouseDrag, leftButton: true);
            Send<PointerUpEvent>(cell, outside, EventType.MouseUp, leftButton: true);

            CollectionAssert.AreEqual(
                new[] { "down captured=True", "move inBounds=False", "up captured=True" },
                drag.Log,
                "a captured pointer must keep delivering move/up to the capturing element after the "
                + "pointer has left its bounds — this is what an event-driven drag relies on");
        }

        /// <summary>
        /// The recogniser's click-vs-drag rule: a press that never travels past the threshold is a
        /// click (<c>wasDrag == false</c>), one that does is a drag. This lives here rather than in a
        /// screen's suite because it is a property of the gesture, not of any screen — the screen
        /// only decides what a click and a drag <i>mean</i>.
        /// </summary>
        [Test]
        public void Recogniser_DistinguishesAClickFromADrag() {
            VisualElement cell = BuildCell();
            var log = new List<string>();
            // Zero double-click window so both presses count as 1 — this test is about
            // click-vs-drag, and click counting is covered by its own tests below.
            var gesture = new BakAgain.UI.InputCore.DragGestureManipulator(
                cell, dragThreshold: 20f, doubleClickSeconds: 0f);
            gesture.Pressed += (_, clicks) => log.Add("pressed clicks=" + clicks);
            gesture.DragStarted += _ => log.Add("dragStarted");
            gesture.Released += (_, wasDrag) => log.Add("released wasDrag=" + wasDrag);
            cell.AddManipulator(gesture);

            Vector2 origin = cell.worldBound.center;

            // Barely moved -> a click.
            Send<PointerDownEvent>(cell, origin, EventType.MouseDown, leftButton: true);
            Send<PointerMoveEvent>(cell, origin + new Vector2(3f, 0f), EventType.MouseDrag, leftButton: true);
            Send<PointerUpEvent>(cell, origin + new Vector2(3f, 0f), EventType.MouseUp, leftButton: true);

            // Well past the threshold -> a drag.
            Send<PointerDownEvent>(cell, origin, EventType.MouseDown, leftButton: true);
            Send<PointerMoveEvent>(cell, origin + new Vector2(500f, 0f), EventType.MouseDrag, leftButton: true);
            Send<PointerUpEvent>(cell, origin + new Vector2(500f, 0f), EventType.MouseUp, leftButton: true);

            CollectionAssert.AreEqual(
                new[] {
                    "pressed clicks=1", "released wasDrag=False",
                    "pressed clicks=1", "dragStarted", "released wasDrag=True",
                },
                log);
        }

        /// <summary>
        /// The threshold is settable and read per event, so a host whose value arrives with
        /// asynchronously-loaded data can apply it to the recogniser it already mounted — and does
        /// not have to replace it, which is the point: a replacement mid-gesture would throw away
        /// the pointer capture the old recogniser was holding, stranding the drag.
        /// </summary>
        [Test]
        public void DragThreshold_ChangesTakeEffectOnTheLiveRecogniser_WithoutLosingCapture() {
            VisualElement cell = BuildCell();
            var log = new List<string>();
            var gesture = new BakAgain.UI.InputCore.DragGestureManipulator(
                cell, dragThreshold: 20f, doubleClickSeconds: 0f);
            gesture.DragStarted += _ => log.Add("dragStarted");
            gesture.Released += (_, wasDrag) => log.Add("released wasDrag=" + wasDrag);
            cell.AddManipulator(gesture);

            Vector2 origin = cell.worldBound.center;

            // Raise the threshold mid-press: the SAME move that would have started a drag at 20 no
            // longer does, and the capture taken on the press is still held when the release lands.
            Send<PointerDownEvent>(cell, origin, EventType.MouseDown, leftButton: true);
            Assert.IsTrue(cell.HasPointerCapture(PointerId.mousePointerId), "captured on press");
            gesture.DragThreshold = 500f;
            Send<PointerMoveEvent>(cell, origin + new Vector2(100f, 0f), EventType.MouseDrag, leftButton: true);
            Assert.IsTrue(cell.HasPointerCapture(PointerId.mousePointerId),
                "changing the threshold must not disturb the in-flight gesture");
            Send<PointerUpEvent>(cell, origin + new Vector2(100f, 0f), EventType.MouseUp, leftButton: true);

            // Lower it again: the same 100px move is now a drag.
            gesture.DragThreshold = 10f;
            Send<PointerDownEvent>(cell, origin, EventType.MouseDown, leftButton: true);
            Send<PointerMoveEvent>(cell, origin + new Vector2(100f, 0f), EventType.MouseDrag, leftButton: true);
            Send<PointerUpEvent>(cell, origin + new Vector2(100f, 0f), EventType.MouseUp, leftButton: true);

            CollectionAssert.AreEqual(
                new[] { "released wasDrag=False", "dragStarted", "released wasDrag=True" }, log);
        }

        /// <summary>
        /// The recogniser counts clicks itself rather than trusting <c>IPointerEvent.clickCount</c>,
        /// which was measured unusable at runtime (2026-07-30: a press 1.5 s after the previous one
        /// still reported 2). These drive the reset conditions directly, so a regression to reading
        /// the platform field would fail here — the earlier test could not, because it supplied the
        /// count itself and only checked the code read it back.
        /// </summary>
        [Test]
        public void ClickCount_IncrementsInsideTheWindow() {
            VisualElement cell = BuildCell();
            var counts = new List<int>();
            var gesture = new BakAgain.UI.InputCore.DragGestureManipulator(
                cell, dragThreshold: 20f, doubleClickSeconds: 60f); // effectively "always inside"
            gesture.Pressed += (_, clicks) => counts.Add(clicks);
            cell.AddManipulator(gesture);

            Vector2 p = cell.worldBound.center;
            for (int i = 0; i < 3; i++) {
                Send<PointerDownEvent>(cell, p, EventType.MouseDown, leftButton: true, clickCount: 99);
                Send<PointerUpEvent>(cell, p, EventType.MouseUp, leftButton: true, clickCount: 99);
            }

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, counts,
                "the count must come from the recogniser, not from the event's own clickCount (99)");
        }

        [Test]
        public void ClickCount_ResetsOnceTheWindowHasElapsed() {
            VisualElement cell = BuildCell();
            var counts = new List<int>();
            // A zero-length window: every press is outside it, so every press is a fresh click.
            var gesture = new BakAgain.UI.InputCore.DragGestureManipulator(
                cell, dragThreshold: 20f, doubleClickSeconds: 0f);
            gesture.Pressed += (_, clicks) => counts.Add(clicks);
            cell.AddManipulator(gesture);

            Vector2 p = cell.worldBound.center;
            for (int i = 0; i < 3; i++) {
                Send<PointerDownEvent>(cell, p, EventType.MouseDown, leftButton: true);
                Send<PointerUpEvent>(cell, p, EventType.MouseUp, leftButton: true);
            }

            CollectionAssert.AreEqual(new[] { 1, 1, 1 }, counts,
                "outside the window each press starts a new run — the bug JvE hit was this never "
                + "resetting, so every second click read as a double-click");
        }

        [Test]
        public void ClickCount_ResetsWhenThePressMovesTooFar() {
            VisualElement cell = BuildCell();
            var counts = new List<int>();
            var gesture = new BakAgain.UI.InputCore.DragGestureManipulator(
                cell, dragThreshold: 20f, doubleClickSeconds: 60f);
            gesture.Pressed += (_, clicks) => counts.Add(clicks);
            cell.AddManipulator(gesture);

            Vector2 p = cell.worldBound.center;
            Send<PointerDownEvent>(cell, p, EventType.MouseDown, leftButton: true);
            Send<PointerUpEvent>(cell, p, EventType.MouseUp, leftButton: true);
            // Second press well beyond the threshold: a different thing was clicked.
            Vector2 far = p + new Vector2(200f, 0f);
            Send<PointerDownEvent>(cell, far, EventType.MouseDown, leftButton: true);
            Send<PointerUpEvent>(cell, far, EventType.MouseUp, leftButton: true);

            CollectionAssert.AreEqual(new[] { 1, 1 }, counts,
                "clicking two different places is two single clicks, however fast");
        }

        /// <summary>
        /// Every REQ button/hotspot carries a <see cref="Clickable"/>, and Clickable captures the
        /// pointer on its own pointer-down — stealing the capture this recogniser just took on the
        /// same press (last capturer wins). The captured release then goes exclusively to the
        /// child, so the recogniser never sees the up. It must notice the theft
        /// (PointerCaptureOutEvent) and reset; the stranded "pressed" state used to swallow the
        /// next press — the dud click after switching members in the inventory (JvE, 2026-08-02).
        /// </summary>
        [Test]
        public void CaptureStolenByAChildClickable_ResetsTheRecogniser_SoTheNextPressIsSeen() {
            VisualElement stage = BuildCell();
            var chrome = new VisualElement {
                name = "chrome",
                style = { position = Position.Absolute, left = 10, top = 10, width = 30, height = 30 },
            };
            stage.Add(chrome);
            chrome.AddManipulator(new Clickable(() => { })); // the REQ hotspot pattern
            MethodInfo validateLayout = stage.panel.GetType()
                .GetMethod("ValidateLayout", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            validateLayout?.Invoke(stage.panel, null);

            var log = new List<string>();
            var gesture = new BakAgain.UI.InputCore.DragGestureManipulator(
                stage, dragThreshold: 20f, doubleClickSeconds: 0f);
            gesture.Pressed += (_, clicks) => log.Add("pressed");
            gesture.Released += (_, wasDrag) => log.Add("released");
            stage.AddManipulator(gesture);

            // Press on the chrome child: the recogniser sees the down (trickle) and captures; the
            // child's Clickable then steals the capture, and receives the up exclusively.
            Vector2 onChrome = chrome.worldBound.center;
            Send<PointerDownEvent>(chrome, onChrome, EventType.MouseDown, leftButton: true);
            Send<PointerUpEvent>(chrome, onChrome, EventType.MouseUp, leftButton: true);
            Assert.IsFalse(gesture.IsPressed,
                "losing the capture to the child must reset the recogniser — a stale press here is "
                + "what swallowed the next click");

            // The next press, away from the chrome, must be recognised first time.
            var onStage = new Vector2(stage.worldBound.xMax - 5f, stage.worldBound.yMax - 5f);
            Send<PointerDownEvent>(stage, onStage, EventType.MouseDown, leftButton: true);
            Send<PointerUpEvent>(stage, onStage, EventType.MouseUp, leftButton: true);

            CollectionAssert.AreEqual(new[] { "pressed", "pressed", "released" }, log,
                "press 1 = seen then stolen (no release here); press 2 = a full press+release — "
                + "NOT swallowed by press 1's stale state");
        }

        [Test]
        public void Manipulator_StopsReceiving_OnceRemoved() {
            VisualElement cell = BuildCell();
            var drag = new RecordingDragManipulator(cell);
            cell.AddManipulator(drag);
            cell.RemoveManipulator(drag);

            Send<PointerDownEvent>(cell, cell.worldBound.center, EventType.MouseDown, leftButton: true);

            CollectionAssert.IsEmpty(drag.Log,
                "RemoveManipulator must unhook the callbacks — teardown correctness for grid rebuilds");
        }

        // --- keyboard activation shares the pointer path's click counter ---

        /// <summary>
        /// Enter is a confirm-click in the original (canassa MENUPAGE.C:444-445 —
        /// key_is_down(0x1c) feeds the same value the left mouse button does), so two quick Enters
        /// on the same item hit the same double-click deadline two mouse clicks do. Keyboard
        /// activation must therefore join the recogniser's run rather than run a second timer.
        /// </summary>
        [Test]
        public void CountActivation_CountsASecondActivationAtTheSameSpot_AsADoubleClick() {
            var target = new VisualElement();
            var gesture = new BakAgain.UI.InputCore.DragGestureManipulator(target, dragThreshold: 20f);

            Assert.AreEqual(1, gesture.CountActivation(new Vector2(100f, 100f)));
            Assert.AreEqual(2, gesture.CountActivation(new Vector2(100f, 100f)));
        }

        [Test]
        public void CountActivation_RestartsTheRun_WhenTheSecondActivationIsElsewhere() {
            var target = new VisualElement();
            var gesture = new BakAgain.UI.InputCore.DragGestureManipulator(target, dragThreshold: 20f);

            Assert.AreEqual(1, gesture.CountActivation(new Vector2(100f, 100f)));
            Assert.AreEqual(1, gesture.CountActivation(new Vector2(900f, 900f)),
                "a different cell is a fresh click run, matching the original's "
                + "*p_selected_slot == old_sel guard");
        }

        [Test]
        public void CountActivation_RestartsTheRun_AfterTheWindowElapses() {
            var target = new VisualElement();
            var gesture = new BakAgain.UI.InputCore.DragGestureManipulator(target, dragThreshold: 20f,
                doubleClickSeconds: 0f); // window already closed by the time the next call lands

            Assert.AreEqual(1, gesture.CountActivation(new Vector2(100f, 100f)));
            Assert.AreEqual(1, gesture.CountActivation(new Vector2(100f, 100f)));
        }
    }
}
