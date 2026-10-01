namespace BakAgain.UI.InGame {
    using BakAgain.Core;
    using GameData.Resources.Data;
    using GameData.Resources.GameState;
    using BakAgain.UI;
    using BakAgain.World;
    using BakAgain.ResourceManagement;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.UIElements;
    using VContainer;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Travel-mode action dispatcher — the Unity counterpart of the original
    /// <c>MainGameLoop</c> input switch (KRONDOR.EXE @ 0x6c1da). Lives on the same
    /// GameObject as a <see cref="ResourceManagement.Loaders.UserInterfaceLoader"/> pointed
    /// at <c>REQ_MAIN.DAT</c> (with <c>renderImageAndClickAreas</c> enabled): the loader
    /// builds the button/click-area hotspots and routes left-clicks to
    /// <see cref="PrimaryAction"/> and right-clicks to <see cref="SecondaryAction"/>.
    ///
    /// <para>The ActionIds come straight from <c>req_main.dat</c> and equal the DOS
    /// arrow-key scancodes for movement (0x48/0x50/0x4B/0x4D). Right-click shows the
    /// per-button help text (DDX 223–232, same ids the original passed to
    /// <c>dialog_Show</c>). Left-click and keyboard movement are intentionally
    /// <b>stubbed</b> here — they log their intent; real MoveParty/TurnParty
    /// (@0x71902 / @0x71a88) and the option/encamp/cast transitions land in a later pass.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InGameScreen : MonoBehaviour, IInGameScreen, IActionHandler, IMenuStateProvider {
        // req_main.dat ActionIds (verified against generated/REQ/REQ_MAIN.json and the
        // MainGameLoop jumptable). Movement ids double as DOS arrow-key scancodes.
        private const int ActionMoveForward = 72;   // 0x48 Up
        private const int ActionMoveBackward = 80;  // 0x50 Down
        private const int ActionTurnLeft = 75;      // 0x4B Left
        private const int ActionTurnRight = 77;     // 0x4D Right
        private const int ActionFollowRoad = 19;    // Toggle
        private const int ActionMap = 50;           // overhead/full map
        private const int ActionCastSpell = 46;
        private const int ActionBookmark = 48;      // quick-save bookmark
        private const int ActionEncamp = 18;
        private const int ActionOptions = 24;       // options / main menu
        private const int ActionPartyMember1 = 2;   // portrait click areas
        private const int ActionPartyMember2 = 3;
        private const int ActionPartyMember3 = 4;
        private const int ActionWorldViewport = 192; // click in the 3D view

        private ILogger _logger;
        private IDialogManager _dialogManager;
        private InGameMenu _inGameMenu;
        private CampMenu _campMenu;
        private BakAgain.UI.Spells.CastScreen _castScreen;
        private BakAgain.UI.Spells.FieldSpellCaster _fieldSpells;
        private BakAgain.World.WorldLightingService _lighting;
        private BakAgain.World.Scenes.LocationScenePlayer _locations;
        private GameSession _gameSession;
        private BakAgain.Core.Services.ISaveGameService _saveGameService;
        private BakAgain.Core.Services.IGameClock _clock;
        private BakAgain.World.PartyMovement _movement;
        private UIDocument _document;
        private IResourceProviderService _resources;
        private UserInterfaceLoader _loader;
        private PartyHeadsView _partyHeads;
        private IWorldViewport _worldViewport;
        private GameViewportRegistry _viewportRegistry;
        private WorldViewportView _worldView;

        /// <summary>The painted backdrop element, while a fight that has one is running.</summary>
        private VisualElement _combatBackdrop;

        /// <summary>Name of that element, so a rebuild replaces it rather than stacking.</summary>
        private const string CombatBackdropName = "BakCombatBackdrop";

        /// <summary>
        /// Covers the world viewport with a painted backdrop for the one fight that has one.
        /// </summary>
        /// <param name="imageAddress">The image, or null/empty to go back to the world.</param>
        /// <remarks>
        /// <b>OVER the viewport, not instead of it</b> — which is what the original does.
        /// <c>combat_captureArenaBackdrop</c> renders the whole world into the buffer that becomes
        /// the fight's backdrop and only then blits <c>fcombat.scx</c> over the lot (0x2227d), so
        /// the world render still happens and is simply not seen. Ours keeps rendering for the same
        /// reason: the arena camera, the depth sort and the combatant sprites all carry on, and only
        /// the ground behind them is replaced. See
        /// <see cref="GameData.Resources.Combat.CombatBackdrop"/>.
        ///
        /// <para>Idempotent: the arena redraws on every turn and hands this over each time.</para>
        /// </remarks>
        public void SetCombatBackdrop(string imageAddress) {
            if (string.IsNullOrEmpty(imageAddress)) {
                _combatBackdrop?.RemoveFromHierarchy();
                _combatBackdrop = null;

                return;
            }

            if (_combatBackdrop != null) {
                return;   // already up for this fight
            }

            VisualElement viewport = _worldView?.Element;
            if (viewport == null) {
                _logger?.LogWarning(
                    "Combat backdrop {Image} has no viewport to cover; the fight shows the world.",
                    imageAddress);

                return;
            }

            // *** A CLIP, THEN A FULL-SCREEN IMAGE INSIDE IT. *** See the remark above: the image
            // is a whole 1600x1200 screen, so it must sit where it would on the screen and be
            // CROPPED by the viewport — not squashed into the viewport's own 1470x606.
            _combatBackdrop = new VisualElement {
                name = CombatBackdropName,
                pickingMode = PickingMode.Ignore,   // the viewport under it still takes clicks
                style = {
                    position = Position.Absolute,
                    left = 0, top = 0, right = 0, bottom = 0,
                    overflow = Overflow.Hidden,
                },
            };
            var image = new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute },
            };
            _combatBackdrop.Add(image);
            viewport.Add(_combatBackdrop);
            LoadCombatBackdropAsync(imageAddress, _combatBackdrop, image).Forget();
        }

        /// <summary>
        /// Loads the image and places it where it would be on a full screen.
        /// </summary>
        /// <remarks>
        /// <b>The offset is MEASURED, not written down.</b> The viewport's own rect comes from
        /// REQ_MAIN, so the distance from the canonical stage's corner to the viewport's is whatever
        /// the data says; taking it from the resolved layout keeps the one copy of those numbers in
        /// the REQ where it belongs. Measured after the await, by which point the panel has laid out.
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTaskVoid LoadCombatBackdropAsync(
            string imageAddress, VisualElement clip, VisualElement image) {
            Sprite sprite = await _resources.LoadAssetAsync<Sprite>(imageAddress, owner: this);
            if (sprite == null) {
                _logger?.LogWarning("Combat backdrop {Image} did not load.", imageAddress);

                return;
            }

            // The fight may have ended while it loaded — then this is already detached and painting
            // it would put the image on nothing.
            if (clip.panel == null) {
                return;
            }

            VisualElement stage = BakAgain.UI.CanonicalStage.Find(_document?.rootVisualElement);
            Rect stageBox = stage?.worldBound ?? clip.worldBound;
            Rect clipBox = clip.worldBound;
            float scale = stageBox.width > 0 ? stageBox.width / BakAgain.Graphics.Canonical.Width : 1f;

            image.style.left = stageBox.x - clipBox.x;
            image.style.top = stageBox.y - clipBox.y;
            image.style.width = BakAgain.Graphics.Canonical.Width * scale;
            image.style.height = BakAgain.Graphics.Canonical.Height * scale;
            image.style.backgroundImage = new StyleBackground(sprite);
        }
        private CompassView _compass;
        // The arena's own frame, shown only in a fight -- see BuildCombatFrame.
        private const string CombatFrameAddress = "CFRAME.SCX";
        private VisualElement _combatFrame;
        private VisualElement[] _compassArrows = System.Array.Empty<VisualElement>();
        private bool _combatChromeShown;
        private SpellEffectCaptionView _effectCaption;
        private BakAgain.UI.Combat.HudParchmentPanelView _combatPanel;
        private WorldInteractionController _interaction;
        private BakAgain.UI.Inventory.InventoryMenu _inventoryMenu;
        private BakAgain.UI.Character.CharacterSheetScreen _characterSheet;
        private BakAgain.UI.Navigation.IScreenNavigator _navigator;
        private BakAgain.UI.Puzzle.PuzzleService _puzzles;
        private BakAgain.World.DoorVisualService _doorVisuals;
        private OverheadMapScreen _overheadMap;
        private IPointer _pointer;
        private IGameplayInput _gameplay;
        private IMovementDriver _movementDriver;
        private TravelLayerHost _travelHost;

        // (rosterSlot, distanceInFine) -> loot that body. Null outside a fight, which is why the
        // click path null-checks rather than assuming.
        private System.Action<int, long, long, bool> _hotspotLoot;

        /// <summary>Hands over what a clicked body means. Called when a fight draws its arena.</summary>
        internal void SetCorpseLootSeam(System.Action<int, long, long, bool> loot) => _hotspotLoot = loot;

        // A click on a LIVE combatant while target selection is armed. Set alongside the loot seam
        // and for the same reason: the fight owns what a click on the arena means, the travel screen
        // owns the viewport it lands in.
        private System.Action<int, bool, bool> _hotspotTarget;

        internal void SetCombatTargetSeam(System.Action<int, bool, bool> target) =>
            _hotspotTarget = target;

        /// <summary>Who stands on the cell under a world point — the arena's own pick.</summary>
        /// <remarks>
        /// Handed over beside the target seam because the two are one gesture: this says WHICH
        /// combatant a point names, that one acts on it. Null outside a fight, which is when
        /// nothing can be targeted anyway.
        /// </remarks>
        private System.Func<UnityEngine.Vector3, (int RosterSlot, bool PartyMember)?>
            _combatantAtPoint;

        private System.Func<Vector3, (int Column, int Row)?> _cellAtPoint;
        private System.Action<(int Column, int Row)?, bool> _setCursorCell;
        private System.Func<int, int, Vector3?> _cellWorld;
        private System.Func<(int Column, int Row)?> _actingCell;
        private System.Func<bool> _awaitingTarget;

        // Touch aids: the arena cell under a floor point, and the ring on the cell a first tap chose.
        internal void SetCombatCellSeams(System.Func<Vector3, (int Column, int Row)?> cellAt,
            System.Action<(int Column, int Row)?, bool> setCursorCell,
            System.Func<int, int, Vector3?> cellWorld, System.Func<(int Column, int Row)?> actingCell,
            System.Func<bool> awaitingTarget) {
            _cellAtPoint = cellAt;
            _setCursorCell = setCursorCell;
            _cellWorld = cellWorld;
            _actingCell = actingCell;
            _awaitingTarget = awaitingTarget;
        }

        internal void SetCombatantAtPointSeam(
            System.Func<UnityEngine.Vector3, (int RosterSlot, bool PartyMember)?> at) =>
            _combatantAtPoint = at;

        // Work the world loop owes once per iteration, handed in by whoever owns game flow. This
        // Update IS the world loop — the ambient driver and the pit drop are already ticked here
        // "where the original ticks it" — so a job that the original does at the top of a
        // MainGameLoop pass belongs in the same place rather than in a listener somewhere else.
        private System.Action _worldLoopPump;

        public void SetWorldLoopSeam(System.Action pump) => _worldLoopPump = pump;

        // A click on the arena FLOOR — a tile rather than a thing. Set beside the target seam and
        // drained by the same owner: the fight decides whether a tile means anything right now.
        private System.Action<UnityEngine.Vector3> _hotspotGround;

        internal void SetCombatGroundSeam(System.Action<UnityEngine.Vector3> ground) =>
            _hotspotGround = ground;

        // What the SHOOT menu's parchment should say for the combatant under the cursor, or null
        // when the menu is not up. Set beside the click seams and for the same reason: the fight
        // owns what is on the panel, the travel screen owns the viewport the cursor is hovering.
        private System.Func<int, bool,
                (System.Collections.Generic.IReadOnlyList<GameData.Resources.Combat.HudPanelLine> Lines,
                 System.Collections.Generic.IReadOnlyList<GameData.Resources.Combat.HudPanelRule> Rules)>
            _combatPanelContent;

        internal void SetCombatPanelSeam(
            System.Func<int, bool,
                (System.Collections.Generic.IReadOnlyList<GameData.Resources.Combat.HudPanelLine> Lines,
                 System.Collections.Generic.IReadOnlyList<GameData.Resources.Combat.HudPanelRule> Rules)>
                content) =>
            _combatPanelContent = content;

        // Whether a fight is running. Asked live rather than latched: the seams above are set once
        // per fight and outlive it, so a latched flag would still read true after the last enemy fell.
        private System.Func<bool> _inCombat;

        internal void SetInCombatPredicate(System.Func<bool> inCombat) => _inCombat = inCombat;
        private Camera _pendingCamera;
        private float _fadeSeconds = 0.15f;

        // FollowRoad is a latching toggle in the original (the road overlay turns on/off).
        // Tracked here so the loader's Toggle icon reflects state via IMenuStateProvider.
        private bool _visible;

        public bool IsVisible => _visible;

        private void Awake() {
            _logger = LogManager.LoggerFactory.CreateLogger<InGameScreen>();
            _document = GetComponent<UIDocument>();
            _loader = GetComponent<UserInterfaceLoader>();
            _travelHost = GetComponent<TravelLayerHost>();
            // Start hidden; InGameState drives Show()/Hide(). Disabling the GameObject keeps
            // the sibling UserInterfaceLoader from building REQ_MAIN before the world is up.
            gameObject.SetActive(false);
        }

        [Inject]
        public void Construct(IDialogManager dialogManager, GameSession gameSession,
            IResourceProviderService resources, IWorldViewport worldViewport,
            GameViewportRegistry viewportRegistry, InGameMenu inGameMenu,
            BakAgain.UI.Inventory.InventoryMenu inventoryMenu, CampMenu campMenu,
            BakAgain.UI.Character.CharacterSheetScreen characterSheet,
            BakAgain.UI.Spells.CastScreen castScreen,
            BakAgain.UI.Spells.FieldSpellCaster fieldSpells,
            BakAgain.World.WorldLightingService lighting,
            BakAgain.World.Scenes.LocationScenePlayer locations,
            BakAgain.UI.Navigation.IScreenNavigator navigator, IPointer pointer,
            IGameplayInput gameplay, BakAgain.UI.Puzzle.PuzzleService puzzles,
            BakAgain.World.DoorVisualService doorVisuals,
            BakAgain.Core.Services.ISaveGameService saveGameService,
            BakAgain.Core.Services.IGameClock clock,
            OverheadMapScreen overheadMap = null,
            VContainer.IObjectResolver resolver = null) {
            _dialogManager = dialogManager;
            _gameSession = gameSession;
            _resolver = resolver;
            _saveGameService = saveGameService;
            _clock = clock;
            _resources = resources;
            _worldViewport = worldViewport;
            _viewportRegistry = viewportRegistry;
            _inGameMenu = inGameMenu;
            _inventoryMenu = inventoryMenu;
            _characterSheet = characterSheet;
            _campMenu = campMenu;
            _castScreen = castScreen;
            _fieldSpells = fieldSpells;
            _lighting = lighting;
            _locations = locations;
            // Subscribed once, here rather than per push: the screen is a singleton and the cast is
            // the world's business, not the screen's — which is why it reports a choice instead of
            // acting on one.
            if (_castScreen != null) {
                _castScreen.Committed -= OnSpellCommitted;
                _castScreen.Committed += OnSpellCommitted;
            }
            _navigator = navigator;
            _pointer = pointer;
            _gameplay = gameplay;
            _puzzles = puzzles;
            _doorVisuals = doorVisuals;
            _overheadMap = overheadMap;
        }

        public void SetMovement(BakAgain.World.PartyMovement movement) {
            _movement = movement;
            _movementDriver = new ClassicMovementDriver(_document, movement, _gameplay, _pointer);
            // The world arrives after the REQ is built, so the button's first honest state is here.
            RefreshFollowRoadButton();
        }

        /// <summary>
        /// Float a combat number over a point in the world — <c>combat_actor_draw_float_damage</c>
        /// (CACTOR.C:973): its top 5 px above the sprite's top, centred, clamped inside the view, one pen a
        /// frame for <paramref name="pens"/>.Count combat frames, then gone.
        /// </summary>
        /// <param name="pens">The colour on each frame — the original walks the pen with the
        /// countdown (<c>0x88 - frames</c>), so the number shifts shade as it fades.</param>
        public void FloatText(Vector3 world, string text, System.Collections.Generic.IReadOnlyList<Color> pens) {
            VisualElement viewport = _worldView?.Element;
            if (viewport == null || _pendingCamera == null || pens == null || pens.Count == 0) {
                return;
            }
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            GameFontText.Apply(label, GameFontText.AnchorX.Centre, GameFontText.AnchorY.Top);
            viewport.Add(label);
            FloatTextAsync(label, viewport, world, pens).Forget();
        }

        private async UniTaskVoid FloatTextAsync(Label label, VisualElement viewport, Vector3 world,
            System.Collections.Generic.IReadOnlyList<Color> pens) {
            float frame = (float)GameData.Resources.Combat.SpellVisuals.FrameSeconds;
            foreach (Color pen in pens) {
                Camera cam = _pendingCamera;
                if (cam == null || label.panel == null) {
                    break;
                }
                Vector3 vp = cam.WorldToViewportPoint(world);
                Rect r = viewport.contentRect;
                float w = label.resolvedStyle.width;
                float x = Mathf.Clamp(vp.x * r.width - w / 2f, 2f, Mathf.Max(2f, r.width - w - 2f));
                // The TEXT TOP sits 5 VGA rows above the sprite's top (`y = scrY - 5`), so the number
                // overlaps the head; clamped 2 rows inside the view and 12 above its bottom.
                float row = BakAgain.Graphics.Canonical.VgaScaleY;
                float y = Mathf.Clamp((1f - vp.y) * r.height - 5 * row, 2 * row, r.height - 12 * row);
                label.style.left = x;
                label.style.top = y;
                label.style.color = pen;
                await UniTask.Delay(System.TimeSpan.FromSeconds(frame));
            }
            label.RemoveFromHierarchy();
        }

        public void SetWorldCamera(Camera worldCamera) {
            _pendingCamera = worldCamera;
            _worldView?.SetWorldCamera(worldCamera);
        }

        public async UniTask ShowAsync() {
            if (_visible) {
                return;
            }
            _visible = true;
            gameObject.SetActive(true); // triggers BackgroundImageLoader + UserInterfaceLoader OnEnable
            VisualElement root = _document != null ? _document.rootVisualElement : null;
            if (root == null) {
                _logger.LogError("InGameScreen requires a UIDocument with a root visual element.");
                return;
            }
            root.style.opacity = 0f;
            BuildContent();
            _logger.LogInformation("InGameScreen shown (zone {Zone}).", _gameSession?.CurrentZone);
            await FadeRootAsync(root, 0f, 1f);
        }

        public async UniTask HideAsync() {
            if (!_visible) {
                // *** THE FLAG GOES FALSE BEFORE THE SCREEN DOES. *** _visible is cleared below
                // before the fade is awaited, and only the line after it deactivates the
                // GameObject — so "not visible" and "not on screen" are not the same thing, and a
                // HUD left active here paints over whatever replaced it. Returning on the flag
                // alone made that unrecoverable: the location's HideTravelScreenAsync asks this to
                // go down and nothing happened, so REQ_MAIN's button cluster stayed over the
                // location description (TASK-620; measured in Romney 2026-09-23 as
                // active=True IsVisible=False).
                if (this != null && gameObject.activeSelf) {
                    _logger?.LogWarning("InGameScreen was hidden but still active; forcing it down "
                        + "(TASK-620).");
                    TeardownContent();
                    gameObject.SetActive(false);
                }
                return;
            }
            _visible = false;
            VisualElement root = _document != null ? _document.rootVisualElement : null;
            if (root != null) {
                await FadeRootAsync(root, 1f, 0f);
            }
            TeardownContent();
            gameObject.SetActive(false);
            _logger.LogInformation("InGameScreen hidden.");
        }

        // Attach the world view, compass, and heads once the REQ panel is built (reconcile against
        // IsBuilt AND subscribe to Built — the cached build can fire before we subscribe).
        private void BuildContent() {
            if (_loader == null || _worldView != null) {
                return;
            }
            if (_loader.IsBuilt) {
                AttachContent();
                RefreshCastButton();
                // *** BOTH BUTTONS, ON BOTH BRANCHES. *** OnLoaderBuilt below refreshes the pair;
                // this arm refreshed only the cast button, so on the normal path — the REQ already
                // built by the time the HUD shows — follow-road was never given its first state at
                // all. Measured live: _navigableOverrides held {46:True} and no entry for 19.
                //
                // The per-frame watcher does not cover it either: it is edge-triggered against
                // _followRoadUsable/_followRoadOn, which both start false, and the opening state
                // off-road is false/false — so it matches the seed and returns without ever having
                // applied anything.
                RefreshFollowRoadButton();
            } else {
                _loader.Built += OnLoaderBuilt;
            }
        }

        private void OnLoaderBuilt(
            System.Collections.Generic.IReadOnlyList<BakAgain.UI.InputCore.NavWidget> widgets) {
            _loader.Built -= OnLoaderBuilt;
            AttachContent();
            RefreshCastButton();
            RefreshFollowRoadButton();
        }

        /// <summary>
        /// Greys out the cast button when nobody in the party can cast —
        /// <c>worldloop_pty_stat7_flag_cd</c>, which writes REQ_MAIN entry 6's enable gate from
        /// "does any active member have a non-zero AccuracyCasting MAXIMUM".
        ///
        /// <para>The maximum, not the current value, so a caster drained to nothing still gets the
        /// button — the same reading <see cref="GameData.Resources.Spells.SpellCasting.IsCaster"/>
        /// ports. Entry 6 is action 46, confirmed against REQ_MAIN rather than assumed.</para>
        ///
        /// <para>The original's gate blocks the mouse hit-test as well as keyboard navigation, so
        /// this leaves the button visible and turns both off.</para>
        /// </summary>
        private void RefreshCastButton() {
            if (_loader == null || !_loader.IsBuilt) {
                return;
            }
            bool caster = PartyHasACaster();
            _loader.SetEntryState(ActionCastSpell, visible: true, navigable: caster);
            // *** "Greys out" is a SPRITE SWAP, not a shade. *** widget_menu_draw returns on the
            // enable gate and blits BICONS1#25, the bare stone, so a partyless-of-casters HUD shows
            // an empty disc where the sigil is. Turning navigation off alone left the sigil lit and
            // the button silently dead — this doc already claimed the greying that was missing.
            _loader.SetEntryGate(ActionCastSpell, !caster);
        }

        /// <summary>
        /// Turns road-following on or off.
        /// </summary>
        /// <remarks>
        /// <b>Engaging can refuse, and the button has to survive that.</b> The party must be on a
        /// road, facing along the lattice and not already travelling; when any of that fails
        /// <see cref="PartyMovement.TryEngageTravel"/> declines and the toggle simply stays off
        /// rather than lying about it.
        /// </remarks>
        private void ToggleFollowRoad() {
            if (_movement == null) {
                return;
            }

            if (_movement.IsTravelling) {
                _movement.DisengageTravel();
            } else {
                _movement.TryEngageTravel();
            }

            RefreshFollowRoadButton();
        }

        /// <summary>
        /// Greys the follow-road button out when there is no road to follow.
        /// </summary>
        /// <remarks>
        /// While travelling it stays live whatever the party is standing on, or the only way to
        /// stop would be to walk off the road first.
        /// </remarks>
        private void RefreshFollowRoadButton() {
            if (_loader == null || !_loader.IsBuilt) {
                return;
            }
            bool usable = _movement != null
                && (_movement.IsTravelling || _movement.CanEngageTravel());
            _loader.SetEntryState(ActionFollowRoad, visible: true, navigable: usable);
            // Measured against the original off-road on 2026-09-07: it shows the bare stone
            // (BICONS1#25) here and we showed the road wedge (#23, the toggle's OFF face), so
            // nothing on screen said whether road travel was available. See TASK-362.
            _loader.SetEntryGate(ActionFollowRoad, !usable);
        }

        private bool PartyHasACaster() {
            if (_gameSession?.IsActive != true) {
                return false;
            }
            foreach (byte characterId in _gameSession.ActivePartyIndices) {
                GameData.Resources.Character.ActorStat[] stats = _gameSession.StatsOf(characterId);
                if (stats == null) {
                    continue;
                }
                var casting = stats[(int)GameData.ActorAttribute.AccuracyCasting];
                if (casting != null
                    && GameData.Resources.Spells.SpellCasting.IsCaster(casting.Max)) {
                    return true;
                }
            }
            return false;
        }

        // Only for the pit handler's lazy PartyMovement accessor — see the handler list below.
        private VContainer.IObjectResolver _resolver;

        private void AttachContent() {
            VisualElement root = _document?.rootVisualElement;
            if (root == null || _worldView != null) {
                return;
            }
            // World viewport RT as a child of hotspot_192.
            _worldView = new WorldViewportView(_worldViewport, _viewportRegistry, _logger);
            _worldView.SetWorldCamera(_pendingCamera);
            _worldView.Attach(root);
            var interactionHandlers = new System.Collections.Generic.List<BakAgain.World.Interaction.IWorldInteractionHandler> {
                new BakAgain.World.Interaction.ContainerInteractionHandler(
                    _gameSession, _dialogManager, _inventoryMenu, _navigator, _puzzles, _clock,
                    (x, y) => _resolver?.Resolve<BakAgain.World.WorldRuntime>()
                        ?.PlayChestExplosionAsync(x, y) ?? UniTask.CompletedTask),
                new BakAgain.World.Interaction.DoorInteractionHandler(
                    _gameSession, _dialogManager, _inventoryMenu, _navigator, _doorVisuals),
                new BakAgain.World.Interaction.BuildingInteractionHandler(
                    _gameSession, _dialogManager, _inventoryMenu, _navigator, _locations,
                    // Lazily, for the reason spelled out on the pit handler below: the fight lives
                    // on WorldRuntime, which is built at a different moment from this list.
                    (x, y) => _resolver?.Resolve<BakAgain.World.WorldRuntime>()
                        ?.FireTrapEncounterAt(x, y) ?? BakAgain.World.Hotspots.HotspotService.TrapDispatch.Proceed),
                new BakAgain.World.Interaction.TraversalInteractionHandler(
                    _gameSession, _dialogManager, _inventoryMenu, _navigator),
                // Tunnels are NOT on the traversal handler — see TunnelClick. Lazily resolved for
                // the same reason the trap springers above are: the hotspot table lives on
                // WorldRuntime, which is built at a different moment from this list.
                new BakAgain.World.Interaction.TunnelInteractionHandler(
                    _gameSession, _dialogManager,
                    (x, y) => _resolver?.Resolve<BakAgain.World.WorldRuntime>()
                        ?.FireZoneCrossingAt(x, y) ?? false),
                new BakAgain.World.Interaction.RiftMachineInteractionHandler(
                    _gameSession, _dialogManager),
                new BakAgain.World.Interaction.CatapultInteractionHandler(
                    _gameSession, _dialogManager),
                new BakAgain.World.Interaction.GraveInteractionHandler(
                    _gameSession, _dialogManager, _inventoryMenu, _navigator,
                    (x, y) => _resolver?.Resolve<BakAgain.World.WorldRuntime>()
                        ?.FireTrapEncounterAt(x, y) ?? BakAgain.World.Hotspots.HotspotService.TrapDispatch.Proceed),
                // *** RESOLVED LAZILY, WHICH IS WHY THERE IS NO WIRING MOMENT. *** The pit crossing
                // is the only click that MOVES the party, and PartyMovement is built during the
                // world build while this list is assembled with the HUD. Pushing a seam in would
                // need a moment when both exist — world build is too early and the fight-start pass
                // that wires the corpse and combat seams is too late for a travel-time click.
                // WorldRuntime is a DI singleton whose constructor does not depend on this screen,
                // so asking for it on demand sidesteps the ordering entirely.
                new BakAgain.World.Interaction.PitInteractionHandler(
                    _gameSession, _dialogManager,
                    () => _resolver?.Resolve<BakAgain.World.WorldRuntime>()?.Movement),
            };
            // Same document root WorldViewportView.Attach got — picking must map through the same
            // on-screen box the viewport itself was drawn into, so both resolve the stage from this
            // root (lazily, see CanonicalStage.FindOrCached) rather than being handed a snapshot
            // that may still be null at this point in the build order.
            _interaction = new WorldInteractionController(
                _pendingCamera, _worldViewport, _pointer, root, interactionHandlers,
                // A body on the arena is not a world object, so it comes back from the pick as
                // itself. The distance is measured here because this is what holds the camera.
                // *** THE CELL UNDER THE CURSOR NAMES THE TARGET, NOT THE SPRITE. *** The seam
                // hands back a roster identity because that is all the arena wants; see
                // HotspotService.CombatantAtPoint for why the pick is a grid lookup (TASK-589).
                pickCombatant: (slot, party, isPrimary) =>
                    _hotspotTarget?.Invoke(slot, party, isPrimary),
                pickGround: p => _hotspotGround?.Invoke(p),
                combatantAtPoint: p => _combatantAtPoint?.Invoke(p),
                hoverPointOverride: () => TouchInputState.Instance?.CombatHoverScreenPoint,
                lootCorpse: (corpse, isPrimary) => {
                    if (corpse == null || _pendingCamera == null) {
                        return;
                    }
                    long fine = (long)(UnityEngine.Vector3.Distance(
                        _pendingCamera.transform.position, corpse.transform.position)
                        * BakAgain.World.Converters.BakCoordinateConverter.WorldScale);
                    _hotspotLoot?.Invoke(corpse.RosterSlot, corpse.EncounterNumber, fine, isPrimary);
                });

            // Compass into the FRAME.SCR compass window. That window is a fixed design rect with no
            // native REQ element, so the extractor synthesizes one (UserInterface.CompassWindowActionId)
            // at the RE-verified drawCompass rect — read it here like any REQ element. Parent to the
            // centered canonical stage (not the raw root) so the canonical rect lands correctly at any aspect.
            if (_loader.TryGetElementRect(
                    GameData.Resources.Menu.UserInterface.CompassWindowActionId, out Rect compassRect)) {
                _compass = new CompassView(_gameSession, _resources);
                _compass.BuildAsync(CanonicalStage.GetOrCreate(root, _loader.Frame), compassRect, this).Forget();
            } else {
                _logger.LogWarning("InGameScreen: compass window REQ element missing; compass skipped.");
            }

            if (_loader.TryGetElementRect(ActionWorldViewport, out Rect viewportRect)) {
                _combatFrame = BuildCombatFrame(CanonicalStage.GetOrCreate(root, _loader.Frame), viewportRect.yMax);
            }
            // The compass's arrow buttons are REQ_MAIN widgets, so they sit above the combat frame;
            // the arena's REQ has none, so a fight hides them with the rest of the compass.
            var arrows = new System.Collections.Generic.List<VisualElement>();
            foreach (int id in new[] { ActionMoveForward, ActionMoveBackward, ActionTurnLeft, ActionTurnRight }) {
                VisualElement arrow = root.Q($"imagebutton_{id}");
                if (arrow != null) {
                    arrows.Add(arrow);
                }
            }
            _compassArrows = arrows.ToArray();
            _combatChromeShown = false; // a rebuilt HUD starts as travel; Update re-applies a fight

            // The active-spell effects strip, at the top of the frame. Its position is the
            // original's own and not a REQ element, so it comes from the layout model rather than
            // from a rect looked up here.
            _effectCaption = new SpellEffectCaptionView(_gameSession, _resources, _logger);
            _effectCaption.BuildAsync(CanonicalStage.GetOrCreate(root, _loader.Frame), this).Forget();

            // The arena's parchment readout -- the shoot menu's target panel and the melee attack
            // preview both draw on it. Built here rather than on a REQ panel because it sits at
            // screen coordinates REQ knows nothing about, and because the two menus that share the
            // strip must not each own a copy -- see HudParchmentPanelView.
            _combatPanel = new BakAgain.UI.Combat.HudParchmentPanelView(_resources, _logger);
            _combatPanel.BuildAsync(CanonicalStage.GetOrCreate(root, _loader.Frame), this).Forget();

            // The Android touch aids, in the side bars only (spec 2026-09-29-android-touch-aids-design.md).
            if (BakAgain.UI.InputCore.TouchInputState.Instance != null) {
                _touchControls = new TouchControlsView(BakAgain.UI.InputCore.TouchInputState.Instance, _pointer,
                    new GameData.Resources.Layout.TouchControlsLayout());
                _touchControls.Build(root, CanonicalStage.GetOrCreate(root, _loader.Frame));
                // Combat: the cursor feeds the hover pick; attacks take the mouse click's path.
                _touchTargeting = new CombatTouchTargeting(TouchInputState.Instance,
                    p => _interaction?.CombatantAtScreenPoint(p),
                    (slot, party, primary) => _hotspotTarget?.Invoke(slot, party, primary));
                _touchControls.MoveRequested += GroundClickAtCursor;
                _touchControls.MeleeRequested += thrust => _touchTargeting?.Melee(thrust);
            }

            // Party heads into portrait hotspots (existing view).
            _partyHeads = new PartyHeadsView(_gameSession, _resources);
            _partyHeads.Attach(root);
            _partyHeads.RenderAsync().Forget();
        }

        /// <summary>
        /// Runs a committed cast.
        /// </summary>
        /// <remarks>
        /// A cancelled choice reports a negative power and is simply dropped — the original's
        /// dispatcher matches nothing for it, the same as for a spell it does not recognise.
        /// </remarks>
        private void OnSpellCommitted(int spellNumber, int power, int duration) {
            if (power <= 0 || _fieldSpells == null || _castScreen == null) {
                return;
            }
            // *** A CAST IN A FIGHT IS NOT A FIELD CAST. *** HotspotService owns that one and arms
            // target selection with it; running the field dispatcher too would resolve the same
            // commit twice — and for the nine field spells it would actually do something.
            if (_inCombat?.Invoke() == true) {
                return;
            }
            _fieldSpells.CastAsync(_castScreen.CasterId, spellNumber, power, duration).Forget();
        }

        /// <summary>
        /// Keeps the follow-road button in step with the movement, which changes without input.
        /// </summary>
        /// <remarks>
        /// Gated on the state actually changing: SetEntryState re-raises Built, so refreshing every
        /// frame would rebuild the REQ every frame.
        /// </remarks>
        private void RefreshFollowRoadState() {
            if (_movement == null) {
                return;
            }
            bool usable = _movement.IsTravelling || _movement.CanEngageTravel();
            if (usable == _followRoadUsable && _movement.IsTravelling == _followRoadOn) {
                return;
            }
            _followRoadUsable = usable;
            _followRoadOn = _movement.IsTravelling;
            RefreshFollowRoadButton();
        }

        private bool _followRoadUsable;
        private bool _followRoadOn;

        /// <summary>
        /// Holds the encamp button in its highlighted frame for as long as the camp panel is up.
        /// </summary>
        /// <remarks>
        /// The camp panel is the only travel-HUD screen that leaves this button strip showing —
        /// the cast screen replaces the strip with its own entries, and map/bookmark/options open
        /// no screen at all — so this is the whole of the rule on this screen rather than a first
        /// case of several. See <see cref="UserInterfaceLoader.SetEntryHeld"/> for the measurement.
        ///
        /// <para>Reconciled per frame like <see cref="RefreshFollowRoadState"/> rather than pushed
        /// from <see cref="PrimaryAction"/>: the panel closes from its own Exit button, so nothing
        /// on this side sees the release.</para>
        /// </remarks>
        private void RefreshEncampButtonFace() {
            bool held = _campMenu != null && _campMenu.IsOpen;
            if (held == _encampHeld) {
                return;
            }
            _encampHeld = held;
            _loader?.SetEntryHeld(ActionEncamp, held);
        }

        private bool _encampHeld;

        private void TeardownContent() {
            if (_loader != null) {
                _loader.Built -= OnLoaderBuilt;
            }
            _worldView?.Dispose(); _worldView = null;
            _partyHeads?.Dispose(); _partyHeads = null;
            _effectCaption?.Dispose(); _effectCaption = null;
            _combatPanel?.Dispose(); _combatPanel = null;
            _touchControls?.Dispose(); _touchControls = null;
            _touchTargeting = null;
            _compass = null;
            _combatFrame = null;
            _compassArrows = System.Array.Empty<VisualElement>();
            _interaction = null;
        }

        private async UniTask FadeRootAsync(VisualElement root, float from, float to) {
            if (_fadeSeconds <= 0f) { root.style.opacity = to; return; }
            root.style.opacity = from;
            float elapsed = 0f;
            while (elapsed < _fadeSeconds) {
                elapsed += Time.unscaledDeltaTime;
                root.style.opacity = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / _fadeSeconds));
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            root.style.opacity = to;
        }

        // Teardown safety net: dispose all sub-views even if this GameObject is
        // deactivated/destroyed by a path other than Hide(). Idempotent: when Hide() drove
        // the deactivation it already disposed and nulled, so this is a no-op then.
        private void OnDisable() {
            if (_loader != null) {
                _loader.Built -= OnLoaderBuilt;
            }
            _worldView?.Dispose(); _worldView = null;
            _partyHeads?.Dispose(); _partyHeads = null;
            _effectCaption?.Dispose(); _effectCaption = null;
            _combatPanel?.Dispose(); _combatPanel = null;
            _touchControls?.Dispose(); _touchControls = null;
            _touchTargeting = null;
            _compass = null;
            _combatFrame = null;
            _compassArrows = System.Array.Empty<VisualElement>();
            _interaction = null;
        }

        // Per-frame: refresh the compass + world view, and drive movement — but only while the
        // travel surface owns input. The gate (TravelLayerHost.IsInputActive) is false whenever a
        // menu/modal is resolved above travel, so a dialog/help/confirm over the HUD structurally
        // stops movement (the old poller ran unconditionally). Movement itself lives in the
        // swappable IMovementDriver.
        /// <summary>
        /// The bookmark quick-save — <c>mainmenu_save_bookmark</c>, reached from the world loop's
        /// action 0x30.
        /// </summary>
        /// <remarks>
        /// <b>It writes slot 0 of the CURRENT save directory</b>, so it refuses when there is none —
        /// the button cannot work until the player has saved once, because there is nowhere to put
        /// the file. The rules (slot, header name, dialogs) are <see cref="BookmarkSave"/>.
        ///
        /// <para><b>A failed write says nothing</b>, deliberately: only the success path speaks in
        /// the original, and the failure is not one the player can act on.</para>
        /// </remarks>
        private async void SaveBookmark() {
            if (!BookmarkSave.CanSave(_gameSession != null && _gameSession.HasSaveLocation)) {
                await _dialogManager.ShowById(BookmarkSave.NoSlotDialog);
                return;
            }
            bool ok = await _saveGameService.SaveAsync(
                _gameSession.CurrentSaveDirectory, BookmarkSave.Slot, BookmarkSave.HeaderName);
            if (!ok) {
                // Silent by design — see the remark above.
                _logger.LogError("Bookmark write failed for {Dir}.", _gameSession.CurrentSaveDirectory);
                return;
            }
            await _dialogManager.ShowById(BookmarkSave.SavedDialog);
        }


        private void Update() {
            if (!_visible) {
                return;
            }
            _compass?.Refresh();
            _touchControls?.Refresh(AFightIsRunning());
            HandleTouchLongPress();
            ClearTouchTargetingAfterAFight();
            HandleTouchCursor();
            _effectCaption?.Refresh();
            RefreshCombatChrome();
            RefreshShootPanel();
            TurnActingCombatantToCursor();
            // Every frame rather than on a dirty flag: the original raises lightNeedsUpdate whenever
            // a source moves AND recomputes on the clock, and a source that flickers (dragon's
            // breath) changes without anything setting a flag. Recomputing is a handful of integer
            // operations and three global shader writes.
            _lighting?.Refresh();
            RefreshFollowRoadState();
            RefreshEncampButtonFace();
            _worldView?.Tick();
            // Not in a fight: the arena runs instead of the world loop in the original, and the touch
            // aids' C3 pad (which sets the same held-pad state) moves the combat cursor there.
            _movementDriver?.Tick(_travelHost != null && _travelHost.IsInputActive && !AFightIsRunning());
            // The world's ambient SFX, ticked where the original ticks it — from the world loop.
            // The driver converts frames to game ticks itself, so this passing Time.deltaTime does
            // NOT tie the sound rate to the frame rate.
            BakAgain.Audio.AmbientSoundDriver.Instance?.Tick(Time.deltaTime);
            // Flow's own per-iteration work — currently the chapter transition a dialog asked for.
            // Runs while the travel screen is VISIBLE, which it still is under a dialog: the pump
            // itself waits for GameSession.DialogsPlaying to reach 0.
            _worldLoopPump?.Invoke();
            // The world loop's other per-iteration job: a pit recorded by the previous step drops
            // the party now, a frame after the step onto it rendered.
            _movement?.TickPendingDescent();
            EndTheLoopIfThePartyIsDown();
            TellQueuedAfflictions();
        }

        /// <summary>
        /// Keeps the shoot menu's parchment showing whatever the cursor is over.
        /// </summary>
        /// <remarks>
        /// <b>The pick only runs while the fight has asked for the panel.</b> The seam is null
        /// outside a fight and answers null whenever the shoot menu is down, so travel costs one
        /// null test per frame and nothing else — but the null answer still has to reach the view,
        /// or the panel would stay on screen after the menu it belongs to closed.
        ///
        /// <para><b>Per frame rather than on a cursor-move event</b>, because that is the question
        /// being asked: the original redraws on <c>hudState = 2</c>, raised when the cursor lands on
        /// a different tile, and a tile change is not a pointer event — the party's camera can move
        /// the arena under a stationary cursor. The view compares what it is about to draw against
        /// what it drew and skips the rebuild when they match.</para>
        /// </remarks>
        /// <summary>
        /// Turns the acting combatant to face the cursor — <c>combat_actor_face_cursor</c> @0x5EBBB.
        /// </summary>
        /// <remarks>
        /// <b>Per frame, like the shoot panel above, and for the same reason.</b> The original
        /// redraws on a TILE change rather than a pointer event, because the party's camera can move
        /// the arena under a stationary cursor. Asking every frame and letting the seam answer null
        /// when the facing is unchanged reproduces that without a pointer-move subscription.
        ///
        /// <para>Cheap when idle: one raycast, and the seam is null outside a fight.</para>
        /// </remarks>
        private void TurnActingCombatantToCursor() {
            if (_combatFace == null || _interaction == null || !(_inCombat?.Invoke() ?? false)) {
                return;
            }
            UnityEngine.Vector3? floor = _interaction.HoverGroundPoint();
            if (floor.HasValue) {
                _combatFace(floor.Value);
            }
        }

        // Turns the acting combatant toward a floor point. Set with the other combat seams; null
        // outside a fight, which is what gates the work above.
        private System.Action<UnityEngine.Vector3> _combatFace;

        internal void SetCombatFaceSeam(System.Action<UnityEngine.Vector3> face) =>
            _combatFace = face;

        /// <summary>
        /// The arena's own frame, laid over the travel frame's lower band.
        /// </summary>
        /// <remarks>
        /// <b>The original swaps the whole screen for a fight</b>: <c>combat_arena_round_trans_show</c>
        /// loads <c>cframe.scx</c> (<c>COMBAT.C:879</c>), which has no compass diamond, one portrait
        /// socket and a parchment where FRAME.SCR has three portrait holes. Above the viewport's
        /// bottom edge the two frames are the same art, and FRAME.SCR's viewport area is opaque (see
        /// <c>CastScreen.DrawChromeAsync</c>), so only the band below it is laid over -- behind the REQ
        /// widgets and the combat panels, which draw their own parchment and portrait on top.
        /// </remarks>
        private VisualElement BuildCombatFrame(VisualElement stage, float splitY) {
            var band = new VisualElement {
                name = "combat_frame",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = 0,
                    top = splitY,
                    width = BakAgain.Graphics.Canonical.Width,
                    height = BakAgain.Graphics.Canonical.Height - splitY,
                    overflow = Overflow.Hidden,
                    display = DisplayStyle.None,
                },
            };
            var image = new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = 0,
                    top = -splitY,
                    width = BakAgain.Graphics.Canonical.Width,
                    height = BakAgain.Graphics.Canonical.Height,
                    // The SCX is 320x200 source pixels; the stage is canonical 1600x1200.
                    backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100)),
                },
            };
            band.Add(image);
            stage.Insert(0, band);
            LoadCombatFrameAsync(image).Forget();
            return band;
        }

        private async UniTaskVoid LoadCombatFrameAsync(VisualElement image) {
            Sprite frame = await _resources.LoadAssetAsync<Sprite>(CombatFrameAddress, this);
            if (frame == null) {
                _logger.LogWarning("InGameScreen: {Frame} did not load; a fight keeps the travel frame.",
                    CombatFrameAddress);
                return;
            }
            image.style.backgroundImage = Background.FromSprite(frame);
        }

        /// <summary>
        /// A fight does not run REQ_MAIN, so none of its entries answer while one is up.
        /// </summary>
        /// <remarks>
        /// <b>The travel HUD stays on screen during a fight and its hotspots stayed LIVE under the
        /// combat strip.</b> The band <see cref="BuildCombatFrame"/> paints over them is
        /// <c>PickingMode.Ignore</c>, so a click on what looks like the combat stats readout went
        /// straight through to the party-portrait hotspot beneath and opened that member's pack.
        /// Reported 2026-09-20 (TASK-590), and it was true of every other entry too — map, cast,
        /// encamp, options, bookmark, and the right-click help and character sheet on top of them.
        ///
        /// <para>The original cannot have this bug because it never runs the page. COMBAT.C:118
        /// loads combat.dat and shoot.dat and the fight loop polls those —
        /// <c>menupage_run(g_combat_menu)</c> at :1736, <c>g_shoot_menu</c> at :1633 — while
        /// REQ_MAIN's page, which owns portrait actions 2/3/4, is simply not polled. The faithful
        /// port of "not polled" is "answers nothing".</para>
        ///
        /// <para><b>Here rather than on the elements' <c>pickingMode</c>, for two reasons.</b> The
        /// per-frame refreshers (<see cref="RefreshCastButton"/>,
        /// <see cref="RefreshFollowRoadButton"/>) rewrite that field from their own rules and would
        /// hand entries back mid-fight; and picking governs the MOUSE only, while the REQ
        /// accelerators reach these same actions from the keyboard.</para>
        ///
        /// <para>Reads the predicate rather than <see cref="_combatChromeShown"/>, which is a
        /// paint flag: a rebuilt HUD resets it to false and waits for the next frame to re-apply a
        /// fight, and a click in that window is exactly what this refuses.</para>
        ///
        /// <para>The world viewport is the one entry that stays live — the arena is clicked
        /// through it.</para>
        /// </remarks>
        internal bool RefusedDuringAFight(int actionId) =>
            (_inCombat?.Invoke() ?? false) && actionId != ActionWorldViewport;

        // Every frame, so only a change does anything (TASK-576 was this call without the guard).
        private void RefreshCombatChrome() {
            bool inCombat = _inCombat?.Invoke() ?? false;
            if (inCombat == _combatChromeShown) {
                return;
            }
            _combatChromeShown = inCombat;
            if (_combatFrame != null) {
                _combatFrame.style.display = inCombat ? DisplayStyle.Flex : DisplayStyle.None;
            }
            foreach (VisualElement arrow in _compassArrows) {
                arrow.style.display = inCombat ? DisplayStyle.None : DisplayStyle.Flex;
            }
            _compass?.SetVisible(!inCombat);
        }

        private void RefreshShootPanel() {
            if (_combatPanel == null) {
                return;
            }
            if (_combatPanelContent == null) {
                _combatPanel.Show(null);
                return;
            }
            (int RosterSlot, bool PartyMember)? hovered = _interaction?.HoverCombatant();
            // The mouse's cursor cell, marked as the original marks it: the move marker on a cell
            // the actor can walk to (WORLDHIT.C:490; COMBAT.C:2324-2326). Touch sets it itself.
            if (_pointer != null && _pointer.IsPresent && AFightIsRunning()) {
                Vector3? floor = _interaction?.HoverGroundPoint();
                _setCursorCell?.Invoke(floor.HasValue ? _cellAtPoint?.Invoke(floor.Value) : null, false);
            }
            var content = _combatPanelContent(
                hovered?.RosterSlot ?? -1, hovered.HasValue && hovered.Value.PartyMember);
            _combatPanel.Show(content.Lines, content.Rules);

            // *** THE FACE IN THE HUD IS WHOEVER IS ACTING. *** The stats-panel routine draws it,
            // and the travel HUD's own three portraits are not drawn during a fight at all -- see
            // ActorStatsPanel.PortraitImage. A fight with no acting party member (a monster's turn)
            // hides it, which is what the original's charSlot guard does.
            int head = _actingHeadId?.Invoke() ?? -1;
            _combatPanel.ShowPortrait(head);
            _partyHeads?.SetVisible(head < 0 && !(_inCombat?.Invoke() ?? false));
        }

        // The HEADS.BMX frame for whoever is acting, or -1. Set with the other combat seams.
        private System.Func<int> _actingHeadId;

        internal void SetActingHeadSeam(System.Func<int> head) => _actingHeadId = head;

        /// <summary>
        /// <c>post_dispatch</c> — the world loop's own exit test (<c>WORLDLP.C:399</c>).
        /// </summary>
        /// <remarks>
        /// <b>Any non-zero party-down state ends the loop</b>, and the original answers by returning
        /// mode 6, which <c>main()</c> turns straight into the main menu
        /// (<c>GMAIN.C:734</c>: <c>if (mode == 6) mode = mainmenu_save_main_menu(0)</c>). So the
        /// destination is the original's, not a design choice.
        ///
        /// <para><b>Last in Update because it is last there</b> — the drivers, the ambient tick and
        /// the descent all get their iteration before the loop is allowed to end.</para>
        ///
        /// <para><b>And it must not fire mid-fall.</b> The original's descent is one call that runs
        /// the whole drop, flags the party and plays its landing dialog before <c>post_dispatch</c>
        /// is reached; ours spreads the same drop over
        /// <see cref="GameData.Resources.World.PitDescent.DescentSteps"/> frames, so exiting the
        /// moment the flag is set would cut the fall off at its first frame and swallow the dialog
        /// that explains it.</para>
        ///
        /// <para>The one-shot latch clears itself whenever the state reads
        /// <see cref="PartyDownState.Standing"/> again, which is what loading or starting a game
        /// does — so a second wipe in a later session exits the same way.</para>
        /// </remarks>
        private void EndTheLoopIfThePartyIsDown() {
            if (_gameSession == null
                || !PartyDownState.EndsTheLoop(_gameSession.PartyDeathState)) {
                _partyDownExitStarted = false;
                return;
            }
            if (_partyDownExitStarted || (_movement?.IsFalling ?? false) || AFightIsRunning()) {
                return;
            }
            _partyDownExitStarted = true;
            LeaveTheWorldPartyDownAsync().Forget();
        }

        private BakAgain.Core.Services.PartyUpkeepService _afflictionUpkeep;

        /// <summary>
        /// Plays an affliction announcement the clock queued while the party walked or a dialog skipped
        /// time — see <see cref="BakAgain.Core.Services.PartyUpkeepService.PlayAnnouncementsAsync"/>.
        /// Held while a fight runs, where the original never reaches the hourly tick.
        /// </summary>
        private void TellQueuedAfflictions() {
            if (AFightIsRunning()) {
                return;
            }
            _afflictionUpkeep ??= _resolver?.Resolve(typeof(BakAgain.Core.Services.PartyUpkeepService))
                as BakAgain.Core.Services.PartyUpkeepService;
            if (_afflictionUpkeep == null || _afflictionUpkeep.PendingAnnouncements.Count == 0) {
                return;
            }
            // Tied to the screen's lifetime: this is fire-and-forget, so without a token a
            // torn-down announcement leaves _announcing true and every later rest waits on it.
            _afflictionUpkeep.PlayAnnouncementsAsync(_dialogManager, destroyCancellationToken)
                .Forget();
        }

        /// <summary>
        /// <b>Exactly 1 speaks; 2 does not.</b> <c>WORLDLP.C:413</c> tests <c>== 1</c>, not
        /// non-zero: the pit and the arena's last kill write 2 precisely because each has already
        /// shown a dialog of its own, and widening the test doubles the message.
        /// </summary>
        private async UniTaskVoid LeaveTheWorldPartyDownAsync() {
            try {
                if (PartyDownState.PlaysTheNoticedDialog(_gameSession.PartyDeathState)) {
                    await _dialogManager.ShowById(PartyDownState.NoticedDialogId);
                }
                var flow = _resolver?.Resolve<BakAgain.Core.Services.IGameFlow>();
                if (flow == null) {
                    _logger?.LogError("Party is down but no IGameFlow to leave the world with.");
                    _partyDownExitStarted = false;
                    return;
                }
                await flow.ShowMainMenu();
            } catch (System.Exception failure) {
                // *** A FAULT HERE MUST NOT COST THE GAME-OVER PERMANENTLY. *** This is
                // fire-and-forget behind a one-shot latch, and the latch only clears when the state
                // reads Standing again — which a downed party never does. So without this, ANY
                // fault inside the exit leaves the player walking a party that has already lost,
                // able to camp, heal and save, with no way back (TASK-617). Clearing the latch lets
                // the next frame try again instead.
                if (!RetriesAfter(failure)) {
                    throw;
                }
                _logger?.LogError(
                    "Party-down exit failed ({Failure}); clearing the latch so the next frame retries.",
                    failure);
                _partyDownExitStarted = false;
            }
        }

        /// <summary>
        /// Whether a failed party-down exit should be retried on the next frame.
        /// </summary>
        /// <remarks>
        /// <b>Cancellation is not a failure to retry.</b> A cancelled exit means the screen is being
        /// torn down underneath the transition, so re-arming would spin against a dying object; the
        /// latch is left set and the cancellation propagates. Everything else is a fault the player
        /// should not be punished for, and re-arming costs one frame.
        /// </remarks>
        internal static bool RetriesAfter(System.Exception failure) =>
            !(failure is System.OperationCanceledException);

        /// <summary>
        /// <b>The world loop cannot end while a fight is on the field.</b>
        /// </summary>
        /// <remarks>
        /// In the original the question does not arise: the arena is a nested call inside the world
        /// loop, so <c>post_dispatch</c> is not reached until it has returned and torn itself down.
        /// Our fight runs beside this per-frame Update, and a party wiped out in the arena sets the
        /// byte DURING it — so without this the menu comes up over a live combat screen, which is
        /// exactly what a play-through produced.
        ///
        /// <para>Waiting rather than ending the fight from here: settling an encounter is the
        /// combat layer's job and it already does it (<c>HotspotService.EndCombat</c>). This only
        /// has to let it finish.</para>
        /// </remarks>
        private bool AFightIsRunning() =>
            (_resolver?.Resolve(typeof(BakAgain.World.WorldRuntime))
                as BakAgain.World.WorldRuntime)?.FightInProgress ?? false;

        // Update runs every frame and the exit is asynchronous, so without this the menu
        // transition would be started once per frame until it completed.
        private bool _partyDownExitStarted;

        private TouchControlsView _touchControls;
        private readonly TouchHoldDetector _touchHold = new TouchHoldDetector();
        private int _touchPressSerial;
        private CombatTouchTargeting _touchTargeting;
        private bool _touchFightWasRunning;

        // A fight played by touch: the battlefield tap places the combat cursor.
        private bool TouchFight() =>
            TouchInputState.Instance != null && AFightIsRunning()
            && _pointer != null && _pointer.CanPoint && !_pointer.IsPresent;

        // ---- The combat cursor (owner's choice, 2026-10-01) -----------------------------------

        private CombatCursor _combatCursor;
        private int _cursorHeldAction = -1;
        private float _cursorRepeatIn;
        private const float CursorRepeatDelay = 0.38f;   // the arrows' own dead time (ClassicMovementDriver)
        private const float CursorRepeatInterval = 1f / 8.84f;

        // Through the world viewport, as the ground pick goes the other way: the arena camera
        // renders into a texture, so its own WorldToScreenPoint is not the screen.
        private Vector2? CellOnScreen(int column, int row) =>
            _cellWorld?.Invoke(column, row) is Vector3 world ? _interaction?.ScreenPointOfGround(world) : null;

        /// <summary>
        /// The pad moves a cell cursor (held: the arrows' dead time, then repeats); the cell under it
        /// drives the same hover path a mouse would — its ring and the Thrust/Swing preview — and
        /// picks the side bar's buttons.
        /// </summary>
        private void HandleTouchCursor() {
            TouchInputState touch = TouchInputState.Instance;
            if (touch == null || !TouchFight()) {
                _combatCursor = null;
                return;
            }
            if (_combatCursor == null) {
                _combatCursor = new CombatCursor(CellOnScreen);
                if (_actingCell?.Invoke() is (int ac, int ar)) {
                    _combatCursor.Cell = (ac, ar);
                }
            }
            int held = touch.HeldTouchAction;
            if (held != _cursorHeldAction) {
                _cursorHeldAction = held;
                _cursorRepeatIn = CursorRepeatDelay;
                StepCursor(held);
            } else if (held >= 0) {
                _cursorRepeatIn -= Time.unscaledDeltaTime;
                if (_cursorRepeatIn <= 0f) {
                    StepCursor(held);
                    _cursorRepeatIn += CursorRepeatInterval;
                }
            }
            (int c, int r) = _combatCursor.Cell;
            Vector2? point = CellOnScreen(c, r);
            (int RosterSlot, bool PartyMember)? occupant = point.HasValue ? _interaction?.CombatantAtScreenPoint(point.Value) : null;
            bool waiting = _awaitingTarget?.Invoke() ?? false;
            // An enemy offers Thrust/Swing; a party member offers nothing — unless a spell or item is
            // waiting for a target, which may well be a friend (a heal).
            bool target = occupant.HasValue && (!occupant.Value.PartyMember || waiting);
            touch.CombatHoverScreenPoint = occupant.HasValue ? point : null;
            _setCursorCell?.Invoke((c, r), true);
            touch.CursorContext = target ? CursorContext.Target
                : occupant.HasValue ? CursorContext.None
                : CursorContext.Ground;
            touch.AwaitingTarget = waiting;
        }

        private void StepCursor(int padAction) {
            Vector2 dir = padAction switch {
                ActionMoveForward => Vector2.up,
                ActionMoveBackward => Vector2.down,
                ActionTurnLeft => Vector2.left,
                ActionTurnRight => Vector2.right,
                _ => Vector2.zero,
            };
            if (dir != Vector2.zero) {
                _combatCursor.Step(dir);
            }
        }

        // The Move / Cast-here button: the mouse's ground click, on the cursor's cell.
        private void GroundClickAtCursor() {
            if (_combatCursor != null && _cellWorld?.Invoke(_combatCursor.Cell.Column, _combatCursor.Cell.Row) is Vector3 floor) {
                _hotspotGround?.Invoke(floor);
            }
        }

        // A selection must not outlive its fight: the next fight's hover would read a stale point.
        private void ClearTouchTargetingAfterAFight() {
            bool running = AFightIsRunning();
            if (_touchFightWasRunning != running) {
                TouchInputState.Instance?.ForgetCombatPreview();
            }
            _touchFightWasRunning = running;
        }

        /// <summary>
        /// Travel: a still finger is the right-click — the REQ element under it gets its
        /// SecondaryAction, and the finger's release is eaten so it does not also click.
        /// </summary>
        private void HandleTouchLongPress() {
            TouchInputState touch = TouchInputState.Instance;
            if (touch == null || _touchControls == null || AFightIsRunning()) {
                // Not ticked here, so a release in these frames would never be seen: an unfinished
                // press must not become an instant long-press on the next tap.
                _touchHold.Reset();
                return;
            }
            // The finger as UI Toolkit's own events report it: the polled pointer never saw a held
            // finger on the owner's phone (2026-09-30). Every new press starts the detector afresh.
            if (_touchControls.PressSerial != _touchPressSerial) {
                _touchPressSerial = _touchControls.PressSerial;
                _touchHold.Reset();
            }
            if (!_touchHold.Tick(_touchControls.TouchDown, _touchControls.TouchPosition, Time.realtimeSinceStartup)) {
                return;
            }
            int? id = ReqActionOf(_touchControls.TouchTarget);
            if (id.HasValue && TouchInputState.LongPressApplies(id.Value)) {
                touch.SuppressSelectFor = id;   // that element's release must not also click
                _ = SecondaryAction(id.Value);
            }
        }

        // The REQ action of an element or its nearest named ancestor (hotspot_N / imagebutton_N).
        private static int? ReqActionOf(VisualElement element) {
            for (VisualElement el = element; el != null; el = el.parent) {
                string n = el.name ?? string.Empty;
                foreach (string prefix in new[] { "hotspot_", "imagebutton_" }) {
                    if (n.StartsWith(prefix) && int.TryParse(n.Substring(prefix.Length), out int id)) {
                        return id;
                    }
                }
            }
            return null;
        }

        // The REQ element under the pointer, by the same pick and naming ClassicMovementDriver uses.
        private int? ReqActionUnderPointer() {
            IPanel panel = _document?.rootVisualElement?.panel;
            if (panel == null) {
                return null;
            }
            Vector2 s = _pointer.ScreenPosition;
            Vector2 p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(s.x, Screen.height - s.y));
            for (VisualElement el = panel.Pick(p); el != null; el = el.parent) {
                string n = el.name ?? string.Empty;
                foreach (string prefix in new[] { "hotspot_", "imagebutton_" }) {
                    if (n.StartsWith(prefix) && int.TryParse(n.Substring(prefix.Length), out int id)) {
                        return id;
                    }
                }
            }
            return null;
        }

        // Left-click / key dispatch. STUBBED: each branch logs its intent. The real
        // movement, encamp, cast, map, options and party-screen transitions plug in here.
        public void PrimaryAction(int menuEntryActionId) {
            if (RefusedDuringAFight(menuEntryActionId)) {
                return;
            }
            switch (menuEntryActionId) {
                case ActionMoveForward:
                    _movement?.MoveForward();
                    break;
                case ActionMoveBackward:
                    _movement?.MoveBackward();
                    break;
                case ActionTurnLeft:
                    _movement?.TurnLeft();
                    break;
                case ActionTurnRight:
                    _movement?.TurnRight();
                    break;
                case ActionFollowRoad:
                    ToggleFollowRoad();
                    break;
                case ActionMap:
                    // The overhead map — the same world from above, in the same chrome. Pushed, so
                    // this screen hides and releases the world camera before that one claims it.
                    if (_overheadMap != null) {
                        _navigator.Push(_overheadMap).Forget();
                    }

                    break;
                case ActionCastSpell:
                    // The button is already greyed out when nobody can cast (see the enable pass
                    // above), so reaching here means the party has a caster.
                    if (_castScreen != null) {
                        _navigator.Push(_castScreen).Forget();
                    }

                    break;
                case ActionBookmark:
                    SaveBookmark();
                    break;
                case ActionEncamp:
                    // *** NOTHING IN VIEW, OR THE PANEL DOES NOT OPEN AT ALL. *** encamp_run's first
                    // act is `if (proxscan_vis_rec_kind_0e3e()) dialog_play_record(0x65, 1);` and a
                    // return — no dial, no rest, no time passed (ENCAMP.C:78-80). Measured against
                    // the original on dir.G01/SAVE13: it refused with that line where we rested the
                    // party down to 7/55, 3/40 and 27/60 health with no stamina and no rations left.
                    // Resolved lazily for the reason the interaction handlers above are.
                    if (_resolver?.Resolve<BakAgain.World.WorldRuntime>()?.CampIsWatched() == true) {
                        _dialogManager?.ShowById(
                            (int)GameData.Resources.World.CampRefusal.WatchedDialogId).Forget();
                        break;
                    }
                    //
                    // Opened directly, NOT pushed on the navigator: the camp panel covers the 3D
                    // viewport and the HUD stays visible around it, exactly as in the original. A
                    // Push would hide this screen (one shown at a time) and take the portraits,
                    // compass and buttons with it — which is the bug this replaced.
                    _campMenu?.Open();
                    break;
                case ActionOptions:
                    // Push the in-game menu (REQ_OPT1) onto the screen stack. Its MenuLayerHost pushes
                    // a NavigableLayer that becomes the input stack's resolved target, so
                    // TravelLayerHost.IsInputActive goes false and the party stops while it's up.
                    // (This travel screen itself isn't stack-managed until migration step 3, so the
                    // menu is the stack's first entry; its Cancel pops back to nothing-hidden.)
                    _navigator.Push(_inGameMenu).Forget();
                    break;
                case ActionPartyMember1:
                case ActionPartyMember2:
                case ActionPartyMember3: {
                    // Plain portrait click → that member's inventory (WORLDLP.C:367,
                    // cmbinv_inventory_screen_run(NULL, slot+1, 0)). The original's right-click /
                    // Shift+click branch opens the character sheet instead — not built yet.
                    int slot = menuEntryActionId - ActionPartyMember1;
                    if (_inventoryMenu != null && _inventoryMenu.SetMember(slot)) {
                        _navigator.Push(_inventoryMenu).Forget();
                    }
                    break;
                }
                case ActionWorldViewport:
                    if (TouchFight()) {
                        // A tap puts the cursor on that cell; the side bar's buttons act on it.
                        if (_combatCursor != null && _interaction?.GroundPointAtScreenPoint(_pointer.ScreenPosition) is Vector3 tapped
                            && _cellAtPoint?.Invoke(tapped) is (int tc, int tr)) {
                            _combatCursor.Cell = (tc, tr);
                        }
                        break;
                    }
                    _interaction?.HandleClick(isPrimary: true).Forget();
                    break;
                default:
                    _logger.LogDebug("InGameScreen unhandled primary action {ActionId}.", menuEntryActionId);
                    break;
            }
        }

        // Right-click help. Matches the original MainGameLoop: each button's right-click shows
        // its DDX help string (ids 223–232). The party portraits and world viewport have no
        // help text in the original, so they fall through.
        public async Awaitable SecondaryAction(int menuEntryActionId) {
            if (RefusedDuringAFight(menuEntryActionId)) {
                return;
            }
            if (menuEntryActionId == ActionWorldViewport) {
                _interaction?.HandleClick(isPrimary: false).Forget();
                return;
            }
            // Right-click / Shift on a portrait opens that member's CHARACTER SHEET where a plain
            // click opens their inventory (WORLDLP.C:360). The same secondary-click idiom the rest
            // of the game uses: asking about a thing rather than working it.
            if (menuEntryActionId >= ActionPartyMember1
                && menuEntryActionId <= ActionPartyMember1 + 2 && _characterSheet != null) {
                await _characterSheet.RunAsync(menuEntryActionId - ActionPartyMember1).AsTask();

                return;
            }

            int helpId = menuEntryActionId switch {
                ActionMoveForward => 223,
                ActionMoveBackward => 224,
                ActionTurnLeft => 225,
                ActionTurnRight => 226,
                ActionFollowRoad => 227,
                ActionCastSpell => 228,
                ActionEncamp => 229,
                ActionBookmark => 230,
                ActionOptions => 231,
                ActionMap => 232,
                _ => -1,
            };
            if (helpId < 0) {
                return;
            }
            await _dialogManager.ShowById(helpId).AsTask();
        }

        // IMenuStateProvider — the loader queries this to pick the FollowRoad toggle's
        // on/off icon frame.
        //
        // *** IT READS THE MOVEMENT, NOT A LOCAL FLAG. *** Travel ends on its own for several
        // reasons the player did not ask for — a refusal, a fork the party will not choose at, a
        // hotspot firing — so a flag toggled by the click would keep showing "on" for a road the
        // party stopped following some steps ago.
        public bool GetToggleState(int actionId) {
            return actionId == ActionFollowRoad && _movement != null && _movement.IsTravelling;
        }
    }
}
