namespace BakAgain.World.Hotspots {
    using GameData.Resources.GameState;
    using System;
    using System.Collections.Generic;
    using BakAgain.Combat;
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Data;
    using GameData.Resources.Combat;
using GameData.Resources.Scene;
    using GameData.Resources.World;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Runs the hotspot activate pass against the live game for the chunk the party is standing in.
    /// Owns the per-chunk <c>Tzzxxyy.DAT</c> tables, the <c>DEF_BLOC.DAT</c> parameters and the
    /// mapping from party world position to sub-tile — everything <see cref="HotspotActivator"/>
    /// deliberately does not know about.
    ///
    /// <para>Scope: this is the <b>activate</b> pass only (does the step stand?). The dispatch pass
    /// — running the combat, entering the town, crossing the zone border — has no owner in Unity
    /// yet, so pending triggers are recorded in <see cref="Pending"/> and logged rather than played
    /// out. See docs/specs/collision-system.md §3.4.</para>
    /// </summary>
    public sealed class HotspotService : IHotspotHost {
        // The tile size the original divides world positions by — one source, in the model that
        // RE'd it. Three separate copies of this literal existed before.
        private const int ChunkSize = GameData.Resources.World.WorldTileCache.TileWorldSize;
        private const int CellSize = 1600;
        private const int CellsPerChunk = 40;

        private readonly ILogger _logger;
        private readonly IResourceProviderService _resources;
        private readonly GameSession _session;
        private readonly IDialogManager _dialogs;

        private readonly Dictionary<(int x, int y), List<TileEventTrigger>> _byChunk = new();
        private readonly Dictionary<(int x, int y), int> _refIndex = new();
        private DefFamilyFile<DefBlocEntry> _bloc;
        private DefFamilyFile<DefCombEntry> _comb;
        private DefFamilyFile<DefTrapEntry> _trap;
        private DefFamilyFile<DefDialEntry> _dial;
        private DefFamilyFile<DefDisaEntry> _disa;
        private DefFamilyFile<DefEnabEntry> _enab;
        private DefFamilyFile<DefTownEntry> _town;
        private DefFamilyFile<DefBkgrEntry> _bkgr;
        private DefFamilyFile<DefZoneEntry> _zoneDef;
        private readonly BakAgain.World.Scenes.LocationScenePlayer _locations;
        // Lazy: IGameFlow -> WorldRuntime -> this, so holding one directly is a DI cycle.
        private readonly System.Func<Core.Services.IGameFlow> _flowAccessor;

        // Lazy for the same reason the flow accessor is: the world layer must not take a hard
        // dependency on a UI object that is built later and may not exist at all in a test.
        private readonly System.Func<BakAgain.UI.Combat.CombatMenu> _combatMenuAccessor;

        // The quarrel picker the melee menu's Shoot button raises. Lazy and optional for the same
        // reasons the melee menu is; absent, Shoot reports that it has nowhere to go rather than
        // silently doing nothing.
        private readonly System.Func<BakAgain.UI.Combat.ShootMenu> _shootMenuAccessor;

        // The screen stack. A screen is only ever raised THROUGH this — ScreenBase says so in as
        // many words ("Only the navigator calls ShowAsync/HideAsync"), and calling ShowAsync
        // directly leaves the travel screen up underneath with nothing able to pop the new one.
        private readonly System.Func<BakAgain.UI.Navigation.IScreenNavigator> _navigatorAccessor;

        // The cast screen, resolved lazily for the same reason the two combat menus are: it is
        // conditionally registered, and a fight must degrade rather than throw when it is absent.
        private readonly System.Func<BakAgain.UI.Spells.CastScreen> _castScreenAccessor;

        // Raised as a fight starts (true) and ends (false). The world layer owns the music because
        // it owns the player and the track to come back to; this only says WHEN.
        private readonly Action<bool> _setCombatMusic;

        // Draws or clears the arena. Separate from the music callback because a fight
        // that cannot be drawn must still run — see WorldRuntime.DrawCombatantsAsync.
        //
        // *** AWAITABLE, WHILE StartCombat STAYS SYNCHRONOUS. *** The arena is built over several
        // frames (DrawCombatantsAsync), so a fade that did not wait for it would reveal an empty
        // board and pop the combatants in afterwards. Nothing on the dispatch path awaits this —
        // SwapArenaThroughFade does, from inside a forgotten task — so the switch in
        // HotspotDispatcher and the 1,313 trigger placements behind it are untouched.
        private readonly Func<bool, UniTask> _showArena;

        // Covers the screen while the world is swapped for an arena, and back. Optional: a service
        // built without one cuts, which is what every test that does not care about presentation
        // gets.
        private readonly BakAgain.UI.Navigation.IScreenFade _fade;

        // Underground zones keep a survivor's STORED pose rather than the one a fight produced, so
        // PersistSurvivors has to know which kind of zone this is. Supplied rather than derived: the
        // zone kind is WorldRuntime's, and it already tracks it for the arena camera.
        private readonly Func<bool> _zoneIsUnderground;

        // Shows a corpse's contents. The inventory screen is the travel HUD's, so the world side
        // hands it over rather than this service reaching into UI.
        private readonly Action<GameData.Resources.Inventory.RuntimeContainer> _openCorpseLoot;

        // Rebuild the arena's sprites from the fight's CURRENT state. Separate from _showArena
        // because "put the arena on screen" and "the board has changed" are different events, and a
        // reader should not have to know that showing it happens to rebuild it.
        private readonly Action _redrawArena;

        /// <summary>The arena is still drawing the last move (WorldRuntime's slide).</summary>
        private readonly Func<bool> _arenaBusy;

        // Plays a sound by id — the same seam PartyMovement takes, so world and combat cues go
        // through one path rather than each reaching for MenuSoundService themselves.
        private readonly Action<int> _playSfx;

        // The party inventory screen, resolved lazily like the cast and shoot screens: it is a
        // prefab-backed singleton built at a different moment from this service.
        private readonly Func<BakAgain.UI.Inventory.InventoryMenu> _inventoryMenuAccessor;
        private readonly Func<BakAgain.UI.Character.CharacterSheetScreen> _characterSheetAccessor;

        // P1.DAT — where each party slot starts a fight. Null is survivable: the party then enters
        // at (0,0) as before rather than the fight failing.
        private GameData.Resources.Combat.PartyCombatEntries _partyEntries;

        // Item type data — categories and the Degradable flag the condition rule turns on.
        private GameData.Resources.Object.ObjectInfoSet _objects;

        // TRAPS.DAT — the encounter's grid layout: crystals, cannons, the exit, the party's entry
        // tiles and the retreat lock. Null is survivable and yields an ordinary empty arena.
        private GameData.Resources.Combat.TrapData _traps;

        // GRID.DAT — the per-zone pen the tactical grid is drawn in.
        private GameData.Resources.Combat.GridData _gridPens;
        // MONST{n}.DAT per creature type — the AI's flee tendency and action-priority rows, none of
        // which live in the save's actor table. Loaded ONCE: the table is global, not per zone.
        private Dictionary<int, GameData.Resources.Monster.MonsterStats> _monsterStats;

        // The AI flee-threshold table, read out of the EXE. Null is survivable and means nothing
        // routs — MonsterMorale.Routs answers false for an absent table, so the degradation is one
        // the rules already define rather than a guess made here.
        private GameData.Resources.Combat.CombatAffinityTables _affinity;

        private int _zone;
        private int _currentRefIndex;
        private (int x, int y)? _lastChunk;

        /// <summary>
        /// The <c>RND(n)</c> seam — returns a value in <c>[0, n)</c>. Injectable so the scouting roll
        /// can be pinned in tests; defaults to the real generator.
        /// </summary>
        private readonly Func<int, int> _random;

        /// <summary>
        /// The world-entity kind at a world point, or -1 where there is nothing — the seam
        /// <see cref="EnoughGroundToFight"/> sweeps the arena's footprint through.
        /// </summary>
        /// <remarks>
        /// <b>Null means "do not refuse".</b> That is what a test rig passes, and what a host with
        /// no world to sweep passes. Whether the check applies at all is decided HERE, from the zone
        /// kind, rather than by the caller withholding the seam.
        /// </remarks>
        private readonly Func<int, int, int> _groundKindAt;

        /// <summary>
        /// Pushes the party's position and heading out to the world camera.
        /// </summary>
        /// <remarks>
        /// <b>Two of this class's writes move the party and nothing was carrying them to the
        /// view.</b> The flight relocation and the underground turn both write
        /// <c>GameSession</c>, which is the party's state, not the camera's — so without this the
        /// party was somewhere the player could not see they were until the next keypress dragged
        /// the camera along.
        /// </remarks>
        private readonly Action _resyncCamera;

        private GameData.Resources.Config.StartData _start;

        /// <summary>
        /// START.DAT, once loaded — null before then.
        /// </summary>
        /// <remarks>
        /// Exposed so the world layer can read the arena camera's pose without loading the record a
        /// second time. Read-only on purpose: this class owns when it is fetched.
        /// </remarks>
        public GameData.Resources.Config.StartData Start => _start;

        /// <summary>
        /// Whether the current zone is underground, from <c>Z##DEF.DAT</c>.
        /// </summary>
        /// <remarks>
        /// Stored as the bool rather than as the definition: it is the only thing read from it here,
        /// and the obvious field name for the definition (<c>_zoneDef</c>) is already taken by the
        /// DEF_ZONE.DAT trigger table, which is a different file with a confusingly similar name.
        /// </remarks>
        private bool _underground;

        /// <summary>The spell catalogue, for the enemy AI's casts. Null until a zone is loaded.</summary>
        private GameData.Resources.Spells.SpellList _spells;

        /// <summary>SPELLWEA / SPELLRES — the per-(creature, spell) affinity masks.</summary>
        private GameData.Resources.Spells.SpellAffinityTable _spellWeakness;

        /// <inheritdoc cref="_spellWeakness"/>
        private GameData.Resources.Spells.SpellAffinityTable _spellResistance;

        /// <summary>How many arena cells show floor from the underground capture pose (TASK-657).</summary>
        private readonly Func<int> _undergroundFloorCells;

        /// <summary>The placements on one map tile, in file order — for the arena's scenery walls.</summary>
        private readonly Func<int, int, IEnumerable<GameData.Resources.Combat.ArenaScenery.Placement>> _sceneryOnTile;

        /// <summary>Trigger indices the last activate pass queued for a dispatch pass we do not have yet.</summary>
        public List<int> Pending { get; } = new();

        public HotspotService(ILogger logger, IResourceProviderService resources, GameSession session,
            IDialogManager dialogs, BakAgain.World.Scenes.LocationScenePlayer locations = null,
            Func<int, int> random = null, Func<Core.Services.IGameFlow> flowAccessor = null,
            Func<BakAgain.UI.Combat.CombatMenu> combatMenuAccessor = null,
            Action<bool> setCombatMusic = null,
            Action<int> playSfx = null,
            Func<BakAgain.UI.Combat.ShootMenu> shootMenuAccessor = null,
            Func<BakAgain.UI.Spells.CastScreen> castScreenAccessor = null,
            Func<BakAgain.UI.Navigation.IScreenNavigator> navigatorAccessor = null,
            Func<int, int, int> groundKindAt = null,
            Action resyncCamera = null,
            Func<bool, UniTask> showArena = null,
            BakAgain.UI.Navigation.IScreenFade fade = null,
            Action redrawArena = null,
            Action<GameData.Resources.Inventory.RuntimeContainer> openCorpseLoot = null,
            Func<bool> zoneIsUnderground = null,
            Func<BakAgain.UI.Inventory.InventoryMenu> inventoryMenuAccessor = null,
            Func<BakAgain.UI.Character.CharacterSheetScreen> characterSheetAccessor = null,
            Func<int, int, IEnumerable<GameData.Resources.Combat.ArenaScenery.Placement>> sceneryOnTile = null,
            Func<int> undergroundFloorCells = null,
            Func<bool> arenaBusy = null) {
            _arenaBusy = arenaBusy;
            _logger = logger;
            _resources = resources;
            _session = session;
            _dialogs = dialogs;
            _locations = locations;
            _flowAccessor = flowAccessor;
            _combatMenuAccessor = combatMenuAccessor;
            _setCombatMusic = setCombatMusic;
            _showArena = showArena;
            _fade = fade ?? new BakAgain.UI.Navigation.NullScreenFade();
            _openCorpseLoot = openCorpseLoot;
            _redrawArena = redrawArena;
            _playSfx = playSfx;
            _shootMenuAccessor = shootMenuAccessor;
            _castScreenAccessor = castScreenAccessor;
            _inventoryMenuAccessor = inventoryMenuAccessor;
            _characterSheetAccessor = characterSheetAccessor;
            _navigatorAccessor = navigatorAccessor;
            _groundKindAt = groundKindAt;
            _sceneryOnTile = sceneryOnTile;
            _undergroundFloorCells = undergroundFloorCells;
            _resyncCamera = resyncCamera;
            _zoneIsUnderground = zoneIsUnderground;
            _random = random ?? (n => UnityEngine.Random.Range(0, n));
        }

        /// <summary>
        /// Load a zone's hotspot tables. <paramref name="chunks"/> are the <c>Tzzxxyy</c> names the
        /// zone scene was built from; a chunk with no file simply has no hotspots.
        /// </summary>
        public async UniTask LoadZoneAsync(int zoneNumber, IEnumerable<string> chunks, object owner) {
            _zone = zoneNumber;
            // Preloaded here rather than when a fight starts: StartCombat is synchronous, and the
            // party's entry tiles are the same for every encounter in the game.
            _partyEntries ??= await LoadOrNull<GameData.Resources.Combat.PartyCombatEntries>("P1.DAT", owner);
            _objects ??= await LoadOrNull<GameData.Resources.Object.ObjectInfoSet>("OBJINFO.DAT", owner);
            _traps ??= await LoadOrNull<GameData.Resources.Combat.TrapData>("TRAPS.DAT", owner);
            // GRID.DAT is one u16 per zone — the colour the arena draws its tactical grid in.
            // Extracted since the format was reversed and never loaded until now.
            _gridPens ??= await LoadOrNull<GameData.Resources.Combat.GridData>("GRID.DAT", owner);
            await LoadMonsterStatsAsync(owner);
            _affinity ??= await LoadOrNull<GameData.Resources.Combat.CombatAffinityTables>(
                "KRONDOR.EXE", owner);
            // The AI's own copy. CastScreen loads the same file for the party; the provider hands
            // back the one parsed instance, so this is a second consumer rather than a second load.
            _spells ??= await LoadOrNull<GameData.Resources.Spells.SpellList>("SPELLS.DAT", owner);
            // Per-(creature, spell) weakness and resistance — TASK-115. Preloaded with the other
            // combat tables because CombatRuntime is built synchronously and cannot await them.
            _spellWeakness ??= await LoadOrNull<GameData.Resources.Spells.SpellAffinityTable>(
                "SPELLWEA.DAT", owner);
            _spellResistance ??= await LoadOrNull<GameData.Resources.Spells.SpellAffinityTable>(
                "SPELLRES.DAT", owner);
            // The arena's cell size, which is what turns a grid cell into a world offset.
            _start ??= await LoadOrNull<GameData.Resources.Config.StartData>("START.DAT", owner);
            // Re-read every zone, not ??=: keeping the first zone's answer would have every dungeon
            // after the first behave like the overworld. WorldRuntime loads the same file; the
            // provider hands back the one parsed instance, so this is a second consumer rather than
            // a second load.
            _underground = (await LoadOrNull<GameData.Resources.World.ZoneDefinition>(
                $"Z{zoneNumber:D2}DEF.DAT", owner))?.IsUnderground ?? false;
            _byChunk.Clear();
            _refIndex.Clear();

            // *** A ZONE CHANGE RE-ARMS THE TRANSIENT FLAGS. A LOAD DOES NOT. ***
            // CZONE.C:52-55 guards the pair of clear-alls with
            // `if (g_gameState.nZoneId != g_gameState.nPrevZoneId)`, and the only other clear is the
            // TILE crossing at CZONE.C:93, which compares the party's 64000-unit tile against the
            // one the zone actor list already holds — loaded from the save, so it matches after a
            // load and nothing is cleared.
            //
            // Measured 2026-09-12 with scripts/re/statediff.py: after loading the shipped saves the
            // original's live state still carries 5200/5210 (SAVE01) and 5202 (SAVE02), and ours did
            // not. A party resuming a save therefore got a fresh scout roll on a hotspot they had
            // already tried. The condition only became expressible when TASK-456 made PreviousZone
            // real — it was loaded, written back, and never updated, so it could never differ.
            if (_session == null || _session.CurrentZone != _session.PreviousZone) {
                ClearTransientHotspotFlags();

                // *** AND THE ENCOUNTER-OBJECT TABLE, WHICH GOES WITH IT. *** CZONE.C:52-55 runs
                // czone_spell_state_reset_pair() and rgnenc_savefile_init_35slot_tbl() under this
                // one condition, so the flags and the table are re-armed together or not at all.
                // The table wipe is NOT the roaming reset: that one (rgnenc_reset_and_save, which
                // ResetRoamers models) touches a single ref-pair's ROAMING entries and writes
                // Unplaced; this writes the same 35-entry table to all forty pairs with Removed in
                // every slot but the first.
                //
                // Measured on dir.G01/SAVE07: the original's TEMP.GAM came back with kind byte 1
                // across 1,363 of the 1,400 entries where the save carried 3, and ours had touched
                // four. Wandering actors were resuming a zone in positions the original had wiped.
                int wiped = _session?.EncounterActorStates?.InitAllRefPairs() ?? 0;
                if (wiped > 0) {
                    // The placement cache names actors that no longer exist — the same drop
                    // OnStepSizeChanged does after its own reset, and for the same reason.
                    _placedChunk = null;
                    _placedActors = null;
                    _logger?.LogDebug(
                        "Zone {Zone}: wiped {Count} encounter-object entries on the zone change.",
                        zoneNumber, wiped);
                }
            }

            // *** SEEDED, NOT NULLED, FOR THE SAME REASON. *** A null makes the first
            // ActivateAtPartyPosition after a load see a chunk change and clear the flags the guard
            // above just preserved — which is the whole fix undone one step later.
            _lastChunk = _session == null
                ? null
                : ((int x, int y)?)(Floor(_session.PositionX, ChunkSize),
                                    Floor(_session.PositionY, ChunkSize));

            // The persistent "this hotspot is done" flag is keyed by the chunk's slot in Z##REF.DAT,
            // not by its coordinates (hotspotevt_done_set: 400*zone + 10*refIndex + hotspotIndex).
            var zoneRef = await LoadOrNull<ZoneRef>($"Z{zoneNumber:D2}REF.DAT", owner);
            if (zoneRef != null) {
                for (int i = 0; i < zoneRef.Tiles.Count; i++) {
                    _refIndex[(zoneRef.Tiles[i].X, zoneRef.Tiles[i].Y)] = i;
                }
            }

            // *** RESOLVED NOW, NOT ON THE FIRST STEP. *** _currentRefIndex was written in exactly
            // one place, ActivateAtPartyPosition, which runs when the party MOVES — so for the whole
            // of a zone build, and for a party that has been loaded and not yet walked, it read 0.
            // Zero is a real ref-pair, not a "none", so everything keyed on it during the build
            // addressed pair 0's slots and its done-flags. Measured on dir.G01/SAVE07, whose party
            // stands in ref-pair 26: the encounter placement seeded and wrote pair 0.
            _currentRefIndex = RefIndexAt(_session?.PositionX ?? 0, _session?.PositionY ?? 0);

            _bloc = await LoadOrNull<DefFamilyFile<DefBlocEntry>>("DEF_BLOC.DAT", owner);
            // Comb/Trap records carry the avoidable bit, the encounter index and the pre-fire dialog
            // the activate pass needs; without them every ambush would fire unseen.
            _comb = await LoadOrNull<DefFamilyFile<DefCombEntry>>("DEF_COMB.DAT", owner);
            _trap = await LoadOrNull<DefFamilyFile<DefTrapEntry>>("DEF_TRAP.DAT", owner);
            // Dial carries the dialog the dispatch pass speaks; Disa/Enab the chance byte and
            // the flag it writes.
            _dial = await LoadOrNull<DefFamilyFile<DefDialEntry>>("DEF_DIAL.DAT", owner);
            _disa = await LoadOrNull<DefFamilyFile<DefDisaEntry>>("DEF_DISA.DAT", owner);
            _enab = await LoadOrNull<DefFamilyFile<DefEnabEntry>>("DEF_ENAB.DAT", owner);
            // Town/Bkgr carry the GDS scene a gate or doorway enters, and its entry dialog.
            _town = await LoadOrNull<DefFamilyFile<DefTownEntry>>("DEF_TOWN.DAT", owner);
            _bkgr = await LoadOrNull<DefFamilyFile<DefBkgrEntry>>("DEF_BKGR.DAT", owner);
            // Zone carries the boundary's confirm prompt and where accepting puts the party.
            _zoneDef = await LoadOrNull<DefFamilyFile<DefZoneEntry>>("DEF_ZONE.DAT", owner);

            int chapter = _session?.Chapter ?? 1;
            foreach (string chunk in chunks) {
                string name = System.IO.Path.GetFileNameWithoutExtension(chunk);
                if (!TryParseChunkCoords(name, out int cx, out int cy)) {
                    continue;
                }

                // Most tiles carry no event file at all; absence is the common case, not a fault.
            var tile = await LoadOrNull<TileEventTile>($"{name}.DAT", owner, optional: true);
                List<TileEventTrigger> triggers = TriggersFor(tile, chapter);
                if (triggers.Count > 0) {
                    _byChunk[(cx, cy)] = triggers;
                }
            }

            if (_logger != null) LoggerExtensions.LogInformation(_logger, "Hotspots: zone {Zone} chapter {Chapter}, {Chunks} chunk(s) carry triggers.",
                zoneNumber, chapter, _byChunk.Count);
        }

        /// <summary>
        /// Stand a zone up without the archive — the seam every test of this class needs.
        /// </summary>
        /// <remarks>
        /// <b>The DEF tables are only ever filled by <see cref="LoadZoneAsync"/></b>, which wants a
        /// real resource provider and the game's data files, so nothing here could be exercised at
        /// all: the class had no test rig and everything it wires — the combat music, the encounter
        /// address, the fought flag, the two meanings of id 33 — was unasserted.
        ///
        /// <para>Deliberately the same shape <see cref="GameSession"/> already uses for this
        /// (<c>SetZoneContainersForTest</c> and friends): an internal seam that fills the fields the
        /// loader would, rather than a parallel constructor that could drift from the real one.
        /// Anything not passed stays null, which is the loader's own answer for a missing file.</para>
        /// </remarks>
        internal void SetZoneForTest(int zoneNumber,
            System.Collections.Generic.Dictionary<(int x, int y), List<TileEventTrigger>> byChunk = null,
            DefFamilyFile<DefCombEntry> comb = null,
            GameData.Resources.Combat.PartyCombatEntries partyEntries = null,
            GameData.Resources.Object.ObjectInfoSet objects = null,
            int refIndex = 0,
            GameData.Resources.Config.StartData start = null,
            bool underground = false,
            DefFamilyFile<DefTrapEntry> trap = null) {
            _zone = zoneNumber;
            _byChunk.Clear();
            if (byChunk != null) {
                foreach (System.Collections.Generic.KeyValuePair<(int x, int y), List<TileEventTrigger>> pair
                        in byChunk) {
                    _byChunk[pair.Key] = pair.Value;
                }
            }
            _comb = comb;
            _partyEntries = partyEntries;
            _objects = objects;
            _currentRefIndex = refIndex;
            _start = start;
            _underground = underground;
            _trap = trap;
            _lastChunk = null;
        }

        /// <summary>The encounter the live fight belongs to, or -1. For tests and diagnostics.</summary>
        internal long FightingEncounter => _fightingEncounter;

        /// <summary>The encounter address the last <see cref="StartCombat"/> handed the fight.</summary>
        /// <remarks>
        /// Exposed so a test can check the address WITHOUT reaching into the runtime: the removal
        /// persistence it feeds is covered at the model level, but which address this service
        /// computes from a chunk's triggers was not.
        /// </remarks>
        internal GameData.Resources.World.EncounterActorPersistence.RecordAddress
            EncounterAddressForTest(long encounterNumber) => EncounterAddressFor(encounterNumber);

        /// <summary>Presses one combat HUD command, as the built menu would.</summary>
        /// <remarks>
        /// A seam for the same reason <see cref="ResolveRetreatForTest"/> is one: the production
        /// caller needs a built menu, and standing one up would test the menu rather than the arm.
        /// </remarks>
        internal void OnCombatCommandForTest(
            GameData.Resources.Combat.CombatCommands.Command command, int actionId) =>
            OnCombatCommand(command, actionId);

        /// <summary>Runs the retreat arm as the HUD's id-33 press would.</summary>
        /// <remarks>
        /// A seam for the same reason <see cref="EndCombatForTest"/> is one: the production caller is
        /// <c>OnCombatCommand</c>, which needs a built menu, and standing one up would test the menu
        /// rather than the arm.
        /// </remarks>
        internal void ResolveRetreatForTest(Combatant acting) => ResolveRetreat(acting);

        /// <summary>Ends the live fight as the HUD would when it reports the encounter over.</summary>
        /// <remarks>
        /// A seam rather than making <c>EndCombat</c> public: the production caller is
        /// <c>RefreshCombatHud</c>, which needs a HUD, and standing one up would test the menu
        /// rather than the settlement.
        /// </remarks>
        internal void EndCombatForTest() => EndCombat();

        /// <summary>What the arena is currently waiting for, so a test can assert it is nothing.</summary>
        /// <remarks>
        /// A read seam because the invariant TASK-572 restored is about a FIELD, not about a visible
        /// effect: spending a turn must leave nothing armed. The nearest observable — whether
        /// hovering an enemy still records an aim — infers absence from a side effect that has its
        /// own reasons to be missing, which is how the gap went unnoticed while
        /// <c>CombatCommandOutcome.SpendsTheTurn</c> was covered and its consequence was not.
        /// </remarks>
        internal GameData.Resources.Combat.CombatCommandOutcome.PendingMode PendingModeForTest
            => _pendingCombatMode;

        /// <summary>Arms target selection for a spell, as a commit from the cast screen does.</summary>
        /// <remarks>
        /// A seam because the production caller, <c>OnCombatSpellCommitted</c>, reads its record from
        /// a pushed cast screen, and standing one up would test the screen rather than the aim.
        /// </remarks>
        internal void ArmSpellForTest(GameData.Resources.Spells.Spell record, int spellId, int power) {
            _pendingSpell = record;
            _pendingSpellId = spellId;
            _pendingSpellPower = power;
            _pendingCombatMode =
                GameData.Resources.Combat.CombatCommandOutcome.PendingMode.TargetSelection;
        }

        /// <summary>
        /// The <c>Func&lt;x, y, bool&gt;</c> <see cref="PartyMovement"/> calls after a successful
        /// step. True keeps the step; false means roll it back.
        /// </summary>
        public bool ActivateAtPartyPosition(int worldX, int worldY) {
            var chunk = (x: Floor(worldX, ChunkSize), y: Floor(worldY, ChunkSize));

            // Crossing onto another chunk re-arms its hotspots. Done before the early return so
            // that walking out over empty ground still counts as having left.
            if (_lastChunk != chunk) {
                if (_lastChunk is (int x, int y) left) {
                    LeaveEncounterChunk(left.x, left.y);
                }
                _lastChunk = chunk;
                ClearTransientHotspotFlags();
            }

            // *** A DOWNED PARTY TRIGGERS NOTHING — BUT STILL FALLS INTO PITS. ***
            // WORLDLP.C calls worldcross_dungeon_descent_anim BEFORE the party-down guard and gates
            // hotspotevt_activate_at_player behind it. Our pit lives in PartyMovement, on the other
            // side of this call, so guarding here reproduces that split by construction: put the
            // guard around the whole world tick instead and a downed party stops falling too.
            // The step is KEPT (true) rather than rolled back — nothing triggering is not a refusal.
            if (!PartyDownState.HotspotsStillFire(_session?.PartyDeathState ?? PartyDownState.Standing)) {
                return true;
            }

            if (!_byChunk.TryGetValue(chunk, out List<TileEventTrigger> triggers)) {
                return true;
            }

            _currentRefIndex = _refIndex.TryGetValue(chunk, out int slot) ? slot : 0;
            int subX = Floor(worldX, CellSize) % CellsPerChunk;
            int subY = Floor(worldY, CellSize) % CellsPerChunk;

            bool keep = new HotspotActivator(this).ActivateAt(triggers, subX, subY, Pending);
            if (Pending.Count > 0) {
                HotspotDispatchResult dispatched = new HotspotDispatcher(this).Dispatch(triggers, Pending);

                // Name the kinds, not just the count: the count says something is unwired, the kind
                // says which task it belongs to. Without it, answering "what was that 1?" meant
                // hand-matching the party's sub-tile against the chunk's T-file.
                if (_logger != null) {
                    LoggerExtensions.LogInformation(_logger,
                        "Hotspots at chunk {Chunk} sub ({X},{Y}): {Fired} fired, {Unhandled} did not act{Kinds}{Refused}{Halted}.",
                        chunk, subX, subY, dispatched.Fired, dispatched.Unhandled,
                        DescribeKinds(dispatched.UnhandledKinds),
                        // A refusal is neither of the two counts above, so without this the one
                        // summary of a pass reports "0 fired, 0 did not act" and looks like an
                        // empty pass rather than a rule turning something down.
                        dispatched.Refused == 0
                            ? string.Empty
                            : $", {dispatched.Refused} refused for want of ground",
                        dispatched.Halted ? ", pass halted" : string.Empty);
                }
            }
            return keep;
        }

        /// <summary>Renders the unhandled kinds as " (Comb, Zone)", or nothing when there are none.</summary>
        private static string DescribeKinds(
            System.Collections.Generic.IReadOnlyList<GameData.Resources.World.TileEventType> kinds) =>
            kinds == null || kinds.Count == 0
                ? string.Empty
                : " (" + string.Join(", ", kinds) + ")";

        // --- IHotspotHost -----------------------------------------------------------------

        public int ReadGlobal(int key) => _session?.GetGlobalValue(key) ?? 0;

        public void WriteGlobal(int key, int value) => _session?.SetGlobalValue(key, value);

        public int DoneFlagKey(int hotspotIndex) => 400 * _zone + 10 * _currentRefIndex + hotspotIndex;

        // HOTSPOT_SCOUT_TRIED(i) — a small transient block, one slot per hotspot in the chunk.
        public int ScoutTriedFlagKey(int hotspotIndex) =>
            GameData.Resources.World.WorldTileCache.ScoutTriedFlagKey(hotspotIndex);

        /// <summary>
        /// <c>HOTSPOT_SCOUTED(idx)</c> — the party SPOTTED this encounter, as opposed to merely
        /// having spent its roll on it.
        /// </summary>
        /// <remarks>
        /// <b>Two flags ten apart, and they mean different things.</b> 5200 says a roll was tried;
        /// this one says it succeeded. Only the second earns the Stealth attempt to slip past later
        /// (<see cref="GameData.Resources.World.CombatEncounterAvoidance.MayAttempt"/>), so
        /// conflating them would let a party that failed to spot an ambush sneak around it anyway.
        /// </remarks>
        public int ScoutedFlagKey(int hotspotIndex) =>
            GameData.Resources.World.WorldTileCache.ScoutedFlagKey(hotspotIndex);

        public void PlayDialog(uint dialogId, bool modal) {
            // Fire-and-forget: the original blocks the world loop here, but PartyMovement.Step is a
            // synchronous call from the input driver. The dialog still appears over the HUD.
            _dialogs?.ShowById((int)dialogId).Forget();
        }

        public uint BlockDialogId(TileEventTrigger trigger) {
            if (_bloc == null || trigger.EntryNumber >= (uint)_bloc.Records.Count) {
                return 0;
            }
            return _bloc.Records[(int)trigger.EntryNumber].Payload.DialogId;
        }

        /// <summary>
        /// The DEF record's "avoidable" bit — <c>def_comb +0x18D</c> / <c>def_trap +0x197</c>, the
        /// same gate in both families. When clear the encounter fires unconditionally and no
        /// scouting roll is offered at all.
        /// </summary>
        /// <inheritdoc/>
        public int TownSceneNumber(TileEventTrigger trigger) => trigger.Type switch {
            TileEventType.Town => TownRecord(trigger)?.GdsSceneNumber ?? 0,
            TileEventType.Bkgr => BkgrRecord(trigger)?.GdsSceneNumber ?? 0,
            _ => 0,
        };

        /// <inheritdoc/>
        public uint TownDialogId(TileEventTrigger trigger) => trigger.Type switch {
            TileEventType.Town => TownRecord(trigger)?.DialogId ?? 0u,
            TileEventType.Bkgr => BkgrRecord(trigger)?.DialogId ?? 0u,
            _ => 0u,
        };

        /// <inheritdoc/>
        public void ApproachBeforeLocation(TileEventTrigger trigger) {
            int offset;
            int heading;
            bool walk;
            switch (trigger.Type) {
                case TileEventType.Town: {
                    DefTownEntry r = TownRecord(trigger);
                    if (r == null) return;
                    offset = r.ApproachTileOffset;
                    heading = r.ApproachHeading;
                    walk = r.DoApproachWalk != 0;
                    break;
                }
                case TileEventType.Bkgr: {
                    DefBkgrEntry r = BkgrRecord(trigger);
                    if (r == null) return;
                    offset = r.ApproachTileOffset;
                    heading = r.ApproachHeading;
                    walk = r.DoApproachWalk != 0;
                    break;
                }
                default:
                    return;
            }

            if (!walk || _session == null) {
                return;
            }

            // The destination is relative to the tile the party is standing on, so read that first.
            int tileX = GameData.Resources.World.WorldPlacement.TileOf(_session.PositionX);
            int tileY = GameData.Resources.World.WorldPlacement.TileOf(_session.PositionY);
            (long x, long y) = TownApproach.DestinationOf(tileX, tileY, offset);

            // ponytail: placed, not walked — the party ends at the right spot, but without the
            // original's step-by-step approach animation. Upgrade to an animated travel when the
            // movement seam can be driven toward a destination; the end pose is already faithful.
            _session.PositionX = (int)x;
            _session.PositionY = (int)y;
            _session.Rotation = GameData.Resources.Scene.TownApproach.FacingAfter(heading);
        }

        private DefZoneEntry ZoneRecord(TileEventTrigger trigger) =>
            _zoneDef != null && trigger.EntryNumber < (uint)_zoneDef.Records.Count
                ? _zoneDef.Records[(int)trigger.EntryNumber].Payload
                : null;

        /// <inheritdoc/>
        public bool ZoneCrossingIsOffered(TileEventTrigger trigger) {
            DefZoneEntry record = ZoneRecord(trigger);
            return record != null && GameData.Resources.World.ZoneTriggerRules.CanCross(record.DialogId1);
        }

        /// <inheritdoc/>
        public void OfferZoneCrossing(TileEventTrigger trigger, int hotspotIndex) {
            DefZoneEntry record = ZoneRecord(trigger);
            if (record == null) {
                return;
            }

            CrossZoneAsync(record, trigger, hotspotIndex).Forget();
        }

        /// <summary>
        /// Ask, then cross.
        /// </summary>
        /// <remarks>
        /// The prompt is the only place in the hotspot system that needs a dialog's ANSWER rather
        /// than just showing one, which is why this is the one arm that awaits instead of
        /// fire-and-forgetting. Everything after the answer — the arrival message, the move, the
        /// on-fire and done flags — is phase 2, and none of it happens on a no.
        /// </remarks>
        private async UniTask CrossZoneAsync(DefZoneEntry record, TileEventTrigger trigger, int hotspotIndex) {
            if (_dialogs == null) {
                return;
            }

            // The confirm path, not the plain one: this is the only hotspot that needs a dialog's
            // ANSWER. Its true is the original's `result == 0`, the same polarity the boundary wants.
            bool accepted = await _dialogs.ShowConfirmById((int)record.DialogId1);
            if (!GameData.Resources.World.ZoneTriggerRules.CrossesAfterPrompt(record.DialogId1, accepted)) {
                return;
            }

            if (GameData.Resources.World.ZoneTriggerRules.AnnouncesArrival(record.DialogId2)) {
                // A statement, not a question — the original discards its result too.
                await _dialogs.ShowById((int)record.DialogId2);
            }

            Core.Services.IGameFlow flow = _flowAccessor?.Invoke();
            if (flow != null) {
                await flow.TransitionTo(record.Location);
            }

            HotspotRules.ApplyOnFire(this, trigger);
            if (trigger.Repeatable == 0) {
                if (trigger.FireOnce != 0) {
                    WriteGlobal(DoneFlagKey(hotspotIndex), 1);
                }
                MarkActedThisChunk(hotspotIndex);
            }
        }

        /// <inheritdoc/>
        public void OfferTownEntry(TileEventTrigger trigger, int gdsSceneNumber, int hotspotIndex) {
            EnterTownAsync(trigger, gdsSceneNumber, hotspotIndex).Forget();
        }

        /// <summary>
        /// Ask, then enter.
        /// </summary>
        /// <remarks>
        /// <b>The answer is the branch POSITION, not a bool.</b> Both shipped town prompts
        /// (1500008 LaMut, 1500085 Northwarden) are <c>TextWithChoice</c> entries whose two
        /// conditional branches carry keyword flags 256 (Yes) and 257 (No) and go nowhere, so
        /// <c>dialog_play_record</c> hands back 0 or 1 — and the original's gate is <c>== 0</c>.
        /// <see cref="IDialogManager.ShowChoiceIndexById"/> is the call that speaks in positions;
        /// <c>ShowConfirmById</c> would answer a bool about the first branch, which is the same
        /// thing here only by accident.
        ///
        /// <para><b>No dialog means no entry</b>, which is the original's <c>else</c> arm rather
        /// than an omission: <c>*out_confirmed = 0</c>. All thirteen shipped records name one, so it
        /// cannot bite — but a mod that dropped the dialog would find the gate shut, exactly as the
        /// engine would.</para>
        /// </remarks>
        private async UniTask EnterTownAsync(
            TileEventTrigger trigger, int gdsSceneNumber, int hotspotIndex) {
            uint dialogId = TownDialogId(trigger);
            if (dialogId == 0 || _dialogs == null) {
                return;
            }

            if (await _dialogs.ShowChoiceIndexById((int)dialogId) != 0) {
                return;
            }

            ApproachBeforeLocation(trigger);
            EnterLocation(gdsSceneNumber);
            HotspotRules.ApplyOnFire(this, trigger);

            if (trigger.Repeatable == 0) {
                if (trigger.FireOnce != 0) {
                    WriteGlobal(DoneFlagKey(hotspotIndex), 1);
                }
                MarkActedThisChunk(hotspotIndex);
            }
        }

        /// <inheritdoc/>
        public void EnterLocation(int gdsSceneNumber) {
            if (_locations == null) {
                if (_logger != null) {
                    LoggerExtensions.LogWarning(_logger,
                        "Hotspots: no location player; GDS scene {Scene} not entered.", gdsSceneNumber);
                }
                return;
            }

            if (_logger != null) {
                LoggerExtensions.LogInformation(_logger, "Hotspots: entering GDS scene {Scene}.", gdsSceneNumber);
            }
            _locations.RunAsync(gdsSceneNumber, 1).Forget();
        }

        private DefTownEntry TownRecord(TileEventTrigger trigger) =>
            _town != null && trigger.EntryNumber < (uint)_town.Records.Count
                ? _town.Records[(int)trigger.EntryNumber].Payload
                : null;

        private DefBkgrEntry BkgrRecord(TileEventTrigger trigger) =>
            _bkgr != null && trigger.EntryNumber < (uint)_bkgr.Records.Count
                ? _bkgr.Records[(int)trigger.EntryNumber].Payload
                : null;

        public bool IsAmbush(TileEventTrigger trigger) => trigger.Type switch {
            TileEventType.Comb => CombRecord(trigger)?.Avoidable ?? false,
            TileEventType.Trap => TrapRecord(trigger)?.Avoidable ?? false,
            _ => false,
        };

        /// <summary>
        /// <c>hotspotevt_enc_fought_read</c>: has this encounter already been fought? An index
        /// outside the table reports true, so a record pointing nowhere never fires.
        /// </summary>
        public bool EncounterFought(TileEventTrigger trigger) {
            long? encounter = EncounterNumberOf(trigger);
            if (!encounter.HasValue) {
                // No DEF record to read: treat as fought so a missing table cannot spawn an
                // encounter we know nothing about.
                return true;
            }
            if (HotspotRules.EncounterIndexOutOfRange(encounter.Value)) {
                return true;
            }
            return ReadGlobal(HotspotRules.EncounterFoughtKey(encounter.Value)) != 0;
        }

        private CombatRuntime _combat;

        // Which encounter the live fight belongs to, so its outcome can be settled when it ends.
        // -1 outside a fight; the encounter number is what every one of the aftermath's writes is
        // keyed by, and nothing else in a running fight carries it.
        private long _fightingEncounter = -1;

        // The hotspot behind that fight. Its post event and its done flag are written when the
        // fight RESOLVES, not when it starts, so both have to survive the fight — and neither is
        // reachable from the encounter number.
        private TileEventTrigger _fightingTrigger;
        private int _fightingHotspotIndex = -1;

        /// <summary>
        /// Where the party stood before a trap yanked them onto its landing, or null.
        /// </summary>
        /// <remarks>
        /// <b>Only a FLIGHT reads it.</b> The original restores this before choosing the exit
        /// landing and leaves a won fight standing where the trap put them, so this is not "undo the
        /// spring" — it is "measure the way out from where they came in".
        /// </remarks>
        private (int X, int Y, short Rotation)? _sprungFrom;

        /// <summary>
        /// The live fight, created on first use.
        /// </summary>
        /// <remarks>
        /// <b>Owned here for now because nothing else owns one.</b> A fight is session state, so
        /// there must be exactly one; when a combat screen arrives it should resolve this rather
        /// than construct its own, or the UI and the world would each hold a different fight.
        /// </remarks>
        public CombatRuntime Combat =>
            _combat ??= NewCombatRuntime();

        /// <summary>Whether a fight is on the field right now.</summary>
        /// <remarks>
        /// <b>Read by the world loop's exit test</b>, because in the original a fight CANNOT be
        /// interrupted by it: <c>combat_arena_run</c> is a nested call inside the world loop, so
        /// <c>post_dispatch</c> is not reached until the arena has returned and torn itself down.
        /// Our fight runs beside a per-frame <c>Update</c> instead, so the same guarantee has to be
        /// asked for. Reads <see cref="CombatRuntime.InCombat"/> WITHOUT creating a runtime — the
        /// <see cref="Combat"/> property builds one on first touch, and a test for "is there a
        /// fight" must not start one.
        /// </remarks>
        public bool FightInProgress => _combat != null && _combat.InCombat;

        /// <summary>
        /// Spring the DEF_TRAP trigger covering one sub-tile of the party's current chunk — the
        /// spawn a trapped container performs when it is opened.
        /// </summary>
        /// <remarks>
        /// <b>The coordinates are SUB-TILE, not world or tile.</b> The original reaches this through
        /// <c>sub_stub187_34(def_trap_dat, X, Y, &amp;out)</c> @0x73c46, whose lookup
        /// <c>get_applicable_def_struct</c> @0x74cfa matches
        /// <c>start_x &lt;= x &lt;= end_x &amp;&amp; start_y &lt;= y &lt;= end_y</c> against the loaded DEF
        /// structs — the same inclusive sub-tile ranges <see cref="TileEventTrigger"/> carries and
        /// <see cref="HotspotActivator"/> already matches. The X and Y come from the container's own
        /// encounter record, NOT from the object's position, so a spawn keyed off the clicked
        /// entity's coordinates would look right and fire the wrong trap (or none).
        ///
        /// <para><b>Only Trap triggers are considered</b>, matching the original's
        /// <c>type == defType</c> filter: the same tile may carry a Comb trigger the player has not
        /// walked onto, and springing that instead would start a different fight.</para>
        ///
        /// <para>The caller disposes the container FIRST — see
        /// <c>GraveDigging.TrapDisposesTheGraveFirst</c>. Spawning first would leave the old object
        /// standing beside whatever the trap created.</para>
        /// </remarks>
        /// <returns>Whether a fight is now on the field.</returns>
        /// <summary>
        /// The triggers of the chunk the party is standing in, or null when there are none.
        /// </summary>
        /// <remarks>
        /// *** THE PARTY'S POSITION, NOT <c>_lastChunk</c>. *** <c>_lastChunk</c> records what the
        /// ACTIVATE pass last visited and is **null until the party has taken a step**, so anything
        /// keyed on it silently answers "no triggers here" for the whole window between loading a
        /// save and the first movement. A click on the Mac Mordain Cadal's stairs in that window
        /// therefore does nothing at all — which is exactly the "nothing happens" TASK-400 reports,
        /// and it survived the routing fix because the routing was only half of it.
        ///
        /// <para>The same trap was found and fixed once already in this file, for the placed-actor
        /// list: "depending on it made the first draw of a zone empty and the second one correct".
        /// Three click-dispatch sites were left on the cached value; this is the shared answer so a
        /// fourth cannot be written.</para>
        /// </remarks>
        private List<TileEventTrigger> TriggersWhereThePartyStands() {
            if (_session == null) {
                return null;
            }
            var chunk = (x: Floor(_session.PositionX, ChunkSize), y: Floor(_session.PositionY, ChunkSize));
            return _byChunk.TryGetValue(chunk, out List<TileEventTrigger> triggers) ? triggers : null;
        }

        /// <summary>What a clicked object's trap hotspot answered — <c>hotspotevt_dispatch_at_point(7, …)</c>.</summary>
        public enum TrapDispatch {
            /// <summary>No trap hotspot covers the point (the dispatch returned 0): the click plays 154 and stops.</summary>
            NoTrap,

            /// <summary>The out flag is set: the trap fired, or an available ambush was armed (spotted or not).</summary>
            Blocks,

            /// <summary>A trap is there but is spent or unavailable: the click carries on.</summary>
            Proceed,
        }

        public TrapDispatch FireTrapEncounterAt(int subX, int subY) {
            List<TileEventTrigger> triggers = TriggersWhereThePartyStands();
            if (triggers == null) {
                return TrapDispatch.NoTrap;
            }

            // The whole tile table, filtered inside the walk: the done/tried flags are keyed by the
            // TABLE index, which a pre-filtered list would renumber (HOTSPOT.C:228).
            Pending.Clear();
            var activator = new HotspotActivator(this);
            activator.ActivateAt(triggers, subX, subY, Pending, TileEventType.Trap);
            if (activator.Matched == 0) {
                return TrapDispatch.NoTrap;
            }

            // HOTSPOT.C:231-239 and :654-696: the out flag is the ambush-armed flag, raised again when the
            // trap fires. A spotted ambush arms without firing, and still stops the grave or building.
            bool fired = Pending.Count > 0;
            if (fired) {
                new HotspotDispatcher(this).Dispatch(triggers, Pending);
                Pending.Clear();
            }
            return activator.AmbushArmed || fired ? TrapDispatch.Blocks : TrapDispatch.Proceed;
        }

        /// <summary>
        /// Cross a zone from a clicked object's own hotspot key —
        /// <c>hotspotevt_dispatch_at_point(8, x, y)</c>, WCURSOR.C:902.
        /// </summary>
        /// <remarks>
        /// <b>The sibling of <see cref="FireTrapEncounterAt"/>, and it exists for the same reason:
        /// the (x,y) is the OBJECT'S, not the party's.</b> A tunnel's <c>SUBREC_HOTSPOT</c> carries
        /// a grid coordinate that names which of the tile's hotspots to run, and
        /// <c>hotspotevt_find_at_point</c> does a plain bbox containment on it. Nothing requires the
        /// party to be standing there — which is just as well, because in the Mac Mordain Cadal it
        /// cannot get there at all.
        ///
        /// <para><b>That is why every zone-10 <c>Zone</c> trigger carries the same
        /// (37,37)-(39,39).</b> Six tiles, four chapters, one rectangle: underground it is a lookup
        /// key rather than a strip of ground, and the mine's stairs hold (38,38) in their own
        /// record. Read as geometry it says the exit is 32000 units inside a wall.</para>
        ///
        /// <para><b>Only <c>Zone</c> triggers are considered</b>, matching the original's
        /// <c>hotspot->wKind == target_type</c> filter — the same tile may carry a Comb or Dial the
        /// key's rectangle also covers, and running one of those instead would be a different event
        /// entirely.</para>
        /// </remarks>
        /// <returns>Whether a crossing was offered.</returns>
        public bool FireZoneCrossingAt(int subX, int subY) {
            List<TileEventTrigger> triggers = TriggersWhereThePartyStands();
            if (triggers == null) {
                return false;
            }

            // *** THE TABLE INDEX, NOT A FILTERED ONE. *** This built a Zone-only list, so the stairs
            // out of the Diviner's Halls (tile (13,15), Zone 38 at table index 4) read index 0's done
            // flag -- a Comb already fought -- and answered "not very important".
            Pending.Clear();
            new HotspotActivator(this).ActivateAt(triggers, subX, subY, Pending, TileEventType.Zone);
            if (Pending.Count == 0) {
                return false;
            }

            HotspotDispatchResult dispatched = new HotspotDispatcher(this).Dispatch(triggers, Pending);
            Pending.Clear();
            return dispatched.Fired > 0;
        }

        /// <summary>
        /// Turn a Comb or Trap trigger into a fight: encounter number, then the actor slots it
        /// names, then their stats.
        /// </summary>
        /// <remarks>
        /// <b>Three tables, keyed differently.</b> The DEF record gives the encounter number, which
        /// indexes the 700 enemy-party records; those hold actor slots, which index the 1730-slot
        /// actor table.
        ///
        /// <para><b>Naming nobody is a refusal only when there is also no puzzle.</b> A trap room's
        /// encounter routinely names no enemies — 25 of the 32 crystal puzzles do — and is entered
        /// anyway, because the puzzle is the encounter and the exit is the win condition. See the
        /// guard's own comment.</para>
        /// </remarks>
        public bool StartCombat(TileEventTrigger trigger, int hotspotIndex = -1) {
            long? encounter = EncounterNumberOf(trigger);
            if (!encounter.HasValue || HotspotRules.EncounterIndexOutOfRange(encounter.Value)) {
                return false;
            }

            IReadOnlyList<short> roster = _session?.RosterOf((int)encounter.Value);
            GameData.Resources.Combat.TrapPuzzle puzzle = PuzzleFor((int)encounter.Value);

            // *** AN EMPTY ROSTER IS NORMAL FOR A TRAP PUZZLE. *** This guard used to refuse on a
            // null roster alone, which was right while only Comb triggers reached here — an
            // encounter naming no monsters is nothing to fight. Trap triggers reach it too, and
            // *** 25 of the 32 crystal puzzles name no enemies at all ***: the puzzle IS the
            // encounter, and the party wins by reaching the exit rather than by killing anybody.
            // Refusing them left every unopposed puzzle unenterable, which is why encounter 15 —
            // the one captured from the original — could not be opened here at all.
            //
            // Safe because the model already expects it:
            // CombatEncounter.IsOver() is `PartyAlive() == 0 || (EnemiesAlive() == 0 &&
            // !HasObjective)`, so a puzzle with an exit does not end the moment it opens, and
            // EnterRoster tolerates a null slot list by building an empty enemy list.
            bool isPuzzle = puzzle != null
                && GameData.Resources.Combat.TrapPuzzleGoal.IsTrapPuzzle(puzzle.Grid);
            if (roster == null && !isPuzzle) {
                _logger?.LogWarning("Encounter {Encounter} names no actors; not opening a fight.",
                    encounter.Value);
                return false;
            }

            // The layout now runs from inside EnterRoster, before anybody is placed — see
            // CombatRuntime.LayOutGround. Calling it here as well would re-prune a grid that
            // already has combatants standing on it, which is the bug it was moved to avoid.
            Combat.EnterRoster(roster, puzzle, EncounterAddressFor(encounter.Value),
                (int)encounter.Value);

            // *** THE ENCOUNTER'S FIRST MONSTER NAMES THE FIGHT. *** Both of the original's
            // encounter arms set nEvtArgAux1 from pCombatants[0].wMonster_index immediately before
            // playing the entry dialog and opening the arena (HOTSPOT.C:525 and :777), and 72 DDX
            // records read it -- "The @1 shouted in surprise", "eyeing the @1's corpse". Without it
            // every one of them says INVALID MONSTER. Inspect narrows it to the actor under the
            // cursor afterwards, which is the same variable used more precisely.
            GameData.Resources.Combat.Combatant first =
                Combat.Encounter?.Enemies is { Count: > 0 } enemies ? enemies[0] : null;
            if (first != null) {
                _session?.SetDialogCreatureType(first.ClassId);
            }

            Combat.PartyHasTheDrop = RollEncounterOpening(encounter.Value);
            _fightingEncounter = encounter.Value;
            _fightingTrigger = trigger;
            _fightingHotspotIndex = hotspotIndex;
            _setCombatMusic?.Invoke(true);
            // ShowCombatHud goes UNDER the cover, not after it — see SwapArenaThroughFade.
            // The record's main-fire line is spoken BEFORE the fade, over the travel view, which is
            // where HOTSPOT.C:526-528 puts it.
            SwapArenaThroughFade(true, ShowCombatHud, EntryDialogId(trigger));
            return true;
        }

        /// <summary>
        /// Which creature to draw for a combatant, or -1 when it cannot be told.
        /// </summary>
        /// <remarks>
        /// <b>The two sides put different things in <c>ClassId</c>.</b> For an enemy it is already
        /// the creature number; for a party member it is a character index, and the translation
        /// lives in P1.DAT — <c>SaveGameCombatData.CreatureType</c>, the field beside the entry
        /// tiles this class has been reading all along.
        ///
        /// <para>-1 rather than a fallback: creature 0 exists, and a party member with no record is
        /// better left undrawn than drawn as whatever sits at zero.</para>
        /// </remarks>
        private int CreatureNumberFor(GameData.Resources.Combat.Combatant c) {
            if (!c.IsPartyMember) {
                return c.ClassId;
            }
            // EntryFor is 1-based; ClassId carries the 0-based character index on this side.
            GameData.Resources.Data.SaveGameCombatData entry = _partyEntries?.EntryFor(c.ClassId + 1);
            return entry?.CreatureType ?? -1;
        }

        /// <summary>
        /// Where this encounter's actor states live in the save — the ref pair and the record index.
        /// </summary>
        /// <remarks>
        /// <b>Both halves come from the CHUNK, not from the encounter number.</b> The ref pair is
        /// the chunk's slot in <c>Z##REF.DAT</c>, which is already the number
        /// <see cref="DoneFlagKey"/> multiplies by ten; the record index is the encounter's position
        /// in the chunk's own encounter list, built by walking its triggers in order
        /// (<see cref="GameData.Resources.World.EncounterReset.RecordIndexOf"/>).
        ///
        /// <para><b>An encounter number is NOT a record index.</b> It indexes the 700 enemy-party
        /// records; the state block holds five records per ref pair. Using one as the other would
        /// write far outside the block for all but the lowest-numbered encounters.</para>
        ///
        /// <para>Returns <c>None</c> when the chunk is unknown or the encounter did not make the
        /// list — an answer, not a failure: nothing is then persisted, which is what the original
        /// does with the same condition.</para>
        /// </remarks>
        private GameData.Resources.World.EncounterActorPersistence.RecordAddress EncounterAddressFor(
            long encounterNumber) {
            // *** _lastChunk HERE, unlike the two CLICK dispatches above. *** This asks which
            // chunk's trigger list the encounter fired in, and the activate pass is the authority
            // for that — not where the party happens to be standing, which a retreat's relocation
            // can already have changed. WithNoChunkEstablishedThereIsNoAddressEither fences it:
            // before any step there is no list to count positions in, and "we do not know where
            // this goes" is the original's answer too.
            if (!_lastChunk.HasValue
                || !_byChunk.TryGetValue(_lastChunk.Value, out List<TileEventTrigger> triggers)) {
                return GameData.Resources.World.EncounterActorPersistence.RecordAddress.None;
            }

            var inOrder = new List<(GameData.Resources.World.TileEventType, long?)>(triggers.Count);
            foreach (TileEventTrigger t in triggers) {
                inOrder.Add((t.Type, EncounterNumberOf(t)));
            }

            int record = GameData.Resources.World.EncounterReset.RecordIndexOf(inOrder, encounterNumber);
            return record < 0
                ? GameData.Resources.World.EncounterActorPersistence.RecordAddress.None
                : new GameData.Resources.World.EncounterActorPersistence.RecordAddress(
                    _currentRefIndex, record);
        }

        /// <summary>
        /// Which encounter actors stand in the current chunk, seeded and placed —
        /// <c>rgnenc_load_encounter_actors</c>.
        /// </summary>
        /// <remarks>
        /// <b>Rebuilt for a chunk, not held across the world.</b> The block is addressed per ref
        /// pair and the placement origin is the party's tile, so a list built in one chunk means
        /// nothing in the next.
        ///
        /// <para><b>The seed runs at most once per ref pair, ever</b>, and it mutates the save
        /// block — so this is not a pure query, and calling it on every frame would be wrong for
        /// reasons of correctness rather than cost. It belongs on a chunk change.</para>
        ///
        /// <para>Nothing draws these yet (TASK-103) and nothing steps them yet — this is the list
        /// those need, and the one <see cref="GameData.Resources.Combat.EncounterCorpseLoot"/> will
        /// look a body up in.</para>
        /// </remarks>
        /// <summary>
        /// Where the fight's ENEMIES stand in the world, for the arena to draw.
        /// </summary>
        /// <remarks>
        /// <b>Built from the grid every time rather than cached</b> — combatants move, and a cached
        /// list would draw last round's positions. The encounter-actor list next to this one is
        /// cached for the opposite reason: that pass mutates the save block.
        ///
        /// <para><b>The offsets are <see cref="GameData.Resources.Combat.CombatArenaPlacement"/>'s,
        /// not the ground sweep's.</b> The two differ by half a cell in the forward direction, and
        /// the sweep's version would stand every actor slightly too near the party — see that type.
        /// The party's heading is applied here, the same rotation the sweep uses, so the arena and
        /// the ground it was tested on face the same way.</para>
        ///
        /// <para><b>Both sides, but they name their creature differently.</b> An enemy's
        /// <c>ClassId</c> IS the creature number, read from the actor table when the roster was
        /// resolved. A party member's is a character index, and the actor table's low slots are
        /// monsters — so passing one straight through draws a goblin for Locklear. The character's
        /// own creature number is in P1.DAT, which this class already loads for the entry tiles:
        /// <c>SaveGameCombatData.CreatureType</c>, holding 17/15/16/45/51/47 for Locklear, Gorath,
        /// Owyn, Pug, James and Patrus. It was parsed and unread until now.</para>
        /// </remarks>
        public List<GameData.Resources.World.EncounterActorPlacement.Placed> PlaceCombatants() {
            var placed = new List<GameData.Resources.World.EncounterActorPlacement.Placed>();
            GameData.Resources.Combat.CombatEncounter fight = Combat?.Encounter;
            if (fight == null || _session == null || _start == null) {
                return placed;
            }

            int cellSize = _start.CombatGridCellSize;
            if (cellSize <= 0) {
                return placed;
            }

            foreach (GameData.Resources.Combat.Combatant c in fight.AllCombatants()) {
                if (c == null) {
                    continue;
                }
                // *** A DEAD COMBATANT IS NOT NECESSARILY GONE. *** Only one of the three death
                // outcomes leaves a body; the other two take the creature off the field entirely.
                // Skipping every dead one made a kill look like a disappearance and left the loot
                // screen with nothing to open.
                bool downed = c.IsDead;
                if (downed && !Combat.LeavesCorpse(c)) {
                    continue;
                }
                int creature = CreatureNumberFor(c);
                if (creature < 0) {
                    continue;
                }
                (int across, int away) =
                    GameData.Resources.Combat.CombatArenaPlacement.CellOffset(c.X, c.Y, cellSize);
                var (dx, dy) = Collision.ProximityMath.Rotate(across, away, _session.Rotation);
                placed.Add(new GameData.Resources.World.EncounterActorPlacement.Placed(
                    // *** PartySlot IS ZERO FOR EVERY ENEMY. *** It names a place in the marching
                    // order, which a monster has none of, so both mordels of an encounter came out
                    // as "#0" — wrong for the sprite's name and useless as identity. An enemy's
                    // place is its index in the encounter's roster, which is also the x its corpse
                    // container is keyed by: GetLiveContainerAt(100, index, encounterNumber).
                    rosterSlot: c.IsPartyMember ? c.PartySlot : fight.Enemies.IndexOf(c),
                    creatureNumber: creature,
                    worldX: _session.PositionX + dx,
                    worldY: _session.PositionY + dy,
                    facing: ArenaSpriteYaw(c),
                    roams: false,
                    downed: downed,
                    // Which list rosterSlot indexes. Without it a click on a target could not tell
                    // party slot 0 from enemy slot 0 — see Placed.PartyMember.
                    partyMember: c.IsPartyMember));
            }
            return placed;
        }

        /// <summary>
        /// The world rotation an arena sprite is drawn at — the original's own expression, from
        /// <c>combat_actor_deploy_encounter</c> @0x5C845:
        /// <c>(facingOctant &lt;&lt; 13) + (cameraYaw &amp; 0xE000) + 0x8000</c>.
        /// </summary>
        /// <remarks>
        /// <b>The party's heading is the arena camera's yaw</b>, which is why it appears here as the
        /// camera term. Snapping it to an octant and adding half a turn is what the original does,
        /// and the half turn is why octant 0 faces the viewer.
        ///
        /// <para><b>This used to be plain <c>_session.Rotation</c> for every combatant.</b> The
        /// camera looks along that heading, so every sprite resolved to the same octant and the
        /// whole arena stood in one identical profile — TASK-324, spotted in the encounter-15
        /// comparison against the original.</para>
        ///
        /// <para>Only the ARENA path uses this. <see cref="PlaceEncounterActors"/> places actors
        /// standing in the world, whose facing is a real world heading and is still resolved against
        /// the camera geometrically — that is correct for them and untouched.</para>
        ///
        /// <para><b>Octant 4 is the actor's BACK and octant 0 its face</b> — the original's numbering
        /// (<see cref="GameData.Resources.Combat.ArenaFacing.OctantToward"/>), so the deployed party
        /// (4) is drawn from behind and the deployed enemies (0) face it. The original hands its result to a renderer that compares the sprite's world
        /// rotation against <c>camera.rotation3d.z</c> directly; ours goes to
        /// <see cref="GameData.Resources.World.EncounterActorPose.Octant"/>, which takes the heading
        /// from the actor <i>to the camera</i> — already half a turn from the camera's own yaw — and
        /// then subtracts its own <see cref="GameData.Resources.World.EncounterActorPose.QuarterTurn"/>.
        /// The two frames therefore differ, and the offset is calibrated rather than derived.
        ///
        /// <para><b>Established from the ART, after getting it backwards once.</b> <c>OWN1.BMX</c>'s
        /// walk columns are the five facings: column 0 draws Owyn's face (staff running upper-left to
        /// lower-right) and column 12 draws his back (staff lower-left to upper-right). The original's
        /// encounter-15 capture matches **column 12**. An earlier version of this method targeted
        /// column 0 and was "verified" against a 320x200 head too small to tell a face from the back
        /// of a skull — the sprite sheet is the reference, not the screenshot.</para>
        ///
        /// <para>Which is also the sensible reading: the arena camera looks over the party's
        /// shoulders at the enemies, so a combatant facing the fight is facing away from you.</para>
        /// </remarks>
        /// <summary>The arena yaw for a combatant, for callers re-aiming a live sprite.</summary>
        /// <remarks>The same expression the placement uses, so a turn mid-fight and a fresh
        /// draw cannot disagree about which way an actor is looking.</remarks>
        public short ArenaSpriteYawFor(GameData.Resources.Combat.Combatant c) => ArenaSpriteYaw(c);

        private short ArenaSpriteYaw(GameData.Resources.Combat.Combatant c) {
            const int OctantStep = 13;      // 0x2000 per octant
            const int OctantMask = 0xE000;  // snap a yaw to its octant
            // The original's own expression (combat_actor_deploy_encounter @0x5C909), half turn
            // included, on the original's octant numbering -- less the quarter turn this frame
            // differs by. Before 2026-09-18 the half turn was dropped and the octants mirrored
            // instead, which drew the party right and every enemy with its back to the party.
            const int HalfTurn = 0x8000;
            int yaw = (c.FacingOctant << OctantStep)
                + (_session.Rotation & OctantMask)
                + HalfTurn
                - GameData.Resources.World.EncounterActorPose.QuarterTurn;

            return unchecked((short)(yaw & 0xFFFF));
        }

        /// <summary>
        /// Where the puzzle's props stand, in world coordinates — the crystals, diamonds, cannons and
        /// burnt-out crystals a trap encounter is built from.
        /// </summary>
        /// <remarks>
        /// <b>An element id IS a <c>COMBAT.TBL</c> row.</b> That table's names say so outright: 7
        /// <c>redcry</c>, 8 <c>greencry</c>, 9 <c>mancrys</c>, 10 <c>wirecrys</c>, 11 <c>frpcnon</c>,
        /// and 0x28 <c>blackcry</c> — which is <see cref="GameData.Resources.Combat.CrystalChain.WreckElementId"/>,
        /// so the "wreck" id was never a sentinel but the black-crystal model's index. Nothing here
        /// needs a mapping table because the mapping is the identity.
        ///
        /// <para>The original reaches the same models through
        /// <c>combat_actor_grid_rndr_terr_prop?</c> @0x5DBBA, which draws them with the ordinary
        /// world-item calls (<c>worldRender_actorAtTile</c> / <c>worldRender_actorSwapTextures</c>)
        /// rather than any dedicated art — which is why no crystal bitmap exists to look for.</para>
        ///
        /// <para><b>Facing is read from the TERRAIN, not the element.</b> Every cannon is recorded as
        /// <see cref="GameData.Resources.Combat.TrapPuzzleBuilder.CannonElementId"/> whichever way it
        /// points, and the direction is kept as the cell's terrain kind (10..13 = W/E/N/S). The
        /// original encodes it the other way round — the grid element is 10..13 and it picks the
        /// rotation from that — so the two carry the same fact in different places. Read the wrong
        /// one and every cannon aims north.</para>
        ///
        /// <para>Placement is <see cref="PlaceCombatants"/>'s, deliberately: props and combatants
        /// share an arena, so a second copy of the cell-to-world math is a thing that can drift.</para>
        /// </remarks>
        public List<(int ElementId, long WorldX, long WorldY, ushort Facing, int GridX, int GridY)>
            PlaceTrapProps() {
            var props = new List<(int, long, long, ushort, int, int)>();
            GameData.Resources.Combat.TrapPuzzle puzzle = Combat?.Puzzle;
            if (puzzle?.Elements == null || _session == null || _start == null) {
                return props;
            }

            int cellSize = _start.CombatGridCellSize;
            if (cellSize <= 0) {
                return props;
            }

            foreach (GameData.Resources.Combat.TrapGridElement e in puzzle.Elements) {
                if (e == null || !HasPropModel(e.ElementId)) {
                    continue;
                }
                (int across, int away) =
                    GameData.Resources.Combat.CombatArenaPlacement.CellOffset(e.X, e.Y, cellSize);
                var (dx, dy) = Collision.ProximityMath.Rotate(across, away, _session.Rotation);
                props.Add((
                    e.ElementId,
                    _session.PositionX + dx,
                    _session.PositionY + dy,
                    CannonFacing(puzzle, e),
                    // The CELL as well as the world point: the emergence animation is per-tile
                    // state (TrapPropEmergence), and recomputing the cell from world coordinates
                    // would invert the rotation this method just applied.
                    e.X, e.Y));
            }
            return props;
        }

        /// <summary>
        /// The crystal run a walker is standing in, as world points (cell centres, in order) — what
        /// the zap cine lights up. The run is the straight line of crystal cells through the cell,
        /// across if a neighbour across is crystal, else along (<c>combatgrid_find_adj_pass_tile</c>).
        /// </summary>
        public List<(long X, long Y)> CrystalRunThrough(GameData.Resources.Combat.Combatant who) {
            var run = new List<(long, long)>();
            GameData.Resources.Combat.CombatGrid grid = Combat?.Grid;
            int cellSize = _start?.CombatGridCellSize ?? 0;
            if (who == null || grid == null || _session == null || cellSize <= 0) {
                return run;
            }
            bool across = grid.IsCrystal(who.X - 1, who.Y) || grid.IsCrystal(who.X + 1, who.Y);
            (int dx, int dy) = across ? (1, 0) : (0, 1);
            int x = who.X, y = who.Y;
            while (grid.IsCrystal(x - dx, y - dy)) {
                x -= dx;
                y -= dy;
            }
            for (; x == who.X && y == who.Y || grid.IsCrystal(x, y); x += dx, y += dy) {
                run.Add(CellCentre(x, y, cellSize));
                if (!GameData.Resources.Combat.CombatGrid.InBounds(x + dx, y + dy)) {
                    break;
                }
            }
            return run;
        }

        /// <summary>
        /// Where the spell-placed tiles stand, and which COMBAT.TBL shape draws each — Mirrorwall's
        /// kind 7 as <c>mrwall</c> (12), Gambit of the Eight's kind 8 as <c>blob</c> (13), per
        /// <c>CACTOR.C:1265-1272</c>. Only cells with a running timer: the arena's own walls are
        /// kind 7 too, and carry no timer.
        /// </summary>
        public List<(int Shape, long X, long Y)> PlaceSpellTiles() {
            var tiles = new List<(int, long, long)>();
            GameData.Resources.Combat.CombatGrid grid = Combat?.Grid;
            int cellSize = _start?.CombatGridCellSize ?? 0;
            if (grid == null || _session == null || cellSize <= 0) {
                return tiles;
            }
            for (var x = 0; x < GameData.Resources.Combat.CombatGrid.Width; x++) {
                for (var y = 0; y < GameData.Resources.Combat.CombatGrid.Height; y++) {
                    if (grid.EffectTimerAt(x, y) == GameData.Resources.Combat.CombatGrid.NoEffect) {
                        continue;
                    }
                    int shape = grid.TerrainAt(x, y) switch {
                        GameData.Resources.Combat.CombatTerrain.Wall => 0xc,
                        GameData.Resources.Combat.CombatTerrain.Trap => 0xd,
                        _ => -1,
                    };
                    if (shape < 0) {
                        continue;
                    }
                    (long wx, long wy) = CellCentre(x, y, cellSize);
                    tiles.Add((shape, wx, wy));
                }
            }
            return tiles;
        }

        /// <summary>This zone's tactical-grid pen, or null when GRID.DAT cannot answer for it.</summary>
        public int? CombatGridBorderPen =>
            GameData.Resources.Combat.CombatGridOutline.PenFor(_gridPens, _zone);

        /// <summary>
        /// Where the arena's tactical grid lines run, in world coordinates.
        /// </summary>
        /// <remarks>
        /// <b>Every fight, not just a trap puzzle.</b> The original draws this from the combat grid
        /// itself (<c>combatgrid_draw_terrain_walls</c>), so it reads <c>Combat.Grid</c> rather than
        /// <c>Combat.Puzzle</c> — a puzzle has extra terrain on that grid, it is not what makes the
        /// grid exist.
        ///
        /// <para>Placement is <see cref="PlaceTrapProps"/>'s, for the same reason that one gives:
        /// props, combatants and now these lines share an arena, and a second copy of the
        /// cell-to-world math is a thing that can drift. Corners rather than centres, via
        /// <c>CombatGridOutline.CornerOffset</c>.</para>
        /// </remarks>
        public List<(long X1, long Y1, long X2, long Y2)> PlaceCombatGridLines() {
            var lines = new List<(long, long, long, long)>();
            GameData.Resources.Combat.CombatGrid grid = Combat?.Grid;
            if (grid == null || _session == null || _start == null) {
                return lines;
            }

            int cellSize = _start.CombatGridCellSize;
            if (cellSize <= 0) {
                return lines;
            }

            foreach (GameData.Resources.Combat.CombatGridOutline.Edge e in
                     GameData.Resources.Combat.CombatGridOutline.Edges(grid)) {
                (int a1, int w1) =
                    GameData.Resources.Combat.CombatGridOutline.CornerOffset(e.X1, e.Y1, cellSize);
                (int a2, int w2) =
                    GameData.Resources.Combat.CombatGridOutline.CornerOffset(e.X2, e.Y2, cellSize);
                var (dx1, dy1) = Collision.ProximityMath.Rotate(a1, w1, _session.Rotation);
                var (dx2, dy2) = Collision.ProximityMath.Rotate(a2, w2, _session.Rotation);
                lines.Add((
                    _session.PositionX + dx1, _session.PositionY + dy1,
                    _session.PositionX + dx2, _session.PositionY + dy2));
            }
            return lines;
        }

        /// <summary>
        /// A flying effect passed over a world point — collapse a crystal chain if it crossed one.
        /// </summary>
        /// <returns>Whether a chain was collapsed, so the caller can redraw.</returns>
        /// <remarks>
        /// <b>Flamecast only, and that is from the original rather than a simplification.</b>
        /// <c>Spell_ApplyHitWithProjectile</c> maps spell id 4 to effect sprite type
        /// <see cref="GameData.Resources.Combat.CombatEffectSprite.Flamecast"/>, and only that type
        /// takes the crystal arm — a crossbow quarrel flies over a crystal and does nothing.
        ///
        /// <para><b>Each cell acts at most once per flight.</b> The flight reports a position every
        /// frame, so a slow shot crosses one cell many times; <see cref="_lastSweptCell"/> is what
        /// makes this a per-CELL rule rather than a per-frame one. Without it a single Flamecast
        /// would collapse the same chain repeatedly.</para>
        ///
        /// <para>The collapse itself is <c>crystalChain_collapseUntilIsolated</c> @0x2F259, already
        /// on <c>TrapPuzzle</c>: it erases the crystal GROUND along the run and then applies
        /// isolation at both ends independently. This adds no rule of its own — it decides only
        /// WHEN that procedure runs, which is what TASK-270 was missing.</para>
        /// </remarks>
        public bool ProjectilePassedOver(UnityEngine.Vector3 point, int effectId) {
            if (effectId != GameData.Resources.Combat.CombatEffectSprite.Flamecast) {
                return false;
            }
            GameData.Resources.Combat.TrapPuzzle puzzle = Combat?.Puzzle;
            if (puzzle == null || _session == null || _start == null) {
                return false;
            }
            int cellSize = _start.CombatGridCellSize;
            if (cellSize <= 0) {
                return false;
            }
            var bakX = (int)(point.x * World.Converters.BakCoordinateConverter.WorldScale);
            var bakY = (int)(point.z * World.Converters.BakCoordinateConverter.WorldScale);
            (int Column, int Row)? cell = World.Encounters.ArenaCellPicker.CellAt(
                bakX, bakY, (int)_session.PositionX, (int)_session.PositionY,
                _session.Rotation, cellSize);
            if (cell == null || cell.Value == _lastSweptCell) {
                return false;
            }
            _lastSweptCell = cell.Value;

            GameData.Resources.Combat.TrapGridElement standing =
                puzzle.ElementAt(cell.Value.Column, cell.Value.Row);
            if (standing == null
                || !GameData.Resources.Combat.CrystalChain.IsCrystalElement(standing.ElementId)) {
                return false;
            }

            return puzzle.CollapseUntilIsolated(cell.Value.Column, cell.Value.Row) > 0;
        }

        /// <summary>Reset the per-flight cell memory. Called as a shot launches.</summary>
        public void BeginProjectileSweep() => _lastSweptCell = (-1, -1);

        // The last cell a flying effect was seen in, so a crossing acts once rather than once per
        // frame. (-1,-1) is off the grid, so the first real cell of a flight always counts.
        private (int Column, int Row) _lastSweptCell = (-1, -1);

        /// <summary>
        /// The tactical overlay's line segments in world coordinates — the grid outline and the
        /// crystal links.
        /// </summary>
        /// <remarks>
        /// <b>Both halves of what the original draws behind one flag</b>:
        /// <c>drawCombatGridOutline</c> @0x2E8A4 and <c>crystalChain_drawLinks</c> @0x2FD88, gated
        /// together by <c>combatGridOverlayShown</c> and toggled with the <b>G</b> key.
        ///
        /// <para><b>The link rule is not restated here.</b> Pairs come from
        /// <see cref="GameData.Resources.Combat.TrapPuzzleBuilder.Aligned"/>, the same predicate the
        /// builder paints crystal ground with, so what is linked and what is drawn as linked cannot
        /// disagree.</para>
        ///
        /// <para><b>Each pair once.</b> The original's inner loop is not restricted to <c>b &gt; a</c>
        /// and so draws every link twice; with a solid pen that is invisible, and iterating once is
        /// the same picture for half the work.</para>
        ///
        /// <para>Grid lines run on cell BOUNDARIES, so they are the cell centre offset by half a
        /// cell — <see cref="GameData.Resources.Combat.CombatArenaPlacement.CellOffset"/> centres in
        /// both axes.</para>
        /// </remarks>
        /// <summary>
        /// The four edges of the cell the acting combatant stands on, or an empty list.
        /// </summary>
        /// <remarks>
        /// <b>Separate from <see cref="ArenaOverlaySegments"/> because it changes every turn.</b>
        /// That one is built once with the arena; this is polled, because the arena is drawn before
        /// the encounter has decided whose turn it is and a ring built then comes out empty.
        ///
        /// <para>Uses the same cell-corner arithmetic the grid does, so the ring sits exactly on the
        /// cell's own boundary rather than near it. It is the only thing on screen that says where
        /// the character whose turn it is stands — the arena shows no coordinates (TASK-435).</para>
        /// </remarks>
        public List<(long X0, long Y0, long X1, long Y1, Encounters.ArenaOverlayKind Kind)>
            ActingCellSegments() {
            var segments = new List<(long, long, long, long, Encounters.ArenaOverlayKind)>();
            GameData.Resources.Combat.Combatant acting = Combat?.Encounter?.Current;
            if (acting == null || acting.IsDead || Combat?.Grid == null
                || _session == null || _start == null) {
                return segments;
            }
            int cellSize = _start.CombatGridCellSize;
            if (cellSize <= 0) {
                return segments;
            }

            // *** AN INSET SQUARE, NOT THE CELL'S OUTLINE. *** Every combat marker in the original
            // is COMBAT.TBL shape 0 ("square") at the cell centre (WORLDHIT.C:401-421), and every
            // variant it selects uses the +-128 vertex square of a 300-unit cell — 22 units in from
            // each edge. Measured in the original too (owner, 2026-10-01: "a smaller square inside a
            // grid square"); this drew the full cell boundary before.
            int half = cellSize * 128 / 300;

            (long X, long Y) Corner(int column, int row, int sx, int sy) {
                (int across, int away) =
                    GameData.Resources.Combat.CombatArenaPlacement.CellOffset(column, row, cellSize);
                var (dx, dy) = Collision.ProximityMath.Rotate(
                    across + (sx * half), away + (sy * half), _session.Rotation);

                return (_session.PositionX + dx, _session.PositionY + dy);
            }

            void Ring(int column, int row, Encounters.ArenaOverlayKind kind) {
                (long ax, long ay) = Corner(column, row, -1, -1);
                (long bx, long by) = Corner(column, row, 1, -1);
                (long cx, long cy) = Corner(column, row, 1, 1);
                (long dx, long dy) = Corner(column, row, -1, 1);
                segments.Add((ax, ay, bx, by, kind));
                segments.Add((bx, by, cx, cy, kind));
                segments.Add((cx, cy, dx, dy, kind));
                segments.Add((dx, dy, ax, ay, kind));
            }

            Ring(acting.X, acting.Y, Encounters.ArenaOverlayKind.ActingCell);

            // *** THE TARGET RING RIDES THE MELEE PREVIEW'S OWN DECISION. *** The original brings
            // the yellow outline up with the Thrust/Swing prompt, in the same beat -- they are one
            // signal. Reading the cell MeleePanelContent last accepted means the outline cannot
            // promise a hit the panel is not offering, and it inherits that method's reach and
            // liveness tests rather than restating them (TASK-586).
            if (_targetHighlightCell.HasValue) {
                Ring(_targetHighlightCell.Value.Column, _targetHighlightCell.Value.Row,
                    Encounters.ArenaOverlayKind.TargetCell);
            }
            // The cursor's own marker, as the original draws it under the mouse (WORLDHIT.C:490):
            // the move marker on a cell the actor can walk to. The touch aids also mark a chosen
            // cell that is neither (NoneCell), since a finger leaves no cursor to show it.
            if (CursorCell is (int cc, int cr) && !Nullable.Equals(_targetHighlightCell, CursorCell)) {
                bool empty = Combat.CombatantAtCell(cc, cr) == null;
                if (empty && Combat.CellMovable(acting, cc, cr)) {
                    Ring(cc, cr, Encounters.ArenaOverlayKind.MoveCell);
                } else if (CursorCellAlwaysShown) {
                    Ring(cc, cr, Encounters.ArenaOverlayKind.NoneCell);
                }
            }

            return segments;
        }

        public List<(long X0, long Y0, long X1, long Y1, Encounters.ArenaOverlayKind Kind)>
            ArenaOverlaySegments() {
            var segments = new List<(long, long, long, long, Encounters.ArenaOverlayKind)>();
            if (Combat?.Grid == null || _session == null || _start == null) {
                return segments;
            }
            int cellSize = _start.CombatGridCellSize;
            if (cellSize <= 0) {
                return segments;
            }

            (long X, long Y) Corner(int column, int row) {
                (int across, int away) =
                    GameData.Resources.Combat.CombatArenaPlacement.CellOffset(column, row, cellSize);
                var (dx, dy) = Collision.ProximityMath.Rotate(
                    across - (cellSize / 2), away - (cellSize / 2), _session.Rotation);

                return (_session.PositionX + dx, _session.PositionY + dy);
            }

            // *** THE GRID IS THE PORTED OUTLINE NOW, NOT A LATTICE OF ITS OWN. ***
            // CombatGridOutline.Edges is the faithful port of drawCombatGridOutline: it walks RUNS
            // and breaks them at impassable cells, so it draws nothing over a wall, over an
            // out-of-bounds cell, or over the six rows combatgrid_load_zone seals off underground
            // (CMBTGRID.C:727-736). This used to hand-roll a plain Width x Height lattice, which
            // drew thirteen rows in a dungeon until TASK-588 clamped the height here -- a second
            // implementation of what the outline is, kept in step by hand. The clamp goes with it:
            // the walled rows break the runs, so the shape falls out of the model (TASK-599).
            //
            // *** THE +1 IS THE WHOLE RECONCILIATION, AND THE TWO CONVENTIONS ARE NOT IN CONFLICT.
            // *** Edges speaks CombatGridOutline.CornerOffset, where corner (i, j) is a cell's
            // BACK-left corner (`away + cellSize / 2` from the centre); this method's Corner is its
            // FRONT-left (`away - cellSize / 2`). Same lattice, indexed one cell apart on the away
            // axis, so CornerOffset(i, j) == Corner(i, j + 1) exactly.
            //
            // Nothing had to be reconciled with the acting-cell and target RINGS, which was the
            // open question when this was filed. A ring outlines ONE CELL and already addresses
            // this same lattice with the same Corner -- Corner(c, r) through Corner(c+1, r+1) -- so
            // it was never using a different convention, only asking a different question.
            //
            // Every interior line is emitted TWICE, once as the back of one run and once as the
            // front of the next. That is what the original does and it is not worth de-duplicating:
            // the segments are drawn, not accumulated.
            foreach (GameData.Resources.Combat.CombatGridOutline.Edge edge in
                     GameData.Resources.Combat.CombatGridOutline.Edges(Combat.Grid)) {
                (long x0, long y0) = Corner(edge.X1, edge.Y1 + 1);
                (long x1, long y1) = Corner(edge.X2, edge.Y2 + 1);
                segments.Add((x0, y0, x1, y1, Encounters.ArenaOverlayKind.Grid));
            }


            GameData.Resources.Combat.TrapPuzzle puzzle = Combat.Puzzle;
            if (puzzle?.Elements == null) {
                return segments;
            }
            for (var a = 0; a < puzzle.Elements.Count; a++) {
                for (int b = a + 1; b < puzzle.Elements.Count; b++) {
                    if (!GameData.Resources.Combat.TrapPuzzleBuilder.Aligned(
                            puzzle.Elements[a], puzzle.Elements[b])) {
                        continue;
                    }
                    (long ax, long ay) = CellCentre(puzzle.Elements[a].X, puzzle.Elements[a].Y, cellSize);
                    (long bx, long by) = CellCentre(puzzle.Elements[b].X, puzzle.Elements[b].Y, cellSize);
                    segments.Add((ax, ay, bx, by, Encounters.ArenaOverlayKind.Link));
                }
            }

            return segments;
        }

        private (long X, long Y) CellCentre(int column, int row, int cellSize) {
            (int across, int away) =
                GameData.Resources.Combat.CombatArenaPlacement.CellOffset(column, row, cellSize);
            var (dx, dy) = Collision.ProximityMath.Rotate(across, away, _session.Rotation);

            return (_session.PositionX + dx, _session.PositionY + dy);
        }

        /// <summary>Whether an element id names a drawable <c>COMBAT.TBL</c> prop.</summary>
        /// <remarks>
        /// The party and retreat markers never reach <see cref="TrapPuzzle.Elements"/> as things
        /// standing on the grid, but an id outside the table would index a creature or run off the
        /// end, so this is an allow-list rather than a range check.
        /// </remarks>
        private static bool HasPropModel(int elementId) =>
            GameData.Resources.Combat.TrapPropEmergence.Rises(elementId);

        /// <summary>
        /// A cannon's facing as a BaK rotation, or the party's own facing for anything else.
        /// </summary>
        /// <remarks>
        /// The four quarter-turns are the original's, read off the jump table in
        /// <c>combat_actor_grid_rndr_terr_prop?</c>: element 10 (west) 0x4000, 11 (east) 0xC000,
        /// 12 (north) 0x0000, 13 (south) 0x8000.
        ///
        /// <para><b>THE QUARTER-TURN IS IN THE ARENA'S FRAME, NOT THE WORLD'S, so the party's
        /// rotation has to be added to it.</b> <see cref="PlaceTrapProps"/> rotates every prop's
        /// POSITION by <c>_session.Rotation</c> — the arena is laid out in front of the party, not
        /// aligned to world north — so a terrain facing of "north" means north along the grid, and
        /// applying it as an absolute world rotation leaves the cannon turned by whatever the party
        /// happens to be facing.
        ///
        /// <para>It hid because the other branch is right by accident: everything that is not a
        /// cannon returns the party's rotation and so already carries the term. Only the cannons
        /// take a terrain-derived facing, and only the cannons looked wrong (TASK-327).</para>
        ///
        /// <para>Measured, at a matched camera on encounter 347 with the party at yaw 0xE000: the
        /// cannon's ground shadow had elongation 6.40 and a principal axis of +12.8 degrees against
        /// the original's 1.47 and -28.3. Adding the party rotation gives Unity euler (0, 45, 0) and
        /// the shadow becomes 1.59 / -27.1 — two independent quantities landing together, where
        /// every other quarter- and eighth-turn missed both.</para>
        /// </remarks>
        private ushort CannonFacing(GameData.Resources.Combat.TrapPuzzle puzzle,
            GameData.Resources.Combat.TrapGridElement e) {
            if (e.ElementId != GameData.Resources.Combat.TrapPuzzleBuilder.CannonElementId) {
                return (ushort)_session.Rotation;
            }
            ushort quarterTurn = (int)puzzle.Grid.TerrainAt(e.X, e.Y) switch {
                10 => (ushort)0x4000,
                11 => (ushort)0xC000,
                13 => (ushort)0x8000,
                _ => (ushort)0x0000,
            };
            return unchecked((ushort)(quarterTurn + (ushort)_session.Rotation));
        }

        /// <summary>
        /// The combatant a placed arena sprite stands for — the inverse of the roster-slot line in
        /// <see cref="PlaceCombatants"/>.
        /// </summary>
        /// <remarks>
        /// <b>Sited here on purpose.</b> The forward mapping is one expression a few lines up
        /// (<c>c.IsPartyMember ? c.PartySlot : fight.Enemies.IndexOf(c)</c>), and a second copy of it
        /// somewhere else is a thing that can drift. Both directions live in one file so they cannot.
        /// </remarks>
        public GameData.Resources.Combat.Combatant CombatantFor(int rosterSlot, bool partyMember) {
            GameData.Resources.Combat.CombatEncounter fight = Combat?.Encounter;
            if (fight == null) {
                return null;
            }
            if (!partyMember) {
                return rosterSlot >= 0 && rosterSlot < fight.Enemies.Count
                    ? fight.Enemies[rosterSlot]
                    : null;
            }
            foreach (GameData.Resources.Combat.Combatant c in fight.AllCombatants()) {
                if (c != null && c.IsPartyMember && c.PartySlot == rosterSlot) {
                    return c;
                }
            }
            return null;
        }

        /// <summary>
        /// Who stands on the cell under a world point — the pick the arena actually does.
        /// </summary>
        /// <remarks>
        /// <b>THE CELL DECIDES, NOT THE SPRITE.</b> <c>combat_actor_terr_under_cur</c>
        /// (CACTOR.C:741) is only <c>combatgrid_tile_terrain(g_cursor_tile_x, g_cursor_tile_y)</c>,
        /// and those two globals are written by projecting the pointer onto the floor. So the
        /// original never hit-tests a figure: the cursor names a cell and the cell names its
        /// occupant.
        ///
        /// <para>Ours raycast the billboard instead, which is a different question with a
        /// different answer. A creature is drawn tall from a low camera, so its sprite covers cells
        /// in front of it — the driving notes record a figure on row 3 whose head is three rows up
        /// the screen — and a click there hit a combatant standing somewhere else entirely, while a
        /// small or prone one was hard to hit at all (TASK-589).</para>
        ///
        /// <para>Returns the roster identity rather than a scene object because that is all a
        /// caller does with it, and it is the same pair <see cref="CombatantFor"/> maps back. The
        /// forward expression is <c>PlaceCombatants</c>'s own, kept in this file for the reason
        /// that method's remark gives.</para>
        /// </remarks>
        /// <summary>The enemy cell the melee preview last accepted, or null — the yellow ring.</summary>
        /// <remarks>
        /// Held rather than recomputed because the hover reaches this service only as an argument to
        /// <see cref="CombatPanelContent"/>, which the HUD calls every frame; deriving it a second
        /// time in the overlay seam would be a second copy of the reach test, free to drift from the
        /// panel's.
        /// </remarks>
        private (int Column, int Row)? _targetHighlightCell;

        /// <summary>The arena cell under a floor point, or null off the grid — CombatantAtPoint's own pick.</summary>
        public (int Column, int Row)? CellAtPoint(UnityEngine.Vector3 point) {
            if (Combat?.Encounter == null || _session == null || _start == null) {
                return null;
            }
            return World.Encounters.ArenaCellPicker.CellAt(
                (int)(point.x * World.Converters.BakCoordinateConverter.WorldScale),
                (int)(point.z * World.Converters.BakCoordinateConverter.WorldScale),
                (int)_session.PositionX, (int)_session.PositionY,
                _session.Rotation, _start.CombatGridCellSize);
        }

        /// <summary>
        /// The cell under the cursor — the mouse's hover, or the touch aids' selection / C3 cursor —
        /// marked as the original marks the cursor cell (see ActingCellSegments).
        /// </summary>
        public (int Column, int Row)? CursorCell { get; set; }

        /// <summary>Touch: mark the cursor cell even when it is neither a target nor movable.</summary>
        public bool CursorCellAlwaysShown { get; set; }

        /// <summary>A cell's centre on the arena floor, in Unity world space (the inverse of CellAtPoint).</summary>
        public UnityEngine.Vector3? CellCentreWorld(int column, int row) {
            if (_session == null || _start == null || _start.CombatGridCellSize <= 0) {
                return null;
            }
            (long x, long y) = CellCentre(column, row, _start.CombatGridCellSize);
            return World.Converters.BakCoordinateConverter.ConvertPosition((int)x, (int)y, 0);
        }

        /// <summary>The acting combatant's cell, or null outside a fight.</summary>
        public (int Column, int Row)? ActingCell() =>
            Combat?.Encounter?.Current is GameData.Resources.Combat.Combatant a && !a.IsDead ? (a.X, a.Y) : null;

        /// <summary>A spell or item is waiting for its target cell or combatant.</summary>
        public bool AwaitingCombatTarget =>
            _pendingCombatMode == GameData.Resources.Combat.CombatCommandOutcome.PendingMode.TargetSelection;

        public (int RosterSlot, bool PartyMember)? CombatantAtPoint(UnityEngine.Vector3 point) {
            GameData.Resources.Combat.CombatEncounter fight = Combat?.Encounter;
            if (fight == null || _session == null || _start == null) {
                return null;
            }
            var bakX = (int)(point.x * World.Converters.BakCoordinateConverter.WorldScale);
            var bakY = (int)(point.z * World.Converters.BakCoordinateConverter.WorldScale);
            (int Column, int Row)? cell = World.Encounters.ArenaCellPicker.CellAt(
                bakX, bakY, (int)_session.PositionX, (int)_session.PositionY,
                _session.Rotation, _start.CombatGridCellSize);
            if (cell == null) {
                return null;
            }

            GameData.Resources.Combat.Combatant occupant =
                Combat.CombatantAtCell(cell.Value.Column, cell.Value.Row);
            if (occupant == null) {
                return null;
            }

            return occupant.IsPartyMember
                ? (occupant.PartySlot, true)
                : (fight.Enemies.IndexOf(occupant), false);
        }

        /// <summary>Arm an effect sprite to fly from <paramref name="from"/> to
        /// <paramref name="to"/> when the arena is next drawn.</summary>
        /// <remarks>
        /// The destination is stored as the (slot, party) pair rather than the combatant itself,
        /// because that pair is what the drawn sprites are labelled with — see
        /// <see cref="CombatantFor"/>, whose inverse this is. Storing a reference would work today
        /// and break the moment a redraw happens between arming and flying.
        /// </remarks>
        private static void ArmFlight(GameData.Resources.Combat.CombatEncounter fight,
            GameData.Resources.Combat.Combatant from,
            GameData.Resources.Combat.Combatant to, int effectId) {
            if (fight == null || from == null || to == null) {
                return;
            }
            int slot = to.IsPartyMember ? to.PartySlot : fight.Enemies.IndexOf(to);
            if (slot < 0) {
                return;
            }
            from.FlightEffectId = effectId;
            from.FlightToSlot = slot;
            from.FlightToParty = to.IsPartyMember;
        }

        /// <summary>Give a pending thrown-rock flight its arc, if that is what is flying.</summary>
        /// <remarks>
        /// Only <see cref="GameData.Resources.Combat.CombatEffectSprite.Shot"/> arcs — it is the
        /// original's <c>action_id 0x14</c>, the one branch of the flight routine that carries a
        /// vertical delta and skips off the ground. Every other effect flies flat, and leaving the
        /// two arc fields at zero is what says so.
        /// </remarks>
        private void ArmRockArc(GameData.Resources.Combat.Combatant from,
            GameData.Resources.Combat.Combatant to, bool hit) {
            if (from == null || to == null
                || from.FlightEffectId != GameData.Resources.Combat.CombatEffectSprite.Shot) {
                return;
            }
            (int steps, int delta) = GameData.Resources.Combat.ThrownRockFlight.Launch(
                from.X - to.X, from.Y - to.Y, _start.CombatGridCellSize, hit, _random);
            from.FlightArcSteps = steps;
            from.FlightArcDelta = delta;
        }

        /// <summary>
        /// Whether something is watching the party closely enough to refuse a camp —
        /// <c>proxscan_vis_rec_kind_0e3e</c> (PROXSCAN.C:249-273).
        /// </summary>
        /// <remarks>
        /// <b>The placed list IS our visible list for encounter actors.</b> The original's scan walks
        /// the resident objects and the encounter actors are appended to that list per ref-pair by
        /// <c>rgnenc_visible_pool_append_spawn</c> — so above ground, where PROXSCAN.C applies no
        /// further distance test, "listed" and "placed in the party's chunk" are the same set. The
        /// dungeon arm's <c>0x2134</c> cap is applied as it is written.
        ///
        /// <para>Uses the cached placement rather than re-deriving it: <c>PlaceEncounterActors</c> is
        /// deliberately not idempotent, and asking it again here would promote pending actors a
        /// second time. See its own remark.</para>
        /// </remarks>
        public bool CampIsWatched() {
            if (_session == null) {
                return false;
            }
            foreach (GameData.Resources.World.EncounterActorPlacement.Placed actor
                    in PlaceEncounterActors()) {
                long distance = Collision.ProximityMath.OctagonalDistance(
                    (int)(actor.WorldX - _session.PositionX),
                    (int)(actor.WorldY - _session.PositionY));
                if (GameData.Resources.World.CampRefusal.Watches(
                        actor.Roams, distance, _underground)) {
                    return true;
                }
            }
            return false;
        }

        public List<GameData.Resources.World.EncounterActorPlacement.Placed> PlaceEncounterActors() {
            var placed = new List<GameData.Resources.World.EncounterActorPlacement.Placed>();
            GameData.Resources.World.EncounterObjectStates states = _session?.EncounterActorStates;
            if (states == null) {
                return placed;
            }

            // *** THE PARTY'S POSITION, NOT _lastChunk. *** Which actors stand here is a function of
            // where the party IS; _lastChunk records what the activate pass last visited, which is
            // null until the party has taken a step. Depending on it made the first draw of a zone
            // empty and the second one correct — a difference no reader would attribute to this.
            var chunk = (x: Floor(_session.PositionX, ChunkSize), y: Floor(_session.PositionY, ChunkSize));

            // *** THIS IS NOT IDEMPOTENT, SO THE ANSWER IS CACHED. *** The seed promotes pending
            // actors to roaming and the promotion is written back, so a SECOND call finds them
            // already placed and takes their stored pose, re-rolling nothing -- and a caller that
            // re-derived mid-chunk would get a new list of new objects for the same actors. One
            // list per chunk, rebuilt when the chunk changes.
            if (_placedChunk == chunk && _placedActors != null) {
                return _placedActors;
            }

            if (!_byChunk.TryGetValue(chunk, out List<TileEventTrigger> triggers)) {
                _placedChunk = chunk;
                _placedActors = placed;
                return placed;
            }

            var inOrder = new List<(GameData.Resources.World.TileEventType, long?)>(triggers.Count);
            foreach (TileEventTrigger t in triggers) {
                inOrder.Add((t.Type, EncounterNumberOf(t)));
            }
            List<long> records = GameData.Resources.World.EncounterReset.RecordIds(inOrder);
            if (records.Count == 0) {
                _placedChunk = chunk;
                _placedActors = placed;
                return placed;
            }

            // *** THE PARTY'S CHUNK DECIDES THE REF-PAIR HERE, for the same reason the chunk above
            // is taken from the party's position rather than from _lastChunk: this pass is about
            // where the party IS. _currentRefIndex follows the ACTIVATE pass, which is a step behind
            // during a build and a whole zone behind before the first step.
            int refPair = RefIndexAt(_session.PositionX, _session.PositionY);
            states.Seed(refPair, records, EncounterRosterOf, RosterActorIsDead);

            int tileX = GameData.Resources.World.WorldPlacement.TileOf(_session.PositionX);
            int tileY = GameData.Resources.World.WorldPlacement.TileOf(_session.PositionY);

            for (var record = 0; record < records.Count; record++) {
                TileEventTrigger trigger = TriggerForRecord(triggers, record);
                GameData.Resources.Data.EncounterActorSetup setup = SetupOf(trigger);
                bool standingOnly = StandingOnly(trigger);

                for (var slot = 0; slot < GameData.Resources.World.EncounterObjectStates.SlotsPerRecord;
                        slot++) {
                    if (placed.Count >= GameData.Resources.World.EncounterActorPlacement.MaxPlacedPerChunk) {
                        break;
                    }

                    int at = GameData.Resources.World.EncounterObjectStates.IndexOf(
                        refPair, record, slot);
                    GameData.Resources.World.EncounterObjectStates.Entry entry = states[at];
                    GameData.Resources.Data.EnemySlot template =
                        setup != null && setup.Slots != null && slot < setup.Slots.Length
                            ? setup.Slots[slot]
                            : null;

                    if (GameData.Resources.World.EncounterActorPlacement.TryPlace(slot,
                            entry.KindState, standingOnly, template, entry, tileX, tileY,
                            _random(GameData.Resources.World.EncounterActorSpawn.WalkFrameCount),
                            _random(2),
                            records[record],
                            out GameData.Resources.World.EncounterActorPlacement.Placed one,
                            out int stateAfter)) {
                        placed.Add(one);
                        // Only a pending actor's word changes; writing it back unconditionally
                        // would rewrite entries this pass never touched.
                        //
                        // *** THE WHOLE WORD, NOT THE KIND. *** TryPlace returns
                        // EncounterActorSpawn.FreshlyPlacedState's answer -- Roaming with a random
                        // walk frame in the low bits and a direction bit -- and the draw reads both
                        // back (RGNENC.C:578-579). Storing KindOf(stateAfter) threw the phase away,
                        // so every actor restarted on frame 0 walking the same way after a load,
                        // which is the lockstep that function's own remark exists to avoid.
                        if (stateAfter != entry.KindState) {
                            states.SetStateWord(refPair, record, slot, stateAfter);
                            // And WHERE it now stands: the original stores every drawn actor's
                            // pose before the block is read again (RGNENC.C:338, :385). Without it
                            // the offsets stay zero and the next derivation -- a re-entered chunk,
                            // a load, an underground fight's body -- lands on the tile's corner.
                            states.SetPose(refPair, record, slot,
                                (int)(one.WorldX - (long)tileX * GameData.Resources.World.WorldPlacement.TileSize),
                                (int)(one.WorldY - (long)tileY * GameData.Resources.World.WorldPlacement.TileSize),
                                one.Facing);
                        }
                    }
                }
            }

            _placedChunk = chunk;
            _placedActors = placed;
            return placed;
        }

        // The chunk the placed list belongs to, and the list. See PlaceEncounterActors: the pass
        // mutates the save block, so it must run at most once per chunk.
        private (int x, int y)? _placedChunk;
        private List<GameData.Resources.World.EncounterActorPlacement.Placed> _placedActors;

        /// <summary>The Nth encounter-carrying trigger in the chunk, or null.</summary>
        private static TileEventTrigger TriggerForRecord(List<TileEventTrigger> triggers, int record) {
            var types = new List<GameData.Resources.World.TileEventType>(triggers.Count);
            foreach (TileEventTrigger t in triggers) {
                types.Add(t.Type);
            }
            int index = GameData.Resources.World.EncounterReset.TriggerIndexForRecord(types, record);
            return index >= 0 && index < triggers.Count ? triggers[index] : null;
        }

        private GameData.Resources.Data.EncounterActorSetup SetupOf(TileEventTrigger trigger) =>
            trigger == null
                ? null
                : trigger.Type == TileEventType.Trap
                    ? TrapRecord(trigger)?.EnemySetup
                    : CombRecord(trigger)?.EnemySetup;

        /// <summary>
        /// The record's flag bit 0 — <b>the same bit as <c>Avoidable</c></b>, which is not a
        /// coincidence: an encounter you can slip past is one whose members stand still.
        /// </summary>
        private bool StandingOnly(TileEventTrigger trigger) =>
            trigger != null && IsAmbush(trigger);

        private System.Collections.Generic.IReadOnlyList<short> EncounterRosterOf(long encounter) =>
            _session?.RosterOf((int)encounter);

        /// <summary>Whether a roster combatant is dead, read the way a fight reads it.</summary>
        private bool RosterActorIsDead(int actorSlot) =>
            // *** THE CAF_DEAD FLAG, NOT THE HEALTH. *** rgnenc_load_encounter_actors makes exactly
            // one test before it seeds — inner.flags & CAF_DEAD (RGNENC.C:205) — and never reads a
            // stat. This used to ask whether the stored health was zero, which calls every
            // un-rolled monster dead: measured on dir.G01/SAVE07, where the original seeded three
            // slots of the party's ref-pair to Unplaced and we seeded none. See
            // GameSession.RosterActorIsFlaggedDead for where that byte lives.
            _session?.RosterActorIsFlaggedDead(actorSlot) ?? false;

        /// <summary>
        /// Loads every creature's MONST record, once.
        /// </summary>
        /// <remarks>
        /// <b>Driven by MNAMES.DAT rather than a list of file numbers</b> — the creature-name table
        /// IS the creature id space, so this asks the data which creatures exist instead of encoding
        /// the answer. Only about half of them ship a MONST file; a creature without one simply has
        /// no entry, and <see cref="ProfileOf"/> gives it the default profile.
        /// </remarks>
        private async UniTask LoadMonsterStatsAsync(object owner) {
            if (_monsterStats != null) {
                return;
            }
            _monsterStats = new Dictionary<int, GameData.Resources.Monster.MonsterStats>();

            var names = await LoadOrNull<GameData.Resources.Creature.CreatureNames>(
                "MNAMES.DAT", owner);
            if (names == null) {
                return;
            }
            foreach (GameData.Resources.Creature.CreatureName creature in names.Creatures) {
                var stats = await LoadOrNull<GameData.Resources.Monster.MonsterStats>(
                    // About half the creatures ship no MONST file — see this method's remarks.
                    $"MONST{creature.Number}.DAT", owner, optional: true);
                if (stats != null) {
                    _monsterStats[creature.Number] = stats;
                }
            }
        }

        /// <summary>
        /// What the AI needs to know about one creature.
        /// </summary>
        /// <remarks>
        /// <b><see cref="Combatant.ClassId"/> is the creature type for an enemy</b>, which is what
        /// keys MONST — the same number MNAMES uses. Stamina is a PERCENTAGE of this creature's own
        /// maximum, so it has to come from the live stat block rather than from the template.
        ///
        /// <para>The template's stats are ranges. <c>Min</c> is taken for the capability tests
        /// because a creature whose range bottoms out at zero cannot be relied on to have the
        /// skill at all.</para>
        /// </remarks>
        /// <summary>The one creature CBENC.C:507 lets into the crossbow turn without quarrels and a
        /// crossbow.</summary>
        private const int ShootsWithoutAPackClass = 0x1a;

        private BakAgain.Combat.MonsterTurnResolver.Profile ProfileOf(Combatant monster) {
            // *** A PARTY MEMBER IS A CANDIDATE TOO, AND WAS AN EMPTY PROFILE. *** No MONST template,
            // so this returned default: never a caster, never a shooter, 0% stamina — every member
            // "wounded", and the rogues that hunt the party's spellcaster in the original could not see
            // one (TASK-528, entry 306). Auto-resolve plays the party through the same profile.
            if (monster != null && monster.IsPartyMember) {
                GameData.Resources.Character.ActorStat[] mine = Combat?.StatsFor(monster);
                int casting = StatBase(mine, GameData.ActorAttribute.AccuracyCasting);
                // CBENC.C:532: in chapter 8 a member carrying no Crystal Staff is no caster.
                bool staffless = _session.Chapter == GameData.Resources.Spells.SpellCasting.PowerSourceChapter
                    && GameData.Resources.Inventory.InventoryQuery.CountByKind(
                        _session.GetActorInventory(monster.ClassId),
                        GameData.Resources.Spells.SpellCasting.PowerSourceObjectId) == 0;
                return new BakAgain.Combat.MonsterTurnResolver.Profile(
                    0, PercentOf(mine, GameData.ActorAttribute.Stamina),
                    canCastSpells: casting > 0 && !staffless,
                    canShoot: Combat?.CanShoot(monster) ?? false,
                    spellcastPattern: monster.AiPatterns?.Spellcast ?? 0,
                    crossbowAccuracy: StatBase(mine, GameData.ActorAttribute.AccuracyCrossbow),
                    healthPercent: PercentOf(mine, GameData.ActorAttribute.Health),
                    castingSkill: casting,
                    crossbowPattern: monster.AiPatterns?.Crossbow ?? 0,
                    meleeMovePattern: monster.AiPatterns?.MeleeMove ?? 0,
                    poolPercent: PoolPercentOf(mine));
            }
            if (monster == null || _monsterStats == null
                || !_monsterStats.TryGetValue(monster.ClassId,
                    out GameData.Resources.Monster.MonsterStats stats)) {
                return default;
            }

            GameData.Resources.Character.ActorStat[] live = Combat.StatsFor(monster);
            int staminaPercent = PercentOf(live, GameData.ActorAttribute.Stamina);

            return new BakAgain.Combat.MonsterTurnResolver.Profile(
                // *** A SUMMON FIGHTS WITH ZERO MORALE, NOT ITS KIND'S. ***
                // cspell_summon_actor zeroes the actor's morale before the stat roll, and the roll
                // reads the template's only `if (morale != 0)` — so the creature's own nerve is
                // skipped and a conjured creature never routs. MonsterSummon.Morale has modelled
                // that from the start with nothing able to reach it; this profile was built from
                // MONST unconditionally, so a summoned Wyvern could turn and run from the fight the
                // party conjured it into. MonsterMorale.Routs rejects a zero threshold after the
                // roll, which is where the constant does its work.
                Combat.IsSummoned(monster)
                    ? GameData.Resources.Combat.MonsterSummon.Morale
                    : stats.FleeThreshold.Min,
                staminaPercent,
                // *** AND A SUMMON FIGHTS WITH ITS KIND'S BODY BUT NOT ITS BOOK. ***
                // cspell_summon_actor zeroes all three spell words at spawn, which
                // MonsterSummon.KnowsSpells has modelled from the start with NOTHING able to reach
                // it — this profile read AccuracyCasting straight off the template, so a conjured
                // creature of a caster kind would have been handed its kind's casting.
                //
                // That was dormant while no monster ever cast; wiring the picker (TASK-379) made it
                // live, which is why it is closed now. No SHIPPED summon spell conjures a caster
                // class — 44, 56 and 57 are not among the eight — so this is a mod and item-summon
                // path, not a visible bug today.
                canCastSpells: !Combat.IsSummoned(monster) && stats.AccuracyCasting.Min > 0,
                // *** A CROSSBOW AND A QUARREL, NOT JUST THE SKILL. *** The door into the crossbow turn,
                // combatenc_show_missile_stat_row (CBENC.C:507), wants quarrels carried and an intact
                // crossbow (or creature 0x1a) on top of stat 5. Reading the template's accuracy alone
                // sent entry 306's quarrel-less rogues down the crossbow row, where every shot fails and
                // they advance on the wrong target; the original's take their melee row (TASK-528).
                canShoot: stats.AccuracyCrossbow.Min > 0
                    && ((Combat?.CanShoot(monster) ?? false) || monster.ClassId == ShootsWithoutAPackClass),
                // *** THE ROWS ARE THE ACTOR'S OWN ROLL, NOT THE TEMPLATE'S MINIMUM. *** Three rogues of
                // one kind walked (0,6,5), (0,2,2) and (0,4,5) in the original (TASK-528).
                spellcastPattern: monster.AiPatterns?.Spellcast ?? stats.SpellcastPattern.Min,
                // Capability and willingness are separate questions: AccuracyCrossbow picks the
                // crossbow branch, this decides whether the branch yields a shot. Min for the same
                // reason the capability tests use it.
                crossbowPattern: monster.AiPatterns?.Crossbow ?? stats.CrossbowPattern.Min,
                // The row the melee/move turn walks (TASK-528); nearly every MONST carries 2.
                meleeMovePattern: monster.AiPatterns?.MeleeMove ?? stats.MeleeMovePattern.Min,
                // The shooter's own accuracy decides how much room it needs around a target before
                // it will fire: CombatAi.AllyClearanceForAccuracy. Left unset, every monster is a
                // perfect shot.
                crossbowAccuracy: stats.AccuracyCrossbow.Min,
                // What the support turn's recipient test reads — MonsterHealTurn.CanReceive wants
                // a percentage, unlike the spell CHOICE, which reads raw health.
                healthPercent: PercentOf(live, GameData.ActorAttribute.Health),
                // The fatigue tests and the Wounded role read the health+stamina pool.
                poolPercent: PoolPercentOf(live),
                // The clearance the cast path demands comes off the caster's own casting skill,
                // the same way the shot's comes off its crossbow — MonsterCasterTurn.ClearanceFor.
                castingSkill: stats.AccuracyCasting.Min);
        }

        /// <summary>
        /// The combat runtime, with the tables that have to be assigned after construction.
        /// </summary>
        /// <remarks>
        /// <b>The affinity tables are set here or they are never set at all.</b> They are properties
        /// rather than constructor arguments because they load asynchronously while the runtime is
        /// built on demand from a synchronous accessor — so a second construction site that forgot
        /// them would silently lose the weakness doubling, with no error and no test to catch it.
        /// One factory keeps that impossible.
        /// </remarks>
        /// <summary>The arena's spell-visual player, set by <c>WorldRuntime</c> while a fight is
        /// drawn (TASK-117). Read at call time, so a runtime built before the arena still reaches it.</summary>
        public System.Action<GameData.Resources.Combat.SpellVisual, GameData.Resources.Combat.Combatant,
            GameData.Resources.Combat.Combatant> SpellVisualSink { get; set; }

        private CombatRuntime NewCombatRuntime() {
            var runtime = new CombatRuntime(_session, _logger, _partyEntries, _objects, _playSfx,
                // The arm's SOUND only — a sequence with waits and an explicit stop, which the
                // rules object cannot run itself. See SpellEffectArmSound and SpellArmAudio.
                playSpellArm: kind => {
                    // The storm's and the blast's cues are sequenced with their pictures when the
                    // arena is drawing them (SpellVfx), so they land on their bolts and rings.
                    if (SpellVisualSink != null
                        && (kind == GameData.Resources.Spells.SpellEffectArmSound.StormFlashKind
                            || kind == GameData.Resources.Spells.SpellEffectArmSound.ParticleBlastKind)) {
                        return;
                    }
                    BakAgain.Audio.SpellArmAudio.Play(kind, n => UnityEngine.Random.Range(0, n));
                }) {
                PlaySpellVisual = (visual, from, to) => SpellVisualSink?.Invoke(visual, from, to),
                SpellWeakness = _spellWeakness,
                SpellResistance = _spellResistance,
                // Runs while the grid is empty — see CombatRuntime.LayOutGround. It reads
                // Combat.Puzzle rather than taking one, because by the time it fires the runtime has
                // already adopted the puzzle it was entered with.
                LayOutGround = _ => LayOutArena(_combat?.Puzzle),
                // A cannon fires from inside a walk, with nobody above it holding the table.
                Spells = _spells,
                ArenaCellSize = _start?.CombatGridCellSize ?? 300,
                Affinity = _affinity,
                // Rolled from, not read from a save: a conjured creature has no roster slot.
                MonsterTemplates = _monsterStats,
                // A summon onto a full field has to say so — MonsterSummon.NoRoomDialog.
                ShowDialog = id => _dialogs?.ShowById(id).Forget(),
                // Nightfingers puts the target's pack up the way corpse loot does, mid-fight.
                // It is still the fight's pack: the give-distance and held-item refusals apply
                // (INVENTOR.C:735-740), so the combat flags go on as the HUD's pack sets them.
                OpenStolenPack = pack => {
                    _openCorpseLoot?.Invoke(pack);
                    BakAgain.UI.Inventory.InventoryMenu menu = _inventoryMenuAccessor?.Invoke();
                    if (menu != null) {
                        menu.InCombat = true;
                        menu.ClearActingCombatantPoison = () => {
                            if (Combat?.Encounter?.Current is GameData.Resources.Combat.Combatant current) {
                                current.Flags &= ~GameData.Resources.Combat.CombatantFlags.Poisoned;
                            }
                        };
                        menu.CombatUnderground = _underground;
                        menu.CombatDistance = CombatDistanceBetween;
                    }
                },
                // *** A LAMBDA, NOT _underground. *** This factory runs once and the runtime is
                // cached for the life of the service, which outlives the zone; capturing the value
                // here would fix the arena at whichever zone was loaded first.
                Underground = () => _underground,
            };
            return runtime;
        }

        /// <summary>
        /// One live stat as a percentage of its own maximum — <c>combat_actor_stat_percent</c>.
        /// </summary>
        /// <remarks>
        /// <b>100 when the stat is missing, not 0.</b> A creature whose stats did not load reads as
        /// untouched rather than as dying, which keeps a missing table out of the morale check and
        /// out of the heal's recipient scan. Zero would rout everything and make every monster a
        /// heal target.
        /// </remarks>
        private static int StatBase(GameData.Resources.Character.ActorStat[] live, GameData.ActorAttribute attribute) =>
            live != null && (int)attribute < live.Length && live[(int)attribute] != null ? live[(int)attribute].Base : 0;

        /// <summary><c>combat_actor_stat_percent(actor, 1)</c> (CACTOR.C:721): health+stamina against their
        /// maxima; 100 when the stats are missing, like <see cref="PercentOf"/>.</summary>
        private static int PoolPercentOf(GameData.Resources.Character.ActorStat[] live) {
            int max = (live != null && live.Length > 1 ? (live[0]?.Max ?? 0) + (live[1]?.Max ?? 0) : 0);
            return max > 0 ? ((live[0]?.Base ?? 0) + (live[1]?.Base ?? 0)) * 100 / max : 100;
        }

        private static int PercentOf(GameData.Resources.Character.ActorStat[] live,
            GameData.ActorAttribute attribute) {
            GameData.Resources.Character.ActorStat stat =
                live != null && (int)attribute < live.Length ? live[(int)attribute] : null;
            return stat != null && stat.Max > 0 ? stat.Base * 100 / stat.Max : 100;
        }

        /// <summary>The AI that plays the enemy side.</summary>
        /// <remarks>
        /// <b>Rebuilt per fight rather than cached</b>, because whether the arena is underground
        /// decides whether anything can rout at all — a dungeon fight is always to the finish.
        /// </remarks>
        private BakAgain.Combat.MonsterTurnResolver EnemyAi() =>
            new BakAgain.Combat.MonsterTurnResolver(
                ProfileOf, _random, _affinity?.AiFleeThresholds,
                // *** WAS A HARD-CODED false. *** The zone's own flag is loaded per zone and used
                // by two other callers; this one ignored it, so every dungeon monster could rout
                // when the rule is that nothing routs underground and a dungeon fight is always to
                // the finish. The doc on this method already said so while the argument said the
                // opposite.
                _underground,
                MonsterCanCast,
                // The catalogue the spell picker scans. Without it a caster selects nothing, which
                // is what it did before TASK-379.
                _spells?.Spells,
                // *** THE ARENA'S OCCUPANCY, so the melee selector can skip a target it could not
                // stand beside. *** The grid belongs to the fight and the resolver is documented as
                // deciding only, so it gets the one TEST rather than the grid — TASK-438.
                (x, y) => Combat?.Grid?.IsBlocked(x, y) ?? false);

        /// <summary>
        /// Whether a creature can pay for a spell right now — the AI's <c>cspell_check_castable</c>.
        /// </summary>
        /// <remarks>
        /// <b>Cost and the known-spell bit.</b> <c>cspell_check_castable</c> tests the actor's
        /// <c>spellsKnown</c> words for everyone, and a creature carries them in its roster record.
        /// This once skipped that test on the belief that creatures have no spellbook, and entry 306's
        /// Rogue Mage paid 30 for an Evil Seek it does not know (TASK-539). The component test is not
        /// ported here: no creature knows a component spell in the data seen so far.
        ///
        /// <para><b>And the chapter-8 power source.</b> The pool it pays from is
        /// <see cref="BakAgain.Combat.CombatRuntime.CastingBudget"/>: in Timirianya a caster with no
        /// equipped Crystal Staff has none, so the Pantathians there cast nothing (CSPELL.C:1581).</para>
        /// </remarks>
        private bool MonsterCanCast(Combatant caster, int spellId) =>
            caster != null && _spells?.Spells != null
            && _spells.Spells.TryGetValue(spellId, out GameData.Resources.Spells.Spell spell)
            && GameData.Resources.Combat.MonsterSpellcasting.CanAfford(
                spell.MinimumCost, Combat?.CastingBudget(caster) ?? caster.Health + caster.Stamina)
            && (Combat?.KnowsSpell(caster, spellId) ?? false);

        /// <summary>
        /// The encounter's grid layout from TRAPS.DAT, or null when the table is not loaded.
        /// </summary>
        /// <remarks>
        /// <b>Built for every encounter, not only the puzzles.</b> Most records are empty and yield a
        /// blank grid, which is exactly what an ordinary fight wants — so there is no "is this a
        /// puzzle" test here, and no second code path that could drift from the first. The puzzle
        /// answers <c>HasObjective</c>, the retreat lock and the party's entry tiles all at once,
        /// which is why the runtime takes the puzzle rather than those three facts separately.
        ///
        /// <para><b>Party size is passed because TRAPS.DAT places up to three members</b> and the
        /// original guards each marker on the member existing
        /// (<c>actor_idx &lt; g_combat_count_A</c>). A marker for a member who is not in the party
        /// places nobody.</para>
        /// </remarks>
        private GameData.Resources.Combat.TrapPuzzle PuzzleFor(int encounterNumber) =>
            _traps == null
                ? null
                : GameData.Resources.Combat.TrapPuzzleBuilder.Build(
                    _traps.ElementsFor(encounterNumber),
                    // *** WAS A HARD-CODED false, THE THIRD IN THIS FILE. *** Underground the back
                    // rows are walled off and the arena is 8x7; passing false gave every dungeon
                    // puzzle six rows the original does not have. See EnemyAi for the same fix on
                    // the same field.
                    underground: _underground,
                    partySize: _session?.ActivePartyIndices?.Length
                        ?? GameData.Resources.Combat.TrapPuzzleBuilder.PartySlots);

        /// <summary>
        /// Put the combat HUD on screen, so a fight the party has walked into is visible.
        /// </summary>
        /// <remarks>
        /// <b>Built for whoever acts first.</b> The shared cell depends on the acting character, so
        /// the HUD advances to the first party turn and asks
        /// <see cref="CombatRuntime.CapabilitiesFor"/>. Casting is real; shooting still reports false
        /// because the quiver and weapon checks need inventory that a Combatant cannot reach yet.
        ///
        /// <para>Absent HUD is not an error: the world runs headless in tests, where no menu is
        /// built.</para>
        /// </remarks>
        private void ShowCombatHud() {
            BakAgain.UI.Combat.CombatMenu menu = _combatMenuAccessor?.Invoke();
            if (menu == null) {
                return;
            }
            // Whose turn it is decides what the cell offers, so the HUD is built for the first
            // party member to act rather than for the party in general.
            // Unsubscribe first: a second encounter must not stack a second handler on the same
            // singleton menu, or one press would resolve the turn twice.
            menu.CommandIssued -= OnCombatCommand;
            menu.CommandIssued += OnCombatCommand;
            menu.HelpRequested -= OnCombatHelpRequested;
            menu.HelpRequested += OnCombatHelpRequested;

            // *** THE OPENING GOES THROUGH THE SAME SETTLE. *** Enemies faster than the whole
            // party take their turns before the player has touched anything, and this used to run
            // them with no redraw after it at all -- so a fight OPENED showing the deploy positions
            // while the model had already moved (TASK-581, measured: E#0 at (5,2) beside Owyn while
            // the arena still drew it at (2,7)). Routing it here draws and paces those opening
            // turns exactly like any other, and opens the menu when they are done.
            SettleTurnAsync(openMenu: true).Forget();
        }

        /// <summary>
        /// A right-click on a combat button: play its describe record and do nothing else.
        /// </summary>
        /// <remarks>
        /// Modal, as the original's <c>dialog_play_record(id, 1)</c> is. <b>No fight state changes</b>
        /// — describing a button must never spend the turn it is describing.
        /// </remarks>
        private void OnCombatHelpRequested(int dialogRecord) =>
            PlayDialog((uint)dialogRecord, modal: true);

        /// <summary>
        /// A press on the combat HUD.
        /// </summary>
        /// <remarks>
        /// <b>Rest, Defend and Retreat act; every other id is logged and ignored on purpose.</b>
        /// Those commands are not modelled yet, and doing something approximate would be worse than
        /// doing nothing.
        /// </remarks>
        private void OnCombatCommand(GameData.Resources.Combat.CombatCommands.Command command,
            int actionId) {
            Combatant acting = Combat.Encounter?.Current;
            if (acting == null) {
                return;
            }

            // *** ANY PRESS INTERRUPTS. *** AutoResolveLoop.Bails names Back and Cancel because
            // those are the results the original's poll can return, but the poll runs while the
            // fight is playing itself — so the honest port of "one press takes control back" is
            // that whatever the player pressed stops it. The command then falls through and does
            // what it says, which is what a player pressing Rest during auto-resolve expects.
            if (_autoResolving) {
                _autoResolveBail = true;
            }

            // Rest and Defend are DIFFERENT actions on different menu ids (19 and 32), and this used
            // to answer Defend by resting. Resting heals and sets DefendCommand, which feeds nothing;
            // defending sets Parry, which MeleeHits already reads. So Defend had no effect at all and
            // Rest was unreachable.
            switch (command) {
                case GameData.Resources.Combat.CombatCommands.Command.Rest:
                    Combat.ResolveRest(acting);
                    break;
                case GameData.Resources.Combat.CombatCommands.Command.Defend:
                    Combat.ResolveDefend(acting);
                    break;
                case GameData.Resources.Combat.CombatCommands.Command.BackOrRetreat:
                    ResolveRetreat(acting);
                    return;   // ResolveRetreat refreshes or tears down the HUD itself
                case GameData.Resources.Combat.CombatCommands.Command.AutoResolve:
                    StartAutoResolve();
                    return;   // the loop hands the HUD on itself when it stops
                case GameData.Resources.Combat.CombatCommands.Command.Cast:
                    OpenCastScreen(acting);
                    return;   // picking a spell is not yet the cast, so no turn is spent
                case GameData.Resources.Combat.CombatCommands.Command.Shoot:
                    // *** PRESSING SHOOT ARMS TARGETING; PICKING A QUARREL DOES NOT. *** The
                    // original's case 31 sets stateA = 4 AND raises the shoot menu in the same
                    // breath (COMBAT.C ~2018), which is what CombatCommandOutcome.ModeFor has said
                    // all along. So the field is live for a click from this moment, with kind 0 --
                    // the turn's default -- already selected.
                    _pendingCombatMode = GameData.Resources.Combat.CombatCommandOutcome.ModeFor(command);
                    _selectedQuarrelKind = 0;
                    OpenShootMenu(acting);
                    return;   // choosing a quarrel is not yet the shot, so no turn is spent
                case GameData.Resources.Combat.CombatCommands.Command.Inspect:
                    // Arms only. The assessment happens on the follow-up click, and the turn is
                    // spent there -- pressing the button costs nothing.
                    _pendingCombatMode = GameData.Resources.Combat.CombatCommandOutcome.ModeFor(command);
                    return;
                case GameData.Resources.Combat.CombatCommands.Command.CharacterScreen:
                    // *** SHIFT TURNS THE PACK INTO THE SHEET. *** combat_arena_suspend_char_screen
                    // (COMBAT.C:1875) runs charscreen_info_loop while either Shift key is down, and
                    // the inventory otherwise. Neither is an action, so no turn is spent (TASK-514).
                    if (GameData.Resources.Combat.CombatCommands.SuspendScreenFor(ShiftHeld())
                        == GameData.Resources.Combat.CombatCommands.SuspendScreen.CharacterSheet) {
                        OpenCombatCharacterSheet(acting);
                    } else {
                        OpenCombatInventory(acting);
                    }
                    return;
                default:
                    _logger?.LogDebug($"Combat HUD: {command} (id {actionId}) is not wired yet.");
                    return;
            }

            // *** SPENDING THE TURN DISARMS WHATEVER WAS PENDING. *** Only Rest and Defend reach
            // here -- every arming command returns above -- and both spend the turn, so anything
            // still armed belongs to a turn that is over. The original does this unconditionally in
            // the same breath as ending the turn (COMBAT.C, the turnInact arm):
            //
            //     combat_arena_turn_actor_inact(&stateA, &done, &hudState, &turnInact);
            //     spellRecId = -1;
            //     castArg    = -1;
            //     stateA = stateB = 0xffff;          // 0xffff is -1, i.e. PendingMode.None
            //
            // Without it a Shoot that was armed and then refused (no quarrels, or an enemy adjacent
            // -- CombatCapability.RangeIsClear) stays armed for the REST OF THE FIGHT, across every
            // later turn and every other character. The arena then half-dies in a way that reads as
            // broken terrain: ResolveCombatGroundClick returns at its TargetSelection guard so
            // nobody can move, and ResolveCombatTargetClick never reaches ResolveUnarmedClick so
            // nobody can melee -- both silently, spending no stamina. Measured 2026-09-17 at
            // def_comb:91: three of four enemies dead, and the fight could not be finished
            // (TASK-572).
            //
            // Summon and decoy state is deliberately NOT dropped here: SummonPlacement.CanCancel is
            // false and the item is spent on arming, so there is no path back for the player either.
            if (GameData.Resources.Combat.CombatCommandOutcome.SpendsTheTurn(command)) {
                _pendingCombatMode = GameData.Resources.Combat.CombatCommandOutcome.PendingMode.None;
                _pendingSpell = null;
                _pendingSpellId = -1;
                _pendingSpellPower = 0;
                _pendingCombatItem = null;
                _selectedQuarrelKind = 0;
            }

            // The turn is spent, so hand the HUD to whoever acts next rather than leaving it showing
            // the last character's options.
            RefreshCombatHud();
        }

        /// <summary>
        /// Id 33: try to leave the fight, say what happened, and end the encounter if it worked.
        /// </summary>
        /// <remarks>
        /// <b>Id 33 means two different things and this asks which one applies.</b> With the SHOOT
        /// menu up it is a cancel that returns to the melee menu; with the melee menu up it is the
        /// retreat attempt. The flag is the shoot menu's own <c>IsOpen</c> now that one exists — the
        /// hardcoded <c>false</c> this used to pass was correct only while nothing could raise it.
        ///
        /// <para>The cancel itself is handled by <see cref="OnShootCancelled"/>, which the shoot
        /// menu raises directly; reaching here with it open means the melee menu was pressed while
        /// the picker was up, and the retreat is refused rather than run underneath it.</para>
        ///
        /// <para><b>The dialog plays before the fight is torn down.</b> The escape line is about the
        /// encounter the party is leaving; calling <c>Leave</c> first would write the party's state
        /// back and drop the encounter while the line is still on screen.</para>
        /// </remarks>
        private void ResolveRetreat(Combatant acting) {
            bool shootMenuIsUp = _shootMenuAccessor?.Invoke()?.IsOpen ?? false;
            if (GameData.Resources.Combat.CombatCommands.BacksOutOfShootMenu(shootMenuIsUp)) {
                return;
            }

            CombatRuntime.RetreatOutcome outcome = Combat.ResolveRetreat(acting, _random);
            PlayDialog((uint)outcome.DialogRecord, modal: true);

            if (!outcome.Escaped) {
                // The turn is spent either way, so hand the HUD on rather than leaving the failed
                // retreat's character still showing as the one to act.
                RefreshCombatHud();
                return;
            }

            // A flight marks NOTHING — the encounter stays armed, which is the point of running
            // away — but it does MOVE the party, and the fight is over, so the remembered encounter
            // must not outlive it and be settled by the next one.
            // *** STAGED BEFORE THE RELOCATION, NOT AFTER IT. *** PersistSurvivors stores each
            // survivor's pose as an offset from the PARTY'S TILE, so moving the party first would
            // write every one of them relative to the wrong origin — and only in zones whose tile
            // index differs from the destination's, which is the kind of wrong that looks right in
            // testing. The damage half does not care about order; this half does.
            StageCombatantEdits();
            RelocateAfterFlight(_fightingTrigger);
            ForgetFight();
            Combat.Leave();
            _setCombatMusic?.Invoke(false);
            SwapArenaThroughFade(false, () => _combatMenuAccessor?.Invoke()?.Close());
        }

        /// <summary>
        /// Raise the quarrel picker for whoever is acting — SHOOT.DAT over the melee menu.
        /// </summary>
        /// <remarks>
        /// <b>Both menus stay in the scene; only one is up.</b> They occupy the same six HUD
        /// anchors, so leaving the melee menu open underneath would draw two buttons per cell and
        /// leave both hit-testable.
        ///
        /// <para>Built from the actor's own pack (<see cref="CombatRuntime.QuarrelsFor"/>), because
        /// which cell each kind claims depends on what THIS character carries — see
        /// <see cref="BakAgain.UI.Combat.ShootMenu"/>.</para>
        /// </remarks>
        private void OpenShootMenu(Combatant acting) {
            BakAgain.UI.Combat.ShootMenu shoot = _shootMenuAccessor?.Invoke();
            if (shoot == null) {
                _logger?.LogDebug("Combat HUD: Shoot pressed, but there is no shoot menu to raise.");
                return;
            }

            // Unsubscribe first, for the reason the melee menu does: these are singletons, and a
            // second fight must not stack a second handler that answers one press twice.
            shoot.QuarrelChosen -= OnQuarrelChosen;
            shoot.QuarrelChosen += OnQuarrelChosen;
            shoot.Cancelled -= OnShootCancelled;
            shoot.Cancelled += OnShootCancelled;
            shoot.HelpRequested -= OnCombatHelpRequested;
            shoot.HelpRequested += OnCombatHelpRequested;

            _combatMenuAccessor?.Invoke()?.Close();
            shoot.Open(Combat.QuarrelsFor(acting));
        }

        /// <summary>
        /// Back on the shoot menu: abandon the shot and put the melee menu back.
        /// </summary>
        /// <remarks>
        /// <b>No turn is spent.</b> The choice of quarrel is not the shot — the original stores the
        /// selection and resolves it afterwards
        /// (<see cref="CombatActionDispatch.LeftClickOnlyRecordsTheChoice"/>), so backing out before
        /// that leaves the character still to act. Refreshing the HUD here instead would hand the
        /// turn to the next character for pressing a cancel.
        /// </remarks>
        private void OnShootCancelled() {
            // Back is the ONE way out of target selection without acting, so it has to disarm the
            // mode too: COMBAT.C ~2085 sets stateA = -1 alongside swapping the menu back.
            _pendingCombatMode = GameData.Resources.Combat.CombatCommandOutcome.PendingMode.None;
            _shootMenuAccessor?.Invoke()?.Close();
            _combatMenuAccessor?.Invoke()?.Open();
        }

        /// <summary>
        /// A quarrel kind was chosen: remember it and keep waiting for a target.
        /// </summary>
        /// <remarks>
        /// <b>The shoot menu STAYS UP.</b> The original's turn loop draws the target-info panel
        /// precisely while <c>menu == g_shoot_menu</c> (COMBAT.C ~2540) and only swaps back to the
        /// melee menu once the shot has resolved — so the picker is the HUD you aim under, not a
        /// dialog you dismiss. Closing it here (which this used to do) both lost that panel and threw
        /// away the arming that <see cref="OnCombatCommand"/> had just done.
        ///
        /// <para><b>It only records.</b> The quarrel is not consumed and no turn is spent until a
        /// click lands on a target — <c>combataiturn_sel_consum_qrl</c> is asked with the consume
        /// flag clear while aiming.</para>
        /// </remarks>
        private void OnQuarrelChosen(int quarrelKind, int actionId) {
            _selectedQuarrelKind = quarrelKind;
            _pendingCombatMode = GameData.Resources.Combat.CombatCommandOutcome.PendingMode.TargetSelection;
            _logger?.LogDebug($"Shoot menu: quarrel kind {quarrelKind} (id {actionId}) selected; "
                + "click a target on the field to fire.");
        }

        /// <summary>
        /// The HEADS.BMX frame for whoever is acting, or -1 when no party member is.
        /// </summary>
        /// <remarks>
        /// <b>-1 covers both "no fight" and "a monster's turn"</b> — the original's portrait blit
        /// sits behind the same <c>actor-&gt;charSlot</c> guard as the stats panel's text, so an
        /// enemy acting leaves the last face up rather than showing one for a creature. We hide it
        /// instead: keeping a stale face beside a panel that has also gone blank reads as a bug,
        /// and nothing in the original depends on it lingering.
        /// </remarks>
        public int ActingPortraitHeadId() {
            Combatant acting = Combat?.Encounter?.Current;
            return acting != null && acting.IsPartyMember ? acting.ClassId : -1;
        }

        /// <summary>
        /// What the SHOOT menu's parchment says for the combatant under the cursor.
        /// </summary>
        /// <param name="rosterSlot">Who is under the cursor, or -1 for nothing.</param>
        /// <param name="partyMember">Which side that slot indexes.</param>
        /// <returns>The panel's content, or <c>null</c> when there is no panel to draw.</returns>
        /// <remarks>
        /// <b>Gated on the menu being up, not on target selection being armed.</b> The original
        /// draws this panel for as long as <c>menu == g_shoot_menu</c> (COMBAT.C:2543) — which
        /// starts the moment Shoot is pressed, before any quarrel has been chosen — and the panel's
        /// no-target line ("<c>7 quarrels remaining</c>") is exactly what it has to say during that
        /// window. Gating on the armed mode instead would blank it for the part of the flow it was
        /// written for.
        ///
        /// <para><b>Kind 0 is a real selection while nothing has been clicked</b>, the same way it
        /// is for the shot itself: <c>g_combat_menu_selected_item</c> is zeroed at the top of every
        /// turn, so the panel opens quoting the first kind's numbers rather than none.</para>
        /// </remarks>
        public (System.Collections.Generic.IReadOnlyList<GameData.Resources.Combat.HudPanelLine> Lines,
                System.Collections.Generic.IReadOnlyList<GameData.Resources.Combat.HudPanelRule> Rules)
            CombatPanelContent(int rosterSlot, bool partyMember) {
            GameData.Resources.Combat.CombatEncounter fight = Combat?.Encounter;
            Combatant acting = fight?.Current;
            if (acting == null) {
                return (null, null);
            }
            Combatant target = TargetAt(fight, rosterSlot, partyMember);

            // *** THE SHOOT MENU WINS, AND THAT ORDER IS THE ORIGINAL'S. *** The turn loop tests
            // `menu == g_shoot_menu` first and only falls through to the other panels when it is
            // not up (COMBAT.C:2543), so a character who has pressed Shoot keeps the quarrel
            // readout even while the cursor is over an enemy they could also walk up and hit.
            BakAgain.UI.Combat.ShootMenu shoot = _shootMenuAccessor?.Invoke();
            if (shoot != null && shoot.IsOpen) {
                // *** POINTING AT A TARGET RECORDS IT. *** combat_arena_draw_tgt_info_panel writes the
                // acting member's target on every redraw: whoever is under the cursor, or nothing over a
                // dead one or a cell outside the fire map (COMBAT.C:1173-1180, the move map's shot bit,
                // which LineOfFireTo answers here). The monsters' Engaged and TargetingTheLeader roles
                // read it, so a member who has only aimed still counts as engaged (TASK-540).
                RecordAim(fight, acting, target, shooting: true);
                int[] carried = Combat.QuarrelsFor(acting);
                int previewed = GameData.Resources.Combat.ShootTargetPanel.PreviewedKind(
                    shoot.HoveredActionId, _selectedQuarrelKind,
                    kind => kind >= 0 && kind < carried.Length ? carried[kind] : 0);
                return (GameData.Resources.Combat.ShootTargetPanel.Lines(
                    Combat.ShootPanel(acting, target, previewed, _selectedQuarrelKind)), null);
            }

            // An armed spell records its target from the hover as well, not only from the click.
            if (_pendingCombatMode
                    == GameData.Resources.Combat.CombatCommandOutcome.PendingMode.TargetSelection
                && _pendingCombatItem == null) {
                RecordAim(fight, acting, target, shooting: false);
            }

            // The melee preview, which the original raises in `stateA == 0` — the state the loop
            // enters when the cursor is over a reachable enemy. Nothing armed and nothing aimable
            // under the cursor leaves the parchment down, which is where the actor stats panel goes
            // when it lands (TASK-241).
            if (_pendingCombatMode
                != GameData.Resources.Combat.CombatCommandOutcome.PendingMode.None) {
                return (null, null);
            }

            var melee = MeleePanelContent(acting, target);
            // Set from the SAME call that decides the panel, so the ring and the prompt cannot
            // disagree about whether this click would land. Cleared when it would not.
            _targetHighlightCell = melee.Item1 != null && target != null
                ? (target.X, target.Y)
                : ((int Column, int Row)?)null;
            if (melee.Item1 != null) {
                return melee;
            }

            // *** THE DEFAULT PANEL, WHICH IS UP FOR MOST OF A TURN. *** The loop opens at
            // stateA = 1 and draws the acting character's stats whenever nothing more specific
            // applies, so this is what the strip shows between actions rather than nothing. It is
            // also what Inspect produces: that arm switches the active actor and this draws for
            // whoever is now current.
            var stats = Combat.ActorStatsFor(acting);
            return stats == null
                ? (null, null)
                : (GameData.Resources.Combat.ActorStatsPanel.Lines(
                    stats.Value.Name, stats.Value.Values), null);
        }

        /// <summary>
        /// The melee attack preview for the enemy under the cursor, or nothing.
        /// </summary>
        /// <remarks>
        /// <b>Shown only while a click would actually attack</b> — a reachable, living encounter
        /// actor. That is the same test <see cref="CombatRuntime.ResolveMeleeClick"/> makes, so the
        /// panel appearing is the promise that the click will be answered.
        /// </remarks>
        private (System.Collections.Generic.IReadOnlyList<GameData.Resources.Combat.HudPanelLine>,
                 System.Collections.Generic.IReadOnlyList<GameData.Resources.Combat.HudPanelRule>)
            MeleePanelContent(Combatant acting, Combatant target) {
            if (target == null || target.IsDead || target.IsPartyMember) {
                return (null, null);
            }
            if (!GameData.Resources.Combat.CombatActionDispatch.WithinReach(
                    GameData.Resources.Combat.CombatGrid.ChebyshevDistance(
                        acting.X, acting.Y, target.X, target.Y),
                    acting.Speed)) {
                return (null, null);
            }

            CombatRuntime.MeleePreview preview = Combat.MeleePreviewFor(acting, target);
            return (GameData.Resources.Combat.MeleeStatsPanel.Lines(
                    preview.ShowsSwing, preview.ThrustDamage, preview.ThrustAccuracy,
                    preview.SwingDamage, preview.SwingAccuracy),
                GameData.Resources.Combat.MeleeStatsPanel.Rules());
        }

        /// <summary>What the arena is waiting for, or None.</summary>
        private GameData.Resources.Combat.CombatCommandOutcome.PendingMode _pendingCombatMode =
            GameData.Resources.Combat.CombatCommandOutcome.PendingMode.None;

        /// <summary>
        /// Which quarrel kind a shot would use.
        /// </summary>
        /// <remarks>
        /// <b>Zero is a real selection, not "none".</b> The original sets
        /// <c>g_combat_menu_selected_item = 0</c> at the top of every actor's turn, so a player who
        /// presses Shoot and clicks a target without touching a quarrel button fires kind 0.
        /// </remarks>
        private int _selectedQuarrelKind;

        /// <summary>
        /// A click on the arena field while target selection is armed.
        /// </summary>
        /// <remarks>
        /// <b>The two DOS-space gates are already satisfied by the time this is called</b>, which is
        /// why it uses <see cref="GameData.Resources.Combat.CombatTargetSelection.ResolveOnField"/>:
        /// our menus are UI Toolkit elements that consume their own clicks (so the cursor can never
        /// be "over the menu bar" here), and a pick is either a combatant or nothing. See that method
        /// for why inventing coordinates would be worse.
        ///
        /// <para><b>The off-grid sentinel is not only off-grid.</b> <c>combat_arena_disp_spell_action</c>
        /// writes move cost 1000 for every cell the armed cursor rejects, and case 4 acts on a click only
        /// below it (COMBAT.C:2411). So a click the aim refuses is ignored and the arm stays up; the
        /// <c>RevertToMove</c> branch needs a valid cell with no target, which a click on the field
        /// never produces (TASK-546).</para>
        /// </remarks>
        public void ResolveCombatTargetClick(int rosterSlot, bool partyMember, bool isPrimary) {
            GameData.Resources.Combat.CombatEncounter fight = Combat?.Encounter;
            Combatant acting = fight?.Current;

            // *** THE ACTOR IS CHECKED HERE, NOT IN EACH ARM. *** Every target-click arm below runs
            // on `acting`, and only one of them looked at it: ResolveUnarmedClick vets the TARGET
            // carefully and then hands a dead actor to Combat.ResolveMeleeClick, whose approach step
            // WALKS THE CORPSE. Measured live in the chapter-1 def_trap fight: a party member killed
            // by crystal ground read `hp0 sta0 Ready, Dead, Knockback`, was still Current, and two
            // melee clicks were accepted -- the second moved the body from (2,3) to (2,4).
            //
            // *** AND THE READY FLAG ON A CORPSE IS FAITHFUL, SO DO NOT "FIX" IT THERE. *** The
            // original's kill sets CAF_DEAD without clearing CAF_READY (canassa COMBAT.C:277) -- our
            // CombatEncounter.Kill matches it exactly. What the original does instead is gate at the
            // point the acting actor is CHOSEN: a dead g_current_actor never becomes g_acting_actor
            // (CACTOR.C:1451-1457, `(flags & CAF_DEAD) == 0 && combatenc_actor_can_act(cur, 1)`).
            // That predicate is combatenc_actor_can_act (CBENC.C:677), which is precisely
            // Combatant.CanAct(strict) -- so applying it per click is the same rule, reached by the
            // door our architecture has. Clearing Ready on death would deviate from the original for
            // no gain and change what a round reset sees.
            //
            // Same wording as ResolveCellCast and ResolveMoveClick, deliberately: three handlers
            // enforcing one rule should be greppable as one string.
            if (acting == null || !acting.IsPartyMember || acting.IsDead
                || !acting.CanAct(strict: true)) {
                return;
            }

            Combatant target = TargetAt(fight, rosterSlot, partyMember);

            // *** WITH NOTHING ARMED, A CLICK ON THE FIELD IS THE MELEE. *** The turn loop's idle
            // state is not "waiting for a menu button": the cursor over a reachable enemy IS the
            // attack prompt (stateA == 0), and the two buttons make two different attacks. Until
            // this was wired the party could only be attacked, never attack.
            if (_pendingCombatMode
                == GameData.Resources.Combat.CombatCommandOutcome.PendingMode.None) {
                ResolveUnarmedClick(acting, target, isPrimary);
                return;
            }

            if (_pendingCombatMode
                == GameData.Resources.Combat.CombatCommandOutcome.PendingMode.InspectTarget) {
                ResolveInspectClick(acting, target);
                return;
            }

            if (_pendingCombatMode
                != GameData.Resources.Combat.CombatCommandOutcome.PendingMode.TargetSelection) {
                return;
            }

            // *** AN ARMED ITEM ANSWERS THE CLICK BEFORE THE SHOT AND SPELL ARMS. *** It has its own
            // validity rule (an encounter actor, alive, and for two of the arms orthogonally
            // adjacent), so running it through the shot's three tests would refuse it for want of a
            // quarrel the item does not use.
            if (_pendingCombatItem != null) {
                ResolveArmedCombatItem(acting, target);
                return;
            }

            (bool canShoot, bool _) = Combat.CapabilitiesFor(acting);

            // The same pass that resolves the click records the aim first, so a click that arrives
            // without a hovered frame before it still leaves the target the hover would have.
            RecordAim(fight, acting, target, shooting: canShoot);

            // *** THE PARTY IS NEVER A TARGET. *** combatenc_is_encounter_actor gates every arm of
            // the check, so clicking your own line does not resolve to a shot -- it falls through to
            // the same "nothing aimable here" branch an empty cell takes.
            bool hasTarget = canShoot
                && GameData.Resources.Combat.CombatTargetSelection.ShotIsValid(
                    targetIsEncounterActor: target != null && !target.IsPartyMember,
                    targetIsDead: target?.IsDead ?? true,
                    targetIsInLineOfFire: target != null && LineOfFireTo(fight, acting, target),
                    hasSelectedQuarrel: Combat.QuarrelsFor(acting) is { } packed
                        && _selectedQuarrelKind >= 0 && _selectedQuarrelKind < packed.Length
                        && packed[_selectedQuarrelKind] > 0);

            // A chosen spell decides what the cursor may point at; with none, -1 is the original's
            // "no record" and matches none of the ground- or crystal-aimed types.
            int targetingType = _pendingSpell?.TargetingType ?? -1;
            if (!canShoot && target != null && GroundCellRefused(target.X, target.Y)) {
                return;   // a ground spell on an occupied cell: the cursor refuses, the spell stays armed
            }
            if (!canShoot && _pendingSpell != null) {
                // The spell arm asks SpellTargetingRules rather than the shot's three tests.
                hasTarget = SpellTargetIsValid(targetingType, target);
            }

            var outcome = GameData.Resources.Combat.CombatTargetSelection.ResolveOnField(
                canShoot, hasTarget, targetingType);
            if (outcome == GameData.Resources.Combat.CombatTargetSelection.Resolution.RevertToMove) {
                return;   // the cursor refused this cell: nothing happens, the shot or spell stays armed
            }

            if (outcome == GameData.Resources.Combat.CombatTargetSelection.Resolution.Shoot) {
                bool shotHit = Combat.ResolveShot(acting, target, _selectedQuarrelKind, _random);
                // *** ARMED BEFORE THE REDRAW, PLAYED BY IT. *** The bolt is carried across the
                // resolve-then-draw gap on the shooter, the same way SwingPending carries a melee
                // blow — the model has no arena to fly anything through.
                ArmFlight(fight, acting, target,
                    GameData.Resources.Combat.CombatEffectSprite.Shot);
                // *** THE ARC IS DECIDED HERE BECAUSE A MISS ROLLS FOR IT. *** ThrownRockFlight's
                // launch takes the combat RNG, and a miss's delta can be negative — which is what
                // drops the rock early and starts it skipping. Resolving it in the draw would put
                // a roll on the wrong side of the model seam and re-roll it on every redraw.
                ArmRockArc(acting, target, shotHit);
                _redrawArena?.Invoke();
            } else if (outcome
                    == GameData.Resources.Combat.CombatTargetSelection.Resolution.CastAtTarget
                || outcome
                    == GameData.Resources.Combat.CombatTargetSelection.Resolution.CastAtGround) {
                if (_pendingSpell == null) {
                    _logger?.LogDebug("Arena: a cast resolved with no spell chosen; ignoring.");
                    return;
                }
                // A ground- or crystal-aimed spell reaches the dispatcher with NO target actor,
                // by design — SpellTargetingRules.CastsWithoutATarget.
                Combatant castAt =
                    outcome == GameData.Resources.Combat.CombatTargetSelection.Resolution.CastAtGround
                        ? null
                        : target;
                Combat.ResolveCast(acting, castAt, _pendingSpell, _pendingSpellId,
                    _pendingSpellPower, _random);
                // *** DANNON'S DELUSIONS IS NOT DONE WHEN IT RESOLVES. *** Its kind-17 arm
                // (cspell_invoke_effect, CSPELL.C:848) asks for a TILE and puts a decoy of the target
                // there, lasting the cast's magnitude — computed before the spell's own post-animation
                // hook zeroes it (CSPELL.C:1495). The turn is spent on the placement click.
                if (_pendingSpellId == GameData.Resources.Spells.SpellIds.DannonsDelusions
                    && castAt != null) {
                    _pendingDecoyOf = castAt;
                    _pendingDecoyCaster = acting;
                    _pendingDecoyDuration = GameData.Resources.Spells.SpellEffectMagnitude.Calculate(
                        _pendingSpell, _pendingSpellId, _pendingSpellPower);
                }
                // *** MOST SPELLS FLY NOTHING, AND "has a target" IS NOT THE TEST. ***
                // Spell_RunAnimationEffect switches on the spell's AnimationEffectType across 20
                // cases and only case 3 reaches the projectile routine — the others fade the
                // palette, flash the target and so on. Three shipped spells are case 3: Flamecast,
                // Bane of Black Slayers and The Fetters of Rime. Gating on the target alone gave
                // Candle Glow a whistling missile.
                if (GameData.Resources.Combat.CombatEffectSprite.FliesProjectile(
                        _pendingSpell.AnimationEffectType)
                    && castAt != null) {
                    // A miss that struck a bystander flies at the bystander; a clean miss flies at
                    // castAt's slot too, but the redraw sends it to FlightMissEnd instead.
                    ArmFlight(fight, acting, acting.FlightInterceptedBy ?? castAt,
                        GameData.Resources.Combat.CombatEffectSprite.ForSpell(_pendingSpellId));
                    acting.FlightInterceptedBy = null;
                }
                _pendingSpell = null;
                _redrawArena?.Invoke();
            }

            EndTargetSelection();
            if (GameData.Resources.Combat.CombatTargetSelection.SpendsTheTurn(outcome)
                && _pendingDecoyOf == null) {
                // *** CLEARING READY IS WHAT SPENDS THE TURN. *** case 4 of
                // combat_arena_resolve_menu_action does `g_current_actor->inner->flags &= ~CAF_READY`
                // on every acting outcome, and RefreshCombatHud only advances past a character who
                // is no longer ready. Without it the shooter keeps their turn: verified live on
                // 2026-08-29 that a shot resolved, wore the crossbow and left the SAME character
                // acting — so a player could fire until the quiver ran out, and a caster until they
                // collapsed from the cost.
                //
                // Done here rather than inside ResolveShot/ResolveCast for the reason the original
                // does it here: those resolve an attack, and whether it also ends a turn is the
                // arena's question. ResolveMelee draws the same line.
                acting.Flags &= ~GameData.Resources.Combat.CombatantFlags.Ready;
                RefreshCombatHud();
            }
        }

        /// <summary>
        /// Inspect's follow-up click: assess the enemy under the cursor.
        /// </summary>
        /// <remarks>
        /// <b>A misclick does NOT clear the mode.</b> <see cref="GameData.Resources.Combat.InspectAction"/>
        /// only resets on a successful inspect or an explicit cancel, so clicking an empty tile or
        /// one of your own leaves the player still choosing rather than silently wasting the
        /// command.
        /// </remarks>
        private void ResolveInspectClick(Combatant acting, Combatant target) {
            GameData.Resources.Combat.InspectAction.Result result =
                GameData.Resources.Combat.InspectAction.Resolve(
                    moveCost: 0, confirmed: true,
                    targetIsEncounterActor: target != null && !target.IsPartyMember);
            if (result != GameData.Resources.Combat.InspectAction.Result.Inspected) {
                return;
            }

            _pendingCombatMode = GameData.Resources.Combat.CombatCommandOutcome.PendingMode.None;
            // *** INSPECTING COSTS THE TURN. *** The button's own help text says it "allows the
            // current character to inspect one enemy" and says nothing about the cost; the arm
            // clears Ready before it shows anything.
            acting.Flags &= ~GameData.Resources.Combat.CombatantFlags.Ready;
            ShowAssessmentAsync(acting, target).Forget();
        }

        /// <summary>
        /// The assessment display — <see cref="GameData.Resources.Combat.CombatAssessment"/>.
        /// </summary>
        /// <remarks>
        /// <b>ONE page that waits, not two records that bracket the numbers.</b> The original is
        /// <c>combatenc_anim_actor_stat_rolls</c> (CBENC.C:307): play 0x84, paint the rows into the
        /// panel it left up, then play 0x85. Awaiting those two here did nothing at all, because
        /// NEITHER of them blocks — 0x84 carries <c>SkipWait</c>, so it returns at once and leaves
        /// its page drawn for whatever repaints next, and 0x85 has no text, so it falls into
        /// <c>ShowById</c>'s nothing-to-show guard and neither draws nor waits.
        ///
        /// <para>So the whole sequence used to finish inside one frame: the rows were published and
        /// cleared before anything rendered, and 0x84's page was abandoned on screen with nothing
        /// that would ever paint over it. That is both halves of what was reported — an inspect
        /// showing no facts AND refusing to close (TASK-591). The turn was spent regardless, which
        /// is how it was told apart from a hang.</para>
        ///
        /// <para>Composing 0x84's own text with the rows in it and showing THAT, once, waiting,
        /// reproduces what the player sees without the original's two-video-page blit — which is
        /// architecture rather than behaviour. The page then takes 0x85's single branch as its
        /// choice row, so the acknowledgement is the Accept button the original draws rather than
        /// a click anywhere (TASK-598).</para>
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTaskVoid ShowAssessmentAsync(
            Combatant acting, Combatant target) {
            System.Collections.Generic.IReadOnlyList<GameData.Resources.Combat.HudPanelLine> lines =
                Combat.AssessmentLines(acting, target, _random);

            // *** THE OPENING LINE NAMES BOTH PARTIES, SO BOTH VARIABLES HAVE TO BE SET FIRST. ***
            // It reads "<inspector> studied his opponent... some of the <creature>'s abilities".
            // Without this it named whoever the last dialog left in global 30004 -- observed live
            // as "Locklear studied his opponent" while Owyn was the one inspecting.
            _session?.SetGlobalValue(
                GameData.Resources.Dialog.DialogSlotPopulator.CurrentActorGlobalKey, acting.ClassId);
            // An enemy's ClassId IS its creature type -- see CombatRuntime.EnterRoster, which keeps
            // the roster's creature number there rather than the table row it came from.
            _session?.SetDialogCreatureType(target.ClassId);

            GameData.Resources.Dialog.DialogPlay play = await _dialogs.ResolveById(
                GameData.Resources.Combat.CombatAssessment.OpeningDialog);
            GameData.Resources.Dialog.DialogEntry entry = play?.Entry;
            if (entry == null) {
                RefreshCombatHud();
                return;
            }

            // WithText clones, so clearing SkipWait here cannot reach the loader's cached copy —
            // the trap that remark exists for. Without clearing it the composed page would not wait
            // either, which is the same bug one layer along.
            GameData.Resources.Dialog.DialogEntry page = entry.WithText(
                GameData.Resources.Combat.CombatAssessment.ComposePageText(entry.Text, lines));
            page.Flags &= ~GameData.Resources.Dialog.DialogEntryFlags.SkipWait;

            // *** 0x85 IS THE ACCEPT BUTTON. *** The closing record carries no text of its own —
            // it is TextWithChoice with a single branch on keyword key 260, which KEYWORD.DAT
            // labels "Accept", and a ResizeDialog putting that button's own box at the panel's
            // bottom right. Since the page above stands in for 0x84's abandoned page, it takes
            // 0x85's row too, and the player acknowledges the assessment the way the original asks
            // rather than by clicking anywhere. Confirmed live 2026-09-20: a click at VGA (283,102)
            // — inside 0x85's box — is what dismisses the original's panel.
            GameData.Resources.Dialog.DialogPlay closing = await _dialogs.ResolveById(
                GameData.Resources.Combat.CombatAssessment.ClosingDialog);
            if (closing?.Entry?.Branches is { Count: > 0 } accept) {
                page.Branches = accept;
                page.Flags |= GameData.Resources.Dialog.DialogEntryFlags.TextWithChoice;
            }

            await _dialogs.ShowEntry(page);
            RefreshCombatHud();
        }

        /// <summary>
        /// A click on the field with no command armed: melee, or the actor's own two self-commands.
        /// </summary>
        /// <remarks>
        /// <b>Clicking YOURSELF is a third arm, not a miss</b> — <c>check_defend</c>, COMBAT.C:2380.
        /// A right click defends; a left click defends too <i>unless</i> the character is below 80%
        /// of their health+stamina, in which case it rests instead. So the button that attacks an
        /// enemy heals you when you are hurt, and the menu's Defend and Rest buttons are not the
        /// only way to reach either.
        ///
        /// <para><b>Only the acting character's own body counts.</b> Clicking a different party
        /// member is neither an attack (they are not an encounter actor) nor a self-command.</para>
        /// </remarks>
        private void ResolveUnarmedClick(Combatant acting, Combatant target, bool isPrimary) {
            if (target == null) {
                return;
            }

            if (ReferenceEquals(target, acting)) {
                (int pool, int maxPool) = Combat.PoolOf(acting);
                if (isPrimary
                    && !GameData.Resources.Combat.DefendAction.LeftClickDefends(pool, maxPool)) {
                    Combat.ResolveRest(acting);
                } else {
                    Combat.ResolveDefend(acting);
                }
                RefreshCombatHud();
                return;
            }

            if (target.IsPartyMember || target.IsDead) {
                return;   // the party is never a melee target
            }

            CombatRuntime.MeleeClick outcome = Combat.ResolveMeleeClick(acting, target,
                GameData.Resources.Combat.CombatActionDispatch.AttackFor(
                    isPrimary
                        ? GameData.Resources.Combat.CombatActionDispatch.LeftButton
                        : GameData.Resources.Combat.CombatActionDispatch.RightButton),
                _random);
            if (outcome == CombatRuntime.MeleeClick.Refused) {
                return;   // out of reach, a diagonal swing, or no route in: the turn is NOT spent
            }

            // The approach clears Ready inside the original's melee_approach; the swing arm clears
            // it explicitly. Both reach here, so it is cleared once, in the place that knows the
            // click actually did something.
            acting.Flags &= ~GameData.Resources.Combat.CombatantFlags.Ready;
            _redrawArena?.Invoke();
            RefreshCombatHud();
        }

        /// <summary>
        /// Raise the spell picker for whoever is acting.
        /// </summary>
        /// <remarks>
        /// <b>IN COMBAT THE CASTER IS NOT CHOSEN.</b> The original calls
        /// <c>cspell_cast_menu_loop(g_current_actor, ...)</c> — the character whose turn it is does
        /// the casting, and the caster row the field screen offers is not part of the combat flow.
        /// So the screen is handed the acting character before it is shown, rather than being left
        /// to ask.
        /// </remarks>
        /// <summary>
        /// Id 44 — the acting character's pack, opened mid-fight.
        /// </summary>
        /// <remarks>
        /// <b>It is the SAME screen the world uses.</b> <c>cmbinv_inventory_screen_run</c> has three
        /// callers — the world loop, the map screen and the arena (COMBAT.C:1880) — differing only
        /// in whether an actor is passed. So this raises the ordinary inventory rather than a combat
        /// one, which is why <see cref="BakAgain.UI.Inventory.InventoryMenu.InCombat"/> exists: the
        /// screen behaves differently, it is not a different screen.
        ///
        /// <para><b>The flag is set BEFORE the push</b>, because the gate chain reads it the moment
        /// the player uses anything, and it clears itself on hide.</para>
        ///
        /// <para><b>Select then push is safe here</b>, unlike the cast screen: <c>SetMember</c>
        /// resolves a container and touches no <c>UIDocument</c>, so there is no stage to be null.
        /// The corpse-loot path does the same.</para>
        ///
        /// <para>Not yet done, and the reason this spends no turn: the arena acts on what the
        /// screen RETURNS (<c>combat_arena_resume_dispatch</c>). Until that result is carried out,
        /// a fighter can open a pack, read it and use the ordinary items in it — which is strictly
        /// more than before, when the command fell to "not wired yet".</para>
        /// </remarks>
        private static bool ShiftHeld() =>
            UnityEngine.InputSystem.Keyboard.current is { } keyboard
            && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

        /// <summary>The acting fighter's character sheet — the Shift arm of combat command 22.</summary>
        /// <remarks>
        /// The original also takes this arm when <c>menupage_state_0e7c()</c> is 2. That is not a
        /// drag mode — it returns <c>g_wMenuDragState</c>, which holds WHICH BUTTON was pressed
        /// (<see cref="GameData.Resources.Menu.MenuClickButton"/>: 1 = primary, 2 = secondary), so
        /// the original's second arm is a RIGHT-CLICK on the portrait. The port's input layer
        /// carries the button as <c>isPrimary</c> on the pointer event, so the equivalent is a
        /// secondary click; only Shift is honoured here.
        /// </remarks>
        private void OpenCombatCharacterSheet(Combatant acting) {
            BakAgain.UI.Character.CharacterSheetScreen sheet = _characterSheetAccessor?.Invoke();
            if (sheet == null) {
                _logger?.LogDebug("Combat HUD: the sheet was asked for, but there is no character sheet screen.");
                return;
            }
            byte[] roster = _session?.ActivePartyIndices;
            for (var i = 0; roster != null && i < roster.Length; i++) {
                if (roster[i] == acting.ClassId) {
                    RunCombatSheetAsync(sheet, i).Forget();
                    return;
                }
            }
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid RunCombatSheetAsync(
            BakAgain.UI.Character.CharacterSheetScreen sheet, int partySlot) {
            await sheet.RunAsync(partySlot);
            RaiseCombatHudAgain();
        }

        /// <summary>
        /// Puts the combat HUD's input back on top after a navigator screen raised from the fight has
        /// closed.
        /// </summary>
        /// <remarks>
        /// <b>The HUD is not a navigator screen; it is a menu switched on over the world.</b> Pushing
        /// the pack or the sheet hides the world screen, and popping it shows the world screen again,
        /// whose MenuLayerHost re-pushes its input layer ABOVE the HUD's. The fight is still running,
        /// but every key and click now reaches the world. Toggling the menu off and on re-pushes its
        /// layer last. The original simply redraws the HUD after the suspend screen
        /// (combat_arena_redraw_hud_bufs, COMBAT.C:2116).
        /// </remarks>
        private void RaiseCombatHudAgain() {
            if (Combat?.Encounter == null) {
                return;
            }
            BakAgain.UI.Combat.CombatMenu menu = _combatMenuAccessor?.Invoke();
            if (menu == null || !menu.gameObject.activeSelf) {
                return;
            }
            menu.Close();
            menu.Open();
        }

        private void OpenCombatInventory(Combatant acting) {
            BakAgain.UI.Inventory.InventoryMenu menu = _inventoryMenuAccessor?.Invoke();
            BakAgain.UI.Navigation.IScreenNavigator navigator = _navigatorAccessor?.Invoke();
            if (menu == null || navigator == null) {
                _logger?.LogDebug("Combat HUD: the pack was asked for, but there is no inventory "
                    + "screen or screen stack to raise it on.");
                return;
            }

            byte[] roster = _session?.ActivePartyIndices;
            var slot = -1;
            for (var i = 0; roster != null && i < roster.Length; i++) {
                if (roster[i] == acting.ClassId) {
                    slot = i;
                    break;
                }
            }
            if (slot < 0) {
                _logger?.LogDebug("Combat HUD: the acting character is not in the active roster; "
                    + "the pack was not opened.");
                return;
            }

            if (!menu.SetMember(slot)) {
                _logger?.LogDebug("Combat HUD: no inventory container for party slot {Slot}.", slot);
                return;
            }
            menu.InCombat = true;
            menu.ClearActingCombatantPoison = () => {
                if (Combat?.Encounter?.Current is GameData.Resources.Combat.Combatant current) {
                    current.Flags &= ~GameData.Resources.Combat.CombatantFlags.Poisoned;
                }
            };
            menu.CombatUnderground = _underground;
            menu.CombatDistance = CombatDistanceBetween;
            RunCombatInventoryAsync(menu, navigator, acting).Forget();
        }

        /// <summary>
        /// <c>combat_arena_dist_actors_by_id</c> (COMBAT.C:1902): cells between two party members in
        /// this fight, by character index; from a non-member (-1), 0 to the acting member and 1000
        /// to anyone else. A member not in the fight is 1000 away.
        /// </summary>
        private int CombatDistanceBetween(int from, int to) {
            GameData.Resources.Combat.CombatEncounter fight = Combat?.Encounter;
            if (fight == null) {
                return 1000;
            }
            Combatant target = fight.Party.Find(c => c.IsPartyMember && c.ClassId == to);
            if (from < 0) {
                return target != null && ReferenceEquals(fight.Current, target) ? 0 : 1000;
            }
            Combatant giver = fight.Party.Find(c => c.IsPartyMember && c.ClassId == from);
            return giver == null || target == null
                ? 1000
                : GameData.Resources.Combat.CombatGrid.ChebyshevDistance(giver.X, giver.Y, target.X, target.Y);
        }

        /// <summary>
        /// Runs the pack and then acts on what it hands back.
        /// </summary>
        /// <remarks>
        /// <b>One read at one moment, not a subscription.</b> The original is
        /// <c>cmdId = cmbinv_inventory_screen_run(...); ... combat_arena_resume_dispatch(cmdId, ...)</c>
        /// — the screen runs to completion and its RETURN VALUE is dispatched. Using a combat item
        /// does not close the screen, and a later ordinary use overwrites the value, so waiting for
        /// the screen to close is the only moment at which the answer is final.
        ///
        /// <para>Waiting on the navigator's top rather than on an event, because a screen has no
        /// "closed" signal and adding one would fire on being pushed OVER as well as on being
        /// popped.</para>
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTaskVoid RunCombatInventoryAsync(
            BakAgain.UI.Inventory.InventoryMenu menu,
            BakAgain.UI.Navigation.IScreenNavigator navigator, Combatant acting) {
            // *** WAIT FOR THE TRANSITION, NOT ONLY FOR THE STACK. *** Current stops being the pack
            // before the world screen underneath has been shown again, and showing it re-pushes its
            // input layer. Raising the HUD before that lands it UNDER the world's layer (TASK-514).
            await navigator.PushAndWaitAsync(menu);
            RaiseCombatHudAgain();

            // *** BEFORE THE COMMAND, AND WHETHER OR NOT THERE IS ONE. *** The screen edits the
            // session's stats directly, and the fight's combatants hold their own copies — so a
            // healing potion drunk in here is consumed and then undone, because WriteBack flushes
            // the stale combat value out when the fight ends. The original copies its combatants
            // back out of g_gameState.characters at exactly this point. Unconditional because
            // drinking something and taking no combat command is the ordinary case.
            Combat?.RefreshPartyStatsFromSession();

            int command = menu.PendingCombatCommandId;
            if (command == GameData.Resources.Combat.CombatItemUse.NoItemUsed) {
                return;
            }
            ResolveCombatItem(acting, command);
        }

        /// <summary>
        /// The arena's half — <c>combat_arena_resume_dispatch</c> @COMBAT.C:1762.
        /// </summary>
        /// <remarks>
        /// <b>The refusals live here, not in the screen.</b> The Lightning Staff underground and the
        /// Idol of Lassur in chapter 8 are refused by the arena as well as by the screen, and
        /// <c>CombatItemUse.Works</c> carries that pair — see the selection rule's remarks for why
        /// it is not duplicated on the way in.
        ///
        /// <para><b>NOTHING IS CONSUMED HERE YET, and that is deliberate.</b> The arena's shared
        /// tail runs <c>itemtbl_inv_consume_one_by_kind</c> — but only after the arm has actually
        /// done something. Consuming before the effects exist would take the item and give nothing
        /// back, which is worse than the item being unusable. The effects are the remaining work.</para>
        /// </remarks>
        private void ResolveCombatItem(Combatant acting, int objectId) {
            GameData.Resources.Combat.CombatItemUse.Use? arm =
                GameData.Resources.Combat.CombatItemUse.For(objectId);
            if (arm == null) {
                // Not one of the ten. The original's switch falls through its default and the item
                // simply does nothing in a fight.
                return;
            }

            if (!GameData.Resources.Combat.CombatItemUse.Works(
                    arm.Value, _underground, _session?.Chapter ?? 0)) {
                // *** A REFUSED ITEM CAN STILL MAKE A NOISE. *** The Lightning Staff underground is
                // `audio_play(0x13); return;` — the cue plays INSTEAD of the effect, and the item is
                // not consumed. The Idol's chapter-8 refusal carries no sound and stays silent
                // because its arm has no SoundId, so this needs no second condition.
                if (arm.Value.SoundId != 0) {
                    _playSfx?.Invoke(arm.Value.SoundId);
                }
                _logger?.LogDebug(
                    $"Combat item {objectId} is refused here (underground {_underground}, "
                    + $"chapter {_session?.Chapter ?? 0}).");
                return;
            }

            if (arm.Value.Targeting == GameData.Resources.Combat.CombatItemUse.Targeting.None) {
                ApplyUntargetedCombatItem(acting, arm.Value);
                return;
            }

            // *** NO SPELL RECORD, NO ARMING. *** The cast screen's own arming path learned this the
            // hard way: a mode set with nothing to resolve makes ResolveCombatTargetClick refuse
            // every click WITHOUT disarming, and the fight wedges with no way out. An item has no
            // screen to fetch its record from, so it is looked up here and checked before arming.
            if (arm.Value.Effect == GameData.Resources.Combat.CombatItemUse.Effect.CastSpell) {
                if (_spells?.Spells == null
                    || !_spells.Spells.TryGetValue(arm.Value.SpellId,
                        out GameData.Resources.Spells.Spell record)) {
                    _logger?.LogDebug($"Combat item {objectId} casts spell {arm.Value.SpellId}, "
                        + "which is not in the catalogue; not arming target selection.");
                    return;
                }
                _pendingSpell = record;
                _pendingSpellId = arm.Value.SpellId;
                // The arm's own cost, which is NEGATIVE by convention -- see SpellCostModifiers.
                _pendingSpellPower = arm.Value.Cost;
            }

            _pendingCombatItem = arm.Value;
            _pendingCombatMode =
                GameData.Resources.Combat.CombatCommandOutcome.PendingMode.TargetSelection;
        }

        /// <summary>
        /// Resolves an armed combat item against the clicked combatant.
        /// </summary>
        /// <remarks>
        /// <b>The target rules are the item's, not the shot's.</b> It must be an encounter actor and
        /// alive — the party is never a target, exactly as for a shot — and an
        /// <c>AdjacentEnemy</c> arm additionally needs the target ORTHOGONALLY adjacent to the user
        /// (<c>combatgrid_actors_ortho_adj</c>). Diagonals do not count, which is the argument this
        /// task warns about getting backwards: read the other way, a Powder Bag flies across the
        /// arena.
        ///
        /// <para><b>A miss disarms rather than wedging.</b> Clicking empty ground or your own line
        /// ends target selection and costs the turn nothing — the alternative, returning without
        /// disarming, is the wedge the cast path documents.</para>
        ///
        /// <para><b>Nothing is consumed here yet</b>, for the reason the dispatch records: the
        /// arena's tail consumes by kind only after an arm has done something, and only the cast
        /// arms can.</para>
        /// </remarks>
        private void ResolveArmedCombatItem(Combatant acting, Combatant target) {
            GameData.Resources.Combat.CombatItemUse.Use arm = _pendingCombatItem.Value;
            _pendingCombatItem = null;

            bool aimable = target != null && !target.IsPartyMember && !target.IsDead;
            if (aimable
                && arm.Targeting
                    == GameData.Resources.Combat.CombatItemUse.Targeting.AdjacentEnemy) {
                // Orthogonal only: a diagonal is (1,1) and sums to 2. Shared with the melee arms,
                // which draw the same line -- CombatGrid.OrthogonallyAdjacent.
                aimable = GameData.Resources.Combat.CombatGrid.OrthogonallyAdjacent(
                    acting.X, acting.Y, target.X, target.Y);
            }

            if (!aimable) {
                _pendingSpell = null;
                EndTargetSelection();
                return;
            }

            var acted = false;
            switch (arm.Effect) {
                case GameData.Resources.Combat.CombatItemUse.Effect.CastSpell:
                    if (_pendingSpell != null) {
                        Combat.ResolveCast(acting, target, _pendingSpell, _pendingSpellId,
                            _pendingSpellPower, _random);
                        acted = true;
                    }
                    break;

                // *** NO ROLL. *** combat_arena_actor_die outright — the Idol does not test
                // anything, which is why its only guard is the chapter.
                case GameData.Resources.Combat.CombatItemUse.Effect.SlayTarget:
                    Combat.KillForTest(target);
                    acted = true;
                    break;

                // Routing is a FLAG, not a removal: the target walks toward its flee tile and leaves
                // the field on arrival. Killing it here instead would leave a corpse to loot and
                // rob the player of nothing, which is not what running away means.
                case GameData.Resources.Combat.CombatItemUse.Effect.RouteOne:
                    target.Flags |= GameData.Resources.Combat.CombatantFlags.Fleeing;
                    acted = true;
                    break;

                default:
                    _logger?.LogDebug(
                        $"Combat item effect {arm.Effect} is not wired yet; nothing was consumed.");
                    break;
            }

            _pendingSpell = null;
            if (acted) {
                SpendCombatItem(acting, arm.ObjectId);
                _redrawArena?.Invoke();
            }

            EndTargetSelection();
        }

        /// <summary>
        /// The arms that pick nothing — currently the team rout.
        /// </summary>
        /// <remarks>
        /// <b>Routing a team is the same flag as routing one</b>, applied to every live encounter
        /// actor: each then walks to its own flee tile and leaves on arrival, so the fight empties
        /// over the next few turns rather than ending on the spot.
        /// </remarks>
        private void ApplyUntargetedCombatItem(
            Combatant acting, GameData.Resources.Combat.CombatItemUse.Use arm) {
            if (arm.Effect == GameData.Resources.Combat.CombatItemUse.Effect.AmplifiedCast) {
                OpenAmplifiedCast(acting, arm);
                return;
            }
            if (arm.Effect == GameData.Resources.Combat.CombatItemUse.Effect.Summon) {
                ArmSummonPlacement(acting, arm);
                return;
            }
            if (arm.Effect != GameData.Resources.Combat.CombatItemUse.Effect.RouteTeam) {
                _logger?.LogDebug(
                    $"Combat item effect {arm.Effect} needs no target and is not wired yet; "
                    + "nothing was consumed.");
                return;
            }

            // *** BEFORE THE ROUT, NOT AFTER IT. *** audio_play(0x4b) is the first statement of
            // case 0x34, so the Fork sounds even when there is nobody left to rout and the item is
            // not spent.
            if (arm.SoundId != 0) {
                _playSfx?.Invoke(arm.SoundId);
            }

            GameData.Resources.Combat.CombatEncounter fight = Combat?.Encounter;
            if (fight == null) {
                return;
            }
            var routed = 0;
            foreach (Combatant enemy in fight.Enemies) {
                if (!enemy.IsDead) {
                    enemy.Flags |= GameData.Resources.Combat.CombatantFlags.Fleeing;
                    routed++;
                }
            }
            if (routed == 0) {
                // Nothing left to rout: the item is not spent on an empty field.
                return;
            }
            SpendCombatItem(acting, arm.ObjectId);
            _redrawArena?.Invoke();
        }

        /// <summary>
        /// The Infinity Pool — <c>combat_arena_resume_dispatch</c>'s case 0x0d.
        /// </summary>
        /// <remarks>
        /// <b>The Pool buys a cast, and it is refunded unless one happens.</b> The original's arm
        /// returns early — WITHOUT reaching the consuming tail — on both of its exits: a caster who
        /// cannot cast, and a cast menu that comes back <c>-1</c>. Only a committed spell raises
        /// <c>g_bStormAmplify</c> and spends the item, so backing out of the menu costs nothing.
        /// That is why the spend lives in the commit handler rather than here.
        /// </remarks>
        private void OpenAmplifiedCast(
            Combatant acting, GameData.Resources.Combat.CombatItemUse.Use arm) {
            if (Combat == null || !Combat.CapabilitiesFor(acting).CanCast) {
                // combatenc_actor_can_cast_spells said no: the menu never opens and the Pool stays
                // in the pack.
                _logger?.LogDebug("Amplified cast: this character cannot cast here; "
                    + "the Infinity Pool was not spent.");
                return;
            }

            _pendingAmplifier = arm.ObjectId;
            _pendingAmplifierUser = acting;
            OpenCastScreen(acting);
        }

        /// <summary>The Infinity Pool waiting on a cast to amplify, or -1.</summary>
        private int _pendingAmplifier = -1;

        /// <summary>Who is holding the Pool from <see cref="_pendingAmplifier"/>.</summary>
        private Combatant _pendingAmplifierUser;

        /// <summary>How many creatures are still waiting for a tile, and which.</summary>
        /// <remarks>
        /// <b>The Horn of Algon Kokoon summons TWICE.</b> Its arm calls
        /// <c>cspell_summon_monster(0x2e, 1)</c>, waits for the player to confirm, and calls it
        /// again — so one use is two placements, and a count rather than a flag is what the original
        /// needs. Eliaem's Heart (0x09) asks for one.
        /// </remarks>
        private int _pendingSummonsLeft;

        /// <inheritdoc cref="_pendingSummonsLeft"/>
        private int _pendingSummonCreature = -1;

        /// <summary>The spell a pending summon was cast from, or null when an item raised it.</summary>
        /// <remarks>
        /// A cast summon is billed and spends the turn once it is placed — see
        /// <see cref="BakAgain.Combat.CombatRuntime.SummonByCast"/>. An item's summon was paid for when
        /// the item was used.
        /// </remarks>
        private GameData.Resources.Spells.Spell _pendingSummonSpell;

        private int _pendingSummonSpellId = -1;

        private int _pendingSummonSpellPower;

        /// <summary>Whom a pending Dannon's Delusions decoy will copy, or null when none is pending.</summary>
        /// <remarks>
        /// The spell resolves on its target and then its kind-17 arm asks for a tile
        /// (<c>cspell_summon_actor</c>); the arena clears the caster's Ready only after that. So the
        /// turn is held until the placement click — see <see cref="ResolveCombatGroundClick"/>.
        /// </remarks>
        private Combatant _pendingDecoyOf;

        private Combatant _pendingDecoyCaster;

        private int _pendingDecoyDuration;

        /// <summary>
        /// The two <c>Effect.Summon</c> arms — <c>combat_arena_resume_dispatch</c> cases 0x0b and 0x09.
        /// </summary>
        /// <remarks>
        /// <b>The item is spent on ARMING, not on placing.</b> Both arms fall into the consuming
        /// tail unconditionally: <c>cspell_summon_monster</c> returns nothing a caller could refuse,
        /// and <see cref="GameData.Resources.Combat.SummonPlacement.CanCancel"/> is false. So a
        /// player who has raised the prompt has already paid, and there is no path back — which is
        /// why the placement cannot be abandoned and the count is not refunded.
        /// </remarks>
        private void ArmSummonPlacement(
            Combatant acting, GameData.Resources.Combat.CombatItemUse.Use arm) {
            if (Combat?.Encounter == null) {
                return;
            }
            // The Horn of Algon Kokoon opens with audio_play(0x4c), before either summon.
            if (arm.SoundId != 0) {
                _playSfx?.Invoke(arm.SoundId);
            }

            _pendingSummonCreature = arm.SummonCreature;
            _pendingSummonsLeft = arm.SummonCount;
            _pendingSummonSpell = null;
            SpendCombatItem(acting, arm.ObjectId);
            _logger?.LogDebug($"Summon armed: {arm.SummonCount} x creature "
                + $"{arm.SummonCreature}; waiting for a tile.");
        }

        /// <summary>
        /// A click on the arena floor — <c>cspell_select_target_tile</c>'s half of a summon.
        /// </summary>
        /// <remarks>
        /// <b>A tile is a SUMMON when one is pending, and the acting character's MOVE otherwise.</b>
        /// This used to return immediately outside a summon, and said so — which meant the player
        /// could attack, shoot, cast and rest but never walk, and a trap-puzzle room could not be
        /// solved because solving one is walking into diamonds. The original's arm is
        /// <c>combat_arena_resolve_menu_action</c> @0x6279f; see
        /// <see cref="CombatRuntime.MoveToTile"/> for its three rules.
        ///
        /// <para><b>The gates on the move arm are here, not in the runtime</b>, because they are
        /// about whose turn it is rather than about walking: only the acting character moves, only
        /// on the party's own turn, and only while nothing else is armed. The original gets the
        /// last of those for free by reaching this arm only from the idle state.</para>
        ///
        /// <para>The point arrives in Unity world space from <c>WorldPicker.PickGroundPoint</c>;
        /// BaK x is Unity x and BaK y is Unity <b>z</b>, both scaled back up by
        /// <c>WorldScale</c>. Reading Unity y as BaK y is the mistake the converter's axis swap
        /// invites, and it would put every click on the floor plane at BaK y = 0.</para>
        /// </remarks>
        /// <summary>
        /// Turn the acting combatant to face the cursor — <c>combat_actor_face_cursor</c> @0x5EBBB.
        /// </summary>
        /// <returns>The combatant that turned, or null when nothing changed.</returns>
        /// <remarks>
        /// <b>The active actor faces the CURSOR</b>, not its target and not the way it last moved.
        /// The original asks <c>combat_actor_direction_to_cursor</c> @0x5EB01 for a direction and,
        /// if it differs from the current one, replays the walk animation with it —
        /// <c>startCreatureBitmapAnimation</c> @0x5EC23 writes the facing on the way, which is why
        /// turning is a side effect of choosing a pose rather than a step of its own.
        ///
        /// <para><b>The already-facing early-out is load-bearing, not an optimisation.</b> The
        /// original returns before touching the animation when the direction is unchanged; without
        /// it the three-frame gait restarts from frame 0 on every mouse move, which reads as a
        /// twitch. Ours returns null for the same reason.</para>
        ///
        /// <para>Answering with the combatant rather than turning the sprite here keeps the
        /// GameObjects on the side of the code that owns them: this sets the model's facing, and the
        /// arena re-aims the live sprite without a rebuild.</para>
        /// </remarks>
        public GameData.Resources.Combat.Combatant TurnActingTowardCursor(UnityEngine.Vector3 point) {
            GameData.Resources.Combat.Combatant acting = Combat?.Encounter?.Current;
            if (acting == null || acting.IsDead || _session == null || _start == null) {
                return null;
            }
            int cellSize = _start.CombatGridCellSize;
            if (cellSize <= 0) {
                return null;
            }
            var bakX = (int)(point.x * World.Converters.BakCoordinateConverter.WorldScale);
            var bakY = (int)(point.z * World.Converters.BakCoordinateConverter.WorldScale);
            (int Column, int Row)? cell = World.Encounters.ArenaCellPicker.CellAt(
                bakX, bakY, (int)_session.PositionX, (int)_session.PositionY,
                _session.Rotation, cellSize);
            if (cell == null) {
                return null;
            }

            int octant = GameData.Resources.Combat.ArenaFacing.OctantToward(
                cell.Value.Column - acting.X, cell.Value.Row - acting.Y);
            if (octant < 0 || octant == acting.FacingOctant) {
                return null;   // the original's "already facing there" return
            }
            acting.FacingOctant = octant;

            return acting;
        }

        public void ResolveCombatGroundClick(UnityEngine.Vector3 point) {
            if (Combat?.Encounter == null || _session == null || _start == null) {
                return;
            }
            int cellSize = _start.CombatGridCellSize;
            var bakX = (int)(point.x * World.Converters.BakCoordinateConverter.WorldScale);
            var bakY = (int)(point.z * World.Converters.BakCoordinateConverter.WorldScale);
            (int Column, int Row)? cell = World.Encounters.ArenaCellPicker.CellAt(
                bakX, bakY, (int)_session.PositionX, (int)_session.PositionY,
                _session.Rotation, cellSize);
            if (cell == null) {
                return;
            }

            // *** A CELL-AIMED SPELL IS AIMED AT THIS CLICK, NOT AT AN ACTOR. *** The cast arm
            // lives in ResolveCombatTargetClick, which only fires when the click lands ON a
            // combatant — and a crystal cell has none. Without this branch a player with Black
            // Nimbus armed clicked the crystal and WALKED there, spending the turn on a move they
            // did not ask for. Types 5 and 6 (Aim.ClearGround) fell in the same hole.
            if (_pendingSpell != null
                && GameData.Resources.Spells.SpellTargetingRules.CastsWithoutATarget(
                    _pendingSpell.TargetingType)) {
                if (GroundCellRefused(cell.Value.Column, cell.Value.Row)) {
                    return;
                }
                ResolveCellCast(cell.Value.Column, cell.Value.Row);
                return;
            }

            if (_pendingDecoyOf != null) {
                if (!GameData.Resources.Combat.SummonPlacement.Accepts(
                        Combat.Grid, cell.Value.Column, cell.Value.Row)) {
                    return;
                }
                Combat.SummonDecoy(_pendingDecoyOf, _pendingDecoyDuration,
                    cell.Value.Column, cell.Value.Row);
                Combatant decoyCaster = _pendingDecoyCaster;
                _pendingDecoyOf = null;
                _pendingDecoyCaster = null;
                if (decoyCaster != null) {
                    decoyCaster.Flags &= ~GameData.Resources.Combat.CombatantFlags.Ready;
                }
                RefreshCombatHud();
                return;
            }

            // *** AN ARMED SHOT, SPELL OR INSPECT HAS NO MOVE ARM. *** combat_arena_resolve_menu_action
            // moves the actor only in states -1, 0 and 1; states 3 and 4 answer a click on ground with
            // nothing. Live on SAVE88 with Invitation armed, the original kept Owyn at (4,0) and the
            // prompt up, while this walked him to (3,2) and spent his turn with the spell still
            // armed (TASK-546).
            if (_pendingCombatMode == GameData.Resources.Combat.CombatCommandOutcome.PendingMode.TargetSelection
                || _pendingCombatMode == GameData.Resources.Combat.CombatCommandOutcome.PendingMode.InspectTarget) {
                return;
            }

            if (_pendingSummonsLeft <= 0) {
                ResolveMoveClick(cell.Value.Column, cell.Value.Row);
                return;
            }

            // Highlights and Accepts are deliberately different questions: crystal ground lights up
            // and then swallows the click. A refused tile leaves the summon still pending, because
            // the placement cannot be cancelled and the item is already spent.
            if (!GameData.Resources.Combat.SummonPlacement.Accepts(
                    Combat.Grid, cell.Value.Column, cell.Value.Row)) {
                return;
            }

            Combatant caster = Combat.Encounter.Current;
            bool fromSpell = _pendingSummonSpell != null;
            Combatant summoned = fromSpell
                ? Combat.SummonByCast(caster, _pendingSummonSpell, _pendingSummonSpellId,
                    _pendingSummonSpellPower, cell.Value.Column, cell.Value.Row, _random)
                : Combat.Summon(_pendingSummonCreature, cell.Value.Column, cell.Value.Row);
            if (summoned == null) {
                // The seven-actor array is full — combat_actor_slot_append @0x5c502 refuses at
                // seven and shows this dialog, which was modelled from the start and unreachable
                // until CombatRuntime.Summon learned to count. The summon stays PENDING rather than
                // being consumed: the original spends the cast either way, but this is the ITEM
                // path, where refusing silently would leave the player holding neither the item nor
                // a creature.
                _dialogs?.ShowById(GameData.Resources.Combat.MonsterSummon.NoRoomDialog).Forget();
                return;
            }

            _pendingSummonsLeft--;
            if (!fromSpell) {
                _redrawArena?.Invoke();
                return;
            }

            // *** THE CAST IS SPENT. *** COMBAT.C:2426 clears CAF_READY after cspell_resolve_cast, and
            // RefreshCombatHud only advances past a character who is no longer ready — so without
            // this the caster summoned and then kept the turn. RefreshCombatHud redraws the arena
            // itself; a second redraw here is the double rebuild TASK-103 removed from the move arm.
            _pendingSummonSpell = null;
            if (caster != null) {
                caster.Flags &= ~GameData.Resources.Combat.CombatantFlags.Ready;
            }
            RefreshCombatHud();
        }

        /// <summary>
        /// A cell-aimed cast: the spell resolves at the clicked cell rather than on a combatant.
        /// </summary>
        /// <remarks>
        /// <b>The cell is the target.</b> <c>cspell_resolve_cast</c> nulls the target for these
        /// spells on the way in (<c>nSpell_kind == 8</c>) and their handlers read
        /// <c>g_cursor_tile_x/y</c> instead, so the cell has to travel with the cast — passing null
        /// alone, as this seam did, leaves the handler with nothing to act on.
        ///
        /// <para>Spends the turn either way, like every other cast
        /// (<c>SpellTargetingRules.CastingEndsTheTurn</c>) and like the move click beside it.</para>
        /// </remarks>
        /// <summary>
        /// Whether the armed spell is ground-aimed and this cell may not take it.
        /// </summary>
        /// <remarks>
        /// The target cursor's arm for types 5 and 6 (COMBAT.C ~2218-2226) breaks out before
        /// <c>validate_common</c> when <c>combatgrid_tile_is_blocked</c> says so: a wall, an element,
        /// or anybody standing there. In V102CD it also breaks when
        /// <c>combatgrid_tile_walkable_kind(x, y, -1)</c> finds a trap crystal. A refused click commits
        /// nothing, so the spell stays armed for the next click, as the original's cursor does.
        /// </remarks>
        private bool GroundCellRefused(int column, int row) =>
            _pendingSpell != null && Combat?.Grid != null
            && GameData.Resources.Spells.SpellTargetingRules.AimOf(_pendingSpell.TargetingType)
                == GameData.Resources.Spells.SpellTargetingRules.Aim.ClearGround
            && !GameData.Resources.Spells.SpellTargetingRules.GroundIsTargetable(
                blocked: Combat.Grid.IsBlocked(column, row),
                hasCrystal: GameData.Resources.Combat.CrystalChain.TilePermitsMovement(
                    Combat.Puzzle, column, row));

        private void ResolveCellCast(int column, int row) {
            Combatant acting = Combat.Encounter.Current;
            if (acting == null || !acting.IsPartyMember || acting.IsDead
                || !acting.CanAct(strict: true)) {
                return;
            }

            Combat.ResolveCast(acting, null, _pendingSpell, _pendingSpellId, _pendingSpellPower,
                _random, groundCell: (column, row));
            _pendingSpell = null;
            // Clearing Ready is what spends the turn — the same line the target-click arm draws,
            // and for the same reason: RefreshCombatHud only advances past a character who is no
            // longer ready, so without it the caster keeps casting until the pool runs out.
            acting.Flags &= ~GameData.Resources.Combat.CombatantFlags.Ready;
            RefreshCombatHud();
        }

        /// <summary>
        /// A click on an empty tile with nothing armed: the acting character walks there.
        /// </summary>
        /// <remarks>
        /// <b>Only the acting character, only on the party's turn, and only with nothing pending.</b>
        /// The third is already true here — a pending summon took the branch above, and every other
        /// mode goes through <see cref="ResolveCombatTargetClick"/>, which owns the target seam
        /// rather than the ground one.
        ///
        /// <para>The click costs the turn whether or not the walk got anywhere; that is the
        /// original's rule and <see cref="CombatRuntime.MoveToTile"/> explains why it is kept.</para>
        /// </remarks>
        private void ResolveMoveClick(int column, int row) {
            Combatant acting = Combat.Encounter.Current;
            if (acting == null || !acting.IsPartyMember || acting.IsDead
                || !acting.CanAct(strict: true)) {
                return;
            }

            // COMBAT.C:2333 clears the target before a move, so the turn's end faces the NEAREST
            // opponent rather than whoever this actor last swung at (CombatEncounter.FaceTarget).
            acting.Target = null;
            Combat.MoveToTile(acting, column, row, _random);
            // *** ONE REDRAW, NOT TWO. *** RefreshCombatHud's first line is RedrawArena, and this
            // arm used to invoke the redraw again after it. Wasted work either way, but it also made
            // WorldRuntime's move slide impossible: the first rebuild had already placed the sprite
            // on its destination, so the slide found nothing to move and every step still looked
            // like a teleport. See TASK-103.
            RefreshCombatHud();
        }

        /// <summary>
        /// The arena's shared tail — <c>itemtbl_inv_consume_one_by_kind(inventory, command_id)</c>.
        /// </summary>
        /// <remarks>
        /// <b>This is the ONLY place a combat item is consumed.</b> The inventory screen
        /// deliberately does not, because its consume block is guarded by <c>combat_result &lt; 0</c>
        /// — consuming in both eats two of the item.
        ///
        /// <para><b>And it runs only after an arm has actually acted.</b> A refused item, an
        /// unwired effect or a rout with nothing to rout leaves the item in the pack; taking it and
        /// giving nothing back is the failure this task exists to avoid.</para>
        /// </remarks>
        private void SpendCombatItem(Combatant acting, int objectId) {
            // *** A SEAT IS NOT A CHARACTER INDEX. *** GetActorInventory is keyed by the character's
            // position in the party roster, which is what a party combatant's ClassId already holds
            // (the same value WriteBack passes to StatsOf). Passing the SEAT — the index within
            // ActivePartyIndices — resolves the wrong member's pack whenever the two differ, and
            // then consumes nothing at all because the item is not in it. Found by driving a live
            // fight: the Tuning Fork routed the team and stayed in the pack.
            GameData.Resources.Inventory.RuntimeContainer pack =
                _session?.GetActorInventory(acting.ClassId);
            if (pack == null) {
                return;
            }
            GameData.Resources.Inventory.InventoryConsume.TryConsumeOne(
                pack, objectId, id => _session.ObjectInfo?.GetById(id));
        }

        /// <summary>The combat item waiting for a target, or null.</summary>
        /// <remarks>
        /// Armed the way a shot or a spell is: the mode goes to target selection and the next click
        /// on a combatant resolves it. Only <c>AnyEnemy</c> and <c>AdjacentEnemy</c> arms get here —
        /// a <c>Targeting.None</c> item has nothing to wait for.
        /// </remarks>
        private GameData.Resources.Combat.CombatItemUse.Use? _pendingCombatItem;

        private void OpenCastScreen(Combatant acting) {
            BakAgain.UI.Spells.CastScreen screen = _castScreenAccessor?.Invoke();
            if (screen == null) {
                _logger?.LogDebug("Combat HUD: Cast pressed, but there is no cast screen to raise.");
                return;
            }

            // Unsubscribe first, for the reason both combat menus do: these are singletons and a
            // second fight must not stack a handler that answers one commit twice.
            screen.Committed -= OnCombatSpellCommitted;
            screen.Committed += OnCombatSpellCommitted;

            System.Collections.Generic.IReadOnlyList<byte> roster = _session?.ActivePartyIndices;
            var slot = -1;
            for (var i = 0; roster != null && i < roster.Count; i++) {
                if (roster[i] == acting.ClassId) {
                    slot = i;
                    break;
                }
            }
            if (slot < 0) {
                _logger?.LogDebug("Combat HUD: the acting character is not in the active roster; "
                    + "Cast refused.");
                return;
            }

            BakAgain.UI.Navigation.IScreenNavigator navigator = _navigatorAccessor?.Invoke();
            if (navigator == null) {
                _logger?.LogDebug("Combat HUD: Cast pressed, but there is no screen stack to raise "
                    + "the picker on.");
                return;
            }

            CastForActingCharacterAsync(screen, navigator, slot, acting).Forget();
        }

        /// <summary>
        /// The push first, then the caster.
        /// </summary>
        /// <remarks>
        /// <b>Through the navigator, never <c>ShowAsync</c>.</b> <c>ScreenBase</c> states the rule
        /// outright — only the navigator shows and hides screens — and it is not a style
        /// preference: <c>Push</c> owns the stack and hides whatever was on top, so a direct show
        /// leaves the travel screen live underneath and nothing able to pop the picker again.
        ///
        /// <para><b>THE ORDER IS PUSH-THEN-SELECT, AND THE OTHER WAY ROUND SILENTLY EMPTIES THE
        /// RING.</b> This was written select-then-push on the reasoning that the ring is built from
        /// whoever is casting — which is true and is exactly why it has to be second.
        /// <c>SelectCaster</c> ends in <c>DrawRingAsync</c>, which bails the moment <c>Stage()</c>
        /// is null, and <c>Stage()</c> reads <c>UIDocument.rootVisualElement</c> — null until the
        /// GameObject is active. So selecting first returned before it reached the line that loads
        /// the spell catalogue, leaving <c>_spells</c> null with the screen up: no castable spells,
        /// no symbols, and nothing the player can pick. Verified in a live fight 2026-08-29.</para>
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTask CastForActingCharacterAsync(
            BakAgain.UI.Spells.CastScreen screen, BakAgain.UI.Navigation.IScreenNavigator navigator,
            int slot, Combatant acting) {
            // *** THE SUMMON CAP IS A CASTABILITY RULE, NOT A FAILURE. *** With seven on the party
            // side cspell_check_castable refuses kind-6 spells outright, so they never reach the
            // ring. Supplied only here: out of combat the original's counter is zero and every
            // summon is offered. See CastScreen.FightActorCount.
            screen.FightActorCount = () => Combat?.Encounter?.Party?.Count ?? 0;

            // *** BEFORE THE PUSH, BECAUSE THE PUSH IS WHAT LOADS THE REQ. *** This is what makes
            // the screen build SPELL.DAT rather than REQ_CAST.DAT, and the two enable different
            // school buttons — the field layout offers a combatant the field's spells and hides
            // every combat one behind a disabled stone (TASK-367).
            screen.CombatCaster = acting;
            await navigator.Push(screen);
            await screen.SelectCaster(slot);
        }

        /// <summary>
        /// A spell was chosen and powered while a fight is running.
        /// </summary>
        /// <remarks>
        /// <b>This arms targeting; it does not cast.</b> <c>CombatCommandOutcome.ModeAfterCast</c>
        /// says the same thing — a chosen spell leaves
        /// <see cref="GameData.Resources.Combat.CombatCommandOutcome.PendingMode.TargetSelection"/>
        /// armed and a cancel leaves <c>CastCancelled</c>. The click that follows is what spends the
        /// turn.
        ///
        /// <para><b>Out of combat this must do nothing at all</b>, or a field cast would be answered
        /// twice: <c>InGameScreen.OnSpellCommitted</c> owns that path and routes to
        /// <c>FieldSpellCaster</c>.</para>
        /// </remarks>
        private void OnCombatSpellCommitted(int spellNumber, int power, int duration) {
            // *** TAKEN BEFORE THE FIRST GUARD, SO EVERY EXIT BELOW DROPS IT. *** A Pool left armed
            // would amplify whatever the player casts next, for free.
            int amplifier = _pendingAmplifier;
            Combatant amplifierUser = _pendingAmplifierUser;
            _pendingAmplifier = -1;
            _pendingAmplifierUser = null;

            if (Combat?.Encounter == null) {
                return;
            }

            var mode = GameData.Resources.Combat.CombatCommandOutcome.ModeAfterCast(
                spellChosen: power > 0);
            if (mode != GameData.Resources.Combat.CombatCommandOutcome.PendingMode.TargetSelection) {
                // CastCancelled clears the pending selection, which is what a cancel should do.
                _pendingCombatMode = GameData.Resources.Combat.CombatCommandOutcome.PendingMode.None;
                _pendingSpell = null;
                return;
            }

            // *** NO RECORD, NO ARMING. *** Found by driving a real fight on 2026-08-29: the screen
            // can raise a commit while its catalogue is still null, and arming on that leaves the
            // mode set with no spell to cast. ResolveCombatTargetClick then refuses every click and
            // returns WITHOUT disarming, so the fight wedges — the player is stuck in a targeting
            // mode that can never resolve and whose only exit (the shoot menu's Back) is not up.
            GameData.Resources.Spells.Spell record = _castScreenAccessor?.Invoke()?.CommittedSpell;
            if (record == null) {
                _logger?.LogDebug($"Combat cast: spell {spellNumber} committed with no catalogue "
                    + "record; not arming target selection.");
                _pendingCombatMode = GameData.Resources.Combat.CombatCommandOutcome.PendingMode.None;
                _pendingSpell = null;
                return;
            }

            if (amplifier >= 0) {
                // The menu returned a spell rather than -1, so the arm reaches the shared tail:
                // raise the flag and spend the Pool.
                Combat.SurchargeNextCast = true;
                SpendCombatItem(amplifierUser, amplifier);
            }

            // *** A SUMMONING SPELL PICKS A TILE, NOT A COMBATANT. *** TargetingType 6 is what
            // makes the polymorphic +10 field a creature id (Spell.SummonedCreatureId), and the
            // original's kind-6 arm calls cspell_summon_monster with prompt_for_tile = 0 — it does
            // not ask, because the cast's own targeting already put the cursor on a tile. Arming
            // combatant selection for one of these would offer the player an enemy to click and
            // then summon on top of it.
            int? summoned = record.SummonedCreatureId;
            if (summoned != null) {
                _pendingSummonCreature = summoned.Value;
                _pendingSummonsLeft = 1;
                _pendingSummonSpell = record;
                _pendingSummonSpellId = spellNumber;
                _pendingSummonSpellPower = power;
                _pendingCombatMode =
                    GameData.Resources.Combat.CombatCommandOutcome.PendingMode.None;
                _pendingSpell = null;
                _logger?.LogDebug($"Cast {spellNumber} summons creature {summoned.Value}; "
                    + "waiting for a tile.");
                return;
            }

            _pendingSpell = record;
            _pendingSpellId = spellNumber;
            _pendingSpellPower = power;
            _pendingCombatMode =
                GameData.Resources.Combat.CombatCommandOutcome.PendingMode.TargetSelection;
        }

        /// <summary>The spell record chosen for a pending combat cast, or null.</summary>
        /// <remarks>Taken from the screen at commit time rather than looked up — see
        /// <c>CastScreen.CommittedSpell</c>.</remarks>
        private GameData.Resources.Spells.Spell _pendingSpell;

        private int _pendingSpellId = -1;

        private int _pendingSpellPower;

        /// <summary>
        /// Auto-resolve: play both sides until a side is gone or the player interrupts.
        /// </summary>
        /// <remarks>
        /// <b>IT IS NOT "hand the fight to the AI and let it play out".</b> That reading writes a
        /// loop that runs to a winner and returns, which gives the player no way out of a battle
        /// going badly — the exact situation the button exists for.
        /// <see cref="GameData.Resources.Combat.AutoResolveLoop"/> has the shape: enemy turns run
        /// back to back with no interruption, and a PARTY turn is where the loop yields so a press
        /// can be seen. The interruption granularity is one party turn, not one turn.
        ///
        /// <para><b>The party's turns are played by the SAME AI</b>, which is why an auto-resolved
        /// party fights exactly like a monster would. The original does it by swapping a global pair
        /// of actor lists; <c>MonsterTurnResolver</c> derives the sides from the acting combatant
        /// instead, so passing a party member simply works.</para>
        ///
        /// <para><b>Refused outright when the grid carries an objective</b>
        /// (<see cref="GameData.Resources.Combat.CombatCommands.AutoResolveAllowed"/>) — a fight
        /// with something to do in it cannot be settled by counting bodies.</para>
        /// </remarks>
        private void StartAutoResolve() {
            if (Combat?.Encounter == null || _autoResolving) {
                return;
            }
            if (!GameData.Resources.Combat.CombatCommands.AutoResolveAllowed(
                    Combat.Encounter.HasObjective)) {
                _logger?.LogDebug("Auto-resolve refused: this fight has an objective.");
                return;
            }

            _autoResolveBail = false;
            AutoResolveAsync().Forget();
        }

        /// <summary>Set by any press that ends auto-resolve — Back, Cancel, or another command.</summary>
        private bool _autoResolveBail;

        private bool _autoResolving;

        private async Cysharp.Threading.Tasks.UniTask AutoResolveAsync() {
            _autoResolving = true;
            try {
                BakAgain.Combat.MonsterTurnResolver ai = EnemyAi();
                // A guard, not a rule: a fight that cannot end in this many party turns means
                // something below is not advancing, and spinning forever hides that.
                for (var guard = 0; guard < 512; guard++) {
                    GameData.Resources.Combat.CombatEncounter fight = Combat?.Encounter;
                    if (fight == null || _autoResolveBail) {
                        break;
                    }
                    if (GameData.Resources.Combat.AutoResolveLoop.Finished(
                            fight.EnemiesAlive(), fight.PartyAlive())) {
                        break;
                    }

                    // Enemy turns run back to back inside the advance; it returns on the first party
                    // member due to act. PACED, like a hand-played round: the original's auto-combat
                    // animates every move and swing (about 26 s for the chapter-1 road ambush,
                    // measured 2026-09-26), where the unpaced advance settled it in a third of a
                    // second with nothing on screen.
                    Combatant acting = await Combat.AdvanceToPartyTurnAsync(
                        monster => RunEnemyTurnPacedAsync(monster, ai));
                    if (acting == null || _autoResolveBail) {
                        break;
                    }

                    // *** THE YIELD IS THE FEATURE. *** Without it the whole fight resolves inside
                    // one frame and no press can ever be seen, which is the "no way out" failure
                    // the model warns about.
                    _redrawArena?.Invoke();
                    await Cysharp.Threading.Tasks.UniTask.Yield();
                    if (_autoResolveBail) {
                        break;
                    }

                    // The party member's turn, played by the same AI — and through the same
                    // delivery, which is what lets an auto-resolved party actually use Bane of
                    // Black Slayers and Final Rest on the creatures those spells exist for.
                    await RunEnemyTurnPacedAsync(acting, ai);
                    // combatenc_ai_run_turn faces the target after the AI acts (CBENC.C:984).
                    if ((acting.Flags & GameData.Resources.Combat.CombatantFlags.Fleeing) == 0) {
                        fight.FaceTarget(acting);
                    }
                    fight.EndTurn();
                }
            } finally {
                _autoResolving = false;
                _redrawArena?.Invoke();
                RefreshCombatHud();
            }
        }

        /// <summary>Put the melee menu back and disarm targeting.</summary>
        private void EndTargetSelection() {
            _pendingCombatMode = GameData.Resources.Combat.CombatCommandOutcome.PendingMode.None;
            BakAgain.UI.Combat.ShootMenu shoot = _shootMenuAccessor?.Invoke();
            if (shoot != null && shoot.IsOpen) {
                shoot.Close();
                _combatMenuAccessor?.Invoke()?.Open();
            }
        }

        /// <summary>
        /// The party's step size changed — reset the roamers if it GREW, and store the new
        /// baseline either way.
        /// </summary>
        /// <param name="stepDistance">
        /// The RESOLVED distance from <c>MovementData.StepDistanceFor</c>, not the preference
        /// index — the original seeks <c>movement.dat</c> by the preference and compares what it
        /// finds, so an enum here would make every change look like a step of one.
        /// </param>
        /// <returns>Whether the reset fired.</returns>
        /// <remarks>
        /// <b>The baseline is stored WHETHER OR NOT IT FIRED</b>, which is the whole reason it is
        /// mutable session state. Storing it only on a reset would re-fire on every subsequent
        /// preferences apply for as long as the larger step size stayed selected.
        ///
        /// <para><b>THE RE-SEED IS NOT OPTIONAL.</b> <c>ResetRoamers</c> leaves the slots
        /// <i>Pending</i>, not placed — so without seeding again in the same pass the roamers are
        /// reset and then simply absent until something else happens to seed that chunk, which
        /// looks to a player like they were deleted. The original calls
        /// <c>rgnenc_load_encounter_actors()</c> immediately afterwards; here that means dropping
        /// the placement cache, because <c>PlaceEncounterActors</c> seeds as part of rebuilding it
        /// and is deliberately cached against being run twice.</para>
        ///
        /// <para><b>The grid-stride arm is NOT implemented.</b> Its action is a party-heading snap
        /// (<c>worldmove_plr_hdg_align_grid</c>) which the port does not have, so the stride
        /// baseline is stored and nothing else — stated rather than silently half-done. Storing it
        /// still matters, because the baseline is a SAVED field: a save written with the wrong one
        /// is not the save the original would have written, and the whole differential harness rests
        /// on one .GAM meaning the same thing to both games.</para>
        /// </remarks>
        public bool OnStepSizeChanged(int stepDistance, int gridStride) {
            if (_session == null) {
                return false;
            }

            // Both baselines move on ANY change, fired or not — see StepSizeChange.NewBaseline.
            if (GameData.Resources.World.StepSizeChange.AlignsHeading(
                    _session.LastSeenGridStride, gridStride)) {
                // worldmove_plr_hdg_align_grid() would go here; see the remarks.
            }
            _session.LastSeenGridStride = (short)GameData.Resources.World.StepSizeChange.NewBaseline(
                _session.LastSeenGridStride, gridStride);

            bool resets = GameData.Resources.World.StepSizeChange.ResetsRoamers(
                _session.LastSeenStepSpeed, stepDistance);
            _session.LastSeenStepSpeed = (short)GameData.Resources.World.StepSizeChange.NewBaseline(
                _session.LastSeenStepSpeed, stepDistance);

            if (!resets) {
                return false;
            }

            GameData.Resources.World.EncounterObjectStates states = _session.EncounterActorStates;
            if (states == null) {
                return false;
            }

            int reset = states.ResetRoamers(_currentRefIndex);
            // Drop the cache so the next placement seeds the reset slots again. Without this they
            // stay Pending and the chunk draws no roamers at all. The re-placement itself is the
            // caller's: a true return makes WorldRuntime redraw the chunk's actors, as the original
            // re-places them straight after the reset (RGNENC.C:476). This used to invoke the ARENA
            // redraw instead, so outside a fight nothing re-placed them and a fight started before
            // the next chunk change wrote its bodies from the zeroed pose -- onto the tile's corner.
            _placedChunk = null;
            _placedActors = null;
            _logger?.LogDebug(
                "Step size grew to {Distance}; reset {Count} roaming slots in ref pair {Ref}.",
                stepDistance, reset, _currentRefIndex);
            return true;
        }

        /// <summary>The combatant a placement's marker names.</summary>
        /// <remarks>The slot alone is ambiguous between the two sides — see
        /// <c>EncounterActorPlacement.Placed.PartyMember</c>.</remarks>
        private static Combatant TargetAt(GameData.Resources.Combat.CombatEncounter fight,
            int rosterSlot, bool partyMember) {
            System.Collections.Generic.List<Combatant> side =
                partyMember ? fight.Party : fight.Enemies;
            if (partyMember) {
                foreach (Combatant c in side) {
                    if (c != null && c.PartySlot == rosterSlot) {
                        return c;
                    }
                }
                return null;
            }
            return rosterSlot >= 0 && rosterSlot < side.Count ? side[rosterSlot] : null;
        }

        /// <summary>
        /// Whether the cell under the cursor is a legal target for this spell.
        /// </summary>
        /// <remarks>
        /// <b>The rules are <see cref="GameData.Resources.Spells.SpellTargetingRules"/>'s, not a
        /// second copy of them.</b> That type has modelled the whole switch inside
        /// <c>combat_arena_disp_spell_action</c> since 2026-08-22 with no production caller; this is
        /// it. Only the actor-aimed aims are answered here — the ground and crystal ones are decided
        /// by the tile rather than by an occupant, and reach the cast with no target at all
        /// (<see cref="GameData.Resources.Spells.SpellTargetingRules.CastsWithoutATarget"/>).
        /// </remarks>
        private static bool SpellTargetIsValid(int targetingType, Combatant target) {
            GameData.Resources.Spells.SpellTargetingRules.Aim aim =
                GameData.Resources.Spells.SpellTargetingRules.AimOf(targetingType);
            switch (aim) {
                case GameData.Resources.Spells.SpellTargetingRules.Aim.NamedCharacter:
                    // Types 2 and 3 reject an actor number of zero, which is what monsters carry.
                    return target != null && target.IsPartyMember && !target.IsDead;
                case GameData.Resources.Spells.SpellTargetingRules.Aim.DownedActor:
                    // Type 7 is the coup de grace: the ONE aim that wants a DEAD target (CAF_DEAD).
                    return target != null && target.IsDead;
                case GameData.Resources.Spells.SpellTargetingRules.Aim.LivingActor:
                    return target != null && !target.IsPartyMember && !target.IsDead;
                default:
                    // ClearGround and Crystal are tile questions, answered by the cast itself.
                    return false;
            }
        }

        /// <summary>
        /// The acting member's target, as the armed turn loop writes it on every pass (TASK-540).
        /// </summary>
        /// <remarks>
        /// <b>The two arms differ in what they clear.</b> The shoot panel writes whoever is under the
        /// cursor, or nothing over a dead one or a cell off the fire map
        /// (<c>combat_arena_draw_tgt_info_panel</c>, COMBAT.C:1173-1180). An armed spell writes only a
        /// target <c>combat_arena_disp_spell_action</c> accepts (case 4 of
        /// <c>combat_arena_resolve_menu_action</c>, COMBAT.C:2408); any other cell drops the state for
        /// that pass, so the last aim stands. Live on SAVE88 with Invitation armed, Owyn's target
        /// became the troll under the cursor and stayed there over an empty cell and over himself.
        /// The monsters' Engaged and TargetingTheLeader roles read it.
        /// </remarks>
        private void RecordAim(GameData.Resources.Combat.CombatEncounter fight, Combatant acting,
            Combatant target, bool shooting) {
            if (shooting) {
                acting.Target = target != null && !target.IsDead && LineOfFireTo(fight, acting, target)
                    ? target
                    : null;
            } else if (_pendingSpell != null
                && SpellTargetIsValid(_pendingSpell.TargetingType, target)) {
                acting.Target = target;
            }
        }

        /// <summary>Whether the shooter has a clear projectile path — the shoot check's real question.</summary>
        /// <remarks><c>combatgrid_tile_has_terr_bit2</c> reads a per-turn map whose bit 1 is set by
        /// <c>combat_actor_trace_proj_path</c>, so the "terrain" flag IS line of fire. See
        /// <see cref="GameData.Resources.Combat.CombatTargetSelection.ShotIsValid"/>.</remarks>
        private static bool LineOfFireTo(GameData.Resources.Combat.CombatEncounter fight,
            Combatant shooter, Combatant target) =>
            GameData.Resources.Combat.CombatLineOfFire.IsClear(
                shooter.X, shooter.Y, target.X, target.Y,
                GameData.Resources.Combat.CombatLineOfFire.BlockedByLivingActor(
                    (x, y) => {
                        foreach (Combatant c in fight.AllCombatants()) {
                            if (c != null && c.X == x && c.Y == y) {
                                return c;
                            }
                        }
                        return null;
                    },
                    shooter));

        /// <summary>
        /// Advance to the next party turn, letting every enemy in between actually take its turn.
        /// </summary>
        /// <remarks>
        /// <b>The seam was always there and nobody ever passed it.</b> Both callers used the no-arg
        /// overload, whose contract is "an enemy with no resolver simply forfeits" — so every monster
        /// in the game stood still, and a fight could not be lost.
        /// </remarks>
        private Combatant AdvanceToPartyTurn() {
            BakAgain.Combat.MonsterTurnResolver ai = EnemyAi();
            return Combat.AdvanceToPartyTurn(monster => RunEnemyTurn(monster, ai));
        }

        /// <summary>The same advance, with each monster's turn drawn and given time to be seen.</summary>
        private Cysharp.Threading.Tasks.UniTask<Combatant> AdvanceToPartyTurnPacedAsync() {
            BakAgain.Combat.MonsterTurnResolver ai = EnemyAi();
            return Combat.AdvanceToPartyTurnAsync(monster => RunEnemyTurnPacedAsync(monster, ai));
        }

        /// <summary>
        /// One monster's turn, then the picture, then long enough to watch it.
        /// </summary>
        /// <remarks>
        /// <b>The redraw is what makes the move visible and the wait is what makes it legible.</b>
        /// Without the redraw the move showed up one party action late (TASK-581); without the wait
        /// every monster in the fight resolved inside a single frame and the whole enemy round was
        /// over before the player could look at it (TASK-596).
        ///
        /// <para>The wait is proportional to the cells walked, because that is what the original
        /// spends its time on — see <see cref="GameData.Resources.Combat.ArenaPacing"/> for the
        /// measurement. A monster that stayed put still gets a short beat, or two of them acting in
        /// a row read as one event.</para>
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTask RunEnemyTurnPacedAsync(
            Combatant monster, BakAgain.Combat.MonsterTurnResolver ai) {
            int fromX = monster?.X ?? 0;
            int fromY = monster?.Y ?? 0;

            RunEnemyTurn(monster, ai);

            RedrawArena();

            int walked = monster == null
                ? 0
                : GameData.Resources.Combat.CombatGrid.ChebyshevDistance(
                    fromX, fromY, monster.X, monster.Y);
            await Cysharp.Threading.Tasks.UniTask.Delay(
                System.TimeSpan.FromSeconds(
                    GameData.Resources.Combat.ArenaPacing.SecondsFor(walked)),
                DelayType.DeltaTime);
        }

        /// <summary>
        /// One AI turn, decided and then carried out — including the cast the runtime cannot
        /// deliver on its own.
        /// </summary>
        /// <remarks>
        /// <b>The spell has to be resolved HERE rather than in <c>CombatRuntime</c>, because the
        /// catalogue is here.</b> <c>ResolveEnemyTurn</c> takes a <c>Spell</c> record it has no way
        /// to look up, so its cast branch logs "decided but not carried out" — correct for it, and
        /// the reason an opportunistic pass would otherwise pick a spell that nothing ever cast.
        ///
        /// <para>An ordinary <see cref="AiAction.Cast"/> now carries an id too:
        /// <c>MonsterTurnResolver.PickCastSpell</c> is the port's
        /// <c>cspell_ai_pick_castable_spell</c> (TASK-379). This remark used to say the id was
        /// missing and to name TASK-241 for it, which was stale twice over — that task is closed,
        /// and it was never the owner of the picker.</para>
        /// </remarks>
        private void RunEnemyTurn(Combatant monster, BakAgain.Combat.MonsterTurnResolver ai) {
            int fromX = monster?.X ?? 0;
            int fromY = monster?.Y ?? 0;
            BakAgain.Combat.MonsterTurnResolver.Decision decision =
                Combat.ResolveEnemyTurn(monster, ai, _random);
            // One line per AI turn, so a fight can be replayed turn by turn against the original's
            // (TASK-528): who acted, from where to where, what it decided and on whom.
            GameData.Resources.Combat.CombatEncounter fight = Combat?.Encounter;
            if (monster != null && fight != null) {
                string Who(Combatant c) => c == null ? "-"
                    : c.IsPartyMember ? "P" + fight.Party.IndexOf(c) : "E" + fight.Enemies.IndexOf(c);
                _logger?.LogDebug($"AI turn: {Who(monster)} ({fromX},{fromY})->({monster.X},{monster.Y}) "
                    + $"{decision.Action} on {Who(decision.Target)} flags={monster.Flags}");
            }
            if (decision.SpellId == GameData.Resources.Combat.OpportunisticCasts.NoSpell
                || decision.Target == null || _spells?.Spells == null
                || !_spells.Spells.TryGetValue(decision.SpellId,
                    out GameData.Resources.Spells.Spell spell)) {
                return;
            }

            // The AI always casts as hard as it can — there is no power roll for it.
            int power = decision.CastPower ?? GameData.Resources.Combat.MonsterSpellcasting.AiCastPower(
                spell.MaximumCost, monster.Health + monster.Stamina);
            Combat.ResolveCast(monster, decision.Target, spell, decision.SpellId, power, _random);
        }

        /// <summary>
        /// Tear the fight down: write the party back, and put the world's music on again.
        /// </summary>
        /// <remarks>
        /// <b>Nothing used to call <c>Leave()</c> on a fight that ENDED</b> — only the retreat path
        /// did. So a won fight left the encounter standing and never wrote the party's health back
        /// through <c>WriteBack</c>, which is also where a downed member is restored to one health
        /// rather than left dead. Winning quietly discarded the whole fight's damage.
        ///
        /// <para>Guarded on <c>IsOver()</c> because <c>AdvanceToPartyTurn</c> also returns null when
        /// it cannot reach a party turn at all — a bug in whatever resolves enemy turns, and not a
        /// reason to end the encounter.</para>
        /// </remarks>
        /// <summary>
        /// Swaps the world for the arena (or back) underneath a fade, then reveals it.
        /// </summary>
        /// <remarks>
        /// <b>Fire-and-forget on purpose, and that is what keeps the transition in the right
        /// order.</b> A faithful fade is out, change, in — so the change has to wait for the
        /// darkness. The callers here (<see cref="StartCombat"/>, the flight branch, EndCombat) are
        /// synchronous and cannot wait, so making them await would push <c>UniTask</c> up through
        /// <c>HotspotDispatcher</c>'s switch and into the path every trigger placement routes
        /// through — a large edit to the game's content router for a cosmetic gain (TASK-305).
        ///
        /// <para>Forgetting it instead is correct here <b>because nothing between the caller
        /// returning and the fade covering changes what is on screen</b>: the world simply keeps
        /// rendering as it already was while the cover darkens, and every visible change — the
        /// camera, the actors, the arena, the HUD — happens inside <paramref name="afterSwap"/> or
        /// the swap itself, under an opaque screen. That is the ordering risk TASK-305 named, and
        /// it is answered by moving the follow-up work in here rather than leaving it at the call
        /// site.</para>
        ///
        /// <para><b>Anything visible the caller does AFTER calling this belongs in
        /// <paramref name="afterSwap"/>.</b> Left at the call site it would paint while the screen
        /// is still showing the old view — a combat HUD appearing over the world before the fade
        /// takes it, which reads as a glitch rather than a transition.</para>
        /// </remarks>
        private void SwapArenaThroughFade(bool entering, Action afterSwap = null,
            uint entryDialogId = 0) {
            SwapArenaThroughFadeAsync(entering, afterSwap, entryDialogId).Forget();
        }

        private async UniTaskVoid SwapArenaThroughFadeAsync(bool entering, Action afterSwap,
            uint entryDialogId = 0) {
            // *** THE ENTRY LINE COMES FIRST, AND OVER THE TRAVEL VIEW. ***
            // hotspotevt_type1_encounter_run plays the record at buf+6 and only THEN calls
            // combat_arena_actor_turn_loop (HOTSPOT.C:526-528), so it is read and dismissed while the
            // party is still standing in the world. Awaited rather than forgotten — firing it
            // alongside the fade would put the line over a half-built arena, which is a different
            // scene from the one the writer wrote.
            if (entryDialogId != 0 && _dialogs != null) {
                await _dialogs.ShowById((int)entryDialogId);
            }

            await _fade.FadeOutAsync();
            try {
                if (_showArena != null) {
                    await _showArena(entering);
                }

                afterSwap?.Invoke();
            } finally {
                // *** IN THE finally. *** A swap that throws must still uncover the screen; without
                // this the game would be left under an opaque black rectangle with no way back,
                // which is a far worse failure than the half-drawn arena the exception describes.
                await _fade.FadeInAsync();
            }
        }

        private void EndCombat() {
            if (Combat.Encounter == null) {
                return;
            }
            if (!Combat.Encounter.IsOver()) {
                _logger?.LogWarning("Combat HUD closed without the encounter being over; not ending it.");
                return;
            }

            int? outcomeLine = PrepareOutcomeLine(Combat.Encounter);
            SettleEncounter(Combat.Encounter);
            StageCombatantEdits();
            Combat.Leave();
            _setCombatMusic?.Invoke(false);
            if (outcomeLine.HasValue && _dialogs != null) {
                ShowOutcomeThenLeaveArenaAsync(outcomeLine.Value).Forget();
            } else {
                SwapArenaThroughFade(false);
            }
        }

        /// <summary>
        /// Picks the line the fight ends on and sets the actors it names — while the fight's
        /// combatants still exist.
        /// </summary>
        /// <remarks>
        /// <b>Every fight ended in silence.</b> The original closes each one with "The battle was
        /// won", "Search the body", "The enemy had fled" or a fallen member getting up
        /// (COMBAT.C:2587-2672, <see cref="GameData.Resources.Combat.CombatOutcomeDialog"/>), before
        /// the arena gives way to the world. Found by an auto-resolved fight that simply vanished.
        /// </remarks>
        private int? PrepareOutcomeLine(CombatEncounter fight) {
            bool trapPuzzle = false;
            for (var x = 0; x < GameData.Resources.Combat.CombatGrid.Width && !trapPuzzle; x++) {
                for (var y = 0; y < GameData.Resources.Combat.CombatGrid.Height; y++) {
                    if (Combat.Grid?.TerrainAt(x, y) == GameData.Resources.Combat.CombatTerrain.Exit) {
                        trapPuzzle = true;
                        break;
                    }
                }
            }
            int? line = GameData.Resources.Combat.CombatOutcomeDialog.For(
                fight, (int)_fightingEncounter, trapPuzzle);
            if (line == null || _session == null) {
                return line;
            }

            // nEvtArgActor0 is whoever acted last on the party's side (charSlot - 1); the fallen-
            // member line names the downed one instead, and the first still standing as actor 1.
            Combatant acting = fight.Current is { IsPartyMember: true } current
                ? current
                : GameData.Resources.Combat.CombatOutcomeDialog.StandingMember(fight);
            if (line == GameData.Resources.Combat.CombatOutcomeDialog.PartyMemberDown) {
                acting = GameData.Resources.Combat.CombatOutcomeDialog.DownedMember(fight);
                Combatant standing = GameData.Resources.Combat.CombatOutcomeDialog.StandingMember(fight);
                if (standing != null) {
                    _session.SetDialogSecondaryActorId(standing.ClassId);
                }
            }
            if (acting != null) {
                _session.EventActor = acting.ClassId;
            }
            return line;
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid ShowOutcomeThenLeaveArenaAsync(int line) {
            try {
                await _dialogs.ShowById(line);
            } finally {
                SwapArenaThroughFade(false);
            }
        }

        /// <summary>
        /// Records the outcome of a finished fight — the tail of <c>combTrigger_phase2</c>.
        /// </summary>
        /// <remarks>
        /// <b>*** THE FOUGHT FLAG WAS READ AND NEVER WRITTEN. ***</b>
        /// <see cref="EncounterFought"/> has gated every ambush on it since the activate pass was
        /// built, and nothing anywhere set it — so a defeated encounter stayed armed and fired again
        /// on the party's next step, for ever. Winning a fight changed nothing at all.
        ///
        /// <para><b>Only a WIN settles it.</b> The original branches on the arena's status: 1
        /// (resolved) fires the post event, sets the hotspot done and marks the encounter fought;
        /// 2 (the party left) relocates them instead and marks nothing. A wipe is neither. So this
        /// asks who is still standing rather than treating "the fight ended" as one case — which is
        /// exactly the mistake <see cref="EncounterAftermath"/>'s own summary warns about.</para>
        ///
        /// <para><b>*** THE POST EVENT WAS WRITTEN WHEN THE FIGHT STARTED. ***</b> It is
        /// <c>OnFire</c> — the field IS <c>wEvent_key_post</c>, at the same offset, with the same
        /// <c>write(key, 1)</c> behind it; the task note claiming our trigger does not carry it was
        /// wrong. What was wrong in the code is the TIMING: the original writes it only in the
        /// resolved branch, so a party that walked into an ambush and was wiped, or that ran, still
        /// set whatever story flag the encounter guards.</para>
        ///
        /// <para><b>The done flag goes with it</b>, for the same reason and in the same branch.</para>
        ///
        /// <para><b>A flight never reaches here</b>, which is why <c>partyFled</c> stays false: the
        /// retreat ends the fight on its own path and relocates the party there — see
        /// <see cref="RelocateAfterFlight"/>. The parameter stays because
        /// <see cref="GameData.Resources.World.EncounterAftermath.OutcomeFor"/> is the shared rule
        /// and it must keep answering the flight case for anything else that ends a fight.</para>
        ///
        /// <para><b>Still not done here:</b> the event-condition dispatch
        /// (<c>evtcond_dispatch_key_to_handler</c>).</para>
        /// </remarks>
        private void SettleEncounter(CombatEncounter fight) {
            long encounter = _fightingEncounter;
            TileEventTrigger trigger = _fightingTrigger;
            int hotspotIndex = _fightingHotspotIndex;
            ForgetFight();
            if (fight == null || encounter < 0) {
                return;
            }

            GameData.Resources.World.EncounterAftermath.Outcome outcome =
                GameData.Resources.World.EncounterAftermath.OutcomeFor(
                    fight.EnemiesAlive(), fight.PartyAlive());
            if (!GameData.Resources.World.EncounterAftermath.FiresThePostEvent(outcome)) {
                _logger?.LogDebug("Encounter {Encounter} ended as {Outcome}; nothing settled.",
                    encounter, outcome);
                return;
            }

            HotspotRules.ApplyOnFire(this, trigger);
            if (hotspotIndex >= 0) {
                WriteGlobal(DoneFlagKey(hotspotIndex), 1);
            }
            WriteGlobal(HotspotRules.EncounterFoughtKey(encounter), 1);
            MarkEncounterDefeated(encounter, trigger);
            _logger?.LogInformation("Encounter {Encounter} is fought; it will not fire again.",
                encounter);
            RunCompletionHook(encounter);
        }

        /// <summary>
        /// The other two thirds of defeating an encounter — <c>rgnenc_mark_defended</c>.
        /// </summary>
        /// <remarks>
        /// <b>THE FOUGHT FLAG IS ONLY ONE OF THREE THINGS, AND WE WERE DOING ONLY THAT ONE.</b>
        /// <see cref="GameData.Resources.World.EncounterDefeat"/> has modelled all three since it
        /// was written and had no caller — the codebase even cites its roster-kill when reasoning
        /// about the re-arm arm, three methods further down, while never invoking it.
        ///
        /// <list type="number">
        ///   <item>flag the encounter fought — done by the caller;</item>
        ///   <item><b>stop every roaming actor</b> in the record, so the beaten group stops
        ///     wandering the map;</item>
        ///   <item><b>kill every still-living actor on its roster</b>, whether or not the party ever
        ///     touched it.</item>
        /// </list>
        ///
        /// <para><b>The third is the one with a visible symptom.</b> A five-monster ambush where the
        /// party killed two and the other three never closed left those three alive — and since the
        /// roster is what a later visit re-seeds from, they went on standing about on the map after
        /// a fight the player had won. That became observable only once combat could actually be
        /// won; before the AI search-radius fix, nothing engaged and nothing was ever defeated.</para>
        ///
        /// <para><b>Already-dead actors are skipped rather than re-killed</b>, which matters because
        /// writing them back would rewrite a combatant the party may have looted.</para>
        /// </remarks>
        private void MarkEncounterDefeated(long encounter, TileEventTrigger trigger) {
            GameData.Resources.World.EncounterObjectStates states = _session?.EncounterActorStates;
            if (states == null) {
                return;
            }

            int recordIndex = RecordIndexOf(encounter, trigger);
            if (recordIndex < 0) {
                _logger?.LogDebug(
                    "Encounter {Encounter} has no record in ref pair {Ref}; nothing to defeat.",
                    encounter, _currentRefIndex);
                return;
            }

            System.Collections.Generic.IReadOnlyList<short> roster = EncounterRosterOf(encounter);
            var slots = new List<int>();
            if (roster != null) {
                foreach (short slot in roster) {
                    slots.Add(slot);
                }
            }

            var killed = new List<GameData.Resources.Data.DirtyRosterActorEdit>();
            GameData.Resources.World.EncounterDefeat.Result result =
                GameData.Resources.World.EncounterDefeat.ApplyToRecord(
                    states, _currentRefIndex, recordIndex, slots,
                    isAlive: slot => !RosterActorIsDead(slot),
                    kill: slot => {
                        ActorStat[] stats = _session?.RosterStatsOf(slot);
                        if (stats == null || (int)ActorAttribute.Health >= stats.Length
                            || stats[(int)ActorAttribute.Health] == null) {
                            return;
                        }
                        // Health.Base is what RosterActorIsDead reads and what a fight builds a
                        // combatant from, so zeroing it is the same statement both make.
                        stats[(int)ActorAttribute.Health].Base = 0;
                        killed.Add(new GameData.Resources.Data.DirtyRosterActorEdit(slot, stats));
                    });

            if (killed.Count > 0) {
                _session.StageRosterActorEdits(killed);
            }
            // The placement cache holds last visit's answer; a stopped or killed actor must not be
            // drawn from it. Same reason the step-size reset drops it.
            _placedChunk = null;
            _placedActors = null;
            if (_logger != null) {
                // Qualified for the same reason the rest of this file qualifies its logging: the
                // project's own ConditionalLoggingExtensions has the same shape as Microsoft's.
                LoggerExtensions.LogInformation(_logger,
                    "Encounter {Encounter} defeated: {Stopped} roamers stopped, {Killed} roster actors killed.",
                    encounter, result.ActorsStopped, result.ActorsKilled.Count);
            }
        }

        /// <summary>
        /// Resolves encounters a dialog has talked down — sub-action 4, id-0 case included.
        /// </summary>
        /// <returns>How many records were resolved.</returns>
        /// <remarks>
        /// <b>Zero means EVERY loaded record, not record zero</b> —
        /// <c>sub_ovr188_AAB</c> @0x75c1b skips a record only when
        /// <c>filter != 0 &amp;&amp; filter != enc_id</c>, so a zero filter disables the test
        /// entirely. Two of the five shipped instances pass 0.
        /// <see cref="GameData.Resources.World.EncounterDefeat.Matches"/> is that rule.
        ///
        /// <para><b>Resolving is THREE operations and the flag is only the first.</b> The original
        /// writes the fought flag, stops every roaming actor in the record, and kills every still-
        /// living actor on its roster. This runs the same
        /// <see cref="MarkEncounterDefeated"/> a won fight does rather than a dialog-shaped copy of
        /// it — a flag-only version leaves the monsters standing on the map for the party to walk
        /// into after the conversation that was supposed to have settled them.</para>
        ///
        /// <para>A null trigger is deliberate: <see cref="RecordIndexOf"/> scans every chunk when it
        /// has none, which is what a dialog needs — there is no trigger behind a conversation, and
        /// the record may not be in the chunk the party is standing in.</para>
        /// </remarks>
        public int ResolveEncounters(long filterEncounterId) {
            var resolved = new HashSet<long>();
            foreach (KeyValuePair<(int x, int y), List<TileEventTrigger>> pair in _byChunk) {
                var inOrder =
                    new List<(GameData.Resources.World.TileEventType, long?)>(pair.Value.Count);
                foreach (TileEventTrigger t in pair.Value) {
                    inOrder.Add((t.Type, EncounterNumberOf(t)));
                }

                foreach (long id in GameData.Resources.World.EncounterReset.RecordIds(inOrder)) {
                    if (!GameData.Resources.World.EncounterDefeat.Matches(filterEncounterId, id)
                        || !resolved.Add(id)) {
                        continue;
                    }

                    WriteGlobal(HotspotRules.EncounterFoughtKey(id), 1);
                    MarkEncounterDefeated(id, null);
                }
            }

            return resolved.Count;
        }

        /// <summary>Which record of its ref pair an encounter occupies, or -1.</summary>
        /// <remarks>
        /// <b>Built the same way the seed builds it</b> — <c>EncounterReset.RecordIds</c> over a
        /// chunk's triggers in order — because the record INDEX is a position in that list, not the
        /// encounter id. Deriving it any other way defeats a different record.
        ///
        /// <para><b>Located from the TRIGGER THAT WAS FOUGHT, not from where the party is standing.
        /// </b> A first cut used the party's chunk and answered -1: the fight's trigger need not
        /// belong to the chunk the party occupies when it ends, and settling reads a record that
        /// must be the one the fight came from. Scanning for the list holding this trigger is exact
        /// where a position lookup is a guess that usually agrees.</para>
        /// </remarks>
        private int RecordIndexOf(long encounter, TileEventTrigger trigger) {
            foreach (KeyValuePair<(int x, int y), List<TileEventTrigger>> pair in _byChunk) {
                if (trigger != null && !pair.Value.Contains(trigger)) {
                    continue;
                }
                var inOrder = new List<(GameData.Resources.World.TileEventType, long?)>(pair.Value.Count);
                foreach (TileEventTrigger t in pair.Value) {
                    inOrder.Add((t.Type, EncounterNumberOf(t)));
                }
                int index = GameData.Resources.World.EncounterReset.RecordIds(inOrder).IndexOf(encounter);
                if (index >= 0) {
                    return index;
                }
            }
            return -1;
        }

        /// <summary>
        /// The script hook a handful of encounters carry — <c>evtcond_dispatch_key_to_handler</c>.
        /// </summary>
        /// <remarks>
        /// <b>Called AFTER the fought flag is written, and that is load-bearing.</b> The group gates
        /// read the fought flags of every member including the one just beaten, so running this
        /// first makes the last kill of a group look like the second-to-last and the group flag is
        /// never earned by anything.
        ///
        /// <para><b>The re-arm arm is NOT wired, and cannot be usefully.</b> Eleven encounters put
        /// themselves back through the full reset, whose last step reloads the creature group —
        /// and defeating an encounter kills every actor on its roster
        /// (<see cref="GameData.Resources.World.EncounterDefeat"/>), so clearing the flags without
        /// that reload gives an encounter that arms again and then fields nobody. That reads as a
        /// broken record rather than a respawn, and it is strictly worse than leaving the eleven
        /// beaten. Logged instead, so the gap is visible where it happens.</para>
        /// </remarks>
        private void RunCompletionHook(long encounter) {
            uint dialog = GameData.Resources.World.EncounterCompletion.DialogAfterDefeat(encounter);
            if (dialog != 0) {
                PlayDialog(dialog,
                    GameData.Resources.World.EncounterCompletion.DialogWaitsForThePlayer);
            }

            int groupFlag = GameData.Resources.World.EncounterCompletion.GroupFlagEarnedBy(
                encounter, e => ReadGlobal(HotspotRules.EncounterFoughtKey(e)) != 0);
            if (groupFlag != 0) {
                WriteGlobal(groupFlag, 1);
                _logger?.LogInformation(
                    "Encounter {Encounter} completed its group; flag {Flag} is set.",
                    encounter, groupFlag);
            }

            if (GameData.Resources.World.EncounterCompletion.ReArmsWhenDefeated(encounter)) {
                RearmEncounter(encounter);
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <b>The sweep, not the pixel test.</b> The original has two implementations of this and
        /// only the above-ground one is a rule — see
        /// <see cref="GameData.Resources.Combat.CombatGroundCheck"/>. This is that one: walk the
        /// arena's footprint through the world from where the party stands and count the cells
        /// standing on ground a fight can happen on.
        ///
        /// <para><b>The footprint is laid out in front of the party and turned with them</b>, so
        /// each cell's offset is rotated by the party's heading before it is sampled.
        /// <c>ProximityMath.Rotate(across, away, heading)</c> is the same transform the party's own
        /// step uses — <c>Rotate(0, away, heading)</c> is exactly
        /// <c>MovementMath.StepDelta(heading, away)</c>, which is asserted rather than assumed.</para>
        ///
        /// <para><b>It counts every cell before answering.</b> Stopping at 24 would be faster and
        /// would report the same boolean, but the count is worth having in the log when an encounter
        /// refuses to fire and nobody can see why.</para>
        /// </remarks>
        public bool EnoughGroundToFight(TileEventTrigger trigger) {
            if (_session == null) {
                return true;
            }

            // *** A TRAP MOVES THE PARTY BEFORE THE CHECK, AND PUTS THEM BACK IF IT FAILS. *** That
            // is the one structural difference between springing a trap and walking into an
            // encounter: hotspotevt_trap_main_fire snaps the party onto the record's primary
            // landing FIRST and asks whether the arena fits from there.
            if (trigger != null && trigger.Type == TileEventType.Trap) {
                return SpringTrapOntoItsLanding(trigger);
            }

            // *** MEASURED, NOT ASSUMED: A GEOMETRIC SWEEP WOULD REFUSE ALMOST EVERY FIGHT DOWN
            // HERE. *** Sampled 265 floor spots across zone 10's loaded records, four headings each:
            // exactly ONE cleared the 24-cell bar. Mine corridors are about 800 units across and
            // this footprint is 2400 wide, so most of it is rock whichever way the party faces. The
            // kinds themselves are right underground — zone 10 paves with 14, Ground and Door, all
            // three open ground — so this is the geometry answering honestly, not a lookup failing.
            //
            // *** IT IS NOT THAT THE SWEEP CANNOT SEE THAT FAR. *** This comment used to say so and
            // pointed at TASK-284, which was then closed as NOT A BUG by the control that settles
            // it: a TryScanAnywhere walking every loaded record instead of the candidate list
            // returned IDENTICAL kinds along a zone-10 corridor out to 9600 units. Kind 14 is kept
            // at any range, exactly as terrain kinds 0-3 are, and the candidate list was never the
            // limiter. The all-104-empty reading that started that theory was a teleport to a spot
            // where a fight genuinely does not fit — the check working.
            //
            // Left spelled out because the disproved version is the more natural guess, and acting
            // on it means "fixing" a distance filter that is not there and then turning this
            // short-circuit off, which refuses every encounter in the mines.
            //
            // The original gets away with it because its underground check is not geometric at all
            // — arena_buildGridByRenderProbe @0x2e671 counts CELLS WHOSE PROJECTED POINT SHOWS FLOOR
            // COLOUR, and a distant cell projects near the horizon where floor pixels are, whether
            // or not that world point is rock. It is far more permissive than asking the geometry,
            // and that permissiveness is the behaviour, not a detail of it. Reproducing the OUTCOME
            // means not refusing underground fights; reproducing the METHOD needs a framebuffer.
            //
            // *** SUPERSEDED, 2026-09-24 (TASK-657). *** "Not refusing underground fights" was
            // measured wrong: from the same save the original refused Comb 321 four times out of
            // four, the room check returning AX=0 under a breakpoint, while this fired it six times.
            // The method is now reproduced against the geometry instead of a framebuffer —
            // WorldRuntime.CountUndergroundFloorCells — and the turn is undone on a refusal, as
            // hotspotevt_type1_encounter_run does (HOTSPOT.C:494-507).
            if (_underground) {
                short heading = _session.Rotation;
                TurnToFaceEncounter(trigger);
                if (HasRoomHere()) {
                    return true;
                }
                _session.Rotation = heading;
                _resyncCamera?.Invoke();
                return false;
            }

            return HasRoomHere();
        }

        /// <summary>
        /// Puts the party on a trap's primary landing, asks whether a fight fits from there, and
        /// puts them back when it does not — <c>hotspotevt_trap_main_fire</c> (HOTSPOT.C:750-762).
        /// </summary>
        /// <remarks>
        /// <b>The revert is not tidiness — a later step depends on it.</b> If the party then flees
        /// the fight, the exit landing is chosen by an outcode measured from where they are
        /// STANDING, and the original restores the original position before measuring it. A port
        /// that left them on the trap's landing would compute the approach direction from the
        /// trap's own spot rather than from where the party actually walked in, and pick the wrong
        /// exit.
        ///
        /// <para><b>The underground turn does not apply here.</b> The Comb arm turns the party to
        /// face the encounter before its check; the trap arm places them instead, and does it in
        /// both kinds of zone. Two different repositions, one per kind — not a shared step with a
        /// variant.</para>
        /// </remarks>
        private bool SpringTrapOntoItsLanding(TileEventTrigger trigger) {
            LandingPosition landing = TrapRecord(trigger)?.LandingPrimary;
            if (landing == null) {
                return HasRoomHere();
            }

            int wasX = _session.PositionX;
            int wasY = _session.PositionY;
            short wasRotation = _session.Rotation;

            int tileX = GameData.Resources.World.WorldPlacement.TileOf(wasX);
            int tileY = GameData.Resources.World.WorldPlacement.TileOf(wasY);
            _session.PositionX =
                (int)GameData.Resources.World.EncounterAftermath.WorldCoordinate(tileX, landing.FineX);
            _session.PositionY =
                (int)GameData.Resources.World.EncounterAftermath.WorldCoordinate(tileY, landing.FineY);
            _session.Rotation = unchecked((short)landing.RotationZ);

            if (HasRoomHere()) {
                // Kept for the flight path, which measures the exit from where the party WALKED IN
                // rather than from where the trap put them.
                _sprungFrom = (wasX, wasY, wasRotation);
                _resyncCamera?.Invoke();
                return true;
            }

            _session.PositionX = wasX;
            _session.PositionY = wasY;
            _session.Rotation = wasRotation;
            _sprungFrom = null;
            _logger?.LogDebug("Trap has no room to spring; the party is put back where they were.");
            return false;
        }

        /// <summary>The sweep itself, with no repositioning of its own.</summary>
        private bool HasRoomHere() {
            // Underground the original counts floor pixels and NEVER sweeps ground kinds
            // (combatgrid_tiles_over_thresh, CMBTGRID.C:379-387). A host without that count
            // abstains, as one without a world to sweep does: the kind sweep is the wrong method
            // down here and refuses nearly every mine fight (TASK-660).
            if (_underground) {
                if (_undergroundFloorCells == null) {
                    return true;
                }
                int floor = _undergroundFloorCells();
                bool fits = GameData.Resources.Combat.CombatGroundCheck.Passes(floor);
                if (!fits) {
                    _logger?.LogDebug($"No room to fight underground: {floor} cells show floor.");
                }
                return fits;
            }
            if (_groundKindAt == null || _start == null) {
                return true;
            }

            int cellSize = _start.CombatGridCellSize;
            if (cellSize <= 0) {
                return true;
            }

            var open = 0;
            for (var column = 0; column < GameData.Resources.Combat.CombatGrid.Width; column++) {
                for (var row = 0; row < GameData.Resources.Combat.CombatGrid.Height; row++) {
                    if (CellIsOpenGround(column, row, cellSize)) {
                        open++;
                    }
                }
            }

            bool enough = GameData.Resources.Combat.CombatGroundCheck.Passes(open);
            if (!enough) {
                _logger?.LogDebug(
                    $"No room to fight here: {open} open cells of "
                    + $"{GameData.Resources.Combat.CombatGroundCheck.SampledCells}, need "
                    + $"{GameData.Resources.Combat.CombatGroundCheck.MinimumOpenCells}.");
            }
            return enough;
        }

        /// <summary>
        /// Whether one arena cell sits on ground a fight can happen on.
        /// </summary>
        /// <remarks>
        /// <b>The sweep that decides whether a fight fits and the one that lays the grid out are the
        /// same sweep</b>, which is why this is its own method: <see cref="HasRoomHere"/> counts the
        /// cells this accepts, and <see cref="LayOutArena"/> writes them. They were one loop that
        /// threw the per-cell answer away and kept the count.
        /// </remarks>
        private bool CellIsOpenGround(int column, int row, int cellSize) {
            (int across, int away) =
                GameData.Resources.Combat.CombatGroundCheck.SampleOffset(column, row, cellSize);
            var (dx, dy) = Collision.ProximityMath.Rotate(across, away, _session.Rotation);
            return GameData.Resources.Combat.CombatGroundCheck.IsOpenGround(
                _groundKindAt(_session.PositionX + dx, _session.PositionY + dy));
        }

        /// <summary>
        /// Writes the world under the party into the arena's grid, then prunes what it isolates.
        /// </summary>
        /// <remarks>
        /// <b>Every arena cell used to be open regardless of what the party was standing in.</b>
        /// <c>CombatGrid</c>'s constructor blocks the two far corners and, underground, the back
        /// rows — and nothing else wrote to the grid, so a fight in a wood had no trees in it. The
        /// original builds the grid from the world at fight start (<c>arena_buildAndPruneGrid</c>
        /// @0x2e749), and the sweep that answers "does a fight fit here" was already computing
        /// exactly the per-cell answer it needs.
        ///
        /// <para><b>Skipped for a trap puzzle</b>, whose grid is authored by TRAPS.DAT and adopted
        /// whole — overwriting it with the world would erase the puzzle.</para>
        ///
        /// <para><b>THE TEST IS WHETHER THE PUZZLE AUTHORED ANYTHING, NOT WHETHER ONE EXISTS.</b>
        /// <see cref="PuzzleFor"/> returns a <c>TrapPuzzle</c> for EVERY encounter as long as
        /// TRAPS.DAT is loaded — an encounter with no entry gets an empty one rather than null. A
        /// <c>puzzle != null</c> guard therefore skips every fight in the game, which is what this
        /// method did until a play-verify in a live encounter found the grid untouched. An empty
        /// puzzle has no elements and no exit, so those two are the test.</para>
        ///
        /// <para><b>Skipped underground, and the reason is now known and filed.</b> The kinds are
        /// not the problem — zone 10 paves its corridors with 14, Ground and Door, all three in
        /// <see cref="GameData.Resources.Combat.CombatGroundCheck.OpenGroundKinds"/>, pinned by
        /// <c>UndergroundArenaGroundTests</c>. The reach is: <see cref="CellIsOpenGround"/> goes
        /// through <see cref="Collision.ProximityWorld.TryScan"/>, which consults only the CANDIDATE
        /// list built around the PARTY, and this footprint reaches 7100 units in front of them. See
        /// TASK-284.</para>
        /// </remarks>
        private void LayOutArena(GameData.Resources.Combat.TrapPuzzle puzzle) {
            bool authored = puzzle != null
                && (puzzle.Elements.Count > 0
                    || GameData.Resources.Combat.TrapPuzzleGoal.IsTrapPuzzle(puzzle.Grid));
            if (authored || _underground || _groundKindAt == null || _session == null
                || _start == null || Combat?.Grid == null) {
                return;
            }

            int cellSize = _start.CombatGridCellSize;
            if (cellSize <= 0) {
                return;
            }

            int open = GameData.Resources.Combat.ArenaLayout.Build(
                Combat.Grid,
                (column, row) => CellIsOpenGround(column, row, cellSize),
                Combat.CellReaches);
            _logger?.LogDebug($"Arena laid out from the world: {open} cells open.");

            // After the prune, as the original does (CMBTGRID.C:405 then :431), and only the
            // party's own tile. TASK-652.
            if (_sceneryOnTile != null) {
                var walled = GameData.Resources.Combat.ArenaScenery.Wall(Combat.Grid,
                    _sceneryOnTile(
                        GameData.Resources.World.WorldPlacement.TileOf(_session.PositionX),
                        GameData.Resources.World.WorldPlacement.TileOf(_session.PositionY)),
                    _session.PositionX, _session.PositionY, _session.Rotation, cellSize);
                _logger?.LogDebug($"Arena scenery walls: {string.Join(" ", walled)}");
            }
        }

        /// <summary>
        /// Turns the party to face the encounter's box, snapped to the nearest quarter turn —
        /// the underground half of <c>hotspotevt_type1_encounter_run</c>'s opening.
        /// </summary>
        /// <remarks>
        /// <b>Underground only.</b> Above ground the party fights facing wherever they were walking;
        /// below it they are turned toward the middle of the hotspot's box first, so the arena is
        /// laid out along the corridor rather than across it.
        ///
        /// <para><b>The original reverts this if the room check then fails</b>, and so does
        /// <see cref="EnoughGroundToFight"/> (TASK-657): a turn kept after a refused encounter would
        /// leave the party facing a wall.</para>
        /// </remarks>
        private void TurnToFaceEncounter(TileEventTrigger trigger) {
            if (trigger == null) {
                return;
            }

            int tileX = GameData.Resources.World.WorldPlacement.TileOf(_session.PositionX);
            int tileY = GameData.Resources.World.WorldPlacement.TileOf(_session.PositionY);
            _session.Rotation = unchecked((short)GameData.Resources.Combat.ArenaFacing.FacingFor(
                trigger, tileX, tileY, _session.PositionX, _session.PositionY));
            _resyncCamera?.Invoke();
        }

        /// <summary>
        /// Where a party that ran away comes out — <c>combTrigger_phase2</c>'s
        /// <c>combatStatus == 2</c> arm.
        /// </summary>
        /// <remarks>
        /// <b>Running away is a MOVE, not just an early exit.</b> Left where they stood, the party
        /// is still on the encounter's tile with the hotspot still armed, so the next step walks
        /// straight back into the fight they just escaped — which is why the original relocates them
        /// and why "a flight marks nothing" is only half the sentence.
        ///
        /// <para><b>The landing is chosen by where they finished, not by how they arrived</b> —
        /// <see cref="GameData.Resources.World.EncounterAftermath.ApproachDirection"/> against the
        /// hotspot's box, then one of the record's four entries. The stored values are offsets
        /// INSIDE a tile and the tile is the party's own, so they have to be rebased rather than
        /// used as positions.</para>
        ///
        /// <para><b>The heading is part of the landing</b>, and it is not a courtesy: the record
        /// turns the party to face away from what they fled, so dropping it leaves them looking
        /// back into the encounter.</para>
        /// </remarks>
        private void RelocateAfterFlight(TileEventTrigger trigger) {
            if (trigger == null || _session == null) {
                return;
            }

            // *** MEASURED FROM WHERE THEY WALKED IN, NOT FROM WHERE THE TRAP PUT THEM. *** The
            // original restores the pre-spring position before running the outcode, so a trap that
            // is fled sends the party out on the side they arrived from. Without this the direction
            // is read from the trap's own landing and the exit is wrong for every sprung trap.
            if (_sprungFrom.HasValue) {
                (int x, int y, short rotation) = _sprungFrom.Value;
                _session.PositionX = x;
                _session.PositionY = y;
                _session.Rotation = rotation;
            }

            int tileX = GameData.Resources.World.WorldPlacement.TileOf(_session.PositionX);
            int tileY = GameData.Resources.World.WorldPlacement.TileOf(_session.PositionY);
            int direction = GameData.Resources.World.EncounterAftermath.ApproachDirection(
                trigger, tileX, tileY, _session.PositionX, _session.PositionY);
            GameData.Resources.World.EncounterAftermath.Landing which =
                GameData.Resources.World.EncounterAftermath.LandingFor(direction);

            // Both record kinds carry the same four landings; which table they come out of is the
            // trigger's kind, not something the direction knows.
            LandingPosition landing = trigger.Type == TileEventType.Trap
                ? TrapRecord(trigger)?.LandingFor(which)
                : CombRecord(trigger)?.LandingFor(which);
            if (landing == null) {
                return;
            }

            _session.PositionX =
                (int)GameData.Resources.World.EncounterAftermath.WorldCoordinate(tileX, landing.FineX);
            _session.PositionY =
                (int)GameData.Resources.World.EncounterAftermath.WorldCoordinate(tileY, landing.FineY);
            _session.Rotation = unchecked((short)landing.RotationZ);
            _resyncCamera?.Invoke();
            _logger?.LogDebug(
                $"Fled encounter {_fightingEncounter}; landing {direction} puts the party at "
                + $"{_session.PositionX},{_session.PositionY}.");
        }

        /// <summary>Drop everything the settle needs, so it cannot be applied to the next fight.</summary>
        /// <summary>
        /// Puts a defeated encounter's monsters back on their feet, for the eleven that re-arm.
        /// </summary>
        /// <remarks>
        /// <b>Healing and clearing the flags are one operation, not two.</b> Heal without clearing
        /// and the monsters are whole but unreachable; clear without healing and the encounter arms
        /// again and fields the wounded and the dead — which is what it would have done here until
        /// TASK-226 and TASK-230 made damage survive a fight at all. Before those, this method could
        /// not have been written honestly: there was nothing persisted to undo.
        ///
        /// <para>The heal is <c>Health.Current = Health.Maximum</c> and the combat status goes to 1
        /// — see <see cref="GameData.Resources.World.EncounterRearm"/>, which is the whole of what
        /// the original writes. Staged through the same two channels a fight's own state uses, so it
        /// reaches the save the same way and by the same offsets.</para>
        ///
        /// <para><b>It cannot bring back an actor that was REMOVED.</b> The original re-reads the
        /// records already in TEMP.GAM rather than the shipped template, so a creature whose death
        /// persisted as gone stays gone. That is the original's behaviour, not a gap.</para>
        ///
        /// <para><b>Public because a DIALOG can ask for it too.</b> Sub-action 3 is the same
        /// routine (<c>sub_ovr188_BCD</c> @0x75d3d calls
        /// <c>combatenc_rearm_roster_actors</c> after the same flag clears), so
        /// <c>DialogExecutor</c> reaches it through a callback rather than a second implementation.
        /// The callback exists because a constructor dependency would cycle — this service takes
        /// <c>IDialogManager</c>, which owns the executor.</para>
        /// </remarks>
        public void RearmEncounter(long encounter) {
            IReadOnlyList<short> roster = _session?.RosterOf((int)encounter);
            if (roster == null) {
                return;
            }

            var actorEdits = new List<GameData.Resources.Data.DirtyRosterActorEdit>();
            var combatEdits = new List<GameData.Resources.Data.DirtyCombatantEdit>();
            foreach (short slot in roster) {
                if (slot < 0) {
                    continue;
                }
                ActorStat[] stats = _session.RosterStatsOf(slot);
                if (GameData.Resources.World.EncounterRearm.HealToFull(stats)) {
                    actorEdits.Add(new GameData.Resources.Data.DirtyRosterActorEdit(slot, stats));
                }
                GameData.Resources.Data.SaveGameCombatData record = _session.CombatRecordOf(slot);
                if (record != null) {
                    combatEdits.Add(new GameData.Resources.Data.DirtyCombatantEdit(slot,
                        GameData.Resources.World.EncounterRearm.WithStatusReset(record)));
                }
            }
            _session.StageRosterActorEdits(actorEdits);
            _session.StageCombatantEdits(combatEdits);

            int cleared = ClearEncounterFlags(encounter);
            _logger?.LogInformation(
                $"Encounter {encounter} re-armed: {actorEdits.Count} actor(s) healed, "
                + $"{cleared} hotspot flag(s) cleared.");
        }

        /// <summary>
        /// Clears every "you have already done this" flag for a re-arming encounter.
        /// </summary>
        /// <returns>How many hotspot flags were cleared; 0 when the hotspot could not be located.</returns>
        /// <remarks>
        /// <b>The hotspot is found POSITIONALLY, never by encounter id.</b> The record list is built
        /// by walking the chunk's triggers in order and appending each encounter-carrying one, so
        /// record N is the Nth such trigger — and two records can share an id.
        /// <see cref="GameData.Resources.World.EncounterReset.TriggerIndexForRecord"/> re-walks the
        /// same list; looking the trigger up by id would clear the wrong hotspot, or none.
        ///
        /// <para>The encounter's own fought flag is cleared regardless, because it is keyed by the
        /// encounter and not by a hotspot — so a chunk we cannot resolve still stops reporting the
        /// fight as done.</para>
        /// </remarks>
        private int ClearEncounterFlags(long encounter) {
            WriteGlobal(HotspotRules.EncounterFoughtKey(encounter), 0);

            if (!_lastChunk.HasValue
                || !_byChunk.TryGetValue(_lastChunk.Value, out List<TileEventTrigger> triggers)) {
                return 0;
            }

            var inOrder = new List<(GameData.Resources.World.TileEventType, long?)>(triggers.Count);
            var types = new List<GameData.Resources.World.TileEventType>(triggers.Count);
            foreach (TileEventTrigger trigger in triggers) {
                inOrder.Add((trigger.Type, EncounterNumberOf(trigger)));
                types.Add(trigger.Type);
            }

            int record = GameData.Resources.World.EncounterReset.RecordIndexOf(inOrder, encounter);
            int hotspot = GameData.Resources.World.EncounterReset.TriggerIndexForRecord(types, record);
            if (hotspot < 0) {
                return 0;
            }

            var n = 0;
            foreach (GameData.Resources.World.EncounterReset.ClearedFlag flag
                     in GameData.Resources.World.EncounterReset.ClearedHotspotFlags) {
                WriteGlobal(KeyForClearedFlag(flag, hotspot), 0);
                n++;
            }
            return n;
        }

        private int KeyForClearedFlag(
            GameData.Resources.World.EncounterReset.ClearedFlag flag, int hotspotIndex) =>
            flag switch {
                GameData.Resources.World.EncounterReset.ClearedFlag.Done => DoneFlagKey(hotspotIndex),
                GameData.Resources.World.EncounterReset.ClearedFlag.ScoutTried =>
                    ScoutTriedFlagKey(hotspotIndex),
                _ => ScoutedFlagKey(hotspotIndex),
            };

        /// <summary>
        /// Loots the body the player clicked — <c>wcursor_loot_corpse</c>.
        /// </summary>
        /// <param name="rosterSlot">
        /// The dead enemy's index in the encounter's roster, from <c>ArenaCorpse</c>.
        /// </param>
        /// <param name="distance">
        /// Camera-to-body distance in world units, for the reach test.
        /// </param>
        /// <param name="isPrimary">
        /// Which button clicked. The original reads the pressed button (<c>g_wMenuDragState</c>)
        /// and refuses anything but the primary with dialog 0x5f.
        /// </param>
        /// <remarks>
        /// <b>The container is keyed (zone 100, x = roster slot, y = ENCOUNTER NUMBER.)</b> Not the
        /// record index <see cref="GameData.Resources.Combat.EncounterCorpseLoot.RecordIndexOf"/>
        /// decodes — that splits a hotspot slot and does not apply here. Measured live: encounter 5
        /// yields containers at x = 0 and x = 1 for its two enemies, holding three and five items.
        /// The records ship with their loot; nothing generates it.
        ///
        /// <para><b>Out of reach is SILENT.</b> The original returns before the click sound and
        /// before any dialog, so "nothing happened" is the correct feedback for a distant body and a
        /// port that explains itself is adding a message the game does not have.</para>
        ///
        /// <para>The click cue sounds BEFORE the validity checks — it acknowledges the click, not
        /// the outcome — so a body that turns out to hold nothing still clicks.</para>
        ///
        /// <para><b>A loot SPEAKS before the pack opens</b> (WCURSOR.C <c>wcursor_loot_corpse</c>): the
        /// body's own <c>SUBREC_INTERACT_MSG</c> line, else 0x4e. The corpse container's Dialog
        /// subrecord is that same 6-byte record ({kind, flags, message id}), so its
        /// <c>DialogId</c> is the id the original plays.</para>
        /// </remarks>
        /// <summary>
        /// The party has walked off chunk (<paramref name="chunkX"/>, <paramref name="chunkY"/>):
        /// <c>rgnenc_zone_rectr_save_objects</c> over its ref pair (see
        /// <c>EncounterObjectStates.LeaveChunk</c>), as <c>czone_resync_on_world_move</c> runs it
        /// before loading the next chunk (CZONE.C:79-99). The next chunk's actors are placed by
        /// WorldRuntime's redraw on the same step.
        /// </summary>
        private void LeaveEncounterChunk(int chunkX, int chunkY) {
            GameData.Resources.World.EncounterObjectStates states = _session?.EncounterActorStates;
            if (states == null) {
                return;
            }
            states.LeaveChunk(RefIndexAt(chunkX * ChunkSize, chunkY * ChunkSize));
            _placedChunk = null;
            _placedActors = null;
        }

        /// <summary>Forgets the chunk's placement so the next draw re-reads the state block.</summary>
        internal void InvalidatePlacement() {
            _placedChunk = null;
            _placedActors = null;
        }

        internal void LootCorpse(int rosterSlot, long encounterNumber, long distance, bool isPrimary) {
            // The original's only route here is the world loop's viewport click (WORLDLP.C:374), so a
            // body is looted after its fight and never during one (TASK-534).
            if (rosterSlot < 0 || encounterNumber < 0 || Combat?.Encounter != null || _session == null) {
                return;
            }
            if (!GameData.Resources.Combat.EncounterCorpseLoot.WithinReach(distance, _underground)) {
                return;   // silent, deliberately
            }
            _playSfx?.Invoke(GameData.Resources.Combat.EncounterCorpseLoot.ClickSoundId);
            if (!isPrimary) {
                _dialogs?.ShowById(GameData.Resources.Combat.EncounterCorpseLoot.RefusedDialog).Forget();
                return;
            }

            GameData.Resources.Inventory.RuntimeContainer body =
                _session.GetLiveContainerAt(CorpseContainerZone, rosterSlot, (int)encounterNumber);
            if (body == null) {
                _dialogs?.ShowById(
                    GameData.Resources.Combat.EncounterCorpseLoot.NothingToLootDialog).Forget();
                return;
            }
            SayThenOpenCorpseLoot(body).Forget();
        }

        private async UniTaskVoid SayThenOpenCorpseLoot(GameData.Resources.Inventory.RuntimeContainer body) {
            if (_dialogs != null) {
                await _dialogs.ShowById(
                    GameData.Resources.Combat.EncounterCorpseLoot.LootDialogFor((int)(body.DialogId ?? 0)));
            }
            _openCorpseLoot?.Invoke(body);
        }

        /// <summary>The container "zone" corpse records live in — <c>actorspawn_objfixed(100, ...)</c>.</summary>
        private const int CorpseContainerZone = 100;

        /// <summary>
        /// Hands the fight's damage to the session before the encounter is dropped.
        /// </summary>
        /// <remarks>
        /// <b>Must run before <c>Combat.Leave()</c>, on every path that ends a fight.</b>
        /// <see cref="CombatRuntime.CollectDirtyCombatantEdits"/> reads the live encounter and the
        /// enemy-to-slot map; <c>Leave</c> clears both, so a caller that leaves first gets an empty
        /// list and the wound is gone. That is the bug this fixes — wound an enemy, run, come back,
        /// and it was at full health.
        ///
        /// <para><b>Both exits, not just the win.</b> Fleeing is exactly the case a player notices,
        /// because the enemy is still alive to meet again.</para>
        ///
        /// <para>The party is deliberately not here: the original writes only the enemy side of the
        /// combat block, the party's state going back through its character records.</para>
        /// </remarks>
        private void StageCombatantEdits() {
            if (_session == null) {
                return;
            }
            _session.StageCombatantEdits(Combat.CollectDirtyCombatantEdits(_session.CombatRecordOf));
            // The 95-byte half. Both or neither: an enemy whose grid position survived but whose
            // wounds did not is a worse answer than losing both, because it looks like it worked.
            _session.StageRosterActorEdits(Combat.CollectDirtyRosterActorEdits());
            // And where the living ended up (TASK-239). The deaths already persist via
            // CombatRuntime.PersistRemoval; without this half, fleeing a fight and returning finds
            // the survivors back at their authored posts instead of where you left them.
            Combat.PersistSurvivors(_start?.CombatGridCellSize ?? 0,
                _zoneIsUnderground != null && _zoneIsUnderground());
        }

        /// <summary>
        /// Rebuilds the arena's sprites from the fight's current state.
        /// </summary>
        /// <remarks>
        /// <b>A whole rebuild each turn, deliberately.</b> The alternative is tracking which sprite
        /// belongs to which combatant and mutating them, which is a second model of the board to
        /// keep in step with the first. A fight is a handful of actors and the textures are cached,
        /// so the cheap thing is also the simple one.
        /// </remarks>
        private void RedrawArena() {
            if (Combat?.Encounter != null) {
                _redrawArena?.Invoke();
            }
        }

        private void ForgetFight() {
            // *** EVERY FIGHT THAT ENDS IS STAMPED, WON OR FLED. *** HOTSPOT.C:531 writes the
            // encounter's fought time before it looks at the outcome, and the next visit heals the
            // survivors by the hours since (TASK-517). Both callers are ends of a fight.
            if (_fightingEncounter >= 0 && _session != null) {
                _session.EncounterFoughtTimes.Stamp(_fightingEncounter, (uint)_session.GameTimeIn2Seconds);
            }
            _fightingEncounter = -1;
            _fightingTrigger = null;
            _fightingHotspotIndex = -1;
            _sprungFrom = null;
        }

        /// <summary>Rebuild the HUD for whoever acts next, or close it when the fight is over.</summary>
        /// <remarks>
        /// <b>Also the one place a turn is known to have happened</b>, which is why the arena is
        /// redrawn from here. The sprites were built once when the fight opened and never again, so
        /// a combatant that moved stayed where it started and one that died stayed standing — see
        /// <see cref="RedrawArena"/>.
        /// </remarks>
        private void RefreshCombatHud() {
            // The party's OWN action, drawn before the enemies get their turns -- this is the
            // rebuild WorldRuntime's move slide rides on, so it has to stay first and stay alone.
            RedrawArena();
            SettleTurnAsync(openMenu: false).Forget();
        }

        /// <summary>
        /// Hand the turn on: let every enemy in between act, drawn and paced, then hand back.
        /// </summary>
        /// <remarks>
        /// <b>Asynchronous because watching a monster act takes time</b> — see
        /// <see cref="RunEnemyTurnPacedAsync"/>. Fire-and-forget is safe here for a reason that was
        /// checked rather than assumed: every one of <see cref="RefreshCombatHud"/>'s call sites is
        /// that call followed immediately by <c>}</c> or <c>return;</c>, so nothing reads fight
        /// state expecting the advance to have finished.
        ///
        /// <para><b>The guard is not decoration.</b> While the enemies are acting the current
        /// combatant is not a party member, so the click arms all refuse — but a second settle
        /// starting on top of a running one would advance the turn order twice, which is a
        /// corrupted fight rather than a cosmetic glitch.</para>
        ///
        /// <para>The closing redraw draws the last monster's final pose; the per-turn ones inside
        /// the advance draw each move as it happens.</para>
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTaskVoid SettleTurnAsync(bool openMenu) {
            if (_settlingTurn) {
                return;
            }
            _settlingTurn = true;
            try {
                // The party's own walk is drawn first, as the original's walk returns before the
                // enemies act (COMBAT.C:1588): a monster's redraw mid-slide rebuilt the arena and
                // both sprites jumped to their ends.
                await Cysharp.Threading.Tasks.UniTask.WaitWhile(() => _arenaBusy?.Invoke() ?? false);
                Combatant next = await AdvanceToPartyTurnPacedAsync();

                BakAgain.UI.Combat.CombatMenu menu = _combatMenuAccessor?.Invoke();
                if (menu == null) {
                    return;
                }
                if (next == null) {
                    EndCombat();
                    menu.Close();
                    // The picker is an overlay on the same anchors, so a fight that ends while it is
                    // up would leave quarrel buttons floating over the travel HUD.
                    _shootMenuAccessor?.Invoke()?.Close();
                    return;
                }

                RedrawArena();
                (bool canShoot, bool canCast) = Combat.CapabilitiesFor(next);
                menu.SetCapabilities(canShoot, canCast);
                if (openMenu) {
                    menu.Open();
                }
            } finally {
                _settlingTurn = false;
            }
        }

        /// <summary>True while enemy turns are being played out; see <see cref="SettleTurnAsync"/>.</summary>
        private bool _settlingTurn;

        /// <summary>
        /// Spend the party's scouting roll on this ambush. True = spotted, which stops the party
        /// short of the tile.
        /// </summary>
        /// <remarks>
        /// <c>RND(100) &lt;= stat_party_find_extreme(Scouting)</c> — the party's <i>best</i> scout
        /// attempts it. On success the party gains Scouting experience and the record's pre-fire
        /// dialog plays, falling back to DDX 0x2f ("you spot…") when the record carries none.
        /// </remarks>
        public bool RollScouting(TileEventTrigger trigger) {
            if (_session == null) {
                return false;
            }

            int best = _session.PartyExtreme(ActorAttribute.Scouting, out int member);
            if (!HotspotRules.ScoutingSpots(_random(100), best)) {
                return false;
            }

            // *** THE WHOLE PARTY LEARNS FROM IT, NOT THE SCOUT. *** HOTSPOT.C:428 is
            // `stat_party_broadcast_status_op(0xe, 1, 3)` — the broadcast walks activeParty. This
            // awarded only the member PartyExtreme named until 2026-09-13, which trained the party
            // about a third as fast (TASK-474). `member` still names who rolled, for EventActor.
            _session.EventActor = member;
            _session.BroadcastSkillUse(ActorAttribute.Scouting, 1);

            uint dialogId = PreFireDialogId(trigger);
            PlayDialog(dialogId != 0 ? dialogId : SpottedAmbushDialogId, true);
            return true;
        }

        /// <summary>
        /// Spend the party's Stealth on slipping past an encounter it is standing in.
        /// </summary>
        /// <remarks>
        /// <b>This is not RollScouting with a different stat.</b> Scouting (0xe) is rolled raw in the
        /// activate pass to decide whether the party stops short; Stealth (0xf) is rolled here,
        /// against a chance that is boosted thirty percent and then held at ninety, with a further
        /// half-the-remaining-distance bonus only when the record is avoidable AND Dragon's Breath is
        /// up. Reusing either roll for the other makes ambushes far easier or far harder than the
        /// game grants.
        ///
        /// <para>Success trains Stealth, the same skill-use award a successful spot gives Scouting.</para>
        /// </remarks>
        /// <summary>
        /// The SECOND stealth roll: who gets the drop — <c>combTrigger_phase2</c> (ovr187 @0x7409d),
        /// the block after the avoidance roll and before the arena.
        /// </summary>
        /// <returns>Whether the party surprised them.</returns>
        /// <remarks>
        /// <b>TWO ROLLS, ON DIFFERENT TERMS, AND ONLY ONE OF THEM WAS BEING MADE.</b>
        /// <see cref="RollEncounterAvoidance"/> decides whether the party walks past; this decides
        /// who opens. It uses the RAW party Stealth — no thirty-percent bonus, no ceiling, no
        /// Dragon's Breath — so reusing the avoidance chance here makes surprises far commoner than
        /// the game grants. <b>"Party Stealth" is the party's WORST</b>, not its best: see
        /// <see cref="GameSession.PartyExtreme"/>, whose minimising arm this and
        /// <see cref="RollEncounterAvoidance"/> are the only two callers of.
        ///
        /// <para><b>What it buys is one forfeited turn per enemy</b>, not an extra action for the
        /// party — see <see cref="CombatEncounterOpening.EnemiesForfeitTheirOpeningTurn"/>, read
        /// from <c>runCombatEncounter</c>'s pre-round loop.</para>
        ///
        /// <para><b>An encounter the party has never looked at reads a visit time of ZERO</b>, which
        /// is not "long ago" at the start of the game: the elapsed time is then the game clock
        /// itself, so during the first half hour every encounter passes the recency test. That is
        /// the original's behaviour and not a rounding artefact.</para>
        ///
        /// <para><b>Our visit stamp is not the original's.</b> The original reads a per-encounter
        /// timestamp out of TEMP.GAM at <c>encounterNumber * 4 + 0x4457</c>, written when the player
        /// CLICKS the encounter in the world. We have no such click yet, so every encounter reads
        /// zero — which lands on the "unvisited" case above, correct for the first half hour of a
        /// new game and too generous afterwards. Stated rather than hidden: the roll is right and
        /// its input is provisional.</para>
        /// </remarks>
        private bool RollEncounterOpening(long encounter) {
            if (_session == null) {
                return false;
            }

            // No per-encounter visit stamp yet — see the remarks.
            const long NeverVisited = 0;
            if (!GameData.Resources.World.CombatEncounterOpening.WasRecentlyVisited(
                    _session.GameTimeIn2Seconds, NeverVisited)) {
                return false;
            }

            int best = _session.PartyExtreme(ActorAttribute.Stealth, out int member);
            GameData.Resources.World.CombatEncounterOpening.Opening opening =
                GameData.Resources.World.CombatEncounterOpening.Resolve(
                    recentlyVisited: true, _random(100), best);

            if (!GameData.Resources.World.CombatEncounterOpening.PartyHasTheDrop(opening)) {
                return false;
            }

            // The same party-wide award the avoidance roll gives, for the same stat —
            // HOTSPOT.C:516's `stat_party_broadcast_status_op(0xf, 1, 3)`.
            _session.EventActor = member;
            _session.BroadcastSkillUse(ActorAttribute.Stealth,
                GameData.Resources.World.CombatEncounterOpening.TrainingOnSurprise);

            _logger?.LogDebug($"Encounter {encounter}: the party has the drop; "
                + "every enemy forfeits its opening turn.");
            return true;
        }

        /// <summary>
        /// Whether an encounter is evaluated on this step — the movement-cell gate.
        /// </summary>
        /// <remarks>
        /// <b>The roll is once per CELL, not once per step.</b> <c>hotspotevt_activate_at_player</c>
        /// reaches its Stealth arm and immediately <c>return</c>s when <c>worldmove_step_tick_get()</c>
        /// is 0 (HOTSPOT.C:474), and that flag is raised only on the press that completes a
        /// 1600-unit movement cell — one press in four at the Small step preset. Without the gate an
        /// avoidable encounter gets four chances where the game grants one, and the ratio moves with
        /// the player's step-size setting.
        ///
        /// <para><b>Only encounters that COULD be avoided are gated</b>, because the gate sits inside
        /// that arm: <see cref="CombatEncounterAvoidance.MayAttempt"/> answering false means the
        /// original jumped straight past the tick test to the encounter itself.</para>
        /// </remarks>
        public bool EncounterIsDueThisStep(TileEventTrigger trigger, int index) {
            bool scouted = ReadGlobal(ScoutedFlagKey(index)) != 0;
            if (!CombatEncounterAvoidance.MayAttempt(
                    IsAmbush(trigger), scouted, DragonsBreathActive, AvoidanceIsWhitelisted(trigger))) {
                return true;
            }
            return GameData.Resources.World.WorldStepTick.IsCellBoundary(
                _session?.TileBoundaryCrossed ?? GameData.Resources.World.WorldStepTick.OnBoundary);
        }

        public bool RollEncounterAvoidance(TileEventTrigger trigger, bool scouted) {
            if (_session == null) {
                return false;
            }

            bool avoidable = IsAmbush(trigger);
            if (!CombatEncounterAvoidance.MayAttempt(avoidable, scouted, DragonsBreathActive,
                    AvoidanceIsWhitelisted(trigger))) {
                return false;
            }

            int best = _session.PartyExtreme(CombatEncounterAvoidance.Stat, out int member);
            int chance = CombatEncounterAvoidance.Chance(best, avoidable, DragonsBreathActive);
            if (!CombatEncounterAvoidance.Evades(_random(100), chance)) {
                return false;
            }

            // *** PARTY-WIDE, AND THE ROLLER IS REMEMBERED. *** HOTSPOT.C:489 broadcasts
            // `stat_party_broadcast_status_op(0xf, 1, 3)` and the line above it is
            // `g_gameState.nEvtArgActor0 = g_gameState.nEvtArgStat` — a dialog sub-action that says
            // "the actor" means whoever the roll named, which is the party's QUIETEST member.
            _session.EventActor = member;
            _session.BroadcastSkillUse(ActorAttribute.Stealth,
                CombatEncounterAvoidance.TrainingOnSuccess);

            return true;
        }

        /// <summary>
        /// <c>spellfx_event_mask_test_bit(0)</c> — the spell that helps the party go unnoticed.
        /// </summary>
        private bool DragonsBreathActive => ((_session?.PaletteEventMask ?? 0) & 1) != 0;

        /// <summary>
        /// <c>hotspotevt_chance_trigger</c> and its clearing twin: roll the record's chance and, if
        /// it comes up, write the flag it names.
        /// </summary>
        /// <remarks>
        /// <b>Enab sets the flag, Disa clears it</b> — the only difference between the two handlers.
        /// A record naming flag 0 rolls but writes nothing, which is how the shipping data expresses
        /// "no effect" without a separate kind.
        /// </remarks>
        public bool ApplyChanceFlagWrite(TileEventTrigger trigger) {
            int chance;
            int flag;
            int value;

            switch (trigger.Type) {
                case TileEventType.Disa: {
                    DefDisaEntry record = DisaRecord(trigger);

                    if (record == null) {
                        return false;
                    }
                    chance = record.Chance;
                    flag = record.GlobalKey;
                    value = 0;

                    break;
                }
                case TileEventType.Enab: {
                    DefEnabEntry record = EnabRecord(trigger);

                    if (record == null) {
                        return false;
                    }
                    chance = record.Chance;
                    flag = record.GlobalKey;
                    value = 1;

                    break;
                }
                default:
                    return false;
            }

            if (!HotspotRules.ChanceFires(_random(100), chance)) {
                return false;
            }
            if (flag != 0) {
                WriteGlobal(flag, value);
            }
            return true;
        }

        private DefDisaEntry DisaRecord(TileEventTrigger trigger) =>
            _disa != null && trigger.EntryNumber < (uint)_disa.Records.Count
                ? _disa.Records[(int)trigger.EntryNumber].Payload
                : null;

        private DefEnabEntry EnabRecord(TileEventTrigger trigger) =>
            _enab != null && trigger.EntryNumber < (uint)_enab.Records.Count
                ? _enab.Records[(int)trigger.EntryNumber].Payload
                : null;

        /// <summary><c>def_dial[trigger].DialogId</c> — what a Dial hotspot says.</summary>
        public uint SpeakDialogId(TileEventTrigger trigger) => DialRecord(trigger)?.DialogId ?? 0;

        /// <summary>
        /// <c>hotspotevt_scout_tried_set</c> — silence this hotspot for as long as the party stays
        /// on this chunk.
        /// </summary>
        public void MarkActedThisChunk(int hotspotIndex) =>
            WriteGlobal(ScoutTriedFlagKey(hotspotIndex), 1);

        private DefDialEntry DialRecord(TileEventTrigger trigger) =>
            _dial != null && trigger.EntryNumber < (uint)_dial.Records.Count
                ? _dial.Records[(int)trigger.EntryNumber].Payload
                : null;

        /// <summary>Slots in the transient per-chunk hotspot block (<c>HOTSPOT_SCOUT_TRIED</c>).</summary>
        private const int TransientSlots =
            GameData.Resources.World.WorldTileCache.SlotsPerTransientBlock;

        /// <summary>
        /// <c>czone_spell_state_reset_pair</c> — wipe the transient per-hotspot block.
        /// </summary>
        /// <remarks>
        /// <b>Without this the world goes quiet permanently.</b> The block is what stops a hotspot
        /// acting twice while the party stands on it, and <see cref="HotspotRules.Available"/> reads
        /// it for <i>every</i> kind — so one scouting roll or one dialog would otherwise disable
        /// every hotspot sharing that slot for the rest of the session. The original clears it
        /// whenever the party crosses onto another chunk or into another zone, which is what re-arms
        /// the hotspots there.
        /// </remarks>
        private void ClearTransientHotspotFlags() {
            for (var slot = 0; slot < TransientSlots; slot++) {
                WriteGlobal(ScoutTriedFlagKey(slot), 0);
                // The original clears the SCOUTED block over the same ten slots. Leaving it behind
                // would let a spot earned on one chunk buy a sneak-past on the next.
                WriteGlobal(ScoutedFlagKey(slot), 0);
            }
        }

        /// <summary>DDX shown when an ambush is spotted and the DEF record names no dialog of its own.</summary>
        private const uint SpottedAmbushDialogId = 0x2f;

        private DefCombEntry CombRecord(TileEventTrigger trigger) =>
            _comb != null && trigger.EntryNumber < (uint)_comb.Records.Count
                ? _comb.Records[(int)trigger.EntryNumber].Payload
                : null;

        private DefTrapEntry TrapRecord(TileEventTrigger trigger) =>
            _trap != null && trigger.EntryNumber < (uint)_trap.Records.Count
                ? _trap.Records[(int)trigger.EntryNumber].Payload
                : null;

        /// <summary>
        /// <c>isEncounterIdWhitelisted</c>: an encounter no amount of Stealth, scouting or fog gets
        /// past. Tested at the very top of the Comb handler, before every other gate.
        /// </summary>
        /// <remarks>
        /// <b>This had no caller until 2026-09-13</b>, and the list itself did not exist — the
        /// predicate <see cref="CombatEncounterAvoidance.AvoidanceIsSkipped"/> was written, tested
        /// and never asked. Found by driving: the port let the party stealth past an encounter the
        /// original walked into, and reading the original's block to settle whether that was the
        /// roll or a rule turned this up beside it.
        /// </remarks>
        private bool AvoidanceIsWhitelisted(TileEventTrigger trigger) {
            long? encounter = EncounterNumberOf(trigger);
            return encounter.HasValue && CombatEncounterAvoidance.IsWhitelisted(encounter.Value);
        }

        private long? EncounterNumberOf(TileEventTrigger trigger) => trigger.Type switch {
            TileEventType.Comb => CombRecord(trigger)?.EncounterNumber,
            TileEventType.Trap => TrapRecord(trigger)?.EncounterNumber,
            _ => null,
        };

        /// <summary>
        /// The record's <b>pre-fire</b> dialog — <c>DialogId2</c>, at DEF offset <c>0xA</c>.
        /// </summary>
        /// <remarks>
        /// <b>The one the SPOTTED arm speaks</b>, when a scouting roll stops the party short of an
        /// ambush. It is not the line played when the fight actually starts — see
        /// <see cref="EntryDialogId"/>, and mind that the two live one field apart with names that
        /// invite the swap.
        /// </remarks>
        private uint PreFireDialogId(TileEventTrigger trigger) => trigger.Type switch {
            TileEventType.Comb => CombRecord(trigger)?.DialogId2 ?? 0,
            TileEventType.Trap => TrapRecord(trigger)?.DialogId2 ?? 0,
            _ => 0,
        };

        /// <summary>
        /// The record's <b>main-fire</b> dialog — <c>DialogId1</c>, at DEF offset <c>6</c>.
        /// </summary>
        /// <remarks>
        /// <b>Spoken over the travel view, before the arena is built.</b>
        /// <c>hotspotevt_type1_encounter_run</c> plays <c>*(unsigned long *)(buf + 6)</c> and only
        /// then calls <c>combat_arena_actor_turn_loop</c> (HOTSPOT.C:526-528), so the player reads
        /// the line and clicks it away while still standing in the world.
        ///
        /// <para><b>Nothing played it until 2026-09-12</b>, so every ambush opened in silence.
        /// Measured on <c>dir.G01/SAVE01</c>: the original stops at (669600,821600) with "They were
        /// being watched. Unsure where their observers were located, Locklear wheeled about just as a
        /// figure emerged from behind the trees!" and ours went straight from the travel HUD to the
        /// combat screen at every one of fourteen presses.</para>
        /// </remarks>
        private uint EntryDialogId(TileEventTrigger trigger) => trigger.Type switch {
            TileEventType.Comb => CombRecord(trigger)?.DialogId1 ?? 0,
            TileEventType.Trap => TrapRecord(trigger)?.DialogId1 ?? 0,
            _ => 0,
        };

        // --- helpers ----------------------------------------------------------------------

        /// <summary>
        /// Parses a <c>Tzzxxyy</c> chunk name into its chunk coordinates. Shared with the debug
        /// trigger overlay so both agree on how a chunk name maps to a position.
        /// </summary>
        public static bool TryParseChunkCoords(string chunkName, out int cx, out int cy) {
            cx = 0;
            cy = 0;
            return chunkName != null && chunkName.Length >= 7
                && int.TryParse(chunkName.Substring(3, 2), out cx)
                && int.TryParse(chunkName.Substring(5, 2), out cy);
        }

        /// <summary>
        /// The triggers a tile carries in the given chapter. A tile holds a separate block per
        /// chapter, so the same ground fires different things as the story moves on; a chapter with
        /// no block simply has no triggers.
        /// </summary>
        public static List<TileEventTrigger> TriggersFor(TileEventTile tile, int chapter) {
            if (tile == null) {
                return new List<TileEventTrigger>();
            }
            foreach (TileEventChapter block in tile.Chapters) {
                if (block.ChapterNumber == chapter) {
                    return block.Triggers;
                }
            }
            return new List<TileEventTrigger>();
        }

        /// <summary>The ref-pair a world position falls in, or 0 when the zone names none there.</summary>
        /// <remarks>
        /// <b>Zero is a real pair AND the fallback</b>, which is the original's own shape — but it
        /// means a lookup that misses writes into pair 0 rather than nowhere. Kept as one helper so
        /// the two callers cannot drift; see the note in <c>LoadZoneAsync</c> for what drifting cost.
        /// </remarks>
        private int RefIndexAt(int worldX, int worldY) =>
            _refIndex.TryGetValue((Floor(worldX, ChunkSize), Floor(worldY, ChunkSize)), out int at)
                ? at
                : 0;

        private static int Floor(int value, int size) {
            int q = value / size;
            return value < 0 && value % size != 0 ? q - 1 : q;
        }

        /// <summary>
        /// Loads a resource, answering null rather than throwing.
        /// </summary>
        /// <param name="optional">
        /// <b>Absence is normal for this address.</b> Only the sparse per-id families qualify: about
        /// half the creatures ship no MONST file, and most tiles carry no event file, so those
        /// misses are data shape rather than fault.
        /// </param>
        /// <remarks>
        /// <b>A REQUIRED RESOURCE THAT FAILS TO LOAD NOW LOGS A WARNING, AND THAT IS THE WHOLE
        /// POINT.</b> Every miss used to be a LogDebug, invisible in a normal run — so when
        /// <c>CombatAffinityTables</c> had no registered extractor, every fight ran with null
        /// affinity tables and the AI flee thresholds never reached the resolver, for as long as it
        /// took someone to read an Editor log by hand (2026-08-29, TASK-255).
        ///
        /// <para>Measured on that log: 303 swallowed loads, of which 296 were the two sparse
        /// families and 7 were the real fault. So a blanket warning would have been 296 lines of
        /// noise hiding 7 lines of signal, and silence hid all 7. The split is what makes either
        /// half readable.</para>
        /// </remarks>
        private async UniTask<T> LoadOrNull<T>(string address, object owner, bool optional = false)
            where T : class {
            try {
                return await _resources.LoadAssetAsync<T>(address, owner);
            } catch (System.Exception e) {
                if (_logger == null) {
                    return null;
                }
                if (optional) {
                    LoggerExtensions.LogDebug(_logger, "Hotspots: {Address} not available ({Message}).", address, e.Message);
                } else {
                    LoggerExtensions.LogWarning(_logger,
                        "Hotspots: REQUIRED resource {Address} did not load ({Message}). The feature "
                        + "that reads it will run degraded and silent.", address, e.Message);
                }
                return null;
            }
        }
    }
}
