namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using GameData.Resources.Data;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// A fallen Nighthawk getting back up as a Black Slayer.
    /// </summary>
    public class SlayerRevivalRuntimeTests {
        private static ActorStat[] StatBlock() {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 20, Max = 99 };
            }
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = 40, Max = 60 };
            stats[(int)ActorAttribute.Stamina] = new ActorStat { Base = 20, Max = 30 };
            stats[(int)ActorAttribute.Speed] = new ActorStat { Base = 5, Max = 99 };
            return stats;
        }

        private static PartyCombatEntries Entries() =>
            new PartyCombatEntries("P1.DAT", new List<SaveGameCombatData> {
                new SaveGameCombatData(0, 0, 3, 4, 0xff, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            });

        // Two enemies: one already a risen Black Slayer (the sweep needs one present), one the
        // transforming Nighthawk that will fall and come back.
        private static (CombatRuntime Runtime, CombatEncounter Fight) Fight() {
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock());
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(400, StatBlock(), SlayerRevival.RisenType, gridX: 2, gridY: 8);
            session.SetRosterActorForTest(401, StatBlock(), SlayerRevival.TransformingType,
                gridX: 4, gridY: 8);
            var runtime = new CombatRuntime(session, null, Entries(), null);
            return (runtime, runtime.EnterRoster(new short[] { 400, 401 }));
        }

        // The same fight with the sfx seam captured — the fifth constructor argument is the one
        // HotspotService passes MenuSoundService.Play through in production.
        private static (CombatRuntime Runtime, CombatEncounter Fight, System.Collections.Generic.List<int> Cues)
            FightWithSound() {
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock());
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(400, StatBlock(), SlayerRevival.RisenType, gridX: 2, gridY: 8);
            session.SetRosterActorForTest(401, StatBlock(), SlayerRevival.TransformingType,
                gridX: 4, gridY: 8);
            var cues = new System.Collections.Generic.List<int>();
            var runtime = new CombatRuntime(session, null, Entries(), null, cues.Add);
            return (runtime, runtime.EnterRoster(new short[] { 400, 401 }), cues);
        }

        private static Combatant Nighthawk(CombatEncounter fight) {
            foreach (Combatant c in fight.Enemies) {
                if (c.ClassId == SlayerRevival.TransformingType) {
                    return c;
                }
            }
            return null;
        }

        [Test]
        public void TheCountdownRunsDown_andTheRiseCHANGESTheSpecies() {
            var (runtime, fight) = Fight();
            Combatant hawk = Nighthawk(fight);
            hawk.RevivalCountdown = 1;
            hawk.Health = 0;
            hawk.Stamina = 0;
            hawk.Flags = CombatantFlags.Dead;

            Assert.AreEqual(0, runtime.TickSlayerRevivals(), "one tick short");
            Assert.AreEqual(0, hawk.RevivalCountdown);

            Assert.AreEqual(1, runtime.TickSlayerRevivals(), "and now it rises");
            Assert.AreEqual(SlayerRevival.RisenType, hawk.ClassId,
                "what gets up is not what went down");
        }

        [Test]
        public void ItRisesAtFULLStrengthAndTheFlagsAreASSIGNED() {
            // *** OR-ing Ready would leave the Dead bit set *** — a fully-healed corpse that never
            // acts and can never be killed again.
            var (runtime, fight) = Fight();
            Combatant hawk = Nighthawk(fight);
            hawk.RevivalCountdown = 0;
            hawk.Health = 0;
            hawk.Stamina = 0;
            hawk.Flags = CombatantFlags.Dead | CombatantFlags.Poisoned;

            runtime.TickSlayerRevivals();

            Assert.AreEqual(60, hawk.Health);
            Assert.AreEqual(30, hawk.Stamina);
            Assert.AreEqual(CombatantFlags.Ready, hawk.Flags);
            Assert.IsFalse(hawk.IsDead);
        }

        [Test]
        public void NothingRisesWithoutARisenBlackSlayerAlreadyInTheFight() {
            // SweepRuns: an encounter carrying only the transforming creature never sees a single
            // revival — the first riser needs one to rise for.
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock());
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(401, StatBlock(), SlayerRevival.TransformingType,
                gridX: 4, gridY: 8);
            var runtime = new CombatRuntime(session, null, Entries(), null);
            CombatEncounter fight = runtime.EnterRoster(new short[] { 401 });

            Combatant hawk = Nighthawk(fight);
            hawk.RevivalCountdown = 0;
            hawk.Flags = CombatantFlags.Dead;

            Assert.AreEqual(0, runtime.TickSlayerRevivals());
            Assert.AreEqual(SlayerRevival.TransformingType, hawk.ClassId);
        }

        [Test]
        public void ACreatureWithNoCountdownIsNeverConsidered() {
            var (runtime, fight) = Fight();
            Combatant hawk = Nighthawk(fight);
            hawk.Flags = CombatantFlags.Dead;

            Assert.AreEqual(SlayerRevival.NoCountdown, hawk.RevivalCountdown);
            Assert.AreEqual(0, runtime.TickSlayerRevivals(),
                "no countdown is not a countdown of zero");
        }

        [Test]
        public void TheCountdownIsRolledInsideItsBand() {
            for (var roll = 0; roll < 7; roll++) {
                int captured = roll;
                int n = SlayerRevival.RollCountdown(_ => captured);
                Assert.GreaterOrEqual(n, SlayerRevival.MinimumCountdown);
                Assert.LessOrEqual(n, SlayerRevival.MaximumCountdown);
            }
        }
    
        [Test]
        public void TheRisePlaysITSCue_OncePerCreatureThatGetsUp() {
            var (runtime, fight, cues) = FightWithSound();
            Combatant hawk = Nighthawk(fight);
            hawk.RevivalCountdown = 0;
            hawk.Health = 0;
            hawk.Stamina = 0;
            hawk.Flags = CombatantFlags.Dead;

            Assert.AreEqual(1, runtime.TickSlayerRevivals(), "it should rise");
            CollectionAssert.AreEqual(new[] { SlayerRevival.RisingSound }, cues,
                "combataiact_bhood_revive_cycle plays 0x47 once, between the grid removal and the VFX");

            // And nothing more on later ticks: the countdown is cleared by the rise, so a second
            // sweep must not find it again and re-sound it.
            runtime.TickSlayerRevivals();
            Assert.AreEqual(1, cues.Count, "the cue belongs to the rise, not to the sweep");
        }

        [Test]
        public void ABodyUnderSOMEONEIsSilent_AndStaysSilentEveryTick() {
            // *** THE CUE IS INSIDE THE TILE TEST. *** The whole cycle is wrapped in
            // `if (combatgrid_tile_is_blocked(...) == 0)`, and by this point the countdown has
            // already run out — so the attempt repeats every single tick for as long as somebody
            // stands on the grave. A cue placed on the attempt rather than the rise would fire once
            // a round, for ever, from a creature that never gets up.
            var (runtime, fight, cues) = FightWithSound();
            Combatant hawk = Nighthawk(fight);
            hawk.RevivalCountdown = 0;
            hawk.Health = 0;
            hawk.Stamina = 0;
            hawk.Flags = CombatantFlags.Dead;

            // Lay the body under the risen slayer that the sweep needs present anyway — the
            // occupancy test is a plain coordinate comparison over the live combatants.
            Combatant blocker = null;
            foreach (Combatant c in fight.Enemies) {
                if (c.ClassId == SlayerRevival.RisenType) {
                    blocker = c;
                    break;
                }
            }
            Assert.IsNotNull(blocker, "the sweep needs a risen slayer present anyway");
            hawk.X = blocker.X;
            hawk.Y = blocker.Y;

            for (var tick = 0; tick < 3; tick++) {
                Assert.AreEqual(0, runtime.TickSlayerRevivals(), "the grave is occupied");
            }
            CollectionAssert.IsEmpty(cues, "a body that cannot rise makes no noise");
        }
}
}
