namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using System;
    using System.Collections.Generic;

    [TestFixture]
    public class GameStateLoaderTests {
        private GameSession _session;
        private FakeResourceProviderService _resources;
        private GameStateLoader _loader;

        [SetUp]
        public void SetUp() {
            _session = new GameSession();
            _resources = new FakeResourceProviderService();
            _loader = new GameStateLoader(_resources, _session, new NullLogger<GameStateLoader>(), new GameClock(_session));
            // OBJINFO.DAT is static catalog data loaded alongside every hydration path (see
            // GameStateLoader.LoadObjectInfoAsync); register a stub so tests that expect a
            // successful hydration don't fail on it by default.
            _resources.Register("OBJINFO.DAT", new ObjectInfoSet("OBJINFO.DAT", new List<ObjectInfo>()));
        }

        [Test]
        public void LoadNewGameAsync_HydratesSession_FromStartupGam() {
            SaveGame startup = new SaveGameBuilder()
                .WithChapter(1)
                .WithZone(1)
                .WithPartyGold(2500)
                .Build();
            _resources.Register("STARTUP.GAM", startup);
            _resources.Register("CHAP1.DAT", new ChapterStartDataBuilder().WithGold(0).Build());

            bool result = _loader.LoadNewGameAsync().AsTask().Result;

            Assert.That(result, Is.True);
            Assert.That(_session.IsActive, Is.True);
            Assert.That(_session.Source, Is.EqualTo(GameSessionSource.NewGame));
            Assert.That(_session.Chapter, Is.EqualTo(1));
            Assert.That(_session.PartyGold, Is.EqualTo(2500));
        }

        [Test]
        public void LoadNewGameAsync_AppliesChapterStart_OverZeroedStartupTemplate() {
            // STARTUP.GAM ships with zone/world coords zeroed — the real chapter-1 start lives
            // in CHAP1.DAT and must override the template (mirrors the engine's go_to_chapter).
            SaveGame startup = new SaveGameBuilder()
                .WithChapter(1)
                .WithZone(0)
                .WithWorldTile(0, 0)
                .WithPartyGold(0)
                .WithGameTime(0)
                .Build();
            _resources.Register("STARTUP.GAM", startup);
            _resources.Register("CHAP1.DAT", new ChapterStartDataBuilder().Build());

            bool result = _loader.LoadNewGameAsync().AsTask().Result;

            Assert.That(result, Is.True);
            Assert.That(_session.CurrentZone, Is.EqualTo((byte)1));
            Assert.That(_session.WorldX, Is.EqualTo((byte)10));
            Assert.That(_session.WorldY, Is.EqualTo((byte)16));
            Assert.That(_session.PositionX, Is.EqualTo(10 * 64000 + 18 * 1600 + 800));
            Assert.That(_session.PositionY, Is.EqualTo(16 * 64000 + 25 * 1600 + 800));
            // Clock advances to the start of the next whole day (43200) plus the chapter base.
            Assert.That(_session.GameTimeIn2Seconds, Is.EqualTo(57600 + 43200));
            Assert.That(_session.MapMarkerVisible, Is.True);
            Assert.That(_session.MapMarkerIcon, Is.EqualTo(18));
            Assert.That(_session.MapMarkerXPercent, Is.EqualTo(116f / 320f * 100f).Within(0.001f));
            Assert.That(_session.MapMarkerYPercent, Is.EqualTo(75f / 200f * 100f).Within(0.001f));
        }

        [Test]
        public void LoadNewGameAsync_ReturnsFalse_WhenChapterDataMissing() {
            _resources.Register("STARTUP.GAM", new SaveGameBuilder().Build());
            _resources.RegisterMissing("CHAP1.DAT");

            bool result = _loader.LoadNewGameAsync().AsTask().Result;

            Assert.That(result, Is.False);
            Assert.That(_resources.OutstandingForOwner(_loader), Is.EqualTo(0));
        }

        [Test]
        public void LoadNewGameAsync_ReturnsFalse_WhenStartupGamMissing() {
            _resources.RegisterMissing("STARTUP.GAM");

            bool result = _loader.LoadNewGameAsync().AsTask().Result;

            Assert.That(result, Is.False);
            Assert.That(_session.IsActive, Is.False);
        }

        [Test]
        public void LoadNewGameAsync_ReturnsFalse_WhenVersionUnsupported() {
            SaveGame badVersion = new SaveGameBuilder().WithVersion(99).Build();
            _resources.Register("STARTUP.GAM", badVersion);

            bool result = _loader.LoadNewGameAsync().AsTask().Result;

            Assert.That(result, Is.False);
            Assert.That(_session.IsActive, Is.False);
        }

        [Test]
        public void LoadNewGameAsync_ReturnsFalse_WhenStateDataMissing() {
            SaveGame headerOnly = new SaveGameBuilder().WithoutData().Build();
            _resources.Register("STARTUP.GAM", headerOnly);

            bool result = _loader.LoadNewGameAsync().AsTask().Result;

            Assert.That(result, Is.False);
            Assert.That(_session.IsActive, Is.False);
        }

        [Test]
        public void LoadNewGameAsync_ReleasesAssets_OnSuccess() {
            SaveGame startup = new SaveGameBuilder().Build();
            _resources.Register("STARTUP.GAM", startup);
            _resources.Register("CHAP1.DAT", new ChapterStartDataBuilder().Build());

            _loader.LoadNewGameAsync().AsTask().Wait();

            Assert.That(_resources.ReleaseCalls, Is.GreaterThanOrEqualTo(1));
            Assert.That(_resources.OutstandingForOwner(_loader), Is.EqualTo(0));
        }

        [Test]
        public void LoadNewGameAsync_ReleasesAssets_OnValidationFailure() {
            _resources.Register("STARTUP.GAM", new SaveGameBuilder().WithoutData().Build());

            _loader.LoadNewGameAsync().AsTask().Wait();

            Assert.That(_resources.OutstandingForOwner(_loader), Is.EqualTo(0));
        }

        [Test]
        public void LoadNewGameAsync_ReleasesAssets_OnLoadException() {
            _resources.RegisterFailure("STARTUP.GAM", new InvalidOperationException("boom"));

            bool result = _loader.LoadNewGameAsync().AsTask().Result;

            Assert.That(result, Is.False);
            Assert.That(_resources.OutstandingForOwner(_loader), Is.EqualTo(0));
        }

        [Test]
        public void LoadNewGameAsync_SetsSessionObjectInfo_FromObjInfoDat() {
            SaveGame startup = new SaveGameBuilder().WithChapter(1).WithZone(1).Build();
            _resources.Register("STARTUP.GAM", startup);
            _resources.Register("CHAP1.DAT", new ChapterStartDataBuilder().Build());
            var picklocks = new ObjectInfo("OBJINFO.DAT") { Number = 80, Name = "Picklocks" };
            _resources.Register("OBJINFO.DAT", new ObjectInfoSet("OBJINFO.DAT", new List<ObjectInfo> { picklocks }));

            bool result = _loader.LoadNewGameAsync().AsTask().Result;

            Assert.That(result, Is.True);
            Assert.That(_session.ObjectInfo, Is.Not.Null);
            Assert.That(_session.ObjectInfo.GetById(80), Is.SameAs(picklocks));
        }

        [Test]
        public void LoadNewGameAsync_ReturnsFalse_WhenObjectInfoMissing() {
            SaveGame startup = new SaveGameBuilder().WithChapter(1).WithZone(1).Build();
            _resources.Register("STARTUP.GAM", startup);
            _resources.Register("CHAP1.DAT", new ChapterStartDataBuilder().Build());
            _resources.RegisterMissing("OBJINFO.DAT");

            bool result = _loader.LoadNewGameAsync().AsTask().Result;

            Assert.That(result, Is.False);
            Assert.That(_resources.OutstandingForOwner(_loader), Is.EqualTo(0));
        }

        [Test]
        public void LoadNewGameAsync_DerivesChapter_FromStartupState() {
            // The starting chapter is read from STARTUP.GAM's state (not passed by the caller),
            // and selects which CHAPx.DAT is applied. A chapter-2 template must load CHAP2.DAT.
            SaveGame startup = new SaveGameBuilder().WithChapter(2).WithZone(0).Build();
            _resources.Register("STARTUP.GAM", startup);
            _resources.Register("CHAP2.DAT",
                new ChapterStartDataBuilder().WithChapter(2).WithLocation(11, 40, 40).Build());
            // CHAP1.DAT deliberately absent — deriving chapter 1 would fail this test.

            bool result = _loader.LoadNewGameAsync().AsTask().Result;

            Assert.That(result, Is.True);
            Assert.That(_session.Chapter, Is.EqualTo(2));
            Assert.That(_session.CurrentZone, Is.EqualTo((byte)11));
        }

        [Test]
        public void LoadFromSaveAsync_HydratesSession_FromSavePath() {
            SaveGame save = new SaveGameBuilder()
                .WithChapter(3)
                .WithZone(7)
                .WithPartyGold(15000)
                .WithFullMapMarker(new FullMapIcon(true, 33.75f, 45f, 22))
                .Build();
            _resources.Register("SAVE01.GAM", save);

            bool result = _loader.LoadFromSaveAsync("SAVE01.GAM").AsTask().Result;

            Assert.That(result, Is.True);
            Assert.That(_session.IsActive, Is.True);
            Assert.That(_session.Source, Is.EqualTo(GameSessionSource.LoadSave));
            Assert.That(_session.Chapter, Is.EqualTo(3));
            Assert.That(_session.CurrentZone, Is.EqualTo((byte)7));
            // The save's world-map marker is restored onto the session (no chapter override).
            Assert.That(_session.MapMarkerVisible, Is.True);
            Assert.That(_session.MapMarkerXPercent, Is.EqualTo(33.75f).Within(0.001f));
            Assert.That(_session.MapMarkerYPercent, Is.EqualTo(45f).Within(0.001f));
            Assert.That(_session.MapMarkerIcon, Is.EqualTo(22));
        }

        [Test]
        public void LoadFromSaveAsync_ThrowsArgumentException_ForEmptyKey() {
            Assert.Throws<ArgumentException>(
                () => _loader.LoadFromSaveAsync(""));
        }

        [Test]
        public void Constructor_ThrowsArgumentNullException_ForNullDependencies() {
            Assert.Throws<ArgumentNullException>(
                () => new GameStateLoader(null, _session, new NullLogger<GameStateLoader>(), new GameClock(_session)));
            Assert.Throws<ArgumentNullException>(
                () => new GameStateLoader(_resources, null, new NullLogger<GameStateLoader>(), null));
            Assert.Throws<ArgumentNullException>(
                () => new GameStateLoader(_resources, _session, null, new GameClock(_session)));
        }
    }
}
