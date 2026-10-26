namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.Tests.PlayMode.UI.InputCore;
    using BakAgain.UI.Cursor;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A finger on a location (TASK-926, the owner on a phone in LaMut): there is no cursor to say what
    /// is under it, so while it is down the hotspot's word shows away from it; the release acts on
    /// the hotspot under it then, or on nothing; the way out asks first; a hold still examines.
    /// </summary>
    public class TouchHotspotGestureTests {
        private const int Door = 10, Well = 11, Exit = 3;

        private GameObject _host;
        private VisualElement _root;
        private readonly Dictionary<VisualElement, int> _hotspots = new();
        private List<int> _primary, _secondary, _words;
        private int _hidden, _clickableFired;
        private bool _armed;

        [SetUp]
        public void SetUp() {
            _host = new GameObject("TouchHotspotGestureDoc");
            UIDocument doc = _host.AddComponent<UIDocument>();
            doc.panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            _root = doc.rootVisualElement;
            _hotspots.Clear();
            Add(Door, 0, 0);
            Add(Well, 200, 0);
            Add(Exit, 0, 200);
            _primary = new List<int>();
            _secondary = new List<int>();
            _words = new List<int>();
            _hidden = 0;
            _clickableFired = 0;
            _armed = true;
            UserInterfaceLoader.RegisterTouchHotspotGesture(_root,
                e => _hotspots.TryGetValue(e, out int a) ? a : -1,
                () => _armed,
                a => _primary.Add(a),
                a => _secondary.Add(a),
                a => a == Exit,
                (a, _) => _words.Add(a),
                () => _hidden++,
                holdMs: 50);
        }

        private void Add(int action, float x, float y) {
            var e = new VisualElement {
                pickingMode = PickingMode.Position,
                style = { position = Position.Absolute, left = x, top = y, width = 100, height = 100 },
            };
            // The mouse's path, which a finger must no longer take: a Clickable on the hotspot.
            e.AddManipulator(new Clickable(() => _clickableFired++));
            _root.Add(e);
            _hotspots[e] = action;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_host);

        private static readonly Vector2 OnDoor = new(50, 50), OnWell = new(250, 50), OnExit = new(50, 250), OnNothing = new(450, 450);

        private void Send<T>(Vector2 at, string type = null) where T : PointerEventBase<T>, new() {
            var stub = new LongPressSecondaryTests.StubPointerEvent { pointerId = 1, pointerType = type ?? UnityEngine.UIElements.PointerType.touch, position = at };
            VisualElement target = _root.panel.Pick(at) ?? _root;
            using (T evt = PointerEventBase<T>.GetPooled(stub)) {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        private IEnumerator Laid() {
            yield return null;
            yield return null;
        }

        private IEnumerator Tap(Vector2 at) {
            Send<PointerDownEvent>(at);
            yield return null;
            Send<PointerUpEvent>(at);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AFingerDown_ShowsTheWordOfTheHotspotUnderIt_AndSlidingFollows() {
            yield return Laid();
            Send<PointerDownEvent>(OnDoor);
            Assert.AreEqual(new[] { Door }, _words.ToArray(), "the door's word, with nothing run yet");
            CollectionAssert.IsEmpty(_primary);
            Send<PointerMoveEvent>(OnWell);
            Assert.AreEqual(Well, _words[^1], "sliding onto the well shows the well's");
            Send<PointerMoveEvent>(OnNothing);
            Assert.Greater(_hidden, 0, "off every hotspot, no word");
            Send<PointerUpEvent>(OnNothing);
        }

        [UnityTest]
        public IEnumerator AFingerDownOffEveryHotspot_ShowsTheWordOnceItSlidesOntoOne() {
            yield return Laid();
            Send<PointerDownEvent>(OnNothing);
            CollectionAssert.IsEmpty(_words);
            Send<PointerMoveEvent>(OnDoor);
            Assert.AreEqual(Door, _words[^1], "TASK-968: the word follows a finger that came down between hotspots");
            Send<PointerUpEvent>(OnDoor);
            yield return null;
            CollectionAssert.AreEqual(new[] { Door }, _primary);
        }

        [UnityTest]
        public IEnumerator TheRelease_ActsOnTheHotspotUnderTheFingerThen() {
            yield return Laid();
            Send<PointerDownEvent>(OnDoor);
            Send<PointerMoveEvent>(OnWell);
            Send<PointerUpEvent>(OnWell);
            yield return null;
            CollectionAssert.AreEqual(new[] { Well }, _primary);
            Assert.AreEqual(0, _clickableFired, "the mouse's Clickable does not also fire for a finger");
        }

        [UnityTest]
        public IEnumerator AReleaseOffEveryHotspot_RunsNothing() {
            yield return Laid();
            Send<PointerDownEvent>(OnDoor);
            Send<PointerMoveEvent>(OnNothing);
            Send<PointerUpEvent>(OnNothing);
            yield return null;
            CollectionAssert.IsEmpty(_primary);
            Assert.AreEqual(0, _clickableFired);
        }

        [UnityTest]
        public IEnumerator TheWayOut_AsksFirst_ThenASecondTapLeaves() {
            yield return Laid();
            yield return Tap(OnExit);
            CollectionAssert.IsEmpty(_primary, "the first tap only says Exit");
            Assert.AreEqual(Exit, _words[^1]);
            int hiddenAfterFirst = _hidden;
            yield return null;
            Assert.AreEqual(hiddenAfterFirst, _hidden, "and keeps it shown");
            yield return Tap(OnExit);
            CollectionAssert.AreEqual(new[] { Exit }, _primary, "the second leaves");
        }

        [UnityTest]
        public IEnumerator AnyOtherTap_ClearsTheAskedExit() {
            yield return Laid();
            yield return Tap(OnExit);
            yield return Tap(OnDoor);
            CollectionAssert.AreEqual(new[] { Door }, _primary, "the other tap does its own thing");
            yield return Tap(OnExit);
            CollectionAssert.AreEqual(new[] { Door }, _primary, "and the exit asks again");
            yield return Tap(OnNothing);
            yield return Tap(OnExit);
            CollectionAssert.AreEqual(new[] { Door }, _primary, "a tap on nothing clears it too");
        }

        // TASK-67 stays: a resting finger examines, and its release runs nothing.
        [UnityTest]
        public IEnumerator AHeldFinger_Examines_AndItsReleaseRunsNothing() {
            yield return Laid();
            Send<PointerDownEvent>(OnDoor);
            float until = Time.realtimeSinceStartup + 0.25f;
            while (Time.realtimeSinceStartup < until) { yield return null; }
            Send<PointerUpEvent>(OnDoor);
            yield return null;
            CollectionAssert.AreEqual(new[] { Door }, _secondary);
            CollectionAssert.IsEmpty(_primary);
        }

        [UnityTest]
        public IEnumerator TheMouse_IsLeftToItsClickable() {
            yield return Laid();
            Send<PointerDownEvent>(OnDoor, UnityEngine.UIElements.PointerType.mouse);
            Send<PointerUpEvent>(OnDoor, UnityEngine.UIElements.PointerType.mouse);
            yield return null;
            CollectionAssert.IsEmpty(_primary, "the gesture leaves the mouse alone");
            CollectionAssert.IsEmpty(_words);
            Assert.AreEqual(1, _clickableFired, "and the Clickable still clicks");
        }

        [UnityTest]
        public IEnumerator AScreenThatDidNotOptIn_IsLeftAlone() {
            _armed = false;
            yield return Laid();
            yield return Tap(OnDoor);
            CollectionAssert.IsEmpty(_primary);
            Assert.AreEqual(1, _clickableFired);
        }

        // The word goes where the finger is not: above it, or below it when there is no room above,
        // and always inside the screen.
        [Test]
        public void TheWord_IsPlacedClearOfTheFinger_AndOnScreen() {
            var bounds = new Vector2(2133, 1200);
            var size = new Vector2(240, 60);
            foreach (Vector2 finger in new[] { new Vector2(1000, 700), new Vector2(10, 40), new Vector2(2120, 1190) }) {
                Vector2 at = CursorManager.TouchWordPosition(finger, size, bounds);
                var word = new Rect(at, size);
                Assert.IsFalse(new Rect(finger - new Vector2(90, 90), new Vector2(180, 180)).Overlaps(word),
                    $"the word for a finger at {finger} is clear of it");
                Assert.GreaterOrEqual(word.xMin, 0); Assert.GreaterOrEqual(word.yMin, 0);
                Assert.LessOrEqual(word.xMax, bounds.x); Assert.LessOrEqual(word.yMax, bounds.y);
            }
        }
    }
}
