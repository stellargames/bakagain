namespace BakAgain.Core.Services {
    using System;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using Microsoft.Extensions.Logging;
    using ResourceExtraction;
    using VContainer;

    /// <summary>
    /// Gathers the modeled fields from <see cref="GameSession"/>, patches them into a copy of the
    /// backing body via <see cref="SaveGameWriter"/> (Phase 1), and writes the SAVE##.GAM slot. The
    /// header worldX/worldY/mapIcon are the source header values the session loaded from (display
    /// metadata — not required byte-exact for interchangeability). Logs save coverage.
    /// </summary>
    public sealed class SaveGameService : ISaveGameService {
        private readonly GameSession _session;
        private readonly ISaveGameDirectoryService _saves;
        private readonly ILogger<SaveGameService> _logger;

        [Inject]
        public SaveGameService(
            GameSession session, ISaveGameDirectoryService saves, ILogger<SaveGameService> logger,
            IGameClock clock, BakAgain.ResourceManagement.IResourceProviderService resources) {
            _resources = resources;
            _session = session;
            _saves = saves;
            _logger = logger;
            _clock = clock;
        }

        // Pending timers live in the clock, not in GameSession, so the writer is handed them here.
        private readonly IGameClock _clock;
        private readonly BakAgain.ResourceManagement.IResourceProviderService _resources;

        public async UniTask<bool> SaveAsync(string directoryName, int slotIndex, string saveName) {
            if (!_session.IsActive || !_session.HasBackingBody) {
                _logger.LogError("SaveAsync: no active session / no backing body; cannot save.");
                return false;
            }
            try {
                // The original looks the marker up afresh at every save (MAINMENU.C:963-970), so
                // the header carries where the party is now, not where the game was loaded (TASK-794).
                await _session.PlaceMapMarkerAsync(_resources, this);
                var header =
                    ResourceExtraction.Extractors.SaveGameExtractor.HeaderFieldsFor(new FullMapIcon(
                        _session.MapMarkerVisible, _session.MapMarkerXPercent, _session.MapMarkerYPercent,
                        _session.MapMarkerIcon));
                var fields = new SaveGameFields(
                    Chapter: (short)_session.Chapter,
                    PartyGold: _session.PartyGold,
                    GameTime: (int)_session.GameTimeIn2Seconds,
                    TimeSnapshot: (int)_session.LastRestTicks,
                    PaletteEventMask: (short)_session.PaletteEventMask,
                    PartyDeathState: _session.PartyDeathState,
                    ChapterTransitionPending: _session.ChapterTransitionPending,
                    PreviousZone: _session.PreviousZone,
                    CurrentZone: _session.CurrentZone,
                    WorldX: _session.WorldX,
                    WorldY: _session.WorldY,
                    PositionX: _session.PositionX,
                    PositionY: _session.PositionY,
                    PositionZ: _session.PositionZ,
                    Rotation: _session.Rotation,
                    // The player's overhead-map zoom. Body offset 55, read back as
                    // SaveGameMovementData.MapCameraZ — before this it was read and never written,
                    // so the zoom reset to the zone default on every load.
                    MapCameraZ: (int)_session.MapCameraZ,
                    // Follow-road, body offset 50. Read and never written until 2026-09-12, exactly
                    // as the map zoom above was — and this one decides where the party may WALK, so
                    // a save written without it hands the original a party free to leave the road
                    // when the player had it engaged. TASK-422.
                    IsAutoTravelling: (short)(_session.IsAutoTravelling ? 1 : 0),
                    // The movement-cell counter and its boundary flag, body offsets 52 and 53. Read
                    // by the extractor since it was written and never saved, the same gap
                    // IsAutoTravelling above had: a save without them restarts the count, and the
                    // flag decides whether an avoidable encounter is evaluated at all on a step.
                    SubTileStepCount: (byte)_session.SubTileStepCount,
                    TileBoundaryCrossed: (short)_session.TileBoundaryCrossed,
                    // The cast screen's remembered caster and school, body offsets 1622/1624. Loaded
                    // and reset (ApplyChapterStart) but never written until TASK-531, so a save kept
                    // whatever pair the loaded file carried.
                    CastMenuCasterSlot: (short)_session.CastMenuCasterSlot,
                    CastMenuSchool: (short)_session.CastMenuSchool,
                    // The session owns the party composition, so a save has to carry it. Nothing
                    // changes it at runtime yet, which means this writes back what the body already
                    // held — the point is that it will not silently stop being written when
                    // something does.
                    ActiveParty: _session.ActivePartyIndices,
                    // ...and the slot image behind it, so a party that shrank stores the action's
                    // zero in its spare slot as the original does, not the loaded file's byte.
                    ActivePartySlots: _session.ActivePartySlots);

                var containerEdits = _session.CollectDirtyContainerEdits();
                var actorEdits = _session.CollectDirtyActorEdits();
                // *** NAMED, NOT POSITIONAL. *** The writer's tail is a run of optional edit lists
                // of different types, and a new one inserted among them binds silently to the wrong
                // argument — adding combatantEdits did exactly that and only failed at the Unity
                // compile, one repo away from the change.
                SaveGameWriteResult r = SaveGameWriter.Write(
                    _session.CloneBackingBody(), fields, saveName,
                    header.X, header.Y, header.Icon,
                    containerEdits: containerEdits,
                    actorEdits: actorEdits,
                    timers: _clock.PendingTimers,
                    automapVisits: _session.AutomapVisits,
                    encounterActorStates: _session.EncounterActorStates,
                    // Stamped at the end of every fight; the next visit heals survivors from it (TASK-517).
                    encounterFoughtTimes: _session.EncounterFoughtTimes,
                    // The purse at each chapter start; chapters 6-8 restore from it (TASK-524).
                    chapterFinishingGold: _session.ChapterFinishingGold,
                    // Reading a stat can FREE an expired modifier slot, so the block has to be
                    // written back or the expiry is undone by the next load — see
                    // GameSession.PartyEffectsFor.
                    statModifiers: _session.StatModifiers,
                    // The change-detector baseline, which MOVES: the roaming-encounter reset stores
                    // a new one after every comparison, fired or not. Saving the loaded value
                    // instead would re-fire the reset on the next apply.
                    lastSeenStepSpeed: _session.LastSeenStepSpeed,
                    lastSeenGridStride: _session.LastSeenGridStride,
                    // The story flags. Nothing wrote these before 2026-08-25 (TASK-210): every flag
                    // a dialog set lasted only until the next save.
                    globalFlagEdits: _session.DirtyGlobalFlags,
                    // Staged when each fight ENDED, not collected here: the encounter is long gone
                    // by save time and asking then returns an empty list for ever (TASK-226).
                    combatantEdits: _session.DirtyCombatantEdits,
                    // Enemy health and stamina, which the combat record does not carry (TASK-230).
                    rosterActorEdits: _session.DirtyRosterActorEdits);

                await _saves.EnsureDirectoryAsync(directoryName);
                string path = _saves.SlotFullPath(directoryName, slotIndex);
                bool written = await _saves.WriteSlotAsync(path, r.Bytes);
                if (!written) {
                    // The bytes never reached disk (disk full / permission denied). Report the
                    // failure so the dialog can surface it (DDX 150) instead of closing on a
                    // phantom success.
                    _logger.LogError("SaveAsync: write failed for {Dir}/slot {Slot}.", directoryName, slotIndex);
                    return false;
                }

                // Remember where this game now lives. The bookmark writes slot 0 of THIS directory,
                // and without it the quick-save has nowhere to go — the original keeps the same pair
                // in g_szSaveSlotDirName + g_wCurrentSaveSlotKey.
                _session.SetSaveLocation(directoryName, slotIndex);

                SaveCoverage cov = r.Coverage;
                _logger.LogInformation(
                    "Saved '{Name}' -> {Path}. Coverage: {Authored}/{Total} bytes authored ({Pct:0.00}%), {Pass} passthrough.",
                    saveName, path, cov.AuthoredBytes, cov.TotalBodyBytes, cov.PercentAuthored, cov.PassthroughBytes);
                return true;
            } catch (Exception e) {
                _logger.LogError(e, "SaveAsync failed for {Dir}/slot {Slot}.", directoryName, slotIndex);
                return false;
            }
        }
    }
}
