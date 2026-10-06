namespace BakAgain.UI.Combat {
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Combat;
    using System.Collections.Generic;
    using System.Text;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The combat HUD's parchment readout — the surface all three arena panels are drawn on
    /// (<c>combat_arena_draw_tgt_info_panel</c>, <c>combat_arena_hud_melee_panel</c>,
    /// <c>combat_actor_draw_stats_panel</c>), each of which blits <c>parch.bmx</c> at the same
    /// 0x49,0x81 and then writes text into it.
    /// </summary>
    /// <remarks>
    /// <b>This view knows nothing about which panel it is drawing.</b> The models produce positioned
    /// <see cref="HudPanelLine"/>s, so a new panel is a model rather than a second renderer, and the
    /// panels cannot drift apart on the parchment, the pen or the placement rules.
    ///
    /// <para><b>It lives on the canonical stage, not on a REQ panel</b>, for the reason
    /// <see cref="BakAgain.UI.InGame.SpellEffectCaptionView"/> does: it is drawn at screen
    /// coordinates the REQ knows nothing about, in the gap between the character zone and the
    /// buttons, and the two REQs that share those buttons must not each own a copy of it.</para>
    ///
    /// <para><b>One label stands in for the original's separate number and suffix.</b> The routines
    /// compute an x for a trailing "%" from the width of the number, which is what a layout engine
    /// does anyway; the only difference is a gap of one space rather than 1px, under a canonical
    /// pixel at this font size.</para>
    /// </remarks>
    public sealed class HudParchmentPanelView {
        /// <summary>Name of the element this view adds to the stage.</summary>
        public const string PanelName = "BakCombatParchmentPanel";

        private const string PanelAddress = ShootTargetPanel.PanelImage + "#0";

        // Pen 0 — the routines set bText_fg_color = 0 and draw with no shadow, unlike the inventory
        // screen's text, which asks for a shadow explicitly.
        private static readonly Color InkColor = Color.black;

        // The rules' pens (2 and 3) against OPTIONS.PAL, the palette the parchment resolves under.
        // Read from the loaded palette when one is available; these are what it resolves to.
        private static readonly Color[] RulePenFallback = {
            new Color(0.42f, 0.30f, 0.16f), new Color(0.62f, 0.47f, 0.27f),
        };

        private readonly IResourceProviderService _resources;
        private readonly ILogger _logger;
        private readonly List<VisualElement> _drawn = new();
        private VisualElement _root;
        private VisualElement _parchment;
        private VisualElement _portrait;
        private int _portraitHeadId = -1;
        private string _shown;

        public HudParchmentPanelView(IResourceProviderService resources, ILogger logger) {
            _resources = resources;
            _logger = logger;
        }

        /// <summary>True once the parchment has loaded and the panel can be shown.</summary>
        public bool IsBuilt => _root != null;

        /// <summary>Builds the (hidden) parchment onto the canonical stage.</summary>
        public async UniTask BuildAsync(VisualElement stage, object owner) {
            if (stage == null || _root != null) {
                return;
            }
            Sprite parchment = await _resources.LoadAssetAsync<Sprite>(PanelAddress, owner);
            if (parchment == null) {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(_logger,
                    "HudParchmentPanelView: {Image} did not load; the combat panels are skipped.",
                    PanelAddress);

                return;
            }

            _root = new VisualElement {
                name = PanelName,
                // A readout over the HUD strip: a pick here would eat clicks meant for the buttons.
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = 0, top = 0, right = 0, bottom = 0,
                    display = DisplayStyle.None,
                },
            };
            _parchment = new VisualElement {
                name = PanelName + "Parchment",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = ShootTargetPanel.PanelX,
                    top = ShootTargetPanel.PanelY,
                    width = parchment.rect.width,
                    height = parchment.rect.height,
                    backgroundImage = Background.FromSprite(parchment),
                },
            };
            _root.Add(_parchment);
            stage.Add(_root);
        }

        /// <summary>
        /// Draw <paramref name="lines"/>, or hide the parchment when they are null or empty.
        /// </summary>
        /// <remarks>
        /// Cheap to call every frame: the content is rebuilt only when what it says changes, which
        /// is the same occasion the original redraws on (<c>hudState = 2</c>, raised when the cursor
        /// moves to another tile).
        /// </remarks>
        /// <param name="withParchment">
        /// False for content the original draws straight onto the world view rather than onto the
        /// parchment — the Inspect assessment is the one case.
        /// </param>
        public void Show(IReadOnlyList<HudPanelLine> lines, IReadOnlyList<HudPanelRule> rules = null,
            bool withParchment = true) {
            if (_root == null) {
                return;
            }
            if (lines == null || lines.Count == 0) {
                _root.style.display = DisplayStyle.None;
                _shown = null;
                return;
            }

            _root.style.display = DisplayStyle.Flex;
            _parchment.style.display = withParchment ? DisplayStyle.Flex : DisplayStyle.None;
            string signature = Signature(lines) + withParchment;
            if (signature == _shown) {
                return;
            }
            _shown = signature;
            foreach (VisualElement drawn in _drawn) {
                drawn.RemoveFromHierarchy();
            }
            _drawn.Clear();

            if (rules != null) {
                foreach (HudPanelRule rule in rules) {
                    AddRule(rule);
                }
            }
            foreach (HudPanelLine line in lines) {
                AddLine(line);
            }
            TextOverflowReport.CheckRows(_root);
        }

        /// <summary>
        /// Show the acting character's head beside the panel, or hide it with a negative id.
        /// </summary>
        /// <remarks>
        /// <b>Its lifetime is the fight, not one panel.</b> Only the stats panel draws it, but the
        /// parchment never overdraws it, so in the original it stays up while the shoot and melee
        /// panels come and go — see <see cref="ActorStatsPanel.PortraitImage"/>. Driving it from
        /// whoever is acting reproduces that without modelling page-2 persistence.
        /// </remarks>
        public void ShowPortrait(int headId) {
            if (_root == null || headId == _portraitHeadId) {
                return;
            }
            _portraitHeadId = headId;
            if (headId < 0) {
                if (_portrait != null) {
                    _portrait.style.display = DisplayStyle.None;
                }
                return;
            }
            LoadPortraitAsync(headId).Forget();
        }

        private async UniTaskVoid LoadPortraitAsync(int headId) {
            Sprite head = await _resources.LoadAssetAsync<Sprite>(
                ActorStatsPanel.PortraitImage + "#" + headId, this);
            if (_root == null || headId != _portraitHeadId) {
                return;   // the actor changed while this was loading
            }
            if (head == null) {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(_logger,
                    "HudParchmentPanelView: {Image}#{Head} did not load; the portrait is skipped.",
                    ActorStatsPanel.PortraitImage, headId);

                return;
            }
            _portrait ??= NewPortraitElement();
            _portrait.style.display = DisplayStyle.Flex;
            _portrait.style.width = head.rect.width;
            _portrait.style.height = head.rect.height;
            _portrait.style.backgroundImage = Background.FromSprite(head);
        }

        private VisualElement NewPortraitElement() {
            var element = new VisualElement {
                name = PanelName + "Portrait",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = ActorStatsPanel.PortraitX,
                    top = ActorStatsPanel.PortraitY,
                },
            };
            // Onto the STAGE, not the panel root: the panel root is hidden whenever no panel is up,
            // and the head outlives any one panel.
            _root.parent.Add(element);
            return element;
        }

        /// <summary>Drops the panel from the stage. Called when the screen it was built on goes.</summary>
        public void Dispose() {
            _root?.RemoveFromHierarchy();
            _portrait?.RemoveFromHierarchy();
            _root = null;
            _parchment = null;
            _portrait = null;
            _portraitHeadId = -1;
            _drawn.Clear();
            _shown = null;
        }

        private static string Signature(IReadOnlyList<HudPanelLine> lines) {
            var text = new StringBuilder();
            foreach (HudPanelLine line in lines) {
                text.Append(line.Text).Append('@').Append(line.X).Append(',').Append(line.Y)
                    .Append(line.Align).Append('\n');
            }
            return text.ToString();
        }

        private void AddRule(HudPanelRule rule) {
            var bar = new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = rule.X,
                    top = rule.Y,
                    width = rule.Width,
                    height = rule.Height,
                    backgroundColor = rule.Pen >= 0 && rule.Pen < RulePenFallback.Length
                        ? RulePenFallback[rule.Pen]
                        : RulePenFallback[RulePenFallback.Length - 1],
                },
            };
            _root.Add(bar);
            _drawn.Add(bar);
        }

        private void AddLine(HudPanelLine line) {
            Label label = LineLabel(line, line.X, line.Y);
            _root.Add(label);
            _drawn.Add(label);
        }

        /// <summary>One parchment line as a label at the given canonical position — shared with the
        /// combat assessment, whose rows are drawn the same way (CBENC.C:307-349).</summary>
        public static Label LineLabel(HudPanelLine line, float left, float top) {
            var label = new Label(line.Text) {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = left,
                    top = top,
                    color = InkColor,
                    // font_draw_text_ds's alignment: 1 is x -= width/2, 2 is x -= width, 0 draws
                    // from x. Expressed as a translate so the label keeps its intrinsic width.
                    translate = new StyleTranslate(new Translate(
                        line.Align switch {
                            HudPanelAlign.Centre => Length.Percent(-50f),
                            HudPanelAlign.Right => Length.Percent(-100f),
                            _ => Length.Percent(0f),
                        }, 0f)),
                },
            };
            GameFontText.Apply(label);
            return label;
        }
    }
}
