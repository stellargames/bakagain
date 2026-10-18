namespace BakAgain.UI.Spells {
    using System.Collections.Generic;
    using BakAgain.Core;
    using GameData.Resources.Spells;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The Enhanced cast screen's full-window layout (TASK-878): the ring a stone on a marble bench at
    /// the left with the school stones under it, the spell text on parchment beside it, the casters
    /// in stone sockets at the right and Exit at the bottom — the original's materials in HD.
    /// </summary>
    /// <remarks>
    /// <para><b>Two sheets.</b> The faithful screen draws everything in the REQ's canonical frame and
    /// hit-tests the ring there. <see cref="RingSheet"/> is a canonical-sized surface scaled so the
    /// ring fills the bench; the screen draws the ring, its gems, the figure and the symbols on it,
    /// and its hit tests convert the pointer through the sheet's transform. <see cref="InfoSheet"/>
    /// is a second one scaled so the faithful info panel's text fills the parchment.</para>
    /// <para><b>The chrome</b> is the REQ's own elements, moved (the inventory's pattern), so every
    /// click still reaches <c>PrimaryAction</c> by its action id. Placement is in the theme
    /// (<c>.enhanced-cast*</c>); the only geometry here is fitting the two sheets.</para>
    /// </remarks>
    public sealed class EnhancedCastLayout {
        public const string ArtFolder = "Enhanced/Cast/";
        public const string CasterClass = "enhanced-cast__portrait--caster";
        public const string ShownClass = "enhanced-cast__school--shown";
        public const string OffClass = "enhanced-cast__school--off";

        /// <summary>The ring art's gem channel radius, as a fraction of its width (pack_cast.py: r 425 of 1100).</summary>
        public const float ArtChannelRadius = 425f / 1100f;

        /// <summary>The ring art's inner edge, as a fraction of its width (its disc is cut at r 341.5).</summary>
        public const float ArtInnerRadius = 341.5f / 1100f;

        /// <summary>A little bench beyond the ring art's own square (which holds the ring and its shadow).</summary>
        private const float BenchMargin = 1.04f;

        private const string ExitName = "imagebutton_1";
        private const string FigureName = "enhanced-cast-figure";

        private readonly VisualElement _root;
        private readonly GameSession _session;
        private readonly MovedElements _moved = new();
        private readonly List<VisualElement> _added = new();
        private VisualElement _layout, _left, _well, _parch, _reqStage;
        private readonly VisualElement[] _portraits = new VisualElement[3];
        private readonly Label[] _names = new Label[3];
        private readonly VisualElement[] _schools = new VisualElement[CastRingLayout.CategoryCount];
        private Vector2 _ringCentre;
        private float _ringRadius;
        private bool _warned;

        public EnhancedCastLayout(VisualElement root, GameSession session) {
            _root = root;
            _session = session;
        }

        public bool Applied => _layout != null;

        /// <summary>RING.DAT's circle radius, canonical: what the gems and the ring art are sized from.</summary>
        public float RingRadius => _ringRadius;

        /// <summary>The canonical surface the ring is drawn and hit-tested on while applied.</summary>
        public VisualElement RingSheet { get; private set; }

        /// <summary>The canonical surface the info panel's lines are drawn on while applied.</summary>
        public VisualElement InfoSheet { get; private set; }

        /// <summary>
        /// Moves the chrome and shows the layout. False, with nothing moved, while a REQ element is
        /// missing (the REQ builds asynchronously); warns once.
        /// </summary>
        /// <param name="reqStage">The REQ's own stage (CAST.SCX and the HUD strip), hidden while applied.</param>
        /// <param name="ring">RING.DAT's positions: the bench is fitted to their circle.</param>
        /// <param name="enabledActions">The REQ entries that are live; a school whose button the layout
        /// disables shows a blank stone, as the faithful one does.</param>
        public bool Apply(VisualElement reqStage, IReadOnlyList<RingPosition> ring, ISet<int> enabledActions, bool combat) {
            if (Applied && _layout.panel == null) {
                Revert(); // the REQ rebuilt the root under us; apply to the new elements
            }
            if (Applied) {
                return true;
            }
            if (ring == null || ring.Count == 0) {
                return false;
            }
            var names = new List<string> { ExitName };
            for (int i = 0; i < CastRingLayout.CategoryCount; i++) names.Add(SchoolName(i));
            if (!combat) {
                for (int i = 0; i < 3; i++) names.Add(PortraitName(i));
            }
            foreach (string n in names) {
                if (_root.Q(n) == null) {
                    if (!_warned) {
                        Debug.LogWarning($"EnhancedCast: REQ element '{n}' missing; keeping the faithful layout.");
                        _warned = true;
                    }
                    return false;
                }
            }
            (_ringCentre, _ringRadius) = Circle(ring);

            _layout = MovedElements.Box("enhanced-cast");
            _layout.name = "enhanced-cast";
            _layout.style.backgroundImage = Art("slab");
            _left = MovedElements.Box("enhanced-cast__left");
            _well = MovedElements.Box("enhanced-cast__well");
            _well.style.backgroundImage = Art("marble");
            RingSheet = Sheet("enhanced-cast-ring");
            _well.Add(RingSheet);
            VisualElement schools = MovedElements.Box("enhanced-cast__schools");
            _left.Add(_well);
            _left.Add(schools);

            VisualElement right = MovedElements.Box("enhanced-cast__right");
            VisualElement top = MovedElements.Box("enhanced-cast__top");
            _parch = MovedElements.Box("enhanced-cast__parch");
            _parch.style.backgroundImage = Art("parchment");
            InfoSheet = Sheet("enhanced-cast-info");
            _parch.Add(InfoSheet);
            VisualElement column = MovedElements.Box("enhanced-cast__portraits");
            top.Add(_parch);
            top.Add(column);
            VisualElement bar = MovedElements.Box("enhanced-cast__bar");
            right.Add(top);
            right.Add(bar);
            _layout.Add(_left);
            _layout.Add(right);
            _root.Add(_layout);

            for (int i = 0; i < CastRingLayout.CategoryCount; i++) {
                VisualElement button = _root.Q(SchoolName(i));
                _moved.Move(button, schools, "enhanced-cast__school", clearSize: true);
                _schools[i] = button;
                Stone(button, i);
                button.EnableInClassList(OffClass, !enabledActions.Contains(CastMenuSelection.FirstSchoolActionId + i));
            }

            if (combat) {
                column.style.display = DisplayStyle.None; // the caster is fixed in a fight; no column yet
            } else {
                for (int i = 0; i < 3; i++) {
                    VisualElement wrap = MovedElements.Box("enhanced-cast__portrait");
                    wrap.style.backgroundImage = Art("socket");
                    column.Add(wrap);
                    _portraits[i] = wrap;
                    _moved.Move(_root.Q(PortraitName(i)), wrap, "enhanced-cast__head", clearSize: true);
                    _names[i] = new Label { pickingMode = PickingMode.Ignore };
                    _names[i].AddToClassList("enhanced-cast__name");
                    wrap.Add(_names[i]);
                }
            }

            var hint = new Label { pickingMode = PickingMode.Ignore };
            hint.AddToClassList("enhanced-cast__hint");
            bar.Add(hint);
            VisualElement exit = _root.Q(ExitName);
            _moved.Move(exit, bar, "enhanced-cast__exit", clearSize: true);
            Stone(exit, -1);

            _reqStage = reqStage;
            if (_reqStage != null) {
                _reqStage.style.display = DisplayStyle.None;
            }
            _layout.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            _well.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            _parch.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            RefreshNames();
            return true;
        }

        /// <summary>Puts every moved element back with its REQ placement. Tolerates a root that was
        /// cleared underneath (the REQ loader clears it when the screen goes away).</summary>
        public void Revert() {
            if (!Applied) {
                return;
            }
            foreach (VisualElement e in _added) e.RemoveFromHierarchy();
            _added.Clear();
            foreach (VisualElement s in _schools) {
                if (s == null) continue;
                s.style.unityBackgroundImageTintColor = StyleKeyword.Null;
                s.RemoveFromClassList(OffClass);
                s.RemoveFromClassList(ShownClass);
            }
            VisualElement exit = _root.Q(ExitName);
            if (exit != null) exit.style.unityBackgroundImageTintColor = StyleKeyword.Null;
            _moved.RestoreAll();
            if (_reqStage != null) {
                _reqStage.style.display = StyleKeyword.Null;
            }
            ReleaseBench();
            _layout.RemoveFromHierarchy();
            _layout = _left = _well = _parch = _reqStage = null;
            RingSheet = InfoSheet = null;
            System.Array.Clear(_portraits, 0, 3);
            System.Array.Clear(_names, 0, 3);
            System.Array.Clear(_schools, 0, _schools.Length);
        }

        /// <summary>Lights the school whose figure the ring shows.</summary>
        public void ShowSchool(int school) {
            for (int i = 0; i < _schools.Length; i++) {
                _schools[i]?.EnableInClassList(ShownClass, i == school);
                _schools[i]?.Q(FigureName)?.MarkDirtyRepaint();
            }
        }

        /// <summary>Rings the caster's portrait, as the faithful highlight does.</summary>
        public void ShowCaster(int slot) {
            for (int i = 0; i < 3; i++) {
                _portraits[i]?.EnableInClassList(CasterClass, i == slot);
            }
        }

        public void RefreshNames() {
            IReadOnlyList<string> names = _session?.ActivePartyMemberNames;
            for (int i = 0; i < 3; i++) {
                if (_names[i] == null) continue;
                string name = names != null && i < names.Count ? names[i] : string.Empty;
                _names[i].text = name;
                _portraits[i].style.display = string.IsNullOrEmpty(name) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        /// <summary>The ring art, centred on RING.DAT's circle and sized so its gem channel runs through
        /// the positions; drawn by the screen at the bottom of <see cref="RingSheet"/>.</summary>
        public VisualElement RingArt() {
            float size = _ringRadius / ArtChannelRadius;
            var art = new VisualElement {
                name = "enhanced_cast_ring_art",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = _ringCentre.x - size / 2f,
                    top = _ringCentre.y - size / 2f,
                    width = size,
                    height = size,
                    backgroundImage = Art("ring"),
                },
            };
            return art;
        }

        /// <summary>A light over the bench, in the ring sheet's canonical units.</summary>
        public readonly struct Light {
            public readonly Vector2 Position;
            public readonly float Radius, Strength;
            public Light(Vector2 position, float radius, float strength) { Position = position; Radius = radius; Strength = strength; }
        }

        /// <summary>The colour the spell symbols' light has: their own red, warmed.</summary>
        private static readonly Color SymbolLight = new(1f, 0.42f, 0.22f);

        /// <summary>How far a full-strength light lifts the marble's colour (linear).</summary>
        private const float LightGain = 5f;

        private const int BenchTexels = 768;
        private const int MaxLights = 16;

        /// <summary>The marble under the ring, lit; it covers the part of the sheet the bench shows.</summary>
        public VisualElement Bench() {
            Rect r = BenchRegion();
            _bench = new VisualElement {
                name = "enhanced_cast_bench",
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = r.x, top = r.y, width = r.width, height = r.height },
            };
            Relight();
            return _bench;
        }

        /// <summary>Re-lights the bench for these lights; kept for when the bench is re-fitted.</summary>
        public void LightBench(IReadOnlyList<Light> lights) {
            _lights.Clear();
            for (int i = 0; i < lights.Count && i < MaxLights; i++) _lights.Add(lights[i]);
            Relight();
        }

        private readonly List<Light> _lights = new();
        private readonly Vector4[] _lightPos = new Vector4[MaxLights];
        private readonly Vector4[] _lightColor = new Vector4[MaxLights];
        private VisualElement _bench;
        private RenderTexture _benchTexture;
        private Material _benchMaterial;
        private Texture2D _marble;

        private void Relight() {
            if (_bench == null) return;
            if (_benchMaterial == null) {
                Shader shader = Shader.Find("BakAgain/CastBenchLight");
                _marble = Resources.Load<Texture2D>(ArtFolder + "marble");
                if (shader == null || _marble == null) {
                    _bench.style.backgroundImage = Art("marble"); // unlit, but still the bench
                    return;
                }
                _benchMaterial = new Material(shader);
                _benchTexture = new RenderTexture(BenchTexels, BenchTexels, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                _bench.style.backgroundImage = Background.FromRenderTexture(_benchTexture);
            }
            Rect r = BenchRegion();
            // The marble at the well's own scale (the theme tiles it 720x480 panel units), so the lit
            // bench and the well around it are one surface.
            float scale = RingSheet != null ? RingSheet.resolvedStyle.scale.value.x : 1f;
            if (scale <= 0f || float.IsNaN(scale)) scale = 1f;
            _benchMaterial.SetVector("_Tile", new Vector4(r.width * scale / 720f, r.height * scale / 480f, 0f, 0f));
            _benchMaterial.SetFloat("_Gain", LightGain);
            Color c = SymbolLight.linear;
            for (int i = 0; i < _lights.Count; i++) {
                Light l = _lights[i];
                _lightPos[i] = new Vector4((l.Position.x - r.x) / r.width, (l.Position.y - r.y) / r.height, l.Radius / r.width, l.Strength);
                _lightColor[i] = new Vector4(c.r, c.g, c.b, 1f);
            }
            float art = _ringRadius / ArtChannelRadius;
            _benchMaterial.SetVector("_Ring", new Vector4((_ringCentre.x - r.x) / r.width, (_ringCentre.y - r.y) / r.height,
                art * ArtInnerRadius / r.width, art * (ArtInnerRadius + 0.03f) / r.width));
            _benchMaterial.SetInt("_LightCount", _lights.Count);
            _benchMaterial.SetVectorArray("_LightPos", _lightPos);
            _benchMaterial.SetVectorArray("_LightColor", _lightColor);
            Graphics.Blit(_marble, _benchTexture, _benchMaterial);
            _bench.MarkDirtyRepaint();
        }

        private void ReleaseBench() {
            if (_benchTexture != null) { _benchTexture.Release(); Object.Destroy(_benchTexture); }
            if (_benchMaterial != null) Object.Destroy(_benchMaterial);
            _benchTexture = null;
            _benchMaterial = null;
            _bench = null;
            _lights.Clear();
        }

        /// <summary>The square of the ring sheet the well shows: the ring art's square and a little more.</summary>
        private Rect BenchRegion() {
            float reach = _ringRadius / ArtChannelRadius / 2f * BenchMargin;
            return new Rect(_ringCentre.x - reach, _ringCentre.y - reach, 2f * reach, 2f * reach);
        }

        public static StyleBackground Art(string name) {
            Texture2D t = Resources.Load<Texture2D>(ArtFolder + name);
            return t != null ? new StyleBackground(t) : new StyleBackground(StyleKeyword.None);
        }

        private static string SchoolName(int school) => $"imagebutton_{CastMenuSelection.FirstSchoolActionId + school}";

        private static string PortraitName(int slot) => $"hotspot_{CastMenuSelection.FirstPartySlotActionId + slot}";

        private static VisualElement Sheet(string name) {
            var sheet = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            sheet.AddToClassList("enhanced-cast__sheet");
            return sheet;
        }

        /// <summary>The faithful face hidden (its icon loader keeps re-applying it, so it is tinted
        /// out, not replaced) and a stone disc with the school's figure — or the Exit word — over it.</summary>
        private void Stone(VisualElement button, int school) {
            button.style.unityBackgroundImageTintColor = Color.clear;
            var stone = new VisualElement { name = FigureName, pickingMode = PickingMode.Ignore };
            stone.AddToClassList("enhanced-cast__stone");
            stone.style.backgroundImage = Art("disc");
            if (school >= 0) {
                stone.generateVisualContent += ctx => DrawFigure(ctx, stone, school, button.ClassListContains(ShownClass));
            } else {
                var word = new Label(GameData.Resources.Text.UiTemplates.Format(GameData.Resources.Text.UiTemplates.EnhancedCastExit)) {
                    pickingMode = PickingMode.Ignore,
                };
                word.AddToClassList("enhanced-cast__exit-word");
                stone.Add(word);
            }
            button.Add(stone);
            _added.Add(stone);
        }

        /// <summary>The school's ring figure, engraved in the stone; lit copper while it is the one shown.</summary>
        private static void DrawFigure(MeshGenerationContext ctx, VisualElement stone, int school, bool shown) {
            if (!CastRingSigil.Has(school)) return;
            Rect box = stone.contentRect;
            int[] xs = CastRingSigil.VertexX[school], ys = CastRingSigil.VertexY[school];
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < xs.Length; i++) {
                (float x, float y) = CastRingSigil.ToCanonical(xs[i], ys[i]);
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            float scale = 0.56f * Mathf.Min(box.width / (maxX - minX), box.height / (maxY - minY));
            Vector2 offset = box.center - new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f) * scale;
            Vector2 P(int i) {
                (float x, float y) = CastRingSigil.ToCanonical(xs[i], ys[i]);
                return new Vector2(x, y) * scale + offset;
            }
            float w = box.width * 0.045f;
            void Stroke(Vector2 shift, Color ink, float width) {
                Painter2D p = ctx.painter2D;
                p.strokeColor = ink;
                p.lineWidth = width;
                p.lineJoin = LineJoin.Round;
                p.lineCap = LineCap.Round;
                foreach (int[] e in CastRingSigil.Edges) {
                    p.BeginPath();
                    p.MoveTo(P(e[0]) + shift);
                    p.LineTo(P(e[1]) + shift);
                    p.Stroke();
                }
            }
            if (shown) {
                Stroke(Vector2.zero, new Color(0.94f, 0.55f, 0.32f, 0.35f), w * 3.2f);
                Stroke(Vector2.zero, new Color(0.94f, 0.64f, 0.42f, 1f), w);
            } else {
                Stroke(new Vector2(0, w * 0.35f), new Color(0.96f, 0.88f, 0.72f, 0.45f), w);  // the lit lower lip
                Stroke(Vector2.zero, new Color(0.11f, 0.08f, 0.04f, 0.92f), w);
            }
        }

        private static (Vector2 Centre, float Radius) Circle(IReadOnlyList<RingPosition> ring) {
            Vector2 c = Vector2.zero;
            foreach (RingPosition p in ring) c += new Vector2(p.X, p.Y);
            c /= ring.Count;
            float r = 0f;
            foreach (RingPosition p in ring) r += Vector2.Distance(c, new Vector2(p.X, p.Y));
            return (c, r / ring.Count);
        }

        private void Fit() {
            if (_layout == null) return;
            // The bench is square: the left column is as wide as the well is tall.
            float h = _well.layout.height;
            if (!float.IsNaN(h) && h > 0f && Mathf.Abs(_left.resolvedStyle.width - h) > 0.5f) {
                _left.style.width = h;
            }
            FitSheet(RingSheet, _well.contentRect, BenchRegion());
            Relight(); // the marble's scale follows the fit
            FitSheet(InfoSheet, _parch.contentRect, InfoRect());
        }

        /// <summary>What the faithful panel's text covers, from the panel's own constants: the title's box
        /// and the body column, down to the caster's health line.</summary>
        private static Rect InfoRect() {
            float left = Mathf.Min(SpellInfoPanel.BodyX, SpellInfoPanel.TitleCentreX - CastScreen.InfoTitleBoxWidth / 2f);
            float right = Mathf.Max(SpellInfoPanel.BodyRight, SpellInfoPanel.TitleCentreX + CastScreen.InfoTitleBoxWidth / 2f);
            float bottom = SpellInfoPanel.HealthStaminaY + SpellInfoPanel.BodyLineStep;
            return Rect.MinMaxRect(left, SpellInfoPanel.TitleY, right, bottom);
        }

        private static void FitSheet(VisualElement sheet, Rect field, Rect region) {
            if (sheet == null || field.width <= 0f || field.height <= 0f || float.IsNaN(field.width)) {
                return;
            }
            (float scale, float x, float y) = GameData.Resources.Layout.FieldFit.Fit(
                region.x, region.y, region.width, region.height, field.x, field.y, field.width, field.height);
            sheet.style.translate = new Translate(x, y);
            sheet.style.scale = new Scale(new Vector2(scale, scale));
        }
    }
}
