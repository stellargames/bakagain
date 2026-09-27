namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using GameData.Resources.World;
    using NUnit.Framework;

    /// <summary>
    /// A fight's BODIES stay where the fight left them — <c>combat_actor_deploy_encounter</c>
    /// (CACTOR.C:343). It walks <c>g_active_combatants</c>, which holds only the dead
    /// (CACTOR.C:152-156, COMBAT.C:227), so a survivor is never written (TASK-534 corrected TASK-239).
    /// </summary>
    /// <remarks>
    /// <b>These assert the SAVE's block, not the combatant.</b> Sibling to
    /// <see cref="CombatRemovalPersistenceTests"/>, which covers the deaths: that one records who is
    /// gone, this one records where the living ended up. With only the first, running from a fight
    /// and coming back finds the enemies at their authored posts.
    /// </remarks>
    public class CombatSurvivorPersistenceTests {
        private const int OrdinaryClass = 12;
        private const int RefPair = 3;
        private const int RecordIndex = 2;
        private const int CellSize = 700;

        // A tile index that is NOT zero, because zero is what hides an absolute-vs-offset mix-up.
        private const int PartyTile = 3;

        private static ActorStat[] StatBlock(byte health) {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 20, Max = 99 };
            }
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = health, Max = 99 };
            stats[(int)ActorAttribute.Stamina] = new ActorStat { Base = 0, Max = 99 };
            stats[(int)ActorAttribute.Speed] = new ActorStat { Base = 5, Max = 99 };
            return stats;
        }

        private static GameData.Resources.Data.SaveGameCombatData EntryAt(byte x, byte y) =>
            new GameData.Resources.Data.SaveGameCombatData(
                0, 0, x, y, 0xff, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        private static PartyCombatEntries Entries() =>
            new PartyCombatEntries("P1.DAT",
                new System.Collections.Generic.List<GameData.Resources.Data.SaveGameCombatData> {
                    EntryAt(3, 4),
                });

        /// <summary>One party member against <paramref name="enemies"/> healthy monsters.</summary>
        private static (CombatRuntime Runtime, GameSession Session, CombatEncounter Fight) Fight(
            int enemies = 1, short rotation = 0) {
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(40));
            session.SetActiveParty(1, new byte[] { 0 });

            var roster = new short[enemies];
            for (var i = 0; i < enemies; i++) {
                roster[i] = (short)(400 + i);
                session.SetRosterActorForTest(roster[i], StatBlock(40), OrdinaryClass,
                    gridX: 5, gridY: 5);
            }

            // A position inside a non-zero tile, so "absolute" and "offset from the tile" cannot be
            // mistaken for each other.
            session.PositionX = (PartyTile * WorldPlacement.TileSize) + 1000;
            session.PositionY = (PartyTile * WorldPlacement.TileSize) + 2000;
            session.Rotation = rotation;

            var runtime = new CombatRuntime(session, null, Entries());
            CombatEncounter fight = runtime.EnterRoster(roster, null,
                new EncounterActorPersistence.RecordAddress(RefPair, RecordIndex));
            return (runtime, session, fight);
        }

        private static EncounterObjectStates.Entry StateOf(GameSession session, int slot) =>
            session.EncounterActorStates[EncounterObjectStates.IndexOf(RefPair, RecordIndex, slot)];

        [Test]
        public void ABodyIsRecordedAsPlaced() {
            (CombatRuntime runtime, GameSession session, CombatEncounter fight) = Fight();
            fight.Enemies[0].X = 2;
            fight.Enemies[0].Y = 6;
            runtime.KillForTest(fight.Enemies[0]);

            runtime.PersistSurvivors(CellSize, underground: false);

            Assert.AreEqual(EncounterObjectStates.KindStanding, StateOf(session, 0).Kind);
        }

        [Test]
        public void THEPOSEIsAnOffsetFromThePartysTile_NotAWorldPosition() {
            // *** THE TEST THIS FILE EXISTS FOR. *** combatgrid_tile_to_world_rotated ends by
            // subtracting (camera / 64000) * 64000, and EncounterActorPlacement adds the party
            // tile's origin back when it reads. Storing an absolute position passes every smoke
            // test in tile 0 and puts survivors a whole tile out everywhere else.
            (CombatRuntime runtime, GameSession session, CombatEncounter fight) = Fight();
            fight.Enemies[0].X = 2;
            fight.Enemies[0].Y = 6;
            runtime.KillForTest(fight.Enemies[0]);

            runtime.PersistSurvivors(CellSize, underground: false);

            (int across, int away) = CombatArenaPlacement.CellOffset(2, 6, CellSize);
            long originX = (long)PartyTile * WorldPlacement.TileSize;
            long originY = (long)PartyTile * WorldPlacement.TileSize;

            EncounterObjectStates.Entry stored = StateOf(session, 0);
            Assert.AreEqual(session.PositionX + across - originX, stored.WorldXOffset,
                "x offset must be measured from the party's TILE, not from the world origin");
            Assert.AreEqual(session.PositionY + away - originY, stored.WorldYOffset,
                "y offset must be measured from the party's TILE, not from the world origin");
        }

        [Test]
        public void ASurvivorIsNotRecorded() {
            // Only the dead are in g_active_combatants: a living enemy keeps its state word, so a
            // fled fight's survivors are not turned into bodies.
            (CombatRuntime runtime, GameSession session, CombatEncounter fight) = Fight();

            runtime.PersistSurvivors(CellSize, underground: false);

            Assert.AreNotEqual(EncounterObjectStates.KindStanding, StateOf(session, 0).Kind);
        }

        [Test]
        public void UNDERGROUNDTheStoredPoseIsKept() {
            // MarkPlaced's own rule: underground the kind is written and the pose is left alone, so
            // a dungeon fight must not move its survivors.
            (CombatRuntime runtime, GameSession session, CombatEncounter fight) = Fight();
            fight.Enemies[0].X = 2;
            fight.Enemies[0].Y = 6;

            runtime.KillForTest(fight.Enemies[0]);
            runtime.PersistSurvivors(CellSize, underground: true);

            EncounterObjectStates.Entry stored = StateOf(session, 0);
            Assert.AreEqual(EncounterObjectStates.KindStanding, stored.Kind);
            Assert.AreEqual(0, stored.WorldXOffset, "underground keeps the pose it already had");
            Assert.AreEqual(0, stored.WorldYOffset, "underground keeps the pose it already had");
        }

        [Test]
        public void TWOSurvivorsOnOneTileDoNotBothPersistThere() {
            // combat_actor_visible_at_tile walks the second one forward, and ASSIGNS the walked tile
            // back onto the actor. Two entries with one pose would stack them on a revisit.
            (CombatRuntime runtime, GameSession session, CombatEncounter fight) = Fight(enemies: 2);
            fight.Enemies[0].X = fight.Enemies[1].X = 2;
            fight.Enemies[0].Y = fight.Enemies[1].Y = 6;
            runtime.KillForTest(fight.Enemies[0]);
            runtime.KillForTest(fight.Enemies[1]);

            runtime.PersistSurvivors(CellSize, underground: false);

            // *** THE FIRST ONE MOVES, NOT THE SECOND — AND THAT IS NOT A TYPO. ***
            // combat_actor_visible_at_tile sees the WHOLE live field, including actors this pass has
            // not reached yet. So slot 0 looks at (2,6), finds slot 1 already standing there, and
            // walks; slot 1 then finds (2,6) vacated and keeps it. The intuitive reading — "the
            // later one gives way" — is what a snapshot of occupancy taken up front would produce,
            // and it is the wrong model.
            Assert.AreEqual((3, 6), (fight.Enemies[0].X, fight.Enemies[0].Y),
                "slot 0 walks forward, because slot 1 is already on its tile");
            Assert.AreEqual((2, 6), (fight.Enemies[1].X, fight.Enemies[1].Y),
                "slot 1 keeps the tile slot 0 vacated");
            Assert.AreNotEqual(
                (StateOf(session, 0).WorldXOffset, StateOf(session, 0).WorldYOffset),
                (StateOf(session, 1).WorldXOffset, StateOf(session, 1).WorldYOffset));
        }

        [Test]
        public void THEFACINGTurnsWithTheParty_BecauseItIsComposedFromTheCamera() {
            // The arena is laid out along the party's line of sight, so the same monster on the same
            // tile persists differently depending on which way the party was looking. A port that
            // stored the actor's own yaw gets a plausible number that is wrong by however far the
            // party had turned.
            (CombatRuntime north, GameSession northSession, CombatEncounter northFight) = Fight();
            northFight.Enemies[0].X = 2;
            northFight.Enemies[0].Y = 6;
            north.KillForTest(northFight.Enemies[0]);
            north.PersistSurvivors(CellSize, underground: false);

            (CombatRuntime east, GameSession eastSession, CombatEncounter eastFight) =
                Fight(rotation: 0x4000);
            eastFight.Enemies[0].X = 2;
            eastFight.Enemies[0].Y = 6;
            east.KillForTest(eastFight.Enemies[0]);
            east.PersistSurvivors(CellSize, underground: false);

            Assert.AreNotEqual(StateOf(northSession, 0).Facing, StateOf(eastSession, 0).Facing);
            Assert.AreEqual(
                unchecked((short)(StateOf(northSession, 0).Facing + 0x4000)),
                StateOf(eastSession, 0).Facing);
        }

        [Test]
        public void THEFACINGFollowsTheCombatantsOwnOctant_NotAConstant() {
            // The call site passed a literal 0 until the arena had anywhere to keep a facing, which
            // persisted every survivor looking back down the party's bearing however far it had
            // itself turned during the fight (TASK-242). Two survivors on the SAME tile with the
            // SAME camera differ only by their octant, so anything shared cancels and what is left
            // is the thing under test.
            (CombatRuntime zero, GameSession zeroSession, CombatEncounter zeroFight) = Fight();
            zeroFight.Enemies[0].X = 2;
            zeroFight.Enemies[0].Y = 6;
            zeroFight.Enemies[0].FacingOctant = 0;
            zero.KillForTest(zeroFight.Enemies[0]);
            zero.PersistSurvivors(CellSize, underground: false);

            (CombatRuntime turned, GameSession turnedSession, CombatEncounter turnedFight) = Fight();
            turnedFight.Enemies[0].X = 2;
            turnedFight.Enemies[0].Y = 6;
            turnedFight.Enemies[0].FacingOctant = 2;
            turned.KillForTest(turnedFight.Enemies[0]);
            turned.PersistSurvivors(CellSize, underground: false);

            // Two octants is a quarter turn: FacingFor multiplies the index by an eighth.
            Assert.AreNotEqual(StateOf(zeroSession, 0).Facing, StateOf(turnedSession, 0).Facing,
                "a turned survivor must not persist the same heading as an unturned one");
            Assert.AreEqual(
                unchecked((short)(StateOf(zeroSession, 0).Facing + 0x4000)),
                StateOf(turnedSession, 0).Facing);
        }
    }
}
