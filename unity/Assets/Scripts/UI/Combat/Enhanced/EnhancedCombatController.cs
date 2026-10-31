namespace BakAgain.UI.Combat.Enhanced {
    using System;
    using BakAgain.Combat;
    using BakAgain.Core;
    using BakAgain.World.Hotspots;
    using GameData.Resources.Combat;
    using GameData.Resources.Layout;
    using GameData.Resources.Text;
    using Command = GameData.Resources.Combat.CombatCommands.Command;

    /// <summary>
    /// Enhanced combat's turn states and input (TASK-972): what the player points at becomes a
    /// forecast, and one input per action commits it through <see cref="HotspotService"/>'s own
    /// click and command paths. No rule lives here; the forecast is the runtime's dry run.
    /// </summary>
    /// <remarks>
    /// Desktop: hover forecasts, a left click acts the default (Thrust / Move), Shift+click Swings, a
    /// right click on a foe forecasts Inspect and a second one on the same foe inspects; a stone
    /// click acts, a stone key forecasts first. Phone: a tap forecasts; Confirm, or a second tap on the
    /// same foe, trail end, selected attack button or forecast stone, acts; only Cancel backs out. A party member's turn opens on the foe that hero last targeted, else the
    /// party's last target, if it still lives: its forecast shown, nothing committed (owner, 2026-10-28).
    /// </remarks>
    public sealed class EnhancedCombatController : IDisposable {
        public enum TurnState { TheirTurn, Ready, Forecast, Acting }

        private readonly HotspotService _hs;
        private readonly CombatRuntime _cr;
        private readonly ICombatHud _hud;
        private readonly GameSession _session;
        private readonly bool _phone;

        // What the pointer is on (desktop hover, phone tap).
        private (int Col, int Row)? _cell;
        private int? _foe;
        private bool _party;

        // The forecast is kept as its intent and computed fresh for every frame and every act.
        private ForecastAction? _action;
        private int _slot = -1;
        private (int Col, int Row) _at;
        private bool _fromKey;
        private Combatant _turnOf;
        private bool _acting;
        private readonly Func<bool> _enhancedTurn;

        // The forecast was set by the pointer passing over (desktop hover), not chosen: it is no pick.
        private bool _hovered;

        // The foe each hero last attacked or inspected, and the party's latest, by enemy index.
        private readonly System.Collections.Generic.Dictionary<Combatant, int> _lastTargetOf = new();
        private int _partyLastTarget = -1;

        /// <param name="enhancedTurn">Whether the turn now playing is Enhanced (the HUD's per-turn latch). The
        /// controller lives for the fight, so in a Classic turn it must neither pre-select nor answer a pick.
        /// Null: always.</param>
        public EnhancedCombatController(HotspotService hs, CombatRuntime cr, ICombatHud hud,
            EnhancedCombatLayout layout, GameSession session = null, bool phone = false, Func<bool> enhancedTurn = null,
            Func<float> clock = null) {
            _layout = layout ?? new EnhancedCombatLayout();
            _clock = clock ?? (() => UnityEngine.Time.realtimeSinceStartup);
            _enhancedTurn = enhancedTurn ?? (() => true);
            _hs = hs;
            _cr = cr;
            _hud = hud;
            _session = session;
            _phone = phone;
            if (_hs != null) {
                _hs.CombatTurnStarted += OnHandoff;
                _hs.CombatTurnEnded += OnHandoff;
                _hs.CombatPickArmed += OnPickArmed;
            }
            if (_hud != null) {
                _hud.ConfirmPressed += Confirm;
                _hud.CancelPressed += Cancel;
                _hud.StoneActivated += OnStone;
                _hud.StoneDescribed += Describe;
                _hud.FoeRowHovered += OnFoeRowHovered;
                _hud.FoeRowChosen += OnFoeRowChosen;
                _hud.HeroCardRequested += OnHeroCard;
                _hud.SwitchPressed += OnSwitch;
            }
        }

        public void Dispose() {
            _disposed = true;
            if (_hs != null) {
                _hs.CombatTurnStarted -= OnHandoff;
                _hs.CombatTurnEnded -= OnHandoff;
                _hs.CombatPickArmed -= OnPickArmed;
            }
            if (_hud != null) {
                _hud.ConfirmPressed -= Confirm;
                _hud.CancelPressed -= Cancel;
                _hud.StoneActivated -= OnStone;
                _hud.StoneDescribed -= Describe;
                _hud.FoeRowHovered -= OnFoeRowHovered;
                _hud.FoeRowChosen -= OnFoeRowChosen;
                _hud.HeroCardRequested -= OnHeroCard;
                _hud.SwitchPressed -= OnSwitch;
            }
        }

        /// <summary>Something the HUD shows changed; show it now rather than at the next poll.</summary>
        public event Action Changed;

        public ICombatHud Hud => _hud;

        public HotspotService Service => _hs;

        public TurnState State {
            get {
                Combatant acting = Acting();
                return acting == null ? TurnState.TheirTurn
                    : _acting || Picking ? TurnState.Acting
                    : Current() != null ? TurnState.Forecast
                    : TurnState.Ready;
            }
        }

        /// <summary>The cell the pointer is on (hover, or where a tap landed), or null; the see-through follows it.</summary>
        public (int Col, int Row)? FocusCell => _cell;

        /// <summary>The previewed action, or null (Confirm is empty).</summary>
        public CombatForecast Current() {
            Combatant acting = Acting();
            if (acting == null || _action == null || _acting || Picking) {
                return null;
            }
            return ForecastOf(acting, _action.Value);
        }

        /// <summary>The HUD's frame: the fight as it stands plus this controller's forecast and card.</summary>
        public CombatHudModel Model(int logLines) {
            CombatForecast f = Current();
            CombatForecast alt = null;
            if (f != null && (f.Action == ForecastAction.Thrust || f.Action == ForecastAction.Swing)) {
                alt = ForecastOf(Acting(), f.Action == ForecastAction.Thrust ? ForecastAction.Swing : ForecastAction.Thrust);
            }
            int? card = f != null && f.TargetSlot >= 0 ? f.TargetSlot : !_phone && !_party ? _foe : null;
            CombatHudModel m = CombatHudModelBuilder.Build(_cr, _session, _hs?.EnhancedBoardMarks(), logLines, f, alt, card);
            // An armed pick waiting for its target says so, by name (owner, 2026-10-28: after the cast
            // screen it was not clear a target had to be chosen again).
            return Picking && _hs?.PendingPickName is string pick && m != null
                ? m with { Sentence = UiTemplates.Format(UiTemplates.HudChooseTarget, ("action", pick)) }
                : m;
        }

        /// <summary>The pointer is on this cell / combatant (hover; or where a tap or long press landed).</summary>
        /// <param name="combatant">The enemy's index when <paramref name="party"/> is false; null on an empty cell.</param>
        public void Point(int? col, int? row, int? combatant, bool party) {
            _cell = col.HasValue && row.HasValue ? (col.Value, row.Value) : null;
            _foe = combatant;
            _party = combatant.HasValue && party;
            if (_phone) {
                return;   // a finger has no hover: the tap decides
            }
            // A pinned forecast (Inspect, a stone key) stays while the pointer stays where it was.
            if (_action != null && (_action == ForecastAction.Inspect || _fromKey) && OnTarget()) {
                return;
            }
            SetHoverIntent();
        }

        /// <summary>Left click (desktop) or tap (phone).</summary>
        public void Primary(bool shift) {
            Combatant acting = Ready();
            if (acting == null) {
                return;
            }
            if (_phone) {
                // A tap forecasts; a second tap on the same thing -- the foe, or the shown trail's end cell --
                // acts it as Confirm would (owner, 2026-10-29). Empty ground out of reach does nothing. The
                // same foe commits the ATTACK: with its Inspect pinned (a long press) a tap forecasts the
                // Thrust instead, and only Confirm inspects.
                if (Foe() is int f) {
                    if (_slot == f && _action is ForecastAction.Thrust or ForecastAction.Swing) {
                        if (RetapCommits()) {
                            Confirm();
                        }
                    } else {
                        Pin(ForecastAction.Thrust, f, fromKey: false);
                    }
                } else if (_cell is (int c, int r) && !_foe.HasValue && _cr.ForecastMove(acting, c, r).Allowed) {
                    if (_action == ForecastAction.Move && _at == (c, r)) {
                        if (RetapCommits()) {
                            Confirm();
                        }
                    } else {
                        Pin(ForecastAction.Move, -1, fromKey: false);
                    }
                }
                return;
            }
            if (Foe() is int foe) {
                ForecastAction a = shift ? ForecastAction.Swing : ForecastAction.Thrust;
                Pin(a, foe, fromKey: false);
                Act(acting);
                return;
            }
            if (_cell.HasValue && !_foe.HasValue) {
                Pin(ForecastAction.Move, -1, fromKey: false);
                Act(acting);
            }
            // Yourself or an ally: nothing (the original's click-on-self shortcut is dropped, spec call 6).
        }

        /// <summary>Right click (desktop): Inspect forecast on a foe, a second one on the same foe inspects.</summary>
        public void Secondary() {
            Combatant acting = Ready();
            if (acting == null) {
                return;
            }
            if (Foe() is int foe) {
                if (_action == ForecastAction.Inspect && _slot == foe) {
                    Act(acting);
                } else {
                    Pin(ForecastAction.Inspect, foe, fromKey: false);
                }
            }
            // Elsewhere: describe. The board has no describe record of its own (phase 1).
        }

        /// <summary>Phone long press: Inspect forecast on a foe; elsewhere describe.</summary>
        public void LongPress() {
            if (Ready() != null && Foe() is int foe) {
                Pin(ForecastAction.Inspect, foe, fromKey: false);
            }
        }

        /// <summary>Phone Confirm, desktop Enter: act the forecast.</summary>
        public void Confirm() {
            Combatant acting = Ready();
            if (acting != null && _action != null) {
                Act(acting);
            }
        }

        /// <summary>Phone Cancel, desktop Esc: back to Ready.</summary>
        public void Cancel() {
            if (_action == null) {
                return;
            }
            Clear();
            if (!_phone) {
                SetHoverIntent();
            }
            Changed?.Invoke();
        }

        /// <summary>A stone. A desktop click acts; a key (or a phone tap) forecasts first, and on a
        /// key the same key again acts. Cast/Shoot open their existing, cancellable screens at once.</summary>
        public void Stone(Command c, bool fromKey) {
            Combatant acting = Ready();
            if (acting == null) {
                return;
            }
            var stones = _cr.AvailabilityFor(acting);
            if (c is Command.Cast or Command.Shoot) {
                bool shoot = CombatMenuSlots.CapabilitySlot(Enabled(stones, Command.Shoot), Enabled(stones, Command.Cast))
                    == CombatMenuSlots.ShootActionId;
                Command cmd = shoot ? Command.Shoot : Command.Cast;
                if (Enabled(stones, cmd)) {
                    // The spell's target is the foe the HUD shows chosen as Cast is pressed -- a tap, a chip, a
                    // long press, the remembered target -- never a desktop hover, and nothing once that is gone.
                    // A shot is not answered: its quarrel is only recorded and may change until the target click.
                    int picked = !shoot && _slot >= 0 && !_hovered
                        && _action is ForecastAction.Thrust or ForecastAction.Swing or ForecastAction.Inspect ? _slot : -1;
                    Clear();
                    _pickTarget = picked;   // after Clear(), which drops any older one (a backed-out cast's)
                    _hs.IssueCombatCommand(cmd, shoot ? CombatCommands.ShootId : CombatCommands.CastId);
                    Changed?.Invoke();
                }
                return;
            }
            if (ActionOf(c) is not ForecastAction a) {
                return;
            }
            if (fromKey && _fromKey && _action == a) {
                Act(acting);
                return;
            }
            // Phone: the forecast stone tapped again acts, as Confirm would (owner, 2026-10-31).
            if (_phone && !fromKey && _action == a) {
                if (RetapCommits()) {
                    Act(acting);
                }
                return;
            }
            Pin(a, -1, fromKey);
            if (!fromKey && !_phone) {
                Act(acting);
            }
        }

        /// <summary>Phone: the peek's Thrust / Swing switch.</summary>
        public void Switch() {
            if (_action is ForecastAction.Thrust or ForecastAction.Swing) {
                _action = _action == ForecastAction.Thrust ? ForecastAction.Swing : ForecastAction.Thrust;
                Changed?.Invoke();
            }
        }

        /// <summary>The keys Enhanced combat takes from the Classic menu; true when taken.</summary>
        public bool Key(int scancode) {
            if (Picking) {
                return false;   // the armed Classic pick owns every key, Esc included (it drops the pick)
            }
            switch (scancode) {
                case CombatCommands.CastId: Stone(Command.Cast, true); return true;
                case CombatCommands.BackOrRetreatId: Stone(Command.BackOrRetreat, true); return true;
                case CombatCommands.DefendId: Stone(Command.Defend, true); return true;
                case PackKey: Stone(Command.CharacterScreen, true); return true;
                case CombatCommands.RestId: Stone(Command.Rest, true); return true;
                case CombatCommands.AutoResolveId: Stone(Command.AutoResolve, true); return true;
                case EnterKey: Confirm(); return true;
                case SheetKey: OnHeroCard(); return true;
                // Esc backs out of a forecast; with none it is the Classic Escape (stops Auto, drops a pick).
                case CombatCommands.EscapeId when _action != null: Cancel(); return true;
                // One path per action: Shoot, Inspect and the pack have no key of their own here.
                case CombatCommands.ShootId:
                case CombatCommands.InspectId:
                case CombatCommands.CharacterScreenId:
                    return true;
                default: return false;
            }
        }

        private const int PackKey = 0x19;   // P
        private const int SheetKey = 0x23;  // H
        private const int EnterKey = 0x1c;

        private void OnStone(Command c) => Stone(c, fromKey: false);

        private void Describe(Command c) {
            int id = c switch {
                Command.Cast => CombatCommands.CastId,
                Command.Shoot => CombatCommands.ShootId,
                Command.BackOrRetreat => CombatCommands.BackOrRetreatId,
                Command.Defend => CombatCommands.DefendId,
                Command.Rest => CombatCommands.RestId,
                Command.AutoResolve => CombatCommands.AutoResolveId,
                _ => -1,
            };
            if (id >= 0) {
                _hs?.DescribeCombatCommand(id);
            }
        }

        private void OnFoeRowHovered(int slot) {
            if (slot < 0 || _cr?.Encounter == null || slot >= _cr.Encounter.Enemies.Count) {
                Point(null, null, null, false);
                return;
            }
            Combatant foe = _cr.Encounter.Enemies[slot];
            Point(foe.X, foe.Y, slot, false);
            Changed?.Invoke();
        }

        // Phone: a chip selects that foe as a tap on it would. Desktop: the row is the card's other
        // path (hover), so a click on it only keeps the card.
        private void OnFoeRowChosen(int slot) {
            OnFoeRowHovered(slot);
            if (_phone) {
                Primary(false);
            }
        }

        // The phone's hero chip opens its own sheet in the HUD; the desktop medallion opens the screen.
        private void OnHeroCard() {
            if (!_phone && Ready() != null) {
                _hs?.ShowCombatSheet();
            }
        }

        /// <summary>Phone: the peek's Thrust / Swing button. The other one switches; the selected one tapped
        /// again acts, as Confirm would (owner, 2026-10-29), unless it is a too-quick double tap.</summary>
        public void SwitchTo(ForecastAction a) {
            if (_action != a) {
                Switch();
                _forecastTapAt = _clock();
            } else if (_phone && _action is ForecastAction.Thrust or ForecastAction.Swing && RetapCommits()) {
                Confirm();
            }
        }

        private void OnSwitch(ForecastAction a) => SwitchTo(a);

        // Phone: a second tap on what is forecast commits it (Confirm's path), but not one that follows the tap
        // that made the forecast within RetapCommitMinSeconds -- that is a double tap, not a decision.
        private bool RetapCommits() => _clock() - _forecastTapAt >= _layout.RetapCommitMinSeconds;

        private readonly EnhancedCombatLayout _layout;
        private readonly Func<float> _clock;
        private float _forecastTapAt = float.NegativeInfinity;

        // A party member's turn starts: the pointer is probably still on something, so the new hero
        // gets its hover forecast at once rather than after the mouse moves (desktop). With nothing
        // under it (and always on the phone, which has no hover), the turn opens on the remembered target.
        private void OnHandoff(Combatant next) {
            OnHandoff();
            if (!_phone) {
                SetHoverIntent();
            }
            if (_action == null && _enhancedTurn() && Acting() is Combatant hero && hero == next
                && RememberedTarget(_lastTargetOf.TryGetValue(hero, out int own) ? own : -1, _partyLastTarget, Alive) is int slot) {
                Combatant foe = _cr.Encounter.Enemies[slot];
                if (_phone) {
                    _cell = (foe.X, foe.Y);   // the board's target ring and the see-through follow the pick
                    _foe = slot;
                    _party = false;
                }
                Pin(ForecastAction.Thrust, slot, fromKey: false);
            }
        }

        // A spell or quarrel was chosen: aim it at the foe picked before its screen opened, through the
        // Classic pick's own click -- which refuses a foe the spell cannot take, leaving the pick armed and
        // the prompt up. With no foe picked the prompt asks for one.
        // Raised from inside the cast screen's Committed, before the screen pops: the answer waits a frame,
        // so the cast resolves (and is drawn) over the arena, as a click after the pop would.
        private void OnPickArmed() {
            int target = _pickTarget;
            _pickTarget = -1;
            Combatant turn = _cr?.Encounter?.Current;
            if (target >= 0 && _enhancedTurn()) {
                AnswerPickNextFrame(target, turn).Forget();
            }
            Changed?.Invoke();
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid AnswerPickNextFrame(int target, Combatant turn) {
            await Cysharp.Threading.Tasks.UniTask.NextFrame();
            if (_disposed || _cr?.Encounter?.Current != turn || !Picking || !Alive(target) || !_enhancedTurn()) {
                return;
            }
            _hs.ResolveCombatTargetClick(target, false, isPrimary: true);
            Changed?.Invoke();
        }

        private bool _disposed;

        private int _pickTarget = -1;

        /// <summary>The hero's own last target, else the party's last if it lives, else null.</summary>
        internal static int? RememberedTarget(int heroLast, int partyLast, Func<int, bool> alive) =>
            heroLast >= 0 && alive(heroLast) ? heroLast : partyLast >= 0 && alive(partyLast) ? partyLast : null;

        private bool Alive(int slot) {
            var enemies = _cr?.Encounter?.Enemies;
            return enemies != null && slot >= 0 && slot < enemies.Count && enemies[slot] != null && !enemies[slot].IsDead;
        }

        private void OnHandoff() {
            _pickTarget = -1;
            Clear();
            _acting = false;
            Changed?.Invoke();
        }

        private void Clear() {
            _pickTarget = -1;   // a cleared selection is no target: nothing hidden may reach a later pick (Infinity Pool)
            _action = null;
            _slot = -1;
            _fromKey = false;
            if (_phone && _hs != null) {
                _hs.CursorCell = null;
            }
        }

        private void Pin(ForecastAction a, int slot, bool fromKey) {
            _hovered = false;
            _forecastTapAt = _clock();
            _action = a;
            _slot = slot;
            _at = _cell ?? default;
            _fromKey = fromKey;
            _turnOf = _cr?.Encounter?.Current;
            if (_phone && _hs != null && _cell.HasValue && a is ForecastAction.Move or ForecastAction.Thrust
                or ForecastAction.Swing or ForecastAction.Inspect) {
                _hs.CursorCell = _cell;   // the board's path and target ring follow the pick
            }
            Changed?.Invoke();
        }

        private void SetHoverIntent() {
            ForecastAction? before = _action;
            int slotBefore = _slot;
            if (Acting() == null) {
                Clear();
            } else if (Foe() is int foe) {
                _action = ForecastAction.Thrust;
                _slot = foe;
                _fromKey = false;
                _hovered = true;
                _turnOf = _cr.Encounter.Current;
            } else if (_cell.HasValue && !_foe.HasValue) {
                _action = ForecastAction.Move;
                _slot = -1;
                _at = _cell.Value;
                _fromKey = false;
                _hovered = true;
                _turnOf = _cr.Encounter.Current;
            } else {
                Clear();
            }
            if (before != _action || slotBefore != _slot || _action == ForecastAction.Move) {
                Changed?.Invoke();
            }
        }

        private bool OnTarget() =>
            _slot >= 0 ? Foe() == _slot : _cell == _at || (!_cell.HasValue && _slot < 0);

        private void Act(Combatant acting) {
            CombatForecast f = ForecastOf(acting, _action.Value);
            if (f == null || !f.Allowed) {
                Changed?.Invoke();   // the forecast stays up and says why
                return;
            }
            ForecastAction a = _action.Value;
            int slot = _slot;
            (int col, int row) = _at;
            if (slot >= 0 && a is ForecastAction.Thrust or ForecastAction.Swing or ForecastAction.Inspect) {
                _lastTargetOf[acting] = slot;
                _partyLastTarget = slot;
            }
            Clear();
            _acting = true;
            Changed?.Invoke();
            switch (a) {
                case ForecastAction.Move:
                    if (_hs.CellCentreWorld(col, row) is UnityEngine.Vector3 centre) {
                        _hs.ResolveCombatGroundClick(centre);
                    }
                    break;
                case ForecastAction.Thrust:
                case ForecastAction.Swing:
                    _hs.ResolveCombatTargetClick(slot, false, isPrimary: a == ForecastAction.Thrust);
                    break;
                case ForecastAction.Inspect:
                    _hs.IssueCombatCommand(Command.Inspect, CombatCommands.InspectId);
                    if (_hs.CombatPickPending) {   // armed: else this click would be a Thrust
                        _hs.ResolveCombatTargetClick(slot, false, isPrimary: true);
                    }
                    break;
                case ForecastAction.Pack:
                    _hs.ShowCombatPack();   // not command 22: its Shift arm would open the turn-spending sheet
                    break;
                default:
                    Command c = CommandOf(a);
                    _hs.IssueCombatCommand(c, IdOf(c));
                    break;
            }
            // A screen still open (the pack) or a refusal spent nothing: the hero is still Ready.
            _acting = !acting.CanAct(strict: true) || _cr.Encounter?.Current != acting;
            Changed?.Invoke();
        }

        private CombatForecast ForecastOf(Combatant acting, ForecastAction a) {
            if (acting == null) {
                return null;
            }
            Combatant foe = _slot >= 0 && _slot < (_cr.Encounter?.Enemies.Count ?? 0) ? _cr.Encounter.Enemies[_slot] : null;
            return a switch {
                ForecastAction.Thrust => _cr.ForecastMelee(acting, foe, CombatActionDispatch.MeleeAttack.Thrust),
                ForecastAction.Swing => _cr.ForecastMelee(acting, foe, CombatActionDispatch.MeleeAttack.Swing),
                ForecastAction.Inspect => foe == null ? null : _cr.ForecastInspect(acting, foe),
                ForecastAction.Move => _cr.ForecastMove(acting, _at.Col, _at.Row),
                _ => _cr.ForecastNoTarget(acting, a),
            };
        }

        // The acting hero, or null on their turn (or with no fight). A turn that changed under a
        // forecast without a handoff event (a death mid-turn) drops it too.
        private Combatant Acting() {
            Combatant c = _cr?.Encounter?.Current;
            if (c == null || !c.IsPartyMember || c.IsDead) {
                return null;
            }
            if (_turnOf != null && _turnOf != c) {
                _turnOf = null;
                Clear();
                _acting = false;
            }
            return c;
        }

        private Combatant Ready() => _acting || Picking ? null : Acting();

        // A Classic pick is armed (a spell's, an item's or a quarrel's target): a click is that pick's,
        // so the controller stands down -- no forecast shown, no keys -- until it is taken or dropped. The
        // hover intent is kept, so the forecast is back the moment Esc drops the pick.
        private bool Picking => _hs?.CombatPickPending ?? false;

        // The living foe under the pointer, by enemy index.
        private int? Foe() {
            if (_party || _foe is not int i || _cr?.Encounter == null || i < 0 || i >= _cr.Encounter.Enemies.Count) {
                return null;
            }
            Combatant foe = _cr.Encounter.Enemies[i];
            return foe == null || foe.IsDead ? null : i;
        }

        private static bool Enabled(System.Collections.Generic.IReadOnlyDictionary<Command, ActionAvailability> stones, Command c) =>
            stones.TryGetValue(c, out ActionAvailability a) && a.Enabled;

        private static ForecastAction? ActionOf(Command c) => c switch {
            Command.Defend => ForecastAction.Defend,
            Command.Rest => ForecastAction.Rest,
            Command.BackOrRetreat => ForecastAction.Retreat,
            Command.AutoResolve => ForecastAction.Auto,
            Command.CharacterScreen => ForecastAction.Pack,
            _ => null,
        };

        private static Command CommandOf(ForecastAction a) => a switch {
            ForecastAction.Defend => Command.Defend,
            ForecastAction.Rest => Command.Rest,
            ForecastAction.Retreat => Command.BackOrRetreat,
            ForecastAction.Auto => Command.AutoResolve,
            _ => Command.CharacterScreen,
        };

        private static int IdOf(Command c) => c switch {
            Command.Defend => CombatCommands.DefendId,
            Command.Rest => CombatCommands.RestId,
            Command.BackOrRetreat => CombatCommands.BackOrRetreatId,
            Command.AutoResolve => CombatCommands.AutoResolveId,
            _ => CombatCommands.CharacterScreenId,
        };
    }
}
