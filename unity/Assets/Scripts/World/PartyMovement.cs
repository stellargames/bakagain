namespace BakAgain.World {
    using System;
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.World.Collision;
    using BakAgain.World.Converters;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Config;
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// Applies faithful single-zone movement to <see cref="GameSession"/> (the source of
    /// truth for the party pose) and syncs the world camera. Step distance / turn angle come
    /// from MOVEMENT.DAT keyed by the player's Preferences StepSize/TurnSize (the config — the
    /// original resolves them from pConfiguration, not the save).
    ///
    /// <para>This is <c>worldmove_party_attempt_move</c> (IDA <c>MoveParty_impl</c> @0x71902): probe
    /// the destination point for walkable ground, take the step, follow the terrain height, then run
    /// the hotspot pass — which may undo the step. A blocked <b>forward</b> step pivots the party
    /// toward the nearest opening instead of moving; a blocked backward step just bumps.
    /// See docs/specs/collision-system.md §2.</para>
    ///
    /// <para><b>Deliberate deviation</b> from the original, in the pivot only — see spec §10.1. The DOS
    /// sweep quantised the correction to the player's turn-stride and searched out to ±90°; this one
    /// finds the smallest correction that clears, to 1°, and refuses anything past 45°. Everything
    /// else on this path stays faithful.</para>
    ///
    /// <para>When no <see cref="ProximityWorld"/> is supplied (debug scenes, pure movement tests) the
    /// party moves unconditionally, as it did before collision existed.</para>
    /// </summary>
    public sealed class PartyMovement {
        /// <summary>The dull "you can't go that way" thud — <c>audio_play(0x31)</c>.</summary>
        public const int BumpSoundId = 0x31;

        private readonly GameSession _session;
        private readonly MovementData _movement;
        private readonly IPreferencesService _preferences;
        private readonly Camera _camera;
        // Zone-def camera setup (resource_loadZoneDataFiles @ 0x7313b): eye altitude in BaK units
        // and pitch in BaK angle space. The altitude is the base the sampled terrain height adds to.
        private readonly int _cameraHeightZ;
        private readonly ushort _cameraPitch;
        private readonly ProximityWorld _collision;
        private readonly Func<int, int, bool> _hotspotPass;
        private readonly Action<int> _playSfx;
        private readonly bool _underground;
        private readonly IGameClock _clock;
        private readonly Action<int> _showDialog;

        /// <summary>
        /// Raised after the party actually moved — not on a blocked step that only pivots.
        /// </summary>
        /// <remarks>
        /// Deliberately carries no distance. The step length differs between an ordinary move
        /// (a Preferences-driven step, quartered underground) and a travel step, and a listener that
        /// re-derives it from the wrong one drifts silently. Anything that needs the distance should
        /// diff the session position across the call, which is exact on every path.
        /// </remarks>
        public Action Stepped;

        // The terrain kind the last completed step CROSSED. Held rather than acted on, because the
        // original falls on the world-loop iteration AFTER the crossing is recorded — which is what
        // gives the step onto the pit a frame to render before the floor disappears.
        private int _pendingCrossingKind = -1;

        /// <summary>Current eye altitude: zone DefaultCameraZ plus the ground height under the party.</summary>
        private int _eyeZ;

        public PartyMovement(GameSession session, MovementData movement, IPreferencesService preferences,
            Camera camera, int cameraHeightZ, ushort cameraPitch,
            ProximityWorld collision = null, Func<int, int, bool> hotspotPass = null,
            Action<int> playSfx = null, bool underground = false, IGameClock clock = null,
            Action<int> showDialog = null) {
            _session = session;
            _movement = movement;
            _preferences = preferences;
            _camera = camera;
            _cameraHeightZ = cameraHeightZ;
            _cameraPitch = cameraPitch;
            _collision = collision;
            _hotspotPass = hotspotPass;
            _playSfx = playSfx;
            _underground = underground;
            _clock = clock;
            // Optional: the only thing the fall needs that movement does not already own.
            _showDialog = showDialog;
            _eyeZ = cameraHeightZ;
        }

        public void MoveForward() => Step(forward: true);
        public void MoveBackward() => Step(forward: false);
        public void TurnLeft() => Turn(turnViewLeft: true);
        public void TurnRight() => Turn(turnViewLeft: false);

        /// <summary>Whether road-following ("travel") mode is engaged.</summary>
        /// <remarks>
        /// <b>It lives on the session because it lives in the save</b> (body offset 50,
        /// <c>bIsAutoTravelling</c>). Four of the shipped chapter-1 saves carry it set, and with it
        /// set the original refuses every step that would leave the road — so a port that kept this
        /// as its own field would start every load with the party free to walk anywhere and disagree
        /// with the original about the map. Measured at (670400, 1066800) on 2026-09-12: all eight
        /// headings refused with the flag on, all four cardinals walking the moment it is off.
        /// TASK-422.
        /// </remarks>
        public bool IsTravelling {
            get => _session.IsAutoTravelling;
            private set => _session.IsAutoTravelling = value;
        }

        /// <summary>
        /// Whether the travel control should be offered right now: not already travelling, and
        /// standing on road or bridge (spec §3.5).
        /// </summary>
        public bool CanEngageTravel() {
            if (_collision == null || IsTravelling) {
                return false;
            }
            // *** THE GATE IS THE SNAP, AND IT SAMPLES CELL CENTRES. ***
            // This used to scan the party's RAW position for a road kind, which is a test the
            // original does not have. `worldmove_step_once_along_axis` (WORLDMOV.C:745-753) engages
            // by *attempting the step* -- `if (worldmove_try_step_along_axis() == 0) return 0;` --
            // and that goes straight to `worldmove_prox_find_near_pos` (:634), which writes the
            // party's own CELL CENTRE into the position and queries there before trying any
            // neighbour. It never asks about the raw position at all.
            //
            // Splitting the two let them disagree: standing off-centre inside a road cell, the raw
            // sample came back non-road and the button refused while the snap -- run a moment later
            // by TryEngageTravel -- would have found the road at distance 0. That is TASK-554's
            // "nearest road: ... = 0 cell(s)" with engage=False, measured from four directions.
            //
            // Asking TryEngage is therefore not a shortcut, it is the original's own predicate.
            _collision.BuildCandidates(_session.PositionX, _session.PositionY, DetailLevel);

            return RoadTravel.TryEngage(_session.PositionX, _session.PositionY, IsRoadAt, out _, out _);
        }

        /// <summary>
        /// Whether the follow-road BUTTON is live: the ground under the party's own position is
        /// road or bridge.
        /// </summary>
        /// <remarks>
        /// <c>worldloop_set_flag_8b_preds</c> (WORLDLP.C:460-473) gates the menu entry on
        /// <c>g_nWorldCrossingKind</c>, which every move, zone load and teleport landing writes for
        /// the party's own position (WORLDMOV.C:104-115, 183, 349, 423). <see cref="CanEngageTravel"/>
        /// is the ACTION's predicate — the snap — and is more lenient off-centre in a road cell,
        /// where the original's button cannot be clicked at all (MENUPAGE.C:447).
        /// </remarks>
        public bool StandsOnRoad() {
            _collision.BuildCandidates(_session.PositionX, _session.PositionY, DetailLevel);
            return IsRoadAt(_session.PositionX, _session.PositionY);
        }

        /// <summary>
        /// Engage travel: snap to the centre of a nearby road cell. Fails, and leaves the party where
        /// it was, when nothing road-like is adjacent.
        /// </summary>
        public bool TryEngageTravel() {
            if (!CanEngageTravel()) {
                return false;
            }
            if (!RoadTravel.TryEngage(_session.PositionX, _session.PositionY, IsRoadAt,
                    out int snapX, out int snapY)) {
                return false;
            }

            _session.PositionX = snapX;
            _session.PositionY = snapY;
            if (_collision.TryScan(snapX, snapY, out _, out int groundZ)) {
                SetEyeHeight(groundZ);
            }
            IsTravelling = true;
            SyncCamera();
            return true;
        }

        /// <summary>Leave travel mode. Pressing the travel control again, or any failed step.</summary>
        public void DisengageTravel() => IsTravelling = false;

        /// <summary>Road/bridge test at a world position, for <see cref="RoadTravel"/>.</summary>
        private bool IsRoadAt(int x, int y) =>
            _collision.TryScan(x, y, out int kind, out _) && RoadTravel.IsRoadKind(kind);

        /// <summary>
        /// One travelling step: a whole cell along the road, then the bend if there is one.
        /// </summary>
        /// <remarks>
        /// Any refusal disengages, which is the original's rule — travel is a mode you fall out of
        /// rather than one that stalls in place. A fork disengages too: the road genuinely continues,
        /// but the party will not choose for you.
        /// <para>The original's step-tick (<c>1600 / stepSize</c>) is a <i>pacing</i> device: it
        /// counts frames while the party slides between cell centres. Our step is discrete — one
        /// input, one cell — so there is nothing to pace and no tick is kept. Distance per step is
        /// unaffected; only the animation between them differs.</para>
        /// </remarks>
        private void TravelStep(bool forward) {
            ushort heading = unchecked((ushort)_session.Rotation);
            ushort probeHeading = forward ? heading : unchecked((ushort)(heading + 0x8000));

            _collision.BuildCandidates(_session.PositionX, _session.PositionY, DetailLevel);

            (int probeX, int probeY) = RoadTravel.ProbeOrigin(
                _session.PositionX, _session.PositionY, probeHeading);
            // *** THE HEADING MUST BE AN EXACT COMPASS VALUE, NOT MERELY ON A LATTICE LINE. ***
            // `worldmove_crossing_check_8dir` switches on the heading itself — `case R3D_DEG(0)`,
            // `R3D_DEG(45)`, ... — and its `default:` is `return 0`, the same refusal a
            // wrong-lattice-line position gets. The original never needed the test to bite because
            // it could not hold any other heading; the remake's by-the-degree pivot can, and without
            // this the party travelled along the TRUNCATED compass direction instead — measured
            // 2026-09-13 at (671200, 823200): heading 64000 stepped a clean +1600/+1600 north-west
            // while facing four degrees off it. Refusing here hands the press to the sweep below,
            // which puts the party back on the lattice.
            if (!RoadTravel.IsCompassHeading(probeHeading)
                || !RoadTravel.IsOnLatticeLine(probeHeading, _session.PositionX, _session.PositionY)
                || !RoadTravel.ProbeAdjacentCell(probeX, probeY, probeHeading, IsRoadAt)) {
                // *** A REFUSED TRAVEL STEP TURNS THE PARTY; IT DOES NOT END TRAVEL. ***
                // `worldmove_party_attempt_move`'s L_fail is shared by the walking and the
                // road-following branches, and the `mode == 1` sweep that follows is what puts the
                // party back on the road. Nothing there touches `nWorldStepPending`: the only
                // writers that clear it are the button (`worldloop_party_move_done_clr`) and the
                // step-speed change at WORLDMOV.C:72.
                //
                // Measured 2026-09-12 at (671200, 823200), a save parked on a DIAGONAL road: the
                // original refuses the press at heading 0, turns to the diagonal, and the next
                // press steps +400/+400. At (670400, 1066800), off the road, it refuses all eight
                // headings and stays engaged through every one until the button is clicked.
                if (forward && TrySweepRoadHeading(heading, out ushort swept)) {
                    BeginSwing();
                    _session.Rotation = unchecked((short)swept);
                    SyncCamera();

                    return;
                }

                _playSfx?.Invoke(BumpSoundId);
                return;
            }

            int savedX = _session.PositionX;
            int savedY = _session.PositionY;
            int savedEye = _eyeZ;

            // *** THE CELL IS THE PROBE DISTANCE, NOT THE STEP. ***
            // `worldmove_crossing_check_8dir` probes a whole cell ahead
            // (`worldmove_probe_adjacent_cell`, offset 0x640) and then moves by
            // `worldmove_crossing_apply_offset(pos, heading, step)` — the ordinary world step
            // speed, quartered underground exactly as walking is. Stepping a whole cell made
            // travel four times too fast at the default step size: measured at (671200, 823200),
            // the original moves +400/+400 per press along that diagonal road and the port moved
            // +1600/+1600.
            //
            // It also keeps `IsOnCellCentre` below meaning what the original's
            // `% 0x640 == 0x320` means: a landing on a cell centre, which now happens every fourth
            // step rather than on every one.
            int travelStep = _movement.StepDistanceFor(_preferences.Current.StepSize);
            if (_underground) {
                travelStep >>= 2;
            }
            (int dx, int dy) = RoadTravel.AxisOffset(probeHeading, travelStep);
            _session.PositionX = savedX + dx;
            _session.PositionY = savedY + dy;
            if (_collision.TryScan(_session.PositionX, _session.PositionY, out _, out int groundZ)) {
                SetEyeHeight(groundZ);
            }

            if (_hotspotPass != null && !_hotspotPass(_session.PositionX, _session.PositionY)) {
                // Same rule as a walking step: whatever fired happens where the party stood, so the
                // step is rolled back. Travel is NOT ended — WORLDMOV.C:135 restores the position
                // and returns 1 without touching the flag.
                _session.PositionX = savedX;
                _session.PositionY = savedY;
                _eyeZ = savedEye;
                _session.PositionZ = savedEye;
                AdvanceClockForStep(stepStands: false);
                SyncCamera();
                return;
            }

            if (RoadTravel.IsOnCellCentre(_session.PositionX, _session.PositionY)) {
                RoadSweep sweep = RoadTravel.FindContinuation(
                    _session.PositionX, _session.PositionY, heading, !forward, IsRoadAt, out ushort target);
                if (sweep == RoadSweep.Turn) {
                    BeginSwing();
                    _session.Rotation = unchecked((short)target);
                }
                // No else: a road that simply ends leaves the party engaged and stationary, and the
                // next press runs the refusal arm above — which sweeps, and failing that bumps.
                // Ending travel here would be the port deciding for the player.
            }

            AdvanceClockForStep(stepStands: true);
            SyncCamera();
        }

        /// <summary>
        /// Push the current GameSession pose into the camera. Called once for
        /// initial framing (via the world build) and after every move/turn. With collision data
        /// loaded it also samples the ground under the party first — <c>worldmove_camera_crossing_apply</c>.
        /// </summary>
        public void SyncToCamera() {
            if (_collision != null) {
                _collision.BuildCandidates(_session.PositionX, _session.PositionY, DetailLevel);
                if (_collision.TryScan(_session.PositionX, _session.PositionY, out _, out int groundZ)) {
                    SetEyeHeight(groundZ);
                }
            }
            SyncCamera();
        }

        /// <summary>
        /// A completed step costs game time. The world loop advances the clock by
        /// <c>g_lWorldTimePerStep</c> right after the hotspot pass runs (canassa
        /// <c>SRC/GAME/WORLD/WORLDLP.C</c> line 193), and by a quarter of it underground
        /// (<c>g_game_mode == 2</c>) — the same quartering the step distance gets. The value is
        /// MOVEMENT.DAT's third array indexed by the <b>step-size</b> preference, exposed by the
        /// extractor as real seconds; the clock counts 2-second ticks, hence the halving.
        ///
        /// <para>Called only where the step actually happened — including the case a hotspot
        /// reverted, because the original advances after the pass and never rolls time back. A
        /// blocked step that only pivots does not call this; that reading of
        /// <c>worldmove_party_attempt_move</c>'s return is unverified against the pivot path.</para>
        /// </summary>
        /// <param name="stepStands">
        /// Whether the party KEPT the ground it moved onto. A hotspot that fires rolls the step back,
        /// and the two halves of this method treat that differently — which is why it is a required
        /// argument rather than a defaulted one: a new call site has to decide.
        /// </param>
        private void AdvanceClockForStep(bool stepStands) {
            // *** Raised ABOVE the clock guard. *** This method is the one place every successful
            // move funnels through, which makes it the "a step happened" signal as much as a clock
            // advance — but it returns early without a clock, and a listener that only fires when
            // the game happens to have one is a bug waiting for the test that omits it.
            Stepped?.Invoke();

            // *** THE MOVEMENT-CELL TICK ADVANCES ONLY ON A STEP THAT STANDS, AND THE CLOCK ALWAYS
            // DOES. *** WORLDMOV.C:126-137 puts worldmove_step_tick_advance() inside the branch
            // where hotspotevt_activate_at_player kept the move, and the revert branch below it
            // returns without touching the tick — while the clock is advanced by the world loop
            // (WORLDLP.C:193) after the pass either way. A single "a step happened" signal driving
            // both would hand the party a cell boundary for a step they did not take.
            if (stepStands && _session != null) {
                // The UNQUARTERED distance on purpose: g_nWorldStepSpeed is what worldmove_dat_load
                // resolved from the preference, and the underground quartering is applied at the
                // step site, never to it. So underground the boundary still falls every four presses
                // — 400 units rather than 1600. That looks like an oversight and is the original's.
                (int count, int tick) = GameData.Resources.World.WorldStepTick.Advance(
                    _session.SubTileStepCount,
                    _movement.StepDistanceFor(_preferences.Current.StepSize));
                _session.SubTileStepCount = count;
                _session.TileBoundaryCrossed = tick;
            }

            if (_clock == null) {
                return;
            }
            long ticks = _movement.SecondsPerStepFor(_preferences.Current.StepSize) / 2;
            if (_underground) {
                ticks /= 4;
            }
            _clock.Advance(ticks);
        }

        /// <summary>
        /// Runs a hotspot pass a dialog asked for, if one is outstanding and nothing moved.
        /// </summary>
        /// <remarks>
        /// <c>hotspotevt_activate_at_player</c> under the world loop's
        /// <c>if (moved == 0 &amp;&amp; flag != 0)</c> guard — a dialog that has just set a flag or
        /// filled a container wants the tile under the party re-evaluated without waiting for a step.
        /// One-shot: the request is cleared before the pass runs, so a pass that raises another
        /// dialog cannot leave the request standing and loop.
        /// </remarks>
        public void RunRequestedHotspotPass() {
            // Held, not dropped, while a conversation is still playing: the travel screen owns input
            // for a frame between two of its pages, and that is not the world loop resuming.
            if (!_session.HotspotPassRequested || _session.DialogsPlaying > 0) {
                return;
            }

            _session.HotspotPassRequested = false;
            _hotspotPass?.Invoke(_session.PositionX, _session.PositionY);
        }

        private void Step(bool forward) {
            if (CameraIsAnimating) {
                return;
            }

            if (IsTravelling && _collision != null) {
                TravelStep(forward);
                return;
            }

            // Faithful to the original: step distance is resolved from the config (Preferences), not
            // the save game — Load_movement.dat @ 0x71790 indexes MOVEMENT.DAT by config.stepSize, and
            // dialog_Preferences re-resolves it live. Read Current each move so a Preferences change
            // takes effect on the very next step.
            int step = _movement.StepDistanceFor(_preferences.Current.StepSize);
            if (_underground) {
                step >>= 2; // g_game_mode == 2: quarter step underground
            }

            ushort heading = unchecked((ushort)_session.Rotation);
            // Mode 4 (backward) probes and moves along the reversed heading; there is no separate
            // negative-step path in the original.
            ushort probeHeading = forward ? heading : unchecked((ushort)(heading + 0x8000));

            if (_collision == null) {
                var (fdx, fdy) = MovementMath.StepDelta(probeHeading, step);
                _session.PositionX += fdx;
                _session.PositionY += fdy;
                AdvanceClockForStep(stepStands: true);
                SyncCamera();
                return;
            }

            // The candidate list is built from where the party stands, then reused by every probe of
            // this move — including the whole pivot sweep (the original rebuilds it once per frame).
            _collision.BuildCandidates(_session.PositionX, _session.PositionY, DetailLevel);

            int savedX = _session.PositionX;
            int savedY = _session.PositionY;
            int savedEye = _eyeZ;

            if (_collision.ProbeWalkable(savedX, savedY, probeHeading, step,
                    out int crossingKind, out int groundZ)) {
                var (dx, dy) = MovementMath.StepDelta(probeHeading, step);
                _session.PositionX = savedX + dx;
                _session.PositionY = savedY + dy;
                SetEyeHeight(groundZ);

                if (_hotspotPass == null || _hotspotPass(_session.PositionX, _session.PositionY)) {
                    // Recorded only on a step that STANDS. A rolled-back step (below) never
                    // happened, so the crossing it probed must not drop the party from a tile they
                    // were pushed off.
                    _pendingCrossingKind = crossingKind;
                    AdvanceClockForStep(stepStands: true);
                    SyncCamera();
                    return;
                }

                // Something scripted fired: the encounter happens where the party was standing, so
                // the completed step is rolled back. The move still counted as an action.
                _session.PositionX = savedX;
                _session.PositionY = savedY;
                _eyeZ = savedEye;
                _session.PositionZ = savedEye;
                AdvanceClockForStep(stepStands: false);
                SyncCamera();
                return;
            }

            if (forward && TrySweepAlternativeHeading(heading, step, out ushort target)) {
                // Rotational wall-sliding: the party turns to follow the obstruction and never
                // translates along it — the blocked step still costs you the step. The correction is
                // the minimum that clears, so the resulting heading is generally OFF the turn-stride
                // lattice (see the note on TrySweepAlternativeHeading).
                BeginSwing();
                _session.Rotation = unchecked((short)target);
                SyncCamera();
                return;
            }

            _playSfx?.Invoke(BumpSoundId);
        }

        /// <summary>
        /// One world-loop iteration: fires a pit fall recorded by the previous step.
        /// </summary>
        /// <returns>True on the frame a fall STARTS — not while one is running.</returns>
        /// <remarks>
        /// <b>A frame late, on purpose.</b> The original reads the crossing kind recorded on the
        /// PREVIOUS iteration, so the step onto the pit renders before the floor gives way. Doing it
        /// inside the step would teleport the party out of a tile they were never seen to enter.
        ///
        /// <para>Called every frame from the in-game screen, beside the ambient tick — the same
        /// place the original's world loop does it. Cheap: one integer compare when nothing is
        /// pending.</para>
        /// </remarks>
        public bool TickPendingDescent() {
            TickTurnSwing();
            if (IsFalling) {
                // A fall already under way owns the frame. The crossing kind is deliberately NOT
                // consumed here — nothing can step while falling, so there is nothing to consume.
                TickDescent();
                return false;
            }

            int kind = _pendingCrossingKind;
            _pendingCrossingKind = -1;
            if (!PitDescent.Triggers(kind, ZoneKind)) {
                return false;
            }
            Fall();
            return true;
        }

        /// <summary>
        /// Put the party at a world position outright, and bring the camera with them.
        /// </summary>
        /// <remarks>
        /// <b>The camera is why this lives here.</b> <c>SyncCamera</c> is private and knows the eye
        /// height and the ground scan; a caller that set <c>PositionX/Y</c> on the session by itself
        /// would move the party and leave the view behind. <see cref="Fall"/> does exactly this pair
        /// internally, which is the precedent — this is that pair made reachable.
        ///
        /// <para><b>A placement, not a step.</b> Nothing here scans for blockers or spends a move:
        /// the callers are the ones that have already decided the party goes somewhere — a pit
        /// crossing that was offered and accepted, and whatever follows it. A stepped version
        /// belongs beside <see cref="MoveForward"/>, not here.</para>
        /// </remarks>
        public void PlaceAt(int x, int y) {
            _session.PositionX = x;
            _session.PositionY = y;
            SyncCamera();
        }


        // ---------------------------------------------------------------- the rope swing

        /// <summary>
        /// Walks the party to the rope's start point and swings them across —
        /// <c>worldcross_hotspot_use_rope</c>'s two loops (WORLDCRS.C:190-252).
        /// </summary>
        /// <param name="crossingAxisIsY">True when the swing travels along Y.</param>
        /// <param name="pitX">The pit entity's position, which the party is snapped onto.</param>
        /// <param name="pitY"><inheritdoc cref="SwingAcrossAsync" path="/param[@name='pitX']"/></param>
        /// <param name="startAcross">The near lip: the pit's crossing coordinate ± the span.</param>
        /// <param name="landingAcross">The far lip.</param>
        /// <remarks>
        /// <b>It is TWO loops, not one, and they differ in more than length.</b> The first walks the
        /// party from wherever they clicked to the near lip on flat ground and stops within half a
        /// step of it; the second crosses in exact increments and writes the sagged height. Folding
        /// them into one would either sag the approach or lose the exact landing.
        ///
        /// <para><b>The lateral coordinate is snapped onto the pit's own line before either.</b> The
        /// party may be up to <c>LateralBand</c> off-centre when they click — the offer only
        /// requires them to be beside the hook — and the original assigns the pit's coordinate
        /// outright. Leaving the party's own puts them down parallel to the rope rather than on it.
        /// </para>
        ///
        /// <para><b>The sag is an ABSOLUTE eye height, not an offset</b>, and outside the band
        /// <see cref="PitRopeCrossing.SagHeightAt"/> answers a sentinel rather than a height — hence
        /// <see cref="PitRopeCrossing.IsSagging"/> guarding every read. The dip goes below zero at
        /// the centre; the party really does hang under the lip.</para>
        /// </remarks>
        public async Cysharp.Threading.Tasks.UniTask SwingAcrossAsync(bool crossingAxisIsY,
            int pitX, int pitY, int startAcross, int landingAcross) {
            IsCrossing = true;
            try {
                int savedEye = _eyeZ;
                _session.Rotation = unchecked((short)PitRopeCrossing.CrossingHeading(
                    crossingAxisIsY, startAcross, landingAcross));

                if (crossingAxisIsY) {
                    _session.PositionX = pitX;
                } else {
                    _session.PositionY = pitY;
                }
                SyncCamera();

                // The approach: flat, and it stops within half a step rather than landing exactly —
                // the party's start is wherever they were standing, not on the lattice.
                await StepAlongAsync(crossingAxisIsY, startAcross,
                    PitRopeCrossing.StepUnits / 2, _ => savedEye);
                SetAcross(crossingAxisIsY, startAcross);
                SyncCamera();

                // The crossing: exact, because it runs from one lip to the other in whole steps.
                int pitAcross = crossingAxisIsY ? pitY : pitX;
                var cued = false;
                await StepAlongAsync(crossingAxisIsY, landingAcross, 0, across => {
                    int d = across - pitAcross;
                    if (!PitRopeCrossing.IsSagging(d)) {
                        return savedEye;
                    }
                    if (!cued && PitRopeCrossing.PlaysSwingSound(d)) {
                        _playSfx?.Invoke(PitRopeCrossing.SwingSoundId);
                        cued = true;
                    }
                    return PitRopeCrossing.SagHeightAt(d);
                });

                // czone_resync_on_world_move: the party is somewhere new, so the candidate list and
                // the ground under them are both stale.
                if (_collision != null) {
                    _collision.BuildCandidates(_session.PositionX, _session.PositionY, DetailLevel);
                    if (_collision.TryScan(_session.PositionX, _session.PositionY,
                            out _, out int groundZ)) {
                        SetEyeHeight(groundZ);
                    }
                }
                SyncCamera();
            } finally {
                IsCrossing = false;
            }
        }

        private int Across(bool crossingAxisIsY) =>
            crossingAxisIsY ? _session.PositionY : _session.PositionX;

        private void SetAcross(bool crossingAxisIsY, int value) {
            if (crossingAxisIsY) {
                _session.PositionY = value;
            } else {
                _session.PositionX = value;
            }
        }

        /// <summary>
        /// Steps the crossing coordinate toward <paramref name="target"/>, one frame per step.
        /// </summary>
        /// <param name="tolerance">How close counts as arrived. Zero means exactly.</param>
        /// <param name="eyeAt">The eye height for a given crossing coordinate.</param>
        /// <remarks>
        /// The step count is computed up front rather than looped on a distance test: the crossing
        /// runs on an exact lattice and the approach does not, and a <c>while</c> over the second
        /// would spin for ever on a target it can never reach exactly.
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTask StepAlongAsync(bool crossingAxisIsY,
            int target, int tolerance, System.Func<int, int> eyeAt) {
            int distance = System.Math.Abs(target - Across(crossingAxisIsY));
            if (distance <= tolerance) {
                return;
            }

            int step = target > Across(crossingAxisIsY)
                ? PitRopeCrossing.StepUnits
                : -PitRopeCrossing.StepUnits;
            int frames = (distance - tolerance + PitRopeCrossing.StepUnits - 1)
                         / PitRopeCrossing.StepUnits;

            for (var i = 0; i < frames; i++) {
                SetAcross(crossingAxisIsY, Across(crossingAxisIsY) + step);
                _eyeZ = eyeAt(Across(crossingAxisIsY));
                _session.PositionZ = _eyeZ;
                SyncCamera();
                await Cysharp.Threading.Tasks.UniTask.Yield();
            }
        }

        /// <summary>The zone kind the pit rule gates on: pits do nothing above ground.</summary>
        private int ZoneKind => _underground
            ? ZoneDefinition.UndergroundZoneLocation
            : -1;

        /// <summary>
        /// Falling in — <c>worldcross_dungeon_descent_anim</c>.
        /// </summary>
        /// <remarks>
        /// The order is the original's, and it matters that the party effects come FIRST: they
        /// happen whether or not there is anywhere to fall to, because the lookup sits inside the
        /// animation branch and they sit outside it. A pit with no target still leaves the party
        /// near dead where they stand.
        ///
        /// <para><b>The party goes to full Near-death, not to a damage figure</b>, and
        /// <c>PartyDeathState</c> is asserted as 2 rather than left for the stat code to notice:
        /// state 1 makes the map screen play its own dialog, and the pit has already played
        /// <see cref="PitDescent.LandingDialogId"/>.</para>
        ///
        /// <para><b>The drop is animated over <see cref="PitDescent.DescentSteps"/> frames</b>, one
        /// per world-loop iteration, driven by <see cref="TickPendingDescent"/>. The camera moves to
        /// the target's x/y IMMEDIATELY and keeps the pit's eye height; only the height then falls.
        /// That is the original's order — <c>WORLDCRS.C</c> writes the target x/y before the loop
        /// and varies nothing but z inside it.</para>
        ///
        /// <para><b>The landing dialog waits for the last frame</b>, because the original shows it
        /// after the loop. Raising it up front would put a modal over the fall it is describing.</para>
        ///
        /// <para><b>One deviation, recorded because it is deliberate:</b> the original never restores
        /// the camera height — it leaves the eye <c>8 x 0x50</c> below where the pit was and relies
        /// on the world loop exiting (<c>bCombatExitRequest = 2</c>) to make that unobservable. Ours
        /// re-grounds on the last frame, because our camera is in a 3D scene and one left inside
        /// geometry looks broken where a 2D raster did not care.</para>
        /// </remarks>
        private void Fall() {
            var conditions = new List<ActorConditions>();
            var pools = new List<(ActorStat Health, ActorStat Stamina)>();
            foreach (byte member in _session.ActivePartyIndices) {
                conditions.Add(_session.ConditionsOf(member));
                ActorStat[] stats = _session.StatsOf(member);
                pools.Add((
                    stats != null ? stats[(int)ActorAttribute.Health] : null,
                    stats != null ? stats[(int)ActorAttribute.Stamina] : null));
            }
            PitDescent.ApplyToParty(conditions, pools);
            _session.PartyDeathState = PitDescent.PartyDeathStateOnFall;

            _playSfx?.Invoke(PitDescent.FallSoundId);

            if (_collision != null && _collision.TryFindDescentTarget(
                    _session.PositionX, _session.PositionY, DetailLevel,
                    out int targetX, out int targetY)) {
                _session.PositionX = targetX;
                _session.PositionY = targetY;
                _collision.BuildCandidates(targetX, targetY, DetailLevel);
                _descentEyeStart = _eyeZ;
                _descentFrame = 0;
                _descentFrames = PitDescent.StepsFor(descendKeyHeld: false);
                ApplyDescentFrame();
                return;
            }

            _showDialog?.Invoke(PitDescent.LandingDialogId);
        }

        /// <summary>Frames of the fall still to render, or zero when nothing is falling.</summary>
        private int _descentFrames;

        private int _descentFrame;

        /// <summary>The eye height the fall started from — the height at the PIT, not the target.</summary>
        private int _descentEyeStart;

        /// <summary>Whether the camera is mid-fall.</summary>
        public bool IsFalling => _descentFrames > 0;

        /// <summary>Whether the party is being swung across a pit.</summary>
        public bool IsCrossing { get; private set; }

        /// <summary>
        /// While true the player cannot move or turn — something is driving the camera.
        /// </summary>
        /// <remarks>
        /// <b>One guard on the two shared entry points rather than a check per caller.</b> The
        /// original shows a busy cursor and runs its descent and swing loops to completion inside
        /// the click, so there is no frame in which input reaches the world; ours animate across
        /// real frames, which re-opens a door the original never had. Both animations end by
        /// re-grounding, so a step taken mid-animation would also fight the height being written.
        /// </remarks>
        private bool CameraIsAnimating => IsFalling || IsCrossing;

        /// <summary>
        /// Puts the camera at the current frame of the fall.
        /// </summary>
        /// <remarks>
        /// <b>Frame 0 is the undropped height, and the last frame is <c>steps - 1</c>.</b> The
        /// original's loop is <c>for (i = 0; i &lt; steps; i++) z = saved_z - i * 0x50</c>, so a
        /// 9-frame fall descends <c>8 x 0x50</c>, not nine. Counting from one drops the party a
        /// step further than the original on every fall.
        /// </remarks>
        private void ApplyDescentFrame() {
            _eyeZ = _descentEyeStart + PitDescent.DropAtStep(_descentFrame);
            _session.PositionZ = _eyeZ;
            SyncCamera();
        }

        /// <summary>
        /// Advances the fall by one frame, and finishes it on the last one.
        /// </summary>
        private void TickDescent() {
            _descentFrame++;
            if (_descentFrame < _descentFrames) {
                ApplyDescentFrame();
                return;
            }

            _descentFrames = 0;
            if (_collision != null && _collision.TryScan(
                    _session.PositionX, _session.PositionY, out _, out int groundZ)) {
                SetEyeHeight(groundZ);
            }
            SyncCamera();
            _showDialog?.Invoke(PitDescent.LandingDialogId);
        }

        /// <summary>
        /// Widest correction the pivot will accept, in BaK angle space: <b>90°</b>, which is the
        /// original's reach.
        /// </summary>
        /// <remarks>
        /// <b>This was 45° (0x2000) and it wedged the party.</b>
        /// <c>worldmove_sweep_alt_headings</c> runs <c>max_iters = 0x4000 / g_nWorldGridStride</c>
        /// iterations widening by one turn-stride each time, so its widest probe is at exactly
        /// <c>0x4000</c> — ninety degrees, not forty-five. Halving that reach does not merely make
        /// the recovery gentler: it makes it <i>fail</i> wherever the only open heading is more than
        /// 45° off the blocked one, and then nothing turns the party at all.
        ///
        /// <para>Met in play on 2026-09-11 at (1230000, 768000) in zone 3. North and east were open
        /// and west and south were refused, so a party facing west needed a 90° correction; every
        /// probe inside ±45° was blocked, the sweep returned false, and the party sat in the corner
        /// through sixteen waypoints and four retries each — "walked 0" every time, on ground the
        /// original crosses. The comment here used to present 45° as a deliberate reading of
        /// "head-on hits stop you dead"; the original's own bound says otherwise.</para>
        /// </remarks>
        private const int MaxPivotAngle = 0x4000;

        /// <summary>
        /// Number of probes per side across <see cref="MaxPivotAngle"/> — 90, i.e. one per degree,
        /// so the widest correction lands exactly on 90°. This is the granularity of "just enough to
        /// get unstuck"; the original instead widens by the player's turn-stride (5.625° / 11.25° /
        /// 22.5°), which is coarser but keeps the corrected heading on the turn lattice — see the
        /// deviation note on <see cref="TrySweepAlternativeHeading"/>. Only the blocked-forward path
        /// pays for these probes, and only until one side opens.
        /// </summary>
        private const int PivotSteps = 90;

        /// <summary>
        /// The blocked-forward recovery. Widen a clockwise and a counter-clockwise probe together
        /// until one of them opens; the first side to open wins, and the party turns by exactly that
        /// much. If both open at the same angle the obstruction is symmetric about the party's
        /// heading — there is no better side, so the party bumps instead (the original's rule, kept).
        ///
        /// <para><b>Deviation from the original</b> (<c>worldmove_sweep_alt_headings</c> /
        /// <c>TryAlternativeMove</c> @0x71c06, spec §2.7 — see §10.1 for the rationale). The DOS
        /// version widened by the player's <i>turn-stride</i> (5.625° / 11.25° / 22.5°) out to ±90°,
        /// so a graze could swing the party a full 22.5° and a near-head-on hit could spin it 90°.
        /// This widens by 1°, and — since 2026-09-11 — out to the same ±90°: stopping at 45° left
        /// the party genuinely stuck in a corner, which the original never does. Only the
        /// <i>granularity</i> is a deviation now; the reach matches.</para>
        ///
        /// <para><b>Consequence:</b> the corrected heading is generally <i>not</i> a multiple of the
        /// turn-stride, which the original guaranteed. Turning afterwards steps in stride increments
        /// from wherever the pivot left you, so the party never returns to the lattice by turning.
        /// Nothing implemented today depends on that; road-following (TASK-91) requires exact compass
        /// headings and would simply refuse to travel until the player re-aligns.</para>
        /// </summary>
        /// <summary>
        /// The blocked-forward recovery while TRAVELLING — the <c>nWorldStepPending != 0</c> arm of
        /// <c>worldmove_sweep_alt_headings</c> (WORLDMOV.C:271).
        /// </summary>
        /// <remarks>
        /// <b>Two things make this a different sweep from <see cref="TrySweepAlternativeHeading"/>,
        /// and both matter.</b>
        ///
        /// <para>It widens by the player's TURN STRIDE rather than by one degree, so the heading it
        /// lands on is still a compass heading. The walking sweep's 1° granularity is a deliberate
        /// deviation (see its own note) and it is fatal here: travel needs
        /// <see cref="RoadTravel.IsOnLatticeLine"/> to hold, and an off-lattice heading can never
        /// satisfy it — the party would turn once and then be unable to travel at all.</para>
        ///
        /// <para>And it asks about ROAD, not about walkable ground. The original's travel arm calls
        /// <c>worldmove_probe_adjacent_cell</c>, whose test admits only kinds 1 and 2; the walking
        /// arm calls <c>worldmove_probe_walkable_at</c> with the full walkable set. Sweeping with
        /// the wrong one turns the party off the road and leaves it stuck on the next press.</para>
        ///
        /// <para>The candidate list is the original's <c>blocked_angles</c> — the headings whose
        /// lattice line the party is standing on — which is exactly what
        /// <see cref="RoadTravel.IsOnLatticeLine"/> answers, so it is asked rather than rebuilt.
        /// <b>Not modelled:</b> the original re-centres the probe origin on the sub-cell
        /// (<c>czone_world_pos_tile_sub_ctr</c>) when the party is off-centre. This probes from
        /// where the party stands, which is what <see cref="TravelStep"/>'s own test does, so the
        /// two agree; a party off the cell centre is a case travel does not produce.</para>
        /// </remarks>
        private bool TrySweepRoadHeading(ushort heading, out ushort target) {
            target = heading;
            int stride = _movement.TurnAngleFor(_preferences.Current.TurnSize);
            if (stride <= 0 || _collision == null) {
                return false;
            }

            // *** THE SWEEP STARTS ON THE TURN LATTICE, NOT FROM WHEREVER THE PIVOT LEFT US. ***
            // `RoadHeadingOpen` demands an EXACT compass heading, and the candidates below are the
            // origin plus whole strides — so an origin that is not itself on the stride lattice can
            // never produce one. The original could not reach such a heading; we can, because the
            // blocked-forward pivot probes by the degree (spec §10.1). Measured 2026-09-13 at
            // (671200, 823200) with the road bending north-east: from heading 0 the press turns the
            // party to 57344 and travel continues, and from 910 — 0.5° off, exactly what one bump
            // leaves behind — the identical press refuses, turns nothing, and no later press ever
            // recovers. Snapping the ORIGIN fixes that without touching the pivot's granularity.
            ushort origin = RoadTravel.SnapToStride(heading, stride);
            if (origin != heading && RoadHeadingOpen(origin)) {
                target = origin;
                return true;
            }

            int maxIterations = MaxPivotAngle / stride;
            int cw = origin, ccw = origin;
            for (int i = 0; i < maxIterations; i++) {
                cw += stride;
                ccw -= stride;
                bool cwOpen = RoadHeadingOpen(unchecked((ushort)cw));
                bool ccwOpen = RoadHeadingOpen(unchecked((ushort)ccw));

                if (cwOpen && ccwOpen) {
                    return false;   // symmetric: no better side, so the party bumps
                }
                if (cwOpen) {
                    target = unchecked((ushort)cw);
                    return true;
                }
                if (ccwOpen) {
                    target = unchecked((ushort)ccw);
                    return true;
                }
            }

            return false;
        }

        // *** THE COMPASS TEST IS THE ORIGINAL'S EXACT EQUALITY, AND IT IS LOAD-BEARING. ***
        // The sweep widens by the TURN stride, which is finer than the compass step, so most
        // candidates are not compass headings at all. The original compares each against
        // `blocked_angles`, a list of exact angles (0, ±90, 180, ±45, ±135), so a near-miss matches
        // nothing and the sweep keeps widening. `CompassIndex` TRUNCATES, so without this a heading
        // of 64512 — 354°, very nearly north — passes as index 7 and the party takes a 315°
        // diagonal step. Measured before this line existed: press one turned to -1024 and press two
        // moved +1600/+1600 on a heading that pointed somewhere else entirely.
        private bool RoadHeadingOpen(ushort heading) {
            if (!RoadTravel.IsCompassHeading(heading)
                || !RoadTravel.IsOnLatticeLine(heading, _session.PositionX, _session.PositionY)) {
                return false;
            }
            // The same cell-centre snap the step itself uses — the original's sweep does it too
            // (WORLDMOV.C:286), and a sweep that probed from a different origin than the step would
            // turn the party onto a heading the very next press then refuses.
            (int px, int py) = RoadTravel.ProbeOrigin(
                _session.PositionX, _session.PositionY, heading);

            return RoadTravel.ProbeAdjacentCell(px, py, heading, IsRoadAt);
        }

        private bool TrySweepAlternativeHeading(ushort heading, int step, out ushort target) {
            target = heading;

            for (int i = 1; i <= PivotSteps; i++) {
                int delta = MaxPivotAngle * i / PivotSteps;
                int cw = heading + delta;
                int ccw = heading - delta;

                bool cwOpen = _collision.ProbeWalkable(
                    _session.PositionX, _session.PositionY, unchecked((ushort)cw), step, out _, out _);
                bool ccwOpen = _collision.ProbeWalkable(
                    _session.PositionX, _session.PositionY, unchecked((ushort)ccw), step, out _, out _);

                if (cwOpen && ccwOpen) {
                    return false;
                }
                if (cwOpen) {
                    target = unchecked((ushort)cw);
                    return true;
                }
                if (ccwOpen) {
                    target = unchecked((ushort)ccw);
                    return true;
                }
            }

            return false;
        }

        private void Turn(bool turnViewLeft) {
            if (CameraIsAnimating) {
                return;
            }

            int angle = _movement.TurnAngleFor(_preferences.Current.TurnSize);
            // A player's own turn snaps in the original too (worldmove_apply_turn_step, no render loop).
            _swingYaw = null;
            // BaK's angle space runs opposite to Unity yaw (BakAngleToDegrees negates via
            // 65536-a), so a visual LEFT turn = ADD in BaK angle space — MovementMath.Turn with
            // left:false. Calibrated in-Editor: dot(new-forward, old-camera-right) is negative for
            // a left turn (forward-step alignment was already +1.0 at every heading).
            // Turning is never blocked and never costs time (spec §2.8).
            _session.Rotation = unchecked((short)MovementMath.Turn(
                unchecked((ushort)_session.Rotation), angle, left: !turnViewLeft));
            SyncCamera();
        }

        /// <summary>
        /// Record the party's altitude on the ground it is standing on.
        /// </summary>
        /// <remarks>
        /// <b>This is the PARTY's Z, not the camera's</b>, and the two are not the same thing in
        /// this game — see <see cref="SyncCamera"/>. It follows the ground because the ground is
        /// real: <c>ProximityWorld</c> returns a height whenever the party stands on a model that
        /// contributes one (a bridge, a ramp), and 0 elsewhere.
        /// </remarks>
        private void SetEyeHeight(int groundZ) {
            _eyeZ = _cameraHeightZ + groundZ;
            _session.PositionZ = _eyeZ;
        }

        /// <summary>
        /// The graphics-detail preference as FILTER.DAT's block index — <c>config.levelOfDetail</c>.
        /// </summary>
        /// <remarks>
        /// Public because the RENDER gate needs the same number the collision gate uses. They read
        /// one FILTER.DAT block between them and must read the same one; see
        /// <see cref="WorldEntityVisibility"/>.
        /// </remarks>
        public int DetailLevel => (int)(_preferences?.Current?.DetailLevel ?? GameData.Resources.Config.DetailLevel.High);

        /// <summary>
        /// While true, the walking eye stops writing the camera — something else owns the view.
        /// </summary>
        /// <remarks>
        /// <b>The combat arena is a DIFFERENT camera in the original</b>, and the only reason this
        /// flag exists is that we re-point the one we have instead of keeping two. Without it the
        /// arena pose is overwritten by the next sync, which happens on a great many paths here —
        /// so the fight looked right for a frame and then dropped back to eye level.
        ///
        /// <para>It suspends the WRITE, not the tracking: <see cref="SyncToCamera"/> still restores
        /// the walking pose from the party's real position once the flag clears, so a fight that
        /// moved them lands the eye in the right place.</para>
        /// </remarks>
        public bool CameraSuspended { get; set; }

        /// <summary>
        /// Raised whenever the party's position changes — every path, not only a walked step.
        /// </summary>
        /// <remarks>
        /// <b><see cref="Stepped"/> is not enough for anything that cares WHERE the party is.</b>
        /// It fires for a walked step, and the party also arrives places by being placed: a pit
        /// crossing (<see cref="PlaceAt"/>), a pit fall, a snap, a blocked step's rollback. All of
        /// those write the position and then sync the camera, which is why this hangs off the sync
        /// rather than off any one of them — the camera is already the thing that has to agree with
        /// the party's position, so it is where the list is complete.
        ///
        /// <para>Raised BEFORE the camera guard on purpose: the position has already changed by the
        /// time this runs, and a subscriber that tracks it must not be skipped just because the
        /// arena has suspended the walking eye.</para>
        /// </remarks>
        public Action Relocated;

        private void SyncCamera() {
            Relocated?.Invoke();
            if (_camera == null || CameraSuspended) {
                return;
            }
            // *** THE EYE FOLLOWS THE GROUND, AND THAT IS THE ORIGINAL'S BEHAVIOUR. ***
            // Settled 2026-08-26 after being got wrong twice in the other direction.
            // worldmove_party_attempt_move (WORLDMOV.C:87-123) lets the step routines write the
            // scanned GROUND z into the camera position and then adds the zone default on top:
            //
            //     result = worldmove_step_free_move(&camera.pos, ...);   // pos.z = ground
            //     camera.pos.nWorld_z += g_lZoneDefaultZ;                // + 230
            //
            // which is exactly _cameraHeightZ + groundZ. worldmove_try_step_along_axis (:759) and
            // worldmove_camera_crossing_apply (:177, run at zone load, after a fight, and on
            // leaving the map) do the same.
            //
            // The ABSOLUTE z in WORLDHIT.C:517-521 is real but belongs to a DIFFERENT camera — the
            // combat arena's scratch camera, which only the arena renderer draws through. Two
            // cameras, two rules; see StartData's type doc.
            //
            // Pitch: all three angles go through ConvertRotation's BakAngleToDegrees. The BaK->Unity
            // transform swaps Y<->Z (a reflection), which flips the sense of every rotation axis, not
            // just yaw — so pitch is inverted by the same rule as the play-verified yaw.
            _camera.transform.position = BakCoordinateConverter.ConvertPosition(
                _session.PositionX, _session.PositionY, _eyeZ);
            _camera.transform.rotation = BakCoordinateConverter.ConvertRotation(
                _cameraPitch, 0, _swingYaw ?? unchecked((ushort)_session.Rotation));
        }

        /// <summary>
        /// The yaw the camera shows while an automatic turn swings round, or null when it shows the
        /// party's heading.
        /// </summary>
        /// <remarks>
        /// <c>worldmove_animate_hdg_tgt</c> (WORLDMOV.C:161) renders a frame per turn stride until the
        /// heading arrives, for exactly the two automatic turns: the road's bend after a step and the
        /// reorientation after a refused one. A player's own turn snaps (TASK-443).
        /// <para><b>Ceiling:</b> only the camera swings; the party's heading is written at once and
        /// input is not held for the few frames the swing takes, where the original's loop blocks.</para>
        /// </remarks>
        private ushort? _swingYaw;

        // Called just before an automatic turn writes the heading: the swing starts from what is shown.
        private void BeginSwing() {
            _swingYaw ??= unchecked((ushort)_session.Rotation);
        }

        /// <summary>One frame of an automatic turn's swing — one stride toward the heading.</summary>
        public void TickTurnSwing() {
            if (_swingYaw is not ushort shown) {
                return;
            }
            _swingYaw = SwingStep(shown, unchecked((ushort)_session.Rotation),
                _movement.TurnAngleFor(_preferences.Current.TurnSize));
            SyncCamera();
        }

        /// <summary>
        /// The next yaw of a swing: one <paramref name="stride"/> the short way round toward
        /// <paramref name="target"/>, or null once within a stride (it lands exactly).
        /// </summary>
        public static ushort? SwingStep(ushort shown, ushort target, int stride) {
            int delta = unchecked((short)(ushort)(target - shown));
            int step = System.Math.Max(1, stride);
            return System.Math.Abs(delta) <= step
                ? (ushort?)null
                : unchecked((ushort)(shown + System.Math.Sign(delta) * step));
        }
    }
}
