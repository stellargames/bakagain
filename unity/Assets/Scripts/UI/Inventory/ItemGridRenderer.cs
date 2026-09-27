namespace BakAgain.UI.Inventory {
    using System;
    using System.Collections.Generic;
    using BakAgain.ResourceManagement;
    using BakAgain.UI;
    using BakAgain.UI.Layout;
    using Cysharp.Threading.Tasks;
    using GameData;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using GameData.Resources.Object;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Which of the original's two grid layouts to build (<c>sub_ovr157_0</c> @0x54210,
    /// source <c>cmbinv_combat_encounter_begin</c> CMBINV.C:44).</summary>
    public enum InventoryLayoutMode {
        /// <summary>Looting a container: pack the whole grid from column 0, then centre the packed
        /// cluster (the @0x54663 centering pass). The two axes read different data:
        /// <b>horizontally</b> the cluster is centred on the design frame's centre line
        /// (<see cref="DesignFrame.Width"/>); <b>vertically</b> it is centred inside
        /// <see cref="InventoryLayout.LootBox"/>'s <see cref="LayoutHint.Height"/> measured from
        /// the grid area's top. <see cref="LayoutHint.Width"/> of the loot box is deliberately
        /// <i>not</i> read by anything — see <c>ResolveGridOrigin</c> for the derivation and for
        /// why using it would move the shipped screen.</summary>
        Loot,
        /// <summary>A party member's own inventory: equipped items (<see cref="ItemFlags.Equipped"/>)
        /// go to fixed paperdoll cells by <see cref="ObjectInfo.ObjectType"/>, the grid's first two
        /// columns are reserved for that paperdoll area, and the general grid is shifted right by
        /// <see cref="InventoryLayout.MemberShiftX"/> into the right-hand panel box. No centering.</summary>
        Member,
        /// <summary>A shop's shelf: <see cref="InventoryLayout.ShopGridArea"/>'s own six wide cells
        /// at their declared origin — no member shift, and none of Loot's centring, because the shop
        /// arm of <c>sub_ovr157_0</c> @0x542e1 writes fixed rects rather than packing a cluster.
        /// One item per cell whatever its size, and the icon rides high to leave room for the price
        /// and name lines beneath it.</summary>
        Shop,
    }

    /// <summary>
    /// Renders a <see cref="RuntimeContainer"/>'s items as pickable icon cells — the loot/inventory
    /// screen's core visual. Faithful to the layout builder <c>sub_ovr157_0</c> @0x54210: a cell grid
    /// filled by a <b>column-major first-fit</b> packing where each item occupies a footprint sized
    /// from its <see cref="ObjectInfo.InventorySlots"/> (1 slot -> 1 cell; 2-3 -> 2 cells wide;
    /// 4+ -> a 2x2 block).
    ///
    /// <para><b>This class owns no coordinates.</b> Every length, the cell size, the column/row
    /// counts, the member shift and the loot centering box come from
    /// <see cref="InventoryLayout"/> — the engine-independent geometry hung off the screen's REQ
    /// resource. What stays here is the packing, which is a game rule (which cells an item may
    /// occupy), not layout. Cells are placed by declaring a
    /// <see cref="LayoutGridPlacement"/> and letting
    /// <see cref="LayoutApplier.ApplyGridPlacement"/> turn it into geometry, so the grid genuinely
    /// reflows when the data changes column count or cell size.</para>
    ///
    /// <para>Placements are grid-relative; the mode-specific offset lives on the grid <i>host</i>
    /// element that the cells are built into — one host for the general grid, plus a second one at
    /// the grid's own origin for the paperdoll in <see cref="InventoryLayoutMode.Member"/>. That is
    /// what keeps the loot centering (a sub-cell pixel offset) expressible without perturbing any
    /// child's column/row. See <see cref="ResolveGridOrigin"/> for the centering derivation.</para>
    ///
    /// <para>In <see cref="InventoryLayoutMode.Member"/> mode equipped items land on the paperdoll:
    /// Sword -> column 0 row 0 (2x1 cells), Staff -> column 0 row 0 (2x2), Crossbow -> column 0
    /// row 1 (2x1), Armor -> column 0 row 2 (2x2), each over a black cell fill (the original blacks
    /// the cell first, @0x569e4, which is what hides the empty-slot placeholder sprite underneath).
    /// An equipped item of any other category gets no cell at all — same as the original's span==0
    /// skip.</para>
    ///
    /// <para>Icon sprites are cached for the <b>screen session</b>: first use of a key loads via
    /// <c>resources.LoadAssetAsync&lt;Sprite&gt;(key, this)</c> (key from
    /// <see cref="ItemIconResolver.ResolveBmxSubResource"/>); every later render resolves from the
    /// cache synchronously — no re-extraction from KRONDOR.001 and no one-frame pop-in on
    /// re-renders. This is the original's own pattern: <c>invui_inspect_images_load_once</c> loads
    /// the INVSHP asset tables once per inventory session and <c>invui_inspect_image_cleanup</c>
    /// frees them at exit (INVENTOR.C:53-98) — the Unity counterparts are the cache-miss load and
    /// <see cref="ReleaseAll"/>, which the screen calls on teardown. <see cref="Clear"/> (every
    /// re-render) removes the built elements only. Cell creation + click wiring is synchronous so
    /// callers/tests can rely on the structure immediately after <see cref="Render"/> returns; only
    /// a first-time icon pops in later.</para>
    /// </summary>
    public sealed class ItemGridRenderer {
        /// <summary>Element name of the host holding the general item grid.</summary>
        internal const string GridHostName = "item_grid";

        /// <summary>Element name of the host holding the member paperdoll's equipped cells.</summary>
        internal const string PaperdollHostName = "item_paperdoll";

        // Cell COUNTS, not lengths: the paperdoll owns the grid's first two columns (@0x5459b) and
        // every equipped item is two cells wide (@0x5451e). Nothing here is a coordinate, so this
        // does not scale with the grid and is not screen geometry.
        private const int PaperdollColumns = 2;

        private const ushort EquippedFlag = (ushort)GameData.ItemFlags.Equipped;

        // Every element this renderer built — cells, their equipped-slot underlays, and the grid
        // hosts they live in. Clear() detaches them all.
        private readonly List<VisualElement> _built = new();
        // Session sprite cache (see the class doc): filled on first use of a key, emptied only by
        // ReleaseAll. Handles stay owner-tracked under `this`, so ReleaseAll's ReleaseAssets drops
        // every Addressables refcount this renderer took.
        private readonly Dictionary<string, Sprite> _spriteCache = new();
        private IResourceProviderService _resources;
        private int _generation;
        // Geometry of the last render, kept so the host screen can ask for a cell footprint (the
        // drag ghost) without repeating the lookup. The model's own defaults are the faithful
        // values, so this is meaningful even before the first Render.
        private InventoryLayout _layout = new();

        // The Fill-mode deferred re-centring (see ResolveGridOrigin/ScheduleFillReposition):
        // tracked so a later Render() — the general grid re-renders on every equip/unequip/transfer
        // — tears down its predecessor's registration before adding a new one. `root` is the
        // long-lived CanonicalStage element (unlike the cells/hosts in `_built`, it survives every
        // Clear()), so leaving old callbacks registered on it would leak one per re-render for the
        // lifetime of the screen.
        private VisualElement _deferredRepositionRoot;
        private EventCallback<GeometryChangedEvent> _deferredReposition;

        private struct Placement {
            public int ItemIndex;
            public LayoutGridPlacement Cell;
            public bool Equipped;
        }

        /// <summary>Build one pickable cell per item into <paramref name="root"/>, clicking through
        /// to <paramref name="onSlotPicked"/> with the item's index. <paramref name="mode"/> selects
        /// the loot layout (packed cluster, centered) or the member layout (paperdoll + shifted
        /// general grid). <paramref name="layout"/> is the screen resource's
        /// <see cref="UserInterface.Inventory"/> geometry and <paramref name="frame"/> its design
        /// frame; both may be null, in which case the model's faithful defaults are used. Icon
        /// sprites load asynchronously via <paramref name="resources"/> and are skipped when it is
        /// null (structure-only unit test).
        ///
        /// <para><paramref name="onSlotActivated"/> is the keyboard/gamepad activation hook for the
        /// <paramref name="navWidgets"/> this builds (their <c>Primary</c>): the live inventory
        /// screen needs <paramref name="onSlotPicked"/> to stay null (see the Clickable note below,
        /// which would otherwise fight the screen's own drag-capture pointer handling) but still
        /// wants Enter/accelerator activation on a focused cell, so it passes only this one. The
        /// widget's <c>Primary</c> falls back to <paramref name="onSlotPicked"/> when <paramref
        /// name="onSlotActivated"/> is null (see the fallback below) — that keeps bare test harnesses
        /// that pass only <paramref name="onSlotPicked"/> working unchanged; it is not a second,
        /// independent activation path.</para></summary>
        /// <summary>What a shop cell says: the item's name, and what it costs.</summary>
        /// <remarks>
        /// The renderer is handed strings rather than a price, because working one out needs the
        /// shop's markup, the zone's exchange rate and the item's condition — shop rules, which
        /// belong to <c>ShopPricing</c> and not to a view. See <see cref="InventoryMenu"/> for the
        /// caller that assembles them.
        /// </remarks>
        public readonly struct ShopCellText {
            public ShopCellText(string name, string price, string nameHead = null,
                bool shadowed = false) {
                Name = name;
                Price = price;
                NameHead = nameHead;
                Shadowed = shadowed;
            }

            /// <summary>The line carrying the end of the name and its condition or count.</summary>
            public string Name { get; }

            public string Price { get; }

            /// <summary>
            /// The first half of a split name, or null when the name fits one line.
            /// </summary>
            /// <remarks>
            /// The original splits at the object's authored <c>WordWrap</c> rather than wrapping —
            /// see <see cref="GameData.Resources.Shop.ShopCellLabel"/>. Without it a long name
            /// overhangs into the neighbouring cell's text, which is what "Standard Kingdom Armor
            /// (100%)" did next to "Broadsword (100%)".
            /// </remarks>
            public string NameHead { get; }

            /// <summary>
            /// Draw a black copy behind every line — the original's outline for a BULKY item.
            /// </summary>
            /// <remarks>
            /// <b>It is how the text stays readable over a tall icon.</b> invui_grid_render guards
            /// each of the three draws with <c>if (wDefault_qty_or_1 == 4)</c> and, when it holds,
            /// draws the same string first at (x-1, y-1) in colour 0. The eleven items that carry
            /// a 4 are exactly the bulky ones — the staves, the six armours and the bag of grain —
            /// whose sprites reach down into the name lines. Without it their names read as
            /// smeared over the icon, which is what "the text does not fit" looks like.
            /// </remarks>
            public bool Shadowed { get; }
        }

        private Func<RuntimeItem, ObjectInfo, ShopCellText?> _shopCellText;

        /// <summary>
        /// Stacks a shop cell's two lines upward from its bottom edge.
        /// </summary>
        /// <remarks>
        /// Price on the bottom line, name one font-height above it (INVENTOR.C:472-492). Both are
        /// centred rather than right-aligned — the ordinary quantity number hugs the lower-right
        /// corner, but a shop cell's text spans the cell.
        /// </remarks>
        private static void AddShopCellText(VisualElement cell, int slot, ShopCellText text,
            float nameLineOffset) {
            AddShopLine(cell, $"item_slot_{slot}_price", text.Price, 0f, text.Shadowed);

            // The name sits one line above the price. It went undrawn for two sessions because
            // names overlapped their neighbours — the cause was never the text but the CELL: these
            // were member cells 200 canonical px wide. A shop cell is 490, which is what the
            // original gives "Standard Kingdom Armor" to sit in.
            AddShopLine(cell, $"item_slot_{slot}_name", text.Name, nameLineOffset,
                text.Shadowed);

            // A split name puts its head one line higher again — the original's fontH*3 against
            // the tail's fontH*2 and the price's fontH*1, evenly spaced.
            if (!string.IsNullOrEmpty(text.NameHead)) {
                AddShopLine(cell, $"item_slot_{slot}_name_head", text.NameHead,
                    nameLineOffset * 2f, text.Shadowed);
            }
        }

        /// <summary>
        /// How far the name line rides above the price, from the layout.
        /// </summary>
        /// <remarks>
        /// Refuses a percentage rather than guessing: the offset is a bottom inset in the same px
        /// space the cell size is expressed in, and a percentage there would resolve against the
        /// cell rather than the font and slide as the cell changes. A refusal stacks the two lines
        /// on each other, which is visibly wrong at a glance — better than a silently wrong offset.
        /// </remarks>
        private float NameLineOffset() {
            LayoutLength offset = _layout.ShopNameLineOffset;
            if (IsDesignPx(offset)) {
                return offset.Value;
            }

            Refuse("InventoryLayout.ShopNameLineOffset", offset.ToString(),
                "the shop cell's name line is offset from the cell's bottom edge in the same px "
                + "space as the cell itself, so a percentage cannot be resolved here — the name "
                + "is drawn on top of the price");

            return 0f;
        }

        /// <summary>
        /// One line of a shop cell: centred on the cell, and <b>allowed to overhang it</b>.
        /// </summary>
        /// <remarks>
        /// <b>The original does not wrap this text and does not fit it to a box.</b>
        /// <c>DisplayText</c> @0x5634d measures the string and shifts x by half its width
        /// (alignment 1), then draws one line; a name wider than the cell simply overhangs its
        /// neighbours. Pinning the label to the cell's edges instead makes UI Toolkit WRAP it,
        /// which is what turned "13 gold 2 silver" into two colliding lines and made the cells look
        /// too small — they are not. Hence auto width, no wrap, and a half-width translate to
        /// centre it.
        /// </remarks>
        private static void AddShopLine(VisualElement cell, string name, string text, float above,
            bool shadowed = false) {
            if (string.IsNullOrEmpty(text)) {
                return;
            }

            var label = new Label(text) {
                name = name,
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = Length.Percent(50), bottom = above,
                    whiteSpace = WhiteSpace.NoWrap,
                    translate = new Translate(Length.Percent(-50), 0),
                    color = InventoryTextStyle.DefaultTextColor,
                },
            };
            if (shadowed) {
                // One VGA pixel up and left, in colour 0 — the original's own offset, expressed in
                // the canonical units this label is laid out in.
                label.style.textShadow = new TextShadow {
                    offset = new Vector2(-BakAgain.Graphics.Canonical.VgaScaleX,
                        -BakAgain.Graphics.Canonical.VgaScaleY),
                    color = Color.black,
                };
            }
            BakAgain.UI.GameFontText.Apply(label,
                BakAgain.UI.GameFontText.AnchorX.Centre, BakAgain.UI.GameFontText.AnchorY.Bottom);
            cell.Add(label);
        }

        public void Render(VisualElement root, RuntimeContainer container, ObjectInfoSet objects,
            IResourceProviderService resources, Action<int> onSlotPicked, InventoryLayoutMode mode,
            InventoryLayout layout, DesignFrame frame,
            IList<BakAgain.UI.InputCore.NavWidget> navWidgets = null,
            Action<int> onSlotActivated = null,
            Func<RuntimeItem, ObjectInfo, ShopCellText?> shopCellText = null,
            int firstItem = 0, int shopChapter = int.MaxValue) {
            _shopCellText = shopCellText;
            Clear();
            navWidgets?.Clear();
            if (root == null || container == null) {
                return;
            }
            _resources = resources;
            // A null layout is not an error: InventoryLayout's defaults ARE the faithful geometry,
            // so a screen whose REQ carries no inventory block still renders correctly rather than
            // rendering nothing at all.
            _layout = layout ?? new InventoryLayout();
            // GridArea (and LootBox below) are settable reference properties an override can null
            // out, so they get the same graceful degradation the missing-grid case has always
            // had: render nothing and leave the rest of the screen standing, rather than throw
            // out of Render and take the whole inventory screen down with it.
            // The shop shelf is a different grid, not the member grid re-used: six wide cells at
            // their own origin (see InventoryLayout.ShopGridArea).
            LayoutHint area = mode == InventoryLayoutMode.Shop
                ? _layout.ShopGridArea
                : _layout.GridArea;
            LayoutGrid grid = area?.Grid;
            if (grid == null) {
                return; // no grid area, or an area with no grid — there is nothing to place cells on
            }
            // Auto is the LayoutGrid default, so `"Grid": {"Columns": 4, "Rows": 4}` in an override
            // reaches here with no cell size at all — and "intrinsic" is not a size a fixed-cell
            // grid can be built from. Every cell would come out unplaced and unsized (see
            // LayoutApplier.ApplyGridPlacement, which refuses the same unit), i.e. an invisible,
            // unreachable grid. Refuse once here rather than once per cell.
            if (grid.CellWidth.Unit == LayoutLengthUnit.Auto
                || grid.CellHeight.Unit == LayoutLengthUnit.Auto) {
                Refuse("InventoryLayout.GridArea.Grid.CellWidth/CellHeight",
                    grid.CellWidth + " x " + grid.CellHeight,
                    "a fixed-cell grid needs a real cell size and Auto is not one, so no item " +
                    "cell can be placed — the grid is not rendered");
                return;
            }
            int gen = _generation;

            List<Placement> placements = Pack(container, objects, mode, grid, firstItem, shopChapter);
            if (placements.Count == 0) {
                return;
            }
            bool member = mode == InventoryLayoutMode.Member;
            Vector2 generalOrigin = ResolveGridOrigin(placements, grid, member,
                mode == InventoryLayoutMode.Shop, frame, area, root,
                out bool needsFillReposition, out float clusterWidth);
            VisualElement generalHost = null;
            VisualElement paperdollHost = null;

            foreach (Placement pl in placements) {
                int slot = pl.ItemIndex; // capture for the closures below
                // The paperdoll sits at the grid's own origin; the general grid is offset (member
                // shift or loot centering). Hosts are created on demand so a container with only
                // equipped items — or none at all — leaves no empty element behind.
                VisualElement host;
                if (pl.Equipped) {
                    host = paperdollHost ?? (paperdollHost = CreateGridHost(root, PaperdollHostName,
                        new Vector2(area.Left.Value, area.Top.Value), grid, area));
                } else {
                    host = generalHost ?? (generalHost =
                        CreateGridHost(root, GridHostName, generalOrigin, grid, area));
                }

                if (pl.Equipped) {
                    // Black cell fill under an occupied paperdoll slot (@0x569e4) — a SIBLING placed
                    // before the cell, so the selection outline's fill toggling never clobbers it,
                    // and the cell's own background stays free for the icon sprite. This is what
                    // hides the empty-slot placeholder sprite when something is equipped.
                    var underlay = new VisualElement {
                        name = $"item_slot_{slot}_equipbg",
                        pickingMode = PickingMode.Ignore,
                        style = { backgroundColor = Color.black },
                    };
                    LayoutApplier.ApplyGridPlacement(underlay, grid, pl.Cell);
                    host.Add(underlay);
                    _built.Add(underlay);
                }
                var cell = new VisualElement { name = $"item_slot_{slot}" };
                LayoutApplier.ApplyGridPlacement(cell, grid, pl.Cell);
                // Optional convenience click hook (used by unit tests). The loot controller passes
                // null and drives its own pointer down/move/up so its drag-capture isn't fought by a
                // Clickable manipulator.
                if (onSlotPicked != null) {
                    cell.AddManipulator(new Clickable(() => onSlotPicked(slot)));
                }
                host.Add(cell);
                _built.Add(cell);
                // The cell as a menu entry. In the original an item cell is not a special kind of
                // thing: cmbinv_combat_encounter_begin appends one MenuEntry per item to the same
                // page->pEntries list the chrome hotspots live in (canassa CMBINV.C:108, 142), and
                // menupage_navigate walks that whole list. So the screen's keyboard/gamepad model
                // gets the cells the same way it gets its buttons.
                //   * live rect: ApplyGridPlacement may place the cell in grid or percentage units,
                //     and cell.layout is relative to the grid host, so the rect is read back from
                //     resolved geometry in the STAGE's space (which is canonical space).
                //   * no label: the first-letter accelerator matches labels; item entries have none.
                //   * no secondary: nothing routes a secondary action from the keyboard, and the
                //     original's inspect flow needs a right-drag state a keyboard cannot produce.
                if (navWidgets != null) {
                    int navSlot = slot;
                    VisualElement navCell = cell;
                    cell.focusable = true;
                    // onSlotActivated wins when given (the live screen: onSlotPicked stays null so
                    // the Clickable above isn't attached, but Enter/accelerator still needs to fire);
                    // falling back to onSlotPicked keeps the pre-existing single-callback callers
                    // (tests, and any future bare harness) working unchanged.
                    navWidgets.Add(new BakAgain.UI.InputCore.NavWidget(
                        cell, null, () => StageRect(root, navCell),
                        () => (onSlotActivated ?? onSlotPicked)?.Invoke(navSlot), null,
                        ItemActionIdBase + navSlot));
                }

                RuntimeItem item = container.Items[slot];
                ObjectInfo obj = objects?.GetById(item.ObjectId);
                // Item-count / condition text, per invui_grid_render's non-shop branch
                // (INVENTOR.C:495-520, UI_DrawInventory @0x56caf-0x56d45). Four outcomes:
                //   0x1000+0x8            → condition as "N%", always
                //   0x8000                → the stack count, always (rations, quarrels)
                //   SharedKeys container  → the count, always (DOS RES_PICKLOCK_BUFFER; IDA reads
                //                           it as container.metaData.containerType == 8)
                //   0x1000 or 0x2000      → the bare number, but ONLY while this slot is the
                //                           highlighted one — that is how a charged item shows its
                //                           uses left when you select it
                // Anything else shows nothing at all.
                // A SHOP CELL IS A DIFFERENT BRANCH, not the ordinary one with a price added.
                // invui_grid_render stacks upward from the cell's bottom edge: the price on the
                // bottom line and the item's NAME one font-height above it, with the condition or
                // charge count appended to that name rather than drawn on its own. So the
                // quantity label below is not merely joined by a price — it is replaced.
                ShopCellText? shopText = _shopCellText?.Invoke(item, obj);
                if (shopText != null) {
                    AddShopCellText(cell, slot, shopText.Value, NameLineOffset());
                }

                string qtyText = null;
                bool onlyWhileSelected = false;
                if (shopText == null && obj != null) {
                    int fl = (int)obj.Flags;
                    if ((fl & 0x1000) != 0 && (fl & 0x8) != 0) {
                        qtyText = $"{item.Variable}%";
                    } else if ((fl & 0x8000) != 0
                        || container.ContainerType == SaveGameContainerType.SharedKeys) {
                        qtyText = item.Variable.ToString();
                    } else if ((fl & 0x3000) != 0) {
                        qtyText = item.Variable.ToString();
                        onlyWhileSelected = true;
                    }
                }
                if (qtyText != null) {
                    var qty = new Label(qtyText) {
                        name = $"item_slot_{slot}_qty",
                        pickingMode = PickingMode.Ignore,
                        style = {
                            position = Position.Absolute,
                            right = 0,
                            bottom = 0,
                            // INVENTOR.PAL pen 0x9F — warm tan, right-aligned at the cell's lower-right
                            // (UI_DrawInventory @0x56d14), in the plain game font (inherited from
                            // ClassicTheme's Game SDF). No bold — the original has no bold face, and
                            // faux-bolding the SDF smeared the digits.
                            color = InventoryTextStyle.DefaultTextColor,
                        },
                    };
                    // Size + the anisotropy stretch, shared with every other game-font surface
                    // (task-45). Anchored bottom-right because the label is pinned to that corner,
                    // so the glyphs grow upward and the baseline stays on the cell's lower edge.
                    BakAgain.UI.GameFontText.Apply(qty,
                        BakAgain.UI.GameFontText.AnchorX.Right, BakAgain.UI.GameFontText.AnchorY.Bottom);
                    if (onlyWhileSelected) {
                        // Built but hidden: selection is applied after the render (the outline works
                        // the same way), so the label is here waiting rather than forcing a rebuild
                        // of the whole grid on every click. The class is what tells the menu this
                        // one is selection-gated — the always-on numbers must not be toggled.
                        qty.AddToClassList(SelectionQuantityClass);
                        qty.style.display = DisplayStyle.None;
                    }
                    cell.Add(qty);
                }

                if (resources != null && obj != null) {
                    // A shop cell raises its icon to clear the two text lines; every other cell
                    // centres it. Passing the height rather than a flag keeps the arithmetic where
                    // the sprite's own size is known.
                    // The FLAGS travel with the object: a lit torch is a different bitmap, and
                    // only the carried stack knows it is lit (ItemIconResolver, TASK-583).
                    LoadIconAsync(cell, obj, item.ItemFlags, gen,
                        mode == InventoryLayoutMode.Shop ? grid.CellHeight.Value : 0f).Forget();
                }
            }

            if (needsFillReposition && generalHost != null) {
                ScheduleFillReposition(root, generalHost, area, clusterWidth, gen);
            }
        }

        /// <summary>Cell footprint (w,h) in <b>design-frame px</b> for an item's slot count — also
        /// used by the controller to size the drag ghost. 1 slot → 1×1 cell, 2–3 → 2 wide, 4+ → 2×2,
        /// against the grid this renderer last rendered with.
        ///
        /// <para>Zero when the grid's cells are authored as percentages: a fraction of a parent is
        /// not a px size, and returning the percentage's bare NUMBER would make the drag ghost a
        /// few px across instead of a cell. The caller sizes nothing rather than size it wrongly —
        /// see <see cref="ResolveGridOrigin"/> for the same rule and why it is a refusal.</para></summary>
        public Vector2 CellSizeCanonical(int inventorySlots) {
            LayoutGrid grid = _layout.GridArea?.Grid;
            if (grid == null) {
                return Vector2.zero;
            }
            if (!IsDesignPx(grid.CellWidth) || !IsDesignPx(grid.CellHeight)) {
                Refuse("InventoryLayout.GridArea.Grid.CellWidth/CellHeight",
                    grid.CellWidth + " x " + grid.CellHeight,
                    "a cell footprint in design-frame px is what sizes the drag ghost, and a " +
                    "percentage of an unmeasured parent is not one — the ghost is left unsized");
                return Vector2.zero;
            }
            FootprintSpans(inventorySlots, out int columnSpan, out int rowSpan);
            return new Vector2(columnSpan * grid.CellWidth.Value, rowSpan * grid.CellHeight.Value);
        }

        // The paperdoll cell for an equipped item's category (sub_ovr157_0 @0x5451e). span 2 = one
        // cell row tall, span 4 = two rows; Sword and Staff share row 0 because a member carries
        // one or the other. Any other category: no paperdoll slot (span 0 in the original — the
        // item simply isn't laid out).
        // internal (not private) so MemberEquip's equip gate can ask "is this an equippable
        // category at all?" without restating the set — this is its single owner.
        internal static bool TryPaperdollSlot(GameData.ObjectType type, out int row, out int span) {
            switch (type) {
                case GameData.ObjectType.Sword: row = 0; span = 2; return true;
                case GameData.ObjectType.Crossbow: row = 1; span = 2; return true;
                case GameData.ObjectType.Staff: row = 0; span = 4; return true;
                case GameData.ObjectType.Armor: row = 2; span = 4; return true;
                default: row = 0; span = 0; return false;
            }
        }

        /// <summary>Where the empty-slot silhouette belonging to paperdoll row
        /// <paramref name="paperdollRow"/> is drawn: that cell's own top-left (column 0, so the grid
        /// area's left inset unchanged), nudged down by
        /// <see cref="InventoryLayout.PaperdollPlaceholderNudgeY"/> — the one original pixel of inset
        /// the original blits it with (<c>UI_DrawInventory</c> @0x56990). The sprite keeps its native
        /// size, so this is a position, not a rect.
        ///
        /// <para>This lives here, beside <see cref="TryPaperdollSlot"/> and the cell arithmetic it
        /// depends on, because the whole point is that a silhouette moves with its cell. The
        /// alternative — a free-standing point in the data, which is what
        /// <see cref="InventoryLayout.CrossbowPlaceholder"/> used to be — is a second encoding of a
        /// geometry only this class derives, and the two drift the moment
        /// <c>GridArea.Grid.CellHeight</c> changes (the armor silhouette lands inside the crossbow
        /// slot).</para>
        ///
        /// <para>Null when the geometry cannot be derived: no grid area/grid at all, or a vertical
        /// axis authored in percentages — the nudge is a design-frame px scalar added to the cell's
        /// top inset, and a percentage of an unmeasured parent cannot be added to px (the same rule
        /// and the same refusal as <see cref="ResolveGridOrigin"/>). The caller draws no silhouette
        /// rather than draw one somewhere the data never said.</para></summary>
        internal static LayoutHint PaperdollPlaceholderHint(InventoryLayout layout, int paperdollRow) {
            LayoutHint area = layout?.GridArea;
            LayoutGrid grid = area?.Grid;
            if (grid == null) {
                return null; // no grid area, or an area with no grid — there is no cell to sit on
            }
            if (!IsDesignPx(area.Top) || !IsDesignPx(grid.CellHeight)) {
                Refuse("InventoryLayout.GridArea (the empty-slot silhouette's own cell)",
                    "top " + area.Top + ", cell height " + grid.CellHeight,
                    "a silhouette sits one original pixel below its paperdoll cell's top edge, and "
                    + "that px nudge cannot be added to a percentage of an unmeasured parent — the "
                    + "silhouette is not drawn");
                return null;
            }
            return new LayoutHint {
                // Column 0: the cell's left edge IS the grid area's, so it passes through untouched
                // and may stay a percentage — no arithmetic happens on this axis.
                Left = area.Left,
                Top = LayoutLength.Px(area.Top.Value
                    + (paperdollRow * grid.CellHeight.Value)
                    + layout.PaperdollPlaceholderNudgeY),
            };
        }

        // Footprint of an item's slot count, in grid CELLS (sub_ovr157_0 @0x544ea). The single owner
        // of that rule — the packer and the drag-ghost sizer both ask here.
        private static void FootprintSpans(int inventorySlots, out int columnSpan, out int rowSpan) {
            if (inventorySlots <= 0) {
                inventorySlots = 1;
            }
            columnSpan = inventorySlots > 1 ? 2 : 1;
            rowSpan = inventorySlots > 2 ? 2 : 1;
        }

        /// <summary>
        /// Where the general-item grid's top-left cell sits inside the screen.
        ///
        /// <para><b>Member mode</b> is a straight offset: the grid starts at its declared origin
        /// nudged right by <see cref="InventoryLayout.MemberShiftX"/>, clear of the paperdoll's
        /// reserved columns (@0x545f3).</para>
        ///
        /// <para><b>Loot mode</b> centres the packed cluster (@0x54663). The original computes this
        /// as an offset added to every item's coordinate, measuring the cluster's right/bottom edge
        /// from the SCREEN origin — i.e. its "maxRight" includes the grid's own left inset — and
        /// then halving with integer division. Written out, with L/T the grid origin, C the cell
        /// size, M/N the columns/rows the cluster occupies and B the loot box:
        /// <code>
        ///   left = L + floor((B.w - (L + M*C.w)) / 2)
        ///   top  = T + (B.h - (T + N*C.h)) / 2
        /// </code>
        /// The vertical half is exact and is used as written. The horizontal one is not: its
        /// remainder is always odd in the original's own units, so the truncation discards a
        /// half-unit that no value in this layout can express (it is a quantum of the original
        /// display's pixel grid, which is exactly the knowledge this class must not carry).
        /// Fortunately it does not have to — <b>for the shipped numbers</b>, which satisfy two
        /// preconditions in the original's own units (L=14, T=12, C=40x30, B=307x132,
        /// frameWidth=320):
        /// <list type="number">
        /// <item><description><c>L + B.w - 1 == frameWidth</c> — the box's left inset (14) and
        /// its right gap (13, i.e. frameWidth - B.w) differ by exactly one, and that odd unit is
        /// what the floor discards (14 + 307 - 1 == 320);</description></item>
        /// <item><description><c>B.w - L</c> is odd (293) and <c>C.w</c> is even (40), so
        /// <c>B.w - (L + M*C.w)</c> is odd for <i>every</i> M and the floor always discards
        /// exactly one half.</description></item>
        /// </list>
        /// Under those two, the truncated form collapses in two steps:
        /// <code>
        ///   L + floor((B.w - L - M*C.w)/2) = L + (B.w - L - M*C.w - 1)/2   [precondition 2]
        ///                                  = (L + B.w - 1 - M*C.w)/2
        ///                                  = (frameWidth - M*C.w)/2        [precondition 1]
        /// </code>
        /// — the cluster centred on the frame's centre line, which is exact, needs no truncation
        /// and reflows. <b>The equality is NOT general.</b> Drop either precondition and the two
        /// sides part company: the tests' synthetic fixture (L=23, B.w=1103, frameWidth=911,
        /// C.w=137, M=1) gives 494.5 for the box form and 387 for the frame form. The frame form
        /// is what this method implements and what the tests pin, so do <i>not</i> "restore" the
        /// box formula for symmetry with the vertical axis — on the shipped data it lands 2.5
        /// design px right of where the original draws, on every loot render.
        /// (The algebra above is the proof. As corroboration, an offline sweep compared this
        /// implementation against the pre-change one over every slot-count sequence of length &lt;= 8
        /// drawn from {1,2,4}, plus 3000 random longer sequences — 0 mismatches in loot and member
        /// mode; that sweep is a sample of the packings the grid admits, not an exhaustive
        /// enumeration of them.)</para>
        ///
        /// <para><b>Both derivations are arithmetic, so both need units that can be added.</b> A
        /// cell placed by <see cref="LayoutApplier.ApplyGridPlacement"/> may be a percentage — that
        /// is the reflow feature and it is untouched here — but the grid's ORIGIN is a different
        /// question: loot centring subtracts the packed cluster's width from the design frame's px
        /// width, and the member shift adds <see cref="InventoryLayout.MemberShiftX"/> to the grid
        /// area's own inset. A percentage is a fraction of a parent whose size only a completed
        /// layout pass knows; this class measures nothing (the same rule
        /// <see cref="LayoutApplier"/> keeps) and <c>resolvedStyle</c> reads 0 before that pass. So
        /// where the sum is not expressible, it is refused loudly — the error names the offending
        /// field and the grid falls back to its <i>declared</i> origin, uncentred and unshifted,
        /// which is a position the data really does state and which every cell inside still
        /// reflows against. Silently adding the percentage's bare number to a px inset is the one
        /// outcome ruled out; it puts the whole cluster somewhere neither the author nor the
        /// original asked for, with no warning. Same treatment as
        /// <c>ItemInspectPanel.TryResolveIconRestingPoint</c>.</para>
        ///
        /// <para><b>Fit = Fill (Phase 5).</b> Everything above proves the frame-centred form equals
        /// the box-centred one <i>under Contain</i>, where <see cref="CanonicalStage.ApplyFrame"/>
        /// makes <c>frame.Width</c> and the stage's own resolved width the same number by
        /// construction. Under <see cref="LayoutFit.Fill"/> that construction doesn't hold — the
        /// stage spans the real window (≈2133 logical px at 16:9) while <c>frame.Width</c> still
        /// reads the declared 1600 — so centring on the frame would leave the cluster visibly left
        /// of the container it actually sits in. Nothing about the derivation above changes: it is
        /// still "centre the cluster on the container's width", just evaluated against the
        /// container's REAL width instead of its declared one when the two can differ. This method
        /// reads which frame/Fit <see cref="CanonicalStage"/> actually applied to <paramref
        /// name="root"/> via <see cref="CanonicalStage.TryGetFrame"/> (rather than trusting
        /// <paramref name="frame"/> blindly, which could in principle name a different frame than
        /// the one the stage was built with) and, under Fill, hands the caller a signal to correct
        /// the horizontal origin once the stage's resolved width is actually known — see
        /// <see cref="ScheduleFillReposition"/>. It cannot do that correction itself: <c>Render()</c>
        /// calls this before the grid host exists and before any UI Toolkit layout pass has run, so
        /// <c>root.resolvedStyle.width</c> reads 0 here. Under Contain (including when <paramref
        /// name="root"/> was never built through <see cref="CanonicalStage.GetOrCreate"/>, so
        /// <c>TryGetFrame</c> returns false) this method's return value is final and unchanged —
        /// the shipped screen's synchronous, single-pass rendering is untouched.</para>
        /// </summary>
        private Vector2 ResolveGridOrigin(List<Placement> placements, LayoutGrid grid, bool member,
            bool shop, DesignFrame frame, LayoutHint area, VisualElement root,
            out bool needsFillReposition, out float clusterWidthOut) {
            needsFillReposition = false;
            clusterWidthOut = 0f;
            // The origin the data declares outright. No arithmetic, so it is expressible in
            // whatever unit the hint carries — and it is where a refused offset leaves the grid.
            var declared = new Vector2(area.Left.Value, area.Top.Value);

            if (shop) {
                // A shop's rects are absolute in the original — nothing is centred and nothing is
                // shifted, so the declared origin IS the answer.
                return declared;
            }

            if (member) {
                // "4% + 12%" is 16% of the same parent and resolves fine; "4% + 12px" is not a
                // length at all. So the shift only has to agree with the inset it is added to.
                if (Resolved(area.Left) != Resolved(_layout.MemberShiftX)) {
                    Refuse("InventoryLayout.MemberShiftX",
                        _layout.MemberShiftX + " against a grid area Left of " + area.Left,
                        "the member shift is added to the grid area's own inset, and two " +
                        "different units cannot be summed without measuring the parent — the " +
                        "general grid is left unshifted, over the paperdoll's columns");
                    return declared;
                }
                return new Vector2(area.Left.Value + _layout.MemberShiftX.Value, area.Top.Value);
            }

            // Loot centring measures the cluster against the design frame, which is px — so every
            // length that meets the frame's own numbers has to be px too.
            if (!IsDesignPx(grid.CellWidth) || !IsDesignPx(grid.CellHeight)
                || !IsDesignPx(area.Left) || !IsDesignPx(area.Top)
                || (_layout.LootBox != null && !IsDesignPx(_layout.LootBox.Height))) {
                Refuse("the loot grid's centring inputs",
                    "cells " + grid.CellWidth + " x " + grid.CellHeight + ", grid area at ("
                    + area.Left + ", " + area.Top + "), LootBox.Height "
                    + (_layout.LootBox == null ? "(none)" : _layout.LootBox.Height.ToString()),
                    "the packed cluster is centred against the design frame's px width and " +
                    "height, so a percentage among those cannot be subtracted from them — the " +
                    "cluster is left at the grid area's declared origin, uncentred");
                return declared;
            }

            float clusterWidth = 0f;
            float clusterHeight = 0f;
            foreach (Placement pl in placements) {
                clusterWidth = Mathf.Max(clusterWidth,
                    (pl.Cell.Column + pl.Cell.ColumnSpan) * grid.CellWidth.Value);
                clusterHeight = Mathf.Max(clusterHeight,
                    (pl.Cell.Row + pl.Cell.RowSpan) * grid.CellHeight.Value);
            }
            clusterWidthOut = clusterWidth;

            // Prefer the frame CanonicalStage actually applied to `root` (and its Fit) over the raw
            // `frame` argument: that is the frame whose Width construction-equals the stage's own
            // resolved width under Contain (the precondition the whole derivation above depends
            // on), so reading it here rather than trusting the argument keeps that guarantee even
            // if a caller ever passed a differently-provenanced frame. TryGetFrame returns false for
            // a root that was never built through CanonicalStage.GetOrCreate — a bare VisualElement,
            // as every structure-only unit test in this file uses — and that degrades to exactly the
            // pre-Fill fallback below, using the argument.
            bool haveStageFrame = CanonicalStage.TryGetFrame(root, out DesignFrame stageFrame, out bool _);
            DesignFrame effectiveFrame = haveStageFrame ? stageFrame : frame;

            // A screen with no design frame of its own falls back to the canonical space every
            // extracted screen is expressed in — the same fallback CanonicalStage makes.
            float frameWidth = effectiveFrame != null && effectiveFrame.Width > 0
                ? effectiveFrame.Width
                : BakAgain.Graphics.Canonical.Width;
            // A layout that declares no loot box has nothing to centre the vertical axis in, so
            // that axis falls back to the same rule the horizontal one already uses — centre in
            // the frame. Degrading (rather than dereferencing a null the data model permits) is
            // the same choice the missing-grid path makes. (Unaffected by Fill: the shared
            // PanelSettings uses match-height scaling, so the panel's logical HEIGHT is always 1200
            // regardless of Fit — only width diverges from the declared frame — see CanonicalStage's
            // class doc.)
            float verticalCentre;
            if (_layout.LootBox != null) {
                verticalCentre = (area.Top.Value + _layout.LootBox.Height.Value) / 2f;
            } else {
                float frameHeight = effectiveFrame != null && effectiveFrame.Height > 0
                    ? effectiveFrame.Height
                    : BakAgain.Graphics.Canonical.Height;
                verticalCentre = frameHeight / 2f;
            }

            // Under Fill, `frameWidth` above is still the DECLARED width (1600) — the stage's real
            // width is unknowable here (see the doc comment) — so this return is only a synchronous
            // placeholder for that axis; flag the caller to correct it once the stage's layout has
            // actually resolved.
            needsFillReposition = haveStageFrame && stageFrame.Fit == LayoutFit.Fill;

            return new Vector2(
                (frameWidth - clusterWidth) / 2f,
                verticalCentre - clusterHeight / 2f);
        }

        /// <summary>
        /// Corrects the loot cluster's horizontal origin once <paramref name="root"/> (the grid
        /// host's PARENT — see <see cref="CreateGridHost"/>) has actually resolved its width, for
        /// the <see cref="LayoutFit.Fill"/> case <see cref="ResolveGridOrigin"/> could not settle
        /// synchronously. Follows this UI's existing deferred-recompute precedent — register on
        /// <see cref="GeometryChangedEvent"/> and redo the math once real geometry exists:
        /// <c>DialogPanelBuilder</c>'s pill-radius recompute, <c>InputFormRenderer</c>'s
        /// caret/highlight recompute, <c>FilePickerRenderer</c>'s scrollbar-thumb recompute,
        /// <c>CreditsView</c>'s leader-dot repaint.
        ///
        /// <para>Reads <c>root.resolvedStyle.width</c> — panel-LOGICAL units, the same space every
        /// design-frame px in this class (including <paramref name="clusterWidth"/> and
        /// <paramref name="area"/>'s own inset) is already expressed in.
        /// <see cref="CanonicalStage.ScreenRect"/> converts to PHYSICAL screen pixels for hit-testing
        /// callers like the world viewport; using it here would silently reintroduce a second, wrong
        /// unit conversion into a purely layout-space computation.</para>
        ///
        /// <para>Registered on <paramref name="root"/> itself, not on <paramref name="host"/>: it is
        /// root's size that is unresolved at <c>Render()</c> time, not the host's (the host's own
        /// geometry is fully determined by its literal style values and settles regardless of root's
        /// size). Unlike the cells/hosts in <c>_built</c>, <paramref name="root"/> is the long-lived
        /// <see cref="CanonicalStage"/> element that outlives any single <c>Render()</c> call — the
        /// general grid re-renders on every equip/unequip/transfer — so a stale registration from an
        /// earlier render is torn down first via <see cref="ClearDeferredReposition"/>; otherwise
        /// every re-render would leak one more permanently-registered closure onto it.</para>
        ///
        /// <para><b>The callback alone is not enough, and this is the whole reason the method
        /// applies synchronously too.</b> An already-laid-out <paramref name="root"/> does not raise
        /// <see cref="GeometryChangedEvent"/> again just because absolutely-positioned children were
        /// added to it — under Fill it is 100%×100%, so its own rect never changes for the rest of
        /// the screen's life. Only the FIRST render on a brand-new stage can be caught by the
        /// callback. Every later one — <c>InventoryMenu.RenderAll</c> runs again on
        /// <c>Built</c>/<c>OnAfterShow</c> and once per equip/unequip/transfer — would register a
        /// callback that never fires, leaving the cluster at
        /// <see cref="ResolveGridOrigin"/>'s declared-frame placeholder: the exact ~266 logical px
        /// left-of-centre offset at 16:9 that this correction exists to remove, reappearing on the
        /// first equip. So the correction is applied inline whenever root's width already resolves,
        /// and the callback is kept for the two cases where it cannot: the stage's first-ever layout
        /// and any later window resize.</para>
        /// </summary>
        private void ScheduleFillReposition(VisualElement root, VisualElement host, LayoutHint area,
            float clusterWidth, int gen) {
            ClearDeferredReposition();

            void Reposition() {
                // Clear() may have torn `host` down (and bumped _generation) since this was
                // scheduled — a later re-render, an equip/unequip, or the screen closing. Stale:
                // no-op rather than repositioning an element nothing points at any more.
                if (gen != _generation) {
                    return;
                }
                float containerWidth = root.resolvedStyle.width;
                if (float.IsNaN(containerWidth) || containerWidth <= 0f) {
                    return; // not laid out yet; the GeometryChangedEvent below will retry
                }
                float newLeft = (containerWidth - clusterWidth) / 2f;
                host.style.left = DerivedLength(newLeft, area.Left);
            }

            EventCallback<GeometryChangedEvent> callback = _ => Reposition();
            _deferredReposition = callback;
            _deferredRepositionRoot = root;
            root.RegisterCallback(callback);

            // Registered first, then applied: on a stage that has never laid out this is a no-op
            // (resolvedStyle.width reads 0) and the callback carries the correction, exactly as
            // before. On an already-laid-out stage — every render after the first — this IS the
            // correction, because no further GeometryChangedEvent is coming. See the doc above.
            Reposition();
        }

        // Tears down a pending ScheduleFillReposition registration, if any. Safe to call
        // unconditionally (Clear() does, on every render, Fill or not) — a no-op when nothing is
        // registered.
        private void ClearDeferredReposition() {
            if (_deferredRepositionRoot != null && _deferredReposition != null) {
                _deferredRepositionRoot.UnregisterCallback(_deferredReposition);
            }
            _deferredRepositionRoot = null;
            _deferredReposition = null;
        }

        // The element the cells are placed into: a grid container at `origin`, sized to the grid it
        // hosts. Pure geometry — it never intercepts a pick, so a press still falls through to a
        // cell or, off a cell, to the chrome beneath, exactly as when cells were built into the
        // stage directly.
        private VisualElement CreateGridHost(VisualElement root, string name, Vector2 origin,
            LayoutGrid grid, LayoutHint area) {
            var host = new VisualElement {
                name = name,
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = DerivedLength(origin.x, area.Left),
                    top = DerivedLength(origin.y, area.Top),
                    width = DerivedLength(grid.CellWidth.Value * grid.Columns, grid.CellWidth),
                    height = DerivedLength(grid.CellHeight.Value * grid.Rows, grid.CellHeight),
                },
            };
            root.Add(host);
            _built.Add(host);
            return host;
        }

        // A computed length carried in the same unit as the datum it was derived from. Auto is not
        // a unit an offset can be expressed in, so it degrades to the design frame's own px. The
        // rule itself lives in LayoutApplier — ItemInspectPanel needs the same one for the icon's
        // snapped flight positions, so this is a local name for it, not a second copy.
        private static StyleLength DerivedLength(float value, LayoutLength source) =>
            LayoutApplier.Derived(value, source);

        // Can this length take part in design-frame px arithmetic? Auto can: it carries no opinion
        // and LayoutApplier.Derived already degrades it to the frame's own px, which is the space
        // every extracted value is in. A percentage cannot — see ResolveGridOrigin. The rule lives
        // in LayoutApplier next to Derived (DialogPanelBuilder needs the same one for the body
        // text's derived top inset), so this is a local name for it, not a second copy.
        private static bool IsDesignPx(LayoutLength length) =>
            LayoutApplier.IsDesignPx(length);

        // The unit a length effectively resolves to, for "may these two be summed" — Auto collapses
        // onto Px for the same reason IsDesignPx accepts it.
        private static LayoutLengthUnit Resolved(LayoutLength length) =>
            length.Unit == LayoutLengthUnit.Auto ? LayoutLengthUnit.Px : length.Unit;

        // Say NO, and say which field and why. The message shape lives in LayoutApplier so every
        // refusal across the UI reads the same; this is a local name for it, not a second copy.
        private static void Refuse(string field, string authored, string consequence) =>
            LayoutApplier.RefuseUnresolvable(field, authored, consequence);

        // Column-major first-fit packing (sub_ovr157_0 @0x544ea): scan columns left→right, rows
        // top→bottom, place each item at the first cell where its whole footprint is free. Member
        // mode first places equipped items on the paperdoll and reserves the leading columns.
        // Placements are GRID-RELATIVE — where the grid itself sits is ResolveGridOrigin's job.
        private static List<Placement> Pack(RuntimeContainer container, ObjectInfoSet objects,
            InventoryLayoutMode mode, LayoutGrid grid, int firstItem, int shopChapter) {
            var placements = new List<Placement>();
            int columns = grid.Columns;
            int rows = grid.Rows;
            if (columns <= 0 || rows <= 0) {
                return placements; // a grid with no cells holds nothing
            }
            var occupied = new bool[columns, rows];
            bool member = mode == InventoryLayoutMode.Member;
            int count = mode == InventoryLayoutMode.Shop
                ? container.Items.Count           // paged below, not truncated here
                : Mathf.Min(container.Items.Count, columns * rows);
            // Counted, not derived from placements.Count: member mode legitimately places only the
            // EQUIPPED subset, so a difference there is not a drop.
            int truncated = mode == InventoryLayoutMode.Shop ? 0 : container.Items.Count - count;
            var unplaced = 0;

            if (member) {
                for (int i = 0; i < count; i++) {
                    RuntimeItem item = container.Items[i];
                    if ((item.ItemFlags & EquippedFlag) == 0) {
                        continue;
                    }
                    ObjectInfo obj = objects?.GetById(item.ObjectId);
                    if (obj == null || !TryPaperdollSlot(obj.ObjectType, out int pdRow, out int pdSpan)) {
                        continue; // equipped-but-uncategorized: no slot, faithful to the span==0 skip
                    }
                    // The paperdoll is placed IN the grid, so it is bounded by the same row/column
                    // limits as the reservation loop below and TryFindFreeCell — but it resolves an
                    // overflow differently: those drop an item whose footprint doesn't fit, while
                    // the paperdoll clips it. A category whose row does not exist in this grid gets
                    // no cell (the same outcome as the span==0 skip), and a footprint that would
                    // hang off the last row/column is clipped to what the grid has instead of being
                    // dropped. On the shipped 7x4 geometry nothing binds:
                    // the deepest slot is Armor at row 2 span 2 (rows 2-3 of 4) and the widest is
                    // 2 of 7 columns, so every canonical rect is unchanged.
                    if (pdRow >= rows) {
                        continue;
                    }
                    placements.Add(new Placement {
                        ItemIndex = i,
                        Cell = new LayoutGridPlacement {
                            Column = 0,
                            Row = pdRow,
                            ColumnSpan = Mathf.Min(PaperdollColumns, columns),
                            RowSpan = Mathf.Min(pdSpan > 2 ? 2 : 1, rows - pdRow),
                        },
                        Equipped = true,
                    });
                }
                for (int c = 0; c < PaperdollColumns && c < columns; c++) {
                    for (int r = 0; r < rows; r++) {
                        occupied[c, r] = true; // the paperdoll owns the first two columns (@0x5459b)
                    }
                }
            }

            for (int i = 0; i < count; i++) {
                if (member && (container.Items[i].ItemFlags & EquippedFlag) != 0) {
                    continue; // equipped items live on the paperdoll, never in the general grid
                }
                if (mode == InventoryLayoutMode.Shop) {
                    int shelfSlot = i - firstItem;
                    if (shelfSlot < 0) {
                        continue;   // on an earlier page
                    }
                    if (shelfSlot >= columns * rows) {
                        break;      // on a later one — the builder's numberOfItems <= slot+page stop
                    }
                    // *** THE SHELF IS GATED PER ITEM, AND THE SLOT IS LEFT EMPTY. *** The builder
                    // skips an item the chapter has not introduced and still advances its cell
                    // index, so the hole stays — see ShopStock.IsOfferedInChapter. Compacting here
                    // would pull a later item onto the page and show a different six.
                    if (!GameData.Resources.Shop.ShopStock.IsOfferedInChapter(
                            objects?.GetById(container.Items[i].ObjectId), shopChapter)) {
                        continue;
                    }
                    // *** THE SHELF FILLS ACROSS, NOT DOWN. *** The shop arm derives its column
                    // from `slot % 3` and its row from `slot <= 2` (0x542e1), which is ROW-major —
                    // the opposite of the member grid's column-major first fit. Packing a shelf the
                    // member way transposes it, so the second item sits under the first instead of
                    // beside it.
                    //
                    // There is also no first FIT to do: a shop cell is one fixed rect per item.
                    // The arm writes width 98 and height 60 unconditionally, never doubling them
                    // for a two- or four-slot item the way the member arm does, so a broadsword
                    // takes one shelf slot rather than two.
                    // ItemIndex stays ABSOLUTE — the original's actionId carries the page offset
                    // too (0x542c0), so a click on the second page still names the real item.
                    placements.Add(new Placement {
                        ItemIndex = i,
                        Cell = new LayoutGridPlacement {
                            Column = shelfSlot % columns,
                            Row = shelfSlot / columns,
                            ColumnSpan = 1,
                            RowSpan = 1,
                        },
                    });
                    continue;
                }

                FootprintSpans(objects?.GetById(container.Items[i].ObjectId)?.InventorySlots ?? 1,
                    out int columnSpan, out int rowSpan);
                if (!TryFindFreeCell(occupied, columns, rows, columnSpan, rowSpan,
                        out int col, out int row)) {
                    unplaced++;
                    continue; // grid full — item dropped, matching the original bound
                }
                for (int c = 0; c < columnSpan; c++) {
                    for (int r = 0; r < rowSpan; r++) {
                        occupied[col + c, row + r] = true;
                    }
                }
                placements.Add(new Placement {
                    ItemIndex = i,
                    Cell = new LayoutGridPlacement {
                        Column = col,
                        Row = row,
                        ColumnSpan = columnSpan,
                        RowSpan = rowSpan,
                    },
                });
            }

            // *** SAY SO WHEN AN ITEM IS NOT DRAWN. *** Dropping past the grid's bound matches the
            // original, but doing it silently does not help anyone: the item is in the container the
            // screen is showing and simply is not on it — indistinguishable from a save that lost it.
            // Not reachable on shipped data (the grid is 7x4 = 28 and no shipped container needs
            // more), so this only ever fires for an authored layout, which is exactly who needs
            // telling. One line per pack rather than one per item.
            if (unplaced > 0 || truncated > 0) {
                Debug.LogWarning(
                    $"Inventory grid {columns}x{rows} could not show {unplaced + truncated} of " +
                    $"{container.Items.Count} items ({truncated} past the cell count, {unplaced} " +
                    "with no free footprint). They are in the container but not on the screen — " +
                    "see TASK-66.");
            }

            return placements;
        }

        private static bool TryFindFreeCell(bool[,] occupied, int columns, int rows,
            int columnSpan, int rowSpan, out int col, out int row) {
            for (int c = 0; c + columnSpan <= columns; c++) {
                for (int r = 0; r + rowSpan <= rows; r++) {
                    if (RegionFree(occupied, c, r, columnSpan, rowSpan)) {
                        col = c;
                        row = r;
                        return true;
                    }
                }
            }
            col = -1;
            row = -1;
            return false;
        }

        private static bool RegionFree(bool[,] occupied, int col, int row, int columnSpan, int rowSpan) {
            for (int c = 0; c < columnSpan; c++) {
                for (int r = 0; r < rowSpan; r++) {
                    if (occupied[col + c, row + r]) {
                        return false;
                    }
                }
            }
            return true;
        }

        private async UniTask LoadIconAsync(VisualElement cell, ObjectInfo obj, ushort itemFlags,
            int gen, float raiseInCellHeight) {
            string key = ItemIconResolver.ResolveBmxSubResource(obj, itemFlags);
            if (key == null) {
                return;
            }
            if (_spriteCache.TryGetValue(key, out Sprite cached)) {
                // Session-cache hit: applied synchronously, so re-renders never pop in.
                PlaceIcon(cell, cached, raiseInCellHeight);
                return;
            }
            Sprite sprite = await _resources.LoadAssetAsync<Sprite>(key, this);
            if (sprite != null) {
                // Cache even when this render was superseded — the NEXT render wants it.
                _spriteCache[key] = sprite;
            }
            if (gen != _generation || sprite == null || cell.panel == null) {
                return; // superseded/cleared while awaiting, load failed, or cell torn down
            }
            PlaceIcon(cell, sprite, raiseInCellHeight);
        }

        /// <summary>
        /// Native size, centred in the cell on both axes (<c>invui_grid_render</c> @0x568e4:
        /// <c>icon_x/y = rect + (rect − sprite)/2</c>) — small icons like vials sit mid-slot.
        /// </summary>
        /// <param name="raiseInCellHeight">The cell's height when the icon should ride high instead
        /// (the shop's /4 divisor), or 0 to centre it.</param>
        private static void PlaceIcon(VisualElement cell, Sprite sprite, float raiseInCellHeight) {
            if (raiseInCellHeight > 0f) {
                cell.SetBackgroundSpriteNativeSizeRaised(sprite, raiseInCellHeight);
            } else {
                cell.SetBackgroundSpriteNativeSizeCentered(sprite);
            }
        }

        /// <summary>A session-cached icon sprite, if this renderer has loaded it — lets the host
        /// reuse grid icons (the drag ghost) without taking another Addressables handle.</summary>
        internal bool TryGetCachedSprite(string key, out Sprite sprite) =>
            _spriteCache.TryGetValue(key, out sprite);

        /// <summary>Remove everything this renderer built — cells, underlays and their grid hosts.
        /// Runs before every re-render; deliberately does NOT release icon handles, so sprites stay
        /// session-cached until <see cref="ReleaseAll"/>.</summary>
        public void Clear() {
            _generation++; // invalidate any in-flight LoadIconAsync
            ClearDeferredReposition();
            foreach (VisualElement built in _built) {
                built.RemoveFromHierarchy();
            }
            _built.Clear();
        }

        /// <summary>End the screen session: clear cells, drop the sprite cache and release every
        /// icon handle — the <c>invui_inspect_image_cleanup</c> moment, called from the screen's
        /// teardown.</summary>
        public void ReleaseAll() {
            Clear();
            _spriteCache.Clear();
            _resources?.ReleaseAssets(this);
            _resources = null;
        }

        /// <summary>
        /// Action id of the item at slot 0. The original numbers item entries
        /// <c>wEntry_count + 0x79</c> (canassa CMBINV.C:108, 142) — with seven chrome entries that
        /// makes the first item 0x80, and the drag handler recovers the item index as
        /// <c>wAction_id - 0x80</c>.
        ///
        /// <para>0x80 is also REQ_INV's full-screen catch-all action, and that is not a clash to be
        /// designed away: the original never has both live at once — it trims the page's live list
        /// to the chrome entries plus the items, so the catch-all is absent from the grid view and
        /// the id is free. Any filter over action ids must therefore run over the CHROME list only,
        /// which is what InventoryMenu.ComposeNavWidgets does.</para>
        /// </summary>
        internal const int ItemActionIdBase = 0x80;

        /// <summary>
        /// Marks a cell's quantity label as one the original draws only while that slot is
        /// highlighted (<c>UI_DrawInventory</c>'s <c>arg_8</c> gate) — a charged or degradable
        /// item's number, shown when you select it. The always-on numbers carry no such class, so
        /// the selection handler cannot hide a stack count by accident.
        /// </summary>
        internal const string SelectionQuantityClass = "item-qty-on-select";

        // A child's rect in the stage's own coordinate space. The stage is the CanonicalStage, so
        // this IS the canonical rect that NavWidget's consumers (spatial nav, cursor warp,
        // focus-under-cursor) compare against. Degrades to empty before the first layout pass:
        // worldBound reads NaN then, and a NaN rect would poison Contains() and the spatial-nav
        // distance maths silently.
        private static Rect StageRect(VisualElement stage, VisualElement element) {
            Rect world = element.worldBound;
            if (stage == null || float.IsNaN(world.x) || float.IsNaN(world.y)
                || float.IsNaN(world.width) || float.IsNaN(world.height)) {
                return Rect.zero;
            }
            Vector2 topLeft = stage.WorldToLocal(new Vector2(world.xMin, world.yMin));
            Vector2 bottomRight = stage.WorldToLocal(new Vector2(world.xMax, world.yMax));
            return new Rect(topLeft, bottomRight - topLeft);
        }
    }
}
