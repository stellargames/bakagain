namespace BakAgain.World.Hotspots {
    using System.Collections.Generic;
    using GameData.Resources.World;

    /// <summary>What a dispatch pass did.</summary>
    public readonly struct HotspotDispatchResult {
        public HotspotDispatchResult(bool halted, int fired, int unhandled,
            IReadOnlyList<TileEventType> unhandledKinds = null, int refused = 0) {
            Halted = halted;
            Fired = fired;
            Unhandled = unhandled;
            UnhandledKinds = unhandledKinds ?? System.Array.Empty<TileEventType>();
            Refused = refused;
        }

        /// <summary>
        /// Hotspots the rules turned down — currently only encounters refused for want of open
        /// ground.
        /// </summary>
        /// <remarks>
        /// <b>Neither fired nor unhandled, and it needed its own number to stay visible.</b>
        /// <see cref="Unhandled"/> means "no runtime for this kind"; a refusal is the opposite —
        /// the runtime exists and the rule said no. Folding it into either would report a working
        /// rule as a missing feature, or a missing feature as work done.
        /// </remarks>
        public int Refused { get; }

        /// <summary>
        /// Whether the pass stopped before visiting every pending hotspot. See
        /// <see cref="HotspotDispatcher.Dispatch"/> for what stops it.
        /// </summary>
        public bool Halted { get; }

        /// <summary>Hotspots that acted.</summary>
        public int Fired { get; }

        /// <summary>Hotspots that did not act.</summary>
        public int Unhandled { get; }

        /// <summary>
        /// The distinct kinds behind <see cref="Unhandled"/>, in the order first seen.
        /// </summary>
        /// <remarks>
        /// <b>The count alone cannot be acted on.</b> It says something is unwired without saying
        /// which task it belongs to, and answering that meant hand-matching the party's sub-tile
        /// against the chunk's T-file every time. The kind is right here as the pass counts it.
        ///
        /// <para>Distinct rather than one entry per trigger: the number is already reported
        /// separately, so repeating a kind adds nothing. Never null — empty when nothing went
        /// unhandled.</para>
        ///
        /// <para><b>Every kind the game ships now has an arm</b>, so anything reported here is a
        /// handler that RAN and declined — an unavailable record, or one naming no scene — which is
        /// ordinary behaviour rather than a gap. The default arm is reachable only by the three dead
        /// kinds, which no data can produce.</para>
        /// </remarks>
        public IReadOnlyList<TileEventType> UnhandledKinds { get; }
    }

    /// <summary>
    /// The hotspot dispatch pass — <c>hotspotevt_disp_pending_events</c> (canassa
    /// <c>SRC/GAME/ENC/HOTSPOT.C</c>), run by the world loop after the activate pass has queued
    /// whatever the party stepped onto.
    ///
    /// <para>Two passes, not one: the activate pass decides whether the <i>step</i> stands, and this
    /// one decides what actually <i>happens</i>. Keeping them apart is what lets an encounter play
    /// out at the tile you were standing on rather than the one you were entering.</para>
    ///
    /// <para><b>This said "Combat, town/background entry and zone transition have no runtime yet"
    /// until 2026-09-04, and all three now do</b> — <see cref="IHotspotHost.StartCombat"/> enters a
    /// roster and lays out an arena, <see cref="IHotspotHost.EnterLocation"/> runs the GDS scene, and
    /// <see cref="IHotspotHost.OfferZoneCrossing"/> prompts and crosses. The claim contradicted the
    /// remark on <see cref="HotspotDispatchResult.UnhandledKinds"/> in this same file, which had been
    /// updated to "every kind the game ships now has an arm"; a reader had no way to tell which was
    /// current. Corrected rather than deleted because the shape of the note is the useful part: what
    /// is partial should be named here, and named in one place.</para>
    ///
    /// <para>All 22 <c>IHotspotHost</c> members have a real implementation in
    /// <c>HotspotService</c>, and the production construction site (<c>WorldRuntime</c>) passes the
    /// dialog manager, the location player and ten accessors — so a handler reached here is not
    /// routing into a null collaborator. What remains unverified is the rung above: that a handler
    /// does the RIGHT thing, which needs the emulator walk costed under TASK-297.</para>
    /// </summary>
    public sealed class HotspotDispatcher {
        private readonly IHotspotHost _host;

        public HotspotDispatcher(IHotspotHost host) {
            _host = host;
        }

        /// <summary>
        /// Run every queued hotspot, in table order.
        /// </summary>
        /// <remarks>
        /// <b>A zone transition stops the pass.</b> Crossing a border always clears the loop's
        /// running flag, so hotspots queued after it in the table never dispatch at all — they belong
        /// to a zone the party has just left. Combat and traps stop it too, but only when the handler
        /// asks to; a zone does it unconditionally, which is why order matters here and not merely
        /// as a tidiness question.
        ///
        /// <para>Each hotspot re-checks <see cref="HotspotRules.Available"/>. That is not redundant
        /// with the activate pass: an earlier hotspot in the same pass can set the very flag a later
        /// one is gated on, so the answer can change between the two passes and within this one.</para>
        /// </remarks>
        public HotspotDispatchResult Dispatch(IReadOnlyList<TileEventTrigger> triggers,
            IReadOnlyList<int> pending) {
            var halted = false;
            var fired = 0;
            var unhandled = 0;
            var refused = 0;
            // Allocated only when something actually goes unhandled — the common pass has none.
            List<TileEventType> unhandledKinds = null;

            void Unhandled(TileEventTrigger t) {
                unhandled++;
                unhandledKinds ??= new List<TileEventType>(2);
                if (!unhandledKinds.Contains(t.Type)) {
                    unhandledKinds.Add(t.Type);
                }
            }

            if (triggers == null || pending == null) {
                return new HotspotDispatchResult(false, 0, 0);
            }

            foreach (int index in pending) {
                if (halted) {
                    break;
                }
                if (index < 0 || index >= triggers.Count) {
                    continue;
                }

                TileEventTrigger trigger = triggers[index];

                switch (trigger.Type) {
                    case TileEventType.Dial:
                        if (Speak(trigger, index)) {
                            fired++;
                        }

                        break;

                    case TileEventType.Disa:
                    case TileEventType.Enab:
                        if (FireFlagWrite(trigger, index)) {
                            fired++;
                        }

                        break;

                    case TileEventType.Zone:
                        // ALWAYS ends the pass, accepted or declined — the halt is not conditional
                        // on the crossing happening. Hotspots queued behind this one belong to a
                        // zone the party may be about to leave, so table order is load-bearing.
                        halted = true;
                        if (OfferZoneCrossing(trigger, index)) {
                            fired++;
                        } else {
                            Unhandled(trigger);
                        }

                        break;

                    case TileEventType.Bkgr:
                    case TileEventType.Town:
                        if (EnterTown(trigger, index)) {
                            fired++;
                            // Deliberate deviation — see EnterTown.
                            halted = true;
                        } else {
                            Unhandled(trigger);
                        }

                        break;

                    // *** THE SAME ARM. *** A trap and an encounter differ only inside the host:
                    // the trap places the party on its own landing before the ground check and puts
                    // them back if it fails (HotspotService.EnoughGroundToFight). Everything the
                    // dispatcher decides — the stealth gate first, a refusal that is neither fired
                    // nor unhandled, the fight halting the pass — is identical, and duplicating the
                    // case to say so would let the two drift.
                    case TileEventType.Trap:
                    case TileEventType.Comb:
                        // *** MID-CELL, AN AVOIDABLE ENCOUNTER IS NOT EVALUATED AT ALL. ***
                        // hotspotevt_activate_at_player returns outright when the movement-cell tick
                        // is 0, so the encounter stays armed and is offered again from a later step —
                        // which is the same shape as the ground refusal below, not a slip-past.
                        if (!_host.EncounterIsDueThisStep(trigger, index)) {
                            refused++;
                            break;
                        }
                        // The GATE in front of the fight is ported; the fight is not. A party that
                        // slips past is fully handled here — no arena is involved in walking away.
                        if (SlipPast(trigger, index)) {
                            fired++;
                        } else if (!_host.EnoughGroundToFight(trigger)) {
                            // *** THE ORIGINAL'S ORDER: stealth gate, THEN this, then the fight. ***
                            // hotspotevt_type1_encounter_run returns outright here — no arena, no
                            // dialog, nothing marked — so the encounter is still armed and will be
                            // offered again from somewhere there is room.
                            refused++;
                        } else if (_host.StartCombat(trigger, index)) {
                            // *** The fight stops the pass. *** Hotspots queued behind this one
                            // belong to a step the party is no longer taking; running them while an
                            // encounter is up would fire them from inside the fight.
                            //
                            // The OnFire write that used to be here belongs to the fight's OUTCOME,
                            // not its start — the original writes it only in the resolved branch, so
                            // losing the fight must not set the flag winning it does. The index goes
                            // with the fight for the same reason.
                            fired++;
                            halted = true;
                        } else {
                            Unhandled(trigger);
                        }

                        break;

                    default:
                        // Nothing reaches here from the shipped data any more. The only kinds left
                        // in the DEF family table are Soun, Comm and Heal, and all three are dead —
                        // no file ships for them and no trigger of those kinds appears in any of the
                        // 266 tile files. See ShippedTriggerCensusTests.
                        Unhandled(trigger);

                        break;
                }
            }

            return new HotspotDispatchResult(halted, fired, unhandled, unhandledKinds,
                refused);
        }

        /// <summary>
        /// The <c>Comb</c> hotspot's opening move — the Stealth gate in
        /// <c>hotspotevt_type1_encounter_run</c>.
        /// </summary>
        /// <remarks>
        /// <b>An avoidable encounter only offers the roll if the party SPOTTED it.</b> The scouting
        /// success recorded in the activate pass is what earns the chance to slip away; an
        /// unavoidable one offers it only under Dragon's Breath. Everything else walks into the
        /// fight, which is why this returning false is the ordinary case rather than a failure.
        ///
        /// <para><b>Both halves exist now.</b> This said "only the walking-away half exists — there
        /// is no arena to hand it to"; since then <see cref="IHotspotHost.StartCombat"/> got a real
        /// one, so the gate saying <i>fight</i> opens it rather than reporting the trigger
        /// unhandled. Slipping past is still the branch this method owns, and it returning false
        /// remains the ordinary case.</para>
        /// </remarks>
        private bool SlipPast(TileEventTrigger trigger, int index) {
            // *** DELIBERATELY NOT HotspotRules.Available, and this one matters. *** Available
            // includes the scout-tried flag, and the activate pass SETS that flag on this very
            // hotspot before it rolls — so re-checking it here would refuse every ambush the pass
            // just queued, and the encounter could never dispatch at all. The other three gates
            // still apply, because an earlier hotspot in the same pass can change them.
            if (HotspotRules.Done(_host, index)
                || HotspotRules.Forbidden(_host, trigger)
                || HotspotRules.Unmet(_host, trigger)) {
                return false;
            }

            bool scouted = _host.ReadGlobal(_host.ScoutedFlagKey(index)) != 0;
            if (!_host.RollEncounterAvoidance(trigger, scouted)) {
                return false;
            }

            // Slipping past consumes the hotspot the same way any fired one is consumed, or the
            // party would be re-offered the roll on every step across the tile.
            HotspotRules.ApplyOnFire(_host, trigger);
            return true;
        }

        /// <summary>
        /// The <c>Dial</c> hotspot — <c>hotspotevt_monst_load_speak</c>: play the record's dialog.
        /// </summary>
        /// <remarks>
        /// <b>The name is a third lie in this family: it loads no monster.</b> The whole handler is
        /// "if the record names a dialog, play it", plus the bookkeeping below.
        ///
        /// <para>Its cleanup differs from <c>Disa</c>/<c>Enab</c> in a way worth keeping: the done
        /// flag <i>and</i> the per-chunk debounce are both skipped when the record is
        /// <see cref="TileEventTrigger.Repeatable"/>. That is the only thing that keeps a repeatable
        /// dialog repeatable — a normal one silences itself for as long as the party stays on the
        /// chunk.</para>
        /// </remarks>
        /// <returns>Whether it acted, which is also the loop's screen-changed bit — a dialog covers
        /// the world.</returns>
        private bool Speak(TileEventTrigger trigger, int index) {
            if (!HotspotRules.Available(_host, trigger, index)) {
                return false;
            }

            uint dialogId = _host.SpeakDialogId(trigger);
            if (dialogId != 0) {
                _host.PlayDialog(dialogId, true);
            }
            HotspotRules.ApplyOnFire(_host, trigger);

            if (trigger.Repeatable == 0) {
                if (trigger.FireOnce != 0) {
                    _host.WriteGlobal(_host.DoneFlagKey(index), 1);
                }
                _host.MarkActedThisChunk(index);
            }

            return true;
        }

        /// <summary>
        /// The <c>Town</c> / <c>Bkgr</c> pair: enter the location the record names.
        /// </summary>
        /// <remarks>
        /// <b>One handler, two files.</b> The kinds differ only in which DEF family supplies the
        /// record — <c>DEF_TOWN.DAT</c> for a town gate, <c>DEF_BKGR.DAT</c> for the rest — and both
        /// carry the GDS scene number at the same offset. The scene is always entered at its first
        /// sub-scene; the letter is not in the record.
        ///
        /// <para><b>Deviation, deliberate:</b> the original runs the scene <i>inline</i> and then
        /// carries on through the remaining pending triggers. We cannot block the pass, so entering
        /// a location ends it. The alternative — carrying on — would fire the rest of the pass
        /// <i>before</i> the location instead of after it, which is a worse order than not firing
        /// them at all. Anything dropped this way is re-evaluated on the next step, because the
        /// party is still standing on the tile.</para>
        /// </remarks>
        /// <returns>Whether it acted, which is also the screen-changed bit — a location covers the
        /// world completely.</returns>
        private bool EnterTown(TileEventTrigger trigger, int index) {
            if (!HotspotRules.Available(_host, trigger, index)) {
                return false;
            }

            int scene = _host.TownSceneNumber(trigger);
            if (scene <= 0) {
                // No record, or one naming no scene: nothing to enter, and not a fired trigger.
                return false;
            }

            // *** A TOWN GATE ASKS FIRST, AND A NO LEAVES YOU ON THE ROAD. ***
            // `hotspotevt_show_record_message` (HOTSPOT.C:618) writes the entry's PENDING flag as
            // `dialog_play_record(record.dialogId, 0) == 0`, and the dispatch pass only runs entries
            // whose flag is set — so the town is entered on the FIRST branch (Yes) and on nothing
            // else. Measured on both games 2026-09-13 at LaMut's gate: the original asks "Do you
            // think we should go in for supplies?" and a No leaves the party standing on the road,
            // nudged back out of the trigger. The port walked straight in, because the answer was
            // discarded. See TASK-470.
            //
            // Everything after the answer — approach, scene, on-fire and done flags — is phase 2 and
            // none of it happens on a no, which is why it all moves into the asking arm.
            // *** BOTH KINDS ASK. *** `hotspotevt_dialog_popup_run` (kind 0, Bkgr) and
            // `hotspotevt_show_record_message` (kind 6, Town) are the same six lines with a
            // different DEF family, so the gate is the same: the two of the four shipped DEF_BKGR
            // records whose dialog offers no choice answer 0 — `nResult` opens at 0 (DIALOG.C:837)
            // — and 0 is the yes.
            _host.OfferTownEntry(trigger, scene, index);

            return true;
        }

        /// <summary>
        /// The <c>Zone</c> arm — <c>zoneTrigger_phase1</c> @0x74a82 and its phase 2.
        /// </summary>
        /// <remarks>
        /// <b>A boundary asks before it moves you.</b> Stepping onto one is an offer, not a
        /// trapdoor: the record names a confirm prompt, and the crossing happens only if the player
        /// accepts. A record naming no prompt never crosses at all — which reads backwards, and is
        /// what the original does.
        ///
        /// <para>What this decides is only whether to make the offer. The answer, the move and the
        /// bookkeeping that depends on the answer all belong to the host — see
        /// <see cref="IHotspotHost.OfferZoneCrossing"/> for why they cannot live here.</para>
        /// </remarks>
        /// <returns>Whether the offer was made.</returns>
        private bool OfferZoneCrossing(TileEventTrigger trigger, int index) {
            if (!HotspotRules.Available(_host, trigger, index)) {
                return false;
            }

            if (!_host.ZoneCrossingIsOffered(trigger)) {
                return false;
            }

            _host.OfferZoneCrossing(trigger, index);

            return true;
        }

        /// <summary>
        /// The <c>Disa</c> / <c>Enab</c> pair — <c>hotspotevt_chance_trigger</c> and its twin.
        /// </summary>
        /// <remarks>
        /// One shape, one difference: <b>Enab sets the record's flag and Disa clears it</b>. canassa
        /// calls the clearing one <c>hotspotevt_combat_play_sfx_voice</c>, which is wrong twice over
        /// — it runs no combat and plays no sound. Going by that name would have put a sound effect
        /// where a flag write belongs.
        ///
        /// <para>The record's own post-event and its done flag are written <b>outside</b> the chance
        /// branch: a roll that fails still marks the hotspot as having happened. (The sound kind is
        /// the odd one out — there the post-event sits inside the branch — which is worth knowing
        /// before anyone factors the three together.)</para>
        /// </remarks>
        private bool FireFlagWrite(TileEventTrigger trigger, int index) {
            if (!HotspotRules.Available(_host, trigger, index)) {
                return false;
            }

            bool wrote = _host.ApplyChanceFlagWrite(trigger);

            HotspotRules.ApplyOnFire(_host, trigger);
            if (trigger.FireOnce != 0) {
                _host.WriteGlobal(_host.DoneFlagKey(index), 1);
            }

            return wrote;
        }
    }
}
