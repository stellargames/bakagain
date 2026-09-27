namespace BakAgain.Core.Services {
    using Cysharp.Threading.Tasks;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Object;
    using Microsoft.Extensions.Logging;
    using System;

    /// <summary>
    /// Runs the passage-of-time effects on the active party: hourly affliction drift and
    /// regeneration, and the daily near-death recovery and constitutional growth. The rules live in
    /// <see cref="UpkeepEngine"/>; this is the thing that decides <i>when</i> to apply them and to
    /// <i>whom</i>, which is the half the engine deliberately leaves to a caller.
    ///
    /// <para>Faithful to <c>gstate_advance_time</c>, which walks the active party roster — not all
    /// six characters — on each boundary.</para>
    /// </summary>
    public sealed class PartyUpkeepService : IDisposable {
        private readonly GameSession _session;
        private readonly IGameClock _clock;
        private readonly ILogger<PartyUpkeepService> _logger;

        public PartyUpkeepService(GameSession session, IGameClock clock,
            ILogger<PartyUpkeepService> logger) {
            _session = session;
            _clock = clock;
            _logger = logger;
            _clock.HourElapsed += OnHourElapsed;
            _clock.DayElapsed += OnDayElapsed;
        }

        /// <summary>
        /// How the party is currently resting: 0 while travelling (the world loop passes zero, so
        /// walking never heals), <see cref="UpkeepEngine.PartialRestQuality"/> for a camp rest that
        /// tops up to 80%, any other non-zero value for a rest that fills the pool. The encampment
        /// screen sets this for the duration of a rest; nothing else may.
        /// </summary>
        public int RestQuality { get; set; }

        /// <summary>
        /// Whether staying awake too long drains health.
        ///
        /// <para><b>On since 2026-08-13</b>, when the encampment screen landed (TASK-84). It was
        /// held off until then because the only cure is resting: with no camp screen a party that
        /// walked for eighteen game-hours would bleed to zero with no way to recover. Camping is now
        /// reachable from the travel HUD, rest heals through this same hourly tick, and a rest can be
        /// stopped, so the drain has a cure.</para>
        ///
        /// <para>Still a flag rather than hard-wired: setting it false is the one-line revert if the
        /// balance turns out wrong in play.</para>
        /// </summary>
        public bool ExhaustionEnabled { get; set; } = true;

        private void OnHourElapsed() {
            // gstate_hourly_tick's second argument: time the party spends AWAKE. The world step and the
            // mender pass 1; camping, the inn and a dialog's time skip pass 0.
            bool awake = ExhaustionEnabled && RestQuality == 0 && !_session.DialogTimeSkipInProgress;
            bool allConscious = true;
            ForEachActiveMember((characterId, stats, conditions) => {
                UpkeepEngine.ApplyHour(
                    stats[(int)ActorAttribute.Health], stats[(int)ActorAttribute.Stamina],
                    conditions, characterId, RestQuality);

                // *** RESTING SUSPENDS THE DRAIN. *** gstate_hourly_tick reaches the exhaustion
                // loop (0x4296c) only when its second argument is non-zero, and BOTH rest callers
                // pass zero — camping at 0x7061a and the inn at 0x5021e — so neither an
                // encampment nor a bought night can wear the party down while it runs. Only the
                // world loop, which is the thing that tires them, passes it.
                //
                // Without this a long rest makes the party WORSE: a 22-hour stay at an inn came
                // out with Locklear on 77 of 100, because the drain fires from the eighteenth
                // hour and LastRestTicks is not moved until the rest ends.
                if (!awake) {
                    return;
                }
                long awakeTicks = _clock.Ticks - _session.LastRestTicks;
                if (UpkeepEngine.ExhaustionAfter(awakeTicks) == ExhaustionLevel.Draining) {
                    if (!UpkeepEngine.ApplyExhaustion(
                            stats[(int)ActorAttribute.Health], stats[(int)ActorAttribute.Stamina],
                            characterId, conditions)) {
                        allConscious = false;
                        // Worn down to nothing: ApplyExhaustion has just written Near-death, and
                        // the party-down byte is derived from those ranks.
                        _session.RecomputePartyDeathState();
                    }
                }
            });

            QueueRestPrompt(awake, allConscious);
            QueueAnnouncements();
        }

        private bool _oneEventDialogPerBatch;
        private bool _batchFired;

        /// <summary>
        /// A multi-hour batch that may raise only ONE event dialog — the mender's
        /// <c>if (gstate_advance_time(...) != 0) allowEventDialog = 0</c> (MODALSCR.C:669). Set it before
        /// the batch and clear it after; setting it either way starts a fresh batch.
        /// </summary>
        public bool OneEventDialogPerBatch {
            get => _oneEventDialogPerBatch;
            set {
                _oneEventDialogPerBatch = value;
                _batchFired = false;
            }
        }

        private bool AllowEventDialog => !_oneEventDialogPerBatch || !_batchFired;

        private void Enqueue(ConditionAnnouncements.Announcement dialog) {
            _announcements.Enqueue(dialog);
            _batchFired = true;
        }

        /// <summary>
        /// Dialog 64, "We need rest" — the head of <c>gstate_hourly_tick</c> (GSTATE.C:305, TASK-494).
        /// </summary>
        /// <remarks>
        /// <b>Every hour from the seventeenth awake</b>, and past the eighteenth only after the drain and
        /// only while the whole party is still conscious. It names nobody itself: its <c>@</c> is whoever
        /// the actor register last held, so it is queued with no actors and the register is left alone.
        /// </remarks>
        private void QueueRestPrompt(bool awake, bool allConscious) {
            if (!awake || !AllowEventDialog) {
                return;
            }
            ExhaustionLevel level = UpkeepEngine.ExhaustionAfter(_clock.Ticks - _session.LastRestTicks);
            if (level == ExhaustionLevel.Tired || (level == ExhaustionLevel.Draining && allConscious)) {
                Enqueue(new ConditionAnnouncements.Announcement(UpkeepEngine.RestPromptDialog, -1, -1, 0));
            }
        }

        private readonly System.Collections.Generic.Queue<ConditionAnnouncements.Announcement> _announcements =
            new System.Collections.Generic.Queue<ConditionAnnouncements.Announcement>();

        private bool _announcing;

        /// <summary>Affliction announcements the clock has queued and nobody has played yet.</summary>
        public System.Collections.Generic.IReadOnlyCollection<ConditionAnnouncements.Announcement>
            PendingAnnouncements => _announcements;

        /// <summary>
        /// The tail of <c>gstate_hourly_tick</c>: <c>if (arg0 &amp;&amp; (bPartyDirtyFlags &amp; 2))
        /// evtcond_pty_dirty_flags_process()</c> (TASK-500).
        /// </summary>
        /// <remarks>
        /// <b>Every hour is allowed to announce.</b> The world step, camping, the inn and a dialog's
        /// time skip all pass <c>arg0 = 1</c>. Only the mender's repair batch latches it after the first
        /// event (MODALSCR.C:669), which is not modelled: a repair spanning two catches tells both.
        ///
        /// <para>With every active member at Near-death (<c>bCombatExitRequest</c>) the process returns
        /// without reading anything, so the flags stay set and only the dirty bit is lost.</para>
        /// </remarks>
        private void QueueAnnouncements() {
            // arg0 == 0 leaves the dirty bit standing, so a latched batch announces on a later hour.
            if (!AllowEventDialog || (_session.PartyDirtyFlags & ConditionAnnouncements.DirtyBit) == 0) {
                return;
            }
            _session.PartyDirtyFlags &= ~ConditionAnnouncements.DirtyBit;
            byte[] roster = _session.ActivePartyIndices;
            if (roster == null || roster.Length == 0 || EveryoneIsNearDeath(roster)) {
                return;
            }
            var party = new int[roster.Length];
            for (var i = 0; i < roster.Length; i++) {
                party[i] = roster[i];
            }
            foreach (ConditionAnnouncements.Announcement announcement in ConditionAnnouncements.Drain(
                         party, flag => (_session.GetGlobalValue(flag) ?? 0) != 0,
                         flag => _session.SetGlobalFlag(flag, false))) {
                Enqueue(announcement);
            }
        }

        private bool EveryoneIsNearDeath(byte[] roster) {
            foreach (byte character in roster) {
                ActorConditions conditions = _session.ConditionsOf(character);
                if (conditions == null || !conditions.Has(ActorCondition.NearDeath)) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Plays every queued announcement in turn, naming its members the way the original's
        /// registers do.
        /// </summary>
        /// <remarks>
        /// <b>The original plays them inside the hourly tick, blocking the hour.</b> The port's tick is a
        /// synchronous event, so whoever advanced the clock awaits this afterwards: the camp and inn
        /// rest loops each hour, the world screen's per-frame check otherwise. A call made while one is
        /// already playing waits for it, so a rest loop never sleeps on under a dialog.
        /// </remarks>
        /// <param name="cancellationToken">
        /// The caller's cancellation, threaded all the way into the dialog's dismissal wait.
        ///
        /// <para><b>WITHOUT IT A HUNG ANNOUNCEMENT WEDGES THE CALLER FOR THE SESSION.</b>
        /// <c>CampMenu.RestAsync</c> awaits this inside its <c>try</c>, and its <c>finally</c> is the
        /// only place that writes <c>LastRestTicks</c>, restores <c>RestQuality</c> and nulls
        /// <c>_restCancel</c>. When the dialog's panel is torn down by something other than its own
        /// dismissal -- a navigator pop, the screen closing -- <c>WaitForDismiss</c> never returns,
        /// so that finally never runs and <c>IsResting</c> stays true; every later rest then dies on
        /// <c>RestAsync</c>'s opening guard, including a dial rest. Measured 2026-09-16 with
        /// <c>_announcing=True queued=18</c> (all dialog 0x40) and the party reading 1115 hours awake
        /// (TASK-563).</para>
        /// </param>
        public async UniTask PlayAnnouncementsAsync(BakAgain.UI.IDialogManager dialogs,
            System.Threading.CancellationToken cancellationToken = default) {
            if (_announcing) {
                await UniTask.WaitWhile(() => _announcing, cancellationToken: cancellationToken);
                return;
            }
            if (dialogs == null) {
                return;
            }
            _announcing = true;
            try {
                while (_announcements.Count > 0) {
                    ConditionAnnouncements.Announcement next = _announcements.Dequeue();
                    if (next.FirstActor >= 0) {
                        _session.EventActor = next.FirstActor;
                        _session.SetDialogSecondaryActorId(next.SecondActor);
                        _session.SetGlobalValue(GameData.Resources.GameState.GameStateEventFields.FieldBase,
                            next.Count);
                    }
                    await dialogs.ShowById(next.DialogId, cancellationToken);
                }
            } finally {
                _announcing = false;
            }
        }

        // The original's order on a day boundary: the periodic growth, then the meal, then the
        // near-death recovery.
        private void OnDayElapsed(int dayCompleted) {
            bool growthDay = UpkeepEngine.IsGrowthDay(dayCompleted);
            ForEachActiveMember((characterId, stats, conditions) => {
                if (growthDay) {
                    UpkeepEngine.ApplyPeriodicGrowth(
                        stats[(int)ActorAttribute.Health], stats[(int)ActorAttribute.Stamina]);
                    MarkGrowthOnTheSheet(characterId);
                }

                // gstate_member_consume_rations opens with `nEvtArgActor0 = member_slot`, so after the
                // meal the register names the last member fed — who a day's "@ yawned" then names
                // (TASK-493).
                _session.EventActor = characterId;
                // *** A SKIP WHOSE PAGE AUTO-ADVANCED DOES NOT FEED THE PARTY. ***
                // GSTATE.C runs gstate_consume_rations_tick only `if (recompute_party != 0)`, and
                // DIALOG.C:1506 sets that to `g_dwDialogInputCooldown == 0` -- which is non-zero
                // exactly when the page dismissed itself (flags & 0x40). The near-death recovery
                // below is NOT gated and must keep running.
                //
                // Measured on the zone 2 -> zone 1 crossing, five days from one save with rations
                // 1/1/0 (TASK-624): the ORIGINAL leaves James at 65 untouched and Owyn at 1 with
                // Near-death 99 -> 94, collapsing only the already-starving Gorath. Feeding on all
                // five boundaries instead made James and Owyn eat their single ration and then
                // starve four days, which drained both pools to zero and owed Near-death 100 on
                // each -- a whole party wiped by a crossing that should have cost one member.
                if (_session.DialogTimeSkipInProgress && _session.DialogTimeSkipAutoAdvanced) {
                    UpkeepEngine.ApplyDailyNearDeathRecovery(conditions);
                    return;
                }
                Meal meal = UpkeepEngine.ConsumeRations(
                    _session.GetActorInventory(characterId), conditions,
                    _session.ObjectInfo != null ? _session.ObjectInfo.GetById : (Func<int, ObjectInfo>)null);
                if (meal != Meal.Rations) {
                    _logger.LogInformation("Character {Character} ate {Meal} today.", characterId, meal);
                }

                UpkeepEngine.ApplyDailyNearDeathRecovery(conditions);
            });
        }

        /// <summary>
        /// Light up Health and Stamina on the character sheet after the monthly growth.
        /// </summary>
        /// <remarks>
        /// <c>GSTATE.C:382-384</c>, inside the same 30-day block as the growth itself:
        /// <c>n = SKILL_IMPROVED(activeParty[i] * 0x11); gstate_event_write(n, 1);
        /// gstate_event_write(n + 1, 1);</c> — Health then Stamina, the sheet's first two rows.
        ///
        /// <para><b>Unconditional, and deliberately so.</b> The original writes both marks without
        /// testing whether either maximum actually moved, so a member already at the 0xfa ceiling
        /// still lights up. That is a quirk of the original rather than a misreading of it —
        /// gating on <c>ApplyPeriodicGrowth</c>'s return value would look tidier and be wrong.</para>
        ///
        /// <para><see cref="CharacterSheetView"/> reads these and clears them as it draws, so the
        /// highlight is shown exactly once. Until this existed nothing wrote the flags at all and
        /// the sheet's highlight could never fire (TASK-611).</para>
        /// </remarks>
        private void MarkGrowthOnTheSheet(int characterId) {
            _session.SetGlobalFlag(
                CharacterSheetRow.ChangedFlagFor(characterId, (int)ActorAttribute.Health), true);
            _session.SetGlobalFlag(
                CharacterSheetRow.ChangedFlagFor(characterId, (int)ActorAttribute.Stamina), true);
        }

        private void ForEachActiveMember(Action<int, ActorStat[], ActorConditions> apply) {
            byte[] roster = _session.ActivePartyIndices;
            if (roster == null) {
                return;
            }
            foreach (byte characterId in roster) {
                ActorStat[] stats = _session.StatsOf(characterId);
                ActorConditions conditions = _session.ConditionsOf(characterId);
                if (stats == null || conditions == null) {
                    // A roster id with no runtime state means the session was never hydrated with
                    // party records — worth saying once rather than throwing every hour.
                    _logger.LogWarning("Upkeep skipped for character {Character}: no runtime state.",
                        characterId);
                    continue;
                }
                apply(characterId, stats, conditions);
            }
        }

        public void Dispose() {
            _clock.HourElapsed -= OnHourElapsed;
            _clock.DayElapsed -= OnDayElapsed;
        }
    }
}
