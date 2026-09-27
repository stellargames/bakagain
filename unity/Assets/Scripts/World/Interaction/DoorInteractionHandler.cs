namespace BakAgain.World.Interaction {
    using BakAgain.Core;
    using BakAgain.UI;
    using BakAgain.UI.Inventory;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Character;
    using GameData.Resources.Data;
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// Opening and shutting a door — <c>handle_Door</c> @0x778df.
    /// </summary>
    /// <remarks>
    /// <b>A door is not a container, and almost nothing the chest handler reads exists here.</b>
    /// Its identity is a door variant and its lock a bare difficulty byte, both on the placement
    /// record rather than in <c>SaveGameContainerLockData</c> — see <see cref="FixedObjectAccess"/>.
    /// The rules are <see cref="DoorMechanics"/>; this resolves the data and acts on the answer.
    ///
    /// <para><b>The open/shut truth is a GLOBAL FLAG, not the entity.</b> <c>7000 + variant</c> is
    /// what the original branches on, and the state word's open bit is the visual that follows it.
    /// That is what makes a door loaded from a save render in the position it was left in, and a
    /// port that trusts the word would open an already-open door.</para>
    /// </remarks>
    public sealed class DoorInteractionHandler : IWorldInteractionHandler {
        private readonly GameSession _session;
        private readonly IDialogManager _dialog;
        private readonly InventoryMenu _inventoryMenu;
        private readonly UI.Navigation.IScreenNavigator _navigator;
        private readonly DoorVisualService _visuals;

        public DoorInteractionHandler(GameSession session, IDialogManager dialog,
            InventoryMenu inventoryMenu, UI.Navigation.IScreenNavigator navigator,
            DoorVisualService visuals = null) {
            _session = session;
            _dialog = dialog;
            _inventoryMenu = inventoryMenu;
            _navigator = navigator;
            _visuals = visuals;
        }

        public string Behavior => "door";

        public async UniTask HandleAsync(WorldEntity entity, bool isPrimary) {
            // Touched at all: the original clicks before it decides anything, ahead of the
            // state-word guard and both branches.
            Audio.MenuSoundService.Instance?.Play(DoorMechanics.TouchSound);

            (int bakX, int bakY) = BakCoordinateConverter.ToBakXY(entity.transform.position);
            SaveGameContainerData? placement =
                _session.GetContainerAt(_session.CurrentZone, bakX, bakY);

            int? variant = FixedObjectAccess.DoorVariant(placement);
            if (variant == null) {
                return;   // a door shape with no placement record: nothing to open
            }

            int lockValue = FixedObjectAccess.LockValue(placement);
            bool isOpen = _session.GetGlobalValue(DoorMechanics.OpenFlagBase + variant.Value) == 1;
            int state = DoorMechanics.SeedState(variant.Value, isOpen);

            if (!isPrimary) {
                // *** ASKING IS NOT WORKING IT. *** The secondary click describes the door and
                // stops there — and WHICH description depends on whether it is open, so the two
                // are not interchangeable. It also answers before the lock is consulted, so asking
                // about a locked door does not open the picklock screen.
                await _dialog.ShowById(DoorMechanics.DescriptionDialogFor(isOpen));

                return;
            }

            DoorMechanics.DoorDecision decision = DoorMechanics.Decide(
                state, lockValue, isOpen,
                bakX - _session.PositionX, bakY - _session.PositionY);

            switch (decision.Action) {
                case DoorMechanics.DoorAction.Open:
                    await SetOpenAsync(entity, variant.Value, true);

                    return;

                case DoorMechanics.DoorAction.Close:
                    await SetOpenAsync(entity, variant.Value, false);

                    return;

                case DoorMechanics.DoorAction.TooCloseToClose:
                    // Not a bare refusal — the original has a companion laugh at you for walking
                    // into it, which is how the player learns to step back.
                    await _dialog.ShowById(DoorMechanics.TooCloseDialog);

                    return;

                case DoorMechanics.DoorAction.Locked:
                    await TryPickAsync(entity, variant.Value, decision.LockValue);

                    return;

                default:
                    return;   // Ignored — including the door-id-0 quirk DoorMechanics documents
            }
        }

        /// <summary>
        /// Runs the picklock screen on a locked door, and opens it if the lock gives.
        /// </summary>
        /// <remarks>
        /// The same screen a chest uses, and the same shape as
        /// <c>ContainerInteractionHandler.TryOpenLockAsync</c> — a door differs only in where its
        /// difficulty comes from and in writing a global rather than a container field.
        ///
        /// <para><b>A lock over 100 is key-only by design.</b> Lockpicks have no roll: the lock
        /// gives iff its score is at most 100 and strictly below the picker's skill, so a high
        /// value is not a failed roll to retry but a door that wants its key.</para>
        /// </remarks>
        private async UniTask TryPickAsync(WorldEntity entity, int variant, int lockValue) {
            if (_inventoryMenu != null && !await _inventoryMenu.AskToOpenLockAsync(
                    GameData.Resources.Character.LockPicking.LockContext.Door)) {
                return;   // see ContainerInteractionHandler — the prompt is the lock screen's own
            }

            if (_inventoryMenu == null || !_inventoryMenu.SetLock(lockValue)) {
                await _dialog.ShowById(PicklockWorkingSet.NothingToTryDialog);

                return;
            }

            await _navigator.Push(_inventoryMenu);
            // The push returns when the screen is SHOWN, not when it closes — see
            // InventoryMenu.LockOutcomeAsync. Reading LockOpened here read it before the player
            // had touched the lock, so a picked door never opened.
            if (await _inventoryMenu.LockOutcomeAsync()) {
                await SetOpenAsync(entity, variant, true);
            }
        }

        /// <summary>
        /// Writes the door's state where the original keeps it, and re-shapes it to match.
        /// </summary>
        /// <remarks>
        /// The FLAG is the truth and the model follows it — which is why the write comes first and
        /// the swap is allowed to fail. A door whose model could not be rebuilt is still open; it
        /// just looks wrong until the zone is next built, which is strictly better than a door that
        /// looks open and is not.
        /// </remarks>
        private async UniTask SetOpenAsync(WorldEntity entity, int variant, bool open) {
            _session.SetGlobalValue(DoorMechanics.OpenFlagBase + variant, open ? 1 : 0);

            // The hinge, then the latch — and the hinge plays whichever way the door is going,
            // which is why it is not called "the opening sound".
            Audio.MenuSoundService.Instance?.Play(DoorMechanics.SwingSound);
            if (_visuals != null) {
                // The panel swings on the door standing there, and only then is the entity rebuilt
                // in the other shape. Swapping first would animate a door that had already become
                // the other one — and since the two shapes are identical geometry differing only in
                // collision, the swap is what makes the doorway passable, not what makes it look
                // open.
                await _visuals.SwingAsync(entity.gameObject, open);
                GameObject rebuilt = await _visuals.SwapAsync(entity, open);
                DoorVisualService.SeedFrame(rebuilt, open);
            }
            // *** ONLY WHEN CLOSING. *** The two branches of the original are not symmetric — both
            // open with the swing, only the closing one ends with the latch. Playing it on the way
            // open made a door latch as it was pulled open.
            if (DoorMechanics.LatchSounds(open)) {
                Audio.MenuSoundService.Instance?.Play(DoorMechanics.LatchSound);
            }
        }
    }
}
