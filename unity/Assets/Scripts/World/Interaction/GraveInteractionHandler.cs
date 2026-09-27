namespace BakAgain.World.Interaction {
    using BakAgain.Core;
    using BakAgain.UI;
    using BakAgain.UI.Inventory;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.World;
    using System.Collections.Generic;

    /// <summary>
    /// Digging a grave — <c>handle_Grave</c> (ovr190 @0x77ca9). 37 entities ship with this type.
    /// </summary>
    /// <remarks>
    /// <b>A grave is a DIG, not a describe-or-loot.</b> It wants a Shovel in the party, spends it,
    /// may spring a positioned trap encounter first, and only then opens the container — or reports
    /// a body, or an empty coffin. <see cref="GraveDigging"/> carries the rules; this resolves the
    /// placement record into their arguments and acts on the answers.
    ///
    /// <para><b>The container is looked up by the grave's WORLD POSITION</b>, not hung off the
    /// clicked entity: <c>GetContainerAtLocation(zone, x, y)</c>. A port that reads state from the
    /// entity finds nothing at all.</para>
    /// </remarks>
    public sealed class GraveInteractionHandler : IWorldInteractionHandler {
        private readonly GameSession _session;
        private readonly IDialogManager _dialog;
        private readonly InventoryMenu _inventoryMenu;
        private readonly UI.Navigation.IScreenNavigator _navigator;
        private readonly System.Func<int, int, Hotspots.HotspotService.TrapDispatch> _fireTrap;

        public GraveInteractionHandler(GameSession session, IDialogManager dialog,
            InventoryMenu inventoryMenu, UI.Navigation.IScreenNavigator navigator,
            System.Func<int, int, Hotspots.HotspotService.TrapDispatch> fireTrap = null) {
            _session = session;
            _dialog = dialog;
            _inventoryMenu = inventoryMenu;
            _navigator = navigator;
            _fireTrap = fireTrap;
        }

        public string Behavior => "grave";

        public async UniTask HandleAsync(WorldEntity entity, bool isPrimary) {
            (int bakX, int bakY) = BakCoordinateConverter.ToBakXY(entity.transform.position);
            SaveGameContainerData placement =
                _session.GetContainerAt(_session.CurrentZone, bakX, bakY);
            SaveGameContainerDialogData dialogData = placement?.DialogData;

            // No container, or one with no dialog of its own, is not a grave this handler can act
            // on — the original returns after ddx 154 rather than falling through to a dig.
            if (dialogData == null || dialogData.DialogId == 0) {
                await _dialog.ShowById(GraveDigging.NothingHereDialog);
                return;
            }

            SaveGameContainerEncounterData hotspot = placement.EncounterData;
            int flags = dialogData.Flags;

            // *** A TRAPPED GRAVE IS DUG ONLY FROM ITS OWN TILE, AND SILENTLY OTHERWISE. ***
            // Before the sound and before any dialog, so clicking from the neighbouring tile
            // produces nothing whatsoever. Untrapped graves have no such test — and neither does a
            // grave with nothing under it: WCURSOR.C:938-940 puts this test inside the
            // `flags & (2|4|8)` branch, so the examine-only `else` at :984 is never gated by it.
            if (GraveDigging.IsDiggable(flags) && hotspot != null && hotspot.HasHotspotSet
                && !GraveDigging.PartyIsCloseEnough(bakX, bakY,
                    WorldPlacement.TileOf(_session.PositionX),
                    WorldPlacement.TileOf(_session.PositionY))) {
                return;
            }

            // *** THE CLICK SOUND PLAYS FOR A NON-DIGGABLE GRAVE TOO. *** Both branches of
            // wcursor_click_fixedobj_dig open with it — WCURSOR.C:944 (diggable) and :985 (the
            // examine-only else) — so it belongs above the IsDiggable split, not inside it.
            // Returning first made a plain tombstone the one clickable object in the world that
            // answers with no sound at all.
            Audio.MenuSoundService.Instance?.Play(FixedObjectClick.ClickSound);

            if (!GraveDigging.IsDiggable(flags)) {
                // *** NOT DIGGABLE IS NOT EXAMINE-ONLY. *** @0x77f1f: a primary click still shows
                // the grave's OWN dialog, and only a secondary click reads the tombstone. Answering
                // 173 to both loses whatever the grave had to say.
                await _dialog.ShowById(isPrimary ? (int)dialogData.DialogId
                    : GraveDigging.ExamineDialog);
                return;
            }

            if (!isPrimary) {
                await _dialog.ShowById(GraveDigging.ExamineDialog);
                return;
            }

            // *** THE GRAVE'S OWN DIALOG IS A QUESTION. *** Declining aborts before the shovel is
            // even looked for, which is why the no-shovel line reads as an afterthought.
            if (!await _dialog.ShowConfirmById((int)dialogData.DialogId)) {
                return;
            }

            // *** THE SHOVEL IS CHECKED AFTER THE CONFIRM. *** Tidier the other way round, and the
            // text ("Besides, we need a shovel") is written for exactly this moment.
            List<RuntimeContainer> packs = PartyPacks();
            if (!InventoryQuery.AnyHolds(packs, GraveDigging.ShovelObjectId)) {
                await _dialog.ShowById(GraveDigging.NoShovelDialog);
                return;
            }

            // *** THE TRAP SPRINGS BEFORE THE SHOVEL IS SPENT. *** @0x77e1a: the spawn runs first
            // and, when it reports nothing, clears the flag that gates the dig — so an ambush that
            // fires costs no shovel and turns up no loot.
            if (hotspot != null && hotspot.HasHotspotSet && _fireTrap != null) {
                // hotspotevt_dispatch_at_point (WCURSOR.C:308-314, :955-962): no trap hotspot at the point says
                // 154 and stops; a trap that fired, or an armed ambush even when spotted, stops without going on.
                Hotspots.HotspotService.TrapDispatch trap = _fireTrap(hotspot.HotspotX, hotspot.HotspotY);
                if (trap == Hotspots.HotspotService.TrapDispatch.NoTrap) {
                    await _dialog.ShowById(GraveDigging.NothingHereDialog);
                    return;
                }
                if (trap == Hotspots.HotspotService.TrapDispatch.Blocks) {
                    return;
                }
            }

            // Spent whatever the dig turns up: an empty coffin costs the same as a full one.
            foreach (RuntimeContainer pack in packs) {
                if (InventoryConsume.TryConsumeOne(pack, GraveDigging.ShovelObjectId,
                        id => _session.ObjectInfo?.GetById(id))) {
                    break;
                }
            }

            GraveDigging.Contents outcome = GraveDigging.OutcomeFor(flags);
            if (outcome != GraveDigging.Contents.Loot) {
                await _dialog.ShowById(GraveDigging.DialogFor(outcome));
                return;
            }

            RuntimeContainer runtime =
                _session.GetLiveContainerAt(_session.CurrentZone, bakX, bakY);
            if (runtime != null && _inventoryMenu != null && _navigator != null) {
                _inventoryMenu.SetContainer(runtime, WorldEntityType.Grave);
                _navigator.Push(_inventoryMenu).Forget();
            }
        }

        /// <summary>
        /// Every pack the shovel could be in — <c>CountItemInWholeParty</c> reads the whole party,
        /// not the leader.
        /// </summary>
        private List<RuntimeContainer> PartyPacks() => new List<RuntimeContainer>(_session.ActivePartyPacks);
    }
}
