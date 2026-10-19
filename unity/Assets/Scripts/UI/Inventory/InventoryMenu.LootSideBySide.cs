namespace BakAgain.UI.Inventory {
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Enhanced side-by-side loot (TASK-889, owner-approved S3, "pack only"): a member's pack on the
    /// left and the chest or body on the right, in the one stone frame, instead of the original's
    /// one-at-a-time view and its container toggle.
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

        /// <summary>Room left round the loot's own cells inside its panel, in canonical units.</summary>
        private const float LootPadding = 24f;

        private const int PlaceRetries = 10; // frames the loot's placement waits for its cells' layout

        private readonly ItemGridRenderer _peer = new ItemGridRenderer { SlotPrefix = PeerSlotPrefix };
        private VisualElement _packHalf, _lootHalf, _packWrap, _lootWrap;
        private Vector2 _halfSize;
        private int _lootMember; // the portrait whose pack is on the left
        private IVisualElementScheduledItem _placeRetry;

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
                Layout, _ui?.Frame);
            PlaceWraps(PlaceRetries);
        }

        private void EnsureHalves() {
            if (_packHalf != null && _packHalf.parent == _stage) {
                return;
            }
            RemoveHalves();
            Rect full = HintRect(Layout.FullItemsBox);
            float gap = full.width * HalfGapShare;
            _halfSize = new Vector2((full.width - gap) / 2f, full.height);
            _packHalf = Half(new Vector2(full.x, full.y));
            _lootHalf = Half(new Vector2(full.x + _halfSize.x + gap, full.y));
            _packWrap = Wrap(_packHalf);
            _lootWrap = Wrap(_lootHalf);
        }

        private VisualElement Half(Vector2 at) {
            var half = new VisualElement {
                name = "panel_bg", // the stone frame joins the two with its middle bar
                pickingMode = PickingMode.Position,
                style = {
                    position = Position.Absolute, left = at.x, top = at.y,
                    width = _halfSize.x, height = _halfSize.y,
                    backgroundColor = Color.black, overflow = Overflow.Hidden,
                },
            };
            _stage.Add(half);
            return half;
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
        /// takes the whole field: the left one spans it and the right one, and both pictures, hide.</summary>
        private void SyncLootHalvesForExamine() {
            if (_packHalf == null || _packHalf.parent != _stage) {
                return;
            }
            bool examining = _inspectOpen || _descriptionOpen;
            Rect full = HintRect(Layout.FullItemsBox);
            _packHalf.style.width = examining ? full.width : _halfSize.x;
            _lootHalf.style.display = examining ? DisplayStyle.None : DisplayStyle.Flex;
            _packWrap.style.display = examining ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void RemoveHalves() {
            _placeRetry?.Pause();
            _placeRetry = null;
            _peer.Clear();
            _packHalf?.RemoveFromHierarchy();
            _lootHalf?.RemoveFromHierarchy();
            _packHalf = _lootHalf = _packWrap = _lootWrap = null;
        }

        /// <summary>
        /// One scale for both panels, set by the pack's own box (so items do not change size as the
        /// loot empties) unless the loot is wider; each side centred in its panel. The loot's extent
        /// is its cells' laid-out rects, so this waits for a layout.
        /// </summary>
        private void PlaceWraps(int retries) {
            _placeRetry?.Pause(); // one chain at a time: every render starts its own
            _placeRetry = null;
            if (_packWrap == null) {
                return;
            }
            Rect pack = HintRect(Layout.GeneralItemsBox);
            if (!TryLootExtent(out Rect loot, out bool pending)) {
                if (pending && retries > 0) {
                    _placeRetry = _stage?.schedule.Execute(() => PlaceWraps(retries - 1));
                }
                loot = new Rect(HintRect(Layout.FullItemsBox).center - pack.size / 2f, pack.size);
            }
            float s = Mathf.Min(1f, Fit(pack), Fit(loot));
            Place(_packWrap, pack, s);
            Place(_lootWrap, loot, s);
        }

        private float Fit(Rect r) =>
            r.width <= 0f || r.height <= 0f ? 1f : Mathf.Min(_halfSize.x / r.width, _halfSize.y / r.height);

        private void Place(VisualElement wrap, Rect r, float s) {
            wrap.style.scale = new Scale(new Vector3(s, s, 1f));
            wrap.style.translate = new Translate(_halfSize.x / 2f - s * r.center.x, _halfSize.y / 2f - s * r.center.y);
        }

        /// <summary>The loot's cells, padded, in stage coordinates; false until they have a layout.
        /// An empty container has no extent and answers false with nothing to wait for.</summary>
        private bool TryLootExtent(out Rect extent, out bool pending) {
            extent = default;
            bool any = false;
            pending = false;
            foreach (VisualElement host in _lootWrap.Children()) {
                foreach (VisualElement cell in host.Children()) {
                    if (cell.name == null || cell.name.EndsWith("_equipbg")
                        || !(cell.name.StartsWith(PeerSlotPrefix) || cell.name.StartsWith("item_slot_"))) continue;
                    Rect c = cell.layout, h = host.layout;
                    if (float.IsNaN(c.width) || float.IsNaN(h.x) || c.width <= 0f) { pending = true; continue; }
                    var r = new Rect(h.x + c.x, h.y + c.y, c.width, c.height);
                    extent = any ? EnhancedSheet.Union(extent, r) : r;
                    any = true;
                }
            }
            if (any && !pending) {
                extent = Rect.MinMaxRect(extent.xMin - LootPadding, extent.yMin - LootPadding,
                    extent.xMax + LootPadding, extent.yMax + LootPadding);
                return true;
            }
            return false;
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
