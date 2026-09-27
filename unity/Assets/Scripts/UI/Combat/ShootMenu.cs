namespace BakAgain.UI.Combat {
    using BakAgain.Core;
    using BakAgain.ResourceManagement.Loaders;
    using GameData.Resources.Combat;
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The quarrel picker — <c>SHOOT.DAT</c> over the travel HUD, raised by the melee menu's Shoot
    /// button and dropped again by Back.
    /// </summary>
    /// <remarks>
    /// <b>It replaces the melee menu in place</b>, on the same six REQ_MAIN anchors
    /// <see cref="CombatMenu"/> sits on, so it is an overlay panel rather than a screen and must not
    /// go through <c>IScreenNavigator</c> for the same reason.
    ///
    /// <para><b>WHICH CELL A BUTTON OCCUPIES IS DECIDED PER ACTOR, NOT BY THE FILE.</b> SHOOT.DAT
    /// authors eight quarrel buttons over four positions — two pages — but
    /// <c>shootmenu_rebuild</c> walks the eight kinds and writes each one the actor actually carries
    /// into the next free cell. So an archer holding only the last two kinds has them on page ONE,
    /// where the authored file would have put them on page two. This menu therefore MOVES the
    /// buttons (<see cref="UserInterfaceLoader.SetEntryPosition"/>) rather than only showing and
    /// hiding them; a port that just toggled visibility would leave gaps in the first page and hide
    /// live ammunition behind MORE.</para>
    ///
    /// <para><b>Ids collide with the melee menu.</b> Id 33 is Retreat on COMBAT.DAT and a cancel
    /// here — see <see cref="CombatCommands.BacksOutOfShootMenu"/>, which is why
    /// <see cref="IsOpen"/> is public: the owner has to answer that question rather than assume it.
    /// </para>
    /// </remarks>
    public class ShootMenu : MonoBehaviour, IActionHandler {
        private ILogger _logger;
        private UserInterfaceLoader _ui;
        private bool _applyingLayout;

        // Per-kind counts as of the turn the menu was opened, the packed cell->id map built from
        // them, and which page is showing. All three are rebuilt by Open(), never accumulated:
        // CombatMenuSlots.EntryFlagsAreRewrittenEachTurn.
        private int[] _quarrelsOfKind = Array.Empty<int>();
        private int[] _cells = Array.Empty<int>();
        private int _page = CombatMenuSlots.FirstPage;

        /// <summary>
        /// Raised when a quarrel kind is chosen, with the kind and the raw action id.
        /// </summary>
        /// <remarks>
        /// The kind is the useful half — <see cref="RangedExchange"/> and
        /// <see cref="RangedAmmoAccuracy"/> are both keyed by it — but the id goes with it so the
        /// owner can log or describe the button that was pressed without mapping back.
        /// </remarks>
        public event Action<int, int> QuarrelChosen;

        /// <summary>Raised when Back is pressed: the shot is abandoned and the melee menu returns.</summary>
        public event Action Cancelled;

        /// <summary>Raised with the describe record a right-click asks for.</summary>
        /// <remarks>Mirrors <see cref="CombatMenu.HelpRequested"/> — the menu reports, the owner plays.</remarks>
        public event Action<int> HelpRequested;

        /// <summary>True while the menu is up. <b>Asked by the retreat/cancel split on id 33.</b></summary>
        public bool IsOpen => gameObject.activeSelf;

        /// <summary>
        /// The quarrel button under the cursor, or -1.
        /// </summary>
        /// <remarks>
        /// <b>Read by the target panel, which names the quarrel you are POINTING at rather than the
        /// one you picked</b> — <c>combat_arena_shootmenu_qrl_cur</c> rect-tests the cursor against
        /// the live entry list on every redraw. Tracked with the buttons' own pointer events instead
        /// of a rect sweep because the entries already raise them for the hover highlight, and a
        /// sweep would need the canonical-to-screen transform this component does not hold.
        /// </remarks>
        public int HoveredActionId { get; private set; } = -1;

        /// <summary>Which page is showing, as the original counts them (1 or 2).</summary>
        public int Page => _page;

        /// <summary>The action id in each cell, or -1 where the actor carries nothing for it.</summary>
        public IReadOnlyList<int> Cells => _cells;

        private void Awake() {
            _logger = LogManager.LoggerFactory.CreateLogger<ShootMenu>();
            _ui = GetComponent<UserInterfaceLoader>();
        }

        /// <summary>
        /// Raise the menu for a character holding <paramref name="quarrelsOfKind"/> of each kind.
        /// </summary>
        /// <remarks>
        /// <b>Always reopens on page one.</b> The original rebuilds the entry list for whoever is
        /// acting, so a page the previous character had flipped to does not carry over — and with
        /// the cells repacked it would not mean the same thing if it did.
        /// </remarks>
        public void Open(IReadOnlyList<int> quarrelsOfKind) {
            _quarrelsOfKind = new int[CombatMenuSlots.ActionIdByQuarrelKind.Length];
            if (quarrelsOfKind != null) {
                for (var kind = 0; kind < _quarrelsOfKind.Length && kind < quarrelsOfKind.Count; kind++) {
                    _quarrelsOfKind[kind] = quarrelsOfKind[kind];
                }
            }

            _cells = CombatMenuSlots.PackCells(_quarrelsOfKind);
            _page = CombatMenuSlots.FirstPage;
            HoveredActionId = -1;
            gameObject.SetActive(true);
            ApplyLayout();
        }

        /// <summary>Drop it and hand the HUD back to the melee menu.</summary>
        public void Close() => gameObject.SetActive(false);

        private void OnEnable() {
            _ui = _ui ? _ui : GetComponent<UserInterfaceLoader>();
            if (_ui == null) {
                return;
            }
            _ui.Built += OnPanelBuilt;
            if (_ui.IsBuilt) {
                OnPanelBuilt(_ui.CurrentNavWidgets);
            }
        }

        private void OnDisable() {
            if (_ui != null) {
                _ui.Built -= OnPanelBuilt;
            }
        }

        private void OnPanelBuilt(IReadOnlyList<BakAgain.UI.InputCore.NavWidget> widgets) {
            TrackHover(widgets);
            ApplyLayout();
        }

        /// <summary>
        /// Follow the cursor across the quarrel buttons, for <see cref="HoveredActionId"/>.
        /// </summary>
        /// <remarks>
        /// Re-registered on every build because the panel rebuilds its widgets — registering twice
        /// on the same element is harmless (the second callback sets the same value), and the old
        /// elements are gone.
        /// </remarks>
        private void TrackHover(IReadOnlyList<BakAgain.UI.InputCore.NavWidget> widgets) {
            if (widgets == null) {
                return;
            }
            HoveredActionId = -1;
            foreach (BakAgain.UI.InputCore.NavWidget widget in widgets) {
                if (widget?.Element == null || CombatMenuSlots.QuarrelKindFor(widget.ActionId) < 0) {
                    continue;
                }
                int actionId = widget.ActionId;
                widget.Element.RegisterCallback<UnityEngine.UIElements.PointerEnterEvent>(
                    _ => HoveredActionId = actionId);
                widget.Element.RegisterCallback<UnityEngine.UIElements.PointerLeaveEvent>(_ => {
                    if (HoveredActionId == actionId) {
                        HoveredActionId = -1;
                    }
                });
            }
        }

        /// <summary>
        /// The authored cells, in file order: the entries carrying a quarrel id, whatever those ids
        /// are.
        /// </summary>
        /// <remarks>
        /// <b>Read from the layout, never listed here.</b> The cell order is the file's entry order
        /// and the positions are the file's positions; hardcoding either would put the pages back
        /// under 'no coordinates in code' and would break on a data override.
        /// </remarks>
        private List<int> AuthoredQuarrelCells() {
            var cells = new List<int>();
            foreach (int actionId in _ui.AuthoredActionIds) {
                if (CombatMenuSlots.QuarrelKindFor(actionId) >= 0) {
                    cells.Add(actionId);
                }
            }
            return cells;
        }

        /// <summary>
        /// Packs the carried kinds into the authored cells and shows the ones on the current page.
        /// </summary>
        /// <remarks>
        /// <para>Guarded because <c>SetEntryState</c> re-raises <c>Built</c> — the same re-entrancy
        /// <see cref="CombatMenu.SetCapabilities"/> and <see cref="BakAgain.UI.CampMenu"/> hit.</para>
        ///
        /// <para><b>The hidden character-screen zone (id 22) is deliberately untouched.</b>
        /// <c>combat_arena_menu_entry_flags</c> activates every entry EXCEPT that one, so it keeps
        /// whatever the file gave it — and SHOOT.DAT ships it invisible. Setting it here, either
        /// way, would be inventing a write the original does not make.</para>
        /// </remarks>
        private void ApplyLayout() {
            if (_ui == null || !_ui.IsBuilt || _applyingLayout || _cells.Length == 0) {
                return;
            }

            List<int> authored = AuthoredQuarrelCells();
            if (authored.Count == 0) {
                _logger.LogWarning("Shoot menu: the layout has no quarrel cells.");
                return;
            }

            _applyingLayout = true;
            try {
                for (var cell = 0; cell < _cells.Length; cell++) {
                    int actionId = _cells[cell];
                    if (actionId < 0) {
                        continue;   // no kind claimed this cell; whatever id is authored here stays hidden
                    }

                    // The cell's POSITION comes from the authored entry that sits there, which is
                    // generally a different button: the pages share four positions, so cell N and
                    // cell N+4 are the same place.
                    if (cell < authored.Count
                        && _ui.TryGetEntryRect(authored[cell], out Rect where)) {
                        _ui.SetEntryPosition(actionId, where.x, where.y);
                    }

                    bool live = CombatMenuSlots.QuarrelIsAvailable(cell, _page,
                        _quarrelsOfKind[CombatMenuSlots.QuarrelKindFor(actionId)]);
                    _ui.SetEntryState(actionId, live, live);
                }

                // Everything the actor is not carrying: hidden wherever the file put it. Done after
                // the packed pass so a kind that IS carried is never hidden by its own authored twin.
                foreach (int actionId in CombatMenuSlots.ActionIdByQuarrelKind) {
                    if (Array.IndexOf(_cells, actionId) < 0) {
                        _ui.SetEntryState(actionId, false, false);
                    }
                }
            } finally {
                _applyingLayout = false;
            }
        }

        /// <inheritdoc />
        public void PrimaryAction(int actionId) {
            if (actionId == CombatMenuSlots.PageFlipActionId) {
                _page = CombatMenuSlots.FlipPage(_page);
                ApplyLayout();
                return;
            }

            if (actionId == CombatMenuSlots.DistinctEnableGateActionId) {
                // Id 33 backs out HERE and retreats on the melee menu. Nothing about the fight
                // changes: abandoning the choice of quarrel is not spending the turn.
                _logger.LogInformation("Shoot menu: cancelled.");
                Cancelled?.Invoke();
                return;
            }

            int kind = CombatMenuSlots.QuarrelKindFor(actionId);
            if (kind < 0) {
                _logger.LogDebug("Shoot menu: id {Id} is not a quarrel button.", actionId);
                return;
            }

            _logger.LogInformation("Shoot menu: quarrel kind {Kind} (id {Id}).", kind, actionId);
            QuarrelChosen?.Invoke(kind, actionId);
        }

        /// <inheritdoc />
        public Awaitable SecondaryAction(int actionId) {
            // Right-click DESCRIBES; it never acts — the same preview arm the melee menu uses, and
            // the same record table: help is addressed by MENU POSITION, and the quarrel ids occupy
            // the first eight of those positions in kind order.
            int record = CombatActionDispatch.HelpRecordFor(actionId);
            if (record < 0) {
                _logger.LogDebug("Shoot menu: id {Id} has no describe record.", actionId);
                return default;
            }

            HelpRequested?.Invoke(record);
            return default;
        }
    }
}
