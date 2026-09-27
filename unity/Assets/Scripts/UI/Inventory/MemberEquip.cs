namespace BakAgain.UI.Inventory {
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The two pure rules behind equip-by-drop (task-44), kept out of <see cref="InventoryMenu"/>
    /// so they can be pinned by unit tests without a live REQ build:
    ///
    /// <list type="bullet">
    /// <item><b>Where</b> — <c>invui_handle_item_drag</c> (INVENTOR.C:609) accepts a drop inside
    /// the paperdoll box, which is <see cref="InventoryLayout.PaperdollBox"/>: the same box
    /// <see cref="InventoryMenu"/> paints as the member-mode panel fill. The visible box *is* the
    /// drop target — not because both read the same hint, but because the zone is taken from the
    /// <b>painted element's own resolved geometry</b>. Sharing the hint was not enough: the fill is
    /// drawn through <see cref="BakAgain.UI.Layout.LayoutApplier"/>, which honours a percentage,
    /// while reading <c>box.Left.Value</c> here would have taken the percentage's bare number as
    /// stage px — so a paperdoll authored as <c>Left "4%"</c> painted at x 64 and accepted drops
    /// at x 4, and dropping on the visible paperdoll would never equip. Asking UI Toolkit where the
    /// box actually ended up makes them one thing by construction, in every unit.</item>
    /// <item><b>What</b> — <c>cmbinv_member_can_equip_cat</c> (CMBINV.C:1084) gates by the actor's
    /// caster-ness: a caster may equip Armor and Staff; a non-caster Armor, Sword and Crossbow.
    /// Everything else (potions, keys, scrolls, …) is never equippable.</item>
    /// </list>
    ///
    /// Loot mode has no equipped area at all (one continuous panel box), so no drop there is ever
    /// an equip — the <c>lootMode</c> overload exists to make that explicit at the call site.
    ///
    /// <para><b>This class owns no coordinates.</b> The box is passed in, so a reflowed or
    /// override-authored paperdoll moves the drop target with it.</para>
    /// </summary>
    internal static class MemberEquip {
        /// <summary>
        /// The drop zone, in the stage's own space, taken from the element the screen actually
        /// painted the paperdoll fill into — so it is the visible box whatever unit it was authored
        /// in, because UI Toolkit has already resolved that unit against the real parent.
        ///
        /// <para><paramref name="drawnBox"/> is null exactly when no paperdoll fill is up (loot
        /// mode, the item-inspect view, or before the first render). Then there is nothing painted
        /// to agree with, and the zone degrades to <paramref name="box"/> — see the
        /// <see cref="LayoutHint"/> overload for why that fallback is px-only.</para>
        /// </summary>
        internal static Rect PaperdollDropZone(VisualElement drawnBox, LayoutHint box) {
            // layout is (0,0,0,0)/NaN until the element has been through a layout pass. A drop
            // cannot happen before then, so treating that as "not painted yet" costs nothing and
            // keeps a half-built element from producing a zone at the origin.
            if (drawnBox != null) {
                Rect painted = drawnBox.layout;
                if (painted.width > 0f && painted.height > 0f
                    && !float.IsNaN(painted.x) && !float.IsNaN(painted.y)) {
                    return painted;
                }
            }
            return PaperdollDropZone(box);
        }

        /// <summary>The paperdoll box as a rect in the screen's own (design-frame) space, read
        /// straight from the hint. A null box — which an override may author — yields an empty
        /// rect, i.e. nothing is a drop. Lengths that are not explicit read as 0, the same
        /// degradation <c>LayoutApplier</c> gives an Auto inset.
        ///
        /// <para>This form can only speak px: a hit test compares against a pointer position in
        /// stage px, and a percentage is a fraction of a parent nothing here measures. Rather than
        /// take the percentage's bare number as px — which would put the zone somewhere the box is
        /// not — it refuses, names the field and yields an empty rect, so a drop simply falls
        /// through to the ordinary snap-back instead of equipping from the wrong place. In the live
        /// screen this does not arise: the <see cref="VisualElement"/> overload above answers from
        /// the painted geometry, which needs no unit.</para></summary>
        internal static Rect PaperdollDropZone(LayoutHint box) {
            if (box == null) {
                return Rect.zero;
            }
            if (IsPercent(box.Left) || IsPercent(box.Top) || IsPercent(box.Width) || IsPercent(box.Height)) {
                Debug.LogError(
                    "InventoryLayout.PaperdollBox is authored in percent (" + box.Left + ", " +
                    box.Top + ", " + box.Width + ", " + box.Height + ") and no paperdoll fill is " +
                    "painted to read it back from, so the equip drop zone cannot be resolved — " +
                    "dropping an item on the paperdoll will not equip it. Author PaperdollBox in " +
                    "design-frame px, or drop onto a rendered member screen.");
                return Rect.zero;
            }
            return new Rect(box.Left.Value, box.Top.Value, box.Width.Value, box.Height.Value);
        }

        private static bool IsPercent(LayoutLength length) => length.Unit == LayoutLengthUnit.Percent;

        /// <summary>True when a stage-local point falls in the painted paperdoll box. Loot mode has
        /// no paperdoll, so it is always false there. <paramref name="drawnBox"/> is the element the
        /// screen painted the fill into and <paramref name="box"/> its hint — see
        /// <see cref="PaperdollDropZone(VisualElement, LayoutHint)"/>.</summary>
        internal static bool IsPaperdollDrop(Vector2 stageLocal, VisualElement drawnBox, LayoutHint box,
            bool lootMode = false) =>
            !lootMode && Inside(stageLocal, PaperdollDropZone(drawnBox, box));

        /// <summary>True when a stage-local point falls in the paperdoll box as the hint declares
        /// it. Loot mode has no paperdoll, so it is always false there.</summary>
        internal static bool IsPaperdollDrop(Vector2 stageLocal, LayoutHint box, bool lootMode = false) =>
            !lootMode && box != null && Inside(stageLocal, PaperdollDropZone(box));

        /// <summary>The hit test itself, and the single owner of its edge rule.
        /// <para>The bounds are <b>exclusive on all four edges</b>, matching the original's compares
        /// at <c>invui_handle_item_drag</c> 0x57307-0x5731e — `cmp x,lo/jle skip`, `cmp x,hi/jge
        /// skip` on both axes. The box's own top-left corner is therefore *outside* the drop zone.
        /// `Rect.Contains` is inclusive at the minimum, so it must not be used here.</para></summary>
        private static bool Inside(Vector2 point, Rect zone) =>
            point.x > zone.xMin && point.x < zone.xMax
            && point.y > zone.yMin && point.y < zone.yMax;

        /// <summary><c>CanEquip</c> @0x55fe2 (CMBINV.C:1084): caster → Armor + Staff; non-caster →
        /// Armor + Sword + Crossbow. The rule itself lives engine-independent in
        /// <see cref="InventoryEquip"/> (it also drives auto-equip in
        /// <c>InventoryTransfer.Move</c>); this forwarder exists so UI call sites keep one name for
        /// "may this member wear that".</summary>
        internal static bool CanEquipCategory(GameData.ObjectType type, bool caster) =>
            InventoryEquip.CanEquipCategory(type, caster);
    }
}
