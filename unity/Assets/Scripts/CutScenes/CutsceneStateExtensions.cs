namespace BakAgain.CutScenes {
    using BakAgain.CutScenes.Extensions;
    using BakAgain.Graphics;
    using GameData.Resources.Animation;
    using UnityEngine;

    public static class CutsceneStateExtensions {
        public static void RenderIndexed(this CutsceneState cutsceneState) {
            if (!cutsceneState.IndexedMaterial) {
                return;
            }
            Graphics.Blit(cutsceneState.CurrentIndexedBuffer, cutsceneState.OutputBuffer, cutsceneState.IndexedMaterial);
        }

        public static void RenderDirect(this CutsceneState cutsceneState) {
            if (!cutsceneState.DirectMaterial) {
                return;
            }
            Graphics.Blit(cutsceneState.CurrentDirectBuffer, cutsceneState.OutputBuffer, cutsceneState.DirectMaterial);
        }

        public static void PrepareBuffers(this CutsceneState cutsceneState) {
            // *** BUFFER 0 IS THE FRAME'S STARTING SNAPSHOT, AND THE FADES DEPEND ON IT. ***
            // This was commented out under "I'm not sure why this was there", which broke fade-out.
            //
            // A fade must ramp the image as it stood when the frame BEGAN, not as it stands when the
            // fade command is reached. INTRO frame 21 is SetColors(0,0), FillArea(230,240,1140,708),
            // StoreScreen, FadeOut(208,48) — the FillArea has already blacked the "presents" text out
            // by the time the fade runs, so fading the CURRENT buffer fades something that is
            // already gone and the text simply vanishes. Fading this snapshot ramps the text away as
            // intended. Frame 19 does the same to the logo.
            //
            // With the copy gone, buffer 0 was left stale, which is why the fade first appeared to
            // blank the screen and then, once it read the current buffer instead, to be instant.
            Graphics.CopyTexture(cutsceneState.CurrentIndexedBuffer, cutsceneState.GetIndexedBuffer(0));
            Graphics.CopyTexture(cutsceneState.CurrentDirectBuffer, cutsceneState.GetDirectBuffer(0));
            Graphics.CopyTexture(cutsceneState.BackgroundIndexedBuffer, cutsceneState.CurrentIndexedBuffer);
            Graphics.CopyTexture(cutsceneState.BackgroundDirectBuffer, cutsceneState.CurrentDirectBuffer);
        }

        public static void RenderOutput(this CutsceneState cutsceneState) {
            Graphics.CopyTexture(cutsceneState.OutputBuffer, cutsceneState.Canvas);
        }

        public static void CopyBuffer(this CutsceneState cutsceneState, int src, int dst) {
            if (src == dst) {
                return;
            }
            Graphics.CopyTexture(cutsceneState.GetIndexedBuffer(src), cutsceneState.GetIndexedBuffer(dst));
            Graphics.CopyTexture(cutsceneState.GetDirectBuffer(src), cutsceneState.GetDirectBuffer(dst));
        }

        public static void CopyArea(this CutsceneState cutsceneState, int src, int dst, IArea area) {
            // A buffer copied onto itself is unchanged; Unity refuses the call and logs an error
            // (C31: SetTargetBuffer(1) while drawing into buffer 1, then CopyToTargetBuffer).
            if (src == dst) {
                return;
            }
            Area scaledArea = area.Scale<Area>(cutsceneState.ScaleFromOriginal);
            Drawing.CopyArea(cutsceneState.GetIndexedBuffer(src), cutsceneState.GetIndexedBuffer(dst), scaledArea);
            Drawing.CopyArea(cutsceneState.GetDirectBuffer(src), cutsceneState.GetDirectBuffer(dst), scaledArea);

        }

        public static void CopyArea(this CutsceneState cutsceneState, int src) {
            int dst = cutsceneState.CurrentDrawBufferIndex;
            IArea area = cutsceneState.Areas[src];
            cutsceneState.CopyArea(src, dst, area);
        }
    }
}