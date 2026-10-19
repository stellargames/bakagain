namespace BakAgain.UI.Spells {
    using System.Collections.Generic;
    using BakAgain.Core;
    using GameData.Resources.Spells;
    using UiTemplates = GameData.Resources.Text.UiTemplates;
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

        private readonly VisualElement _root;
        private readonly GameSession _session;
        private readonly MovedElements _moved = new();
        private VisualElement _layout, _left, _well, _parch, _reqStage;
        private readonly VisualElement[] _portraits = new VisualElement[3];
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
            for (int i = 0; i < 3; i++) names.Add(PortraitName(i));
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
            _layout.style.backgroundImage = EnhancedArt.Load("marble"); // dark stone, edge to edge
            _left = MovedElements.Box("enhanced-cast__left");
            _well = MovedElements.Box("enhanced-cast__well");
            _well.style.backgroundImage = EnhancedArt.Load("marble");
            RingSheet = Sheet("enhanced-cast-ring");
            _well.Add(RingSheet);
            VisualElement schools = MovedElements.Box("enhanced-cast__schools");
            // The bench and the parchment are each set in a rounded slab of light stone; the rest of
            // the screen is the dark stone they are strapped onto.
            _wellFrame = Frame(_well);
            _left.Add(_wellFrame);
            _left.Add(schools);

            VisualElement right = MovedElements.Box("enhanced-cast__right");
            VisualElement top = MovedElements.Box("enhanced-cast__top");
            _parch = MovedElements.Box("enhanced-cast__parch");
            _parch.style.backgroundImage = EnhancedArt.Load("parchment");
            InfoSheet = Sheet("enhanced-cast-info");
            _parch.Add(InfoSheet);
            VisualElement column = MovedElements.Box("enhanced-cast__portraits");
            _parchFrame = Frame(_parch);
            top.Add(_parchFrame);
            // The running spells head the parchment, set into its frame.
            _strip = MovedElements.Box("enhanced-cast__strip");
            SpellSheet = Sheet("enhanced-cast-spells");
            _strip.Add(SpellSheet);
            _parchFrame.Insert(0, _strip);
            top.Add(column);
            VisualElement bar = MovedElements.Box("enhanced-cast__bar");
            _column = column;
            _bar = bar;
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

            // In a fight the caster is the combatant whose turn it is: the column shows only them, and
            // their portrait does not take clicks (the faithful screen draws no portraits there at all).
            _combat = combat;
            for (int i = 0; i < 3; i++) {
                VisualElement wrap = MovedElements.Box("enhanced-cast__portrait");
                wrap.style.backgroundImage = EnhancedArt.Load("socket");
                column.Add(wrap);
                _portraits[i] = wrap;
                VisualElement head = _root.Q(PortraitName(i));
                _moved.Move(head, wrap, "enhanced-cast__head", clearSize: true);
                if (combat) head.pickingMode = PickingMode.Ignore;
                _stats[i] = new Label { pickingMode = PickingMode.Ignore };
                _stats[i].AddToClassList("enhanced-cast__stats");
                wrap.Add(_stats[i]);
            }

            _hint = new Label { pickingMode = PickingMode.Ignore };
            _hint.AddToClassList("enhanced-cast__hint");
            bar.Add(_hint);
            CastTablet = Tablet(UiTemplates.EnhancedCastCast, "enhanced-cast__tablet--cast", () => CastPressed?.Invoke());
            BackTablet = Tablet(UiTemplates.EnhancedCastBack, null, () => BackPressed?.Invoke());
            bar.Add(BackTablet);
            bar.Add(CastTablet);
            ShowTablets(false, false);
            _readout = MovedElements.Box("enhanced-cast__readout");
            _readoutPower = new Label { pickingMode = PickingMode.Ignore };
            _readoutPower.AddToClassList("enhanced-cast__readout-power");
            _readoutOf = new Label { pickingMode = PickingMode.Ignore };
            _readoutOf.AddToClassList("enhanced-cast__readout-of");
            _readout.Add(_readoutPower);
            _readout.Add(_readoutOf);
            _well.Add(_readout);
            SetReadout(null, null);
            VisualElement exit = _root.Q(ExitName);
            _moved.Move(exit, bar, "enhanced-cast__exit", clearSize: true);
            Stone(exit, -1);

            // Hidden, not removed from layout: its canonical frame is what the cursor and keyboard focus
            // map through (CanonicalRectOf), so it has to keep its place and size.
            _reqStage = reqStage;
            if (_reqStage != null) {
                _reqStage.style.visibility = Visibility.Hidden;
            }
            // Five elements report one layout pass: fit once for all of them, not five times.
            foreach (VisualElement e in new[] { _layout, _well, _parch, _column, _strip }) {
                e.RegisterCallback<GeometryChangedEvent>(_ => ScheduleFit());
            }
            RefreshNames();
            _rings = new BakAgain.UI.InGame.PortraitRingSet(_session, 3, i => _root.Q(PortraitName(i)));
            _rings.Refresh();
            return true;
        }

        /// <summary>Puts every moved element back with its REQ placement. Tolerates a root that was
        /// cleared underneath (the REQ loader clears it when the screen goes away).</summary>
        public void Revert() {
            if (!Applied) {
                return;
            }
            foreach (VisualElement s in _schools) {
                if (s == null) continue;
                StoneButton.Undress(s);
                s.RemoveFromClassList(OffClass);
                s.RemoveFromClassList(ShownClass);
            }
            VisualElement exit = _root.Q(ExitName);
            StoneButton.Undress(exit);
            _moved.RestoreAll();
            if (_reqStage != null) {
                _reqStage.style.visibility = StyleKeyword.Null;
            }
            _pendingFit?.Pause();
            _pendingFit = null;
            _rings?.Remove();
            _rings = null;
            ReleaseBench();
            _frame?.Release();
            _frame = null;
            _layout.RemoveFromHierarchy();
            _layout = _left = _well = _parch = _reqStage = null;
            _wellFrame = _parchFrame = _strip = null;
            SpellSheet = null;
            RingSheet = InfoSheet = null;
            _hint = _readoutPower = _readoutOf = null;
            CastTablet = BackTablet = null;
            System.Array.Clear(_stats, 0, 3);
            _readout = null;
            System.Array.Clear(_portraits, 0, 3);
            System.Array.Clear(_schools, 0, _schools.Length);
        }

        /// <summary>Lights the school whose figure the ring shows.</summary>
        public void ShowSchool(int school) {
            for (int i = 0; i < _schools.Length; i++) {
                _schools[i]?.EnableInClassList(ShownClass, i == school);
                _schools[i]?.Q(StoneButton.DiscName)?.MarkDirtyRepaint();
            }
        }

        /// <summary>Rings the caster's portrait, as the faithful highlight does; in a fight the caster is
        /// the only one shown.</summary>
        public void ShowCaster(int slot) {
            _casterSlot = slot;
            for (int i = 0; i < 3; i++) {
                _portraits[i]?.EnableInClassList(CasterClass, i == slot);
            }
            RefreshNames();
            SetCasterStats(_statsText);
        }

        /// <summary>The Cast and Back tablets, shown while a power is chosen; Cast is lit once a power is.</summary>
        public VisualElement CastTablet { get; private set; }

        public VisualElement BackTablet { get; private set; }

        public event System.Action CastPressed, BackPressed;

        public void ShowTablets(bool shown, bool castReady) {
            if (CastTablet == null) return;
            CastTablet.style.display = BackTablet.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            CastTablet.EnableInClassList("enhanced-cast__tablet--off", !castReady);
            _tabletsShown = shown;
            FitHint();
        }

        /// <summary>Whether Back and Cast show: the screen's keyboard focus follows them.</summary>
        public bool TabletsShown => _tabletsShown;

        private bool _tabletsShown;

        /// <summary>Below this bar width the tablets need the hint's room (a 4:3 window); the gems and the
        /// readout still say what to do.</summary>
        private const float HintRoomWithTablets = 1000f;

        private void FitHint() {
            if (_hint == null || _bar == null) return;
            float w = _bar.contentRect.width;
            bool crowded = _tabletsShown && !float.IsNaN(w) && w < HintRoomWithTablets;
            _hint.style.display = crowded ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>The caster's health and stamina under their portrait; the others show none.</summary>
        public void SetCasterStats(string text) {
            _statsText = text;
            _rings?.Refresh(); // the same moment the numbers change: a cast was chosen or paid for
            for (int i = 0; i < 3; i++) {
                if (_stats[i] != null) _stats[i].text = i == _casterSlot ? text ?? string.Empty : string.Empty;
            }
        }

        /// <summary>
        /// An element's box in the REQ's canonical frame, wherever the layout has put it: what the
        /// cursor warp and keyboard focus work in. The REQ's own rect would point at where the element
        /// stood before it was moved.
        /// </summary>
        public Rect CanonicalRectOf(VisualElement element) {
            if (_reqStage == null || element == null || element.panel == null) return Rect.zero;
            Rect wb = element.worldBound;
            if (float.IsNaN(wb.x) || wb.width <= 0f) return Rect.zero;
            Vector2 min = _reqStage.WorldToLocal(wb.min);
            Vector2 max = _reqStage.WorldToLocal(wb.max);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private VisualElement Tablet(string key, string cls, System.Action pressed) {
            var tablet = new VisualElement { pickingMode = PickingMode.Position, focusable = true };
            tablet.AddToClassList("enhanced-cast__tablet");
            if (cls != null) tablet.AddToClassList(cls);
            tablet.style.backgroundImage = EnhancedArt.Load("parchment");
            var word = new Label(UiTemplates.Format(key)) { pickingMode = PickingMode.Ignore };
            word.AddToClassList("enhanced-cast__tablet-word");
            tablet.Add(word);
            tablet.AddManipulator(new Clickable(pressed));
            return tablet;
        }

        public void SetHint(string text) {
            if (_hint != null) _hint.text = text ?? string.Empty;
        }

        /// <summary>The power being chosen, large in the ring's middle, with "of max" under it; null hides it.</summary>
        public void SetReadout(string power, string of) {
            if (_readout == null) return;
            _readout.style.display = of == null ? DisplayStyle.None : DisplayStyle.Flex;
            _readoutPower.text = power ?? string.Empty;
            _readoutOf.text = of ?? string.Empty;
        }

        private VisualElement _column, _bar, _wellFrame, _parchFrame, _strip;

        /// <summary>The canonical surface the active-spell strip is drawn on while applied.</summary>
        public VisualElement SpellSheet { get; private set; }

        /// <summary>A frame's box: its padding is the stone band, drawn by <see cref="StoneFrame"/>.</summary>
        private static VisualElement Frame(VisualElement inner) {
            VisualElement frame = MovedElements.Box("enhanced-cast__frame");
            frame.Add(inner);
            return frame;
        }

        /// <summary>The most of the screen's width the bench may take.</summary>
        private const float MaxBenchShare = 0.5f;

        private StoneFrame _frame;

        /// <summary>
        /// The active-spell strip: the original's plaque and runes, drawn by
        /// <see cref="BakAgain.UI.InGame.SpellEffectCaptionView"/> at their canonical place on
        /// <see cref="SpellSheet"/>, which is fitted so the plaque fills the strip's height.
        /// </summary>
        private void FitStrip() {
            VisualElement plaque = SpellSheet?.Q(BakAgain.UI.InGame.SpellEffectCaptionView.PlaqueName);
            if (plaque == null) return;
            // Only the runes: the original's plaque is a 64-pixel strip of its own frame, which the
            // stone already provides here. It stays as the box the runes are fitted by.
            plaque.style.unityBackgroundImageTintColor = Color.clear;
            float w = plaque.resolvedStyle.width, h = plaque.resolvedStyle.height;
            if (float.IsNaN(w) || w <= 0f) return;
            EnhancedSheet.Fit(SpellSheet, _strip.contentRect,
                new Rect(SpellEffectCaption.PlaqueX, SpellEffectCaption.PlaqueY, w, h));
        }

        /// <summary>Re-fits the strip once its plaque has loaded.</summary>
        public void StripBuilt() => FitStrip();
        private bool _combat;
        private int _casterSlot = -1;
        private Label _hint, _readoutPower, _readoutOf;
        private readonly Label[] _stats = new Label[3];
        private string _statsText;
        private BakAgain.UI.InGame.PortraitRingSet _rings;   // health and stamina round each portrait, as on the HUD
        private VisualElement _readout;

        public void RefreshNames() {
            IReadOnlyList<string> names = _session?.ActivePartyMemberNames;
            for (int i = 0; i < 3; i++) {
                if (_portraits[i] == null) continue;
                string name = names != null && i < names.Count ? names[i] : string.Empty;
                bool shown = !string.IsNullOrEmpty(name) && (!_combat || i == _casterSlot);
                _portraits[i].style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
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
                    backgroundImage = EnhancedArt.Load("ring"),
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
        /// <remarks>One element for the layout's life: the ring is redrawn on every hover and school
        /// change, and a fresh element each time came up without the lit texture (the glow was lost
        /// after the first redraw).</remarks>
        public VisualElement Bench() {
            Rect r = BenchRegion();
            _bench ??= new VisualElement { name = "enhanced_cast_bench", pickingMode = PickingMode.Ignore };
            _bench.style.position = Position.Absolute;
            _bench.style.left = r.x;
            _bench.style.top = r.y;
            _bench.style.width = r.width;
            _bench.style.height = r.height;
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
                _marble = EnhancedArt.Texture("marble");
                if (shader == null || _marble == null) {
                    _bench.style.backgroundImage = EnhancedArt.Load("marble"); // unlit, but still the bench
                    return;
                }
                _benchMaterial = new Material(shader);
                _benchTexture = new RenderTexture(BenchTexels, BenchTexels, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            }
            _bench.style.backgroundImage = Background.FromRenderTexture(_benchTexture);
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

        private static string SchoolName(int school) => $"imagebutton_{CastMenuSelection.FirstSchoolActionId + school}";

        private static string PortraitName(int slot) => $"hotspot_{CastMenuSelection.FirstPartySlotActionId + slot}";

        private static VisualElement Sheet(string name) {
            var sheet = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            sheet.AddToClassList("enhanced-cast__sheet");
            return sheet;
        }

        /// <summary>A stone disc over the button: the school's figure engraved in it, or the Exit word.</summary>
        private void Stone(VisualElement button, int school) {
            if (school < 0) {
                StoneButton.Dress(button, GameData.Resources.Text.UiTemplates.Format(GameData.Resources.Text.UiTemplates.EnhancedExit));
                return;
            }
            VisualElement disc = StoneButton.Dress(button);
            disc.generateVisualContent += ctx => DrawFigure(ctx, disc, school, button.ClassListContains(ShownClass));
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

        private IVisualElementScheduledItem _pendingFit;

        private void ScheduleFit() {
            if (_layout == null || _pendingFit != null) return;
            _pendingFit = _layout.schedule.Execute(() => {
                _pendingFit = null;
                Fit();
            });
        }

        private void Fit() {
            if (_layout == null) return;
            PlaceStraps();
            // The bench is square — the left column as wide as the well is tall, plus its frame — but
            // never more than half the screen, or a 4:3 window leaves the parchment a sliver.
            float h = _well.layout.height;
            float frame = _wellFrame.layout.width - _well.layout.width;
            float room = _layout.contentRect.width * MaxBenchShare;
            if (!float.IsNaN(h) && h > 0f && !float.IsNaN(frame) && !float.IsNaN(room)) {
                float want = Mathf.Min(h + frame, room);
                if (Mathf.Abs(_left.resolvedStyle.width - want) > 0.5f) _left.style.width = want;
            }
            FitStrip();
            FitHint();
            EnhancedSheet.Fit(RingSheet, _well.contentRect, BenchRegion());
            Relight(); // the marble's scale follows the fit
            EnhancedSheet.Fit(InfoSheet, _parch.contentRect, InfoRect());
        }

        /// <summary>
        /// Copper straps across the gaps between the panels, two to a gap, as the original's frame joins
        /// its slabs. Placed from the panels' own resolved boxes, so they follow any window shape.
        /// </summary>
        private void PlaceStraps() {
            Rect well = Box(_wellFrame), parch = Box(_parchFrame);
            if (well.width <= 0f || parch.width <= 0f) return;
            float band = _wellFrame.resolvedStyle.paddingLeft;
            Rect outer = Rect.MinMaxRect(well.xMin, Mathf.Min(well.yMin, parch.yMin), parch.xMax, Mathf.Max(well.yMax, parch.yMax));
            float middle = well.xMax - band / 2f;
            _frame ??= new StoneFrame(_layout);
            _frame.Draw(outer, band, middle);
            // Each strap straddles the bar it holds down; the middle bar gets its own pair.
            var spots = new List<(float X, float Y, bool Vertical)>();
            StoneFrame.AddSides(spots, Rect.MinMaxRect(outer.xMin, outer.yMin, middle, outer.yMax), band, right: false);
            StoneFrame.AddSides(spots, Rect.MinMaxRect(middle, outer.yMin, outer.xMax, outer.yMax), band, left: false);
            StoneFrame.AddMiddle(spots, outer, middle);
            _frame.Strap(spots, band);
        }

        private Rect Box(VisualElement e) {
            if (e == null || e.panel == null || float.IsNaN(e.worldBound.width)) return Rect.zero;
            Rect w = e.worldBound;
            Vector2 o = _layout.WorldToLocal(w.position);
            return new Rect(o.x, o.y, w.width, w.height);
        }

        /// <summary>What the faithful panel's text covers, from the panel's own constants: the title's box
        /// and the body column, down to the caster's health line.</summary>
        private static Rect InfoRect() {
            float left = Mathf.Min(SpellInfoPanel.BodyX, SpellInfoPanel.TitleCentreX - CastScreen.InfoTitleBoxWidth / 2f);
            float right = Mathf.Max(SpellInfoPanel.BodyRight, SpellInfoPanel.TitleCentreX + CastScreen.InfoTitleBoxWidth / 2f);
            float bottom = SpellInfoPanel.HealthStaminaY + SpellInfoPanel.BodyLineStep;
            return Rect.MinMaxRect(left, SpellInfoPanel.TitleY, right, bottom);
        }

    }
}
