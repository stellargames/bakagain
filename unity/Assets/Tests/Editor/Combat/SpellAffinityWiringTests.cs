namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using GameData.Resources.Combat;
    using GameData.Resources.Spells;
    using NUnit.Framework;

    /// <summary>
    /// SPELLWEA / SPELLRES reaching the cast path — TASK-115.
    /// </summary>
    /// <remarks>
    /// <b>The tables were fully extracted and nothing loaded them</b>, so the doubling was passed
    /// as <c>false</c> unconditionally and no creature ever took double from the spell it is weak
    /// to.
    /// </remarks>
    public class SpellAffinityWiringTests {
        // Winds of Eortis (27) doubles against creature 19, Brak Nurr — a real pair from SPELLWEA.DAT
        // (row 19, bit 27: a row is the creature and a bit the spell, TASK-541).
        private const int WeakSpell = 27;
        private const int WeakCreature = 19;

        private static SpellAffinityTable TableListing(int spellNumber, int creatureType) {
            var table = new SpellAffinityTable("test");
            for (var i = 0; i <= spellNumber; i++) {
                table.Spells.Add(new SpellAffinity { SpellNumber = i });
            }
            table.Spells[spellNumber].CreatureTypes.Add(creatureType);
            return table;
        }

        private static Combatant Creature(int classId) => new Combatant {
            PartySlot = 0, ClassId = classId, Health = 20, Stamina = 20,
        };

        [Test]
        public void AListedCreatureIsVulnerableToThatSpellAndNoOther() {
            var runtime = new CombatRuntime(null) {
                SpellWeakness = TableListing(WeakSpell, WeakCreature),
            };

            Assert.IsTrue(runtime.TargetIsVulnerableTo(Creature(WeakCreature), WeakSpell));
            Assert.IsFalse(runtime.TargetIsVulnerableTo(Creature(WeakCreature), WeakSpell + 1),
                "the pairing is per spell, not per creature");
            Assert.IsFalse(runtime.TargetIsVulnerableTo(Creature(WeakCreature + 1), WeakSpell),
                "and per creature, not per spell");
        }

        [Test]
        public void APartyMemberCannotTriggerEitherTable() {
            // A runtime with no party entries falls back to ClassId, the character index, and row 0
            // carries no bit in either table. With the save's entries the lookup uses the party's real
            // creature types, 15-17, whose rows DO resist Evil Seek (TASK-542).
            var runtime = new CombatRuntime(null) {
                SpellWeakness = TableListing(WeakSpell, WeakCreature),
                SpellResistance = TableListing(WeakSpell, WeakCreature),
            };
            var partyMember = new Combatant { PartySlot = 1, ClassId = 0, Health = 20 };

            Assert.IsFalse(runtime.TargetIsVulnerableTo(partyMember, WeakSpell));
            Assert.IsFalse(runtime.TargetResists(partyMember, WeakSpell));
        }

        [Test]
        public void NoTableMeansNoDoubling_WhichIsTheOldBehaviourNotANeutralDefault() {
            // Leaving the property unset must be inert rather than throwing — but it is also
            // exactly the bug this task closes, which is why HotspotService sets it in one factory.
            var runtime = new CombatRuntime(null);
            Assert.IsFalse(runtime.TargetIsVulnerableTo(Creature(WeakCreature), WeakSpell));
            Assert.IsFalse(runtime.TargetResists(Creature(WeakCreature), WeakSpell));
        }

        [Test]
        public void VulnerabilityDoublesTheCostAndNotTheMagnitude() {
            // The check is on the (creature, spell) pair and knows nothing about the sign, so a
            // creature listed as vulnerable also receives double from a spell aimed at it to help.
            Assert.AreEqual(20,
                SpellCostModifiers.Effective(10, surcharged: false, targetIsWeak: true));
            Assert.AreEqual(10,
                SpellCostModifiers.Effective(-5, surcharged: false, targetIsWeak: true),
                "the sign is stripped first, so a heal's cost doubles all the same");
        }

        [Test]
        public void DoublingTheCostIsNotTheSameAsDoublingTheDamage() {
            // *** THE WHOLE REASON THE DOUBLING HAS TO SIT WHERE IT SITS. *** A flat-magnitude
            // spell ignores its cost entirely, so doubling the cost changes nothing and doubling
            // the result changes everything. ResolveCast used to do the latter.
            var flat = new Spell("S") {
                TargetingType = 1, Calculation = GameData.SpellCalculation.FixedAmount, Damage = 7,
            };
            int plain = SpellEffectMagnitude.Calculate(flat, WeakSpell, 10,
                targetHasMetalGear: false);
            int doubledCost = SpellEffectMagnitude.Calculate(flat, WeakSpell,
                SpellCostModifiers.Effective(10, surcharged: false, targetIsWeak: true),
                targetHasMetalGear: false);

            Assert.AreEqual(plain, doubledCost,
                "a flat-magnitude spell does not scale with its cost, so the doubling is inert");
            Assert.AreNotEqual(plain * 2, doubledCost,
                "doubling the computed magnitude instead would have doubled it");
        }
    
        // *** THE CALL SITE ITSELF IS NOT COVERED, AND THAT IS WORTH SAYING OUT LOUD. ***
        // The tests above exercise TargetIsVulnerableTo directly, so reverting ResolveCast to
        // `targetIsVulnerable: false` would sail through all of them — the same gap a clearance
        // test in this suite had before it was rewritten.
        //
        // An end-to-end version was written and removed: ResolveCast cannot land a hit on a bare
        // runtime, because the accuracy it rolls against comes from StatsFor(caster), which needs a
        // GameSession. It measured 0 damage in both the plain and the doubled case and so proved
        // nothing. Covering it properly means the live-fight route (editor_play, DebugStart, then a
        // real encounter) rather than a unit test — see reference_driving_a_live_fight_in_editor.
}
}
