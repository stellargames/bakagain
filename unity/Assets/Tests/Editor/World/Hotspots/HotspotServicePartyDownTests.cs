namespace BakAgain.Tests.Editor.World.Hotspots {
    using BakAgain.Core;
    using BakAgain.World.Hotspots;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Data;
    using GameData.Resources.GameState;
    using GameData.Resources.World;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// A downed party triggers nothing (TASK-264).
    ///
    /// <para><b>The byte had three writers and no readers.</b> A party wipe set
    /// <c>PartyDeathState</c>, the save persisted it, and the world carried on exactly as before —
    /// ambushes still sprang, dialogs still fired. <c>WORLDLP.C</c> gates
    /// <c>hotspotevt_activate_at_player</c> behind the flag; this is that gate.</para>
    /// </summary>
    public class HotspotServicePartyDownTests {
        private const int Zone = 1;
        private const long EncounterNumber = 40;
        private const short RosterSlot = 400;

        private static ActorStat[] StatBlock() {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 20, Max = 99 };
            }
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = 40, Max = 99 };
            return stats;
        }

        private static TileEventTrigger CombTrigger() => new TileEventTrigger {
            Type = TileEventType.Comb, EntryNumber = 0,
            StartX = 0, StartY = 0, EndX = 7, EndY = 7,
        };

        private static HotspotService Build(GameSession session) {
            session.SetActorStatsForTest(0, StatBlock());
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(RosterSlot, StatBlock(), 12, gridX: 5, gridY: 5);
            session.SetEncounterRosterForTest((int)EncounterNumber, new short[] { RosterSlot });

            var service = new HotspotService(null, null, session, null, random: _ => 99);
            service.SetZoneForTest(Zone,
                byChunk: new Dictionary<(int x, int y), List<TileEventTrigger>> {
                    [(0, 0)] = new List<TileEventTrigger> { CombTrigger() },
                },
                comb: new DefFamilyFile<DefCombEntry>("DEF_COMB.DAT",
                    new List<DefRecord<DefCombEntry>> {
                        new DefRecord<DefCombEntry> {
                            Status = 1,
                            Payload = new DefCombEntry { EncounterNumber = (uint)EncounterNumber },
                        },
                    }));
            return service;
        }

        /// <summary>The control: this ambush really does spring, so the next test is measuring the
        /// guard rather than an inert fixture.</summary>
        [Test]
        public void AStandingPartyWalksIntoTheAmbush() {
            var session = new GameSession { PartyDeathState = PartyDownState.Standing };
            HotspotService service = Build(session);

            service.ActivateAtPartyPosition(0, 0);

            Assert.AreEqual(EncounterNumber, service.FightingEncounter,
                "without this the guard test below would pass against a fixture that never "
                + "ambushed anyone, which is the way a gate test flatters itself");
        }

        /// <summary>THE GUARD. Both non-zero values stop the pass — <c>WORLDLP.C</c> tests
        /// <c>!= 0</c>, and the pit writes 2 while the stat sweep writes 1.</summary>
        [TestCase(PartyDownState.Noticed)]
        [TestCase(PartyDownState.Asserted)]
        public void ADownedPartySpringsNothing(int state) {
            var session = new GameSession { PartyDeathState = (byte)state };
            HotspotService service = Build(session);

            bool kept = service.ActivateAtPartyPosition(0, 0);

            Assert.AreEqual(-1, service.FightingEncounter,
                "a party that has just been wiped out must not be ambushed on the way out");
            Assert.IsTrue(kept,
                "the step is KEPT: nothing triggering is not the same as a rule refusing the step");
        }

        /// <summary>
        /// <b>The signal the world loop's exit waits on</b>, and asking must not CREATE a fight.
        /// <c>HotspotService.Combat</c> builds a runtime on first touch, so a naive
        /// <c>Combat.InCombat</c> would stand one up just to answer "is there a fight".
        /// </summary>
        [Test]
        public void FightInProgress_IsFalseBeforeAFightAndTrueDuringOne_WithoutStartingOne() {
            var session = new GameSession { PartyDeathState = PartyDownState.Standing };
            HotspotService service = Build(session);

            Assert.IsFalse(service.FightInProgress, "no fight has started");
            Assert.AreEqual(-1, service.FightingEncounter,
                "asking must not have created a runtime and entered one");

            service.ActivateAtPartyPosition(0, 0);

            Assert.IsTrue(service.FightInProgress, "the ambush put a fight on the field");
        }

        // ---- the trapped-container spawn (TASK-136) -----------------------------------------------

        private static TileEventTrigger TrapTrigger() => new TileEventTrigger {
            Type = TileEventType.Trap, EntryNumber = 0,
            // A window that does NOT cover the whole tile, so "matched the range" and "matched
            // anything" are distinguishable.
            StartX = 2, StartY = 3, EndX = 4, EndY = 5,
        };

        private static DefFamilyFile<DefTrapEntry> TrapTable(long encounter) =>
            new DefFamilyFile<DefTrapEntry>("DEF_TRAP.DAT",
                new List<DefRecord<DefTrapEntry>> {
                    new DefRecord<DefTrapEntry> {
                        Status = 1,
                        Payload = new DefTrapEntry { EncounterNumber = (uint)encounter },
                    },
                });

        private static HotspotService WithATrap(GameSession session, long trapEncounter) {
            session.SetActorStatsForTest(0, StatBlock());
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(RosterSlot, StatBlock(), 12, gridX: 5, gridY: 5);
            session.SetEncounterRosterForTest((int)trapEncounter, new short[] { RosterSlot });

            var service = new HotspotService(null, null, session, null, random: _ => 99);
            service.SetZoneForTest(Zone,
                byChunk: new Dictionary<(int x, int y), List<TileEventTrigger>> {
                    [(0, 0)] = new List<TileEventTrigger> { TrapTrigger() },
                },
                trap: TrapTable(trapEncounter));
            // Establishes the current chunk, which the spawn looks the trigger up in.
            service.ActivateAtPartyPosition(0, 0);
            return service;
        }

        /// <summary>
        /// THE SPAWN. A trapped container hands the trap its OWN encounter record's sub-tile, and
        /// the trigger covering it springs.
        /// </summary>
        [Test]
        public void FireTrapEncounterAt_SpringsTheTriggerCoveringThatSubTile() {
            var session = new GameSession();
            HotspotService service = WithATrap(session, trapEncounter: 41);

            Assert.AreEqual(HotspotService.TrapDispatch.Blocks, service.FireTrapEncounterAt(3, 4),
                "the sub-tile is inside the trigger's window, so it springs and stops the click");
            Assert.AreEqual(41, service.FightingEncounter);
        }

        /// <summary>
        /// <b>The window is a RANGE and it is inclusive.</b> A spawn keyed off the clicked object's
        /// own coordinates rather than the encounter record's would land outside it and do nothing
        /// — silently, which is how a dropped mechanic hides.
        /// </summary>
        [Test]
        public void FireTrapEncounterAt_OutsideTheWindow_SpringsNothing() {
            var session = new GameSession();
            HotspotService service = WithATrap(session, trapEncounter: 41);

            Assert.AreEqual(HotspotService.TrapDispatch.NoTrap, service.FireTrapEncounterAt(9, 9));
            Assert.AreEqual(-1, service.FightingEncounter);
        }

        /// <summary>
        /// A spent trap still covers the point, so the click carries on: the grave is dug after its ambush
        /// (HOTSPOT.C:231, the dispatch returns 1; :667, a fought encounter neither fires nor arms).
        /// </summary>
        [Test]
        public void FireTrapEncounterAt_ASpentTrap_LetsTheClickProceed() {
            var session = new GameSession();
            HotspotService service = WithATrap(session, trapEncounter: 41);
            session.SetGlobalValue(HotspotRules.EncounterFoughtKey(41), 1);

            Assert.AreEqual(HotspotService.TrapDispatch.Proceed, service.FireTrapEncounterAt(3, 4));
            Assert.AreEqual(-1, service.FightingEncounter);
        }

        /// <summary>A chunk with no trap at all is an answer, not a failure.</summary>
        [Test]
        public void FireTrapEncounterAt_WithNoTrapInTheChunk_IsFalse() {
            var session = new GameSession { PartyDeathState = PartyDownState.Standing };
            HotspotService service = Build(session);   // a Comb trigger, no Trap
            service.ActivateAtPartyPosition(0, 0);

            Assert.AreEqual(HotspotService.TrapDispatch.NoTrap, service.FireTrapEncounterAt(3, 4),
                "only Trap triggers are considered; the Comb one on the same tile must not spring");
        }

        // ---- the acting fighter's pack (TASK-263) --------------------------------------------------

        /// <summary>
        /// <b>Id 44 no longer falls to "not wired yet".</b>
        /// </summary>
        /// <remarks>
        /// The value of this test is not that a screen appears — it cannot, with no accessor — but
        /// that the command reaches an arm of its own and asks for one. Before this it hit the
        /// default arm alongside Inspect, so a player pressing it in a fight got a debug line.
        ///
        /// <para>With no inventory screen and no navigator the arm declines quietly, which is the
        /// same shape Cast and Shoot take when their screens are absent: a headless fight must not
        /// throw because a UI singleton was never built.</para>
        /// </remarks>
        [Test]
        public void TheCharacterScreenCommandIsHandled_AndDeclinesQuietlyWithNoScreen() {
            var session = new GameSession();
            HotspotService service = Build(session);
            service.ActivateAtPartyPosition(0, 0);
            // Encounter.Current is null until a turn has begun, so the fixture asserts on the
            // roster instead — the command only needs a live fight, not a live turn.
            Assert.IsNotNull(service.Combat.Encounter,
                "the fixture must have a live fight for a pack to be opened in");
            Assert.IsNotEmpty(service.Combat.Encounter.Party);

            Assert.DoesNotThrow(() => service.OnCombatCommandForTest(
                GameData.Resources.Combat.CombatCommands.Command.CharacterScreen, 44));
        }
    }
}
