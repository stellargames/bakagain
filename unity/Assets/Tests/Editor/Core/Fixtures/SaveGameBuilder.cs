namespace BakAgain.Tests.Editor.Core.Fixtures {
    using GameData.Resources.Data;
    using GameData.Resources.Location;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Builder for hand-crafted <see cref="SaveGame"/> fixtures used in unit tests.
    /// Every required field gets a sensible default so individual tests only need to
    /// override what they actually care about. Keep this minimal — if a field doesn't
    /// surface in <see cref="BakAgain.Core.GameSession"/>, leave its default alone.
    /// </summary>
    public sealed class SaveGameBuilder {
        // Header
        private string _id = "TEST.GAM";
        private string _saveGameName = "test save";
        private short _chapterNumber = 1;
        private short _worldX = 116;
        private short _worldY = 75;
        private short _mapIcon = 18;
        private short _version = SaveGame.SupportedVersion;
        private bool _includeData = true;
        private byte[] _tempGameData = Array.Empty<byte>();
        // Default marker matches the default header (116,75 icon 18): 116/320, 75/200, icon+2.
        private FullMapIcon _fullMapMarker = new FullMapIcon(true, 116f / 320f * 100f, 75f / 200f * 100f, 20);

        // State data (subset that GameSession actually reads)
        private int _partyGold = 100;
        private int _gameTimeIn2Seconds = 0;
        private byte _currentZoneNumber = 1;
        private byte _worldXCoordinate = 116;
        private byte _worldYCoordinate = 75;
        private int _positionX = 700000;
        private int _positionY = 700000;
        private int _positionZ = 0;
        private short _currentZRotation = 0;
        // The world scalars as the game last saw them — a change detector, not a setting.
        private short _lastSeenStepSpeed = 400;
        private short _lastSeenGridStride = 256;
        private string[] _actorNames = { "Locklear", "Gorath", "Owyn" };
        private byte _activePartyCount = 3;
        private byte[] _activePartyIndices = { 0, 1, 2 };
        private SaveGameActorData[] _partyActors = Array.Empty<SaveGameActorData>();

        public SaveGameBuilder WithVersion(short version) {
            _version = version;
            return this;
        }

        public SaveGameBuilder WithoutData() {
            _includeData = false;
            return this;
        }

        /// <summary>Attach a full-size TEMP.GAM backing body so a hydrated
        /// <see cref="BakAgain.Core.GameSession"/> reports <c>HasBackingBody</c> and can
        /// be saved. The bytes are zero-filled — enough to exercise the save path.</summary>
        public SaveGameBuilder WithBackingBody() {
            _tempGameData = new byte[ResourceExtraction.SaveGameOffsets.BodySize];
            return this;
        }

        public SaveGameBuilder WithChapter(short chapter) {
            _chapterNumber = chapter;
            return this;
        }

        public SaveGameBuilder WithMapIcon(short icon) {
            _mapIcon = icon;
            return this;
        }

        public SaveGameBuilder WithFullMapMarker(FullMapIcon marker) {
            _fullMapMarker = marker;
            return this;
        }

        public SaveGameBuilder WithZone(byte zone) {
            _currentZoneNumber = zone;
            return this;
        }

        public SaveGameBuilder WithWorldTile(byte wx, byte wy) {
            _worldXCoordinate = wx;
            _worldYCoordinate = wy;
            _worldX = wx;
            _worldY = wy;
            return this;
        }

        public SaveGameBuilder WithPosition(int x, int y, int z, short rotation) {
            _positionX = x;
            _positionY = y;
            _positionZ = z;
            _currentZRotation = rotation;
            return this;
        }

        public SaveGameBuilder WithLastSeenWorldScalars(short stepSpeed, short gridStride) {
            _lastSeenStepSpeed = stepSpeed;
            _lastSeenGridStride = gridStride;
            return this;
        }

        public SaveGameBuilder WithPartyGold(int gold) {
            _partyGold = gold;
            return this;
        }

        public SaveGameBuilder WithGameTime(int time) {
            _gameTimeIn2Seconds = time;
            return this;
        }

        /// <summary>
        /// Attach the six party actor records a real save always carries. Attributes are seeded
        /// with a distinct value per character and per attribute, so a test that mixes up a
        /// character id or an attribute index fails on the value rather than passing by luck.
        /// </summary>
        public SaveGameBuilder WithPartyActors() {
            var actors = new SaveGameActorData[6];
            for (var character = 0; character < actors.Length; character++) {
                SaveGameAttributeValuesData Attribute(int index) {
                    var value = (byte)(10 + character * 16 + index);
                    return new SaveGameAttributeValuesData(200, value, value, 0, 0);
                }

                actors[character] = new SaveGameActorData(
                    0, 0, 0, 0,
                    Attribute(0), Attribute(1), Attribute(2), Attribute(3),
                    Attribute(4), Attribute(5), Attribute(6), Attribute(7),
                    Attribute(8), Attribute(9), Attribute(10), Attribute(11),
                    Attribute(12), Attribute(13), Attribute(14), Attribute(15),
                    (byte)character, 0, 0);
            }
            _partyActors = actors;
            return this;
        }

        public SaveGameBuilder WithParty(string[] names, byte[] activeIndices) {
            _actorNames = names;
            _activePartyIndices = activeIndices;
            _activePartyCount = (byte)activeIndices.Length;
            return this;
        }

        /// <summary>
        /// A roster whose SIZE disagrees with its array — which every real save has, because the
        /// slot array is a fixed three bytes and the writer leaves the spare slots alone.
        /// </summary>
        public SaveGameBuilder WithParty(string[] names, byte[] slots, byte activeCount) {
            _actorNames = names;
            _activePartyIndices = slots;
            _activePartyCount = activeCount;
            return this;
        }

        public SaveGame Build() {
            SaveGameData data = _includeData ? BuildData() : null;
            return new SaveGame(
                _id,
                _saveGameName,
                _chapterNumber,
                _worldX,
                _worldY,
                _mapIcon,
                _version,
                tempGameData: _tempGameData,
                data: data,
                fullMapMarker: _fullMapMarker);
        }

        private SaveGameData BuildData() {
            SaveGameStateData state = BuildStateData();
            SaveGameWorldData world = new SaveGameWorldData(
                explorationStateEntries: Array.Empty<SaveGameExplorationStateEntryData>(),
                zoneDataEntries: Array.Empty<SaveGameZoneDataData>(),
                chapterGoldAmounts: new int[9],
                enemyPartyActorSlots: new Dictionary<int, List<short>>(),
                combatEncounterTimestamps: new Dictionary<int, int>(),
                enemyTrapRespawnTimestamps: new Dictionary<int, int>(),
                worldEncounterItemData: Array.Empty<SaveGameWorldEncounterItemData[]>());
            SaveGameZoneContainerStateData zoneContainers = new SaveGameZoneContainerStateData(
                Array.Empty<SaveGameZoneContainerEntryData>());

            return new SaveGameData(
                stateData: state,
                worldStateData: world,
                actorStateData: Array.Empty<SaveGameActorData>(),
                combatStateData: Array.Empty<SaveGameCombatData>(),
                zoneContainerStateData: zoneContainers,
                worldData: Array.Empty<byte>(),
                actorData: Array.Empty<byte>(),
                combatData: Array.Empty<byte>(),
                zoneContainerData: Array.Empty<byte>());
        }

        private SaveGameStateData BuildStateData() {
            SaveGamePartyConfigurationData partyConfig = new SaveGamePartyConfigurationData(
                numberOfActivePartyCharacters: _activePartyCount,
                activePartyCharacters: _activePartyIndices,
                sharedInventoryPointer1: 0,
                sharedInventoryPointer2: 0,
                attributeIncreasedFlag: 0,
                rewardMoneyCounter: 0,
                initialAttributeGainModifiers: Array.Empty<short>(),
                actorStatusEffects: Array.Empty<SaveGameActorStatusEffectsData>());

            SaveGameMovementData movement = new SaveGameMovementData(
                isAutoTraveling: 0,
                subTileStepCount: 0,
                tileBoundaryCrossed: 0,
                mapCameraZ: 0);

            SaveGameMiscStateData misc = new SaveGameMiscStateData(
                dialogPrimaryActorNumber: 0,
                dialogSecondaryActorNumber: 0,
                dialogTertiaryActorNumber: 0,
                unusedPadding: 0,
                global30000: 0,
                creatureType: 0,
                keyObjectId: 0,
                global30013AttributeValue: 0,
                global30014Money: 0,
                global30015: 0,
                global30018: 0);

            SaveGameLightingStateData lighting = new SaveGameLightingStateData(
                activeSpellTimerFlags: 0,
                partyMember: -1,
                lastSpellSymbolFile: -1,
                lightNeedsUpdate: 0,
                previousDayLightLevel: 0,
                currentDaylightLevel: 0,
                itemLightLevel: 0,
                candleglowLightLevel: 0,
                starduskLightLevel: 0,
                dragonsbreathLightLevel: 0);

            SaveGameZoneDataData zoneCache = new SaveGameZoneDataData(
                zoneNumber: 0,
                field2: 0,
                tempGamFileOffset: 0,
                objFixedFileOffset: 0,
                fieldE: 0,
                field12: 0);

            return new SaveGameStateData(
                chapterNumber: _chapterNumber,
                partyGold: _partyGold,
                gameTimeIn2Seconds: _gameTimeIn2Seconds,
                timeSnapshot: 0,
                partyDeathState: 0,
                chapterTransitionPending: 0,
                padding: 0,
                previousZoneNumber: 0,
                currentZoneNumber: _currentZoneNumber,
                worldXCoordinate: _worldXCoordinate,
                worldYCoordinate: _worldYCoordinate,
                positionX: _positionX,
                positionY: _positionY,
                positionZ: _positionZ,
                currentZRotation: _currentZRotation,
                teleportDestination: new TeleportDestination(),
                lastSeenStepSpeed: _lastSeenStepSpeed,
                lastSeenGridStride: _lastSeenGridStride,
                movementData: movement,
                actorNames: _actorNames,
                partyActorData: Array.Empty<byte>(),
                partyActors: _partyActors,
                partyConfigurationData: partyConfig,
                timedEffectsByActor: Array.Empty<SaveGameTimedEffectData[]>(),
                miscStateData: misc,
                currentTimerAmount: 0,
                timers: Array.Empty<SaveGameTimerData>(),
                lightingStateData: lighting,
                cachedZoneData: zoneCache,
                globalFlags: Array.Empty<byte>(),
                globalFlags2: Array.Empty<byte>());
        }
    }
}
