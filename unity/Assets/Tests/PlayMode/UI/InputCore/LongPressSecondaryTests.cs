namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A resting finger is the secondary press, because a finger has no second button.
    /// </summary>
    /// <remarks>
    /// Right-click opens the examine/describe path throughout this port, and <c>e.button == 1</c> is
    /// a thing touch never synthesises — so before this a touch player lost a whole verb. TASK-67 §3.
    ///
    /// <para><b>The hold is 0.5 s because Android says so</b> —
    /// <c>ViewConfiguration.getLongPressTimeout()</c>. The task called the duration "a feel value
    /// needing a device"; taking the platform's own published number is not a feel judgement, and it
    /// is what makes the gesture match every other app on the phone. These tests inject a short one
    /// so the suite does not spend half a second per case.</para>
    ///
    /// <para><b>Events carry a pointerType</b>, which an IMGUI-sourced event cannot — the house
    /// <c>Send</c> helper in <c>PointerCaptureContractTests</c> builds from a <c>UnityEngine.Event</c>
    /// and always reports a mouse. <see cref="StubPointerEvent"/> exists for that one reason.</para>
    /// </remarks>
    public class LongPressSecondaryTests {
        private const float Hold = 0.05f;
        private const float PastTheHold = 0.25f;

        /// <summary>An <see cref="IPointerEvent"/> with a settable pointer type, for GetPooled.</summary>
        private sealed class StubPointerEvent : IPointerEvent {
            public int pointerId { get; set; }
            public string pointerType { get; set; } = UnityEngine.UIElements.PointerType.touch;
            public bool isPrimary => true;
            public int button { get; set; }
            public int pressedButtons => 0;
            public Vector3 position { get; set; }
            public Vector3 localPosition => position;
            public Vector3 deltaPosition => Vector3.zero;
            public float deltaTime => 0f;
            public int clickCount => 1;
            public float pressure => 1f;
            public float tangentialPressure => 0f;
            public float altitudeAngle => 0f;
            public float azimuthAngle => 0f;
            public float twist => 0f;
            public Vector2 tilt => Vector2.zero;
            public PenStatus penStatus => PenStatus.None;
            public Vector2 radius => Vector2.zero;
            public Vector2 radiusVariance => Vector2.zero;
            public EventModifiers modifiers => EventModifiers.None;
            public bool shiftKey => false;
            public bool ctrlKey => false;
            public bool commandKey => false;
            public bool altKey => false;
            public bool actionKey => false;
        }

        private GameObject _host;
        private VisualElement _stage;
        private DragGestureManipulator _gesture;
        private List<Vector2> _secondary;
        private List<Vector2> _pressed;
        private List<bool> _released;

        [SetUp]
        public void SetUp() {
            // A panel is required: the scheduler only runs for an element attached to one.
            _host = new GameObject("LongPressTestDoc");
            UIDocument doc = _host.AddComponent<UIDocument>();
            doc.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _stage = new VisualElement { style = { width = 400, height = 400 } };
            doc.rootVisualElement.Add(_stage);

            _secondary = new List<Vector2>();
            _pressed = new List<Vector2>();
            _released = new List<bool>();
            _gesture = new DragGestureManipulator(_stage, dragThreshold: 10f, longPressSeconds: Hold);
            _gesture.SecondaryPressed += p => _secondary.Add(p);
            _gesture.Pressed += (p, _) => _pressed.Add(p);
            _gesture.Released += (_, wasDrag) => _released.Add(wasDrag);
            _stage.AddManipulator(_gesture);
        }

        [TearDown]
        public void TearDown() {
            if (_host != null) { Object.DestroyImmediate(_host); }
        }

        private void Send<T>(Vector2 at, string type, int button = 0, int pointerId = 1)
            where T : PointerEventBase<T>, new() {
            var stub = new StubPointerEvent {
                pointerId = pointerId, pointerType = type, button = button,
                position = new Vector3(at.x, at.y, 0f),
            };
            using (T evt = PointerEventBase<T>.GetPooled(stub)) {
                evt.target = _stage;
                _stage.SendEvent(evt);
            }
        }

        /// <summary>
        /// Waits until the hold has passed on the REAL clock, then one more frame.
        /// </summary>
        /// <remarks>
        /// <b>Not <c>WaitForSeconds</c> (TASK-671).</b> The hold is a UI Toolkit scheduler item: it
        /// fires in a panel update that runs AFTER its due time, on the real clock. <c>WaitForSeconds</c>
        /// counts game time, and one slow frame after the press (software GL on a CI runner, capped at
        /// <c>Time.maximumDeltaTime</c>) can use up the whole wait before any panel update runs past the
        /// due time — the assert then sees nothing although the game would fire on its next frame. That
        /// failed CI and nowhere else, and it also let the "must NOT fire" cases pass without the
        /// scheduler ever running. The extra frame guarantees a panel update past the due time.
        /// </remarks>
        private static IEnumerator PastTheHoldOnTheRealClock() {
            float until = Time.realtimeSinceStartup + PastTheHold;
            while (Time.realtimeSinceStartup < until) { yield return null; }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AFingerHeldStillRaisesSecondary() {
            Send<PointerDownEvent>(new Vector2(50f, 50f), UnityEngine.UIElements.PointerType.touch);
            Assert.AreEqual(1, _pressed.Count, "sanity: the press itself must land, or nothing below means anything");

            yield return PastTheHoldOnTheRealClock();

            Assert.AreEqual(1, _secondary.Count, "a resting finger must become the secondary press");
            Assert.IsEmpty(_released,
                "the hold ENDS the gesture; raising Released too would also run the host's click path");
        }

        /// <summary>
        /// One slow frame right after the press must not lose the hold (TASK-671).
        /// </summary>
        /// <remarks>
        /// Reproduces the CI runner: the press frame stalls 400 ms AFTER its panel update (a one-shot
        /// sleep at the end of <c>PostLateUpdate</c>), so the next frame's game-time step alone is past
        /// the hold. With a game-time wait this read 0 secondaries, exactly as CI did.
        /// </remarks>
        private static bool _stallOnce;

        [UnityTest]
        public IEnumerator ASlowFrameAfterThePressStillRaisesSecondary() {
            var saved = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            var loop = saved;
            for (int i = 0; i < loop.subSystemList.Length; i++) {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate)) continue;
                var subs = new List<UnityEngine.LowLevel.PlayerLoopSystem>(loop.subSystemList[i].subSystemList);
                subs.Add(new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(LongPressSecondaryTests),
                    updateDelegate = () => { if (_stallOnce) { _stallOnce = false; System.Threading.Thread.Sleep(400); } } });
                loop.subSystemList[i].subSystemList = subs.ToArray();
            }
            UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
            try {
                Send<PointerDownEvent>(new Vector2(50f, 50f), UnityEngine.UIElements.PointerType.touch);
                _stallOnce = true;
                yield return PastTheHoldOnTheRealClock();
                Assert.AreEqual(1, _secondary.Count, "a slow frame must delay the hold, never lose it");
            } finally { UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(saved); }
        }

        /// <summary>
        /// The control that separates "touch becomes secondary" from "any held press becomes
        /// secondary". Without it, arming the hold for the mouse as well would pass the test above
        /// and would fire examine on every slow click and on every press held before a drag.
        /// </summary>
        [UnityTest]
        public IEnumerator AHeldMOUSEButtonDoesNot() {
            Send<PointerDownEvent>(new Vector2(50f, 50f), UnityEngine.UIElements.PointerType.mouse);

            yield return PastTheHoldOnTheRealClock();

            Assert.IsEmpty(_secondary, "a mouse has a right button; holding the left one is not it");
        }

        /// <summary>
        /// A finger that moves is dragging, not resting — and the cancel deliberately keys on the
        /// gesture's own drag promotion rather than a slop constant of its own, so it inherits the
        /// host's real rule (the inventory's is a diamond no radius reproduces).
        /// </summary>
        [UnityTest]
        public IEnumerator AFingerThatDragsDoesNot() {
            Send<PointerDownEvent>(new Vector2(50f, 50f), UnityEngine.UIElements.PointerType.touch);
            Send<PointerMoveEvent>(new Vector2(200f, 200f), UnityEngine.UIElements.PointerType.touch);

            yield return PastTheHoldOnTheRealClock();

            Assert.IsEmpty(_secondary, "a dragging finger must not also raise examine");
        }

        [UnityTest]
        public IEnumerator AFingerLiftedBeforeTheHoldDoesNot() {
            Send<PointerDownEvent>(new Vector2(50f, 50f), UnityEngine.UIElements.PointerType.touch);
            Send<PointerUpEvent>(new Vector2(50f, 50f), UnityEngine.UIElements.PointerType.touch);

            yield return PastTheHoldOnTheRealClock();

            Assert.IsEmpty(_secondary, "a tap is a tap");
            Assert.AreEqual(1, _released.Count, "and it still completes as an ordinary click");
        }
    }
}
