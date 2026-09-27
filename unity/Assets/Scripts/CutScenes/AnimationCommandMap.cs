namespace BakAgain.CutScenes {
    using BakAgain.CutScenes.AnimationCommands;
    using GameData.Resources.Animation.FrameCommands;
    using System;
    using UnityEngine;

    public static class AnimationCommandMap {
        public static Func<CutsceneState, Awaitable> GetAction(FrameCommand frameCommand) {
            return frameCommand switch {
                CopyAreaBetweenBuffers args => args.ToAction(),
                CopyToTargetBuffer args => args.ToAction(),
                DialogCommand args => args.ToAction(),
                DisposeCurrentBitmap args => args.ToAction(),
                DisposeCurrentPalette args => args.ToAction(),
                DrawBorder args => args.ToAction(),
                FillArea args => args.ToAction(),
                DrawAreaFromBuffer args => args.ToAction(),
                DrawImage args => args.ToAction(),
                DrawImageScaled args => args.ToAction(),
                DrawImageFlippedHorizontally args => args.ToAction(),
                DrawImageFlippedHorizontallyScaled args => args.ToAction(),
                DrawImageFlippedVertically args => args.ToAction(),
                DrawImageFlippedVerticallyScaled args => args.ToAction(),
                DrawImageRotated args => args.ToAction(),
                DrawImageRotated180 args => args.ToAction(),
                DrawImageRotated180Scaled args => args.ToAction(),
                EndScene args => args.ToAction(),
                FadeIn args => args.ToAction(),
                FadeOut args => args.ToAction(),
                DisposeTargetBuffer args => args.ToAction(),
                GotoFrame args => args.ToAction(),
                LoadFontResource args => args.ToAction(),
                LoadPaletteResource args => args.ToAction(),
                LoadImageResource args => args.ToAction(),
                LoadScreenResource args => args.ToAction(),
                LoadSound args => args.ToAction(),
                LoadSoundResource args => args.ToAction(),
                PlaySound args => args.ToAction(),
                ResetPalette args => args.ToAction(),
                ScreenTransitionBoxIn args => args.ToAction(),
                ScreenTransitionBoxOut args => args.ToAction(),
                ScreenTransitionInstant args => args.ToAction(),
                ScreenTransitionInstant5 args => args.ToAction(),
                SelectFontSlot args => args.ToAction(),
                SelectImageSlot args => args.ToAction(),
                SelectPaletteSlot args => args.ToAction(),
                SetTargetBuffer args => args.ToAction(),
                SetClipArea args => args.ToAction(),
                SetColors args => args.ToAction(),
                SetFramesDuration args => args.ToAction(),
                SetRange1 args => args.ToAction(),
                SetRange2 args => args.ToAction(),
                SetRange3 args => args.ToAction(),
                StartPaletteCycle args => args.ToAction(),
                StopSound args => args.ToAction(),
                StopSoundPlayback args => args.ToAction(),
                StoreArea args => args.ToAction(),
                StoreScreen args => args.ToAction(),
                _ => null
            };
        }
    }
}