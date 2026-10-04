namespace BakAgain.UI.Puzzle {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Scene;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;
    using VContainer;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The lettered dial puzzle — <c>REQ_PUZL</c> over <c>PUZZLE.SCX</c>, from
    /// <c>sub_ovr191_0</c> @0x78c60.
    /// </summary>
    /// <remarks>
    /// <b>A combination lock made of letters.</b> Every column is a wheel showing one letter from
    /// each dial row; clicking rotates it. The puzzle is solved when the selected rows spell the
    /// target word — the player never types, so there is no text field here and never should be.
    ///
    /// <para>The columns are REQ_PUZL's own click areas (action ids 128..142), which the data ships
    /// hidden so that a puzzle can light exactly as many as its word is long. A space in the target
    /// is a dead column and stays switched off. That is what
    /// <see cref="UserInterfaceLoader.SetEntryState"/> is for — the same capability the camp
    /// screen's Stop button needed.</para>
    /// </remarks>
    [RequireComponent(typeof(UserInterfaceLoader))]
    public sealed class PuzzleScreen : MonoBehaviour, IActionHandler {
        /// <summary>Name of the letter layer, so a redraw replaces it rather than stacking.</summary>
        private const string LettersName = "BakPuzzleLetters";

        /// <summary>Name of the riddle layer.</summary>
        private const string RiddleName = "BakPuzzleRiddle";

        /// <summary>Name prefix of the opened-latch sprites, one per bolt.</summary>
        private const string LatchName = "BakPuzzleLatch";

        /// <summary>PUZZLE.PAL — the pens the riddle's three passes name (0x78d58).</summary>
        private const string Palette = "PUZZLE.PAL";

        private UserInterfaceLoader _ui;
        private ILogger _logger;
        private CipherPuzzle _puzzle;
        private int[] _rows = System.Array.Empty<int>();

        /// <summary>
        /// False while the screen is still showing the alien script — see
        /// <see cref="CipherPuzzleLayout.AlienIsAlwaysDrawnFirst"/>.
        /// </summary>
        /// <remarks>
        /// It is set once, by the resolve pass, and stays set for the life of the open puzzle so
        /// that every later redraw (a wheel turning, a roll) keeps the face the player is now
        /// reading. A party that cannot read the riddle never sets it, which is the original's
        /// <c>else { font_activate(nFontAlien); }</c>.
        /// </remarks>
        private bool _resolved;

        [Inject]
        public void Construct(CutScenes.IResourceCache resources,
            BakAgain.ResourceManagement.IResourceProviderService provider,
            BakAgain.Audio.MidiPlaybackManager midi, BakAgain.Core.GameSession session,
            IDialogManager dialogs = null) {
            _dialogs = dialogs;
            _resources = resources;
            _session = session;
            _provider = provider;
            _midi = midi;
            _logger = Microsoft.Extensions.Logging.LoggerFactoryExtensions
                .CreateLogger<PuzzleScreen>(LogManager.LoggerFactory);
        }

        private CutScenes.IResourceCache _resources;
        private BakAgain.Core.GameSession _session;
        private BakAgain.ResourceManagement.IResourceProviderService _provider;
        private BakAgain.Audio.MidiPlaybackManager _midi;
        private IDialogManager _dialogs;

        /// <summary>Plays one of the screen's own dialog records, if there is anything to play it.</summary>
        /// <remarks>
        /// Optional so a puzzle driven from a test or a probe still runs; the screen's behaviour does
        /// not depend on the narration.
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTask SayAsync(int dialogId) {
            if (_dialogs != null) {
                await _dialogs.ShowById(dialogId);
            }
        }

        /// <summary>What was playing before the riddle, so it can be put back.</summary>
        private int _trackBeforePuzzle = GameData.Resources.Audio.MusicPlayback.NoTrack;

        /// <summary>Whether the dials currently spell the target word.</summary>
        public bool IsSolved => _puzzle != null && _puzzle.IsSolved(_rows);

        /// <summary>
        /// Shows a puzzle and completes when the player solves it or leaves.
        /// </summary>
        /// <returns>
        /// Whether it was solved — <b>and that answer is the caller's decision</b>. The original
        /// stores <c>UI_RunCipherPuzzle</c>'s return straight into the flag that opens the chest
        /// (0x77464), so there is nothing else to consult afterwards.
        /// </returns>
        /// <remarks>
        /// <b>The riddle has its own track, and it is put back on the way out.</b> The original
        /// starts it as the screen opens and keeps what was playing in a local, which is only ever
        /// used to restore — so leaving the riddle theme running after the screen closes would
        /// carry it into wherever the player came from.
        /// </remarks>
        public async Cysharp.Threading.Tasks.UniTask<bool> RunAsync(CipherPuzzle puzzle) {
            _outcome = new Cysharp.Threading.Tasks.UniTaskCompletionSource<bool>();
            if (_midi != null && _provider != null) {
                _trackBeforePuzzle = await _midi.PlayTrackAsync(
                    CipherPuzzleLayout.MusicTrack, _provider, owner: this);
            }

            // *** THE OPENING LINE COMES BEFORE THE PUZZLE IS ON SCREEN. *** CIPHER.C plays it at
            // :63, between starting the track and loading anything — the fade-out and the first
            // blit are both later — so it is spoken over wherever the player still is, and the
            // chest appears afterwards. Opening the screen first would put it over the puzzle.
            await SayAsync(CipherPuzzleLayout.OpeningDialog);
            Open(puzzle);

            bool solved = await _outcome.Task;
            await SayAsync(solved
                ? CipherPuzzleLayout.SolvedDialog
                : CipherPuzzleLayout.UnsolvedDialog);
            if (_midi != null && _provider != null) {
                await _midi.PlayTrackAsync(_trackBeforePuzzle, _provider, owner: this);
            }
            Close();

            return solved;
        }

        private Cysharp.Threading.Tasks.UniTaskCompletionSource<bool> _outcome;

        /// <summary>Puts a puzzle on the screen, with every wheel back at its first row.</summary>
        public void Open(CipherPuzzle puzzle) {
            _puzzle = puzzle;
            _rows = new int[puzzle?.Width ?? 0];
            _resolved = false;
            _solving = false;
            ClearLatches();
            gameObject.SetActive(true);
            if (_ui != null && _ui.IsBuilt) {
                OnPanelBuilt(_ui.CurrentNavWidgets);
            }
        }

        /// <summary>Takes it down.</summary>
        public void Close() {
            _puzzle = null;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Claims the panel before it builds. REQ_PUZL <b>is</b> painted — its exit button is the
        /// only way out of a puzzle you cannot solve.
        /// </summary>
        /// <remarks>
        /// <b>This used to set <c>HitTestOnly = true</c>, and that was wrong.</b> The comment
        /// justifying it said "UI_RunCipherPuzzle never runs a menu draw"; <c>CIPHER.C:135</c> and
        /// <c>:141</c> are exactly that draw, called twice per loop pass whenever the redraw flag is
        /// set. Painting nothing removed REQ_PUZL's one visible entry — the exit ImageButton at
        /// canonical (1280, 1008) — so our cipher screen had no way out at all: a player who cannot
        /// solve a wordlock was stuck on it.
        ///
        /// <para>Confirmed by driving the original to zone-1 puzzle chest 54 on 2026-09-07: its
        /// dial screen shows "Exit" at the bottom right of the chest plate, exactly where the REQ
        /// places it. The earlier side-by-side that removed the paint had read a placement
        /// difference as "the original does not draw this".</para>
        ///
        /// <para>The prefab's serialised <c>hitTestOnly</c> is cleared here rather than in the
        /// asset, so a prefab still carrying the old value cannot resurrect the bug.</para>
        /// </remarks>
        private void Awake() {
            _ui = _ui ? _ui : GetComponent<UserInterfaceLoader>();
            if (_ui != null) {
                _ui.HitTestOnly = false;
            }
        }

        private void OnEnable() {
            _ui = _ui ? _ui : GetComponent<UserInterfaceLoader>();
            if (_ui == null) {
                return;
            }
            _ui.Built += OnPanelBuilt;
            // The cached build can complete before this subscribes — the same trap CampMenu and
            // CastScreen both hit — so reconcile against IsBuilt rather than waiting for the event.
            if (_ui.IsBuilt) {
                OnPanelBuilt(_ui.CurrentNavWidgets);
            }
        }

        private void OnDisable() {
            if (_ui != null) {
                _ui.Built -= OnPanelBuilt;
            }
        }

        private void OnPanelBuilt(IReadOnlyList<InputCore.NavWidget> _) {
            // *** RE-ENTRANCY GUARD, AND IT IS NOT OPTIONAL. *** SetEntryState re-raises Built so
            // the input layer is rebuilt over the new widget set, which lands straight back here —
            // and lighting the columns calls SetEntryState fifteen times. Without this the handler
            // recurses until the Editor hangs, which is exactly what it did.
            // *** AND A PRESENTATION IN FLIGHT IS THE SECOND HALF OF THAT GUARD. *** The draws
            // themselves call SetEntryPosition, which re-raises Built once per column — so a
            // presentation that is midway through its two passes gets a fresh one started
            // underneath it, and the layer the dissolve is animating is replaced by a layer the
            // newcomer drew. Measured: the resolve ran (both fonts loaded, _resolved set) and the
            // dissolve never showed a single frame, because its incoming layer had already been
            // detached. Redrawing was idempotent while there was only one pass; it is not now.
            if (_puzzle == null || _lighting || _presenting) {
                return;
            }

            _lighting = true;
            try {
                LightTheLiveColumns();
            } finally {
                _lighting = false;
            }

            PresentAsync().Forget();
        }

        private bool _lighting;

        /// <summary>Whether <see cref="PresentAsync"/> is between its first draw and its last.</summary>
        private bool _presenting;

        /// <summary>Where the riddle goes — the DDX entry's own resize rect.</summary>
        public void SetTextArea(GameData.Resources.Layout.LayoutHint area) => _textArea = area;

        private GameData.Resources.Layout.LayoutHint _textArea;
        private GameData.Resources.Palette.PaletteResource _palette;

        /// <summary>
        /// Draws the riddle the player is meant to solve.
        /// </summary>
        /// <remarks>
        /// <b>Three passes, and they are the same string.</b> The original renders it at y-1 in pen
        /// 65, at y+1 in pen 149, then on the baseline in pen 16 (0x78e0b-0x78e4e): a highlight
        /// above, a shadow below, and the body last on top. Drawn once it loses the emboss that
        /// makes it readable against the chest lid — see
        /// <see cref="CipherPuzzleLayout.TextPasses"/>.
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTask<VisualElement> DrawRiddleAsync() {
            VisualElement stage = CanonicalStage.Find(GetComponent<UIDocument>()?.rootVisualElement);
            if (stage == null || _textArea == null || string.IsNullOrEmpty(_puzzle?.Description)) {
                return null;
            }

            // *** LOAD THE PALETTE BEFORE RESOLVING A PEN. *** PaletteColors answers a null palette
            // with plain black and no complaint, so three passes in three different pens came out
            // as three identical black ones and the emboss simply vanished. The same trap the inn
            // screen's bevel fell into.
            if (_palette == null && _resources != null) {
                _palette = await _resources
                    .GetOrLoadAsync<GameData.Resources.Palette.PaletteResource>(Palette);
            }

            // The riddle is drawn in the SAME face as the wheels — never in the game font. When the
            // party cannot read the puzzle, the text is alien too.
            string fontKey = CipherPuzzleLayout.FontForPass(firstPass: !_resolved, legible: Legible);
            var font = await _resources.GetOrLoadAsync<GameData.Resources.Font.FontResource>(fontKey);
            if (font == null) {
                _logger.LogError("PuzzleScreen: {Font} did not load; the riddle has no text.", fontKey);

                return null;
            }

            stage.Q<VisualElement>(RiddleName)?.RemoveFromHierarchy();
            var layer = new VisualElement {
                name = RiddleName,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };

            // *** NO WRAPPING, AND THAT IS MEASURED RATHER THAN ASSUMED. *** The author pre-wrapped
            // every riddle — all 143 carry their own newlines — and the widest line in the shipped
            // data ("it can knock down trees with a single push.", puzzle 1700014) comes to 263 VGA
            // px / 1315 canonical against that entry's own 1375-wide box. So every line fits as
            // written and the original's wrap never triggers. CipherRiddleFitsItsBoxTests pins it;
            // if a mod adds a longer line, that test is where the wrap gets built.
            string[] lines = _puzzle.Description.Split('\n');

            // *** ONE PIXEL OF LEADING, AND THE BLOCK IS VERTICALLY CENTRED IN ITS BOX. ***
            // textwrap_draw_aligned (TEXTWRAP.C:95) advances by `line_height + line_spacing`, and
            // the dialog renderer passes line_spacing = 1 — so the advance is the font's height plus
            // one, not the height. Its vertical alignment is a flag pair on the style: 0x10 centres
            // (`voff = max_height - advance * lines`, halved), 0x20 bottom-aligns, neither is top.
            //
            // The riddle is centred. Measured against the original: centring predicts the first line
            // at VGA y=136 and the capture has it at ~138, while top-aligning predicts 105 — the
            // port was drawing it at 105, which put the text a third of the way down the plate
            // instead of two thirds.
            float lineHeight = (font.Height + CipherPuzzleLayout.RiddleLineSpacing)
                * Canonical.VgaScaleY;
            float boxLeft = _textArea.Left.Value;
            float boxWidth = _textArea.Width.Value;
            float boxTop = _textArea.Top.Value
                + ((_textArea.Height.Value - (lineHeight * lines.Length)) / 2f);

            foreach ((int yOffset, int pen) in CipherPuzzleLayout.TextPasses()) {
                for (var line = 0; line < lines.Length; line++) {
                    float x = boxLeft + ((boxWidth - LineWidth(font, lines[line])) / 2f);
                    float y = boxTop + (line * lineHeight) + (yOffset * Canonical.VgaScaleY);
                    foreach (char letter in lines[line]) {
                        // Advance on the GLYPH's width even when it has no sprite: a space has a
                        // width and no ink, and dropping its advance would close every gap up.
                        GameData.Resources.Font.FontGlyph metrics = font.GlyphFor(letter);
                        float advance = (metrics?.Width ?? 0) * Canonical.VgaScaleX;
                        Sprite glyph = GlyphSprite(font, fontKey, pen, letter);
                        if (glyph != null) {
                            layer.Add(new VisualElement {
                                pickingMode = PickingMode.Ignore,
                                style = {
                                    position = Position.Absolute,
                                    left = x, top = y,
                                    width = glyph.rect.width, height = glyph.rect.height,
                                    backgroundImage = new StyleBackground(glyph),
                                },
                            });
                        }

                        x += advance;
                    }
                }
            }

            stage.Add(layer);

            return layer;
        }

        /// <summary>How wide one line comes out, in canonical units.</summary>
        /// <remarks>
        /// A plain sum of glyph advances, which is <c>getStringWidthInPixels</c> @0x15be5 — no
        /// inter-character spacing term. Deliberately NOT a new measurement routine: BakFontData
        /// says in as many words that it is the only one and that a GameData twin was written and
        /// deleted once already. This asks the loaded font for widths it cannot know, and sums them
        /// by that same rule.
        /// </remarks>
        private static float LineWidth(GameData.Resources.Font.FontResource font, string line) {
            var width = 0f;
            foreach (char letter in line) {
                width += (font.GlyphFor(letter)?.Width ?? 0) * Canonical.VgaScaleX;
            }

            return width;
        }

        /// <summary>
        /// Switches on exactly the columns this puzzle uses, and no others.
        /// </summary>
        /// <remarks>
        /// REQ_PUZL ships fifteen; a five-letter word lights five. A SPACE in the target stays off,
        /// which is the original writing that column's gate to disabled so it cannot be clicked —
        /// not merely drawn blank. Leaving it live would let the player rotate a column that means
        /// nothing and is ignored by the solved check.
        /// </remarks>
        private void LightTheLiveColumns() {
            for (var column = 0; column < CipherPuzzleLayout.MaxColumns; column++) {
                int actionId = CipherPuzzleLayout.ActionIdFor(column);
                bool live = column < _puzzle.Width && _puzzle.IsColumnInteractive(column);
                _ui.SetEntryState(actionId, live, live);
            }
        }

        /// <summary>
        /// Draws the letter each wheel is currently showing.
        /// </summary>
        /// <remarks>
        /// The glyph is centred in the column's own box — <see cref="CipherPuzzleLayout.GlyphX"/> is
        /// the rule, with its one-pixel rightward lean — and sits on the raised box
        /// <see cref="Bevel"/> draws, which is what marks the column as something that turns.
        /// </remarks>
        private void DrawLetters() {
            DrawLettersAsync().Forget();
        }

        /// <summary>
        /// Draws the screen in the alien script, then — for a party that can read it — dissolves
        /// the readable version in over the top.
        /// </summary>
        /// <remarks>
        /// <b>CIPHER.C renders the whole screen TWICE.</b> It lays out and draws the wheels and the
        /// riddle in ALIEN.FNT for everyone, plays its dialog, and only then tests
        /// <c>gstate_is_party_member(1) || spellfx_event_mask_test_bit(4)</c>; if that passes it
        /// re-blits <c>puzzle.scr</c>, re-lays the letters, re-renders the three text passes in
        /// PUZZLE.FNT and brings them in with <c>dissolve_transition_lfsr(0, 0, 0x140, 200)</c>.
        ///
        /// <para>So the alien script is not the "cannot read it" state — it is what everyone sees
        /// first, and a party that CAN read it watches the riddle resolve. Drawing straight in the
        /// final face, which is what this screen did until now, loses the only moment that script
        /// is ever on screen for a party that could read it anyway.</para>
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTaskVoid PresentAsync() {
            _presenting = true;
            try {
                VisualElement letters = await DrawLettersAsync();
                VisualElement riddle = await DrawRiddleAsync();

                // *** BOTH PARTIES HEAR THIS ONE. *** CIPHER.C:110 plays it after the alien screen
                // is up and BEFORE the legibility test, so it lands on the unreadable riddle whether
                // or not the party can go on to read it. Playing it after the resolve would give the
                // line to a screen that had already answered it.
                await SayAsync(CipherPuzzleLayout.AfterDrawDialog);

                if (_resolved || _puzzle == null || !Legible) {
                    return;   // the alien pass IS the screen for a party that cannot read it
                }

                // Set BEFORE the second draw: it is what picks the readable font, and it keeps
                // every later redraw on that face.
                _resolved = true;

                // The outgoing layers have to survive their replacement, so take them out of the
                // way of the by-name lookup each draw starts with rather than letting it remove
                // them.
                Retire(letters);
                Retire(riddle);
                VisualElement newLetters = HideChildren(await DrawLettersAsync());
                VisualElement newRiddle = HideChildren(await DrawRiddleAsync());

                await Cysharp.Threading.Tasks.UniTask.WhenAll(
                    DissolveAsync(letters, newLetters),
                    DissolveAsync(riddle, newRiddle));
            } finally {
                _presenting = false;
            }
        }

        /// <summary>Renames a layer so the next draw does not find and delete it.</summary>
        private static void Retire(VisualElement layer) {
            if (layer != null) {
                layer.name += "Outgoing";
            }
        }

        /// <summary>
        /// Hides a freshly drawn layer's glyphs without a frame passing first.
        /// </summary>
        /// <remarks>
        /// The draw ends with <c>stage.Add</c>, so the layer is on screen the moment it returns —
        /// hiding it on a later frame would flash the finished readable version before dissolving
        /// it in.
        /// </remarks>
        private static VisualElement HideChildren(VisualElement layer) {
            for (var i = 0; layer != null && i < layer.childCount; i++) {
                layer[i].visible = false;
            }

            return layer;
        }

        /// <summary>
        /// Swaps one layer for another a glyph at a time, in random order.
        /// </summary>
        /// <remarks>
        /// <b>The original dissolves PIXELS; this dissolves glyphs, and that is deliberate.</b>
        /// <c>dissolve_transition_lfsr</c> walks every pixel of the 320x200 frame in LFSR order and
        /// copies page 2 over the front page — but the two pages differ only where the text is,
        /// because the chest art either side of the re-blit is identical. So the visible effect is
        /// the letters scattering from one script into the other, which is what this reproduces at
        /// glyph granularity against a UI Toolkit hierarchy that has no pixel buffer to walk.
        ///
        /// <para>ponytail: glyph-for-glyph by index. The two layers are built by the same loop over
        /// the same string, so index N is the same character in both — until a font is missing a
        /// glyph the other has and the pairing slips by one. That costs a stray extra speckle
        /// mid-dissolve and nothing after it, since the outgoing layer leaves whole at the end.</para>
        /// </remarks>
        internal static async Cysharp.Threading.Tasks.UniTask DissolveAsync(
            VisualElement outgoing, VisualElement incoming) {
            if (incoming == null || incoming.childCount == 0) {
                outgoing?.RemoveFromHierarchy();

                return;
            }

            int count = incoming.childCount;
            var order = new int[count];
            for (var i = 0; i < count; i++) {
                order[i] = i;
            }

            for (int i = count - 1; i > 0; i--) {
                int j = UnityEngine.Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            float started = Time.unscaledTime;
            var shown = 0;
            while (shown < count) {
                float progress = Mathf.Clamp01(
                    (Time.unscaledTime - started) / CipherPuzzleLayout.DissolveSeconds);
                int want = Mathf.CeilToInt(progress * count);
                for (; shown < want; shown++) {
                    int at = order[shown];
                    incoming[at].visible = true;
                    if (outgoing != null && at < outgoing.childCount) {
                        outgoing[at].visible = false;
                    }
                }

                if (shown >= count) {
                    break;
                }

                await Cysharp.Threading.Tasks.UniTask.Yield();
                if (incoming.panel == null) {
                    break;   // the screen closed under us
                }
            }

            outgoing?.RemoveFromHierarchy();

            // Whatever the ramp did not reach — the screen closed, or the last frame overshot —
            // still has to end up on screen rather than half-hidden.
            for (var i = 0; i < count; i++) {
                incoming[i].visible = true;
            }
        }

        /// <summary>
        /// Where one wheel's cell sits, in canonical space.
        /// </summary>
        /// <remarks>
        /// <b>The row is centred and sized from the FONT, not read out of the REQ.</b>
        /// <c>cipher_puzzle_layout_letters</c> overwrites every authored rect: a cell is the glyph
        /// box plus <see cref="CipherPuzzleLayout.CellPaddingVga"/>, cells step by that plus
        /// <see cref="CipherPuzzleLayout.ColumnGapVga"/>, and the whole row is centred across the
        /// screen at <see cref="CipherPuzzleLayout.RowTopVga"/>. So the wheels grow outwards from
        /// the middle as the word gets longer, which is the point of the screen and is exactly what
        /// the authored left-aligned rects do not do.
        ///
        /// <para>The padding and gap are the original's pixels and so are scaled into canonical
        /// space here; the glyph box is already canonical, because the font sprites are.</para>
        /// </remarks>
        private Rect ColumnBox(int column, int glyphWidth, int glyphHeight) {
            int padding = CipherPuzzleLayout.CellPaddingVga * Canonical.VgaScaleX;
            int gap = CipherPuzzleLayout.ColumnGapVga * Canonical.VgaScaleX;
            int cellWidth = glyphWidth + padding;
            int cellHeight = glyphHeight + (CipherPuzzleLayout.CellPaddingVga * Canonical.VgaScaleY);
            // The row is laid out for the WHOLE word, including any dead columns a space leaves —
            // the original steps x past a space rather than closing the gap up, so a word with one
            // keeps its letters where they would be if it were interactive throughout.
            int span = CipherPuzzleLayout.RowSpan(_puzzle.Width, cellWidth, gap);
            int startX = CipherPuzzleLayout.RowStartX(Canonical.Width, span);

            return new Rect(
                CipherPuzzleLayout.ColumnX(column, startX, cellWidth, gap),
                CipherPuzzleLayout.RowTopVga * Canonical.VgaScaleY,
                cellWidth, cellHeight);
        }

        /// <summary>
        /// Draws the letter each wheel is showing, from the real font.
        /// </summary>
        /// <remarks>
        /// <b>The glyphs come from PUZZLE.FNT / ALIEN.FNT as sprites, not from a TMP typeface.</b>
        /// Both fonts are indexed from character ZERO with 251 glyphs, which makes them symbol sets
        /// addressed by byte value rather than typefaces — the same shape as SPELL.FNT, and the
        /// reason the "convert them to font assets" step this screen waited on was never the right
        /// tool.
        ///
        /// <para>Having the real glyphs also retires this method's old approximation: it used to
        /// centre a label across the column box because the puzzle font had no widths on this side.
        /// <see cref="CipherPuzzleLayout.GlyphX"/> can now be used as written, measured lean and
        /// all.</para>
        ///
        /// <para><b>Which font is the legibility gate</b>
        /// (<see cref="CipherPuzzleLayout.FontForPass"/>): the alien script always, then the
        /// readable face for a party that can read it — see <see cref="PresentAsync"/>, which runs
        /// this twice and dissolves between the two.</para>
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTask<VisualElement> DrawLettersAsync() {
            VisualElement stage = CanonicalStage.Find(GetComponent<UIDocument>()?.rootVisualElement);
            if (stage == null || _resources == null || _puzzle == null) {
                return null;
            }

            string fontKey = CipherPuzzleLayout.FontForPass(firstPass: !_resolved, legible: Legible);
            var font = await _resources.GetOrLoadAsync<GameData.Resources.Font.FontResource>(fontKey);
            _palette ??= await _resources
                .GetOrLoadAsync<GameData.Resources.Palette.PaletteResource>(Palette);
            if (font == null) {
                _logger.LogError("PuzzleScreen: {Font} did not load; the wheels have no letters.",
                    fontKey);

                return null;
            }

            stage.Q<VisualElement>(LettersName)?.RemoveFromHierarchy();
            _columnBoxes.Clear();
            var layer = new VisualElement {
                name = LettersName,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };

            // Only the live columns are here at all: a space in the target word is switched off in
            // LightTheLiveColumns and so is not a nav widget — which is also why no dead column gets
            // a box, matching the original's skip on wEnable_gate.
            foreach (InputCore.NavWidget widget in _ui.CurrentNavWidgets) {
                int column = widget.ActionId - CipherPuzzleLayout.FirstColumnActionId;
                if (column < 0 || column >= _rows.Length) {
                    continue;
                }

                char letter = _puzzle.LetterAt(column, _rows[column]);
                Sprite body = GlyphSprite(font, fontKey, CipherPuzzleLayout.TextBodyPen, letter);
                if (body == null) {
                    continue;
                }

                // *** NOT widget.CanonicalRect. *** REQ_PUZL's authored column rects are a
                // placeholder the original overwrites before drawing anything: the data ships
                // fifteen columns marching from the left edge, so using them puts a short word in
                // the top-left corner instead of centred on the chest. See
                // CipherPuzzleLayout.AuthoredColumnRectsAreOverwritten.
                Rect box = ColumnBox(column, (int)body.rect.width, (int)body.rect.height);

                // *** MOVE THE CLICK AREA ONTO THE WHEEL. *** cipher_puzzle_layout_letters writes
                // the computed rect back into the menu entry (rect.x/.y/.width/.height); until this
                // call the port drew the wheels centred at y=87 and left their hit areas at the
                // authored rects marching from x=150, y=192 — so clicking a letter did nothing and
                // clicking bare plate to its left rotated one. Measured live before the fix:
                // hotspot_128 sat at (454, 237) while its letter was drawn near (880, 530).
                //
                // ponytail: position only. The authored 75x90 is close enough to the computed cell
                // that the size mismatch does not cost a click; give the loader a size setter if a
                // font ever makes that untrue.
                _ui.SetEntryPosition(CipherPuzzleLayout.ActionIdFor(column), box.x, box.y);
                _columnBoxes[column] = box;

                // The box first, so the letter lands on top of it.
                layer.Add(Bevel(box));

                // *** THE SAME THREE PASSES AS THE RIDDLE. *** The dial letter is drawn at y-1 in
                // pen 65, at y+1 in pen 149, then on the baseline in pen 16 — the identical emboss
                // TextPasses() already describes, which was written for the riddle and turns out to
                // govern the wheels too. Drawing it once leaves the letter flat against its box.
                foreach ((int yOffset, int pen) in CipherPuzzleLayout.TextPasses()) {
                    Sprite glyph = GlyphSprite(font, fontKey, pen, letter);
                    if (glyph == null) {
                        continue;
                    }

                    layer.Add(new VisualElement {
                        pickingMode = PickingMode.Ignore,
                        style = {
                            position = Position.Absolute,
                            left = CipherPuzzleLayout.GlyphX((int)box.x, (int)box.width,
                                (int)glyph.rect.width),
                            top = CipherPuzzleLayout.GlyphY((int)box.y, (int)box.height,
                                (int)glyph.rect.height) + (yOffset * Canonical.VgaScaleY),
                            width = glyph.rect.width,
                            height = glyph.rect.height,
                            backgroundImage = new StyleBackground(glyph),
                        },
                    });
                }
            }

            stage.Add(layer);

            return layer;
        }

        /// <summary>
        /// Whether the party can read the riddle.
        /// </summary>
        /// <remarks>
        /// <b>Gorath, or the spell that lends his reading to the rest.</b> The party check is on a
        /// CHARACTER ID scanned across the active party rather than a roster slot, so it does not
        /// matter which seat he is in; and the spell route is a slot of the running-effects mask,
        /// which is Union.
        ///
        /// <para>With no session — a screen driven straight from a test or a probe — it reads as
        /// legible, so the puzzle is still usable rather than being unreadable for a reason that
        /// has nothing to do with the party.</para>
        /// </remarks>
        private bool Legible {
            get {
                if (_session == null || !_session.IsActive) {
                    return true;
                }

                bool readerPresent = false;
                System.Collections.Generic.IReadOnlyList<byte> party = _session.ActivePartyIndices;
                for (var i = 0; party != null && i < party.Count; i++) {
                    if (party[i] == CipherPuzzleLayout.ReaderPartyMember) {
                        readerPresent = true;
                    }
                }

                bool spellActive = (_session.PaletteEventMask
                    & GameData.Resources.Spells.SpellPaletteEvents.BitFor(
                        CipherPuzzleLayout.ReaderSpellEvent)) != 0;

                return CipherPuzzleLayout.IsLegible(readerPresent, spellActive);
            }
        }

        /// <summary>A glyph's sprite, made once per FONT AND letter.</summary>
        /// <remarks>
        /// <b>The font has to be part of the key.</b> The two scripts share the whole character
        /// range, so a cache keyed on the letter alone hands back the script the party read last —
        /// which shows as the gate simply not working, with no error anywhere.
        /// </remarks>
        /// <summary>One glyph, in one pen, cached.</summary>
        /// <remarks>
        /// *** THE PEN IS PART OF THE KEY. *** The letter is drawn three times in three different
        /// pens (see <see cref="CipherPuzzleLayout.TextPasses"/>), so a key of font+letter alone
        /// hands the second and third passes the first pass's colour and the emboss silently
        /// collapses into one flat glyph.
        /// </remarks>
        private Sprite GlyphSprite(GameData.Resources.Font.FontResource font, string fontKey,
            int pen, char letter) {
            string key = fontKey + pen + letter;
            if (_glyphs.TryGetValue(key, out Sprite cached)) {
                return cached;
            }
            Sprite sprite = ResourceManagement.Converters.FontGlyphConverter.ToSprite(
                font, font.GlyphFor(letter), PaletteColors.ResolvePen(_palette, pen, Color.white),
                _palette);
            _glyphs[key] = sprite;

            return sprite;
        }

        /// <summary>
        /// The raised box a dial letter sits on — the thing that says the column turns.
        /// </summary>
        /// <remarks>
        /// <c>cipher_draw_bevelled_box</c>: a filled rect in
        /// <see cref="CipherPuzzleLayout.BevelFillPen"/> outlined in
        /// <see cref="CipherPuzzleLayout.BevelOutlinePen"/>, then the left, right and bottom edges
        /// overpainted in their own pens. UI Toolkit's per-side border colours are that shape
        /// exactly, so the whole box is one element.
        ///
        /// <para><b>The four corner pixels are deliberately not drawn.</b> The original tints each
        /// corner individually (pens 44/67/108/187); at canonical scale one VGA pixel is a 5x6
        /// block, so reproducing them would put four coloured squares on each tile — more visible
        /// than the original's, and reading as damage rather than as relief. Per the TASK-290
        /// decision the port is faithful-LOOKING and not pixel-exact, which is what makes this the
        /// right call rather than a shortcut.</para>
        /// </remarks>
        private VisualElement Bevel(Rect box) {
            Color Pen(int pen) => PaletteColors.ResolvePen(_palette, pen, Color.clear);

            return new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = box.x, top = box.y, width = box.width, height = box.height,
                    backgroundColor = Pen(CipherPuzzleLayout.BevelFillPen),
                    borderTopWidth = Canonical.VgaScaleY,
                    borderBottomWidth = Canonical.VgaScaleY,
                    borderLeftWidth = Canonical.VgaScaleX,
                    borderRightWidth = Canonical.VgaScaleX,
                    borderTopColor = Pen(CipherPuzzleLayout.BevelOutlinePen),
                    borderLeftColor = Pen(CipherPuzzleLayout.BevelLeftPen),
                    borderRightColor = Pen(CipherPuzzleLayout.BevelRightPen),
                    borderBottomColor = Pen(CipherPuzzleLayout.BevelBottomPen),
                },
            };
        }

        private readonly System.Collections.Generic.Dictionary<string, Sprite> _glyphs = new();

        /// <summary>
        /// Rolls one wheel from the letter it was showing to the next.
        /// </summary>
        /// <remarks>
        /// <b>A slot machine, not a swap.</b> The original draws BOTH letters at once — the
        /// outgoing one and the incoming one stacked <see cref="CipherPuzzleLayout.RollTravel"/>
        /// above it — clipped to the column's inset box, and slides the pair down a pixel a frame
        /// (0x79455-0x7954c). So the old letter falls out of the bottom while the new one arrives
        /// from the top, and the box's inset is what does the clipping.
        ///
        /// <para>The whole ring is redrawn at the end rather than the rolled column patched, so a
        /// roll interrupted by another click cannot leave two letters in one box.</para>
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTaskVoid RollColumnAsync(int column,
            char outgoing, char incoming) {
            VisualElement stage = CanonicalStage.Find(GetComponent<UIDocument>()?.rootVisualElement);
            Rect box = BoxOf(column);
            if (stage == null || box.width <= 0f) {
                return;   // the letters are already painted; there is simply nothing to animate
            }

            (int x, int y, int right, int bottom) = CipherPuzzleLayout.BevelRect(
                (int)box.x, (int)box.y, (int)box.width, (int)box.height);
            // Named per COLUMN: a second click on the same wheel before the first roll finishes
            // must replace that column's window, not add another one on top of it. Sharing one
            // name across columns let concurrent rolls stack and left stray letters on screen.
            string windowName = LettersName + "Roll" + column;
            stage.Q<VisualElement>(windowName)?.RemoveFromHierarchy();
            var window = new VisualElement {
                name = windowName,
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = x, top = y, width = right - x, height = bottom - y,
                    overflow = Overflow.Hidden,   // the inset box IS the clip
                },
            };
            string fontKey = CipherPuzzleLayout.FontForPass(firstPass: !_resolved, legible: Legible);
            var font = await _resources.GetOrLoadAsync<GameData.Resources.Font.FontResource>(fontKey);
            if (font == null) {
                return;   // the redraw will still land the new letter; only the roll is lost
            }

            VisualElement falling = RollGlyph(font, fontKey, outgoing, box);
            VisualElement arriving = RollGlyph(font, fontKey, incoming, box);
            window.Add(arriving);
            window.Add(falling);
            stage.Add(window);

            // *** THE LOOP COUNTS VGA PIXELS, NOT CANONICAL ONES. *** The original steps one screen
            // pixel per frame on a 320x200 display, so a ten-pixel font rolls in about thirteen
            // frames. Counting frames in CANONICAL pixels instead makes the same distance take
            // fifty-seven — the wheel still lands correctly and just crawls, which is the sort of
            // wrong that looks deliberate.
            int fontHeightVga = Book.BakFontData.GameFontHeight;
            int frames = CipherPuzzleLayout.RollFrames(fontHeightVga);
            int travel = CipherPuzzleLayout.RollTravel(fontHeightVga) * Canonical.VgaScaleY;
            for (var frame = 0; frame <= frames; frame++) {
                float at = travel * (frame / (float)frames);
                falling.style.top = at;
                arriving.style.top = at - travel;
                await Cysharp.Threading.Tasks.UniTask.Yield();
                if (_puzzle == null || window.panel == null) {
                    break;   // the screen went away, or a newer roll replaced this window
                }
            }

            window.RemoveFromHierarchy();
        }

        /// <summary>One rolling letter, as the sprite the redraw will paint.</summary>
        /// <remarks>
        /// <b>The roll has to use the same glyphs as everything else.</b> This was a TMP
        /// <c>Label</c>, which meant a game-font letter slid down the wheel and a PUZZLE.FNT sprite
        /// landed at the end of it — the face changing mid-animation. Nothing showed it because the
        /// wheels only became sprites when the real fonts were wired up.
        /// </remarks>
        private VisualElement RollGlyph(GameData.Resources.Font.FontResource font, string fontKey,
            char letter, Rect box) {
            Sprite glyph = GlyphSprite(font, fontKey, CipherPuzzleLayout.TextBodyPen, letter);
            if (glyph == null) {
                return new VisualElement { pickingMode = PickingMode.Ignore };
            }

            return new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = CipherPuzzleLayout.GlyphX(0, (int)box.width, (int)glyph.rect.width),
                    width = glyph.rect.width,
                    height = glyph.rect.height,
                    backgroundImage = new StyleBackground(glyph),
                },
            };
        }

        /// <summary>The column's canonical box, or an empty rect when it is not live.</summary>
        /// <remarks>
        /// Reads the box the LETTERS were drawn in, not the widget's rect. A NavWidget captures its
        /// rect when the widget list is built, so it still reports the authored position after
        /// <see cref="UserInterfaceLoader.SetEntryPosition"/> moves the element — which would put
        /// the roll animation's window somewhere the wheel is not.
        /// </remarks>
        private Rect BoxOf(int column) {
            if (_columnBoxes.TryGetValue(column, out Rect drawn)) {
                return drawn;
            }

            int actionId = CipherPuzzleLayout.ActionIdFor(column);
            foreach (InputCore.NavWidget widget in _ui.CurrentNavWidgets) {
                if (widget.ActionId == actionId) {
                    return widget.CanonicalRect;
                }
            }

            return default;
        }

        /// <summary>Where each wheel was actually drawn, keyed by column.</summary>
        private readonly System.Collections.Generic.Dictionary<int, Rect> _columnBoxes = new();

        /// <summary>Left click — rotate that wheel one row on.</summary>
        public void PrimaryAction(int actionId) {
            if (_solving) {
                return;   // the lock is already opening; the screen answers when it finishes
            }

            if (actionId == ButtonExit) {
                // Leaving an unsolved puzzle is a refusal, not a failure: the chest stays shut and
                // the player can come back to it.
                _outcome?.TrySetResult(IsSolved);

                return;
            }

            int column = actionId - CipherPuzzleLayout.FirstColumnActionId;
            if (_puzzle == null || column < 0 || column >= _rows.Length) {
                return;
            }

            char outgoing = _puzzle.LetterAt(column, _rows[column]);
            _rows[column] = CipherPuzzleLayout.NextRow(_rows[column], _puzzle.DialRows.Count);

            // Repaint from state FIRST, then animate over it. The roll used to own the repaint,
            // which meant a second click while the first was still rolling could let the older
            // roll finish last and paint a letter the wheel had already left — the state was
            // right and the screen was one turn behind. The animation is decoration; the ring is
            // the truth.
            DrawLetters();
            RollColumnAsync(column, outgoing, _puzzle.LetterAt(column, _rows[column])).Forget();

            if (IsSolved) {
                _logger.LogInformation("Puzzle solved: {Target}.", _puzzle.Target);
                PlaySolveSequenceAsync().Forget();
            }
        }

        /// <summary>The lock giving way — two bolts, unequally spaced.</summary>
        /// <remarks>
        /// <b>The outcome is delivered at the END of this, not on the solving click.</b> That is the
        /// original's order: <c>UI_RunCipherPuzzle</c> waits, sounds a bolt, animates it, waits
        /// again and sounds the second before it leaves, so whatever the puzzle guards opens when
        /// the mechanism finishes rather than the instant the last letter lines up. Completing the
        /// outcome first would leave the sequence playing over a screen that had already gone.
        ///
        /// <para><b>A bolt is a sound AND a sprite.</b> Each cue is followed immediately by a blit
        /// of one of PUZZLE.BMX's two images over the lock plate PUZZLE.SCX paints shut — see
        /// <see cref="CipherPuzzleLayout.LatchOriginsVga"/>. This screen played the two cues and
        /// drew nothing, so the chest was heard opening while it still looked locked.</para>
        ///
        /// <para>Then a third, longer hold before the outcome, which is what carries the closing
        /// line past the moment the second latch lands rather than over it.</para>
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTaskVoid PlaySolveSequenceAsync() {
            _solving = true;
            (int X, int Y)[] latches = CipherPuzzleLayout.LatchOriginsVga();
            var bolt = 0;
            foreach (double delay in CipherPuzzleSound.BoltDelaysSeconds) {
                await Cysharp.Threading.Tasks.UniTask.Delay(
                    System.TimeSpan.FromSeconds(delay), ignoreTimeScale: false);
                if (this == null || _puzzle == null) {
                    return;
                }
                Audio.MenuSoundService.Instance?.Play(CipherPuzzleSound.BoltCue);
                if (bolt < latches.Length) {
                    ShowLatch(bolt, latches[bolt]);
                }
                bolt++;
            }

            await Cysharp.Threading.Tasks.UniTask.Delay(
                System.TimeSpan.FromSeconds(CipherPuzzleSound.AfterBoltsSeconds),
                ignoreTimeScale: false);
            if (this == null) {
                return;
            }

            _outcome?.TrySetResult(true);
        }

        /// <summary>Whether the lock is already giving way, so the wheels are no longer live.</summary>
        /// <remarks>
        /// The original leaves its input loop the moment <c>cipher_puzzle_is_solved</c> returns 1
        /// and only then runs the bolts, so the word cannot be taken apart while the chest opens.
        /// Ours keeps the screen up for those few seconds, and without this a click would roll a
        /// wheel off the answer under a lock that still opens.
        /// </remarks>
        private bool _solving;

        /// <summary>Paints one opened latch over the plate PUZZLE.SCX draws shut.</summary>
        /// <remarks>
        /// The sprite carries its own size — <see cref="ArchiveImage"/> sizes to it — so only the
        /// origin is placed, scaled out of the original's VGA pixels.
        /// </remarks>
        private void ShowLatch(int index, (int X, int Y) originVga) {
            VisualElement stage = CanonicalStage.Find(GetComponent<UIDocument>()?.rootVisualElement);
            if (stage == null) {
                return;
            }

            string name = LatchName + index;
            stage.Q<VisualElement>(name)?.RemoveFromHierarchy();
            stage.Add(new ArchiveImage($"{CipherPuzzleLayout.LatchImageSet}#{index}") {
                name = name,
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = originVga.X * Canonical.VgaScaleX,
                    top = originVga.Y * Canonical.VgaScaleY,
                },
            });
        }

        /// <summary>Takes any latch left over from a previous puzzle back off the stage.</summary>
        private void ClearLatches() {
            VisualElement stage = CanonicalStage.Find(GetComponent<UIDocument>()?.rootVisualElement);
            for (var i = 0; stage != null && i < CipherPuzzleSound.Bolts; i++) {
                stage.Q<VisualElement>(LatchName + i)?.RemoveFromHierarchy();
            }
        }

        /// <summary>The exit button — REQ_PUZL's only ordinary widget.</summary>
        private const int ButtonExit = 18;

        /// <summary>Right click — the original describes nothing here.</summary>
        public Awaitable SecondaryAction(int actionId) => null;
    }
}
