namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.UI.Inventory;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// InventoryMenu's item select / drag / drop gesture, driven the way production drives it: the
    /// screen mounts a <c>DragGestureManipulator</c> on its stage, and these tests send real UI
    /// Toolkit pointer events to that stage rather than calling a frame-stepping seam.
    ///
    /// <para>Synthesizing events is the house pattern (<c>ItemGridRendererTests.SimulateClick</c>,
    /// <c>NavigableLayer.DispatchPointer</c>) and avoids device injection, which is unreliable
    /// headless — the box's real pointer re-asserts (0,0) every frame.</para>
    ///
    /// <para>Panel-space (0,0) is the target: <c>item_slot_0</c> is built to cover the panel's
    /// top-left corner, and the stage here IS the panel root, so stage-local and panel coordinates
    /// coincide — no scale math to reproduce.</para>
    /// </summary>
    public class InventoryPointerTests {
        // The inert collaborators InventoryMenu.Construct needs (resources, dialogs, navigation)
        // live in InventoryMenuStubs.cs, shared with InventoryMenuLayoutTests.

        private GameObject _go;
        private PanelSettings _panelSettings;
        private VisualElement _stage;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        // Builds an InventoryMenu with a real UIDocument/panel (so panel.Pick and the manipulator's
        // capture behave exactly as in production) and a single item_slot_0 covering
        // (0,0)..(2000,2000) — big enough to certainly contain (0,0) at any panel scale.
        // _displayed gets a one-item container of the given type: the select-vs-use rule branches
        // on it (member view runs Use on double-click; a container view leaves the selection).
        private InventoryMenu BuildMenu(out VisualElement itemSlot0,
            GameData.Resources.Data.SaveGameContainerType containerType =
                GameData.Resources.Data.SaveGameContainerType.Inventory) {
            _go = new GameObject("InventoryMenuUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;

            var menu = _go.AddComponent<InventoryMenu>(); // Awake() resolves UIDocument/UserInterfaceLoader
            var session = new GameSession();
            menu.Construct(session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                new NoOpDialogs());

            var displayed = new GameData.Resources.Inventory.RuntimeContainer {
                Capacity = 24, ContainerType = containerType,
            };
            displayed.Items.Add(new GameData.Resources.Inventory.RuntimeItem(90, 1, 0));
            typeof(InventoryMenu).GetField("_displayed", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(menu, displayed);

            itemSlot0 = new VisualElement {
                name = "item_slot_0",
                style = {
                    position = Position.Absolute,
                    left = 0, top = 0, width = 2000, height = 2000,
                },
            };
            document.rootVisualElement.Add(itemSlot0);

            // _stage is normally set by RenderCurrent (which needs a real REQ build); point it at the
            // panel root and mount the gesture recogniser the way RenderCurrent would.
            _stage = document.rootVisualElement;
            typeof(InventoryMenu).GetField("_stage", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(menu, _stage);
            typeof(InventoryMenu).GetMethod("EnsureStageInput", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(menu, null);

            // Force a layout pass so worldBound/panel.Pick resolve inside this single-frame test
            // (mirrors ItemGridRendererTests.AttachToRuntimePanel).
            MethodInfo validateLayout = _stage.panel.GetType()
                .GetMethod("ValidateLayout", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            validateLayout?.Invoke(_stage.panel, null);
            return menu;
        }

        private void Send<T>(Vector2 position, EventType type, int button, int clickCount = 1,
            VisualElement target = null)
            where T : PointerEventBase<T>, new() {
            // clickCount must be set explicitly: a bare `new Event{...}` reports 0, and the
            // select-vs-use rule reads it. target defaults to the stage; pass a child to aim a
            // click at it specifically (its own manipulators only run when it is the target).
            var imgui = new Event {
                type = type, mousePosition = position, button = button, clickCount = clickCount,
            };
            using (T evt = PointerEventBase<T>.GetPooled(imgui)) {
                evt.target = target ?? _stage;
                (target ?? _stage).SendEvent(evt);
            }
        }

        private void Click(Vector2 at, int clickCount = 1) {
            Send<PointerDownEvent>(at, EventType.MouseDown, button: 0, clickCount);
            Send<PointerUpEvent>(at, EventType.MouseUp, button: 0, clickCount);
        }

        private static float Outline(VisualElement cell) => cell.style.borderTopWidth.value;

        /// <summary>
        /// The selected cell's border, TOP edge — one original pixel, times the VERTICAL scale.
        /// </summary>
        /// <remarks>
        /// <b>These asserted a flat 8f, which was a guess nothing in the original supports.</b> The
        /// highlight is a <c>draw_rect_filled</c> with an outline PEN, so its width is the blitter's
        /// single pixel — and one original pixel is 5 canonical across against 6 down. The old value
        /// was neither, and no flat value could have been both.
        /// </remarks>
        private static float SelectedBorderY =>
            GameData.Resources.Inventory.InventoryDragGesture.OutlineWidthVga
            * BakAgain.Graphics.Canonical.VgaScaleY;

        [Test]
        public void TheSelectionBorderIsTHICKERTopAndBottomThanLeftAndRight() {
            // *** The asymmetry is the finding, not a rounding detail. *** One original pixel is 5
            // canonical across and 6 down, so a faithful one-pixel outline CANNOT be one number.
            // The flat 8f these tests used to assert hid that entirely.
            InventoryMenu menu = BuildMenu(out VisualElement itemSlot0);
            Click(Vector2.zero);

            Assert.AreEqual(BakAgain.Graphics.Canonical.VgaScaleY,
                itemSlot0.style.borderTopWidth.value, 0.001f);
            Assert.AreEqual(BakAgain.Graphics.Canonical.VgaScaleX,
                itemSlot0.style.borderLeftWidth.value, 0.001f);
            Assert.Greater(itemSlot0.style.borderTopWidth.value,
                itemSlot0.style.borderLeftWidth.value);
        }

        [Test]
        public void ClickOnItem_SelectsIt() {
            InventoryMenu menu = BuildMenu(out VisualElement itemSlot0);
            Assert.AreEqual(0f, Outline(itemSlot0), "no selection outline before any gesture");

            Click(Vector2.zero);

            Assert.AreEqual(SelectedBorderY, Outline(itemSlot0),
                "a click delivered as pointer events must apply the selection outline");
        }

        /// <summary>
        /// Double-clicking the already-selected item clears the selection: that is the original's
        /// double-click (0x573A5), which returns -2, becomes action 0x16 "Use", and the Use handler
        /// ends with <c>selSlot = -1</c> (0x54BAD). The window is the platform's, via UI Toolkit's
        /// clickCount, so the second press arrives with clickCount == 2.
        /// </summary>
        [Test]
        public void DoubleClickingTheSelectedItem_UsesItAndDeselects() {
            InventoryMenu menu = BuildMenu(out VisualElement itemSlot0);

            Click(Vector2.zero);
            Assert.AreEqual(SelectedBorderY, Outline(itemSlot0), "first click selects");

            Click(Vector2.zero); // second click at the same spot, same frame -> count 2
            Assert.AreEqual(0f, Outline(itemSlot0), "the double-click uses the item and deselects");

            Click(Vector2.zero);
            Assert.AreEqual(SelectedBorderY, Outline(itemSlot0), "and the next click selects again");
        }

        /// <summary>
        /// A re-click outside the double-click window is not a double-click — the item simply stays
        /// selected. Covered at the recogniser (<c>ClickCount_ResetsOnceTheWindowHasElapsed</c>),
        /// which can drive the window deterministically; a screen-level version would have to sleep.
        /// </summary>

        /// <summary>
        /// In a container (loot) view the original's action 0x16 falls through both residence
        /// branches (CMBINV.C:358/399) and does nothing — a double-click there neither uses nor
        /// deselects. Spec docs/specs/inventory-item-handling.md §3.
        /// </summary>
        [Test]
        public void DoubleClickInContainerView_LeavesTheSelection() {
            InventoryMenu menu = BuildMenu(out VisualElement itemSlot0,
                GameData.Resources.Data.SaveGameContainerType.Corpse);

            Click(Vector2.zero);
            Assert.AreEqual(SelectedBorderY, Outline(itemSlot0), "first click selects");

            Click(Vector2.zero); // same spot, same frame -> count 2
            Assert.AreEqual(SelectedBorderY, Outline(itemSlot0),
                "a loot-view double-click is not a Use — the selection stays");
        }

        /// <summary>
        /// A press on dead space clears the selection — the original's
        /// `if (focused == 0 && click) { selSlot = -1 }` (0x570B1..0x570CD), where the live entries
        /// are the chrome hotspots plus one per item (CMBINV.C:60).
        /// </summary>
        [Test]
        public void PressOnDeadSpace_ClearsTheSelection() {
            InventoryMenu menu = BuildMenu(out VisualElement itemSlot0);

            Click(Vector2.zero);
            Assert.AreEqual(SelectedBorderY, Outline(itemSlot0), "precondition: the item is selected");

            // Well outside item_slot_0 (which stops at 2000) and outside any REQ hotspot — no
            // UserInterfaceLoader is wired here, so nothing reports a chrome rect.
            Send<PointerDownEvent>(new Vector2(5000f, 5000f), EventType.MouseDown, button: 0);

            Assert.AreEqual(0f, Outline(itemSlot0), "a press on dead space must clear the selection");
        }

        /// <summary>
        /// The dud-click regression (JvE, 2026-08-02): a click on REQ chrome — a portrait, the
        /// container window, any hotspot with a <c>Clickable</c> — steals the pointer capture from
        /// the stage recogniser, which then never saw the release and swallowed the NEXT click.
        /// After a chrome click, the very next click on an item must select it, first time.
        /// </summary>
        [Test]
        public void ClickOnChromeThenOnItem_SelectsFirstTime() {
            InventoryMenu menu = BuildMenu(out VisualElement itemSlot0);
            var chrome = new VisualElement {
                name = "portrait_hotspot",
                style = { position = Position.Absolute, left = 2500, top = 0, width = 200, height = 200 },
            };
            _stage.Add(chrome);
            chrome.AddManipulator(new Clickable(() => { })); // what UserInterfaceLoader gives every hotspot
            MethodInfo validateLayout = _stage.panel.GetType()
                .GetMethod("ValidateLayout", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            validateLayout?.Invoke(_stage.panel, null);

            // The chrome click: down trickles to the recogniser first, then the Clickable steals
            // the capture and consumes the release.
            var onChrome = chrome.worldBound.center;
            Send<PointerDownEvent>(onChrome, EventType.MouseDown, button: 0, target: chrome);
            Send<PointerUpEvent>(onChrome, EventType.MouseUp, button: 0, target: chrome);

            Click(Vector2.zero); // first click on the item after the chrome click
            Assert.AreEqual(SelectedBorderY, Outline(itemSlot0),
                "the first item click after a chrome click must select — a stale captured press "
                + "swallowing it is the dud-click bug");
        }

        [Test]
        public void PressOnAnItem_DoesNotRunTheDeadSpaceDeselect() {
            InventoryMenu menu = BuildMenu(out VisualElement itemSlot0);

            Click(Vector2.zero);
            Assert.AreEqual(SelectedBorderY, Outline(itemSlot0), "click selects");

            Send<PointerDownEvent>(Vector2.zero, EventType.MouseDown, button: 0);
            Assert.AreEqual(SelectedBorderY, Outline(itemSlot0),
                "pressing the item must not take the dead-space branch");
        }

        /// <summary>
        /// Right-click on an item opens the description (sub_ovr157_4E3 @0x549A2) and must not run
        /// the primary select path. The stub dialog manager means nothing renders; what is pinned is
        /// that the branches stay separate.
        /// </summary>
        [Test]
        public void RightClickOnItem_DoesNotSelect() {
            InventoryMenu menu = BuildMenu(out VisualElement itemSlot0);

            Send<PointerDownEvent>(Vector2.zero, EventType.MouseDown, button: 1);

            Assert.AreEqual(0f, Outline(itemSlot0),
                "a right-click must not apply the selection outline");
        }

        [Test]
        public void RightClickAwayFromAnyItem_IsIgnored() {
            InventoryMenu menu = BuildMenu(out VisualElement itemSlot0);

            Send<PointerDownEvent>(new Vector2(5000f, 5000f), EventType.MouseDown, button: 1);

            Assert.AreEqual(0f, Outline(itemSlot0), "nothing to inspect, nothing to select");
        }

        /// <summary>
        /// The detail window's keys/bag swap. The original's <c>highlight_slot</c> has three states
        /// and the icon flag is <c>highlight_slot != -1</c> (INVENTOR.C:339): a selected slot
        /// (<c>&gt;= 0</c>) and a drag in progress (<c>-2</c>, set at INVENTOR.C:668/753/780) both
        /// show the bag; only <c>-1</c> shows the keys. The drag state was missing, which is why
        /// starting a drag didn't swap the icon (JvE, 2026-07-30).
        /// </summary>
        [Test]
        public void BagIcon_ShowsWhileSelectedOrDraggingOrInspecting() {
            Assert.IsFalse(InventoryMenu.ShowsBagIcon(-1, -1, false), "nothing highlighted -> keys");
            Assert.IsTrue(InventoryMenu.ShowsBagIcon(0, -1, false), "a selected slot -> bag");
            Assert.IsTrue(InventoryMenu.ShowsBagIcon(-1, 0, false), "a drag in progress -> bag");
            Assert.IsTrue(InventoryMenu.ShowsBagIcon(-1, -1, true), "inspecting -> bag");
            Assert.IsTrue(InventoryMenu.ShowsBagIcon(3, 3, false), "selected AND dragging -> bag");
        }

        /// <summary>
        /// UI_DrawPartyHeadHighlightCircles @0x562a5 pulses the red ring only when the drag-hover
        /// target is a portrait AND is not the active member (`or di,di` 0x562D4, then
        /// `cmp di,[bp+arg_0]` 0x562D8). The active member's steady ring (#7) is drawn regardless.
        /// </summary>
        [Test]
        public void ResolveHoverPortrait_SuppressesThePulseOnTheActiveMember() {
            Assert.AreEqual(-1, InventoryMenu.ResolveHoverPortrait(1, 1),
                "hovering the member whose inventory is shown must not pulse their ring red");
            Assert.AreEqual(0, InventoryMenu.ResolveHoverPortrait(0, 1), "a different member pulses");
            Assert.AreEqual(2, InventoryMenu.ResolveHoverPortrait(2, 1), "a different member pulses");
        }

        [Test]
        public void ResolveHoverPortrait_NoHoverAndNoActiveMemberBothStayEmpty() {
            Assert.AreEqual(-1, InventoryMenu.ResolveHoverPortrait(-1, 1), "not over a portrait -> no ring");
            Assert.AreEqual(0, InventoryMenu.ResolveHoverPortrait(0, -1), "loot mode still pulses on hover");
            Assert.AreEqual(-1, InventoryMenu.ResolveHoverPortrait(-1, -1), "nothing hovered, nothing active");
        }

        /// <summary>
        /// The container window's drag-time border, from the two <c>invui_portrait_panel_draw</c>
        /// call sites: member view (0x57250) only while hovered — that's the live drop target;
        /// container view (0x57279, and the redraw at INVENTOR.C:399-400) always.
        /// </summary>
        [Test]
        public void ContainerBorder_ShowsOnHoverInMemberView_AndAlwaysInContainerView() {
            Assert.IsFalse(InventoryMenu.ShouldShowContainerBorder(false, true, memberView: true),
                "no drag, no border");
            Assert.IsFalse(InventoryMenu.ShouldShowContainerBorder(true, false, memberView: true),
                "member view: only while the drag is over the window");
            Assert.IsTrue(InventoryMenu.ShouldShowContainerBorder(true, true, memberView: true));

            Assert.IsTrue(InventoryMenu.ShouldShowContainerBorder(true, false, memberView: false),
                "container view: the whole drag, wherever the cursor is");
            // The screen REDRAW draws it too — `if (actor->bResidence != RES_PARTY_SLOT)
            // invui_portrait_panel_draw(-1, 0)` (INVENTOR.C:399-400) — so a shop, chest or corpse
            // shows it with no drag at all. Measured at Romney's Port Exchange, 2026-10-02.
            Assert.IsTrue(InventoryMenu.ShouldShowContainerBorder(false, false, memberView: false),
                "container view: always, from the redraw");
        }

        /// <summary>
        /// Border pens. sub_ovr158_3D0 @0x56480: phase %6, `&lt;= 3` gives <c>107 + phase</c> and above
        /// gives <c>113 - phase</c> — a triangular 0x6B..0x6E pulse. A negative flag takes flat 0x69.
        /// </summary>
        [Test]
        public void ContainerBorderPen_WalksTheTriangularPulse() {
            var pens = new int[6];
            for (int phase = 0; phase < 6; phase++) {
                pens[phase] = InventoryMenu.ContainerBorderPen(memberView: true, phase);
            }
            CollectionAssert.AreEqual(new[] { 0x6B, 0x6C, 0x6D, 0x6E, 0x6D, 0x6C }, pens);
            // The phase counter runs 0..11 (mod 12 at 0x5734E) and is folded to 6 by each consumer.
            Assert.AreEqual(0x6B, InventoryMenu.ContainerBorderPen(memberView: true, 6));
            Assert.AreEqual(0x6E, InventoryMenu.ContainerBorderPen(memberView: true, 9));

            Assert.AreEqual(0x69, InventoryMenu.ContainerBorderPen(memberView: false, 0));
            Assert.AreEqual(0x69, InventoryMenu.ContainerBorderPen(memberView: false, 3),
                "the container view's border is steady — the phase must not move it");
        }

        // ---- drag gestures (task-26) -------------------------------------------------------------
        // The click tests above never move the pointer, so the press-vs-drag split was live-verified
        // only. These drive the whole press -> move -> release run through the same real pointer
        // events, because that split is what decides between "select this item" and "move this item".

        private void Drag(Vector2 from, Vector2 to) {
            Send<PointerDownEvent>(from, EventType.MouseDown, button: 0);
            Send<PointerMoveEvent>(to, EventType.MouseDrag, button: 0);
            Send<PointerUpEvent>(to, EventType.MouseUp, button: 0);
        }

        private static int ItemCount(InventoryMenu menu) {
            var displayed = (GameData.Resources.Inventory.RuntimeContainer)typeof(InventoryMenu)
                .GetField("_displayed", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(menu);
            return displayed.Items.Count;
        }

        /// <summary>
        /// A drag must not fall through to click-select. <c>OnReleased</c> branches on the recogniser's
        /// <c>wasDrag</c> flag: false selects, true resolves a drop. If the threshold logic regressed so
        /// that a drag reported itself as a click, dragging an item onto empty space would silently
        /// select it instead — which is the state the *next* click then acts on.
        /// </summary>
        [Test]
        public void DragEndingOnDeadSpace_SnapsBackWithoutSelecting() {
            InventoryMenu menu = BuildMenu(out VisualElement itemSlot0);
            int before = ItemCount(menu);

            // Well past Layout.DragThreshold, and staying inside item_slot_0 so the release lands on
            // no drop target (no portraits or container window exist in this harness).
            Drag(Vector2.zero, new Vector2(400f, 400f));

            Assert.AreEqual(0f, Outline(itemSlot0),
                "a drag is not a click — it must not leave the item selected");
            Assert.AreEqual(before, ItemCount(menu),
                "dropping on nothing snaps back: no item may move");
        }

        /// <summary>
        /// The other side of the same branch: a press that wobbles less than the threshold is still a
        /// click. Real mice jitter a pixel or two between down and up, so a threshold that counted any
        /// movement as a drag would make items unselectable — the failure mode the threshold exists
        /// for. <see cref="InventoryMenu.DragThreshold"/> also refuses a zero/negative value, since the
        /// recogniser compares <c>magnitude &lt;= threshold</c>.
        /// </summary>
        [Test]
        public void PressThatWobblesBelowTheThreshold_StillSelects() {
            InventoryMenu menu = BuildMenu(out VisualElement itemSlot0);

            Drag(Vector2.zero, new Vector2(1f, 0f));

            Assert.AreEqual(SelectedBorderY, Outline(itemSlot0),
                "sub-threshold movement is mouse jitter, not a drag — the click must still select");
        }

        /// <summary>
        /// A drag that starts on dead space carries no item, so its release must do nothing at all.
        /// <c>OnReleased</c> guards on <c>_pressSlot &lt; 0</c>; without that guard a drag begun on
        /// chrome would resolve a drop for whatever slot was last pressed.
        /// </summary>
        [Test]
        public void DragStartedOffAnyItem_ResolvesNoDrop() {
            InventoryMenu menu = BuildMenu(out VisualElement itemSlot0);
            var chrome = new VisualElement {
                name = "portrait_hotspot",
                style = {
                    position = Position.Absolute, left = 0, top = 0, width = 2000, height = 2000,
                },
            };
            _stage.Add(chrome); // added last, so panel.Pick finds it above item_slot_0
            MethodInfo validateLayout = _stage.panel.GetType().GetMethod("ValidateLayout",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            validateLayout?.Invoke(_stage.panel, null);
            int before = ItemCount(menu);

            Drag(Vector2.zero, new Vector2(400f, 400f));

            Assert.AreEqual(before, ItemCount(menu), "no item was under the press, so none may move");
            Assert.AreEqual(0f, Outline(itemSlot0), "and nothing may be selected either");
        }
    }
}
