namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using GameData.Resources.Combat;
    using NUnit.Framework;

    /// <summary>
    /// The resolver plays EITHER side, which is what makes auto-resolve possible.
    /// </summary>
    /// <remarks>
    /// <b>The original does this by swapping a global.</b> <c>combat_arena_swap_tgt_state</c> re-aims
    /// one pair of actor lists so the same AI routine plays the party — which is why an
    /// auto-resolved party fights exactly like a monster would
    /// (<see cref="AutoResolveLoop.PartyTurnsUseTheMonsterAiWithSidesSwapped"/>). Deriving the sides
    /// from the acting combatant gets the same behaviour with no global to swap back.
    ///
    /// <para>Before this the selectors took <c>encounter.Party</c> as their candidates outright,
    /// which was correct only because the sole caller passed a monster. A party member would have
    /// hunted their own line.</para>
    /// </remarks>
    public class MonsterTurnResolverSideTests {
        private static Combatant Actor(int slot, int x, int y) => new Combatant {
            PartySlot = slot,
            ClassId = slot == 0 ? 1 : slot - 1,
            Health = 20,
            Stamina = 20,
            X = x,
            Y = y,
            Flags = CombatantFlags.Ready,
        };

        private static CombatEncounter TwoSided() {
            var fight = new CombatEncounter();
            // Two tiles apart: inside every search radius, so the selectors have something to
            // choose and a null answer means a SIDE problem rather than an out-of-range one.
            fight.Party.Add(Actor(1, 3, 5));
            fight.Party.Add(Actor(2, 4, 5));
            fight.Enemies.Add(Actor(0, 3, 7));
            fight.Enemies.Add(Actor(0, 4, 7));
            return fight;
        }

        private static MonsterTurnResolver Resolver() =>
            new MonsterTurnResolver(
                _ => new MonsterTurnResolver.Profile(0, 100, false, false),
                n => 0, null, isUnderground: false);

        [Test]
        public void AMonsterTargetsTheParty() {
            CombatEncounter fight = TwoSided();
            MonsterTurnResolver.Decision d = Resolver().Resolve(fight, fight.Enemies[0]);

            Assert.IsNotNull(d.Target);
            Assert.IsTrue(d.Target.IsPartyMember, "a monster hunts the party");
        }

        [Test]
        public void APartyMemberTargetsTheENEMIES_notTheirOwnLine() {
            // *** THE LATENT BUG THIS CLOSES. *** With the candidate list hardcoded to
            // encounter.Party, running the AI for a party member — which is exactly what
            // auto-resolve does — had them picking a target from their own side.
            CombatEncounter fight = TwoSided();
            MonsterTurnResolver.Decision d = Resolver().Resolve(fight, fight.Party[0]);

            Assert.IsNotNull(d.Target);
            Assert.IsFalse(d.Target.IsPartyMember, "the party's AI turn hunts the enemies");
        }

        [Test]
        public void NeitherSideEverTargetsItself() {
            CombatEncounter fight = TwoSided();
            MonsterTurnResolver resolver = Resolver();

            foreach (Combatant actor in fight.AllCombatants()) {
                MonsterTurnResolver.Decision d = resolver.Resolve(fight, actor);
                if (d.Target != null) {
                    Assert.AreNotEqual(actor.IsPartyMember, d.Target.IsPartyMember,
                        $"an actor on slot {actor.PartySlot} chose its own side");
                }
            }
        }
    }
}
