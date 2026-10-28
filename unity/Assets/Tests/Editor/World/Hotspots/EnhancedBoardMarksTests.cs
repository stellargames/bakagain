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
    using System.Linq;

    /// <summary>The enhanced board's marks (TASK-972): each one is the click's own rule, asked as a dry run.</summary>
    public class EnhancedBoardMarksTests {
        private const long EncounterNumber = 40;

        private static ActorStat[] StatBlock(byte health, byte speed = 5) {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 20, Max = 99 };
            }
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = health, Max = 99 };
            stats[(int)ActorAttribute.Stamina] = new ActorStat { Base = 20, Max = 99 };
            stats[(int)ActorAttribute.Speed] = new ActorStat { Base = speed, Max = 99 };
            return stats;
        }

        internal static (HotspotService Service, Combatant Acting) PartyTurn(
            byte partyHealth = 99, bool underground = false, int enemies = 1, int roll = 99,
            System.Func<bool> arenaBusy = null, BakAgain.UI.IDialogManager dialogs = null,
            System.Func<BakAgain.UI.Inventory.InventoryMenu> inventory = null,
            System.Func<BakAgain.UI.Character.CharacterSheetScreen> sheet = null,
            System.Func<BakAgain.UI.Combat.ShootMenu> shoot = null,
            System.Action<GameSession> outfit = null, GameData.Resources.Object.ObjectInfoSet objects = null,
            System.Func<BakAgain.UI.Combat.CombatMenu> combatMenu = null) {
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(partyHealth));
            session.SetActiveParty(1, new byte[] { 0 });
            var slots = new List<short>();
            for (short i = 0; i < enemies; i++) {
                session.SetRosterActorForTest((short)(400 + i), StatBlock(40), 12, gridX: 5, gridY: (byte)(5 + i));
                slots.Add((short)(400 + i));
            }
            session.SetEncounterRosterForTest((int)EncounterNumber, slots.ToArray());
            outfit?.Invoke(session);
            var service = new HotspotService(null, null, session, dialogs, random: _ => roll,
                setCombatMusic: _ => { }, resyncCamera: () => { }, arenaBusy: arenaBusy,
                inventoryMenuAccessor: inventory, characterSheetAccessor: sheet, shootMenuAccessor: shoot,
                combatMenuAccessor: combatMenu);
            service.SetZoneForTest(1,
                byChunk: new Dictionary<(int x, int y), List<TileEventTrigger>> {
                    [(0, 0)] = new List<TileEventTrigger> { new TileEventTrigger {
                        Type = TileEventType.Comb, EntryNumber = 0, StartX = 0, StartY = 0, EndX = 7, EndY = 7 } },
                },
                comb: new DefFamilyFile<DefCombEntry>("DEF_COMB.DAT", new List<DefRecord<DefCombEntry>> {
                    new DefRecord<DefCombEntry> { Status = 1, Payload = new DefCombEntry {
                        EncounterNumber = (uint)EncounterNumber } },
                }), underground: underground, objects: objects,
                start: new GameData.Resources.Config.StartData("START.DAT") { CombatGridCellSize = 300 });
            Assert.IsTrue(service.StartCombat(new TileEventTrigger {
                Type = TileEventType.Comb, EntryNumber = 0, StartX = 0, StartY = 0, EndX = 7, EndY = 7 }));
            CombatEncounter fight = service.Combat.Encounter;
            // The real picker: it re-reads Speed through the health scaling (CBENC.C:726-738).
            service.Combat.AdvanceToPartyTurn();
            Assert.IsTrue(fight.Current?.IsPartyMember ?? false, "the fixture needs a party member's turn");
            return (service, fight.Current);
        }

        /// <summary>Put everyone where the test says; the grid's occupancy follows.</summary>
        internal static void Place(HotspotService s, Combatant acting, (int X, int Y) at, int speed,
            params (int Index, int X, int Y)[] foes) {
            CombatGrid grid = s.Combat.Grid;
            foreach (Combatant c in s.Combat.Encounter.AllCombatants()) {
                grid.SetOccupied(c.X, c.Y, false);
            }
            acting.X = at.X; acting.Y = at.Y; acting.Speed = speed;
            grid.SetOccupied(at.X, at.Y, true);
            foreach ((int i, int x, int y) in foes) {
                Combatant foe = s.Combat.Encounter.Enemies[i];
                foe.X = x; foe.Y = y;
                grid.SetOccupied(x, y, true);
            }
        }

        [Test]
        public void TheMarksAreComputedOncePerChangeOfWhatTheyRead() {
            // Final review I-2: the shapes, the plates and the HUD poll all ask every frame; ~200 dry-run
            // walks must run once per change, not once per ask.
            var (s, acting) = PartyTurn();
            Place(s, acting, (3, 3), 2, (0, 6, 6));
            for (int frame = 0; frame < 10; frame++) {
                s.EnhancedBoardMarks();
                s.EnhancedBoardShapes(0.4f);
                s.EnhancedFoePlates();
            }
            Assert.AreEqual(1, s.BoardMarksComputed, "ten frames, nothing changed: one computation");

            Place(s, acting, (3, 4), 2, (0, 6, 6));
            Assert.IsTrue(s.EnhancedBoardMarks().Reach.Contains((3, 6)), "the move is seen");
            Assert.AreEqual(2, s.BoardMarksComputed);

            s.CursorCell = (1, 1);
            s.EnhancedBoardMarks();
            s.Combat.Grid.SetOccupied(2, 2, true);
            Assert.IsFalse(s.EnhancedBoardMarks().Reach.Contains((2, 2)), "an occupied cell drops out of reach");
            s.Combat.Encounter.Enemies[0].Health = 0;
            s.EnhancedBoardMarks();
            Assert.AreEqual(5, s.BoardMarksComputed, "the cursor, the grid and a foe's pool each recompute");
        }

        [Test]
        public void ReachIsEveryCellTheActorCanWalkTo() {
            var (s, acting) = PartyTurn();
            Place(s, acting, (3, 3), 2, (0, 6, 6));
            BoardMarks marks = s.EnhancedBoardMarks();
            var expected = new HashSet<(int, int)>();
            for (int x = 0; x < CombatGrid.Width; x++) {
                for (int y = 0; y < CombatGrid.Height; y++) {
                    if (System.Math.Max(System.Math.Abs(x - 3), System.Math.Abs(y - 3)) <= 2 && (x, y) != (3, 3)) {
                        expected.Add((x, y));
                    }
                }
            }
            expected.Remove((0, 0)); // never playable (CMBTGRID.C:724-725)
            CollectionAssert.AreEquivalent(expected, marks.Reach);
        }

        [Test]
        public void ReachUsesTheHealthScaledSpeed() {
            // The runtime re-reads Speed through the health scaling at every pick (CBENC.C:726-738);
            // marks take Combatant.Speed as the click does, so a wounded actor's reach is smaller.
            var (full, fa) = PartyTurn(partyHealth: 99);
            var (half, ha) = PartyTurn(partyHealth: 40);
            Assert.Less(ha.Speed, fa.Speed, "the fixture: wounded is slower");
            Assert.Less(half.EnhancedBoardMarks().Reach.Count, full.EnhancedBoardMarks().Reach.Count);
        }

        [Test]
        public void ThrustMarksOnlyEnemiesWithAReachableStraightSide() {
            var (s, acting) = PartyTurn(enemies: 2);
            Place(s, acting, (2, 2), 5, (0, 5, 5), (1, 5, 8));
            Assert.AreEqual(new[] { 0 }, s.EnhancedBoardMarks().ThrustTargets.OrderBy(i => i).ToArray(),
                "the far one is out of reach");
            foreach ((int x, int y) in new[] { (6, 5), (4, 5), (5, 6), (5, 4) }) {
                s.Combat.Grid.SetOccupied(x, y, true);
            }
            Assert.IsEmpty(s.EnhancedBoardMarks().ThrustTargets, "every straight side held: no approach");
        }

        [Test]
        public void SwingMarksOnlyStraightSideNeighbours_NotDiagonals() {
            var (s, acting) = PartyTurn(enemies: 2);
            Place(s, acting, (4, 4), 5, (0, 4, 5), (1, 5, 5));
            BoardMarks marks = s.EnhancedBoardMarks();
            CollectionAssert.AreEquivalent(new[] { 0 }, marks.SwingTargets);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, marks.ThrustTargets, "a diagonal foe is engaged by a thrust");
        }

        [Test]
        public void UndergroundReachStopsAtRowSix() {
            var (s, acting) = PartyTurn(underground: true);
            Place(s, acting, (3, 4), 12, (0, 6, 6));
            BoardMarks marks = s.EnhancedBoardMarks();
            Assert.IsNotEmpty(marks.Reach);
            Assert.AreEqual(6, marks.Reach.Max(c => c.Row));
        }

        [Test]
        public void ThePathIsTheWalkTheThrustWillTake() {
            // TASK-970, recorded in the original: Locklear (2,1) at the foe (5,2), (4,2) held.
            var (s, acting) = PartyTurn();
            Place(s, acting, (2, 1), 4, (0, 5, 2));
            s.Combat.Grid.SetOccupied(4, 2, true);
            CollectionAssert.AreEqual(new[] { (2, 1), (3, 1), (4, 1), (5, 1) }, s.ForecastPath(0, 5, 2));
            Assert.AreEqual((2, 1), (acting.X, acting.Y), "a forecast moves nobody");
            Assert.IsEmpty(s.ForecastPath(-1, 7, 12), "an unreachable cell has no path");
            CollectionAssert.AreEqual(new[] { (2, 1), (3, 1) }, s.ForecastPath(-1, 3, 1));
        }

        [Test]
        public void LettersFollowRosterOrder_AndStayWhenAFoeDies() {
            var (s, acting) = PartyTurn(enemies: 2);
            Place(s, acting, (2, 2), 5, (0, 5, 5), (1, 5, 6));
            s.Combat.Encounter.Enemies[0].Flags |= CombatantFlags.Dead;
            BoardMarks marks = s.EnhancedBoardMarks();
            Assert.AreEqual('A', marks.FoeLetters[0]);
            Assert.AreEqual('B', marks.FoeLetters[1]);
            CollectionAssert.DoesNotContain(marks.ThrustTargets, 0);
        }
    }
}
