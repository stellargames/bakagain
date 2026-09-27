namespace BakAgain.UI {
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Shared UI Toolkit sprite-application helpers.</summary>
    public static class VisualElementSpriteExtensions {
        /// <summary>Draw <paramref name="sprite"/> as the element's background at its NATIVE
        /// size, anchored top-left — the original engine's bitmap-blit convention (the element's
        /// width/height is a click/layout region, not the icon size, so the default
        /// background-size of 100% 100% would stretch it). Shared by
        /// <see cref="BakAgain.ResourceManagement.Loaders.MenuIconLoader"/> (BICONS) and
        /// <see cref="Inventory.ItemGridRenderer"/> (INVSHP item icons).</summary>
        public static void SetBackgroundSpriteNativeSizeTopLeft(this VisualElement element, Sprite sprite) {
            element.style.backgroundImage = Background.FromSprite(sprite);
            element.style.backgroundSize =
                new BackgroundSize(new Length(sprite.rect.width), new Length(sprite.rect.height));
            element.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left);
            element.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top);
        }

        /// <summary>Native-size background centered in the element — the original's item-icon
        /// placement (<c>invui_grid_render</c>: <c>rect + (rect.size − sprite.size)/2</c>).</summary>
        public static void SetBackgroundSpriteNativeSizeCentered(this VisualElement element, Sprite sprite) {
            element.style.backgroundImage = Background.FromSprite(sprite);
            element.style.backgroundSize =
                new BackgroundSize(new Length(sprite.rect.width), new Length(sprite.rect.height));
            element.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            element.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
        }

        /// <summary>
        /// Native-size background centred horizontally but sitting HIGH in the element.
        /// </summary>
        /// <remarks>
        /// The shop cell's icon: <c>UI_DrawInventory</c> @0x569a3 divides the leftover height by 4
        /// instead of 2, so the icon rides in the upper third and leaves the space beneath it for
        /// the price and name lines. Same rule, different divisor — which is why this is a sibling
        /// of the centred version rather than a special case inside it.
        /// </remarks>
        /// <param name="cellHeight">The element's own height, which the keyword form gets for free
        /// but an explicit offset has to be told.</param>
        public static void SetBackgroundSpriteNativeSizeRaised(this VisualElement element,
            Sprite sprite, float cellHeight) {
            element.style.backgroundImage = Background.FromSprite(sprite);
            element.style.backgroundSize =
                new BackgroundSize(new Length(sprite.rect.width), new Length(sprite.rect.height));
            element.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            float top = Mathf.Max(0f, (cellHeight - sprite.rect.height) / 4f);
            element.style.backgroundPositionY =
                new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(top));
        }
    }
}
