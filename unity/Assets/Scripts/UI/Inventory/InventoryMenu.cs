namespace BakAgain.UI.Inventory {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.Core.Services;
    using BakAgain.ResourceManagement;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI;
    using BakAgain.UI.InGame;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.Dialog;
    using GameData.Resources.Character;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using GameData.Resources.Object;
    using GameData.Resources.Shop;
    using GameData.Resources.World;
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;
    using VContainer;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Controller for the loot/inventory screen (REQ_INV.DAT over INVENTOR.SCX), the Unity
    /// counterpart of <c>sub_ovr157_4E3</c> @0x546f3 and its gesture handler
    /// <c>sub_ovr158_1013</c> @0x57063. Shows one <see cref="RuntimeContainer"/> at a time — a loot
    /// container (<see cref="SetContainer"/>, e.g. a corpse or chest), or a party member's own
    /// inventory (<see cref="SetMember"/> from the travel HUD's portrait click, or clicking a
    /// portrait inside the screen). Member view = paperdoll (equipped items + placeholders) on the
    /// left, general grid on the right.
    ///
    /// <para>Interaction, faithful to the original mouse UI:
    /// <list type="bullet">
    /// <item>a plain <b>click</b> on an item <b>selects</b> it (red box outline) — the item is not
    /// removed;</item>
    /// <item>a <b>press-drag</b> lifts the item's icon to follow the cursor (centered);</item>
    /// <item>while dragging, hovering a party portrait overlays the <b>HEADS.BMX red-circle</b>
    /// highlight (pulsing frames 8–11) on that portrait;</item>
    /// <item><b>releasing over a portrait</b> moves the item into that member's inventory via
    /// <see cref="InventoryTransfer.Move"/>; releasing elsewhere snaps it back.</item>
    /// <item><b>releasing over another item</b> in a member's own grid <b>uses</b> the dragged item
    /// on it (<see cref="InventoryUse"/>) — the original's only target-selection gesture.</item>
    /// </list></para>
    ///
    /// <para>A navigator-managed Opaque screen: callers <see cref="SetContainer"/> then push it
    /// (the navigator hides the travel root beneath and re-shows it on Pop/Exit), so only this
    /// screen is live while looting. It renders into the shared <see cref="CanonicalStage"/>
    /// (1600×1200); because <see cref="UserInterfaceLoader"/> clears the stage during its async
    /// build, the grid is (re)rendered on the loader's <see cref="UserInterfaceLoader.Built"/>
    /// event.</para>
    /// </summary>
    public class InventoryMenu : BakAgain.UI.Navigation.ScreenBase, IActionHandler {
        // REQ_INV action ids: portraits = party slots 1-3, Use, Exit, gold/funds.
        private const int ButtonExit = 1;
        private const int PortraitSlot1 = 2;
        private const int PortraitSlot2 = 3;
        private const int PortraitSlot3 = 4;
        private const int ButtonUse = 22;
        private const int ButtonGold = 34;
        private const int ButtonMoreInfo = 57;        // "More Info" text button — item-inspect view only, hidden in the grid
        private const int WindowContainerImage = 32; // detail window showing the container-type image
        private const int BackgroundCatchAll = 128;  // REQ_INV full-screen (1600×1200) catch-all ClickArea

        private const float OutlineWidth = 8f;

        // Categories that refuse to be "used" from the inventory and play a flavour DDX instead —
        // objectType Repair (8), Poison (9), BowString (12) at 0x54B6F..0x54B82. Typed ObjectType:
        // the old int[] + Array.IndexOf(Array, object) compared boxed int against boxed enum and
        // never matched, so the refusal dialog could not fire.
        private static readonly GameData.ObjectType[] NotDirectlyUsableTypes = {
            GameData.ObjectType.Repair, GameData.ObjectType.Poison, GameData.ObjectType.BowString,
        };
        private const int NotDirectlyUsableDialogId = 1800010; // 0x1B774A "jam on my sword"
        private const int MustKeepEquippedDialogId = 1800014;  // 0x1B774E "strip himself of his defenses"
        private const int NoRoomDialogId = 1800008;            // 0x1B7748, Var 0 = destination kind
        private const int UseHadNoEffectDialogId = 1800003;    // 0x1B7743, Var 0 = object id
        private const int LitTorchDialogId = 1800029;          // 0x1B775D "before dropping a flaming torch"
        private const int SpareBagDialogId = 1800009;          // 0x1B7749 "shoved the @1 into a spare bag"
        private const int PartyMoneyDialogId = 1800034;        // 0x1B7762 "@4 checked their funds", Var 0 = the purse

        // The use-gate chain every "use" runs before any dispatch — itemuse_dispatch_on_target
        // (ITEMUSE.C:104-131, Use_Item @0x58cbd). Each gate plays its record and refuses.
        private const int NeedsCasterDialogId = 1800005;        // 0x1B7745 flag 0x80, member can't cast
        private const int RefusesCasterDialogId = 1800049;      // 0x1B7771 flag 0x200, member CAN cast
        private const int CombatOnlyDialogId = 1800006;         // 0x1B7746 flag 0x40, not in combat
        private const int NotInCombatDialogId = 1800007;        // 0x1B7747 flag 0x100, in combat
        private const int UsedUpDialogId = 1800044;             // 0x1B776C, Var 0 = 0 / 1 / 2

        // INVENTOR.PAL pens from UI_DrawInventory @0x569bd: selected-item outline = pen 0x8B red,
        // selection fill = pen 0x8F dark maroon. (The drag-hover portrait highlight is NOT a pen
        // outline — it's the HEADS.BMX red circle; see the hover-circle members below.)
        private static readonly Color OutlineRed = new Color(124f / 255f, 16f / 255f, 8f / 255f);
        private static readonly Color SelectFill = new Color(64f / 255f, 4f / 255f, 8f / 255f);

        // Party-portrait circles, both from HEADS.BMX. Faithful to
        // UI_DrawPartyHeadHighlightCircles(active, hover, phase) @0x562a5, which draws TWO distinct
        // rings and is called on every inventory repaint (not only during a drag):
        //
        //   * the ACTIVE member (arg_0, = actorNr) always gets the steady frame #7 — this is the ring
        //     that tells you whose inventory you're looking at (0x5632D..0x56341);
        //   * the drag-HOVER target (arg_2) gets the pulsing red ring, frames 8→9→10→11→10→9 driven by
        //     a phase counter mod 6 (0x562CA..0x5631A).
        //
        // The pulse is skipped entirely when hover == 0 OR hover == active (0x562D4/0x562D8): hovering
        // the member you're already viewing leaves their steady ring alone rather than turning it red.
        // Frames 7..11 load into _circleSprites[frame - 7]; HoverCirclePulse indexes the pulse frames.
        private const string HeadsBmx = "HEADS.BMX";
        private const int CircleFirstFrame = 7;                        // #7 steady, #8..#11 pulse
        private const int CircleFrameCount = 5;                        // #7..#11
        private const int ActiveCircleFrame = 7;
        private static readonly int[] HoverCirclePulse = { 8, 9, 10, 11, 10, 9 };
        private const float HoverCircleFrameSeconds = 0.11f;           // per-frame cadence (autoDecreasingTimer approx)
        private Sprite[] _circleSprites;                               // [i] = HEADS.BMX #(7+i)
        private readonly object _hoverCircleOwner = new object();
        private VisualElement _hoverCircle;
        private VisualElement _activeCircle;

        // One phase counter drives every drag-time pulse, as in the original: `sub_ovr158_1013`
        // keeps a single `var_10`, steps it `% 12` per frame (0x57344) and hands the same value to
        // both the head circles and the container-window border — so they pulse in lockstep. Each
        // consumer takes it `% 6`.
        private const int PulsePhaseCount = 12;
        private int _pulsePhase;

        // The action-32 container window doubles as a drop target while an item is being dragged
        // (`invui_portrait_panel_draw` / `sub_ovr158_3D0` @0x56420 draws its 1px border, pen chosen
        // by the caller at 0x57250/0x57279). What the pen says:
        //   * viewing a MEMBER's inventory  — pulsing 0x6B→0x6E→0x6C while the drag hovers the
        //     window, pen 0 (black, i.e. invisible) otherwise. This is the live drop target.
        //   * viewing the loot CONTAINER    — steady 0x69 for the whole drag, and no drop: the
        //     drop branch requires `bResidence == 1`, so here the border is decoration only.
        private const int DiscardBorderSteadyPen = 0x69;
        // The 6-step triangular pulse, shared by both drag-time borders: the container window's
        // (`invui_portrait_panel_draw`) and the paperdoll box's (`invui_portr_panel_fill_pulsing`
        // @0x563D6). Both compute the same pen from the same phase counter — `phase > 3 ? 0x71 -
        // phase : 0x6B + phase` — so they are one table, not two coincidentally equal ones.
        private static readonly int[] BorderPulsePens = GameData.Resources.Inventory.InventoryDragGesture.PortraitPulsePens;
        private VisualElement _discardBorder;
        // The paperdoll box's own drag-time border (`invui_portr_panel_fill_pulsing` @0x563D6,
        // INVENTOR.C:181 — it STROKES the box, `bGfx_fill_enabled = 0`). The original draws it on
        // every drag frame while a member's inventory is displayed, but with pen 0 — black on a
        // black fill, i.e. nothing — unless the cursor is inside the box AND the item is one this
        // member could equip (INVENTOR.C:696-703 passes the item's category, which is nonzero
        // exactly then). So the element only exists while it would actually be visible.
        private VisualElement _paperdollBorder;
        private GameData.Resources.Palette.PaletteResource _inventoryPalette;

        private ILogger _logger;
        private GameSession _gameSession;
        private IResourceProviderService _resources;
        private BakAgain.Core.Services.DialogExecutor _dialogExecutor;
        private IDialogManager _dialogs;
        private BakAgain.World.IGroundBagSpawner _groundBags;
        private InputLayerStack _inputStack; // quantity-picker modality (ActionLayer push)
        // The quantity picker is up: gate the stage gesture handlers — the picker's scrim stops
        // picks, but the stage recogniser registers TrickleDown and would still see the presses.
        private bool _pickerOpen;
        private bool _descriptionOpen; // guards re-entry while the description is being resolved
        private bool _inspectOpen;     // the inspect view is on screen (with or without More Info)
        private bool _inspectClosing;  // the close animation is running; swallow further presses
        private int _inspectStatSlot = -1; // slot whose More Info button is currently offered, else -1
        private int _inspectSlot = -1;     // the inspected slot, held for the whole view (fly-back)
        private Vector2 _inspectFrom;      // that slot's cell centre — the icon flies home to it
        private VisualElement _statsBox;   // parchment behind the stat block, built into _stage
        private UIDocument _document;
        private UserInterfaceLoader _ui;
        private MenuLayerHost _layerHost;
        // Item-cell nav widgets, refilled by ItemGridRenderer on every render.
        private readonly List<BakAgain.UI.InputCore.NavWidget> _gridNavWidgets =
            new List<BakAgain.UI.InputCore.NavWidget>();
        private readonly ItemGridRenderer _renderer = new();
        private readonly ItemInspectPanel _inspect = new();
        private readonly MoneyReadoutView _money = new();

        // Every coordinate this screen draws with — the panel fills, the paperdoll drop target, the
        // item-inspect layout, the drag threshold and the container window's hairline — comes from
        // the REQ resource's inventory block. That block is null until the async REQ load lands
        // (and for any REQ that carries none), so this falls back to the model's own defaults,
        // which ARE the faithful geometry. Same "null layout is not an error" rule ItemGridRenderer
        // follows.
        private readonly InventoryLayout _defaultLayout = new();
        private InventoryLayout Layout => _ui?.Inventory ?? _defaultLayout;

        private RuntimeContainer _container;

        /// <summary>
        /// What the window is showing — a shop shelf, a corpse, or one member's pack.
        /// </summary>
        /// <remarks>
        /// <b>Every assignment is followed by <see cref="NoteKeeperKind"/>.</b> It stays a plain
        /// field rather than becoming a property that does it automatically, because the tests and
        /// <c>scripts/unity-drive.sh</c> both reach it with <c>GetField("_displayed")</c> — turning
        /// it into a property took 33 tests down.
        /// </remarks>
        private RuntimeContainer _displayed;
        // The ground bag a discard claimed during this screen session (the original's
        // g_pDroppedItemActor), kept so leaving settles it alongside the looted container —
        // CMBINV.C:454 destroys exactly these two.
        private RuntimeContainer _droppedBag;
        private BakAgain.UI.Navigation.IScreenNavigator _navigator;

        /// <summary>The map screen a note opens, or null in a harness with no screens.</summary>
        private RiftMapScreen _riftMap;

        // Party-member portrait faces (HEADS.BMX into hotspot_2/3/4) — the reusable travel-HUD view.
        private PartyHeadsView _partyHeads;
        private bool _headsAttached;

        // Container-type image (INVMISC.BMX) drawn in the detail window (action 32). Its own release
        // owner so it isn't freed alongside the drag ghost (which releases on `this`).
        private VisualElement _containerImage;
        private readonly object _containerImageOwner = new object();
        // INVMISC.BMX index for the looted container's image, resolved from the clicked world item's
        // type in SetContainer (sub_ovr158_59E). Defaults to the dead-body sprite as a safe fallback.
        private int _containerImageIndex = ContainerImage.DeadBodyIndex;

        // Black item-panel fill boxes (UI_DrawInventory @0x5687d). Loot mode paints ONE wide box over
        // the whole item area (covering the INVENTOR.SCX divider — and, in the grid view, the "More
        // Info" button, which the original only reveals in the item-inspect view); member mode paints
        // TWO boxes, leaving the divider showing in the gap.
        private readonly List<VisualElement> _panelBoxes = new();

        // Empty paperdoll-slot placeholder sprites (member mode; INVSHP2 #10/#11), own release owner.
        private readonly List<VisualElement> _placeholders = new();
        private readonly object _placeholderOwner = new object();

        private VisualElement _stage;
        private VisualElement _ghost;
        private float _ghostW = 200f;
        private float _ghostH = 180f;

        // Where the drag was released, and what was dropped on which portrait — captured on the way
        // into the transfer because the animation runs AFTER it resolves, by which time the grid has
        // been re-rendered and the dragged slot no longer means anything.
        private Vector2 _dropStagePos;
        private int _dropPortraitSlot = -1;
        private ObjectInfo _dropObject;
        private ushort _dropItemFlags;

        // Gesture state, driven by DragGestureManipulator (UI Toolkit pointer events + capture).
        // _pressSlot = the item cell a
        // press started on; _dragSlot ≥ 0 once it becomes a drag; _selectedSlot = the click-selected
        // (red-outlined) item; _hoverPortrait = the portrait carrying the red-circle highlight during a drag.
        private int _selectedSlot = -1;
        private int _pressSlot = -1;
        private int _dragSlot = -1;
        private int _hoverPortrait = -1;
        private Vector2 _pressPos;   // stage-local position of the press
        private int _pressClickCount; // UI Toolkit's click count for that press (2 = double)
        private BakAgain.UI.InputCore.DragGestureManipulator _gesture;
        private IVisualElementScheduledItem _pulseTick;

        private void Awake() {
            _logger = LogManager.LoggerFactory.CreateLogger<InventoryMenu>();
            _document = GetComponent<UIDocument>();
            _ui = GetComponent<UserInterfaceLoader>();
            _layerHost = GetComponent<MenuLayerHost>();
        }

        [Inject]
        public void Construct(GameSession gameSession, IResourceProviderService resources,
            BakAgain.UI.Navigation.IScreenNavigator navigator,
            BakAgain.Core.Services.DialogExecutor dialogExecutor, IDialogManager dialogs,
            InputLayerStack inputStack = null,
            BakAgain.World.IGroundBagSpawner groundBags = null,
            RiftMapScreen riftMap = null,
            BakAgain.Audio.MidiPlaybackManager midi = null,
            BakAgain.Core.Services.IGameClock clock = null,
            BakAgain.Core.Services.PartyUpkeepService upkeep = null,
            VContainer.IObjectResolver resolver = null) {
            _gameSession = gameSession ?? throw new ArgumentNullException(nameof(gameSession));
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            _dialogExecutor = dialogExecutor ?? throw new ArgumentNullException(nameof(dialogExecutor));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _upkeep = upkeep;
            _riftMap = riftMap;       // optional: a bare harness has no screens to push
            // Resolved when the Spyglass is used, not here: the locator screen needs WorldRuntime,
            // which reaches this menu through LocationScreen, so a constructor link is a cycle.
            _resolver = resolver;
            _inputStack = inputStack; // quantity-picker modality; optional for bare test harnesses
            // Optional too: without a world on screen a discard still claims and persists its bag,
            // and the next zone build spawns the entity for it.
            _groundBags = groundBags;
            // Optional: only the practice lute asks for music, and a bare harness has none.
            _midi = midi;
            // Optional too: only the mender charges the party time, and only from a location.
            _clock = clock;
        }

        private BakAgain.Audio.MidiPlaybackManager _midi;
        private VContainer.IObjectResolver _resolver;

        private BakAgain.UI.Spells.ILocatorMapView LocatorMap =>
            _resolver?.Resolve(typeof(BakAgain.UI.Spells.ILocatorMapView)) as BakAgain.UI.Spells.ILocatorMapView;

        private BakAgain.Core.Services.IGameClock _clock;
        private BakAgain.Core.Services.PartyUpkeepService _upkeep;

        /// <summary>Set the container to loot, along with the clicked world item's type (which selects
        /// the detail-window container image — chest vs. corpse vs. …). Call before pushing this screen
        /// (the typed pre-push setter pattern — see the navigation design doc §3.2).</summary>
        public void SetContainer(RuntimeContainer container, WorldEntityType entityType) {
            _lockScratch = null; // this screen is a singleton; a previous lock must not leak in
            _blessing = null;    // ...nor a previous temple's offer
            // *** SHOP-NESS IS THE CONTAINER'S, NOT THE SCREEN'S. *** The original has no "shop
            // screen" mode to be in: cmbinv_transfer branches on the CONTAINER carrying a shop
            // subrecord (`actorrec_get_subrecord(.., SUBREC_EVENT_STATE)`, CMBINV.C:787-792) and
            // hands off to shop_npc_transaction there. Its one screen-wide flag,
            // `g_bInventoryShopMode`, is the PICKLOCK screen's — set only by PICKLOCK.C, whatever
            // its name suggests.
            //
            // So this cannot clear the shop the way it clears a lock: location action codes 5/6/8
            // open a shopkeeper's shelf through exactly this call (TOWNSCN.C:519/527/544 — three
            // identical arms), and a shelf opened with no shop attached hands its stock over free.
            _shop = container is { IsShop: true } ? container : null;
            _shopData = _shop?.Shop;
            _shopPrices.Clear();  // a haggled price belongs to the visit, not to the shop
            _shopPage = 0;
            _container = container;
            _displayed = container;
            NoteKeeperKind();
            // A shop's detail window is the "Shop" sign, whatever the world item behind it is —
            // the container-type case the original resolves BEFORE the world-item switch.
            _containerImageIndex = _shop != null
                ? ContainerImage.ShopIndex
                : ContainerImage.IndexForWorldEntityType(entityType);
        }

        /// <summary>Open directly on a party member's own inventory (no loot container) — the travel
        /// HUD's plain portrait click, WORLDLP.C:367 <c>cmbinv_inventory_screen_run(NULL, slot+1, 0)</c>.
        /// The detail window shows the default member-inventory image (INVMISC#11). Call before pushing;
        /// returns false when the portrait has no active member behind it (party_count gate @0x359).</summary>
        public bool SetMember(int portraitSlot) {
            RuntimeContainer inventory = ResolveMemberContainer(portraitSlot);
            if (inventory == null) {
                return false;
            }
            _lockScratch = null; // see SetContainer
            _shop = null;
            // Cleared on OPEN, not on read: a value left from the last fight would be replayed as
            // this one's item the moment the arena asked.
            PendingCombatCommandId = GameData.Resources.Combat.CombatItemUse.NoItemUsed;
            _container = null;
            _displayed = inventory;
            NoteKeeperKind();
            _containerImageIndex = ContainerImage.MemberInventoryIndex;
            return true;
        }

        /// <summary>
        /// Open on a lock — the picklock screen (<c>sub_ovr166_DF</c> @0x5bdf9).
        /// </summary>
        /// <remarks>
        /// <b>Not a new screen: the ordinary inventory screen over a SCRATCH container.</b> The
        /// original copies the party's shared inventory (where keys live) and appends the party's
        /// picklocks as ONE stack holding the whole-party count — see
        /// <see cref="PicklockWorkingSet"/>. What the player drags onto the lock is a working set
        /// assembled for the occasion, so nothing shown here is the container it came from; that is
        /// what makes the write-back on a snapped tool tool-specific.
        ///
        /// <para>Returns false when the working set comes out empty, which is the original's
        /// refusal (ddx 86, "you need keys or picklocks") — the caller shows it and does not push.
        /// A party with keys but no picks still gets the screen and simply cannot pick.</para>
        /// </remarks>
        /// <param name="lockDifficulty">The lock's score, from the container's lock record.</param>
        public bool SetLock(int lockDifficulty) {
            RuntimeContainer shared = _gameSession.SharedKeysInventory;
            // Packs are keyed by CHARACTER; walking roster positions 0..n-1 read Locklear's pack
            // and skipped James's whenever the party was not characters 0-2.
            int picks = 0;
            foreach (RuntimeContainer pack in _gameSession.ActivePartyPacks) {
                picks += InventoryQuery.CountByKind(pack, LockPicking.LockpickObjectId);
            }

            var scratch = new RuntimeContainer {
                ContainerType = SaveGameContainerType.SharedKeys,
                Capacity = (shared?.Items?.Count ?? 0) + 1,
            };
            if (shared?.Items != null) {
                foreach (RuntimeItem item in shared.Items) {
                    scratch.Items.Add(item.Clone());
                }
            }
            if (PicklockWorkingSet.HasPickStack(picks)) {
                scratch.Items.Add(new RuntimeItem(
                    (byte)LockPicking.LockpickObjectId,
                    (byte)PicklockWorkingSet.PickStackQuantity(picks),
                    (ushort)PicklockWorkingSet.PickStackItemFlags));
            }

            if (scratch.Items.Count == 0) {
                return false;
            }

            _container = null;
            _shop = null;        // a lock reached after a shop visit is not a trade — see SetContainer
            _shopData = null;
            _displayed = scratch;
            NoteKeeperKind();
            _lockScratch = scratch;
            _lockDifficulty = lockDifficulty;
            _lockPicker = BestLockPicker().Slot;
            _lockOpened = false;
            _lockClosed = new UniTaskCompletionSource<bool>();
            _containerImageIndex = ContainerImage.MemberInventoryIndex;

            return true;
        }

        /// <summary>
        /// Open on a member's own pack with the temple's blessing offer attached — the original's
        /// <c>g_inventory_screen_mode = 2</c>.
        /// </summary>
        /// <remarks>
        /// <b>It is not a screen of its own.</b> <c>modalscreen_req_inv_run</c> sets a mode and runs
        /// the ORDINARY inventory screen; what changes is that using an item offers to bless it
        /// instead. Building a separate blessing screen would duplicate the whole inventory for one
        /// changed verb.
        ///
        /// <para>The three numbers come from the temple's container and mean nothing on their own —
        /// they are a fee, a percentage of the item's price and the blessing's TIER, and they are
        /// those things only because the action code says so.</para>
        /// </remarks>
        public bool SetBlessing(SaveGameContainerShopData temple, int portraitSlot) {
            if (temple == null || !SetMember(portraitSlot)) {
                return false;
            }
            _blessing = temple;

            return true;
        }

        /// <summary>The temple whose offer is attached; null on every other screen.</summary>
        /// <remarks>Its presence IS the mode, the same way the lock's scratch container is.</remarks>
        private SaveGameContainerShopData _blessing;

        /// <summary>True while the screen is offering blessings.</summary>
        public bool IsBlessingMode => _blessing != null;

        /// <summary>
        /// Open on a member's own pack with a shop's mender attached —
        /// <c>modalscreen_inventory_request</c> (MODALSCR.C:503), which a location hotspot reaches
        /// on action code 16.
        /// </summary>
        /// <remarks>
        /// <b>Not a shop and not a screen of its own.</b> The original sets
        /// <c>g_inventory_screen_mode = 2</c> and runs this very screen on <c>req_inv.dat</c> /
        /// <c>INVENTOR.SCR</c>; the player browses the party's OWN items and the mender mends them.
        /// Nothing is bought, so <see cref="SetShop"/> is the wrong door — it was the one the port
        /// used, and it showed a price list where the original shows the party's worn gear.
        ///
        /// <para>Two bytes of the shop block parameterise the whole thing and nothing else does:
        /// <see cref="SaveGameContainerShopData.RepairCategories"/> (which of swords, armour and
        /// crossbows this mender touches — the same value the shopkeeper's dialog 1800038 branches
        /// on as Var 18) and <see cref="SaveGameContainerShopData.RepairCostMarkup"/> (what he
        /// charges over the base rate).</para>
        /// </remarks>
        public bool SetRepair(SaveGameContainerShopData shop, int portraitSlot) {
            if (shop == null || !SetMember(portraitSlot)) {
                return false;
            }
            _mender = shop;
            _menderTimeMask = 0;

            return true;
        }

        /// <summary>The mender whose services are attached; null on every other screen.</summary>
        /// <remarks>Its presence IS the mode, exactly as the blessing's temple is.</remarks>
        private SaveGameContainerShopData _mender;

        /// <summary>
        /// What the visit has cost so far, as the original's <c>timeFlags</c> — an OR of 4 (a
        /// sword), 8 (armour) and 2 (a crossbow), read out as a number of hours when the screen
        /// closes.
        /// </summary>
        private int _menderTimeMask;

        /// <summary>True while the screen is showing a mender.</summary>
        public bool IsRepairMode => _mender != null;

        /// <summary>The scratch container while a lock is open; null on every other screen.</summary>
        /// <remarks>Its presence IS the mode — there is no separate flag to get out of step with
        /// the container being displayed.</remarks>
        private RuntimeContainer _lockScratch;

        private int _lockDifficulty;

        /// <summary>True while the screen is showing a lock.</summary>
        public bool IsLockMode => _lockScratch != null;

        /// <summary>Test seam: the container whose items the grid is showing.</summary>
        /// <remarks>Read-only and internal, the shape <c>GameSession</c>'s <c>*ForTest</c> seams
        /// use. "Which container is on screen" is otherwise only observable through a full render,
        /// and it is the whole of the lock-mode portrait rule.</remarks>
        internal RuntimeContainer DisplayedContainer => _displayed;

        /// <summary>
        /// Open on a shop's stock — <c>SHOP.C</c>'s buy/sell screen.
        /// </summary>
        /// <remarks>
        /// <b>A shop is not a new screen.</b> The original runs this very inventory screen with the
        /// shopkeeper's container as the displayed one and its mode flag set, which is the same
        /// flag the picklock screen sets — see <see cref="InventoryPanelMode.ShopMode"/>. So the
        /// difference between a loot window and a shop is which container is shown and one bit.
        ///
        /// <para>Returns false for a shop with nothing in it, which the caller should treat as
        /// "there is nothing to trade" rather than opening an empty window.</para>
        /// </remarks>
        public bool SetShop(RuntimeContainer shop, SaveGameContainerShopData shopData = null) {
            if (shop == null || shop.Items.Count == 0) {
                return false;
            }

            // The block is a separate subrecord in the original for the same reason it is a
            // separate argument here: RuntimeContainer is the generic type behind corpses and bags
            // too, so a markup has no business living on it.
            _shopData = shopData;

            _lockScratch = null;   // see SetContainer
            _shopPrices.Clear();    // a haggled price belongs to the visit, not to the shop
            _shopPage = 0;
            _shop = shop;
            _container = shop;
            _displayed = shop;
            NoteKeeperKind();
            _containerImageIndex = ContainerImage.ShopIndex;

            return true;
        }

        /// <summary>
        /// Tell the dialog layer which word the keeper gets — "tavernkeeper" for an establishment
        /// that sells beds, "shopkeeper" otherwise.
        /// </summary>
        /// <remarks>
        /// <b>THE PAGE THAT IS UP DECIDES, so this hangs off the `_displayed` setter.</b> CMBINV.C:63-67
        /// zeroes <c>g_bIsRestEncounter</c> as the first act of building an inventory page and
        /// re-raises it only from the DISPLAYED actor's <c>SUBREC_EVENT_STATE</c> — which a party
        /// member does not have. It used to be written at the five places that OPEN something, which
        /// is the same idea one step less reliable: every new way of switching had to remember, and
        /// <see cref="SelectMember"/> — the portrait click, i.e. every sell — did not.
        ///
        /// <para>Measured at Joftaz's in Silden, 2026-09-13: the original says "tavernkeeper" on a
        /// BUY, dragged off the shelf, and "shopkeeper" on a SELL, dragged out of a member's pack.
        /// Ours said tavernkeeper for both.</para>
        ///
        /// <para>The nightly COST is the discriminator, not the rest hours, and a container with no
        /// shop block at all is a shop. Both are <c>DialogSlotContext.RunsAnInn</c>'s to decide.</para>
        /// </remarks>
        private void NoteKeeperKind() {
            if (_gameSession != null) {
                // The shop's own block when the shop's page is up, and whatever the displayed
                // container carries otherwise — which is nothing for a member's pack or a chest.
                // `_shopData` rather than `_shop.Shop` because SetShop takes the block as its own
                // argument: RuntimeContainer is the generic type behind corpses and bags too.
                SaveGameContainerShopData block =
                    ReferenceEquals(_displayed, _shop) ? _shopData : _displayed?.Shop;
                _gameSession.OpenShopRunsAnInn =
                    GameData.Resources.Dialog.DialogSlotContext.RunsAnInn(block?.InnCostPerNight);
            }
        }

        /// <summary>
        /// What one cell of a shop's shelf says.
        /// </summary>
        /// <remarks>
        /// Price is <c>itemtbl_compute_value</c>: the catalogue price marked up by this shop and
        /// scaled by the zone's exchange rate, then by the item's own condition or charges. The
        /// wording is GOLD AND SILVER, not the sovereigns-and-royals prose the dialogs use.
        ///
        /// <para>A negative value prints "Unavailable" — the shopkeeper-refuses case
        /// (SHOP.C:96-97), which is a real answer rather than a missing price.</para>
        ///
        /// <para><b>A Magical Scroll gets NO price line, and that is a gap rather than a rule.</b>
        /// Its value is the price of the spell it carries, and no spell-price table has been
        /// extracted yet — SPELLDOC.DAT carries display text, not prices. Passing zero would print
        /// "0 gold" for every scroll in the game, which is a wrong number stated as fact; showing
        /// nothing is at least visibly absent.</para>
        /// </remarks>
        private ItemGridRenderer.ShopCellText? ShopCellTextFor(
            RuntimeItem item, GameData.Resources.Object.ObjectInfo info) {
            if (info == null) {
                return null;
            }

            // The condition or count rides INSIDE the name on a shelf cell — the shop's name line
            // replaces the corner label the ordinary grid draws, so without this the figure is not
            // shown at all. See ShopCellLabel for why it is not the grid's flag test.
            (string head, string name) = GameData.Resources.Shop.ShopCellLabel.LinesFor(
                info.Name, info.WordWrap, info.Flags, item.Variable);
            // A bulky item's sprite reaches into the name lines, and the original outlines the
            // text rather than moving it — see ShopCellText.Shadowed.
            bool bulky = GameData.Resources.Shop.ShopCellLabel.NeedsTextOutline(info.InventorySlots);
            long value = PriceOf(item, info);
            string price = value < 0
                ? GameData.Resources.Text.UiStrings.Get(ShopUnavailableKey)
                : GameData.Money.MoneyFormatter.Format((int)value, GameData.Money.CurrencyStyle.GoldAndSilver);

            return new ItemGridRenderer.ShopCellText(name, price, head, bulky);
        }

        /// <summary>
        /// What this shop asks for one item — <c>itemtbl_compute_value</c>.
        /// </summary>
        /// <remarks>
        /// One computation for both the cell and the offer, so a player can never be quoted one
        /// price on the shelf and charged another at the counter.
        /// </remarks>
        private long PriceOf(RuntimeItem item, GameData.Resources.Object.ObjectInfo info) {
            if (info == null) {
                return -1;
            }

            return GameData.Resources.Shop.ShopPricing.ItemValue(
                item, info, (int)ListPriceFor(item.ObjectId, info),
                // A scroll is worth its SPELL, and the spell number is the condition byte.
                _gameSession.ObjectInfo?.SpellPriceFor(item.Variable) ?? 0);
        }

        /// <summary>
        /// This shop's price-table entry for an object TYPE, before the item's own condition.
        /// </summary>
        /// <remarks>
        /// <b>The table is per-visit state, not a formula.</b> <c>calculateObjectPrices</c> @0x5b417
        /// fills 138 entries when a shop opens and haggling then <i>writes into</i> that array — a
        /// won haggle marks the type cheaper for the rest of the visit, and a shopkeeper who takes
        /// offence writes -1 and will not sell it at all. Recomputing the price on each render would
        /// erase both. <see cref="_shopPrices"/> holds only the entries that moved; everything else
        /// answers from the formula, which is what the untouched array would hold.
        /// </remarks>
        private long ListPriceFor(int objectId, GameData.Resources.Object.ObjectInfo info) {
            if (_shopPrices.TryGetValue(objectId, out long haggled)) {
                return haggled;
            }

            SaveGameContainerShopData shop = _shopData;
            int exchange = GameData.Resources.Shop.ShopPricing.ExchangeRate(
                _gameSession.CurrentZone, shop?.ShopType ?? 0,
                inflationFlagSet: _gameSession.GetGlobalValue(GameData.Resources.Shop.ShopPricing.InflationFlag) != 0,
                inflationEndedFlagSet: _gameSession.GetGlobalValue(GameData.Resources.Shop.ShopPricing.InflationEndedFlag) != 0);

            return GameData.Resources.Shop.ShopPricing.ListPrice(
                info.Price, shop?.MarkupPercentage ?? 0, exchange);
        }

        /// <summary>What the table would hold had nobody haggled — the once-per-type gate.</summary>
        /// <remarks>
        /// <b>Without the exchange rate, deliberately.</b> The original builds this comparison from
        /// <c>basePrice × (100+markup)/100</c> while the table it compares against has the exchange
        /// factor applied on top, so at the zone-3 6× shop the two can never be equal and haggling
        /// silently cannot engage there. Folding the rate in here would "fix" a difference the
        /// original has.
        /// </remarks>
        private long UnhaggledListPrice(GameData.Resources.Object.ObjectInfo info) =>
            GameData.Resources.Shop.ShopPricing.ListPrice(
                info.Price, _shopData?.MarkupPercentage ?? 0, exchangeRatePercent: 100);

        /// <summary>Price-table entries this visit has moved, keyed by object id. See
        /// <see cref="ListPriceFor"/>.</summary>
        private readonly System.Collections.Generic.Dictionary<int, long> _shopPrices = new();

        /// <summary>"Unavailable" — what a shop prints instead of a price it will not quote.
        /// Already in the catalog; no new string needed.</summary>
        private const string ShopUnavailableKey = "base:uistring:item.unavailable";

        private SaveGameContainerShopData _shopData;

        /// <summary>The shop being traded with, or null. Presence IS the mode, as with the lock.</summary>
        public bool IsShopMode => _shop != null;

        /// <summary>
        /// Whether the grid currently shows the SHOP's shelf rather than a party member's pack.
        /// </summary>
        /// <remarks>
        /// <b>Prices belong to the container, not to the screen.</b> <c>UI_DrawInventory</c> @0x5674d
        /// derives its <c>isShop</c> from a shop block on the container it is drawing, so switching
        /// to a member's pack inside a shop drops the price lines — the shelf is priced, your own
        /// belongings are not. Gating on <see cref="IsShopMode"/> instead would price the player's
        /// pack with what the shop CHARGES, which is not what it would pay for them either.
        /// </remarks>
        private bool ShowingShopShelf => _shop != null && ReferenceEquals(_displayed, _shop);

        private RuntimeContainer _shop;

        // Pre-activation reset for a (re-)push: fresh gesture/selection state and party heads,
        // and (re)subscribe to the REQ build the activation kicks off.
        protected override UniTask OnBeforeShowAsync() {
            ResetGesture();
            _selectedSlot = -1;
            _droppedBag = null;
            _partyHeads?.Dispose();
            _partyHeads = new PartyHeadsView(_gameSession, _resources);
            _headsAttached = false;
            // Subscribe before enabling so we don't miss the REQ build that OnEnable kicks off; remove
            // first so a re-show without a matching hide can't double-subscribe.
            if (_ui != null) {
                _ui.Built -= OnReqBuilt;
                _ui.Built += OnReqBuilt;
            }
            return UniTask.CompletedTask;
        }

        protected override void OnAfterShow() {
            // Warm the HEADS.BMX ring frames, then place the active member's steady ring — the load
            // has to finish before that ring can be painted, so it chains off the same task.
            PreloadCircleFramesAsync().ContinueWith(UpdateActiveMemberCircle).Forget();
            if (_ui == null || _ui.IsBuilt) {
                RenderCurrent();
            }
        }

        // Load the five HEADS.BMX ring frames (#7 steady + #8..#11 pulse) once per screen open.
        // Released in OnDisable via _hoverCircleOwner. Generation-free: guarded by the null check, and
        // OnDisable nulls _circleSprites so a re-open reloads.
        private async UniTask PreloadCircleFramesAsync() {
            if (_circleSprites != null || _resources == null) {
                return;
            }
            var sprites = new Sprite[CircleFrameCount];
            for (int i = 0; i < CircleFrameCount; i++) {
                sprites[i] = await _resources.LoadAssetAsync<Sprite>(
                    $"{HeadsBmx}#{CircleFirstFrame + i}", _hoverCircleOwner);
            }
            _circleSprites = sprites;
            // INVENTOR.PAL backs the container window's border pens; same owner, released together.
            _inventoryPalette = await _resources
                .LoadAssetAsync<GameData.Resources.Palette.PaletteResource>(
                    "INVENTOR.PAL", _hoverCircleOwner);
        }

        private void OnReqBuilt(IReadOnlyList<NavWidget> _) => RenderCurrent();

        private void OnDisable() {
            if (_ui != null) {
                _ui.Built -= OnReqBuilt;
            }
            TeardownStageInput();
            ResetGesture();
            _selectedSlot = -1;
            // Session end (invui_inspect_image_cleanup): drop the icon/chrome sprite caches and
            // release their owners — the only place these handles are released.
            _renderer.ReleaseAll();
            _chromeSprites.Clear();
            _partyHeads?.Dispose();
            _partyHeads = null;
            _headsAttached = false;
            _containerImage?.RemoveFromHierarchy();
            _containerImage = null;
            _resources?.ReleaseAssets(_containerImageOwner);
            RemoveHoverCircle();
            _activeCircle?.RemoveFromHierarchy();
            _activeCircle = null;
            _discardBorder?.RemoveFromHierarchy();
            _discardBorder = null;
            _circleSprites = null;
            _inventoryPalette = null;
            _resources?.ReleaseAssets(_hoverCircleOwner);
            foreach (VisualElement box in _panelBoxes) {
                box.RemoveFromHierarchy();
            }
            _panelBoxes.Clear();
            _paperdollFill = null;
            foreach (VisualElement ph in _placeholders) {
                ph.RemoveFromHierarchy();
            }
            _placeholders.Clear();
            _resources?.ReleaseAssets(_placeholderOwner);
            _money.Clear();
            _stage = null;
        }

        private void RenderCurrent() {
            if (_document == null || _document.rootVisualElement == null) {
                return;
            }
            PublishDisplayedActor();
            // _ui?.Frame is null (-> CanonicalStage's own fallback) only when this screen has no
            // sibling UserInterfaceLoader at all (defensive — see the `_ui == null` guard above);
            // the normal path reads the real REQ_INV/REQ_INV2 frame.
            _stage = CanonicalStage.GetOrCreate(_document.rootVisualElement, _ui?.Frame);
            // Classic full-screen presentation: black the pillarbox margins so the 4:3 stage sits in
            // letterbox bars. (This once doubled as hiding the live world that bled through the margins
            // in the overlay era; the world camera is now disabled whenever this screen is up — see
            // WorldViewportView — so the black is purely the classic letterbox now.)
            _document.rootVisualElement.style.backgroundColor = Color.black;
            NeutralizeCatchAllHotspot();
            HideInspectOnlyChrome();
            _renderer.Clear();
            // NOT simply "this is not a member's pack": UI_DrawInventory @0x56880 draws the narrow
            // split panel when the container is a member's pack OR the shop/picklock mode flag
            // (byte_dseg_12C4) is set, and the wide loot panel only when NEITHER holds. The rule
            // lives in InventoryPanelMode so both halves are stated once.
            //
            // ShopMode.Off is today's truth: the only writer that sets the flag is the picklock
            // screen (sub_ovr166_DF @0x5be4f), which this port does not have yet — see TASK-161.
            // When it lands it must pass On, or its scratch container (typed SharedKeys, not
            // Inventory) will be drawn as a loot window.
            // Lock mode IS the shop-mode flag the original sets (byte_dseg_12C4 = 1 in
            // sub_ovr166_DF), and that flag is half of UI_DrawInventory's panel test — so the
            // picklock screen gets the NARROW split panel, which is the box the lock is drawn in.
            // Passing Off here would give the scratch container the wide loot background and leave
            // the lock floating in it.
            //
            // A SHOP does not set it. `byte_dseg_12C4` has exactly one writer and the shop screens
            // are not it, so a shelf gets the same wide loot panel any other non-member container
            // does — see InventoryPanelMode.ShopMode, whose name is the original's misnomer.
            bool lootMode = InventoryPanelMode.UsesWideBackground(
                _displayed.ContainerType,
                IsLockMode
                    ? InventoryPanelMode.ShopMode.On
                    : InventoryPanelMode.ShopMode.Off);
            // Sort + merge stacks first, every (re)render — the original runs
            // cmbinv_consolidate_stacks on entry and after every item op, and the resulting order
            // is save-visible (the DOS engine mutates the actor's item array the same way).
            // A shelf sorts by the shop's keys, not the pack's — see InventoryOrder.ShouldSwap.
            if (InventoryOrder.Consolidate(_displayed, _gameSession.ObjectInfo,
                    equippedOrder: !lootMode,
                    shopChapter: ShowingShopShelf ? _gameSession.Chapter : (int?)null)) {
                _displayed.Dirty = true;
            }
            // Paint the black item panel(s) FIRST (over the SCX divider), then the empty-slot
            // placeholders, then the item cells on top (equipped cells carry their own black fill,
            // which is what hides a placeholder under an occupied slot — UI_DrawInventory order).
            DrawPanelBackground(lootMode);
            DrawPaperdollPlaceholders();
            DrawLockAsync().Forget();
            // The party purse, bottom right. Unconditional and mode-independent: the original writes
            // it at the end of every UI_DrawInventory, so it is on the loot screen and the shop as
            // much as on a member's own pack. Redrawn here rather than only on a purse change,
            // because every path that spends or collects money ends in a RenderCurrent anyway.
            _money.Render(_stage, _gameSession.PartyGold, Layout);
            // The grid's geometry comes from the REQ resource (_ui.Inventory/_ui.Frame), not from
            // the renderer — same source as the stage's own frame above. Handed over as `Layout`,
            // the one property that resolves "the screen's inventory geometry": before the async
            // REQ load lands (this method is also called from OnAfterShow) that is the model's
            // faithful defaults, and the Built event re-renders with the real data. Reading
            // _ui?.Inventory again here would be a second, silently divergable answer to the same
            // question.
            _renderer.Render(_stage, _displayed, _gameSession.ObjectInfo, _resources, onSlotPicked: null,
                ShowingShopShelf ? InventoryLayoutMode.Shop
                    : lootMode ? InventoryLayoutMode.Loot : InventoryLayoutMode.Member,
                Layout, _ui?.Frame, _gridNavWidgets, onSlotActivated: ActivateSlot,
                shopCellText: ShowingShopShelf ? ShopCellTextFor : (System.Func<RuntimeItem,
                    GameData.Resources.Object.ObjectInfo, ItemGridRenderer.ShopCellText?>)null,
                firstItem: ShowingShopShelf
                    ? GameData.Resources.Shop.ShopPaging.FirstItem(_shopPage)
                    : 0,
                // Only a shelf is chapter-gated; every other grid shows what it holds.
                shopChapter: ShowingShopShelf ? _gameSession.Chapter : int.MaxValue);
            SyncShopPageButton();
            EnsureStageInput();
            EnsurePartyHeads();
            UpdateActiveMemberCircle(); // whose inventory this is — moves with a portrait switch
            // Clear the selection BEFORE resolving the detail-window image, which is derived from it:
            // done the other way round, a re-render after an equip/unequip kept showing the bag
            // because the image was computed against the selection this line is about to drop.
            // A re-render rebuilds the cell elements, so any prior selection outline is gone anyway.
            _selectedSlot = -1;
            UpdateContainerImage();
            RefreshNavWidgets();
        }

        // REQ_INV's action-128 element is a full-screen (1600×1200) ClickArea. UserInterfaceLoader adds
        // menu entries in list order, so this catch-all — added last — sits ON TOP of every button and
        // intercepts their clicks (Exit/Use/portrait-switch were all dead: panel.Pick returned
        // hotspot_128, never the button). Disabling its picking lets the button clicks beneath it
        // register. Idempotent; runs on every (re)render because a rebuild re-creates the hotspot.
        //
        // This is FAITHFUL, not a workaround — an earlier note here claimed the opposite. The
        // original trims the page's live entry list to the seven chrome hotspots plus one per item
        // (CMBINV.C:60, `wEntry_count = 7` then one append per item), so REQ entry 36 — the
        // action-128 catch-all — is not live in the grid view at all. Its absence is precisely what
        // makes `focused == 0` fire for a press on dead space, which is the deselect rule. The
        // original does make it live in the INSPECT view (`pEntries + 0x23, wEntry_count = 2` —
        // More Info plus the catch-all, INVINSP.C:407-417), which is that view's
        // click-anywhere-to-close target; here the stage recogniser provides that, so the effect is
        // the same.
        private void NeutralizeCatchAllHotspot() {
            VisualElement catchAll = _stage?.Q($"hotspot_{BackgroundCatchAll}");
            if (catchAll != null) {
                catchAll.pickingMode = PickingMode.Ignore;
            }
        }

        /// <summary>
        /// The screen's keyboard/gamepad entry list: chrome first, then one entry per item — the
        /// order cmbinv_combat_encounter_begin builds page->pEntries in (canassa CMBINV.C:60,
        /// wEntry_count = 7 then one append per item).
        ///
        /// <para>The action-128 catch-all is dropped. It is a full-screen 1600x1200 ClickArea, and
        /// the original does not have it live in the grid view at all — the same trim that makes a
        /// dead-space press deselect, which <see cref="NeutralizeCatchAllHotspot"/> already applies
        /// to picking. Without this, Tab lands on an invisible screen-sized hotspot.</para>
        ///
        /// <para>The filter runs over the CHROME list only, and must: item cells carry action ids
        /// from 0x80 up (<c>ItemGridRenderer.ItemActionIdBase</c>), so item slot 0's id equals the
        /// catch-all's. That overlap is the original's own — it can afford to reuse 0x80 for the
        /// first item precisely because the catch-all is not live in this view.</para>
        /// </summary>
        internal static IReadOnlyList<BakAgain.UI.InputCore.NavWidget> ComposeNavWidgets(
            IReadOnlyList<BakAgain.UI.InputCore.NavWidget> chrome,
            IReadOnlyList<BakAgain.UI.InputCore.NavWidget> grid) {
            var composed = new List<BakAgain.UI.InputCore.NavWidget>();
            if (chrome != null) {
                foreach (BakAgain.UI.InputCore.NavWidget w in chrome) {
                    if (w?.Element == null || w.ActionId == BackgroundCatchAll) {
                        continue;
                    }
                    composed.Add(w);
                }
            }
            if (grid != null) {
                composed.AddRange(grid);
            }
            return composed;
        }

        // Hand the freshly-composed list to the layer the MenuLayerHost pushed. Called on every
        // render because the grid's cells are rebuilt each time; the layer swaps its list in place
        // and re-resolves focus from the cursor (NavigableLayer.SetWidgets).
        private void RefreshNavWidgets() =>
            _layerHost?.SetWidgets(ComposeNavWidgets(_ui?.CurrentNavWidgets, _gridNavWidgets));

        // Keyboard/gamepad activation of an item cell. The count comes from the drag recogniser so
        // Enter-twice reaches the same Use branch a mouse double-click does — in the original Enter
        // is a confirm-click: key_is_down(0x1c) feeds the same deadline the left mouse button does
        // (canassa MENUPAGE.C:444-445; line 348 is a comment over an empty stub, not the support).
        // The mouse path does NOT come through here; it goes through the recogniser's
        // press/threshold/release.
        //
        // Refuses in the same states the pointer path does (OnPrimaryPressed/OnSecondaryPressed):
        // while the description is resolving or the inspect view is up, the grid cells are gone or
        // covered, and two Enters landing on a stale focused cell could equip an item the player
        // cannot see.
        private void ActivateSlot(int slot) {
            if (_descriptionOpen || _inspectOpen || _pickerOpen) {
                return;
            }
            VisualElement cell = _stage?.Q($"item_slot_{slot}");
            Vector2 at = cell != null ? _stage.WorldToLocal(cell.worldBound.center) : Vector2.zero;
            SelectItem(slot, _gesture?.CountActivation(at) ?? 1);
        }

        // The "More Info" text button (action 57) belongs to the item-inspect view (right-click an
        // item), not the grid. UserInterfaceLoader builds it visible; the panel boxes used to just
        // COVER it — but the member-mode two-box layout leaves its spot uncovered, so it flashed into
        // view on a loot⇆member switch. Hide it outright in the grid (same visibility:hidden the
        // loader uses for data-hidden elements); the inspect view will un-hide it when that's built.
        private void HideInspectOnlyChrome() {
            _stage?.Q($"button_{ButtonMoreInfo}")?.AddToClassList("req-hidden");
        }

        // Black item-panel fill boxes, faithful to UI_DrawInventory @0x5687d: loot mode paints the
        // single continuous InventoryLayout.FullItemsBox over the background art's divider; member
        // mode paints PaperdollBox + GeneralItemsBox, leaving that divider showing in the gap
        // between them. The boxes sit above the REQ chrome but below the item cells (RenderCurrent
        // adds those afterwards). They no longer double as the "More Info" hider —
        // HideInspectOnlyChrome does that explicitly, since the member-mode layout can't cover
        // action 57's spot.
        private void DrawPanelBackground(bool lootMode) {
            foreach (VisualElement box in _panelBoxes) {
                box.RemoveFromHierarchy();
            }
            _panelBoxes.Clear();
            // Dropped with the boxes, not re-assigned after them: a detached element keeps its last
            // layout rect, so a stale reference would still answer the drop test. The pulse border
            // lives inside the fill, so it goes with it — the next drag frame re-creates it.
            _paperdollFill = null;
            _paperdollBorder = null;
            if (_stage == null) {
                return;
            }
            InventoryLayout layout = Layout;
            // The item-inspect view uses the CONTINUOUS box, not the split member layout: measured
            // off the original, its black area runs unbroken to where the description panel begins
            // — i.e. the divider between the two grid boxes is absorbed.
            if (lootMode || _inspectStatSlot >= 0 || _descriptionOpen) {
                AddPanelBox(layout.FullItemsBox);
            } else if (_displayed?.ContainerType != SaveGameContainerType.Inventory) {
                // The same two-box split, WITHOUT the paperdoll fill — because the fill IS the
                // equip drop target, and there is no body to equip unless this is a member's own
                // pack. UI_DrawInventory gates the paperdoll on containerType == 1 (0x568de), so
                // the lock's scratch container and a shop's shelf are both excluded by the SAME
                // rule rather than as two special cases.
                AddPanelBox(layout.PaperdollBox);
                AddPanelBox(layout.GeneralItemsBox);
            } else {
                // The paperdoll fill IS the equip drop target (invui_handle_item_drag
                // INVENTOR.C:609). Not "both read the one hint" — the ELEMENT is kept, and the drop
                // test reads its resolved geometry, so the target is the visible box in whatever
                // unit it was authored in. See MemberEquip.PaperdollDropZone.
                _paperdollFill = AddPanelBox(layout.PaperdollBox);
                AddPanelBox(layout.GeneralItemsBox);
            }
        }

        // The element DrawPanelBackground painted the member-mode paperdoll fill into — the equip
        // drop target, asked for its own geometry rather than re-derived from the hint. Null
        // whenever no paperdoll is up (loot mode, the inspect view, before the first render), which
        // is exactly when a drop there must not equip anyway.
        private VisualElement _paperdollFill;

        // Empty paperdoll-slot placeholder sprites (invui_grid_render INVENTOR.C:402-406 /
        // UI_DrawInventory @0x56990): member mode blits INVSHP2.BMX#10 (crossbow silhouette) over
        // the crossbow paperdoll cell — skipped for casters, who can't carry a crossbow — and
        // INVSHP2.BMX#11 (armor silhouette) over the armor cell, always. Both positions are derived
        // from the grid (see PlaceholderHint), so they move with it; InventoryLayout's
        // CrossbowPlaceholder/ArmorPlaceholder are optional overrides, null by default. They sit
        // under the item cells; an occupied slot's black fill covers them. There is no sword/staff
        // placeholder in the original.
        // Remove the empty-slot silhouettes. Shared by the re-render path and by the item-inspect
        // view, which must leave nothing of the paperdoll behind it. Handles are NOT released here
        // — the sprites stay session-cached (see _chromeSprites) until OnDisable.
        private void ClearPaperdollPlaceholders() {
            foreach (VisualElement ph in _placeholders) {
                ph.RemoveFromHierarchy();
            }
            _placeholders.Clear();
        }

        // Session cache for the menu's own chrome sprites (INVSHP2 placeholders, INVMISC window
        // images) — the same invui_inspect_images_load_once pattern as ItemGridRenderer's icon
        // cache: first use loads and keeps the handle under the given owner, every later render
        // resolves synchronously; OnDisable releases the owners and clears the cache.
        private readonly Dictionary<string, Sprite> _chromeSprites = new Dictionary<string, Sprite>();

        private async UniTask<Sprite> GetChromeSpriteAsync(string key, object owner) {
            if (_chromeSprites.TryGetValue(key, out Sprite cached)) {
                return cached;
            }
            Sprite sprite = await _resources.LoadAssetAsync<Sprite>(key, owner);
            if (sprite != null) {
                _chromeSprites[key] = sprite;
            }
            return sprite;
        }

        /// <summary>
        /// Draws the equip silhouettes — <b>only when the screen is showing a member's own pack</b>.
        /// </summary>
        /// <remarks>
        /// <c>UI_DrawInventory</c> gates this whole block on <c>containerType == 1</c> (0x568de),
        /// so a loot window, a shop and the picklock's scratch container all get no paperdoll —
        /// not because each is a special case, but because none of them is a member's pack. This
        /// used to be a growing list of modes to suppress, which is the same answer written the
        /// long way round and one that needed extending for every new mode.
        /// </remarks>
        private void DrawPaperdollPlaceholders() {
            ClearPaperdollPlaceholders();
            if (_stage == null || _displayed?.ContainerType != SaveGameContainerType.Inventory) {
                return;
            }
            if (!IsDisplayedMemberCaster()) {
                AddPlaceholder("INVSHP2.BMX#10",
                    PlaceholderHint(Layout.CrossbowPlaceholder, GameData.ObjectType.Crossbow));
            }
            AddPlaceholder("INVSHP2.BMX#11",
                PlaceholderHint(Layout.ArmorPlaceholder, GameData.ObjectType.Armor));
        }

        // Where a silhouette goes: the layout's explicit override when it states one, otherwise
        // derived from the same grid the equipped-item cells are placed against, so the silhouette
        // follows a resized grid instead of staying behind at a coordinate nothing else uses. The
        // slot's row comes from ItemGridRenderer.TryPaperdollSlot — its single owner — so this
        // cannot disagree with where the occupied cell would have been drawn.
        private LayoutHint PlaceholderHint(LayoutHint over, GameData.ObjectType type) {
            if (over != null) {
                return over; // an author who pins a point owns keeping it with its cell
            }
            return ItemGridRenderer.TryPaperdollSlot(type, out int row, out int _)
                ? ItemGridRenderer.PaperdollPlaceholderHint(Layout, row)
                : null;
        }

        private void AddPlaceholder(string key, LayoutHint at) {
            if (at == null) {
                return; // nothing to place: no override, and the grid could not be derived from
            }
            var el = new VisualElement {
                name = "paperdoll_placeholder",
                pickingMode = PickingMode.Ignore,
            };
            BakAgain.UI.Layout.LayoutApplier.Apply(el, at);
            _stage.Add(el);
            _placeholders.Add(el);
            LoadPlaceholderAsync(el, key).Forget();
        }

        private async UniTask LoadPlaceholderAsync(VisualElement target, string key) {
            Sprite sprite = await GetChromeSpriteAsync(key, _placeholderOwner);
            if (sprite == null || target.panel == null) {
                return; // load failed or re-rendered away in the meantime
            }
            // The element was created position-only; without an explicit size it lays out 0×0 and
            // the background never shows. Size it to the sprite (canonical px).
            target.style.width = sprite.rect.width;
            target.style.height = sprite.rect.height;
            target.SetBackgroundSpriteNativeSizeTopLeft(sprite);
        }

        // gstate_actor_is_caster (GSTATE.C:416): a caster is an actor with a nonzero Casting skill
        // maximum. Resolved from a member container's owner; non-member containers are never asked.
        private bool IsDisplayedMemberCaster() => IsMemberCaster(_displayed);

        /// <summary>
        /// Position of the displayed member in the party record set — the original's
        /// <c>charSlot - 1</c>, which is what indexes the per-character stat and status arrays.
        /// </summary>
        /// <remarks>
        /// Looked up by <c>ActorNumber</c> rather than assumed equal to it: <c>OwnerActorNumber</c>
        /// is an actor number, while <see cref="GameSession.StatsOf"/> indexes by position, and
        /// nothing guarantees those coincide. Returns -1 for a container that is not a member's.
        /// </remarks>
        private int DisplayedMemberIndex() {
            int owner = _displayed?.OwnerActorNumber ?? -1;
            SaveGameActorData[] actors = _gameSession.PartyActors;
            for (var i = 0; i < actors.Length; i++) {
                if (actors[i].ActorNumber == owner) {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Publish whose pack is on screen as the dialog's <c>@</c> actor.
        /// </summary>
        /// <remarks>
        /// <b>The inventory screen writes <c>nEvtArgActor0</c> on every redraw</b> — CMBINV.C:264,
        /// and both of MODALSCR's modes do the same (:408 for the priest, :540 for the mender). So
        /// any dialog these screens raise says the displayed member's name where its text carries a
        /// bare <c>@</c>, and a page turn to another member changes who is named.
        ///
        /// <para><b>A container view picks a RANDOM active member</b> (<c>activeParty[RND(partySize)]</c>)
        /// rather than leaving the register alone. That is not a mistake to tidy: a loot window has
        /// no owner to name, and the original still wants a party member in the slot.</para>
        ///
        /// <para>Measured at Highcastle on 2026-09-13: the mender's "@ rummaged around for the
        /// needed sovereigns" said <b>Locklear</b> — whose pack was open — while the port said
        /// Gorath, the byte the save happened to be written with. TASK-493.</para>
        /// </remarks>
        private void PublishDisplayedActor() {
            int member = DisplayedMemberIndex();
            if (member >= 0) {
                _gameSession.EventActor = member;

                return;
            }
            byte[] roster = _gameSession.ActivePartyIndices;
            if (roster != null && roster.Length > 0) {
                _gameSession.EventActor = roster[UnityEngine.Random.Range(0, roster.Length)];
            }
        }

        /// <summary>
        /// Character context for the use branches that act on the reader rather than the item —
        /// today that is the book stat gain. Null when the displayed container is not a party
        /// member's, which is the right answer: a chest cannot read a book, and the dispatch then
        /// reports the category unported rather than claiming nothing happened.
        /// </summary>
        private ItemUseContext BuildUseContext() {
            int index = DisplayedMemberIndex();
            if (index < 0) {
                return null;
            }
            GameData.Resources.Character.ActorStat[] stats = _gameSession.StatsOf(index);
            if (stats == null) {
                return null;
            }
            // *** THE MODIFIER SLOTS ARE A COPY, AND ApplyUseResult PUTS THEM BACK. *** The block
            // is stored flat, so a per-character view has to be copied out and committed; both
            // Use() call sites funnel through ApplyUseResult, which is why the commit lives there
            // and not beside each of them. Without it a potion would fill a slot nobody keeps —
            // the same silent nothing the category had before it was wired at all.
            _pendingUseCharacter = index;
            _pendingUseSlots = _gameSession.StatModifierSlotsFor(index);
            return new ItemUseContext(
                stats,
                index + 1,
                key => _gameSession.GetGlobalValue(key) ?? 0,
                (key, value) => _gameSession.SetGlobalValue(key, value),
                n => UnityEngine.Random.Range(0, n),
                _gameSession.ConditionsOf(index),
                _gameSession.KnownSpellsOf(index),
                _pendingUseSlots,
                (uint)_gameSession.GameTimeIn2Seconds,
                // The carried light's timer: key 0 of the Light kind (TASK-518).
                itemLightBurning: _clock == null ? (System.Func<bool>)null
                    : () => _clock.HasTimer(GameData.Resources.Dialog.Actions.TimerType.Light,
                        GameData.Resources.Inventory.ItemLight.TimerKey),
                lightItem: _clock == null ? (System.Action<long>)null
                    : ticks => _clock.ScheduleTimer(GameData.Resources.Dialog.Actions.TimerType.Light,
                        GameData.Resources.Inventory.ItemLight.TimerKey, ticks, replaceExisting: true),
                extinguishItemLight: _clock == null ? (System.Action)null
                    : () => _clock.ExpireTimers(GameData.Resources.Dialog.Actions.TimerType.Light,
                        GameData.Resources.Inventory.ItemLight.TimerKey)) {
                Chapter = _gameSession.Chapter,
                Zone = _gameSession.CurrentZone,
                InCombat = InCombat,
                // The Cup of Rlnn Skr reaches Owyn and Pug by character, in the party or not.
                IsPartyMember = character =>
                    System.Array.IndexOf(_gameSession.ActivePartyIndices ?? System.Array.Empty<byte>(),
                        (byte)character) >= 0,
                SpellsOfCharacter = character => _gameSession.KnownSpellsOf(character),
                ClearCombatPoison = ClearActingCombatantPoison,
                // g_game_mode: the zone's kind, Z##DEF's first word (2 = underground).
                ZoneKind = (int)((_resolver?.Resolve(typeof(BakAgain.World.WorldRuntime)) as BakAgain.World.WorldRuntime)
                    ?.ZoneDefinition?.ZoneLocation ?? 0),
                // g_dialog_in_scene: a location (GDS) screen is up behind this inventory.
                InLocationScene = UnityEngine.Object.FindAnyObjectByType<BakAgain.World.Scenes.LocationScreen>()
                    is BakAgain.World.Scenes.LocationScreen location && location.gameObject.activeInHierarchy,
            };
        }

        // Set by BuildUseContext, consumed by ApplyUseResult — see the note there.
        private int _pendingUseCharacter = -1;
        private GameData.Resources.Character.ActorStatModifiers.Slot[] _pendingUseSlots;

        private bool IsMemberCaster(RuntimeContainer container) {
            int owner = container?.OwnerActorNumber ?? -1;
            foreach (SaveGameActorData actor in _gameSession.PartyActors) {
                if (actor.ActorNumber == owner) {
                    return actor.AccuracyCasting.Maximum != 0;
                }
            }
            return false;
        }

        // Returns the element it painted (null when the hint is absent), so a caller that needs the
        // box's resolved geometry later — the equip drop test — can ask the box itself.
        /// <summary>Name of the lock layer, so a re-render replaces it rather than stacking.</summary>
        private const string LockLayerName = "BakPicklockLock";

        /// <summary>
        /// Draws the lock the player drags a tool onto — <c>UI_DrawLock</c> @0x5bca0.
        /// </summary>
        /// <remarks>
        /// <b>The body's image is the lock's DIFFICULTY TIER</b>, so a harder lock is drawn as a
        /// heavier one — that is what <see cref="LockPicking.DifficultyTier"/> is for. The latch
        /// (image 0) is drawn UNDER it, and is the piece that moves — see
        /// <see cref="OpenLatchAsync"/>.
        ///
        /// <para>It sits in the NARROW panel box, which is the same box the shop/picklock mode
        /// makes <c>UI_DrawInventory</c> draw — the two are byte-identical at VGA (13,11,82,121),
        /// and that is why lock mode has to select that background rather than the loot one.</para>
        /// </remarks>
        private async UniTaskVoid DrawLockAsync() {
            _stage?.Q<VisualElement>(LockLayerName)?.RemoveFromHierarchy();
            if (!IsLockMode || _stage == null) {
                return;
            }

            var layer = new VisualElement {
                name = LockLayerName,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };
            _stage.Add(layer);

            // Latch FIRST, at a fixed spot; the body is drawn OVER it, so the two read as a padlock
            // hanging from a hasp. Reversing them puts the latch in front, which looks like a
            // separate object lying on the lock.
            await AddLockPieceAsync(layer, PicklockWorkingSet.LatchImageIndex,
                PicklockWorkingSet.LatchVgaX * BakAgain.Graphics.Canonical.VgaScaleX,
                PicklockWorkingSet.LatchVgaY * BakAgain.Graphics.Canonical.VgaScaleY);

            // The body is CENTRED in the panel, not placed at its origin: the four difficulty
            // images have different widths, so a fixed x would step the lock sideways as the
            // difficulty changed. Centred in canonical space directly rather than round-tripping
            // the sprite width back through VGA.
            Sprite body = await _resources.LoadAssetAsync<Sprite>(
                PicklockWorkingSet.LockIconSet + "#"
                + PicklockWorkingSet.LockImageIndexFor(_lockDifficulty), this);
            if (body != null) {
                const int scaleX = BakAgain.Graphics.Canonical.VgaScaleX;
                int panelLeft = PicklockWorkingSet.PanelVgaX * scaleX;
                int panelWidth = PicklockWorkingSet.PanelVgaWidth * scaleX;
                AddLockSprite(layer, body,
                    panelLeft + Mathf.RoundToInt((panelWidth - body.rect.width) / 2f),
                    PicklockWorkingSet.BodyVgaY * BakAgain.Graphics.Canonical.VgaScaleY);
            }
        }

        // Both coordinates are CANONICAL here, not VGA — the caller scales, because the body's x
        // is a centring computed against the canonical panel width and cannot be expressed in VGA
        // without rounding twice.
        private async UniTask AddLockPieceAsync(VisualElement layer, int imageIndex,
            int canonicalX, int canonicalY) {
            Sprite sprite = await _resources.LoadAssetAsync<Sprite>(
                PicklockWorkingSet.LockIconSet + "#" + imageIndex, this);
            if (sprite != null) {
                AddLockSprite(layer, sprite, canonicalX, canonicalY);
            }
        }

        private static void AddLockSprite(VisualElement layer, Sprite sprite, int x, int y) {
            if (layer.panel == null) {
                return; // the screen went away while the sprite was loading
            }

            layer.Add(new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = x,
                    top = y,
                    width = sprite.rect.width,
                    height = sprite.rect.height,
                    backgroundImage = new StyleBackground(sprite),
                },
            });
        }

        private VisualElement AddPanelBox(LayoutHint hint) {
            if (hint == null) {
                return null; // an override may null a box out — then that part simply isn't blacked
            }
            var box = new VisualElement {
                name = "panel_bg",
                pickingMode = PickingMode.Position, // opaque black fill over the SCX divider
                style = { backgroundColor = Color.black },
            };
            BakAgain.UI.Layout.LayoutApplier.Apply(box, hint);
            _stage.Add(box);
            _panelBoxes.Add(box);
            return box;
        }

        // Draw the detail-window image (action 32, canonical 975,888,320×186) — the INVMISC.BMX
        // sprite chosen in SetContainer/SetMember. When looting, it's the container's world-item type
        // (chest, corpse, grave, … — sub_ovr158_59E @0x565ee) and is keyed off _container, NOT
        // _displayed, so it stays visible while the player views a member's inventory (the container
        // is still the loot source; clicking it switches back). When opened member-first from travel
        // there is no loot container and the window shows the default member-inventory image
        // (INVMISC#11 — the containerType special case before the world-item switch).
        /// <summary>
        /// Whether the detail window has an image at all. <c>invui_grid_render</c> (INVENTOR.C:386)
        /// blits one whenever <c>invui_actor_inventory_kind</c> answered <c>&gt;= 0</c>, which it
        /// does for every case this port has: a loot container (its world-item image), a member's
        /// own pack (keys/bag), and the party's shared keys inventory (always the keyring). The
        /// -1 case is a screen mode the remake doesn't have yet.
        /// </summary>
        private bool HasContainerImage =>
            _container != null
            || _displayed?.ContainerType == SaveGameContainerType.Inventory
            || _displayed?.ContainerType == SaveGameContainerType.SharedKeys;

        private void UpdateContainerImage() {
            _containerImage?.RemoveFromHierarchy();
            _containerImage = null; // handles stay session-cached (_chromeSprites) until OnDisable
            if (_stage == null || !HasContainerImage) {
                return;
            }
            VisualElement host = _stage.Q($"hotspot_{WindowContainerImage}");
            if (host == null) {
                return;
            }
            _containerImage = new VisualElement {
                name = "container_image",
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };
            host.Add(_containerImage);
            LoadContainerImageAsync(_containerImage, EffectiveContainerImageIndex).Forget();
        }

        /// <summary>
        /// Which INVMISC image the detail window shows. The shared keys inventory always shows the
        /// keyring; a loot container keeps the image its world
        /// item chose; a party member's own inventory swaps on the original's <c>highlight_slot</c>,
        /// which has THREE states — <c>invui_grid_render</c> (INVENTOR.C:339) computes the icon flag
        /// as <c>highlight_slot != -1</c> and <c>invui_actor_inventory_kind</c> (@0x565EE) returns the
        /// keys (0xB) only for <c>flag == 0</c>:
        /// <list type="bullet">
        /// <item><c>-1</c> — nothing highlighted → keys;</item>
        /// <item><c>&gt;= 0</c> — a selected slot → bag;</item>
        /// <item><c>-2</c> — a drag in progress → bag. The original passes -2 explicitly when the
        /// drag starts (INVENTOR.C:668) and at both drop re-renders (753, 780).</item>
        /// </list>
        /// Inspecting an item also counts, which is why the bag shows in the original's inspect
        /// screens.
        /// </summary>
        private int EffectiveContainerImageIndex {
            get {
                // invui_actor_inventory_kind's FIRST branch (INVENTOR.C:269): the shared keys
                // inventory shows the keyring whatever is selected — the three-state rule below
                // is only about a member's own pack.
                if (_displayed?.ContainerType == SaveGameContainerType.SharedKeys) {
                    return ContainerImage.MemberInventoryIndex;
                }
                if (_container != null) {
                    return _containerImageIndex; // loot: keyed off the clicked world item
                }
                return ShowsBagIcon(_selectedSlot, _dragSlot, _inspectOpen)
                    ? ContainerImage.MemberInventorySelectedIndex
                    : ContainerImage.MemberInventoryIndex;
            }
        }

        /// <summary>
        /// The original's icon flag, <c>highlight_slot != -1</c> (INVENTOR.C:339): true — the bag —
        /// for a selected slot, for a drag in progress (the engine's <c>-2</c>), or while inspecting;
        /// false — the keys — only when nothing at all is highlighted.
        /// </summary>
        internal static bool ShowsBagIcon(int selectedSlot, int dragSlot, bool inspectOpen) =>
            selectedSlot >= 0 || dragSlot >= 0 || inspectOpen;

        // Re-resolve just the detail-window image after the selection state changes, without
        // rebuilding the whole screen. Keys/bag flips hit the session cache, so the swap is
        // synchronous after each sprite's first load.
        private void RefreshContainerImage() {
            if (_containerImage == null || _resources == null) {
                return;
            }
            LoadContainerImageAsync(_containerImage, EffectiveContainerImageIndex).Forget();
        }

        private async UniTask LoadContainerImageAsync(VisualElement target, int index) {
            Sprite sprite = await GetChromeSpriteAsync($"INVMISC.BMX#{index}", _containerImageOwner);
            if (_containerImage != target || sprite == null) {
                return; // superseded (re-render/close) or load failed
            }
            target.style.backgroundImage = Background.FromSprite(sprite);
            // Native size, centered — the original blits the INVMISC sprite 1:1 into the detail
            // window (Contain scaled small sprites UP, which is how the keyring ballooned).
            target.style.backgroundSize =
                new BackgroundSize(new Length(sprite.rect.width), new Length(sprite.rect.height));
            target.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            target.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
        }

        // Draw the 3 party-member faces into the REQ portrait click areas (hotspot_2/3/4), once —
        // the heads live under the hotspots (not the grid cells), so they survive grid re-renders.
        private void EnsurePartyHeads() {
            if (_partyHeads == null || _stage == null || _headsAttached) {
                return;
            }
            _partyHeads.Attach(_stage);
            _partyHeads.RenderAsync().Forget();
            _headsAttached = true;
        }

        // --- pointer input (event-driven) ---
        // The gesture runs on UI Toolkit pointer events via DragGestureManipulator, which captures
        // the pointer on press so move/up keep arriving after it leaves the item cell
        // (PointerCaptureContractTests pins that). A press over an item cell arms the gesture;
        // movement past the threshold becomes a drag whose ghost follows the cursor; release
        // resolves it (portrait = transfer, container window = put back, paperdoll = equip, a click
        // with no drag = select). Button/portrait clicks are still handled by UserInterfaceLoader,
        // which the recogniser does not interfere with — it never stops propagation.
        //
        // This screen used to poll IPointer in Update(), justified by "this project has no
        // InputSystemUIInputModule". That was measured false (task-48): the module is there with
        // Point/Click enabled. Continuous input — movement, look, cursor position — stays polled,
        // which is what Unity's own Input System docs recommend for values with a continuous effect;
        // discrete gestures like this one belong on events.

        // Attach the gesture recogniser and the pulse ticker to the stage. Both are idempotent: the
        // stage element survives grid rebuilds, so this runs on every render but wires only once.
        // The threshold is re-read every time, because the first render happens before the async
        // REQ load lands — so the recogniser may have been created against the model's default
        // rather than the screen's own value. Assigning it (rather than rebuilding the recogniser)
        // is what keeps a mid-gesture pointer capture intact.
        private void EnsureStageInput() {
            if (_stage == null) {
                return;
            }
            float threshold = DragThreshold(Layout.DragThreshold);
            if (_gesture != null) {
                _gesture.DragThreshold = threshold;
                return;
            }
            _gesture = new DragGestureManipulator(_stage, threshold) {
                // *** THE ORIGINAL'S RULE IS A DIAMOND, NOT A CIRCLE. ***
                // invui_handle_item_drag promotes on abs(dx) + abs(dy) > 4 in VGA pixels, and the
                // two canonical axes scale differently (x5 across, x6 down) — so 4 VGA px is 20
                // canonical across and 24 down, and no single radius is right on both. The scalar
                // above stays for the double-click position tolerance, which is a different
                // question.
                StartsDrag = moved => GameData.Resources.Inventory.InventoryDragGesture.StartsDrag(
                    moved.x / Canonical.VgaScaleX, moved.y / Canonical.VgaScaleY),
            };
            _gesture.Pressed += OnPrimaryPressed;
            _gesture.SecondaryPressed += OnSecondaryPressed;
            _gesture.DragStarted += OnDragStarted;
            _gesture.Dragged += OnDragged;
            _gesture.Released += OnReleased;
            _stage.AddManipulator(_gesture);
            // The pulse is an animation, not input, so it rides UI Toolkit's scheduler — the same
            // mechanism ItemInspectPanel's icon flight uses — instead of needing an Update() pump.
            // Only reached on the first wire (the branch above returns otherwise); the null check
            // is belt-and-braces so a restart can never stack two tickers on one stage.
            if (_pulseTick == null) {
                _pulseTick = _stage.schedule
                    .Execute(AdvancePulse)
                    .Every((long)(HoverCircleFrameSeconds * 1000f));
            }
        }

        // The faithful threshold, used when the data's is degenerate. Read off the model rather than
        // written here so there is still exactly one place the number lives.
        private static readonly float FaithfulDragThreshold = new InventoryLayout().DragThreshold;

        /// <summary>
        /// The drag threshold to recognise gestures with. Zero or negative is not a valid
        /// threshold: <see cref="DragGestureManipulator"/> compares <c>magnitude &lt;= threshold</c>,
        /// so at zero the very first pointer move promotes every press to a drag and an item can
        /// never be selected — and the same number is the double-click position tolerance, so
        /// double-click dies with it. The analogous guard in
        /// <c>ItemInspectPanel.StepSize</c> degrades to the finest usable value; here that would be
        /// a 1px threshold, which is barely less broken, so this degrades to the original's own
        /// threshold instead.
        /// </summary>
        private static float DragThreshold(float declared) =>
            declared > 0.0001f ? declared : FaithfulDragThreshold;

        private void TeardownStageInput() {
            if (_gesture != null) {
                _stage?.RemoveManipulator(_gesture);
                _gesture = null;
            }
            _pulseTick?.Pause();
            _pulseTick = null;
        }

        // Advance the red-circle / container-border pulse. Mirrors the original's frame timer
        // (autoDecreasingTimer): step the 6-entry pulse (8→9→10→11→10→9) at HoverCircleFrameSeconds.
        private void AdvancePulse() {
            if (_hoverCircle == null && _discardBorder == null && _paperdollBorder == null) {
                return; // nothing pulsing
            }
            _pulsePhase = (_pulsePhase + 1) % PulsePhaseCount;
            ApplyHoverCircleSprite();
            ApplyDiscardBorderPen();
            ApplyPaperdollBorderPen();
        }

        /// <summary>
        /// Primary press. While the inspect view is up a press dismisses it — except on the More
        /// Info button, which gets the click instead. Otherwise the press either arms an item
        /// gesture or, on dead space, clears the selection.
        /// </summary>
        internal void OnPrimaryPressed(Vector2 stageLocal, int clickCount) {
            if (_stage == null || _pickerOpen) {
                return;
            }
            if (_inspectOpen) {
                if (_inspectStatSlot >= 0 && IsOverMoreInfo(stageLocal)) {
                    ShowItemStats(); // description gives way to the stat block
                } else {
                    CloseInspect();
                }
                return;
            }
            _pressSlot = SlotAt(stageLocal); // -1 if not on an item (buttons handle themselves)
            _pressPos = stageLocal;
            _pressClickCount = clickCount;
            _dragSlot = -1;
            // Pressing dead space clears the selection. The original's drag handler opens with
            // `if (focused == 0 && click) { selSlot = -1; return 1; }` (0x570B1..0x570CD) —
            // "focused" being any LIVE menu entry, and the inventory page trims its entry list to the
            // seven chrome hotspots plus one per item (CMBINV.C:60, wEntry_count = 7 + itemCount). So
            // anything that is neither an item nor a hotspot — the black panel, the empty paperdoll,
            // the frame — deselects. The hotspots that deselect do it themselves in PrimaryAction,
            // because two of them (gold, More Info) don't.
            if (_pressSlot < 0 && IsLockMode && IsOverLock(stageLocal)) {
                // The lock is a live hotspot, not dead space: clicking it looks it over. In the
                // original that is a menu entry with action 127, but no shipped REQ carries one —
                // the picklock screen builds it, and here the lock is a stage element, so the
                // press path is where the same gesture lands.
                ExamineLockAsync().Forget();

                return;
            }
            if (_pressSlot < 0 && !OverChromeHotspot(stageLocal)) {
                DeselectItem();
            }
        }

        /// <summary>
        /// Right-click, faithful to <c>sub_ovr157_4E3</c> @0x549A2: <c>menu_getButtonClicked() ==
        /// button_Secondary</c> on an item slot (action >= 128, canassa CMBINV.C:305) opens the
        /// item-inspect view via <c>UI_showItem</c> @0x5A778.
        /// </summary>
        internal void OnSecondaryPressed(Vector2 stageLocal) {
            if (_descriptionOpen || _inspectOpen || _stage == null || _pickerOpen) {
                return;
            }
            // Never interrupt an in-flight drag (the right-drag gate, canassa CMBINV.C:303) — the
            // original polls the two buttons independently, but a right-click mid-drag would strand
            // the ghost and the dimmed origin cell.
            if (_gesture != null && _gesture.IsPressed) {
                return;
            }
            int slot = SlotAt(stageLocal);
            if (slot >= 0) {
                ShowItemDescriptionAsync(slot).Forget();
            }
        }

        private void OnDragStarted(Vector2 stageLocal) {
            if (_pressSlot >= 0) {
                StartDrag(_pressSlot);
            }
        }

        private void OnDragged(Vector2 stageLocal) {
            if (_dragSlot < 0) {
                return;
            }
            PositionGhost(stageLocal);
            SetHoverPortrait(PortraitUnder(stageLocal));
            UpdateDiscardBorder(dragging: true, overWindow: IsOverContainerWindow(stageLocal));
            UpdatePaperdollBorder(dragging: true, _dragSlot, stageLocal);
        }

        internal void OnReleased(Vector2 stageLocal, bool wasDrag) {
            if (_pressSlot < 0 || _pickerOpen) {
                return; // the press didn't start on an item (or the picker owns input)
            }
            int startSlot = _pressSlot;
            _pressSlot = -1;
            if (!wasDrag || _dragSlot < 0) {
                SelectItem(startSlot, _pressClickCount); // click selects; double-click uses
                return;
            }
            int dragged = _dragSlot;
            _dragSlot = -1;
            int portrait = PortraitUnder(stageLocal);
            _dropStagePos = stageLocal; // where the sprite starts its flight, if the drop transfers
            bool overWindow = IsOverContainerWindow(stageLocal);
            SetHoverPortrait(-1);
            UpdateDiscardBorder(dragging: false, overWindow: false);
            UpdatePaperdollBorder(dragging: false, dragged, stageLocal);
            DestroyGhost();
            // Drop resolution order is the original's chain of `if (dragging && …)` blocks: party
            // portrait (INVENTOR.C:729), then the container window, then the paperdoll. Each returns,
            // so the first match wins.
            if (DropOnLock(dragged, stageLocal)) {
                // handled: tried the tool on the lock. FIRST in the chain because in lock mode the
                // lock is the only meaningful target — the portraits and the container window
                // belong to screens that are not showing one.
            } else if (portrait >= 0) {
                TransferItemTo(dragged, portrait);
            } else if (overWindow && DropOnContainerWindow(dragged)) {
                // handled: put back into the loot container
            } else if (EquipOnPaperdollDrop(dragged, stageLocal)) {
                // handled: equipped onto the paperdoll (there is no unequip gesture — spec §2.4)
            } else if (UseOnItemDrop(dragged, stageLocal)) {
                // handled: used on the item underneath (poison a blade, restring a crossbow, …)
            } else {
                RenderCurrent(); // snap back — restores the dimmed origin cell, no data change
            }
        }

        /// <summary>
        /// Show an item's description, the prose half of the inspect view (<c>UI_showItem</c> @0x5A778
        /// step 5). The original seeds <c>global_30000</c> and plays a DDX whose Var-0 branches select
        /// the text: dialog 1800001 keyed by object id for everything, except spell scrolls, which use
        /// 1800033 keyed by the scroll's spell.
        ///
        /// <para>Resolution deliberately goes through <see cref="DialogExecutor.ResolveLeafEntryAsync"/>
        /// rather than <c>ShowById</c>: the root entry of 1800001 is text-less (134 branches), so
        /// <c>ShowById</c> would render nothing. The executor walks the Var-0 branch to the leaf that
        /// actually carries the prose.</para>
        ///
        /// <para>Not yet the full inspect layout — the icon fly-in and the positioned name/type/status
        /// lines (see <see cref="ItemInspectText"/>) still need their own panel; task-11.</para>
        /// </summary>
        private async UniTaskVoid ShowItemDescriptionAsync(int slot) {
            ObjectInfo obj = ObjectAt(slot);
            if (obj == null || _displayed == null || slot >= _displayed.Items.Count) {
                return;
            }
            RuntimeItem item = _displayed.Items[slot];
            (int dialogId, int globalValue) = ItemInspectText.DescriptionLookup(item, obj);
            _descriptionOpen = true;
            try {
                _gameSession.SetGlobalValue(ItemInspectText.DescriptionGlobalKey, globalValue);
                // ResolvePlayAsync, not ResolveLeafEntryAsync: 1800001's root is text-less with 134
                // branches, and entries on the way to the leaf can fill text variables the leaf then
                // reads. The play carries those writes; a bare entry would lose them.
                GameData.Resources.Dialog.DialogPlay play =
                    await _dialogExecutor.ResolvePlayAsync(dialogId);
                GameData.Resources.Dialog.DialogEntry entry = play?.Entry;
                if (entry == null || string.IsNullOrEmpty(entry.Text)) {
                    // Faithful: objects with no description branch simply show nothing.
                    _logger.LogInformation("No description for object {Object} (dialog {Dialog}).",
                        item.ObjectId, dialogId);
                    return;
                }
                // The original does not call UI_DrawInventory in this state — the grid is replaced by
                // the inspect layout for as long as the description is up. "affecting" is set for a
                // member's own inventory and clear for loot (the original's `di`, which decides
                // whether the status line may say "Using").
                // Clear the grid AND the empty-slot silhouettes: the original doesn't call
                // UI_DrawInventory in this state, so nothing of the paperdoll survives behind the
                // inspected item. Clearing only the cells left INVSHP2 #10/#11 showing through.
                // Where the icon flies FROM — read before the grid is torn down.
                Vector2 from = SlotCentreCanonical(slot);
                _inspectSlot = slot;
                _inspectFrom = from; // held for the fly-back: the cell is gone by then
                _renderer.Clear();
                ClearPaperdollPlaceholders();
                // The purse READOUT stays. The original never erases it here: the inspect view
                // clears only `draw_rect_filled(0xd, 0xb, 0x126, 0x79)` (INVINSP.C:27/187/209) —
                // VGA y 11..132 — while the number is drawn at (0x103, 0xb7), y 183, in the bottom
                // panel. An immediate-mode framebuffer keeps what it does not redraw; a retained UI
                // keeps what it does not REMOVE, so "the grid render is what draws it" is not a
                // reason to take it away. Measured at Malac's Cross: the original shows "99s 9r"
                // behind the Restoratives description, the port showed an empty box.
                // Repaint the fill as ONE continuous box (the _descriptionOpen branch), so the
                // divider between the two grid boxes vanishes for the inspect view. This is also the
                // rect the original clears on every frame of the fly animation.
                DrawPanelBackground(_container != null);
                bool affecting = _container == null;
                _inspect.Render(_stage, item, obj, affecting, _resources, Layout, from);
                // The original flies the icon across BEFORE showing the description (@0x5A7D8, then
                // the ddx at @0x5A9BC), so this is awaited rather than left running underneath.
                await _inspect.WaitForIconFlightAsync();

                // DisplayEntry, not ShowEntry: the original's dialog_play_record DRAWS the
                // description and returns — it does not wait for dismissal. What follows it is
                // dialog_input_wait_release, a debounce for the right-click that opened the view,
                // after which the More Info sub-menu is drawn with the description still on screen.
                // (An earlier reading took that wait for "user dismisses the description", which
                // delayed the button by a click and is contradicted by the original's own UI.)
                _inspectOpen = true;
                RefreshContainerImage(); // inspecting counts as a selection: keys -> bag
                await _dialogs.DisplayEntry(play);
                // The description sits in the dialog overlay, above this screen, and would swallow
                // the press that ends the view -- the original takes a press anywhere, text included.
                _dialogs.LetClicksThroughPanel();

                // A shop suppresses the More Info menu outright: sub_ovr157_4E3 @0x549BA tests the
                // DISPLAYED container for a shop sub-record and, when it has one, passes page = NULL
                // to UI_showItem — and invinspect_item_flow only runs the More Info sub-menu under
                // `if (page != NULL)` (INVENTOR.C / INVINSP.C:407-417). So the item's own eligibility
                // never even gets asked.
                if (_displayed?.IsShop != true && ItemInspectText.HasMoreInfo(item, obj)) {
                    _inspectStatSlot = slot;
                    VisualElement moreInfo = _stage?.Q($"button_{ButtonMoreInfo}");
                    if (moreInfo != null) {
                        moreInfo.RemoveFromClassList("req-hidden");
                        // The panel fill is added to the stage after the REQ chrome, so without this
                        // the black box paints straight over the button — which is exactly what it is
                        // relied on to do in grid view.
                        moreInfo.BringToFront();
                    }
                }
                // The view now stays up until a press: either on More Info (-> stats) or anywhere
                // else (-> back to the grid). CloseInspect does the teardown.
            } catch {
                CloseInspect();
                throw;
            } finally {
                _descriptionOpen = false;
            }
        }

        /// <summary>The centre of an item's grid cell, stage-local — the icon's start point for the
        /// inspect fly-in, in the same space <see cref="ItemInspectPanel"/> draws in. Falls back to
        /// the icon's own resting point (a zero-length flight) when the cell isn't found, so a
        /// missing cell degrades to "appears in place" rather than flying in from the origin.</summary>
        private Vector2 SlotCentreCanonical(int slot) {
            VisualElement cell = _stage?.Q($"item_slot_{slot}");
            if (cell == null || _stage == null) {
                LayoutHint icon = Layout.InspectIcon;
                return icon == null ? Vector2.zero : new Vector2(icon.Left.Value, icon.Top.Value);
            }
            Rect stageRect = _stage.worldBound;
            Rect cellRect = cell.worldBound;
            return new Vector2(cellRect.center.x - stageRect.x, cellRect.center.y - stageRect.y);
        }

        /// <summary>
        /// True when a stage-local point lands on a visible More Info button.
        ///
        /// <para>An ordinary hit test. It used to be worldBound arithmetic because the dialog
        /// overlay — a separate UIDocument on a higher sortingOrder — absorbed picks across the whole
        /// screen while a description was up, so <c>panel.Pick</c> over the button returned the
        /// overlay and the button never saw a click. That is fixed at the source: the overlay's own
        /// root and stage are non-picking, so only actual dialog content (box, buttons, modal scrim)
        /// picks — see <c>DialogManager.EnsureOverlayIsClickThrough</c>.</para>
        /// </summary>
        private bool IsOverMoreInfo(Vector2 stageLocal) {
            VisualElement button = _stage?.Q($"button_{ButtonMoreInfo}");
            if (button == null || button.ClassListContains("req-hidden") || _stage.panel == null) {
                return false;
            }
            for (VisualElement el = _stage.panel.Pick(_stage.LocalToWorld(stageLocal));
                 el != null; el = el.parent) {
                if (ReferenceEquals(el, button)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Tear down the inspect view (and any stat panel) and bring the grid back, flying the item
        /// icon home first. Safe to call when no inspect view is up, and re-entrant presses during
        /// the flight are swallowed.
        ///
        /// <para>Fire-and-forget because every caller is a synchronous input handler; the
        /// <c>_inspectClosing</c> guard is what keeps the half-torn-down state from being re-entered
        /// (the original is a blocking call chain and can't be re-entered at all).</para>
        /// </summary>
        private void CloseInspect() => CloseInspectAsync().Forget();

        private async UniTaskVoid CloseInspectAsync() {
            if (_inspectClosing) {
                return;
            }
            _inspectClosing = true;
            try {
                _inspectStatSlot = -1;
                _statsBox?.RemoveFromHierarchy(); // parchment is in our stage, so we own its teardown
                _statsBox = null;
                _dialogs?.ClearDialog();          // the description is non-blocking; close it here
                HideInspectOnlyChrome();          // and take the More Info button with it
                // Then the icon flies home. The original erases the whole content area on every
                // frame of that animation (draw_rect_filled 13,11,294,121 in
                // invinspect_animate_item_move), so by the time the icon starts moving the
                // description text is already gone — which is why the clears above come first.
                if (_inspectSlot >= 0) {
                    await _inspect.FlyIconBackAsync(_inspectFrom);
                }
                _inspectSlot = -1;
                _inspectOpen = false; // last: EffectiveContainerImageIndex keeps the bag until now
                _inspect.Clear();
                RenderCurrent();      // rebuilds the grid, which re-hides the More Info button
                RefreshContainerImage(); // bag -> keys once nothing is selected again
            } finally {
                _inspectClosing = false;
            }
        }

        /// <summary>
        /// Swap the inspect view for the "More Info" stat panel (<c>UI_showItemStats</c> @0x5A1DA).
        /// Only reachable while a description is up on a stat-bearing item, since that is the only
        /// state in which the button is un-hidden.
        ///
        /// <para>The original replaces the inspect text with the stat block over the same frame, so
        /// the panel is cleared and re-rendered rather than drawn on top. It also refuses to draw at
        /// all when the item yields no rows — <see cref="ItemStatsText.Build"/> returns an empty list
        /// in exactly that case, which is how the original's "cursor never moved" check behaves.</para>
        /// </summary>
        private void ShowItemStats() => ShowItemStatsAsync().Forget();

        private async UniTaskVoid ShowItemStatsAsync() {
            if (_inspectStatSlot < 0 || _stage == null) {
                return;
            }
            ObjectInfo obj = ObjectAt(_inspectStatSlot);
            if (obj == null || _displayed == null || _inspectStatSlot >= _displayed.Items.Count) {
                return;
            }
            RuntimeItem item = _displayed.Items[_inspectStatSlot];
            IReadOnlyList<ItemStatsText.Line> lines =
                ItemStatsText.Build(item, obj, affecting: _container == null, Layout);
            if (lines.Count == 0) {
                return; // nothing to show — the original skips the panel entirely
            }
            // The stats take the description's place ON THE SAME PARCHMENT PANEL — the original
            // doesn't blank that area, it loads ddx 1800040 and draws its frame there
            // (UI_showItemStats @0x5A1FA-0x5A250). That entry is pure chrome: no text, no branches,
            // just a ResizeDialog at canonical (515,66) 1020x726 = VGA (103,11) 204x121, i.e. exactly
            // the description's footprint. Displaying it swaps the description's text for an empty
            // panel of identical styling, which the stat lines are then drawn over. Clearing the
            // dialog instead left the stats floating on the black fill.
            // The stats keep the description's parchment: the original doesn't blank that area, it
            // loads ddx 1800040 and draws its frame there (UI_showItemStats @0x5A1FA-0x5A250). That
            // entry is pure chrome — no text, no branches, just a ResizeDialog at canonical
            // (515,66) 1020x726 = VGA (103,11) 204x121, i.e. exactly the description's footprint.
            //
            // The box is built INTO this screen's stage rather than shown as a dialog: the dialog
            // overlay is a separate UIDocument on a higher sortingOrder, so a panel shown through it
            // covers the stat text drawn here (the same layering that swallowed the More Info click).
            _dialogs?.ClearDialog();
            GameData.Resources.Dialog.DialogEntry chrome =
                await _dialogExecutor.ResolveLeafEntryAsync(ItemStatsText.ChromeDialogId);
            if (chrome != null && _dialogs != null) {
                _statsBox = await _dialogs.BuildStyledBoxAsync(chrome, _stage);
            }
            // The left panel (name, icon, status) deliberately survives: the original clears only
            // the More Info button's own rect before drawing.
            _inspect.RenderStats(_stage, lines);
            // invinspect_render_details' first act is to clear the button's own rect (VGA 22,114
            // 74x13) before drawing, so it can't be pressed twice.
            _stage.Q($"button_{ButtonMoreInfo}")?.AddToClassList("req-hidden");
            _inspectStatSlot = -1; // button consumed; a further press now closes the view
        }

        // The item-slot index under a STAGE-LOCAL point (walk up from the picked element to
        // item_slot_{n}). -1 if the point isn't over an item cell.
        private int SlotAt(Vector2 stageLocal) {
            IPanel panel = _stage?.panel;
            if (panel == null) {
                return -1;
            }
            const string prefix = "item_slot_";
            for (VisualElement el = panel.Pick(_stage.LocalToWorld(stageLocal)); el != null; el = el.parent) {
                string n = el.name;
                if (!string.IsNullOrEmpty(n) && n.StartsWith(prefix)
                    && int.TryParse(n.Substring(prefix.Length), out int slot)) {
                    return slot;
                }
            }
            return -1;
        }

        private void StartDrag(int slot) {
            _dragSlot = slot;
            RefreshContainerImage(); // keys -> bag: the original's highlight_slot = -2 (INVENTOR.C:668)
            CreateGhost(slot);
            VisualElement cell = _stage.Q($"item_slot_{slot}");
            if (cell != null) {
                // *** THE ORIGINAL EMPTIES THE CELL; IT DOES NOT DIM IT. ***
                // invui_handle_item_drag sets focused->wSprite_base = 0 and re-renders, so the slot
                // reads as empty with the item on the cursor — at no point are there two of it. A
                // dimmed cell leaves a ghost behind instead. The shop exception is the original's
                // own: unbought STOCK keeps its sprite while you drag a copy of it — which is the
                // shelf, not the visit. Dragging out of your own pack inside a shop empties the
                // cell like anywhere else, so this asks ShowingShopShelf and not IsShopMode.
                cell.style.opacity = StyleKeyword.Null;
                cell.visible = !InventoryDragGesture.EmptiesTheOriginCell(ShowingShopShelf);
            }
        }

        // The ObjectInfo behind a displayed slot, or null when the slot or the entry is missing.
        // Single owner for the slot → RuntimeItem.ObjectId → ObjectInfo hop the drag, ghost and
        // equip paths all need.
        private ObjectInfo ObjectAt(int slot) {
            if (_displayed == null || slot < 0 || slot >= _displayed.Items.Count) {
                return null;
            }
            return _gameSession.ObjectInfo?.GetById(_displayed.Items[slot].ObjectId);
        }

        private void CreateGhost(int slot) {
            DestroyGhost();
            // _displayed is checked too: the guard used to dereference it while only null-checking
            // _stage, which an event-driven drag can reach before a container is set.
            if (_stage == null || _displayed == null || slot < 0 || slot >= _displayed.Items.Count) {
                return;
            }
            ObjectInfo obj = ObjectAt(slot);
            Vector2 size = _renderer.CellSizeCanonical(obj?.InventorySlots ?? 1);
            _ghostW = size.x;
            _ghostH = size.y;
            _ghost = new VisualElement {
                name = "loot_ghost",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    width = _ghostW,
                    height = _ghostH,
                },
            };
            _stage.Add(_ghost);
            PositionGhost(_pressPos);
            if (obj != null && _resources != null) {
                // The dragged picture is the item's own: a lit torch keeps its lit face while it
                // is in the air, as it does in the grid (TASK-583).
                LoadGhostIconAsync(_ghost, obj, _displayed.Items[slot].ItemFlags).Forget();
            }
        }

        // Center the ghost on a STAGE-LOCAL pointer position — the held sprite tracks the cursor
        // centered (sub_ovr158_489 @0x56526: x -= w/2, y -= h/2), unlike the hotspot-offset pointer.
        private void PositionGhost(Vector2 stageLocal) {
            if (_ghost == null || _stage == null) {
                return;
            }
            _ghost.style.left = stageLocal.x - _ghostW / 2f;
            _ghost.style.top = stageLocal.y - _ghostH / 2f;
        }

        private async UniTask LoadGhostIconAsync(VisualElement ghost, ObjectInfo obj,
            ushort itemFlags) {
            string key = ItemIconResolver.ResolveBmxSubResource(obj, itemFlags);
            if (key == null) {
                return;
            }
            // The grid has almost always session-cached this icon already (the drag started on a
            // rendered cell) — reuse it instead of taking another Addressables handle per drag.
            if (!_renderer.TryGetCachedSprite(key, out Sprite sprite)) {
                sprite = await _resources.LoadAssetAsync<Sprite>(key, this);
            }
            if (_ghost != ghost || sprite == null) {
                return; // superseded (new drag) or load failed
            }
            // Size the ghost to the icon so centering the element centers the icon on the cursor.
            _ghostW = sprite.rect.width;
            _ghostH = sprite.rect.height;
            ghost.style.width = _ghostW;
            ghost.style.height = _ghostH;
            ghost.SetBackgroundSpriteNativeSizeTopLeft(sprite);
        }

        // --- selection / hover highlights ---

        /// <summary>
        /// Click on an item cell. A double-click on the item that is <b>already selected</b> runs
        /// "Use", which ends by clearing the selection — <c>invui_handleItemDrag</c> @0x57063
        /// compares the freshly-clicked slot against the selection the press started with and,
        /// inside the double-click window, returns -2, which the caller turns into action 0x16.
        ///
        /// <para>The window is the recogniser's own count (<see cref="DragGestureManipulator"/>,
        /// 0.5 s) — not the original's derived 0.845 s (JvE, 2026-07-28). A single click on an
        /// already-selected item leaves it selected, as in the original outside its window.</para>
        ///
        /// <para>Member view only: in a container view the original's action 0x16 falls through
        /// both residence branches (CMBINV.C:358/399) and does nothing — the selection stays.</para>
        /// </summary>
        private void SelectItem(int slot, int clickCount) {
            if ((IsRepairMode || IsBlessingMode)
                && _displayed?.ContainerType == SaveGameContainerType.Inventory
                && slot >= 0 && slot < _displayed.Items.Count) {
                // *** THE MENDER AND THE PRIEST BOTH ANSWER A SINGLE CLICK. *** Their loops act on
                // the item's action id the moment menupage_run returns it — MODALSCR.C:464 for the
                // blessing and :579 for the repair are the same shape — and neither has a
                // select-then-use step. Waiting for a double click leaves the first one looking
                // ignored: measured at the Chapel of Ishap on 2026-09-13, where one click on a sword
                // opens the priest's offer in the original and did nothing here.
                if (IsRepairMode) {
                    RepairAsync(slot).Forget();
                } else if (ObjectAt(slot) is { } blessable) {
                    BlessAsync(slot, blessable).Forget();
                }

                return;
            }
            // Every click on an item names it for the next line (INVENTOR.C:720, nEvtArgItemId):
            // "This @1 doesn't work as it once did" otherwise names the last item stamped.
            if (_displayed != null && slot >= 0 && slot < _displayed.Items.Count) {
                _gameSession.SetDialogKeyObjectId(_displayed.Items[slot].ObjectId);
            }
            bool sameSlot = _selectedSlot == slot;
            if (sameSlot && clickCount >= 2
                && _displayed?.ContainerType == SaveGameContainerType.Inventory) {
                UseSelectedItem();
                return;
            }
            if (sameSlot) {
                return; // already selected, single click: nothing changes
            }
            if (_selectedSlot >= 0) {
                SetOutline(_stage.Q($"item_slot_{_selectedSlot}"), on: false, fill: true);
                ShowSelectionQuantity(_selectedSlot, on: false);
            }
            _selectedSlot = slot;
            SetOutline(_stage.Q($"item_slot_{slot}"), on: true, fill: true);
            ShowSelectionQuantity(slot, on: true);
            RefreshContainerImage(); // keys -> bag on the first selection
        }

        /// <summary>
        /// Action 0x16, "Use" — reached from the Use button and from a double-click on the selected
        /// item, which is why both go through here (the original routes them into one handler too:
        /// the double-click returns -2 and the caller rewrites it to <c>action = 0x16</c>).
        ///
        /// <para>Faithful to the party-slot branch at 0x54B6C: an item of type Repair (8), Poison (9)
        /// or BowString (12) can't be used on its own and plays DDX 1800010 instead; anything else
        /// goes to the item-use dispatch. <b>The selection is cleared either way</b> (0x54BAD).</para>
        ///
        /// <para>The equippable categories (Sword/Crossbow/Staff/Armor) are the first dispatch
        /// branch of <c>Use_Item</c> @0x58cbd (ITEMUSE.C:136-167) and are implemented: "use" a
        /// weapon or armor = equip it, silently (outcome -2). A category the member can't wear
        /// falls through to the no-effect exit, DDX 1800003 with Var 0 = object id. Everything else
        /// goes to <see cref="InventoryUse.Use"/> in its no-target form; the categories that still
        /// have no ported branch there log rather than claim nothing happened (spec §17.2).</para>
        /// </summary>
        /// <summary>
        /// The use-gate chain, run before any dispatch — <c>itemuse_dispatch_on_target</c>
        /// (ITEMUSE.C:104-131, <c>Use_Item</c> @0x58cbd). Returns <c>true</c> when the item is refused,
        /// having played the record the original plays; the caller then does nothing else.
        ///
        /// <para>Order matters and is the original's: caster gates first, then the combat-mode gates,
        /// then exhaustion. The first match wins, so an out-of-charges combat-only item held by the
        /// wrong member reports the member problem, not the charges.</para>
        ///
        /// <para><b>No combat mode yet.</b> <c>g_wInCombatMode</c> is effectively 0, so
        /// <see cref="ObjectFlags.OnlyUsableInCombat"/> always refuses (right — those items genuinely
        /// cannot be used here) and <see cref="ObjectFlags.NotUsableInCombat"/> never fires. Both stay
        /// written against a flag so wiring combat later is a one-line change, not a re-read of
        /// ITEMUSE.C.</para>
        /// </summary>
        internal static bool RefuseUse(ObjectInfo obj, RuntimeItem item, bool isCaster, bool inCombat,
            out int dialogId, out int var0) {
            var0 = 0;
            ObjectFlags flags = obj.Flags;
            // stats[7].max == 0 is "has no casting skill" — index 7 is AccuracyCasting.
            if ((flags & ObjectFlags.SpellcastersOnly) != 0 && !isCaster) {
                dialogId = NeedsCasterDialogId;
                return true;
            }
            if ((flags & ObjectFlags.NonSpellcastersOnly) != 0 && isCaster) {
                dialogId = RefusesCasterDialogId;
                return true;
            }
            if ((flags & ObjectFlags.OnlyUsableInCombat) != 0 && !inCombat) {
                dialogId = CombatOnlyDialogId;
                return true;
            }
            if ((flags & ObjectFlags.NotUsableInCombat) != 0 && inCombat) {
                dialogId = NotInCombatDialogId;
                return true;
            }
            // Exhaustion. Var 0 picks the wording: 1 for an item the player is wearing or one that
            // matters to the plot, else 0 — and object 1 specifically takes leaf 2.
            // ITEMUSE.C:119: (item->flags & 0x40) || (flags & 2).
            if ((flags & ObjectFlags.LimitedUses) != 0 && item.Variable == 0) {
                bool equippedOrProtected = (item.ItemFlags & EquippedItemFlag) != 0
                    || (flags & ObjectFlags.Protected) != 0;
                dialogId = UsedUpDialogId;
                var0 = equippedOrProtected ? 1 : 0;
                return true;
            }
            if (item.ObjectId == 1 && item.Variable == 0) {
                dialogId = UsedUpDialogId;
                var0 = 2;
                return true;
            }
            dialogId = 0;
            return false;
        }

        // item->flags & 0x40 — the equipped bit, same one InventoryTransfer sets on auto-equip.
        private const ushort EquippedItemFlag = 0x0040;

        /// <summary>
        /// Whether this screen was opened from a fight — the original's <c>g_wInCombatMode</c>.
        /// </summary>
        /// <remarks>
        /// <b>It changes which items are refused, both ways.</b>
        /// <see cref="ObjectFlags.OnlyUsableInCombat"/> items are refused OUTSIDE a fight and
        /// allowed inside; <see cref="ObjectFlags.NotUsableInCombat"/> items are the reverse. Until
        /// this existed the flag was hard-coded false, so the first group could never be used at all
        /// and the second was never refused.
        ///
        /// <para><b>Set it immediately before the push, and it clears itself on hide</b> — beside
        /// the other per-open mode state in <c>OnBeforeHide</c>, for the same reason: a value left
        /// standing would make the NEXT open, from the world, refuse combat items it should allow.
        /// </para>
        ///
        /// <para>The gate chain it feeds is <see cref="RefuseUse(ObjectInfo, RuntimeItem, bool, bool,
        /// out int, out int)"/>, whose combat arms have been tested from the start; only the value
        /// reaching them was a literal.</para>
        /// </remarks>
        public bool InCombat { get; set; }

        /// <summary>
        /// Set with <see cref="InCombat"/> by the fight that opens this pack: clears the acting
        /// combatant's poison flag when anti-venom is used (ITEMUSE.C:195-197). Cleared on hide.
        /// </summary>
        public System.Action ClearActingCombatantPoison { get; set; }

        /// <summary>Whether the fight this pack was opened from is underground (<c>g_game_mode ==
        /// 2</c>) -- staff 2 will not fire there.</summary>
        public bool CombatUnderground { get; set; }

        /// <summary>
        /// Cells between two members in the fight this pack was opened from, by character index
        /// (-1 for a pack that is not a member's) -- <c>combat_arena_dist_actors_by_id</c>. Null
        /// outside a fight.
        /// </summary>
        public System.Func<int, int, int> CombatDistance { get; set; }

        /// <summary>"@0 didn't look up..." -- a give to a member out of reach mid-fight.</summary>
        private const int TooFarInCombatDialogId = 1800012; // 0x1B774C
        /// <summary>"@ was unable to steal the @1" — a held item in a Nightfingers pack (INVENTOR.C:760).</summary>
        private const int CannotStealHeldItemDialogId = 1800041; // 0x1B7769

        private async Cysharp.Threading.Tasks.UniTaskVoid RefuseStaffAsync(int line, int objectId) {
            if (line != 0 && _dialogs != null) {
                await _dialogs.ShowById(line);
            }
            await ShowVar0MessageAsync(UseHadNoEffectDialogId, objectId);
        }

        private bool RefuseUse(ObjectInfo obj, RuntimeItem item) {
            if (!RefuseUse(obj, item, IsDisplayedMemberCaster(), InCombat,
                    out int dialogId, out int var0)) {
                return false;
            }
            ShowVar0Message(dialogId, var0);
            return true;
        }

        /// <summary>
        /// The command id the arena will act on when this screen closes, or
        /// <see cref="GameData.Resources.Combat.CombatItemUse.NoItemUsed"/>.
        /// </summary>
        /// <remarks>
        /// <b>This is the original's return value, not a side channel.</b>
        /// <c>cmbinv_inventory_screen_run</c> returns <c>combat_result</c> in a fight and the arena
        /// passes it straight to <c>combat_arena_resume_dispatch</c>. A screen cannot return a value
        /// through the navigator, so it is left here for the opener to read.
        ///
        /// <para>Cleared when the screen is opened, not when it is read: an opener that forgets to
        /// clear would replay the previous fight's item.</para>
        /// </remarks>
        public int PendingCombatCommandId { get; private set; }

        /// <summary>
        /// Claims an item for the arena rather than using it here. True when it was claimed.
        /// </summary>
        /// <remarks>
        /// <b>Nothing is consumed and nothing is equipped on this path</b>, which is the whole
        /// point — see <c>CombatItemUse.TheScreenConsumesOnlyWhatItKeeps</c>. The two refusals
        /// (Lightning Staff underground, Idol in chapter 8) are NOT checked here: the arena checks
        /// them, as it does in the original, and duplicating them would let the copies drift.
        ///
        /// <para><b>*** EVERY USE ASSIGNS THE PENDING ID, INCLUDING THE ONES THAT ARE NOT COMBAT
        /// ITEMS. ***</b> Using a combat item does NOT close the screen — the player does — and the
        /// original's loop overwrites its single <c>result</c> on every use, returning only the last
        /// one. So eating a ration after raising the Horn <b>cancels the Horn</b>, because the
        /// ration's use puts -1 back. Assigning only on a claim would let a stale item fire when the
        /// player left the pack, which looks like the game acting on a decision they changed their
        /// mind about.</para>
        /// </remarks>
        private bool TakeCombatCommand(ObjectInfo obj, RuntimeItem item) {
            if (InCombat && obj.ObjectType == GameData.ObjectType.Staff
                && (item.ItemFlags & EquippedItemFlag) != 0 && item.Variable != 0
                && GameData.Resources.Combat.CombatItemUse.StaffRefuses(
                    item.ObjectId, CombatUnderground, _gameSession?.Chapter ?? 0, out int line)) {
                PendingCombatCommandId = GameData.Resources.Combat.CombatItemUse.NoItemUsed;
                _gameSession.SetDialogKeyObjectId(item.ObjectId);   // the line's @1
                RefuseStaffAsync(line, item.ObjectId).Forget();
                return true;
            }
            int? command = GameData.Resources.Combat.CombatItemUse.CommandIdFrom(
                obj.ObjectType, item.ObjectId, InCombat,
                equipped: (item.ItemFlags & EquippedItemFlag) != 0,
                intact: item.Variable != 0);

            // The assignment is unconditional, mirroring `result = itemuse_dispatch_on_target(...)`.
            PendingCombatCommandId =
                command ?? GameData.Resources.Combat.CombatItemUse.NoItemUsed;
            return command != null;
        }

        private void UseSelectedItem() {
            int slot = _selectedSlot;
            DeselectItem();
            if (slot < 0) {
                return;
            }
            ObjectInfo obj = ObjectAt(slot);
            if (obj == null) {
                return;
            }
            if (IsBlessingMode) {
                // The verb is what the mode changes: in a temple, using an item offers to bless it.
                BlessAsync(slot, obj).Forget();

                return;
            }
            if (System.Array.IndexOf(NotDirectlyUsableTypes, obj.ObjectType) >= 0) {
                _dialogs?.ShowById(NotDirectlyUsableDialogId).Forget();
                return;
            }
            if (RefuseUse(obj, _displayed.Items[slot])) {
                return;
            }

            // *** IN A FIGHT, SOME ITEMS ARE THE ARENA'S TO RESOLVE, NOT THIS SCREEN'S. *** The
            // original's dispatch sets combat_result and falls straight to its tail: no equip, no
            // effect, and NOTHING CONSUMED, because combat_arena_resume_dispatch's own tail
            // consumes by kind. Doing it here as well eats two of the item.
            //
            // Note this comes BEFORE the equip branch on purpose: an equipped, unbroken staff in a
            // fight is a weapon to fire, not a weapon to re-equip.
            if (TakeCombatCommand(obj, _displayed.Items[slot])) {
                return;
            }

            bool equippable = obj.ObjectType == GameData.ObjectType.Sword
                || obj.ObjectType == GameData.ObjectType.Crossbow
                || obj.ObjectType == GameData.ObjectType.Staff
                || obj.ObjectType == GameData.ObjectType.Armor;
            if (equippable) {
                if (MemberEquip.CanEquipCategory(obj.ObjectType, IsDisplayedMemberCaster())) {
                    InventoryEquip.Equip(_displayed, slot, _gameSession.ObjectInfo);
                    RenderCurrent(); // the item moves grid -> paperdoll (and the old one back)
                } else {
                    ShowVar0Message(UseHadNoEffectDialogId, _displayed.Items[slot].ObjectId);
                }
                return;
            }
            // Everything else goes to the dispatch with no target — the form the Use button has.
            ItemUseResult used = InventoryUse.Use(_displayed, slot, InventoryUse.NoTarget,
                _gameSession.ObjectInfo, BuildUseContext());
            if (obj.ObjectType == GameData.ObjectType.Restorative
                && used.Outcome == ItemUseOutcome.Handled) {
                DoseAgainAsync(slot, obj.Number, used).Forget();
                return;
            }
            ApplyUseResult(used, slot);
        }

        /// <summary>"@ has @1 of @2 total health points." -- the restorative's use-another prompt.</summary>
        private const int DoseAgainDialogId = 1800004; // 0x1B7744

        /// <summary>
        /// A restorative keeps dosing while the player says so (ITEMUSE.C:331-354): "used", then after
        /// each dose, while any is left, the member's pool against its maximum and a choice -- the
        /// first answer doses again.
        /// </summary>
        /// <remarks>
        /// The prompt's two numbers are <c>lEvtArgGoldCost</c> and <c>lEvtArgValue</c> (globals 30014
        /// and 30015), set to <c>stat_actor_get(member, 0x10, 0/1)</c> before it is asked.
        /// </remarks>
        private async UniTaskVoid DoseAgainAsync(int slot, int objectId, ItemUseResult first) {
            int member = DisplayedMemberIndex();
            ItemUseResult result = first;
            while (true) {
                if (result.DialogId != 0 && _dialogs != null) {
                    await ShowVar0MessageAsync(result.DialogId, result.DialogVar0);
                }
                ApplyUseResult(new ItemUseResult(result.Outcome, 0, 0, result.SourceRemoved), slot,
                    redrawAnyway: true);
                if (result.SourceRemoved || member < 0 || _dialogs == null
                    || slot >= _displayed.Items.Count || _displayed.Items[slot].ObjectId != objectId) {
                    return;
                }
                _gameSession.SetGlobalValue(30014, _gameSession.EffectivePool(member));
                _gameSession.SetGlobalValue(30015, _gameSession.EffectivePoolMax(member));
                if (await _dialogs.ShowChoiceIndexById(DoseAgainDialogId) != 0) {
                    return;
                }
                ItemUseResult again = InventoryUse.Use(_displayed, slot, InventoryUse.NoTarget,
                    _gameSession.ObjectInfo, BuildUseContext());
                // Only the first dose says "used"; the loop's own prompt reports the rest.
                result = new ItemUseResult(again.Outcome, 0, again.DialogVar0, again.SourceRemoved);
            }
        }

        /// <summary>
        /// The temple's offer on one item: bless it, at a price, if it can be blessed at all.
        /// </summary>
        /// <remarks>
        /// <b>A lower tier REPLACES a higher one.</b> The original clears all three blessing bits
        /// before setting the new one, so paying a tier-1 temple to bless a tier-3 sword makes it
        /// WORSE — which is exactly why it asks "already blessed, replace it?" first rather than
        /// silently upgrading. A port that OR-ed the new bit in would turn every re-blessing into a
        /// free upgrade and leave that confirm with nothing to confirm.
        ///
        /// <para><b>Swords and armour only</b> — not crossbows and not staves, so a magician's staff
        /// can never be blessed even though the bonus applies to whatever is equipped. Anything else
        /// is refused before a price is even computed.</para>
        /// </remarks>
        /// <summary>
        /// One item offered to the mender — the <c>action &gt;= 0x80</c> arm of
        /// <c>modalscreen_inventory_request</c> (MODALSCR.C:600-660).
        /// </summary>
        /// <remarks>
        /// <b>The clock is charged before the quote, not after the sale.</b> The original's
        /// <c>timeFlags |= di</c> sits inside <c>if (di != 0)</c> and OUTSIDE the condition test and
        /// the agreement, so merely asking about a sword the mender handles costs the four hours
        /// whether the player buys the repair, declines the price, or is told the blade is already
        /// fine. Only "he cannot mend that" is free. That is the behaviour, not an oversight to
        /// tidy: <see cref="ShopRepair.TimeBitFor"/> carries the rest of the quirk.
        /// </remarks>
        private async UniTaskVoid RepairAsync(int slot) {
            if (_displayed == null || slot < 0 || slot >= _displayed.Items.Count) {
                return;
            }
            RuntimeItem item = _displayed.Items[slot];
            ObjectInfo obj = ObjectAt(slot);
            if (obj == null || _dialogs == null) {
                return;
            }

            // @0 in all three of the mender's lines is the item, through the same global every
            // other subject on this screen is published by.
            _gameSession.SetDialogKeyObjectId(item.ObjectId);

            ShopRepair.Outcome outcome =
                ShopRepair.OutcomeFor(item, obj, _mender.RepairCategories);
            if (outcome == ShopRepair.Outcome.CannotMend) {
                await _dialogs.ShowById(ShopRepair.CannotMendDialogId);

                return;
            }

            _menderTimeMask |= ShopRepair.TimeBitFor(obj.ObjectType);

            if (outcome == ShopRepair.Outcome.NeedsNoRepair) {
                await _dialogs.ShowById(ShopRepair.NeedsNoRepairDialogId);

                return;
            }

            ObjectInfoSet objects = _gameSession.ObjectInfo;
            int price = ShopRepair.PriceFor(item, obj, _mender.RepairCostMarkup,
                objects == null ? (System.Func<int, ObjectInfo>)null : objects.GetById);
            _gameSession.SetGlobalValue(DialogSlotPopulator.QuotedAmountGlobalKey, price);
            // *** THE QUOTE OWNS THE AFFORDABILITY RULE, SO RUN THE WHOLE DIALOG. ***
            // MODALSCR.C is `dialog_play_record(0x1b7763, 0); if (gstate_event_read(0x104) != 0)
            // { gold -= cost; condition = 100; }` — there is no gold test in C at all. 1800035's
            // accept branch (flag 260 = 0x104) leads to an entry gated on Var 3, whose default arm
            // is the mender's "I seem to be short" — and that arm CLEARS flag 260 on its way past,
            // which is what stops the charge.
            //
            // `ShowConfirmById` stops at the first entry and answers which button was pressed, so
            // the gate was never evaluated, the refusal never spoke, and this site re-derived the
            // rule with its own `PartyGold < price` guard. Measured side by side at Highcastle on
            // 2026-09-13: the original says "Locklear rummaged around for the needed sovereigns",
            // the port said nothing. The guard is not deleted lightly — the dialog now carries it,
            // and the flag is the same one the original reads back.
            await _dialogs.ShowById(ShopRepair.QuoteDialogId);
            if (_gameSession.GetGlobalValue(ShopRepair.AgreedEventKey) == 0) {
                return;
            }

            _gameSession.PartyGold -= price;
            ShopRepair.Apply(item);
            _displayed.Dirty = true;
            RenderCurrent();
        }

        private async UniTaskVoid BlessAsync(int slot, ObjectInfo obj) {
            if (!TempleBlessing.CanBless(obj.ObjectType)) {
                await _dialogs.ShowById((int)TempleBlessing.CannotBlessDialogId);

                return;
            }

            RuntimeItem item = _displayed.Items[slot];
            if (TempleBlessing.IsBlessed((GameData.ItemFlags)item.ItemFlags)
                && !await _dialogs.ShowConfirmById((int)TempleBlessing.AlreadyBlessedDialogId)) {
                return;
            }

            long price = TempleBlessing.Price(obj.Price, _blessing.MaxHagglingDiscount,
                _blessing.MarkupPercentage);
            // The offer's wording differs for armour, and the dialog reads which from the same
            // global the rest of this screen publishes its subject through.
            _gameSession.SetDialogKeyObjectId(item.ObjectId);
            _gameSession.SetGlobalValue(DialogArgCountGlobal,
                TempleBlessing.OfferWordingFor(obj.ObjectType));
            _gameSession.SetGlobalValue(DialogSlotPopulator.QuotedAmountGlobalKey, (int)price);
            // *** THE OFFER OWNS THE RULE, SO RUN THE WHOLE DIALOG. *** MODALSCR.C:471 is
            // `dialog_play_record(0x13d670, 0); if (gstate_event_read(0x104) != 0) { gold -= cost;
            // ... }` — the same shape as the mender's quote, and with no gold test in C either.
            // 1300080's accept branch (flag 260 = 0x104) leads to an entry gated on Var 3, whose
            // arms are the priest's two outcomes: "your weapon has been enchanted" if the purse
            // covers it, and "@ blanched as he realized he was short of funds" if it does not —
            // and the short arm CLEARS flag 260, which is what stops the charge.
            //
            // ShowConfirmById stopped at the first entry, so NEITHER outcome was ever spoken: a
            // player who could afford it got a silent blessing and one who could not got a dead
            // click. Measured at the Chapel of Ishap, Malac's Cross, on 2026-09-13.
            await _dialogs.ShowById((int)TempleBlessing.PriceOfferDialogId);
            if (_gameSession.GetGlobalValue(TempleBlessing.AcceptedFlag) == 0) {
                return;
            }
            _gameSession.PartyGold -= (int)price;
            item.ItemFlags = (ushort)TempleBlessing.Bless((GameData.ItemFlags)item.ItemFlags,
                _blessing.MarkDownPercentage);
            _displayed.Dirty = true;
            RenderCurrent();
        }

        /// <summary>
        /// <c>nEvtArgCount</c> — the number a dialog branches on as <b>Var 0</b>.
        /// </summary>
        /// <remarks>
        /// <b>It was 30016 until 2026-09-09, and that is the party-down byte.</b> Event key 30016 is
        /// index 16, which <see cref="GameData.Resources.GameState.GameStateEventFields"/> maps to
        /// <c>PartyDeathState</c>; the arg count is index 0, key 30000. So every caller here was
        /// publishing its dialog mode into the byte that means "the party is down", and
        /// <c>InGameScreen.LeaveTheWorldPartyDownAsync</c> then did the right thing with a non-zero
        /// value: it left the world for the main menu.
        ///
        /// <para>Measured: clicking the tunnel just inside the Mac Mordain Cadal took
        /// <c>PartyDeathState</c> from 0 to <b>3</b> — <c>LockContext.Traversal</c>. Doors (1) and
        /// containers (2) do it too; only <c>Person</c> (0) was harmless, which is why it survived.
        /// TASK-397.</para>
        ///
        /// <para>Both users want this key and both say so. <c>picklock_screen_run</c>
        /// (PICKLOCK.C:68) is literally <c>g_gameState.nEvtArgCount = mode;</c>, and the shipped
        /// prompt confirms it: dialog 79's four branches are <c>VarCondition Var 0</c> with
        /// Min=Max=0, 1, 2 and 3 — exactly the four <c>LockContext</c> values. Writing 30016 never
        /// selected any of them, so a tunnel got a chest's wording as well as ending the session.
        /// <c>TempleBlessing.OfferWordingFor</c>'s own summary names <c>nEvtArgCount</c> too.</para>
        /// </remarks>
        internal const int DialogArgCountGlobal =
            GameData.Resources.GameState.GameStateEventFields.FieldBase;

        /// <summary>
        /// Play a DDX whose root is text-less and branches on Var 0 (global 30000) — the pattern
        /// behind the inventory's parameterised messages (1800003 keyed by object id, 1800008 by
        /// destination kind). Same seeding the item-description path uses; entries whose walk ends
        /// text-less show nothing, faithfully.
        /// </summary>
        private void ShowVar0Message(int dialogId, int var0) => ShowVar0MessageAsync(dialogId, var0)
            .Forget();

        /// <summary>
        /// The practice lute's arm: its tune over the message, and whatever was playing put back.
        /// </summary>
        /// <remarks>
        /// <b>It interrupts rather than replaces.</b> <c>PlayTrackAsync</c> hands back the outgoing
        /// track, which is the whole reason the original keeps it in a scratch slot — the zone's
        /// music resumes as the track it was rather than restarting or falling silent.
        ///
        /// <para>The restore has to wait for the dialog, which is why this exists at all: the
        /// ordinary path fires the message and forgets it.</para>
        /// </remarks>
        private async UniTaskVoid ShowVar0MessageOverTrackAsync(int dialogId, int var0, int track) {
            int previous = await _midi.PlayTrackAsync(track, _resources, owner: this);
            await ShowVar0MessageAsync(dialogId, var0);
            _midi.PlayTrackAsync(previous, _resources, owner: this).Forget();
        }

        /// <summary>
        /// The first look at a map note: the note's own line, and only then the picture.
        /// </summary>
        /// <remarks>
        /// <b>The order is the original's, and so is where each line is seen.</b> ITEMUSE.C plays
        /// the preface while the INVENTORY is still on screen and only then blits RIFTMAP, so this
        /// awaits the message before pushing the map rather than showing both over the art.
        /// <see cref="InventoryUse"/> decides whether there is one — it plays once ever, gated on
        /// the map's viewed flag read before that same use writes it.
        /// </remarks>
        private async UniTaskVoid LookThroughSpyglassAsync(int dialogId, int var0) {
            await ShowVar0MessageAsync(dialogId, var0);
            await LocatorMap.RunSpyglassAsync();
        }

        private async UniTaskVoid ShowMapAsync(int prefaceDialogId, int var0) {
            if (prefaceDialogId != 0) {
                await ShowVar0MessageAsync(prefaceDialogId, var0);
            }
            await _riftMap.RunAsync(var0);
        }

        private async UniTask ShowVar0MessageAsync(int dialogId, int var0) {
            _gameSession.SetGlobalValue(ItemInspectText.DescriptionGlobalKey, var0);
            GameData.Resources.Dialog.DialogPlay play =
                await _dialogExecutor.ResolvePlayAsync(dialogId);
            if (play?.Entry != null && !string.IsNullOrEmpty(play.Entry.Text)) {
                await _dialogs.ShowEntry(play);
            }
        }

        /// <summary>
        /// Clear the item selection — drop the outline and swap the detail window's bag back to the
        /// keys. Idempotent, so callers don't have to check first.
        /// </summary>
        private void DeselectItem() {
            if (_selectedSlot < 0) {
                return;
            }
            SetOutline(_stage?.Q($"item_slot_{_selectedSlot}"), on: false, fill: true);
            ShowSelectionQuantity(_selectedSlot, on: false);
            _selectedSlot = -1;
            RefreshContainerImage(); // bag -> keys
        }

        /// <summary>
        /// Show or hide a slot's selection-gated quantity — the "uses left" number a charged or
        /// degradable item displays only while it is the highlighted slot
        /// (<c>UI_DrawInventory</c> @0x56d29-0x56d43: flags 0x3000, <c>arg_8 >= 0</c>, and the
        /// entry's own action id). Only labels the renderer marked as gated are touched, so a
        /// stack count is never hidden by selecting and deselecting it.
        /// </summary>
        private void ShowSelectionQuantity(int slot, bool on) {
            if (slot < 0) {
                return;
            }
            VisualElement qty = _stage?.Q($"item_slot_{slot}_qty");
            if (qty == null || !qty.ClassListContains(ItemGridRenderer.SelectionQuantityClass)) {
                return;
            }
            qty.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // The REQ hotspots that are live alongside the item cells (CMBINV.C:60 keeps entries 0..6).
        // Order is the REQ's own: three portraits, container window, Use, Exit, gold.
        private static readonly int[] ChromeHotspotActions = {
            PortraitSlot1, PortraitSlot2, PortraitSlot3, WindowContainerImage,
            ButtonUse, ButtonExit, ButtonGold,
        };

        private bool OverChromeHotspot(Vector2 stageLocal) {
            if (_ui == null) {
                return false;
            }
            foreach (int action in ChromeHotspotActions) {
                if (_ui.TryGetElementRect(action, out Rect rect) && rect.Contains(stageLocal)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The portrait slot (0/1/2) whose member inventory is currently displayed — the original's
        /// <c>actorNr</c>, which <c>UI_DrawPartyHeadHighlightCircles</c> takes as arg_0. -1 in loot
        /// mode (a container, not a member, is on screen) or when the owner isn't in the active party.
        /// </summary>
        internal int ActivePortraitSlot() {
            // A lock shows a scratch container that nobody owns, so the owner lookup below cannot
            // answer. The original still marks a member — the one doing the picking.
            if (IsLockMode) {
                return _lockPicker;
            }

            int owner = _displayed?.OwnerActorNumber ?? -1;
            if (owner < 0 || _displayed?.ContainerType != SaveGameContainerType.Inventory) {
                return -1;
            }
            byte[] activeIndices = _gameSession.ActivePartyIndices;
            SaveGameActorData[] actors = _gameSession.PartyActors;
            for (int slot = 0; slot < activeIndices.Length; slot++) {
                byte rosterIndex = activeIndices[slot];
                if (rosterIndex < actors.Length && actors[rosterIndex].ActorNumber == owner) {
                    return slot;
                }
            }
            return -1;
        }

        // Steady "this is whose inventory you're viewing" ring (HEADS.BMX #7) over the active member's
        // portrait — the original draws it on every repaint, drag or not (0x5632D). Re-run after each
        // render so a portrait switch moves it; hidden in loot mode, where there is no active member.
        private void UpdateActiveMemberCircle() {
            int slot = ActivePortraitSlot();
            VisualElement host = slot < 0 ? null : _stage?.Q($"hotspot_{slot + PortraitSlot1}");
            if (host == null) {
                _activeCircle?.RemoveFromHierarchy();
                return;
            }
            _activeCircle ??= CreateCircleOverlay("active_member_circle");
            if (_activeCircle.parent != host) {
                _activeCircle.RemoveFromHierarchy();
                host.Add(_activeCircle);
            }
            PaintCircle(_activeCircle, ActiveCircleFrame);
        }

        // Drag-hover portrait highlight: overlay the pulsing HEADS.BMX ring on the hovered portrait
        // (removing it from the previous one). The circle sits above the portrait face at the same
        // click-area rect, so it reads as a ring around the head — the faithful equivalent of the
        // original's per-frame circle draw (UI_DrawPartyHeadHighlightCircles @0x562a5).
        //
        // Hovering the ACTIVE member is deliberately not a highlight — see ResolveHoverPortrait.
        private void SetHoverPortrait(int portraitSlot) {
            portraitSlot = ResolveHoverPortrait(portraitSlot, ActivePortraitSlot());
            if (portraitSlot == _hoverPortrait) {
                return;
            }
            _hoverPortrait = portraitSlot;
            if (portraitSlot < 0) {
                RemoveHoverCircle();
            } else {
                ShowHoverCircle(portraitSlot);
            }
        }

        // Parent (or reparent) the pulsing-circle overlay onto portrait 0/1/2's click area and restart
        // its pulse. The element is reused across hovers; released with the screen in OnDisable.
        private void ShowHoverCircle(int portraitSlot) {
            VisualElement host = _stage?.Q($"hotspot_{portraitSlot + PortraitSlot1}");
            if (host == null) {
                return;
            }
            if (_hoverCircle == null) {
                _hoverCircle = CreateCircleOverlay("hover_circle");
            } else {
                _hoverCircle.RemoveFromHierarchy();
            }
            host.Add(_hoverCircle);
            ApplyHoverCircleSprite();
        }

        private void RemoveHoverCircle() {
            _hoverCircle?.RemoveFromHierarchy();
            _hoverCircle = null;
        }

        /// <summary>
        /// Which portrait, if any, should carry the pulsing drag-hover ring — the original's
        /// <c>hover != 0 &amp;&amp; hover != active</c> gate at 0x562D4/0x562D8, with -1 standing in for the
        /// engine's 0 ("no portrait"). Hovering the member you are already viewing is NOT a highlight:
        /// their steady ring stays as it is rather than turning red.
        /// </summary>
        internal static int ResolveHoverPortrait(int hoveredSlot, int activeSlot) =>
            hoveredSlot == activeSlot ? -1 : hoveredSlot;

        // A ring overlay fills its host portrait's click area and never eats the drag/drop pointer.
        private static VisualElement CreateCircleOverlay(string name) => new() {
            name = name,
            pickingMode = PickingMode.Ignore,
            style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
        };

        // Paint one HEADS.BMX ring frame. Contain + top-left anchor matches PartyHeadsView so the
        // circle (280×270, same as the face) lands exactly over the head; the sprite's transparent
        // centre lets the face show through.
        private void PaintCircle(VisualElement circle, int frame) {
            if (circle == null || _circleSprites == null) {
                return;
            }
            Sprite sprite = _circleSprites[frame - CircleFirstFrame];
            if (sprite == null) {
                return;
            }
            circle.style.backgroundImage = Background.FromSprite(sprite);
            circle.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            circle.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left);
            circle.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top);
        }

        private void ApplyHoverCircleSprite() =>
            PaintCircle(_hoverCircle, HoverCirclePulse[_pulsePhase % HoverCirclePulse.Length]);

        // --- container window (action 32) as a drop target ---

        /// <summary>
        /// Is <paramref name="stageLocal"/> over the action-32 container window? The original tests
        /// pointer identity against <c>page-&gt;pEntries[3]</c> (0x5724C, the 4th REQ entry = action
        /// 32), so this is a plain hit test on that hotspot's rect, not a Pick.
        /// </summary>
        private bool IsOverContainerWindow(Vector2 stageLocal) =>
            _ui != null && _ui.TryGetElementRect(WindowContainerImage, out Rect rect)
                && rect.Contains(stageLocal);

        /// <summary>
        /// Show or hide the container window's drag-time border. Faithful to the two call sites of
        /// <c>invui_portrait_panel_draw</c>: while dragging a MEMBER's item the window pulses when
        /// hovered (0x57250 passes 1 only for <c>pEntries[3]</c> + member mode); while dragging in
        /// the CONTAINER view it takes a steady pen instead (0x57279 passes -1 when the displayed
        /// container isn't a member inventory). Not dragging → no border at all.
        /// </summary>
        internal static bool ShouldShowContainerBorder(bool dragging, bool overWindow, bool memberView) =>
            dragging && (!memberView || overWindow);

        /// <summary>Which INVENTOR.PAL pen the window's border takes — see the constants.</summary>
        internal static int ContainerBorderPen(bool memberView, int pulsePhase) => memberView
            ? PulsePen(pulsePhase)
            : DiscardBorderSteadyPen;

        /// <summary>The pulse pen for a drag frame: <c>phase % 6</c> into the triangular table.</summary>
        internal static int PulsePen(int pulsePhase) =>
            BorderPulsePens[pulsePhase % BorderPulsePens.Length];

        private void UpdateDiscardBorder(bool dragging, bool overWindow) {
            bool memberView = _displayed?.ContainerType == SaveGameContainerType.Inventory;
            bool wanted = ShouldShowContainerBorder(dragging, overWindow, memberView);
            if (!wanted) {
                _discardBorder?.RemoveFromHierarchy();
                _discardBorder = null;
                return;
            }
            VisualElement host = _stage?.Q($"hotspot_{WindowContainerImage}");
            if (host == null) {
                return;
            }
            if (_discardBorder == null) {
                _discardBorder = new VisualElement {
                    name = "container_window_border",
                    pickingMode = PickingMode.Ignore,
                    style = {
                        position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0,
                        // A hairline frame. The two axes differ because the design frame is not an
                        // isotropic blow-up of what the original drew on — which is precisely why
                        // the widths are data rather than one shared number.
                        borderLeftWidth = Layout.ContainerBorderWidthX,
                        borderRightWidth = Layout.ContainerBorderWidthX,
                        borderTopWidth = Layout.ContainerBorderWidthY,
                        borderBottomWidth = Layout.ContainerBorderWidthY,
                    },
                };
                host.Add(_discardBorder);
            }
            ApplyDiscardBorderPen();
        }

        private void ApplyDiscardBorderPen() {
            if (_discardBorder == null) {
                return;
            }
            int pen = ContainerBorderPen(
                _displayed?.ContainerType == SaveGameContainerType.Inventory, _pulsePhase);
            Color c = BakAgain.Graphics.PaletteColors.ResolvePen(_inventoryPalette, pen, OutlineRed);
            _discardBorder.style.borderLeftColor = c;
            _discardBorder.style.borderRightColor = c;
            _discardBorder.style.borderTopColor = c;
            _discardBorder.style.borderBottomColor = c;
        }

        /// <summary>
        /// Resolve a drop onto the container window. The original runs
        /// <c>cmbinv_actor_transfer_item(g_other_inventory_actor, actor, item)</c> (INVENTOR.C:773),
        /// gated on the displayed actor being a party member — dropping while the loot container
        /// itself is displayed does nothing.
        ///
        /// <para><c>g_other_inventory_actor</c> is the container the screen was opened on, or NULL
        /// when it was opened on a member from travel (CMBINV.C:248). With a container it's a plain
        /// transfer — "put this back". With NULL the transfer falls through to
        /// <c>cmbinv_actor_drop_item_at_pos</c>, which puts the item on the ground at the party's
        /// position: this is the discard, and <see cref="DiscardToGround"/> carries it out.</para>
        ///
        /// <para>Returns true when the drop was consumed.</para>
        /// </summary>
        private bool DropOnContainerWindow(int slot) {
            if (_displayed?.ContainerType != SaveGameContainerType.Inventory) {
                return false; // container view: the window is decoration, not a target
            }
            if (_container == null) {
                DiscardToGround(slot);
                return true;
            }
            TransferItemToContainer(slot);
            return true;
        }

        /// <summary>
        /// Discard an item onto the ground — <c>cmbinv_actor_drop_item_at_pos</c> (CMBINV.C:1047),
        /// reached when the screen was opened on a party member so there is no other container to
        /// transfer into.
        ///
        /// <para>Order follows the original: stamp the object the messages name, run the drop-only
        /// refusal gate, resolve the destination (an existing container on this spot, else a bag
        /// claimed from the zone's pool), narrate the spare bag, and only then move the item. The
        /// move itself is the shared <see cref="RunTransfer"/>, which already carries the guards
        /// this path shares with every other transfer and its own refusal messages.</para>
        /// </summary>
        private void DiscardToGround(int slot) {
            if (_displayed == null || slot < 0 || slot >= _displayed.Items.Count) {
                RenderCurrent();
                return;
            }

            RuntimeItem item = _displayed.Items[slot];
            // Stamped here as well as on the shared path: this gate refuses BEFORE reaching it, and
            // its refusal names the item too.
            _gameSession.SetDialogKeyObjectId(item.ObjectId);

            // The one gate the drop adds over a normal transfer: a record flagged un-discardable
            // refuses outright. (No shipped OBJINFO record sets it — it is honoured for overrides.)
            ObjectInfo rec = _gameSession.ObjectInfo?.GetById(item.ObjectId);
            if (rec != null && (rec.Flags & ObjectFlags.CannotBeDiscarded) != 0) {
                _dialogs?.ShowById(MustKeepEquippedDialogId).Forget();
                DeselectItem();
                RenderCurrent();
                return;
            }

            (int bakX, int bakY) = PartyGroundPosition();
            GroundDropTarget target = _gameSession.ResolveGroundDropTarget(
                _gameSession.CurrentZone, bakX, bakY);
            if (!target.Resolved) {
                // Neither a free slot nor a recyclable bag: the engine would spawn from a zeroed
                // header here, so keeping the item is the honest outcome. Not reachable with
                // shipped data, where every travel zone ships a pool.
                _logger.LogWarning(
                    "Zone {Zone} has no ground-bag slot to claim; discard refused, item kept.",
                    _gameSession.CurrentZone);
                DeselectItem();
                RenderCurrent();
                return;
            }

            if (target.Recycled) {
                _logger.LogInformation(
                    "Ground-bag pool exhausted in zone {Zone}: recycled the least-recently-touched bag.",
                    _gameSession.CurrentZone);
            }
            if (target.IsNewBag) {
                // "@ shoved the @1 into a spare bag." Played at spawn time, before the move — so it
                // narrates the bag appearing, not the transfer succeeding.
                _dialogs?.ShowById(SpareBagDialogId).Forget();
            }

            // *** SETTLE AFTER THE PICKER, NOT AFTER STARTING IT. *** A stack's transfer waits on
            // the quantity picker; settling straight away saw the bag still empty, freed it, and the
            // stack then moved into a Free record nothing indexes (TASK-507). The original runs the
            // picker inside cmbinv_actor_pickup_item and recomputes only after it returns.
            RunTransfer(slot, target.Container, allowShare: false)
                .ContinueWith(() => FinishGroundDrop(target.Container)).Forget();
        }

        /// <summary>
        /// Settle a ground bag after a discard: recompute the protected-item flag the recycler
        /// orders by (<c>cmbinv_recompute_has_weapon_flag</c>, CMBINV.C:1078 — the only place the
        /// original recomputes it, which is why looting a container does not), then either free a
        /// bag nothing landed in (the refused-transfer case) or show it in the world.
        /// </summary>
        private void FinishGroundDrop(RuntimeContainer bag) {
            _droppedBag = bag;
            GroundContainerPool.RecomputeHoldsProtectedItem(bag, _gameSession.ObjectInfo);
            if (SettleContainer(bag)) {
                return;
            }
            _groundBags?.SpawnAsync(bag).Forget();
        }

        /// <summary>
        /// Screen-exit settlement, the counterpart of <c>actorspawn_destroy_and_persist</c>: every
        /// world-container handler calls it the moment <c>cmbinv_inventory_screen_run</c> returns
        /// (WCURSOR.C:213/252/1054) for the container the screen was opened on, and CMBINV.C:454
        /// does the same for the bag a discard claimed while it was up. Both settle here.
        ///
        /// <para>Without this a bag whose contents the player picked up stayed on the ground for
        /// good: still claimed, so the pool slot never came back; still a <c>Bag</c> record, so
        /// every zone build respawned its entity and every save carried it; and un-openable,
        /// because the loot gate needs items.</para>
        /// </summary>
        protected override void OnBeforeHide() {
            // Answer whoever is waiting on the lock BEFORE the scratch goes, since dropping it is
            // what makes IsLockMode false. Exit and a picked-open pop both land here, so a caller
            // that gave up on the lock is answered rather than left waiting — see LockOutcomeAsync.
            _lockClosed?.TrySetResult(_lockOpened);
            // *** AND ANSWER AN UNANSWERED QUANTITY PICKER, FOR THE SAME REASON. *** It is the other
            // wait on this screen that only its own buttons could end, so a teardown while it was up
            // parked the continuation for ever -- leaving `_pickerOpen` true on a component that
            // outlives the screen, which deadens every gesture handler from then on. Cancelling
            // resolves it as "None", so the picker's own teardown and the caller's both run. See
            // ShowQuantityPickerAsync (TASK-568).
            _pickerAbandoned?.Cancel();
            _lockPicker = -1;
            // Drop the scratch container with the screen. It is assembled per lock and holds copies,
            // so keeping it would both leak a stale working set into the next open and leave
            // IsLockMode true for a screen that is no longer showing a lock.
            _lockScratch = null;
            _shop = null;
            _blessing = null;
            // *** THE HOURS ARE PAID ON THE WAY OUT, ALL AT ONCE. *** The original advances the
            // clock in a loop after its screen loop ends, not per repair — so the visit costs the
            // accumulated mask read as a number, and an event the advance raises lands after the
            // player has left the mender rather than over him.
            if (_menderTimeMask != 0) {
                // One event dialog for the whole batch (MODALSCR.C:669): a twelve-hour repair nags once.
                if (_upkeep != null) {
                    _upkeep.OneEventDialogPerBatch = true;
                }
                _clock?.AdvanceHours(ShopRepair.HoursFor(_menderTimeMask));
                if (_upkeep != null) {
                    _upkeep.OneEventDialogPerBatch = false;
                }
            }
            _menderTimeMask = 0;
            _mender = null;
            // Per-open, exactly like the three above: a fight's flag left standing would make the
            // next open from the world refuse the combat-only items it should now allow.
            InCombat = false;
            ClearActingCombatantPoison = null;
            CombatDistance = null;
            SettleContainer(_container);
            SettleContainer(_droppedBag);
        }

        /// <summary>
        /// Return <paramref name="container"/> to its zone's pool if it is a self-spawning bag with
        /// nothing left in it, and take its entity off the ground. Idempotent and safe for anything
        /// else — a corpse, chest or party inventory carries no self-spawn flag, so the original
        /// leaves it exactly where it is however empty it gets. True when the record was freed.
        /// </summary>
        private bool SettleContainer(RuntimeContainer container) {
            if (container == null || !_gameSession.ReleaseGroundBagIfEmpty(container)) {
                return false;
            }
            _groundBags?.Despawn(container);
            return true;
        }

        /// <summary>The party's fine world position — the engine drops at the camera's
        /// <c>nWorld_x</c>/<c>nWorld_y</c>, which is what the session's PositionX/PositionY hold.</summary>
        private (int X, int Y) PartyGroundPosition() =>
            (_gameSession.PositionX, _gameSession.PositionY);

        // Put a dragged/selected item back into the loot container the screen was opened on.
        // Countables still raise the quantity picker on this direction (record 0x8000 asks on
        // ANY transfer); Share never appears (the destination is not a member).
        private void TransferItemToContainer(int slot) {
            if (_container == null || _displayed == null || ReferenceEquals(_displayed, _container)
                || slot < 0 || slot >= _displayed.Items.Count) {
                RenderCurrent();
                return;
            }
            if (IsShopMode) {
                // *** A WORN SWORD IS NOT FOR SALE. *** Selling short-circuits here, ahead of the
                // shared transfer, so it has to ask the equipped guard itself — the original
                // refuses at cmbinv_actor_transfer_item's first branch, because a shop's container
                // is not a member's Inventory and so cannot take the swap arm. Without this the
                // port sold the sword off Locklear's back; the original answers "it would be utter
                // madness to strip himself of his defenses".
                RuntimeItem offered = _displayed.Items[slot];
                if (InventoryTransfer.NeverUnequips(offered,
                        _gameSession.ObjectInfo?.GetById(offered.ObjectId)?.ObjectType
                            ?? GameData.ObjectType.Misc,
                        _displayed.ContainerType)) {
                    _gameSession.SetDialogKeyObjectId(offered.ObjectId);
                    _dialogs?.ShowById(MustKeepEquippedDialogId).Forget();
                    DeselectItem();
                    RenderCurrent();

                    return;
                }

                // The mirror of a purchase: transferItem @0x55659 asks which SIDE carries the shop
                // block — the source means buy, the destination means sell.
                SellToShopAsync(slot).Forget();
                return;
            }
            RunTransfer(slot, _container, allowShare: false).Forget();
        }

        // Item-selection highlight only (the red box outline + maroon fill on a click-selected cell,
        // UI_DrawInventory @0x569bd). The portrait drag-hover no longer uses this — it draws the
        // HEADS.BMX circle instead (see ShowHoverCircle).
        private static void SetOutline(VisualElement e, bool on, bool fill) {
            if (e == null) {
                return;
            }
            // *** ONE VGA PIXEL, WHICH IS NOT ONE NUMBER. *** The original's highlight is a
            // draw_rect_filled with an outline PEN (0x8b over fill 0x8f), so its width is the
            // blitter's single pixel — and a single original pixel is 5 canonical across against 6
            // down. The old flat 8f was neither, and no flat value could have been both.
            int vga = on ? InventoryDragGesture.OutlineWidthVga : 0;
            e.style.borderTopWidth = vga * Canonical.VgaScaleY;
            e.style.borderBottomWidth = vga * Canonical.VgaScaleY;
            e.style.borderLeftWidth = vga * Canonical.VgaScaleX;
            e.style.borderRightWidth = vga * Canonical.VgaScaleX;
            e.style.borderTopColor = OutlineRed;
            e.style.borderBottomColor = OutlineRed;
            e.style.borderLeftColor = OutlineRed;
            e.style.borderRightColor = OutlineRed;
            if (fill) {
                e.style.backgroundColor = on ? SelectFill : Color.clear;
            }
        }

        // --- IActionHandler ---

        /// <summary>
        /// REQ button/hotspot clicks. Which of these clear the item selection is not uniform, and
        /// follows the original's action dispatch in <c>sub_ovr157_4E3</c> exactly: portraits
        /// (0x54A6F), Use (0x54C63) and the container window (0x54C22) each assign
        /// <c>selSlot = -1</c>; the gold readout and More Info deliberately do not, and Exit closes
        /// the screen anyway.
        /// </summary>
        public void PrimaryAction(int actionId) {
            switch (actionId) {
                case ButtonExit:
                    Close();
                    break;
                case PortraitSlot1:
                    DeselectItem();
                    SelectMember(0);
                    break;
                case PortraitSlot2:
                    DeselectItem();
                    SelectMember(1);
                    break;
                case PortraitSlot3:
                    DeselectItem();
                    SelectMember(2);
                    break;
                case WindowContainerImage:
                    ClickContainerWindow();
                    break;
                case ButtonMoreInfo:
                    ShowItemStats();
                    break;
                case ButtonUse:
                    // Button 22 is "Use" over a member's own pack and the next-page control on a
                    // shop that pages (0x56801) — one rect, two meanings, so the branch is here
                    // rather than in a second handler. On anything else the original clears the
                    // element's active flag, so the click does nothing at all: a chest's contents
                    // are not "usable" from the loot window.
                    if (ShelfPages) {
                        TurnShelfPage();
                    } else if (UsesUseButton) {
                        UseSelectedItem();
                    }
                    break;
                case ButtonGold:
                    // No deselect: the original's action-0x22 branch only plays the gold DDX.
                    ShowPartyMoney();
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// The money readout's own button, action 34 (<c>0x22</c>) — <c>sub_ovr157_4E3</c> @0x54b18,
        /// CMBINV.C:352-355. Two statements in the original, and the order of them is the point: it
        /// copies the purse into the <b>quoted-amount</b> global first, then plays DDX 1800034,
        /// whose <c>@0</c> is text-variable source 19 (the quoted amount) — <i>not</i> source 20
        /// (the purse). Wiring the dialog straight to the purse would read identically today and
        /// diverge the moment anything else quotes an amount, so the copy is kept.
        ///
        /// <para>Spec: <c>docs/specs/party-money-display.md</c> §3.1, §3.5.</para>
        /// </summary>
        private void ShowPartyMoney() {
            _gameSession.SetGlobalValue(DialogSlotPopulator.QuotedAmountGlobalKey,
                _gameSession.PartyGold);
            _dialogs?.ShowById(PartyMoneyDialogId).Forget();
        }

        /// <summary>Which page of the shelf is showing. Zero unless a shop is open.</summary>
        private int _shopPage;

        /// <summary>Whether this shelf has more stock than its six cells can hold.</summary>
        private bool ShelfPages =>
            ShowingShopShelf && GameData.Resources.Shop.ShopPaging.Pages(_displayed.Items.Count);

        /// <summary>The BICONS face of the next-page control (icon 0x67, set at 0x5680f).</summary>
        private const int ShopPageIcon = 0x67;

        /// <summary>
        /// Turn the shelf — <c>sub_ovr157_4E3</c> @0x54d0a.
        /// </summary>
        /// <remarks>
        /// One button, three meanings: plain forward (wrapping to the front), left shift back
        /// (stopping at the front rather than wrapping), right shift straight to the front.
        /// </remarks>
        private void TurnShelfPage() {
            UnityEngine.InputSystem.Keyboard keys = UnityEngine.InputSystem.Keyboard.current;
            GameData.Resources.Shop.ShopPaging.Turn turn =
                keys?.rightShiftKey.isPressed == true
                    ? GameData.Resources.Shop.ShopPaging.Turn.First
                    : keys?.leftShiftKey.isPressed == true
                        ? GameData.Resources.Shop.ShopPaging.Turn.Previous
                        : GameData.Resources.Shop.ShopPaging.Turn.Next;

            _shopPage = GameData.Resources.Shop.ShopPaging.Turned(
                _shopPage, _displayed.Items.Count, turn);
            DeselectItem();
            RenderCurrent();
        }

        /// <summary>
        /// Give button 22 the face that matches what it currently does — or no face at all.
        /// </summary>
        /// <remarks>
        /// <b>Three states, not two.</b> INVENTOR.C:356-376 activates this element for a member's
        /// OWN pack (action 22, sprite 34 — the hand) or for a shop holding more than one page
        /// (action 50, sprite 103 — the arrow), and in every other case clears
        /// <c>bActive_flag</c> and draws nothing. A loot container, a lock, and a shop with six or
        /// fewer items all fall in that third arm and show a bare stone disc.
        ///
        /// <para>We had only the first two, so the hand appeared on the picklock and loot screens —
        /// offering "Use" over a chest's contents, which is not a thing the original lets you do.
        /// Compared against it at the zone-1 chest, 2026-09-07.</para>
        ///
        /// <para>Restoring the authored icon on the way out still matters: this screen is a
        /// singleton, so a shop left paging would otherwise hand its arrow to the next inventory
        /// that opens.</para>
        /// </remarks>
        private void SyncShopPageButton() =>
            _ui?.SetEntryIcon(ButtonUse,
                UsesUseButton ? (int?)null
                : ShelfPages ? ShopPageIcon
                : NoIcon);

        /// <summary>
        /// Whether "Use" applies at all — only over a party member's own belongings.
        /// </summary>
        /// <remarks>
        /// The original's test is <c>actor-&gt;bResidence == RES_PARTY_SLOT</c>, i.e. the displayed
        /// container is a member's pack. A lock's scratch container is typed
        /// <see cref="SaveGameContainerType.SharedKeys"/> and a chest a world container, so neither
        /// qualifies.
        /// </remarks>
        private bool UsesUseButton =>
            _displayed?.ContainerType == SaveGameContainerType.Inventory;

        /// <summary>Clears the element's icon — <see cref="MenuIconLoader"/> treats it as "none".</summary>
        private const int NoIcon = -1;

        /// <summary>
        /// A right-click on one of the screen's buttons says what it does -- CMBINV.C:312-325.
        /// </summary>
        /// <remarks>
        /// The container slot (0x20) asks record 0x1b7751 with Var 0 = the kind the slot's image is
        /// drawn by (shop, key ring, anything else), and skips it when there is no such kind; every
        /// other button but the portraits (2..4), the purse (0x22) and 0x7f asks 0x1b775a with Var 0 =
        /// the sprite the button wears. Nothing answered a right-click here before.
        /// </remarks>
        public Awaitable SecondaryAction(int actionId) {
            if (actionId is >= 2 and <= 4 || actionId == 0x22 || actionId == 0x7f) {
                return default;
            }
            if (actionId == 0x20) {
                int kind = EffectiveContainerImageIndex;
                if (kind >= 0) {
                    ShowVar0Message(ContainerSlotHelpDialogId, kind);
                }
                return default;
            }
            if (_ui != null && _ui.TryGetIconBase(actionId, out int sprite)) {
                ShowVar0Message(ButtonHelpDialogId, sprite);
            }
            return default;
        }

        private const int ContainerSlotHelpDialogId = 1800017; // 0x1B7751
        private const int ButtonHelpDialogId = 1800026;        // 0x1B775A

        /// <summary>Switch the displayed grid to the active-party member behind portrait 0/1/2. A
        /// plain portrait click (no drag) does this; a drag-drop onto a portrait transfers instead.</summary>
        /// <remarks>
        /// <b>In lock mode a portrait picks WHO IS PICKING, and changes nothing on screen but the
        /// ring.</b> CMBINV.C has two portrait arms and they are not the same: the party-inventory
        /// one (:331-345) reassigns <c>actor</c> to that member's record, so the grid becomes their
        /// pack; the shop/picklock one (:409-423, guarded by <c>g_bInventoryShopMode</c>) does
        /// <c>memberIdx = target;</c> and nothing else. The displayed actor stays the picklock
        /// working set — swapping it out would take the party's tools off the screen the player is
        /// dragging them from.
        ///
        /// <para>The choice is not cosmetic: <c>memberIdx</c> is the <c>slot</c> that reaches
        /// <c>picklock_screen_handle_drop</c>, which reads that member's LockPicking and trains
        /// that member. See <see cref="PickerLockPicking"/>.</para>
        /// </remarks>
        private void SelectMember(int portraitSlot) {
            // *** IN A FIGHT THE PACK STAYS ON WHOEVER IS ACTING. *** CMBINV.C:344 gates the
            // switch arm on `(target != memberIdx) && (g_wInCombatMode == 0)`, so the original
            // refuses it outright mid-combat -- the member whose turn it is is the member whose
            // belongings you may rummage in. Ours had no combat gate at all (TASK-592).
            //
            // The Shift arm is NOT gated this way in the original (CMBINV.C:336-341 runs
            // charscreen_info_loop for that member either way), so if a secondary portrait action
            // is ever added here it must sit ABOVE this return rather than behind it.
            if (InCombat) {
                return;
            }

            if (IsLockMode) {
                if (CharacterIndexOf(portraitSlot) >= 0) {   // `target <= partySize`
                    _lockPicker = portraitSlot;
                    RenderCurrent();                          // moves the ring, keeps the tools
                }
                return;
            }

            RuntimeContainer inventory = ResolveMemberContainer(portraitSlot);
            if (inventory != null) {
                _displayed = inventory;
                NoteKeeperKind();
                // *** A MEMBER'S PACK CLEARS THE KEEPER KIND. *** CMBINV.C:63-67 zeroes
                // g_bIsRestEncounter as the first act of building a page and only re-raises it from
                // the displayed actor's SUBREC_EVENT_STATE — which a party member does not have — so
                // whatever page is up decides the word, not which shop is open. Measured at Joftaz's
                // in Silden on 2026-09-13: the original says "tavernkeeper" on a BUY, dragged off the
                // shelf, and "shopkeeper" on a SELL, dragged out of a member's pack. Ours said
                // tavernkeeper for both.
                RenderCurrent();
            }
        }

        /// <summary>
        /// Click (not drag) on the container window, action 32 — <c>sub_ovr157_4E3</c> @0x54BB7.
        /// The window does two different jobs depending on whether anything is selected:
        /// <list type="bullet">
        /// <item>an item IS selected → transfer it, same destination as a drop (0x54BC2);</item>
        /// <item>nothing selected → switch the view to the other inventory (0x54C08): the loot
        /// container, or the shared party inventory when the screen was opened from travel.</item>
        /// </list>
        /// Either way the selection is cleared afterwards (0x54C22).
        /// </summary>
        private void ClickContainerWindow() {
            if (_selectedSlot >= 0) {
                int slot = _selectedSlot;
                if (!DropOnContainerWindow(slot)) {
                    DeselectItem(); // refused, but the original clears the selection regardless
                }
                return;
            }
            // CMBINV.C:385-392 / 0x54C08: with a loot container it shows that; opened from travel
            // (no container) it shows the party's SHARED keys inventory instead. Either way the
            // switch is one-way — a portrait click is how you get back to a member.
            RuntimeContainer other = _container ?? _gameSession.SharedKeysInventory;
            if (other != null && !ReferenceEquals(_displayed, other)) {
                _displayed = other;
                NoteKeeperKind();
                // Back on the shop's own page, so the keeper gets its word again — the other half of
                // the rule in SelectMember. The shared keys inventory is nobody's establishment.
                RenderCurrent();
            }
        }

        /// <summary>
        /// Equip-by-drop (task-44/49, <c>invui_handleItemDrag</c> @0x57063 → <c>Use_Item</c>
        /// @0x58cbd). Returns true when the drop was consumed as an equip, false to let the caller
        /// fall through to its normal snap-back.
        ///
        /// <para>One direction only: dropping an item <i>onto</i> the paperdoll box equips it
        /// (category-keyed — <see cref="InventoryEquip.Equip"/> clears the old occupant back to
        /// the grid). <b>There is no unequip gesture</b>: the original has no "drag off the
        /// paperdoll" move (spec docs/specs/inventory-item-handling.md §2.4) — an equipped item
        /// leaves the paperdoll only by being transferred somewhere, and a melee weapon/staff not
        /// even then. An ineligible category never arms the original's pulse gate, so that drop
        /// falls through to the silent snap-back.</para>
        ///
        /// <para>Gated on the <i>displayed</i> container being a member inventory (the paperdoll
        /// is drawn whenever a member is viewed, including while a loot container is open —
        /// RenderCurrent keys the layout the same way).</para>
        /// </summary>
        private bool EquipOnPaperdollDrop(int slot, Vector2 stageLocal) {
            if (!IsEquipDropTarget(slot, stageLocal)) {
                return false; // pulse gate never armed in the original — silent snap-back
            }
            InventoryEquip.Equip(_displayed, slot, _gameSession.ObjectInfo); // idempotent re-equip
            RenderCurrent();
            return true;
        }

        /// <summary>
        /// Use-by-drop: the target-selection gesture, and the last of the drag handler's four drop
        /// branches (INVENTOR.C:793-806, the <c>else</c> after the portrait, container-window and
        /// paperdoll blocks). Dropping an item onto <b>another item in the same grid</b> uses the
        /// first on the second — poison a blade, fit a bowstring, pour manna into the staff. Returns
        /// true when the drop was consumed.
        ///
        /// <para>Three gates, all the original's: the screen must be showing a <b>party member</b>
        /// (<c>selected_slot == 0</c> bails — a corpse cannot use its own contents), the release
        /// must land on a different item cell, and the dragged item's category must be one of 8-12
        /// or 25 (<see cref="InventoryUse.CanUseOnAnotherItem"/>). Anything else snaps back
        /// silently; in particular a sword dragged onto a potion is neither a move nor a use.</para>
        ///
        /// <para>The use-gate chain runs first, exactly as it does for the Use button — the
        /// original reaches both through the same <c>itemuse_dispatch_on_target</c>, which opens
        /// with it. So a combat-only coating still refuses here rather than silently applying.</para>
        /// </summary>
        /// <summary>
        /// Try a dragged tool on the lock — <c>sub_ovr166_210</c> @0x5beb0.
        /// </summary>
        /// <remarks>
        /// <b>The two tools are not two mechanics with a shared skin.</b> Picklocks are a
        /// deterministic skill comparison; a key either fits the lock exactly or does not, and may
        /// snap trying. <see cref="PicklockAttempt"/> carries both, and
        /// <see cref="PicklockDrop"/> says which message follows and — the part that is easy to get
        /// wrong — where a broken tool has to be taken from.
        ///
        /// <para>Returns true whenever the drop was CONSUMED, including a failed attempt: a tool
        /// tried and rejected has still been used, and must not fall through to the portrait or
        /// paperdoll arms below.</para>
        /// </remarks>
        private bool DropOnLock(int slot, Vector2 stageLocal) {
            if (!IsLockMode || slot < 0 || slot >= _displayed.Items.Count) {
                return false;
            }
            if (!IsOverLock(stageLocal)) {
                return false;
            }

            RuntimeItem dropped = _displayed.Items[slot];
            bool usedPicklocks = dropped.ObjectId == LockPicking.LockpickObjectId;
            int skill = PickerLockPicking();

            PicklockAttempt.AttemptResult result;
            var skillAwarded = 0;
            if (usedPicklocks) {
                result = PicklockAttempt.WithLockpicks(
                    _lockDifficulty, skill, Roll100, out skillAwarded);
            } else {
                // A key's "value" is the lock it fits: object id minus the key base. A key for a
                // different lock simply does not match, which is the whole of the key mechanic.
                int keyValue = dropped.ObjectId - PicklockDrop.KeyObjectIdBase;
                result = PicklockAttempt.WithKey(keyValue, _lockDifficulty, skill, Roll100);
            }

            BakAgain.Audio.MenuSoundService.Instance?.Play(PicklockDrop.AttemptSound(usedPicklocks));
            ApplyLockOutcome(usedPicklocks, result, dropped.ObjectId, skillAwarded).Forget();

            return true;
        }

        private static int Roll100(int bound) => UnityEngine.Random.Range(0, bound);

        /// <summary>Whether a stage-local point is over the lock body.</summary>
        /// <remarks>
        /// The BODY, not the latch: the latch is decoration hanging above it, and treating it as
        /// part of the target would accept drops on empty panel above the lock.
        /// </remarks>
        private bool IsOverLock(Vector2 stageLocal) {
            VisualElement layer = _stage?.Q<VisualElement>(LockLayerName);
            if (layer == null || layer.childCount < 2) {
                return false;
            }

            // [0] is the latch, [1] the body — the order DrawLockAsync adds them in.
            Rect body = layer[1].layout;

            return body.Contains(stageLocal);
        }

        /// <summary>
        /// Applies a completed attempt: the message, the breakage, the skill, and the opening.
        /// </summary>
        /// <remarks>
        /// The order is the original's and it is all pacing: the attempt cue has already played,
        /// then <see cref="PicklockDrop.VerdictDelaySeconds"/> of nothing, THEN the verdict — its
        /// own cue, the opening animation, and only then the message. Doing any of it before the
        /// pause collapses the whole thing onto the frame the tool was dropped.
        ///
        /// <para>The lock opens last of all, because popping the screen first would take the
        /// message down with it.</para>
        /// </remarks>
        private async UniTaskVoid ApplyLockOutcome(bool usedPicklocks,
            PicklockAttempt.AttemptResult result, int droppedObjectId, int skillAwarded) {
            await UniTask.Delay(System.TimeSpan.FromSeconds(PicklockDrop.VerdictDelaySeconds),
                DelayType.DeltaTime);
            if (!IsLockMode) {
                return; // the screen was left during the pause
            }

            ApplyLockBreakage(usedPicklocks, result, droppedObjectId);
            AwardLockPickingSkill(skillAwarded);
            RenderCurrent();

            bool opened = result == PicklockAttempt.AttemptResult.Opened;
            if (opened && !usedPicklocks) {
                RememberKeyLock();
            }
            if (opened) {
                BakAgain.Audio.MenuSoundService.Instance?.Play(
                    PicklockDrop.OpenedSound(usedPicklocks));
                await OpenLatchAsync();
            } else if (result == PicklockAttempt.AttemptResult.ToolBroke) {
                BakAgain.Audio.MenuSoundService.Instance?.Play(PicklockDrop.BrokeSound);
            }

            await _dialogs.ShowById(PicklockDrop.DialogFor(usedPicklocks, result));

            if (opened) {
                _lockOpened = true;
                await _navigator.Pop();
            }
        }

        /// <summary>
        /// Remember that this lock has been opened with its key (0x5bf35).
        /// </summary>
        /// <remarks>
        /// <b>A key success only, never a pick.</b> The flag is not "this lock is open" — that is
        /// the container's business and dies with the runtime. It is the party having MET this kind
        /// of lock and learned which key it takes, which is why it is a global and survives a save,
        /// and why picking one teaches nothing.
        ///
        /// <para>A difficulty that matches no named lock writes global 7260, the no-match slot. The
        /// original does that too rather than skipping the write, and the same slot is read back on
        /// examine, so the pair stays consistent.</para>
        /// </remarks>
        private void RememberKeyLock() =>
            _gameSession.SetGlobalValue(
                GameData.Resources.Character.KeyLocks.OpenedGlobal(
                    GameData.Resources.Character.KeyLocks.NumberFor(_lockDifficulty)),
                1);

        /// <summary>
        /// Looking the lock over — <c>sub_ovr166_417</c> @0x5c0b7, action 127.
        /// </summary>
        /// <remarks>
        /// Two different answers, and which one you get depends on whether the party has opened
        /// this kind of lock with its key before. If they have, the lock is NAMED and the message
        /// counts how many of its key they are carrying; if not, it is only weighed against the
        /// party's best lockpicking — and past 100 not even that, because the skill stops being
        /// consulted at all.
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTaskVoid ExamineLockAsync() {
            int lockNumber = GameData.Resources.Character.KeyLocks.NumberFor(_lockDifficulty);
            if (_gameSession.GetGlobalValue(
                    GameData.Resources.Character.KeyLocks.OpenedGlobal(lockNumber)) != 0) {
                int keyId = GameData.Resources.Character.PicklockDrop.KeyObjectIdFor(lockNumber);
                _gameSession.SetDialogKeyObjectId(keyId);
                _gameSession.SetGlobalValue(MessageSelectorGlobal, CountAcrossParty(keyId));
                await _dialogs.ShowById(LockRecognisedDialog);

                return;
            }

            _gameSession.SetGlobalValue(MessageSelectorGlobal,
                (int)GameData.Resources.Character.KeyLocks.Assess(
                    _lockDifficulty, BestPartyLockPicking()));
            await _dialogs.ShowById(LockExaminedDialog);
        }

        /// <summary>How many of an object the whole party is carrying.</summary>
        /// <remarks><c>CountItemInWholeParty</c> — every active member's pack, not the shared
        /// keys container and not the displayed one.</remarks>
        private int CountAcrossParty(int objectId) {
            var total = 0;
            byte[] roster = _gameSession.ActivePartyIndices ?? System.Array.Empty<byte>();
            for (var slot = 0; slot < roster.Length; slot++) {
                total += InventoryQuery.CountByKind(
                    _gameSession.GetActorInventory(roster[slot]), objectId);
            }

            return total;
        }

        /// <summary>"You have seen this lock before" — it names the key and counts them.</summary>
        private const int LockRecognisedDialog = 1800042;

        /// <summary>The plain assessment, chosen by <see cref="KeyLocks.Assessment"/>.</summary>
        private const int LockExaminedDialog = 1800043;

        /// <summary>Swings the latch open — <c>UI_DrawLock(bIsOpen: 1)</c>.</summary>
        /// <remarks>
        /// Only the LATCH moves; the body is fixed. The original redraws the whole panel each pass
        /// because it is blitting to a framebuffer — here the latch element is simply moved, which
        /// is the same picture without the clear.
        ///
        /// <para>One frame per pass, as the original had: it steps as fast as it can draw, with no
        /// timer of its own.</para>
        /// </remarks>
        private async UniTask OpenLatchAsync() {
            VisualElement layer = _stage?.Q<VisualElement>(LockLayerName);
            if (layer == null || layer.childCount < 1) {
                return;
            }

            VisualElement latch = layer[0]; // [0] is the latch — the order DrawLockAsync adds in
            foreach (int offset in PicklockWorkingSet.OpeningLatchOffsets()) {
                latch.style.top = (PicklockWorkingSet.LatchVgaY - offset)
                    * BakAgain.Graphics.Canonical.VgaScaleY;
                await UniTask.NextFrame();
            }
        }

        /// <summary>Set when an attempt succeeded, so the caller can open what was locked.</summary>
        public bool LockOpened => _lockOpened;

        private bool _lockOpened;

        /// <summary>Portrait slot of the member picking this lock, or -1.</summary>
        private int _lockPicker = -1;

        /// <summary>
        /// "Shall we try to open it?" — the question the lock screen asks before it opens.
        /// </summary>
        /// <remarks>
        /// Lives here rather than in the three world handlers because the original asks it from
        /// inside <c>picklock_screen_run</c>, so chest, door, ladder and locked NPC all get the
        /// same prompt from one place — and because the picker lookup it names is already here.
        ///
        /// <para>Call it BEFORE <see cref="SetLock"/>: the original asks first and assembles the
        /// working set afterwards, so a party with nothing to try is still asked and only then
        /// told so.</para>
        /// </remarks>
        public async Cysharp.Threading.Tasks.UniTask<bool> AskToOpenLockAsync(
            GameData.Resources.Character.LockPicking.LockContext context) {
            if (_dialogs == null) {
                return true;   // a bare harness has no dialog layer to answer with
            }

            // Which wording the prompt uses. `nEvtArgCount = mode` in picklock_screen_run.
            _gameSession.SetGlobalValue(DialogArgCountGlobal, (int)context);

            // *** THE PROMPT DOES NOT NAME THE PICKER, THOUGH IT LOOKS AS IF IT DOES. *** Its "@0"
            // is text SLOT 0, and the record carries no SetTextVariable op, so the slot keeps the
            // standing default the engine seeds every play with — a random party member who is not
            // the chapter speaker (DIALOG.C:789-792, DialogSlotPopulator.CreateForPlay). The name is
            // meant to vary. It read "Owyn" in both games on 2026-09-07 and that was chance; I filed
            // it as a defect before tracing the slot.
            //
            // nEvtArgActor0 is still published because the original publishes it here, and a record
            // reached from this one can read it as a bare "@" — it is just not what this line shows.
            int slot = BestLockPicker().Slot;
            byte[] roster = _gameSession.ActivePartyIndices ?? System.Array.Empty<byte>();
            if (slot >= 0 && slot < roster.Length) {
                _gameSession.EventActor = roster[slot];
            }

            return await _dialogs.ShowConfirmById(
                GameData.Resources.Character.PicklockWorkingSet.AskToOpenDialog);
        }

        /// <summary>
        /// Whether the lock was opened, answered when the picklock screen actually CLOSES.
        /// </summary>
        /// <remarks>
        /// <b><see cref="BakAgain.UI.Navigation.IScreenNavigator.Push"/> returns once the screen is
        /// SHOWN, not once it is dismissed.</b> Three handlers read <see cref="LockOpened"/> on the
        /// line after awaiting the push, which is a frame or two after the screen appeared and long
        /// before the player has touched anything — so it was always false, and a lock the party
        /// picked open left its chest locked, its door shut and its crossing barred. The screen
        /// looked right and said the right thing; nothing downstream of it ever ran.
        ///
        /// <para>The outcome is published from <c>OnBeforeHide</c>, so leaving by Exit answers
        /// false rather than hanging the caller.</para>
        /// </remarks>
        public UniTask<bool> LockOutcomeAsync() =>
            _lockClosed?.Task ?? UniTask.FromResult(false);

        private UniTaskCompletionSource<bool> _lockClosed;

        /// <summary>Takes a snapped tool out of the world, which is NOT the same place for both.</summary>
        /// <remarks>
        /// The rule — and the reason the dropped id has to be carried this far rather than looked
        /// up again — is <see cref="PicklockDrop.ApplyBreakage"/>. This only supplies the party.
        /// </remarks>
        private void ApplyLockBreakage(bool usedPicklocks, PicklockAttempt.AttemptResult result,
            int droppedObjectId) {
            PicklockDrop.ApplyBreakage(usedPicklocks, result, droppedObjectId,
                _lockScratch, _gameSession.SharedKeysInventory, PartyPacks(),
                _gameSession.ObjectInfo.GetById);
        }

        private System.Collections.Generic.IEnumerable<RuntimeContainer> PartyPacks() =>
            _gameSession.ActivePartyPacks;

        /// <summary>Awards LockPicking to the member who actually did the picking.</summary>
        /// <remarks>
        /// <see cref="PickerCharacterIndex"/> — the ringed member, the same one whose skill the
        /// attempt was judged against. Not the party's best: PICKLOCK.C:150/162 trains
        /// <c>characters[nEvtArgActor0]</c>, and :110 set that from the <c>slot</c> the screen
        /// handed down, i.e. the selected portrait.
        ///
        /// <para><b>The amount is a number of USES, not points.</b> PICKLOCK.C:150/162 passes it to
        /// <c>stat_combatant_modify</c> with mode <b>3</b> — skill-use advancement, where the delta
        /// is scaled by a per-skill rate that slides as the value climbs, so a skill near 100 barely
        /// moves. Adding it to the stored value instead handed out two whole points for opening one
        /// chest. The amounts themselves (2 on success, 1 on a 40% roll after failure) were already
        /// right in <see cref="PicklockAttempt"/>; only the application was not.</para>
        /// </remarks>
        private void AwardLockPickingSkill(int amount) {
            if (amount <= 0) {
                return;
            }

            int character = PickerCharacterIndex();
            if (character < 0) {
                return;
            }

            // Through ModifyStatOf so the sheet mark follows the change (TASK-611).
            _gameSession.ModifyStatOf(character, GameData.ActorAttribute.LockPicking, amount,
                GameData.Resources.Character.StatChangeMode.SkillUse,
                _gameSession.StudyBonusFor(character, GameData.ActorAttribute.LockPicking));
        }

        /// <summary>The character index of the member picking this lock, or -1.</summary>
        private int PickerCharacterIndex() => CharacterIndexOf(System.Math.Max(0, _lockPicker));

        /// <summary>
        /// The LOCKPICKING OF THE MEMBER ON THE RING, which is what an attempt is judged against.
        /// </summary>
        /// <remarks>
        /// <c>stat_actor_get(gstate_party_member_record(slot - 1), 0xd, 0)</c>, PICKLOCK.C:107 —
        /// mode 0, so a wounded picker is a worse picker. <b>Not the party's best</b>: the screen
        /// only <i>starts</i> on the best (<see cref="BestLockPicker"/> seeds
        /// <see cref="_lockPicker"/> in <see cref="SetLock"/>) and the player may hand the job to
        /// someone else by clicking their portrait. Using the best regardless meant the choice did
        /// nothing, and every lock was picked by whoever was already good at it.
        ///
        /// <para><see cref="BestPartyLockPicking"/> is still right for <see cref="ExamineLockAsync"/>
        /// — <c>picklock_inv_info_query_disp</c> sizes the lock up against the party, not the
        /// picker. Two questions, two readings.</para>
        /// </remarks>
        private int PickerLockPicking() {
            int character = PickerCharacterIndex();
            return character < 0
                ? 0
                : _gameSession.EffectiveStat(character, GameData.ActorAttribute.LockPicking);
        }

        /// <summary>The party's BEST LockPicking — not the leader's, and not the displayed member's.</summary>
        /// <remarks><c>getHighestValueInParty(LockPicking)</c>, which the screen resolves once up front.</remarks>
        private int BestPartyLockPicking() => BestLockPicker().Skill;

        /// <summary>
        /// The party's best lock-picker: which portrait, and how good.
        /// </summary>
        /// <remarks>
        /// <b>The screen shows WHO is picking.</b> <c>picklock_screen_run</c> opens with
        /// <c>stat_party_find_extreme(0x0d, 0, &amp;memberIdx)</c> and hands that member's party
        /// slot to the inventory screen as its <c>idx</c>, which is what puts the steady ring on
        /// their portrait. Our screen showed no ring at all, because a lock displays the scratch
        /// container and the ring is derived from the displayed container's owner.
        ///
        /// <para>First max wins a tie, matching the <c>&gt;</c> the original's search uses.</para>
        /// </remarks>
        private (int Slot, int Skill) BestLockPicker() {
            // *** ONE SEARCH, AND IT IS THE EFFECTIVE VALUE. *** This used to walk the roster itself
            // comparing `ActorStat.Base`, while PICKLOCK.C:107 reads
            // `stat_actor_get(member, 0xd, 0)` — mode 0, so a wounded picker is a worse picker.
            // GameSession.PartyExtreme is the original's own primitive
            // (`stat_party_find_extreme(0x0d, 0, &memberIdx)`), so asking it keeps the tie-break and
            // the reading in one place (TASK-477).
            int skill = _gameSession.PartyExtreme(GameData.ActorAttribute.LockPicking,
                out int member);
            byte[] roster = _gameSession.ActivePartyIndices ?? System.Array.Empty<byte>();
            var bestSlot = -1;
            for (var slot = 0; slot < roster.Length; slot++) {
                if (roster[slot] == member) {
                    bestSlot = slot;
                    break;
                }
            }

            return (bestSlot, member >= 0 ? skill : 0);
        }

        private bool UseOnItemDrop(int slot, Vector2 stageLocal) {
            if (_displayed?.ContainerType != SaveGameContainerType.Inventory
                || slot < 0 || slot >= _displayed.Items.Count) {
                return false;
            }
            int target = SlotAt(stageLocal);
            if (target < 0 || target == slot || target >= _displayed.Items.Count) {
                return false;
            }
            ObjectInfo obj = ObjectAt(slot);
            if (obj == null || !InventoryUse.CanUseOnAnotherItem(obj.ObjectType)) {
                return false;
            }
            DeselectItem(); // the original drops the selection on this branch either way
            if (RefuseUse(obj, _displayed.Items[slot])) {
                RenderCurrent();
                return true;
            }
            // Always redraw: the drag dimmed the origin cell to 0.25, and only a re-render puts it
            // back — so even the outcomes that change nothing have to repaint.
            ApplyUseResult(
                InventoryUse.Use(_displayed, slot, target, _gameSession.ObjectInfo, BuildUseContext()),
                slot, redrawAnyway: true);
            return true;
        }

        /// <summary>
        /// Say what the dispatch's outcome says and redraw what it changed — the common tail of
        /// <c>Use_Item</c> (ITEMUSE.C:485-489), whose records are all Var-0 branching roots.
        ///
        /// <para>A category the remake has no branch for stays <b>silent</b>: the original would
        /// have healed, lit or buffed something, so "nothing happens" would be a visible lie. The
        /// log names the object so the gap is findable (spec §17.2 lists the blocker per
        /// category).</para>
        /// </summary>
        /// <summary>
        /// The item's OWN use cue, off its record — <c>itemuse_dispatch_on_target</c>'s tail.
        /// </summary>
        /// <remarks>
        /// <b>Data, not a per-item rule.</b> 30 of the 138 shipped items carry a sound, and until
        /// now the only one that ever played was the tavern drink. One call covers all of them.
        ///
        /// <para><b>A use that achieved nothing is silent</b> — the original returns on outcome 0
        /// before it reaches the cue, which is what makes the sound mean "that worked".</para>
        ///
        /// <para><b>The stored count is EXTRA repeats</b> — the Armorer's Hammer strikes three
        /// times, the Whetstone passes twice — so it is passed through rather than dropped, and
        /// <see cref="Audio.MenuSoundService"/> sequences them. Firing the cue N times in one frame
        /// would take N pool players and sound them together: one louder strike, not three.</para>
        /// </remarks>
        private void PlayItemUseCue(ItemUseOutcome outcome, int slot) {
            if (!ItemUseSound.Sounds(outcome)) {
                return;
            }
            ObjectInfo info = ObjectAt(slot);
            if (info != null && info.SoundId != 0) {
                Audio.MenuSoundService.Instance?.Play(info.SoundId, info.SoundRepeat);
            }
        }

        private void ApplyUseResult(ItemUseResult result, int slot, bool redrawAnyway = false) {
            // *** THE ONE PLACE THE MODIFIER SLOTS GET WRITTEN BACK. *** BuildUseContext hands the
            // dispatch a COPY of the acting character's eight slots (the block is flat, so a
            // per-character view cannot be a live array), and both Use() call sites funnel through
            // here — so this is the single commit rather than two that could drift apart. It is a
            // no-op unless something actually changed.
            if (_pendingUseSlots != null && _pendingUseCharacter >= 0) {
                _gameSession.CommitStatModifierSlots(_pendingUseCharacter, _pendingUseSlots);
                _pendingUseSlots = null;
                _pendingUseCharacter = -1;
            }

            PlayItemUseCue(result.Outcome, slot);

            if (result.RaisesCameraOnClose) {
                _gameSession.CameraLiftRequested = true;   // the travel screen runs it once this closes
            }
            if (result.Outcome == ItemUseOutcome.NotPorted) {
                _logger.LogInformation(
                    "Use of object {Object} (category {Category}) has no ported dispatch branch yet "
                    + "— see docs/specs/inventory-item-handling.md §17.2 (task-16).",
                    _displayed.Items[slot].ObjectId, ObjectAt(slot)?.ObjectType);
            } else if (result.DialogId == GameData.Resources.Inventory.NoteMapView.MapShownDialogId
                    && _riftMap != null) {
                // The map is not a message with a picture attached — it is the picture, with the
                // message held over it. NoteMapView says which map; the screen shows that dialog
                // itself and closes when it does, which is the original's shape (it never opened a
                // navigable screen for this).
                ShowMapAsync(result.PrefaceDialogId, result.DialogVar0).Forget();
            } else if (result.OpensSpyglassView && LocatorMap != null) {
                // The text first, then the view, as ITEMUSE.C:400-401 plays them.
                LookThroughSpyglassAsync(result.DialogId, result.DialogVar0).Forget();
            } else if (result.DialogId != 0) {
                if (result.MusicTrack != GameData.Resources.Audio.MusicPlayback.QueryOnly
                        && _midi != null) {
                    ShowVar0MessageOverTrackAsync(result.DialogId, result.DialogVar0,
                        result.MusicTrack).Forget();
                } else {
                    ShowVar0Message(result.DialogId, result.DialogVar0);
                }
            }
            if (redrawAnyway || (result.Outcome != ItemUseOutcome.NoEffect
                    && result.Outcome != ItemUseOutcome.NotPorted)) {
                RenderCurrent();
            }
        }

        /// <summary>
        /// The original's <b>one</b> equip gate: cursor inside the paperdoll box AND
        /// <c>cmbinv_member_can_equip_cat</c> says this member may wear that category, on a
        /// displayed member inventory (INVENTOR.C:696-700). It answers two questions with the same
        /// code, exactly as the original does — whether the box pulses under a live drag, and
        /// whether the release equips — so the highlight can never promise a drop that then
        /// refuses.
        /// </summary>
        private bool IsEquipDropTarget(int slot, Vector2 stageLocal) {
            if (_displayed?.ContainerType != SaveGameContainerType.Inventory
                || slot < 0 || slot >= _displayed.Items.Count
                || !MemberEquip.IsPaperdollDrop(stageLocal, _paperdollFill, Layout.PaperdollBox)) {
                return false;
            }
            ObjectInfo obj = ObjectAt(slot);
            return obj != null
                && MemberEquip.CanEquipCategory(obj.ObjectType, IsDisplayedMemberCaster());
        }

        /// <summary>
        /// Show or hide the paperdoll box's drag-time pulse (<c>invui_portr_panel_fill_pulsing</c>
        /// @0x563D6). The original strokes the box every drag frame and picks pen 0 — invisible —
        /// unless <see cref="IsEquipDropTarget"/> holds, so "wanted" here is that same gate.
        /// </summary>
        private void UpdatePaperdollBorder(bool dragging, int slot, Vector2 stageLocal) {
            bool wanted = dragging && IsEquipDropTarget(slot, stageLocal);
            if (!wanted || _paperdollFill == null) {
                _paperdollBorder?.RemoveFromHierarchy();
                _paperdollBorder = null;
                return;
            }
            if (_paperdollBorder == null) {
                _paperdollBorder = new VisualElement {
                    name = "paperdoll_pulse_border",
                    pickingMode = PickingMode.Ignore,
                    style = {
                        position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0,
                        // Same hairline widths as the container window's border: both are the
                        // original's 1px stroke, and the design frame is anisotropic.
                        borderLeftWidth = Layout.ContainerBorderWidthX,
                        borderRightWidth = Layout.ContainerBorderWidthX,
                        borderTopWidth = Layout.ContainerBorderWidthY,
                        borderBottomWidth = Layout.ContainerBorderWidthY,
                    },
                };
                _paperdollFill.Add(_paperdollBorder);
            }
            ApplyPaperdollBorderPen();
        }

        private void ApplyPaperdollBorderPen() {
            if (_paperdollBorder == null) {
                return;
            }
            Color c = BakAgain.Graphics.PaletteColors.ResolvePen(
                _inventoryPalette, PulsePen(_pulsePhase), OutlineRed);
            _paperdollBorder.style.borderLeftColor = c;
            _paperdollBorder.style.borderRightColor = c;
            _paperdollBorder.style.borderTopColor = c;
            _paperdollBorder.style.borderBottomColor = c;
        }

        private void TransferItemTo(int slot, int portraitSlot) {
            RuntimeContainer target = ResolveMemberContainer(portraitSlot);
            if (target == null || target == _displayed || slot < 0 || slot >= _displayed.Items.Count) {
                RenderCurrent(); // invalid drop → just restore the grid
                return;
            }
            // *** THE REFUSALS NAME BOTH THE ITEM AND THE MEMBER, SO STAMP THEM ON THE DROP. ***
            // The original does it while the item is still being dragged: nEvtArgItemId is the
            // dragged item (INVENTOR.C:653) and nEvtArgActor1 the member under the cursor (:682),
            // and a source that is not a party member's own pack makes that member the primary
            // actor as well (:684). Without them "@0 waved off the @1" printed as "Locklear waved
            // off the ." — the save's stale actor and a hole where the item should be.
            _gameSession.SetDialogKeyObjectId(_displayed.Items[slot].ObjectId);
            // Remember what is flying where, for the drop animation that plays if this succeeds.
            // Read HERE because the transfer mutates both containers and re-renders the grid.
            _dropPortraitSlot = portraitSlot;
            _dropObject = ObjectAt(slot);
            _dropItemFlags = _displayed.Items[slot].ItemFlags;
            int receiver = CharacterIndexOf(portraitSlot);
            if (receiver >= 0) {
                _gameSession.SetDialogSecondaryActorId(receiver);
                if (_displayed.ContainerType != GameData.Resources.Data.SaveGameContainerType.Inventory) {
                    _gameSession.EventActor = receiver;
                }
            }

            // *** A TRANSFER OUT OF A SHOP IS A PURCHASE. *** transferItem checks whether the
            // SOURCE container carries a shop block and hands off to BuyItem when it does
            // (0x55673-0x5568d), so buying is not its own gesture — dragging an item to a member
            // is the same act as taking loot, and the shop-ness of the source is what changes what
            // it means. That is why this hangs off the ordinary portrait drop.
            //
            // The check is on the SHELF, not on the screen: once selling let the player switch to a
            // member's pack without leaving the shop, gating on shop MODE turned handing an item to
            // a companion into a purchase of the party's own goods.
            if (ShowingShopShelf) {
                BuyForAsync(slot, target, portraitSlot).Forget();

                return;
            }

            // *** MID-FIGHT, ONLY A NEIGHBOUR CAN TAKE IT. *** INVENTOR.C:735-738 refuses a give to
            // a member more than one cell from the giver (from a pack that is not a member's, only
            // the acting member counts as near).
            // From the Nightfingers pack (bResidence 7) a far member is not a drop target at all
            // (INVENTOR.C:676-679 zero target_slot mid-drag), so it drops silently, no line.
            if (InCombat && CombatDistance != null && receiver >= 0
                && CombatDistance(DisplayedMemberIndex(), receiver) > 1) {
                if (DisplayedMemberIndex() >= 0) {
                    _dialogs?.ShowById(TooFarInCombatDialogId).Forget();
                }
                RenderCurrent();
                return;
            }
            // *** A HELD ITEM CANNOT BE STOLEN. *** INVENTOR.C:739-740: mid-fight, from a pack that is
            // not a member's, an equipped item is refused. The only such pack in a fight is the one
            // Nightfingers opened (bResidence 7), whose line is 0x1b7769; 0x1b774d, the other arm,
            // needs a non-member, non-combatant pack open mid-fight, which nothing raises.
            if (InCombat && _displayed.ContainerType != GameData.Resources.Data.SaveGameContainerType.Inventory
                && (_displayed.Items[slot].ItemFlags & (ushort)GameData.ItemFlags.Equipped) != 0) {
                _dialogs?.ShowById(CannotStealHeldItemDialogId).Forget();
                RenderCurrent();
                return;
            }

            // Share is offered when the destination is a party member and not in combat
            // (pickupItem's picker argument).
            RunTransfer(slot, target, allowShare: true).Forget();
        }

        /// <summary>
        /// Offers an item at this shop's price and, if the player accepts, sells it to them.
        /// </summary>
        /// <remarks>
        /// The offer is a real question: <c>BuyItem</c> seeds the quoted price into global 30014,
        /// shows ddx 1800023 ("That will be @1") and reads global 260 for the answer. Declining is
        /// where haggling begins — <b>not implemented yet</b>, so a refusal simply ends the offer
        /// rather than opening the haggle loop (ddx 1800020/1800021, and a Magical Scroll's
        /// suppliers who will not haggle at all, ddx 1800031).
        ///
        /// <para>The money and the stock are <see cref="ShopStock.Buy"/>'s business, including the
        /// rule that infinite stock is COPIED rather than moved.</para>
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTaskVoid BuyForAsync(int slot, RuntimeContainer target,
            int portraitSlot) {
            if (slot < 0 || slot >= _displayed.Items.Count) {
                RenderCurrent();

                return;
            }

            RuntimeItem item = _displayed.Items[slot];
            GameData.Resources.Object.ObjectInfo info = _gameSession.ObjectInfo?.GetById(item.ObjectId);
            if (info == null) {
                RenderCurrent();

                return;
            }

            // The transaction is a LOOP, not a single question (0x5b79f..0x5b987): a won haggle
            // re-asks at the new price, and only accepting, declining, or a shopkeeper who has had
            // enough ends it.
            while (true) {
                long price = PriceOf(item, info);
                if (price < 0) {
                    break;   // no longer for sale — a failed haggle can do this mid-transaction
                }

                _gameSession.SetGlobalValue(InnStay.PriceGlobal, (int)price);
                // *** RUN THE OFFER, THEN READ WHICH ANSWER IT LEFT. *** The accept branch is not
                // the end of it: 1800023 goes on to a Var 3 gate and, in chapter 1, to
                // base:ddx:dial_z18:91897 — *"It's yours," the @0 said cheerily.* ShowChoiceById
                // stops at the first entry and answers which button was pressed, so that line was
                // never spoken and an accepted purchase went straight to the quantity picker.
                // Measured at the Battleworks, Highcastle, 2026-09-13: the original says it, we did
                // not. Same defect as the mender's quote and the priest's offer (TASK-492).
                //
                // The flags are latched by the button itself (CreateMenuEntriesFromDialogData
                // clears all three candidates, the pick sets one), which is how the original reads
                // the answer back — GetGlobalValue(260) and GetGlobalValue(262) by number.
                await _dialogs.ShowById(ShopOfferDialog);
                int answer = _gameSession.GetGlobalValue(OfferAccepted) != 0 ? OfferAccepted
                    : _gameSession.GetGlobalValue(OfferHaggle) != 0 ? OfferHaggle : 0;
                if (answer == OfferAccepted) {
                    await CompletePurchaseAsync(item, info, target, portraitSlot, price);

                    break;
                }

                if (answer != OfferHaggle || !await HaggleForAsync(item, info, price, portraitSlot)) {
                    break;   // declined outright, or the shopkeeper closed the negotiation
                }
            }

            DeselectItem();
            RenderCurrent();
        }

        /// <summary>
        /// Handing over the money, and whatever that turns out to mean.
        /// </summary>
        /// <remarks>
        /// <b>Not every purchase puts something in a pack.</b> Three of the tavern's goods are
        /// special-cased inside <c>BuyItem</c> and all three are decided by object id, so the shop
        /// itself knows nothing about them — see <see cref="GameData.Resources.Shop.ShopPurchase"/>.
        ///
        /// <para>The gold moves FIRST and is handed back if the drinker turns out to have had
        /// enough, which is the original's order (0x5b7df deducts, 0x5b80e refunds) and the reason
        /// the refusal reads as a refund rather than as a purchase that never happened.</para>
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTask CompletePurchaseAsync(RuntimeItem item,
            GameData.Resources.Object.ObjectInfo info, RuntimeContainer target, int portraitSlot,
            long price) {
            if (_gameSession.PartyGold < price) {
                // The same guard ShopStock.Buy applies to everything it moves — and, as at the
                // temple, the shopkeeper's own refusal (base:ddx:dial_z18:91849 gates the buy on
                // Var 3) never gets to play, so this reads as a dead click. TASK-406.
                return;
            }

            if (GameData.Resources.Shop.ShopPurchase.IsCounterDrink(item.ObjectId)) {
                await DrinkAtCounterAsync(info, portraitSlot, price);

                return;
            }

            RuntimeItem delivered = GameData.Resources.Shop.ShopPurchase.Delivered(item);
            // *** THE WHOLE ROOM QUESTION, NOT JUST THE SLOT BUDGET. *** This asked CanFit, which
            // knows nothing about stacking, so a member at the budget was refused an item they
            // ALREADY carry a stack of — which costs no new slot and which the original allows
            // (canMergeIntoExistingStack @0x552F9). Measured at Romney 2026-09-23: actor2 at 20/20
            // slots holding 2 Rations ran the whole offer and then kept its gold, while actor4 at
            // 18/20 bought the same stack fine. HasRoomFor is the classification Plan already used,
            // so the buy path now asks the same question as every other transfer (TASK-625).
            if (!InventoryTransfer.HasRoomFor(target, delivered, _gameSession.ObjectInfo)) {
                // A shop drop is an ordinary transfer in the original, and a transfer that finds no
                // room plays 0x1b7748 keyed on the destination kind (INVENTOR.C:750). Without this
                // an accepted offer is a dead click: the gold does not move, the pack does not
                // change and the shopkeeper says nothing. TASK-413.
                ShowVar0Message(NoRoomDialogId, target != null ? (int)target.ContainerType : 0);

                return;
            }

            // *** THE ORIGINAL ASKS HOW MANY, AND OFFERS SHARE. *** Measured at Romney's Port
            // Exchange 2026-09-12: accepting an offer for the Quarrels (25) says "It's yours" and
            // then raises "Select amount: Give: 25 (All) / Share with party". Delivering the stack
            // unasked is not merely a missing prompt — sharing is the only way to split a countable
            // across the members who use it, and 25 quarrels bought for one member are 25 nobody
            // else can shoot. TASK-425.
            //
            // *** THE PRICE IS FOR THE STACK, NOT PER UNIT. *** Also measured: sharing the same
            // stack still cost the full 14 sovereigns 4 royals (152 -> 8 royals), so the quantity
            // decides delivery and not the bill.
            int chosen = delivered.Variable;
            // *** THE PICKER IS GATED ON THE RECORD'S 0x8000, NOT ON THE COUNT. *** CMBINV.C:911
            // raises it for `rec->wFlags & 0x8000`, or for 0x800 when BOTH ends are party slots —
            // which a shop is not. `InventoryTransfer.Plan` already applies exactly that test on
            // every other path; this site tested the quantity alone and so asked about anything
            // that arrived in twos.
            //
            // Measured at the Battleworks, Highcastle, 2026-09-13: a Herbal Pack (3) is
            // `DiscardWhenEmpty, Stackable, LimitedUses` with no 0x8000, and the original hands
            // over all three without asking. Quarrels and Rations do carry it, which is why the
            // prompt was seen at Romney and taken for the general rule.
            if ((info.Flags & GameData.Resources.Object.ObjectFlags.B8000) != 0
                && delivered.Variable >= 2) {
                chosen = await ShowQuantityPickerAsync(delivered.Variable, allowShare: true);
            }

            bool share = chosen < 0;
            if (!share && chosen <= 0) {
                return;   // picked none — no sale, and nothing charged
            }
            if (!share && chosen < delivered.Variable) {
                delivered.Variable = (byte)chosen;
            }

            int gold = _gameSession.PartyGold;

            if (share) {
                // Distributed out of a scratch holder rather than out of the receiving pack: a
                // member who already carries the same object would otherwise have HIS stack merged
                // into the purchase and spread around with it. The room check above has already
                // found one member with space, so Distribute cannot come back empty-handed.
                var holder = new RuntimeContainer {
                    Capacity = 2,
                    ContainerType = GameData.Resources.Data.SaveGameContainerType.Inventory,
                };
                if (!GameData.Resources.Shop.ShopStock.Buy(
                        _displayed, holder, item, _gameSession.ObjectInfo, price, ref gold, delivered)) {
                    return;
                }
                _gameSession.PartyGold = gold;
                InventoryTransfer.Distribute(holder, 0, ActiveMemberContainers(),
                    _gameSession.ObjectInfo);

                return;
            }

            if (!GameData.Resources.Shop.ShopStock.Buy(
                    _displayed, target, item, _gameSession.ObjectInfo, price, ref gold, delivered)) {
                return;
            }
            _gameSession.PartyGold = gold;

            EatOnTheSpot(delivered, target, portraitSlot);
        }

        /// <summary>
        /// A drink bought and drunk where it stands.
        /// </summary>
        /// <remarks>
        /// It never enters an inventory, so there is no room check and nothing to carry away. The
        /// one refusal is the drinker who is already as drunk as the game allows: the money goes
        /// back and a companion calls a halt (ddx 1800028).
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTask DrinkAtCounterAsync(
            GameData.Resources.Object.ObjectInfo info, int portraitSlot, long price) {
            _gameSession.PartyGold -= (int)price;

            int characterIndex = CharacterIndexOf(portraitSlot);
            GameData.Resources.Character.ActorStat[] stats = _gameSession.StatsOf(characterIndex);
            bool drunk = GameData.Resources.Shop.ShopPurchase.Drink(
                info,
                _gameSession.ConditionsOf(characterIndex),
                stats?[(int)GameData.ActorAttribute.Health],
                stats?[(int)GameData.ActorAttribute.Stamina]);

            if (!drunk) {
                _gameSession.PartyGold += (int)price;
                await _dialogs.ShowById(HadEnoughToDrinkDialog);

                return;
            }

            Audio.MenuSoundService.Instance?.Play(info.SoundId);
        }

        /// <summary>
        /// Food bought by someone starving is eaten before it is put away.
        /// </summary>
        /// <remarks>
        /// The buyer eats through <see cref="GameData.Resources.Character.UpkeepEngine.ConsumeRations"/>
        /// — the same meal the hourly upkeep serves, so the shop does not get its own idea of what
        /// rations do. That matters because the pack may already hold spoiled or poisoned rations,
        /// and the eating order is the meal's business, not the shop's: what was just bought is not
        /// necessarily what gets eaten.
        /// </remarks>
        private void EatOnTheSpot(RuntimeItem delivered, RuntimeContainer target, int portraitSlot) {
            GameData.Resources.Object.ObjectInfo rec =
                _gameSession.ObjectInfo?.GetById(delivered.ObjectId);
            if (rec?.ObjectType != GameData.ObjectType.Food) {
                return;
            }

            GameData.Resources.Character.ActorConditions conditions =
                _gameSession.ConditionsOf(CharacterIndexOf(portraitSlot));
            if (conditions == null
                || !conditions.Has(GameData.ActorCondition.Starving)) {
                return;
            }

            GameData.Resources.Character.UpkeepEngine.ConsumeRations(
                target, conditions, id => _gameSession.ObjectInfo?.GetById(id));
        }

        /// <summary>
        /// One round of haggling — <c>HaggleOverItemPrice</c> @0x5b4f1.
        /// </summary>
        /// <returns>True to re-offer, false when the shopkeeper has ended the transaction.</returns>
        /// <remarks>
        /// <b>Winning re-opens the offer; losing closes it.</b> Success lowers the type's price-table
        /// entry and the shopkeeper quotes again (ddx 1800021); failure gets "buy at my price or be
        /// off with you" (ddx 1800020) and the transaction is over. A failed haggle can also offend
        /// the shopkeeper into refusing the type outright, which is the -1 entry that makes the cell
        /// read "Unavailable" for the rest of the visit.
        ///
        /// <para>The rules — both rolls, the discount, the clamp and the refusal chance — are
        /// <see cref="GameData.Resources.Shop.ShopPricing.Haggle"/>'s; this resolves the shop and
        /// haggler around them and spends the answer.</para>
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTask<bool> HaggleForAsync(RuntimeItem item,
            GameData.Resources.Object.ObjectInfo info, long price, int portraitSlot) {
            // Scrolls are the one thing no shopkeeper will discuss — SHOP.C's haggle arm answers
            // item_id 0x85 with ddx 1800031 and re-offers at the same price.
            if (item.ObjectId == GameData.Resources.Shop.ShopPricing.MagicalScrollObjectId) {
                await _dialogs.ShowById(HaggleRefusedForScrollDialog);

                return true;   // the price stands, and the shopkeeper asks again
            }

            // *** THE EFFECTIVE HAGGLING, NOT THE STORED BYTE. *** SHOP.C:73 is
            // `stat_actor_get(combatant, 0xc, 0)` — mode 0, so modifiers, afflictions and the health
            // scaling all count. Measured in the running original: Locklear haggles at 37 healthy
            // and at 22 with his health at 10, while Base stays 37 (TASK-477).
            GameData.Resources.Shop.HaggleOutcome outcome = GameData.Resources.Shop.ShopPricing.Haggle(
                ListPriceFor(item.ObjectId, info),
                UnhaggledListPrice(info),
                _gameSession.EffectiveStat(CharacterIndexOf(portraitSlot),
                    GameData.ActorAttribute.Haggling),
                _shopData?.ShopkeeperSkill ?? 0,
                _shopData?.MaxHagglingDiscount ?? 0,
                // +5 is the shop reading of the byte the temples use for teleport cost.
                _shopData?.TeleportParam ?? 0,
                Roll100);

            _shopPrices[item.ObjectId] =
                outcome.ShopkeeperRefusedToSell ? -1 : outcome.Price;

            if (outcome.PartyHagglingXp) {
                AwardHagglingSkill();
            }
            if (outcome.HagglerHagglingXp) {
                AwardHagglingSkill(portraitSlot);
            }

            await _dialogs.ShowById(outcome.Succeeded ? HaggleWonDialog : HaggleLostDialog);

            return outcome.Succeeded;
        }

        /// <summary>
        /// +1 Haggling, to the whole party or to one member.
        /// </summary>
        /// <remarks>
        /// The original awards BOTH on a win — <c>ChangeAttributeValueForWholeParty</c> then
        /// <c>ChangeAttributeValue</c> on the haggler — so the member who did the talking gains
        /// twice. That is not a double-count to tidy away; it is how the skill is meant to favour
        /// whoever is doing the work.
        ///
        /// <para><b>One USE, not one point.</b> SHOP.C:88-94 passes 1 with mode <b>3</b> —
        /// skill-use advancement through the per-skill rate — to both
        /// <c>stat_party_broadcast_status_op</c> and <c>stat_combatant_modify</c>. This added 1 to
        /// the stored value until 2026-09-03, which is far more than the original grants and does
        /// not slow down as the skill climbs.</para>
        ///
        /// <para><b>The consolation award is ALREADY MODELLED — I claimed otherwise here on
        /// 2026-09-03 and was wrong.</b> A failed haggle rolls
        /// <c>RND(100) &lt; (100 - partyRoll) / 5</c> for a party-wide use (SHOP.C:93), and
        /// <see cref="GameData.Resources.Shop.ShopPricing.Haggle"/> has carried exactly that
        /// formula all along — <c>HaggleOutcome.PartyHagglingXp</c> is what
        /// <see cref="HaggleForAsync"/> already checks before calling this. The claim came from
        /// reading this call site alone, without looking at the layer that owns the rule.</para>
        ///
        /// <para>That model also handles the case the original gets wrong: on a non-negotiable price
        /// <c>partyRoll</c> is never assigned and the consolation roll reads an uninitialised local,
        /// so the port skips it rather than inventing a value.</para>
        /// </remarks>
        private void AwardHagglingSkill(int? portraitSlot = null) {
            byte[] roster = _gameSession.ActivePartyIndices ?? System.Array.Empty<byte>();
            for (var slot = 0; slot < roster.Length; slot++) {
                if (portraitSlot.HasValue && slot != portraitSlot.Value) {
                    continue;
                }

                // Through ModifyStatOf so the sheet mark follows the change (TASK-611).
                _gameSession.ModifyStatOf(roster[slot], GameData.ActorAttribute.Haggling, 1,
                    GameData.Resources.Character.StatChangeMode.SkillUse,
                    _gameSession.StudyBonusFor(roster[slot], GameData.ActorAttribute.Haggling));
            }
        }

        /// <summary>"That will be @1" — the shopkeeper naming a price (BuyItem @0x5b6d2).</summary>
        private const int ShopOfferDialog = 1800023;

        /// <summary>The buy offer's three answers, by the flag each branch carries. Not positions:
        /// the original reads GetGlobalValue(260) and GetGlobalValue(262) by number.</summary>
        private const int OfferAccepted = 260;

        private const int OfferHaggle = 262;

        /// <summary>The haggle succeeded — one of ten wordings the entry picks between.</summary>
        private const int HaggleWonDialog = 1800021;

        /// <summary>"Buy at my price or be off with you." Ends the transaction.</summary>
        private const int HaggleLostDialog = 1800020;

        /// <summary>"My suppliers don't haggle" — a Magical Scroll's price is not negotiable.</summary>
        private const int HaggleRefusedForScrollDialog = 1800031;

        /// <summary>"You've had quite enough" — a companion stops the drinking (BuyItem @0x5b815).</summary>
        private const int HadEnoughToDrinkDialog = 1800028;

        /// <summary>"I can offer only @1 for it" — the shopkeeper's counter-offer (SellItem @0x5b991).</summary>
        private const int ShopSellOfferDialog = 1800022;

        /// <summary>"I have no use for such an item" — the refusal for goods outside the shop's
        /// categories that it does not already stock.</summary>
        private const int ShopNoUseDialog = 1800025;

        /// <summary>The shop is full of stock it will not displace.</summary>
        private const int ShopWontFitDialog = 1800008;

        /// <summary>Which "won't fit" wording that dialog picks (0x5bab8 writes 10 before showing it).</summary>
        private const int ShopWontFitMessage = 10;

        /// <summary>
        /// Global 30000 — the scratch selector a screen writes before showing a dialog that branches
        /// on it. It already answers to two other names for two other screens
        /// (<see cref="InnStay.RepeatOfferGlobal"/>, <c>TeleportMenu.HelpTopicGlobal</c>); those are
        /// three uses of ONE reused global, not three globals.
        /// </summary>
        private const int MessageSelectorGlobal = 30000;

        /// <summary>
        /// Whether a price can be quoted for this item at all.
        /// </summary>
        /// <remarks>
        /// <b>Sell side only.</b> A Magical Scroll is worth the price of the SPELL it carries, and
        /// that table now reaches the buy path through <see cref="ObjectInfoSet.SpellPriceFor"/>,
        /// so a scroll is quoted and sold like anything else.
        ///
        /// <para>Selling one to a shop is a separate rule this does not yet implement:
        /// <c>shop_sell_item</c> refuses when <c>nBase_price == 0</c> AND the shop does not already
        /// stock that item id, and object 133's base price is 0 — so a shop that deals in scrolls
        /// takes them and one that does not refuses. Until that gate is ported, the sell path keeps
        /// this blanket refusal rather than paying out the spell price everywhere.</para>
        /// </remarks>
        private static bool Priceable(RuntimeItem item) =>
            item.ObjectId != GameData.Resources.Shop.ShopPricing.MagicalScrollObjectId;

        /// <summary>
        /// Offering an item to the shop — <c>SellItem</c> @0x5b991.
        /// </summary>
        /// <remarks>
        /// Same gesture as putting something into a loot container: with a member's pack on screen,
        /// drop the item on the shop window.
        ///
        /// <para><b>Both refusals come before any price is named.</b> A shop that does not deal in
        /// the item says so (ddx 1800025), a shop with no slot it is willing to displace says so
        /// (ddx 1800008), and only then does the shopkeeper name an offer the player may turn down
        /// (ddx 1800022, answered through global 260 like every other confirm). Declining simply
        /// ends the offer — haggling (ddx 1800020/1800021) is not implemented on either side of the
        /// counter yet.</para>
        ///
        /// <para>The move itself is <see cref="ShopStock.Sell"/>'s, including the rule that a full
        /// shop overwrites its DEAREST stock and loses it.</para>
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTaskVoid SellToShopAsync(int slot) {
            RuntimeContainer seller = _displayed;
            if (slot < 0 || slot >= seller.Items.Count) {
                RenderCurrent();

                return;
            }

            RuntimeItem item = seller.Items[slot];
            ObjectInfo info = _gameSession.ObjectInfo?.GetById(item.ObjectId);
            if (info == null || !Priceable(item)) {
                DeselectItem();
                RenderCurrent();

                return;
            }

            GameData.ShopItemCategories categories =
                _shopData?.ShopCategories ?? default;

            // The escape that makes the category gate less strict than it looks: a shop always buys
            // more of something it already has on the shelf, whatever it nominally trades in.
            bool stocked = InventoryQuery.CountByKind(_shop, item.ObjectId) != 0;
            if (!GameData.Resources.Shop.ShopPricing.WillBuy(
                    info.Price, (GameData.ShopItemCategories)info.ShopType,
                    categories, stocked)) {
                await _dialogs.ShowById(ShopNoUseDialog);
                DeselectItem();
                RenderCurrent();

                return;
            }

            if (GameData.Resources.Shop.ShopStock.SelectSellSlot(
                    _shop, _gameSession.ObjectInfo, out _) < 0) {
                _gameSession.SetGlobalValue(MessageSelectorGlobal, ShopWontFitMessage);
                await _dialogs.ShowById(ShopWontFitDialog);
                DeselectItem();
                RenderCurrent();

                return;
            }

            // PriceOf is the shelf price, and computeItemValue @0x578a9 reads that same per-shop
            // price list — so what the shop pays is a markdown of what it would charge, not of some
            // separate catalogue value.
            long price = GameData.Resources.Shop.ShopPricing.SellPrice(
                PriceOf(item, info), _shopData?.MarkDownPercentage ?? 0, info.ObjectType);

            _gameSession.SetGlobalValue(InnStay.PriceGlobal, (int)price);
            if (await _dialogs.ShowConfirmById(ShopSellOfferDialog)) {
                int gold = _gameSession.PartyGold;
                if (GameData.Resources.Shop.ShopStock.Sell(
                        _shop, seller, item, _gameSession.ObjectInfo, categories, price, ref gold)
                    == GameData.Resources.Shop.ShopStock.SellResult.Sold) {
                    _gameSession.PartyGold = gold;
                }
            }

            DeselectItem();
            RenderCurrent();
        }

        /// <summary>
        /// The shared transfer flow: <see cref="InventoryTransfer.Plan"/> resolves everything
        /// that needs no answer; a plan that asks runs the quantity picker (spec §14) and feeds
        /// the choice to Apply (amount) or Distribute (Share). Ends with the refusal dialog, a
        /// deselect and a re-render either way.
        /// </summary>
        private UniTask RunTransfer(int slot, RuntimeContainer target, bool allowShare) {
            // *** THE REFUSALS NAME THE ITEM, SO EVERY TRANSFER HAS TO SAY WHICH ONE. ***
            // "For a moment he'd thought to discard his @1..." resolves the object through the
            // engine's nEvtArgItemId, which the original stamps at each site that can refuse. It was
            // stamped only where discarding does it, so a refusal on any OTHER path — dropping onto
            // a portrait, moving into a container — printed the sentence with a hole in it. Stamping
            // on the shared path covers every caller, including ones not written yet.
            if (_displayed != null && slot >= 0 && slot < _displayed.Items.Count) {
                _gameSession.SetDialogKeyObjectId(_displayed.Items[slot].ObjectId);
            }

            int gold = _gameSession.PartyGold;
            InventoryTransfer.TransferPlan plan = InventoryTransfer.Plan(_displayed, slot, target,
                _gameSession.ObjectInfo, ref gold, IsMemberCaster(target), allowShare,
                _gameSession.SharedKeysInventory);
            _gameSession.PartyGold = gold;
            if (plan.Immediate is { } immediate) {
                ShowTransferResult(immediate, target);
                DeselectItem();
                RenderCurrent();
                return UniTask.CompletedTask;
            }
            return RunPickerTransferAsync(plan, target);
        }

        /// <summary>
        /// Raise the quantity picker, with this screen's teardown as a way out of the wait.
        /// </summary>
        /// <remarks>
        /// <b>ONE OWNER FOR THE PICKER'S LIFETIME.</b> Both callers used to set
        /// <see cref="_pickerOpen"/> and clear it in their own <c>finally</c> — correct as far as it
        /// went, and useless when the wait was never answered at all. An abandoned picker (the
        /// screen torn down, a save loaded underneath it) left the continuation parked for ever, so
        /// neither <c>finally</c> ran: the flag stayed true on a component that OUTLIVES the screen,
        /// and <c>OnPrimaryPressed</c>/<c>OnSecondaryPressed</c>/<c>OnReleased</c> all early-return
        /// on it. The next open rebuilt the stage, so the scrim and panel were gone and the screen
        /// looked entirely normal while accepting no input for the rest of the session — the
        /// combination that made it unrecognisable from the screen alone (TASK-568).
        ///
        /// <para>Measured 2026-09-17 on save walk/182: `_pressClickCount` rose (PointerDown DID
        /// arrive) while `_pressSlot` stayed -1, because the handler returns before ever calling
        /// `SlotAt`. `_pickerOpen=True scrim=absent`, four samples over 8 s across a reload.</para>
        ///
        /// <para><b>Cancellation, not a reset flag</b>, because clearing the flag alone would leave
        /// the scrim and panel allocated and the input layer pushed. <see cref="OnBeforeHide"/>
        /// cancels, the picker resolves as zero, and both finallys run exactly as if the player had
        /// answered "None" — which is the same thing <c>_lockClosed</c> does a few hundred lines up,
        /// for the same reason.</para>
        /// </remarks>
        private async UniTask<int> ShowQuantityPickerAsync(int max, bool allowShare) {
            _pickerAbandoned?.Dispose();
            _pickerAbandoned = new System.Threading.CancellationTokenSource();
            _pickerOpen = true;
            try {
                return await QuantityPickerView.ShowAsync(_stage, _inputStack, max, allowShare,
                    _resources, _pickerAbandoned.Token);
            } finally {
                _pickerOpen = false;
            }
        }

        /// <summary>Cancelled by <see cref="OnBeforeHide"/> to end an unanswered picker.</summary>
        private System.Threading.CancellationTokenSource _pickerAbandoned;

        // The plan knows its own target, but TransferPlan.Target is internal to GameData, so the
        // destination rides along rather than widening that library's surface for a message.
        private async UniTask RunPickerTransferAsync(InventoryTransfer.TransferPlan plan,
            RuntimeContainer target) {
            try {
                int qty = await ShowQuantityPickerAsync(plan.MaxQuantity, plan.AllowShare);
                InventoryTransfer.Result result = qty < 0
                    ? InventoryTransfer.Distribute(plan, ActiveMemberContainers())
                    : InventoryTransfer.Apply(plan, qty);
                ShowTransferResult(result, target);
            } finally {
                DeselectItem();
                RenderCurrent();
            }
        }

        // The Share recipients: the active party in roster order (distributeStackToParty walks
        // party_roster 0..party_count). ResolveMemberContainer maps portrait slots the same way.
        private List<RuntimeContainer> ActiveMemberContainers() {
            var members = new List<RuntimeContainer>();
            int count = _gameSession.ActivePartyIndices?.Length ?? 0;
            for (int i = 0; i < count; i++) {
                RuntimeContainer member = ResolveMemberContainer(i);
                if (member != null) {
                    members.Add(member);
                }
            }
            return members;
        }

        private void ShowTransferResult(InventoryTransfer.Result result, RuntimeContainer target) {
            // *** THE ORIGINAL ANIMATES ONLY ON A TRANSFER THAT HAPPENED. *** invui_dropOnPortrait
            // guards the whole blit loop on `xfer_result > 0`, so a refusal shows its dialog and
            // nothing flies. These three are our "it moved"; every other leaf below is a refusal,
            // and Cancelled is the picker being dismissed.
            if (result is InventoryTransfer.Result.Moved
                or InventoryTransfer.Result.GoldConverted
                or InventoryTransfer.Result.SwappedEquipped) {
                FlyDroppedItemToPortrait();
            }
            _dropPortraitSlot = -1;
            _dropObject = null;
            switch (result) {
                case InventoryTransfer.Result.MustKeepEquipped:
                    // transferItem @0x5555e: no same-category swap partner — the equipped melee
                    // weapon/staff stays put ("strip himself of his defenses"). Also the active
                    // Ring of Prandur guard, which plays the same record.
                    _dialogs?.ShowById(MustKeepEquippedDialogId).Forget();
                    break;
                case InventoryTransfer.Result.LitTorchRefused:
                    _dialogs?.ShowById(LitTorchDialogId).Forget();
                    break;
                case InventoryTransfer.Result.DoesNotFit:
                    // Var 0 is the DESTINATION container's type byte, read straight off the record:
                    // sub_ovr157_4E3 @0x54be9 does `mov al, es:[bx+container.metaData.containerType]`
                    // / `mov global_30000, ax` on the failed-transfer branch, then shows this record.
                    // SaveGameContainerType lines up with the leaves 1:1 — 1 Inventory ("waved off
                    // the ..."), 2/3 Bag ("canvas sack"), 4 Chest ("wooden chest"), 5/7 Corpse and
                    // NpcInventory ("pawed the dead body"), 10 shop ("room ... on my shelves").
                    // Passing the byte therefore covers every kind we can open, not just members;
                    // an unmodelled kind still lands on the record's own default leaf.
                    ShowVar0Message(NoRoomDialogId, target != null ? (int)target.ContainerType : 0);
                    break;
                case InventoryTransfer.Result.Blocked:
                    _logger.LogInformation("Transfer blocked.");
                    break;
            }
        }

        /// <summary>
        /// The number of steps the dropped sprite takes on its way into the portrait.
        /// </summary>
        /// <remarks>
        /// <b>The original's loop is <c>for (i = 14; i >= 0; i--)</c> with no frame wait in it</b> —
        /// one <c>screen_frame_present()</c> per step, so at mode 13h's ~70 Hz the whole flight is
        /// about 0.21 s. Faithful, and fast.
        ///
        /// <para>Kept as a named constant precisely because it is the one free parameter here: JvE
        /// has said the existing item-inspect fly is too quick for him, and this is the same kind of
        /// call as TASK-593/594. Raising this number is the whole adjustment — the geometry below is
        /// expressed in terms of it.</para>
        /// </remarks>
        private const int DropFlightSteps = GameData.Resources.Inventory.ItemDropFlight.Steps;

        /// <summary>Cue for an item flying into a portrait; the shop has its own.</summary>
        /// <remarks><c>audio_sfx_play_n_times(is_shop != 0 ? 0x3c : 0x3d, 0, 0)</c>.</remarks>
        private const int DropFlightSoundId = GameData.Resources.Inventory.ItemDropFlight.SoundId;

        /// <summary>The same cue when the item was bought rather than handed over.</summary>
        private const int DropFlightShopSoundId = GameData.Resources.Inventory.ItemDropFlight.ShopSoundId;

        /// <summary>
        /// Fly the dropped item's icon into the portrait it was dropped on, shrinking as it goes.
        /// </summary>
        /// <remarks>
        /// <b>It shrinks to nothing and it travels BACKWARDS from how a drop reads.</b> The
        /// original's <c>i</c> counts DOWN from 14, and both the scale and the interpolation are
        /// <c>i/15</c> — so the sprite starts at 14/15 of full size near the cursor and arrives at
        /// the portrait centre at zero size. It is the item being absorbed into the head, not a
        /// counter sliding into place, and reversing the loop produces something that looks
        /// deliberate and is wrong.
        ///
        /// <para>A fresh element rather than the drag ghost: the ghost is destroyed the moment the
        /// pointer comes up, several async hops before the transfer resolves, and keeping it alive
        /// across those would tie its lifetime to a path that can be cancelled or torn down.</para>
        /// </remarks>
        private void FlyDroppedItemToPortrait() {
            int portraitSlot = _dropPortraitSlot;
            ObjectInfo obj = _dropObject;
            if (_stage == null || obj == null || portraitSlot < 0 || _ui == null
                || !_ui.TryGetElementRect(portraitSlot + PortraitSlot1, out Rect portrait)) {
                return;
            }
            Audio.MenuSoundService.Instance?.Play(
                ShowingShopShelf ? DropFlightShopSoundId : DropFlightSoundId);

            Vector2 from = _dropStagePos;
            var to = new Vector2(portrait.center.x, portrait.center.y);
            var flyer = new VisualElement {
                name = "drop_flight",
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute },
            };
            _stage.Add(flyer);
            LoadFlightIconAsync(flyer, obj, _dropItemFlags, from, to).Forget();
        }

        // Load the icon, then run the flight. Split out so the element is sized to the real sprite
        // before the first step: scaling from a placeholder size makes the first frame jump.
        private async UniTask LoadFlightIconAsync(VisualElement flyer, ObjectInfo obj,
            ushort itemFlags, Vector2 from, Vector2 to) {
            string key = ItemIconResolver.ResolveBmxSubResource(obj, itemFlags);
            Sprite sprite = null;
            if (key != null && !_renderer.TryGetCachedSprite(key, out sprite)) {
                sprite = await _resources.LoadAssetAsync<Sprite>(key, this);
            }
            if (sprite == null || flyer.panel == null) {
                flyer.RemoveFromHierarchy(); // the screen closed under us, or the icon is missing
                return;
            }
            flyer.SetBackgroundSpriteNativeSizeTopLeft(sprite);
            float fullW = sprite.rect.width;
            float fullH = sprite.rect.height;
            // The flight is ~0.25 s, which is far too short to observe by polling from outside the
            // Editor — this line is how it gets verified at all.
            _logger?.LogDebug("Drop flight: {0} from {1} to {2}, {3} steps at {4}x{5}.",
                key, from, to, DropFlightSteps, fullW, fullH);

            int i = DropFlightSteps - 1;
            IVisualElementScheduledItem flight = null;
            flight = flyer.schedule.Execute(() => {
                // The original's arithmetic, kept in the same order: scale first, then the
                // interpolated corner, then half the SCALED size back off it.
                (double w, double h) = GameData.Resources.Inventory.ItemDropFlight.SizeAt(
                    fullW, fullH, i);
                flyer.style.width = (float)w;
                flyer.style.height = (float)h;
                (double left, double top) = GameData.Resources.Inventory.ItemDropFlight.CornerAt(
                    from.x, from.y, to.x, to.y, fullW, fullH, i);
                flyer.style.left = (float)left;
                flyer.style.top = (float)top;
                if (--i < 0) {
                    flight?.Pause();
                    flyer.RemoveFromHierarchy();
                }
            }).Every(0); // every panel update — the original steps once per presented frame
        }

        // Resolve the RuntimeContainer for the active-party member behind portrait 0/1/2.
        private RuntimeContainer ResolveMemberContainer(int portraitSlot) {
            int characterIndex = CharacterIndexOf(portraitSlot);

            return characterIndex < 0 ? null : _gameSession.GetActorInventory(characterIndex);
        }

        /// <summary>
        /// The character index behind a portrait — what every per-member lookup is keyed by.
        /// </summary>
        /// <remarks>
        /// <b>A portrait slot is not a character index and neither is an actor number.</b> The
        /// portrait is a seat (0..2), the character index is a position in the party roster, and the
        /// stored actor number is that position plus one. Resolving a portrait through the ACTOR
        /// NUMBER handed every drop to the next member along — an item dropped on Locklear went to
        /// Gorath — and the third portrait resolved to someone not in the party at all.
        ///
        /// <para><see cref="GameSession.GetActorInventory"/>, <see cref="GameSession.StatsOf"/> and
        /// <see cref="GameSession.ConditionsOf"/> all take this index, so it is worth having once
        /// rather than being re-derived (differently) at each call site.</para>
        /// </remarks>
        private int CharacterIndexOf(int portraitSlot) {
            byte[] activeIndices = _gameSession.ActivePartyIndices;
            if (portraitSlot < 0 || activeIndices == null || portraitSlot >= activeIndices.Length) {
                return -1;
            }

            return activeIndices[portraitSlot];
        }

        private int PortraitUnder(Vector2 stageLocal) {
            for (int slot = 0; slot < 3; slot++) {
                if (_ui != null && _ui.TryGetElementRect(slot + PortraitSlot1, out Rect rect)
                    && rect.Contains(stageLocal)) {
                    return slot;
                }
            }
            return -1;
        }

        private void ResetGesture() {
            _gesture?.Cancel();
            DestroyGhost();
            SetHoverPortrait(-1);
            UpdateDiscardBorder(dragging: false, overWindow: false);
            UpdatePaperdollBorder(dragging: false, -1, Vector2.zero);
            _pressSlot = -1;
            _dragSlot = -1;
        }

        private void DestroyGhost() {
            if (_ghost != null) {
                _ghost.RemoveFromHierarchy();
                _ghost = null;
                _resources?.ReleaseAssets(this);
            }
        }

        // Pop back to whatever pushed this screen (travel); the navigator re-shows it.
        private void Close() => _navigator.Pop().Forget();
    }
}
