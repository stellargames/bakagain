namespace BakAgain.UI.Spells {
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.Graphics;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Spells;
    using UiTemplates = GameData.Resources.Text.UiTemplates;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The Enhanced cast screen (TASK-878): the same screen, re-placed and re-skinned by
    /// <see cref="EnhancedCastLayout"/>. The ring is a stone on a marble bench, its gems sit on RING.DAT's
    /// positions, the school's figure is a dim laser and the castable spells hover in it as holograms —
    /// the one under the pointer or being cast bright, the others dimmer — and the bench catches their
    /// light. Every rule, hit test and state stays the faithful screen's.
    /// </summary>
    public partial class CastScreen {
        private EnhancedCastLayout _enhanced;

        private bool EnhancedApplied => _enhanced?.Applied == true;

        private const string GemClass = "enhanced-cast__gem";
        private const string HoloLitClass = "enhanced-cast__holo--lit";
        private const string HoloDimClass = "enhanced-cast__holo--dim";

        /// <summary>A gem's canonical size: the art's 96 px gem canvas at the ring art's scale.</summary>
        private float GemSize => 96f / 1100f * _enhanced.RingRadius / EnhancedCastLayout.ArtChannelRadius;

        /// <summary>Applies the layout when the option is on (and takes it down when it is not). The
        /// ring is loaded first, because the bench is fitted to its circle.</summary>
        private async UniTask ApplyEnhancedAsync() {
            if (!GameOptions.IsOn(EnhancedFeature.Casting)) {
                _enhanced?.Revert();
                _enhanced = null;
                return;
            }
            VisualElement root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null || _ui == null || _resources == null) {
                return;
            }
            _ring ??= await _resources.GetOrLoadAsync<CastRing>("RING.DAT");
            if (_enhanced == null) {
                _enhanced = new EnhancedCastLayout(root, _session);
                _enhanced.CastPressed += CastChosenPower;
                _enhanced.BackPressed += BackToSpells;
            }
            var enabled = new HashSet<int>();
            foreach (BakAgain.UI.InputCore.NavWidget w in _ui.CurrentNavWidgets) enabled.Add(w.ActionId);
            VisualElement reqStage = CanonicalStage.GetOrCreate(root, _ui.Frame);
            if (!_enhanced.Apply(reqStage, _ring?.Positions, enabled, CombatCaster != null)) {
                _enhanced = null;
                return;
            }
            _wipeCover?.BringToFront(); // the layout arrives under the opening wipe, not before it
            RefreshEnhancedNav();
            // The running spell effects, as the travel HUD's strip shows them: whether a spell is
            // already up is what decides whether to cast it again.
            _effectStrip?.Dispose();
            if (_providers != null) {
                _effectStrip = new BakAgain.UI.InGame.SpellEffectCaptionView(_session, _providers, _logger);
                await _effectStrip.BuildAsync(_enhanced.SpellSheet, this);
                _enhanced?.StripBuilt();
            }
        }

        private BakAgain.UI.InGame.SpellEffectCaptionView _effectStrip;

        /// <summary>
        /// Keyboard and gamepad focus over the moved buttons where they now stand (and the tablets
        /// while they show): the REQ's own rects point at where the buttons were before the move.
        /// </summary>
        private void RefreshEnhancedNav() {
            BakAgain.UI.InputCore.MenuLayerHost host = _layerHost ??= GetComponent<BakAgain.UI.InputCore.MenuLayerHost>();
            // Update asks every frame until it takes: cheaply, until the host has a layer to take it.
            if (host == null || _ui == null || !EnhancedApplied || !host.HasNavigableLayer) return;
            _navApplied = false;
            var widgets = new List<BakAgain.UI.InputCore.NavWidget>();
            foreach (BakAgain.UI.InputCore.NavWidget w in _ui.CurrentNavWidgets) {
                VisualElement element = w.Element;
                widgets.Add(new BakAgain.UI.InputCore.NavWidget(element, w.Label, () => _enhanced?.CanonicalRectOf(element) ?? Rect.zero,
                    w.Primary, w.Secondary, w.ActionId));
            }
            if (_enhanced.TabletsShown) {
                VisualElement back = _enhanced.BackTablet, cast = _enhanced.CastTablet;
                widgets.Add(new BakAgain.UI.InputCore.NavWidget(back, UiTemplates.Format(UiTemplates.EnhancedCastBack),
                    () => _enhanced?.CanonicalRectOf(back) ?? Rect.zero, BackToSpells, null));
                widgets.Add(new BakAgain.UI.InputCore.NavWidget(cast, UiTemplates.Format(UiTemplates.EnhancedCastCast),
                    () => _enhanced?.CanonicalRectOf(cast) ?? Rect.zero, CastChosenPower, null));
            }
            // False until the host has pushed its layer, which can come after the layout is applied;
            // Update keeps asking until it takes.
            _navApplied = host.SetWidgets(widgets);
        }

        private bool _navApplied;
        private BakAgain.UI.InputCore.MenuLayerHost _layerHost;

        /// <summary>Cast: the power chosen on the ring, once one is.</summary>
        private void CastChosenPower() {
            if (_sliderSpell >= 0 && _hoveredPosition >= 0) {
                CommitPowerSelection();
            }
        }

        /// <summary>Back: drop the power choice and pick a spell again; the screen stays up.</summary>
        private void BackToSpells() {
            if (_sliderSpell < 0) return;
            AbandonPowerSelection();
            DrawRingAndNamesAsync().Forget();
        }

        /// <summary>Under the ring: the lit bench, then the ring art itself.</summary>
        private void DrawEnhancedBench(VisualElement stage) {
            Clear(stage, ChromeClass);
            VisualElement bench = _enhanced.Bench();
            bench.AddToClassList(ChromeClass);
            stage.Insert(0, bench);
            VisualElement art = _enhanced.RingArt();
            art.AddToClassList(ChromeClass);
            stage.Insert(1, art);
        }

        /// <summary>The figure as a red laser: a soft halo of stacked strokes and a thin bright core.</summary>
        private static void StrokeLaser(MeshGenerationContext context, int[] xs, int[] ys, Color ink) {
            StrokeFigure(context, xs, ys, new Color(0.85f, 0.02f, 0.0f, 0.2f * ink.a), 9f, LineCap.Round);
            StrokeFigure(context, xs, ys, new Color(1f, 0.06f, 0.02f, 0.4f * ink.a), 5f, LineCap.Round);
            StrokeFigure(context, xs, ys, new Color(1f, 0.24f, 0.14f, 0.95f * ink.a), 2.6f, LineCap.Round);
        }

        /// <summary>Drops the power choice and goes back to picking a spell, with the screen kept up and
        /// nothing reported: no spell was cast, as if it had never been picked.</summary>
        private void AbandonPowerSelection() {
            _sliderSpell = -1;
            _hoveredPosition = -1;
            _hoveredSpell = -1;
            _fillFrame = -1;
            SyncHolograms(); // the tablets, readout and lights leave with the choice, not on the redraw
        }

        /// <summary>The gem a ring position shows for the icon the faithful ring would draw there.</summary>
        private static string GemFor(int icon, int position, bool choosing) {
            bool anchor = CastRingLayout.IsAnchor(position);
            if (icon == CastRingLayout.SliderHoverIcon) return "gem-hover";
            if (icon is CastRingLayout.SliderFilledIcon or CastRingLayout.SliderFilledIcon + CastRingLayout.AnchorIconOffset) return "gem-lit";
            if (choosing) return anchor ? "gem-anchor-dim" : "gem-dim";
            return anchor ? "gem-anchor" : "gem-rest";
        }

        private VisualElement GemElement(RingPosition position, int index) {
            float size = GemSize;
            var gem = new VisualElement {
                name = $"ring_{index}",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = position.X - size / 2f,
                    top = position.Y - size / 2f,
                    width = size,
                    height = size,
                },
            };
            gem.AddToClassList(RingClass);
            gem.AddToClassList(GemClass);
            PaintGem(gem, index);
            return gem;
        }

        private void PaintGem(VisualElement gem, int index) {
            string name = GemFor(IconAt(index), index, _sliderSpell >= 0 || _fillFrame >= 0);
            if (gem.userData as string == name) return;
            gem.userData = name;
            gem.style.backgroundImage = EnhancedArt.Load(name);
        }

        /// <summary>
        /// Every castable symbol is a light over the bench; the one under the pointer, or being cast,
        /// burns brighter and the others dim. The symbols themselves stay solid.
        /// </summary>
        private void SyncHolograms() {
            if (!EnhancedApplied) return;
            VisualElement stage = Stage();
            int lit = _sliderSpell >= 0 ? _sliderSpell : _hoveredSpell;
            _lights.Clear();
            foreach ((int x, int y, int spellId) in _drawnSymbols) {
                bool on = spellId == lit;
                bool dim = lit >= 0 && !on;
                VisualElement symbol = stage.Q($"spell_symbol_{spellId}");
                symbol?.EnableInClassList(HoloLitClass, on);
                symbol?.EnableInClassList(HoloDimClass, dim);
                _lights.Add(new EnhancedCastLayout.Light(new Vector2(x, y),
                    on ? SymbolLitRadius : SymbolRadius, on ? SymbolLitStrength : dim ? SymbolDimStrength : SymbolStrength));
            }
            _enhanced.LightBench(_lights);
            SyncHintAndReadout();
        }

        /// <summary>The hint for what the pointer does now, and the power readout while one is chosen.</summary>
        private void SyncHintAndReadout() {
            // By platform, not by the pointer: IsPresent is also false before a mouse has moved.
            bool touch = Application.isMobilePlatform;
            bool choosing = _sliderSpell >= 0;
            string key = choosing
                ? touch ? UiTemplates.EnhancedCastHintPowerTouch : UiTemplates.EnhancedCastHintPower
                : touch ? UiTemplates.EnhancedCastHintPickTouch : UiTemplates.EnhancedCastHintPick;
            _enhanced.SetHint(UiTemplates.Format(key));
            _enhanced.SetCasterStats(CasterStatsText());
            bool tabletsWere = _enhanced.TabletsShown;
            _enhanced.ShowTablets(choosing, _hoveredPosition >= 0);
            if (choosing != tabletsWere) {
                RefreshEnhancedNav();
            }
            if (!choosing) {
                _enhanced.SetReadout(null, null);
                return;
            }
            // Only while the pointer is on an affordable gem: off the ring there is no power to show.
            int power = CastRingLayout.PreviewPower(_hoveredPosition);
            if (power <= 0) {
                _enhanced.SetReadout(null, null);
                return;
            }
            _enhanced.SetReadout(power.ToString(), UiTemplates.Format(UiTemplates.EnhancedCastPowerOf, ("max", _maximumPower)));
        }

        /// <summary>The caster's health and stamina, current of max: what a cast is paid from. In a fight
        /// the current values are the combatant's own.</summary>
        private string CasterStatsText() {
            GameData.Resources.Character.ActorStat[] stats = _session?.StatsOf(CasterId);
            if (stats == null) return null;
            int healthMax = stats[(int)GameData.ActorAttribute.Health].Max;
            int staminaMax = stats[(int)GameData.ActorAttribute.Stamina].Max;
            int health = CombatCaster?.Health ?? _session.PeekEffectiveStat(CasterId, GameData.ActorAttribute.Health);
            int stamina = CombatCaster?.Stamina ?? _session.PeekEffectiveStat(CasterId, GameData.ActorAttribute.Stamina);
            return UiTemplates.Format(UiTemplates.EnhancedCastPool,
                ("health", UiTemplates.Format(UiTemplates.EnhancedCastHealth, ("current", health), ("max", healthMax))),
                ("stamina", UiTemplates.Format(UiTemplates.EnhancedCastStamina, ("current", stamina), ("max", staminaMax))));
        }

        private readonly List<EnhancedCastLayout.Light> _lights = new();
        private const float SymbolRadius = 120f, SymbolLitRadius = 160f;
        private const float SymbolStrength = 0.55f, SymbolLitStrength = 1.1f, SymbolDimStrength = 0.25f;
    }
}
