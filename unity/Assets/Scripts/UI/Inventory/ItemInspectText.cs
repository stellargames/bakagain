namespace BakAgain.UI.Inventory {
    using System.Collections.Generic;
    using GameData;
    using GameData.Resources.Inventory;
    using GameData.Resources.Object;

    /// <summary>
    /// The text of the item-inspect view (right-click an item), ported line-for-line from
    /// <c>UI_showItem</c> @0x5A778. Pure so the faithfulness of each gate can be pinned by tests
    /// without a REQ build — the layout/positioning lives in the view that consumes this.
    ///
    /// <para>The original draws four things: the item name (one or two lines), one type line, one
    /// status line, and a DDX description dialog. Only the first three are text the view formats
    /// itself; the description prose comes from the dialog and is resolved separately (see
    /// <see cref="DescriptionDialogId"/>).</para>
    /// </summary>
    internal static class ItemInspectText {
        // objectFlags bits. IDA's objectFlags enum names Stackable=0x800, Degradable=0x1000,
        // LimitedUses=0x2000 and leaves 0x8000 unnamed. Our GameData ObjectFlags names 0x800 and
        // 0x2000 but leaves 0x1000/0x8000 as B1000/B8000, so those two are spelled out here.
        private const int FlagDegradable = 0x1000;  // IDA objectFlags.Degradable
        private const int FlagAmount = 0x8000;      // IDA objectFlags_8000h — the "Amount:" gate.
                                                    // NOT Stackable (0x800); that trap is easy to hit.
        private const int ShopCategoryJewelry = 0x4; // itemShopCategories.Jewelry

        private const ushort ItemBroken = (ushort)GameData.ItemFlags.Broken;         // 0x10
        private const ushort ItemRepairable = (ushort)GameData.ItemFlags.Repairable; // 0x20
        private const ushort ItemEquipped = (ushort)GameData.ItemFlags.Equipped;     // 0x40

        /// <summary>The per-object description dialog: 134 Var-0 branches keyed by object id.</summary>
        internal const int DescriptionDialogId = 1800001;

        /// <summary>Spell scrolls take a different dialog, keyed by the scroll's spell
        /// (<see cref="RuntimeItem.Variable"/>) rather than the object id, and skip More Info.</summary>
        internal const int ScrollDescriptionDialogId = 1800033;

        /// <summary>Var 0 of a dialog condition is global 30000 (see DialogBranchWalker).</summary>
        internal const int DescriptionGlobalKey = 30000;

        /// <summary>
        /// The item name split for display — <see cref="ObjectNameLines"/>, shared with the shoot
        /// menu's target panel, which splits the same names. (Faithful detail: a single line is
        /// drawn here at the *second* line's y, not the first — that's the view's business, not
        /// this method's, and it is the one thing the two panels do differently.)
        /// </summary>
        internal static IReadOnlyList<string> NameLines(ObjectInfo obj) =>
            ObjectNameLines.Split(obj);

        /// <summary>
        /// The single type line, or null when the item has none. First match wins, exactly as the
        /// if/else chain at 0x5A865-0x5A8E1 does; every variant formats
        /// <see cref="RuntimeItem.Variable"/>.
        /// </summary>
        internal static string TypeLine(RuntimeItem item, ObjectInfo obj) {
            if (item == null || obj == null) {
                return null;
            }
            int flags = (int)obj.Flags;
            int amount = item.Variable;

            if ((flags & FlagAmount) != 0 || obj.ObjectType == ObjectType.Key) {
                return GameData.Resources.Text.CFormat.Apply(GameData.Resources.Text.UiStrings.Get("base:uistring:item.amount"), amount);
            }
            if ((flags & (int)ObjectFlags.LimitedUses) != 0 && obj.ObjectType != ObjectType.Book) {
                return GameData.Resources.Text.CFormat.Apply(GameData.Resources.Text.UiStrings.Get("base:uistring:item.uses_left"), amount);
            }
            if ((flags & FlagDegradable) != 0 && (obj.ShopType & ShopCategoryJewelry) != 0) {
                return GameData.Resources.Text.CFormat.Apply(GameData.Resources.Text.UiStrings.Get("base:uistring:item.value_rating"), amount);
            }
            if ((flags & FlagDegradable) != 0) {
                return GameData.Resources.Text.CFormat.Apply(GameData.Resources.Text.UiStrings.Get("base:uistring:item.condition"), amount);
            }
            return null; // e.g. rations, keys-that-aren't-Key-type, plain misc
        }

        /// <summary>
        /// The status line, built by the strcpy/strcat chain at 0x5A905-0x5A977. Empty when nothing
        /// applies (the original still draws the empty buffer; callers may skip it).
        /// <para><paramref name="affecting"/> is the caller's flag — set for a party member's own
        /// inventory, clear for loot/shop — and gates the "Using" prefix only.</para>
        /// </summary>
        internal static string StatusLine(RuntimeItem item, ObjectInfo obj, bool affecting) {
            if (item == null) {
                return string.Empty;
            }
            // The separator belongs to "Using": INVINSP.C:377-383 strcat's ", " only inside the
            // branch that wrote "Using", so a carried broken item reads "Broken", not ", Broken".
            // (Measured on James's 74% Lamprey: the original shows "Repairable".) The joined form is
            // a template, so a language can join them its own way (TASK-775).
            string usingText = affecting
                && ((item.ItemFlags & ItemEquipped) != 0 || (obj?.EquipAttributeMask ?? 0) != 0)
                ? GameData.Resources.Text.UiStrings.Get("base:uistring:item.using")
                : string.Empty;
            string state = (item.ItemFlags & ItemBroken) != 0
                ? GameData.Resources.Text.UiStrings.Get("base:uistring:item.broken")
                : (item.ItemFlags & ItemRepairable) != 0
                    ? GameData.Resources.Text.UiStrings.Get("base:uistring:item.repairable")
                    : string.Empty;
            return usingText.Length > 0 && state.Length > 0
                ? GameData.Resources.Text.UiTemplates.Format(GameData.Resources.Text.UiTemplates.ItemUsingAndState,
                    ("using", (object)usingText), ("state", (object)state))
                : usingText + state;
        }

        /// <summary>
        /// Which description dialog to show and what to seed global 30000 with. Spell scrolls key
        /// the scroll dialog by their spell (<c>item.variable</c>); everything else keys the object
        /// dialog by its object id (0x5A9A2-0x5A9DA).
        /// </summary>
        internal static (int DialogId, int GlobalValue) DescriptionLookup(RuntimeItem item, ObjectInfo obj) {
            bool isScroll = obj?.ObjectType == ObjectType.MagicalScroll;
            return isScroll
                ? (ScrollDescriptionDialogId, item.Variable)
                : (DescriptionDialogId, item.ObjectId);
        }

        /// <summary>
        /// Whether the "More Info" button (action 57) is offered — the gate at 0x5A9F7-0x5AA1F.
        /// Shops pass no menu page at all, so the caller must also suppress it there.
        /// </summary>
        /// <remarks>
        /// <b>The Misc test is a GUARD, not an accept.</b> The original reads
        /// <c>cmp objectType, Misc / jz</c> — jumping <i>past</i> the accept below — and only then
        /// <c>cmp objectType, Armor / jle</c> to show. Since <c>Misc = 0</c> and <c>Armor = 4</c>,
        /// <c>Misc &lt;= Armor</c> is true, so that guard is the only thing keeping Misc items out of
        /// the shortcut. Written as an OR it becomes an accept, and every Misc item grows a More Info
        /// button it has nothing to put in — picklocks being the one that gave it away.
        /// </remarks>
        internal static bool HasMoreInfo(RuntimeItem item, ObjectInfo obj) {
            if (obj == null || item == null) {
                return false;
            }
            if (obj.ObjectType != ObjectType.Misc && (int)obj.ObjectType <= (int)ObjectType.Armor) {
                return true;
            }
            if (item.ObjectId >= 36 && item.ObjectId <= 43) {
                return true; // quarrels
            }
            return obj.EquipAttributeMask != 0;
        }
    }
}
