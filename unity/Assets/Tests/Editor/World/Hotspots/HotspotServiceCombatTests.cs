namespace BakAgain.Tests.Editor.World.Hotspots {
    using BakAgain.Core;
    using BakAgain.World.Hotspots;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using GameData.Resources.Data;
    using GameData.Resources.GameState;
    using GameData.Resources.World;

    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// The wiring <c>HotspotService</c> owns around a fight — start, settle, retreat.
    /// </summary>
    /// <remarks>
    /// <b>This class had no test rig at all</b> (TASK-207). Its tests targeted the pure
    /// <c>HotspotRules</c> / <c>HotspotActivator</c> / <c>HotspotDispatcher</c>, so everything the
    /// service itself joins up was unasserted — which is how the fought flag came to be read by the
    /// activate pass and written by nothing, leaving every defeated encounter armed.
    ///
    /// <para>The seam is <c>SetZoneForTest</c>: the DEF tables are only ever filled by
    /// <c>LoadZoneAsync</c>, which wants the game archive.</para>
    /// </remarks>
    public class HotspotServiceCombatTests {
        private const int Zone = 1;
        private const long EncounterNumber = 40;
        private const short RosterSlot = 400;

        private static DefFamilyFile<DefCombEntry> CombTable(long encounter, bool avoidable = false) =>
            new DefFamilyFile<DefCombEntry>("DEF_COMB.DAT",
                new List<DefRecord<DefCombEntry>> {
                    new DefRecord<DefCombEntry> {
                        Status = 1,
                        Payload = new DefCombEntry {
                            EncounterNumber = (uint)encounter, Avoidable = avoidable,
                            // Four DISTINGUISHABLE landings, so a test can tell which one was
                            // picked rather than only that the party moved.
                            LandingDir1 = Landing(1),
                            LandingDir2 = Landing(2),
                            LandingDir4 = Landing(4),
                            LandingDir8 = Landing(8),
                        },
                    },
                });

        private static LandingPosition Landing(int direction) => new LandingPosition {
            FineX = 1000 * direction, FineY = 2000 * direction,
            RotationZ = (ushort)(0x1000 * direction),
        };

        private static TileEventTrigger CombTrigger(int postEventFlag = 0) => new TileEventTrigger {
            Type = TileEventType.Comb, EntryNumber = 0,
            StartX = 0, StartY = 0, EndX = 7, EndY = 7,
            OnFire = postEventFlag == 0
                ? null
                : new SetFlagEffect { Flag = postEventFlag, Set = true },
        };

        // The story flag a Comb hotspot guards — the original's wEvent_key_post, which our extractor
        // reads into OnFire at the same offset.
        private const int PostEventFlag = 8100;
        private const int HotspotIndex = 3;

        private static ActorStat[] StatBlock(byte health = 40) {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 20, Max = 99 };
            }
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = health, Max = 99 };
            stats[(int)ActorAttribute.Stamina] = new ActorStat { Base = 20, Max = 99 };
            stats[(int)ActorAttribute.Speed] = new ActorStat { Base = 5, Max = 99 };
            return stats;
        }

        private sealed class Rig {
            public HotspotService Service;
            public GameSession Session;
            public readonly List<bool> Music = new();
            public int CameraResyncs;
        }

        /// <param name="roll">
        /// The <c>rnd(n)</c> the service uses. <b>Injected because the retreat's escape test is a
        /// d100 against a small chance</b> — an uncontrolled one makes the outcome a coin toss, and
        /// this fixture demonstrated exactly that: the escape test failed one gate run and passed
        /// the next with no file change in between.
        /// </param>
        private static Rig Build(int roll = 99) {
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock());
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(RosterSlot, StatBlock(), 12, gridX: 5, gridY: 5);
            session.SetEncounterRosterForTest((int)EncounterNumber, new short[] { RosterSlot });

            var rig = new Rig { Session = session };
            rig.Service = new HotspotService(null, null, session, null,
                random: _ => roll,
                setCombatMusic: on => rig.Music.Add(on),
                resyncCamera: () => rig.CameraResyncs++);
            rig.Service.SetZoneForTest(Zone,
                byChunk: new Dictionary<(int x, int y), List<TileEventTrigger>> {
                    [(0, 0)] = new List<TileEventTrigger> { CombTrigger() },
                },
                comb: CombTable(EncounterNumber));
            return rig;
        }

        private static int FoughtFlag(Rig rig) =>
            rig.Session.GetGlobalValue(HotspotRules.EncounterFoughtKey(EncounterNumber)) ?? 0;

        [Test]
        public void StartCombatOpensTheFightAndTurnsTheMusicOn() {
            Rig rig = Build();

            Assert.IsTrue(rig.Service.StartCombat(CombTrigger()));

            Assert.IsNotNull(rig.Service.Combat.Encounter);
            Assert.AreEqual(1, rig.Service.Combat.Encounter.Enemies.Count);
            CollectionAssert.Contains(rig.Music, true);
            Assert.AreEqual(EncounterNumber, rig.Service.FightingEncounter);
        }

        [Test]
        public void AnArmedSpellRecordsTheTargetUnderTheCursor_AndKeepsItOverACellItCannotAim() {
            // TASK-540. Case 4 of combat_arena_resolve_menu_action writes the cursor's combatant on
            // every pass the cell is a legal aim (COMBAT.C:2408), with no click; any other cell drops
            // the state for that pass and clears nothing. Live on SAVE88 with Invitation armed, Owyn's
            // target became the troll under the cursor and stayed on it over an empty cell and himself.
            Rig rig = Build();
            Assert.IsTrue(rig.Service.StartCombat(CombTrigger()));
            CombatEncounter fight = rig.Service.Combat.Encounter;
            for (int i = 0; i < 4 && (fight.Current == null || !fight.Current.IsPartyMember); i++) {
                fight.EndTurn();
                fight.PickNext();
            }
            Combatant acting = fight.Current;
            Assert.IsNotNull(acting, "the fixture needs a party member's turn");
            Assert.IsTrue(acting.IsPartyMember);
            Combatant enemy = fight.Enemies[0];
            int livingActorAim = System.Linq.Enumerable.First(System.Linq.Enumerable.Range(0, 9),
                t => GameData.Resources.Spells.SpellTargetingRules.AimOf(t)
                    == GameData.Resources.Spells.SpellTargetingRules.Aim.LivingActor);
            rig.Service.ArmSpellForTest(
                new GameData.Resources.Spells.Spell("S") { TargetingType = livingActorAim }, 0, 5);

            rig.Service.CombatPanelContent(0, partyMember: false);
            Assert.AreSame(enemy, acting.Target, "hovering a legal target records it");

            rig.Service.CombatPanelContent(-1, partyMember: false);
            Assert.AreSame(enemy, acting.Target, "an empty cell is no aim, and clears nothing");

            rig.Service.CombatPanelContent(acting.PartySlot, partyMember: true);
            Assert.AreSame(enemy, acting.Target, "nor does the caster's own cell");
        }

        [Test]
        public void AClickTheArmedAimRefusesIsIgnored_TheTurnIsKeptAndTheSpellStaysArmed() {
            // TASK-546. The original's cursor marks a refused cell with move cost 1000, and case 4 acts
            // on a click only below that (COMBAT.C:2411): the prompt stays up and nothing is spent.
            // The port used to drop back to movement, which closed the shoot menu and disarmed the spell.
            Rig rig = Build();
            Assert.IsTrue(rig.Service.StartCombat(CombTrigger()));
            CombatEncounter fight = rig.Service.Combat.Encounter;
            for (int i = 0; i < 4 && (fight.Current == null || !fight.Current.IsPartyMember); i++) {
                fight.EndTurn();
                fight.PickNext();
            }
            Combatant acting = fight.Current;
            Assert.IsNotNull(acting, "the fixture needs a party member's turn");
            Assert.IsTrue(acting.IsPartyMember, "the fixture needs a party member's turn");
            int livingActorAim = System.Linq.Enumerable.First(System.Linq.Enumerable.Range(0, 9),
                t => GameData.Resources.Spells.SpellTargetingRules.AimOf(t)
                    == GameData.Resources.Spells.SpellTargetingRules.Aim.LivingActor);
            rig.Service.ArmSpellForTest(
                new GameData.Resources.Spells.Spell("S") { TargetingType = livingActorAim }, 0, 5);

            rig.Service.ResolveCombatTargetClick(acting.PartySlot, partyMember: true, isPrimary: true);

            Assert.IsTrue(acting.CanAct(strict: true), "a refused click spends nothing");
            rig.Service.CombatPanelContent(0, partyMember: false);
            Assert.AreSame(fight.Enemies[0], acting.Target,
                "the spell is still armed, so pointing at the enemy still aims it");
        }

        [Test]
        public void WhileTheAssessmentIsUp_TheParchmentKeepsTheInspectorsStats_NotTheMeleePreview() {
            // CBENC.C:307-349 plays 0x84, paints the rows and plays 0x85 inside the inspect click;
            // the turn loop does not run, so the HUD keeps what case 47's stateA = 3 left there --
            // the acting member's stats (TASK-743). Measured in the zone-1 ambush.
            Rig rig = Build();
            Assert.IsTrue(rig.Service.StartCombat(CombTrigger()));
            CombatEncounter fight = rig.Service.Combat.Encounter;
            for (int i = 0; i < 4 && (fight.Current == null || !fight.Current.IsPartyMember); i++) {
                fight.EndTurn();
                fight.PickNext();
            }
            Assert.IsTrue(fight.Current?.IsPartyMember ?? false, "the fixture needs a party member's turn");
            Combatant enemy = fight.Enemies[0];
            fight.Current.X = enemy.X - 1;
            fight.Current.Y = enemy.Y;
            var stats = rig.Service.CombatPanelContent(-1, partyMember: false);
            Assert.IsNotNull(stats.Lines, "the control: the acting member's stats are the default panel");
            CollectionAssert.AreNotEqual(stats.Lines, rig.Service.CombatPanelContent(0, partyMember: false).Lines,
                "the control: hovering the adjacent enemy normally shows the melee preview");

            rig.Service.AssessingForTest = true;

            CollectionAssert.AreEqual(stats.Lines, rig.Service.CombatPanelContent(0, partyMember: false).Lines);
        }

        [Test]
        public void AutoResolveKeepsTheLastPartyMembersPanelThroughEnemyTurns() {
            // combat_arena_turn_loop draws the stats panel for party turns only, so the last party
            // member stays up while the monsters act (TASK-757). The port blanked panel and portrait.
            Rig rig = Build();
            Assert.IsTrue(rig.Service.StartCombat(CombTrigger()));
            CombatEncounter fight = rig.Service.Combat.Encounter;
            Combatant member = fight.Party[0];
            for (int i = 0; i < 8 && (fight.Current == null || fight.Current.IsPartyMember); i++) {
                fight.EndTurn();
                fight.PickNext();
            }
            Assert.IsFalse(fight.Current?.IsPartyMember ?? true, "the fixture needs an enemy's turn");
            Assert.AreEqual(-1, rig.Service.ActingPortraitHeadId(), "the control: no portrait on an enemy turn");

            rig.Service.AutoResolveHudForTest(member);

            Assert.AreEqual(member.ClassId, rig.Service.ActingPortraitHeadId());
            var panel = rig.Service.CombatPanelContent(-1, partyMember: false);
            Assert.IsNotNull(panel.Lines);
            Assert.IsNotEmpty(panel.Lines);
        }

        [Test]
        public void TheAssessmentPageIsTheOpeningRecordsOwnText_NoRowsAndNoMenuReserve() {
            // CBENC.C:307-349 paints the rows into 0x84's page; they are not text. And 0x84 carries
            // no TextWithChoice, so its text is laid out without the menu row's reserve (TASK-742).
            var opening = new GameData.Resources.Dialog.DialogEntry {
                Text = "Locklear studied his opponent.",
                Flags = GameData.Resources.Dialog.DialogEntryFlags.SkipWait,
            };
            GameData.Resources.Dialog.DialogEntry page = HotspotService.AssessmentPage(opening);
            Assert.AreEqual(opening.Text, page.Text);
            Assert.AreEqual((GameData.Resources.Dialog.DialogEntryFlags)0, page.Flags & (
                GameData.Resources.Dialog.DialogEntryFlags.SkipWait
                | GameData.Resources.Dialog.DialogEntryFlags.TextWithChoice));
        }

        [Test]
        public void AssessmentRowsAndTheAcceptBoxSitWhereTheOriginalDrawsThem_InThePanelsSpace() {
            // A row at screen VGA (70,68) inside a panel whose area starts at canonical (65,66):
            // canonical (350-65, 408-66). 0x85's ResizeDialog box is relative the same way.
            var panel = GameData.Resources.Layout.LayoutHint.PxRect(65, 66, 1470, 726);
            Assert.AreEqual((285f, 342f), HotspotService.AssessmentRowInPanel(
                new GameData.Resources.Combat.HudPanelLine("Health:", 70, 68), panel));
            // 0x85's box is VGA (259,98,38,18); its one button is laid out inside it like any
            // choice row (ASKABOUT.C:400-410): w = "Accept" (29) + 10, x = 38/2 + 4 - 39/2, y = 18 - (10 + 11),
            // so the original draws Accept at VGA (263,95,39,14): x 263-301, as measured in the zone-1 ambush.
            Assert.AreEqual((263f * 5 - 65, 95f * 6 - 66, 39f * 5, 14f * 6), HotspotService.AcceptButtonInPanel(
                GameData.Resources.Layout.LayoutHint.PxRect(1295, 588, 190, 108), "Accept", panel));
        }

        [Test]
        public void ArmingInspectKeepsTheActingMembersStatsOnTheParchment() {
            // Case 47 only sets stateA = 3; with no spell chosen the turn loop keeps drawing
            // combat_actor_draw_stats_panel(g_current_actor) (COMBAT.C:2062-2067, 2552-2558).
            // Measured in the zone-1 ambush: the original kept Locklear's stats, the port blanked them.
            Rig rig = Build();
            Assert.IsTrue(rig.Service.StartCombat(CombTrigger()));
            CombatEncounter fight = rig.Service.Combat.Encounter;
            for (int i = 0; i < 4 && (fight.Current == null || !fight.Current.IsPartyMember); i++) {
                fight.EndTurn();
                fight.PickNext();
            }
            Assert.IsTrue(fight.Current?.IsPartyMember ?? false, "the fixture needs a party member's turn");

            rig.Service.OnCombatCommandForTest(CombatCommands.Command.Inspect, CombatCommands.InspectId);
            Assert.AreEqual(CombatCommandOutcome.PendingMode.InspectTarget, rig.Service.PendingModeForTest,
                "the control: Inspect really is armed");

            var panel = rig.Service.CombatPanelContent(-1, partyMember: false);
            Assert.IsNotNull(panel.Lines, "the stats stay up while an enemy is being picked");
            Assert.IsNotEmpty(panel.Lines);
        }

        /// <summary>
        /// <b>Spending a turn leaves NOTHING armed</b> — the other half of the rule TASK-546 kept.
        /// </summary>
        /// <remarks>
        /// TASK-546 made a refused click deliberately keep the aim, which is what the original does.
        /// The missing half was the disarm when a turn is later spent, and without it a Shoot armed
        /// and then refused (no quarrels, or an enemy adjacent — <c>CombatCapability.RangeIsClear</c>)
        /// stayed armed for the REST OF THE FIGHT. Because <c>_pendingCombatMode</c> gates BOTH
        /// resolve-clicks, the arena then half-dies: <c>ResolveCombatGroundClick</c> returns at its
        /// <c>TargetSelection</c> guard so nobody can move, and <c>ResolveCombatTargetClick</c> never
        /// reaches <c>ResolveUnarmedClick</c> so nobody can melee — both silently, spending no
        /// stamina. Found driving <c>def_comb:91</c> on 2026-09-17: three of four enemies dead, a
        /// party at full health, and the fight could not be finished (TASK-572).
        ///
        /// <para><c>CombatCommandOutcome.SpendsTheTurn</c> was already covered — the PREDICATE had
        /// tests and its CONSEQUENCE had none, which is how a documented invariant went unheld.</para>
        /// </remarks>
        [TestCase(CombatCommands.Command.Defend, CombatCommands.DefendId)]
        [TestCase(CombatCommands.Command.Rest, CombatCommands.RestId)]
        public void SpendingTheTurnDisarmsWhateverWasStillPending(
            CombatCommands.Command command, int actionId) {
            Rig rig = Build();
            Assert.IsTrue(rig.Service.StartCombat(CombTrigger()));
            CombatEncounter fight = rig.Service.Combat.Encounter;
            for (int i = 0; i < 4 && (fight.Current == null || !fight.Current.IsPartyMember); i++) {
                fight.EndTurn();
                fight.PickNext();
            }
            Combatant acting = fight.Current;
            Assert.IsNotNull(acting, "the fixture needs a party member's turn");
            Assert.IsTrue(acting.IsPartyMember, "the fixture needs a party member's turn");

            int livingActorAim = System.Linq.Enumerable.First(System.Linq.Enumerable.Range(0, 9),
                t => GameData.Resources.Spells.SpellTargetingRules.AimOf(t)
                    == GameData.Resources.Spells.SpellTargetingRules.Aim.LivingActor);
            rig.Service.ArmSpellForTest(
                new GameData.Resources.Spells.Spell("S") { TargetingType = livingActorAim }, 0, 5);

            // The control. Without it this passes against a fixture that was never armed, which is
            // the way a disarm test flatters itself.
            Assert.AreEqual(CombatCommandOutcome.PendingMode.TargetSelection,
                rig.Service.PendingModeForTest, "the fixture must really be armed first");

            rig.Service.OnCombatCommandForTest(command, actionId);

            Assert.AreEqual(CombatCommandOutcome.PendingMode.None, rig.Service.PendingModeForTest,
                $"{command} spends the turn, so it must leave nothing armed");
        }

        [Test]
        public void AWONFightMarksTheEncounterFOUGHT() {
            // *** The flag the activate pass has always read and nothing ever wrote. *** Without it
            // the same ambush fires again on the party's very next step, for ever.
            Rig rig = Build();
            rig.Service.StartCombat(CombTrigger());
            Assert.AreEqual(0, FoughtFlag(rig), "nothing is marked while the fight runs");

            foreach (Combatant enemy in rig.Service.Combat.Encounter.Enemies) {
                rig.Service.Combat.Encounter.Kill(enemy);
            }
            rig.Service.EndCombatForTest();

            Assert.AreNotEqual(0, FoughtFlag(rig));
            Assert.IsTrue(rig.Service.EncounterFought(CombTrigger()),
                "and the activate pass now refuses it");
        }

        [Test]
        public void AWIPEMarksNothing() {
            // Ending the fight is not winning it: IsOver() is true either way, and marking it here
            // clears an ambush the party LOST to.
            Rig rig = Build();
            rig.Service.StartCombat(CombTrigger());
            foreach (Combatant member in rig.Service.Combat.Encounter.Party) {
                rig.Service.Combat.Encounter.Kill(member);
            }
            rig.Service.EndCombatForTest();

            Assert.AreEqual(0, FoughtFlag(rig));
        }

        [Test]
        public void TheFightRemembersItsEncounterAndForgetsItAfterwards() {
            // Left set, the NEXT fight would settle this encounter instead of its own.
            Rig rig = Build();
            rig.Service.StartCombat(CombTrigger());
            foreach (Combatant enemy in rig.Service.Combat.Encounter.Enemies) {
                rig.Service.Combat.Encounter.Kill(enemy);
            }
            rig.Service.EndCombatForTest();

            Assert.AreEqual(-1, rig.Service.FightingEncounter);
        }

        [Test]
        public void TheMusicGoesBackWhenTheFightEnds() {
            // The world layer owns the track to come back to; this only says WHEN. Recorded as
            // unasserted on TASK-143 since the day it was wired.
            Rig rig = Build();
            rig.Service.StartCombat(CombTrigger());
            foreach (Combatant enemy in rig.Service.Combat.Encounter.Enemies) {
                rig.Service.Combat.Encounter.Kill(enemy);
            }
            rig.Service.EndCombatForTest();

            CollectionAssert.AreEqual(new[] { true, false }, rig.Music);
        }

        [Test]
        public void AWONFightWritesThePostEventAndTheHotspotsDONEFlag() {
            // *** BOTH USED TO BE WRITTEN WHEN THE FIGHT STARTED. *** The original writes them only
            // in the resolved branch, alongside the fought flag — see SettleEncounter.
            Rig rig = Build();
            var trigger = CombTrigger(PostEventFlag);
            rig.Service.StartCombat(trigger, HotspotIndex);

            Assert.AreEqual(0, rig.Session.GetGlobalValue(PostEventFlag) ?? 0,
                "the post event must not fire merely because a fight opened");
            Assert.AreEqual(0, rig.Session.GetGlobalValue(rig.Service.DoneFlagKey(HotspotIndex)) ?? 0);

            foreach (Combatant enemy in rig.Service.Combat.Encounter.Enemies) {
                rig.Service.Combat.Encounter.Kill(enemy);
            }
            rig.Service.EndCombatForTest();

            Assert.AreNotEqual(0, rig.Session.GetGlobalValue(PostEventFlag) ?? 0);
            Assert.AreNotEqual(0, rig.Session.GetGlobalValue(rig.Service.DoneFlagKey(HotspotIndex)) ?? 0);
        }

        [Test]
        public void ALOSTFightWritesNeither() {
            // The half that made the old timing a bug rather than a nicety: a party that is wiped
            // out set the very flag the encounter exists to guard.
            Rig rig = Build();
            rig.Service.StartCombat(CombTrigger(PostEventFlag), HotspotIndex);
            foreach (Combatant member in rig.Service.Combat.Encounter.Party) {
                rig.Service.Combat.Encounter.Kill(member);
            }
            rig.Service.EndCombatForTest();

            Assert.AreEqual(0, rig.Session.GetGlobalValue(PostEventFlag) ?? 0);
            Assert.AreEqual(0, rig.Session.GetGlobalValue(rig.Service.DoneFlagKey(HotspotIndex)) ?? 0);
        }

        [Test]
        public void WithNoHotspotIndexTheDoneWriteIsSkipped_NotAimedAtSlotZero() {
            // -1 is "the caller has none". Writing DoneFlagKey(0) instead would silently retire a
            // DIFFERENT hotspot in the same chunk.
            Rig rig = Build();
            rig.Service.StartCombat(CombTrigger(PostEventFlag));
            foreach (Combatant enemy in rig.Service.Combat.Encounter.Enemies) {
                rig.Service.Combat.Encounter.Kill(enemy);
            }
            rig.Service.EndCombatForTest();

            Assert.AreNotEqual(0, rig.Session.GetGlobalValue(PostEventFlag) ?? 0,
                "the post event still fires — it needs no index");
            Assert.AreEqual(0, rig.Session.GetGlobalValue(rig.Service.DoneFlagKey(0)) ?? 0);
        }

        [Test]
        public void AnEncounterWithNoRosterDoesNotOpenAFight() {
            // "Names nobody" is a data case, not an error — an empty arena would be worse.
            var session = new GameSession();
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetActorStatsForTest(0, StatBlock());
            var rig = new Rig { Session = session };
            rig.Service = new HotspotService(null, null, session, null,
                setCombatMusic: on => rig.Music.Add(on));
            rig.Service.SetZoneForTest(Zone, comb: CombTable(EncounterNumber));

            Assert.IsFalse(rig.Service.StartCombat(CombTrigger()));
            CollectionAssert.IsEmpty(rig.Music);
        }
    

    // ---- the encounter address StartCombat hands the fight ---------------------------------------

    [Test]
    public void TheEncounterAddressComesFromTheCHUNK_notTheEncounterNumber() {
        // *** An encounter NUMBER is not a record index. *** It indexes the 700 enemy-party records;
        // the state block holds five records per ref pair. Using one as the other writes far outside
        // the block for all but the lowest-numbered encounters.
        Rig rig = Build();
        rig.Service.ActivateAtPartyPosition(0, 0);   // establishes the current chunk

        EncounterActorPersistence.RecordAddress address =
            rig.Service.EncounterAddressForTest(EncounterNumber);

        Assert.IsTrue(address.IsKnown);
        Assert.AreEqual(0, address.RecordIndex, "the only encounter trigger in the chunk is record 0");
        Assert.AreNotEqual(EncounterNumber, address.RecordIndex);
    }

    [Test]
    public void AnEncounterTheChunkDoesNotCarryHasNOAddress() {
        // rgnenc_persist_actor_removed scans the chunk's record-id list and returns without writing
        // when the id is absent. "We do not know where this goes" is an answer, not a failure.
        Rig rig = Build();
        rig.Service.ActivateAtPartyPosition(0, 0);

        Assert.IsFalse(rig.Service.EncounterAddressForTest(EncounterNumber + 1).IsKnown);
    }

    [Test]
    public void WithNoChunkEstablishedThereIsNoAddressEither() {
        // Before the party has stepped anywhere, there is no trigger list to count positions in.
        Rig rig = Build();

        Assert.IsFalse(rig.Service.EncounterAddressForTest(EncounterNumber).IsKnown);
    }

    // ---- retreat -------------------------------------------------------------------------------

    [Test]
    public void AnESCAPEEndsTheFightAndPutsTheMusicBack_butMarksNOTHING() {
        // Running away is the one outcome that leaves the encounter armed; that is the point of it.
        Rig rig = Build(roll: 0);        // beats the escape chance
        rig.Service.StartCombat(CombTrigger());
        Assert.IsTrue(rig.Service.Combat.Encounter.EscapeAllowed, "this encounter permits a retreat");
        Combatant member = rig.Service.Combat.Encounter.Party[0];

        rig.Service.ResolveRetreatForTest(member);

        Assert.AreEqual(0, FoughtFlag(rig), "a flight settles nothing");
        Assert.AreEqual(-1, rig.Service.FightingEncounter,
            "but the fight is over, so the next one cannot inherit this encounter");
        CollectionAssert.Contains(rig.Music, false);
    }

    // ---- placing the chunk's encounter actors -----------------------------------------------------

    private static Rig PlacementRig(byte health = 40, bool dead = false) {
        var session = new GameSession();
        session.SetActorStatsForTest(0, StatBlock());
        session.SetActiveParty(1, new byte[] { 0 });
        session.SetRosterActorForTest(RosterSlot, StatBlock(health), 12, gridX: 5, gridY: 5, dead: dead);
        session.SetEncounterRosterForTest((int)EncounterNumber, new short[] { RosterSlot });
        session.PositionX = (int)WorldPlacement.CornerOf(2, 0);
        session.PositionY = (int)WorldPlacement.CornerOf(3, 0);

        var rig = new Rig { Session = session };
        rig.Service = new HotspotService(null, null, session, null, random: _ => 0);
        rig.Service.SetZoneForTest(Zone,
            // The chunk key is the party's TILE — registering at (0,0) with the party at (2,3)
            // finds nothing, and "nothing placed" then looks like a placement bug.
            byChunk: new Dictionary<(int x, int y), List<TileEventTrigger>> {
                [(2, 3)] = new List<TileEventTrigger> { CombTrigger() },
            },
            comb: CombTableWithSpawn(EncounterNumber));
        rig.Service.ActivateAtPartyPosition(session.PositionX, session.PositionY);
        return rig;
    }

    private static DefFamilyFile<DefCombEntry> CombTableWithSpawn(long encounter) =>
        new DefFamilyFile<DefCombEntry>("DEF_COMB.DAT",
            new List<DefRecord<DefCombEntry>> {
                new DefRecord<DefCombEntry> {
                    Status = 1,
                    Payload = new DefCombEntry {
                        EncounterNumber = (uint)encounter,
                        EnemySetup = new EncounterActorSetup {
                            SlotCount = 1,
                            Slots = new[] {
                                new EnemySlot {
                                    CreatureNumber = 12,
                                    PrimarySpawnX = 7000, PrimarySpawnY = 8000,
                                    PrimaryRotationZ = 0x0999,
                                },
                            },
                        },
                    },
                },
            });

    [Test]
    public void APENDINGActorIsPlacedAtItsAuthoredSpawnRelativeToThePartysTile() {
        Rig rig = PlacementRig();

        var placed = rig.Service.PlaceEncounterActors();

        Assert.AreEqual(1, placed.Count);
        Assert.AreEqual((int)WorldPlacement.CornerOf(2, 0) + 7000, placed[0].WorldX);
        Assert.AreEqual((int)WorldPlacement.CornerOf(3, 0) + 8000, placed[0].WorldY);
        Assert.AreEqual(12, placed[0].CreatureNumber);
        Assert.IsTrue(placed[0].Roams, "a freshly placed actor walks");
    }

    [Test]
    public void ADEADRosterActorIsNeverSeededAndSoNeverPlaced() {
        // *** DEAD IS A FLAG, NOT A HEALTH OF ZERO. *** rgnenc_load_encounter_actors reads the
        // combat record and tests inner.flags & CAF_DEAD (RGNENC.C:205); it never looks at a stat.
        // This used to pass `health: 0` and so asserted a rule the game does not have — and the port
        // implemented that rule, which called every un-rolled monster dead and seeded nothing.
        // Measured on dir.G01/SAVE07: the original seeded three slots of the party's ref-pair and we
        // seeded none.
        Rig rig = PlacementRig(dead: true);

        CollectionAssert.IsEmpty(rig.Service.PlaceEncounterActors());
    }

    [Test]
    public void ASECONDCallReturnsTheSameListRatherThanRePlacing() {
        // *** THE PASS IS NOT IDEMPOTENT, WHICH IS WHY THE ANSWER IS CACHED. *** The seed promotes
        // pending actors to roaming and writes that back, so re-deriving finds them already placed
        // and takes their STORED pose. Before the pose was stored that was zero, which put every
        // actor at its tile's origin; the cache is what kept callers from reaching it (see
        // WalkingBackIntoAChunkFindsItsActorWhereItWasPlacedNotAtTheTileCorner for the fix).
        //
        // Found by driving the real thing: a probe called this, then the world's own draw called it
        // again, and three Moredhel warriors stacked on one spot.
        Rig rig = PlacementRig();
        var first = rig.Service.PlaceEncounterActors();

        var again = rig.Service.PlaceEncounterActors();

        Assert.AreEqual(1, again.Count);
        Assert.AreEqual(first[0].WorldX, again[0].WorldX);
        Assert.AreEqual((int)WorldPlacement.CornerOf(2, 0) + 7000, again[0].WorldX,
            "still the authored spawn, not the tile origin the second derivation would give");
    }

    [Test]
    public void MovingToANOTHERChunkRebuildsTheList() {
        // The cache is per chunk, not for the session: walking on has to place the next chunk's
        // actors, and walking back must not resurrect the previous list.
        Rig rig = PlacementRig();
        Assert.AreEqual(1, rig.Service.PlaceEncounterActors().Count);

        rig.Session.PositionX = (int)WorldPlacement.CornerOf(9, 0);
        rig.Session.PositionY = (int)WorldPlacement.CornerOf(9, 0);

        CollectionAssert.IsEmpty(rig.Service.PlaceEncounterActors(),
            "chunk (9,9) has no triggers");
    }

    [Test]
    public void WalkingBackIntoAChunkFindsItsActorWhereItWasPlacedNotAtTheTileCorner() {
        // *** A PLACED ACTOR'S POSE IS STORED, NOT JUST ITS KIND. *** The original writes each drawn
        // actor's position back into the block, relative to the party's tile, before anything
        // re-reads it (rgnenc_persist_zone_snapshot, RGNENC.C:385; rgnenc_zone_rectr_save_objects,
        // :338), and its own saves carry those poses. Storing only the kind left the offsets zero,
        // so the next derivation -- a chunk re-entered, a load, or an underground fight's body
        // (rgnenc_persist_actor_placed keeps the stored pose there, :424) -- put the actor on the
        // tile's corner. Found in chapter 9: every zone-12 body lay at (704000,896000).
        Rig rig = PlacementRig();
        rig.Service.PlaceEncounterActors();
        rig.Session.PositionX = (int)WorldPlacement.CornerOf(9, 0);
        rig.Session.PositionY = (int)WorldPlacement.CornerOf(9, 0);
        rig.Service.PlaceEncounterActors();
        rig.Session.PositionX = (int)WorldPlacement.CornerOf(2, 0);
        rig.Session.PositionY = (int)WorldPlacement.CornerOf(3, 0);

        var back = rig.Service.PlaceEncounterActors();

        Assert.AreEqual(1, back.Count);
        Assert.AreEqual((int)WorldPlacement.CornerOf(2, 0) + 7000, back[0].WorldX);
        Assert.AreEqual((int)WorldPlacement.CornerOf(3, 0) + 8000, back[0].WorldY);
        Assert.AreEqual(0x0999, back[0].Facing);
    }

    [Test]
    public void WalkingOffAChunkPutsItsRoamersBackToPending() {
        // rgnenc_zone_rectr_save_objects on the chunk being left (CZONE.C:88, RGNENC.C:338): a roamer
        // is owed a fresh placement, so it comes back at its template spawn with a new walk phase.
        Rig rig = PlacementRig();
        rig.Service.PlaceEncounterActors();
        int at = -1;
        for (var i = 0; i < EncounterObjectStates.EntryCount && at < 0; i++) {
            if (rig.Session.EncounterActorStates[i].Kind == EncounterObjectStates.KindRoaming) {
                at = i;
            }
        }
        Assert.GreaterOrEqual(at, 0, "the fixture places one roamer");

        rig.Service.ActivateAtPartyPosition((int)WorldPlacement.CornerOf(9, 0),
            (int)WorldPlacement.CornerOf(9, 0));

        EncounterObjectStates.Entry left = rig.Session.EncounterActorStates[at];
        Assert.AreEqual(EncounterObjectStates.KindReset, left.Kind);
        Assert.AreEqual(0, left.WorldXOffset);
    }

    [Test]
    public void AStepSizeResetReportsItselfAndTheRePlacementStoresThePoseAgain() {
        // rgnenc_reset_and_save zeroes a roamer's pose and re-places it at once (RGNENC.C:457-476).
        // The true return is what makes WorldRuntime do the re-placing; before, nothing did, and a
        // fight started in the same chunk wrote its bodies from the zeroed pose.
        Rig rig = PlacementRig();
        rig.Service.PlaceEncounterActors();
        rig.Session.LastSeenStepSpeed = 400;

        Assert.IsTrue(rig.Service.OnStepSizeChanged(1600, rig.Session.LastSeenGridStride));
        var again = rig.Service.PlaceEncounterActors();

        Assert.AreEqual(1, again.Count);
        Assert.AreEqual((int)WorldPlacement.CornerOf(2, 0) + 7000, again[0].WorldX);
        int at = -1;
        for (var i = 0; i < EncounterObjectStates.EntryCount && at < 0; i++) {
            if (rig.Session.EncounterActorStates[i].Kind == EncounterObjectStates.KindRoaming) {
                at = i;
            }
        }
        Assert.GreaterOrEqual(at, 0, "the re-placement made the actor a roamer again");
        Assert.AreEqual(7000, rig.Session.EncounterActorStates[at].WorldXOffset,
            "the re-placed roamer's pose is stored again");
    }

    // ---- springing a trap ------------------------------------------------------------------------

    private const int TrapFineX = 4000;
    private const int TrapFineY = 9000;

    private static DefFamilyFile<DefTrapEntry> TrapTable(long encounter) =>
        new DefFamilyFile<DefTrapEntry>("DEF_TRAP.DAT",
            new List<DefRecord<DefTrapEntry>> {
                new DefRecord<DefTrapEntry> {
                    Status = 1,
                    Payload = new DefTrapEntry {
                        EncounterNumber = (uint)encounter,
                        LandingPrimary = new LandingPosition {
                            FineX = TrapFineX, FineY = TrapFineY, RotationZ = 0x2000,
                        },
                        LandingDir1 = Landing(1), LandingDir2 = Landing(2),
                        LandingDir4 = Landing(4), LandingDir8 = Landing(8),
                    },
                },
            });

    private static TileEventTrigger TrapTrigger() => new TileEventTrigger {
        Type = TileEventType.Trap, EntryNumber = 0,
        StartX = 0, StartY = 0, EndX = 7, EndY = 7,
    };

    // A rig whose zone carries the trap table, with the ground seam under our control.
    private static Rig TrapRig(bool room = true, int roll = 99) {
        var session = new GameSession();
        session.SetActorStatsForTest(0, StatBlock());
        session.SetActiveParty(1, new byte[] { 0 });
        session.SetRosterActorForTest(RosterSlot, StatBlock(), 12, gridX: 5, gridY: 5);
        session.SetEncounterRosterForTest((int)EncounterNumber, new short[] { RosterSlot });

        var rig = new Rig { Session = session };
        rig.Service = new HotspotService(null, null, session, null,
            random: _ => roll,
            setCombatMusic: on => rig.Music.Add(on),
            groundKindAt: (x, y) => room
                ? (int)GameData.Resources.World.WorldEntityType.Ground
                : -1,
            resyncCamera: () => rig.CameraResyncs++);
        rig.Service.SetZoneForTest(Zone, comb: CombTable(EncounterNumber),
            start: new GameData.Resources.Config.StartData("START.DAT") { CombatGridCellSize = 300 },
            trap: TrapTable(EncounterNumber));
        return rig;
    }

    [Test]
    public void ATrapPLACESThePartyOnItsLandingBeforeTheFight() {
        // *** THE ONE STRUCTURAL DIFFERENCE FROM AN ENCOUNTER. *** A Comb fight happens where the
        // party stands; a trap yanks them onto its own landing first.
        Rig rig = TrapRig();
        StandAt(rig, 2, 3, cellX: 20, cellY: 20);

        Assert.IsTrue(rig.Service.EnoughGroundToFight(TrapTrigger()));

        Assert.AreEqual((int)WorldPlacement.CornerOf(2, 0) + TrapFineX, rig.Session.PositionX);
        Assert.AreEqual((int)WorldPlacement.CornerOf(3, 0) + TrapFineY, rig.Session.PositionY);
        Assert.AreEqual(unchecked((short)0x2000), rig.Session.Rotation);
        Assert.AreEqual(1, rig.CameraResyncs, "and the view goes with them");
    }

    [Test]
    public void ATrapWithNoRoomPutsThePartyBACKAndDoesNotFire() {
        // The revert. Without it a refused trap leaves the party standing somewhere they never
        // walked to, with nothing having happened to explain it.
        Rig rig = TrapRig(room: false);
        StandAt(rig, 2, 3, cellX: 20, cellY: 20);
        int x = rig.Session.PositionX;
        int y = rig.Session.PositionY;
        short rotation = rig.Session.Rotation;

        Assert.IsFalse(rig.Service.EnoughGroundToFight(TrapTrigger()));

        Assert.AreEqual(x, rig.Session.PositionX);
        Assert.AreEqual(y, rig.Session.PositionY);
        Assert.AreEqual(rotation, rig.Session.Rotation);
    }

    [Test]
    public void FleeingASprungTrapMeasuresTheExitFromWhereTheyWALKEDIN() {
        // *** THE REVERT'S REAL PURPOSE. *** The original restores the pre-spring position before
        // running the outcode, so the party leaves on the side they arrived from. Measured from the
        // trap's own landing instead, the direction — and so the landing — is a different one.
        Rig rig = TrapRig(roll: 0);   // beats the escape chance, as the Comb flight tests do
        StandAt(rig, 0, 0, cellX: 3, cellY: 10);   // past the box's max-Y edge -> direction 1

        rig.Service.EnoughGroundToFight(TrapTrigger());
        // The spring moved them to cell (2.5, 5.6)-ish, which is direction 4 territory.
        rig.Service.StartCombat(TrapTrigger());
        rig.Service.ResolveRetreatForTest(rig.Service.Combat.Encounter.Party[0]);

        Assert.AreEqual(1000, rig.Session.PositionX, "direction 1's landing, not direction 4's");
        Assert.AreEqual(2000, rig.Session.PositionY);
    }

    // ---- the script hook a handful of encounters carry -------------------------------------------

    private static Rig BuildFor(long encounter) {
        var session = new GameSession();
        session.SetActorStatsForTest(0, StatBlock());
        session.SetActiveParty(1, new byte[] { 0 });
        session.SetRosterActorForTest(RosterSlot, StatBlock(), 12, gridX: 5, gridY: 5);
        session.SetEncounterRosterForTest((int)encounter, new short[] { RosterSlot });

        var rig = new Rig { Session = session };
        rig.Service = new HotspotService(null, null, session, null,
            setCombatMusic: on => rig.Music.Add(on));
        rig.Service.SetZoneForTest(Zone, comb: CombTable(encounter));
        return rig;
    }

    private static void Win(Rig rig) {
        rig.Service.StartCombat(CombTrigger());
        foreach (Combatant enemy in rig.Service.Combat.Encounter.Enemies) {
            rig.Service.Combat.Encounter.Kill(enemy);
        }
        rig.Service.EndCombatForTest();
    }

    [Test]
    public void BeatingTheLastOfAGroupSetsTheGroupsFlag() {
        // Encounters 0x83-0x87 earn flag 0xdb1c between them. The hook is asked on EVERY defeat,
        // because nothing knows which one is last.
        Rig rig = BuildFor(0x87);
        foreach (long already in new long[] { 0x83, 0x84, 0x85, 0x86 }) {
            rig.Session.SetGlobalValue(HotspotRules.EncounterFoughtKey(already), 1);
        }

        Assert.AreEqual(0, rig.Session.GetGlobalValue(0xdb1c) ?? 0);
        Win(rig);
        Assert.AreNotEqual(0, rig.Session.GetGlobalValue(0xdb1c) ?? 0);
    }

    [Test]
    public void BeatingAnyOtherMemberOfTheGroupSetsNothingYet() {
        // The discriminating half: with one member outstanding the flag must stay clear, or the
        // group means nothing and the first kill earns it.
        Rig rig = BuildFor(0x87);
        foreach (long already in new long[] { 0x83, 0x84, 0x85 }) {
            rig.Session.SetGlobalValue(HotspotRules.EncounterFoughtKey(already), 1);
        }

        Win(rig);

        Assert.AreEqual(0, rig.Session.GetGlobalValue(0xdb1c) ?? 0);
        Assert.AreNotEqual(0, rig.Session.GetGlobalValue(
            HotspotRules.EncounterFoughtKey(0x87)) ?? 0, "but its own flag is still written");
    }

    [Test]
    public void AnOrdinaryEncounterEarnsNoGroupFlagAtAll() {
        Rig rig = BuildFor(EncounterNumber);
        Win(rig);

        Assert.AreEqual(0, rig.Session.GetGlobalValue(0xdb1c) ?? 0);
        Assert.AreEqual(0, rig.Session.GetGlobalValue(0x1d17) ?? 0);
    }

    // ---- where a party that ran away comes out ---------------------------------------------------

    // Cell coordinates inside a tile, for a hotspot box of StartX 0, EndY 7, EndX 7, StartY 0.
    private static void StandAt(Rig rig, int tileX, int tileY, int cellX, int cellY) {
        rig.Session.PositionX = (int)WorldPlacement.CornerOf(tileX, cellX);
        rig.Session.PositionY = (int)WorldPlacement.CornerOf(tileY, cellY);
    }

    private static void Flee(Rig rig) {
        rig.Service.StartCombat(CombTrigger());
        rig.Service.ResolveRetreatForTest(rig.Service.Combat.Encounter.Party[0]);
    }

    [Test]
    public void AnEscapedRetreatMovesThePartyToTheRecordsLanding() {
        // *** RUNNING AWAY USED TO LEAVE THE PARTY WHERE THEY STOOD. *** Still on the encounter's
        // tile with the hotspot still armed, the next step walks straight back into the fight.
        Rig rig = Build(roll: 0);
        StandAt(rig, 0, 0, cellX: 3, cellY: 10);   // past the box's max-Y edge -> direction 1

        Flee(rig);

        Assert.AreEqual(1000, rig.Session.PositionX);
        Assert.AreEqual(2000, rig.Session.PositionY);
        Assert.AreEqual(unchecked((short)0x1000), rig.Session.Rotation,
            "the heading is part of the landing — without it the party faces back into the encounter");
    }

    [Test]
    public void TheLandingDependsOnWhichSideTheyFinishedOn() {
        // Four positions, four different landings. A relocation wired to one entry would pass a
        // single-position test and be wrong for three quarters of the encounters in the game.
        foreach ((int cellX, int cellY, int expected) in
                 new[] { (3, 10, 1), (0, 3, 2), (3, 0, 4), (3, 3, 8) }) {
            Rig rig = Build(roll: 0);
            StandAt(rig, 0, 0, cellX, cellY);

            Flee(rig);

            Assert.AreEqual(1000 * expected, rig.Session.PositionX,
                $"standing at cell {cellX},{cellY}");
            Assert.AreEqual(2000 * expected, rig.Session.PositionY);
        }
    }

    [Test]
    public void TheLandingIsAnOffsetInsideTheirOWNTile_NotAWorldPosition() {
        // The discriminating case: from a tile that is not the origin. Treating the stored values as
        // absolute would drop every fleeing party at the same handful of coordinates near 0,0
        // whatever map they were on.
        Rig rig = Build(roll: 0);
        StandAt(rig, 2, 3, cellX: 3, cellY: 10);   // direction 1 again

        Flee(rig);

        Assert.AreEqual((int)WorldPlacement.CornerOf(2, 0) + 1000, rig.Session.PositionX);
        Assert.AreEqual((int)WorldPlacement.CornerOf(3, 0) + 2000, rig.Session.PositionY);
    }

    [Test]
    public void TheMOVEIsPushedOutToTheCamera() {
        // *** THE PARTY MOVED AND THE VIEW DID NOT. *** The relocation writes GameSession, which is
        // the party's state and not the camera's, so without this the player stayed looking at the
        // spot they fled from until the next keypress dragged the camera along.
        Rig rig = Build(roll: 0);
        StandAt(rig, 0, 0, cellX: 3, cellY: 10);

        Flee(rig);

        Assert.AreEqual(1, rig.CameraResyncs);
    }

    [Test]
    public void AFAILEDRetreatResyncsNothingBecauseNothingMoved() {
        Rig rig = Build(roll: 99);
        StandAt(rig, 0, 0, cellX: 3, cellY: 10);

        Flee(rig);

        Assert.AreEqual(0, rig.CameraResyncs);
    }

    [Test]
    public void AFAILEDRetreatLeavesThePartyWhereTheyStood() {
        // The move belongs to the escape, not to the attempt.
        Rig rig = Build(roll: 99);
        StandAt(rig, 0, 0, cellX: 3, cellY: 10);
        int x = rig.Session.PositionX;
        int y = rig.Session.PositionY;

        Flee(rig);

        Assert.AreEqual(x, rig.Session.PositionX);
        Assert.AreEqual(y, rig.Session.PositionY);
    }

    [Test]
    public void AFAILEDRetreatLeavesTheFightRunning() {
        // The other half, and the one an uncontrolled roll hides: the encounter is still
        // remembered, nothing is marked, and the music does NOT go back.
        //
        // *** THIS TEST'S SIBLING FLAKED BEFORE THE ROLL WAS INJECTED. *** It failed one gate run
        // and passed the next with no file change in between, because the escape is a d100 against
        // a small chance and nothing was controlling it.
        Rig rig = Build(roll: 99);       // fails the escape chance
        rig.Service.StartCombat(CombTrigger());
        Combatant member = rig.Service.Combat.Encounter.Party[0];

        rig.Service.ResolveRetreatForTest(member);

        Assert.AreEqual(EncounterNumber, rig.Service.FightingEncounter);
        Assert.AreEqual(0, FoughtFlag(rig));
        CollectionAssert.DoesNotContain(rig.Music, false);
    }
    /// <summary>
    /// A CORPSE CANNOT SWING, AND CANNOT BE WALKED BY A CLICK. (TASK-562)
    /// </summary>
    /// <remarks>
    /// <b>The dead actor keeping Ready is faithful</b> -- the original's kill sets CAF_DEAD and
    /// leaves CAF_READY alone (canassa COMBAT.C:277), exactly as <c>CombatEncounter.Kill</c> does.
    /// The original refuses at the point the ACTING actor is chosen: a dead <c>g_current_actor</c>
    /// never becomes <c>g_acting_actor</c> (CACTOR.C:1451-1457). <c>ResolveCombatTargetClick</c> is
    /// where our architecture reaches that same predicate, and it was the one of three sibling
    /// handlers that did not.
    ///
    /// <para>Found live in the chapter-1 <c>def_trap:1</c> fight: a member killed by crystal ground
    /// read <c>hp0 sta0 Ready, Dead, Knockback</c>, stayed Current, and two melee clicks were
    /// ACCEPTED -- the second moved the body from (2,3) to (2,4). The cell assertion below is that
    /// symptom, not a proxy for it.</para>
    /// </remarks>
    [Test]
    public void ADeadActingCombatantCannotMeleeClick_AndIsNotWalkedByTheAttempt() {
        Rig rig = Build();
        Assert.IsTrue(rig.Service.StartCombat(CombTrigger()));
        CombatEncounter fight = rig.Service.Combat.Encounter;
        for (int i = 0; i < 4 && (fight.Current == null || !fight.Current.IsPartyMember); i++) {
            fight.EndTurn();
            fight.PickNext();
        }
        Combatant acting = fight.Current;
        Assert.IsNotNull(acting, "the fixture needs a party member's turn");
        Assert.IsTrue(acting.IsPartyMember, "the fixture needs a party member's turn");

        // Killed the way the crystal ground kills -- through the encounter's own Kill, so the flags
        // are whatever production would really leave behind rather than a hand-set pair.
        fight.Kill(acting, playAnimation: true, grid: rig.Service.Combat.Grid);
        Assert.IsTrue(acting.IsDead);
        Assert.AreSame(acting, fight.Current,
            "the fixture depends on the corpse still being Current -- that is the reported state, "
            + "and it is what the original does too");
        (int x, int y) = (acting.X, acting.Y);

        // Nothing armed, so this is the melee arm: the exact click that walked the body.
        rig.Service.ResolveCombatTargetClick(0, partyMember: false, isPrimary: true);

        Assert.AreEqual(x, acting.X, "a dead actor must not be walked by a melee click");
        Assert.AreEqual(y, acting.Y, "a dead actor must not be walked by a melee click");
    }
}
}
