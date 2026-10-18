namespace BakAgain.UI.Spells {
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.Graphics;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Spells;
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
        private const string PoolClass = "enhanced-cast__pool";
        private const string HoloClass = "enhanced-cast__holo";
        private const string HoloLitClass = "enhanced-cast__holo--lit";
        private const string HoloDimClass = "enhanced-cast__holo--dim";

        /// <summary>A gem's canonical size: the art's 96 px gem canvas at the ring art's scale.</summary>
        private float GemSize => 96f * _enhanced.RingRadius / 425f;

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
            _enhanced ??= new EnhancedCastLayout(root, _session);
            var enabled = new HashSet<int>();
            foreach (BakAgain.UI.InputCore.NavWidget w in _ui.CurrentNavWidgets) enabled.Add(w.ActionId);
            VisualElement reqStage = CanonicalStage.GetOrCreate(root, _ui.Frame);
            if (!_enhanced.Apply(reqStage, _ring?.Positions, enabled, CombatCaster != null)) {
                _enhanced = null;
            }
        }

        /// <summary>Under the ring: the light the figure throws on the bench, the pools under the
        /// symbols go in the same layer as they are drawn. Then the ring art itself.</summary>
        private void DrawEnhancedBench(VisualElement stage) {
            Clear(stage, ChromeClass);
            // The light each line of the figure throws on the bench: soft glow discs strung along it.
            if (CastRingSigil.Has(_school)) {
                int[] xs = CastRingSigil.VertexX[_school], ys = CastRingSigil.VertexY[_school];
                foreach (int[] edge in CastRingSigil.Edges) {
                    (float x0, float y0) = CastRingSigil.ToCanonical(xs[edge[0]], ys[edge[0]]);
                    (float x1, float y1) = CastRingSigil.ToCanonical(xs[edge[1]], ys[edge[1]]);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1)) / LineLightSpacing));
                    for (int i = 0; i <= steps; i++) {
                        float t = i / (float)steps;
                        VisualElement glow = EnhancedCastLayout.Glow(Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), LineLightSize, LineLightClass);
                        glow.AddToClassList(ChromeClass);
                        stage.Insert(0, glow);
                    }
                }
            }
            VisualElement art = _enhanced.RingArt();
            art.AddToClassList(ChromeClass);
            stage.Insert(stage.Query(className: LineLightClass).ToList().Count, art);
        }

        private const string LineLightClass = "enhanced-cast__line-light";
        private const float LineLightSpacing = 34f;
        private const float LineLightSize = 120f;

        /// <summary>The figure as a laser: a soft halo of stacked strokes and a thin bright core.</summary>
        private static void StrokeLaser(MeshGenerationContext context, int[] xs, int[] ys, Color ink) {
            StrokeFigure(context, xs, ys, new Color(1f, 0.3f, 0.18f, 0.16f * ink.a), 9f);
            StrokeFigure(context, xs, ys, new Color(1f, 0.36f, 0.24f, 0.3f * ink.a), 5f);
            StrokeFigure(context, xs, ys, new Color(1f, 0.62f, 0.5f, 0.85f * ink.a), 2.6f);
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
            gem.style.backgroundImage = EnhancedCastLayout.Art(name);
        }

        /// <summary>A symbol as a hologram over the bench, with the pool of light it throws.</summary>
        private void AddHologram(VisualElement stage, VisualElement symbol, int x, int y, int spellId) {
            symbol.AddToClassList(HoloClass);
            VisualElement pool = EnhancedCastLayout.Glow(x, y, 150f, PoolClass);
            pool.name = $"enhanced_pool_{spellId}";
            pool.AddToClassList(SymbolClass); // cleared with the symbols
            stage.Insert(Mathf.Max(0, stage.IndexOf(stage.Q("enhanced_cast_ring_art"))), pool); // on the bench, under the ring art
        }

        /// <summary>The spell under the pointer, or the one being cast, glows; the others dim.</summary>
        private void SyncHolograms() {
            if (!EnhancedApplied) return;
            VisualElement stage = Stage();
            int lit = _sliderSpell >= 0 ? _sliderSpell : _hoveredSpell;
            foreach ((int _, int _, int spellId) in _drawnSymbols) {
                bool on = spellId == lit;
                VisualElement symbol = stage.Q($"spell_symbol_{spellId}");
                symbol?.EnableInClassList(HoloLitClass, on);
                symbol?.EnableInClassList(HoloDimClass, lit >= 0 && !on);
                VisualElement pool = stage.Q($"enhanced_pool_{spellId}");
                pool?.EnableInClassList(HoloLitClass, on);
                pool?.EnableInClassList(HoloDimClass, lit >= 0 && !on);
            }
        }
    }
}
