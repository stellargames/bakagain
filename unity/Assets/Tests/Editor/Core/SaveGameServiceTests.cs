namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using System.Collections.Generic;

    [TestFixture]
    public class SaveGameServiceTests {
        private GameSession _session;
        private FakeSaveGameDirectoryService _saves;
        private SaveGameService _service;

        [SetUp]
        public void SetUp() {
            _session = new GameSession();
            // A hydrated session with a real backing body — the precondition SaveAsync needs.
            _session.Initialize(
                new SaveGameBuilder().WithBackingBody().Build(),
                GameSessionSource.NewGame);
            _saves = new FakeSaveGameDirectoryService();
            _service = new SaveGameService(_session, _saves, new NullLogger<SaveGameService>(), new GameClock(_session));
        }

        [Test]
        public void SaveAsync_ReturnsTrue_WhenWriteSucceeds() {
            _saves.WriteResult = true;

            bool ok = _service.SaveAsync("SAVES.G01", 1, "my save").AsTask().Result;

            Assert.That(ok, Is.True);
            Assert.That(_saves.LastWrittenPath, Is.EqualTo("SAVES.G01/SAVE01.GAM"));
            Assert.That(_saves.LastWrittenBytes, Is.Not.Null.And.Length.GreaterThan(0));
        }

        [Test]
        public void SaveAsync_ReturnsFalse_WhenWriteFails() {
            // Disk full / permission denied: the directory service reports the write failed.
            _saves.WriteResult = false;

            bool ok = _service.SaveAsync("SAVES.G01", 1, "my save").AsTask().Result;

            // The user must NOT be told the save succeeded when the bytes never hit disk.
            Assert.That(ok, Is.False);
        }

        /// <summary>
        /// A staged combat edit has to reach the written BYTES, not merely the session.
        /// </summary>
        /// <remarks>
        /// <b>The one silent link in a four-hop chain.</b> A fight's damage travels
        /// <c>CombatRuntime.CollectDirtyCombatantEdits</c> -> <c>HotspotService.StageCombatantEdits</c>
        /// -> <c>GameSession.DirtyCombatantEdits</c> -> <c>SaveGameService</c> -> the writer. The
        /// staging half is covered by <c>GameSessionCombatantEditTests</c> and the writing half by
        /// the .NET round trip, but nothing asserted that the service actually PASSES what the
        /// session holds.
        ///
        /// <para>Breaking that hop is invisible: the save succeeds, the file is valid, and the only
        /// symptom is the bug the staging was written to fix — wound an enemy, leave, come back, and
        /// it is at full health again.</para>
        /// </remarks>
        [Test]
        public void SaveAsync_CarriesStagedCombatEdits_IntoTheWrittenBytes() {
            _saves.WriteResult = true;
            const int actorSlot = 3;
            const byte gridX = 5;
            const byte gridY = 9;
            _session.StageCombatantEdits(new List<GameData.Resources.Data.DirtyCombatantEdit> {
                new GameData.Resources.Data.DirtyCombatantEdit(actorSlot, Wounded(gridX, gridY)),
            });

            bool ok = _service.SaveAsync("SAVES.G01", 1, "my save").AsTask().Result;

            Assert.That(ok, Is.True);
            byte[] written = _saves.LastWrittenBytes;
            Assert.That(written, Is.Not.Null);

            // Assert on the RECORD'S OWN BYTES rather than recomputing the offset here: repeating
            // the writer's `CombatDataOffset + slot * RecordSize` would make the test agree with the
            // implementation by construction, and it would still pass if both moved together.
            byte[] expected = ResourceExtraction.Extractors.CombatRecordWriter.ToBytes(
                Wounded(gridX, gridY));
            Assert.That(IndexOf(written, expected), Is.GreaterThanOrEqualTo(0),
                "the staged record never reached the written bytes");
        }

        /// <summary>First index of <paramref name="needle"/> in <paramref name="hay"/>, or -1.</summary>
        private static int IndexOf(byte[] hay, byte[] needle) {
            for (var i = 0; i + needle.Length <= hay.Length; i++) {
                var match = true;
                for (var j = 0; j < needle.Length; j++) {
                    if (hay[i + j] != needle[j]) {
                        match = false;
                        break;
                    }
                }
                if (match) {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>A combat record standing in for "this enemy moved and took a wound".</summary>
        private static GameData.Resources.Data.SaveGameCombatData Wounded(byte x, byte y) =>
            new GameData.Resources.Data.SaveGameCombatData(
                targetActorPointer: 0, creatureType: 18, xOnGrid: x, yOnGrid: y,
                targetXOnGrid: 0, targetYOnGrid: 0, combatStatus: 0, animEffectType: 0,
                activeSpellEffectSlot: 0, unusedPadding: 0, animDurationTimer: 0,
                monsterSpellAbility: 0, meleeAttackType: 0, rangedAttackType: 0,
                movementAiType: 0, preferredArrowType: -1, lastSpellSymbolFile: 0,
                floatingDamageValue: 0, floatingDamageTimer: -1);

        [Test]
        public void SaveAsync_ReturnsFalse_WhenNoActiveSession() {
            var freshSession = new GameSession(); // never Initialize'd → not active, no body
            var service = new SaveGameService(freshSession, _saves, new NullLogger<SaveGameService>(), new GameClock(freshSession));

            bool ok = service.SaveAsync("SAVES.G01", 1, "my save").AsTask().Result;

            Assert.That(ok, Is.False);
            Assert.That(_saves.LastWrittenPath, Is.Null, "no write should be attempted");
        }

        /// <summary>Minimal fake: the save path only exercises EnsureDirectoryAsync,
        /// SlotFullPath, and WriteSlotAsync. The browse members return empty defaults.</summary>
        private sealed class FakeSaveGameDirectoryService : ISaveGameDirectoryService {
            public bool WriteResult = true;
            public string LastWrittenPath;
            public byte[] LastWrittenBytes;

            public string SaveRoot => "/fake/Saves";
            public UniTask EnsureRootAsync() => UniTask.CompletedTask;
            public UniTask<string> EnsureDefaultDirectoryAsync() => UniTask.FromResult("SAVES.G01");
            public int LowestFreeSlotIndex(string directoryName) => 1;
            public int LowestFreeDirectoryNumber() => 0;
            public UniTask<string> CreateDirectoryAsync(string baseName) => UniTask.FromResult(baseName + ".G00");
            public bool IsValidDirectoryName(string name) => true;
            public UniTask<IReadOnlyList<SaveDirectoryInfo>> ListDirectoriesAsync() =>
                UniTask.FromResult<IReadOnlyList<SaveDirectoryInfo>>(new List<SaveDirectoryInfo>());
            public UniTask<IReadOnlyList<SaveSlotInfo>> ListSlotsAsync(string directoryName) =>
                UniTask.FromResult<IReadOnlyList<SaveSlotInfo>>(new List<SaveSlotInfo>());
            public UniTask<string> EnsureDirectoryAsync(string directoryName) =>
                UniTask.FromResult(directoryName);
            public string SlotFullPath(string directoryName, int slotIndex) =>
                $"{directoryName}/SAVE{slotIndex:D2}.GAM";

            public UniTask<bool> WriteSlotAsync(string fullPath, byte[] bytes) {
                LastWrittenPath = fullPath;
                LastWrittenBytes = bytes;
                return UniTask.FromResult(WriteResult);
            }

            public UniTask DeleteSlotAsync(string fullPath) => UniTask.CompletedTask;
            public UniTask DeleteDirectoryAsync(string directoryName) => UniTask.CompletedTask;
        }
    }
}
