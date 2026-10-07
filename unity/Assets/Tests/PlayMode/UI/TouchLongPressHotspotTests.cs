namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.Tests.PlayMode.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A location's hotspots take a resting finger as their right-click — examine is right-click-only,
    /// so without it no location description was reachable by touch (TASK-67).
    /// </summary>
    public class TouchLongPressHotspotTests {
        private GameObject _host;
        private VisualElement _hotspot;
        private int _fired;
        private bool _armed;

        [SetUp]
        public void SetUp() {
            _host = new GameObject("TouchLongPressHotspotDoc");
            UIDocument doc = _host.AddComponent<UIDocument>();
            doc.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _hotspot = new VisualElement { style = { width = 100, height = 100 } };
            doc.rootVisualElement.Add(_hotspot);
            _fired = 0;
            _armed = true;
            UserInterfaceLoader.RegisterTouchLongPress(_hotspot, () => _armed, () => _fired++, holdMs: 50);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_host);

        private void Send<T>(string type) where T : PointerEventBase<T>, new() {
            var stub = new LongPressSecondaryTests.StubPointerEvent {
                pointerId = 1, pointerType = type, position = new Vector3(50f, 50f, 0f),
            };
            using (T evt = PointerEventBase<T>.GetPooled(stub)) {
                evt.target = _hotspot;
                _hotspot.SendEvent(evt);
            }
        }

        // Real clock, not WaitForSeconds: the hold is a scheduler item (TASK-671).
        private static IEnumerator PastTheHold() {
            float until = Time.realtimeSinceStartup + 0.25f;
            while (Time.realtimeSinceStartup < until) { yield return null; }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AHeldFingerRunsTheSecondaryOnce() {
            Send<PointerDownEvent>(UnityEngine.UIElements.PointerType.touch);
            yield return PastTheHold();
            Assert.AreEqual(1, _fired);
        }

        [UnityTest]
        public IEnumerator ATapDoesNot() {
            Send<PointerDownEvent>(UnityEngine.UIElements.PointerType.touch);
            Send<PointerUpEvent>(UnityEngine.UIElements.PointerType.touch);
            yield return PastTheHold();
            Assert.AreEqual(0, _fired);
        }

        [UnityTest]
        public IEnumerator AHeldMouseButtonDoesNot() {
            Send<PointerDownEvent>(UnityEngine.UIElements.PointerType.mouse);
            yield return PastTheHold();
            Assert.AreEqual(0, _fired, "the mouse has a right button; a slow click must stay a click");
        }

        [UnityTest]
        public IEnumerator AScreenThatDidNotOptInDoesNot() {
            _armed = false;
            Send<PointerDownEvent>(UnityEngine.UIElements.PointerType.touch);
            yield return PastTheHold();
            Assert.AreEqual(0, _fired, "the travel screen has its own detector; two would fire twice");
        }
    }
}
