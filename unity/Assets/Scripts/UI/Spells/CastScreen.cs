namespace BakAgain.UI.Spells {
    using BakAgain.CutScenes;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using BakAgain.Graphics;
    using GameData.Resources.Font;
    using GameData.Resources.Spells;
    using Microsoft.Extensions.Logging;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The spell-casting screen — the ring, its school anchors, and (later) the spell symbols and
    /// info panel.
    ///
    /// <para>The seven buttons come free from <see cref="UserInterfaceLoader"/>; what this adds is
    /// the part the REQ does not describe. The ring's icons are <b>bitmaps</b> from
    /// <see cref="CastRingLayout.IconSet"/>, while the spell symbols are <b>font glyphs</b> — the two
    /// halves of this screen are drawn from different kinds of resource, which is worth remembering
    /// when the symbol half lands.</para>
    /// </summary>
    public class CastScreen : BakAgain.UI.Navigation.ScreenBase, IActionHandler,
        BakAgain.UI.InputCore.IUnmatchedScancodeHandler {
        /// <summary>M (0x32) ends the cast screen outright (CSPELL.C:2285) — TASK-809.</summary>
        public bool OnUnmatchedScancode(int scancode) {
            if (scancode != 0x32) {
                return false;
            }
            if (_sliderSpell >= 0) {
                CancelPowerSelection();
            }
            _navigator?.PopUnfaded().Forget();
            return true;
        }

        private const string RingClass = "cast-ring-icon";
        private const string InfoClass = "cast-info-line";
        private const string ChromeClass = "cast-chrome";

        /// <summary>The full HUD chrome, shared with the travel HUD and the overhead map.</summary>
        private const string ChromeKey = "FRAME.SCR";

        /// <summary>
        /// The canonical Y below which this screen shows the ordinary HUD chrome.
        ///
        /// <para><b>Measured from the original, not from the asset.</b> Comparing the original's
        /// cast screen against its own travel HUD row by row, the two are pixel-identical from
        /// y=680 down (mean delta 0.0 across 680..720) and differ above it — so the cast panel
        /// covers the world-view rect and nothing more. That rect is (65,66,1470,606), i.e. y
        /// 66..672, which is where this lands.</para>
        ///
        /// <para>CAST.SCX's own opaque art actually runs to about y=780, a strip of stone ledge
        /// that simply repeats what FRAME.SCR already has there. Splitting at <i>that</i> edge is
        /// the obvious mistake and it cuts the compass diamond in half, because the diamond belongs
        /// to FRAME.SCR and straddles the line. Split where the original stops overlaying instead
        /// and the diamond arrives whole.</para>
        /// </summary>
        private const int ChromeSplitY = 676;

        private ILogger _logger;
        private IResourceCache _resources;
        private UI.InputCore.IPointer _pointer;
        private BakAgain.UI.Navigation.IScreenNavigator _navigator;
        private IDialogManager _dialogs;
        private Core.GameSession _session;
        private UserInterfaceLoader _ui;
        private BakAgain.UI.InGame.PartyHeadsView _heads;
        private BakAgain.UI.InGame.CompassView _compass;
        private ResourceManagement.IResourceProviderService _providers;
        private CastRing _ring;
        private SpellDescriptions _descriptions;
        private GameData.Resources.Spells.SpellList _spells;
        private int _sliderSpell = -1;
        private int _minimumPower;
        private int _maximumPower;
        private int _hoveredPosition = -1;
        private int _school = -1;

        /// <summary>The school the ring is leaving, for the sigil morph; -1 when nothing is morphing.</summary>
        private int _morphFrom = -1;

        /// <summary>
        /// The icon the ring is drawn with. The original passes this per call; the screen uses one.
        /// </summary>
        [SerializeField]
        private int baseIcon;

        [VContainer.Inject]
        public void Construct(IResourceCache resources, UI.InputCore.IPointer pointer,
            BakAgain.UI.Navigation.IScreenNavigator navigator, IDialogManager dialogs,
            Core.GameSession session, ResourceManagement.IResourceProviderService providers) {
            _resources = resources;
            _pointer = pointer;
            _navigator = navigator;
            _dialogs = dialogs;
            _session = session;
            _providers = providers;
            _logger = Core.LogManager.LoggerFactory.CreateLogger<CastScreen>();
        }

        private void Awake() => _ui = GetComponent<UserInterfaceLoader>();

        /// <summary>
        /// The combatant casting, or null for a field cast — <b>set before the screen is pushed</b>.
        /// </summary>
        /// <remarks>
        /// <b>This is the whole discriminator, and it picks the REQ.</b>
        /// <c>cspell_cast_menu_loop</c> branches on the caster carrying combat data and loads
        /// <c>spell.dat</c> if it does, <c>req_cast.dat</c> if it does not (CSPELL.C:2173-2178) —
        /// one screen in its two call contexts, chosen by what is casting rather than by where.
        ///
        /// <para><b>The two layouts are not cosmetic variants: they enable different school
        /// buttons.</b> SPELL.DAT lights schools 0-3 and disables 4 and 5; REQ_CAST.DAT does the
        /// exact opposite. So the layout is what makes a spell reachable at all, and opening the
        /// field one inside a fight offers the field's spells to a combatant — which is what this
        /// port did until the two lists were compared side by side (TASK-367).</para>
        ///
        /// <para>Assigning it writes the address straight through to the loader, because the loader
        /// reads it in <c>OnEnable</c> and the navigator's <c>Push</c> is what enables it. Setting
        /// this after the push is too late and <see cref="UserInterfaceLoader.Address"/> says so
        /// out loud.</para>
        /// </remarks>
        public GameData.Resources.Combat.Combatant CombatCaster {
            get => _combatCaster;
            set {
                _combatCaster = value;
                if (_ui == null) {
                    _ui = GetComponent<UserInterfaceLoader>();
                }
                if (_ui != null) {
                    _ui.Address = CastMenuSelection.LayoutFor(value != null).ToUpperInvariant();
                }
            }
        }

        private GameData.Resources.Combat.Combatant _combatCaster;


        private void OnEnable() {
            if (_ui == null) {
                return;
            }

            _ui.Built += OnBuilt;
            CoverWithSnapshot();

            // Reconcile against IsBuilt, not just the event: the loader's build is cached and async,
            // so it can finish BEFORE a sibling component subscribes and the event never arrives.
            // MenuLayerHost solves the same problem the same way — subscribing alone left the ring
            // undrawn with the buttons already up and nothing logged to say why.
            //
            // Route through OnBuilt rather than repeating part of it. This path used to call
            // DrawRingAsync directly, so anything ADDED to OnBuilt silently did not happen on a
            // cached build — which is exactly how the party portraits came up missing.
            if (_ui.IsBuilt) {
                OnBuilt(_ui.CurrentNavWidgets);
            }
        }

        /// <summary>
        /// Closing the screen is what makes the selection sticky.
        /// </summary>
        /// <remarks>
        /// <b>The original writes both back whether or not a spell was cast</b> —
        /// <c>*preferred_caster_slot = casterSlot; *preselect_spell = school;</c> after the loop
        /// ends (CSPELL.C:2416-2420), on every exit including the Exit button. So browsing the
        /// schools and backing out still changes what you see next time, which is the behaviour
        /// this reproduces.
        ///
        /// <para><b>Combat deliberately does not do this.</b> The combat caller passes no pointers
        /// at all, so a combat cast neither reads nor writes the pair — see
        /// <see cref="CastMenuSelection.OpeningSchool"/>. The guard is the caster having come from
        /// the party roster: a combatant is not in it, so its slot resolves to -1 and nothing is
        /// stored, and the overworld's selection cannot leak into a fight or back.</para>
        /// </remarks>
        private void OnDisable() {
            RememberSelection();
            EndOpenWipe();
            _fillFrame = -1;
            // A commit pops the screen without clearing the slider (only a cancel did), so the next
            // open came up still choosing power for the last spell: no symbol could be hovered or
            // picked, and Exit reported a cancel for a spell never chosen. Seen on Android, Owyn's
            // second cast of a fight.
            _sliderSpell = -1;
            _hoveredSpell = -1;
            _hoveredPosition = -1;
            // Both halves of "this open is over". CombatCaster going null puts the FIELD layout
            // back for whoever opens next, which is why no field caller has to remember to; and
            // CasterId has to go with it, because ApplyInitialSelectionAsync is what re-resolves
            // the school and it early-returns while a caster is still set. Leaving either behind
            // opens the next screen on the previous context's school, whose button the layout
            // showing has disabled.
            CombatCaster = null;
            CasterId = -1;
            if (_ui != null) {
                _ui.Built -= OnBuilt;
            }
            _heads?.Dispose();
            _heads = null;
            _compass = null;
            // Nulled before disposing so an in-flight AttachCombatPanelAsync sees it has been
            // superseded and does not draw into a torn-down panel.
            BakAgain.UI.Combat.HudParchmentPanelView panel = _combatPanel;
            _combatPanel = null;
            panel?.Dispose();
        }

        /// <inheritdoc cref="OnDisable"/>
        private void RememberSelection() {
            if (CombatCaster != null) {
                if (_school >= 0) {
                    CombatCaster.SpellSchool = _school;
                }

                return;
            }

            if (_session?.IsActive != true) {
                return;
            }

            int slot = CasterSlot();
            if (slot < 0 || _school < 0) {
                return;
            }

            _session.CastMenuCasterSlot = slot;
            _session.CastMenuSchool = _school;
        }

        private void OnBuilt(IReadOnlyList<BakAgain.UI.InputCore.NavWidget> widgets) {
            // The loaders create the stage after OnEnable laid the cover, so lift it back on top.
            _wipeCover?.BringToFront();
            OpenThenWipeAsync().Forget();
            // *** IN A FIGHT THE STRIP BELONGS TO THE COMBAT HUD, NOT THE TRAVEL HUD. ***
            // The original overlays only the world-view rect (see ChromeSplitY) and leaves whatever
            // was beneath it showing: in the field that is the travel HUD's heads and compass, in a
            // fight it is the acting caster's portrait and stat box. The port drew the field
            // furniture unconditionally, so it painted over a panel the original keeps (TASK-368).
            //
            // The chrome itself stays drawn in BOTH cases. FRAME.SCR comes from this screen, because
            // the navigator deactivates InGameScreen when the cast screen is pushed — gating it out
            // as well is what turned the whole strip black on the first attempt at this.
            if (CombatCaster != null) {
                AttachCombatPanel();
            } else {
                AttachPartyHeads();
                AttachCompass();
            }
        }

        /// <summary>
        /// Draws the acting caster's portrait and stat box, as the combat HUD does beneath.
        /// </summary>
        /// <remarks>
        /// Nothing new is built here: <see cref="BakAgain.UI.Combat.HudParchmentPanelView"/> is the
        /// same view <c>InGameScreen</c> uses, and the content comes from the same two seams
        /// <c>WorldRuntime</c> already hands it — so the panel cannot drift from the one the fight
        /// shows between actions.
        ///
        /// <para>The content seam is called with <c>(-1, false)</c> — no hovered combatant — which is
        /// exactly the arm <c>HotspotService.CombatPanelContent</c> documents as "the acting
        /// character's stats whenever nothing more specific applies". That is what the original's
        /// cast screen shows: the caster, not a target preview.</para>
        /// </remarks>
        private void AttachCombatPanel() {
            VisualElement stage = Stage();
            if (stage == null || _providers == null) {
                return;
            }

            // Re-opening runs OnBuilt again against the same persistent stage, so drop the previous
            // panel rather than stacking a second one — the same reason AttachCompass does.
            stage.Q<VisualElement>(BakAgain.UI.Combat.HudParchmentPanelView.PanelName)
                ?.RemoveFromHierarchy();

            _combatPanel?.Dispose();
            _combatPanel = new BakAgain.UI.Combat.HudParchmentPanelView(_providers, _logger);
            AttachCombatPanelAsync(stage).Forget();
        }

        private async UniTask AttachCombatPanelAsync(VisualElement stage) {
            BakAgain.UI.Combat.HudParchmentPanelView panel = _combatPanel;
            await panel.BuildAsync(stage, this);
            // The screen can close while the build is in flight; _combatPanel is nulled there.
            if (panel != _combatPanel) {
                return;
            }
            // *** THE CASTER, NOT WHOEVER THE FIGHT CALLS "ACTING". *** The first cut fed this from
            // HotspotService.CombatPanelContent(-1, false), the same seam the combat HUD uses. Driven
            // in a live fight with Owyn casting, it drew JAMES — name, portrait and all four stats —
            // because that seam answers for `Encounter.Current`, which is not necessarily the
            // combatant this screen was opened for. The original's cast screen shows the caster's own
            // panel, so read it off CombatCaster and there is nothing to disagree with.
            var stats = _casterStats?.Invoke(CombatCaster);
            if (stats.HasValue) {
                panel.Show(GameData.Resources.Combat.ActorStatsPanel.Lines(
                    stats.Value.Name, stats.Value.Values));
            }
            panel.ShowPortrait(CombatCaster?.ClassId ?? -1);
        }

        private BakAgain.UI.Combat.HudParchmentPanelView _combatPanel;
        private System.Func<GameData.Resources.Combat.Combatant,
            (string Name, IReadOnlyList<int> Values)?> _casterStats;

        /// <summary>
        /// The caster's name and stat values, handed over with the rest of the fight's seams.
        /// </summary>
        /// <remarks>
        /// This is <c>CombatRuntime.ActorStatsFor</c>, the same call that feeds the combat HUD's own
        /// panel — so the two cannot render different numbers for the same character.
        /// </remarks>
        internal void SetCasterStatsSeam(
            System.Func<GameData.Resources.Combat.Combatant,
                (string Name, IReadOnlyList<int> Values)?> casterStats) =>
            _casterStats = casterStats;

        /// <summary>
        /// Draw the ring, then open on a caster and a school the way the original does.
        /// </summary>
        /// <remarks>
        /// <b>Select first, draw once.</b> The ring and the spell list are both drawn for a
        /// particular caster, so the caster has to be resolved before either runs.
        /// </remarks>
        private async UniTask OpenAsync() {
            await ApplyInitialSelectionAsync();
            await DrawRingAndNamesAsync();
        }

        private async UniTask OpenThenWipeAsync() {
            await OpenAsync();
            await PlayOpenWipeAsync();
        }

        // ------------------------------------------------------------ the opening wipe (TASK-815)

        private Texture2D _openingSnapshot;
        private VisualElement _wipeCover;
        private readonly List<VisualElement> _wipeCurtains = new();

        /// <summary>
        /// Raises the screen the way the original opens it: a copy of what was showing, then the
        /// panel revealed over the world view from the centre outward
        /// (<see cref="CastOpenWipe"/>). Openers call this instead of pushing directly.
        /// </summary>
        /// <remarks>
        /// <b>The copy has to be taken before the push.</b> Pushing deactivates the travel HUD,
        /// whose RenderTexture is the world view, so afterwards there is nothing left to reveal the
        /// panel over. The original does the same thing in its own terms: it copies the front page
        /// into the back page before the cast panel is drawn (CSPELL.C:2114).
        /// </remarks>
        public async UniTask PushAsync(BakAgain.UI.Navigation.IScreenNavigator navigator) {
            await Awaitable.EndOfFrameAsync();
            DestroySnapshot();
            // The screen's alpha is not coverage — the panel leaves it at zero — so the capture is
            // copied into an opaque texture, or the cover draws as a hole.
            // Fixed in place rather than copied: a phone-sized screen is millions of pixels, and a
            // managed copy of them was most of the time the opening took on Android (TASK-818).
            _openingSnapshot = ScreenCapture.CaptureScreenshotAsTexture();
            if (_openingSnapshot.format == TextureFormat.RGBA32) {
                Unity.Collections.NativeArray<byte> raw = _openingSnapshot.GetRawTextureData<byte>();
                for (int i = 3; i < raw.Length; i += 4) {
                    raw[i] = 255;
                }
                _openingSnapshot.Apply(false);
            }
            await navigator.PushUnfaded(this);
        }

        /// <summary>The whole screen as it was, over everything, until the wipe takes over.</summary>
        private void CoverWithSnapshot() {
            VisualElement root = GetComponent<UIDocument>()?.rootVisualElement;
            if (_openingSnapshot == null || root == null) {
                return;
            }
            _wipeCover = SnapshotImage(0, 0, root.layout.width, root.layout.height, fill: true);
            root.Add(_wipeCover);
        }

        private async UniTask PlayOpenWipeAsync() {
            VisualElement root = GetComponent<UIDocument>()?.rootVisualElement;
            VisualElement stage = Stage();
            if (_openingSnapshot == null || root == null || stage == null) {
                EndOpenWipe();
                return;
            }
            // A cached build can finish before the panel's first layout pass; the rect is NaN until then.
            Rect wb = stage.worldBound;
            for (int wait = 0; wait < 30 && (float.IsNaN(wb.width) || float.IsNaN(root.layout.width)); wait++) {
                await UniTask.Yield();
                wb = stage.worldBound;
            }
            if (float.IsNaN(wb.width) || wb.width <= 0 || _openingSnapshot == null) {
                EndOpenWipe();
                return;
            }

            // Canonical -> panel. The cover and curtains live on the root, in panel space, so the
            // snapshot (a screen image) lines up with the screen whatever the stage's fit.
            float sx = wb.width / Canonical.Width;
            float sy = wb.height / Canonical.Height;
            float rootW = root.layout.width;
            float rootH = root.layout.height;
            (int rx, int ry, int rw, int rh) = CastOpenWipe.CanonicalRect;
            float top = wb.y + ry * sy;
            float height = rh * sy;

            // The party bar and the frame outside the rect arrive at once; only the rect wipes.
            _wipeCover?.RemoveFromHierarchy();
            _wipeCover = null;
            VisualElement left = Curtain(root, rootW, rootH);
            VisualElement right = Curtain(root, rootW, rootH);

            // Paced by elapsed time, not by one step per wait: a wait is at least a frame, and at a
            // phone's frame rate 49 frame-long steps took over a second (TASK-818).
            float start = Time.unscaledTime;
            for (int step = 1; step < CastOpenWipe.StepCount && _openingSnapshot != null;) {
                (int bx, int bw) = CastOpenWipe.RevealedBand(step);
                PlaceCurtain(left, wb.x + rx * sx, top, (bx - rx) * sx, height);
                PlaceCurtain(right, wb.x + (bx + bw) * sx, top, (rx + rw - bx - bw) * sx, height);
                await UniTask.Yield();
                step = 1 + (int)((Time.unscaledTime - start) * CastOpenWipe.TicksPerSecond);
            }
            EndOpenWipe();
        }

        private VisualElement Curtain(VisualElement root, float rootW, float rootH) {
            var curtain = new VisualElement {
                name = "cast_wipe_curtain",
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, overflow = Overflow.Hidden },
            };
            curtain.Add(SnapshotImage(0, 0, rootW, rootH, fill: false));
            root.Add(curtain);
            _wipeCurtains.Add(curtain);
            return curtain;
        }

        private static void PlaceCurtain(VisualElement curtain, float x, float y, float w, float h) {
            curtain.style.left = x;
            curtain.style.top = y;
            curtain.style.width = Mathf.Max(0f, w);
            curtain.style.height = h;
            // The snapshot inside stays pinned to the screen while its window moves.
            VisualElement image = curtain[0];
            image.style.left = -x;
            image.style.top = -y;
        }

        private VisualElement SnapshotImage(float x, float y, float w, float h, bool fill) {
            var image = new VisualElement {
                name = "cast_wipe_snapshot",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    backgroundImage = Background.FromTexture2D(_openingSnapshot),
                    backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100)),
                },
            };
            if (fill) {
                image.style.left = 0; image.style.top = 0; image.style.right = 0; image.style.bottom = 0;
            } else {
                image.style.left = x; image.style.top = y; image.style.width = w; image.style.height = h;
            }
            return image;
        }

        private void EndOpenWipe() {
            _wipeCover?.RemoveFromHierarchy();
            _wipeCover = null;
            foreach (VisualElement curtain in _wipeCurtains) {
                curtain.RemoveFromHierarchy();
            }
            _wipeCurtains.Clear();
            DestroySnapshot();
        }

        private void DestroySnapshot() {
            if (_openingSnapshot != null) {
                Destroy(_openingSnapshot);
                _openingSnapshot = null;
            }
        }

        /// <summary>
        /// The ring and the panel beneath it — <b>one redraw, not two</b>.
        /// </summary>
        /// <remarks>
        /// <b>These have drifted apart twice.</b> The panel's resting state is the caster's
        /// castable spells IN THE CURRENT SCHOOL, so it is exactly as stale as the ring is:
        /// CSPELL.C:2322 draws the info panel only while a castable symbol is hovered and calls
        /// <c>cspell_list_draw_castable</c> otherwise, so the original's panel is never blank.
        /// First the open path drew neither and came up empty; then the open path was fixed and the
        /// SCHOOL SWITCH was left drawing only the ring, so choosing a school showed its symbol on
        /// the ring with no name under it. Measured against the original with Owyn on SYMBOL5,
        /// which names "Scent of Sarig" where we showed nothing.
        ///
        /// <para>They are one method now so a third caller cannot forget the second half. The order
        /// is load-bearing: the list walks <c>_drawnSymbols</c>, which the ring draw fills.</para>
        /// </remarks>
        private async UniTask DrawRingAndNamesAsync() {
            await DrawRingAsync();
            await ShowSpellNamesAsync();
        }

        /// <summary>
        /// Open the screen on a caster and a school, as the original does with no further input.
        /// </summary>
        /// <remarks>
        /// <b>The original remembers, it does not compute.</b> WORLDLP.C's cast case reaches
        /// <c>cspell_cast_menu_loop</c> with <c>&amp;g_gameState.nSpellMenuCasterSlot</c> and
        /// <c>&amp;nSpellMenuPreselect</c>, so the caster and spell are persisted state — which is
        /// why the shipped probe save opens on Owyn with "Scent of Sarig" rather than on whoever
        /// happens to be first.
        ///
        /// <para>Both come from the save, through
        /// <see cref="GameSession.CastMenuCasterSlot"/> and <see cref="GameSession.CastMenuSchool"/>
        /// — body offsets 1622/1624, which the parser has always read but nothing consumed. The
        /// -1/-1 sentinel <c>savegame_chapter_start_dispatch</c> writes at every chapter start falls
        /// back to the first caster and the default school, which is what a fresh chapter should
        /// open on.</para>
        ///
        /// <para>Resolution itself is not decided here: <see cref="CastMenuSelection"/> already
        /// carries the original's rules, including that a remembered slot is honoured only while
        /// that character can still cast.</para>
        /// </remarks>
        private async UniTask ApplyInitialSelectionAsync() {
            if (CasterId >= 0 || _session?.IsActive != true) {
                return;
            }

            // A fight resolves both halves from the combatant, and neither from the save. The
            // caster is the one whose turn it is — not the party's remembered slot — and the school
            // is the combatant's own, so a cast mid-fight cannot read or move the overworld's
            // sticky pair in either direction. See CastMenuSelection.OpeningSchool.
            if (CombatCaster != null) {
                CasterId = CombatCaster.ClassId;
                _school = CastMenuSelection.OpeningSchool(true, CombatCaster.SpellSchool,
                    CastMenuSelection.None);
                await LoadSymbolsAsync();

                return;
            }

            IReadOnlyList<byte> roster = _session.ActivePartyIndices;
            if (roster == null || roster.Count == 0) {
                return;
            }

            var canCast = new bool[roster.Count];
            for (var slot = 0; slot < roster.Count; slot++) {
                canCast[slot] = CanCast(roster[slot]);
            }

            int caster = CastMenuSelection.ResolveCasterSlot(_session.CastMenuCasterSlot, canCast);
            if (caster < 0) {
                // A party with no caster is offered nothing but the way out, which is the
                // original's behaviour rather than an error.
                return;
            }

            // Set the state directly rather than through SelectCaster/SelectSchool: both of those
            // redraw, and the whole point here is that the caller draws ONCE afterwards with
            // everything already decided. They also carry click-time behaviour that does not belong
            // on an open — SelectCaster plays the "not a spellcaster" dialog, which nobody asked for.
            CasterId = roster[caster];
            _school = CastMenuSelection.ResolveSchool(_session.CastMenuSchool);
            await LoadSymbolsAsync();
        }

        private async UniTask LoadSymbolsAsync() {
            Symbols = await _resources.GetOrLoadAsync<SpellSymbolLayout>($"SYMBOL{_school + 1}.DAT");
            if (Symbols == null) {
                _logger.LogError("CastScreen: SYMBOL{File}.DAT did not load on open.", _school + 1);
            }
        }

        /// <summary>
        /// Draws the frame's scrolling compass, exactly as the travel HUD does.
        /// </summary>
        /// <remarks>
        /// The compass lives in the frame's bottom strip, which this screen shares — so it is the
        /// same furniture as the portraits, from the same synthesized REQ rect
        /// (<see cref="GameData.Resources.Menu.UserInterface.CompassWindowActionId"/>) and the same
        /// <see cref="BakAgain.UI.InGame.CompassView"/>. It is live here rather than a frozen copy of
        /// whatever heading the travel screen last drew: the original leaves the pixels behind
        /// because it has one framebuffer, which is not a behaviour worth reproducing.
        /// </remarks>
        private void AttachCompass() {
            if (_session == null || _providers == null || _ui == null) {
                return;
            }

            VisualElement stage = Stage();
            if (stage == null || !_ui.TryGetElementRect(
                    GameData.Resources.Menu.UserInterface.CompassWindowActionId, out Rect rect)) {
                return;
            }

            // Showing the screen again re-runs the build against the same persistent stage, so drop
            // the previous window rather than stacking a second compass on top of the first.
            stage.Q<VisualElement>(BakAgain.UI.InGame.CompassView.WindowName)?.RemoveFromHierarchy();

            _compass = new BakAgain.UI.InGame.CompassView(_session, _providers);
            _compass.BuildAsync(stage, rect, this).Forget();
        }

        /// <summary>
        /// Draws the party portraits over their click areas.
        /// </summary>
        /// <remarks>
        /// <b>The click areas already exist</b> — REQ_CAST ships them as ClickAreas 128..130,
        /// Visible:false, in exactly the same places REQ_MAIN puts its own. Only the drawing was
        /// missing, which is why the caster could be switched by clicking a portrait that was not
        /// there. PartyHeadsView is the one owner of that drawing; it needed only its base action id
        /// parameterised, since the two screens differ in nothing else.
        /// </remarks>
        private void AttachPartyHeads() {
            if (_session == null || _providers == null) {
                return;
            }

            var document = GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root == null) {
                return;
            }

            _heads?.Dispose();
            _heads = new BakAgain.UI.InGame.PartyHeadsView(_session, _providers,
                firstActionId: CastMenuSelection.FirstPartySlotActionId);
            _heads.Attach(root);
            _heads.RenderAsync().Forget();
        }

        /// <summary>The spellbook page, which is also the school grouping. Loaded once.</summary>
        private SpellBookPage _spellPage;

        private const string SpellBookPageKey = "INVSPELL.DAT";

        /// <summary>
        /// Draws the thirty ring positions.
        /// </summary>
        /// <remarks>
        /// Every fifth position is a school anchor and draws a different icon — see
        /// <see cref="CastRingLayout.IconFor"/>. The icons sit one original pixel up and left of
        /// their stored position, which <see cref="CastRingLayout.IconDrawOffsetX"/> carries.
        /// </remarks>
        private async UniTask DrawRingAsync() {
            if (_ui == null || _resources == null) {
                return;
            }

            _ring ??= await _resources.GetOrLoadAsync<CastRing>("RING.DAT");
            if (_ring == null || _ring.Positions == null) {
                _logger.LogError("CastScreen: RING.DAT did not load; no casting ring.");
                return;
            }

            VisualElement stage = Stage();
            if (stage == null) {
                return;
            }

            await DrawChromeAsync(stage);

            // Rebuilding the screen re-runs this, so clear our own elements rather than stacking.
            Clear(stage, RingClass);

            for (var i = 0; i < _ring.Positions.Count; i++) {
                RingPosition position = _ring.Positions[i];
                int icon = IconAt(i);
                var sprite = await _resources.GetOrLoadAsync<Sprite>($"{CastRingLayout.IconSet}#{icon}");
                if (sprite == null) {
                    continue;
                }

                stage.Add(IconElement(position, sprite, i));
            }

            await DrawSigilAsync(stage);
            await DrawSymbolsAsync(stage);
            await DrawCasterHighlightAsync();
        }

        private const string CasterHighlightClass = "cast-caster-highlight";

        /// <summary>
        /// The ring around the casting party member's portrait.
        /// </summary>
        /// <remarks>
        /// <b>It goes inside the portrait's hotspot, not on the stage.</b> The stage carries
        /// FRAME.SCR (see <see cref="DrawChromeAsync"/>) and the REQ panel's hotspots render above
        /// it, so a highlight drawn on the stage at the same canonical coordinates would sit behind
        /// the face it is meant to circle.
        ///
        /// <para>That means positioning it RELATIVE to the hotspot, and the two are not the same
        /// box: <see cref="CastCasterHighlight"/> is the original's evenly-spaced portrait band
        /// while REQ_CAST's click areas drift from it by up to 25 canonical units. Both are known in
        /// canonical space, so the offset is just their difference — which keeps the band's own
        /// geometry in <see cref="CastCasterHighlight"/> rather than as constants here.</para>
        /// </remarks>
        private async UniTask DrawCasterHighlightAsync() {
            var document = GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root == null || _ui == null) {
                return;
            }

            foreach (VisualElement stale in
                     root.Query<VisualElement>(className: CasterHighlightClass).ToList()) {
                stale.RemoveFromHierarchy();
            }

            int slot = CasterSlot();
            if (slot < 0) {
                return;
            }

            int actionId = CastMenuSelection.FirstPartySlotActionId + slot;
            VisualElement hotspot = root.Q(name: $"hotspot_{actionId}");
            if (hotspot == null || !_ui.TryGetEntryRect(actionId, out Rect area)) {
                return;
            }

            var sprite = await _resources.GetOrLoadAsync<Sprite>(CastCasterHighlight.SpriteKey);
            if (sprite == null) {
                _logger.LogError("CastScreen: {Key} did not load; the caster is not marked.",
                    CastCasterHighlight.SpriteKey);

                return;
            }

            (int x, int y, int width, int height) = CastCasterHighlight.SlotRect(slot);
            var ring = new VisualElement {
                name = $"cast_caster_highlight_{slot}",
                // The hotspot underneath owns the click; this is only a mark.
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = x - area.x,
                    top = y - area.y,
                    width = width,
                    height = height,
                    backgroundImage = Background.FromSprite(sprite),
                },
            };
            ring.AddToClassList(CasterHighlightClass);
            hotspot.Add(ring);
        }

        /// <summary>The active-party slot the current caster occupies, or -1.</summary>
        private int CasterSlot() {
            IReadOnlyList<byte> roster = _session?.ActivePartyIndices;
            for (var i = 0; roster != null && i < roster.Count; i++) {
                if (roster[i] == CasterId) {
                    return i;
                }
            }
            return -1;
        }

        private const string SigilClass = "cast-sigil";

        /// <summary>
        /// The selected school's symbol, filling the ring.
        /// </summary>
        /// <remarks>
        /// <b>This is the big red figure in the middle of the original's ring</b> — a square with
        /// both diagonals for one school, a triangle and spokes for another — and it is the same
        /// artwork the spellbook prints beside that school's row: <c>BICONS1.BMX#&lt;group.Icon&gt;</c>,
        /// indexed straight, not through <c>IconKeyForCombined</c>. Confirmed by cropping the
        /// original's ring and the spellbook's Scent-of-Sarig icon to a common box: the same figure,
        /// differing only in palette.
        ///
        /// <para>It is NOT the per-node glyphs, which come from SPELL.FNT and are drawn one per
        /// castable spell by <see cref="DrawSymbolsAsync"/>. The original shows both at once, which
        /// is why its ring measures as one large connected figure plus one small glyph.</para>
        ///
        /// <para>Sized to the ring's own bounds, taken from RING.DAT rather than from constants, so
        /// it stays centred on the bead circle whatever the ring data says.</para>
        /// </remarks>
        private async UniTask DrawSigilAsync(VisualElement stage) {
            Clear(stage, SigilClass);
            if (_school < 0 || _ring?.Positions == null || _ring.Positions.Count == 0) {
                return;
            }
            _spellPage ??= await _resources.GetOrLoadAsync<SpellBookPage>(SpellBookPageKey);
            if (_spellPage?.Groups == null || _school >= _spellPage.Groups.Count) {
                return;
            }

            if (!GameData.Resources.Spells.CastRingSigil.Has(_school)) {
                return;
            }

            _symbolPalette ??= await _resources.GetOrLoadAsync<GameData.Resources.Palette.PaletteResource>(
                GameData.PaletteMapping.GetPaletteFor(CastRingLayout.IconSet));
            Color ink = BakAgain.Graphics.PaletteColors.ResolvePen(
                _symbolPalette, GameData.Resources.Spells.CastRingSigil.RestingPen, Color.red);

            int school = _school;
            var element = new VisualElement {
                name = $"cast_sigil_{school}",
                // The ring positions under it stay hoverable; this is only a face.
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = 0,
                    top = 0,
                    width = Canonical.Width,
                    height = Canonical.Height,
                },
            };
            element.generateVisualContent += context => StrokeSigil(context, school, ink);
            element.AddToClassList(SigilClass);
            stage.Add(element);

            int from = _morphFrom;
            _morphFrom = -1;
            if (GameData.Resources.Spells.CastRingSigil.Has(from) && from != school) {
                MorphSigilAsync(stage, element, from, school).Forget();
            }
        }

        /// <summary>
        /// The figure flowing from the old school's shape to the new one, a seven-copy trail in pens
        /// 0x83..0x89 (<see cref="GameData.Resources.Spells.CastRingSigil.Morph"/>). The resting
        /// figure is hidden while it plays and shown again after, as the routine's tail redraws it.
        /// </summary>
        /// <remarks>
        /// One frame per present, and the original presents on vertical retrace with no tick wait —
        /// so 37 frames at the VGA's 70 Hz, about half a second.
        /// </remarks>
        private async UniTaskVoid MorphSigilAsync(VisualElement stage, VisualElement resting, int from, int to) {
            var pens = new Color[GameData.Resources.Spells.CastRingSigil.TrailLength];
            for (var i = 0; i < pens.Length; i++) {
                pens[i] = BakAgain.Graphics.PaletteColors.ResolvePen(
                    _symbolPalette, GameData.Resources.Spells.CastRingSigil.TrailFirstPen + i, Color.red);
            }
            (int[] X, int[] Y)[] frame = null;
            var trail = new VisualElement {
                name = "cast_sigil_morph",
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = 0, top = 0,
                    width = Canonical.Width, height = Canonical.Height },
            };
            trail.generateVisualContent += context => {
                for (var i = 0; frame != null && i < frame.Length; i++) {
                    StrokeFigure(context, frame[i].X, frame[i].Y, pens[i]);
                }
            };
            trail.AddToClassList(SigilClass);
            resting.visible = false;
            stage.Add(trail);
            // *** THE SYMBOLS ARE GONE WHILE THE FIGURE MOVES. *** The original fades the old
            // school's glyphs out, morphs with none on the ring, then fades the new ones in
            // (cspell_menu_animate_hilite either side of hexanim_move_tiles, CSPELL.C:2341-2348) —
            // seen live on 2026-09-26. Hidden here each frame because the ring may redraw them.
            void SymbolOpacity(float o) =>
                stage.Query(className: SymbolClass).ForEach(e => e.style.opacity = o);
            // Indexed by elapsed time, not by frames drawn: at 70 Hz the morph is ~0.53 s however
            // slowly the host renders, where one-step-per-frame stretched it to seconds.
            var frames = new System.Collections.Generic.List<(int[] X, int[] Y)[]>(
                GameData.Resources.Spells.CastRingSigil.Morph(from, to));
            float start = Time.realtimeSinceStartup;
            while (trail.panel != null && _school == to) {
                int index = (int)((Time.realtimeSinceStartup - start) * 70f);
                if (index >= frames.Count) {
                    break;
                }
                frame = frames[index];
                SymbolOpacity(0f);
                trail.MarkDirtyRepaint();
                await UniTask.Yield();
            }
            trail.RemoveFromHierarchy();
            resting.visible = true;
            // The fade in: seven pen steps, seven IRQ ticks apart (CSPELL.C:1833-1851).
            double step = GameData.Resources.Combat.SpellVisuals.IrqSeconds * 7;
            for (var i = 1; i <= 7; i++) {
                SymbolOpacity(i / 7f);
                await UniTask.Delay(System.TimeSpan.FromSeconds(step));
            }
            SymbolOpacity(1f);
        }

        /// <summary>
        /// Strokes one school's figure, in canonical coordinates over the whole stage.
        /// </summary>
        /// <remarks>
        /// The vertices are the ring's own positions, which CastRingSigil turns canonical, so
        /// nothing here needs to know where the ring is. <see cref="Painter2D"/> rather than styles because this is an
        /// arbitrary closed path, the same reason <c>RestDialShadow</c> uses it.
        ///
        /// <para>A school change plays the morph over it first — <see cref="MorphSigilAsync"/>.</para>
        /// </remarks>
        private static void StrokeSigil(MeshGenerationContext context, int school, Color ink) =>
            StrokeFigure(context, GameData.Resources.Spells.CastRingSigil.VertexX[school],
                GameData.Resources.Spells.CastRingSigil.VertexY[school], ink);

        private static void StrokeFigure(MeshGenerationContext context, int[] xs, int[] ys, Color ink) {
            Painter2D painter = context.painter2D;
            painter.strokeColor = ink;
            painter.lineWidth = GameData.Resources.Spells.CastRingSigil.StrokeWidth;
            foreach (int[] edge in GameData.Resources.Spells.CastRingSigil.Edges) {
                (float x0, float y0) = GameData.Resources.Spells.CastRingSigil.ToCanonical(xs[edge[0]], ys[edge[0]]);
                (float x1, float y1) = GameData.Resources.Spells.CastRingSigil.ToCanonical(xs[edge[1]], ys[edge[1]]);
                painter.BeginPath();
                painter.MoveTo(new Vector2(x0, y0));
                painter.LineTo(new Vector2(x1, y1));
                painter.Stroke();
            }
        }

        /// <summary>
        /// The HUD chrome under the cast panel.
        /// </summary>
        /// <remarks>
        /// <b>CAST.SCX is an overlay, not a background.</b> Its art fills only the top 780 canonical
        /// px — the frame border, the ring and the info panel, i.e. exactly the region the travel
        /// HUD gives to the 3D view — and the rest of the 1600x1200 canvas is transparent. The
        /// original never notices, because it blits CAST.SCX over a screen that already has the HUD
        /// chrome on it and simply leaves the party bar alone. We push this screen through the
        /// navigator, which hides the travel screen, so nothing was left to show through and the
        /// bottom third rendered black with the portraits and buttons floating on it.
        ///
        /// <para>So FRAME.SCR — the same full chrome the travel HUD and the overhead map use — is
        /// drawn in as a child, but <b>only over the band CAST.SCX leaves empty</b>. It cannot
        /// simply go behind the whole stage: FRAME.SCR's world-view area is <i>opaque black</i>,
        /// not transparent. The travel HUD gets away with that because it covers the hole with the
        /// world's RenderTexture; here there is nothing to cover it, so a full-stage FRAME.SCR
        /// blacks out the ring and the info panel instead — which is exactly what the first attempt
        /// at this did.</para>
        ///
        /// <para>Clipping rather than a second background keeps the two images from ever
        /// overlapping, so neither can hide the other and the order they load in stops mattering.
        /// Drawn here rather than by <c>BackgroundImageLoader</c> because that loader carries one
        /// address per screen, and which of these two is background and which is overlay is a fact
        /// about the assets, not a setting.</para>
        /// </remarks>
        private async UniTask DrawChromeAsync(VisualElement stage) {
            Clear(stage, ChromeClass);
            // In a fight what lies under the cast panel is the arena's frame, cframe.scx
            // (COMBAT.C:879), which has no compass diamond; FRAME.SCR there brought it back.
            string key = CombatCaster != null ? BakAgain.UI.InGame.InGameScreen.CombatFrameAddress : ChromeKey;
            Sprite chrome = await _resources.GetOrLoadAsync<Sprite>(key);
            if (chrome == null) {
                _logger.LogError("CastScreen: {Chrome} did not load; the party bar stays blank.", key);

                return;
            }

            // The window onto FRAME.SCR: everything from where CAST.SCX's art stops to the bottom.
            var window = new VisualElement {
                name = "cast_chrome",
                // Purely decorative: the portraits and buttons over it own their hit-testing, and
                // an element this size that took clicks would swallow every one of them.
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = 0,
                    top = ChromeSplitY,
                    width = Canonical.Width,
                    height = Canonical.Height - ChromeSplitY,
                    overflow = Overflow.Hidden,
                },
            };
            window.AddToClassList(ChromeClass);

            // The whole chrome at stage size, pushed up so the band that shows through the window
            // is the part of FRAME.SCR that belongs there. Sized in canonical units because that is
            // the stage's own coordinate space — the ring icons are positioned the same way.
            var image = new VisualElement {
                name = "cast_chrome_image",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = 0,
                    top = -ChromeSplitY,
                    width = Canonical.Width,
                    height = Canonical.Height,
                    backgroundImage = Background.FromSprite(chrome),
                    // Same stretch BackgroundImageLoader applies: the source is 320x200 and the
                    // stage is canonical 1600x1200.
                    backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100)),
                },
            };
            window.Add(image);
            // Index 0: behind the ring icons, the symbols and the REQ's own widgets.
            stage.Insert(0, window);
        }

        /// <summary>
        /// The chosen school's spell symbols, on the ring.
        /// </summary>
        /// <remarks>
        /// <b>They are FONT GLYPHS, not sprites from a sheet.</b> Each node names an index into
        /// SPELL.FNT, whose glyphs are pictures rather than letters — the font starts at character
        /// zero, so it is a symbol SET indexed by number. Nothing else in the archive carries these
        /// shapes.
        ///
        /// <para>The node's position is the glyph's CENTRE, which is why the element is translated
        /// by half itself rather than placed at its top-left.</para>
        /// </remarks>
        private async UniTask DrawSymbolsAsync(VisualElement stage) {
            Clear(stage, SymbolClass);
            if (Symbols?.Nodes == null) {
                return;
            }

            _spellFont ??= await _resources.GetOrLoadAsync<FontResource>(SpellFontKey);
            // The palette from the one owner of that mapping rather than a name repeated here.
            _symbolPalette ??= await _resources.GetOrLoadAsync<GameData.Resources.Palette.PaletteResource>(
                GameData.PaletteMapping.GetPaletteFor(CastRingLayout.IconSet));
            if (_spellFont == null) {
                _logger.LogError("CastScreen: {Font} did not load; the ring has no symbols.",
                    SpellFontKey);

                return;
            }

            SpellCastContext context = await CastContextAsync();
            _drawnSymbols.Clear();
            foreach (SpellSymbolNode node in Symbols.Nodes) {
                // *** AN UNCASTABLE SPELL HAS NO SYMBOL AT ALL. *** The original tests castability
                // inside the draw and inside the hit test, so the player is never shown a spell
                // they cannot cast, nor left with a dead widget to click at.
                if (!IsCastable(node.SpellId, context)) {
                    continue;
                }
                Sprite sprite = SymbolSprite(node.FontGlyph);
                if (sprite == null) {
                    continue;
                }
                _drawnSymbols.Add((node.X, node.Y, node.SpellId));
                // *** THE TWO AXES ARE CENTRED DIFFERENTLY, AND THAT IS THE ORIGINAL. *** This was
                // a -50%/-50% translate, which centres each glyph on its own box. Only the
                // HORIZONTAL half is the glyph's: cspell_menu_animate_hilite subtracts half the
                // MEASURED width but a hard-coded `iHeight = 10` >> 1 from Y, so every symbol is
                // lifted the same five original pixels whatever its height. SPELL.FNT's header
                // gives it a height of 9, so centring on the glyph lifted each symbol by 27
                // canonical px (54/2) where the original lifts 30 — every symbol sat three
                // canonical pixels low. Small, and not self-correcting: the fixed lift is not
                // derivable from the font, so a different symbol font diverges by however much its
                // height differs from ten.
                (int originX, int originY) = SpellSymbolDisplay.GlyphOrigin(
                    node.X, node.Y, (int)sprite.rect.width,
                    SpellSymbolDisplay.HalfLineBoxCanonical);
                var element = new VisualElement {
                    name = $"spell_symbol_{node.SpellId}",
                    pickingMode = PickingMode.Ignore,
                    style = {
                        position = Position.Absolute,
                        left = originX,
                        top = originY,
                        width = sprite.rect.width,
                        height = sprite.rect.height,
                        backgroundImage = new StyleBackground(sprite),
                    },
                };
                element.AddToClassList(SymbolClass);
                stage.Add(element);
            }
        }

        /// <summary>
        /// What the castability rules need to know about this caster, right now.
        /// </summary>
        /// <remarks>
        /// Rebuilt per draw rather than cached: the pool it reads is health and stamina, which a
        /// cast has just spent, so a stale one keeps offering a spell the caster can no longer
        /// afford. The zone definition behind it is cached — that does not change while the screen
        /// is up.
        /// </remarks>
        private async UniTask<SpellCastContext> CastContextAsync() {
            if (_session?.IsActive != true || CasterId < 0) {
                return null;
            }

            // The catalogue is part of answering "can this be cast", not just of describing a spell
            // — every castability rule reads the spell record. Loading it lazily somewhere else left
            // the first draw with no catalogue and therefore no castable spells at all.
            _spells ??= await _resources.GetOrLoadAsync<GameData.Resources.Spells.SpellList>(SpellCatalog);

            // *** KEYED ON THE ZONE, NOT `??=`. *** This is a SCREEN singleton, so a `??=` keeps the
            // first zone's definition for the rest of the session and every castability rule that
            // reads ZoneKind then answers for the wrong place: above ground after a dungeon the
            // screen offered Candle Glow and refused Skyfire, Stardusk and Mad God's Rage, and
            // below ground after the surface it did the reverse. Reachable by walking through any
            // dungeon door, not only by loading a save. HotspotService carries the same note about
            // the same file and for the same reason.
            string zoneId = $"Z{_session.CurrentZone:D2}DEF.DAT";
            if (_zone == null || !string.Equals(_zone.Id, zoneId, System.StringComparison.OrdinalIgnoreCase)) {
                _zone = await _resources.GetOrLoadAsync<GameData.Resources.World.ZoneDefinition>(zoneId);
            }

            GameData.Resources.Character.ActorStat[] stats = _session.StatsOf(CasterId);

            return new SpellCastContext {
                Chapter = _session.Chapter,
                ZoneKind = _zone?.ZoneLocation ?? 0,
                GameTimeIn2Seconds = (int)_session.GameTimeIn2Seconds,
                KnownSpells = _session.KnownSpellsOf(CasterId),
                Inventory = _session.GetActorInventory(CasterId),   // keyed by character, not roster slot
                HealthStaminaPool = PoolOf(CasterId),
                CombatActorCount = FightActorCount?.Invoke() ?? NoFight,
            };
        }

        /// <summary>
        /// How many actors are on the party side of the running fight, if one is running.
        /// </summary>
        /// <remarks>
        /// <b>This is the summon cap, reached through castability rather than through the summon.</b>
        /// <c>cspell_check_castable</c> ends with
        /// <c>if ((kind == 6 || spellId == 1) &amp;&amp; g_combat_count_A == 7) castable = 0;</c>
        /// (CSPELL.C:1624) — so with a full field a summon does not fail, it never appears on the
        /// ring at all. <see cref="SpellCasting.IsCastable"/> has carried that rule the whole time
        /// and this screen passed a literal 0 for the count, which made it unreachable.
        ///
        /// <para><b>Zero is right when nothing is fighting</b>, which is why the default is not a
        /// party-size guess: outside combat there is no A-side array and the original's counter is
        /// zero, so every summon is offered. The supplier is set by the combat caller only.</para>
        ///
        /// <para>The other guard is a different one and is already wired:
        /// <c>combat_actor_party_add</c> refuses past <see cref="MonsterSummon.FightActorCapacity"/>
        /// and the caller shows <see cref="MonsterSummon.NoRoomDialog"/>. That one catches a summon
        /// cast from somewhere this check did not cover; this one stops it being offered.</para>
        /// </remarks>
        public System.Func<int> FightActorCount { get; set; }

        /// <summary>The count when no fight is running — the original's zeroed counter.</summary>
        private const int NoFight = 0;

        /// <summary>The pool a spell's cost comes out of — health and stamina together.</summary>
        /// <remarks>
        /// <b>The EFFECTIVE sum, not the stored pair.</b> <c>CSPELL.C:266</c> reads
        /// <c>stat_actor_get(actor, 0x10, 0)</c>, and <c>STAT.C:109</c> makes stat <c>0x10</c>
        /// <c>get(0, mode) + get(1, mode)</c> — so each half goes through the modifier and
        /// affliction pipeline. An afflicted caster has less to spend, and reading
        /// <c>StatEngine.HealthPool</c> (which is deliberately the STORED pair) gave them the
        /// healthy figure.
        ///
        /// <para>Measured 2026-09-13 with Owyn drunk at 28 from a Keshian Ale at Joftaz's: the
        /// original's stat cache reads health 40 / stamina <b>33</b> — a pool of <b>73</b> — while
        /// the stored pair is 40/40 for <b>80</b>. Both games agree on the per-stat effective read;
        /// only this sum disagreed.</para>
        /// </remarks>
        private int PoolOf(int casterId) => _session?.EffectivePool(casterId) ?? 0;

        /// <summary>
        /// Whether this caster can cast this spell at this moment.
        /// </summary>
        /// <remarks>
        /// The same answer drives three things: whether the symbol is drawn, whether it can be
        /// clicked, and whether the spell appears in the panel's list. The original asks the one
        /// function in all three places, which is why they can never disagree.
        /// </remarks>
        private bool IsCastable(int spellNumber, SpellCastContext context) =>
            context != null && _spells?.Spells != null
            && _spells.Spells.TryGetValue(spellNumber, out GameData.Resources.Spells.Spell spell)
            && SpellCasting.IsCastable(spellNumber, spell, context);

        private GameData.Resources.World.ZoneDefinition _zone;

        /// <summary>The castable symbols as drawn, for hit-testing against.</summary>
        private readonly List<(int X, int Y, int SpellId)> _drawnSymbols = new();

        /// <summary>A symbol's sprite, made once and kept for as long as the font is.</summary>
        private Sprite SymbolSprite(int glyphIndex) {
            if (_symbolSprites.TryGetValue(glyphIndex, out Sprite cached)) {
                return cached;
            }
            // *** THE INK IS NEVER USED HERE. *** SPELL.FNT spends a byte on each pixel, so a
            // symbol carries its own indices and its own shading; the ink only stands in if the
            // palette fails to load, and inventing a colour for that would be inventing the look.
            Sprite sprite = ResourceManagement.Converters.FontGlyphConverter.ToSprite(
                _spellFont, _spellFont.GlyphFor(glyphIndex), Color.white, _symbolPalette);
            _symbolSprites[glyphIndex] = sprite;

            return sprite;
        }

        private readonly Dictionary<int, Sprite> _symbolSprites = new();
        private FontResource _spellFont;
        private GameData.Resources.Palette.PaletteResource _symbolPalette;

        /// <summary>The font the casting symbols are drawn from.</summary>
        private const string SpellFontKey = "SPELL.FNT";

        private const string SymbolClass = "cast-spell-symbol";

        /// <summary>
        /// Follows the pointer while a power is being chosen.
        /// </summary>
        /// <remarks>
        /// The original polls the mouse once per pass of its own loop, so polling here is the
        /// faithful shape rather than a shortcut. <b>The redraw is gated on the position actually
        /// changing</b>: the hover work rebuilds thirty ring elements and the whole info panel, and
        /// doing that every frame for a cursor that has not moved between ring slots is pure churn.
        /// </remarks>
        private void Update() {
            _compass?.Refresh();

            if (_pointer == null || _ring?.Positions == null || !Tracks(_pointer) || _fillFrame >= 0) {
                return;
            }

            Vector2 canonical = CanonicalConversion.ScreenToCanonical(
                _pointer.ScreenPosition,
                new Vector2(Screen.width, Screen.height),
                Stage());
            int cursorX = Mathf.RoundToInt(canonical.x);
            int cursorY = Mathf.RoundToInt(canonical.y);

            if (_sliderSpell < 0) {
                TrackSymbols(cursorX, cursorY);

                return;
            }

            // A mouse hits the original's 10x10 boxes; a finger takes the nearest affordable
            // position, since a fingertip is bigger than the box and hides it (TASK-821).
            int position = _pointer.IsPresent
                ? CastRingLayout.PositionAt(
                    _ring.Positions, cursorX, cursorY,
                    CastRingLayout.PositionForPower(_minimumPower),
                    CastRingLayout.PositionForPower(_maximumPower),
                    CastRingLayout.CanonicalHitBoxWidth, CastRingLayout.CanonicalHitBoxHeight)
                : CastRingLayout.NearestPositionInBand(
                    _ring.Positions, cursorX, cursorY,
                    CastRingLayout.PositionForPower(_minimumPower),
                    CastRingLayout.PositionForPower(_maximumPower),
                    CastRingLayout.TouchReach);

            if (position != _hoveredPosition) {
                _hoveredPosition = position;
                RefreshHoverAsync().Forget();
            }

            if (Picks(_pointer) && _hoveredPosition >= 0) {
                CommitPowerSelection();
            }
        }

        /// <summary>
        /// Whether the pointer's position means anything this frame. A mouse always hovers; a finger
        /// only while it is down, and on the frame it lifts.
        /// </summary>
        public static bool Tracks(UI.InputCore.IPointer pointer) =>
            pointer.IsPresent || pointer.Primary.IsDown || pointer.Primary.ReleasedThisFrame;

        /// <summary>
        /// Whether this frame picks what is under the pointer — the original's click. A mouse picks on
        /// the press, after hovering showed the preview. <b>A finger cannot hover</b>, so touching
        /// is the preview and lifting is the pick (owner, 2026-10-05, TASK-816): sliding over the
        /// ring shows each power's cost before anything is cast.
        /// </summary>
        public static bool Picks(UI.InputCore.IPointer pointer) =>
            pointer.IsPresent ? pointer.Primary.PressedThisFrame : pointer.Primary.ReleasedThisFrame;

        /// <summary>
        /// Follows the pointer over the spell symbols, and picks one on a click.
        /// </summary>
        /// <remarks>
        /// <b>The panel is a live readout here too.</b> Over a symbol it shows that spell's info;
        /// over nothing it goes back to listing the caster's spells. The original redraws on the
        /// hovered symbol CHANGING, which is what the comparison below reproduces — the loop itself
        /// runs every pass either way.
        ///
        /// <para><b>The click both picks the spell and opens the slider, and cannot also commit a
        /// power.</b> The original spins waiting for the button to come back up before it starts
        /// the slider, precisely so the press that chose the spell is not read again as a choice of
        /// power. An edge-triggered press gives us that for free — but only if this returns rather
        /// than falling through to the slider in the same frame, which is why it does.</para>
        /// </remarks>
        private void TrackSymbols(int cursorX, int cursorY) {
            int index = CastRingLayout.SymbolAt(_drawnSymbols, cursorX, cursorY, isCastable: null,
                CastRingLayout.CanonicalHitBoxWidth, CastRingLayout.CanonicalHitBoxHeight);
            int spell = index < 0 ? -1 : _drawnSymbols[index].SpellId;

            if (spell != _hoveredSpell) {
                _hoveredSpell = spell;
                ShowHoveredSpellAsync().Forget();
            }

            if (Picks(_pointer) && _hoveredSpell >= 0) {
                ChooseSpellAsync(_hoveredSpell).Forget();
            }
        }

        /// <summary>The panel's two modes: the hovered spell, or the caster's list.</summary>
        private async UniTask ShowHoveredSpellAsync() {
            if (_hoveredSpell < 0) {
                await ShowSpellNamesAsync();

                return;
            }

            // Cost zero: no power has been chosen yet, so the shipped "Cost: 5-15" template stands.
            await ShowSpellInfoAsync(_hoveredSpell, 0, await DamageForAsync(_hoveredSpell, 0));
        }

        /// <summary>
        /// Picks a spell and opens the power slider on it.
        /// </summary>
        /// <remarks>
        /// The band's low end is the spell's own minimum cost and its high end is what this caster
        /// can afford, which is <see cref="SpellCasting"/>'s budget rule rather than anything this
        /// screen decides.
        /// </remarks>
        public async UniTask ChooseSpellAsync(int spellNumber) {
            _spells ??= await _resources.GetOrLoadAsync<GameData.Resources.Spells.SpellList>(SpellCatalog);
            SpellCastContext context = await CastContextAsync();
            if (context == null || _spells?.Spells == null
                || !_spells.Spells.TryGetValue(spellNumber, out GameData.Resources.Spells.Spell spell)) {
                return;
            }

            GameData.Resources.Spells.PowerRange band = SpellCasting.GetPowerRange(spell, context);
            _hoveredSpell = -1;
            BeginPowerSelection(spellNumber, band.Minimum, band.Maximum);

            // *** A BAND WITH NOTHING TO CHOOSE CASTS ON THIS CLICK. *** cspell_select_power skips
            // the slider entirely when the range has collapsed onto the spell's minimum, casting at
            // the base cost — SpellCasting.GetPowerRange documents it as PowerRange.IsFixed, which
            // until now had no caller outside its own tests. Verified against the original: clicking
            // Union's symbol casts it, with no ring interaction at all.
            //
            // *** NOT ONLY THE THREE FIXED-COST SPELLS. *** GetPowerRange lowers the maximum to
            // budget - 1, so the band collapses on ANY spell once the caster is tired enough. A
            // player near the end of their pool meets this constantly, which is what makes it worth
            // more than the three records whose Cost field happens to be a single number.
            //
            // Minimum > 0 because a FAILED range is default(PowerRange), whose Minimum and Maximum
            // are both zero and therefore also "fixed" — committing that would close the screen on a
            // cast that never happened.
            if (band.IsFixed && band.Minimum > 0) {
                _hoveredPosition = CastRingLayout.PositionForPower(band.Minimum);
                CommitPowerSelection();

                return;
            }

            await PlayRingFillAsync();
            await RefreshHoverAsync();
        }

        /// <summary>
        /// The caster's castable spells in this school, which is what the panel rests on.
        /// </summary>
        /// <remarks>
        /// Only castable ones appear and a skipped spell leaves no gap — the original's line
        /// advance sits inside the castable branch. That matches the ring, where an uncastable
        /// spell has no symbol either.
        ///
        /// <para><b>These names come from SPELLS.DAT, and the hovered title's come from SPELLDOC —
        /// the two tables disagree.</b> <c>cspell_list_draw_castable</c> prints
        /// <c>g_pSpellDefs[id].pName</c> (CSPELL.C:1902) while
        /// <c>cspell_menu_draw_spell_info</c> prints row 0 of the spell's SPELLDOC block
        /// (CSPELL.C:1938), so one screen legitimately uses both. Four of the six combat spells are
        /// spelled identically in the two tables, which is why drawing this list from SPELLDOC
        /// looked right until a caster who knew all of them was put in front of both games: the
        /// original lists "The Fetters of Rime" and "Bane of Black Slayers", SPELLDOC calls them
        /// "Fetters of Rime" and "Bane of the Black Slayers".</para>
        /// </remarks>
        public async UniTask ShowSpellNamesAsync() {
            VisualElement stage = Stage();
            // *** THE SAME GUARD DrawRingAsync HAS, AND IT WAS MISSING HERE. *** A screen opened
            // without its collaborators — which is what a reachability probe does — reached the
            // catalogue load and threw a NullReferenceException into a fire-and-forget task. That
            // surfaces at GC time and is reported against whatever test happens to be running, so it
            // read as an intermittent failure in an unrelated suite (2026-09-13, blamed on
            // ScreenReachabilityTests). Listing no names is the honest answer for a screen with no
            // resource cache.
            if (stage == null || _resources == null) {
                return;
            }

            Clear(stage, InfoClass);
            _spells ??= await _resources.GetOrLoadAsync<GameData.Resources.Spells.SpellList>(SpellCatalog);

            var drawn = 0;
            foreach ((int _, int _, int spellId) in _drawnSymbols) {
                if (_spells?.Spells == null
                    || !_spells.Spells.TryGetValue(spellId,
                        out GameData.Resources.Spells.Spell spell)) {
                    continue;
                }
                stage.Add(NameElement(spell.Name, SpellInfoPanel.NameListY(drawn)));
                drawn++;
            }
        }

        private static VisualElement NameElement(string text, int y) {
            var label = new Label(text) {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = SpellInfoPanel.NameListCentreX - (TitleBoxWidth / 2),
                    top = y,
                    width = TitleBoxWidth,
                    unityTextAlign = TextAnchor.MiddleCenter,
                },
            };
            label.AddToClassList(InfoClass);
            GameFontText.Apply(label);

            return label;
        }

        /// <summary>The spell whose symbol the cursor is over, or -1.</summary>
        private int _hoveredSpell = -1;

        /// <summary>
        /// Raised when the power choice ends: the spell, the power, and the spell's duration.
        /// </summary>
        /// <remarks>
        /// <b>The duration travels with the event rather than being looked up afterwards.</b> The
        /// original disposes the spell catalogue as the screen closes and passes the duration into
        /// its handlers, so by the time one runs the record is gone. Holding the catalogue in memory
        /// would hide that, and a consumer written against a lookup would then be relying on
        /// something the original guarantees is unavailable.
        ///
        /// <para>The power is <see cref="CastRingLayout.Cancelled"/> when the choice was
        /// abandoned.</para>
        /// </remarks>
        public event System.Action<int, int, int> Committed;

        /// <summary>
        /// The record of the spell <see cref="Committed"/> last reported, captured as it was raised.
        /// </summary>
        /// <remarks>
        /// <b>Captured, not looked up.</b> A consumer that needs more than the duration — the combat
        /// path wants the targeting type and the damage calculation — must not go back to the
        /// catalogue for it, for the reason <see cref="Committed"/> gives: the original has disposed
        /// it by the time a handler runs. Holding the one record the screen already had at commit
        /// time keeps that guarantee true without widening the event.
        /// </remarks>
        public GameData.Resources.Spells.Spell CommittedSpell { get; private set; }

        private void RaiseCommitted(int power) {
            int duration = 0;
            CommittedSpell = null;
            if (_spells?.Spells != null
                && _spells.Spells.TryGetValue(_sliderSpell, out GameData.Resources.Spells.Spell spell)) {
                duration = spell.Duration;
                CommittedSpell = spell;
            }
            Committed?.Invoke(_sliderSpell, power, duration);
        }

        /// <summary>The frame of the slider's opening sweep being shown, or -1.</summary>
        private int _fillFrame = -1;

        /// <summary>
        /// The ring turning over and the band growing to the caster's reach before the slider
        /// takes input — <see cref="CastRingLayout.FillIconAt"/>.
        /// </summary>
        /// <remarks>
        /// One frame per <c>screen_frame_present</c> in the original. ponytail: paced at the VGA
        /// refresh (70 Hz) on the assumption that the present waits for the retrace the open
        /// transition hooked; not yet measured against the running original.
        /// </remarks>
        private async UniTask PlayRingFillAsync() {
            VisualElement stage = Stage();
            if (stage == null || _ring?.Positions == null) {
                return;
            }
            int spell = _sliderSpell;
            int frames = CastRingLayout.FillFrameCount(_maximumPower);
            float start = Time.unscaledTime;
            for (int f = 0; f < frames && _sliderSpell == spell && isActiveAndEnabled;) {
                _fillFrame = f;
                await PaintRingIconsAsync(stage);
                await UniTask.Yield();
                // Elapsed time picks the frame, so a slow device skips frames rather than slowing down.
                f = Mathf.Max(f + 1, (int)((Time.unscaledTime - start) * RingFillFramesPerSecond));
            }
            _fillFrame = -1;
        }

        private const double RingFillFramesPerSecond = 70.0;

        /// <summary>Re-skins the ring's existing icons without rebuilding the screen.</summary>
        private async UniTask PaintRingIconsAsync(VisualElement stage) {
            for (var i = 0; i < _ring.Positions.Count; i++) {
                VisualElement element = stage.Q<VisualElement>($"ring_{i}");
                if (element == null) {
                    continue;
                }
                var sprite = await _resources.GetOrLoadAsync<Sprite>($"{CastRingLayout.IconSet}#{IconAt(i)}");
                if (sprite != null) {
                    element.style.backgroundImage = new StyleBackground(sprite);
                    element.style.width = sprite.rect.width;
                    element.style.height = sprite.rect.height;
                }
            }
        }

        private async UniTask RefreshHoverAsync() {
            await DrawRingAsync();
            int power = CastRingLayout.PreviewPower(_hoveredPosition);
            await ShowSpellInfoAsync(_sliderSpell, power, await DamageForAsync(_sliderSpell, power));
        }

        /// <summary>
        /// Opens the power slider for a spell.
        /// </summary>
        /// <param name="spellNumber">The spell being cast.</param>
        /// <param name="minimumPower">Its minimum cost - the band's fixed low end.</param>
        /// <param name="maximumPower">The highest power this caster can afford.</param>
        public void BeginPowerSelection(int spellNumber, int minimumPower, int maximumPower) {
            _sliderSpell = spellNumber;
            _minimumPower = minimumPower;
            _maximumPower = maximumPower;
            _hoveredPosition = -1;
        }

        /// <summary>
        /// Moves the slider to the ring position under a canonical-space point.
        /// </summary>
        /// <returns>The power now under the cursor, or 0 when the point is off the ring.</returns>
        /// <remarks>
        /// <b>Three things change together</b>, which is why they live in one call: the band grows
        /// or shrinks, the hovered position gets its own icon, and the info panel's cost line
        /// follows - the original recomputes all three every pass of its loop. The hit test is
        /// clamped to the affordable band, so positions above the caster's budget are not merely
        /// unclickable but unhoverable.
        /// </remarks>
        public async UniTask<int> HoverAtAsync(int canonicalX, int canonicalY) {
            if (_ring?.Positions == null || _sliderSpell < 0) {
                return 0;
            }

            _hoveredPosition = CastRingLayout.PositionAt(
                _ring.Positions, canonicalX, canonicalY,
                CastRingLayout.PositionForPower(_minimumPower),
                CastRingLayout.PositionForPower(_maximumPower),
                CastRingLayout.CanonicalHitBoxWidth, CastRingLayout.CanonicalHitBoxHeight);

            await DrawRingAsync();

            int power = CastRingLayout.PreviewPower(_hoveredPosition);
            // Cost 0 puts the shipped "Cost: 5-15" template back, which is what the original shows
            // when nothing is hovered.
            await ShowSpellInfoAsync(_sliderSpell, power, await DamageForAsync(_sliderSpell, power));
            return power;
        }

        /// <summary>
        /// Casts at the power under the cursor, and closes the screen behind it.
        /// </summary>
        /// <remarks>
        /// <b>A completed cast closes the screen, exactly as a cancel does.</b>
        /// <c>cspell_cast_menu_loop</c> RETURNS once a power is picked: the menu is torn down and
        /// only THEN does the caller dispatch the spell. That ordering is visible — it is why the
        /// original shows a spell's narrative over the travel HUD, and why its ring sits empty
        /// behind the locator map. The port left the screen up through both, with Exit the only
        /// thing that ever popped it (TASK-373, TASK-374).
        ///
        /// <para><b>The pop comes AFTER the event, not before.</b> <see cref="Committed"/> is
        /// synchronous and its handlers read this screen — <c>InGameScreen</c> passes
        /// <see cref="CasterId"/> to the field dispatcher, <c>HotspotService</c> reads
        /// <see cref="CommittedSpell"/> — while <see cref="OnDisable"/> clears both. Popping first
        /// would hand the dispatcher a caster of -1.</para>
        ///
        /// <para>A method rather than two lines in <c>Update</c> so the commit has one definition:
        /// the pointer reaches it through the click, and a test or an agent through the call.</para>
        /// </remarks>
        public void CommitPowerSelection() {
            RaiseCommitted(CommitPower());
            _navigator?.PopUnfaded().Forget();
        }

        /// <summary>The power a click would commit, or 0 when the cursor is off the ring.</summary>
        public int CommitPower() =>
            CastRingLayout.PreviewPower(_hoveredPosition);

        /// <summary>
        /// The damage figure the info panel prints for a spell at a chosen power.
        /// </summary>
        /// <remarks>
        /// <b>This is the same calculation the cast itself uses</b> — the original's info panel and
        /// its damage application both call <c>Spell_CalcEffectMagnitude</c>, which is why the
        /// number on the panel is a promise rather than an estimate. Two of the six calculations
        /// answer zero here on purpose (their magnitude is duration-based and belongs to the
        /// dispatcher), and <see cref="SpellInfoPanel.DamageLineIsReplaced"/> is what decides
        /// whether a zero means "no damage line" or a real zero.
        ///
        /// <para><c>targetHasMetalGear</c> is left false: the panel is shown before a target is
        /// chosen, and only Skyfire consults it. A panel that guessed at a target would print a
        /// figure the cast then contradicts.</para>
        /// </remarks>
        private async UniTask<int> DamageForAsync(int spellNumber, int power) {
            _spells ??= await _resources.GetOrLoadAsync<GameData.Resources.Spells.SpellList>(SpellCatalog);
            if (_spells?.Spells == null
                || !_spells.Spells.TryGetValue(spellNumber, out GameData.Resources.Spells.Spell spell)) {
                return SpellInfoPanel.NoDamageMagnitude;
            }

            return GameData.Resources.Spells.SpellEffectMagnitude.Calculate(spell, spellNumber, power);
        }

        /// <summary>The spell catalogue — cost bands, effect amounts and calculations.</summary>
        private const string SpellCatalog = "SPELLS.DAT";

        /// <summary>The spell descriptions — names and the panel's seven lines.</summary>
        private const string SpellDocumentation = "SPELLDOC.DAT";

        /// <summary>
        /// Draws a spell's info panel.
        /// </summary>
        /// <param name="spellNumber">The spell being described.</param>
        /// <param name="cost">The chosen power, or 0 while none is chosen.</param>
        /// <param name="damageMagnitude">
        /// The computed damage, or <see cref="SpellInfoPanel.NoDamageMagnitude"/> when the spell has
        /// no damage figure.
        /// </param>
        /// <remarks>
        /// The seven lines come from SPELLDOC; two of them are replaced at runtime and empty ones are
        /// skipped without leaving a gap — see <see cref="SpellInfoPanel"/>, which carries the rules.
        /// </remarks>
        public async UniTask ShowSpellInfoAsync(int spellNumber, int cost, int damageMagnitude) {
            _descriptions ??= await _resources.GetOrLoadAsync<SpellDescriptions>(SpellDocumentation);
            SpellDescription spell = _descriptions?.Spells?.Find(s => s.SpellNumber == spellNumber);
            if (spell == null) {
                _logger.LogError("CastScreen: SPELLDOC has no spell {Spell}.", spellNumber);
                return;
            }

            VisualElement stage = Stage();
            if (stage == null) {
                return;
            }

            Clear(stage, InfoClass);
            stage.Add(TitleElement(spell.Name));

            var drawn = 0;
            foreach (string line in BodyLines(spell, cost, damageMagnitude)) {
                if (!SpellInfoPanel.LineAdvances(line)) {
                    // Skipped without advancing, so the panel closes up rather than showing a gap.
                    continue;
                }
                stage.Add(BodyElement(line, SpellInfoPanel.BodyY(drawn)));
                drawn++;
            }

            // Nine spells carry a footer with the caster's pool — the jump table at CSPELL.C:1964,
            // drawn AFTER the description at its own fixed y, so a short panel leaves a gap above it
            // rather than pulling it up. SpellInfoPanel has carried the id list and the position the
            // whole time with nothing calling either; the line is what the player weighs the power
            // slider against, so without it the panel says "Cost: 1-15 Health" and never says out of
            // what.
            if (SpellInfoPanel.ShowsHealthStamina(spellNumber)) {
                GameData.Resources.Character.ActorStat[] pool = _session?.StatsOf(CasterId);
                if (pool != null) {
                    GameData.Resources.Character.ActorStat health =
                        pool[(int)GameData.ActorAttribute.Health];
                    GameData.Resources.Character.ActorStat stamina =
                        pool[(int)GameData.ActorAttribute.Stamina];
                    // CSPELL.C:1976-1977 prints mode 0 over mode 1, so the current figure carries
                    // the caster's afflictions and the maximum is the plain stored pair — the same
                    // split the camp and inn table has. See PoolOf.
                    stage.Add(BodyElement(
                        SpellInfoPanel.HealthStaminaLine(
                            PoolOf(CasterId),
                            GameData.Resources.Character.StatEngine.HealthPoolMax(health, stamina)),
                        SpellInfoPanel.HealthStaminaY));
                }
            }
            TextOverflowReport.CheckRightEdge(stage, SpellInfoPanel.BodyRight, InfoLineClass);
        }

        private static IEnumerable<string> BodyLines(SpellDescription spell, int cost, int damage) {
            yield return SpellInfoPanel.CostLineIsReplaced(cost)
                ? SpellInfoPanel.CostLine(cost)
                : spell.Cost;
            yield return SpellInfoPanel.DamageLineIsReplaced(damage)
                ? SpellInfoPanel.DamageLine(damage)
                : spell.Damage;
            yield return spell.Duration;
            yield return spell.LineOfSight;
            yield return spell.Effect;
            yield return spell.EffectLine2;
        }

        // Centred by laying the label across the panel and centring its text, rather than measuring
        // the string and subtracting half of it. Same result, and it avoids UI Toolkit's
        // measure-before-layout problem.
        private static VisualElement TitleElement(string text) {
            var label = new Label(text) {
                name = "cast_info_title",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = SpellInfoPanel.TitleCentreX - (TitleBoxWidth / 2),
                    top = SpellInfoPanel.TitleY,
                    width = TitleBoxWidth,
                    unityTextAlign = TextAnchor.MiddleCenter,
                },
            };
            label.AddToClassList(InfoClass);
            // The game font's size and its VGA aspect stretch, from the one owner of both. Anchored
            // at the top because the panel's y positions are the original's line tops, so the text
            // must grow downward from them rather than about its middle.
            GameFontText.Apply(label);
            return label;
        }

        private const int TitleBoxWidth = 900;

        private static VisualElement BodyElement(string text, int y) {
            var label = new Label(text) {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = SpellInfoPanel.BodyX,
                    top = y,
                },
            };
            label.AddToClassList(InfoClass);
            label.AddToClassList(InfoLineClass);
            GameFontText.Apply(label);
            return label;
        }

        /// <summary>A body line of the info panel, for the overflow report's edge check.</summary>
        private const string InfoLineClass = "cast-info-body";

        private VisualElement Stage() {
            var document = GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;
            return root == null ? null : CanonicalStage.GetOrCreate(root, _ui.Frame);
        }

        private static void Clear(VisualElement stage, string className) {
            foreach (VisualElement stale in stage.Query<VisualElement>(className: className).ToList()) {
                stale.RemoveFromHierarchy();
            }
        }

        /// <summary>
        /// The icon a ring position draws, honouring the slider when one is open.
        /// </summary>
        /// <remarks>
        /// Three roles in priority order: the hovered position wins, then the affordable band, then
        /// the untouched ring — CSPELL.C:2051-2060, redrawn every pass. Both the ring and the band
        /// mark their fifths (TASK-755).
        /// </remarks>
        private int IconAt(int position) {
            if (_fillFrame >= 0) {
                return CastRingLayout.FillIconAt(_fillFrame, position, _minimumPower, _maximumPower);
            }
            if (_sliderSpell < 0) {
                return CastRingLayout.IconFor(baseIcon, position, markAnchors: true);
            }
            if (position == _hoveredPosition) {
                return CastRingLayout.SliderHoverIcon;
            }
            if (CastRingLayout.IsInAffordableBand(position, _minimumPower, _maximumPower)) {
                return CastRingLayout.IconFor(CastRingLayout.SliderFilledIcon, position,
                    CastRingLayout.BandMarksAnchors);
            }
            return CastRingLayout.IconFor(CastRingLayout.SliderRingIcon, position, markAnchors: true);
        }

        private static VisualElement IconElement(RingPosition position, Sprite sprite, int index) {
            var element = new VisualElement {
                name = $"ring_{index}",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = position.X + CastRingLayout.IconDrawOffsetX,
                    top = position.Y + CastRingLayout.IconDrawOffsetY,
                    width = sprite.rect.width,
                    height = sprite.rect.height,
                    backgroundImage = new StyleBackground(sprite),
                },
            };
            element.AddToClassList(RingClass);
            return element;
        }

        /// <summary>The REQ's cancel/exit widget - Escape maps to this id on every REQ screen.</summary>
        private const int CancelActionId = 1;

        /// <inheritdoc/>
        /// <remarks>
        /// <b>CANCELLING THE POWER SLIDER ENDS THE WHOLE CAST, NOT JUST THE POWER.</b> Corrected
        /// 2026-08-19 by reading UI_Cast_Spell's own use of the slider's answer rather than the
        /// slider alone: once a spell is picked the selection loop is left for good, and a cost of
        /// -1 comes back to a caller that clears the spell and falls straight into its teardown.
        /// There is no path back to picking a spell. This screen previously kept itself up on that
        /// cancel, which read as reasonable and was not what the game does.
        ///
        /// <para>The original has to wait for the key to come back up here, so the same press is
        /// not seen again by the screen underneath. We do not: one press produces one intent and
        /// the stack delivers it to one layer, so the double-cancel it guards against cannot
        /// happen. Recorded because the absence of that wait is deliberate, not an oversight.</para>
        /// </remarks>
        public void PrimaryAction(int menuEntryActionId) {
            int school = CastMenuSelection.SchoolForAction(menuEntryActionId);
            if (school >= 0) {
                SelectSchool(school);

                return;
            }

            int slot = CastMenuSelection.PartySlotForAction(menuEntryActionId);
            if (slot >= 0) {
                SelectCaster(slot).Forget();

                return;
            }

            if (menuEntryActionId != CancelActionId) {
                _logger.LogInformation("CastScreen: button {ActionId} has no handler yet.", menuEntryActionId);

                return;
            }

            if (_sliderSpell >= 0) {
                CancelPowerSelection();
            }

            _navigator?.PopUnfaded().Forget();
        }

        /// <summary>The party member currently casting, or -1 before one is chosen.</summary>
        public int CasterId { get; private set; } = -1;

        /// <summary>
        /// Hands the casting over to a party member, by active-roster slot.
        /// </summary>
        /// <remarks>
        /// <b>Picking someone who cannot cast is refused out loud.</b> The original says so with
        /// its own dialog rather than quietly keeping the previous caster - the difference between
        /// a portrait that looks broken and one that explains itself. Re-picking the current
        /// caster is a no-op.
        ///
        /// <para>Castability is tested on the MAXIMUM casting stat, not the current one, so a
        /// caster drained to nothing is still a caster - the same rule the world HUD uses to
        /// decide whether the cast button is live, reused rather than restated.</para>
        /// </remarks>
        public async UniTask SelectCaster(int slot) {
            if (_session?.IsActive != true) {
                return;
            }

            System.Collections.Generic.IReadOnlyList<byte> roster = _session.ActivePartyIndices;
            if (roster == null || slot < 0 || slot >= roster.Count) {
                return;
            }

            byte characterId = roster[slot];
            if (!CanCast(characterId)) {
                if (_dialogs != null) {
                    await _dialogs.ShowById(CastMenuSelection.NotASpellcasterDialog);
                }

                return;
            }

            if (characterId == CasterId) {
                return;
            }

            CasterId = characterId;
            // Which symbols are drawable depends on the caster, so the ring is rebuilt for them —
            // AND the names under it, which are the same caster's castable spells in the same
            // school. This was the third caller DrawRingAndNamesAsync exists to stop forgetting.
            await DrawRingAndNamesAsync();
        }

        private bool CanCast(byte characterId) {
            GameData.Resources.Character.ActorStat[] stats = _session.StatsOf(characterId);
            GameData.Resources.Character.ActorStat casting =
                stats?[(int)GameData.ActorAttribute.AccuracyCasting];
            return casting != null && SpellCasting.IsCaster(casting.Max);
        }

        /// <summary>
        /// Abandons the power choice, reporting the cancel to whoever opened the slider.
        /// </summary>
        /// <remarks>
        /// The screen goes with it — see <see cref="PrimaryAction"/>. Kept separate from the pop so
        /// the cancel is still reported to the opener rather than being swallowed by the teardown.
        /// </remarks>
        public void CancelPowerSelection() {
            _hoveredPosition = -1;
            _hoveredSpell = -1;
            RaiseCommitted(CastRingLayout.Cancelled);
            _sliderSpell = -1;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Right-click is help on this screen, as on every other: the school buttons and the exit
        /// button have their own texts, and the original shows them from here rather than from a
        /// tooltip.
        /// </remarks>
        public async Awaitable SecondaryAction(int menuEntryActionId) {
            int help = CastMenuSelection.SchoolForAction(menuEntryActionId) >= 0
                ? CastMenuSelection.SchoolButtonHelpDialog
                : menuEntryActionId == CancelActionId
                    ? CastMenuSelection.ExitButtonHelpDialog
                    : 0;
            if (help == 0 || _dialogs == null) {
                return;
            }

            await _dialogs.ShowById(help).AsTask();
        }

        /// <summary>
        /// Switches which school's symbols the ring offers.
        /// </summary>
        /// <remarks>
        /// <b>Switching is a load, not a swap.</b> The original disposes the resident symbol data
        /// and reads the new school's file, so only one school is in memory at a time - which is
        /// why the switch redraws rather than changing instantly. Re-picking the school already
        /// showing is a no-op.
        /// </remarks>
        public void SelectSchool(int school) {
            if (school == _school) {
                return;
            }

            _morphFrom = _school;
            _school = school;
            LoadSchoolAsync(school).Forget();
        }

        /// <summary>The school whose symbols are currently loaded, or -1 before any are.</summary>
        public int School => _school;

        /// <summary>The symbol set currently resident - one at a time, as the original keeps it.</summary>
        public SpellSymbolLayout Symbols { get; private set; }

        private async UniTask LoadSchoolAsync(int school) {
            // SYMBOL files are 1-based on disk while the school index is 0-based.
            Symbols = await _resources.GetOrLoadAsync<SpellSymbolLayout>($"SYMBOL{school + 1}.DAT");
            if (Symbols == null) {
                _logger.LogError("CastScreen: SYMBOL{File}.DAT did not load.", school + 1);

                return;
            }

            // Switching school is a load AND a redraw: the symbols on the ring are the school's,
            // and so are the names under it — see DrawRingAndNamesAsync for why those are one call.
            await DrawRingAndNamesAsync();
        }
    }
}
