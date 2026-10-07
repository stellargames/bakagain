namespace BakAgain.Core.Services {
    using System;
    using System.Collections.Generic;
    using GameData.Resources.Dialog.Actions;

    /// <summary>
    /// The game clock: the single owner of "how much game time has passed" and of the
    /// timer pool that time-scheduled effects hang off.
    ///
    /// <para>Faithful port of the original's clock — <c>gstate_advance_time</c> (ovr131 @0x42b37,
    /// canassa <c>SRC/GAME/STATE/GSTATE.C</c>) plus the timer pool <c>timerpool_tick</c>
    /// (ovr133 @0x4396f, <c>SRC/GAME/STATE/TIMERPL.C</c>).</para>
    ///
    /// <para>Time lives in <see cref="GameSession.GameTimeIn2Seconds"/> — state stays data, this
    /// class is only the mutator. Everything that happens *because* time passed (party regen,
    /// conditions, rations, daylight) subscribes to <see cref="HourElapsed"/> /
    /// <see cref="DayElapsed"/> rather than living here.</para>
    ///
    /// <para><b>Boundary events fire at most once per call</b>, exactly as the original does: it
    /// compares the hour/day *index* before and after the addition, so advancing eight hours in
    /// one call runs the hourly work once, not eight times. Callers that want per-hour effects
    /// step hour by hour, which is what the rest loop does (<c>gstate_advance_half_hours</c>,
    /// called once per increment from ENCAMP.C / MODALSCR.C).</para>
    ///
    /// <para>Not yet modelled, deliberately: the sleep/last-action snapshot
    /// (<c>dwLastActionTimeSnapshot</c>), which only feeds the rest branch inside the hourly
    /// tick, and rehydrating pending timers from a loaded save's timer block.</para>
    /// </summary>
    public sealed class GameClock {
        // The unit arithmetic itself lives in GameData so the extractors, the save reader and this
        // service cannot drift apart on it; these stay as the names Unity code already uses.

        /// <summary>0x708 ticks — 1800 × 2s = one hour.</summary>
        public const long TicksPerHour = GameData.Resources.GameState.GameTime.UnitsPerHour;

        /// <summary>0xa8c0 ticks — 43200 × 2s = one day.</summary>
        public const long TicksPerDay = GameData.Resources.GameState.GameTime.UnitsPerDay;

        /// <summary>The engine's fixed pool size (<c>aTimerEventPool[0x14]</c>); a 21st timer is dropped.</summary>
        public const int MaxTimers = 0x14;

        private readonly GameSession _session;
        private readonly List<Entry> _timers = new List<Entry>(MaxTimers);

        public GameClock(GameSession session) {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <summary>Game time in 2-second ticks — the engine's own unit (<c>GameSession.GameTimeIn2Seconds</c>).</summary>
        public long Ticks => _session.GameTimeIn2Seconds;
        /// <summary>Whole days elapsed since time zero.</summary>
        public int Day => GameData.Resources.GameState.GameTime.DayOf(_session.GameTimeIn2Seconds);
        /// <summary>Hour of the current day, 0..23.</summary>
        public int HourOfDay =>
            GameData.Resources.GameState.GameTime.HourOfDay(_session.GameTimeIn2Seconds);

        /// <summary>Raised once per <see cref="Advance"/> that crosses an hour boundary.</summary>
        public event Action HourElapsed;
        /// <summary>
        /// Raised once per <see cref="Advance"/> that crosses a day boundary, before
        /// <see cref="HourElapsed"/>. The argument is the day just <b>completed</b>, not the one
        /// being entered — the engine's periodic checks are keyed on the day being left.
        /// </summary>
        public event Action<int> DayElapsed;

        /// <summary>Advance the clock by <paramref name="ticks"/> 2-second ticks.</summary>
        public void Advance(long ticks) {
            if (ticks <= 0) {
                return;
            }

            long before = _session.GameTimeIn2Seconds;
            long after = before + ticks;
            _session.GameTimeIn2Seconds = after;

            // Day first, then hour — the original's order, and it matters: the daily work
            // (rations, the 30-day stat growth) runs before the hourly regen tick.
            if (after / TicksPerDay != before / TicksPerDay) {
                DayElapsed?.Invoke((int)(before / TicksPerDay));
            }
            if (after % TicksPerDay / TicksPerHour != before % TicksPerDay / TicksPerHour) {
                HourElapsed?.Invoke();
            }

            TickTimers(ticks);
        }

        /// <summary>Advance by whole hours (resting, waiting, scripted skips).</summary>
        public void AdvanceHours(int hours) {
            for (int i = 0; i < hours; i++) {
                Advance(TicksPerHour);
            }
        }

        /// <summary>
        /// Counts every timer down WITHOUT moving the clock — <c>timerpool_tick</c> on its own.
        /// </summary>
        /// <remarks>
        /// <c>savegame_chapter_start_dispatch</c> calls <c>timerpool_tick(30000)</c> eighty times while
        /// the game time is set separately, so a chapter start expires (and fires) the pool without
        /// raising the day and hour events <see cref="Advance"/> would.
        /// </remarks>
        public void TickTimersOnly(long ticks) {
            if (ticks > 0) {
                TickTimers(ticks);
            }
        }

        /// <summary>
        /// Add a timer, or fold into the matching one. <paramref name="accumulate"/> adds to an
        /// existing timer's remaining time; <paramref name="replaceExisting"/> overwrites it.
        /// With neither, a new entry is always appended. Returns false when the pool is full.
        /// </summary>
        /// <remarks>
        /// <b>A spell timer raises its effect bit AT ONCE, not on the next tick.</b> Every caller
        /// that sets one follows it with <c>ApplyActiveSpellTimerFlag</c> (0x6cecb), which is the
        /// same hook <see cref="TickTimers"/> runs — so the bit going on is part of scheduling and
        /// only its going off waits for the countdown. Leaving it to the first tick would hide a
        /// just-cast spell from the travel screen's effects strip until the clock next moved, which
        /// on a stationary party is a long time.
        /// </remarks>
        public bool ScheduleTimer(TimerType type, int key, long ticks, bool accumulate = false,
            bool replaceExisting = false) {
            if (accumulate || replaceExisting) {
                for (int i = 0; i < _timers.Count; i++) {
                    Entry existing = _timers[i];
                    if (existing.Type != type || existing.Key != key) {
                        continue;
                    }
                    existing.Remaining = accumulate ? existing.Remaining + ticks : ticks;
                    _timers[i] = existing;
                    ApplySpellFlag(type, key, existing.Remaining);
                    return true;
                }
            }

            if (_timers.Count >= MaxTimers) {
                return false;
            }
            _timers.Add(new Entry { Type = type, Key = key, Remaining = ticks });
            ApplySpellFlag(type, key, ticks);
            return true;
        }

        /// <summary>The running-effects bit for a spell timer; nothing for the other kinds.</summary>
        /// <summary>
        /// The carried light going out — <c>palette_fade_run_scheduled</c>'s key-0 arm calls
        /// <c>itemuse_party_tick_temporary</c> when the value reaches zero (PALETTE.C:305-313).
        /// </summary>
        private void ApplyItemLight(TimerType type, int key, long remaining) {
            if (type != TimerType.Light || !GameData.Resources.World.LightSourceDecay.SpendsItemChargesAt(
                    (GameData.Resources.World.LightSourceDecay.Source)key, remaining)) {
                return;
            }
            byte[] party = _session.ActivePartyIndices;
            if (party == null) {
                return;
            }
            foreach (byte member in party) {
                GameData.Resources.Inventory.ItemLight.BurnDown(_session.GetActorInventory(member));
            }
        }

        private void ApplySpellFlag(TimerType type, int key, long remaining) {
            if (type != TimerType.Spell) {
                return;
            }
            _session.PaletteEventMask = GameData.Resources.Spells.SpellPaletteEvents.Apply(
                _session.PaletteEventMask, key, remaining);
        }

        /// <summary>
        /// The live pool, for the save writer. Pending timers must persist: a temporary flag whose
        /// clear is still queued would otherwise stay set for the rest of the game.
        /// </summary>
        public IReadOnlyList<GameData.Resources.Data.SaveGameTimerData> PendingTimers {
            get {
                var live = new List<GameData.Resources.Data.SaveGameTimerData>(_timers.Count);
                foreach (Entry e in _timers) {
                    // Mode is not round-tripped: it only ever decides how an incoming schedule is
                    // merged, and by the time an entry is in the pool it has already been applied.
                    live.Add(new GameData.Resources.Data.SaveGameTimerData(
                        e.Type, 0, (short)e.Key, (int)e.Remaining));
                }
                return live;
            }
        }

        /// <summary>Restore a loaded save's pool, replacing whatever is queued.</summary>
        public void LoadTimers(IEnumerable<GameData.Resources.Data.SaveGameTimerData> timers, int count) {
            _timers.Clear();
            if (timers == null) {
                return;
            }
            int taken = 0;
            foreach (GameData.Resources.Data.SaveGameTimerData t in timers) {
                if (taken >= count || taken >= MaxTimers) {
                    break;
                }
                taken++;
                // A slot past the live count is stale padding; one at zero has already fired.
                if (t == null || t.Time <= 0) {
                    continue;
                }
                _timers.Add(new Entry { Type = t.Type, Key = t.Key, Remaining = t.Time });
            }
        }

        /// <summary>How long that timer has left, or zero when none is running.</summary>
        public long RemainingTicks(TimerType type, int key) {
            for (int i = 0; i < _timers.Count; i++) {
                if (_timers[i].Type == type && _timers[i].Key == key) {
                    return _timers[i].Remaining;
                }
            }

            return 0;
        }

        /// <summary>
        /// Runs every matching timer out now, then ticks the pool so it settles — the effect ends
        /// as though its time were up.
        /// </summary>
        /// <returns>How many entries were expired.</returns>
        /// <remarks>
        /// <b>Zero the entries and tick by ZERO — the two halves are the original's, in that
        /// order.</b> EVTCOND.C case 13 writes <c>nValue = 0</c> over the matching pool entries and
        /// then calls <c>timerpool_tick(0)</c>, and the tick is what actually ends the effect:
        /// <see cref="TickTimers"/> runs the per-tick hooks and removes the zeroed entries. Writing
        /// the zeroes without ticking would leave dead entries in the pool holding their effect on
        /// until the clock next moved, which on a stationary party is a long time.
        ///
        /// <para>A zero-length tick is safe for everything else in the pool: every other entry
        /// loses nothing and keeps its remaining time.</para>
        /// </remarks>
        public int ExpireTimers(TimerType type, int key) {
            var matched = 0;
            for (int i = 0; i < _timers.Count; i++) {
                Entry entry = _timers[i];
                if (entry.Type != type || entry.Key != key) {
                    continue;
                }

                entry.Remaining = 0;
                _timers[i] = entry;
                matched++;
            }

            if (matched > 0) {
                TickTimers(0);
            }

            return matched;
        }

        /// <summary>Is a timer of this type pending for this key?</summary>
        public bool HasTimer(TimerType type, int key) {
            for (int i = 0; i < _timers.Count; i++) {
                if (_timers[i].Type == type && _timers[i].Key == key) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// <c>timerpool_tick</c>: every entry loses the elapsed ticks, clamped at zero; one that
        /// reaches zero is removed.
        ///
        /// <para><b>The two kinds do their work at different moments, and the order matters.</b>
        /// <see cref="TimerType.Light"/> and <see cref="TimerType.Spell"/> run a hook on
        /// <i>every</i> tick — including the one where the entry hits zero, which is exactly how
        /// their effect gets switched back off — while the flag kinds act only on expiry. So the
        /// per-tick hooks run before the removal check, not after it. An earlier shape here
        /// returned early once the entry was still live, which would have made it impossible to
        /// ever clear a spell's palette bit.</para>
        ///
        /// <para>The Light kind's hook is the carried item's burn-down: on the tick its timer reaches
        /// zero, every lit torch and Ring of Prandur in the party goes out and spends a use (TASK-518).
        /// The light LEVEL needs no hook — WorldLightingService reads the remaining ticks.</para>
        ///
        /// <para>The engine swap-removes with the last entry, so pool order is not observable;
        /// iterating backwards is the same behaviour with less bookkeeping.</para>
        /// </summary>
        private void TickTimers(long elapsed) {
            for (int i = _timers.Count - 1; i >= 0; i--) {
                Entry entry = _timers[i];
                entry.Remaining -= elapsed;
                if (entry.Remaining < 0) {
                    entry.Remaining = 0;
                }

                // Per-tick kinds, run whether or not the entry survives this tick.
                ApplySpellFlag(entry.Type, entry.Key, entry.Remaining);
                ApplyItemLight(entry.Type, entry.Key, entry.Remaining);

                if (entry.Remaining != 0) {
                    _timers[i] = entry;
                    continue;
                }

                _timers.RemoveAt(i);
                switch (entry.Type) {
                    case TimerType.SetFlag:
                        _session.SetGlobalFlag(entry.Key, true);
                        break;
                    case TimerType.ClearFlag:
                        _session.SetGlobalFlag(entry.Key, false);
                        break;
                }
            }
        }

        private struct Entry {
            public TimerType Type;
            public int Key;
            public long Remaining;
        }
    }
}
