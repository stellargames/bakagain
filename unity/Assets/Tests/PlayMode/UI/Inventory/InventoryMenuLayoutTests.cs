namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Geometry tests for the parts of <see cref="InventoryMenu"/> that are placed from
    /// <see cref="InventoryLayout"/> rather than by the REQ resource: the black item-panel fills,
    /// the empty-slot silhouettes, the container window's drag-time hairline, and the drag
    /// threshold.
    ///
    /// <para>Same two groups as <c>ItemInspectPanelLayoutTests</c>. <b>Mechanism</b> tests drive the
    /// screen with a synthetic layout whose every number is non-round, asymmetric and unlike the
    /// shipped one, so a screen that quietly kept its own literals cannot reproduce them.
    /// <b>Faithfulness</b> tests use the shipped defaults and assert the exact numbers the
    /// pre-conversion code hardcoded.</para>
    ///
    /// <para><see cref="InventoryMenu"/> is a DI'd MonoBehaviour whose renders normally need a real
    /// REQ build, so these drive the individual drawing methods directly (the same reflection the
    /// sibling <c>InventoryPointerTests</c> uses to mount the gesture recogniser) with the stage
    /// pointed at a real UIDocument panel.</para>
    /// </summary>
    public class InventoryMenuLayoutTests {
        // Every value differs from the shipped one, from the other axis, and from round numbers.
        private const float SynthFullX = 37f, SynthFullY = 41f, SynthFullW = 1301f, SynthFullH = 617f;
        private const float SynthDollX = 53f, SynthDollY = 71f, SynthDollW = 389f, SynthDollH = 503f;
        private const float SynthGenX = 457f, SynthGenY = 83f, SynthGenW = 911f, SynthGenH = 547f;
        // The cell grid the silhouettes are DERIVED from (task-54), plus the one-original-pixel
        // nudge below the cell's top edge — all synthetic, so a screen that kept the shipped
        // numbers (or the deleted free-standing points) cannot reproduce what these produce.
        private const float SynthGridX = 47f, SynthGridY = 59f;
        private const float SynthCellW = 213f, SynthCellH = 151f;
        private const float SynthNudge = 13f;
        // Crossbow is paperdoll row 1, armor row 2 (ItemGridRenderer.TryPaperdollSlot), both at
        // column 0 — so their silhouettes sit at the grid's left inset, one cell-row apart.
        private const float SynthBowX = SynthGridX, SynthBowY = SynthGridY + SynthCellH + SynthNudge;
        private const float SynthArmX = SynthGridX, SynthArmY = SynthGridY + (2f * SynthCellH) + SynthNudge;
        private const float SynthBorderX = 3f, SynthBorderY = 11f;
        private const float SynthDragThreshold = 47f;

        private GameObject _go;
        private GameObject _loaderGo;
        private PanelSettings _panelSettings;
        private VisualElement _stage;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_loaderGo != null) { Object.DestroyImmediate(_loaderGo); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        private static LayoutHint Box(float left, float top, float width, float height) =>
            new LayoutHint {
                Left = LayoutLength.Px(left), Top = LayoutLength.Px(top),
                Width = LayoutLength.Px(width), Height = LayoutLength.Px(height),
            };

        private static LayoutHint Point(float left, float top) =>
            new LayoutHint { Left = LayoutLength.Px(left), Top = LayoutLength.Px(top) };

        /// <summary>A cell-grid area of the shape <c>InventoryLayout.GridArea</c> carries — the
        /// geometry the empty-slot silhouettes are derived from. Column/row COUNTS are the shipped
        /// ones; nothing in this file depends on them, only on the origin and the cell size.</summary>
        private static LayoutHint Grid(float left, float top, float cellWidth, float cellHeight) =>
            new LayoutHint {
                Left = LayoutLength.Px(left), Top = LayoutLength.Px(top),
                Grid = new LayoutGrid {
                    CellWidth = LayoutLength.Px(cellWidth),
                    CellHeight = LayoutLength.Px(cellHeight),
                    Columns = 7, Rows = 4,
                },
            };

        /// <summary>An InventoryMenu wired far enough to draw: a real panel for its stage, an
        /// inventory-type container as the displayed one (so the member/loot branch can be picked
        /// by the caller), and a <c>hotspot_32</c> for the container window's border to hang on.
        /// The returned layout is the instance the menu will read — mutate it to drive a test.</summary>
        private InventoryMenu BuildMenu(out InventoryLayout layout) {
            _go = new GameObject("InventoryMenuLayoutUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;

            var menu = _go.AddComponent<InventoryMenu>();
            var session = new GameSession();
            menu.Construct(session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                new NoOpDialogs());

            var displayed = new RuntimeContainer {
                Capacity = 24,
                ContainerType = GameData.Resources.Data.SaveGameContainerType.Inventory,
            };
            SetField(menu, "_displayed", displayed);

            _stage = document.rootVisualElement;
            SetField(menu, "_stage", _stage);
            _stage.Add(new VisualElement { name = "hotspot_32" });

            // No UserInterfaceLoader sibling, so InventoryMenu.Layout falls through to _defaultLayout
            // — which is a live object the test can rewrite, standing in for an override's REQ block.
            layout = (InventoryLayout)typeof(InventoryMenu)
                .GetField("_defaultLayout", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(menu);
            return menu;
        }

        /// <summary>Give <paramref name="menu"/> a <see cref="UserInterfaceLoader"/> whose loaded REQ
        /// carries <paramref name="inventory"/> — i.e. what a mod override's REQ_INV produces, and
        /// the branch of <c>InventoryMenu.Layout</c>'s <c>??</c> that <see cref="BuildMenu"/>
        /// deliberately does not exercise.
        ///
        /// <para>The loader sits on its own <b>inactive</b> GameObject so Unity never runs its
        /// OnEnable, which would start a real Addressables load of REQ_OPT0.DAT. Nothing here
        /// touches the loader except the <c>Inventory</c> property the menu reads.</para></summary>
        private void AttachLoader(InventoryMenu menu, InventoryLayout inventory) {
            _loaderGo = new GameObject("UserInterfaceLoaderUnderTest");
            _loaderGo.SetActive(false);
            var loader = _loaderGo.AddComponent<BakAgain.ResourceManagement.Loaders.UserInterfaceLoader>();
            SetField(loader, "_userInterface",
                new GameData.Resources.Menu.UserInterface("REQ_INV.DAT") { Inventory = inventory });
            SetField(menu, "_ui", loader);
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(target, value);

        private static void Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, args);

        private static void ApplySynthetic(InventoryLayout layout) {
            layout.FullItemsBox = Box(SynthFullX, SynthFullY, SynthFullW, SynthFullH);
            layout.PaperdollBox = Box(SynthDollX, SynthDollY, SynthDollW, SynthDollH);
            layout.GeneralItemsBox = Box(SynthGenX, SynthGenY, SynthGenW, SynthGenH);
            // The silhouettes are not positioned directly any more — they follow this grid. Leaving
            // CrossbowPlaceholder/ArmorPlaceholder null is the shipped state (null = derive).
            layout.GridArea = Grid(SynthGridX, SynthGridY, SynthCellW, SynthCellH);
            layout.PaperdollPlaceholderNudgeY = SynthNudge;
            layout.ContainerBorderWidthX = SynthBorderX;
            layout.ContainerBorderWidthY = SynthBorderY;
            layout.DragThreshold = SynthDragThreshold;
        }

        private List<VisualElement> Named(string name) {
            var found = new List<VisualElement>();
            foreach (VisualElement child in _stage.Children()) {
                if (child.name == name) { found.Add(child); }
            }
            return found;
        }

        /// <summary>An element's inset and size, asserted to be explicit px first — an unset inline
        /// style reads back as 0px, so without the keyword check a missing style would pass as
        /// "at the origin, zero-sized".</summary>
        private static void AssertBox(VisualElement el, string what,
            float left, float top, float width, float height) {
            Assert.IsNotNull(el, what + " not found");
            Assert.AreEqual(Position.Absolute, el.style.position.value, what + " must be absolute");
            AssertPx(el.style.left, left, what + " left");
            AssertPx(el.style.top, top, what + " top");
            AssertPx(el.style.width, width, what + " width");
            AssertPx(el.style.height, height, what + " height");
        }

        private static void AssertPoint(VisualElement el, string what, float left, float top) {
            Assert.IsNotNull(el, what + " not found");
            Assert.AreEqual(Position.Absolute, el.style.position.value, what + " must be absolute");
            AssertPx(el.style.left, left, what + " left");
            AssertPx(el.style.top, top, what + " top");
        }

        private static void AssertPx(StyleLength actual, float expected, string what) {
            Assert.AreEqual(StyleKeyword.Undefined, actual.keyword,
                what + " must be an explicit length, not a style keyword");
            Assert.AreEqual(LengthUnit.Pixel, actual.value.unit, what + " must be in px");
            Assert.AreEqual(expected, actual.value.value, 0.001f, what);
        }

        private static float Threshold(InventoryMenu menu) {
            var gesture = (BakAgain.UI.InputCore.DragGestureManipulator)typeof(InventoryMenu)
                .GetField("_gesture", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(menu);
            Assert.IsNotNull(gesture, "the gesture recogniser must be mounted");
            return gesture.DragThreshold;
        }

        // ======================================================================================
        // Mechanism — synthetic geometry only.
        // ======================================================================================

        /// <summary>Loot mode (and the inspect view) paint ONE continuous fill over the background
        /// art's divider — <c>UI_DrawInventory</c> @0x5687d.</summary>
        [Test]
        public void PanelFill_LootMode_IsTheOneBoxTheLayoutDeclares() {
            InventoryMenu menu = BuildMenu(out InventoryLayout layout);
            ApplySynthetic(layout);

            Invoke(menu, "DrawPanelBackground", true);

            List<VisualElement> boxes = Named("panel_bg");
            Assert.AreEqual(1, boxes.Count, "loot mode paints a single continuous fill");
            AssertBox(boxes[0], "continuous fill", SynthFullX, SynthFullY, SynthFullW, SynthFullH);
        }

        /// <summary>Member mode paints the paperdoll and general fills separately, leaving the
        /// background art's divider showing in the gap.</summary>
        [Test]
        public void PanelFill_MemberMode_IsTheTwoBoxesTheLayoutDeclares() {
            InventoryMenu menu = BuildMenu(out InventoryLayout layout);
            ApplySynthetic(layout);

            Invoke(menu, "DrawPanelBackground", false);

            List<VisualElement> boxes = Named("panel_bg");
            Assert.AreEqual(2, boxes.Count, "member mode paints the two split fills");
            AssertBox(boxes[0], "paperdoll fill", SynthDollX, SynthDollY, SynthDollW, SynthDollH);
            AssertBox(boxes[1], "general fill", SynthGenX, SynthGenY, SynthGenW, SynthGenH);
        }

        /// <summary>The two empty-slot silhouettes (INVSHP2.BMX#10/#11) are positioned from the
        /// layout's cell grid — their own paperdoll cell's top-left plus the layout's nudge — not
        /// from literals beside the sprite keys.</summary>
        [Test]
        public void Placeholders_TakeTheirPositionsFromTheLayout() {
            InventoryMenu menu = BuildMenu(out InventoryLayout layout);
            ApplySynthetic(layout);

            Invoke(menu, "DrawPaperdollPlaceholders");

            List<VisualElement> placeholders = Named("paperdoll_placeholder");
            Assert.AreEqual(2, placeholders.Count, "crossbow + armor silhouettes");
            AssertPoint(placeholders[0], "crossbow silhouette", SynthBowX, SynthBowY);
            AssertPoint(placeholders[1], "armor silhouette", SynthArmX, SynthArmY);
        }

        /// <summary>
        /// <b>The property task-54 buys.</b> The silhouettes are derived from the grid, so resizing
        /// the grid moves them with their cells.
        ///
        /// <para>Before the change they were free-standing points: growing <c>CellHeight</c> moved
        /// the crossbow and armor cells down while the silhouettes stayed put, and the armor
        /// silhouette ended up sitting inside the crossbow slot. So this asserts the movement
        /// itself, not just the resulting numbers — a screen that re-pinned the silhouettes to
        /// fixed coordinates would pass the faithfulness test below and fail this one.</para>
        /// </summary>
        [Test]
        public void Placeholders_FollowTheGridWhenItsCellsAreResized() {
            InventoryMenu menu = BuildMenu(out InventoryLayout layout);
            ApplySynthetic(layout);

            Invoke(menu, "DrawPaperdollPlaceholders");
            List<VisualElement> before = Named("paperdoll_placeholder");
            AssertPoint(before[0], "crossbow silhouette (original cells)", SynthBowX, SynthBowY);
            AssertPoint(before[1], "armor silhouette (original cells)", SynthArmX, SynthArmY);

            // A taller cell — and nothing else — is enough to move both cells, so it must move both
            // silhouettes by the same amount: one cell row for the crossbow, two for the armor.
            const float taller = SynthCellH + 61f;
            layout.GridArea.Grid.CellHeight = LayoutLength.Px(taller);

            Invoke(menu, "DrawPaperdollPlaceholders");

            List<VisualElement> after = Named("paperdoll_placeholder");
            Assert.AreEqual(2, after.Count, "the previous pair is cleared, not accumulated");
            AssertPoint(after[0], "crossbow silhouette (taller cells)",
                SynthGridX, SynthGridY + taller + SynthNudge);
            AssertPoint(after[1], "armor silhouette (taller cells)",
                SynthGridX, SynthGridY + (2f * taller) + SynthNudge);
        }

        /// <summary>Null means "derive"; a non-null value is an explicit override and wins. An
        /// author who pins a point takes responsibility for keeping it with its cell — which is why
        /// this one deliberately does NOT sit where the grid would have put it.</summary>
        [Test]
        public void Placeholders_HonourAnExplicitOverridePosition() {
            InventoryMenu menu = BuildMenu(out InventoryLayout layout);
            ApplySynthetic(layout);
            layout.CrossbowPlaceholder = Point(613f, 907f);

            Invoke(menu, "DrawPaperdollPlaceholders");

            List<VisualElement> placeholders = Named("paperdoll_placeholder");
            Assert.AreEqual(2, placeholders.Count, "crossbow + armor silhouettes");
            AssertPoint(placeholders[0], "overridden crossbow silhouette", 613f, 907f);
            // The one left null still derives — an override on one is not an override on both.
            AssertPoint(placeholders[1], "derived armor silhouette", SynthArmX, SynthArmY);
        }

        /// <summary>A layout with no cell grid has no cell to hang a silhouette off, and there is no
        /// override to fall back on — so nothing is drawn, rather than a sprite landing at an origin
        /// the data never stated. Same graceful degradation ItemGridRenderer gives a null grid.</summary>
        [Test]
        public void Placeholders_WithNoGridAndNoOverride_DrawNothing() {
            InventoryMenu menu = BuildMenu(out InventoryLayout layout);
            ApplySynthetic(layout);
            layout.GridArea = null;

            Invoke(menu, "DrawPaperdollPlaceholders");

            Assert.AreEqual(0, Named("paperdoll_placeholder").Count,
                "no grid to derive from and no override — no silhouette");
        }

        /// <summary>
        /// The screen reads its geometry from the REQ resource an override authored, not from the
        /// built-in defaults.
        ///
        /// <para>Every other test in this file mutates <c>_defaultLayout</c>, which is the OTHER
        /// branch of <c>InventoryMenu.Layout</c>'s <c>??</c> — and because the shipped extractor
        /// emits a value-identical <see cref="InventoryLayout"/> onto REQ_INV, dropping
        /// <c>_ui?.Inventory</c> entirely would leave the shipped screen pixel-identical and every
        /// suite green while silently ignoring every mod override. So this is the one test that
        /// proves the override path is connected at all: a loader carrying a deliberately
        /// non-default block, and the screen has to move to it.</para>
        /// </summary>
        [Test]
        public void Layout_ComesFromTheScreensResource_NotTheBuiltInDefault() {
            InventoryMenu menu = BuildMenu(out InventoryLayout builtIn);
            var overridden = new InventoryLayout();
            ApplySynthetic(overridden);
            AttachLoader(menu, overridden);

            // Guard the guard: the synthetic block must actually differ from the built-in one, or
            // this test would pass against a screen that ignored the resource.
            Assert.AreNotEqual(builtIn.PaperdollBox.Left.Value, overridden.PaperdollBox.Left.Value,
                "the fixture must differ from the default it is meant to displace");

            Invoke(menu, "DrawPanelBackground", false);
            List<VisualElement> boxes = Named("panel_bg");
            Assert.AreEqual(2, boxes.Count, "member mode paints the two split fills");
            AssertBox(boxes[0], "paperdoll fill", SynthDollX, SynthDollY, SynthDollW, SynthDollH);
            AssertBox(boxes[1], "general fill", SynthGenX, SynthGenY, SynthGenW, SynthGenH);

            Invoke(menu, "DrawPaperdollPlaceholders");
            List<VisualElement> placeholders = Named("paperdoll_placeholder");
            AssertPoint(placeholders[0], "crossbow silhouette", SynthBowX, SynthBowY);
            AssertPoint(placeholders[1], "armor silhouette", SynthArmX, SynthArmY);

            Invoke(menu, "EnsureStageInput");
            Assert.AreEqual(SynthDragThreshold, Threshold(menu), 0.001f,
                "the resource's drag threshold, not the model default");
        }

        /// <summary>A screen whose REQ carries no inventory block still gets the faithful geometry —
        /// the <c>??</c>'s other branch, which must survive the test above.</summary>
        [Test]
        public void Layout_WithAResourceThatCarriesNoInventoryBlock_FallsBackToTheDefaults() {
            InventoryMenu menu = BuildMenu(out InventoryLayout _);
            AttachLoader(menu, null);

            Invoke(menu, "DrawPanelBackground", false);

            List<VisualElement> boxes = Named("panel_bg");
            AssertBox(boxes[0], "paperdoll fill", 65f, 66f, 410f, 726f);
            AssertBox(boxes[1], "general fill", 525f, 66f, 1010f, 726f);
        }

        /// <summary>The container window's drag-time hairline takes a different width per axis,
        /// because the design frame is not an isotropic blow-up of what the original drew on — so
        /// one shared number would be wrong, and both come from the data.</summary>
        [Test]
        public void ContainerBorder_TakesItsWidthsFromTheLayout() {
            InventoryMenu menu = BuildMenu(out InventoryLayout layout);
            ApplySynthetic(layout);

            Invoke(menu, "UpdateDiscardBorder", true, true);

            VisualElement border = _stage.Q("container_window_border");
            Assert.IsNotNull(border, "a hovered drag over the window shows the border");
            Assert.AreEqual(SynthBorderX, border.style.borderLeftWidth.value, 0.001f, "left");
            Assert.AreEqual(SynthBorderX, border.style.borderRightWidth.value, 0.001f, "right");
            Assert.AreEqual(SynthBorderY, border.style.borderTopWidth.value, 0.001f, "top");
            Assert.AreEqual(SynthBorderY, border.style.borderBottomWidth.value, 0.001f, "bottom");
        }

        [Test]
        public void DragThreshold_ComesFromTheLayout() {
            InventoryMenu menu = BuildMenu(out InventoryLayout layout);
            ApplySynthetic(layout);

            Invoke(menu, "EnsureStageInput");

            Assert.AreEqual(SynthDragThreshold, Threshold(menu), 0.001f);
        }

        /// <summary>The first render happens before the screen's REQ resource has landed, so the
        /// recogniser is created against the model's default and the real value arrives later. It
        /// must reach the recogniser that is already mounted — and without replacing it, because a
        /// replacement would drop whatever pointer capture the old one was holding.</summary>
        [Test]
        public void DragThreshold_ArrivingAfterTheFirstRender_ReachesTheLiveRecogniser() {
            InventoryMenu menu = BuildMenu(out InventoryLayout layout);

            Invoke(menu, "EnsureStageInput");
            var first = (BakAgain.UI.InputCore.DragGestureManipulator)typeof(InventoryMenu)
                .GetField("_gesture", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(menu);
            Assert.AreEqual(20f, first.DragThreshold, 0.001f, "the model default, before the REQ lands");

            layout.DragThreshold = SynthDragThreshold;
            Invoke(menu, "EnsureStageInput");

            Assert.AreEqual(SynthDragThreshold, Threshold(menu), 0.001f);
            Assert.AreSame(first, typeof(InventoryMenu)
                    .GetField("_gesture", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(menu),
                "the recogniser is updated in place, not rebuilt — a rebuild loses pointer capture");
        }

        /// <summary>Zero is not a usable threshold: DragGestureManipulator promotes a press to a
        /// drag on <c>magnitude &lt;= threshold</c>, so at zero the first pointer move makes every
        /// press a drag and nothing can be selected — and the same number is the double-click
        /// position tolerance, so double-click dies too. The screen falls back to the original's
        /// own threshold rather than honour it.</summary>
        [Test]
        public void DragThreshold_Degenerate_FallsBackToTheFaithfulValue() {
            InventoryMenu menu = BuildMenu(out InventoryLayout layout);
            layout.DragThreshold = 0f;

            Invoke(menu, "EnsureStageInput");

            Assert.AreEqual(20f, Threshold(menu), 0.001f, "0 -> the faithful 20");

            layout.DragThreshold = -5f;
            Invoke(menu, "EnsureStageInput");
            Assert.AreEqual(20f, Threshold(menu), 0.001f, "negative -> the faithful 20");
        }

        // ======================================================================================
        // Faithfulness — the shipped geometry, at the numbers the pre-conversion code hardcoded.
        // ======================================================================================

        /// <summary>The fills and silhouettes at the coordinates the deleted literals produced:
        /// continuous VGA (13,11,294,121) -> (65,66,1470,726), paperdoll VGA (13,11,82,121) ->
        /// (65,66,410,726), general VGA (105,11,202,121) -> (525,66,1010,726), crossbow VGA (14,43)
        /// -> (70,258), armor VGA (14,73) -> (70,438).</summary>
        [Test]
        public void Faithful_FillsAndSilhouettesLandWhereTheOriginalDrawsThem() {
            InventoryMenu menu = BuildMenu(out InventoryLayout _);

            Invoke(menu, "DrawPanelBackground", true);
            AssertBox(Named("panel_bg")[0], "continuous fill", 65f, 66f, 1470f, 726f);

            Invoke(menu, "DrawPanelBackground", false);
            List<VisualElement> split = Named("panel_bg");
            AssertBox(split[0], "paperdoll fill", 65f, 66f, 410f, 726f);
            AssertBox(split[1], "general fill", 525f, 66f, 1010f, 726f);

            Invoke(menu, "DrawPaperdollPlaceholders");
            List<VisualElement> placeholders = Named("paperdoll_placeholder");
            AssertPoint(placeholders[0], "crossbow silhouette", 70f, 258f);
            AssertPoint(placeholders[1], "armor silhouette", 70f, 438f);
        }

        /// <summary>One of the original's pixels of hairline — 5 across, 6 down — and its ~4
        /// original px drag threshold, 20 across.</summary>
        [Test]
        public void Faithful_ContainerHairlineAndDragThreshold() {
            InventoryMenu menu = BuildMenu(out InventoryLayout _);

            Invoke(menu, "UpdateDiscardBorder", true, true);
            VisualElement border = _stage.Q("container_window_border");
            Assert.AreEqual(5f, border.style.borderLeftWidth.value, 0.001f);
            Assert.AreEqual(6f, border.style.borderTopWidth.value, 0.001f);

            Invoke(menu, "EnsureStageInput");
            Assert.AreEqual(20f, Threshold(menu), 0.001f);
        }
    
        /// <summary>
        /// The silhouettes are gated on the container being a MEMBER'S OWN PACK, not on a list of
        /// modes to suppress.
        /// </summary>
        /// <remarks>
        /// <c>UI_DrawInventory</c> draws the paperdoll block only when <c>containerType == 1</c>
        /// (0x568de). A loot window, a shop's shelf and the picklock's scratch container are all
        /// excluded by that one rule — which is why this asserts on the container type rather than
        /// on each mode: the previous form was a growing "|| IsSomethingMode" chain that needed a
        /// new clause for every screen added.
        /// </remarks>
        [Test]
        public void Placeholders_DrawOnlyForAMembersOwnPack() {
            InventoryMenu menu = BuildMenu(out InventoryLayout layout);
            ApplySynthetic(layout);

            Invoke(menu, "DrawPaperdollPlaceholders");
            Assert.AreEqual(2, Named("paperdoll_placeholder").Count, "a member's pack has both");

            foreach (GameData.Resources.Data.SaveGameContainerType notAPack in new[] {
                         GameData.Resources.Data.SaveGameContainerType.FixedWorldItem,  // a shop
                         GameData.Resources.Data.SaveGameContainerType.Chest,           // loot
                         GameData.Resources.Data.SaveGameContainerType.SharedKeys,      // the lock
                     }) {
                var other = new RuntimeContainer { Capacity = 24, ContainerType = notAPack };
                SetField(menu, "_displayed", other);

                Invoke(menu, "DrawPaperdollPlaceholders");

                Assert.AreEqual(0, Named("paperdoll_placeholder").Count,
                    $"{notAPack} is not a member's pack and must draw no silhouettes");
            }
        }
    }
}
