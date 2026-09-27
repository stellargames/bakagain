namespace BakAgain.UI {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using Cysharp.Threading.Tasks;
    using GameData;
    using BakAgain.CutScenes;
    using BakAgain.Graphics;
    using GameData.Resources.Character;
    using GameData.Resources.Config;
    using System;
    using System.Threading;
    using UnityEngine;
    using UnityEngine.UIElements;
    using VContainer;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The encampment screen (<c>REQ_CAMP.DAT</c> over <c>ENCAMP.SCX</c>) — ported from
    /// <c>ENCAMP.C</c>'s <c>encamp_screen_run</c>.
    ///
    /// <para>Resting is the only way the party recovers, so this screen is what makes
    /// <see cref="PartyUpkeepService.ExhaustionEnabled"/> safe to switch on: until it existed a
    /// party could be drained by walking with no way back.</para>
    ///
    /// <para><b>Only two of the three buttons are live at once</b>: Camp and Exit while idle, Stop
    /// while a rest runs. REQ_CAMP ships Stop as Visible=false/Disabled=1 and the original swaps
    /// which entries are live by writing their <c>bActive_flag</c>/<c>wEnable_gate</c>; this does
    /// the same through <c>UserInterfaceLoader.SetEntryState</c>.</para>
    ///
    /// <para><b>This is not a navigator screen — it is an overlay on the travel HUD.</b> REQ_CAMP's
    /// origin is canonical (65,66), which is <see cref="BakAgain.World.WorldViewport"/>'s origin to
    /// the pixel: the panel occupies the 3D viewport and nothing else, and ENCAMP.SCX is fully
    /// transparent outside it. In the original the portraits, compass and six action buttons stay
    /// visible around it. Pushing this on the <c>IScreenNavigator</c> hid the HUD (one screen shown
    /// at a time, by design), which is what left the bottom 40% of the frame black.
    ///
    /// <para>So camp is UI <i>inside</i> the persistent in-game layer, not a screen — the same
    /// reading combat will need. The navigator's invariant is untouched because this was never a
    /// navigator screen; it simply stops pretending to be one.</para></para>
    /// </summary>
    public class CampMenu : MonoBehaviour, IActionHandler {
        // REQ_CAMP action ids (ENCAMP.C cases 0xc0..0xc2).
        private const int ButtonCamp = 193;   // 0xc1 — rest until healed
        private const int ButtonStop = 194;   // 0xc2 — abandon the rest; hidden while idle
        private const int ButtonExit = 192;   // 0xc0 — leave the screen

        // Right-click describe records, one per button.
        private const int HelpExit = 0xed;
        private const int HelpCamp = 0xee;
        private const int HelpStop = 0xef;

        /// <summary>
        /// How rested the party must be for "rest until healed" to stop: every active member above
        /// 80% of their pool. The same threshold the original passes to its hourly tick, which is
        /// why a camp rest tops up to 80% rather than to full.
        /// </summary>
        public const int RestedPercent = 80;

        /// <summary>
        /// Game minutes per rest step. The original advances 900 two-second units per frame and
        /// only does the hourly work when the hour index changes; stepping a whole hour at a time
        /// reaches the same state through the same <see cref="IGameClock.AdvanceHours"/> path.
        /// </summary>
        private const int RestStepHours = 1;

        /// <summary>
        /// Hours of continuous rest that cure Sick outright, applied once per rest.
        /// <c>ENCAMP.C</c> compares the elapsed hour count against 0xd.
        /// </summary>
        public const int SickCureHours = 13;

        /// <summary>
        /// A guard against a rest that can never end — <b>not</b> a rule, and deliberately far
        /// beyond any reachable one.
        /// </summary>
        /// <remarks>
        /// <b>The original's heal rest has TWO endings, and this used to stand in for the second.</b>
        /// ENCAMP.C:140-167 keeps stepping hours while
        /// <c>stat_party_all_above_pct(0x50) == 0</c>, and the loop leaves on either the 80% test
        /// passing or <c>g_gameState.bCombatExitRequest != 0</c> (:159). That flag is set by
        /// STAT.C:383-388, which raises it when <b>every</b> active member's Near-death rank is
        /// non-zero and clears it the moment one of them reads 0 — the same rule
        /// <see cref="GameData.Resources.GameState.PartyDownState.Recompute"/> already implements
        /// here. So the port had the real ending all along, behind a ceiling that fired first.
        ///
        /// <para>At <b>72</b> it was reachable, and it cut a real rest short: the same Near-death
        /// party rested <b>108 hours</b> in the original (day 6 12:24 to day 11 00:24, ending at
        /// Starving 5 / Near-death 95) and only 72 here — TASK-552. A rest that still hits this
        /// bound is a defect in upkeep, not a party that needs more sleep, so it logs an error
        /// rather than a warning.</para>
        /// </remarks>
        public const int RestSpinGuardHours = 24 * 30;

        private ILogger _logger;
        private IDialogManager _dialogManager;
        private GameSession _session;
        private IGameClock _clock;
        private PartyUpkeepService _upkeep;

        private CancellationTokenSource _restCancel;
        private BakAgain.ResourceManagement.Loaders.UserInterfaceLoader _ui;

        /// <summary>True while a rest is running — the Stop button replaces Camp and Exit.</summary>
        public bool IsResting => _restCancel != null;

        private void Awake() {
            _logger = LogManager.LoggerFactory.CreateLogger<CampMenu>();
            _ui = GetComponent<BakAgain.ResourceManagement.Loaders.UserInterfaceLoader>();
        }

        /// <summary>Raises the camp panel over the travel HUD, which stays visible beneath it.</summary>
        /// <remarks>
        /// Enabling the GameObject is the whole of it: <c>BackgroundImageLoader</c>,
        /// <c>UserInterfaceLoader</c> and <c>MenuLayerHost</c> all build from their own OnEnable,
        /// and the layer host's Exclusive layer is what stops the HUD underneath from taking input
        /// (and therefore what stops the party walking while camped).
        /// </remarks>
        public void Open() => gameObject.SetActive(true);

        /// <summary>Drops the panel and hands the HUD back.</summary>
        public void Close() => gameObject.SetActive(false);

        /// <summary>True while the panel is up.</summary>
        public bool IsOpen => gameObject.activeSelf;

        // The REQ builds asynchronously, so set the idle button state once it has — and again on
        // every show, since the panel is rebuilt each time the panel is enabled.
        private void OnEnable() {
            _ui = _ui ? _ui : GetComponent<BakAgain.ResourceManagement.Loaders.UserInterfaceLoader>();
            if (_ui != null) {
                _ui.Built += OnPanelBuilt;
                // Route through OnPanelBuilt rather than repeating part of it: this path used to
                // call ApplyButtonState alone, so the party table silently did not draw when the
                // loader's build was already cached. Same trap as CastScreen's.
                if (_ui.IsBuilt) {
                    OnPanelBuilt(_ui.CurrentNavWidgets);
                }
            }
        }

        private void OnDisable() {
            if (_ui != null) {
                _ui.Built -= OnPanelBuilt;
            }
            // Was ScreenBase.OnBeforeHide. A rest left running with the panel down would keep
            // advancing the clock from a screen the player has left.
            StopResting();
        }

        private void OnPanelBuilt(
            System.Collections.Generic.IReadOnlyList<BakAgain.UI.InputCore.NavWidget> _) {
            ApplyButtonState();
            BuildBackdropAsync().Forget();
            BuildDialStonesAsync().Forget();
            BuildPartyTableAsync().Forget();
        }

        // ---- the backdrop ------------------------------------------------------------------------

        /// <summary>ENCAMP.SCX — the dial and the panel behind the party table.</summary>
        private const string CampBackdrop = "ENCAMP.SCX";

        /// <summary>Name of the clipping window, so a rebuild replaces it rather than stacking.</summary>
        private const string BackdropName = "BakCampBackdrop";

        /// <summary>
        /// Draws ENCAMP.SCX <b>clipped to the REQ's panel rect</b>, so the travel HUD survives
        /// around it.
        /// </summary>
        /// <remarks>
        /// <b>This is why the camp screen does not use <c>BackgroundImageLoader</c>.</b> That
        /// component stretches an SCX over the whole canonical stage, which is right for a
        /// full-screen menu background and wrong here: ENCAMP.SCX is a 320x200 canvas carrying the
        /// panel plus a copy of the surrounding frame chrome, and everything outside the panel is
        /// palette index 0 — which the runtime converter bakes as opaque BLACK for SCX (only BMX
        /// sub-images get index-0 transparency). Painted across the stage it covered the HUD in
        /// black, which is exactly the bug this screen had.
        ///
        /// <para><b>The original never paints it across the frame either.</b> <c>Load_encamp</c>
        /// @0x7087e points the SCX loader at <c>VGA_videoBuffer_C</c> — an offscreen buffer — and
        /// the screen blits a sub-rect of it into the visible frame. The chrome in the image is a
        /// copy of what FRAME already draws, so clipping it away loses nothing.</para>
        ///
        /// <para>The rect comes from the REQ itself (<see cref="UserInterfaceLoader.PanelRect"/>),
        /// not from constants here.</para>
        /// </remarks>
        private async UniTask BuildBackdropAsync() {
            VisualElement stage = CanonicalStage.Find(GetComponent<UIDocument>()?.rootVisualElement);
            if (stage == null || _ui == null || _resources == null) {
                return;
            }

            Rect panel = _ui.PanelRect;
            if (panel.width <= 0f || panel.height <= 0f) {
                return;
            }

            var backdrop = await _resources.GetOrLoadAsync<Sprite>(CampBackdrop);
            if (backdrop == null) {
                _logger.LogError("CampMenu: {Backdrop} did not load; the panel has no artwork.",
                    CampBackdrop);
                return;
            }

            stage.Q<VisualElement>(BackdropName)?.RemoveFromHierarchy();

            // A window at the panel rect, holding the full-canvas image shifted so the panel's own
            // pixels land in it. Everything outside the window — chrome and index-0 black alike —
            // is clipped, leaving the HUD beneath visible.
            var window = new VisualElement {
                name = BackdropName,
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = panel.x, top = panel.y, width = panel.width, height = panel.height,
                    overflow = Overflow.Hidden,
                },
            };
            window.Add(new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = -panel.x, top = -panel.y,
                    width = Canonical.Width, height = Canonical.Height,
                    backgroundImage = Background.FromSprite(backdrop),
                    backgroundSize = new BackgroundSize(
                        new Length(Canonical.Width), new Length(Canonical.Height)),
                },
            });

            // Behind the buttons and the table, both of which are added to the same stage.
            stage.Insert(0, window);
        }

        /// <summary>
        /// Camp and Exit while idle, Stop while resting — the original's entry-flag swap.
        /// Re-entrant guard: SetEntryState re-raises Built, so only touch entries whose state is
        /// actually changing or this recurses.
        /// </summary>
        /// <remarks>
        /// <b>Camp also hides when there is nothing to rest off.</b> The reference screenshot of the
        /// original shows a fully-healed party with only Exit on the panel, and
        /// <see cref="RestAsync"/> already refuses to start in that state — so the button was live
        /// but inert, which reads as a broken click rather than as "you are fine".
        /// </remarks>
        private void ApplyButtonState() {
            if (_ui == null || _applyingButtonState) {
                return;
            }
            _applyingButtonState = true;
            try {
                bool resting = IsResting;
                bool canCamp = !resting && !PartyIsRested();
                _ui.SetEntryState(ButtonCamp, canCamp, canCamp);
                _ui.SetEntryState(ButtonExit, !resting, !resting);
                _ui.SetEntryState(ButtonStop, resting, resting);
                PlaceExitButton(canCamp);
            } finally {
                _applyingButtonState = false;
            }
        }

        /// <summary>
        /// <b>With Camp disabled, Exit moves into Stop's slot.</b>
        /// </summary>
        /// <remarks>
        /// The original does two things when the whole party is already above
        /// <see cref="RestedPercent"/>: it clears entry 0's active flag AND writes
        /// <c>pEntries[2].rect</c> to (0xbe, 0x4d) — ENCAMP.C, just after the
        /// <c>stat_party_all_above_pct(0x50)</c> test. Only the first half was ported, so a rested
        /// party saw Exit still sitting out at the right-hand end with a gap where Camp had been.
        ///
        /// <para><b>Taken from the data, not written down as a constant.</b> Scaled to canonical
        /// that override is (950, 462), which is <i>exactly</i> where REQ_CAMP authors the Stop
        /// button — so the rule is "Exit takes Stop's place", and asking the REQ for Stop's rect
        /// keeps it true if the layout is ever re-extracted or overridden by a mod. Hard-coding 950
        /// would have looked identical today and drifted silently later.</para>
        /// </remarks>
        private void PlaceExitButton(bool campAvailable) {
            if (_ui == null) {
                return;
            }
            if (campAvailable) {
                _ui.ClearEntryPosition(ButtonExit);
                return;
            }
            if (_ui.TryGetEntryRect(ButtonStop, out UnityEngine.Rect stop)) {
                _ui.SetEntryPosition(ButtonExit, stop.x, stop.y);
            }
        }

        private bool _applyingButtonState;

        // ---- NO PARTY PURSE HERE ----------------------------------------------------------------
        // UI_DisplayPartyGold @0x4ff0a is called ONLY from UI_RestUntilTime @0x4ff5c (twice), which
        // is the INN's rest screen — never from encamp_run. The readout was built here because
        // TASK-68 was blocked on "the rest screen existing" and the camp screen landed first; with
        // the party table drawn it visibly collided with the third member's row, and a capture of
        // the original shows no gold on this screen at all. It belongs to the inn (TASK-165).

        private IResourceCache _resources;
        private GameData.Resources.Palette.PaletteResource _palette;


        // ---- the dial's stones -----------------------------------------------------------------
        // sub_ovr182_67A @0x70a4a. ENCAMP.DAT gives 24 clock entries (one per hour, midnight at the
        // bottom of the dial and noon at the top); the icon each draws is EncampDial.IconFor.

        private GameData.Resources.Config.EncampData _encamp;

        /// <summary>
        /// Draws the dial's stones, with the hours slept so far marked.
        /// </summary>
        /// <remarks>
        /// <b>The arc grows as the night passes</b> — the original passes the hour the rest began
        /// and the hour the clock has reached (0x70526), so the red range is a progress display,
        /// not just a "now" marker. While idle there is no range and only the current hour is red.
        ///
        /// <para>The drawing itself is <see cref="Rest.RestDialView"/>, shared with the inn.</para>
        /// </remarks>
        private async UniTask BuildDialStonesAsync() {
            VisualElement stage = CanonicalStage.Find(GetComponent<UIDocument>()?.rootVisualElement);
            if (stage == null || _resources == null || _clock == null) {
                return;
            }

            _encamp ??= await _resources.GetOrLoadAsync<GameData.Resources.Config.EncampData>("ENCAMP.DAT");
            if (_encamp?.ClockEntries == null) {
                _logger.LogError("CampMenu: ENCAMP.DAT did not load; the dial has no stones.");

                return;
            }

            WireDial(stage);

            int now = _clock.HourOfDay;
            int spanStart = IsResting ? _restStartHour : -1;
            await Rest.RestDialView.BuildAsync(stage, _resources, _encamp, now,
                spanStartHour: spanStart, spanEndHour: IsResting ? now : -1,
                shadowTicksOfDay: (int)(_clock.Ticks % GameData.Resources.Character.InnStay.TicksPerDay),
                palette: _palette, shadedArt: await ShadedArtAsync());
        }

        /// <summary>The hour the running rest began, so its stones can be marked.</summary>
        private int _restStartHour = -1;

        /// <summary>No hour was chosen — rest until the party is healed instead.</summary>
        private const int NoTargetHour = -1;

        /// <summary>
        /// How long the finished dial stays up before the camp screen leaves by itself.
        /// </summary>
        /// <remarks>
        /// <b>Measured, not chosen.</b> ENCAMP.C:131 sets <c>g_nFrameTickCountdown = 0x6e</c> (110)
        /// and spins presenting frames until it drains, then <c>running = 0</c>. The counter is
        /// decremented once per IRQ0 (TIMER.ASM:294), and <c>timer_install(0xd)</c> (BOOT.C:205)
        /// programs the PIT with <c>0xffff / 13 = 5041</c>, so IRQ0 runs at
        /// <c>1193182 / 5041 = 236.7 Hz</c> and 110 ticks is <b>0.465 s</b>.
        /// </remarks>
        private const float DialRestCloseDelaySeconds = 0.465f;

        /// <summary>The stone the pointer went down on, or <see cref="EncampData.NoEntry"/>.</summary>
        /// <remarks>
        /// <b>A stone commits on RELEASE, and only on the one it was pressed on.</b> The original
        /// latches the pressed stone and compares it to the stone under the release
        /// (<c>UI_Encamp</c> @0x706fc); sliding off before letting go abandons the choice, and
        /// moving off every stone clears the latch outright.
        /// </remarks>
        private int _pressedStone = GameData.Resources.Config.EncampData.NoEntry;

        /// <summary>
        /// Wire the dial's 24 stones to the pointer.
        /// </summary>
        /// <remarks>
        /// <b>The stones are not REQ entries.</b> REQ_CAMP ships only the three buttons, so the
        /// dial cannot come through <see cref="UserInterfaceLoader"/> like every other clickable
        /// thing on a REQ screen — the screen hit-tests it itself, which is what
        /// <see cref="EncampData.ClockEntryAt"/> is.
        ///
        /// <para><b>Stone index IS the hour, with no offset</b> — stone 0 is midnight and 23 is
        /// 11pm, even though the artwork begins its run at the lower right, which invites a
        /// rotation that is not there.</para>
        /// </remarks>
        private void WireDial(VisualElement stage) {
            // *** WIRED PER STAGE, NOT ONCE PER SCREEN. *** OnEnable rebuilds the panel every time
            // the screen is shown, and a re-enabled UIDocument brings a fresh stage. A one-shot flag
            // left every later stage without its hit area, so the dial answered the first camp of a
            // session and never again (TASK-547, reopened).
            if (stage == null || UQueryExtensions.Q(stage, DialHitName) != null) {
                return;
            }

            // *** THE STAGE IS NOT PICKABLE, SO THE DIAL NEEDS ITS OWN HIT AREA. *** CanonicalStage has
            // made every stage PickingMode.Ignore since TASK-267, and the dial's layers are Ignore
            // too, so a press over a stone had no target and these callbacks never ran from a real
            // click. A pickable element over exactly the stones' hit boxes brings back ENCAMP.C's
            // press/release without letting anything swallow clicks elsewhere. Its events carry
            // positions relative to itself, so they are taken back to stage space for StoneAt.
            (int x, int y, int width, int height) = _encamp.ClockHitBounds();
            var hit = new VisualElement { name = DialHitName, pickingMode = PickingMode.Position };
            hit.style.position = Position.Absolute;
            hit.style.left = x;
            hit.style.top = y;
            hit.style.width = width;
            hit.style.height = height;
            stage.Add(hit);
            hit.SendToBack();

            hit.RegisterCallback<PointerDownEvent>(evt => {
                int stone = StoneAt(stage.WorldToLocal(evt.position));
                if (stone == GameData.Resources.Config.EncampData.NoEntry) {
                    _pressedStone = GameData.Resources.Config.EncampData.NoEntry;

                    return;
                }
                if (evt.button == 1) {
                    _dialogManager?.ShowById(GameData.Resources.Config.EncampDial.StoneHelpDialog).Forget();

                    return;
                }
                _pressedStone = stone;
            });

            hit.RegisterCallback<PointerUpEvent>(evt => {
                int stone = StoneAt(stage.WorldToLocal(evt.position));
                int pressed = _pressedStone;
                _pressedStone = GameData.Resources.Config.EncampData.NoEntry;
                if (evt.button != 0 || stone == GameData.Resources.Config.EncampData.NoEntry
                    || stone != pressed || IsResting) {
                    return;
                }

                RestAsync(stone).Forget();
            });
        }


        internal const string DialHitName = "BakCampDialHit";


        /// <summary>The stone under a stage-local point, or <see cref="EncampData.NoEntry"/>.</summary>
        private int StoneAt(Vector2 stageLocal) =>
            _encamp == null
                ? GameData.Resources.Config.EncampData.NoEntry
                : _encamp.ClockEntryAt((int)stageLocal.x, (int)stageLocal.y);

        /// <summary>
        /// The screen's artwork with the wedge's remap applied, built once.
        /// </summary>
        /// <remarks>
        /// Once rather than per redraw: the table depends only on the palette, and the rest loop
        /// redraws the dial every game hour.
        /// </remarks>
        private async UniTask<Texture2D> ShadedArtAsync() {
            if (_shadedArt != null || _resources == null) {
                return _shadedArt;
            }

            _palette ??= await _resources
                .GetOrLoadAsync<GameData.Resources.Palette.PaletteResource>(CampPalette);
            var backdrop = await _resources.GetOrLoadAsync<Sprite>(CampBackdrop);
            _shadedArt = Rest.RestDialShading.Build(backdrop, _palette);

            return _shadedArt;
        }

        private Texture2D _shadedArt;

        // ---- the party table -----------------------------------------------------------------
        // UI_show_actor_healthStatus @0x70d2d. The rules are CampPartyStats; this only draws them.

        /// <summary>The camp screen runs under the shared UI palette.</summary>
        private const string CampPalette = "OPTIONS.PAL";

        /// <summary>
        /// Draws the per-member table that fills the panel.
        /// </summary>
        /// <remarks>
        /// The table itself is <see cref="Rest.RestPartyTableView"/>, shared with the inn — the
        /// original's <c>UI_show_actor_healthStatus</c> takes no arguments and writes at absolute
        /// positions, so both screens draw the identical thing.
        ///
        /// <para>Rebuilt wholesale each time, because SetEntryState re-raises Built and stacking a
        /// second table over the first is the obvious failure.</para>
        /// </remarks>
        private async UniTask BuildPartyTableAsync() {
            VisualElement stage = CanonicalStage.Find(GetComponent<UIDocument>()?.rootVisualElement);
            if (stage == null) {
                return;
            }

            if (_resources != null) {
                _palette ??= await _resources
                    .GetOrLoadAsync<GameData.Resources.Palette.PaletteResource>(CampPalette);
            }

            _partyTable.Build(stage, _session, _palette);
        }

        private readonly Rest.RestPartyTableView _partyTable = new();

        [Inject]
        public void Construct(IDialogManager dialogManager, GameSession session, IGameClock clock,
            PartyUpkeepService upkeep, IResourceCache resources) {
            _dialogManager = dialogManager ?? throw new ArgumentNullException(nameof(dialogManager));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _upkeep = upkeep ?? throw new ArgumentNullException(nameof(upkeep));
            _resources = resources;
        }

        public void PrimaryAction(int actionId) {
            switch (actionId) {
                case ButtonCamp:
                    RestAsync().Forget();
                    break;
                case ButtonStop:
                    StopResting();
                    break;
                case ButtonExit:
                    if (!IsResting) {
                        Close();
                    }
                    break;
                default:
                    _logger.LogDebug("Unhandled camp action {ActionId}", actionId);
                    break;
            }
        }

        public async Awaitable SecondaryAction(int actionId) {
            int record = actionId switch {
                ButtonCamp => HelpCamp,
                ButtonStop => HelpStop,
                ButtonExit => HelpExit,
                _ => 0,
            };
            if (record != 0) {
                await _dialogManager.ShowById(record).AsTask();
            }
        }

        /// <summary>
        /// Rests an hour at a time until the party is above <see cref="RestedPercent"/>, the player
        /// stops, or <see cref="RestSpinGuardHours"/> is reached.
        ///
        /// <para>A rest started from an hour stone then <b>closes the screen</b> after
        /// <see cref="DialRestCloseDelaySeconds"/>; a "Camp until Healed" rest that finishes leaves
        /// it open. Both are ENCAMP.C:128-167 — see the close at the end of this method.</para>
        ///
        /// <para><see cref="PartyUpkeepService.RestQuality"/> is raised for the duration and lowered
        /// again in every exit path — leaving it raised would make walking heal the party.</para>
        /// </summary>
        private async UniTaskVoid RestAsync(int targetHour = NoTargetHour) {
            // A dial rest is a time the player asked for, so "already rested" does not refuse it —
            // they may want to sleep to morning regardless of how healthy everyone is.
            if (IsResting || (targetHour == NoTargetHour && PartyIsRested())) {
                return;
            }

            _restCancel = new CancellationTokenSource();
            _restStartHour = _clock.HourOfDay;
            ApplyButtonState();
            CancellationToken token = _restCancel.Token;
            int previousQuality = _upkeep.RestQuality;
            _upkeep.RestQuality = UpkeepEngine.PartialRestQuality;
            long startTicks = _clock.Ticks;

            // *** ONLY A DIAL REST THAT REACHED ITS HOUR CLOSES THE SCREEN. *** Stop, the ceiling
            // and a party going down are all different endings and none of them takes the screen
            // down by itself — see the close below.
            var reachedDialTarget = false;

            try {
                var hours = 0;
                while (!token.IsCancellationRequested && hours < RestSpinGuardHours) {
                    // The hour is advanced BEFORE the stop test so a dial rest onto the current
                    // hour sleeps the day round rather than returning instantly — "the party will
                    // wake up when the time reaches the stone you have selected" (ddx 240).
                    _clock.AdvanceHours(RestStepHours);
                    hours += RestStepHours;
                    // A catch this hour is told before the next one, as gstate_hourly_tick does.
                    await _upkeep.PlayAnnouncementsAsync(_dialogManager, token);
                    // ENCAMP.C:159: a party that went down this hour stops the rest, and the travel
                    // screen then plays 0x145 and leaves (TASK-505).
                    if (GameData.Resources.GameState.PartyDownState.EndsTheLoop(_session.PartyDeathState)) {
                        break;
                    }

                    if (hours == SickCureHours) {
                        CureSickness();
                    }

                    // Redraw both, or "the dial and party bars animate" is only true of the clock
                    // the player cannot see: the stones and the table are built once on show.
                    BuildDialStonesAsync().Forget();
                    BuildPartyTableAsync().Forget();

                    // One frame per hour, so the clock dial and party bars animate rather than the
                    // whole rest happening inside a single frozen frame.
                    await UniTask.Yield(PlayerLoopTiming.Update, token, cancelImmediately: false);

                    bool done = targetHour == NoTargetHour
                        ? PartyIsRested()
                        : _clock.HourOfDay == targetHour;
                    if (done) {
                        reachedDialTarget = targetHour != NoTargetHour;
                        break;
                    }
                }

                if (hours >= RestSpinGuardHours) {
                    // Unreachable in a working game: the original's own two endings (80% rested,
                    // or every member Near-death) both apply here, and upkeep drives an unfed
                    // party to the second within days. Getting here means neither can fire.
                    _logger.LogError(
                        "Rest hit the {Hours}-hour spin guard: the party is below {Percent}% and "
                        + "nobody is Near-death, so neither of the original's endings can ever "
                        + "fire. That is an upkeep defect, not a long sleep.",
                        RestSpinGuardHours, RestedPercent);
                }
            } catch (System.OperationCanceledException) {
                // Stop, or the screen closing, is a deliberate end to the rest — not a fault. The
                // finally below still runs, which is the whole point: it owns LastRestTicks,
                // RestQuality and _restCancel.
            } finally {
                _upkeep.RestQuality = previousQuality;
                _session.LastRestTicks = _clock.Ticks;
                _restCancel?.Dispose();
                _restCancel = null;
                _restStartHour = -1;
                ApplyButtonState();
                BuildDialStonesAsync().Forget();   // drop the arc, leave the new hour marked
                _logger.LogInformation("Rested {Hours} game hours.",
                    (_clock.Ticks - startTicks) / GameClock.TicksPerHour);
            }
            // The original's `running = 0` leaves the whole camp screen, not just the rest.
            if (GameData.Resources.GameState.PartyDownState.EndsTheLoop(_session.PartyDeathState)) {
                Close();

                return;
            }

            // *** A DIAL REST LEAVES; "CAMP UNTIL HEALED" STAYS. *** ENCAMP.C:128-167 brackets the
            // two endings and they are NOT the same. With `restUntilHealed == 0` the target hour
            // holds the finished dial for 110 frame ticks and then sets `running = 0` — the screen
            // closes on its own. With `restUntilHealed != 0` the 80%-rested test instead resets
            // `restUntilHealed`/`advancing` to 0 and puts the Camp button back (`showCampBtn = 1`),
            // which is exactly the state this screen is already left in.
            //
            // The port kept BOTH open, and that is not a cosmetic difference: a camp screen that
            // will not close keeps the clock running, and on a starving party in the 2026-09-16
            // dimwood run it took Locklear from 56 hp to 5 and every member to 0 stamina with no
            // combat involved (TASK-548).
            //
            // The delay is suppressed rather than awaited bare: this runs in a UniTaskVoid, so a
            // cancel on destroy would surface as an unobserved exception against whatever is
            // running when the GC finds it — the shape that made TASK-382 look intermittent.
            if (reachedDialTarget) {
                bool cancelled = await UniTask.Delay(
                    TimeSpan.FromSeconds(DialRestCloseDelaySeconds),
                    ignoreTimeScale: true,
                    cancellationToken: this.GetCancellationTokenOnDestroy(),
                    cancelImmediately: false).SuppressCancellationThrow();
                if (!cancelled) {
                    Close();
                }
            }
        }

        private void StopResting() {
            _restCancel?.Cancel();
        }

        /// <summary>Every active member above the rested threshold — <c>stat_party_all_above_pct</c>.</summary>
        /// <remarks>
        /// <b>internal for the test that pins WHICH POOL it reads.</b> The rule is one line and the
        /// bug was never in the line — it was in the quantity fed to it (TASK-606), which nothing
        /// below this method can observe.
        /// </remarks>
        internal bool PartyIsRested() {
            if (_session == null || !_session.IsActive) {
                return true;
            }
            foreach (byte characterId in _session.ActivePartyIndices) {
                ActorStat[] stats = _session.StatsOf(characterId);
                if (stats == null) {
                    continue;
                }
                int max = _session.EffectivePoolMax(characterId);
                if (max == 0) {
                    continue;
                }
                // The rule itself lives in UpkeepEngine, where it can be tested: the original
                // truncates the THRESHOLD (`percent * max / 100`), not the ratio, and the two forms
                // differ by up to two points — measured against the running original on 2026-09-13.
                //
                // *** ENCAMP.C HAS TWO 80% COMPARISONS AND THIS CITED THE WRONG ONE. ***
                // :495-527 is the party TABLE's colouring — `stat_actor_get(char, 0x10, 0)`, the
                // EFFECTIVE pool, against `maxHealthStamina * 0x50 / 100`, deciding whether a
                // member's figure is drawn in pen 'k'. Cosmetic.
                //
                // The rest loop's stop test at :140 is `stat_party_all_above_pct(0x50)`, and that
                // routine (STAT.C:451-470) reads **mode 3** — `st->base`, the stored pair — against
                // mode 1's `st->max`. So the behavioural test is on the BASE pool, not the
                // effective one, and an afflicted member IS counted as rested by it.
                //
                // *** SO THIS READS THE BASE PAIR, AND THAT IS THE WHOLE RULE. ***
                // `StatEngine.HealthPool` is exactly `health.Base + stamina.Base` and says so. It
                // stood on EffectivePool until 2026-09-21: the reason to wait was that swapping it
                // stops the rest EARLIER, and the open measurement ran the other way (the original
                // rested 108 hours where we rested 90). TASK-605 settled that — the gap was
                // Near-death's heal ceiling, not this — so the faithful reading is safe to take.
                //
                // The same test gates the Camp BUTTON, not only the loop: ENCAMP.C:82-87 greys
                // entry 0 and slides entry 2 into its place `if (stat_party_all_above_pct(0x50))`,
                // which is what ApplyButtonState + PlaceExitButton do here. One predicate, two
                // users, and the original uses one too.
                //
                // The threshold arithmetic is right either way: the original truncates
                // `percent * max / 100`, not the ratio, and the two forms differ by up to two
                // points — measured against the running original on 2026-09-13.
                if (!GameData.Resources.Character.UpkeepEngine.IsAbovePercent(
                        GameData.Resources.Character.StatEngine.HealthPool(
                            stats[(int)GameData.ActorAttribute.Health],
                            stats[(int)GameData.ActorAttribute.Stamina]),
                        max, RestedPercent)) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Thirteen hours of rest clears Sick from everyone, once per rest.</summary>
        private void CureSickness() => _session.CureSicknessAcrossParty();
    }
}
