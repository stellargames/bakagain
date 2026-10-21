namespace BakAgain.Tests.PlayMode.World {
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.Tests.Editor.World;
    using BakAgain.Tests.PlayMode.UI.Inventory;
    using BakAgain.World;
    using BakAgain.World.Collision;
    using BakAgain.World.Hotspots;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Config;
    using GameData.Resources.Data;
    using GameData.Resources.World;
    using NUnit.Framework;

    /// <summary>
    /// A town gate, stepped onto: asked from the gate cell, walked on the overhead map, and charged
    /// for the step only after the visit (TASK-906 items 1 and 2).
    /// </summary>
    /// <remarks>
    /// The original moves the party onto the gate, asks there, and only then puts it back
    /// (<c>worldmove_party_attempt_move</c>, WORLDMOV.C:113-135, around
    /// <c>hotspotevt_show_record_message</c>, HOTSPOT.C:618). The town runs from the dispatch pass and
    /// the step's time is charged after it (WORLDLP.C:190-198), so the "Locklear yawned" a step can
    /// raise comes on leaving the town. The port rolled the step back and charged it before the
    /// question was even on screen, and the yawn knocked the question away. Drives the real
    /// <see cref="PartyMovement"/>, activator, dispatcher and <see cref="HotspotService"/>; only the
    /// dialogs, the map presentation and the world are stand-ins.
    /// </remarks>
    public class TownGateStepTests {
        private const int Cell = RoadTravel.CellSize;
        private const short East = unchecked((short)0xC000);
        internal const long TicksPerLargeStep = 240 / 2;

        private static int C(int cell) => (cell * Cell) + RoadTravel.HalfCell;

        internal sealed class HeldAnswer : NoOpDialogs {
            public readonly UniTaskCompletionSource<int> Answer = new();
            public int Asked;

            public override UniTask<int> ShowChoiceIndexById(int id, System.Threading.CancellationToken ct = default) {
                Asked++;
                return Answer.Task;
            }
        }

        internal sealed class Rig {
            public GameSession Session;
            public GameClock Clock;
            public PartyMovement Movement;
            public HotspotService Hotspots;
            public HeldAnswer Dialogs;
            public List<(int X, int Y, ushort Heading)> Walked;
            public (int X, int Y, long Ticks, long MapZ)? AtWalk;
        }

        // LaMut as shipped: the gate is the column x 14 (T011014), the record starts the walk on
        // (16,20) heading west (DEF_TOWN 0: offset 0x1410, heading 0x4000). Road runs 16..12 on row 20.
        // TownEntryFadeOrderTests adds a real location player and a flow, for the visit and what
        // leaving it does (TASK-906 item 3).
        internal static Rig Build(BakAgain.World.Scenes.LocationScenePlayer locations = null,
            System.Func<IGameFlow> flow = null) {
            var rig = new Rig {
                Session = new GameSession { PositionX = C(13), PositionY = C(20), Rotation = East, MapCameraZ = 50000 },
                Dialogs = new HeldAnswer(),
            };
            rig.Clock = new GameClock(rig.Session);
            var records = new List<ProximityRecord> { new(Entry(0, 32000), 32000, 32000, 0, 0) };
            for (int x = 12; x <= 16; x++) {
                records.Add(new ProximityRecord(Entry(1, 700), C(x), C(20), 0, 0));
            }
            var prefs = new FakePreferencesService();
            prefs.Current.StepSize = StepSize.Large;
            rig.Hotspots = new HotspotService(null, null, rig.Session, rig.Dialogs,
                locations: locations, flowAccessor: flow,
                movementAccessor: () => rig.Movement,
                showApproach: path => {
                    rig.Walked = path;
                    rig.AtWalk = (rig.Session.PositionX, rig.Session.PositionY, rig.Clock.Ticks, rig.Session.MapCameraZ);
                    return UniTask.CompletedTask;
                });
            rig.Movement = new PartyMovement(rig.Session,
                new MovementData("MOVEMENT.DAT") {
                    StepDistances = new[] { 400, 800, 1600 },
                    TurnAngles = new[] { 0x1000, 0x2000, 0x4000 },
                    SecondsPerStep = new[] { 60, 120, 240 },
                },
                prefs, camera: null, cameraHeightZ: 0, cameraPitch: 0,
                collision: new ProximityWorld(records, AlwaysDraw()),
                hotspotPass: rig.Hotspots.ActivateAtPartyPosition, clock: rig.Clock);
            rig.Hotspots.SetZoneForTest(1,
                byChunk: new Dictionary<(int x, int y), List<TileEventTrigger>> {
                    [(0, 0)] = new List<TileEventTrigger> {
                        new TileEventTrigger { Type = TileEventType.Town, EntryNumber = 0, StartX = 14, EndX = 14, StartY = 11, EndY = 27 },
                    },
                },
                town: new DefFamilyFile<DefTownEntry>("DEF_TOWN.DAT", new List<DefRecord<DefTownEntry>> {
                    new DefRecord<DefTownEntry> {
                        Status = 1,
                        Payload = new DefTownEntry {
                            GdsSceneNumber = 1, DialogId = 1500008,
                            ApproachTileOffset = 0x1410, ApproachHeading = 0x4000, DoApproachWalk = 1,
                        },
                    },
                }));
            return rig;
        }

        [Test]
        public void TheGateAsksFromTheGateCellAndANoPutsThePartyBackAndChargesTheStep() {
            Rig rig = Build();
            long before = rig.Clock.Ticks;

            rig.Movement.MoveForward();

            Assert.AreEqual(1, rig.Dialogs.Asked, "the gate asks");
            Assert.AreEqual((C(14), C(20)), (rig.Session.PositionX, rig.Session.PositionY),
                "the question is asked with the party on the gate cell, as the original's is");
            Assert.AreEqual(before, rig.Clock.Ticks, "and the step is not charged while it is asked");

            rig.Dialogs.Answer.TrySetResult(1);   // No

            Assert.AreEqual((C(13), C(20)), (rig.Session.PositionX, rig.Session.PositionY), "a No puts the party back");
            Assert.AreEqual(before + TicksPerLargeStep, rig.Clock.Ticks, "and then the step costs its time");
            Assert.IsNull(rig.Walked, "and there is no walk");
        }

        [Test]
        public void AYesWalksTheRoadOnTheMapThenTheVisitThenChargesTheStep() {
            Rig rig = Build();
            long before = rig.Clock.Ticks;
            rig.Movement.MoveForward();

            rig.Dialogs.Answer.TrySetResult(0);   // Yes

            Assert.IsNotNull(rig.Walked, "a Yes walks the approach before the location");
            Assert.AreEqual(new[] { 16, 15, 14, 13, 12 }, rig.Walked.ConvertAll(p => p.X / Cell),
                "from the record's cell along the road until it ends (MAP.C:537-549)");
            Assert.AreEqual((C(13), C(20)), (rig.AtWalk.Value.X, rig.AtWalk.Value.Y),
                "the step was put back before the walk");
            Assert.AreEqual(before, rig.AtWalk.Value.Ticks, "the step is not charged until the visit is over");
            Assert.AreEqual(TownApproachMapHeight, rig.AtWalk.Value.MapZ, "the walk is seen from 0x1b968 (MAP.C:521)");

            Assert.AreEqual((C(16), C(20), East), (rig.Session.PositionX, rig.Session.PositionY, rig.Session.Rotation),
                "the party is left on the record's cell facing away from the town (MAP.C:556-557)");
            Assert.AreEqual(TownApproachMapHeight, rig.Session.MapCameraZ,
                "and the map keeps the walk's height, as map_camera_restore_z_yaw stores it (MAP.C:559, :482)");
            Assert.AreEqual(before + TicksPerLargeStep, rig.Clock.Ticks, "the step is charged once, after the visit");
        }

        private const long TownApproachMapHeight = 0x1b968;

        internal static ZoneTableEntry Entry(int kind, int half) {
            var region = new GidRegion();
            region.Subedges.Add(new GidSubedge { Dx = 0, Dy = 125, AnchorX = (short)-half, AnchorY = (short)half });
            region.Subedges.Add(new GidSubedge { Dx = 125, Dy = 0, AnchorX = (short)half, AnchorY = (short)half });
            region.Subedges.Add(new GidSubedge { Dx = 0, Dy = -125, AnchorX = (short)half, AnchorY = (short)-half });
            region.Subedges.Add(new GidSubedge { Dx = -125, Dy = 0, AnchorX = (short)-half, AnchorY = (short)-half });
            var entry = new ZoneTableEntry {
                Dat = new TableDatInfo { EntityType = (WorldEntityType)(byte)kind },
                Gid = new TableGidInfo { XRadius = (short)half, YRadius = (short)half },
            };
            entry.Gid.Regions.Add(region);
            return entry;
        }

        internal static FilterData AlwaysDraw() {
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
    }
}
