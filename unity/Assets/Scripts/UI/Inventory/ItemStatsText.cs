namespace BakAgain.UI.Inventory {
    using System.Collections.Generic;
    using System.Text;
    using BakAgain.UI.Layout;
    using GameData;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using GameData.Resources.Object;
    using GameData.Resources.Text;
    using UnityEngine;

    /// <summary>
    /// The "More Info" stat panel (<c>UI_showItemStats</c> @0x5A1DA), reached from the item-inspect
    /// view by pressing More Info (action 57/0x39, or 0x32). Produces the panel's lines with their
    /// design-frame positions so the whole layout can be pinned by tests; rendering is the view's
    /// job.
    ///
    /// <para>The panel is a stack of sections, each appended only when it applies, with the cursor
    /// advancing down as it goes. Which sections appear depends on the item's category:</para>
    /// <list type="bullet">
    /// <item><b>Sword / Staff</b> — a two-column Thrust|Swing table of base damage and accuracy.</item>
    /// <item><b>Crossbow, or quarrel ids 36..43</b> — single-column base damage and accuracy, each
    /// naming the partner item it combines with.</item>
    /// <item><b>Armor</b> — a single "Armor Mod:" percentage.</item>
    /// <item><b>Anything else</b> — no combat block at all (the original jumps straight past it).</item>
    /// </list>
    /// <para>Then, for swords and armor only, the enchantment block ("Active Mods:" / "Resistances:"
    /// and "Bless Type:"), followed by an optional racial line and an optional
    /// affects-player-statistics line.</para>
    ///
    /// <para>Every position the walk produces comes from <see cref="InventoryLayout"/> — the two
    /// origins plus the named column and advance distances — so this class holds no coordinates of
    /// its own and knows nothing of the original's 320x200 space. An override reflows the panel by
    /// editing that data.</para>
    ///
    /// <para>One piece of the original is deliberately NOT modelled: before drawing the frame
    /// chrome it clears VGA (22,114) 74x13 in pen 0 — the More Info button's own rect, so the
    /// button does not show through the panel that replaces it. Here the button is hidden with a
    /// USS class instead, which is the same outcome without a rect to keep in sync, so there is no
    /// ClearRect field. Recorded because the disassembly has one and a reader will look for
    /// it.</para>
    /// </summary>
    internal static class ItemStatsText {
        /// <summary>One positioned line, in the screen's own design-frame space. Labels render in
        /// the heading pen; values in the body pen.</summary>
        internal readonly struct Line {
            internal readonly string Text;
            internal readonly float X;
            internal readonly float Y;
            internal readonly bool Centred; // the original's align==1; labels/values use align==0
            internal readonly bool IsLabel;

            internal Line(string text, float x, float y, bool centred, bool isLabel) {
                Text = text; X = x; Y = y; Centred = centred; IsLabel = isLabel;
            }
        }

        /// <summary>The DDX supplying the panel's frame chrome.</summary>
        internal const int ChromeDialogId = 1800040;

        // The faithful geometry: the fallback when an override authors an origin this walk cannot
        // resolve, and the layout a null argument walks. Read-only by contract — it is one shared
        // instance, so anything that mutated it would silently move the panel for every caller.
        private static readonly InventoryLayout Defaults = new InventoryLayout();

        /// <summary>
        /// Resolve one of the walk's two origins to design-frame px.
        ///
        /// <para>The walk is a cursor: every column and advance is a px distance added to this
        /// point. A percentage is a fraction of a parent nobody here has measured — this class
        /// deliberately measures nothing, the same rule <see cref="LayoutApplier"/> follows — so
        /// summing the two would put every line somewhere neither the author nor the original
        /// asked for. Refuse loudly and walk from the faithful origin instead, which still renders
        /// a correct panel.</para>
        /// </summary>
        private static Vector2 ResolveOrigin(LayoutHint hint, LayoutHint fallback, string field) =>
            LayoutApplier.TryResolvePoint(hint, "InventoryLayout." + field,
                "the stat panel is a cursor walk whose columns and advances are design-frame px, "
                + "so a percentage origin cannot be summed with them and the panel falls back to "
                + "the original's position",
                out Vector2 point)
                ? point
                : new Vector2(fallback.Left.Value, fallback.Top.Value);

        /// <summary>
        /// Build the panel. <paramref name="affecting"/> selects the wording of the final line
        /// ("Affecting" vs "Can affect" player statistics). Returns an empty list when the item has
        /// nothing to show — the original detects that as "the cursor never moved" and skips the
        /// whole panel rather than drawing an empty frame.
        /// </summary>
        internal static IReadOnlyList<Line> Build(RuntimeItem item, ObjectInfo obj, bool affecting,
            InventoryLayout layout) {
            var lines = new List<Line>();
            if (item == null || obj == null) {
                return lines;
            }
            layout ??= Defaults;
            ObjectType type = obj.ObjectType;
            bool quarrel = item.ObjectId >= 36 && item.ObjectId <= 43;
            bool melee = type == ObjectType.Sword || type == ObjectType.Staff;

            // The cursor. Everything below is design-frame px: the origin comes from the layout
            // data and every column and advance is one of its named distances.
            Vector2 cursor = melee
                ? ResolveOrigin(layout.StatsWeaponOrigin, Defaults.StatsWeaponOrigin, "StatsWeaponOrigin")
                : ResolveOrigin(layout.StatsOrigin, Defaults.StatsOrigin, "StatsOrigin");
            float x = cursor.x;
            float y = cursor.y;

            if (melee) {
                y += type == ObjectType.Staff ? layout.StatsStaffNudge : 0f; // staves sit one row lower
                lines.Add(new Line(UiStrings.Get("base:uistring:itemstats.thrust_label"),
                    x + layout.StatsThrustColumn, y, true, true));
                lines.Add(new Line(UiStrings.Get("base:uistring:itemstats.swing_label"),
                    x + layout.StatsSwingColumn, y, true, true));
                // The underline row has no catalog entry — it is decorative punctuation, not a
                // baselined display string, so it stays a literal.
                lines.Add(new Line("________", x + layout.StatsThrustColumn,
                    y + layout.StatsUnderlineOffset, true, true));
                lines.Add(new Line("________", x + layout.StatsSwingColumn,
                    y + layout.StatsUnderlineOffset, true, true));
                y += layout.StatsHeaderHeight;
                lines.Add(new Line(UiStrings.Get("base:uistring:itemstats.base_damage_melee_label"),
                    x, y, false, true));
                lines.Add(new Line(
                    CFormat.Apply(UiStrings.Get("base:uistring:itemstats.thrust_damage_value"), obj.ThrustBaseDamage),
                    x + layout.StatsThrustColumn, y, true, false));
                lines.Add(new Line(
                    CFormat.Apply(UiStrings.Get("base:uistring:itemstats.swing_damage_value"), obj.SwingBaseDamage),
                    x + layout.StatsSwingColumn, y, true, false));
                y += layout.StatsLineAdvance;
                lines.Add(new Line(UiStrings.Get("base:uistring:itemstats.accuracy_melee_label"),
                    x, y, false, true));
                lines.Add(new Line(
                    CFormat.Apply(UiStrings.Get("base:uistring:itemstats.thrust_accuracy_value"), obj.ThrustAccuracy),
                    x + layout.StatsThrustColumn, y, true, false));
                lines.Add(new Line(
                    CFormat.Apply(UiStrings.Get("base:uistring:itemstats.swing_accuracy_value"),
                        obj.SwingAccuracy_ArmorMod_BowAccuracy),
                    x + layout.StatsSwingColumn, y, true, false));
                y += layout.StatsLineAdvance;
            } else if (type == ObjectType.Crossbow || quarrel) {
                // Each names the partner it combines with: a crossbow's damage is "+Quarrel",
                // a quarrel's is "+CrossBow". The damage row and accuracy row each embed their own
                // copy of the partner-name literal in the original (four distinct catalog keys,
                // not one reused across both rows) — see ExeStringManifest.cs.
                bool isCrossbow = type == ObjectType.Crossbow;
                string damagePartner = isCrossbow
                    ? UiStrings.Get("base:uistring:itemstats.ranged_damage_partner_quarrel")
                    : UiStrings.Get("base:uistring:itemstats.ranged_damage_partner_crossbow");
                string accuracyPartner = isCrossbow
                    ? UiStrings.Get("base:uistring:itemstats.ranged_accuracy_partner_quarrel")
                    : UiStrings.Get("base:uistring:itemstats.ranged_accuracy_partner_crossbow");
                y += layout.StatsCrossbowOffsetY;
                lines.Add(new Line(UiStrings.Get("base:uistring:itemstats.base_damage_ranged_label"),
                    x, y, false, true));
                lines.Add(new Line(
                    CFormat.Apply(UiStrings.Get("base:uistring:itemstats.ranged_damage_value"),
                        obj.SwingBaseDamage, damagePartner),
                    x + layout.StatsValueColumn, y, false, false));
                y += layout.StatsLineAdvance;
                lines.Add(new Line(UiStrings.Get("base:uistring:itemstats.accuracy_ranged_label"),
                    x, y, false, true));
                lines.Add(new Line(
                    CFormat.Apply(UiStrings.Get("base:uistring:itemstats.ranged_accuracy_value"),
                        obj.SwingAccuracy_ArmorMod_BowAccuracy, accuracyPartner),
                    x + layout.StatsValueColumn, y, false, false));
                y += layout.StatsLineAdvance;
            } else if (type == ObjectType.Armor) {
                x += layout.StatsArmorIndentX;
                y += layout.StatsArmorOffsetY;
                lines.Add(new Line(UiStrings.Get("base:uistring:itemstats.armor_mod_label"),
                    x, y, false, true));
                // No catalog entry covers the trailing "%" — the manifest declares no armor-mod
                // value-format string (see ExeStringManifest.cs), so it stays a literal.
                lines.Add(new Line(GameData.Resources.Text.UiTemplates.Percent(obj.SwingAccuracy_ArmorMod_BowAccuracy),
                    x + layout.StatsValueColumn, y, false, false));
                y += layout.StatsLineAdvance;
            }
            // Any other category contributes no combat block, and does NOT get the trailing line
            // advance either — the original jumps past it.

            // Enchantments, swords and armor only.
            if (type == ObjectType.Sword || type == ObjectType.Armor) {
                y += layout.StatsSectionGap;
                lines.Add(new Line(type == ObjectType.Sword
                        ? UiStrings.Get("base:uistring:itemstats.active_mods_label")
                        : UiStrings.Get("base:uistring:itemstats.resistances_label"),
                    x, y, false, true));
                lines.Add(new Line(ActiveMods(item), x + layout.StatsValueColumn, y, false, false));
                y += layout.StatsLineAdvance;
                lines.Add(new Line(UiStrings.Get("base:uistring:itemstats.bless_type_label"),
                    x, y, false, true));
                lines.Add(new Line(BlessType(item), x + layout.StatsValueColumn, y, false, false));
                y += layout.StatsLineAdvance;
            }

            // Gated purely on the raw mask being non-zero. "Human" is the fallback label for a
            // non-zero mask that isn't 1/2/4 — an item with NO racial mask shows no line at all.
            if (RaceMask(obj) != 0) {
                y += layout.StatsSectionGap;
                lines.Add(new Line(UiStrings.Get("base:uistring:itemstats.racial_mod_label"),
                    x, y, false, true));
                lines.Add(new Line(RaceName(obj), x + layout.StatsValueColumn, y, false, false));
                y += layout.StatsLineAdvance;
            }

            if (obj.EquipAttributeMask != 0) {
                y += layout.StatsSectionGap;
                string affectingWord = affecting
                    ? UiStrings.Get("base:uistring:itemstats.affects_stats_affecting")
                    : UiStrings.Get("base:uistring:itemstats.affects_stats_can_affect");
                lines.Add(new Line(
                    CFormat.Apply(UiStrings.Get("base:uistring:itemstats.affects_stats_format"), affectingWord),
                    x, y, false, false));
            }

            return lines;
        }

        // The item's enchantment flags, concatenated in the original's order with its spacing.
        // Faithful oddity: both Enhanced1 (0x800) and Enhanced2 (0x1000) render the same word, so an
        // item carrying both reads "EnhancedEnhanced".
        /// <summary>
        /// The mod words, or "None". The original builds this with
        /// <c>sprintf("%s%s%s%s%s%s%s", (flags &amp; 0x1f80) ? "" : "None", &lt;the six words&gt;)</c>
        /// (INVINSP.C:288) — SEVEN slots for six words, because the first is a "None"-or-empty
        /// sentinel and <c>0x1F80</c> is exactly the six mod bits. The if/else below is that
        /// sentinel expressed directly, and produces the same string.
        ///
        /// <para>So <c>itemstats.active_mods_value_format</c> stays deliberately unconsumed: it
        /// carries no translatable words — every word it joins is separately keyed — and formatting
        /// through it would only re-import the sentinel trick. Resolved RE question, task-74.</para>
        /// </summary>
        private static string ActiveMods(RuntimeItem item) {
            const ushort anyMod = (ushort)(ItemFlags.Poisoned | ItemFlags.Flaming | ItemFlags.SteelFired
                | ItemFlags.Frosted | ItemFlags.Enhanced1 | ItemFlags.Enhanced2);
            if ((item.ItemFlags & anyMod) == 0) {
                return UiStrings.Get("base:uistring:itemstats.active_mods_none");
            }
            var s = new StringBuilder();
            if ((item.ItemFlags & (ushort)ItemFlags.Poisoned) != 0) s.Append(UiStrings.Get("base:uistring:itemstats.mod_poisoned"));
            if ((item.ItemFlags & (ushort)ItemFlags.Frosted) != 0) s.Append(UiStrings.Get("base:uistring:itemstats.mod_frosted"));
            if ((item.ItemFlags & (ushort)ItemFlags.Flaming) != 0) s.Append(UiStrings.Get("base:uistring:itemstats.mod_flaming"));
            if ((item.ItemFlags & (ushort)ItemFlags.SteelFired) != 0) s.Append(UiStrings.Get("base:uistring:itemstats.mod_steelfired"));
            if ((item.ItemFlags & (ushort)ItemFlags.Enhanced1) != 0) s.Append(UiStrings.Get("base:uistring:itemstats.mod_enhanced_1"));
            if ((item.ItemFlags & (ushort)ItemFlags.Enhanced2) != 0) s.Append(UiStrings.Get("base:uistring:itemstats.mod_enhanced_2"));
            return s.ToString();
        }

        /// <summary>The bless tiers, or "None" — the same sentinel shape as
        /// <see cref="ActiveMods"/>: <c>sprintf("%s%s%s%s", (flags &amp; 0xe000) ? "" : "None", …)</c>
        /// (INVINSP.C:297), four slots for three tiers. See task-74.</summary>
        private static string BlessType(RuntimeItem item) {
            const ushort anyBless = 0xE000; // Blessed1|Blessed2|Blessed3
            if ((item.ItemFlags & anyBless) == 0) {
                return UiStrings.Get("base:uistring:itemstats.bless_type_none");
            }
            var s = new StringBuilder();
            if ((item.ItemFlags & 0x2000) != 0) s.Append(UiStrings.Get("base:uistring:itemstats.bless_tier_1"));
            if ((item.ItemFlags & 0x4000) != 0) s.Append(UiStrings.Get("base:uistring:itemstats.bless_tier_2"));
            if ((item.ItemFlags & 0x8000) != 0) s.Append(UiStrings.Get("base:uistring:itemstats.bless_tier_3"));
            return s.ToString();
        }

        // ObjectInfo.Race is the +0x38 racial mask (canassa's wRace_mask) mapped to an enum whose
        // numeric values ARE the raw mask (None=0, Tsurani=1, Elf=2, Human=3, Dwarf=4). The original
        // switches on 1/2/4 and falls back to "Human" for any other non-zero value — which is why
        // Human=3 lands on the fallback and reads correctly. Reordering the enum would silently
        // break this; RaceEnumValues_MatchTheRawMask pins it.
        private static int RaceMask(ObjectInfo obj) => (int)obj.Race;

        private static string RaceName(ObjectInfo obj) => RaceMask(obj) switch {
            1 => UiStrings.Get("base:uistring:itemstats.race_tsurani"),
            2 => UiStrings.Get("base:uistring:itemstats.race_elf"),
            4 => UiStrings.Get("base:uistring:itemstats.race_dwarf"),
            _ => UiStrings.Get("base:uistring:itemstats.race_human"),
        };
    }
}
