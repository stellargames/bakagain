namespace BakAgain.CutScenes.AnimationCommands {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement.Models;
    using BakAgain.UI;
    using GameData.Resources.Animation.FrameCommands;
    using GameData.Resources.Dialog;
    using System;
    using System.Linq;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public static class DialogCommandExtensions {
        private const string DialogBackground = "DIALOG.SCX";

        // Pen 6 is the chrome FILL colour used by HandleDialogClear when
        // painting the DIALOG.SCX plate background. NOT the text colour —
        // body text uses the per-style body pen `dialogTypeData.field_2`
        // (with a `field_3 - 1` shadow) in the original engine. See
        // `DialogStyle.BodyTextPenColor` and `RenderDialogText` at 0x48d7b.
        private const int DialogPlateFillPenIndex = 6;

        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(DialogCommandExtensions));

        public static Func<CutsceneState, Awaitable> ToAction(this DialogCommand args) {
            return async cutsceneState => {
                Logger.LogDebug("Running frame command: {Command}", args);
                // The pair decides this, not Dialog16Id alone: id 0 is a clear, a book-page step or
                // nothing depending on Arg2. See CutsceneDialogCommand, which owns the rule and is
                // tested without a cutscene.
                switch (GameData.Resources.Animation.CutsceneDialogCommand.KindOf(args.Dialog16Id, args.Arg2)) {
                    case GameData.Resources.Animation.CutsceneDialogCommand.Kind.Clear:
                        HandleDialogClear(cutsceneState);
                        break;
                    case GameData.Resources.Animation.CutsceneDialogCommand.Kind.BookAnimation:
                        HandleBookAnimation(args, cutsceneState);
                        break;
                    case GameData.Resources.Animation.CutsceneDialogCommand.Kind.Display:
                        HandleDialogDisplay(args, cutsceneState);
                        break;
                    case GameData.Resources.Animation.CutsceneDialogCommand.Kind.OpenBook:
                        cutsceneState.RequestedDialogs.Enqueue(new CutsceneDialogRequest {
                            Book = GameData.Resources.Animation.CutsceneDialogCommand.BookFor(args.Dialog16Id),
                        });
                        break;
                    default:
                        // Kind.None — including Dialog16Id -1, which draws from image slot 2 and is
                        // deliberately unimplemented rather than falling through to a dialog lookup.
                        break;
                }
            };
        }

        private static void HandleDialogClear(CutsceneState cutsceneState) {
            IndexedTexture dialogScreen = cutsceneState.Resources.Get<IndexedTexture>(DialogBackground);

            Drawing.DrawImage(dialogScreen, 0, 0, Orientation.Normal, Vector3.one, cutsceneState);

            // Draw flat area behind "window", just larger than the window, to act
            // as a border. RE-derived VGA rect (14, 10, 291, 103), in canonical px.
            Drawing.FillArea(
                new Area(14 * Canonical.VgaScaleX, 10 * Canonical.VgaScaleY,
                    291 * Canonical.VgaScaleX, 103 * Canonical.VgaScaleY),
                DialogPlateFillPenIndex, cutsceneState, true);

            // Draw background in dialog "window" — VGA rect (15, 11, 289, 102).
            cutsceneState.CopyArea(
                cutsceneState.BackgroundBufferIndex, cutsceneState.CurrentDrawBufferIndex,
                new Area(15 * Canonical.VgaScaleX, 11 * Canonical.VgaScaleY,
                    289 * Canonical.VgaScaleX, 102 * Canonical.VgaScaleY));
            cutsceneState.CopyBuffer(cutsceneState.CurrentDrawBufferIndex, cutsceneState.BackgroundBufferIndex);

            // Clear the text overlay
            cutsceneState.RequestedDialogs.Enqueue(new CutsceneDialogRequest { Clear = true });
        }

        private static void HandleDialogDisplay(DialogCommand args, CutsceneState cutsceneState) {
            int dialogId = GameData.Resources.Animation.CutsceneDialogCommand.DialogIdFor(args.Dialog16Id);
            string ddxKey = GameData.Resources.Animation.CutsceneDialogCommand.DdxKeyFor(dialogId);

            Dialog dialog = cutsceneState.Resources.Get<Dialog>(ddxKey);
            if (dialog == null) {
                Logger.LogWarning("Dialog resource {DdxKey} not found in preloaded resources", ddxKey);

                return;
            }

            DialogEntry entry = dialog.Entries.FirstOrDefault(e => e.Id == (uint)dialogId);
            if (entry == null) {
                Logger.LogWarning("Dialog entry {EntryId} not found in {DdxKey}", dialogId, ddxKey);

                return;
            }

            bool waitForInput = GameData.Resources.Animation.CutsceneDialogCommand.WaitsForInput(args.Arg2, entry.Flags);

            // No area override: per anim_show_dialog at 0x53df0, the original
            // cutscene narrative path (cases 0/3) calls RenderDialogText with
            // just the entry — text positioning comes from the entry's own
            // resolved DialogStyle.DefaultArea (PlainWithoutBox → row 3
            // → (8, 118, 305, 73) VGA). The (0, 115, 320, 85) strip the
            // original cutscene manages is purely a back-buffer save/restore
            // region; it's never passed to the dialog renderer.
            // Hand the live cutscene palette over via the request; the player
            // pushes it to DialogManager.SetActivePalette before showing, so
            // text/chrome pens (body = dialogTypeData.field_2, shadow =
            // field_3 - 1, speaker name = pen 0x0A / shadow 1) resolve against
            // the same indexed palette the original engine used.
            cutsceneState.RequestedDialogs.Enqueue(new CutsceneDialogRequest {
                Entry = waitForInput
                    ? GameData.Resources.Animation.CutsceneDialogCommand.WaitingEntry(args.Arg2, entry)
                    : entry,
                Palette = cutsceneState.CurrentPalette,
                WaitForInput = waitForInput,
            });
        }

        private static void HandleBookAnimation(DialogCommand args, CutsceneState cutsceneState) {
            IndexedTexture image = cutsceneState.GetImage(0, 0);
            if (image != null) {
                // The curve lives in GameData.BookPageTurn: it is a pure function of the step, it
                // is OUR approximation rather than a port (nothing in the reconstructed source skews
                // this bitmap), and keeping it here made both facts invisible. Skew is in canonical
                // px, proportional to the drawn image's canonical height — image.Height is raw
                // bitmap px, which is why CanonicalHeight is the one to scale.
                var widthFactor =
                    (float)GameData.Resources.Animation.BookPageTurn.WidthFactorAt(args.Arg2);
                var skew = (float)GameData.Resources.Animation.BookPageTurn.SkewAt(
                    args.Arg2, image.CanonicalHeight);
                float scaledSkew = skew * cutsceneState.ScaleFromOriginal.y;

                var resize = new Vector2(widthFactor, 1.0f);
                Drawing.DrawImageWithSkew(image, 0, 0, Orientation.Normal, resize, -scaledSkew, -scaledSkew, cutsceneState);
            }
        }
    }
}
