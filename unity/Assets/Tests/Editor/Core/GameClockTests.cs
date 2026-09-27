namespace BakAgain.Tests.Core {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using GameData.Resources.Dialog.Actions;
    using NUnit.Framework;

    public class GameClockTests {
        private static (GameSession, GameClock) NewClock(long startTicks = 0) {
            var session = new GameSession { GameTimeIn2Seconds = startTicks };
            return (session, new GameClock(session));
        }

        [Test]
        public void Advance_AddsTicksToTheSession() {
            var (session, clock) = NewClock();

            clock.Advance(100);

            Assert.AreEqual(100, session.GameTimeIn2Seconds);
            Assert.AreEqual(100, clock.Ticks);
        }

        [Test]
        public void HourAndDay_DecomposeTheTickCount() {
            // 0xa8c0 = one day, 0x708 = one hour: 2 days + 5 hours in.
            var (_, clock) = NewClock(2 * GameClock.TicksPerDay + 5 * GameClock.TicksPerHour);

            Assert.AreEqual(2, clock.Day);
            Assert.AreEqual(5, clock.HourOfDay);
        }

        [Test]
        public void Advance_RaisesHourElapsed_OnlyWhenTheHourIndexChanges() {
            var (_, clock) = NewClock();
            int hours = 0;
            clock.HourElapsed += () => hours++;

            clock.Advance(GameClock.TicksPerHour - 1); // still hour 0
            Assert.AreEqual(0, hours);

            clock.Advance(1);                          // crosses into hour 1
            Assert.AreEqual(1, hours);
        }

        [Test]
        public void Advance_RaisesHourElapsedOncePerCall_HoweverManyHoursItCovers() {
            // Faithful to gstate_advance_time: it compares the hour index before/after, so one
            // eight-hour jump does the hourly work once. Callers wanting per-hour effects step.
            var (_, clock) = NewClock();
            int hours = 0;
            clock.HourElapsed += () => hours++;

            clock.Advance(8 * GameClock.TicksPerHour);

            Assert.AreEqual(1, hours);
        }

        [Test]
        public void AdvanceHours_StepsHourByHour() {
            var (_, clock) = NewClock();
            int hours = 0;
            clock.HourElapsed += () => hours++;

            clock.AdvanceHours(8);

            Assert.AreEqual(8, hours);
            Assert.AreEqual(8 * GameClock.TicksPerHour, clock.Ticks);
        }

        [Test]
        public void Advance_RaisesDayElapsedBeforeHourElapsed() {
            var (_, clock) = NewClock();
            var order = new System.Collections.Generic.List<string>();
            clock.HourElapsed += () => order.Add("hour");
            clock.DayElapsed += _ => order.Add("day");

            clock.Advance(GameClock.TicksPerDay);

            Assert.AreEqual(new[] { "day" }, order.ToArray(),
                "crossing exactly one day lands on hour 0 again, so only the day event fires");
        }

        [Test]
        public void DayElapsed_ReportsTheDayJustCompleted_NotTheOneBeingEntered() {
            // The engine's periodic checks key on the day being left, so the argument must be it.
            var (_, clock) = NewClock(3 * GameClock.TicksPerDay + 100);
            int reported = -1;
            clock.DayElapsed += day => reported = day;

            clock.Advance(GameClock.TicksPerDay);

            Assert.AreEqual(3, reported);
        }

        [Test]
        public void Advance_NonPositive_DoesNothing() {
            var (session, clock) = NewClock(500);
            int ticks = 0;
            clock.HourElapsed += () => ticks++;

            clock.Advance(0);
            clock.Advance(-10);

            Assert.AreEqual(500, session.GameTimeIn2Seconds);
            Assert.AreEqual(0, ticks);
        }

        [Test]
        public void SetFlagTimer_SetsTheGlobalFlagWhenItExpires() {
            var (session, clock) = NewClock();
            clock.ScheduleTimer(TimerType.SetFlag, key: 8127, ticks: 100);

            clock.Advance(99);
            Assert.IsTrue(clock.HasTimer(TimerType.SetFlag, 8127));
            Assert.AreNotEqual(1, session.GetGlobalValue(8127));

            clock.Advance(1);
            Assert.IsFalse(clock.HasTimer(TimerType.SetFlag, 8127));
            Assert.AreEqual(1, session.GetGlobalValue(8127));
        }

        [Test]
        public void ClearFlagTimer_ClearsTheGlobalFlagWhenItExpires() {
            var (session, clock) = NewClock();
            session.SetGlobalFlag(8127, true);
            clock.ScheduleTimer(TimerType.ClearFlag, key: 8127, ticks: 10);

            clock.Advance(10);

            Assert.AreEqual(0, session.GetGlobalValue(8127));
        }

        [Test]
        public void ScheduleTimer_Accumulate_ExtendsTheExistingTimer() {
            var (session, clock) = NewClock();
            clock.ScheduleTimer(TimerType.SetFlag, 1, 100);
            clock.ScheduleTimer(TimerType.SetFlag, 1, 50, accumulate: true);

            clock.Advance(140);
            Assert.IsTrue(clock.HasTimer(TimerType.SetFlag, 1), "100 + 50 = 150 remaining");

            clock.Advance(10);
            Assert.AreEqual(1, session.GetGlobalValue(1));
        }

        [Test]
        public void ScheduleTimer_Replace_OverwritesTheRemainingTime() {
            var (_, clock) = NewClock();
            clock.ScheduleTimer(TimerType.SetFlag, 1, 100);
            clock.ScheduleTimer(TimerType.SetFlag, 1, 10, replaceExisting: true);

            clock.Advance(10);

            Assert.IsFalse(clock.HasTimer(TimerType.SetFlag, 1));
        }

        [Test]
        public void ScheduleTimer_WithoutAMergeFlag_AppendsASecondTimer() {
            var (_, clock) = NewClock();
            clock.ScheduleTimer(TimerType.SetFlag, 1, 10);
            clock.ScheduleTimer(TimerType.SetFlag, 1, 100);

            clock.Advance(10);

            Assert.IsTrue(clock.HasTimer(TimerType.SetFlag, 1), "the second entry still has 90 left");
        }

        [Test]
        public void ScheduleTimer_RefusesA21stTimer() {
            var (_, clock) = NewClock();
            for (int i = 0; i < GameClock.MaxTimers; i++) {
                Assert.IsTrue(clock.ScheduleTimer(TimerType.SetFlag, i, 100));
            }

            Assert.IsFalse(clock.ScheduleTimer(TimerType.SetFlag, 999, 100));
        }

        [Test]
        public void TickTimers_ExpiresEveryDueTimerInOneAdvance() {
            var (session, clock) = NewClock();
            clock.ScheduleTimer(TimerType.SetFlag, 1, 10);
            clock.ScheduleTimer(TimerType.SetFlag, 2, 20);
            clock.ScheduleTimer(TimerType.SetFlag, 3, 500);

            clock.Advance(50);

            Assert.AreEqual(1, session.GetGlobalValue(1));
            Assert.AreEqual(1, session.GetGlobalValue(2));
            Assert.IsTrue(clock.HasTimer(TimerType.SetFlag, 3));
        }

        [Test]
        public void SpellTimer_SetsItsPaletteBitWhileItRuns() {
            var (session, clock) = NewClock();
            clock.ScheduleTimer(TimerType.Spell, key: 3, ticks: 100);

            clock.Advance(10);

            Assert.AreEqual(GameData.Resources.Spells.SpellPaletteEvents.BitFor(3),
                session.PaletteEventMask);
        }

        [Test]
        public void SpellTimer_ClearsItsPaletteBitOnTheTickItExpires() {
            // The hook runs on every tick INCLUDING the expiring one, which is the only moment the
            // bit can be cleared — the entry is removed immediately afterwards. A tick loop that
            // skipped live-entry work would leave the effect on forever.
            var (session, clock) = NewClock();
            clock.ScheduleTimer(TimerType.Spell, key: 3, ticks: 100);
            clock.Advance(10);

            clock.Advance(90);

            Assert.AreEqual(0, session.PaletteEventMask);
            Assert.IsFalse(clock.HasTimer(TimerType.Spell, 3));
        }

        [Test]
        public void SpellTimer_ClearsTheBitEvenWhenOvershotInOneAdvance() {
            var (session, clock) = NewClock();
            clock.ScheduleTimer(TimerType.Spell, key: 5, ticks: 10);

            clock.Advance(10_000);

            Assert.AreEqual(0, session.PaletteEventMask);
        }

        [Test]
        public void SpellTimers_DoNotDisturbEachOthersBits() {
            var (session, clock) = NewClock();
            clock.ScheduleTimer(TimerType.Spell, key: 1, ticks: 10);
            clock.ScheduleTimer(TimerType.Spell, key: 4, ticks: 500);

            clock.Advance(50);

            Assert.AreEqual(GameData.Resources.Spells.SpellPaletteEvents.BitFor(4),
                session.PaletteEventMask);
        }

        [Test]
        public void LightTimer_StillExpiresWithoutAHook() {
            // Light's palette fade belongs to the torch task; until then it must at least count
            // down and disappear rather than accumulating in the pool.
            var (_, clock) = NewClock();
            clock.ScheduleTimer(TimerType.Light, key: 2, ticks: 10);

            clock.Advance(50);

            Assert.IsFalse(clock.HasTimer(TimerType.Light, 2));
        }
    }
}
