namespace BakAgain.UI.Inventory {
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Enhanced side-by-side loot (TASK-889, owner-approved S3, "pack only"): the chest or body on the
    /// left, beside the container window, and a member's pack on the right, beside the portraits
    /// (TASK-944), in the one stone frame, instead of the original's one-at-a-time view and its
    /// container toggle. Each side is drawn over its real capacity in squares: the pack's 20 slots
    /// and the container's 28 (more rows only if the game filled it past that), the budgets of
    /// <c>cmbinv_has_space_for_item</c> (canassa CMBINV.C:631).
    ///
    /// <para><b>Nothing here decides a rule.</b> One side is the faithful screen's live
    /// <c>_displayed</c>, drawn by the usual renderer; the other is a passive picture drawn by a
    /// second renderer under its own cell names. A press on the picture makes it the live side,
    /// exactly as a portrait click or the container window would. A drop on the other side is the
    /// drop the original already has for it: onto the pack is onto that member's portrait, onto the
    /// loot is onto the container window. So every transfer, refusal and message is the original's.</para>
    /// </summary>
    public partial class InventoryMenu {
        private const string PeerSlotPrefix = "loot_peer_";

        /// <summary>The share of the item field between the two panels; the frame's middle bar lies over it.</summary>
        private const float HalfGapShare = 0.026f;

        /// <summary>Room left round each side's squares inside its panel, in canonical units.</summary>
        private const float SquaresPadding = 24f;

        private readonly ItemGridRenderer _peer = new ItemGridRenderer { SlotPrefix = PeerSlotPrefix };
        private VisualElement _packHalf, _lootHalf, _packWrap, _lootWrap;
        private Vector2 _packSize, _lootSize;
        private int _lootMember; // the portrait whose pack is beside the portraits

        /// <summary>Looting a chest, a body or a pile with the Enhanced layout — not a shop, a lock,
        /// a temple, a mender, a fight, or the party's own packs and keys.</summary>
        private bool SideBySide =>
            EnhancedApplied && _container != null && _shop == null && !IsLockMode && !IsBlessingMode
            && !IsRepairMode && !InCombat
            && _container.ContainerType != SaveGameContainerType.Inventory
            && _container.ContainerType != SaveGameContainerType.SharedKeys
            && HintRect(Layout.FullItemsBox).width > 0f && HintRect(Layout.GeneralItemsBox).width > 0f;

        private bool ShowingLootSide => ReferenceEquals(_displayed, _container);

        private VisualElement PeerHalf => ShowingLootSide ? _packHalf : _lootHalf;

        /// <summary>Where the live renderer draws: its side's panel when looting side by side.</summary>
        private VisualElement LiveRenderRoot() {
            if (!SideBySide) {
                return _stage;
            }
            EnsureHalves();
            return ShowingLootSide ? _lootWrap : _packWrap;
        }

        /// <summary>After the live render: hide the faithful boxes and draw the other side.</summary>
        private void RenderLootPeer() {
            if (!SideBySide) {
                RemoveHalves();
                return;
            }
            foreach (VisualElement box in _panelBoxes) box.style.display = DisplayStyle.None;
            foreach (VisualElement ph in _placeholders) ph.style.display = DisplayStyle.None;
            if (!ShowingLootSide) {
                int shown = ActivePortraitSlot();
                if (shown >= 0) _lootMember = shown;
            }
            RuntimeContainer other = ShowingLootSide ? ResolveMemberContainer(_lootMember) : _container;
            // Sorted as it will be when it is the live side, so a press on the picture names the
            // same slot the live render will give that item (InventoryOrder runs on every render).
            if (other != null && InventoryOrder.Consolidate(other, _gameSession.ObjectInfo,
                    equippedOrder: ShowingLootSide, shopChapter: null)) {
                other.Dirty = true;
            }
            _peer.Render(ShowingLootSide ? _packWrap : _lootWrap, other, _gameSession.ObjectInfo, _resources,
                onSlotPicked: null, ShowingLootSide ? InventoryLayoutMode.Member : InventoryLayoutMode.Loot,
                Layout, _ui?.Frame, squares: true);
            PlaceWraps();
        }

        private void EnsureHalves() {
            if (_packHalf != null && _packHalf.parent == _stage) {
                return;
            }
            RemoveHalves();
            _lootHalf = Half();
            _packHalf = Half();
            _packWrap = Wrap(_packHalf);
            _lootWrap = Wrap(_lootHalf);
            Split(0.5f); // until both sides have rendered: PlaceWraps splits by their squares
        }

        private VisualElement Half() {
            var half = new VisualElement {
                name = "panel_bg", // the stone frame joins the two with its middle bar
                pickingMode = PickingMode.Position,
                style = {
                    position = Position.Absolute,
                    backgroundColor = Color.black, overflow = Overflow.Hidden,
                },
            };
            _stage.Add(half);
            return half;
        }

        /// <summary>The loot's panel on the left with <paramref name="lootShare"/> of the field, the
        /// pack's on the right with the rest: the container window sits bottom left of the Enhanced
        /// screen and the portraits in a column on its right.</summary>
        private void Split(float lootShare) {
            Rect full = HintRect(Layout.FullItemsBox);
            float gap = full.width * HalfGapShare;
            float lootW = (full.width - gap) * lootShare;
            _lootSize = new Vector2(lootW, full.height);
            _packSize = new Vector2(full.width - gap - lootW, full.height);
            Size(_lootHalf, new Rect(full.x, full.y, _lootSize.x, _lootSize.y));
            Size(_packHalf, new Rect(full.x + lootW + gap, full.y, _packSize.x, _packSize.y));
        }

        private static void Size(VisualElement half, Rect r) {
            half.style.left = r.x;
            half.style.top = r.y;
            half.style.width = r.width;
            half.style.height = r.height;
        }

        private VisualElement Wrap(VisualElement half) {
            // Stage coordinates inside: the renderers place their cells exactly as on the stage,
            // and the wrap's scale and offset bring the part that matters into its panel.
            var wrap = new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute, left = 0, top = 0,
                    width = _ui?.Frame?.Width ?? _stage.layout.width,
                    height = _ui?.Frame?.Height ?? _stage.layout.height,
                    transformOrigin = new TransformOrigin(0, 0),
                },
            };
            half.Add(wrap);
            return wrap;
        }

        /// <summary>While an item is examined the two panels become one, as the faithful examine
        /// takes the whole field: the left one (the loot's) spans it and the right one, and both
        /// pictures, hide.</summary>
        private void SyncLootHalvesForExamine() {
            if (_packHalf == null || _packHalf.parent != _stage) {
                return;
            }
            bool examining = _inspectOpen || _descriptionOpen;
            Rect full = HintRect(Layout.FullItemsBox);
            _lootHalf.style.width = examining ? full.width : _lootSize.x;
            _packHalf.style.display = examining ? DisplayStyle.None : DisplayStyle.Flex;
            _lootWrap.style.display = examining ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void RemoveHalves() {
            _peer.Clear();
            _packHalf?.RemoveFromHierarchy();
            _lootHalf?.RemoveFromHierarchy();
            _packHalf = _lootHalf = _packWrap = _lootWrap = null;
        }

        /// <summary>
        /// The field is split between the two panels in proportion to their squares, so one scale
        /// fits both and an item is the same size on either side; each grid is centred in its panel.
        /// The squares are the renderers' own (no layout pass to wait for).
        /// </summary>
        private void PlaceWraps() {
            if (_packWrap == null) {
                return;
            }
            Rect pack = Padded(ShowingLootSide ? _peer.SquaresRect : _renderer.SquaresRect, Layout.GeneralItemsBox);
            Rect loot = Padded(ShowingLootSide ? _renderer.SquaresRect : _peer.SquaresRect, Layout.FullItemsBox);
            Split(loot.width / (loot.width + pack.width));
            float s = Mathf.Min(1f, Fit(pack, _packSize), Fit(loot, _lootSize));
            Place(_packWrap, pack, s, _packSize);
            Place(_lootWrap, loot, s, _lootSize);
        }

        private static Rect Padded(Rect squares, LayoutHint fallback) =>
            squares.width > 0f
                ? Rect.MinMaxRect(squares.xMin - SquaresPadding, squares.yMin - SquaresPadding,
                    squares.xMax + SquaresPadding, squares.yMax + SquaresPadding)
                : HintRect(fallback); // that side drew nothing (no member to show)

        private static float Fit(Rect r, Vector2 half) =>
            r.width <= 0f || r.height <= 0f ? 1f : Mathf.Min(half.x / r.width, half.y / r.height);

        private static void Place(VisualElement wrap, Rect r, float s, Vector2 half) {
            wrap.style.scale = new Scale(new Vector3(s, s, 1f));
            wrap.style.translate = new Translate(half.x / 2f - s * r.center.x, half.y / 2f - s * r.center.y);
        }

        /// <summary>A press on the other side's picture makes that side live, as the portrait or the
        /// container window would, then takes up the press on its cells once they are laid out.</summary>
        private bool PressOnLootPeer(Vector2 stageLocal, int clickCount) {
            if (!SideBySide || _packHalf == null || !PeerHalf.layout.Contains(stageLocal)) {
                return false;
            }
            // The slot under the press, read off the picture before it is redrawn as the live side:
            // the same container in the same order, so the index carries over and a drag can start
            // at once (the new cells have no layout until the next frame).
            int slot = SlotAt(stageLocal, PeerSlotPrefix);
            bool switched = MakePeerLive();
            _pressPos = stageLocal;
            _pressClickCount = clickCount;
            _pressSlot = switched ? slot : -1; // Shift opened a character sheet: the index is not ours
            _dragSlot = -1;
            return true;
        }

        /// <summary>Right-click (or a long press) on the other panel: that side becomes live, then
        /// its item is examined once the redrawn cells have a layout to fly the icon from.</summary>
        private bool ExamineOnLootPeer(Vector2 stageLocal) {
            if (!SideBySide || _packHalf == null || !PeerHalf.layout.Contains(stageLocal)) {
                return false;
            }
            int slot = SlotAt(stageLocal, PeerSlotPrefix);
            if (slot < 0) {
                return true;
            }
            if (MakePeerLive()) {
                _stage.schedule.Execute(() => ShowItemDescriptionAsync(slot).Forget()).StartingIn(ExamineDelayMs);
            }
            return true;
        }

        private const long ExamineDelayMs = 50; // a layout pass for the swapped cells

        /// <summary>The picture's side becomes the live one, as its portrait or the container window
        /// would make it; false when that click did something else (Shift: the character sheet).</summary>
        private bool MakePeerLive() {
            RuntimeContainer before = _displayed;
            if (ShowingLootSide) {
                SelectMember(_lootMember);
            } else {
                DeselectItem();
                ShowOtherInventory();
            }
            return !ReferenceEquals(_displayed, before);
        }

        /// <summary>A drop on the pack while the loot is live goes to that member: the portrait drop.</summary>
        private int LootPackPortraitUnder(Vector2 stageLocal) =>
            SideBySide && ShowingLootSide && _packHalf != null && _packHalf.layout.Contains(stageLocal) ? _lootMember : -1;

        /// <summary>A drop on the loot while a pack is live goes into it: the container-window drop.</summary>
        private bool OverLootFromPack(Vector2 stageLocal) =>
            SideBySide && !ShowingLootSide && _lootHalf != null && _lootHalf.layout.Contains(stageLocal);
    }
}
