namespace BakAgain.Core.Services {
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using GameData.Resources.Object;
    using Microsoft.Extensions.Logging;
    using ResourceExtraction.Extractors;
    using System;
    using System.IO;

    /// <summary>
    /// Default <see cref="IGameStateLoader"/>. Pulls a <see cref="SaveGame"/> through
    /// the standard resource pipeline (so override providers apply), validates it,
    /// and hands it to <see cref="GameSession.Initialize"/>.
    /// </summary>
    public sealed class GameStateLoader : IGameStateLoader {
        private const string StartupGameKey = "STARTUP.GAM";
        private const string ChapterDataKeyFormat = "CHAP{0}.DAT";
        private const string ObjectInfoKey = "OBJINFO.DAT";
        private const string CreatureNamesKey = "MNAMES.DAT";

        private readonly IResourceProviderService _resources;
        private readonly GameSession _session;
        private readonly ILogger<GameStateLoader> _logger;

        public GameStateLoader(
            IResourceProviderService resources,
            GameSession session,
            ILogger<GameStateLoader> logger,
            GameClock clock) {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clock = clock;
        }

        private readonly GameClock _clock;

        /// <summary>
        /// Hand a loaded save's pending timers back to the clock. The pool is the clock's state,
        /// not the session's, so hydration is wired here alongside Initialize rather than inside
        /// GameSession — which has no business knowing about the clock.
        /// </summary>
        private void RestoreTimers(SaveGame saveGame) {
            GameData.Resources.Data.SaveGameStateData state = saveGame?.Data?.StateData;
            if (state == null || _clock == null) {
                return;
            }
            _clock.LoadTimers(state.Timers, state.CurrentTimerAmount);
        }

        public UniTask<bool> LoadNewGameAsync() {
            // The starting chapter isn't a caller decision — it's stored in STARTUP.GAM's
            // state block (ChapterNumber) and drives which CHAPx.DAT is applied over the
            // template. Derived after hydration in LoadAndHydrateAsync so the record and the
            // chapter data can't drift.
            _logger.LogInformation("Loading {Key} for a new game.", StartupGameKey);
            return LoadAndHydrateAsync(StartupGameKey, GameSessionSource.NewGame, applyChapterStart: true);
        }

        public UniTask<bool> LoadFromSaveAsync(string saveGameAddressableKey) {
            if (string.IsNullOrWhiteSpace(saveGameAddressableKey)) {
                throw new ArgumentException("Save key must be provided.", nameof(saveGameAddressableKey));
            }

            _logger.LogInformation("Loading save game {Key}.", saveGameAddressableKey);
            return LoadAndHydrateAsync(saveGameAddressableKey, GameSessionSource.LoadSave, applyChapterStart: false);
        }

        public UniTask<bool> LoadFromFileAsync(string filePath) {
            if (string.IsNullOrWhiteSpace(filePath)) {
                throw new ArgumentException("File path must be provided.", nameof(filePath));
            }
            _logger.LogInformation("Loading save game from {Path}.", filePath);

            SaveGame saveGame;
            try {
                // SaveGameExtractor reads ~334 KB synchronously; on the player's
                // Restore click that's a brief blocking read, acceptable for a
                // modal screen. If it ever needs to be non-blocking, wrap the
                // body in UniTask.RunOnThreadPool.
                using FileStream stream = File.OpenRead(filePath);
                saveGame = new SaveGameExtractor().Extract(Path.GetFileName(filePath), stream);
            } catch (Exception ex) {
                _logger.LogError(ex, "Exception loading save game from {Path}.", filePath);
                return UniTask.FromResult(false);
            }

            if (!Validate(saveGame, filePath)) {
                return UniTask.FromResult(false);
            }

            _session.Initialize(saveGame, GameSessionSource.LoadSave);
            RestoreTimers(saveGame);
            return FinishLoadFromFileAsync(filePath);   // loads OBJFIXED too, see the tail
        }

        private async UniTask<bool> FinishLoadFromFileAsync(string filePath) {
            try {
                if (!await LoadObjectInfoAsync()) {
                    return false;
                }

                // MNAMES.DAT (creature names) is static catalog data too, and dialog text needs it:
                // text-variable kind 17 names the creature a dialog is about, and it is the most-used
                // kind in the shipped DDX files. Not fatal on failure — those tokens then resolve to
                // nothing rather than blocking a game from starting.
                await LoadCreatureNamesAsync();
                await LoadFixedObjectsAsync();
                await UiStringLoader.InstallAsync(_resources, _logger);

                _logger.LogInformation(
                    "GameSession hydrated from {Path}: chapter {Chapter}, zone {Zone}, " +
                    "world ({WX},{WY}), gold {Gold}.",
                    filePath, _session.Chapter, _session.CurrentZone,
                    _session.WorldX, _session.WorldY, _session.PartyGold);
                return true;
            } finally {
                _resources.ReleaseAssets(this);
            }
        }

        /// <summary>
        /// Loads OBJFIXED.DAT, the shipped half of the placement lookup.
        /// </summary>
        /// <remarks>
        /// <b>Without this most fixed objects cannot be found at all.</b> The save only SHADOWS the
        /// shipped file — <c>actorspawn_objfixed</c> checks both — and a great many placements are
        /// never written back to a save. Doors are the plain case: not one door placement exists in
        /// any save, so a save-only lookup answers null for every door in the game.
        ///
        /// <para>Best-effort: a failure leaves <c>FixedObjects</c> null and the lookup degrades to
        /// save-only, which is the behaviour every caller had before this source existed.</para>
        /// </remarks>
        private async UniTask LoadFixedObjectsAsync() {
            try {
                _session.FixedObjects = await _resources
                    .LoadAssetAsync<GameData.Resources.Data.FixedObjectSet>("OBJFIXED.DAT", owner: this);
            } catch (Exception ex) {
                _logger.LogWarning(ex,
                    "OBJFIXED.DAT did not load; fixed-object placements fall back to the save alone.");
            }
        }

        private async UniTask<bool> LoadAndHydrateAsync(string key, GameSessionSource source, bool applyChapterStart) {
            // Hydration copies fields out of the SaveGame; the asset itself can be
            // released as soon as we're done. A future save flow will go through a
            // separate serializer, not by retaining this handle.
            try {
                SaveGame saveGame;
                try {
                    saveGame = await _resources.LoadAssetAsync<SaveGame>(key, owner: this);
                } catch (Exception ex) {
                    _logger.LogError(ex, "Exception loading save game {Key}.", key);
                    return false;
                }

                if (!Validate(saveGame, key)) {
                    return false;
                }

                _session.Initialize(saveGame, source);
                RestoreTimers(saveGame);
                await LoadFixedObjectsAsync();

                // STARTUP.GAM is only a template — its state block leaves zone/position/clock
                // zeroed, but it does carry the starting ChapterNumber (Initialize copied it to
                // _session.Chapter). A new game applies that chapter's record (CHAPx.DAT) over the
                // template, exactly as the DOS engine's go_to_chapter does. Loaded saves already
                // carry real state, so they skip this.
                if (applyChapterStart && !await ApplyChapterStartAsync(_session.Chapter)) {
                    return false;
                }

                // OBJINFO.DAT (the 138 item-definition records) is static, chapter/save-independent
                // catalog data — load it alongside every hydration path so inventory systems can
                // resolve object ids as soon as the session is active.
                if (!await LoadObjectInfoAsync()) {
                    return false;
                }

                // MNAMES.DAT (creature names) is static catalog data too, and dialog text needs it:
                // text-variable kind 17 names the creature a dialog is about, and it is the most-used
                // kind in the shipped DDX files. Not fatal on failure — those tokens then resolve to
                // nothing rather than blocking a game from starting.
                await LoadCreatureNamesAsync();
                await UiStringLoader.InstallAsync(_resources, _logger);

                _logger.LogInformation(
                    "GameSession hydrated from {Key}: chapter {Chapter}, zone {Zone}, " +
                    "world ({WX},{WY}), gold {Gold}, mapMarker ({MX}%,{MY}% icon {Icon} vis {Vis}), " +
                    "party [{Party}].",
                    key, _session.Chapter, _session.CurrentZone,
                    _session.WorldX, _session.WorldY, _session.PartyGold,
                    _session.MapMarkerXPercent, _session.MapMarkerYPercent,
                    _session.MapMarkerIcon, _session.MapMarkerVisible,
                    string.Join(", ", _session.PartyActorNames));
                return true;
            } finally {
                _resources.ReleaseAssets(this);
            }
        }

        /// <summary>
        /// Applies one chapter's CHAPx.DAT over the live session — <b>without re-hydrating.</b>
        /// </summary>
        /// <remarks>
        /// <b>A chapter TRANSITION must not re-hydrate, which is why this is reachable on its own.</b>
        /// It used to be private, callable only through
        /// <see cref="LoadAndHydrateAsync"/>'s <c>applyChapterStart</c> arm — and that path also runs
        /// <c>Initialize</c> from STARTUP.GAM, which would reset the party, their inventories and
        /// every story flag. Chapter 2 is not a new game.
        ///
        /// <para>The caller owns the release. <see cref="LoadAndHydrateAsync"/> releases in its own
        /// <c>finally</c>; a standalone call has no such wrapper, so this one releases what it
        /// loaded.</para>
        /// </remarks>
        public async UniTask<bool> ApplyChapterStartAsync(int chapterNumber) {
            string chapterKey = string.Format(ChapterDataKeyFormat, chapterNumber);

            ChapterStartData chapter;
            try {
                chapter = await _resources.LoadAssetAsync<ChapterStartData>(chapterKey, owner: this);
            } catch (Exception ex) {
                _logger.LogError(ex, "Exception loading chapter data {Key}.", chapterKey);
                return false;
            }

            if (chapter?.StartLocation == null) {
                _logger.LogError("Failed to load chapter data {Key}: resource not found or malformed.", chapterKey);
                return false;
            }

            _session.ApplyChapterStart(chapter);
            return true;
        }

        private async UniTask<bool> LoadObjectInfoAsync() {
            ObjectInfoSet objectInfo;
            try {
                objectInfo = await _resources.LoadAssetAsync<ObjectInfoSet>(ObjectInfoKey, owner: this);
            } catch (Exception ex) {
                _logger.LogError(ex, "Exception loading object info {Key}.", ObjectInfoKey);
                return false;
            }

            if (objectInfo == null) {
                _logger.LogError("Failed to load object info {Key}: resource not found or malformed.", ObjectInfoKey);
                return false;
            }

            // *** THE MOD SOURCE IS A LATER ENTRY, NOT A SPECIAL CASE. *** The registry merges an
            // ORDERED list and the base carries no privilege, so installing a mod is appending one
            // source — see GameSession.CatalogOf. Without one the list has a single entry and this
            // is exactly SetObjectInfo.
            var sources = new System.Collections.Generic.List<GameData.Resources.Content.IContentSource<
                GameData.Resources.Object.ObjectInfo>> {
                new GameData.Resources.Object.ObjectInfoContentSource(objectInfo),
            };
            GameData.Resources.Content.IContentSource<GameData.Resources.Object.ObjectInfo> mod =
                BakAgain.ResourceManagement.ModItemCatalogSource.FromOverrides(objectInfo, _logger);
            if (mod != null) {
                sources.Add(mod);
            }

            GameData.Resources.Object.ObjectInfoCatalog catalog = GameSession.CatalogOf(sources);
            // The scroll price table rides along: it is OBJINFO.DAT's, not the catalog's, and the
            // numeric projection cannot reconstruct it from the merged records.
            _session.SetItemCatalog(catalog, objectInfo.SpellPrices);
            if (catalog.Merged.Overrides.Count > 0) {
                _logger.LogInformation(
                    "Item catalog: {Count} entr(ies) overridden by a mod source.",
                    catalog.Merged.Overrides.Count);
            }
            return true;
        }

        private async UniTask LoadCreatureNamesAsync() {
            try {
                var names = await _resources.LoadAssetAsync<GameData.Resources.Creature.CreatureNames>(
                    CreatureNamesKey, owner: this);
                if (names == null) {
                    _logger.LogWarning("Creature names {Key} not found; dialog creature tokens will " +
                        "resolve to nothing.", CreatureNamesKey);
                    return;
                }
                _session.SetCreatureNames(names);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Exception loading creature names {Key}; dialog creature " +
                    "tokens will resolve to nothing.", CreatureNamesKey);
            }
        }

        private bool Validate(SaveGame saveGame, string key) {
            if (saveGame == null) {
                _logger.LogError("Failed to load {Key}: resource not found.", key);
                return false;
            }
            if (!saveGame.IsSupportedVersion) {
                _logger.LogError(
                    "Save {Key} has unsupported version {Version} (expected {Expected}).",
                    key, saveGame.Version, SaveGame.SupportedVersion);
                return false;
            }
            if (saveGame.Data?.StateData == null) {
                _logger.LogError("Save {Key} has no parsed StateData.", key);
                return false;
            }
            return true;
        }
    }
}
