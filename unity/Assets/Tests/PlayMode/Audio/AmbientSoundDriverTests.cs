namespace BakAgain.Tests.PlayMode.Audio {
    using System.Collections.Generic;
    using BakAgain.Audio;
    using GameData.Resources.Animation;
    using GameData.Resources.Audio;
    using NUnit.Framework;

    /// <summary>
    /// The ambient driver: how often the world speaks, and what it says.
    /// </summary>
    public class AmbientSoundDriverTests {
        private List<(int Sound, int Intensity)> _played;

        [SetUp]
        public void SetUp() => _played = new List<(int, int)>();

        // rnd always returns 0, so every chance passes and every range picks its first value.
        private AmbientSoundDriver AlwaysFires() =>
            new AmbientSoundDriver(_ => 0, (s, i) => _played.Add((s, i)));

        // rnd returns n-1, so no one-in-N ever comes up.
        private AmbientSoundDriver NeverFires() =>
            new AmbientSoundDriver(n => n - 1, (s, i) => _played.Add((s, i)));

        private static double OneTick => GameTick.SecondsPerTick;

        [Test]
        public void NoSoundBeforeAWholeGameTickHasPassed() {
            // *** The point of the driver. *** Rolling per Update would make a 144Hz machine more
            // than twice as noisy as a 60Hz one; the rate has to be a property of the game.
            AmbientSoundDriver d = AlwaysFires();
            d.Chapter = 1;
            d.Zone = 3;

            Assert.AreEqual(-1, d.Tick(OneTick * 0.4), "well inside one tick");
            Assert.IsEmpty(_played);
            Assert.AreEqual(-1, d.Tick(OneTick * 0.4), "still inside, cumulatively");
            Assert.IsEmpty(_played);

            Assert.AreNotEqual(-1, d.Tick(OneTick * 0.4), "the third crosses the boundary");
            Assert.AreEqual(1, _played.Count);
        }

        [Test]
        public void ManyElapsedTicksStillPlayAtMostONECue() {
            // After a hitch the accumulator holds several ticks. Rolling them is right; playing
            // several cues in one frame would be audibly wrong.
            AmbientSoundDriver d = AlwaysFires();
            d.Chapter = 1;
            d.Zone = 3;

            d.Tick(OneTick * 50);

            Assert.AreEqual(1, _played.Count);
        }

        [Test]
        public void ALongStallDoesNotLeaveABacklogThatFiresLater() {
            // The backlog is dropped rather than ground through, so the frame after a load is not
            // followed by a run of cues.
            AmbientSoundDriver d = NeverFires();
            d.Chapter = 1;
            d.Zone = 3;
            d.Tick(OneTick * 10000);

            // Now make every roll fire: a single tick's worth should produce exactly one, not a flood.
            AmbientSoundDriver d2 = AlwaysFires();
            d2.Chapter = 1;
            d2.Zone = 3;
            d2.Tick(OneTick * 10000);
            Assert.AreEqual(1, _played.Count);
        }

        [Test]
        public void ARollThatDoesNotComeUpPlaysNothing() {
            AmbientSoundDriver d = NeverFires();
            d.Chapter = 1;
            d.Zone = 3;

            Assert.AreEqual(-1, d.Tick(OneTick * 3));
            Assert.IsEmpty(_played);
        }

        [Test]
        public void CHAPTEREIGHTIsSilentOutdoorsHoweverLongItRuns() {
            AmbientSoundDriver d = AlwaysFires();
            d.Chapter = AmbientSound.SilentChapter;
            d.Zone = 3;

            d.Tick(OneTick * 4);

            Assert.IsEmpty(_played);
        }

        [Test]
        public void ButADungeonInChapterEightStillDrips() {
            // The mode check comes first, so the outdoor exclusions do not reach underground.
            AmbientSoundDriver d = AlwaysFires();
            d.Underground = true;
            d.Chapter = AmbientSound.SilentChapter;
            d.Zone = AmbientSound.SilentZone;

            d.Tick(OneTick);

            Assert.AreEqual(1, _played.Count);
            Assert.AreEqual(AmbientSound.UndergroundSfx, _played[0].Sound);
        }

        [Test]
        public void TheOutdoorPaletteFollowsTheStoryFlag() {
            AmbientSoundDriver before = AlwaysFires();
            before.Chapter = 1;
            before.Zone = 3;
            before.Tick(OneTick);
            int pre = _played[0].Sound;

            _played.Clear();
            AmbientSoundDriver after = AlwaysFires();
            after.Chapter = 1;
            after.Zone = 3;
            after.MoodFlagSet = true;
            after.Tick(OneTick);

            Assert.AreNotEqual(pre, _played[0].Sound);
            Assert.AreEqual(AmbientSound.RarePreFlagSfx, pre, "rnd 0 takes the rare branch");
        }

        [Test]
        public void TheCoastGetsGullsOnceTheFlagIsSet() {
            AmbientSoundDriver d = AlwaysFires();
            d.Chapter = 1;
            d.Zone = AmbientSound.DistinctZone;
            d.MoodFlagSet = true;

            d.Tick(OneTick);

            Assert.AreEqual(AmbientSound.DistinctZoneSfx, _played[0].Sound);
        }

        [Test]
        public void IntensityStaysInsideTheDeclaredRange() {
            // Pass the one-in-N chance but take the TOP of every other roll, so the intensity lands
            // at its maximum — the end that would overshoot if the range were computed wrongly.
            var top = new AmbientSoundDriver(
                n => n == AmbientSound.OneIn(true) ? 0 : n - 1, (s, i) => _played.Add((s, i)));
            top.Underground = true;

            top.Tick(OneTick);

            (int min, int max) = AmbientSound.IntensityRange(underground: true);
            Assert.AreEqual(1, _played.Count);
            Assert.That(_played[0].Intensity, Is.InRange(min, max));
            Assert.AreEqual(max, _played[0].Intensity, "the top roll gives the top of the range");
        }

        [Test]
        public void ANonPositiveDeltaDoesNothing() {
            AmbientSoundDriver d = AlwaysFires();
            d.Chapter = 1;
            d.Zone = 3;

            Assert.AreEqual(-1, d.Tick(0));
            Assert.AreEqual(-1, d.Tick(-1));
            Assert.IsEmpty(_played);
        }
    }
}
