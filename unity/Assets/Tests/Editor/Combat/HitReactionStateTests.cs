namespace BakAgain.Tests.Editor.Combat {
    using GameData.Resources.Combat;
    using NUnit.Framework;

    /// <summary>
    /// Who flinches, who stops flinching, and how long it lasts.
    /// </summary>
    /// <remarks>
    /// <b>The rule was modelled long before anything set it.</b> <c>HitReaction</c> and
    /// <c>CombatantFlags.Knockback</c> both existed with zero non-test callers, which is the
    /// implemented-but-unreachable shape this project keeps finding.
    ///
    /// <para><b>Nothing DRAWS a recoil yet.</b> The tint is the sprite redrawn through a zone RMP
    /// block and is TASK-103's next slice; this is the state that will feed it.</para>
    /// </remarks>
    public class HitReactionStateTests {
        private static Combatant Struck() {
            var c = new Combatant();
            (CombatantFlags flags, int timer, int remap) =
                HitReaction.Begin(c.Flags, HitReaction.BlowRemap);
            c.Flags = flags;
            c.HitReactionTimer = timer;
            c.HitReactionRemap = remap;
            return c;
        }

        [Test]
        public void ABlowFlinchesThroughRampRungOne() {
            // *** READ OFF THE CALL, NOT CHOSEN. *** resolveSwingAttack damages its target with
            // hitDir = 1 and bills its attacker with hitDir = 0, so a blow is rung 1 of the zone's
            // fade ramp. A port that picked a number here would be inventing the colour.
            Combatant hit = Struck();

            Assert.AreEqual(1, HitReaction.BlowRemap);
            Assert.AreEqual(1, hit.HitReactionRemap);
            // *** CAST. *** `flags & Knockback` is a CombatantFlags, and NUnit comparing an enum to
            // the int 0 never matches — so the un-cast form of this assertion passed no matter what
            // the flag was, and its sibling below failed with "But was: None" and caught it.
            Assert.AreNotEqual(0, (int)(hit.Flags & CombatantFlags.Knockback));
            Assert.AreEqual(HitReaction.Ticks, hit.HitReactionTimer);
        }

        [Test]
        public void TheRecoilLastsExactlyTwoRedrawsAndThenClears() {
            Combatant hit = Struck();

            (CombatantFlags flags, int timer) = HitReaction.Tick(hit.Flags, hit.HitReactionTimer);
            Assert.AreNotEqual(0, (int)(flags & CombatantFlags.Knockback), "still recoiling after one");

            (flags, timer) = HitReaction.Tick(flags, timer);
            Assert.AreEqual(0, (int)(flags & CombatantFlags.Knockback), "cleared after two");
            Assert.AreEqual(0, timer);
        }

        [Test]
        public void NotEveryEffectFlashesTheSameRung() {
            // *** THE INDEX IS PER-EFFECT, NOT "STRUCK". *** Swept six of the eight markActorHit
            // call sites: a blow, the generic spell hit flash, the touch-slay and the strength drain
            // all pass 1, a storm passes 3, and a HEAL passes 4. Pinning the three apart is what
            // stops a later change collapsing them to one "hit colour" — which would tint a mending
            // exactly like a wounding.
            Assert.AreEqual(1, HitReaction.BlowRemap);
            Assert.AreEqual(3, HitReaction.StormRemap);
            Assert.AreEqual(4, HitReaction.HealRemap);
            Assert.AreNotEqual(HitReaction.BlowRemap, HitReaction.HealRemap);
        }

        [Test]
        public void TickingAnUnstruckCombatantDoesNothing() {
            // The sweep runs over every combatant every redraw, so the no-op case is the common one
            // and must not decrement a timer that was never set.
            var calm = new Combatant { HitReactionTimer = 5 };

            (CombatantFlags flags, int timer) = HitReaction.Tick(calm.Flags, calm.HitReactionTimer);

            Assert.AreEqual(calm.Flags, flags);
            Assert.AreEqual(5, timer, "an unset flag leaves the timer alone");
        }
    }
}
