namespace BakAgain.Tests.PlayMode.Inventory {
    using BakAgain.UI.Inventory;
    using GameData.Resources.Inventory;
    using GameData.Resources.Object;
    using NUnit.Framework;

    /// <summary>
    /// The use-gate chain that runs before any item dispatch — <c>itemuse_dispatch_on_target</c>
    /// (ITEMUSE.C:104-131, <c>Use_Item</c> @0x58cbd).
    ///
    /// <para>Two things are worth pinning independently: that each gate maps to the right refusal
    /// record, and that they are tested in the original's <b>order</b>. Order is observable — an
    /// out-of-charges combat-only item held by the wrong member reports the member problem, not the
    /// charges — so a chain that happened to check exhaustion first would pass a per-gate test suite
    /// and still be wrong.</para>
    /// </summary>
    public class UseGateChainTests {
        private const int NeedsCaster = 1800005;    // 0x1B7745
        private const int RefusesCaster = 1800049;  // 0x1B7771
        private const int CombatOnly = 1800006;     // 0x1B7746
        private const int NotInCombat = 1800007;    // 0x1B7747
        private const int UsedUp = 1800044;         // 0x1B776C

        private static ObjectInfo Obj(ObjectFlags flags) => new ObjectInfo("test") { Flags = flags };

        // Variable doubles as condition/charges; 1 = has a charge left, 0 = spent.
        private static RuntimeItem Item(byte objectId = 90, byte variable = 1, ushort itemFlags = 0) =>
            new RuntimeItem(objectId, variable, itemFlags);

        [Test]
        public void NoGatingFlags_IsNotRefused() {
            Assert.IsFalse(InventoryMenu.RefuseUse(Obj(0), Item(), isCaster: false, inCombat: false,
                out _, out _));
        }

        // flag 0x80 with stats[7].max == 0 (no casting skill).
        [Test]
        public void SpellcastersOnly_RefusesANonCaster_AndPassesACaster() {
            Assert.IsTrue(InventoryMenu.RefuseUse(Obj(ObjectFlags.SpellcastersOnly), Item(),
                isCaster: false, inCombat: false, out int dialogId, out int var0));
            Assert.AreEqual(NeedsCaster, dialogId);
            Assert.AreEqual(0, var0);

            Assert.IsFalse(InventoryMenu.RefuseUse(Obj(ObjectFlags.SpellcastersOnly), Item(),
                isCaster: true, inCombat: false, out _, out _));
        }

        // flag 0x200: the mirror gate. Was mis-named ArchersOnly — the predicate is casting skill.
        [Test]
        public void NonSpellcastersOnly_RefusesACaster_AndPassesANonCaster() {
            Assert.IsTrue(InventoryMenu.RefuseUse(Obj(ObjectFlags.NonSpellcastersOnly), Item(),
                isCaster: true, inCombat: false, out int dialogId, out _));
            Assert.AreEqual(RefusesCaster, dialogId);

            Assert.IsFalse(InventoryMenu.RefuseUse(Obj(ObjectFlags.NonSpellcastersOnly), Item(),
                isCaster: false, inCombat: false, out _, out _));
        }

        // No combat mode exists yet, so this gate refuses in every reachable state today — which is
        // correct for a combat-only item, and the reason it is written against a flag.
        [Test]
        public void OnlyUsableInCombat_RefusesOutOfCombat_AndPassesInCombat() {
            Assert.IsTrue(InventoryMenu.RefuseUse(Obj(ObjectFlags.OnlyUsableInCombat), Item(),
                isCaster: false, inCombat: false, out int dialogId, out _));
            Assert.AreEqual(CombatOnly, dialogId);

            Assert.IsFalse(InventoryMenu.RefuseUse(Obj(ObjectFlags.OnlyUsableInCombat), Item(),
                isCaster: false, inCombat: true, out _, out _));
        }

        [Test]
        public void NotUsableInCombat_RefusesInCombatOnly() {
            Assert.IsTrue(InventoryMenu.RefuseUse(Obj(ObjectFlags.NotUsableInCombat), Item(),
                isCaster: false, inCombat: true, out int dialogId, out _));
            Assert.AreEqual(NotInCombat, dialogId);

            Assert.IsFalse(InventoryMenu.RefuseUse(Obj(ObjectFlags.NotUsableInCombat), Item(),
                isCaster: false, inCombat: false, out _, out _));
        }

        /// <summary>
        /// Exhaustion, and the Var 0 that picks the wording: 1 when the item is equipped or can never
        /// be equipped, else 0. A charge remaining is not exhausted at all.
        /// </summary>
        [Test]
        public void LimitedUses_RefusesOnlyWhenSpent_AndPicksTheWording() {
            Assert.IsFalse(InventoryMenu.RefuseUse(Obj(ObjectFlags.LimitedUses), Item(variable: 1),
                isCaster: false, inCombat: false, out _, out _), "a charge left is usable");

            Assert.IsTrue(InventoryMenu.RefuseUse(Obj(ObjectFlags.LimitedUses), Item(variable: 0),
                isCaster: false, inCombat: false, out int dialogId, out int var0));
            Assert.AreEqual(UsedUp, dialogId);
            Assert.AreEqual(0, var0, "a spare, unworn consumable -> leaf 0");

            InventoryMenu.RefuseUse(Obj(ObjectFlags.LimitedUses), Item(variable: 0, itemFlags: 0x0040),
                isCaster: false, inCombat: false, out _, out int equippedVar0);
            Assert.AreEqual(1, equippedVar0, "the equipped bit (0x40) takes leaf 1");

            InventoryMenu.RefuseUse(Obj(ObjectFlags.LimitedUses | ObjectFlags.Protected),
                Item(variable: 0), isCaster: false, inCombat: false, out _, out int protectedVar0);
            Assert.AreEqual(1, protectedVar0, "so does a plot-critical item, worn or not");
        }

        // Object 1 at zero condition has its own leaf, and needs no LimitedUses flag to reach it.
        [Test]
        public void ObjectOne_AtZeroCondition_TakesItsOwnLeaf() {
            Assert.IsTrue(InventoryMenu.RefuseUse(Obj(0), Item(objectId: 1, variable: 0),
                isCaster: false, inCombat: false, out int dialogId, out int var0));
            Assert.AreEqual(UsedUp, dialogId);
            Assert.AreEqual(2, var0);

            Assert.IsFalse(InventoryMenu.RefuseUse(Obj(0), Item(objectId: 1, variable: 1),
                isCaster: false, inCombat: false, out _, out _));
        }

        /// <summary>
        /// The ordering pin. An item that trips several gates at once must report the earliest one:
        /// caster before combat, combat before exhaustion. Without this, a chain reordered into
        /// "cheapest check first" would still pass every test above.
        /// </summary>
        [Test]
        public void EarliestFailingGateWins() {
            // Caster gate + combat-only + spent, held by a non-caster out of combat.
            ObjectInfo all = Obj(ObjectFlags.SpellcastersOnly | ObjectFlags.OnlyUsableInCombat
                | ObjectFlags.LimitedUses);
            Assert.IsTrue(InventoryMenu.RefuseUse(all, Item(variable: 0), isCaster: false,
                inCombat: false, out int dialogId, out _));
            Assert.AreEqual(NeedsCaster, dialogId, "the caster gate is tested first");

            // Satisfy the caster gate: the combat gate is next, still ahead of exhaustion.
            Assert.IsTrue(InventoryMenu.RefuseUse(all, Item(variable: 0), isCaster: true,
                inCombat: false, out dialogId, out _));
            Assert.AreEqual(CombatOnly, dialogId, "combat mode is tested before charges");

            // Satisfy both: only then does exhaustion surface.
            Assert.IsTrue(InventoryMenu.RefuseUse(all, Item(variable: 0), isCaster: true,
                inCombat: true, out dialogId, out _));
            Assert.AreEqual(UsedUp, dialogId);
        }
    }
}
