namespace BakAgain.World.Hotspots {
    using System.Collections.Generic;
    using GameData.Resources.GameState;
    using GameData.Resources.World;

    /// <summary>
    /// Everything the hotspot activate pass needs from live game state. One interface rather than a
    /// bag of delegates because it has two real implementations: <c>BakHotspotHost</c> (the running
    /// game) and the test fake.
    /// </summary>
    public interface IHotspotHost {
        /// <summary><c>gstate_event_read</c>.</summary>
        int ReadGlobal(int key);

        /// <summary><c>gstate_event_write</c>.</summary>
        void WriteGlobal(int key, int value);

        /// <summary>Global key of the persistent per-(zone, chunk, hotspot) "already done" flag.</summary>
        int DoneFlagKey(int hotspotIndex);

        /// <summary>Global key of the per-hotspot "a scouting roll was already spent" flag.</summary>
        int ScoutTriedFlagKey(int hotspotIndex);

        /// <summary>
        /// Global key of the per-hotspot "the party SPOTTED this" flag — a different flag from
        /// <see cref="ScoutTriedFlagKey"/>, which only records that a roll happened.
        /// </summary>
        int ScoutedFlagKey(int hotspotIndex);

        /// <summary>
        /// Spend the party's Stealth on slipping past an encounter it is already standing in.
        /// True = avoided, and the encounter does not run.
        /// </summary>
        /// <remarks>
        /// <b>Not the scouting roll.</b> Different stat, different pass, and a modified chance —
        /// see <see cref="GameData.Resources.World.CombatEncounterAvoidance"/>. The host owns it for
        /// the same reason it owns <see cref="RollScouting"/>: one RNG seam.
        /// </remarks>
        bool RollEncounterAvoidance(TileEventTrigger trigger, bool scouted);

        /// <summary>
        /// Whether an encounter is evaluated on THIS step at all — <c>worldmove_step_tick_get</c>'s
        /// gate in <c>hotspotevt_activate_at_player</c> (HOTSPOT.C:474).
        /// </summary>
        /// <remarks>
        /// <b>False means the encounter does not happen, not that it was not avoided.</b> The
        /// original <c>return</c>s out of the whole activation, leaving the hotspot armed for a later
        /// step. It applies only where the avoidance roll would: an encounter with no route into
        /// that roll fires on any step.
        /// </remarks>
        bool EncounterIsDueThisStep(TileEventTrigger trigger, int index);

        /// <summary>Show a DDX dialog. <paramref name="modal"/> mirrors the original's second argument.</summary>
        void PlayDialog(uint dialogId, bool modal);

        /// <summary><c>def_bloc[trigger].DialogId</c>, or 0 when the record has none.</summary>
        uint BlockDialogId(TileEventTrigger trigger);

        /// <summary>The DEF record's "avoidable" bit — this encounter can be spotted in advance.</summary>
        bool IsAmbush(TileEventTrigger trigger);

        /// <summary><c>hotspotevt_enc_fought_read</c> for this trigger's encounter index.</summary>
        bool EncounterFought(TileEventTrigger trigger);

        /// <summary>
        /// Begin the fight this encounter names. False when there is nothing to fight.
        /// </summary>
        /// <remarks>
        /// The host owns it because resolving a trigger to combatants crosses three tables it
        /// already holds — the DEF record for the encounter number, the enemy-party record for the
        /// actor slots, and the actor table for their stats. Returning false is ordinary: a record
        /// naming an empty roster fields nobody, and the caller reports the hotspot unhandled rather
        /// than opening an empty arena.
        ///
        /// <para><paramref name="hotspotIndex"/> is passed because the fight's OUTCOME writes the
        /// hotspot's done flag, and by then the caller that knew the index is long gone. -1 means
        /// the caller has none, and the done write is skipped rather than aimed at slot 0.</para>
        /// </remarks>
        bool StartCombat(TileEventTrigger trigger, int hotspotIndex = -1);

        /// <summary>
        /// Whether there is enough open ground here to lay the arena out —
        /// <c>combatgrid_tiles_over_thresh</c>, the rule in
        /// <see cref="GameData.Resources.Combat.CombatGroundCheck"/>.
        /// </summary>
        /// <remarks>
        /// <b>Answer TRUE when you cannot tell.</b> A host with no world to sweep must not refuse
        /// fights: the check exists to stop an arena being laid across trees and water, and a
        /// default of "no" would stop combat happening at all.
        ///
        /// <para><b>It takes the trigger because the original does more than answer here.</b>
        /// Underground it first turns the party to face the encounter's box, then asks whether the
        /// arena fits from that heading, and puts the heading back if it does not — one operation,
        /// not a query beside a command. Splitting it into two members invites a caller to ask
        /// without turning, or to turn without asking.</para>
        /// </remarks>
        bool EnoughGroundToFight(TileEventTrigger trigger);

        /// <summary>
        /// Spend the party's one scouting roll on this encounter. True = spotted, which stops the
        /// party short of the tile. The host owns the "you spot an ambush" dialog.
        /// </summary>
        bool RollScouting(TileEventTrigger trigger);

        /// <summary>
        /// Roll a <c>Disa</c>/<c>Enab</c> record's chance and, if it comes up, write the flag it
        /// names — cleared for <c>Disa</c>, set for <c>Enab</c>. The host owns the roll for the same
        /// reason it owns <see cref="RollScouting"/>: the RNG is one seam, not one per pass.
        /// </summary>
        /// <returns>Whether the chance came up. A record naming no flag is a no-op either way.</returns>
        bool ApplyChanceFlagWrite(TileEventTrigger trigger);

        /// <summary><c>def_dial[trigger].DialogId</c>, or 0 when the record names none.</summary>
        uint SpeakDialogId(TileEventTrigger trigger);

        /// <summary>
        /// The GDS scene a <c>Town</c> / <c>Bkgr</c> record enters, or 0 when there is none.
        /// </summary>
        /// <remarks>
        /// The two kinds are the same handler over different files — <c>DEF_TOWN.DAT</c> and
        /// <c>DEF_BKGR.DAT</c>, both 21-byte records with the scene number at +2 — so the host reads
        /// whichever family the trigger names.
        /// </remarks>
        int TownSceneNumber(TileEventTrigger trigger);

        /// <summary>The entry dialog a <c>Town</c> / <c>Bkgr</c> record shows, or 0 for none.</summary>
        uint TownDialogId(TileEventTrigger trigger);

        /// <summary>
        /// Put the party where a <c>Town</c> / <c>Bkgr</c> record wants them before its scene opens.
        /// </summary>
        /// <remarks>
        /// Every shipped town record asks for this, so it is the normal way a location is entered.
        /// The destination is relative to the tile the party is standing on — see
        /// <c>TownApproach.DestinationOf</c>.
        /// </remarks>
        void ApproachBeforeLocation(TileEventTrigger trigger);

        /// <summary>
        /// Enter an interactive location, at its first sub-scene.
        /// </summary>
        /// <remarks>
        /// The original runs the scene <i>inline</i> and carries on through the remaining pending
        /// triggers when it returns. Ours cannot block the pass, so the host starts the location and
        /// the pass ends — see the Town arm in <c>HotspotDispatcher</c> for what that costs.
        /// </remarks>
        void EnterLocation(int gdsSceneNumber);

        /// <summary>
        /// Ask the town gate's question, and enter only on a yes.
        /// </summary>
        /// <remarks>
        /// The second place in the hotspot system that needs a dialog's ANSWER rather than just
        /// showing one (<see cref="OfferZoneCrossing"/> is the first). The original's gate is
        /// `dialog_play_record(record.dialogId, 0) == 0` in the ACTIVATE pass — it decides whether
        /// the entry is pending at all — so approach, scene and flags are all downstream of the
        /// answer. See TASK-470.
        /// </remarks>
        void OfferTownEntry(TileEventTrigger trigger, int gdsSceneNumber, int hotspotIndex);

        /// <summary>
        /// Whether a <c>Zone</c> record exists for this trigger and can ever be crossed.
        /// </summary>
        /// <remarks>
        /// A record naming no confirm prompt is inert — see
        /// <see cref="GameData.Resources.World.ZoneTriggerRules.CanCross"/>, which reads backwards
        /// and is the original's behaviour.
        /// </remarks>
        bool ZoneCrossingIsOffered(TileEventTrigger trigger);

        /// <summary>
        /// Put the zone crossing to the player, and perform it if they accept.
        /// </summary>
        /// <remarks>
        /// <b>This one arm owns its own bookkeeping, unlike every other kind here, and that is
        /// deliberate.</b> The original ties the on-fire flag and the done flag to phase 2, which
        /// runs only on a yes — so whether the hotspot counts as having happened depends on an
        /// answer that arrives asynchronously, long after the dispatch pass has returned. Leaving
        /// the bookkeeping in the dispatcher would mean writing it optimistically and marking a
        /// boundary as used by someone who declined to cross it.
        /// </remarks>
        void OfferZoneCrossing(TileEventTrigger trigger, int hotspotIndex);

        /// <summary>
        /// Mark this hotspot as having acted for as long as the party stays on this chunk —
        /// <c>hotspotevt_scout_tried_set</c>. Cleared wholesale when the party moves to another
        /// chunk or zone, which is what re-arms it.
        /// </summary>
        void MarkActedThisChunk(int hotspotIndex);
    }

    /// <summary>
    /// The hotspot activate pass — <c>hotspotevt_activate_at_player</c> (canassa
    /// WORLD/ENC/HOTSPOT.C), the second half of a completed move. See
    /// docs/specs/collision-system.md §3.2-§3.4.
    ///
    /// <para>It decides one thing: does the step stand, or is it undone? Every hotspot overlapping
    /// the party's sub-tile is visited in table order; the pass keeps the step only if <b>none</b> of
    /// them reports an interaction. That is how <c>Bloc</c> works as a data-driven invisible wall and
    /// how an encounter ends up happening where you were standing rather than where you were
    /// going.</para>
    /// </summary>
    public sealed class HotspotActivator {
        private readonly IHotspotHost _host;

        public HotspotActivator(IHotspotHost host) {
            _host = host;
        }

        /// <summary>
        /// Indices of every trigger whose sub-tile rectangle contains the point, in table order.
        /// The original's iteration is stateful (mode 1 restarts, mode 2 continues) but the effect
        /// is exactly this list — all overlapping hotspots fire, not just the first.
        /// <para><paramref name="onlyKind"/> is <c>hotspotevt_dispatch_at_point</c>'s
        /// <c>hotspot->wKind == target_type</c> (HOTSPOT.C:228): it skips the other kinds INSIDE the
        /// table walk, so every index stays the tile table's own — the done and tried flags are keyed
        /// by it. Filtering into a new list first renumbered them.</para>
        /// </summary>
        public static List<int> MatchAt(IReadOnlyList<TileEventTrigger> triggers, int subX, int subY,
            TileEventType? onlyKind = null) {
            var matches = new List<int>();
            if (triggers == null) {
                return matches;
            }
            for (int i = 0; i < triggers.Count; i++) {
                TileEventTrigger t = triggers[i];
                if (onlyKind.HasValue && t.Type != onlyKind.Value) {
                    continue;
                }
                if (t.StartX <= subX && subX <= t.EndX && t.StartY <= subY && subY <= t.EndY) {
                    matches.Add(i);
                }
            }
            return matches;
        }

        /// <summary>
        /// Run the pass at the party's sub-tile. Returns <c>true</c> when nobody interacted — the
        /// step stands. <c>false</c> means the caller must restore the pre-step position (the turn
        /// still counts as an action). <paramref name="pending"/> receives the trigger indices queued
        /// for the dispatch pass.
        /// </summary>
        public bool ActivateAt(IReadOnlyList<TileEventTrigger> triggers, int subX, int subY, List<int> pending,
            TileEventType? onlyKind = null) {
            pending?.Clear();
            bool allNoInteraction = true;
            bool ambushArmed = false;
            int matched = 0;

            foreach (int index in MatchAt(triggers, subX, subY, onlyKind)) {
                matched++;
                TileEventTrigger trigger = triggers[index];
                bool noInteraction;
                bool queue;

                switch (trigger.Type) {
                    case TileEventType.Bkgr:
                    case TileEventType.Town:
                        // Shows the record's prompt; whether the player accepts decides what the
                        // dispatch pass does, but either way the step is undone first.
                        //
                        // *** IT HAS TO BE QUEUED, OR THE DISPATCH PASS NEVER SEES IT. *** This arm
                        // read `queue = false` until 2026-09-09, and `HotspotDispatcher.EnterTown`
                        // is only ever reached through `pending` — so walking into LaMut refused the
                        // step and did nothing else, and every GDS location in the game was
                        // unreachable from the world. Measured at LaMut's own gate (zone 1, chunk
                        // (10,14), sub-tile 14,20): `matched=1 keep=False pending=0`.
                        noInteraction = !Available(trigger, index);
                        queue = !noInteraction;
                        break;

                    case TileEventType.Comb:
                    case TileEventType.Trap:
                        SkillCheck(trigger, index, isAuto: ambushArmed,
                            out bool justArmed, out noInteraction, out queue);
                        if (!ambushArmed) {
                            ambushArmed = justArmed;
                        }
                        break;

                    case TileEventType.Zone:
                        // Queued for the same reason as Town above: `OfferZoneCrossing` lives in the
                        // dispatch pass and is reached only through `pending`.
                        noInteraction = !Available(trigger, index);
                        queue = !noInteraction;
                        break;

                    case TileEventType.Bloc:
                        noInteraction = !FireBloc(trigger, index);
                        queue = false;
                        break;

                    default:
                        // Dial / Soun / Disa / Enab: nothing happens now, everything happens in the
                        // dispatch pass, and the step always stands.
                        noInteraction = true;
                        queue = true;
                        break;
                }

                allNoInteraction &= noInteraction;
                if (queue) {
                    pending?.Add(index);
                }
            }

            Matched = matched;
            AmbushArmed = ambushArmed;
            return allNoInteraction;
        }

        /// <summary>How many triggers the last <see cref="ActivateAt"/> found covering its point.</summary>
        public int Matched { get; private set; }

        /// <summary>Whether the last <see cref="ActivateAt"/> armed an ambush — <c>pOut_ambush_just_armed</c>,
        /// set for an available ambush even when the party spotted it and nothing fired.</summary>
        public bool AmbushArmed { get; private set; }

        /// <summary>The invisible wall (spec §3.4.1). Returns true when it fired.</summary>
        private bool FireBloc(TileEventTrigger trigger, int index) {
            if (!Available(trigger, index)) {
                return false;
            }

            uint dialogId = _host.BlockDialogId(trigger);
            if (dialogId != 0) {
                _host.PlayDialog(dialogId, true);
            }
            ApplyOnFire(trigger);
            if (trigger.FireOnce != 0) {
                _host.WriteGlobal(_host.DoneFlagKey(index), 1);
            }
            return true;
        }

        // hotspotevt_skill_check_trigger / hotspotevt_trap_prefire_scout — one shape, two DEF families.
        private void SkillCheck(TileEventTrigger trigger, int index, bool isAuto,
            out bool justArmed, out bool noInteraction, out bool queue) {
            justArmed = false;
            noInteraction = true;
            queue = false;

            // Note: this gate deliberately omits the scout-tried flag that hotspotevt_available adds.
            if (Done(index) || Forbidden(trigger) || Unmet(trigger) || _host.EncounterFought(trigger)) {
                return;
            }

            if (_host.IsAmbush(trigger)) {
                if (isAuto) {
                    // A later ambush on the same tile: the pass already spent its one roll.
                    return;
                }
                justArmed = true;
                int scoutKey = _host.ScoutTriedFlagKey(index);
                if (_host.ReadGlobal(scoutKey) == 0) {
                    _host.WriteGlobal(scoutKey, 1);
                    if (_host.RollScouting(trigger)) {
                        // hotspotevt_scouted_set — success is recorded separately from "a roll was
                        // spent", because it is what earns the Stealth attempt in the dispatch pass.
                        _host.WriteGlobal(_host.ScoutedFlagKey(index), 1);
                        noInteraction = false; // spotted: stop short, run nothing
                        return;
                    }
                }
            }

            queue = true;
        }

        // The gates and the OnFire write are shared with the dispatch pass, which applies them
        // again before running anything — see HotspotRules.
        private bool Available(TileEventTrigger trigger, int index) =>
            HotspotRules.Available(_host, trigger, index);

        private bool Done(int index) => HotspotRules.Done(_host, index);

        private bool Forbidden(TileEventTrigger trigger) => HotspotRules.Forbidden(_host, trigger);

        private bool Unmet(TileEventTrigger trigger) => HotspotRules.Unmet(_host, trigger);

        private void ApplyOnFire(TileEventTrigger trigger) => HotspotRules.ApplyOnFire(_host, trigger);
    }
}
