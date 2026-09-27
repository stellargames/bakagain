namespace BakAgain.World.Interaction {
    using System;
    using BakAgain.Core;
    using BakAgain.UI;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.World;

    /// <summary>
    /// Clicking a tunnel or a tunnel exit — <c>wcursor_click_npc_or_trap</c> (WCURSOR.C:857).
    /// </summary>
    /// <remarks>
    /// <b>The sibling of <see cref="TraversalInteractionHandler"/>, and for a long time these two
    /// kinds were wrongly on that one.</b> <see cref="TunnelClick"/> carries the rules and the
    /// dispatch table that separates them; the short version is that a ladder's traversal is its
    /// message's Teleport, while a tunnel's is a zone hotspot the object names by grid coordinate.
    /// </remarks>
    public sealed class TunnelInteractionHandler : IWorldInteractionHandler {
        private readonly GameSession _session;
        private readonly IDialogManager _dialog;
        private readonly Func<int, int, bool> _crossZoneAt;

        public TunnelInteractionHandler(GameSession session, IDialogManager dialog,
            Func<int, int, bool> crossZoneAt) {
            _session = session;
            _dialog = dialog;
            _crossZoneAt = crossZoneAt;
        }

        public string Behavior => "tunnel";

        public async UniTask HandleAsync(WorldEntity entity, bool isPrimary) {
            (int bakX, int bakY) = BakCoordinateConverter.ToBakXY(entity.transform.position);
            SaveGameContainerData placement =
                _session.GetContainerAt(_session.CurrentZone, bakX, bakY);
            SaveGameContainerEncounterData hotspot = placement?.EncounterData;

            // *** THE REACH TEST IS BEFORE THE SOUND. *** The original returns outright when the
            // object is not in the party's own map tile — no cue, no line. Playing the click first
            // and refusing after would announce a click the game never acknowledged.
            bool inReach = TunnelClick.IsWithinReach(
                WorldPlacement.TileOf(bakX), WorldPlacement.TileOf(bakY),
                WorldPlacement.TileOf(_session.PositionX), WorldPlacement.TileOf(_session.PositionY));

            TunnelClick.Outcome outcome = TunnelClick.For(isPrimary, inReach,
                hasFixedObject: placement != null,
                hasHotspot: hotspot?.HasHotspotSet ?? false,
                interactDialogId: FixedObjectAccess.InteractDialogId(placement) ?? 0);

            if (outcome == TunnelClick.Outcome.OutOfReach) {
                return;
            }

            Audio.MenuSoundService.Instance?.Play(TunnelClick.ClickSound);

            switch (outcome) {
                case TunnelClick.Outcome.Describe:
                    await _dialog.ShowById(TunnelClick.DescribeDialog);

                    return;

                case TunnelClick.Outcome.DispatchHotspot:
                    // The key is the OBJECT'S, not the party's — see HotspotService
                    // .FireZoneCrossingAt. A dispatch that matches nothing falls through to the
                    // same "nothing happens" the original reaches by `goto play_9a`.
                    if (_crossZoneAt != null && _crossZoneAt(hotspot.HotspotX, hotspot.HotspotY)) {
                        return;
                    }

                    break;

                case TunnelClick.Outcome.PlayMessage:
                    await _dialog.ShowById((int)(FixedObjectAccess.InteractDialogId(placement) ?? 0));

                    return;
            }

            await _dialog.ShowById(TunnelClick.NothingToDoDialog);
        }
    }
}
