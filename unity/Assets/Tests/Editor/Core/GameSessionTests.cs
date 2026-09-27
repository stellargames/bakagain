namespace BakAgain.Tests.Editor.Core {
    using GameData.Resources.World;
    using BakAgain.Core;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using GameData.Resources.Data;
    using NUnit.Framework;
    using System;

    [TestFixture]
    public class GameSessionTests {
        /// <summary>
        /// The saved roster's SIZE bounds it, not the length of the slot array.
        /// </summary>
        /// <remarks>
        /// <b>The array is always three bytes and its tail is stale.</b> `SaveGameWriter` says so
        /// itself — "slots past the party's size are LEFT ALONE, not zeroed: the engine reads only
        /// the first `size` of them" — and the original bounds every walk of
        /// <c>activePartyCharacters</c> by <c>numberOfActivePartyCharacters</c>.
        ///
        /// <para>Until 2026-09-10 loading took the array whole, so chapter 2 — a party of two —
        /// came back with a THIRD member: the byte chapter 1 had left in slot 3. He drew a combat
        /// turn, took damage and ate rations of his own, and the only visible tell was the roster
        /// printing the same character twice.</para>
        /// </remarks>
        [Test]
        public void Initialize_TrimsTheActiveRoster_ToItsSavedSize() {
            SaveGame saveGame = new SaveGameBuilder()
                .WithParty(new[] { "Locklear", "Gorath", "Owyn" },
                    slots: new byte[] { 2, 1, 1 }, activeCount: 2)
                .Build();

            GameSession session = new GameSession();
            session.Initialize(saveGame, GameSessionSource.LoadSave);

            Assert.That(session.ActivePartyCount, Is.EqualTo((byte)2));
            Assert.That(session.ActivePartyIndices, Is.EqualTo(new byte[] { 2, 1 }));
        }

        [Test]
        public void Initialize_CopiesHeaderAndStateFields_FromSaveGame() {
            SaveGame saveGame = new SaveGameBuilder()
                .WithChapter(1)
                .WithMapIcon(18)
                .WithZone(1)
                .WithWorldTile(116, 75)
                .WithPosition(700000, 700000, 0, rotation: 256)
                .WithLastSeenWorldScalars(stepSpeed: 400, gridStride: 256)
                .WithPartyGold(2500)
                .WithGameTime(1800)
                .WithParty(new[] { "Locklear", "Gorath", "Owyn" }, new byte[] { 0, 1, 2 })
                .Build();

            GameSession session = new GameSession();
            session.Initialize(saveGame, GameSessionSource.NewGame);

            Assert.That(session.IsActive, Is.True);
            Assert.That(session.Source, Is.EqualTo(GameSessionSource.NewGame));
            Assert.That(session.Chapter, Is.EqualTo(1));
            Assert.That(session.MapIcon, Is.EqualTo((short)18));
            Assert.That(session.CurrentZone, Is.EqualTo((byte)1));
            Assert.That(session.PositionX, Is.EqualTo(700000));
            Assert.That(session.PositionY, Is.EqualTo(700000));

            // *** WorldX/Y are NOT copied — they are derived from the position. *** The fixture
            // stores 116/75 next to a position in tile (10,10), which is a pairing no original
            // save can hold: all 16 shipped saves have bytes 19/20 equal to PositionX/Y / 64000.
            // Since 2026-09-10 the port cannot write that pairing either, and reading a save that
            // has it lands at the tile the position names.
            Assert.That(session.WorldX, Is.EqualTo((byte)(700000 / WorldPlacement.TileSize)));
            Assert.That(session.WorldY, Is.EqualTo((byte)(700000 / WorldPlacement.TileSize)));
            Assert.That(session.PositionZ, Is.EqualTo(0));
            Assert.That(session.Rotation, Is.EqualTo((short)256));
            Assert.That(session.PartyGold, Is.EqualTo(2500));
            Assert.That(session.GameTimeIn2Seconds, Is.EqualTo(1800L));
            Assert.That(session.PartyActorNames, Is.EqualTo(new[] { "Locklear", "Gorath", "Owyn" }));
            Assert.That(session.ActivePartyCount, Is.EqualTo((byte)3));
            Assert.That(session.ActivePartyIndices, Is.EqualTo(new byte[] { 0, 1, 2 }));
        }

        [Test]
        public void Initialize_RecordsSource_LoadSave() {
            SaveGame saveGame = new SaveGameBuilder().Build();
            GameSession session = new GameSession();

            session.Initialize(saveGame, GameSessionSource.LoadSave);

            Assert.That(session.Source, Is.EqualTo(GameSessionSource.LoadSave));
        }

        [Test]
        public void Initialize_ThrowsArgumentNullException_WhenSaveGameIsNull() {
            GameSession session = new GameSession();

            Assert.Throws<ArgumentNullException>(
                () => session.Initialize(null, GameSessionSource.NewGame));
        }

        [Test]
        public void Initialize_ThrowsArgumentException_WhenStateDataMissing() {
            SaveGame headerOnly = new SaveGameBuilder().WithoutData().Build();
            GameSession session = new GameSession();

            Assert.Throws<ArgumentException>(
                () => session.Initialize(headerOnly, GameSessionSource.NewGame));
        }

        [Test]
        public void Clear_ReturnsSessionToUnloadedState() {
            SaveGame saveGame = new SaveGameBuilder().WithPartyGold(999).Build();
            GameSession session = new GameSession();
            session.Initialize(saveGame, GameSessionSource.NewGame);

            session.Clear();

            Assert.That(session.IsActive, Is.False);
            Assert.That(session.Source, Is.EqualTo(GameSessionSource.Unloaded));
            Assert.That(session.PartyGold, Is.EqualTo(0));
            Assert.That(session.PartyActorNames, Is.Empty);
            Assert.That(session.ActivePartyIndices, Is.Empty);
        }

        [Test]
        public void Initialize_CanBeCalledMultipleTimes_LastWriteWins() {
            GameSession session = new GameSession();
            session.Initialize(new SaveGameBuilder().WithPartyGold(100).WithZone(1).Build(),
                GameSessionSource.NewGame);
            session.Initialize(new SaveGameBuilder().WithPartyGold(5000).WithZone(7).Build(),
                GameSessionSource.LoadSave);

            Assert.That(session.PartyGold, Is.EqualTo(5000));
            Assert.That(session.CurrentZone, Is.EqualTo((byte)7));
            Assert.That(session.Source, Is.EqualTo(GameSessionSource.LoadSave));
        }
    }
}
