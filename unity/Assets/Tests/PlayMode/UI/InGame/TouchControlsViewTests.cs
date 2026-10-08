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
            var state = new TouchInputState();
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
        public IEnumerator TravelSplitsThePadsTurningLeftWalkingRight() =>
            Harness(2666, Touch(), (view, root, state) => {
                VisualElement left = root.Q("touch-left"), right = root.Q("touch-right");
                Assert.IsTrue(left.Contains(root.Q("touchpad_75")), "turning on the left thumb");
                Assert.IsTrue(left.Contains(root.Q("touchpad_77")));
                Assert.IsTrue(right.Contains(root.Q("touchpad_72")), "walking on the right thumb");
                Assert.IsTrue(right.Contains(root.Q("touchpad_80")));
                Assert.IsNull(root.Q("touch-cycle"), "the variant chooser is gone (owner chose T2 + C3, 2026-10-01)");
                Assert.IsNull(root.Q("touch-diag"), "and the developer readout with it");
                Assert.IsNull(root.Q("touch-examine"), "long-press is the right-click now (owner, 2026-10-01)");
                Assert.AreEqual(DisplayStyle.None, root.Q("touch-thrust").resolvedStyle.display, "no Thrust outside a fight");
            });

        [UnityTest]
        public IEnumerator ThePadCentreYOverrideLowersThePadsAndKeepsThemInTheWindow() =>
            Harness(2666, Touch(), (view, root, state) => {
                // Place() writes inline styles, so read those: layout only updates next frame.
                float Top(string pad) => root.Q(pad).style.top.value.value;
                float faithful = Top("touchpad_72");
                float? centre = 0.86f;
                view.PadCentreYOverride = () => centre;
                view.Refresh(inFight: false);
                Assert.Greater(Top("touchpad_72"), faithful);
                VisualElement back = root.Q("touchpad_80");
                Assert.LessOrEqual(back.style.top.value.value + back.style.height.value.value, 1200f, "the back pad stays inside the window");
                centre = null;
                view.Refresh(inFight: false);
                Assert.AreEqual(faithful, Top("touchpad_72"), 0.01f, "null restores the layout's value");
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
                // A REQ element: its Clickable captures the pointer on down, and UI Toolkit then sends
                // the release to it alone — the panel-level listener never heard a finger lift
                // (logcat on the emulator, 2026-10-01), so the tracker thought it was still down.
                var target = new VisualElement { name = "hotspot_2" };
                target.AddManipulator(new Clickable(() => { }));
                root.Q("test-stage").Add(target);
                state.SuppressSelectFor = 4;
                Send<PointerDownEvent>(target);
                Assert.IsTrue(view.TouchDown);
                Assert.AreSame(target, view.TouchTarget);
                Assert.IsNull(state.SuppressSelectFor, "a new press drops a stale suppression");
                int serial = view.PressSerial;
                Send<PointerUpEvent>(target);
                Assert.IsFalse(view.TouchDown, "the release reached the tracker");
                Send<PointerDownEvent>(target);
                Assert.AreEqual(serial + 1, view.PressSerial, "every press is a new press");
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

        /// <summary>Owner, 2026-10-01: the grid toggle should be much more unobtrusive.</summary>
        [UnityTest]
        public IEnumerator TheGridButtonIsASmallCornerButton() =>
            Harness(2666, Touch(), (view, root, state) => {
                view.Refresh(inFight: true);
                // Styles, not worldBound: the relayout this Refresh asked for has not run yet.
                IStyle grid = root.Q("touch-grid").style;
                Assert.AreEqual(DisplayStyle.Flex, grid.display.value);
                Assert.Less(grid.width.value.value, root.Q("touch-thrust").style.width.value.value / 2f, "small");
                Assert.Less(grid.top.value.value + grid.height.value.value,
                    root.Q("touch-thrust").style.top.value.value, "above the action buttons");
            });

        [UnityTest]
        public IEnumerator CursorModeShowsThePadAndTheButtonsForTheCellUnderIt() =>
            Harness(2666, Touch(), (view, root, state) => {
                var moved = 0;
                view.MoveRequested += () => moved++;
                state.CursorContext = CursorContext.Target;
                view.Refresh(inFight: true);
                Assert.AreEqual(DisplayStyle.Flex, root.Q("touchpad_72").style.display.value, "the pad moves the cursor");
                Assert.IsTrue(root.Q("touch-left").Contains(root.Q("touchpad_72")), "all four on the left thumb in a fight");
                Assert.AreEqual(DisplayStyle.Flex, root.Q("touch-thrust").style.display.value);
                Assert.AreEqual(DisplayStyle.None, root.Q("touch-move").style.display.value);

                state.CursorContext = CursorContext.Ground;
                state.MoveAccepted = false;
                view.Refresh(inFight: true);
                Assert.AreEqual(DisplayStyle.None, root.Q("touch-move").style.display.value,
                    "no Move onto a cell the actor cannot reach (TASK-819)");
                state.MoveAccepted = true;
                view.Refresh(inFight: true);
                Assert.AreEqual(DisplayStyle.None, root.Q("touch-thrust").style.display.value);
                Assert.AreEqual(DisplayStyle.Flex, root.Q("touch-move").style.display.value);
                Click(root.Q("touch-move"));
                Assert.AreEqual(1, moved);

                state.AwaitingTarget = true;
                state.CastAccepted = false;
                view.Refresh(inFight: true);
                Assert.AreEqual(DisplayStyle.None, root.Q("touch-move").style.display.value,
                    "no Cast here where the spell would not land (TASK-823)");
                state.CastAccepted = true;
                view.Refresh(inFight: true);
                Assert.AreEqual(DisplayStyle.Flex, root.Q("touch-move").style.display.value);
                Assert.AreEqual("Cast here", root.Q("touch-move").Q<Label>().text, "a waiting spell names the button");
            });

        [UnityTest]
        public IEnumerator ThrustAndSwingRaiseTheMeleeOnATarget() =>
            Harness(2666, Touch(), (view, root, state) => {
                var asked = new List<bool>();
                view.MeleeRequested += asked.Add;
                state.CursorContext = CursorContext.Target;
                view.Refresh(inFight: true);
                Assert.AreEqual(DisplayStyle.Flex, root.Q("touch-thrust").style.display.value);
                Click(root.Q("touch-thrust"));
                Click(root.Q("touch-swing"));
                CollectionAssert.AreEqual(new[] { true, false }, asked);
            });
    }
}
