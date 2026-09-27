namespace BakAgain.UI {
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using BakAgain.Core;
    using BakAgain.CutScenes;
    using BakAgain.UI.Layout;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Credits;
    using GameData.Resources.Layout;
    using UnityEngine;
    using UnityEngine.TextCore.Text;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The intro credits scroll, reimplemented from ShowCredits / scrollCredits
    /// (KRONDOR.EXE 0x40934 / 0x405f1). Renders the CRED.DAT (role, name) lines
    /// scrolling up through the BLANK.SCX parchment window in canonical 1600x1200
    /// space, with the static "CREDITS" title above and a per-line edge fade.
    ///
    /// Easter eggs (gated on the attract-pass index, matching word_dseg_5E6 %
    /// 30 == 8): the RareReveal "LOVELY LADY:" credit only appears on those
    /// passes; the Sparkle "Nels Bruckner" name flickers a random colour on
    /// those passes or while N is held.
    ///
    /// NOTE: this was authored without an Editor, and carried EDITOR-VERIFY markers plus a
    /// "claude-pc tasks" routing note. **All of it is settled as of 2026-09-03** and the markers are
    /// gone: the prefab wiring is done (see <c>creditsFont</c>), and the one calibration point —
    /// scroll speed — was derived from the binary rather than eyeballed (see
    /// <c>ScrollPxPerSecond</c>). The routing note was also obsolete: claude-vps has driven the
    /// Editor since 2026-06-14.
    ///
    /// **Checked against the running original on 2026-09-03** (Spice86, attract-loop credits), on
    /// the three things this file decides for itself rather than reading out of CRED.DAT:
    /// role-less names right-align on one shared edge (the <c>Justify.FlexEnd</c> below); the two
    /// <c>Centered</c> lines centre across the whole stage rather than the name column; and the
    /// top and bottom of the window really do fade line-by-line (<c>UpdateFade</c>) rather than
    /// clipping hard. The bottom "Portions (C) ... / (C) 1993 / Dynamix, Inc." block is NOT ours to
    /// place -- it is painted into the parchment art and appears in no CRED.DAT line, so a reader
    /// comparing screenshots should not go looking for it here.
    /// </summary>
    public class CreditsView : MonoBehaviour, BakAgain.UI.Navigation.IScreen {
        public UniTask ShowAsync() {
            if (!gameObject.activeSelf) {
                gameObject.SetActive(true);
            }
            return UniTask.CompletedTask;
        }

        public UniTask HideAsync() {
            if (gameObject.activeSelf) {
                gameObject.SetActive(false);
            }
            return UniTask.CompletedTask;
        }

        // Screen geometry, from the extracted data (CreditsLayout). Never derived here:
        // this view holds no knowledge of the original display.
        private CreditsLayout _layout = new CreditsLayout();
        private static readonly Color LeaderColor = new Color(0.2f, 0.15f, 0.1f);

        /// <summary>
        /// Scroll speed, DERIVED from the original rather than calibrated by eye (2026-09-03).
        ///
        /// <para>The chain, all read out of KRONDOR.EXE:</para>
        /// <list type="bullet">
        /// <item><c>InitializeGameHardware</c> @0x41A20 does <c>push 13</c> into
        /// <c>InitializeTimers</c>, which programs the PIT with reload <c>65535 / 13 = 5041</c>
        /// (@0x19878-0x1988A), giving <c>1193181.67 / 5041</c> = <b>236.70 Hz</b> of INT 8.</item>
        /// <item><c>ShowCredits</c> @0x409E3 sets <c>autoDecreasingTimer = 10</c> and spins on it
        /// after each step (@0x40A25), so one step per <b>10 ticks</b> = 23.67 steps/sec.</item>
        /// <item><c>scrollCredits</c> @0x40924 does <c>inc word_dseg_4CFC</c> and the line Y is
        /// <c>0x36 - offset</c>, so a step is <b>1 VGA px</b>.</item>
        /// </list>
        ///
        /// <para>23.67 VGA px/sec, and vertical VGA scales x6 into canonical space, so
        /// <b>142 canonical px/sec</b>. The vsync wait in the same loop does not gate it: 10 ticks
        /// is 42 ms against a ~14 ms frame, so the timer dominates.</para>
        ///
        /// <para>Was <c>60f</c> with an EDITOR-VERIFY "calibrate against the DOS wall-clock feel"
        /// marker — i.e. 2.4x too slow, and waiting on a judgement nobody could make reliably. The
        /// original is measurable, so it was measured.</para>
        /// </summary>
        private const float ScrollPxPerSecond = 142f;

        private const string CredKey = "CRED.DAT";
        private const string BackgroundKey = "BLANK.SCX";
        private const int RareCycle = 30;
        private const int RareCyclePhase = 8;

        private IResourceCache _resourceCache;
        private BakAgain.UI.InputCore.InputLayerStack _stack;
        private BakAgain.UI.InputCore.ICheatInput _cheat;
        private ILogger _logger;
        private UIDocument _document;

        private readonly List<(VisualElement element, float topInScroller)> _lineElements = new();
        private readonly List<VisualElement> _sparkleElements = new();
        private float _windowHeight;
        private bool _interrupted;

        private void Awake() {
            _logger = LogManager.LoggerFactory.CreateLogger<CreditsView>();
            _document = GetComponent<UIDocument>();
            gameObject.SetActive(false);
        }

        [VContainer.Inject]
        public void Construct(
            IResourceCache resourceCache, BakAgain.UI.InputCore.InputLayerStack stack,
            BakAgain.UI.InputCore.ICheatInput cheat) {
            _resourceCache = resourceCache;
            _stack = stack;
            _cheat = cheat;
        }

        /// <summary>
        /// Plays one credits pass. Returns true if the scroll completed
        /// naturally, false if the player interrupted it (any key / mouse click).
        /// </summary>
        public async UniTask<bool> PlayAsync(int cycleIndex, CancellationToken cancellationToken) {
            CreditsData credits = await _resourceCache.GetOrLoadAsync<CreditsData>(CredKey);
            if (credits == null) {
                _logger.LogError("Failed to load credits ({Key}).", CredKey);
                return true; // nothing to show — treat as completed so the attract loop continues
            }
            // An override supplying "Layout": null must not NRE through BuildLayout — fall back
            // to the faithful defaults (CreditsLayout's own field initializers reproduce the
            // original geometry) rather than requiring every override to restate the whole block.
            _layout = credits.Layout ?? new CreditsLayout();

            bool revealRare = cycleIndex % RareCycle == RareCyclePhase;

            // Activate before building the layout: a UIDocument only creates its
            // rootVisualElement once the GameObject is enabled (OnEnable). Awake
            // disables this object, so building first would hand a null root to
            // CanonicalStage.GetOrCreate.
            gameObject.SetActive(true);
            BuildLayout(credits, revealRare);

            _interrupted = false;
            // Push an Exclusive skip layer: Activate/Skip or Cancel interrupts the scroll. The stack
            // guarantees the dismiss can't fall through to the main menu shown next (no _dismissArmed).
            var skipLayer = new BakAgain.UI.InputCore.ActionLayer(
                "credits", () => _interrupted = true, () => _interrupted = true);
            _stack.Push(skipLayer);
            try {
                return await RunScroll(cycleIndex, cancellationToken);
            } finally {
                _stack.Remove(skipLayer);
                gameObject.SetActive(false);
            }
        }

        private async UniTask<bool> RunScroll(int cycleIndex, CancellationToken cancellationToken) {
            bool sparkleActive = cycleIndex % RareCycle == RareCyclePhase;
            float totalHeight = _lineElements.Count > 0
                ? _lineElements[^1].topInScroller + _layout.Row.Height.Value
                : 0f;
            // Start fully below the window; end once the last line clears the top.
            float scrollOffset = 0f;
            float endOffset = totalHeight + _windowHeight;

            VisualElement scroller = _document.rootVisualElement.Q("CreditsScroller");

            while (scrollOffset < endOffset) {
                if (_interrupted || cancellationToken.IsCancellationRequested) {
                    return false;
                }

                scrollOffset += ScrollPxPerSecond * Time.deltaTime;
                float scrollerTop = _windowHeight - scrollOffset;
                if (scroller != null) {
                    scroller.style.top = scrollerTop;
                }

                UpdateFade(scrollerTop);
                if (sparkleActive || NKeyHeld()) {
                    ApplySparkle();
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
            return true;
        }

        // Per-line opacity: ramp in across the top band, out across the bottom
        // band — the canonical-RGBA equivalent of scrollCredits' pen-index ramp.
        private void UpdateFade(float scrollerTop) {
            foreach ((VisualElement element, float topInScroller) in _lineElements) {
                float yInWindow = topInScroller + scrollerTop;
                float opacity;
                if (yInWindow < _layout.FadeTopBand.Value) {
                    opacity = Mathf.Clamp01(yInWindow / _layout.FadeTopBand.Value);
                } else if (yInWindow > _windowHeight - _layout.FadeBottomBand.Value) {
                    opacity = Mathf.Clamp01((_windowHeight - yInWindow) / _layout.FadeBottomBand.Value);
                } else {
                    opacity = 1f;
                }
                element.style.opacity = opacity;
            }
        }

        private void ApplySparkle() {
            // Per-frame random colour, matching scrollCredits' GetRandomNumber pen.
            foreach (VisualElement element in _sparkleElements) {
                element.style.color = new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value);
                element.MarkDirtyRepaint(); // re-run the leader painter so its dots pick up the new colour
            }
        }

        private bool NKeyHeld() => _cheat != null && _cheat.RevealRareCredits;

        private void BuildLayout(CreditsData credits, bool revealRare) {
            VisualElement root = _document.rootVisualElement;
            VisualElement stage = CanonicalStage.GetOrCreate(root, credits.Frame);
            stage.Clear();
            _lineElements.Clear();
            _sparkleElements.Clear();
            // Window.Height is a box property (LayoutApplier writes it straight to
            // style.height); reading it here as a raw float assumes Px, which is the only
            // unit CreditsLayout's own default uses for it. A percent override would need
            // this recomputed from resolved layout instead — out of scope for this pass.
            _windowHeight = _layout.Window.Height.Value;

            // Parchment background (with the baked "Betrayal at Krondor" sign).
            LoadBackground(stage).Forget();

            // Static title above the scroll window. LayoutApplier.Apply does the whole job
            // here: TopCenter anchors + centers the (intrinsically-sized) label as a whole,
            // and the explicit Top inset overrides the anchor's implied top=0 — no manual
            // width/left/position bookkeeping needed.
            Label title = MakeLabel(credits.Title);
            LayoutApplier.Apply(title, _layout.Title);
            stage.Add(title);

            // Clipped scroll window with an absolutely-positioned scroller. Window models no
            // Width (it's always full-bleed horizontally), so that one dimension is finished
            // by hand; Top/Height/Anchor(default TopLeft, giving left=0) all come from Apply.
            var window = new VisualElement { name = "CreditsWindow" };
            LayoutApplier.Apply(window, _layout.Window);
            window.style.width = Length.Percent(100);
            window.style.overflow = Overflow.Hidden;
            stage.Add(window);

            var scroller = new VisualElement { name = "CreditsScroller" };
            scroller.style.position = Position.Absolute;
            scroller.style.left = 0;
            scroller.style.width = Length.Percent(100);
            scroller.style.top = _windowHeight;
            window.Add(scroller);

            float y = 0f;
            foreach (CreditLine line in credits.Lines) {
                if (line.RareReveal && !revealRare) {
                    continue; // collapsed to zero height except on the rare pass
                }
                VisualElement row = MakeRow(line, y);
                scroller.Add(row);
                _lineElements.Add((row, y));
                y += _layout.Row.Height.Value;
            }
        }

        private VisualElement MakeRow(CreditLine line, float top) {
            var row = new VisualElement();

            if (line.Centered) {
                // The rare closing/"LOVELY LADY" line centers across the whole stage —
                // independent of the role/name column geometry Row.Left/Right describe below,
                // so it does not go through _layout.Row at all (only its Height, shared by
                // every row template).
                row.style.position = Position.Absolute;
                row.style.top = top;
                row.style.left = 0;
                row.style.width = Length.Percent(100);
                row.style.height = LayoutApplier.ToStyleLength(_layout.Row.Height);

                Label centered = MakeLabel(line.Role);
                centered.style.width = Length.Percent(100);
                centered.style.unityTextAlign = TextAnchor.UpperCenter;
                row.Add(centered);
                return row;
            }

            // Role/leader/name band. Row.Position defaults to Absolute (CreditsLayout never
            // overrides it), so this one call places the row at Row.Left/Right within the
            // scroller AND makes it a flex row for its role/leader/name children — Position and
            // Flow are independent concerns in LayoutApplier.Apply, so there is no more manual
            // inset bookkeeping or restoring Position afterwards.
            LayoutApplier.Apply(row, _layout.Row);
            row.style.top = top;

            Label roleLabel = null;
            if (!string.IsNullOrEmpty(line.Role)) {
                roleLabel = MakeLabel(line.Role);
                row.Add(roleLabel);
            }

            if (!string.IsNullOrEmpty(line.Name)) {
                Label nameLabel = MakeLabel(line.Name);
                row.Add(nameLabel);
                if (line.Sparkle) {
                    _sparkleElements.Add(nameLabel);
                }

                // Dotted leader (original draws a pixel every 4 VGA px ≈ 20 canonical px).
                // Painted as discrete dots via generateVisualContent so it reads as dots, not
                // a solid line. Unlike the pre-Phase-2b absolute layout, the leader is now a
                // flex child in the row's own Row/SpaceBetween flow, sitting between role and
                // name — flexGrow is not a modeled LayoutFlow property (see the brief /
                // CreditsLayout.Row docs), so it is set directly here: it is what makes the
                // leader absorb exactly the free space between the two labels, reproducing
                // the original's absolute placement at Row's insets.
                // *** NO LEADER ON A CONTINUATION LINE. *** The original guards the whole dot loop
                // on BOTH halves of the pair being non-empty
                // (`strings[i][0] != 0 && strings[i+1][0] != 0`, CREDITS.C:131), so an entry's second
                // and later names — which carry a name and no role — get no dots at all. Drawing on
                // the name alone gave those lines a leader spanning almost the whole row, which a
                // side-by-side against the running original showed immediately: the original's
                // "Raymond E. Feist" under "ORIGINAL STORY:" sits bare, ours had a full-width dotted
                // rule under it.
                if (string.IsNullOrEmpty(line.Role)) {
                    // *** BUT IT STILL SITS IN THE NAME COLUMN. *** The row is SpaceBetween and the
                    // leader is what pushes the name to the right edge, so returning here with the
                    // name as the only child drops it back to the LEFT — which is what this did
                    // between the dot fix and 2026-09-03, and what a side-by-side against the
                    // running original showed: the original's role-less names all end on one right
                    // edge (a column of them under "MONSTERS PORTRAYED BY:"), ours ran down the far
                    // left. Ending the row's own justification is the flex equivalent of the leader
                    // that is deliberately not here.
                    row.style.justifyContent = Justify.FlexEnd;

                    return row;
                }

                var leader = new VisualElement { name = "Leader" };
                leader.style.flexGrow = 1;
                // The leader's own vertical placement can no longer be expressed as an absolute
                // `bottom` inset the way the pre-Phase-2b leader did: `bottom` means something
                // different once the element is a flex child. On a Position.Absolute element,
                // `bottom` is an edge ANCHOR measured from the parent's bottom edge. On a flex
                // child (this leader, so flexGrow can absorb the gap between role and name),
                // `bottom` is instead an OFFSET applied on top of the element's normal flow
                // position — and with Row.Flow.Align = Start, that flow position is the row's
                // TOP, so a bare `bottom` shift here would push the leader up and off the row
                // entirely (this was exactly the regression: dots rendering above the line).
                // alignSelf: FlexEnd puts the leader's own bottom edge at the row's bottom —
                // the flex equivalent of the old anchor — and marginBottom then lifts it the
                // same 25%-of-row-height the original did, reproducing the baseline span.
                leader.style.alignSelf = Align.FlexEnd;
                leader.style.marginBottom = _layout.Row.Height.Value * 0.25f;
                leader.style.height = _layout.LeaderDotRadius.Value * 2f;
                leader.style.color = LeaderColor; // painter reads resolvedStyle.color (sparkle tints it)
                CreditsLayout layout = _layout;
                leader.generateVisualContent += mgc => DrawLeaderDots(mgc, layout);
                // Text widths aren't known until layout settles; repaint the dots once the
                // labels report their geometry so the leader's flex-resolved width (and hence
                // the gap it draws into) is measured right.
                roleLabel?.RegisterCallback<GeometryChangedEvent>(_ => leader.MarkDirtyRepaint());
                nameLabel.RegisterCallback<GeometryChangedEvent>(_ => leader.MarkDirtyRepaint());
                row.Insert(row.IndexOf(nameLabel), leader); // between role and name in flex order
                if (line.Sparkle) {
                    _sparkleElements.Add(leader);
                }
            }

            return row;
        }

        // Draws the leader as discrete dots, matching scrollCredits' pixel-every-4px leader.
        // Since the leader is now a flex child with flexGrow:1 (see MakeRow), its own box IS
        // exactly the gap between role and name — unlike the pre-Phase-2b absolute leader,
        // which spanned the full role-left -> name-right band and had to be told the role's
        // and name's widths to avoid drawing under them. So the dot band is simply
        // [LeaderGap, width - LeaderGap] in the leader's own local space. Reads the element's
        // resolved colour so the Sparkle easter egg can tint it.
        private static void DrawLeaderDots(MeshGenerationContext mgc, CreditsLayout layout) {
            VisualElement leader = mgc.visualElement;
            float width = leader.resolvedStyle.width;
            if (width <= 0f) {
                return;
            }
            float start = layout.LeaderGap.Value;
            float end = width - layout.LeaderGapName.Value;
            if (end <= start) {
                return; // role + name fill the line — no room for dots
            }
            float y = leader.resolvedStyle.height * 0.5f;
            float pitch = layout.LeaderDotPitch.Value;

            // *** PHASE IS GLOBAL, NOT PER-ROW. ***
            // The original steps absolute screen x on a 4-VGA-px grid, snapping the start down to a
            // multiple of 4 and clipping to the band (scrollCredits @0x40888-0x408B3). So every
            // row's dots land on the SAME multiples and read as vertical columns down the credits.
            // Stepping from this element's own left edge instead — which moves with the role text's
            // width — gives each row a different phase and the columns drift apart. That drift is
            // what the "leaders look wrong" report was seeing.
            //
            // leader.layout.x is the element's offset inside its row, and every row shares one left
            // origin, so snapping against it aligns rows to each other exactly as absolute x would;
            // the two differ only by a constant, which shifts all rows together and is invisible.
            // Taking the first grid point at or after the band start is equivalent to the original's
            // snap-down-then-clip, without drawing dots it would only clip away.
            float originInRow = leader.layout.x;

            Painter2D painter = mgc.painter2D;
            painter.fillColor = leader.resolvedStyle.color;
            for (float x = CreditsLayout.FirstDotOffset(originInRow, start, pitch); x <= end; x += pitch) {
                painter.BeginPath();
                painter.Arc(new Vector2(x, y), layout.LeaderDotRadius.Value, 0f, 360f);
                painter.Fill();
            }
        }

        // Position is deliberately NOT set here: the title is placed by LayoutApplier.Apply
        // (Position.Absolute, via _layout.Title), while role/name/centered labels are normal
        // flow children of their row (Position.Relative, UI Toolkit's default) so the row's
        // flex properties (Row.Flow) actually place them.
        private Label MakeLabel(string text) {
            var label = new Label(text);
            label.style.fontSize = LayoutApplier.ToStyleLength(_layout.FontSize);
            if (GameFonts.Game != null) {
                label.style.unityFontDefinition = new StyleFontDefinition(GameFonts.Game);
            }
            return label;
        }

        private async UniTaskVoid LoadBackground(VisualElement stage) {
            try {
                Sprite background = await _resourceCache.GetOrLoadAsync<Sprite>(BackgroundKey);
                if (background != null) {
                    stage.style.backgroundImage = Background.FromSprite(background);
                    stage.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                }
            } catch (Exception e) {
                _logger.LogError(e, "Failed to load credits background {Key}.", BackgroundKey);
            }
        }
    }
}
