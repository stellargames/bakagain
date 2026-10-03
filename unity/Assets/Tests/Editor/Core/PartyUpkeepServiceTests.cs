namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using Cysharp.Threading.Tasks;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Dialog;
    using NUnit.Framework;
    using System.Threading;

    /// <summary>
    /// The clock actually driving party upkeep: who it touches, and when.
    /// </summary>
    [TestFixture]
    public class PartyUpkeepServiceTests {
        private GameSession _session;
        private GameClock _clock;
        private PartyUpkeepService _upkeep;

        [SetUp]
        public void SetUp() {
            _session = new GameSession();
            _session.Initialize(
                new SaveGameBuilder().WithBackingBody().WithPartyActors()
                    .WithParty(new[] { "A", "B", "C" }, new byte[] { 0, 1, 2 }).Build(),
                GameSessionSource.NewGame);
            _clock = new GameClock(_session);
            _upkeep = new PartyUpkeepService(_session, _clock, new NullLogger<PartyUpkeepService>());
        }

        [TearDown]
        public void TearDown() => _upkeep.Dispose();

        private ActorStat Health(int character) =>
            _session.StatsOf(character)[(int)ActorAttribute.Health];

        /// <summary>
        /// Health and Stamina are one pool, and any change to it redistributes them — Health
        /// saturates first, the remainder spills into Stamina. So a drain can leave Health *higher*
        /// than it started while the pool is smaller. The total is the honest measure.
        /// </summary>
        private int Pool(int character) =>
            _session.StatsOf(character)[(int)ActorAttribute.Health].Base
            + _session.StatsOf(character)[(int)ActorAttribute.Stamina].Base;

        [Test]
        public void ARaisedSkillQueuesItsNotice_AndTheSheetKeepsItsMark() {
            // EVTCOND.C:333-361 from the world loop: a failed barding raised Barding party-wide, and
            // the original said "The party's Barding ability has increased." on arrival in the world.
            // Use accumulates; repeat until a member's skill actually goes up (the fixture's actors
            // are not the ones that crossed a point on a single failed performance).
            for (int i = 0; i < 500 && (_session.PartyDirtyFlags & CharacterSheetRow.ImprovedDirtyBit) == 0; i++) {
                _session.BroadcastSkillUse(ActorAttribute.Barding, 1);
            }
            Assert.That(_session.PartyDirtyFlags & CharacterSheetRow.ImprovedDirtyBit, Is.Not.Zero,
                "the precondition: the raise set the bit");

            _upkeep.QueueSkillNotice();

            ConditionAnnouncements.Announcement queued = System.Linq.Enumerable.Single(_upkeep.PendingAnnouncements);
            Assert.That(queued.DialogId, Is.EqualTo(0x200b30));
            Assert.That(queued.AuxValue, Is.EqualTo((int)ActorAttribute.Barding));
            Assert.That(_session.PartyDirtyFlags & CharacterSheetRow.ImprovedDirtyBit, Is.Zero);
            Assert.That(_session.GetGlobalValue(CharacterSheetRow.ChangedFlagFor(0, (int)ActorAttribute.Barding)),
                Is.EqualTo(1), "the notice reads the mark; the sheet is what clears it");
        }

        [Test]
        public void ACaughtAfflictionIsQueuedOnTheNextHour_AndItsFlagIsReadAndCleared() {
            // TASK-500: stat_combatant_apply_condition sets CONDITION(7320 + character*7 + condition)
            // and the party-dirty bit; the hourly tick drains them into "felt ill" dialogs.
            int flag = ConditionAnnouncements.FlagFor(1, ActorCondition.Poisoned);
            ConditionEngine.Apply(_session.ConditionsOf(1), ActorCondition.Poisoned, 20);
            Assert.That(_session.GetGlobalValue(flag), Is.EqualTo(1));
            Assert.That(_session.PartyDirtyFlags & ConditionAnnouncements.DirtyBit, Is.Not.Zero);

            _clock.AdvanceHours(1);

            ConditionAnnouncements.Announcement queued = System.Linq.Enumerable.Single(_upkeep.PendingAnnouncements);
            Assert.That(queued.DialogId, Is.EqualTo(0xf7));
            Assert.That((queued.FirstActor, queued.Count), Is.EqualTo((1, 1)));
            Assert.That(_session.GetGlobalValue(flag), Is.EqualTo(0), "read and cleared");
            Assert.That(_session.PartyDirtyFlags & ConditionAnnouncements.DirtyBit, Is.Zero);
        }

        [Test]
        public void NearDeathOnEveryMemberPutsThePartyDown_AndHealingOneLiftsIt() {
            // TASK-505: the hourly and daily upkeep write Near-death through ConditionEngine.Apply and
            // nothing recomputed the party-down byte there, so a party that collapsed while camping
            // rested on for 80 days where the original ends the game.
            foreach (byte character in new byte[] { 0, 1, 2 }) {
                ConditionEngine.Apply(_session.ConditionsOf(character), ActorCondition.NearDeath, 50);
            }
            Assert.That((int)_session.PartyDeathState,
                Is.EqualTo(GameData.Resources.GameState.PartyDownState.Noticed));

            ConditionEngine.Apply(_session.ConditionsOf(1), ActorCondition.NearDeath, -100);
            Assert.That((int)_session.PartyDeathState,
                Is.EqualTo(GameData.Resources.GameState.PartyDownState.Standing));
        }

        private int RestPrompts() => System.Linq.Enumerable.Count(_upkeep.PendingAnnouncements,
            queued => queued.DialogId == UpkeepEngine.RestPromptDialog);

        // One hour short of the warning, so the next hour lands on it.
        private void AwakeAlmostSeventeenHours() =>
            _session.LastRestTicks = _clock.Ticks + GameClock.TicksPerHour - UpkeepEngine.ExhaustionWarningTicks;

        [Test]
        public void AwakeSeventeenHoursTheyAreToldToRest_EveryHourWhileNobodyDrops() {
            // TASK-494, GSTATE.C:305: the warning alone at seventeen hours, then past eighteen the drain
            // followed by the warning again while the whole party is still conscious.
            AwakeAlmostSeventeenHours();

            _clock.AdvanceHours(3);

            Assert.That(RestPrompts(), Is.EqualTo(3));
        }

        [Test]
        public void TheMendersTwelveHourBatchNagsOnce() {
            // MODALSCR.C:669 clears allowEventDialog after the first event of the countdown.
            AwakeAlmostSeventeenHours();
            _upkeep.OneEventDialogPerBatch = true;

            _clock.AdvanceHours(3);
            _upkeep.OneEventDialogPerBatch = false;

            Assert.That(RestPrompts(), Is.EqualTo(1));
        }

        [Test]
        public void ADialogTimeSkipAndARestNeverTellThemToRest() {
            AwakeAlmostSeventeenHours();
            _session.DialogTimeSkipInProgress = true;
            _clock.AdvanceHours(2);
            _session.DialogTimeSkipInProgress = false;

            _upkeep.RestQuality = UpkeepEngine.PartialRestQuality;
            _clock.AdvanceHours(2);

            Assert.That(RestPrompts(), Is.Zero);
        }

        [Test]
        public void TheDailyMealLeavesTheLastMemberFedInTheActorRegister() {
            // TASK-493: gstate_member_consume_rations writes nEvtArgActor0 for each member it feeds.
            _session.EventActor = -1;

            _clock.Advance(GameClock.TicksPerDay - _clock.Ticks % GameClock.TicksPerDay);

            Assert.That(_session.EventActor,
                Is.EqualTo((int)_session.ActivePartyIndices[_session.ActivePartyIndices.Length - 1]));
        }

        [Test]
        public void WalkingForAnHourHealsNobody() {
            int before = Pool(0);

            _clock.AdvanceHours(1);

            Assert.That(Pool(0), Is.EqualTo(before), "the world loop rests nobody");
        }

        [Test]
        public void RestingForAnHourHealsTheActiveParty() {
            int before = Pool(0);
            _upkeep.RestQuality = 133; // a rest that fills the pool

            _clock.AdvanceHours(1);

            Assert.That(Pool(0), Is.GreaterThan(before));
        }

        [Test]
        public void AfflictionsDriftOnceAnHourEvenWhileTravelling() {
            _session.ConditionsOf(1)[ActorCondition.Plagued] = 10;

            _clock.AdvanceHours(1);

            Assert.That(_session.ConditionsOf(1)[ActorCondition.Plagued], Is.EqualTo(11));
        }

        [Test]
        public void OnlyTheActivePartyIsTouched() {
            _session.ConditionsOf(5)[ActorCondition.Plagued] = 10; // character 5 is not in the roster

            _clock.AdvanceHours(1);

            Assert.That(_session.ConditionsOf(5)[ActorCondition.Plagued], Is.EqualTo(10));
        }

        [Test]
        public void AnHourIsAnHourHoweverTheClockGetsThere() {
            _session.ConditionsOf(0)[ActorCondition.Plagued] = 10;

            // One eight-hour jump runs the hourly work once — the engine compares hour indices.
            _clock.Advance(GameClock.TicksPerHour * 8);

            Assert.That(_session.ConditionsOf(0)[ActorCondition.Plagued], Is.EqualTo(11));
        }

        [Test]
        public void NearDeathLiftsWhenADayPasses() {
            _session.ConditionsOf(0)[ActorCondition.NearDeath] = 50;

            _clock.Advance(GameClock.TicksPerDay);

            Assert.That(_session.ConditionsOf(0)[ActorCondition.NearDeath], Is.EqualTo(44)); // -6
        }

        [Test]
        public void MaximumsGrowOnlyOnTheThirtiethDay() {
            byte startingMax = Health(0).Max;

            // Day 0 is a growth day, and it is the day being LEFT that counts.
            _clock.Advance(GameClock.TicksPerDay);
            Assert.That(Health(0).Max, Is.EqualTo(startingMax + 1));

            // Leaving day 1 is not.
            _clock.Advance(GameClock.TicksPerDay);
            Assert.That(Health(0).Max, Is.EqualTo(startingMax + 1));
        }

        [Test]
        public void TheGrowthLightsUpHealthAndStaminaOnTheSheet() {
            // GSTATE.C:382-384 writes SKILL_IMPROVED(actor*0x11) and +1 beside the growth. Nothing
            // wrote these flags at all before TASK-611, so the sheet's highlight never fired.
            int health = CharacterSheetRow.ChangedFlagFor(0, (int)ActorAttribute.Health);
            int stamina = CharacterSheetRow.ChangedFlagFor(0, (int)ActorAttribute.Stamina);
            Assert.That(_session.GetGlobalValue(health) ?? 0, Is.EqualTo(0), "nothing marked yet");

            _clock.Advance(GameClock.TicksPerDay);   // leaving day 0 — a growth day

            Assert.That(_session.GetGlobalValue(health) ?? 0, Is.Not.EqualTo(0));
            Assert.That(_session.GetGlobalValue(stamina) ?? 0, Is.Not.EqualTo(0));
        }

        [Test]
        public void AnOrdinaryDayMarksNothing() {
            // The control: without it the test above would pass against code that marked every day.
            _clock.Advance(GameClock.TicksPerDay);   // growth day, marks written
            _session.SetGlobalValue(
                CharacterSheetRow.ChangedFlagFor(0, (int)ActorAttribute.Health), 0);

            _clock.Advance(GameClock.TicksPerDay);   // leaving day 1 — not a growth day

            Assert.That(
                _session.GetGlobalValue(
                    CharacterSheetRow.ChangedFlagFor(0, (int)ActorAttribute.Health)) ?? 0,
                Is.EqualTo(0));
        }

        [Test]
        public void ExhaustionIsOnNowThatRestingExists() {
            // Was ExhaustionIsOffUntilRestingExists, a tripwire holding the drain off while there
            // was no cure for it. The encampment screen landed (TASK-84), so the premise is spent:
            // camping is reachable from the travel HUD, rest heals through this same hourly tick,
            // and a rest can be stopped. What matters now is that it defaults on.
            Assert.That(_upkeep.ExhaustionEnabled, Is.True);
        }

        [Test]
        public void TurningExhaustionOffIsStillTheRevertLever() {
            _upkeep.ExhaustionEnabled = false;
            _session.LastRestTicks = 0;
            int before = Pool(0);

            _clock.Advance(GameClock.TicksPerHour * 20); // well past the 18-hour threshold

            Assert.That(Pool(0), Is.EqualTo(before),
                "clearing the flag must still suppress the drain — it is the one-line revert");
        }

        [Test]
        public void ExhaustionDrainsHealthOnceItIsSwitchedOn() {
            _upkeep.ExhaustionEnabled = true;
            _session.LastRestTicks = 0;
            int before = Pool(0);

            _clock.Advance(GameClock.TicksPerHour * 20);

            Assert.That(Pool(0), Is.EqualTo(before - 2), "Locklear loses 2 an hour");
        }

        [Test]
        public void RESTINGSUSPENDSTheDrain() {
            // gstate_hourly_tick only reaches the exhaustion loop when its second argument is
            // non-zero, and BOTH rest callers pass zero (camping 0x7061a, the inn 0x5021e). Only
            // the world loop tires the party.
            //
            // Caught end-to-end rather than by reading: a 22-hour stay at an inn came out with
            // Locklear on 77 of 100, because the drain fires from the eighteenth hour and
            // LastRestTicks is not moved until the rest ends.
            _upkeep.ExhaustionEnabled = true;
            _session.LastRestTicks = 0;
            _upkeep.RestQuality = 133;              // an inn's quality
            int before = Pool(0);

            _clock.Advance(GameClock.TicksPerHour * 20);

            Assert.That(Pool(0), Is.GreaterThanOrEqualTo(before),
                "a rest must never leave the party worse off than it found them");
        }

        [Test]
        public void WalkingStillTiresThePartyWhileNotResting() {
            // The other half of the same guard: quality 0 IS the world loop, and it must still drain.
            _upkeep.ExhaustionEnabled = true;
            _session.LastRestTicks = 0;
            _upkeep.RestQuality = 0;
            int before = Pool(0);

            _clock.Advance(GameClock.TicksPerHour * 20);

            Assert.That(Pool(0), Is.EqualTo(before - 2), "Locklear loses 2 an hour");
        }

        /// <summary>
        /// Records the cancellation token each show is handed, and answers at once so the play
        /// loop runs without a player loop to pump it.
        /// </summary>
        private sealed class TokenCapturingDialogs : BakAgain.UI.IDialogManager {
            public int Shown { get; private set; }
            public CancellationToken LastToken { get; private set; }

            public UniTask<int> ShowById(int id, CancellationToken cancellationToken = default) {
                Shown++;
                LastToken = cancellationToken;
                return UniTask.FromResult(0);
            }

            public UniTask ShowEntry(DialogEntry entry, CancellationToken cancellationToken = default)
                => UniTask.CompletedTask;
            public UniTask ShowEntry(DialogPlay play, CancellationToken cancellationToken = default)
                => UniTask.CompletedTask;
            public UniTask DisplayEntry(DialogEntry entry, CancellationToken cancellationToken = default)
                => UniTask.CompletedTask;
            public UniTask DisplayEntry(DialogPlay play, CancellationToken cancellationToken = default)
                => UniTask.CompletedTask;
            public UniTask<UnityEngine.UIElements.VisualElement> BuildStyledBoxAsync(
                DialogEntry entry, UnityEngine.UIElements.VisualElement host)
                => UniTask.FromResult<UnityEngine.UIElements.VisualElement>(null);
            public UniTask<DialogPlay> ResolveById(int id, CancellationToken cancellationToken = default)
                => UniTask.FromResult<DialogPlay>(null);
            public void ClearDialog() { }
            public void LetClicksThroughPanel() { }
            public Cysharp.Threading.Tasks.UniTask ShowEntry(GameData.Resources.Dialog.DialogEntry entry,
                System.Action<UnityEngine.UIElements.VisualElement, GameData.Resources.Layout.LayoutHint> decorate,
                System.Threading.CancellationToken cancellationToken = default) => Cysharp.Threading.Tasks.UniTask.CompletedTask;
            public Cysharp.Threading.Tasks.UniTask<UnityEngine.Color[]> ResolvePaletteAsync() => Cysharp.Threading.Tasks.UniTask.FromResult<UnityEngine.Color[]>(null);
            public void SetActivePalette(UnityEngine.Color[] palette) { }
            public UniTask<bool> ShowAcceptOrCancelById(int id, System.Threading.CancellationToken ct = default) =>
                UniTask.FromResult(false);
            public UniTask<bool> ShowConfirmById(int id, CancellationToken cancellationToken = default)
                => UniTask.FromResult(false);
            public UniTask<int> ShowChoiceById(int id, CancellationToken cancellationToken = default)
                => UniTask.FromResult(-1);
            public UniTask<int> ShowChoiceIndexById(int id, CancellationToken cancellationToken = default)
                => UniTask.FromResult(-1);
        }

        [Test]
        public void AnAnnouncementIsShownWithTheCallersCancellation_NotAnUncancellableOne() {
            // TASK-563. CampMenu.RestAsync awaits PlayAnnouncementsAsync inside its try, and its
            // finally is the ONLY place that writes LastRestTicks, restores RestQuality and nulls
            // _restCancel. The call used to pass no token, so when a dialog's panel was torn down by
            // something other than its own dismissal the await never returned, that finally never
            // ran, and IsResting stayed true — every later rest, dial-stone rests included, then
            // died on RestAsync's opening guard. Measured live with _announcing=True and 18 queued
            // copies of dialog 0x40 while the party read 1115 hours awake.
            //
            // The token is what makes StopResting()'s Cancel() able to unwind it, so assert the
            // caller's own token arrives at the dialog rather than default(CancellationToken).
            ConditionEngine.Apply(_session.ConditionsOf(1), ActorCondition.Poisoned, 20);
            _clock.AdvanceHours(1);
            Assert.That(System.Linq.Enumerable.Any(_upkeep.PendingAnnouncements), Is.True,
                "the hourly tick queues the announcement this test plays");

            var dialogs = new TokenCapturingDialogs();
            using var cancellation = new CancellationTokenSource();

            _upkeep.PlayAnnouncementsAsync(dialogs, cancellation.Token).Forget();

            Assert.That(dialogs.Shown, Is.GreaterThan(0), "the queued announcement was played");
            Assert.That(dialogs.LastToken.CanBeCanceled, Is.True,
                "a hung announcement must be cancellable, or it wedges the rest for the session");
            Assert.That(dialogs.LastToken, Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void DisposingStopsTheUpkeep() {
            _upkeep.Dispose();
            _session.ConditionsOf(0)[ActorCondition.Plagued] = 10;

            _clock.AdvanceHours(1);

            Assert.That(_session.ConditionsOf(0)[ActorCondition.Plagued], Is.EqualTo(10));
        }
    
        /// <summary>
        /// A dialog time-skip whose page ADVANCED ITSELF must not feed the party — TASK-624.
        /// </summary>
        /// <remarks>
        /// <c>GSTATE.C</c> runs <c>gstate_consume_rations_tick</c> only
        /// <c>if (recompute_party != 0)</c>, and <c>DIALOG.C:1506</c> sets that to
        /// <c>g_dwDialogInputCooldown == 0</c>. The cooldown is set by exactly one thing —
        /// <c>dialog_wait_for_acknowledge</c>'s <c>flags &amp; 0x40</c> branch
        /// (<c>AutoAdvanceTimer</c>), where the page dismisses itself instead of waiting for the
        /// player. So a page that advanced itself does not earn the party a meal.
        ///
        /// <para>What it cost before the gate existed: the zone 2 -&gt; zone 1 crossing skips five
        /// game days and the port fed on every one of the five boundaries. A party carrying one
        /// ration each ate it on the first and then starved four days, which drained both pools to
        /// zero and owed Near-death 100 on every member. The original wounds exactly one. Measured
        /// on both sides from the same save.</para>
        /// </remarks>
        [Test]
        public void ASkipWhoseDialogAutoAdvancedDoesNotFeedThePartyAndSoCannotStarveIt() {
            _session.DialogTimeSkipInProgress = true;
            _session.DialogTimeSkipAutoAdvanced = true;

            _clock.Advance(GameClock.TicksPerDay - _clock.Ticks % GameClock.TicksPerDay);

            _session.DialogTimeSkipAutoAdvanced = false;
            _session.DialogTimeSkipInProgress = false;

            foreach (byte member in _session.ActivePartyIndices) {
                Assert.That(_session.ConditionsOf(member)[ActorCondition.Starving], Is.Zero,
                    "a skip that auto-advanced must not run the meal, so nobody goes hungry for it");
            }
        }

        /// <summary>
        /// The control that makes the test above mean something: with the same day boundary and the
        /// same empty packs, an ACKNOWLEDGED skip does feed — and feeding with no food is what
        /// applies Starving. Without this, "Starving is 0" would also pass if the meal never ran at
        /// all.
        /// </summary>
        [Test]
        public void ASkipThePlayerAcknowledgedStillFeedsThePartyAndSoStarvesIt() {
            _session.DialogTimeSkipInProgress = true;
            _session.DialogTimeSkipAutoAdvanced = false;

            _clock.Advance(GameClock.TicksPerDay - _clock.Ticks % GameClock.TicksPerDay);

            _session.DialogTimeSkipInProgress = false;

            foreach (byte member in _session.ActivePartyIndices) {
                Assert.That(_session.ConditionsOf(member)[ActorCondition.Starving], Is.EqualTo(5),
                    "an acknowledged skip runs the meal; with nothing to eat that is Starving +5");
            }
        }

        /// <summary>
        /// Only the MEAL is gated. <c>GSTATE.C</c> runs the near-death recovery immediately below
        /// it and outside the <c>recompute_party</c> test, so a skipped meal must not also skip the
        /// one free recovery a wounded party gets — the only way out of Near-death without a temple
        /// or Restoratives.
        /// </summary>
        [Test]
        public void AGatedSkipStillAppliesTheDailyNearDeathRecovery() {
            byte member = _session.ActivePartyIndices[0];
            ConditionEngine.Apply(_session.ConditionsOf(member), ActorCondition.NearDeath, 99);
            Assert.That(_session.ConditionsOf(member)[ActorCondition.NearDeath], Is.EqualTo(99),
                "sanity: the rank must be set before the boundary, or this asserts nothing");

            _session.DialogTimeSkipInProgress = true;
            _session.DialogTimeSkipAutoAdvanced = true;
            _clock.Advance(GameClock.TicksPerDay - _clock.Ticks % GameClock.TicksPerDay);
            _session.DialogTimeSkipAutoAdvanced = false;
            _session.DialogTimeSkipInProgress = false;

            // (rank - 100) / 10 - 1 truncates to -1 for every rank in 91..100.
            Assert.That(_session.ConditionsOf(member)[ActorCondition.NearDeath], Is.EqualTo(98),
                "the recovery is not gated; it must still run one point");
        }
}
}
