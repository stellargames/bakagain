namespace BakAgain.UI.InGame {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Font;
    using GameData.Resources.Spells;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The strip along the top of the travel screen showing which timed spell effects are running
    /// — <c>UI_DrawActiveSpellSymbols</c> (ovr179 @0x6d238).
    /// </summary>
    /// <remarks>
    /// <b>The symbols are spell-font glyphs, the same ones the casting ring draws.</b> The original
    /// builds a run of glyph codes and draws it as a word, which is why they centre together rather
    /// than sitting at fixed slots.
    ///
    /// <para>The plaque is drawn whether or not anything is running; only its contents come and
    /// go.</para>
    /// </remarks>
    public sealed class SpellEffectCaptionView {
        /// <summary>Name of the element this view adds to the stage.</summary>
        /// <remarks>Public so a screen rebuilding onto a persistent stage can drop the old one.</remarks>
        public const string PlaqueName = "BakSpellEffectCaption";

        private readonly GameSession _session;
        private readonly IResourceProviderService _resources;
        private readonly ILogger _logger;
        private readonly Dictionary<int, Sprite> _glyphs = new();
        private VisualElement _plaque;
        private VisualElement _symbols;
        private FontResource _font;
        private GameData.Resources.Palette.PaletteResource _palette;
        private int _shownMask = -1;

        public SpellEffectCaptionView(GameSession session, IResourceProviderService resources,
            ILogger logger) {
            _session = session;
            _resources = resources;
            _logger = logger;
        }

        /// <summary>Builds the plaque onto the canonical stage and fills it for the current mask.</summary>
        public async UniTask BuildAsync(VisualElement stage, object owner) {
            Sprite plaque = await _resources.LoadAssetAsync<Sprite>(PlaqueAddress, owner);
            if (plaque == null) {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(_logger,
                    "SpellEffectCaptionView: {Image} did not load; the strip is skipped.",
                    PlaqueAddress);

                return;
            }

            _plaque = new VisualElement {
                name = PlaqueName,
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = SpellEffectCaption.PlaqueX * Canonical.VgaScaleX,
                    top = SpellEffectCaption.PlaqueY * Canonical.VgaScaleY,
                    width = plaque.rect.width,
                    height = plaque.rect.height,
                    backgroundImage = Background.FromSprite(plaque),
                },
            };
            // The caption centres on the SCREEN, not on the plaque, so the row is a child of the
            // stage rather than of the plaque — and it is a row so the glyphs centre as one word.
            _symbols = new VisualElement {
                name = PlaqueName + "Symbols",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = SpellEffectCaption.TextCentreX * Canonical.VgaScaleX,
                    top = SpellEffectCaption.TextY * Canonical.VgaScaleY,
                    flexDirection = FlexDirection.Row,
                    translate = new StyleTranslate(new Translate(Length.Percent(-50f), 0)),
                },
            };
            stage.Add(_plaque);
            stage.Add(_symbols);

            _font = await _resources.LoadAssetAsync<FontResource>(SpellFontAddress, owner);
            // The plaque's palette, from the one owner of that mapping — not a name repeated here.
            _palette = await _resources.LoadAssetAsync<GameData.Resources.Palette.PaletteResource>(
                GameData.PaletteMapping.GetPaletteFor(SpellEffectCaption.PlaqueImage), owner);
            Refresh();
        }

        /// <summary>
        /// Redraws when the running effects have changed. Cheap; call each frame while shown.
        /// </summary>
        /// <remarks>
        /// The original redraws on a flag the world loop raises after casting, camping, a character
        /// screen or any step that advanced time — all of which are the occasions an effect can
        /// start or expire. Watching the mask itself covers the same moments without the loop.
        /// </remarks>
        public void Refresh() {
            if (_symbols == null || _session.PaletteEventMask == _shownMask) {
                return;
            }
            _shownMask = _session.PaletteEventMask;
            _symbols.Clear();
            if (_font == null) {
                return;
            }
            foreach (int glyph in SpellEffectCaption.Glyphs(_shownMask)) {
                Sprite sprite = GlyphSprite(glyph);
                if (sprite == null) {
                    continue;
                }
                _symbols.Add(new VisualElement {
                    pickingMode = PickingMode.Ignore,
                    style = {
                        width = sprite.rect.width,
                        height = sprite.rect.height,
                        backgroundImage = new StyleBackground(sprite),
                    },
                });
            }
        }

        /// <summary>Drops the elements this view put on the stage.</summary>
        public void Dispose() {
            _plaque?.RemoveFromHierarchy();
            _symbols?.RemoveFromHierarchy();
            _plaque = null;
            _symbols = null;
            _glyphs.Clear();
            _shownMask = -1;
        }

        private Sprite GlyphSprite(int glyph) {
            if (_glyphs.TryGetValue(glyph, out Sprite cached)) {
                return cached;
            }
            // The spell font spends a byte on each pixel, so a symbol carries its own indices; the
            // ink only stands in if the palette is missing.
            Sprite sprite = ResourceManagement.Converters.FontGlyphConverter.ToSprite(
                _font.GlyphFor(glyph), Color.white, _palette);
            _glyphs[glyph] = sprite;

            return sprite;
        }

        private const string PlaqueAddress = SpellEffectCaption.PlaqueImage + "#0";
        private const string SpellFontAddress = "SPELL.FNT";
    }
}
