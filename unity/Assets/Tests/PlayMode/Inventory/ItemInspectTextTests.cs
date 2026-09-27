namespace BakAgain.Tests.PlayMode.Inventory {
    using System.Collections.Generic;
    using BakAgain.UI.Inventory;
    using GameData;
    using GameData.Resources.Inventory;
    using GameData.Resources.Object;
    using NUnit.Framework;

    /// <summary>
    /// Pins the item-inspect text against <c>UI_showItem</c> @0x5A778 (right-click an item).
    /// Each test names the address of the branch it covers.
    /// </summary>
    public class ItemInspectTextTests {
        private static ObjectInfo Obj(ObjectType type = ObjectType.Misc, int flags = 0,
            string name = "Thing", int wordWrap = 0, int shopType = 0, int equipMask = 0) =>
            new ObjectInfo("test") {
                Name = name, WordWrap = wordWrap, Flags = (ObjectFlags)flags,
                ObjectType = type, ShopType = shopType, EquipAttributeMask = (ActorAttributeFlag)equipMask,
            };

        private static RuntimeItem Item(byte objectId = 1, byte variable = 0, ushort itemFlags = 0) =>
            new RuntimeItem(objectId, variable, itemFlags);

        // ---- name split (0x5A7DE-0x5A862) ----------------------------------------------

        [Test]
        public void NameLines_SplitsAtWordWrap_DroppingTheSpace() {
            IReadOnlyList<string> lines = ItemInspectText.NameLines(
                Obj(name: "Standard Kingdom Armor", wordWrap: 8));
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual("Standard", lines[0], "name[0..wordWrap)");
            Assert.AreEqual("Kingdom Armor", lines[1], "name[wordWrap+1..] — the space is dropped");
        }

        [Test]
        public void NameLines_SingleLine_WhenWordWrapIsZero() {
            IReadOnlyList<string> lines = ItemInspectText.NameLines(Obj(name: "Broadsword"));
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("Broadsword", lines[0]);
        }

        // ---- type line (0x5A865-0x5A8E1), first match wins ------------------------------

        [Test]
        public void TypeLine_Amount_ComesFromTheUnnamed0x8000_NotStackable() {
            // The trap: IDA's Stackable is 0x800, but the Amount gate tests 0x8000.
            Assert.AreEqual("Amount: 12",
                ItemInspectText.TypeLine(Item(variable: 12), Obj(flags: 0x8000)));
            Assert.IsNull(ItemInspectText.TypeLine(Item(variable: 12), Obj(flags: 0x800)),
                "Stackable (0x800) alone must NOT produce an Amount line");
        }

        [Test]
        public void TypeLine_Amount_AlsoForKeyObjectType() {
            Assert.AreEqual("Amount: 3",
                ItemInspectText.TypeLine(Item(variable: 3), Obj(ObjectType.Key)));
        }

        [Test]
        public void TypeLine_UsesLeft_ForLimitedUses_ButNotBooks() {
            Assert.AreEqual("Uses left: 5",
                ItemInspectText.TypeLine(Item(variable: 5), Obj(flags: 0x2000)));
            Assert.IsNull(ItemInspectText.TypeLine(Item(variable: 5), Obj(ObjectType.Book, flags: 0x2000)),
                "Books are excluded from the Uses-left line");
        }

        [Test]
        public void TypeLine_ValueRating_ForDegradableJewelry() {
            Assert.AreEqual("Value Rating: 80%",
                ItemInspectText.TypeLine(Item(variable: 80), Obj(flags: 0x1000, shopType: 0x4)));
        }

        /// <summary>The fourth branch, missing from the original work-todo #17 spec — and the
        /// common case, since weapons and armor are degradable non-jewelry.</summary>
        [Test]
        public void TypeLine_Condition_ForDegradableNonJewelry() {
            Assert.AreEqual("Condition: 98%",
                ItemInspectText.TypeLine(Item(variable: 98), Obj(ObjectType.Sword, flags: 0x1000)));
        }

        [Test]
        public void TypeLine_Null_WhenNoGateMatches() {
            Assert.IsNull(ItemInspectText.TypeLine(Item(variable: 6), Obj(ObjectType.Food)),
                "rations have no type line");
        }

        [Test]
        public void TypeLine_FirstMatchWins_AmountBeatsCondition() {
            Assert.AreEqual("Amount: 7",
                ItemInspectText.TypeLine(Item(variable: 7), Obj(ObjectType.Sword, flags: 0x8000 | 0x1000)));
        }

        // ---- status line (0x5A905-0x5A977) ---------------------------------------------

        [Test]
        public void StatusLine_Using_WhenEquippedAndAffecting() {
            Assert.AreEqual("Using",
                ItemInspectText.StatusLine(Item(itemFlags: 0x40), Obj(), affecting: true));
        }

        [Test]
        public void StatusLine_Using_AlsoWhenItemAffectsAttributes() {
            Assert.AreEqual("Using",
                ItemInspectText.StatusLine(Item(), Obj(equipMask: 0x2), affecting: true));
        }

        [Test]
        public void StatusLine_NoUsing_WhenNotAffecting() {
            Assert.AreEqual(string.Empty,
                ItemInspectText.StatusLine(Item(itemFlags: 0x40), Obj(), affecting: false),
                "loot/shop views pass affecting=false, so no Using prefix");
        }

        [Test]
        public void StatusLine_AppendsBrokenOrRepairable_WithSeparator() {
            Assert.AreEqual("Using, Broken",
                ItemInspectText.StatusLine(Item(itemFlags: 0x40 | 0x10), Obj(), affecting: true));
            Assert.AreEqual("Using, Repairable",
                ItemInspectText.StatusLine(Item(itemFlags: 0x40 | 0x20), Obj(), affecting: true));
        }

        [Test]
        public void StatusLine_BrokenWins_OverRepairable() {
            Assert.AreEqual("Using, Broken",
                ItemInspectText.StatusLine(Item(itemFlags: 0x40 | 0x10 | 0x20), Obj(), affecting: true));
        }

        /// <summary>Faithful oddity: the ", " separator is appended on the broken/repairable test
        /// alone, so a carried (non-Using) broken item's line really does start with ", ".</summary>
        [Test]
        public void StatusLine_LeadingSeparator_WhenBrokenWithoutUsing() {
            Assert.AreEqual(", Broken",
                ItemInspectText.StatusLine(Item(itemFlags: 0x10), Obj(), affecting: false));
        }

        // ---- description lookup (0x5A9A2-0x5A9DA) ---------------------------------------

        [Test]
        public void DescriptionLookup_ObjectDialog_KeyedByObjectId() {
            (int id, int global) = ItemInspectText.DescriptionLookup(Item(objectId: 18), Obj(ObjectType.Sword));
            Assert.AreEqual(1800001, id, "the per-object description dialog");
            Assert.AreEqual(18, global, "global 30000 = object id");
        }

        [Test]
        public void DescriptionLookup_ScrollDialog_KeyedBySpellVariable() {
            (int id, int global) = ItemInspectText.DescriptionLookup(
                Item(objectId: 90, variable: 7), Obj(ObjectType.MagicalScroll));
            Assert.AreEqual(1800033, id, "spell scrolls use their own dialog");
            Assert.AreEqual(7, global, "global 30000 = the scroll's spell, not the object id");
        }

        // ---- More Info gate (0x5A9F7-0x5AA1F) -------------------------------------------

        [Test]
        public void HasMoreInfo_ForWeaponsAndArmor_AndQuarrels_AndAttributeItems() {
            Assert.IsTrue(ItemInspectText.HasMoreInfo(Item(), Obj(ObjectType.Sword)), "objectType <= Armor");
            Assert.IsTrue(ItemInspectText.HasMoreInfo(Item(), Obj(ObjectType.Armor)), "Armor is the boundary");
            Assert.IsTrue(ItemInspectText.HasMoreInfo(Item(objectId: 36), Obj(ObjectType.Food)), "quarrel id 36");
            Assert.IsTrue(ItemInspectText.HasMoreInfo(Item(objectId: 43), Obj(ObjectType.Food)), "quarrel id 43");
            Assert.IsTrue(ItemInspectText.HasMoreInfo(Item(objectId: 90), Obj(ObjectType.Potion, equipMask: 0x4)),
                "affectedAttributes != 0");
        }

        [Test]
        public void HasMoreInfo_False_ForPlainItems() {
            Assert.IsFalse(ItemInspectText.HasMoreInfo(Item(objectId: 72), Obj(ObjectType.Food)),
                "rations: not a weapon/armor, not a quarrel id, no attribute effect");
            Assert.IsFalse(ItemInspectText.HasMoreInfo(Item(objectId: 44), Obj(ObjectType.Food)),
                "id 44 is just past the quarrel range");
        }

        [Test]
        public void HasMoreInfo_False_ForPicklocks() {
            // Reported from play 2026-08-16: picklocks offered More Info and the original does not.
            // ObjectInfo 80 is ObjectType.Misc with no attribute effect.
            Assert.IsFalse(ItemInspectText.HasMoreInfo(Item(objectId: 80), Obj(ObjectType.Misc)),
                "picklocks: Misc, not a quarrel, no attribute effect");
        }

        [Test]
        public void HasMoreInfo_MiscIsAGuardNotAnAccept() {
            // THE BUG THIS PINS. Misc = 0 and Armor = 4, so `Misc <= Armor` is TRUE — which is why
            // the original tests `== Misc` FIRST and jumps PAST the accept (0x5a9fa/0x5a9ff).
            // Written as `Misc || <= Armor` the guard becomes an accept and every Misc item shows a
            // More Info button with nothing behind it.
            Assert.IsFalse(ItemInspectText.HasMoreInfo(Item(objectId: 90), Obj(ObjectType.Misc)),
                "a plain Misc item falls through to the quarrel and attribute tests, and fails both");

            // ...but Misc still reaches those tests, rather than being rejected outright.
            Assert.IsTrue(ItemInspectText.HasMoreInfo(Item(objectId: 90), Obj(ObjectType.Misc, equipMask: 0x4)),
                "a Misc item that DOES affect attributes still qualifies");
            Assert.IsTrue(ItemInspectText.HasMoreInfo(Item(objectId: 40), Obj(ObjectType.Misc)),
                "a Misc item in the quarrel id range still qualifies");
        }
    }
}
