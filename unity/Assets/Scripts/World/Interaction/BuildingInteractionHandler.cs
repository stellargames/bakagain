namespace BakAgain.World.Interaction {
    using BakAgain.Core;
    using BakAgain.UI;
    using BakAgain.UI.Inventory;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.World;

    /// <summary>
    /// Clicking a building or town gate — <c>wcursor_click_fixedobj_full</c> (WCURSOR.C:259).
    /// </summary>
    /// <remarks>
    /// <b>A building is a way IN, not a container.</b> The rules are
    /// <see cref="FixedObjectClick"/>; this resolves the placement record into that decision's
    /// arguments and acts on the answer.
    ///
    /// <para>Everything it needs is on the one record: the interact message and its flag bits in
    /// the dialog block, the lookup key in the lock block, and the GDS scene it leads to — plus
    /// whether it has a hotspot at all — in the encounter block, which IS the original's
    /// <c>SUBREC_HOTSPOT</c>.</para>
    /// </remarks>
    public sealed class BuildingInteractionHandler : IWorldInteractionHandler {
        private readonly GameSession _session;
        private readonly IDialogManager _dialog;
        private readonly InventoryMenu _inventoryMenu;
        private readonly UI.Navigation.IScreenNavigator _navigator;
        private readonly Scenes.LocationScenePlayer _locations;

        public BuildingInteractionHandler(GameSession session, IDialogManager dialog,
            InventoryMenu inventoryMenu, UI.Navigation.IScreenNavigator navigator,
            Scenes.LocationScenePlayer locations = null,
            System.Func<int, int, Hotspots.HotspotService.TrapDispatch> fireTrap = null) {
            _session = session;
            _dialog = dialog;
            _inventoryMenu = inventoryMenu;
            _navigator = navigator;
            _locations = locations;
            _fireTrap = fireTrap;
        }

        // Springs a trapped location's ambush. Optional and resolved lazily by the caller for the
        // same reason PitInteractionHandler's movement seam is: the fight lives on WorldRuntime,
        // which is built at a different moment from this list.
        private readonly System.Func<int, int, Hotspots.HotspotService.TrapDispatch> _fireTrap;

        public string Behavior => "building";

        public async UniTask HandleAsync(WorldEntity entity, bool isPrimary) {
            (int bakX, int bakY) = BakCoordinateConverter.ToBakXY(entity.transform.position);
            SaveGameContainerData placement =
                _session.GetContainerAt(_session.CurrentZone, bakX, bakY);

            SaveGameContainerEncounterData hotspot = placement?.EncounterData;

            // *** THE REACH TEST IS A TILE COMPARISON AND IT COMES FIRST — BEFORE THE SOUND. ***
            // A TRAPPED object clicked from the next tile along produces nothing at all, which is
            // the original's behaviour and not an oversight to improve on.
            //
            // *** AND "TRAPPED" IS THE FLAG, NOT THE RECORD. *** This passed `hotspot != null`,
            // which pinned every encounter-bearing object to the party's own tile — 28 of the 96
            // that carry encounter data fire no trap and should be clickable from anywhere.
            // WCURSOR.C:285 tests the subrecord AND the byte inside it.
            if (!FixedObjectClick.IsWithinReach(hotspot?.HasHotspotSet ?? false,
                    WorldPlacement.TileOf(bakX), WorldPlacement.TileOf(bakY),
                    WorldPlacement.TileOf(_session.PositionX),
                    WorldPlacement.TileOf(_session.PositionY))) {
                return;
            }

            Audio.MenuSoundService.Instance?.Play(FixedObjectClick.ClickSound);

            // *** A SECONDARY CLICK DESCRIBES; IT DOES NOT ENTER. *** The original's gate is
            // menupage_state_0e7c() != 1 — the poll's "which button", not a mode (see
            // MenuClickButton). It publishes the object's kind for the message to name and answers
            // with one dialog, so right-clicking a gate tells you what it is and leaves you outside.
            if (!isPrimary) {
                _session.SetGlobalValue(GdsDialogArgument,
                    placement?.DialogData?.ExamineMessageIndex ?? 0);
                await _dialog.ShowById(FixedObjectClick.DescribeDialog);

                return;
            }

            long dialogId = FixedObjectAccess.InteractDialogId(placement) ?? 0;
            int flags = placement?.DialogData?.Flags ?? 0;
            // A global the save has never written reads as absent; the gate treats that as "not
            // set", which is what an untouched story flag means.
            int eventValue = _session.GetGlobalValue(FixedObjectClick.EntryEventKey) ?? 0;

            // Published before the message plays, so a gated door can say something different
            // while it is still shut.
            _session.SetGlobalValue(GdsDialogArgument,
                FixedObjectClick.GateArgument(flags, eventValue));

            FixedObjectClick.Outcome outcome = FixedObjectClick.Resolve(
                FixedObjectAccess.LockValue(placement), dialogId != 0,
                hotspot != null && hotspot.GdsNumber != 0, flags, eventValue);

            // *** THE MESSAGE COMES AFTER THE HOTSPOT, AND A SPRUNG AMBUSH SKIPS IT. ***
            // WCURSOR.C:297-356: only a record carrying MessageFirstFlag speaks before the hotspot
            // dispatch (and none shipped does); every other message plays after it, and only when
            // the dispatch let the click go on. Playing it first had the Crystal Grove's dwellings say
            // "finally assured that they were alone, went inside" and then spring their ambush —
            // the original opens straight on "The door burst open".
            bool speaks = dialogId != 0 && outcome != FixedObjectClick.Outcome.NothingToDo;
            if (speaks && (flags & FixedObjectClick.MessageFirstFlag) != 0) {
                // The early message's refusal drops the hotspot and ends the click (:297-301).
                if (FixedObjectClick.AnswerCancelsClick(await _dialog.ShowById((int)dialogId))) {
                    return;
                }
                speaks = false;
            }

            // *** A TRAPPED LOCATION SPRINGS ITS AMBUSH HERE — BEFORE THE GDS/LOCK/INVENTORY ARM. ***
            // handle_Building @0x76c76: encounter data present, hasHotspot non-zero, then
            // sub_stub187_34(def_trap_dat, encounter.x, encounter.y). Putting it after the branch
            // would let a town scene open on top of the fight.
            //
            // *** THE SUB-TILE COMES FROM THE ENCOUNTER RECORD, NOT THE OBJECT. *** Passing the
            // clicked entity's own coordinates looks right and fires the wrong trap, or none.
            if (hotspot != null && hotspot.HasHotspotSet && _fireTrap != null) {
                // hotspotevt_dispatch_at_point (WCURSOR.C:308-314, :955-962): no trap hotspot at the point says
                // 154 and stops; a trap that fired, or an armed ambush even when spotted, stops without going on.
                Hotspots.HotspotService.TrapDispatch trap = _fireTrap(hotspot.HotspotX, hotspot.HotspotY);
                if (trap == Hotspots.HotspotService.TrapDispatch.NoTrap) {
                    await _dialog.ShowById(FixedObjectClick.NothingToDoDialog);
                    return;
                }
                if (trap == Hotspots.HotspotService.TrapDispatch.Blocks) {
                    return;
                }
            }

            // *** A NEGATIVE ANSWER CANCELS THE WHOLE CLICK. *** WCURSOR.C:355 is
            // `if (dialog_play_record(msgId, 0) == -1) goto cleanup;` — no town scene, no
            // inventory, nothing. The refusal leaf says so itself with a `SetReturnValue` of -1,
            // and the armourer is the case in hand: told "Go away! I am very, very busy", the port
            // used to walk into his shop anyway because this result was discarded.
            //
            // The value only reads as -1 because the extractor now signs it; it arrived as 65535
            // until 2026-09-13, which is why nothing could have tested this. See TASK-467.
            if (speaks && FixedObjectClick.AnswerCancelsClick(await _dialog.ShowById((int)dialogId))) {
                return;
            }

            switch (outcome) {
                case FixedObjectClick.Outcome.EntersTownScene:
                    // The pair is handed over as it stands: GdsSceneRules.UnpackScene, which
                    // LocationScenePlayer applies on the way in, owns the high-byte convention.
                    if (_locations != null) {
                        await _locations.RunAsync(hotspot.GdsNumber, hotspot.GdsLetter);
                    }

                    return;

                case FixedObjectClick.Outcome.OpensInventory: {
                    // *** ON THE CLICKED OBJECT'S OWN CONTAINER. *** WCURSOR.C:340/346/370 is
                    // `cmbinv_inventory_screen_run(actor, 0, 0)` with `actor` the object's record
                    // from actorspawn_objfixed — so an inn opens on its own shelf, and a container
                    // with a shop subrecord IS the shop. Pushing the singleton screen unbound showed
                    // whatever it was last set to: at Tyr-Sog's inn, "What would you like?" opened a
                    // member's pack and no food could be bought (TASK-555).
                    GameData.Resources.Inventory.RuntimeContainer runtime =
                        _session.GetLiveContainerAt(_session.CurrentZone, bakX, bakY);
                    if (runtime != null && _inventoryMenu != null && _navigator != null) {
                        // Robbed while the party was away? Every WCURSOR.C open handler asks (TASK-506).
                        _session.ExposeStashOnOpen?.Invoke(runtime);
                        _inventoryMenu.SetContainer(runtime, entity.EntityType);
                        await _navigator.Push(_inventoryMenu);
                    }

                    return;
                }

                case FixedObjectClick.Outcome.NothingToDo:
                    await _dialog.ShowById(FixedObjectClick.NothingToDoDialog);

                    return;

                default:
                    // Refused, Locked and MessageOnly have already said their piece, or have nothing
                    // to say. MessageOnly is WCURSOR.C's cleanup after the message: no warp, no
                    // inventory, nothing more (TASK-550).
                    return;
            }
        }

        /// <summary>
        /// The global the gate argument is published through.
        /// </summary>
        /// <remarks>
        /// The original writes <c>nEvtArgCount</c>, the same dialog-argument slot the encounter
        /// opening uses — so it is state the message reads, not a local.
        /// </remarks>
        private const int GdsDialogArgument = 30011;
    }
}
