namespace BakAgain.Tests.PlayMode.World {
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.Tests.Editor.World;
    using BakAgain.Tests.PlayMode.UI.Inventory;
    using BakAgain.UI.InGame;
    using BakAgain.World;
    using BakAgain.World.Collision;
    using BakAgain.World.Hotspots;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Config;
    using GameData.Resources.Data;
    using GameData.Resources.World;
    using Microsoft.Extensions.Logging.Abstractions;
    using NUnit.Framework;

    /// <summary>
    /// A hotspot's statement — a <c>Bloc</c> wall's or a <c>Dial</c>'s text — is read before the
    /// step's hour says "Locklear yawned" (TASK-911).
    /// </summary>
    /// <remarks>
    /// The original plays both modally inside the hotspot passes — Bloc in the activate pass
    /// (HOTSPOT.C:133-134, :929-947), Dial in the dispatch pass (HOTSPOT.C:173-175, :579-597) — and
    /// the world loop advances the clock for the step only after them (WORLDLP.C:190-198), so dialog
    /// 64 (GSTATE.C:305-318) comes after the text. Measured on t911.G01 SAVE01/SAVE03 in both games:
    /// the original showed the text, then the yawn; the port showed the yawn 5-13 ms after the text
    /// and the text was gone. Drives the real <see cref="PartyMovement"/>, activator, dispatcher,
    /// <see cref="HotspotService"/>, <see cref="PartyUpkeepService"/> and the travel screen's
    /// announcement check.
    /// </remarks>
    public class HotspotStatementStepTests {
        private const int Cell = RoadTravel.CellSize;
        private const uint BlocText = 2700021;
        private const uint DialText = 3000032;
        private const int RestPrompt = 64;

        private static int C(int cell) => (cell * Cell) + RoadTravel.HalfCell;

        // Holds every dialog but the rest prompt open until released, counting itself as playing the
        // way DialogManager.Playing does.
        private sealed class HeldDialogs : NoOpDialogs {
            private readonly GameSession _session;
            public readonly UniTaskCompletionSource Release = new();
            public readonly List<int> Shown = new();

            public HeldDialogs(GameSession session) {
                _session = session;
            }

            public override async UniTask<int> ShowById(int id, System.Threading.CancellationToken ct = default) {
                Shown.Add(id);
                _session.DialogsPlaying++;
                try {
                    if (id != RestPrompt) {
                        await Release.Task;
                    }
                } finally {
                    _session.DialogsPlaying--;
                }
                return 0;
            }
        }

        private sealed class Rig {
            public GameSession Session;
            public GameClock Clock;
            public PartyMovement Movement;
            public HeldDialogs Dialogs;
            public PartyUpkeepService Upkeep;
        }

        // t911.G01's clock: the next Large step (120 ticks) lands on an hour with the party 21 hours
        // awake, which queues dialog 64.
        private static Rig Build(TileEventType kind) {
            var session = new GameSession {
                PositionX = C(13), PositionY = C(20), Rotation = unchecked((short)0xC000), CurrentZone = 1,
                GameTimeIn2Seconds = 2017680, LastRestTicks = 1979760,
            };
            var rig = new Rig { Session = session, Dialogs = new HeldDialogs(session) };
            var prefs = new FakePreferencesService();
            prefs.Current.StepSize = StepSize.Large;
            rig.Clock = new GameClock(session);
            rig.Upkeep = new PartyUpkeepService(session, rig.Clock, NullLogger<PartyUpkeepService>.Instance);
            var movementData = new MovementData("MOVEMENT.DAT") {
                StepDistances = new[] { 400, 800, 1600 },
                TurnAngles = new[] { 0x1000, 0x2000, 0x4000 },
                SecondsPerStep = new[] { 60, 120, 240 },
            };
            var records = new List<ProximityRecord> { new(TownGateStepTests.Entry(0, 32000), 32000, 32000, 0, 0) };
            for (int x = 12; x <= 16; x++) {
                records.Add(new ProximityRecord(TownGateStepTests.Entry(1, 700), C(x), C(20), 0, 0));
            }
            var hotspots = new HotspotService(null, null, session, rig.Dialogs, movementAccessor: () => rig.Movement);
            rig.Movement = new PartyMovement(session, movementData, prefs, camera: null, cameraHeightZ: 0,
                cameraPitch: 0, collision: new ProximityWorld(records, TownGateStepTests.AlwaysDraw()),
                hotspotPass: hotspots.ActivateAtPartyPosition, clock: rig.Clock);
            hotspots.SetZoneForTest(1,
                byChunk: new Dictionary<(int x, int y), List<TileEventTrigger>> {
                    [(0, 0)] = new List<TileEventTrigger> {
                        new TileEventTrigger { Type = kind, EntryNumber = 0, StartX = 14, EndX = 14, StartY = 14, EndY = 24 },
                    },
                },
                bloc: new DefFamilyFile<DefBlocEntry>("DEF_BLOC.DAT", new List<DefRecord<DefBlocEntry>> {
                    new DefRecord<DefBlocEntry> { Status = 1, Payload = new DefBlocEntry { DialogId = BlocText } },
                }),
                dial: new DefFamilyFile<DefDialEntry>("DEF_DIAL.DAT", new List<DefRecord<DefDialEntry>> {
                    new DefRecord<DefDialEntry> { Status = 1, Payload = new DefDialEntry { DialogId = DialText } },
                }));
            return rig;
        }

        // One pass of the travel screen's world loop.
        private static bool WorldLoopFrame(Rig rig) =>
            InGameScreen.TellQueuedAfflictions(rig.Upkeep, rig.Session, rig.Dialogs, default);

        [TestCase(TileEventType.Bloc, (int)BlocText)]
        [TestCase(TileEventType.Dial, (int)DialText)]
        public void TheStatementIsReadBeforeTheStepsHourYawns(TileEventType kind, int text) {
            Rig rig = Build(kind);

            rig.Movement.MoveForward();

            CollectionAssert.AreEqual(new[] { text }, rig.Dialogs.Shown, "the step onto the rect shows its text");
            Assert.AreEqual(2017800, rig.Clock.Ticks, "and the step's hour has passed");
            Assert.That(rig.Upkeep.PendingAnnouncements, Is.Not.Empty, "so the yawn is queued");

            WorldLoopFrame(rig);
            CollectionAssert.AreEqual(new[] { text }, rig.Dialogs.Shown,
                "the yawn waits while the text is up — the original advances the clock after the pass");

            rig.Dialogs.Release.TrySetResult();
            WorldLoopFrame(rig);
            CollectionAssert.AreEqual(new[] { text, RestPrompt }, rig.Dialogs.Shown, "and comes once it is read");
        }
    }
}
