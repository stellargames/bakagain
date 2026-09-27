namespace BakAgain.Audio {
    using GameData.Resources.Animation;
    using GameData.Resources.Audio;
    using System;

    /// <summary>
    /// Plays the world's ambient sound effects — the Unity end of
    /// <see cref="AmbientSound"/> (<c>audio_ambient_tick</c>).
    /// </summary>
    /// <remarks>
    /// <b>The roll happens on the GAME clock, not per rendered frame.</b> The original rolls once per
    /// pass of its world loop; ours would otherwise roll once per Update, which ties how often the
    /// world makes a sound to the frame rate — a 144Hz machine would be more than twice as noisy as a
    /// 60Hz one. Accumulating against <see cref="GameTick"/> makes the rate a property of the game
    /// rather than of the hardware.
    ///
    /// <para><b>At most one sound per Tick, however many ticks elapsed.</b> After a hitch the
    /// accumulator can hold several ticks' worth; rolling them all is right, but playing several
    /// cues in one frame would be audibly wrong, so the first that fires ends the catch-up. Missed
    /// rolls are simply dropped — an ambient sound nobody heard is not owed.</para>
    /// </remarks>
    public sealed class AmbientSoundDriver {
        /// <summary>The live driver, set by the world when a zone is built.</summary>
        /// <remarks>Mirrors <see cref="MenuSoundService.Instance"/>, the SFX path already in use.</remarks>
        public static AmbientSoundDriver Instance { get; set; }

        private readonly Func<int, int> _rnd;
        private readonly Action<int, int> _play;
        private double _accumulator;

        /// <param name="rnd"><c>rnd(n)</c> returns a value in <c>[0, n)</c>.</param>
        /// <param name="play">Plays a sound id at an intensity.</param>
        public AmbientSoundDriver(Func<int, int> rnd, Action<int, int> play) {
            _rnd = rnd ?? throw new ArgumentNullException(nameof(rnd));
            _play = play ?? throw new ArgumentNullException(nameof(play));
        }

        /// <summary>Whether the party is underground — a different rate and a different sound.</summary>
        public bool Underground { get; set; }

        /// <summary>The current chapter; chapter 8 is silent outdoors.</summary>
        public int Chapter { get; set; }

        /// <summary>The current zone; zone 6 is silent outdoors and zone 2 is the coast.</summary>
        public int Zone { get; set; }

        /// <summary>Whether the story flag that changes the outdoor palette is set.</summary>
        public bool MoodFlagSet { get; set; }

        /// <summary>How many elapsed ticks one Tick will roll before giving up on the backlog.</summary>
        /// <remarks>
        /// Bounds the work after a long stall (a load, a breakpoint) so the accumulator cannot turn
        /// into a thousand rolls. Four is arbitrary and generous: at one-in-110 the chance of even one
        /// firing across four rolls is under four percent.
        /// </remarks>
        public const int MaxCatchUpTicks = 4;

        /// <summary>Advances the clock and possibly plays one ambient sound.</summary>
        /// <returns>The sound played, or -1.</returns>
        public int Tick(double deltaSeconds) {
            if (deltaSeconds <= 0) {
                return -1;
            }
            _accumulator += deltaSeconds;

            double secondsPerTick = GameTick.SecondsPerTick;
            var ticks = 0;
            while (_accumulator >= secondsPerTick) {
                _accumulator -= secondsPerTick;
                ticks++;
                if (ticks > MaxCatchUpTicks) {
                    _accumulator = 0;   // drop the rest of the backlog rather than grinding it
                    break;
                }
            }

            for (var i = 0; i < ticks && i < MaxCatchUpTicks; i++) {
                int played = RollOnce();
                if (played >= 0) {
                    return played;   // one cue per Tick, however many ticks elapsed
                }
            }
            return -1;
        }

        /// <summary>One tick's roll, exactly as the original's per-pass body.</summary>
        private int RollOnce() {
            if (AmbientSound.IsSilent(Underground, Chapter, Zone)) {
                return -1;
            }
            if (!AmbientSound.Fires(_rnd(AmbientSound.OneIn(Underground)))) {
                return -1;
            }

            int sound = Underground
                ? AmbientSound.UndergroundSfx
                : AmbientSound.PickAboveground(Zone, MoodFlagSet,
                    percentRoll: _rnd(100),
                    pairRoll: _rnd(2),
                    rangeRoll: AmbientSound.PostFlagFirstSfx
                        + _rnd(AmbientSound.PostFlagLastSfx - AmbientSound.PostFlagFirstSfx + 1));

            (int min, int max) = AmbientSound.IntensityRange(Underground);
            _play(sound, min + _rnd(max - min + 1));
            return sound;
        }
    }
}
