namespace BakAgain.UI.Spells {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.CutScenes;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Character;
    using GameData.Resources.Dialog.Actions;
    using GameData.Resources.Spells;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Runs a spell cast from the travel screen — <c>Cast_field_spell</c> (ovr179 @0x6ca30).
    /// </summary>
    /// <remarks>
    /// <b>Nine spells do anything outside a fight, and everything else is silently ignored.</b> The
    /// original scans a nine-entry table and falls off the end with nothing done — no message and
    /// no refund, since the screen has already settled the cost. <see cref="FieldSpells"/> carries
    /// that list and every rule below it; this only sequences them.
    ///
    /// <para>The three groups behave differently enough to be worth naming: three lighting spells
    /// whose duration scales with the power, three timed ones whose duration ignores it, and three
    /// locators that roll once and finish.</para>
    /// </remarks>
    public sealed class FieldSpellCaster {
        private readonly GameSession _session;
        private readonly IGameClock _clock;
        private readonly IDialogManager _dialogs;
        private readonly IResourceCache _resources;
        private readonly ILocatorMapView _locatorMap;
        private readonly ILogger _logger;

        public FieldSpellCaster(GameSession session, IGameClock clock, IDialogManager dialogs,
            IResourceCache resources, ILocatorMapView locatorMap) {
            _session = session;
            _clock = clock;
            _dialogs = dialogs;
            _resources = resources;
            _locatorMap = locatorMap;
            _logger = Core.LogManager.LoggerFactory.CreateLogger<FieldSpellCaster>();
        }

        /// <summary>
        /// Casts a spell at a chosen power.
        /// </summary>
        /// <param name="casterId">The party member who cast it — the one the screen settled on.</param>
        /// <param name="spellNumber">The spell.</param>
        /// <param name="power">The power invested, which is also the cost.</param>
        /// <param name="duration">
        /// The spell record's duration, read before the catalogue goes away. The original passes it
        /// in for exactly that reason: by the time a handler runs, the table it came from has been
        /// disposed.
        /// </param>
        public async UniTask CastAsync(int casterId, int spellNumber, int power, int duration) {
            if (power <= 0 || !FieldSpells.IsFieldSpell(spellNumber)) {
                // An unrecognised spell — or a cancelled cast — matches nothing and does nothing.
                return;
            }

            bool underground = await IsUndergroundAsync();

            // *** THE TWO ZONE-GATED SPELLS RETURN BEFORE THE COST. *** Candle Glow above ground and
            // Stardusk below it are complete no-ops: silent, and free. Charging for them and saying
            // why would be more informative than the original and wrong about the price.
            if (FieldSpells.RefusedInZone(spellNumber, underground)) {
                return;
            }

            // *** AFTER the zone refusal, deliberately. *** A refused cast is documented above as
            // silent and free; cueing before the check would give it a sound the original does not.
            // A spell with no mapped cue stays silent rather than borrowing another's — see
            // SpellCastSound, which keeps "verified silent" apart from "not yet mapped".
            if (SpellCastSound.ForCast(spellNumber) is { } castCue) {
                Audio.MenuSoundService.Instance?.Play(castCue);
            }

            if (FieldSpells.IsLocatorRoll(spellNumber)) {
                await CastLocatorAsync(casterId, spellNumber, power);

                return;
            }

            await ShowDialogAsync(FieldSpells.DialogFor(spellNumber));

            int ticks = FieldSpells.DurationTicks(duration, power,
                FieldSpells.PowerExtendsDuration(spellNumber));
            if (ticks != 0) {
                // Accumulating rather than replacing: recasting a running spell extends it.
                _clock?.ScheduleTimer(TimerType.Spell, FieldSpells.EventIdOf(spellNumber), ticks,
                    accumulate: true);
                if (FieldSpells.DrivesWorldLighting(spellNumber)) {
                    // A second timer against the same lifetime, and under a DIFFERENT numbering:
                    // the light system keys its sources 0 Item, 1 Dragon's Breath, 2 Candle Glow,
                    // 3 Stardusk, which does not agree with the effect slots above.
                    _clock?.ScheduleTimer(TimerType.Light, FieldSpells.LightTimerKeyOf(spellNumber),
                        ticks, accumulate: true);
                }
            }

            // *** OUTSIDE THE TIMER BRANCH. *** A duration that computes to zero leaves no effect
            // and still charges, which is the original's own ordering rather than an oversight.
            ApplyCost(casterId, power);
        }

        /// <summary>
        /// A locator: charge, then roll.
        /// </summary>
        /// <remarks>
        /// <b>The cost comes off before the roll</b>, so a failure costs full price. The chance is
        /// ten percent per point of power, offset for Nacre Cicatrix, and all three reach certainty
        /// at their own maximum.
        ///
        /// <para><b>The map exists now</b> — <c>LocatorMapScreen</c> is registered as
        /// <c>ILocatorMapView</c> and a success opens it, verified in a live cast. This paragraph
        /// used to say it was unbuilt and that a success was only logged; that stopped being true
        /// and the comment did not follow. The <c>_locatorMap == null</c> branch below is now the
        /// case where the prefab field was left unassigned, not the normal one.</para>
        /// </remarks>
        private async UniTask CastLocatorAsync(int casterId, int spellNumber, int power) {
            ApplyCost(casterId, power);

            int roll = UnityEngine.Random.Range(1, 101);
            if (!FieldSpells.LocatorSucceeds(spellNumber, roll, power)) {
                await ShowDialogAsync(LocatorFailureDialog);

                return;
            }

            FieldSpells.LocatorTarget target = FieldSpells.TargetOf(spellNumber);
            if (_locatorMap == null) {
                _logger?.LogWarning(
                    "FieldSpellCaster: {Spell} succeeded ({Target}) but no locator view is bound.",
                    spellNumber, target);

                return;
            }

            // The cast is already paid for and the roll already made, so the screen is the last
            // thing that happens — it returns when the player closes it and the spell is over.
            await _locatorMap.RunAsync(target);
        }

        /// <summary>"A complete waste of time" — what a failed locator says.</summary>
        private const int LocatorFailureDialog = 0xd1;

        private void ApplyCost(int casterId, int power) {
            ActorStat[] stats = _session?.StatsOf(casterId);
            if (stats == null) {
                return;
            }

            var context = new SpellCastContext {
                Chapter = _session.Chapter,
                Inventory = _session.GetActorInventory(PartySlotOf(casterId)),
            };
            SpellCasting.ApplyCost(context, power,
                stats[(int)GameData.ActorAttribute.Health],
                stats[(int)GameData.ActorAttribute.Stamina], out bool collapsed,
                _session.ConditionsOf(casterId));
            if (collapsed) {
                _logger?.LogInformation(
                    "FieldSpellCaster: character {Caster} collapsed paying for the cast.", casterId);
                _session.RecomputePartyDeathState();
            }
        }

        private int PartySlotOf(int characterId) {
            System.Collections.Generic.IReadOnlyList<byte> roster = _session?.ActivePartyIndices;
            for (var i = 0; roster != null && i < roster.Count; i++) {
                if (roster[i] == characterId) {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Whether the party's zone is the enclosed kind the two light spells test.</summary>
        private async UniTask<bool> IsUndergroundAsync() {
            if (_resources == null || _session == null) {
                return false;
            }
            var zone = await _resources.GetOrLoadAsync<GameData.Resources.World.ZoneDefinition>(
                $"Z{_session.CurrentZone:D2}DEF.DAT");

            return zone != null && zone.IsUnderground;
        }

        private async UniTask ShowDialogAsync(int dialogId) {
            if (dialogId >= 0 && _dialogs != null) {
                await _dialogs.ShowById(dialogId);
            }
        }
    }
}
