namespace BakAgain.UI.InGame {
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Enhanced HUD (spec 2026-10-08 §3): a stamina-then-health arc behind a moved portrait, and one
    /// dot on its rim per active condition. Values from the same reader the camp table uses.
    /// </summary>
    public sealed class PortraitRingView {
        private static readonly Color StaminaColour = new Color32(212, 160, 42, 255);
        private static readonly Color HealthColour = new Color32(184, 50, 42, 255);
        private static readonly Color EmptyColour = new Color(0, 0, 0, 0.5f);

        private readonly GameSession _session;
        private readonly int _slot;
        private readonly VisualElement _ring;
        private readonly VisualElement _dots;
        private (double S, double H) _fractions;
        private int _conditionMask = -1;

        public PortraitRingView(GameSession session, VisualElement head, int slot) {
            _session = session;
            _slot = slot;
            _ring = new VisualElement { pickingMode = PickingMode.Ignore };
            _ring.AddToClassList("enhanced-hud__ring");
            _ring.generateVisualContent += Draw;
            _dots = new VisualElement { pickingMode = PickingMode.Ignore };
            _dots.AddToClassList("enhanced-hud__ring");
            head.Insert(0, _ring);
            head.Add(_dots);
        }

        public void Remove() { _ring.RemoveFromHierarchy(); _dots.RemoveFromHierarchy(); }

        public void Refresh() {
            byte[] roster = _session.ActivePartyIndices;
            bool present = _slot < roster.Length;
            _ring.visible = _dots.visible = present;
            if (!present) return;
            int id = roster[_slot];
            (double S, double H) f = PortraitRing.Fractions(
                // Peek, not EffectiveStat: the faithful HUD makes no such read, and EffectiveStat
                // writes the cached-effective byte the save carries.
                _session.PeekEffectiveStat(id, ActorAttribute.Stamina),
                _session.PeekEffectiveStat(id, ActorAttribute.Health),
                _session.EffectivePoolMax(id));
            if (f != _fractions) { _fractions = f; _ring.MarkDirtyRepaint(); }
            RebuildDots(_session.ConditionsOf(id));
        }

        private void RebuildDots(ActorConditions conditions) {
            int mask = 0;
            for (int c = 0; c < ActorConditions.Count; c++) {
                if (conditions != null && conditions.Has((ActorCondition)c)) mask |= 1 << c;
            }
            if (mask == _conditionMask) return;
            _conditionMask = mask;
            _dots.Clear();
            int i = 0;
            for (int c = 0; c < ActorConditions.Count; c++) {
                if ((mask & (1 << c)) == 0) continue;
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("enhanced-hud__dot");
                dot.AddToClassList($"enhanced-hud__dot--{(ActorCondition)c}");
                // On the rim, clockwise from the bottom: proportions of the portrait's own box.
                float a = Mathf.PI * (0.5f + 0.18f * i++);
                dot.style.left = Length.Percent(50 + 46 * Mathf.Cos(a) - 9);
                dot.style.top = Length.Percent(50 + 46 * Mathf.Sin(a) - 9);
                _dots.Add(dot);
            }
        }

        private void Draw(MeshGenerationContext ctx) {
            Rect r = _ring.contentRect;
            Vector2 c = r.center;
            float radius = Mathf.Min(r.width, r.height) * 0.5f;
            if (radius <= 0f) return;
            Painter2D p = ctx.painter2D;
            p.lineWidth = radius * 0.12f;
            Arc(p, c, radius, 0, 1, EmptyColour);
            Arc(p, c, radius, 0, (float)_fractions.S, StaminaColour);
            Arc(p, c, radius, (float)_fractions.S, (float)(_fractions.S + _fractions.H), HealthColour);
        }

        private static void Arc(Painter2D p, Vector2 c, float radius, float from, float to, Color colour) {
            if (to <= from) return;
            p.strokeColor = colour;
            p.BeginPath();
            p.Arc(c, radius - p.lineWidth / 2, Angle.Degrees(-90 + 360 * from), Angle.Degrees(-90 + 360 * to));
            p.Stroke();
        }
    }
}
