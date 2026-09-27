namespace BakAgain.Tests.Editor.World.Collision {
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.World;
    using BakAgain.World.Collision;
    using BakAgain.World.Converters;
    using GameData.Resources.Config;
    using GameData.Resources.World;
    using NUnit.Framework;

    /// <summary>
    /// Walking into a pit — the wiring between the recorded crossing kind and
    /// <see cref="PitDescent"/>.
    /// </summary>
    /// <remarks>
    /// <b>The bar this task set for itself is that the party MOVES.</b> A pit that downs them where
    /// they stand is worse than no pit, so the assertion that matters most here is the position
    /// change; the rest is the effects that ride along with it.
    ///
    /// <para>The condition half is not asserted here: afflictions are only populated from a loaded
    /// save, so a bare session has none and <c>ApplyToParty</c> skips nulls. That rule is covered
    /// where it lives, in the .NET suite.</para>
    /// </remarks>
    public class PitDescentWiringTests {
        private const int Ground = 0;
        private const int Pit = 15;
        private const int ZoneCameraZ = 5000;

        /// <summary>
        /// Where one forward step lands underground: the medium step, quartered.
        /// </summary>
        /// <remarks>
        /// <b>A dungeon quarters the step</b>, so the 800 an above-ground medium step covers is 200
        /// here. Placing the pit at 800 put it three steps away and the first cut of these tests
        /// walked straight past it.
        /// </remarks>
        private const int ForwardStep = 200;

        private static MovementData Table() => new MovementData("MOVEMENT.DAT") {
            StepDistances = new[] { 100, 800, 1600 },
            TurnAngles = new[] { 0x1000, 0x2000, 0x4000 },
            SecondsPerStep = new[] { 60, 120, 240 },
        };

        private static GidRegion Square(int half, short baseElevation = 0) {
            var region = new GidRegion { BaseElevation = baseElevation };
            region.Subedges.Add(new GidSubedge { Dx = 0, Dy = 125, AnchorX = (short)-half, AnchorY = (short)half });
            region.Subedges.Add(new GidSubedge { Dx = 125, Dy = 0, AnchorX = (short)half, AnchorY = (short)half });
            region.Subedges.Add(new GidSubedge { Dx = 0, Dy = -125, AnchorX = (short)half, AnchorY = (short)-half });
            region.Subedges.Add(new GidSubedge { Dx = -125, Dy = 0, AnchorX = (short)-half, AnchorY = (short)-half });
            return region;
        }

        private static ZoneTableEntry Entry(int kind, GidRegion region, short radius) {
            var entry = new ZoneTableEntry {
                Dat = new TableDatInfo { EntityType = (WorldEntityType)(byte)kind },
                Gid = new TableGidInfo { XRadius = radius, YRadius = radius },
            };
            if (region != null) {
                entry.Gid.Regions.Add(region);
            }
            return entry;
        }

        private static FilterData AlwaysDraw() {
            var filter = new FilterData("FILTER.DAT");
            for (int level = 0; level < FilterData.DetailLevelCount; level++) {
                var block = new DetailLevelFilter { Level = level };
                for (int k = 0; k < FilterData.EntityTypeCount; k++) {
                    block.DrawDistances[k] = 1;
                }
                block.DrawDistances[7] = -1;
                filter.DetailLevels.Add(block);
            }
            return filter;
        }

        /// <summary>
        /// Ground everywhere, a pit polygon straight ahead, and optional extra pit ENTITIES to land
        /// on. IndexInTile matters: the automap list — the one that keeps pits — requires it.
        /// </summary>
        private static ProximityWorld World(params (int kind, int x, int y, int half)[] extras) {
            var records = new List<ProximityRecord> {
                new ProximityRecord(Entry(Ground, Square(16000), 16000), 0, 12000, 0, 0,
                    indexInTile: 0),
                // The pit the party walks onto: walkable, so it is a crossing rather than a block.
                // Centred on where an UNDERGROUND step lands — the quartered step is 200, not 800 —
                // and small enough that the party's start (0) and a backward step (-200) are both
                // outside it, so only the forward step crosses it.
                new ProximityRecord(Entry(Pit, Square(150), 150), 0, ForwardStep, 0, 0,
                    indexInTile: 1),
            };
            var index = 2;
            foreach (var (kind, x, y, half) in extras) {
                records.Add(new ProximityRecord(Entry(kind, Square(half), (short)half), x, y, 0, 0,
                    indexInTile: index++));
            }
            return new ProximityWorld(records, AlwaysDraw());
        }

        /// <summary>
        /// The same world, but with the pit polygon kept OFF the automap list.
        /// </summary>
        /// <remarks>
        /// <b>This is the only way to reach the no-target branch, and finding that out was the
        /// point.</b> A plain <c>World()</c> with no extra pit entities still lands the party
        /// somewhere: the polygon they walked ONTO is itself kind 15 and sits in the automap list,
        /// so <c>SelectTarget</c> picks it and they "fall" onto their own position. The original is
        /// the same — <c>proxscan_paged_find_next_type0f</c> scans the visible list, which contains
        /// the pit you are standing in — so in practice the branch is reached through
        /// <c>g_full_redraw_needed</c> rather than through an absent pit.
        ///
        /// <para><c>indexInTile: -1</c> is what excludes a record from the automap set while leaving
        /// it in the collision set, so the step still crosses a pit and the lookup still fails.</para>
        /// </remarks>
        private static ProximityWorld WorldWithNothingToFallTo() =>
            new ProximityWorld(new List<ProximityRecord> {
                new ProximityRecord(Entry(Ground, Square(16000), 16000), 0, 12000, 0, 0,
                    indexInTile: 0),
                new ProximityRecord(Entry(Pit, Square(150), 150), 0, ForwardStep, 0, 0,
                    indexInTile: -1),
            }, AlwaysDraw());

        private sealed class Rig {
            public PartyMovement Movement;
            public GameSession Session;
            public readonly List<int> Sounds = new();
            public readonly List<int> Dialogs = new();
        }

        private static Rig Build(ProximityWorld world, bool underground = true) {
            var rig = new Rig { Session = new GameSession { PositionX = 0, PositionY = 0, Rotation = 0 } };
            var prefs = new FakePreferencesService();
            prefs.Current.StepSize = StepSize.Medium;
            prefs.Current.TurnSize = TurnSize.Large;
            rig.Movement = new PartyMovement(rig.Session, Table(), prefs, camera: null,
                cameraHeightZ: ZoneCameraZ, cameraPitch: 0, collision: world,
                playSfx: id => rig.Sounds.Add(id), underground: underground,
                showDialog: id => rig.Dialogs.Add(id));
            return rig;
        }

        [Test]
        public void TheStepOntoAPitDoesNotFallYET() {
            // *** A frame late, on purpose. *** The original reads the crossing kind recorded on the
            // PREVIOUS iteration, so the step onto the pit renders before the floor gives way.
            // Falling inside the step teleports the party out of a tile nobody saw them enter.
            Rig rig = Build(World((Pit, 4000, 4000, 400)));

            rig.Movement.MoveForward();

            Assert.AreEqual(ForwardStep, rig.Session.PositionY, "the step landed on the pit");
            CollectionAssert.IsEmpty(rig.Dialogs);
            Assert.AreEqual(0, rig.Session.PartyDeathState);
        }

        [Test]
        public void TheNextWorldLoopIterationDropsThem() {
            Rig rig = Build(World((Pit, 4000, 4000, 400)));
            rig.Movement.MoveForward();

            Assert.IsTrue(rig.Movement.TickPendingDescent());

            Assert.AreEqual(PitDescent.PartyDeathStateOnFall, rig.Session.PartyDeathState);
            Assert.Contains(PitDescent.FallSoundId, rig.Sounds);

            // The dialog waits for the last frame of the drop, as the original's does — it sits
            // after the descent loop in WORLDCRS.C. Raising it on the first tick would put a modal
            // over the fall it describes.
            CollectionAssert.IsEmpty(rig.Dialogs);
            Fall(rig);
            Assert.Contains(PitDescent.LandingDialogId, rig.Dialogs);
        }

        /// <summary>Runs a started fall to its last frame.</summary>
        private static void Fall(Rig rig) {
            for (var i = 0; i < PitDescent.DescentSteps && rig.Movement.IsFalling; i++) {
                rig.Movement.TickPendingDescent();
            }
        }

        [Test]
        public void TheDropTakesNINEFRAMESAndDESCENDS() {
            Rig rig = Build(World((Pit, 4000, 4000, 400)));
            rig.Movement.MoveForward();
            rig.Movement.TickPendingDescent();

            int atTheLip = rig.Session.PositionZ;
            var heights = new System.Collections.Generic.List<int>();
            for (var i = 0; i < PitDescent.DescentSteps + 2; i++) {
                if (!rig.Movement.IsFalling) {
                    break;
                }
                rig.Movement.TickPendingDescent();
                heights.Add(rig.Session.PositionZ);
            }

            // Eight ticks drop the eye (frame 0 was drawn by the tick that started the fall), and a
            // ninth ends the descent and re-grounds — so nine ticks, of which eight are the drop.
            Assert.AreEqual(PitDescent.DescentSteps, heights.Count);
            for (var i = 1; i < PitDescent.DescentSteps - 1; i++) {
                Assert.Less(heights[i], heights[i - 1], "the eye only ever falls");
            }
            Assert.Less(heights[PitDescent.DescentSteps - 2], atTheLip,
                "the last frame of the drop is below the lip it started at");
            Assert.AreEqual(atTheLip + PitDescent.DropAtStep(PitDescent.DescentSteps - 1),
                heights[PitDescent.DescentSteps - 2],
                "the fall covers steps-1 increments, not steps");
        }

        [Test]
        public void FrameZeroIsTheUNDROPPEDHeight() {
            // *** The off-by-one an obvious implementation makes. *** The original's loop is
            // `for (i = 0; i < steps; i++) z = saved_z - i * 0x50`, so the FIRST frame shows the
            // party at the height they fell from and a nine-frame fall descends eight steps.
            // Counting from one drops them 0x50 further than the original on every fall.
            Rig rig = Build(World((Pit, 4000, 4000, 400)));
            rig.Movement.MoveForward();
            int beforeTheFall = rig.Session.PositionZ;

            rig.Movement.TickPendingDescent();

            Assert.AreEqual(beforeTheFall, rig.Session.PositionZ);
            Assert.AreEqual(0, PitDescent.DropAtStep(0));
        }

        [Test]
        public void NothingElseHappensWhileTheFallIsRunning() {
            // The fall owns the frame: TickPendingDescent reports false throughout, so a caller
            // keying off its return does not re-trigger, and the dialog fires exactly once.
            Rig rig = Build(World((Pit, 4000, 4000, 400)));
            rig.Movement.MoveForward();
            rig.Movement.TickPendingDescent();

            while (rig.Movement.IsFalling) {
                Assert.IsFalse(rig.Movement.TickPendingDescent());
            }

            Assert.AreEqual(1, rig.Dialogs.Count);
            Assert.IsFalse(rig.Movement.TickPendingDescent());
            Assert.AreEqual(1, rig.Dialogs.Count);
        }

        [Test]
        public void ThePartyACTUALLYMOVES_whichIsTheBarThisTaskSet() {
            // A pit that downs the party without moving them is worse than no pit. The target is
            // the pit ENTITY, not the polygon they walked onto.
            Rig rig = Build(World((Pit, 4000, 4000, 400)));
            rig.Movement.MoveForward();

            rig.Movement.TickPendingDescent();

            // On the FIRST tick, before a single frame of the drop — the original writes the target
            // x/y before the descent loop and varies nothing but z inside it.
            Assert.AreEqual(4000, rig.Session.PositionX);
            Assert.AreEqual(4000, rig.Session.PositionY);
        }

        [Test]
        public void WithNoPitEntityInRangeTheyAreStillDropped_justNotMoved() {
            // The lookup sits INSIDE the animation branch; the condition, sound and dialog sit
            // outside it. "Nowhere to fall to" is a handled case, not an error.
            Rig rig = Build(WorldWithNothingToFallTo());
            rig.Movement.MoveForward();
            int x = rig.Session.PositionX, y = rig.Session.PositionY;

            Assert.IsTrue(rig.Movement.TickPendingDescent());

            Assert.AreEqual(PitDescent.PartyDeathStateOnFall, rig.Session.PartyDeathState);
            Assert.Contains(PitDescent.LandingDialogId, rig.Dialogs);
            Assert.AreEqual(x, rig.Session.PositionX);
            Assert.AreEqual(y, rig.Session.PositionY);
        }

        [Test]
        public void APitDoesNothingABOVEGround() {
            // The whole routine is gated on g_game_mode == 2. Hoisting the pit rule above the mode
            // test would open holes in every outdoor zone that happens to use the kind.
            Rig rig = Build(World((Pit, 4000, 4000, 400)), underground: false);
            rig.Movement.MoveForward();

            Assert.IsFalse(rig.Movement.TickPendingDescent());
            Assert.AreEqual(0, rig.Session.PartyDeathState);
            CollectionAssert.IsEmpty(rig.Dialogs);
        }

        [Test]
        public void TheCrossingIsCONSUMED_soATickWithoutAStepDoesNothing() {
            // The recorded kind is a one-shot. Left set, every following frame would drop the party
            // again — near-death, sound and dialog once per frame.
            Rig rig = Build(World((Pit, 4000, 4000, 400)));
            rig.Movement.MoveForward();
            Assert.IsTrue(rig.Movement.TickPendingDescent());
            Fall(rig);

            Assert.IsFalse(rig.Movement.TickPendingDescent());
            Assert.AreEqual(1, rig.Dialogs.Count);
        }

        [Test]
        public void AnOrdinaryStepArmsNothing() {
            Rig rig = Build(World((Pit, 4000, 4000, 400)));

            rig.Movement.TurnLeft();
            rig.Movement.TurnLeft();
            rig.Movement.MoveForward();   // away from the pit polygon

            Assert.IsFalse(rig.Movement.TickPendingDescent());
            Assert.AreEqual(0, rig.Session.PartyDeathState);
        }
    }
}
