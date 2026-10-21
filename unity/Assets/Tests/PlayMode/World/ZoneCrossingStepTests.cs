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
    using GameData.Resources.Location;
    using GameData.Resources.World;
    using NUnit.Framework;

    /// <summary>
    /// A zone border, stepped onto: asked from the border cell, and charged for the step only after
    /// the answer, or after the crossing (TASK-910).
    /// </summary>
    /// <remarks>
    /// The original asks inside <c>hotspotevt_activate_at_player</c> (<c>hotspotevt_action_try_enter_zone</c>,
    /// HOTSPOT.C:839-860) with the party on the border cell, puts the step back once that returns
    /// (WORLDMOV.C:126-135), crosses from the dispatch pass (<c>hotspotevt_action_enter_zone</c>,
    /// HOTSPOT.C:192, :863-889) and only then advances the clock for the step, quartered when the zone
    /// it is now in is underground (WORLDLP.C:190-198). The port charged the step before the prompt
    /// was answered, and the hour's "Locklear yawned" replaced the prompt. Same shape as
    /// <see cref="TownGateStepTests"/>; drives the real <see cref="PartyMovement"/>, activator,
    /// dispatcher and <see cref="HotspotService"/>.
    /// </remarks>
    public class ZoneCrossingStepTests {
        private const int Cell = RoadTravel.CellSize;
        private const short East = unchecked((short)0xC000);
        private const long TicksPerLargeStep = 240 / 2;

        private static int C(int cell) => (cell * Cell) + RoadTravel.HalfCell;

        private sealed class HeldConfirm : NoOpDialogs {
            public readonly UniTaskCompletionSource<bool> Answer = new();
            public int Asked;

            public override UniTask<bool> ShowConfirmById(int id, System.Threading.CancellationToken ct = default) {
                Asked++;
                return Answer.Task;
            }
        }

        private sealed class Rig : IGameFlow {
            public GameSession Session;
            public GameClock Clock;
            public PartyMovement Movement;
            public HotspotService Hotspots;
            public HeldConfirm Dialogs;
            public MovementData MovementData;
            public FakePreferencesService Prefs;
            public (int X, int Y, long Ticks)? AtCrossing;

            // The crossing: the session is placed in the new zone and a new movement is built for it,
            // as GameFlow.TransitionTo -> WorldRuntime.BuildAsync does. The destination is underground.
            public UniTask<ZoneTransitionKind> TransitionTo(Location destination) {
                AtCrossing = (Session.PositionX, Session.PositionY, Clock.Ticks);
                Session.CurrentZone = (byte)destination.ZoneNumber;
                Movement = new PartyMovement(Session, MovementData, Prefs, camera: null, cameraHeightZ: 0,
                    cameraPitch: 0, underground: true, clock: Clock);
                return UniTask.FromResult(ZoneTransitionKind.ChangeZone);
            }

            public UniTask<bool> LoadSave(string path) => UniTask.FromResult(false);
            public UniTask Boot() => UniTask.CompletedTask;
            public UniTask ShowMainMenu() => UniTask.CompletedTask;
            public UniTask StartNewGame(bool playChapterScenes = true) => UniTask.CompletedTask;
            public UniTask GoToChapter(int chapter) => UniTask.CompletedTask;
            public UniTask EnterWorld() => UniTask.CompletedTask;
        }

        // Zone 1's east border as shipped (T011510 Zone#7: x 36, y 14..24), moved to column 14 of a
        // one-chunk world. The record is DEF_ZONE 0's shape: a prompt and no arrival message.
        private static Rig Build() {
            var rig = new Rig {
                Session = new GameSession { PositionX = C(13), PositionY = C(20), Rotation = East, CurrentZone = 1 },
                Dialogs = new HeldConfirm(),
                MovementData = new MovementData("MOVEMENT.DAT") {
                    StepDistances = new[] { 400, 800, 1600 },
                    TurnAngles = new[] { 0x1000, 0x2000, 0x4000 },
                    SecondsPerStep = new[] { 60, 120, 240 },
                },
                Prefs = new FakePreferencesService(),
            };
            rig.Prefs.Current.StepSize = StepSize.Large;
            rig.Clock = new GameClock(rig.Session);
            var records = new List<ProximityRecord> { new(TownGateStepTests.Entry(0, 32000), 32000, 32000, 0, 0) };
            for (int x = 12; x <= 16; x++) {
                records.Add(new ProximityRecord(TownGateStepTests.Entry(1, 700), C(x), C(20), 0, 0));
            }
            rig.Hotspots = new HotspotService(null, null, rig.Session, rig.Dialogs,
                flowAccessor: () => rig,
                movementAccessor: () => rig.Movement);
            rig.Movement = new PartyMovement(rig.Session, rig.MovementData, rig.Prefs, camera: null,
                cameraHeightZ: 0, cameraPitch: 0,
                collision: new ProximityWorld(records, TownGateStepTests.AlwaysDraw()),
                hotspotPass: rig.Hotspots.ActivateAtPartyPosition, clock: rig.Clock);
            rig.Hotspots.SetZoneForTest(1,
                byChunk: new Dictionary<(int x, int y), List<TileEventTrigger>> {
                    [(0, 0)] = new List<TileEventTrigger> {
                        new TileEventTrigger { Type = TileEventType.Zone, EntryNumber = 0, StartX = 14, EndX = 14, StartY = 14, EndY = 24 },
                    },
                },
                zoneDef: new DefFamilyFile<DefZoneEntry>("DEF_ZONE.DAT", new List<DefRecord<DefZoneEntry>> {
                    new DefRecord<DefZoneEntry> {
                        Status = 1,
                        Payload = new DefZoneEntry {
                            DialogId1 = 2700023,
                            Location = new Location { ZoneNumber = 10, X = 11, Y = 10, XOffset = 17, YOffset = 39, ZRotation = 0x4000 },
                        },
                    },
                }));
            return rig;
        }

        [Test]
        public void TheBorderAsksFromTheBorderCellAndANoPutsThePartyBackAndChargesTheStep() {
            Rig rig = Build();
            long before = rig.Clock.Ticks;

            rig.Movement.MoveForward();

            Assert.AreEqual(1, rig.Dialogs.Asked, "the border asks");
            Assert.AreEqual((C(14), C(20)), (rig.Session.PositionX, rig.Session.PositionY),
                "the prompt is asked with the party on the border cell, as the original's is");
            Assert.AreEqual(before, rig.Clock.Ticks, "and the step is not charged while it is asked");

            rig.Dialogs.Answer.TrySetResult(false);   // No

            Assert.AreEqual((C(13), C(20)), (rig.Session.PositionX, rig.Session.PositionY), "a No puts the party back");
            Assert.AreEqual(before + TicksPerLargeStep, rig.Clock.Ticks, "and then the step costs its time");
            Assert.IsNull(rig.AtCrossing, "and nothing crosses");
        }

        [Test]
        public void AYesCrossesThenChargesTheStepInTheNewZone() {
            Rig rig = Build();
            long before = rig.Clock.Ticks;
            rig.Movement.MoveForward();

            rig.Dialogs.Answer.TrySetResult(true);   // Yes

            Assert.IsNotNull(rig.AtCrossing, "a Yes crosses");
            Assert.AreEqual((C(13), C(20)), (rig.AtCrossing.Value.X, rig.AtCrossing.Value.Y),
                "the step was put back before the crossing");
            Assert.AreEqual(before, rig.AtCrossing.Value.Ticks, "the step is not charged until the crossing is over");
            Assert.AreEqual(before + (TicksPerLargeStep / 4), rig.Clock.Ticks,
                "it is charged once, after, at the rate of the zone the party is now in (WORLDLP.C:192-197)");
        }
    }
}
