namespace BakAgain.UI {
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Layout;
    using GameData.Resources.Menu;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The main menu's V: the version strip until the next click or key — see
    /// <see cref="VersionBanner"/> (TASK-804). The text is this build's version, not the original's.
    /// </summary>
    internal static class VersionBannerView {
        internal static async UniTask ShowAsync(VisualElement documentRoot, IResourceProviderService resources,
            InputLayerStack stack, object owner) {
            VisualElement stage = CanonicalStage.Find(documentRoot) ?? documentRoot;
            if (stage == null || stack == null) {
                return;
            }
            var palette = await resources.LoadAssetAsync<GameData.Resources.Palette.PaletteResource>(
                VersionBanner.Palette, owner);
            Color Pen(int pen) => PaletteColors.ResolvePen(palette, pen, Color.black);

            // The original waits in a loop that reads every click and key, so nothing under the
            // strip can be pressed while it is up.
            var blocker = new VisualElement {
                name = "version-banner",
                pickingMode = PickingMode.Position,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };
            var strip = new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = VersionBanner.X, top = VersionBanner.Y,
                    width = VersionBanner.Width, height = VersionBanner.Height,
                    backgroundColor = Pen(VersionBanner.FillPen),
                    borderLeftColor = Pen(VersionBanner.LeftPen), borderLeftWidth = OriginalPixel.Width,
                    borderRightColor = Pen(VersionBanner.RightPen), borderRightWidth = OriginalPixel.Width,
                    borderBottomColor = Pen(VersionBanner.BottomPen), borderBottomWidth = OriginalPixel.Height,
                },
            };
            var label = new Label(GameData.Resources.Text.UiTemplates.Format(
                GameData.Resources.Text.UiTemplates.VersionBannerKey, ("version", Application.version))) {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute, left = 0, right = 0, top = 0, bottom = 0,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    color = Pen(VersionBanner.TextPen),
                    textShadow = new TextShadow {
                        offset = new Vector2(0, OriginalPixel.Height),
                        color = Pen(VersionBanner.ShadowPen),
                    },
                },
            };
            GameFontText.Apply(label);
            strip.Add(label);
            blocker.Add(strip);

            // Only a mouse button (or keypad 5/0/+, the same buttons) ends it — the wait is
            // screen_input_poll_confirm_cancel (COMBAT.C:191); every other key is read and dropped.
            var layer = new ActionLayer("version-banner", onActivate: null, onCancel: null,
                skipActivates: false, onAccelerator: _ => true);
            stage.Add(blocker);
            stack.Push(layer);
            try {
                IPointer pointer;
                do {
                    await UniTask.Yield();
                    pointer = InputDriver.Pointer;
                } while (pointer != null && !pointer.Primary.PressedThisFrame && !pointer.Secondary.PressedThisFrame);
            } finally {
                stack.Remove(layer);
                blocker.RemoveFromHierarchy();
            }
        }
    }
}
