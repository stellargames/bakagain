namespace BakAgain.World.Scenes {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Data;
    using Microsoft.Extensions.Logging;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// A paid overnight stay at an inn — <c>UI_RestUntilTime</c> @0x4ff5c, reached from GDS action
    /// code 7.
    /// </summary>
    /// <remarks>
    /// <b>Not the camp rest with a price tag.</b> The rules are <see cref="InnStay"/>; this is the
    /// flow around them: offer, sleep, pay, and offer again while anyone is still hurt.
    ///
    /// <para>The rest itself goes through the same seam camping uses —
    /// <see cref="PartyUpkeepService.RestQuality"/> plus <see cref="GameClock.AdvanceHours"/>, with
    /// the hourly tick doing the healing — so there is one implementation of "an hour of rest" and
    /// the inn differs from a camp only in the figure it sets.</para>
    /// </remarks>
    public sealed class InnService {
        private readonly GameSession _session;
        private readonly GameClock _clock;
        private readonly PartyUpkeepService _upkeep;
        private readonly IDialogManager _dialogs;
        private readonly BakAgain.UI.Rest.InnScreen _screen;
        private readonly ILogger _logger;

        public InnService(GameSession session, GameClock clock, PartyUpkeepService upkeep,
            IDialogManager dialogs, BakAgain.UI.Rest.InnScreen screen) {
            _session = session;
            _clock = clock;
            _upkeep = upkeep;
            _dialogs = dialogs;
            _screen = screen;
            _logger = Microsoft.Extensions.Logging.LoggerFactoryExtensions
                .CreateLogger<InnService>(LogManager.LoggerFactory);
        }

        /// <summary>
        /// Runs the nightmaster's offer, and as many nights as the player buys.
        /// </summary>
        /// <param name="shop">
        /// The location's shop block. It is a <b>type-discriminated union</b> — the same bytes are
        /// markup and haggling for a shop — so reading it as an inn is only correct because the
        /// action code said so.
        /// </param>
        public async UniTask RunAsync(SaveGameContainerShopData shop) {
            if (shop == null) {
                _logger.LogWarning("Inn hotspot with no container shop block.");

                return;
            }

            int price = InnStay.CostInRoyals(shop.InnCostPerNight);
            var firstOffer = true;
            // Taken ONCE, before the first offer (MODALSCR.C:715): the 13-hour Sick cure counts
            // from here across every night bought, not from the start of each night.
            long arrivedTicks = _clock.Ticks;

            // Up BEFORE the offer, and it stays up between nights: the original draws the panel and
            // the purse once and only then shows the nightmaster's dialog (0x50196 precedes
            // 0x501f6), so the player can see what they have while deciding whether to spend it.
            _screen?.Open(shop.InnRestHours);
            try {
                // *** THE OFFER'S OWN CHAIN DECIDES, AS dialog_play_record's ANSWER DOES. ***
                // MODALSCR.C:772 is `if (dialog_play_record(0x13d672, 0) == 0) { sleep }`. Yes walks
                // on to the Var 3 router (global 30003, "party gold >= the price"): affordable plays
                // "@4 settled up the account…" and answers 0, a pauper reaches "The innkeeper
                // frowned…" whose SetReturnValue answers -1, and No answers its branch index, 1.
                // ShowConfirmById stopped at the index, so an accepted night was silent and the
                // refusal had to be hand-played by key (TASK-496).
                while (await OfferAsync(price, shop.InnRestHours, firstOffer) == AcceptedAndAffordable) {
                    await StayTheNightAsync(shop.InnRestHours, arrivedTicks);
                    // "In moments, they were all fast asleep…" ends the chain with SkipWait, so it is
                    // still up: the original's night redraw only repaints the top of the screen.
                    _dialogs.ClearDialog();

                    // AFTER the night, and with no affordability check — see
                    // InnStay.ChargedAfterTheStay.
                    _session.PartyGold -= price;
                    _screen?.Refresh();   // the purse has just changed
                    firstOffer = false;

                    if (!InnStay.OfferAnotherNight(PartyIsWhole())) {
                        return;
                    }
                }
            } finally {
                _screen?.Close();
            }
        }

        /// <summary>
        /// Seeds the three globals the offer's text reads, then plays it through its own chain.
        /// </summary>
        /// <remarks>
        /// The globals are the whole reason the offer can name a price at all: the dialog text is
        /// authored with variable slots, and 30014/30015 are where the price and the waking hour go.
        /// Showing the dialog without writing them first prints the previous speaker's numbers.
        /// </remarks>
        private async UniTask<int> OfferAsync(int price, int wakeHour, bool firstOffer) {
            _session.SetGlobalValue(InnStay.PriceGlobal, price);
            _session.SetGlobalValue(InnStay.HoursGlobal, wakeHour);
            _session.SetGlobalValue(InnStay.RepeatOfferGlobal, firstOffer ? 0 : 1);

            return await _dialogs.ShowById(InnStay.OfferDialog);
        }

        /// <summary>What the offer answers for a night accepted and paid for — the original's <c>== 0</c>.</summary>
        private const int AcceptedAndAffordable = 0;

        /// <summary>
        /// Sleeps until the inn's waking hour.
        /// </summary>
        /// <remarks>
        /// How long it runs is <see cref="InnStay.HoursOfStay"/> — never fewer than two hours, and
        /// that off-by-one is deliberate. Looping on
        /// <see cref="InnStay.StayComplete"/> directly would work too, but only with the advance
        /// before the test; taking the count up front makes the rule checkable.
        ///
        /// <para><see cref="PartyUpkeepService.RestQuality"/> is restored in a finally: leaving it
        /// raised would make walking heal the party at inn rates.</para>
        /// </remarks>
        private async UniTask StayTheNightAsync(int wakeHour, long arrivedTicks) {
            int previousQuality = _upkeep.RestQuality;
            _upkeep.RestQuality = InnStay.RestQuality;
            long startTicks = _clock.Ticks;
            int hours = InnStay.HoursOfStay(InnStay.HourOfDay(_clock.Ticks), wakeHour);

            try {
                for (var hour = 0; hour < hours; hour++) {
                    _clock.AdvanceHours(1);
                    await _upkeep.PlayAnnouncementsAsync(_dialogs);
                    if (_clock.Ticks - arrivedTicks >= SickCureTicks) {
                        CureSickness();
                    }

                    // A frame per hour, so the dial's hour marker walks round the rim rather than
                    // the whole night happening inside one frozen frame.
                    _screen?.Refresh();
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
            } finally {
                _upkeep.RestQuality = previousQuality;
                _session.LastRestTicks = _clock.Ticks;
                _logger.LogInformation("Stayed {Hours} game hours at an inn, waking at {Wake}:00.",
                    (_clock.Ticks - startTicks) / InnStay.TicksPerHour, wakeHour);
            }
        }

        /// <summary>
        /// Thirteen hours since the party walked up to the counter clears Sick outright, checked
        /// after every hour (MODALSCR.C:790-794). Measured in the original: a 10-hour first night,
        /// then the cure in the second night's third hour.
        /// </summary>
        private const long SickCureTicks = 13 * InnStay.TicksPerHour;

        private void CureSickness() => _session.CureSicknessAcrossParty();

        /// <summary>
        /// Every active member at their full pool — what decides whether another night is offered.
        /// </summary>
        /// <remarks>
        /// Health AND stamina, both at maximum. The original reads the combined
        /// <c>HealthStaminaCombo</c> effective value against its maximum per member (0x5035f); a
        /// member short on either is not whole.
        /// </remarks>
        private bool PartyIsWhole() {
            if (_session == null || !_session.IsActive) {
                return true;
            }

            foreach (byte characterId in _session.ActivePartyIndices) {
                // MODALSCR.C:803 compares `stat_actor_get(char, 0x10, 0)` against mode 1 — the
                // EFFECTIVE pool against the stored maxima — so a member an affliction is holding
                // down keeps the night going. Summing the stored pair called them rested.
                // See GameSession.EffectivePool.
                if (_session.EffectivePool(characterId) != _session.EffectivePoolMax(characterId)) {
                    return false;
                }
            }

            return true;
        }
    }
}
