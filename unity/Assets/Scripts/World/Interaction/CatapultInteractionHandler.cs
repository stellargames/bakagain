namespace BakAgain.World.Interaction {
    using BakAgain.Core;
    using BakAgain.UI;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// Firing the catapult — <c>handle_Catapult</c> (ovr190 @0x770aa). One entity ships with it.
    /// </summary>
    /// <remarks>
    /// <b>The animation IS the mechanic.</b> Nothing is granted, no encounter fires, no state
    /// changes: the arm swings and the sound cracks. <see cref="CatapultUse"/> carries the rules.
    ///
    /// <para><b>It is the door's mechanism, not a new one.</b> A flagged mesh is a flip-book —
    /// <c>renderShapeDispatcher</c> @0x2a7b2 draws face <c>(runtimeFlag % faceCount)</c> — which
    /// <c>TblMeshConverter</c> already emits and <see cref="WorldMeshFrames"/> already drives for
    /// <c>DoorVisualService.SwingAsync</c>.</para>
    /// </remarks>
    public sealed class CatapultInteractionHandler : IWorldInteractionHandler {
        private readonly GameSession _session;
        private readonly IDialogManager _dialog;

        public CatapultInteractionHandler(GameSession session, IDialogManager dialog) {
            _session = session;
            _dialog = dialog;
        }

        public string Behavior => "catapult";

        public async UniTask HandleAsync(WorldEntity entity, bool isPrimary) {
            // *** THE CLICK SOUND COMES FIRST AND FOR BOTH BUTTONS. *** The original plays it
            // before it has looked at anything, so even a catapult with no story attached clicks.
            Audio.MenuSoundService.Instance?.Play(FixedObjectClick.ClickSound);

            // A secondary click never touches the container at all — the engineer's appraisal is
            // reached before the lookup.
            if (!isPrimary) {
                await _dialog.ShowById(CatapultUse.ExamineDialog);
                return;
            }

            (int bakX, int bakY) = BakCoordinateConverter.ToBakXY(entity.transform.position);
            SaveGameContainerData placement =
                _session.GetContainerAt(_session.CurrentZone, bakX, bakY);
            SaveGameContainerDialogData dialogData = placement?.DialogData;
            SaveGameContainerEncounterData hotspot = placement?.EncounterData;

            if (dialogData == null || dialogData.DialogId == 0 || hotspot == null
                || !CatapultUse.HasAnything(hotspot.GlobalDataKey1)) {
                await _dialog.ShowById(CatapultUse.NothingHereDialog);
                return;
            }

            await _dialog.ShowById((int)dialogData.DialogId);

            // *** TWO GATES THAT READ ALIKE AND ARE NOT. *** A key of zero is "no story attached"
            // and was answered above. A real key whose VALUE is zero is "the story has not reached
            // this point": the container's own dialog plays and then nothing happens, silently.
            int globalValue = _session.GetGlobalValue(hotspot.GlobalDataKey1) ?? 0;
            if (!CatapultUse.Fires(hotspot.GlobalDataKey1, globalValue)) {
                return;
            }

            await SwingAsync(entity);

            // *** THE SOUND FOLLOWS THE ANIMATION. *** All four frames are drawn first; playing it
            // on the first frame would put the crack of the arm before the arm moves.
            Audio.MenuSoundService.Instance?.Play(CatapultUse.FireSoundId);
        }

        /// <summary>
        /// Steps the arm through <see cref="CatapultUse.FrameSequence"/>, a frame per frame.
        /// </summary>
        /// <remarks>
        /// <b>Four writes, not two.</b> The original runs two loops — down from 1, then up from 0 —
        /// which together read as a wind-back and a release. A single 0..1 sweep drops half the
        /// motion and looks like a twitch.
        ///
        /// <para>The original redraws the whole world between steps and so needs no timer; here a
        /// frame is a frame, which is the pacing <c>DoorVisualService</c> already settled on.</para>
        /// </remarks>
        private static async UniTask SwingAsync(WorldEntity entity) {
            WorldMeshFrames frames = entity == null ? null : entity.GetComponent<WorldMeshFrames>();
            if (frames == null || frames.FrameCount <= 1) {
                return;
            }
            foreach (int frame in CatapultUse.FrameSequence) {
                frames.SetFrame(frame % frames.FrameCount);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }
    }
}
