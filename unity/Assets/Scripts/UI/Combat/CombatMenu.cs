namespace BakAgain.UI.Combat {
    using BakAgain.Core;
    using BakAgain.ResourceManagement.Loaders;
    using GameData.Resources.Combat;
    using System;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The combat action menu — <c>COMBAT.DAT</c> over the travel HUD.
    /// </summary>
    /// <remarks>
    /// <b>It replaces the six travel buttons in place.</b> COMBAT's cells sit on REQ_MAIN's own six
    /// anchors — (1000,786) (1185,786) (1365,786) (1000,990) (1185,990) (1365,990) — with slightly
    /// smaller art (160x156 against 170x174). So "combat is an element in the HUD" needs no new
    /// mechanism: it is an overlay panel on the same cluster, exactly as
    /// <see cref="BakAgain.UI.CampMenu"/> is for the viewport.
    ///
    /// <para><b>Combat is not a screen and must not go through <c>IScreenNavigator</c></b>, whose
    /// invariant is one screen visible at a time — pushing it would hide the HUD it is part of.</para>
    ///
    /// <para><b>ACTION IDS COLLIDE ACROSS REQs, WHICH IS WHY THIS HANDLER EXISTS.</b> Id 19 is
    /// FollowRoad on REQ_MAIN and Defend here; 46 is CastSpell there and Cast here; 22 is a party
    /// portrait there and the character screen here. Feeding COMBAT.DAT through the travel screen's
    /// handler would fire the travel action every time — silently, since both ids are live. The ids
    /// are per-screen and only <see cref="CombatCommands"/> knows what they mean in this one.</para>
    /// </remarks>
    public class CombatMenu : MonoBehaviour, IActionHandler {
        private ILogger _logger;
        private UserInterfaceLoader _ui;
        private bool _applyingSlot;

        /// <summary>
        /// Raised when a button is pressed, with the command it means and the raw action id.
        /// </summary>
        /// <remarks>
        /// Deliberately an event rather than an injected service: the arena that will answer these
        /// does not exist yet (TASK-94's Unity layer), and inventing an interface with no
        /// implementation would be a guess at its shape. Nothing subscribed means the press is
        /// logged and dropped, which is what a HUD with no fight behind it should do.
        /// </remarks>
        public event Action<CombatCommands.Command, int> CommandIssued;

        /// <summary>
        /// Raised with the describe record a right-click asks for.
        /// </summary>
        /// <remarks>
        /// Mirrors <see cref="CommandIssued"/> deliberately: the HUD reports what was asked for and
        /// the owner decides what to do with it. Playing the dialog here would put dialog knowledge
        /// inside a menu that otherwise knows only about buttons.
        /// </remarks>
        public event Action<int> HelpRequested;

        /// <summary>Whether the acting character can take a ranged shot.</summary>
        public bool CanShoot { get; private set; }

        /// <summary>Whether the acting character can cast.</summary>
        public bool CanCast { get; private set; }

        private void Awake() {
            _logger = LogManager.LoggerFactory.CreateLogger<CombatMenu>();
            _ui = GetComponent<UserInterfaceLoader>();
        }

        /// <summary>Raises the combat menu over the travel HUD.</summary>
        public void Open() => gameObject.SetActive(true);

        /// <summary>Drops it and hands the HUD back.</summary>
        public void Close() => gameObject.SetActive(false);

        /// <summary>True while the menu is up.</summary>
        public bool IsOpen => gameObject.activeSelf;

        /// <summary>
        /// Tells the menu what the character whose turn it is can do, which decides the shared cell.
        /// </summary>
        /// <remarks>
        /// Until something calls this the cell shows the can-do-neither face, which is the honest
        /// default: it is the one COMBAT ships Disabled, so an unwired menu offers nothing rather
        /// than offering an action that would not work.
        /// </remarks>
        public void SetCapabilities(bool canShoot, bool canCast) {
            CanShoot = canShoot;
            CanCast = canCast;
            ApplyCapabilitySlot();
        }

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

        private void OnPanelBuilt(
            System.Collections.Generic.IReadOnlyList<BakAgain.UI.InputCore.NavWidget> _) =>
            ApplyCapabilitySlot();

        /// <summary>
        /// Shows exactly one of the three entries stacked on the capability cell.
        /// </summary>
        /// <remarks>
        /// All three are authored at (1000,786) and would otherwise be drawn on top of each other —
        /// looking correct until the state changed. <see cref="CombatMenuSlots.CapabilitySlot"/>
        /// picks the winner (shooting is tested first, so a character who can do both shows Shoot).
        ///
        /// <para>Guarded because <c>SetEntryState</c> re-raises <c>Built</c>, the same re-entrancy
        /// <see cref="BakAgain.UI.CampMenu"/> hit.</para>
        /// </remarks>
        private void ApplyCapabilitySlot() {
            if (_ui == null || !_ui.IsBuilt || _applyingSlot) {
                return;
            }

            _applyingSlot = true;
            try {
                int live = CombatMenuSlots.CapabilitySlot(CanShoot, CanCast);
                bool clickable = CombatMenuSlots.CapabilitySlotIsClickable(live);

                _ui.SetEntryState(CombatMenuSlots.ShootActionId, live == CombatMenuSlots.ShootActionId,
                    live == CombatMenuSlots.ShootActionId);
                _ui.SetEntryState(CombatMenuSlots.CastActionId, live == CombatMenuSlots.CastActionId,
                    live == CombatMenuSlots.CastActionId);
                // The neither-face is drawn but never navigable — it is a label, and the one entry
                // COMBAT ships Disabled.
                _ui.SetEntryState(CombatMenuSlots.NeitherActionId,
                    live == CombatMenuSlots.NeitherActionId, false);

                if (!clickable && live != CombatMenuSlots.NeitherActionId) {
                    _logger.LogWarning("Capability slot {Id} is live but not clickable.", live);
                }
            } finally {
                _applyingSlot = false;
            }
        }

        /// <inheritdoc />
        public void PrimaryAction(int actionId) {
            CombatCommands.Command command = CombatCommands.For(actionId);
            if (command == CombatCommands.Command.None) {
                _logger.LogDebug("Combat menu: id {Id} is not a combat command.", actionId);
                return;
            }
            if (command == CombatCommands.Command.CapabilityLabel) {
                return;   // drawn, never clickable
            }

            _logger.LogInformation("Combat menu: {Command} (id {Id}).", command, actionId);
            CommandIssued?.Invoke(command, actionId);
        }

        /// <inheritdoc />
        public Awaitable SecondaryAction(int actionId) {
            // Right-click DESCRIBES; it never acts. Each case of the original's command switch opens
            // with `if (is_preview) { dialog_play_record(id, 1); return; }`, so no fight state is
            // touched on this path — see CombatHelp.
            int record = CombatActionDispatch.HelpRecordFor(actionId);
            if (record < 0) {
                // The label and the hidden character-screen zone are the only two, and neither can be
                // clicked in the first place.
                _logger.LogDebug("Combat menu: id {Id} has no describe record.", actionId);
                return default;
            }

            HelpRequested?.Invoke(record);
            return default;
        }
    }
}
