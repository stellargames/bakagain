namespace BakAgain.Tests.PlayMode.Inventory {
    using BakAgain.Tests.TestSupport;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using BakAgain.UI;
    using BakAgain.UI.Inventory;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// Tests for <see cref="ItemGridRenderer"/>, in two deliberately separate groups.
    ///
    /// <para><b>Mechanism</b> tests drive the renderer with a SYNTHETIC
    /// <see cref="InventoryLayout"/> whose every number is asymmetric and non-round (cell 137x91,
    /// 5 columns x 3 rows, origin 23,41, member shift 17, frame 911). Nothing in that fixture is a
    /// value any implementation would plausibly hardcode, so a renderer that quietly kept its own
    /// constants cannot reproduce these results by coincidence.</para>
    ///
    /// <para><b>Faithfulness</b> tests use the shipped geometry (<see cref="InventoryLayout"/>'s
    /// defaults + the 1600x1200 design frame) and assert the exact positions the pre-change
    /// implementation produced, so the screen is pinned numerically, not structurally.</para>
    ///
    /// <para>Every geometric assertion is on the FINAL resolved rect (<see cref="Resolved"/> walks
    /// the parent chain), never on a cell's own style — cells are now grid-relative inside a
    /// positioned host, so their own left/top mean nothing on their own.</para>
    ///
    /// <para>Icon loading is async and NOT exercised here (that needs a live resource service and
    /// is verified in-Editor) — <c>Render</c> is called with a null resources argument, which the
    /// renderer treats as "skip the icon load".</para>
    /// </summary>
    public class ItemGridRendererTests {
        private readonly List<GameObject> _spawned = new();
        private readonly List<Object> _scratchAssets = new();

        // --- fixtures -------------------------------------------------------------------------

        // The shipped geometry: InventoryLayout's defaults ARE the faithful values, and REQ_INV's
        // design frame is the canonical 1600x1200 space.
        private static InventoryLayout FaithfulLayout() => new InventoryLayout();

        private static DesignFrame FaithfulFrame() => new DesignFrame { Width = 1600, Height = 1200 };

        // Deliberately hostile fixture: no value here coincides with the shipped geometry, with a
        // round number, or with the other axis. See the class doc.
        private const float SynthCellW = 137f;
        private const float SynthCellH = 91f;
        private const int SynthColumns = 5;
        private const int SynthRows = 3;
        private const float SynthLeft = 23f;
        private const float SynthTop = 41f;
        private const float SynthShiftX = 17f;
        private const float SynthLootH = 653f;
        private const int SynthFrameW = 911;

        private static InventoryLayout SyntheticLayout(int columns = SynthColumns, int rows = SynthRows) =>
            new InventoryLayout {
                GridArea = new LayoutHint {
                    Left = LayoutLength.Px(SynthLeft),
                    Top = LayoutLength.Px(SynthTop),
                    Grid = new LayoutGrid {
                        CellWidth = LayoutLength.Px(SynthCellW),
                        CellHeight = LayoutLength.Px(SynthCellH),
                        Columns = columns,
                        Rows = rows,
                    },
                },
                MemberShiftX = LayoutLength.Px(SynthShiftX),
                // Height only, deliberately: the renderer centres the cluster horizontally on the
                // design FRAME and only the vertical axis reads the box (see the Loot enum doc /
                // ResolveGridOrigin). A LootBox.Width here would be a decoy no assertion depends
                // on; leaving it Auto means anyone who "restores" the box formula for horizontal
                // symmetry breaks these tests loudly instead of quietly moving the shipped screen.
                LootBox = new LayoutHint { Height = LayoutLength.Px(SynthLootH) },
            };

        private static DesignFrame SyntheticFrame() =>
            new DesignFrame { Width = SynthFrameW, Height = 707 };

        [TearDown]
        public void TearDown() {
            foreach (GameObject go in _spawned) {
                if (go != null) {
                    Object.DestroyImmediate(go);
                }
            }
            _spawned.Clear();
            foreach (Object asset in _scratchAssets) {
                if (asset != null) {
                    Object.DestroyImmediate(asset);
                }
            }
            _scratchAssets.Clear();
        }

        // obj80: rations-style stack (flags 0x8800 = Stackable + always-show-count), 1 slot.
        // obj72: plain item, no count display.
        private static ObjectInfoSet BuildObjects() {
            var stacking = new ObjectInfo("obj80") {
                Number = 80, Icon = 0, MaxAmount = 5, InventorySlots = 1, Flags = (ObjectFlags)0x8800,
            };
            var single = new ObjectInfo("obj72") { Number = 72, Icon = 0, MaxAmount = 1, InventorySlots = 1 };
            return new ObjectInfoSet("objects", new List<ObjectInfo> { stacking, single });
        }

        private static RuntimeContainer BuildContainer() {
            var container = new RuntimeContainer();
            container.Items.Add(new RuntimeItem(80, 2, 0));
            container.Items.Add(new RuntimeItem(72, 4, 0));
            return container;
        }

        // n items of the given slot count, all of one object id.
        private static ObjectInfoSet ObjectsWithSlots(int slots) =>
            new ObjectInfoSet("objects", new List<ObjectInfo> {
                new ObjectInfo("o") { Number = 60, Icon = 0, MaxAmount = 1, InventorySlots = slots },
            });

        private static RuntimeContainer ContainerOf(int count) {
            var container = new RuntimeContainer();
            for (int i = 0; i < count; i++) {
                container.Items.Add(new RuntimeItem(60, 1, 0));
            }
            return container;
        }

        // --- resolved-geometry helper ---------------------------------------------------------

        /// <summary>The cell's FINAL rect in <paramref name="root"/>'s space: its own grid-relative
        /// offset plus every positioned ancestor up to (not including) the root. This is what the
        /// player sees — asserting a cell's own style.left would prove nothing now that cells sit
        /// inside an offset grid host.
        ///
        /// <para>Summing insets is only <i>equivalent</i> to the resolved position while every
        /// element in the chain is <see cref="Position.Absolute"/> and every length is an explicit
        /// px value — so both are asserted here rather than assumed. A regression that left cells
        /// in flow (where an inset is a relative nudge, not a position) or that emitted percent
        /// lengths (whose <c>.value.value</c> would be summed as if it were px) would otherwise
        /// keep every geometric assertion in this file green while the screen visibly moved.</para>
        /// </summary>
        private static Rect Resolved(VisualElement root, string name) {
            VisualElement el = root.Q<VisualElement>(name);
            Assert.IsNotNull(el, name + " not found");
            float x = 0f;
            float y = 0f;
            for (VisualElement e = el; e != null && !ReferenceEquals(e, root); e = e.parent) {
                Assert.AreEqual(Position.Absolute, e.style.position.value,
                    e.name + " must be absolutely positioned for its inset to be a position");
                AssertExplicitPx(e.style.left, e.name + " left");
                AssertExplicitPx(e.style.top, e.name + " top");
                x += e.style.left.value.value;
                y += e.style.top.value.value;
            }
            AssertExplicitPx(el.style.width, name + " width");
            AssertExplicitPx(el.style.height, name + " height");
            return new Rect(x, y, el.style.width.value.value, el.style.height.value.value);
        }

        // A style length must be a real number of px: not a keyword (Auto/Null/Initial — an unset
        // inline style reads back as 0px, which would silently pass a numeric check) and not a
        // percentage, which is not addable to px.
        private static void AssertExplicitPx(StyleLength length, string what) {
            Assert.AreEqual(StyleKeyword.Undefined, length.keyword,
                what + " must be an explicit length, not a style keyword");
            Assert.AreEqual(LengthUnit.Pixel, length.value.unit, what + " must be in px");
        }

        private static void AssertRect(Rect expected, Rect actual, string what) {
            Assert.AreEqual(expected.x, actual.x, 0.001f, what + " left");
            Assert.AreEqual(expected.y, actual.y, 0.001f, what + " top");
            Assert.AreEqual(expected.width, actual.width, 0.001f, what + " width");
            Assert.AreEqual(expected.height, actual.height, 0.001f, what + " height");
        }

        // Counts loads/releases and returns one real sprite for everything — enough to observe the
        // session cache (task-50): re-renders must not re-request from the provider.
        private sealed class CountingResources : BakAgain.ResourceManagement.IResourceProviderService {
            public readonly List<object> LoadedKeys = new();
            public int Releases;
            private readonly Sprite _sprite;
            public CountingResources(Sprite sprite) { _sprite = sprite; }
            public Cysharp.Threading.Tasks.UniTask<T> LoadAssetAsync<T>(object key, object owner)
                where T : class {
                LoadedKeys.Add(key);
                return Cysharp.Threading.Tasks.UniTask.FromResult(_sprite as T);
            }
            public void ReleaseAssets(object owner) { Releases++; }
        }

        private CountingResources BuildCountingResources() {
            var tex = new Texture2D(4, 4);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), Vector2.zero);
            _scratchAssets.Add(tex);
            _scratchAssets.Add(sprite);
            return new CountingResources(sprite);
        }

        // ======================================================================================
        // Mechanism — synthetic geometry only. A hardcoded constant cannot fake these.
        // ======================================================================================

        [Test]
        public void MemberMode_ReadsCellSizeOriginAndShiftFromTheLayout() {
            ShippedGameData.RequireOrIgnore(); // the game font needs the player's files
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();

            renderer.Render(root, BuildContainer(), BuildObjects(), resources: null, onSlotPicked: null,
                InventoryLayoutMode.Member, SyntheticLayout(), SyntheticFrame());

            // Member reserves the first two columns for the paperdoll, so the first free cell is
            // column 2, row 0; the general grid starts at Left + MemberShiftX = 23 + 17 = 40.
            AssertRect(new Rect(40f + 2f * SynthCellW, SynthTop, SynthCellW, SynthCellH),
                Resolved(root, "item_slot_0"), "first general cell");
            // Column-major: the second item goes below the first, one CellHeight down.
            AssertRect(new Rect(40f + 2f * SynthCellW, SynthTop + SynthCellH, SynthCellW, SynthCellH),
                Resolved(root, "item_slot_1"), "second general cell");
        }

        /// <summary>The paperdoll is placed in the same grid as everything else, so its cells are
        /// cellWidth/cellHeight multiples at the grid's own origin. Uses a FOUR-row synthetic grid
        /// because Armor occupies rows 2-3: asserting a two-row armor cell against a three-row grid
        /// would be pinning an out-of-grid overflow as correct (it is clamped now — see
        /// <see cref="MemberMode_PaperdollIsClampedToASmallerGrid"/>).</summary>
        [Test]
        public void MemberMode_PaperdollCellsComeFromTheSameGrid() {
            var objects = new ObjectInfoSet("objects", new List<ObjectInfo> {
                new ObjectInfo("bow") { Number = 2, Icon = 0, MaxAmount = 1, InventorySlots = 2, ObjectType = GameData.ObjectType.Crossbow },
                new ObjectInfo("mail") { Number = 3, Icon = 0, MaxAmount = 1, InventorySlots = 4, ObjectType = GameData.ObjectType.Armor },
            });
            var container = new RuntimeContainer();
            ushort equipped = (ushort)GameData.ItemFlags.Equipped;
            container.Items.Add(new RuntimeItem(2, 1, equipped));
            container.Items.Add(new RuntimeItem(3, 1, equipped));
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();

            renderer.Render(root, container, objects, resources: null, onSlotPicked: null,
                InventoryLayoutMode.Member, SyntheticLayout(rows: 4), SyntheticFrame());

            // Crossbow = column 0, row 1, 2 columns x 1 row, at the grid's own (unshifted) origin.
            AssertRect(new Rect(SynthLeft, SynthTop + SynthCellH, 2f * SynthCellW, SynthCellH),
                Resolved(root, "item_slot_0"), "crossbow paperdoll cell");
            // Armor = column 0, row 2, 2x2 — rows 2-3 of a four-row grid, so it stays inside it.
            AssertRect(new Rect(SynthLeft, SynthTop + 2f * SynthCellH, 2f * SynthCellW, 2f * SynthCellH),
                Resolved(root, "item_slot_1"), "armor paperdoll cell");
            // The black underlay tracks the cell it hides the placeholder for.
            AssertRect(Resolved(root, "item_slot_1"), Resolved(root, "item_slot_1_equipbg"),
                "armor underlay");
            // Nothing hangs off the grid the placements were computed against.
            AssertInsideGrid(root, SyntheticLayout(rows: 4));
        }

        /// <summary>Paperdoll placements are bounded by the grid they are placed in, exactly like
        /// the packer's own reservation and free-cell search. A grid too short for Armor's rows
        /// 2-3 clamps its footprint; a grid with no row 2 at all gives it no cell (the same outcome
        /// as an uncategorized equipped item), instead of rendering outside the grid host.</summary>
        [Test]
        public void MemberMode_PaperdollIsClampedToASmallerGrid() {
            var objects = new ObjectInfoSet("objects", new List<ObjectInfo> {
                new ObjectInfo("mail") { Number = 3, Icon = 0, MaxAmount = 1, InventorySlots = 4, ObjectType = GameData.ObjectType.Armor },
            });
            var container = new RuntimeContainer();
            container.Items.Add(new RuntimeItem(3, 1, (ushort)GameData.ItemFlags.Equipped));

            // Three rows: Armor's row 2 exists but its second row does not — the span is clipped.
            var shortGrid = new VisualElement();
            new ItemGridRenderer().Render(shortGrid, container, objects, resources: null,
                onSlotPicked: null, InventoryLayoutMode.Member, SyntheticLayout(rows: 3),
                SyntheticFrame());
            AssertRect(new Rect(SynthLeft, SynthTop + 2f * SynthCellH, 2f * SynthCellW, SynthCellH),
                Resolved(shortGrid, "item_slot_0"), "armor clipped to the grid's last row");
            AssertInsideGrid(shortGrid, SyntheticLayout(rows: 3));

            // One column: the 2-column paperdoll footprint is clipped to the single column there is.
            var narrowGrid = new VisualElement();
            new ItemGridRenderer().Render(narrowGrid, container, objects, resources: null,
                onSlotPicked: null, InventoryLayoutMode.Member, SyntheticLayout(columns: 1, rows: 4),
                SyntheticFrame());
            AssertRect(new Rect(SynthLeft, SynthTop + 2f * SynthCellH, SynthCellW, 2f * SynthCellH),
                Resolved(narrowGrid, "item_slot_0"), "armor clipped to the grid's single column");
            AssertInsideGrid(narrowGrid, SyntheticLayout(columns: 1, rows: 4));

            // Two rows: there is no row 2 at all, so Armor gets no cell rather than one outside.
            var tinyGrid = new VisualElement();
            new ItemGridRenderer().Render(tinyGrid, container, objects, resources: null,
                onSlotPicked: null, InventoryLayoutMode.Member, SyntheticLayout(rows: 2),
                SyntheticFrame());
            Assert.IsNull(tinyGrid.Q<VisualElement>("item_slot_0"),
                "a grid with no row 2 has no armor slot — no cell, not an out-of-grid one");
        }

        // Every element the renderer built sits within its host's own cell extent: no cell may
        // start before the grid's origin or reach past its last column/row.
        private static void AssertInsideGrid(VisualElement root, InventoryLayout layout) {
            LayoutGrid grid = layout.GridArea.Grid;
            float width = grid.Columns * grid.CellWidth.Value;
            float height = grid.Rows * grid.CellHeight.Value;
            foreach (string hostName in new[] { "item_grid", "item_paperdoll" }) {
                VisualElement host = root.Q<VisualElement>(hostName);
                if (host == null) {
                    continue;
                }
                foreach (VisualElement child in host.Children()) {
                    Assert.GreaterOrEqual(child.style.left.value.value, -0.001f,
                        child.name + " starts left of " + hostName);
                    Assert.GreaterOrEqual(child.style.top.value.value, -0.001f,
                        child.name + " starts above " + hostName);
                    Assert.LessOrEqual(child.style.left.value.value + child.style.width.value.value,
                        width + 0.001f, child.name + " reaches past " + hostName + "'s last column");
                    Assert.LessOrEqual(child.style.top.value.value + child.style.height.value.value,
                        height + 0.001f, child.name + " reaches past " + hostName + "'s last row");
                }
            }
        }

        /// <summary>The two loot axes read different data, and this pins both: the horizontal
        /// centre comes from the design frame's width, the vertical from the grid area's top plus
        /// the loot box's HEIGHT. The box's width is not part of either — the fixture does not set
        /// one.</summary>
        [Test]
        public void LootMode_CentersOnTheFrameWidthAndTheBoxHeight() {
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();

            // One 1x1 item: cluster is one cell.
            renderer.Render(root, ContainerOf(1), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, SyntheticLayout(), SyntheticFrame());

            float expectedLeft = (SynthFrameW - SynthCellW) / 2f;
            float expectedTop = (SynthTop + SynthLootH) / 2f - SynthCellH / 2f;
            AssertRect(new Rect(expectedLeft, expectedTop, SynthCellW, SynthCellH),
                Resolved(root, "item_slot_0"), "centred single-cell cluster");
        }

        // Degradation: GridArea (and LootBox below) are settable reference properties an override
        // can null out. Render's guard is the sole line standing between that and an NRE — these
        // pin the graceful-degradation behaviour so a later "simplify GridArea ?? new LayoutHint()
        // back to a direct dereference" regresses loudly instead of only on modder-authored data.
        // ======================================================================================
        // Unresolvable units — refused loudly, never resolved into the wrong place.
        // ======================================================================================

        // A grid whose cells are a percentage of the host. LayoutApplier places such cells
        // perfectly (that is the reflow feature); it is only the grid's ORIGIN that cannot be
        // derived from them, because centring measures against the design frame's px width.
        private static InventoryLayout PercentCellLayout() {
            InventoryLayout layout = SyntheticLayout();
            layout.GridArea.Grid.CellWidth = LayoutLength.Percent(10f);
            layout.GridArea.Grid.CellHeight = LayoutLength.Percent(15f);
            return layout;
        }

        private static StyleLength HostLeft(VisualElement root, string host) =>
            root.Q<VisualElement>(host).style.left;

        /// <summary>
        /// Percent cells + loot centring: the cluster width is 10 <i>percent-of-parent</i> and the
        /// frame width is 1600 <i>px</i>, so (1600-20)/2 = 790 would be stamped as 790px onto a
        /// host that is 70% wide — the whole grid hanging off the right, silently. It refuses
        /// instead, and the grid stays at its declared origin, still reflowing internally.
        /// </summary>
        [Test]
        public void LootMode_PercentCells_RefuseTheCentring_AndKeepTheDeclaredOrigin() {
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();

            LogAssert.Expect(LogType.Error, new Regex("loot grid's centring inputs"));
            renderer.Render(root, ContainerOf(1), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, PercentCellLayout(), SyntheticFrame());

            StyleLength left = HostLeft(root, "item_grid");
            Assert.AreEqual(SynthLeft, left.value.value, 0.001f, "the grid area's declared Left");
            Assert.AreEqual(LengthUnit.Pixel, left.value.unit, "declared in px, so stamped in px");
            Assert.AreNotEqual(790f, left.value.value,
                "the percent NUMBER must never have been centred against the px frame width");
        }

        /// <summary>Member mode: a percent shift added to a px inset is not a length at all, so the
        /// shift is refused and the general grid stays at the grid area's own origin.</summary>
        [Test]
        public void MemberMode_PercentShiftOnAPxInset_RefusesAndLeavesTheGridUnshifted() {
            ShippedGameData.RequireOrIgnore(); // the game font needs the player's files
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();
            InventoryLayout layout = SyntheticLayout();
            layout.MemberShiftX = LayoutLength.Percent(12f);

            LogAssert.Expect(LogType.Error, new Regex("MemberShiftX"));
            renderer.Render(root, BuildContainer(), BuildObjects(), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Member, layout, SyntheticFrame());

            Assert.AreEqual(SynthLeft, HostLeft(root, "item_grid").value.value, 0.001f,
                "unshifted, not shifted by the percentage's bare number");
        }

        /// <summary>But two percentages of the same parent DO add up — "23% + 17%" is 40% — so an
        /// override that expresses the whole grid relatively still gets its member shift.</summary>
        [Test]
        public void MemberMode_PercentShiftOnAPercentInset_IsSummedAsAPercentage() {
            ShippedGameData.RequireOrIgnore(); // the game font needs the player's files
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();
            InventoryLayout layout = SyntheticLayout();
            layout.GridArea.Left = LayoutLength.Percent(SynthLeft);
            layout.MemberShiftX = LayoutLength.Percent(SynthShiftX);

            renderer.Render(root, BuildContainer(), BuildObjects(), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Member, layout, SyntheticFrame());

            StyleLength left = HostLeft(root, "item_grid");
            Assert.AreEqual(SynthLeft + SynthShiftX, left.value.value, 0.001f);
            Assert.AreEqual(LengthUnit.Percent, left.value.unit, "and it stays a percentage");
        }

        /// <summary>LayoutGrid's cell sizes default to Auto, so <c>{"Columns": 4, "Rows": 4}</c> in
        /// an override reaches the renderer with no cell size at all. "Intrinsic" is not a size a
        /// fixed-cell grid can be built from, so it refuses instead of emitting a grid of collapsed,
        /// unreachable cells.</summary>
        [Test]
        public void AutoCells_AreRefused_AndNoGridIsBuilt() {
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();
            var layout = new InventoryLayout {
                GridArea = new LayoutHint {
                    Left = LayoutLength.Px(SynthLeft), Top = LayoutLength.Px(SynthTop),
                    Grid = new LayoutGrid { Columns = 4, Rows = 4 },
                },
            };

            LogAssert.Expect(LogType.Error, new Regex("CellWidth/CellHeight"));
            renderer.Render(root, BuildContainer(), BuildObjects(), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, layout, SyntheticFrame());

            Assert.AreEqual(0, root.childCount, "an Auto-celled grid builds nothing");
        }

        /// <summary>The drag ghost is sized in design-frame px. Against percent cells there is no
        /// px answer, and returning the percentage's number would make the ghost a 10x15 speck — so
        /// it refuses and the caller sizes nothing.</summary>
        [Test]
        public void CellSizeCanonical_PercentCells_RefusesInsteadOfReturningTheBareNumber() {
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();

            LogAssert.Expect(LogType.Error, new Regex("loot grid's centring inputs"));
            renderer.Render(root, ContainerOf(1), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, PercentCellLayout(), SyntheticFrame());

            LogAssert.Expect(LogType.Error, new Regex("CellWidth/CellHeight"));
            Assert.AreEqual(Vector2.zero, renderer.CellSizeCanonical(1));
        }

        [Test]
        public void Render_NullGridArea_BuildsNothingAndDoesNotThrow() {
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();
            var layout = new InventoryLayout { GridArea = null };

            Assert.DoesNotThrow(() => renderer.Render(root, BuildContainer(), BuildObjects(),
                resources: null, onSlotPicked: null, InventoryLayoutMode.Loot, layout,
                SyntheticFrame()), "a null GridArea must degrade, not throw");

            Assert.AreEqual(0, root.childCount, "no grid area means nothing to place cells on");
        }

        /// <summary>The LootBox-null path is new behaviour, not just a guard: it centres the
        /// vertical axis on the design frame's HEIGHT, mirroring the horizontal rule's use of the
        /// frame's width. Fixture: frame height 707 (see <see cref="SyntheticFrame"/>), one 1x1
        /// item (cluster = <see cref="SynthCellW"/> x <see cref="SynthCellH"/>). By hand:
        /// left = (911 - 137) / 2 = 387; top = 707 / 2 - 91 / 2 = 353.5 - 45.5 = 308.</summary>
        [Test]
        public void LootMode_NullLootBox_CentersOnTheFrameHeight() {
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();
            InventoryLayout layout = SyntheticLayout();
            layout.LootBox = null;

            renderer.Render(root, ContainerOf(1), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, layout, SyntheticFrame());

            AssertRect(new Rect(387f, 308f, SynthCellW, SynthCellH),
                Resolved(root, "item_slot_0"), "cluster centred on the frame's height");
        }

        [Test]
        public void Placements_AreGridRelative_TheOffsetLivesOnTheHost() {
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();

            renderer.Render(root, ContainerOf(4), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Member, SyntheticLayout(), SyntheticFrame());

            // The layering fix: a cell's OWN geometry is column x cellWidth — pure grid, no screen
            // coordinates. Everything mode-specific (the member shift here) is on the host.
            VisualElement cell = root.Q<VisualElement>("item_slot_0");
            Assert.AreEqual(2f * SynthCellW, cell.style.left.value.value, 0.001f,
                "cell left is its column offset inside the grid, not a screen coordinate");
            Assert.AreEqual(0f, cell.style.top.value.value, 0.001f, "row 0 is at the grid's own top");

            VisualElement host = root.Q<VisualElement>("item_grid");
            Assert.IsNotNull(host, "the general grid host");
            Assert.AreEqual(SynthLeft + SynthShiftX, host.style.left.value.value, 0.001f,
                "the mode offset belongs to the host");
            Assert.AreSame(host, cell.parent, "cells are built into the grid host");
        }

        /// <summary>
        /// THE REFLOW PROOF (task-3 step 6). Hand the renderer a 4-column grid and the packing must
        /// come out four wide — spans intact, the overflow item dropped — while the identical items
        /// against a 7-column grid spill into a fifth column instead. Nothing but the data changes
        /// between the two halves, so a renderer holding its own column count cannot pass both.
        /// </summary>
        [Test]
        public void Reflow_FourColumnGrid_PacksFourWideWithSpansIntact() {
            var objects = new ObjectInfoSet("objects", new List<ObjectInfo> {
                new ObjectInfo("block") { Number = 10, Icon = 0, MaxAmount = 1, InventorySlots = 4 },
                new ObjectInfo("wide") { Number = 11, Icon = 0, MaxAmount = 1, InventorySlots = 2 },
                new ObjectInfo("unit") { Number = 12, Icon = 0, MaxAmount = 1, InventorySlots = 1 },
            });
            var container = new RuntimeContainer();
            container.Items.Add(new RuntimeItem(10, 1, 0)); // 2x2
            container.Items.Add(new RuntimeItem(11, 1, 0)); // 2x1
            container.Items.Add(new RuntimeItem(12, 1, 0)); // 1x1
            container.Items.Add(new RuntimeItem(12, 1, 0)); // 1x1
            container.Items.Add(new RuntimeItem(12, 1, 0)); // 1x1 — only fits in the wider grid

            // --- 4 columns x 2 rows: capacity is 8 cells, exactly filled by the first four items.
            var narrow = new VisualElement();
            new ItemGridRenderer().Render(narrow, container, objects, resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, SyntheticLayout(columns: 4, rows: 2),
                SyntheticFrame());

            float narrowLeft = (SynthFrameW - 4f * SynthCellW) / 2f;   // cluster is the full 4 columns
            float narrowTop = (SynthTop + SynthLootH) / 2f - 2f * SynthCellH / 2f;
            AssertRect(new Rect(narrowLeft, narrowTop, 2f * SynthCellW, 2f * SynthCellH),
                Resolved(narrow, "item_slot_0"), "2x2 block keeps its span in a 4-wide grid");
            AssertRect(new Rect(narrowLeft + 2f * SynthCellW, narrowTop, 2f * SynthCellW, SynthCellH),
                Resolved(narrow, "item_slot_1"), "2x1 item keeps its span, packed beside the block");
            AssertRect(new Rect(narrowLeft + 2f * SynthCellW, narrowTop + SynthCellH, SynthCellW, SynthCellH),
                Resolved(narrow, "item_slot_2"), "unit item under the wide item");
            AssertRect(new Rect(narrowLeft + 3f * SynthCellW, narrowTop + SynthCellH, SynthCellW, SynthCellH),
                Resolved(narrow, "item_slot_3"), "last free cell of the 4-wide grid");
            Assert.IsNull(narrow.Q<VisualElement>("item_slot_4"),
                "a 4x2 grid holds 8 cells — the fifth item has nowhere to go");

            // Every cell stays inside four columns: nothing reaches the fifth column's offset.
            VisualElement narrowHost = narrow.Q<VisualElement>("item_grid");
            Assert.IsNotNull(narrowHost, "the general grid host");
            foreach (VisualElement cell in narrowHost.Children()) {
                Rect r = Resolved(narrow, cell.name);
                Assert.LessOrEqual(r.xMax, narrowLeft + 4f * SynthCellW + 0.001f,
                    cell.name + " must not spill past the grid's fourth column");
            }

            // --- same items, same everything, 7 columns: the fifth item now fits, in column 4.
            var wide = new VisualElement();
            new ItemGridRenderer().Render(wide, container, objects, resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, SyntheticLayout(columns: 7, rows: 2),
                SyntheticFrame());

            float wideLeft = (SynthFrameW - 5f * SynthCellW) / 2f;     // cluster is now 5 columns
            AssertRect(new Rect(wideLeft + 4f * SynthCellW, narrowTop, SynthCellW, SynthCellH),
                Resolved(wide, "item_slot_4"), "the overflow item lands in column 4 of a 7-wide grid");
            Assert.AreNotEqual(Resolved(narrow, "item_slot_0").x, Resolved(wide, "item_slot_0").x,
                "the whole cluster re-centres when the column count changes");
        }

        [Test]
        public void CellSizeCanonical_FollowsTheRenderedGrid() {
            var renderer = new ItemGridRenderer();
            renderer.Render(new VisualElement(), ContainerOf(1), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, SyntheticLayout(), SyntheticFrame());

            Assert.AreEqual(new Vector2(SynthCellW, SynthCellH), renderer.CellSizeCanonical(1));
            Assert.AreEqual(new Vector2(2f * SynthCellW, SynthCellH), renderer.CellSizeCanonical(2));
            Assert.AreEqual(new Vector2(2f * SynthCellW, 2f * SynthCellH), renderer.CellSizeCanonical(4));
        }

        // ======================================================================================
        // Faithfulness — the shipped screen must land on exactly the pre-change coordinates.
        // ======================================================================================

        /// <summary>
        /// The numeric pin for the shipped screen. Every expected rect here is the value the
        /// pre-change (hardcoded) implementation produced for that scenario, so a regression shows
        /// up as a moved cell rather than as a merely different-looking structure.
        /// </summary>
        [Test]
        public void Faithful_MemberMode_MatchesThePreChangeCoordinates() {
            ShippedGameData.RequireOrIgnore(); // the game font needs the player's files
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();

            renderer.Render(root, BuildContainer(), BuildObjects(), resources: null, onSlotPicked: null,
                InventoryLayoutMode.Member, FaithfulLayout(), FaithfulFrame());

            AssertRect(new Rect(530f, 72f, 200f, 180f), Resolved(root, "item_slot_0"), "member cell 0");
            AssertRect(new Rect(530f, 252f, 200f, 180f), Resolved(root, "item_slot_1"), "member cell 1");
        }

        [Test]
        public void Faithful_MemberMode_TwoSlotItemSpansTwoCellsWide() {
            var root = new VisualElement();
            new ItemGridRenderer().Render(root, ContainerOf(1), ObjectsWithSlots(2), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Member, FaithfulLayout(), FaithfulFrame());

            AssertRect(new Rect(530f, 72f, 400f, 180f), Resolved(root, "item_slot_0"), "2-slot item");
        }

        [Test]
        public void Faithful_MemberMode_EquippedItemsLandOnThePaperdollRects() {
            var objects = new ObjectInfoSet("objects", new List<ObjectInfo> {
                new ObjectInfo("sword") { Number = 1, Icon = 0, MaxAmount = 1, InventorySlots = 2, ObjectType = GameData.ObjectType.Sword },
                new ObjectInfo("bow") { Number = 2, Icon = 0, MaxAmount = 1, InventorySlots = 2, ObjectType = GameData.ObjectType.Crossbow },
                new ObjectInfo("mail") { Number = 3, Icon = 0, MaxAmount = 1, InventorySlots = 4, ObjectType = GameData.ObjectType.Armor },
                new ObjectInfo("loose") { Number = 4, Icon = 0, MaxAmount = 1, InventorySlots = 1 },
            });
            var container = new RuntimeContainer();
            ushort equipped = (ushort)GameData.ItemFlags.Equipped;
            container.Items.Add(new RuntimeItem(1, 1, equipped));
            container.Items.Add(new RuntimeItem(2, 1, equipped));
            container.Items.Add(new RuntimeItem(3, 1, equipped));
            container.Items.Add(new RuntimeItem(4, 1, 0));
            var root = new VisualElement();

            new ItemGridRenderer().Render(root, container, objects, resources: null, onSlotPicked: null,
                InventoryLayoutMode.Member, FaithfulLayout(), FaithfulFrame());

            // Paperdoll rects (sub_ovr157_0 @0x5451e): Sword (70,72,400,180),
            // Crossbow (70,252,400,180), Armor (70,432,400,360).
            AssertRect(new Rect(70f, 72f, 400f, 180f), Resolved(root, "item_slot_0"), "sword");
            AssertRect(new Rect(70f, 252f, 400f, 180f), Resolved(root, "item_slot_1"), "crossbow");
            AssertRect(new Rect(70f, 432f, 400f, 360f), Resolved(root, "item_slot_2"), "armor");
            // Each equipped cell has the black underlay sibling that hides the placeholder sprite.
            Assert.IsNotNull(root.Q<VisualElement>("item_slot_0_equipbg"));
            Assert.IsNotNull(root.Q<VisualElement>("item_slot_2_equipbg"));
            // The general item stays out of the paperdoll columns.
            AssertRect(new Rect(530f, 72f, 200f, 180f), Resolved(root, "item_slot_3"), "general item");
            Assert.IsNull(root.Q<VisualElement>("item_slot_3_equipbg"), "no underlay for unequipped items");
        }

        [Test]
        public void Faithful_MemberMode_EquippedStaffSpansTwoRows() {
            var objects = new ObjectInfoSet("objects", new List<ObjectInfo> {
                new ObjectInfo("staff") { Number = 9, Icon = 0, MaxAmount = 1, InventorySlots = 4, ObjectType = GameData.ObjectType.Staff },
            });
            var container = new RuntimeContainer();
            container.Items.Add(new RuntimeItem(9, 1, (ushort)GameData.ItemFlags.Equipped));
            var root = new VisualElement();

            new ItemGridRenderer().Render(root, container, objects, resources: null, onSlotPicked: null,
                InventoryLayoutMode.Member, FaithfulLayout(), FaithfulFrame());

            // Staff shares the weapon slot at row 0 but spans two rows: canonical (70,72,400,360).
            AssertRect(new Rect(70f, 72f, 400f, 360f), Resolved(root, "item_slot_0"), "staff");
        }

        /// <summary>
        /// Loot-mode centering, pinned against the pre-change implementation for several cluster
        /// shapes. This is the case the data model could not express directly (the original halves
        /// with integer division, truncating a half-unit of the original display's pixel grid) — so
        /// it is the one most worth pinning numerically. Expected values were taken from the
        /// pre-change algorithm, which was also cross-checked against this one over every
        /// slot-count sequence of length &lt;= 8 drawn from {1,2,4} plus 3000 random longer
        /// sequences — a broad sample of the packings the 7x4 grid admits, not an exhaustive
        /// enumeration. The exactness argument is the algebra in <c>ResolveGridOrigin</c>'s doc.
        /// </summary>
        [Test]
        public void Faithful_LootMode_CenteringMatchesThePreChangeCoordinates() {
            // 1 single-slot item: cluster 1x1.
            var root1 = new VisualElement();
            new ItemGridRenderer().Render(root1, ContainerOf(1), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, FaithfulLayout(), FaithfulFrame());
            AssertRect(new Rect(700f, 342f, 200f, 180f), Resolved(root1, "item_slot_0"), "1 item");

            // 5 single-slot items: column-major fills column 0 (4 rows) then column 1 → 2x4.
            var root5 = new VisualElement();
            new ItemGridRenderer().Render(root5, ContainerOf(5), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, FaithfulLayout(), FaithfulFrame());
            AssertRect(new Rect(600f, 72f, 200f, 180f), Resolved(root5, "item_slot_0"), "5 items, first");
            AssertRect(new Rect(600f, 612f, 200f, 180f), Resolved(root5, "item_slot_3"), "5 items, fourth");
            AssertRect(new Rect(800f, 72f, 200f, 180f), Resolved(root5, "item_slot_4"), "5 items, fifth");

            // One 4-slot item: cluster 2x2.
            var rootBlock = new VisualElement();
            new ItemGridRenderer().Render(rootBlock, ContainerOf(1), ObjectsWithSlots(4), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, FaithfulLayout(), FaithfulFrame());
            AssertRect(new Rect(600f, 252f, 400f, 360f), Resolved(rootBlock, "item_slot_0"),
                "one 2x2 block");

            // A full 7x4 grid: cluster fills the grid, so the offsets bottom out.
            var rootFull = new VisualElement();
            new ItemGridRenderer().Render(rootFull, ContainerOf(28), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, FaithfulLayout(), FaithfulFrame());
            AssertRect(new Rect(100f, 72f, 200f, 180f), Resolved(rootFull, "item_slot_0"), "full grid, first");
            AssertRect(new Rect(1300f, 612f, 200f, 180f), Resolved(rootFull, "item_slot_27"),
                "full grid, last");
        }

        [Test]
        public void Faithful_GridCapacityIsTheDataGridsCellCount() {
            var root = new VisualElement();
            new ItemGridRenderer().Render(root, ContainerOf(30), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, FaithfulLayout(), FaithfulFrame());

            Assert.IsNotNull(root.Q<VisualElement>("item_slot_27"), "the 7x4 grid holds 28 items");
            Assert.IsNull(root.Q<VisualElement>("item_slot_28"), "and no more than 28");
        }

        // ======================================================================================
        // Reflow — Fit = Fill must centre on the STAGE's resolved width, not the declared frame
        // (task-5). Every fixture above (Faithful_* and the Mechanism group) builds its DesignFrame
        // with no Fit set at all, which defaults to Contain — so none of them, however many pass,
        // exercise the Fill/deferred path this section is the only cover for. A root built as a
        // bare `new VisualElement()` (every test above) never carries CanonicalStage's frame state,
        // so CanonicalStage.TryGetFrame always returns false for it — Fill can only be observed
        // through a real stage built via CanonicalStage.GetOrCreate, attached to a live panel so a
        // genuine UI Toolkit layout pass (and GeometryChangedEvent) actually happens.
        // ======================================================================================

        // 3 single-slot items in a 3-column, 1-row grid: column-major first-fit places one per
        // column (row 0 fills first, but with only 1 row each item lands in the next column), so
        // the cluster is exactly 3 cells wide — 600 canonical px at CellWidth 200.
        private const float ReflowCellW = 200f;
        private const float ReflowCellH = 100f;
        private const float ReflowClusterWidth = 3f * ReflowCellW; // 600

        // LootBox.Height chosen so the vertical centre lands on a round, easy-to-verify number
        // ((100 + 200) / 2 - 100/2 == 100) — this section is about the HORIZONTAL divergence
        // Fill introduces (Gap 3); the vertical axis is untouched by this change (it never reads
        // frame.Width) and is asserted merely to prove that's still true, not as the thing under
        // test. Leaving LootBox at InventoryLayout's own default (Height 792) would work too but
        // would make the expected top an incidental value with no obvious derivation.
        private static InventoryLayout ReflowLayout() =>
            new InventoryLayout {
                GridArea = new LayoutHint {
                    Left = LayoutLength.Px(100f),
                    Top = LayoutLength.Px(100f),
                    Grid = new LayoutGrid {
                        CellWidth = LayoutLength.Px(ReflowCellW),
                        CellHeight = LayoutLength.Px(ReflowCellH),
                        Columns = 3,
                        Rows = 1,
                    },
                },
                LootBox = new LayoutHint { Height = LayoutLength.Px(200f) },
            };

        // Builds a live UIDocument + a real CanonicalStage (so TryGetFrame sees genuine Fill state),
        // forces its resolved width to `stageWidth` (standing in for the real ~2133 logical px a
        // 16:9 window resolves to under match-height scaling — a fixed value keeps the test
        // independent of whatever the test runner's own Game View happens to be), and returns both
        // the document (for ForceLayout) and the stage (the `root` Render() and Resolved() use).
        private (UIDocument document, VisualElement stage) BuildFillStage(float stageWidth, DesignFrame frame) {
            var go = new GameObject("ItemGridRendererFillStage");
            _spawned.Add(go);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            _scratchAssets.Add(settings);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = settings;

            VisualElement stage = CanonicalStage.GetOrCreate(document.rootVisualElement, frame);
            // CanonicalStage.ApplyFrame sets Length.Percent(100) under Fill; overriding with an
            // explicit px here is what stands in for "the panel resolved to a real 16:9 window" —
            // deterministically, rather than depending on this test runner's actual panel size.
            stage.style.width = stageWidth;
            return (document, stage);
        }

        // Forces a synchronous UI Toolkit layout pass, the same reflection hook
        // AttachToRuntimePanel uses for the click test — otherwise worldBound/resolvedStyle/
        // GeometryChangedEvent all wait for the next player-loop tick, which a plain [Test] never
        // sees.
        private static void ForceLayout(UIDocument document) {
            MethodInfo validateLayout = document.rootVisualElement.panel.GetType()
                .GetMethod("ValidateLayout", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            validateLayout?.Invoke(document.rootVisualElement.panel, null);
        }

        /// <summary>
        /// The reflow case Gap 3 exists for: at 16:9 the stage resolves to ~2133 logical px wide
        /// while the declared frame still reads 1600, so the 3-column/600px cluster must centre at
        /// (2133 − 600) / 2 = 766.5, not (1600 − 600) / 2 = 500. This only goes red if the deferred
        /// Fill correction actually ran — see Step 5's falsification, which hardcodes frameWidth
        /// back to Canonical.Width and confirms this specific assertion (and only this one) fails.
        /// </summary>
        [Test]
        public void Fill_LootMode_RecentersOnTheStagesResolvedWidth_NotOnTheDeclaredFrame() {
            var frame = new DesignFrame { Width = 1600, Height = 1200, Fit = LayoutFit.Fill };
            (UIDocument document, VisualElement stage) = BuildFillStage(2133f, frame);

            new ItemGridRenderer().Render(stage, ContainerOf(3), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, ReflowLayout(), frame);
            // Render() runs before any layout pass, so it can only place the host at its synchronous
            // (declared-frame) placeholder — this is what proves the correction is genuinely
            // deferred rather than already-correct by coincidence.
            AssertRect(new Rect(500f, 100f, ReflowCellW, ReflowCellH), Resolved(stage, "item_slot_0"),
                "synchronous placeholder, before layout: centred on the declared 1600, same as Contain");

            ForceLayout(document); // fires the stage's first-ever GeometryChangedEvent

            AssertRect(new Rect(766.5f, 100f, ReflowCellW, ReflowCellH), Resolved(stage, "item_slot_0"),
                "corrected: centred on the stage's real resolved width (2133), not the declared frame");
        }

        /// <summary>
        /// THE FENCE for the re-render case, which the test above structurally cannot cover:
        /// <see cref="BuildFillStage"/> mints a brand-new <c>UIDocument</c> per test, so its stage's
        /// first-ever layout is guaranteed to raise <c>GeometryChangedEvent</c> — and a
        /// registration-only implementation passes on that event alone.
        ///
        /// <para>The real screen never gets that guarantee twice. The stage is the long-lived
        /// <c>CanonicalStage</c> element, and <c>InventoryMenu.RenderAll</c> runs again on
        /// <c>Built</c>/<c>OnAfterShow</c> and once per equip/unequip/transfer. An already-laid-out
        /// stage does NOT raise <c>GeometryChangedEvent</c> again because absolutely-positioned
        /// children were added to it — under Fill it is 100%x100%, so its own rect never changes.
        /// So every render after the first would unregister the already-fired callback and register
        /// one that never fires, snapping the cluster back to the 500 placeholder: the whole
        /// ~266 logical px error, reappearing on the first equip.</para>
        ///
        /// <para>The second <c>Render</c> below is deliberately NOT followed by a
        /// <see cref="ForceLayout"/> — adding one would hide the bug by manufacturing the very
        /// event the real screen does not send.</para>
        /// </summary>
        [Test]
        public void Fill_LootMode_ASecondRenderOnAnAlreadyLaidOutStage_IsStillRecentred() {
            var frame = new DesignFrame { Width = 1600, Height = 1200, Fit = LayoutFit.Fill };
            (UIDocument document, VisualElement stage) = BuildFillStage(2133f, frame);
            var renderer = new ItemGridRenderer();

            renderer.Render(stage, ContainerOf(3), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, ReflowLayout(), frame);
            ForceLayout(document); // the stage's first-ever layout: the one event the callback catches
            AssertRect(new Rect(766.5f, 100f, ReflowCellW, ReflowCellH), Resolved(stage, "item_slot_0"),
                "first render, corrected by the stage's first-ever GeometryChangedEvent");

            // What an equip/unequip/transfer does: re-render into the SAME, already-laid-out stage.
            renderer.Render(stage, ContainerOf(3), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, ReflowLayout(), frame);

            AssertRect(new Rect(766.5f, 100f, ReflowCellW, ReflowCellH), Resolved(stage, "item_slot_0"),
                "re-render on a laid-out stage: 500 here means the correction is waiting on a "
                + "GeometryChangedEvent that will never arrive, and the cluster has snapped back "
                + "to the declared-frame placeholder");
        }

        /// <summary>
        /// Contain must stay byte-identical even when read through the same TryGetFrame path a Fill
        /// screen now uses — CanonicalStage.ApplyFrame makes frame.Width and the stage's resolved
        /// width equal by construction under Contain, so there is nothing to defer and no
        /// GeometryChangedEvent is needed for the origin to already be final.
        /// </summary>
        [Test]
        public void Contain_LootMode_ThroughARealStage_MatchesTheDeclaredFrame_NoLayoutNeeded() {
            var frame = new DesignFrame { Width = 1600, Height = 1200 }; // Fit defaults to Contain
            (UIDocument _, VisualElement stage) = BuildFillStage(1600f, frame);

            new ItemGridRenderer().Render(stage, ContainerOf(3), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, ReflowLayout(), frame);

            // No ForceLayout call: under Contain the origin must already be final without any
            // layout pass, exactly like every Faithful_* test above (which also never lays out).
            AssertRect(new Rect(500f, 100f, ReflowCellW, ReflowCellH), Resolved(stage, "item_slot_0"),
                "Contain centres on the declared/resolved 1600 either way — no deferral needed");
        }

        /// <summary>
        /// A root that was never built through CanonicalStage.GetOrCreate — every fixture in this
        /// file outside this Reflow section uses exactly this bare-root shape — makes TryGetFrame
        /// return false. That must degrade to the pre-Fill behaviour (centre on the `frame` argument
        /// verbatim) rather than throwing or silently mis-centring, even when the argument itself
        /// declares Fit = Fill: with no stage to defer against, Fill has no meaning to fall back on.
        /// </summary>
        [Test]
        public void NoStageFrame_DegradesToTheFrameArgument_EvenWhenItDeclaresFill() {
            var root = new VisualElement(); // never touched CanonicalStage — TryGetFrame(root) is false
            var frame = new DesignFrame { Width = 1600, Height = 1200, Fit = LayoutFit.Fill };

            new ItemGridRenderer().Render(root, ContainerOf(3), ObjectsWithSlots(1), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, ReflowLayout(), frame);

            AssertRect(new Rect(500f, 100f, ReflowCellW, ReflowCellH), Resolved(root, "item_slot_0"),
                "no CanonicalStage frame state to read: falls back to the frame argument's own " +
                "Width, same as every pre-Fill test in this file");
        }

        // ======================================================================================
        // Behaviour unrelated to geometry (unchanged by task-3, kept as regression cover)
        // ======================================================================================

        /// <summary>
        /// The session icon cache (task-50, JvE 2026-08-02): every re-render used to release +
        /// re-request each icon from Addressables, re-extracting the BMX from KRONDOR.001 per
        /// member switch / equip / transfer. Faithful shape is the original's own
        /// <c>invui_inspect_images_load_once</c> / <c>invui_inspect_image_cleanup</c> pair: load
        /// once per screen session, free at exit.
        /// </summary>
        [Test]
        public void ReRender_ServesIconsFromTheSessionCache_NotTheProvider() {
            ShippedGameData.RequireOrIgnore(); // the game font needs the player's files
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();
            CountingResources resources = BuildCountingResources();

            renderer.Render(root, BuildContainer(), BuildObjects(), resources, onSlotPicked: null,
                InventoryLayoutMode.Member, FaithfulLayout(), FaithfulFrame());
            int loadsAfterFirstRender = resources.LoadedKeys.Count;
            Assert.Greater(loadsAfterFirstRender, 0, "first render loads the icons");

            renderer.Render(root, BuildContainer(), BuildObjects(), resources, onSlotPicked: null,
                InventoryLayoutMode.Member, FaithfulLayout(), FaithfulFrame());
            Assert.AreEqual(loadsAfterFirstRender, resources.LoadedKeys.Count,
                "a re-render must resolve every icon from the session cache — re-requesting is the "
                + "per-render churn task-50 removes");
            Assert.AreEqual(0, resources.Releases,
                "Clear/re-render must not release the session's handles");

            renderer.ReleaseAll();
            Assert.AreEqual(1, resources.Releases,
                "ReleaseAll is the session end — the one place handles are released");

            renderer.Render(root, BuildContainer(), BuildObjects(), resources, onSlotPicked: null,
                InventoryLayoutMode.Member, FaithfulLayout(), FaithfulFrame());
            Assert.Greater(resources.LoadedKeys.Count, loadsAfterFirstRender,
                "after ReleaseAll the cache is empty, so a new session loads afresh");
        }

        [Test]
        public void ReRender_RemovesTheGridHosts_NotJustTheCells() {
            ShippedGameData.RequireOrIgnore(); // the game font needs the player's files
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();

            renderer.Render(root, BuildContainer(), BuildObjects(), resources: null, onSlotPicked: null,
                InventoryLayoutMode.Member, FaithfulLayout(), FaithfulFrame());
            int afterFirst = root.childCount;
            renderer.Render(root, BuildContainer(), BuildObjects(), resources: null, onSlotPicked: null,
                InventoryLayoutMode.Member, FaithfulLayout(), FaithfulFrame());

            Assert.AreEqual(afterFirst, root.childCount, "a re-render must not stack up grid hosts");
            renderer.Clear();
            Assert.AreEqual(0, root.childCount, "Clear removes the hosts too");
        }

        [Test]
        public void Render_NoItems_BuildsNothing() {
            var root = new VisualElement();

            new ItemGridRenderer().Render(root, new RuntimeContainer(), BuildObjects(), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, FaithfulLayout(), FaithfulFrame());

            Assert.AreEqual(0, root.childCount, "no items means not even an empty grid host");
        }

        [Test]
        public void Render_ClickingCellZero_InvokesOnSlotPickedWithIndexZero() {
            ShippedGameData.RequireOrIgnore(); // the game font needs the player's files
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();
            int picked = -1;

            renderer.Render(root, BuildContainer(), BuildObjects(), resources: null, onSlotPicked: i => picked = i,
                InventoryLayoutMode.Member, FaithfulLayout(), FaithfulFrame());

            AttachToRuntimePanel(root, FaithfulFrame());
            VisualElement cell0 = root.Q<VisualElement>("item_slot_0");
            Assert.IsNotNull(cell0);

            SimulateClick(cell0);

            Assert.AreEqual(0, picked);
        }

        [Test]
        public void QuantityLabel_ShownForStackable_HiddenForNonStackable() {
            ShippedGameData.RequireOrIgnore(); // the game font needs the player's files
            var root = new VisualElement();
            var renderer = new ItemGridRenderer();

            // Slot 0 = ObjectId 80 (stacking, MaxAmount 5, Variable 2); slot 1 = ObjectId 72
            // (non-stacking, MaxAmount 1) — see BuildContainer()/BuildObjects().
            renderer.Render(root, BuildContainer(), BuildObjects(), resources: null, onSlotPicked: null,
                InventoryLayoutMode.Member, FaithfulLayout(), FaithfulFrame());

            VisualElement cell0 = root.Q<VisualElement>("item_slot_0");
            VisualElement cell1 = root.Q<VisualElement>("item_slot_1");
            Assert.IsNotNull(cell0, "item_slot_0 not found");
            Assert.IsNotNull(cell1, "item_slot_1 not found");

            Label qty0 = cell0.Q<Label>("item_slot_0_qty");
            Label qty1 = cell1.Q<Label>("item_slot_1_qty");

            Assert.IsNotNull(qty0, "0x8000-flagged item should always show its stack count");
            Assert.AreEqual("2", qty0.text, "quantity label text should be item.Variable");
            Assert.IsNull(qty1, "unflagged item shows no number (INVENTOR.C:495 display gates)");
        }

        // Cells are wired via the UI Toolkit Clickable manipulator (matching the project's
        // FilePickerRenderer/UserInterfaceLoader convention), which only fires from a real
        // pointer down+up sequence dispatched through a live panel — so the click-routing test
        // needs an actual panel (a throwaway UIDocument+PanelSettings), not just a bare
        // VisualElement tree.
        private void AttachToRuntimePanel(VisualElement root, DesignFrame frame) {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            _scratchAssets.Add(settings);

            // *** THE PANEL MUST NOT TAKE ITS SIZE FROM Screen. *** A UIDocument's runtime panel is
            // screen-sized by default, and in the Editor `Screen` is the Game view — i.e. whichever
            // Game view SIZE PRESET happens to be selected, which is ambient developer state, not a
            // property of the machine or the project. Rendering to a target texture pins the panel
            // to the frame this fixture lays out against, so the click coordinates mean the same
            // thing whatever the Game view is set to.
            //
            // Found 2026-08-28: Screen read 321x531 — a leftover `*-restore` preset in the Editor's
            // own preferences — so cell 0's centre at ~(630,162) fell OUTSIDE a 321-wide panel and
            // Clickable never fired. Only one of 1281 tests failed, which is the tell: not a
            // renderer or platform fault, but the single test that read Screen.
            //
            // The size comes from the FRAME UNDER TEST, not from a constant. 1600x1200 is only the
            // space the EXTRACTOR emits resources in; the port ships at 1920x1080 (so an unmodded
            // faithful screen pillarboxes) and a mod may supply a responsive frame instead. A
            // fixture that hardcoded either number would be asserting against a resolution rather
            // than against the resource it was handed.
            var panelTexture = new RenderTexture(frame.Width, frame.Height, 0);
            _scratchAssets.Add(panelTexture);
            settings.targetTexture = panelTexture;

            var go = new GameObject("ItemGridRendererTestPanel");
            _spawned.Add(go);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.rootVisualElement.Add(root);

            // *** SIZE THE HOST TO THE FRAME, NOT TO THE SCREEN. *** Necessary but NOT sufficient,
            // and worth keeping both halves in mind: this fixes the LAYOUT (cell 0's worldBound
            // becomes a correct 530,72,200x180), while the target texture above is what fixes the
            // PANEL. Setting only this one still fails, because the cells are then laid out
            // correctly inside a panel that is still the wrong size.
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.width = frame.Width;
            root.style.height = frame.Height;

            // Force an immediate layout pass so worldBound (used to target the click) resolves;
            // otherwise it stays NaN until the next player-loop tick, which a plain [Test] never sees.
            MethodInfo validateLayout = document.rootVisualElement.panel.GetType()
                .GetMethod("ValidateLayout", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            validateLayout?.Invoke(document.rootVisualElement.panel, null);
        }

        private static void SimulateClick(VisualElement target) {
            Vector2 center = target.worldBound.center;
            using (PointerDownEvent down = PointerDownEvent.GetPooled(
                       new Event { type = EventType.MouseDown, mousePosition = center, button = 0 })) {
                down.target = target;
                target.SendEvent(down);
            }
            using (PointerUpEvent up = PointerUpEvent.GetPooled(
                       new Event { type = EventType.MouseUp, mousePosition = center, button = 0 })) {
                up.target = target;
                target.SendEvent(up);
            }
        }
    }
}
