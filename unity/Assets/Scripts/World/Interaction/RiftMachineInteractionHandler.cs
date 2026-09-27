namespace BakAgain.World.Interaction {
    using BakAgain.Core;
    using BakAgain.UI;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.World;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Running the rift machine — <c>handle_RiftMachine</c> (ovr190 @0x782c0). One entity ships.
    /// </summary>
    /// <remarks>
    /// <b>THE MACHINE ITSELF NEVER ANIMATES.</b> It raises a global render flag, and while that is
    /// set <c>worlditem_render_scrambled_while_rift_runs</c> @0x79a70 draws EVERY world item with
    /// two random state bytes in place of its own, restoring them afterwards. The whole scene
    /// flickers for ten frames; the gate is the one thing that stands still. That is why
    /// <c>gate2</c> carries no polygon region and why looking for its animation found nothing.
    ///
    /// <para><b>So this is not the catapult with different numbers.</b> The catapult steps its own
    /// item's frame and touches nothing global; this touches everything but itself.</para>
    /// </remarks>
    public sealed class RiftMachineInteractionHandler : IWorldInteractionHandler {
        private readonly GameSession _session;
        private readonly IDialogManager _dialog;

        public RiftMachineInteractionHandler(GameSession session, IDialogManager dialog) {
            _session = session;
            _dialog = dialog;
        }

        public string Behavior => "rift";

        public async UniTask HandleAsync(WorldEntity entity, bool isPrimary) {
            Audio.MenuSoundService.Instance?.Play(FixedObjectClick.ClickSound);

            if (!isPrimary) {
                await _dialog.ShowById(RiftMachineUse.ExamineDialog);
                return;
            }

            (int bakX, int bakY) = BakCoordinateConverter.ToBakXY(entity.transform.position);
            SaveGameContainerData placement =
                _session.GetContainerAt(_session.CurrentZone, bakX, bakY);
            SaveGameContainerDialogData dialogData = placement?.DialogData;
            SaveGameContainerEncounterData hotspot = placement?.EncounterData;

            // *** IT ACCEPTS TWO CONTAINER TYPES, NOT ONE. *** Unlike the grave and the catapult,
            // which require a fixedWorldItem, this takes that OR type 9 — see
            // RiftMachineUse.AcceptsContainerType.
            if (placement == null || !RiftMachineUse.AcceptsContainerType((int)placement.ContainerType)
                || dialogData == null || dialogData.DialogId == 0 || hotspot == null
                || hotspot.GlobalDataKey1 == 0) {
                await _dialog.ShowById(RiftMachineUse.NothingHereDialog);
                return;
            }

            await _dialog.ShowById((int)dialogData.DialogId);

            // A key with a zero VALUE is "the story has not reached this point": the dialog above
            // plays and then nothing happens, silently. Same two-gate split as the catapult.
            int globalValue = _session.GetGlobalValue(hotspot.GlobalDataKey1) ?? 0;
            if (!RiftMachineUse.Runs(hotspot.GlobalDataKey1, globalValue)) {
                return;
            }

            Audio.MenuSoundService.Instance?.Play(RiftMachineUse.RunSoundId);
            await ScrambleTheSceneAsync();
        }

        /// <summary>
        /// Ten frames with every world object showing a random frame, then two to settle.
        /// </summary>
        /// <remarks>
        /// <b>The settle frames are not padding.</b> The original clears the flag and redraws twice
        /// more; without them the last frame the player sees is one still carrying the effect, so
        /// the scene stays scrambled until something else happens to redraw it.
        ///
        /// <para><b>Every object's own frame is restored, not reset to zero.</b> The original saves
        /// and puts back each item's render record; a door caught mid-swing must still be mid-swing
        /// afterwards.</para>
        ///
        /// <para><b>One deliberate imprecision:</b> the original writes slot 0 and slot 1
        /// separately (<c>rand() &amp; 3</c> and <c>rand() % 5</c>) and each mesh part reads the slot
        /// its <c>RuntimeFlagsIndex</c> names. Our converter collapses a part's slot into one frame
        /// ordinal per object, so there is no slot to distinguish here; slot 0's range is used,
        /// being the one nearly every flagged mesh reads. The visible difference is confined to
        /// objects with more than four frames.</para>
        /// </remarks>
        private static async UniTask ScrambleTheSceneAsync() {
            WorldMeshFrames[] all = Object.FindObjectsByType<WorldMeshFrames>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var restore = new List<(WorldMeshFrames Frames, int Frame)>(all.Length);
            foreach (WorldMeshFrames frames in all) {
                if (frames.FrameCount > 1) {
                    restore.Add((frames, frames.Frame));
                }
            }
            if (restore.Count == 0) {
                return;
            }

            try {
                for (var frame = 0; frame < RiftMachineUse.EffectFrames; frame++) {
                    foreach ((WorldMeshFrames frames, int _) in restore) {
                        frames.SetFrame(Random.Range(0, RiftMachineUse.ScrambleSlot0Range));
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
            } finally {
                foreach ((WorldMeshFrames frames, int original) in restore) {
                    frames.SetFrame(original);
                }
            }

            for (var settle = 0; settle < RiftMachineUse.SettleFrames; settle++) {
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }
    }
}
