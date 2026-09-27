namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using NUnit.Framework;

    /// <summary>
    /// Spell status effects reaching a party member's modifier table — TASK-251's spell call site.
    /// </summary>
    /// <remarks>
    /// <c>cspell_try_add_status_effect</c> (CSPELL.C:1053). Three rules, each of which reads
    /// backwards from the obvious guess, so each is asserted rather than assumed.
    /// </remarks>
    public class GameSessionSpellStatusTests {
        private const int Roster = 2;
        private static readonly int MeleeMask = 1 << (int)ActorAttribute.AccuracyMelee;

        private static GameSession Session() => new GameSession { GameTimeIn2Seconds = 500 };

        private static ActorStatModifiers.Slot SlotOf(GameSession s, int slot) =>
            s.StatModifiers[ActorStatModifiers.IndexOf(Roster, slot)];

        [Test]
        public void ASpellStatusIsCombatOnlyAndCarriesNoExpiresBit() {
            // The routine writes flags 0x100 flat: CombatOnly, cost 0, and NO Expires. It stores
            // game_time << 1 in the expiry field, which is not a duration and which nothing reads —
            // spell statuses come off by clear-by-mask. Reading that field as a lifetime invents one.
            GameSession s = Session();

            Assert.IsTrue(s.AddSpellStatusEffect(Roster, ActorAttribute.AccuracyMelee, -20,
                inCombat: true));

            ActorStatModifiers.Slot slot = SlotOf(s, 0);
            Assert.AreEqual(ActorStatModifiers.SpellStatusFlags, slot.Flags);
            Assert.AreEqual(0, slot.Flags & (int)ActorStatModifiers.ModifierFlags.Expires);
            Assert.AreEqual(0, ActorStatModifiers.CostOf(slot.Flags), "cost 0 — evicted first");
            Assert.AreEqual(-20, slot.Value);
            Assert.AreEqual(MeleeMask, slot.StatMask);
            Assert.AreEqual(1000u, slot.ExpiresAt, "game_time << 1, and it is not a duration");
        }

        [Test]
        public void TwoCastsOfTheSameDebuffSTACK_ButAnItemsModifierShutsItOut() {
            // *** THE DEDUPE RULE IS ASYMMETRIC AND THIS IS THE HALF THAT SURPRISES. *** The block
            // test is `flags != 0 && mask matches && flags != 0x100`, so another SPELL status does
            // not block; anything else does.
            GameSession s = Session();
            Assert.IsTrue(s.AddSpellStatusEffect(Roster, ActorAttribute.AccuracyMelee, -20, true));
            Assert.IsTrue(s.AddSpellStatusEffect(Roster, ActorAttribute.AccuracyMelee, -20, true),
                "a second cast of the same debuff stacks");
            Assert.IsFalse(SlotOf(s, 1).IsEmpty);

            // Now an ITEM modifier on that stat — anything without the 0x100 flag — refuses it.
            GameSession other = Session();
            other.StatModifiers[ActorStatModifiers.IndexOf(Roster, 0)] =
                new ActorStatModifiers.Slot(0x200, MeleeMask, 5, 0, 9999);
            Assert.IsFalse(other.AddSpellStatusEffect(Roster, ActorAttribute.AccuracyMelee, -20, true));
        }

        [Test]
        public void InsertionSweepsSlotsForSTATSNOBODYASKEDABOUT() {
            // The sweep is the whole reason dead slots do not accumulate: a read only ever expires
            // the slot it matched, so a lapsed modifier on an unread stat would linger for ever and
            // eventually evict a live one. Here a lapsed CROSSBOW slot is freed by a MELEE insert.
            GameSession s = Session();
            int lapsed = (int)ActorStatModifiers.ModifierFlags.Expires;
            for (var i = 0; i < ActorStatModifiers.SlotsPerCharacter; i++) {
                s.StatModifiers[ActorStatModifiers.IndexOf(Roster, i)] =
                    new ActorStatModifiers.Slot(lapsed, 1 << (int)ActorAttribute.AccuracyCrossbow,
                        -5, 0, 10);
            }

            // All eight are full and none is on the melee stat, so without the sweep this could only
            // evict — and the lapsed entries would still be there.
            Assert.IsTrue(s.AddSpellStatusEffect(Roster, ActorAttribute.AccuracyMelee, -20, true));

            var live = 0;
            for (var i = 0; i < ActorStatModifiers.SlotsPerCharacter; i++) {
                if (!SlotOf(s, i).IsEmpty) {
                    live++;
                }
            }
            Assert.AreEqual(1, live, "the eight lapsed slots were swept and only the new one stands");
        }

        [Test]
        public void TheRosterIndexIsNotThePartyPosition() {
            // The table is six characters wide and addressed by ROSTER index. A combatant's
            // PartySlot is its position in the ACTIVE party, and the two coincide only while the
            // party is the first three characters in order — so writing by PartySlot would debuff
            // the wrong character on any other party.
            GameSession s = Session();
            Assert.IsTrue(s.AddSpellStatusEffect(5, ActorAttribute.AccuracyMelee, -20, true));

            Assert.IsFalse(s.StatModifiers[ActorStatModifiers.IndexOf(5, 0)].IsEmpty);
            Assert.IsTrue(s.StatModifiers[ActorStatModifiers.IndexOf(0, 0)].IsEmpty);
            Assert.IsFalse(s.AddSpellStatusEffect(ActorStatModifiers.Characters, 
                ActorAttribute.AccuracyMelee, -20, true), "and a roster index off the end is refused");
        }
    }
}
