namespace BakAgain.Tests.Editor.UI.InputCore {
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    public class NavigableLayerTests {
        // Three widgets in a horizontal row at canonical x = 0, 100, 200 (y = 0), so spatial Right/Left
        // are deterministic. Primary delegates record which index fired.
        private static (NavigableLayer layer, int[] fired, List<NavWidget> widgets) MakeRow(
            string[] labels, Action onCancel = null, BakAgain.UI.Cursor.ICursorManager cursor = null) {
            var fired = new[] { 0, 0, 0 };
            var widgets = new List<NavWidget>();
            for (int i = 0; i < labels.Length; i++) {
                int idx = i;
                widgets.Add(new NavWidget(new VisualElement(), labels[i],
                    new Rect(i * 100, 0, 50, 20), () => fired[idx]++, null));
            }
            var layer = new NavigableLayer("test", CaptureMode.Passive, widgets, onCancel, cursor);
            return (layer, fired, widgets);
        }

        // Records WarpTo calls so a test can assert when the cursor is (not) moved.
        private sealed class RecordingCursor : BakAgain.UI.Cursor.ICursorManager {
            public readonly List<Vector2> Warps = new();
            public Vector2 CanonicalPosition { get; private set; }
            public void WarpTo(Vector2 p) { Warps.Add(p); CanonicalPosition = p; }
            public void SelectSet(string setName) { }
            public void SetByIndex(int index) { }
            public void Set(GameData.Resources.Cursor.GameCursor cursor) { }
            public void Hide() { }
            public void Show() { }
        }

        [Test]
        public void MoveFocusNext_ThenActivate_FiresFocusedWidget() {
            var (layer, fired, _) = MakeRow(new[] { "Yes", "No", "Maybe" });
            layer.FocusIndex(0);
            Assert.IsTrue(layer.HandleIntent(UiIntent.Move(NavDirection.Next)));
            layer.HandleIntent(UiIntent.Activate());
            Assert.AreEqual(1, fired[1], "Activate fires the now-focused widget");
            Assert.AreEqual(0, fired[0]);
        }

        [Test]
        public void MoveFocusNext_WrapsAround() {
            var (layer, fired, _) = MakeRow(new[] { "A", "B", "C" });
            layer.FocusIndex(2);
            layer.HandleIntent(UiIntent.Move(NavDirection.Next));
            layer.HandleIntent(UiIntent.Activate());
            Assert.AreEqual(1, fired[0], "Next from the last widget wraps to the first");
        }

        [Test]
        public void Spatial_Right_PicksNearestInDirection() {
            var (layer, fired, _) = MakeRow(new[] { "A", "B", "C" });
            layer.FocusIndex(0);
            layer.HandleIntent(UiIntent.Move(NavDirection.Right));
            layer.HandleIntent(UiIntent.Activate());
            Assert.AreEqual(1, fired[1], "Right picks the immediate right neighbour, not the far one");
        }

        // MENUPAGE.C:346-366: a REQ entry's action id is the scancode of the key that presses it, so the
        // options menu's S is Save (0x1f) whatever its label starts with (TASK-796).
        [Test]
        public void Accelerator_PressesTheEntryWhoseActionIdIsTheKeysScancode() {
            var fired = new[] { 0, 0, 0 };
            var widgets = new List<NavWidget>();
            string[] labels = { "Start New Game", "Save Game", "Quit" };
            int[] ids = { 0x31, 0x1f, 0x20 };
            for (int i = 0; i < 3; i++) {
                int idx = i;
                widgets.Add(new NavWidget(new VisualElement(), labels[i], new Rect(i * 100, 0, 50, 20),
                    () => fired[idx]++, null, ids[i]));
            }
            var layer = new NavigableLayer("options", CaptureMode.Passive, widgets, null, null);

            Assert.IsTrue(layer.HandleIntent(UiIntent.Accelerator('s')));
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, fired, "S is 0x1f: Save, not Start New Game");
            layer.HandleIntent(UiIntent.Accelerator('d'));
            CollectionAssert.AreEqual(new[] { 0, 1, 1 }, fired, "D is 0x20: Quit");
            Assert.IsFalse(layer.HandleIntent(UiIntent.Accelerator('q')), "Q (0x10) is no entry here");
            CollectionAssert.AreEqual(new[] { 0, 1, 1 }, fired);
        }

        [Test]
        public void Accelerator_ADigitPressesItsEntryToo() {
            var fired = new[] { 0 };
            var widgets = new List<NavWidget> {
                new NavWidget(new VisualElement(), null, new Rect(0, 0, 50, 20), () => fired[0]++, null, 0x02),
            };
            var layer = new NavigableLayer("portraits", CaptureMode.Passive, widgets, null, null);

            Assert.IsTrue(layer.HandleIntent(UiIntent.Accelerator('1')));
            Assert.AreEqual(1, fired[0], "1 is scancode 2, the first portrait");
        }

        // ASKABOUT.C:499-511: the dialog choice menu counts the matches and a letter two labels share
        // selects nothing; every other menu keeps the first match.
        [Test]
        public void Accelerator_SharedLetter_SelectsNothingOnlyWhenTheLayerAsksForIt() {
            var labels = new[] { "Spoon", "Bawdy", "Ship" };
            var widgets = new List<NavWidget>();
            var fired = new[] { 0, 0, 0 };
            for (int i = 0; i < labels.Length; i++) {
                int idx = i;
                widgets.Add(new NavWidget(new VisualElement(), labels[i], new Rect(i * 100, 0, 50, 20), () => fired[idx]++, null));
            }
            var dialog = new NavigableLayer("dialog-choice", CaptureMode.Passive, widgets, null, null,
                ambiguousLetterSelectsNothing: true);
            Assert.IsTrue(dialog.HandleIntent(UiIntent.Accelerator('s')), "the letter is consumed");
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, fired, "a shared letter selects nothing");
            dialog.HandleIntent(UiIntent.Accelerator('b'));
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, fired, "a unique letter still selects");

            var (menu, menuFired, _) = MakeRow(labels);
            menu.HandleIntent(UiIntent.Accelerator('s'));
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, menuFired,
                "a REQ menu matches action ids, never a label's first letter");
        }

        [Test]
        public void Cancel_InvokesCancelDelegate() {
            bool cancelled = false;
            var (layer, _, _) = MakeRow(new[] { "Yes", "No" }, onCancel: () => cancelled = true);
            Assert.IsTrue(layer.HandleIntent(UiIntent.Cancel()));
            Assert.IsTrue(cancelled);
        }

        [Test]
        public void OnActiveChanged_False_MakesWidgetsInert() {
            var (layer, _, widgets) = MakeRow(new[] { "A", "B" });
            layer.OnActiveChanged(false);
            Assert.IsFalse(widgets[0].Element.focusable, "blocked layer's widgets are not focusable");
            Assert.AreEqual(PickingMode.Ignore, widgets[0].Element.pickingMode, "and not pickable");
            layer.OnActiveChanged(true);
            Assert.IsTrue(widgets[0].Element.focusable);
            Assert.AreEqual(PickingMode.Position, widgets[0].Element.pickingMode);
        }

        [Test]
        public void Skip_IsIgnoredByMenus() {
            var (layer, _, _) = MakeRow(new[] { "A" });
            Assert.IsFalse(layer.HandleIntent(UiIntent.Skip()), "menus don't consume Skip");
        }

        [Test]
        public void Cursor_WarpsOnKeyboardNav_NotOnInitialFocus() {
            var cursor = new RecordingCursor();
            var (layer, _, _) = MakeRow(new[] { "A", "B", "C" }, cursor: cursor);

            layer.FocusIndex(0);
            Assert.AreEqual(0, cursor.Warps.Count,
                "initial focus (e.g. a dialog default, or a click via FocusInEvent) must NOT warp the cursor");

            layer.HandleIntent(UiIntent.Move(NavDirection.Next));
            Assert.AreEqual(1, cursor.Warps.Count, "keyboard MoveFocus warps the cursor");
            Assert.AreEqual(new Vector2(125f, 10f), cursor.Warps[0],
                "warps to the focused widget centre (widget 1 at canonical (100,0,50,20))");
        }

        // --- SetWidgets: swapping the list in place (the inventory grid rebuilds constantly) ---

        private static NavWidget At(Rect rect, Action primary) =>
            new NavWidget(new VisualElement(), null, rect, primary, null);

        [Test]
        public void SetWidgets_ReplacesTheList_SoActivateHitsTheNewWidget() {
            int oldFired = 0, newFired = 0;
            var cursor = new RecordingCursor();
            var layer = new NavigableLayer("t", CaptureMode.Passive,
                new List<NavWidget> { At(new Rect(0, 0, 100, 100), () => oldFired++) },
                onCancel: null, cursor: cursor);
            layer.FocusIndex(0);

            layer.SetWidgets(new List<NavWidget> { At(new Rect(0, 0, 100, 100), () => newFired++) });
            layer.HandleIntent(UiIntent.Activate());

            Assert.AreEqual(0, oldFired, "the discarded widget must not be reachable");
            Assert.AreEqual(1, newFired);
        }

        /// <summary>
        /// In the original, focus IS the cursor — menupage_navigate warps it to the chosen entry
        /// (canassa MENUPAGE.C:312-315) — so a redraw cannot move focus. Ours is bound to element
        /// identity, so after a swap it re-resolves from the cursor position. An index would be
        /// wrong on its own terms too: the inventory re-sorts its container on every render, so
        /// slot indices are not stable across a rebuild.
        /// </summary>
        [Test]
        public void SetWidgets_ReResolvesFocus_ToTheWidgetUnderTheCursor() {
            int first = 0, second = 0;
            var cursor = new RecordingCursor();
            cursor.WarpTo(new Vector2(150f, 50f)); // over the second widget below
            var layer = new NavigableLayer("t", CaptureMode.Passive,
                new List<NavWidget> { At(new Rect(0, 0, 100, 100), () => { }) },
                onCancel: null, cursor: cursor);

            layer.SetWidgets(new List<NavWidget> {
                At(new Rect(0, 0, 100, 100), () => first++),
                At(new Rect(100, 0, 100, 100), () => second++),
            });
            layer.HandleIntent(UiIntent.Activate());

            Assert.AreEqual(0, first);
            Assert.AreEqual(1, second, "focus must land on the widget the cursor is over");
        }

        [Test]
        public void SetWidgets_LeavesNothingFocused_WhenTheCursorIsOverNoWidget() {
            int fired = 0;
            var cursor = new RecordingCursor();
            cursor.WarpTo(new Vector2(900f, 900f));
            var layer = new NavigableLayer("t", CaptureMode.Passive,
                new List<NavWidget> { At(new Rect(0, 0, 100, 100), () => { }) },
                onCancel: null, cursor: cursor);

            layer.SetWidgets(new List<NavWidget> { At(new Rect(0, 0, 100, 100), () => fired++) });

            Assert.IsFalse(layer.HandleIntent(UiIntent.Activate()),
                "no widget under the cursor means no focus, so Activate is unhandled");
            Assert.AreEqual(0, fired);
        }

        [Test]
        public void SetWidgets_AppliesTheCurrentActiveState_ToTheNewWidgets() {
            var layer = new NavigableLayer("t", CaptureMode.Passive,
                new List<NavWidget> { At(new Rect(0, 0, 100, 100), () => { }) },
                onCancel: null, cursor: new RecordingCursor());
            layer.OnActiveChanged(false); // an Exclusive layer (the quantity picker) sits above

            NavWidget fresh = At(new Rect(0, 0, 100, 100), () => { });
            layer.SetWidgets(new List<NavWidget> { fresh });

            Assert.IsFalse(fresh.Element.focusable,
                "widgets added while a modal is up must arrive inert, not interactable");
            Assert.AreEqual(PickingMode.Ignore, fresh.Element.pickingMode);
        }

        // --- Hidden-widget nav stall (task-7): a live element whose resolvedStyle.visibility is
        // Hidden refuses UI Toolkit focus and clears the panel's focus WITHOUT firing FocusInEvent, so
        // _focusIndex — updated only from that event — never advances past it and every later Tab
        // recomputes the same stuck target. resolvedStyle only resolves an inline style through a real
        // panel + layout pass (unattached VisualElements, as MakeRow above uses, never resolve it), so
        // these tests build a throwaway UIDocument/PanelSettings and force ValidateLayout, mirroring
        // CanonicalStageTests / ItemGridRendererTests.AttachToRuntimePanel. ---

        // Elements this batch of tests spawned, torn down after each test so panels/GameObjects don't
        // leak across the Editor test run.
        private readonly List<UnityEngine.Object> _panelScratch = new();

        [TearDown]
        public void TearDown() {
            foreach (UnityEngine.Object o in _panelScratch) {
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }
            _panelScratch.Clear();
        }

        // Builds a row of widgets attached to a real panel (unlike MakeRow's bare VisualElements), with
        // widget i hidden via style.visibility whenever hidden[i] is true, then forces a synchronous
        // layout pass so resolvedStyle actually reflects it. focusable=true on every element mirrors
        // InputLayerStack's OnActiveChanged(true) on push (real REQ widgets start non-focusable and are
        // only made focusable when their layer becomes the active top of stack).
        //
        // Initial focus is seeded onto widget 0 via SetWidgets' cursor-under-position resolution
        // (WidgetUnderCursor, already covered by SetWidgets_ReResolvesFocus_ToTheWidgetUnderTheCursor
        // above) instead of FocusIndex()/element.Focus(): that path's _focusIndex update depends on
        // FocusInEvent round-tripping through the live panel, which is exactly the mechanism the bug
        // report says a hidden element breaks, but which a *visible* widget 0 has no reason to break —
        // seeding via cursor math keeps this helper's setup independent of that either way, so a test
        // failure below can only mean the Cyclic/Spatial skip logic is wrong, not a setup fluke.
        private (NavigableLayer layer, int[] fired, List<VisualElement> elements) MakeRowOnPanel(
            string[] labels, bool[] hidden) {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var go = new GameObject("NavigableLayerHiddenWidgetTestPanel");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = settings;
            _panelScratch.Add(go);
            _panelScratch.Add(settings);

            var fired = new int[labels.Length];
            var elements = new List<VisualElement>();
            var widgets = new List<NavWidget>();
            for (int i = 0; i < labels.Length; i++) {
                int idx = i;
                var el = new VisualElement { name = $"widget_{i}", focusable = true };
                if (hidden[i]) el.style.visibility = Visibility.Hidden;
                document.rootVisualElement.Add(el);
                elements.Add(el);
                widgets.Add(new NavWidget(el, labels[i], new Rect(i * 100, 0, 50, 20), () => fired[idx]++, null));
            }

            // Forces the panel to actually resolve styles/layout NOW rather than on the next
            // player-loop tick, which a plain [Test] never sees (same reflection hook as
            // CanonicalStageTests.Fill_ReallySpansTheAttachedPanel / ItemGridRendererTests.ForceLayout).
            MethodInfo validateLayout = document.rootVisualElement.panel.GetType()
                .GetMethod("ValidateLayout", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            validateLayout?.Invoke(document.rootVisualElement.panel, null);

            var cursor = new RecordingCursor();
            cursor.WarpTo(widgets[0].Center);
            var layer = new NavigableLayer("test", CaptureMode.Passive, new List<NavWidget>(), null, cursor);
            layer.SetWidgets(widgets); // _focusIndex -> 0 via WidgetUnderCursor, no focus events involved

            return (layer, fired, elements);
        }

        [Test]
        public void HiddenWidget_SanityCheck_ResolvedStyleReportsHiddenThroughARealPanel() {
            // Guards the realism of every test below: if this ever stops reporting Hidden (e.g. a Unity
            // API change to when styles resolve), the hidden-skip tests would pass vacuously because
            // CanFocus would see a visible element and never actually exercise the skip path.
            var (_, _, elements) = MakeRowOnPanel(new[] { "A", "B" }, new[] { false, true });
            Assert.AreEqual(Visibility.Hidden, elements[1].resolvedStyle.visibility,
                "precondition: the panel must actually resolve the inline Hidden style before the " +
                "skip tests below mean anything");
        }

        [Test]
        public void Tab_SkipsAHiddenWidget_AndLandsOnTheOneAfterIt() {
            // Reproduces the measured stall: widget 1 is hidden (like the inventory's "More Info"
            // button under req-hidden), so a naive Cyclic(1) from widget 0 would target it, call
            // Focus() on an element that refuses focus, and never advance _focusIndex again. The fix
            // must step past it and land on widget 2.
            var (layer, fired, _) = MakeRowOnPanel(new[] { "A", "B", "C" }, new[] { false, true, false });

            Assert.IsTrue(layer.HandleIntent(UiIntent.Move(NavDirection.Next)),
                "Tab must still be handled even though the immediate next widget is hidden");
            layer.HandleIntent(UiIntent.Activate());

            Assert.AreEqual(1, fired[2], "focus must skip the hidden widget 1 and land on widget 2");
            Assert.AreEqual(0, fired[1], "the hidden widget itself must never be activated");

            // And Tab again must not be stuck re-targeting the same hidden widget forever: from
            // widget 2, Next wraps and must skip widget 1 again, landing back on widget 0.
            Assert.IsTrue(layer.HandleIntent(UiIntent.Move(NavDirection.Next)));
            layer.HandleIntent(UiIntent.Activate());
            Assert.AreEqual(1, fired[0], "wrapping past the hidden widget must still reach widget 0");
        }

        [Test]
        public void Spatial_DoesNotSelectAHiddenWidget() {
            // Widget 1 (the immediate right neighbour of widget 0) is hidden; Right must skip it and
            // land on widget 2, not stall or select the hidden one.
            var (layer, fired, _) = MakeRowOnPanel(new[] { "A", "B", "C" }, new[] { false, true, false });

            Assert.IsTrue(layer.HandleIntent(UiIntent.Move(NavDirection.Right)),
                "Right must still find widget 2 even though the nearer widget 1 is hidden");
            layer.HandleIntent(UiIntent.Activate());

            Assert.AreEqual(1, fired[2], "Right must skip the hidden immediate neighbour");
            Assert.AreEqual(0, fired[1], "the hidden widget itself must never be activated");
        }

        [Test]
        public void Spatial_ReturnsUnhandled_WhenNoFocusableWidgetExistsInThatDirection() {
            // Only widget 1 sits to the right of widget 0, and it is hidden — so Right has no focusable
            // candidate at all and must report unhandled rather than selecting the hidden widget.
            var (layer, fired, _) = MakeRowOnPanel(new[] { "A", "B" }, new[] { false, true });

            Assert.IsFalse(layer.HandleIntent(UiIntent.Move(NavDirection.Right)),
                "no focusable widget to the right means Right is unhandled");
            Assert.AreEqual(0, fired[1], "the hidden widget must never be activated");
        }

        [Test]
        public void AllWidgetsHidden_TabDoesNotHang_AndReportsIntentUnhandled() {
            // If every widget in the list is unfocusable, Cyclic must still terminate (bounded by the
            // widget count) instead of looping forever, and MoveFocus must report the intent unhandled.
            var (layer, fired, _) = MakeRowOnPanel(new[] { "A", "B", "C" }, new[] { true, true, true });

            Assert.IsFalse(layer.HandleIntent(UiIntent.Move(NavDirection.Next)),
                "an all-hidden list has nothing to focus, so Tab must be unhandled, not hang");
            Assert.AreEqual(0, fired[0] + fired[1] + fired[2], "nothing should ever have been activated");
        }

        // --- SetWidgets' re-resolve must be LAZY (regression for the eager-resolve bug: an item
        // cell's rect comes from a LIVE Func<Rect> — ItemGridRenderer.StageRect — that reads real
        // resolved geometry. SetWidgets is called the instant those cells exist, before UI Toolkit
        // has laid them out, so resolving focus-under-cursor AT SetWidgets time can never match any
        // widget: every live rect still reads a degenerate zero-size box. This never showed up on
        // the bare-VisualElement tests above (SetWidgets_ReResolvesFocus_ToTheWidgetUnderTheCursor
        // etc.) because those widgets carry a STATIC Rect handed in at construction, which reads the
        // same value whether or not a layout pass ran. ---

        [Test]
        public void SetWidgets_WithALiveRectWidget_ResolvesFocusAfterLayoutCatchesUp_NotAtSwapTime() {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var go = new GameObject("NavigableLayerLiveRectTestPanel");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = settings;
            _panelScratch.Add(go);
            _panelScratch.Add(settings);

            var el = new VisualElement { name = "cell", focusable = true };
            el.style.position = Position.Absolute;
            el.style.left = 100;
            el.style.top = 100;
            el.style.width = 50;
            el.style.height = 50;
            document.rootVisualElement.Add(el);

            // No layout pass has run yet — exactly the state ItemGridRenderer's freshly-built cells
            // are in when RenderCurrent's last statement calls SetWidgets. worldBound is degenerate
            // (zero-size), so a Rect.Contains test against it can never match any point.
            Assert.LessOrEqual(el.worldBound.width, 0f,
                "precondition: no layout pass has run yet, so the live rect is still unresolved");

            int fired = 0;
            // Live rect (the NavWidget ctor overload ItemGridRenderer's cells use via StageRect):
            // reads el.worldBound fresh on every call, not a value captured up front.
            var widget = new NavWidget(el, null, () => el.worldBound, () => fired++, null);

            var cursor = new RecordingCursor();
            var layer = new NavigableLayer("t", CaptureMode.Passive, new List<NavWidget>(), null, cursor);

            // The swap itself — same timing as RenderCurrent's SetWidgets call, before layout.
            layer.SetWidgets(new List<NavWidget> { widget });

            // Layout catches up before the next intent arrives (a real frame in the game; forced
            // here for a deterministic Editor test, same reflection hook MakeRowOnPanel uses).
            MethodInfo validateLayout = document.rootVisualElement.panel.GetType()
                .GetMethod("ValidateLayout", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            validateLayout?.Invoke(document.rootVisualElement.panel, null);
            Assert.Greater(el.worldBound.width, 0f,
                "sanity: the forced layout pass must have actually resolved the element's geometry");

            // The cursor is over the widget's NOW-resolved centre. With the eager bug, _focusIndex
            // was permanently pinned to -1 by the zero-rect scan inside SetWidgets above, and no
            // later cursor position could ever change that. With the lazy fix, this Activate is the
            // first intent since the swap, so it re-scans now — against the resolved geometry — and
            // must land on and fire the widget.
            cursor.WarpTo(el.worldBound.center);

            Assert.IsTrue(layer.HandleIntent(UiIntent.Activate()),
                "focus must resolve lazily against the now-laid-out geometry, not the stale zero " +
                "rect captured at SetWidgets time");
            Assert.AreEqual(1, fired, "the live-rect widget under the cursor must have been activated");
        }
    }
}
