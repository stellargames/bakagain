namespace BakAgain.World.Interaction {
    using BakAgain.Core;
    using BakAgain.UI;
    using BakAgain.UI.Inventory;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.Character;
    using GameData.Resources.World;

    /// <summary>
    /// Clicking a ladder, tunnel or tunnel exit — <c>wcursor_click_fixedobj_picklock</c>
    /// (WCURSOR.C:999).
    /// </summary>
    /// <remarks>
    /// <b>Shorter than the building click and different in every part that matters.</b> No reach
    /// test, no flag bits, no world-event gate, no warp — a lock and a message, and the message is
    /// where the traversal lives. <see cref="TraversalClick"/> carries the rules.
    /// </remarks>
    public sealed class TraversalInteractionHandler : IWorldInteractionHandler {
        private readonly GameSession _session;
        private readonly IDialogManager _dialog;
        private readonly InventoryMenu _inventoryMenu;
        private readonly UI.Navigation.IScreenNavigator _navigator;

        public TraversalInteractionHandler(GameSession session, IDialogManager dialog,
            InventoryMenu inventoryMenu, UI.Navigation.IScreenNavigator navigator) {
            _session = session;
            _dialog = dialog;
            _inventoryMenu = inventoryMenu;
            _navigator = navigator;
        }

        public string Behavior => "traversal";

        public async UniTask HandleAsync(WorldEntity entity, bool isPrimary) {
            Audio.MenuSoundService.Instance?.Play(TraversalClick.ClickSound);

            if (!isPrimary) {
                // Its own describe line — 0xae, not the building's 0x60.
                await _dialog.ShowById(TraversalClick.DescribeDialog);

                return;
            }

            (int bakX, int bakY) = BakCoordinateConverter.ToBakXY(entity.transform.position);
            SaveGameContainerData placement =
                _session.GetContainerAt(_session.CurrentZone, bakX, bakY);

            // *** THE PARAMS SUBRECORD MUST BE PRESENT, NOT MERELY ZERO. *** The original's guard
            // is an assignment inside the test — `(pSubrec1 = actorrec_get_subrecord(actor_record,
            // SUBREC_PARAMS)) != 0` — so an object with no lock block says "nothing happens" and
            // the picklock screen never opens. `LockValue` defaults a missing block to 0, which is
            // indistinguishable from a real lock of zero, and the whole prompt-screen-verdict
            // sequence then runs against a lock that is not there. See TraversalClick.HasLockToRun.
            if (placement == null || !TraversalClick.HasLockToRun(placement.LockData != null)) {
                await _dialog.ShowById(TraversalClick.NothingToDoDialog);

                return;
            }

            // *** THE LOCK RUNS WHATEVER THE VALUE. *** Unlike the building click there is no
            // "lockKey != 0" test: the original enters the picklock flow unconditionally, and a
            // zero simply lands in the lowest difficulty tier. So there is no way to walk a ladder
            // through by giving it no lock, and none is invented here.
            if (!await LockOpensAsync(FixedObjectAccess.LockValue(placement))) {
                return;
            }

            // *** THE TRAVERSAL IS THE DIALOG'S JOB. *** The message's own Teleport action moves
            // the party; moving anyone here would do it twice once the dialog runs.
            long message = FixedObjectAccess.InteractDialogId(placement) ?? 0;
            await _dialog.ShowById(
                (int)TraversalClick.DialogFor(isPrimary: true, hasFixedObject: true,
                    lockOpened: true, message));
        }

        /// <summary>
        /// Runs the lock and reports whether it gave way.
        /// </summary>
        /// <remarks>
        /// A working set that comes out empty is the original's own refusal — the party has neither
        /// keys nor picks — and it answers with that rather than opening a screen with nothing in
        /// it. Same shape the chest handler uses, through the same screen.
        /// </remarks>
        private async UniTask<bool> LockOpensAsync(int difficulty) {
            if (_inventoryMenu == null || _navigator == null) {
                return false;
            }

            if (!await _inventoryMenu.AskToOpenLockAsync(
                    GameData.Resources.Character.LockPicking.LockContext.Traversal)) {
                return false;   // see ContainerInteractionHandler
            }

            if (!_inventoryMenu.SetLock(difficulty)) {
                await _dialog.ShowById(PicklockWorkingSet.NothingToTryDialog);

                return false;
            }

            // The push returns when the screen is SHOWN, not when it closes, so the answer comes
            // from the screen itself — see InventoryMenu.LockOutcomeAsync. Reading LockOpened on
            // the line after the push read it before the player had touched the lock.
            await _navigator.Push(_inventoryMenu);

            return await _inventoryMenu.LockOutcomeAsync();
        }
    }
}
