namespace BakAgain.UI.Inventory {
    using System.Collections.Generic;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.Dialog;
    using GameData.Resources.Inventory;
    using GameData.Resources.Object;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Templates = GameData.Resources.Text.UiTemplates;

    /// <summary>
    /// The Enhanced inventory's interactions (spec 2026-10-13): what a drag shows and the merged
    /// examine view. Giving is the faithful drag and examining the faithful right-click or long
    /// press, so nothing here decides a rule.
    /// </summary>
    public partial class InventoryMenu {
        /// <summary>Which verb moving an item onto a member means in this view.</summary>
        internal enum GiveVerb { Give, Take, Buy }

        internal static GiveVerb VerbFor(bool shopShelf, bool memberPack) =>
            shopShelf ? GiveVerb.Buy : memberPack ? GiveVerb.Give : GiveVerb.Take;

        private GiveVerb CurrentVerb =>
            VerbFor(ShowingShopShelf, _displayed?.ContainerType == SaveGameContainerType.Inventory);

        // The Enhanced shop shelf scrolls instead of paging (TASK-890).
        private ShelfScroller _shelf;
        private float _shelfOffset;   // kept across re-renders (a buy redraws the shelf)
        private bool _shelfDrag;

        /// <summary>After a render: drives the scrolling shelf the renderer built, if it built one.</summary>
        private void AttachShelfScroller() {
            _shelf?.StopCoast();
            _shelf = null;
            _shelfDrag = false;
            if (_renderer.ShelfViewport == null) {
                return;
            }
            _shelf = new ShelfScroller(_renderer.ShelfViewport, _renderer.ShelfContent,
                _renderer.ShelfRows, _renderer.ShelfVisibleRows, _shelfOffset);
            ShelfScroller shelf = _shelf;
            foreach (VisualElement cell in _renderer.ShelfContent.Children()) {
                // Keyboard and gamepad focus walks every item; one out of view scrolls in.
                cell.RegisterCallback<FocusInEvent>(_ => shelf.Reveal(cell));
            }
        }

        /// <summary>
        /// A drag that starts on the shelf and moves mostly up or down scrolls it; mostly sideways —
        /// towards the portraits — it is the faithful drag that buys.
        /// </summary>
        private bool BeginShelfDrag(Vector2 stageLocal) {
            if (_shelf == null || !_shelf.Scrolls || _pickerOpen
                || !_shelf.Contains(_stage.LocalToWorld(_pressPos))) {
                return false;
            }
            Vector2 moved = stageLocal - _pressPos;
            if (Mathf.Abs(moved.y) <= Mathf.Abs(moved.x)) {
                return false;
            }
            _shelfDrag = true;
            _shelf.BeginDrag(_stage.LocalToWorld(_pressPos).y);
            _shelf.DragTo(_stage.LocalToWorld(stageLocal).y);
            return true;
        }

        private const float WheelNotch = 120f; // world units per wheel step: about a third of a row

        private void OnShelfWheel(WheelEvent evt) {
            if (_shelf == null || !_shelf.Scrolls || !_shelf.Contains(evt.mousePosition)) {
                return;
            }
            _shelf.Wheel(Mathf.Sign(evt.delta.y) * WheelNotch);
            evt.StopPropagation();
        }

        /// <summary>The bar's hint: how to get back while an item is examined.</summary>
        private void SyncEnhancedChrome() {
            if (EnhancedApplied) {
                _enhanced.SetHint(_inspectOpen ? Templates.Format(Templates.EnhancedInvBack) : null);
            }
        }

        // In a member's own pack the displayed member holds the item; a chest, a shelf or the key
        // ring has no holder among the portraits.
        private bool IsHolder(int portraitSlot) =>
            _displayed?.ContainerType == SaveGameContainerType.Inventory && portraitSlot == ActivePortraitSlot();

        /// <summary>Lights the portraits that can take the dragged item (and dims its holder), or clears them.</summary>
        private void MarkPortraits(bool on) {
            if (!EnhancedApplied) {
                return;
            }
            for (int i = 0; i < 3; i++) {
                VisualElement p = _enhanced.Portrait(i);
                if (p == null) continue;
                bool holder = on && IsHolder(i);
                p.EnableInClassList(EnhancedInventoryLayout.HolderClass, holder);
                SetShade(Root?.Q($"hotspot_{i + PortraitSlot1}"), holder);
                p.EnableInClassList(EnhancedInventoryLayout.TargetClass, on && !holder);
                p.EnableInClassList(EnhancedInventoryLayout.HoverClass, false);
                _enhanced.SetPortraitLabel(i, holder ? Templates.Format(Templates.EnhancedInvHasIt) : null);
            }
        }

        /// <summary>
        /// At drag start: the portraits light, and the paperdoll and the other items
        /// dim where the dragged item cannot go. Presentation only — the drop is still resolved by the
        /// faithful chain, which also gives the original's refusal where this cannot predict one.
        /// </summary>
        private void ShowDragFeedback(int slot) {
            if (!EnhancedApplied) {
                return;
            }
            if (!IsLockMode) { // at a lock the tool goes on the lock, which comes first in the drop chain
                MarkPortraits(on: true);
            }
            ObjectInfo obj = ObjectAt(slot);
            bool memberPack = _displayed?.ContainerType == SaveGameContainerType.Inventory;
            bool equips = memberPack && obj != null
                && MemberEquip.CanEquipCategory(obj.ObjectType, IsDisplayedMemberCaster());
            bool usesOnItems = memberPack && obj != null
                && GameData.Resources.Inventory.InventoryUse.CanUseOnAnotherItem(obj.ObjectType);
            int count = _displayed?.Items.Count ?? 0;
            for (int i = 0; i < count; i++) {
                VisualElement cell = i == slot ? null : _stage?.Q($"item_slot_{i}");
                if (cell == null) continue;
                // A worn item is the paperdoll — a target when the item can be worn; a pack item
                // is a target when the item can be used on another.
                bool worn = cell.parent?.name == ItemGridRenderer.PaperdollHostName;
                SetShade(cell, worn ? !equips : !usesOnItems);
            }
            foreach (VisualElement silhouette in _placeholders) { // the empty paperdoll slots
                SetShade(silhouette, !equips);
            }
        }

        /// <summary>
        /// Darkens an element with a near-opaque layer over it. The project blends in linear colour
        /// space, so a shade that reads as clearly dimmed needs far more alpha than it would in sRGB
        /// (0.7 measured as only ~40% darker); the theme holds the value.
        /// </summary>
        private static void SetShade(VisualElement element, bool on) {
            if (element == null) return;
            VisualElement shade = element.Q(ShadeName);
            if (on && shade == null) {
                shade = new VisualElement { name = ShadeName, pickingMode = PickingMode.Ignore };
                shade.AddToClassList(EnhancedInventoryLayout.ShadeClass);
                element.Add(shade);
            } else if (!on) {
                shade?.RemoveFromHierarchy();
            }
        }

        private const string ShadeName = "enhanced-inv-shade";

        private void ClearDragFeedback() {
            if (!EnhancedApplied) {
                return;
            }
            MarkPortraits(on: false);
            Root?.Query(ShadeName).ToList().ForEach(e => e.RemoveFromHierarchy());
        }

        /// <summary>The hovered portrait names what the drop will do — the member's name, never the item's.</summary>
        private void ShowHoverLabel(int portraitSlot) {
            if (!EnhancedApplied || _dragSlot < 0) {
                return;
            }
            for (int i = 0; i < 3; i++) {
                if (IsHolder(i)) continue;
                bool hovered = i == portraitSlot;
                _enhanced.Portrait(i)?.EnableInClassList(EnhancedInventoryLayout.HoverClass, hovered);
                string key = CurrentVerb switch {
                    GiveVerb.Buy => Templates.EnhancedInvBuyFor,
                    GiveVerb.Take => Templates.EnhancedInvTakes,
                    _ => Templates.EnhancedInvGiveTo,
                };
                _enhanced.SetPortraitLabel(i, hovered ? Templates.Format(key, ("name", _enhanced.NameOf(i))) : null);
            }
        }

        /// <summary>
        /// The Enhanced examine view's right half: the description and, where the faithful More Info
        /// would offer them, the stats — together on the parchment the faithful stat panel uses
        /// (<see cref="ItemStatsText.ChromeDialogId"/>). The left half is the faithful inspect panel,
        /// already drawn. Same text, same lines, same gates; only the extra click is gone.
        /// </summary>
        private async UniTask ShowEnhancedExamineAsync(RuntimeItem item, ObjectInfo obj, DialogPlay play) {
            DialogEntry chrome = await _dialogExecutor.ResolveLeafEntryAsync(ItemStatsText.ChromeDialogId);
            if (chrome == null || _dialogs == null || !_inspectOpen) {
                return;
            }
            VisualElement box = await _dialogs.BuildStyledBoxAsync(chrome, _stage);
            if (box == null) {
                return;
            }
            if (!_inspectOpen) { // closed while the box was being built
                box.RemoveFromHierarchy();
                return;
            }
            _statsBox = box; // CloseInspect tears it down
            // The casting screen's parchment, not the faithful box's shadow, bevel and flat fill.
            foreach (VisualElement chromePart in box.Children()) chromePart.style.display = DisplayStyle.None;
            box.style.backgroundImage = EnhancedArt.Load("parchment");
            box.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            var body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.AddToClassList("enhanced-inv__examine");
            // The same text a shown description draws: variables filled, then the inline codes and
            // tabs resolved by the dialog formatter (a raw code otherwise shows as a box).
            string text = TextVariableResolver.Substitute(play.Entry.Text, play.Slots,
                play.Context.NameOf(play.Context.CurrentActorId));
            var description = new Label(DialogTextFormatter.Format(text, centered: false, palette: null, bodyPen: 0)) {
                pickingMode = PickingMode.Ignore,
                enableRichText = true,
            };
            description.AddToClassList("enhanced-inv__description");
            body.Add(description);
            // A long description beside the stats outgrows the parchment: step the type down until it fits.
            description.RegisterCallback<GeometryChangedEvent>(_ => {
                float size = description.resolvedStyle.fontSize;
                if (description.layout.height > body.contentRect.height && size > MinDescriptionFontSize) {
                    description.style.fontSize = size - 2f;
                }
            });
            box.Add(body);

            if (_displayed?.IsShop == true || !ItemInspectText.HasMoreInfo(item, obj)) {
                return;
            }
            IReadOnlyList<ItemStatsText.Line> lines = ItemStatsText.Build(item, obj, affecting: _container == null, Layout);
            if (lines.Count == 0) {
                return;
            }
            var holder = new VisualElement { pickingMode = PickingMode.Ignore };
            holder.AddToClassList("enhanced-inv__stats");
            // The faithful lines keep their own positions; this root is shifted and scaled so that
            // their extent fills what the description leaves.
            var lineRoot = new VisualElement { pickingMode = PickingMode.Ignore };
            lineRoot.AddToClassList("enhanced-inv__stat-lines");
            holder.Add(lineRoot);
            body.Add(holder);
            _inspect.RenderStats(lineRoot, lines);
            holder.RegisterCallback<GeometryChangedEvent>(_ => FitStats(holder, lineRoot));
            lineRoot.RegisterCallback<GeometryChangedEvent>(_ => FitStats(holder, lineRoot));
        }

        private const float MinDescriptionFontSize = 28f;

        /// <summary>The faithful stat lines are small; they may grow this much into the room they get.</summary>
        private const float MaxStatsScale = 1.6f;

        private static void FitStats(VisualElement holder, VisualElement lineRoot) {
            Rect extent = Rect.zero;
            bool any = false;
            foreach (VisualElement line in lineRoot.Children()) {
                Rect r = line.layout;
                if (float.IsNaN(r.width) || r.width <= 0f) continue;
                extent = any ? Rect.MinMaxRect(Mathf.Min(extent.xMin, r.xMin), Mathf.Min(extent.yMin, r.yMin),
                    Mathf.Max(extent.xMax, r.xMax), Mathf.Max(extent.yMax, r.yMax)) : r;
                any = true;
            }
            Rect room = holder.contentRect;
            if (!any || room.width <= 0f || room.height <= 0f) {
                return;
            }
            float scale = Mathf.Min(MaxStatsScale, room.width / extent.width, room.height / extent.height);
            lineRoot.style.scale = new Scale(new Vector2(scale, scale));
            lineRoot.style.translate = new Translate(-extent.x * scale, -extent.y * scale);
        }

        /// <summary>A finger's double tap selects but never Uses with the Enhanced layout: a stray one
        /// would eat whatever was tapped. Use is the button.</summary>
        private bool DoubleTapUses => !(EnhancedApplied && _gesture != null && _gesture.LastPressWasTouch);
    }
}
