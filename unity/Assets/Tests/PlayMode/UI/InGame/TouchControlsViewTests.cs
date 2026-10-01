namespace BakAgain.Tests.PlayMode.UI.InGame {
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.UI.InGame;
    using BakAgain.UI.InputCore;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The Android touch aids' side-bar view (spec 2026-09-29-android-touch-aids-design.md).
    /// </summary>
    /// <remarks>
    /// The root and stage are built at fixed sizes (a 20:9 phone at the canonical 1200 height is
    /// 2666 wide) rather than through CanonicalStage and the Game view, so the side-bar logic is
    /// tested without depending on the batch runner's screen size. The CanonicalStage integration
    /// is verified on the Android emulator.
    /// </remarks>
    public class TouchControlsViewTests {
        private sealed class MemPrefs : IPrefsStore {
            private readonly Dictionary<string, int> _v = new Dictionary<string, int>();
            public int GetInt(string k, int f) => _v.TryGetValue(k, out int x) ? x : f;
            public void SetInt(string k, int x) => _v[k] = x;
        }

        private static IEnumerator Harness(float width, IPointer pointer,
            System.Action<TouchControlsView, VisualElement, TouchInputState> check) {
            var host = new GameObject("TouchDoc");
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            var root = new VisualElement { name = "test-root" };
            root.style.position = Position.Absolute;
            root.style.width = width;
            root.style.height = 1200;
            var stage = new VisualElement { name = "test-stage", pickingMode = PickingMode.Ignore };
            stage.style.position = Position.Absolute;
            stage.style.left = (width - 1600) / 2f;
            stage.style.width = 1600;
            stage.style.height = 1200;
            root.Add(stage);
            doc.rootVisualElement.Add(root);
            var state = new TouchInputState(new MemPrefs());
            var view = new TouchControlsView(state, pointer, new TouchControlsLayout());
            view.Build(root, stage);
            yield return null;
            yield return null;
            view.Refresh(inFight: false);
            yield return null;
            try {
                check(view, root, state);
            } finally {
                view.Dispose();
                Object.DestroyImmediate(host);
            }
        }

        private static FakePointer Touch() => new FakePointer { IsPresent = false, CanPointOverride = true };

        private static void Click(VisualElement e) {
            using (ClickEvent evt = ClickEvent.GetPooled()) {
                evt.target = e;
                e.SendEvent(evt);
            }
        }

        [UnityTest]
        public IEnumerator ThumbPadSitsInTheLeftBarAndExamineInTheRight() =>
            Harness(2666, Touch(), (view, root, state) => {
                VisualElement left = root.Q("touch-left"), right = root.Q("touch-right");
                Assert.IsTrue(left.worldBound.Contains(root.Q("touchpad_72").worldBound.center));
                Assert.IsTrue(left.worldBound.Contains(root.Q("touchpad_77").worldBound.center));
                Assert.IsNull(root.Q("touch-examine"), "long-press is the right-click now (owner, 2026-10-01)");
                Assert.AreEqual(DisplayStyle.None, root.Q("touch-thrust").resolvedStyle.display, "no Thrust outside a fight");
            });

        [UnityTest]
        public IEnumerator TheGridButtonShowsInAFightAndAsksForTheToggle() =>
            Harness(2666, Touch(), (view, root, state) => {
                Assert.AreEqual(DisplayStyle.None, root.Q("touch-grid").style.display.value, "not on the travel screen");
                view.Refresh(inFight: true);
                Assert.AreEqual(DisplayStyle.Flex, root.Q("touch-grid").style.display.value);
                Click(root.Q("touch-grid"));
                Assert.IsTrue(state.TakeGridToggle());
            });

        /// <summary>
        /// The long-press reads the finger from UI Toolkit's own pointer events, panel-wide: the
        /// polled pointer never reported a held finger on the owner's phone.
        /// </summary>
        [UnityTest]
        public IEnumerator ThePressTrackerFollowsAFingerAnywhereOnThePanel() =>
            Harness(2666, Touch(), (view, root, state) => {
                var target = new VisualElement { name = "hotspot_2" };
                root.Q("test-stage").Add(target);
                state.SuppressSelectFor = 4;
                Send<PointerDownEvent>(target);
                Assert.IsTrue(view.TouchDown);
                Assert.AreSame(target, view.TouchTarget);
                Assert.IsNull(state.SuppressSelectFor, "a new press drops a stale suppression");
                Send<PointerUpEvent>(target);
                Assert.IsFalse(view.TouchDown);
            });

        [UnityTest]
        public IEnumerator CycleButtonMovesToSplitPads() =>
            Harness(2666, Touch(), (view, root, state) => {
                Click(root.Q("touch-cycle"));
                Assert.AreEqual(TouchTravelVariant.SplitPads, state.Travel);
                Assert.IsTrue(root.Q("touch-right").Contains(root.Q("touchpad_72")),
                    "split pads: forward/back move to the right thumb");
                Assert.IsTrue(root.Q("touch-left").Contains(root.Q("touchpad_75")), "turning stays left");
            });

        [UnityTest]
        public IEnumerator NoSideBarsNoControls() =>
            Harness(1600, Touch(), (view, root, state) => {
                Assert.AreEqual(DisplayStyle.None, root.Q("touch-left").style.display.value);
                Assert.AreEqual(DisplayStyle.None, root.Q("touch-right").style.display.value);
            });

        [UnityTest]
        public IEnumerator DesktopPointerHidesTheLayer() =>
            Harness(2666, new FakePointer { IsPresent = true }, (view, root, state) => {
                Assert.AreEqual(DisplayStyle.None, root.Q("touch-left").style.display.value);
                Assert.AreEqual(DisplayStyle.None, root.Q("touch-right").style.display.value);
            });

        /// <summary>
        /// Final review #3: REQ_MAIN orders the arrows 75, 72, 80, 77. A zone inserted just before
        /// its OWN arrow sat above the earlier arrows, so the enlarged Turn-Right area stole taps on
        /// the Forward/Back art. Every zone must sit behind every arrow.
        /// </summary>
        [UnityTest]
        public IEnumerator MinimalZonesSitBehindEveryArrow() =>
            Harness(2666, Touch(), (view, root, state) => {
                VisualElement stage = root.Q("test-stage");
                var panel = new VisualElement { name = "req-panel" };
                stage.Add(panel);
                foreach (int id in new[] { 75, 72, 80, 77 }) {
                    var arrow = new VisualElement { name = $"imagebutton_{id}" };
                    arrow.style.position = Position.Absolute;
                    arrow.style.left = 100 + id; arrow.style.top = 100; arrow.style.width = 40; arrow.style.height = 40;
                    panel.Add(arrow);
                }
                state.CycleTravel();
                state.CycleTravel();   // Minimal: Changed re-lays the view out and adds the zones
                Assert.AreEqual(TouchTravelVariant.Minimal, state.Travel);
                int lastZone = -1, firstArrow = int.MaxValue;
                for (int i = 0; i < panel.childCount; i++) {
                    if (panel[i].name.StartsWith("touchpad_")) lastZone = System.Math.Max(lastZone, i);
                    if (panel[i].name.StartsWith("imagebutton_")) firstArrow = System.Math.Min(firstArrow, i);
                }
                Assert.AreEqual(8, panel.childCount, "four zones and four arrows");
                Assert.Less(lastZone, firstArrow, "every zone is drawn (and picked) behind every arrow");
            });

        /// <summary>An IPointerEvent source for GetPooled (the LongPressSecondaryTests pattern).</summary>
        private sealed class Finger : IPointerEvent {
            public int pointerId { get; set; } = 1;
            public string pointerType { get; set; } = UnityEngine.UIElements.PointerType.touch;
            public bool isPrimary => true;
            public int button { get; set; }
            public int pressedButtons => 1;
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

        private static void Send<T>(VisualElement target) where T : PointerEventBase<T>, new() {
            using (T evt = PointerEventBase<T>.GetPooled(new Finger { position = target.worldBound.center })) {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        /// <summary>
        /// The owner's phone (2026-09-30): the pads did nothing, because the polled pointer never
        /// reported a held finger there. The pads are held through UI Toolkit's own pointer events,
        /// the path the working Examine button proved.
        /// </summary>
        [UnityTest]
        public IEnumerator APadIsHeldThroughItsOwnPointerEvents() =>
            Harness(2666, Touch(), (view, root, state) => {
                VisualElement pad = root.Q("touchpad_77");
                Send<PointerDownEvent>(pad);
                Assert.AreEqual(77, state.HeldTouchAction);
                Assert.IsFalse(state.HeldIsReqArrow);
                Send<PointerUpEvent>(pad);
                Assert.AreEqual(-1, state.HeldTouchAction);
            });

        [UnityTest]
        public IEnumerator AReqCompassArrowIsHeldToo() =>
            Harness(2666, Touch(), (view, root, state) => {
                // Like UserInterfaceLoader's REQ arrows: a Clickable on the element itself. The
                // owner's phone (2026-09-30): holding an arrow still did nothing while pads worked.
                var arrow = new VisualElement { name = "imagebutton_72" };
                arrow.AddManipulator(new Clickable(() => { }));
                root.Q("test-stage").Add(arrow);
                view.Refresh(inFight: true);
                view.Refresh(inFight: false);   // a relayout picks the arrow up
                Send<PointerDownEvent>(arrow);
                Assert.AreEqual(72, state.HeldTouchAction);
                Assert.IsTrue(state.HeldIsReqArrow, "its own click takes a tap's step");
                Send<PointerCancelEvent>(arrow);
                Assert.AreEqual(-1, state.HeldTouchAction);
            });

        [UnityTest]
        public IEnumerator InAFightThrustAndSwingShowAndRaiseTheMelee() =>
            Harness(2666, Touch(), (view, root, state) => {
                var asked = new List<bool>();
                view.MeleeRequested += asked.Add;
                view.Refresh(inFight: true);
                Assert.AreEqual(DisplayStyle.Flex, root.Q("touch-thrust").style.display.value);
                Assert.AreEqual(DisplayStyle.None, root.Q("touchpad_72").style.display.value, "no pad in a fight");
                Click(root.Q("touch-thrust"));
                Click(root.Q("touch-swing"));
                CollectionAssert.AreEqual(new[] { true, false }, asked);
            });
    }
}
