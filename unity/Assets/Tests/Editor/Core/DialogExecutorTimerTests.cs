namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using GameData.Resources.Dialog.Actions;
    using GameData.Resources.GameState;
    using NUnit.Framework;

    /// <summary>
    /// The two timed-flag paths out of a dialog: op 14 (SetTemporaryFlag, carried as
    /// <see cref="SetFlagEffect.ForTicks"/>) and op 22 (<see cref="SetTimerAction"/>).
    /// Durations here are the ones actually shipped in the DDX files.
    /// </summary>
    [TestFixture]
    public class DialogExecutorTimerTests {
        private GameSession _session;
        private GameClock _clock;
        private DialogExecutor _executor;

        [SetUp]
        public void SetUp() {
            _session = new GameSession();
            _clock = new GameClock(_session);
            _executor = new DialogExecutor(new NullLogger<DialogExecutor>(), _session, _clock);
        }

        private bool FlagSet(int key) => (_session.GetGlobalValue(key) ?? 0) != 0;

        [Test]
        public void TemporaryFlag_IsSetImmediately() {
            // DIAL_Z19: flag 56004 for 1800 ticks (one hour).
            _executor.ApplyEffect(new SetFlagEffect { Flag = 56004, Set = true, ForTicks = 1800 });

            Assert.IsTrue(FlagSet(56004));
        }

        [Test]
        public void TemporaryFlag_ClearsWhenItsDurationRunsOut() {
            _executor.ApplyEffect(new SetFlagEffect { Flag = 56004, Set = true, ForTicks = 1800 });

            _clock.Advance(1799);
            Assert.IsTrue(FlagSet(56004), "still inside the hour");

            _clock.Advance(1);
            Assert.IsFalse(FlagSet(56004), "the hour is up");
        }

        [Test]
        public void TemporaryFlag_ReTriggering_RestartsTheCountdownInsteadOfStacking() {
            // timerpool_upsert mode 0x40 overwrites the entry for this key.
            _executor.ApplyEffect(new SetFlagEffect { Flag = 56194, Set = true, ForTicks = 5400 });
            _clock.Advance(5000);

            _executor.ApplyEffect(new SetFlagEffect { Flag = 56194, Set = true, ForTicks = 5400 });
            _clock.Advance(5000);
            Assert.IsTrue(FlagSet(56194), "the second look restarted the 5400-tick countdown");

            _clock.Advance(400);
            Assert.IsFalse(FlagSet(56194));
        }

        [Test]
        public void PlainFlag_WithNoDuration_StaysSet() {
            _executor.ApplyEffect(new SetFlagEffect { Flag = 1234, Set = true });

            _clock.Advance(GameClock.TicksPerDay * 30);

            Assert.IsTrue(FlagSet(1234));
        }

        // The one shipped SetTimer action, from DIAL_Z00: clear the corpse-flavor flag 8127
        // 3600 ticks (two hours) after the body was examined. TimerFlag's member named "Add" is
        // the 0x40 bit, which the engine treats as overwrite — hence the re-arm test below.
        private static SetTimerAction CorpseFlavourTimer() => new SetTimerAction {
            Type = TimerType.ClearFlag,
            Flag = (TimerFlag)0x40,
            Time = 3600,
            OnExpiry = new SetFlagEffect { Flag = 8127, Set = false },
        };

        [Test]
        public void CorpseFlavourFlag_ClearsTwoHoursAfterTheBodyWasExamined() {
            _session.SetGlobalFlag(8127, true);
            _executor.ApplyTimerAction(CorpseFlavourTimer());

            _clock.Advance(3599);
            Assert.IsTrue(FlagSet(8127), "flavour text still suppressed");

            _clock.Advance(1);
            Assert.IsFalse(FlagSet(8127), "the corpse reads as fresh again");
        }

        [Test]
        public void CorpseFlavourFlag_LookingAgain_RestartsTheTwoHours() {
            _session.SetGlobalFlag(8127, true);
            _executor.ApplyTimerAction(CorpseFlavourTimer());
            _clock.Advance(1800);

            _session.SetGlobalFlag(8127, true);
            _executor.ApplyTimerAction(CorpseFlavourTimer());

            _clock.Advance(1800);
            Assert.IsTrue(FlagSet(8127), "0x40 overwrites the pending timer rather than adding to it");

            _clock.Advance(1800);
            Assert.IsFalse(FlagSet(8127));
        }

        [Test]
        public void SetTimerAction_AccumulateBit_ExtendsThePendingTimer() {
            _session.SetGlobalFlag(8127, true);
            SetTimerAction accumulate = CorpseFlavourTimer();
            accumulate.Flag = (TimerFlag)0x80;

            _executor.ApplyTimerAction(accumulate);
            _executor.ApplyTimerAction(accumulate);

            _clock.Advance(3600);
            Assert.IsTrue(FlagSet(8127), "3600 + 3600 pending");

            _clock.Advance(3600);
            Assert.IsFalse(FlagSet(8127));
        }

        // ---- time a dialog takes (op 13) --------------------------------------

        // SpendDialogTime is private and fed by the branch walk, so these drive it directly —
        // the walk's job (finding the actions) is covered by the walker's own tests.
        //
        // *** PASS EVERY PARAMETER, OPTIONAL OR NOT. *** MethodInfo.Invoke matches on the exact
        // argument count and does NOT fill C# default values in, so adding an optional parameter to
        // the target throws TargetParameterCountException here rather than failing to compile.
        // `autoAdvanced` gained one on 2026-09-22 (TASK-624) and took all seven of these with it.
        private void SpendDialogTime(long ticks, bool autoAdvanced = false) =>
            typeof(DialogExecutor)
                .GetMethod("SpendDialogTime", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .Invoke(_executor, new object[] { ticks, autoAdvanced });

        [Test]
        public void AConversationCostsTheTimeItsOpsAskFor() {
            SpendDialogTime(GameClock.TicksPerHour);

            Assert.AreEqual(GameClock.TicksPerHour, _clock.Ticks);
        }

        [Test]
        public void ALongSkipIsSpentHourByHour_SoEveryHourlyTickHappens() {
            // The shipped dialogs skip up to 192 hours. Spending that as one lump would age the
            // party by a single tick instead of a week.
            int hours = 0;
            _clock.HourElapsed += () => hours++;

            SpendDialogTime(GameClock.TicksPerHour * 30);

            Assert.AreEqual(30, hours);
            Assert.AreEqual(GameClock.TicksPerHour * 30, _clock.Ticks);
        }

        [Test]
        public void ADaysWorthOfSkipCrossesTheDayBoundaryToo() {
            var days = new System.Collections.Generic.List<int>();
            _clock.DayElapsed += day => days.Add(day);

            SpendDialogTime(GameClock.TicksPerDay * 3);

            Assert.AreEqual(new[] { 0, 1, 2 }, days.ToArray(),
                "three days skipped is three day boundaries, not one");
        }

        [Test]
        public void APartialHourIsNotLost() {
            SpendDialogTime(GameClock.TicksPerHour + 100);

            Assert.AreEqual(GameClock.TicksPerHour + 100, _clock.Ticks);
        }

        [Test]
        public void ASkipOverTwelveHoursCountsAsHavingRested() {
            _session.LastRestTicks = 0;

            SpendDialogTime(GameClock.TicksPerHour * 20);

            Assert.AreEqual(_clock.Ticks, _session.LastRestTicks,
                "nobody comes out of a multi-day journey exhausted");
        }

        [Test]
        public void AShortConversationIsNotRest() {
            _session.LastRestTicks = 0;

            SpendDialogTime(GameClock.TicksPerHour * 2);

            Assert.AreEqual(0, _session.LastRestTicks, "two hours of talking is not a night's sleep");
        }

        [Test]
        public void ADialogThatCostsNoTimeDoesNotMoveTheClock() {
            SpendDialogTime(0);

            Assert.AreEqual(0, _clock.Ticks);
        }

        [Test]
        public void SetTimerAction_SetFlagKind_SetsTheFlagOnExpiry() {
            _executor.ApplyTimerAction(new SetTimerAction {
                Type = TimerType.SetFlag,
                Flag = (TimerFlag)0x40,
                Time = 100,
                OnExpiry = new SetFlagEffect { Flag = 4242, Set = true },
            });

            Assert.IsFalse(FlagSet(4242), "a SetFlag timer does not fire early");

            _clock.Advance(100);
            Assert.IsTrue(FlagSet(4242));
        }
    }
}
