namespace BakAgain.UI.Character {
    using BakAgain.CutScenes;
    using BakAgain.Graphics;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Spells;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;
    using PaletteResource = GameData.Resources.Palette.PaletteResource;

    /// <summary>
    /// A character's spellbook page — <c>charscreen_draw_spell_book_actor</c>. Six category rows,
    /// each an icon in a box and the spells this character knows from that category.
    /// </summary>
    /// <remarks>
    /// <b>Every row is drawn, including the empty ones.</b> The box and its icon go down before the
    /// character's known spells are consulted, so someone who knows a single school still sees six
    /// categories rather than a shortened page.
    ///
    /// <para><b>The order is the file's.</b> INVSPELL.DAT's grouping and sequence are what the page
    /// shows — not alphabetical, not by spell id — and the same six groups the casting ring uses.</para>
    ///
    /// <para>Embeddable, like the sheet it belongs to: it fills a host element and owns no place.
    /// Geometry and pens are <see cref="SpellBookPageLayout"/>'s.</para>
    /// </remarks>
    internal sealed class SpellBookPageView {
        private readonly List<VisualElement> _elements = new();
        private int _generation;

        /// <summary>Remove everything the last render added.</summary>
        internal void Clear() {
            _generation++;
            foreach (VisualElement element in _elements) {
                element.RemoveFromHierarchy();
            }
            _elements.Clear();
        }

        /// <summary>Draw <paramref name="page"/> as this character knows it.</summary>
        /// <param name="knownSpells">The character's three known-spell words.</param>
        internal async UniTask RenderAsync(VisualElement host, SpellBookPage page,
            ushort[] knownSpells, IResourceCache sprites, PaletteResource palette,
            ILogger logger = null) {
            Clear();
            if (host == null || page?.Groups == null) {
                return;
            }
            int generation = _generation;

            for (var row = 0; row < page.Groups.Count && row < SpellBookPageLayout.Rows; row++) {
                SpellBookGroup group = page.Groups[row];
                int y = SpellBookPageLayout.RowY(row + 1);   // the original counts rows from one

                DrawBox(host, y, palette);
                DrawLine(host, y, LineFor(group, knownSpells), palette);
                await DrawIconAsync(host, group.Icon, y, sprites, generation, logger);
            }
        }

        private static string LineFor(SpellBookGroup group, ushort[] knownSpells) =>
            GameData.Resources.Spells.SpellBookPageView.Line(group, knownSpells);

        /// <summary>
        /// A row's box, and the black one behind it.
        /// </summary>
        /// <remarks>
        /// The shadow is a whole second rectangle of the same size, one original pixel down-right —
        /// drawn first, exactly as this screen family draws its text.
        /// </remarks>
        private void DrawBox(VisualElement host, int y, PaletteResource palette) {
            Add(host, "BakSpellBookBoxShadow",
                SpellBookPageLayout.BoxX + SpellBookPageLayout.ShadowOffsetX,
                y + SpellBookPageLayout.ShadowOffsetY,
                PaletteColors.ResolvePen(palette, SpellBookPageLayout.BoxShadowPen, Color.black),
                border: null);

            Add(host, "BakSpellBookBox", SpellBookPageLayout.BoxX, y,
                PaletteColors.ResolvePen(palette, SpellBookPageLayout.BoxFillPen, BoxFallback),
                PaletteColors.ResolvePen(palette, SpellBookPageLayout.BoxOutlinePen, OutlineFallback));
        }

        private VisualElement Add(VisualElement host, string name, float x, float y, Color fill,
            Color? border) {
            var box = new VisualElement {
                name = name,
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = x,
                    top = y,
                    width = SpellBookPageLayout.BoxWidth,
                    height = SpellBookPageLayout.BoxHeight,
                    backgroundColor = fill,
                },
            };
            if (border is { } colour) {
                box.style.borderLeftWidth = box.style.borderRightWidth = 1;
                box.style.borderTopWidth = box.style.borderBottomWidth = 1;
                box.style.borderLeftColor = box.style.borderRightColor = colour;
                box.style.borderTopColor = box.style.borderBottomColor = colour;
            }
            host.Add(box);
            _elements.Add(box);

            return box;
        }

        private async UniTask DrawIconAsync(VisualElement host, int icon, int y,
            IResourceCache sprites, int generation, ILogger logger) {
            if (sprites == null) {
                return;
            }
            Sprite sprite = await sprites.GetOrLoadAsync<Sprite>(
                SpellBookPageLayout.IconSet + "#" + icon);
            if (generation != _generation) {
                return;
            }
            if (sprite == null) {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger,
                    "SpellBookPageView: category icon {Icon} did not load.", icon);

                return;
            }
            var element = new VisualElement {
                name = "BakSpellBookIcon",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = SpellBookPageLayout.IconX,
                    top = y + SpellBookPageLayout.IconOffsetY,
                    width = sprite.rect.width,
                    height = sprite.rect.height,
                },
            };
            element.SetBackgroundSpriteNativeSizeTopLeft(sprite);
            host.Add(element);
            _elements.Add(element);
        }

        /// <summary>A row's spell list, and the shadow beneath it.</summary>
        private void DrawLine(VisualElement host, int y, string text, PaletteResource palette) {
            if (string.IsNullOrEmpty(text)) {
                return;   // the row is still drawn; only its list is empty
            }
            AddLabel(host, text,
                SpellBookPageLayout.TextX + SpellBookPageLayout.ShadowOffsetX,
                y + SpellBookPageLayout.ShadowOffsetY,
                PaletteColors.ResolvePen(palette, SpellBookPageLayout.TextShadowPen, Color.black));
            AddLabel(host, text, SpellBookPageLayout.TextX, y,
                PaletteColors.ResolvePen(palette, SpellBookPageLayout.TextPen, TextFallback));
        }

        private void AddLabel(VisualElement host, string text, float x, float y, Color colour) {
            var label = new Label(text) {
                name = "BakSpellBookLine",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = x,
                    top = y,
                    width = SpellBookPageLayout.TextWidth,
                    // Centred in the row's 30-line box: textwrap_draw_aligned's flag 0x10
                    // (TEXTWRAP.C, voff = (max_height - lines * line_height) / 2; CHARSCRN.C:83-87).
                    height = SpellBookPageLayout.TextHeight,
                    color = colour,
                    whiteSpace = WhiteSpace.Normal,   // the list wraps inside its width
                },
            };
            GameFontText.Apply(label, GameFontText.AnchorX.Left, GameFontText.AnchorY.Middle);
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            host.Add(label);
            _elements.Add(label);
        }

        // Legible stand-ins until the page's palette lands.
        private static readonly Color BoxFallback = new(0.16f, 0.13f, 0.10f);
        private static readonly Color OutlineFallback = new(0.55f, 0.45f, 0.30f);
        private static readonly Color TextFallback = new(244f / 255f, 196f / 255f, 164f / 255f);
    }
}
