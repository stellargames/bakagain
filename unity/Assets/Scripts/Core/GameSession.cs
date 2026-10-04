namespace BakAgain.Core {
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Data;
    using GameData.Resources.Dialog;
    using GameData.Resources.GameState;
    using GameData.Resources.Inventory;
    using GameData.Resources.Content;
    using GameData.Resources.Object;
    using GameData.Resources.Spells;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Live in-memory game state for an active gameplay session.
    ///
    /// Hydrated once from a <see cref="SaveGame"/> (STARTUP.GAM for a new game, SAVE*.GAM
    /// for a loaded save) and then mutated by gameplay systems. Mirrors the live globals
    /// the DOS engine keeps in dseg — party gold, game time, current zone + world tile,
    /// camera/party 3D position + facing, etc.
    ///
    /// The session is a single, DI-resolved singleton. States and gameplay services
    /// read and write it directly; persistence (save/load) is the responsibility of
    /// <see cref="Services.IGameStateLoader"/> on the way in and a future save service
    /// on the way out.
    /// </summary>
    public sealed class GameSession {
        public bool IsActive { get; private set; }

        /// <summary>
        /// The save directory this game was last written to or loaded from, or null.
        /// </summary>
        /// <remarks>
        /// <b>The original's <c>g_szSaveSlotDirName</c> + <c>g_wSaveSlotDirValid</c>.</b> A bookmark
        /// writes slot 0 of THIS directory, so without it the quick-save has nowhere to go — which is
        /// exactly the condition the original refuses on. A new game has none until it is saved.
        /// </remarks>
        public string CurrentSaveDirectory { get; private set; }

        /// <summary>The slot this game was last written to or loaded from, or -1.</summary>
        public int CurrentSaveSlot { get; private set; } = -1;

        /// <summary>Whether there is a save location to bookmark into.</summary>
        public bool HasSaveLocation => !string.IsNullOrEmpty(CurrentSaveDirectory);

        /// <summary>Records where this game now lives, after a successful save or load.</summary>
        public void SetSaveLocation(string directoryName, int slot) {
            CurrentSaveDirectory = directoryName;
            CurrentSaveSlot = slot;
        }
        public GameSessionSource Source { get; private set; }

        // The raw TEMP.GAM body we loaded from (a save's body, or STARTUP.GAM for a new game).
        // The save writer patches a COPY of this so unmodeled regions stay byte-exact.
        private byte[] _backingBody = Array.Empty<byte>();
        private short _headerWorldX;
        private short _headerWorldY;
        private short _headerMapIcon;

        // Retained parsed save state, for flag/global reads (GetGlobalValue).
        private SaveGameStateData _state;

        // In-session overrides for global flags/values written by gameplay (e.g. a dialog's
        // SetFlagEffect via DialogBranchWalker). Explicit keys only — consulted before the loaded
        // save state, so an override wins but an *unset* key still falls through to _state (a byte[]
        // overlay would mask every _state flag with 0 once created).
        private Dictionary<int, int> _flagOverlay;

        public bool HasBackingBody => _backingBody.Length == ResourceExtraction.SaveGameOffsets.BodySize;

        /// <summary>
        /// Which dungeon entities the party has been near, per tile — what the underground map
        /// screen draws. Filled by the proximity scan as you walk (see
        /// <c>ProximityWorld.RecordAutomapVisits</c>); above ground nothing writes to it.
        /// </summary>
        public GameData.Resources.World.EncounterVisitTable AutomapVisits { get; } =
            new GameData.Resources.World.EncounterVisitTable();

        /// <summary>
        /// What happened to each encounter's actors — the state that makes a killed roaming group
        /// <b>stay</b> killed across a save and a revisit.
        /// </summary>
        /// <remarks>
        /// Written by the combat death handler through
        /// <see cref="GameData.Resources.World.EncounterObjectStates.MarkRemoved"/>, and read back
        /// by whoever re-places an encounter's actors. Distinct from <see cref="AutomapVisits"/>,
        /// which only remembers that the party came NEAR something.
        /// </remarks>
        public GameData.Resources.World.EncounterObjectStates EncounterActorStates { get; } =
            new GameData.Resources.World.EncounterObjectStates();

        /// <summary>When each encounter was last fought — see <see cref="GameData.Resources.World.EncounterFoughtTimes"/>.</summary>
        public GameData.Resources.World.EncounterFoughtTimes EncounterFoughtTimes { get; } =
            new GameData.Resources.World.EncounterFoughtTimes();

        /// <summary>The purse at the start of each chapter — see <see cref="GameData.Resources.GameState.ChapterFinishingGold"/>.</summary>
        public GameData.Resources.GameState.ChapterFinishingGold ChapterFinishingGold { get; } =
            new GameData.Resources.GameState.ChapterFinishingGold();

        /// <summary>
        /// The party's timed stat modifiers, eight slots per member, straight from the save block.
        /// </summary>
        /// <remarks>
        /// <b>Loaded but NOT yet applied to anything</b> — see TASK-251. Reading a stat still returns
        /// the stored value, because <see cref="StatsOf"/> hands out the LIVE mutable array that
        /// callers write damage back through; a modified view has to be a separate accessor, and
        /// which call sites want which is an open decision rather than a wiring detail.
        ///
        /// <para>Indexed flat by <c>ActorStatModifiers.IndexOf(character, slot)</c>. Empty until a
        /// save is loaded, which is also what a new game wants — STARTUP.GAM carries no modifiers.</para>
        /// </remarks>
        public GameData.Resources.Character.ActorStatModifiers.Slot[] StatModifiers { get; private set; } =
            new GameData.Resources.Character.ActorStatModifiers.Slot[
                GameData.Resources.Character.ActorStatModifiers.Characters
                * GameData.Resources.Character.ActorStatModifiers.SlotsPerCharacter];
        public byte[] CloneBackingBody() => (byte[])_backingBody.Clone();
        public short HeaderWorldX => _headerWorldX;
        public short HeaderWorldY => _headerWorldY;
        public short HeaderMapIcon => _headerMapIcon;

        // Header / chapter metadata.
        // The setter is private because hydration owns it — with ONE exception: event id 30007
        // (GameStateEventFields.Field.Chapter) is how dialog data changes the chapter, which is why
        // grepping the sources for a `go_to_chapter` routine finds nothing. That write goes through
        // WriteEventField, inside this class.
        public int Chapter { get; private set; }
        public short MapIcon { get; private set; }

        // World-map marker shown at chapter start. Coordinates are percentages (0..100)
        // of the full-map image so no display resolution is baked in. Populated from
        // CHAPx.DAT on a new game (see ApplyChapterStart), and from the save's own header on a
        // load (see Initialize, which reads saveGame.FullMapMarker) — the comment here used to
        // say the load path was unwired, which stopped being true when that path landed.
        public bool MapMarkerVisible { get; private set; }
        public float MapMarkerXPercent { get; private set; }
        public float MapMarkerYPercent { get; private set; }
        public int MapMarkerIcon { get; private set; }

        // Live mutable state — gameplay updates these every step / tick.
        public int PartyGold { get; set; }

        /// <summary>
        /// <c>stat_party_heal_all(amount)</c>: each active member through <c>stat_combatant_heal</c>.
        /// A heal that changes a pool stamps <see cref="LastRestTicks"/>, as the chapter heal does.
        /// </summary>
        public void HealActiveParty(int amount) {
            foreach (byte member in ActivePartyIndices ?? System.Array.Empty<byte>()) {
                GameData.Resources.Character.ActorStat[] stats = StatsOf(member);
                if (stats != null && GameData.Resources.Character.CharacterHeal.Apply(stats, ConditionsOf(member), amount)) {
                    LastRestTicks = GameTimeIn2Seconds;
                }
            }
        }
        public long GameTimeIn2Seconds { get; set; }

        /// <summary>
        /// When the party last rested, in game ticks (<c>dwLastActionTimeSnapshot</c>). Exhaustion
        /// is measured from here, and the rest screen stamps it. Loaded saves carry it.
        /// </summary>
        public long LastRestTicks { get; set; }

        /// <summary>
        /// A dialog is skipping time — <c>DIALOG.C:1511</c> passes the hourly tick a zero "awake"
        /// argument, so the skip neither drains the party nor tells it to rest.
        /// </summary>
        public bool DialogTimeSkipInProgress { get; set; }

        /// <summary>
        /// The dialog whose time is being skipped AUTO-ADVANCED, so the skip must not feed the
        /// party — this mirrors <c>g_dwDialogInputCooldown != 0</c>.
        /// </summary>
        /// <remarks>
        /// <c>DIALOG.C:1506</c> computes <c>recomputeParty = (g_dwDialogInputCooldown == 0)</c> and
        /// hands it to every <c>gstate_advance_time</c> pass of the skip; in <c>GSTATE.C</c> the day
        /// block then runs <c>gstate_consume_rations_tick</c> <b>only</b> when it is set, while the
        /// near-death recovery immediately below runs unconditionally. So a skip can age the party
        /// without feeding it, and only the MEAL is gated.
        ///
        /// <para><b>The cooldown is set by auto-advance and nothing else.</b>
        /// <c>dialog_wait_for_acknowledge</c> (DIALOG.C:203-221) clears it on entry, returns early
        /// leaving it clear for <c>SkipWait</c> (0x4000), and sets it to a reading-time deadline
        /// only under <c>flags &amp; 0x40</c> — <see cref="DialogEntryFlags.AutoAdvanceTimer"/> —
        /// the branch that dismisses the page itself instead of waiting for the player. A page the
        /// player acknowledges leaves it zero. So the rule is: <b>a page that advanced itself does
        /// not earn the party a meal.</b></para>
        ///
        /// <para>Measured across the zone 2 -> zone 1 crossing (TASK-624), five game days from one
        /// save, with rations 1/1/0. ORIGINAL: James untouched at 65 with no conditions, Owyn
        /// unchanged at 1 with Near-death 99 -> 94, only the already-starving Gorath collapsing.
        /// PORT before this flag: all three at 0 health and Near-death 100, because
        /// <c>OnDayElapsed</c> fed on every one of the five boundaries, so James and Owyn ate their
        /// single ration and then starved four days. <c>dial_z27:4410</c>, the entry carrying the
        /// <c>AdvanceTime: 432000</c>, is flagged <c>Legacy10, AutoAdvanceTimer</c>.</para>
        /// </remarks>
        public bool DialogTimeSkipAutoAdvanced { get; set; }

        /// <summary>
        /// Decides, as a container is opened, whether it was robbed while the party was away —
        /// <c>actor_maybeEmptyStashByExposure</c>, run by every WCURSOR.C open handler right before
        /// the inventory screen (TASK-506). Set by the world, which owns the scenery the score reads.
        /// </summary>
        public Action<GameData.Resources.Inventory.RuntimeContainer> ExposeStashOnOpen { get; set; }

        /// <summary>
        /// Which overworld spell palette-effects are running (<c>wPalEventMask</c>), one bit per
        /// effect. Maintained by the clock's Spell timers every tick — see
        /// <see cref="GameData.Resources.Spells.SpellPaletteEvents"/>.
        ///
        /// <para><b>It IS persisted</b>, both ways: hydrated below from
        /// <c>LightingStateData.ActiveSpellTimerFlags</c> and written by <c>SaveGameWriter</c> at
        /// <c>SaveGameOffsets.PaletteEventMask</c> (body 1620). This said "not yet persisted: the
        /// save block holding it is unmodelled, so the mask starts clear on load" — that stopped
        /// being true when the block was modelled, and it is the same stale-note shape that hid the
        /// two words after it (<see cref="CastMenuCasterSlot"/>) for as long.</para>
        /// </summary>
        public int PaletteEventMask { get; set; }
        /// <summary>
        /// Non-zero once the whole active party is down. Carried so a save written after a wipe
        /// comes back in the same state instead of looking survivable.
        /// </summary>
        public byte PartyDeathState { get; set; }

        /// <summary>1 when the world loop should exit into the next chapter.</summary>
        public byte ChapterTransitionPending { get; set; }

        /// <summary>
        /// The zone the party came FROM. Zone loading compares it against
        /// <see cref="CurrentZone"/> to tell a zone change from a rebuild of the same zone.
        /// </summary>
        public byte PreviousZone { get; set; }

        public byte CurrentZone { get; set; }

        /// <summary>
        /// The party's map tile — <b>derived from <see cref="PositionX"/>, never stored.</b>
        /// </summary>
        /// <remarks>
        /// These were settable and were only ever set on zone entry, chapter start, teleport and
        /// load — never as the party walked — while the ONLY reader is the save writer. So a save
        /// taken anywhere but the tile you arrived on recorded the tile you arrived on.
        ///
        /// <para><b>Measured 2026-09-10 across both engines.</b> Of the 16 shipped original saves,
        /// 16 have bytes 19/20 equal to <c>PositionX/Y / 64000</c>. Of 36 saves this port wrote
        /// during the chapter-1 run, <b>29 did not</b> — SAVE39 sat at tile (19,10) by position and
        /// recorded (10,18), the tile it entered zone 2 on. The original keeps its own
        /// <c>nPlayerTileX/Y</c> in step (CZONE.C:266), which is why its saves always agree.</para>
        ///
        /// <para>Derived rather than kept in sync on move: it is a pure function of the position
        /// already in this object, and a second copy of a derived value is what went stale.</para>
        /// </remarks>
        public byte WorldX => (byte)GameData.Resources.World.WorldPlacement.TileOf(PositionX);

        /// <inheritdoc cref="WorldX"/>
        public byte WorldY => (byte)GameData.Resources.World.WorldPlacement.TileOf(PositionY);

        public int PositionX { get; set; }
        public int PositionY { get; set; }
        public int PositionZ { get; set; }
        public short Rotation { get; set; }

        /// <summary>
        /// The overhead map's camera height, remembered between visits to the screen.
        /// </summary>
        /// <remarks>
        /// The original keeps it in the game state (<c>lInsetCameraPosZ</c>, body offset 55) rather than in the
        /// map screen, so the player's zoom survives closing the map — and it is re-seeded from the
        /// zone definition only when the zone changes (<see cref="MapCameraZone"/>). See
        /// <see cref="GameData.Resources.World.LocalMapScreen.ZoomIsRemembered"/>.
        /// </remarks>
        public long MapCameraZ { get; set; }

        /// <summary>The zone <see cref="MapCameraZ"/> was last seeded for.</summary>
        public int MapCameraZone { get; set; } = -1;

        // Party — both the name list (for UI) and the rich actor records
        // (HP, attributes, inventory, status effects) the gameplay systems need.
        public string[] PartyActorNames { get; private set; } = Array.Empty<string>();
        public byte ActivePartyCount { get; private set; }
        public byte[] ActivePartyIndices { get; private set; } = Array.Empty<byte>();

        /// <summary>
        /// The active-party slot bytes exactly as last set — the loaded file's, or a ChangeParty's
        /// three arguments — including the slots past the count, which only a save reads. TASK-532.
        /// </summary>
        public byte[] ActivePartySlots { get; private set; } = Array.Empty<byte>();
        public SaveGameActorData[] PartyActors { get; private set; } = Array.Empty<SaveGameActorData>();

        // The live half of the party. PartyActors is the immutable record as loaded; these are what
        // gameplay actually changes — skill advancement, damage, afflictions — and they are written
        // back on save by CollectDirtyActorEdits. Indexed by character id, parallel to PartyActors.
        // Anything that mutates a party member goes through these, never through PartyActors.
        private SaveGameActorData[] _rosterActors = Array.Empty<SaveGameActorData>();
        private SaveGameCombatData[] _combatActors = Array.Empty<SaveGameCombatData>();

        // Converted on first use and kept, so a fight does not re-allocate a roster actor's stats
        // every time the turn loop asks. Sized from the actor table; entries stay null until asked
        // for. This is also the array the test seam writes, so tests drive the real lookup.
        private ActorStat[][] _rosterStats = Array.Empty<ActorStat[]>();

        // Flattened from section 3 at load: one int per slot is cheap, where converting 1730 actors'
        // full stat blocks up front would not be.
        private int[] _creatureTypes = Array.Empty<int>();

        // Where each actor stood on the combat grid when the fight was saved. Flattened alongside
        // the creature types for the same reason: one small array beats keeping 1730 parsed records.
        private (byte X, byte Y)[] _gridPositions = Array.Empty<(byte, byte)>();

        // Encounter number -> the actor slots that encounter fields. Section 1's 700 enemy-party
        // records; the extractor has already dropped the -1 fillers.
        private Dictionary<int, List<short>> _encounterRosters = new Dictionary<int, List<short>>();
        private ActorStat[][] _actorStats = Array.Empty<ActorStat[]>();
        private ActorConditions[] _actorConditions = Array.Empty<ActorConditions>();
        private ushort[][] _actorKnownSpells = Array.Empty<ushort[]>();

        /// <summary>
        /// Attributes for an actor an encounter roster names, or null when the slot is empty.
        /// </summary>
        /// <remarks>
        /// <b>Roster slots are NOT party indices.</b> They index the save's 1730-entry actor table
        /// (section 2), while <see cref="StatsOf"/> reads the six-entry party array in section 0.
        /// Using StatsOf for a roster slot is wrong twice over: most slots fall outside it and
        /// resolve to nothing, and the few that do not hand back a PARTY MEMBER'S stats to be
        /// fielded as an enemy.
        /// </remarks>
        /// <summary>A roster actor's three known-spell words, or null when the slot has no record —
        /// what <c>cspell_check_castable</c> tests for a creature exactly as for the party.</summary>
        public ushort[] RosterKnownSpellsOf(int actorSlot) {
            SaveGameActorData saved = actorSlot >= 0 && actorSlot < _rosterActors.Length
                ? _rosterActors[actorSlot] : null;
            return saved == null ? null : new[] {
                unchecked((ushort)saved.KnownSpells1),
                unchecked((ushort)saved.KnownSpells2),
                unchecked((ushort)saved.KnownSpells3),
            };
        }

        public ActorStat[] RosterStatsOf(int actorSlot) {
            if (actorSlot < 0 || actorSlot >= _rosterStats.Length) {
                return null;
            }
            if (_rosterStats[actorSlot] != null) {
                return _rosterStats[actorSlot];
            }
            SaveGameActorData saved = actorSlot < _rosterActors.Length ? _rosterActors[actorSlot] : null;
            return saved == null ? null : (_rosterStats[actorSlot] = StatEngine.FromSaved(saved));
        }

        /// <summary>
        /// Whether a roster actor carries the <c>CAF_DEAD</c> flag — the ONE test
        /// <c>rgnenc_load_encounter_actors</c> makes before seeding a placement.
        /// </summary>
        /// <remarks>
        /// <b>The FLAG, not the health.</b> RGNENC.C:199-207 reads the actor's
        /// <c>CombatantState</c> out of TEMP.GAM and tests <c>inner.flags &amp; CAF_DEAD</c>; it never
        /// looks at a stat. Asking "is its stored health zero" instead calls every un-rolled monster
        /// dead — measured 2026-09-12, and it is why the port seeded nothing where the original
        /// seeded three slots of the party's ref-pair.
        ///
        /// <para><c>SaveGameCombatData.CombatStatus</c> is that byte: the combat record's field order
        /// is target, creatureType, gridX, gridY, destX, destY, <b>flags</b>, knockbackFrame — so the
        /// seventh field is <c>CombatantState.flags</c> under a different name.</para>
        ///
        /// <para><b>A missing record reads as ALIVE</b>, which is the opposite of the old health
        /// path's fallback and deliberate: the original makes one test and has no "cannot tell" arm,
        /// so a gap in OUR parse must not masquerade as a dead monster. The placement still has to
        /// find a template before it draws anything.</para>
        /// </remarks>
        public bool RosterActorIsFlaggedDead(int actorSlot) {
            if (actorSlot < 0 || actorSlot >= _combatActors.Length) {
                return false;
            }
            SaveGameCombatData record = _combatActors[actorSlot];
            return record != null
                && (record.CombatStatus & (int)GameData.Resources.Combat.CombatantFlags.Dead) != 0;
        }

        /// <summary>
        /// The actor slots a combat encounter fields, or null when it names none.
        /// </summary>
        /// <remarks>
        /// Keyed by the <b>encounter number</b> — what <c>HotspotService.EncounterNumberOf</c>
        /// resolves from a Comb trigger's DEF record — not by an actor slot. The values ARE actor
        /// slots, to be read with <see cref="RosterStatsOf"/>.
        ///
        /// <para>An empty roster comes back as null rather than an empty list, so a caller cannot
        /// mistake "this encounter fields nobody" for a fight with no enemies in it.</para>
        /// </remarks>
        public IReadOnlyList<short> RosterOf(int encounterNumber) =>
            _encounterRosters.TryGetValue(encounterNumber, out List<short> slots)
            && slots != null && slots.Count > 0
                ? slots
                : null;

        /// <summary>
        /// Where an actor stood on the combat grid when the fight was saved — its DESIRED tile.
        /// </summary>
        /// <remarks>
        /// Desired rather than final: two actors can share a saved tile, and terrain can block one,
        /// so the placement pass resolves it. See <c>CombatPlacement.FindTile</c>.
        /// </remarks>
        public (int X, int Y) GridPositionOf(int actorSlot) =>
            actorSlot >= 0 && actorSlot < _gridPositions.Length
                ? (_gridPositions[actorSlot].X, _gridPositions[actorSlot].Y)
                : (0, 0);

        /// <summary>
        /// The creature class of an actor the roster names — <see cref="Combatant.ClassId"/>'s value.
        /// </summary>
        /// <remarks>
        /// This is what species-keyed combat rules read: the always-acts class, the ones that vanish
        /// on death, and the conjured ones that unsummon. Section 3, parallel to section 2.
        /// </remarks>
        public int CreatureTypeOf(int actorSlot) =>
            actorSlot >= 0 && actorSlot < _creatureTypes.Length ? _creatureTypes[actorSlot] : 0;

        /// <summary>
        /// The three AI rows a roster actor was rolled — <see cref="Combatant.AiPatterns"/>.
        /// </summary>
        /// <remarks>
        /// CombatantState +15/+16/+17 (<c>aiTurnProfile</c>, <c>aiEncounterProfile</c>,
        /// <c>aiPathProfile</c>); <see cref="SaveGameCombatData"/> carries them under the older names
        /// MeleeAttackType, RangedAttackType and MovementAiType. Section 3, like the grid cell.
        /// </remarks>
        public (int Spellcast, int Crossbow, int MeleeMove)? AiPatternsOf(int actorSlot) {
            SaveGameCombatData record = actorSlot >= 0 && actorSlot < _combatActors.Length
                ? _combatActors[actorSlot]
                : null;
            return record == null
                ? ((int, int, int)?)null
                : (record.MeleeAttackType, record.RangedAttackType, record.MovementAiType);
        }

        /// <summary>Live attributes for a party member, or null when the id is outside the party.</summary>
        public ActorStat[] StatsOf(int characterIndex) =>
            characterIndex >= 0 && characterIndex < _actorStats.Length ? _actorStats[characterIndex] : null;

        /// <summary>
        /// The study bonus one rating change earns from the character sheet's emphasis marks.
        /// </summary>
        /// <remarks>
        /// <b>This is what the mark on a rating bar is FOR.</b> Clicking a rating sets a flag the
        /// sheet renders and the save carries, and until 2026-09-12 nothing read it back:
        /// <c>StatEngine.Modify</c> has always taken a <c>studyBonusPer52</c> and not one of its
        /// fourteen production callers passed one (TASK-430).
        ///
        /// <para>The rule is <see cref="SkillEmphasis.BonusFor"/>'s — <c>26 / marked</c> over 52, so
        /// a single marked rating advances 50% faster — and it is computed here rather than cached
        /// because the flags are the source of truth and a cache that misses a toggle is worse than
        /// sixteen flag reads.</para>
        /// </remarks>
        /// <param name="characterIndex">The character, as <see cref="StatsOf"/> indexes them.</param>
        /// <param name="attribute">The rating being changed.</param>
        public int StudyBonusFor(int characterIndex, GameData.ActorAttribute attribute) =>
            SkillEmphasis.BonusFor(key => GetGlobalValue(key) ?? 0, characterIndex, (int)attribute,
                isPartyMember: true);

        /// <summary>
        /// Clears Sick from every active member — what a long enough rest does.
        /// </summary>
        /// <remarks>
        /// Thirteen unbroken hours, whether that is a camp or a bought night at an inn: both rest
        /// paths reach the same threshold and apply the same full clear, so it lives here rather
        /// than once per screen.
        /// </remarks>
        public void CureSicknessAcrossParty() {
            foreach (byte characterId in ActivePartyIndices) {
                ActorConditions conditions = ConditionsOf(characterId);
                if (conditions != null) {
                    ConditionEngine.Apply(conditions, ActorCondition.Sick, -100);
                }
            }
        }

        /// <summary>
        /// The world step distance as it stood when the game last compared it — the baseline the
        /// roaming-encounter reset fires on.
        /// </summary>
        /// <remarks>
        /// <b>The RESOLVED distance, not the preference index.</b> 1600 is
        /// <c>MovementData.StepDistances[2]</c>; comparing enum values instead would make every
        /// change look like a step of one. Confirmed against the shipped saves by
        /// <c>SaveGameChangeDetectorTests</c>, which asserts each value is a member of the
        /// MOVEMENT.DAT table.
        ///
        /// <para><b>Settable, and the setter is the point.</b> The original stores the new value
        /// after every comparison, fired or not; a read-only baseline would re-fire the reset on
        /// every preferences apply for as long as the larger step size stayed selected.</para>
        /// </remarks>
        public short LastSeenStepSpeed { get; set; }

        /// <inheritdoc cref="LastSeenStepSpeed"/>
        public short LastSeenGridStride { get; set; }

        /// <summary>Live afflictions for a party member, or null when the id is outside the party.</summary>
        /// <summary>
        /// Re-derive the party-down byte from the active party's Near-death ranks —
        /// <c>STAT.C:383</c>, the tail of <c>stat_combatant_apply_condition</c>.
        /// </summary>
        /// <remarks>
        /// <b>Call this WHERE A NEAR-DEATH RANK CHANGES, never on a tick.</b> The original runs it
        /// only when condition 6 is written, and that timing is load-bearing: the pit applies its
        /// conditions, plays its own dialog, and writes 2 AFTERWARDS
        /// (<c>WORLDCRS.C:82</c>) precisely to overwrite the 1 this leaves behind. Recomputing once
        /// per frame would put the 1 straight back, and the world loop would play dialog 0x145 on
        /// top of the pit's landing dialog — the double message the 1/2 split exists to prevent.
        ///
        /// <para>It RECOMPUTES rather than latching, so healing one member back off Near-death
        /// clears it again. That is the original's behaviour and not an oversight here.</para>
        /// </remarks>
        public void RecomputePartyDeathState() {
            if (ActivePartyIndices == null) {
                return;
            }
            var ranks = new List<int>(ActivePartyIndices.Length);
            foreach (byte member in ActivePartyIndices) {
                ranks.Add(ConditionsOf(member)?[ActorCondition.NearDeath] ?? 0);
            }
            PartyDeathState = (byte)PartyDownState.Recompute(ranks);
        }

        public ActorConditions ConditionsOf(int characterIndex) =>
            characterIndex >= 0 && characterIndex < _actorConditions.Length
                ? _actorConditions[characterIndex]
                : null;

        /// <summary>
        /// The party effects a stat read has to fold in — <b>both</b> halves, in the original's
        /// order.
        /// </summary>
        /// <param name="characterIndex">The party member.</param>
        /// <param name="attribute">Which attribute is being read.</param>
        /// <param name="inCombat">
        /// Whether a fight is running. A <c>CombatOnly</c> modifier is skipped entirely outside one
        /// — <b>not applied and not expired</b>, which is why this cannot be defaulted away.
        /// </param>
        /// <returns>A hook for <see cref="StatEngine.Get"/>'s <c>applyPartyEffects</c>.</returns>
        /// <remarks>
        /// <b>The eight timed modifiers had no owner until now.</b> <c>StatEngine.Get</c> has
        /// carried the hook and named them in its own doc since the condition penalties were wired;
        /// <see cref="ActorStatModifiers"/> modelled and loaded them. Nothing joined the two, so
        /// every temporary buff and debuff on a party member did nothing at all — and because a
        /// missing modifier just means the stat reads its unmodified value, it never looked broken
        /// (TASK-251).
        ///
        /// <para><b>Modifiers first, then conditions.</b> <c>stat_actor_get</c> (STAT.C:91) walks
        /// the eight modifier slots and only then the seven condition ranks, and the condition step
        /// is multiplicative — so applying them the other way round scales a different number.</para>
        ///
        /// <para><b>Reading a stat is what expires a modifier</b>, and only a read of a matching
        /// attribute: a live slot for another stat is not even examined, so its expiry is not
        /// noticed. That is the original's behaviour, not an oversight to tidy — see
        /// <see cref="ActorStatModifiers.Affects"/>. The slot is zeroed here because something must,
        /// or the eight fill with dead entries and new modifiers start evicting live ones.</para>
        /// </remarks>
        public Func<int, int> PartyEffectsFor(int characterIndex, ActorAttribute attribute,
            bool inCombat) => value => {
            value = ApplyStatModifiers(characterIndex, attribute, value, inCombat);
            return ConditionEngine.ApplyAttributePenalties(
                value, attribute, ConditionsOf(characterIndex));
        };

        /// <summary>
        /// Folds a party member's live timed modifiers into a value, freeing any that have lapsed.
        /// </summary>
        /// <remarks>
        /// <b>Only party members have a table at all</b> — the original gates the whole loop on
        /// <c>charSlot != 0</c>, so a monster's buffs are a different mechanism and must not be
        /// forced through this one.
        /// </remarks>
        private int ApplyStatModifiers(int characterIndex, ActorAttribute attribute, int value,
            bool inCombat) {
            if (StatModifiers == null
                || characterIndex < 0
                || characterIndex >= GameData.Resources.Character.ActorStatModifiers.Characters) {
                return value;
            }

            var time = (uint)GameTimeIn2Seconds;
            for (var slot = 0;
                 slot < GameData.Resources.Character.ActorStatModifiers.SlotsPerCharacter;
                 slot++) {
                int index = GameData.Resources.Character.ActorStatModifiers.IndexOf(
                    characterIndex, slot);
                if (index < 0 || index >= StatModifiers.Length) {
                    continue;
                }
                GameData.Resources.Character.ActorStatModifiers.Slot entry = StatModifiers[index];
                if (!GameData.Resources.Character.ActorStatModifiers.Affects(entry, attribute)) {
                    continue;
                }
                value = GameData.Resources.Character.ActorStatModifiers.Apply(
                    entry, value, inCombat, time, out bool expired);
                if (expired) {
                    StatModifiers[index] = default;
                    MarkStatModifiersDirty();
                }
            }
            return value;
        }

        /// <summary>
        /// Put a spell's status effect into a party member's modifier table —
        /// <c>cspell_try_add_status_effect</c> (CSPELL.C:1053).
        /// </summary>
        /// <param name="characterIndex">
        /// The ROSTER index, which is <c>charSlot - 1</c>. <b>Not the active-party position.</b> The
        /// table is six characters wide and addressed by roster index, so using a combatant's
        /// PartySlot would write the wrong character's modifiers on any party that is not the first
        /// three in order.
        /// </param>
        /// <returns>Whether a slot was actually taken.</returns>
        /// <remarks>
        /// <b>Three rules, and each reads backwards from the obvious guess</b> — see
        /// <see cref="ActorStatModifiers"/> for the disassembly behind them:
        ///
        /// <para>It SWEEPS FIRST. Every one of the eight slots is expiry-checked before anything is
        /// decided, whatever stat it carries, which is the opposite of the read path and is what
        /// stops dead slots accumulating until they evict live ones.</para>
        ///
        /// <para>It is blocked ONLY by a non-spell modifier on the same stat, so two casts of the
        /// same debuff STACK while a potion's buff shuts the spell out entirely.</para>
        ///
        /// <para>The slot it writes is CombatOnly and PERMANENT. The original stores
        /// <c>game_time &lt;&lt; 1</c> as the expiry, which is not a duration and which nothing
        /// reads because the Expires bit is never set — spell statuses come off by clear-by-mask,
        /// not by lapsing. It is also cost 0, so it is the first thing evicted when all eight fill.
        /// </para>
        /// </remarks>
        public bool AddSpellStatusEffect(int characterIndex, ActorAttribute attribute, int value,
            bool inCombat) {
            if (StatModifiers == null || characterIndex < 0
                || characterIndex >= ActorStatModifiers.Characters) {
                return false;
            }

            var slots = new ActorStatModifiers.Slot[ActorStatModifiers.SlotsPerCharacter];
            for (var i = 0; i < slots.Length; i++) {
                slots[i] = StatModifiers[ActorStatModifiers.IndexOf(characterIndex, i)];
            }

            var time = (uint)GameTimeIn2Seconds;
            bool freed = ActorStatModifiers.SweepExpired(slots, inCombat, time) > 0;
            int statMask = 1 << (int)attribute;
            int slot = ActorStatModifiers.SpellStatusIsBlocked(slots, statMask)
                ? -1
                : ActorStatModifiers.SlotToFill(slots);
            if (slot >= 0) {
                slots[slot] = new ActorStatModifiers.Slot(ActorStatModifiers.SpellStatusFlags,
                    statMask, (short)value, time, time << 1);
            }

            if (!freed && slot < 0) {
                return false;
            }

            for (var i = 0; i < slots.Length; i++) {
                StatModifiers[ActorStatModifiers.IndexOf(characterIndex, i)] = slots[i];
            }
            MarkStatModifiersDirty();
            return slot >= 0;
        }

        /// <summary>
        /// A COPY of one character's eight modifier slots, for a caller that will mutate them.
        /// </summary>
        /// <remarks>
        /// <b>A copy, because the block is stored flat and a strided view is not an array.</b> The
        /// six characters' slots are contiguous, so this hands out the eight and
        /// <see cref="CommitStatModifierSlots"/> puts them back. The pairing is why both live here
        /// rather than letting a caller index the flat block itself — the roster/party-position
        /// mistake that indexing invites is the one thing this whole table keeps inviting.
        /// </remarks>
        public ActorStatModifiers.Slot[] StatModifierSlotsFor(int characterIndex) {
            var slots = new ActorStatModifiers.Slot[ActorStatModifiers.SlotsPerCharacter];
            if (StatModifiers == null || characterIndex < 0
                || characterIndex >= ActorStatModifiers.Characters) {
                return slots;
            }

            for (var i = 0; i < slots.Length; i++) {
                slots[i] = StatModifiers[ActorStatModifiers.IndexOf(characterIndex, i)];
            }
            return slots;
        }

        /// <summary>Write a character's eight slots back, if anything about them changed.</summary>
        /// <remarks>
        /// Marks the block dirty only when something actually differs, so an item use that changed
        /// no modifier does not force a save-back.
        /// </remarks>
        public void CommitStatModifierSlots(int characterIndex, ActorStatModifiers.Slot[] slots) {
            if (StatModifiers == null || slots == null || characterIndex < 0
                || characterIndex >= ActorStatModifiers.Characters) {
                return;
            }

            var changed = false;
            for (var i = 0; i < slots.Length && i < ActorStatModifiers.SlotsPerCharacter; i++) {
                int index = ActorStatModifiers.IndexOf(characterIndex, i);
                ActorStatModifiers.Slot was = StatModifiers[index];
                if (was.Flags == slots[i].Flags && was.StatMask == slots[i].StatMask
                    && was.Value == slots[i].Value && was.ExpiresAt == slots[i].ExpiresAt) {
                    continue;
                }
                StatModifiers[index] = slots[i];
                changed = true;
            }

            if (changed) {
                MarkStatModifiersDirty();
            }
        }

        /// <summary>Whether a modifier slot has been freed since the block was last written.</summary>
        /// <remarks>The save writes the block back from <see cref="StatModifiers"/>, so an expiry
        /// that is not recorded reappears on the next load and expires again forever.</remarks>
        public bool StatModifiersDirty { get; private set; }

        private void MarkStatModifiersDirty() => StatModifiersDirty = true;

        /// <summary>
        /// Live known-spell words for a party member, or null when the id is outside the party.
        /// Three 16-bit words; use <see cref="SpellBook"/> to read or set a bit rather than doing
        /// the arithmetic at the call site.
        /// </summary>
        public ushort[] KnownSpellsOf(int characterIndex) =>
            characterIndex >= 0 && characterIndex < _actorKnownSpells.Length
                ? _actorKnownSpells[characterIndex]
                : null;

        /// <summary>How many party members have live state (the save's six records).</summary>
        public int RuntimeActorCount => _actorStats.Length;

        // Zone containers (chests, corpses, graves, …). Surfaced from the loaded save so the
        // world-interaction system can resolve GetContainerAtLocation faithfully. Previously
        // dropped on hydration.
        public SaveGameZoneContainerStateData ZoneContainers { get; private set; }
            = new SaveGameZoneContainerStateData(Array.Empty<SaveGameZoneContainerEntryData>());

        // Static item-definition catalog, indexable by object id. Chapter/save-independent, so it's
        // loaded once by GameStateLoader alongside session hydration rather than parsed out of the
        // save itself. Null until that load completes.
        //
        // *** THE NUMERIC VIEW OF THE MERGED CATALOG, NOT A DIRECT READ OF OBJINFO.DAT. *** The
        // archive's 138 records are the priority-0 source and nothing more; ItemCatalog folds them
        // together with whatever later sources a mod supplies. This projection is what every
        // gameplay caller uses, and for a base-only merge it is byte-identical to the old direct
        // load — that identity is the design's no-regression invariant (ObjectInfoRegistryTests).
        public ObjectInfoSet ObjectInfo { get; private set; }

        // The string-keyed merged catalog the above is projected from. Overrides and provenance
        // live here; so does any item a mod ADDS, which by definition has no numeric slot and so
        // cannot appear in the projection. Callers that only look items up by object id want
        // ObjectInfo; anything reasoning about WHERE an item came from wants this.
        public ObjectInfoCatalog ItemCatalog { get; private set; }

        // Mutable runtime layer, built once from ZoneContainers at hydration time (see
        // BuildRuntimeContainers). Parallel to the immutable ZoneContainers snapshot — looting
        // mutates these, not the source save data. Keyed by exact world location and by owning
        // actor (for the Inventory-type containers that represent a party member's pack).
        // A LIST per location, in record order: one spot can carry several records told apart only
        // by chapter band (zone 7's stump at (803640,917246) is rations in chapter 7 and Coltari
        // Poison in every other), and a single slot kept whichever came last.
        private readonly Dictionary<(int Zone, int X, int Y), List<RuntimeContainer>> _runtimeContainersByLocation
            = new Dictionary<(int, int, int), List<RuntimeContainer>>();
        private readonly Dictionary<int, RuntimeContainer> _runtimeContainersByActor
            = new Dictionary<int, RuntimeContainer>();

        // Pairing from a built RuntimeContainer back to its source zone entry + index-in-zone, so
        // CollectDirtyContainerEdits can recompute the container's absolute save-body offset via
        // ContainerGeometry.ContainerBodyOffset. Rebuilt alongside the runtime layer.
        private readonly List<(RuntimeContainer Runtime, SaveGameZoneContainerEntryData ZoneEntry, int IndexInZone)>
            _runtimeContainerRecords = new List<(RuntimeContainer, SaveGameZoneContainerEntryData, int)>();

        /// <summary>
        /// Attach the loaded item-definition catalog. Called by <see cref="Services.IGameStateLoader"/>
        /// once OBJINFO.DAT has resolved; independent of <see cref="Initialize"/> since it isn't part
        /// of the save body.
        /// </summary>
        public void SetObjectInfo(ObjectInfoSet objectInfo) {
            if (objectInfo == null) {
                throw new ArgumentNullException(nameof(objectInfo));
            }

            SetItemCatalog(CatalogOf(objectInfo), objectInfo.SpellPrices);
        }

        /// <summary>
        /// Attach a merged item catalog directly — the path a mod-aware loader takes.
        /// </summary>
        /// <remarks>
        /// <see cref="ObjectInfo"/> is projected from it rather than stored separately, so the two
        /// cannot drift: there is one catalog and one view of it.
        /// </remarks>
        public void SetItemCatalog(ObjectInfoCatalog catalog,
                                   IReadOnlyList<int> spellPrices = null) {
            ItemCatalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _spellPrices = spellPrices ?? _spellPrices;
            ObjectInfo = NumericViewOf(catalog, _spellPrices);
        }

        // *** OBJINFO.DAT's SCROLL PRICE TABLE HAS TO BE CARRIED ACROSS THE PROJECTION. *** It is
        // not a per-record field, so it cannot come through catalog.Merged.Entries the way every
        // ObjectInfo does. Dropping it leaves NumericViewOf's set with an empty table and the shop
        // quotes every Magical Scroll at zero -- which is what it did until this was plumbed.
        private IReadOnlyList<int> _spellPrices = Array.Empty<int>();

        /// <summary>
        /// Fold an ordered source list into an item catalog. The base archive is entry zero and
        /// carries no privilege — a mod is simply a later source, and a total conversion drops the
        /// base one.
        /// </summary>
        public static ObjectInfoCatalog CatalogOf(ObjectInfoSet archive) =>
            CatalogOf(new List<IContentSource<ObjectInfo>> { new ObjectInfoContentSource(archive) });

        /// <inheritdoc cref="CatalogOf(ObjectInfoSet)"/>
        public static ObjectInfoCatalog CatalogOf(IReadOnlyList<IContentSource<ObjectInfo>> sources) =>
            new ObjectInfoCatalog(ContentRegistry.Merge(sources));

        /// <summary>
        /// The by-object-id view of a merged catalog.
        /// </summary>
        /// <remarks>
        /// <b>Mod-ADDED items are dropped here, and that is the documented ceiling of the numeric
        /// surface rather than a shortcut.</b> An added item has no numeric slot to occupy —
        /// ObjectInfoCatalog says so itself — so it is reachable only by string key on
        /// <see cref="ItemCatalog"/>. Overrides of EXISTING items, which is what a mod does far more
        /// often, come through in full because they keep the number they replace.
        ///
        /// <para>Projecting rather than changing every signature to take the catalog is deliberate:
        /// the twenty-odd rules in GameData that take an <see cref="ObjectInfoSet"/> use nothing but
        /// <see cref="ObjectInfoSet.GetById"/>, so widening their type would buy them nothing and
        /// tie pure inventory logic to the registry.</para>
        /// </remarks>
        private static ObjectInfoSet NumericViewOf(ObjectInfoCatalog catalog,
                                                   IReadOnlyList<int> spellPrices) {
            var items = new List<ObjectInfo>();
            foreach (KeyValuePair<string, ObjectInfo> entry in catalog.Merged.Entries) {
                if (ContentKey.TryParseBase(entry.Key, ObjectInfoContentSource.Catalog, out _)) {
                    items.Add(entry.Value);
                }
            }

            return new ObjectInfoSet(ObjectInfoContentSource.Catalog, items, spellPrices);
        }

        // The subjects a dialog can be "about". The engine reads these straight out of dseg — only
        // the primary one has a 300xx accessor (30004) — so they are surfaced individually here for
        // the text-variable kinds that name them (11, 12, 17, 18). TEMP.GAM +0x0598 / +0x059A /
        // +0x05A0 / +0x05A2, all already parsed into SaveGameMiscStateData.
        /// <summary>
        /// The cast screen's sticky pair — which party slot was casting and which school was
        /// showing when it last closed.
        /// </summary>
        /// <remarks>
        /// <b>Already parsed, under two names that do not say so.</b> These are
        /// <c>g_gameState.nSpellMenuCasterSlot</c> and <c>nSpellMenuPreselect</c> at body offsets
        /// 1622/1624, and the parser files them on <c>LightingStateData</c> as
        /// <c>PartyMember</c>/<c>LastSpellSymbolFile</c> — that block starts at the timer pool's end
        /// and its first three words are not lighting state at all (see
        /// <see cref="SaveGameLightingStateData.ActiveSpellTimerFlags"/>). Nothing read them until
        /// now, which is why the cast screen always opened on the default.
        ///
        /// <para>Measured across the shipped saves: the played ones carry slot 1 / school 4 —
        /// school 4 is SYMBOL5, which is why the original opens naming "Scent of Sarig" — and
        /// <c>ggg.G01/SAVE01</c> carries the -1/-1 sentinel that
        /// <c>savegame_chapter_start_dispatch</c> writes at the start of every chapter.</para>
        /// </remarks>
        public int CastMenuCasterSlot { get; set; } =
            GameData.Resources.Spells.CastMenuSelection.None;

        /// <inheritdoc cref="CastMenuCasterSlot"/>
        public int CastMenuSchool { get; set; } =
            GameData.Resources.Spells.CastMenuSelection.None;

        public int DialogSecondaryActorId =>
            _dialogSecondaryActorOverride ?? _state?.MiscStateData.DialogSecondaryActorNumber ?? 0;
        public int DialogTertiaryActorId =>
            _dialogTertiaryActorOverride ?? _state?.MiscStateData.DialogTertiaryActorNumber ?? 0;
        public int DialogCreatureType => _dialogCreatureTypeOverride ?? _state?.MiscStateData.CreatureType ?? 0;
        public int DialogKeyObjectId => _dialogKeyObjectIdOverride ?? _state?.MiscStateData.KeyObjectId ?? 0;

        // The live value of the engine's nEvtArgItemId, which several screens write just before
        // playing a record that names an object ("@ shoved the @1 into a spare bag"). It is a dseg
        // global in the original, so it shadows whatever the save was hydrated with.
        private int? _dialogKeyObjectIdOverride;

        // nEvtArgActor1, the same shape: a dseg global whichever screen is about to play a line
        // stamps, so it shadows the save's value for the rest of the session.
        private int? _dialogSecondaryActorOverride;

        // nEvtArgStat, which slot kind 12 names (DIALOG.C:486-488). Its only writer is
        // stat_party_find_extreme (STAT.C:325/335), so PartyExtreme stamps it: the scout who spotted
        // an ambush, the best lockpick. Read from the save alone, the chapter-3 scouting warning
        // (dialog 41) named Locklear -- roster 0, a save's zero -- to a party he was not in.
        private int? _dialogTertiaryActorOverride;

        // The same shape for nEvtArgAux1, the creature a following record is about. Without a
        // writer every line naming a monster showed whatever the save was hydrated with -- seen
        // live as "grossly misjudging some of the INVALID MONSTER's abilities" when inspecting.
        private int? _dialogCreatureTypeOverride;

        /// <summary>
        /// Whether the establishment whose screen is open also sells beds — the engine's
        /// <c>shopOrTavern</c>.
        /// </summary>
        /// <remarks>
        /// <b>It decides one word.</b> A dialog that names the person behind the counter says
        /// "tavernkeeper" when this is set and "shopkeeper" when it is not (slot kind 28, seven
        /// shipped lines). With no writer it always said shopkeeper — the slot was filled, just with
        /// the wrong one of two words, which is the kind of wrong that reads as fine.
        ///
        /// <para><b>Its lifetime is the open screen, not the location.</b> The original clears it
        /// and recomputes it as part of the shop screen's setup (<c>sub_ovr157_0</c> @0x5425c), so a
        /// screen opened on anything else must clear it rather than leave the last tavern's answer
        /// standing. The rule for computing it is <c>DialogSlotContext.RunsAnInn</c>.</para>
        /// </remarks>
        public bool OpenShopRunsAnInn { get; set; }

        /// <summary>Set the object a following dialog is "about" — <c>g_gameState.nEvtArgItemId</c>,
        /// which the object-name text variable resolves through. Discarding an item writes it first
        /// thing (CMBINV.C:1051), the same way <c>UI_showItem</c> does for a description.</summary>
        /// <summary>
        /// Whether follow-road ("travel") mode is engaged — REQ_MAIN's ActionId 19, body offset 50.
        /// </summary>
        /// <remarks>
        /// <b>The session owns it because the save does.</b> <see cref="World.PartyMovement"/> seeds
        /// its own <c>IsTravelling</c> from this on wire-up and writes back through it when the
        /// player toggles, so the flag survives a save/load the way the original's does — and a save
        /// written here is one the original will honour.
        /// </remarks>
        public bool IsAutoTravelling { get; set; }

        /// <summary>
        /// Steps taken inside the current 1600-unit movement cell — <c>nWorldStepTickCount</c>,
        /// body offset 52.
        /// </summary>
        /// <remarks>
        /// Saved state, like <see cref="IsAutoTravelling"/> above and for the same reason: the pair
        /// it forms with <see cref="TileBoundaryCrossed"/> decides whether an avoidable encounter is
        /// evaluated on a given step, so dropping it on a save hands the party a roll they had
        /// already spent. See <see cref="GameData.Resources.World.WorldStepTick"/>.
        /// </remarks>
        public int SubTileStepCount { get; set; }

        /// <summary>
        /// The cell-boundary flag — <c>world_step_tick</c>, body offset 53.
        /// </summary>
        /// <remarks>
        /// <b>1 means "this step completed a movement cell"</b>, which at the Small step preset is
        /// one press in four. <c>hotspotevt_activate_at_player</c> returns outright when it reads 0,
        /// so a 0 is not "no roll" but "this encounter does not happen this step at all".
        ///
        /// <para>Starts at <see cref="GameData.Resources.World.WorldStepTick.OnBoundary"/> so a
        /// session that has never stepped — a new game, or a load into a zone whose ids agree —
        /// behaves like the original's first step rather than swallowing it.</para>
        /// </remarks>
        public int TileBoundaryCrossed { get; set; } =
            GameData.Resources.World.WorldStepTick.OnBoundary;

        public void SetDialogKeyObjectId(int objectId) => _dialogKeyObjectIdOverride = objectId;

        /// <summary>Set the actor a following dialog names through slot kind 11 —
        /// <c>g_gameState.nEvtArgActor1</c>. Dragging an item over a portrait writes it
        /// (INVENTOR.C:682), which is who "@0 waved off the @1" refuses on behalf of.</summary>
        public void SetDialogSecondaryActorId(int characterIndex) =>
            _dialogSecondaryActorOverride = characterIndex;

        /// <summary>Set the creature a following dialog is "about" — <c>g_gameState.nEvtArgAux1</c>,
        /// which the creature-name text variable (slot kind 17) resolves through MNAMES.DAT.</summary>
        /// <remarks>
        /// <b>Written by whatever is about to play the line, exactly as the object id is.</b> The
        /// arena's Inspect sets it from the target's creature type before
        /// <see cref="GameData.Resources.Combat.CombatAssessment.OpeningDialog"/>
        /// (<c>combatenc_anim_actor_stat_rolls</c> does the same). It shadows the save's value for
        /// the rest of the session, which is what a dseg global does.
        /// </remarks>
        public void SetDialogCreatureType(int creatureType) =>
            _dialogCreatureTypeOverride = creatureType;

        /// <summary>Creature names (MNAMES.DAT), indexed by creature id. Static catalog data like
        /// <see cref="ObjectInfo"/>; dialog text-variable kind 17 names the creature a dialog is
        /// about from it. Null until <see cref="Services.IGameStateLoader"/> resolves it — and it is
        /// allowed to stay null, so callers must cope.</summary>
        public GameData.Resources.Creature.CreatureNames CreatureNames { get; private set; }

        public void SetCreatureNames(GameData.Resources.Creature.CreatureNames names) {
            CreatureNames = names;
        }

        /// <summary>A creature's name by id, or empty when the catalog is absent or the id unknown.
        /// Linear over 64 entries, called once per dialog token — indexing it would cost more than
        /// it saves.</summary>
        public string CreatureNameOf(int creatureId) {
            List<GameData.Resources.Creature.CreatureName> all = CreatureNames?.Creatures;
            if (all == null) {
                return "";
            }
            foreach (GameData.Resources.Creature.CreatureName c in all) {
                if (c.Number == creatureId) {
                    return c.Name ?? "";
                }
            }
            return "";
        }

        /// <summary>
        /// Raised when the active-party roster changes (members added/removed/reordered) —
        /// NOT when a member's stats change (that will be a future PartyStatsChanged event).
        /// Discrete-state signal: consumers react instead of polling. Raised on Initialize;
        /// future roster mutators (and Clear) should raise it too.
        /// </summary>
        public event Action PartyCompositionChanged;

        /// <summary>
        /// Replace the entire session with the contents of <paramref name="saveGame"/>.
        /// Throws if the save has no parsed <see cref="SaveGameData.StateData"/>.
        /// </summary>
        public void Initialize(SaveGame saveGame, GameSessionSource source) {
            if (saveGame == null) {
                throw new ArgumentNullException(nameof(saveGame));
            }

            SaveGameStateData state = saveGame.Data?.StateData
                ?? throw new ArgumentException(
                    "SaveGame has no parsed StateData; cannot hydrate session.",
                    nameof(saveGame));

            _state = state;
            // The change-detector baseline, taken as MUTABLE session state rather than read
            // through _state on demand: the roaming-encounter reset stores a new one whether or
            // not it fired, and _state is the immutable parse of the save as loaded.
            LastSeenStepSpeed = state.LastSeenStepSpeed;
            LastSeenGridStride = state.LastSeenGridStride;
            // Sections 2 and 3 — the FULL 1730-slot actor and combat tables. Distinct from
            // StateData's PartyActors, which is only the six party members; encounter rosters index
            // these, not that. See RosterStatsOf.
            _rosterActors = saveGame.Data?.ActorStateData ?? Array.Empty<SaveGameActorData>();
            _combatActors = saveGame.Data?.CombatStateData ?? Array.Empty<SaveGameCombatData>();
            _encounterRosters = saveGame.Data?.WorldStateData?.EnemyPartyActorSlots
                ?? new Dictionary<int, List<short>>();
            _rosterStats = new ActorStat[_rosterActors.Length][];

            // *** THE STAGED ENEMY EDITS BELONG TO THE SAVE BEING REPLACED. *** Unlike the party's,
            // which CollectDirtyActorEdits recomputes from _actorStats at save time, these ACCUMULATE
            // in a dictionary that nothing emptied — so an enemy killed before a load was still
            // staged after it, and the next save wrote that kill into a file whose own timeline had
            // never fought them. The victims come back at zero health with no CAF_DEAD, which
            // EnemiesAlive counts as living, and the encounter can then never be won or left
            // (TASK-619; the symptom is TASK-618's).
            _rosterActorEdits = null;

            _creatureTypes = new int[_combatActors.Length];
            _gridPositions = new (byte, byte)[_combatActors.Length];
            for (int i = 0; i < _combatActors.Length; i++) {
                _creatureTypes[i] = _combatActors[i]?.CreatureType ?? 0;
                _gridPositions[i] = ((byte)(_combatActors[i]?.XOnGrid ?? 0),
                    (byte)(_combatActors[i]?.YOnGrid ?? 0));
            }
            Source = source;
            _backingBody = saveGame.TempGameData ?? Array.Empty<byte>();
            // Marks the party already walked past in a previous session, so re-entering a dungeon
            // shows the map it left with. A short/absent body resets the table rather than reading
            // rubbish, which is also what a brand-new game wants.
            AutomapVisits.Load(_backingBody);
            // Same treatment, same reason: a short/absent body clears the block rather than reading
            // rubbish, and a new game genuinely has removed nothing (STARTUP.GAM's is all zero).
            EncounterActorStates.Load(_backingBody);
            EncounterFoughtTimes.Load(_backingBody);
            ChapterFinishingGold.Load(_backingBody);
            // Same treatment again. Load returns an all-empty block for a short or absent body, so
            // a new game starts with no modifiers rather than with rubbish read off the end.
            StatModifiers = GameData.Resources.Character.ActorStatModifiers.Load(_backingBody);
            _headerWorldX = saveGame.WorldX;
            _headerWorldY = saveGame.WorldY;
            _headerMapIcon = saveGame.MapIcon;

            MapIcon = saveGame.MapIcon;

            // Loaded saves carry the world-map marker in their header; new games leave this as
            // the (template) STARTUP.GAM value, which ApplyChapterStart overwrites afterwards.
            FullMapIcon marker = saveGame.FullMapMarker;
            MapMarkerVisible = marker?.Visible ?? false;
            MapMarkerXPercent = marker?.XPercent ?? 0f;
            MapMarkerYPercent = marker?.YPercent ?? 0f;
            MapMarkerIcon = marker?.IconIndex ?? 0;

            Chapter = state.ChapterNumber;
            PartyGold = state.PartyGold;
            GameTimeIn2Seconds = state.GameTimeIn2Seconds;
            LastRestTicks = state.TimeSnapshot;
            // wPalEventMask, which the parser reads under the lighting block's
            // ActiveSpellTimerFlags. Without this a save restored while a palette spell is running
            // comes back with the timer live and the mask clear, so the effects strip shows nothing
            // and PuzzleScreen reads the spell as inactive — until the clock next moves and the
            // tick rebuilds it, which on a stationary party is a long time.
            PaletteEventMask = state.LightingStateData?.ActiveSpellTimerFlags ?? 0;
            CastMenuCasterSlot = state.LightingStateData?.PartyMember
                ?? GameData.Resources.Spells.CastMenuSelection.None;
            CastMenuSchool = state.LightingStateData?.LastSpellSymbolFile
                ?? GameData.Resources.Spells.CastMenuSelection.None;
            PartyDeathState = state.PartyDeathState;
            ChapterTransitionPending = state.ChapterTransitionPending;
            PreviousZone = state.PreviousZoneNumber;
            CurrentZone = state.CurrentZoneNumber;
            // WorldX/Y are derived from the position below, so the save's own bytes are not read
            // back. That is deliberate: a save whose recorded tile disagrees with its position
            // (29 of this port's 36 chapter-1 saves, before the tile became derived) loads at the
            // tile its position actually names rather than the stale one it stored.
            PositionX = state.PositionX;
            PositionY = state.PositionY;
            PositionZ = state.PositionZ;
            Rotation = state.CurrentZRotation;

            // *** THE MAP ZOOM, AND IT MUST SET MapCameraZone TOO. *** MapCameraZ is reseeded from
            // the zone default whenever MapCameraZone disagrees with CurrentZone (WorldRuntime),
            // which is the original's own condition. Restoring the height without also claiming the
            // zone would let that reseed fire on the next world build and throw the loaded zoom
            // away — so the two are set together or not at all.
            MapCameraZ = state.MovementData?.MapCameraZ ?? 0;
            MapCameraZone = MapCameraZ != 0 ? CurrentZone : -1;

            // *** FOLLOW-ROAD IS SAVED STATE, AND IT GATES MOVEMENT. *** Parsed since the extractor
            // was written and read by nothing until 2026-09-12, which is what made the port and the
            // original disagree about where the party may walk: four of the shipped saves carry a 1,
            // the original then refuses every step that would leave the road, and the port — having
            // dropped the flag — walked anywhere. TASK-422.
            IsAutoTravelling = (state.MovementData?.IsAutoTraveling ?? 0) != 0;

            // *** THE CELL COUNTER AND ITS BOUNDARY FLAG. *** Parsed since the extractor was written
            // and read by nothing until now, exactly like follow-road above. They are restored
            // rather than reset because the original saves them: a party half-way across a cell
            // resumes half-way across it, and one that had just spent its encounter roll does not
            // get a second one for reloading.
            SubTileStepCount = state.MovementData?.SubTileStepCount ?? 0;
            TileBoundaryCrossed = state.MovementData?.TileBoundaryCrossed
                ?? GameData.Resources.World.WorldStepTick.OnBoundary;

            PartyActorNames = state.ActorNames ?? Array.Empty<string>();
            // *** THE COUNT BOUNDS THE ARRAY ON THIS PATH TOO. *** The saved roster is a fixed
            // three bytes and its tail is stale — the original bounds every walk of
            // activePartyCharacters by numberOfActivePartyCharacters. Taking the array whole gave
            // chapter 2, which is a party of two, a THIRD member: the byte chapter 1 left behind.
            // He drew a combat turn, took damage and ate rations of his own, and the only tell was
            // the roster printing the same character twice. SetActiveParty has always trimmed;
            // going through it is what makes the two writers agree.
            SetActiveParty(
                state.PartyConfigurationData.NumberOfActivePartyCharacters,
                state.PartyConfigurationData.ActivePartyCharacters);
            PartyActors = state.PartyActors ?? Array.Empty<SaveGameActorData>();
            HydrateRuntimeActors(state);

            ZoneContainers = saveGame.Data?.ZoneContainerStateData
                ?? new SaveGameZoneContainerStateData(Array.Empty<SaveGameZoneContainerEntryData>());
            BuildRuntimeContainers();

            // *** THE RUNTIME OVERLAY SHADOWS THE SAVE, SO HYDRATING HAS TO DROP IT. ***
            // Every SetGlobalFlag/SetGlobalValue writes into _flagOverlay, and GetGlobalValue reads
            // the overlay FIRST — so without this, loading a save keeps every flag the abandoned
            // game set and answers it over the file's own value. Only Clear() dropped it, and that
            // runs on the way back to the main menu; LoadGameMenu -> GameFlow.LoadSave ->
            // GameStateLoader -> here never passes through it, which is the ordinary in-game
            // "Restore" a player clicks.
            //
            // Measured 2026-09-13 with the same save loaded twice: walking onto Northwarden's Town
            // trigger set ScoutTried(0) (global 5200), and every LATER load of a save carrying 5200
            // clear still read it as 1 — so hotspotevt_available refused the town and the party
            // walked past the gate. Nulling the overlay here made the very next step open it again.
            //
            // The two dialog overrides and EventActor go with it for the same reason: each is
            // conversation- or event-scoped state that answers OVER something the save carries, and
            // a load ends whatever conversation set them.
            _flagOverlay = null;
            _dialogKeyObjectIdOverride = null;
            _dialogSecondaryActorOverride = null;
            _dialogTertiaryActorOverride = null;
            DialogsPlaying = 0;   // no play survives a load or a clear
            EventActor = -1;

            IsActive = true;
            PartyCompositionChanged?.Invoke();
        }

        /// <summary>
        /// Build the mutable <see cref="RuntimeContainer"/> layer from the just-hydrated
        /// <see cref="ZoneContainers"/> snapshot. Runs once per hydration; the immutable
        /// snapshot is left untouched (see <see cref="GetContainerAt"/>).
        /// </summary>
        private void BuildRuntimeContainers() {
            _runtimeContainersByLocation.Clear();
            _runtimeContainersByActor.Clear();
            _runtimeContainerRecords.Clear();

            foreach (SaveGameZoneContainerEntryData entry in ZoneContainers.Zones) {
                for (int index = 0; index < entry.Containers.Length; index++) {
                    SaveGameContainerData snap = entry.Containers[index];
                    RuntimeContainer rc = RuntimeContainer.FromSnapshot(snap);
                    // Any content change now stamps the container's last-touch time, so the
                    // ground-bag recycler orders by genuine last use rather than by claim age.
                    // A no-op for records without a timestamp sub-record, i.e. everything but the
                    // self-spawning bags the recycler actually considers.
                    rc.TouchClock = () => (int)GameTimeIn2Seconds;
                    _runtimeContainerRecords.Add((rc, entry, index));

                    // Free ground-bag pool records all park at zone 255 / (0,0), so indexing them
                    // by location would collapse every one of them onto a single key and lose all
                    // but the last. They are reachable through the per-zone record scan instead
                    // (see PoolRecordsInZone), and enter this index when a discard claims one.
                    if (rc.ContainerType != SaveGameContainerType.Free) {
                        var key = (Zone: (int)entry.ZoneNumber, X: snap.Location.X, Y: snap.Location.Y);
                        if (!_runtimeContainersByLocation.TryGetValue(key, out List<RuntimeContainer> here)) {
                            _runtimeContainersByLocation[key] = here = new List<RuntimeContainer>();
                        }
                        here.Add(rc);
                    }

                    // Keyed by CHARACTER index, not by the stored actor number — the stored number
                    // is 1-based (SaveGameContainerData.OwnerCharacterIndex carries the evidence).
                    // Keying by the raw number gave every member the pack of the member before them
                    // and left Locklear, actor 1, with none at all.
                    if (snap.IsActorInventoryContainer && snap.OwnerCharacterIndex.HasValue) {
                        int owner = snap.OwnerCharacterIndex.Value;
                        _runtimeContainersByActor[owner] = rc;
                        // What a character carries decides six attributes' modifiers, so every change
                        // to a pack rebuilds them — cmbinv_actor_pickup_item does it for each item that
                        // lands in a party pack, and the inventory screen on each member redraw (TASK-510).
                        rc.ContentChanged = () => RecalculateItemModifiers(owner);
                    }
                }
            }
        }

        /// <summary>
        /// Live-inventory save-writer input: for each looted (<see cref="RuntimeContainer.Dirty"/>)
        /// container, recompute its absolute save-body offset (<see cref="ResourceExtraction.ContainerGeometry"/>)
        /// against the immutable <see cref="SaveGameContainerData"/> snapshot geometry, and pack its
        /// live item bytes. Everything else about the container (capacity, dataTypes, and hence its
        /// serialized size) is unchanged by looting, so an in-place patch is exact — see
        /// <see cref="ResourceExtraction.ContainerGeometry"/>.
        /// </summary>
        /// <summary>
        /// Build the live attribute/affliction slots from the loaded save. Both halves come from the
        /// same record set the immutable <see cref="PartyActors"/> does, so they start identical and
        /// diverge only as gameplay changes them.
        /// </summary>
        private void HydrateRuntimeActors(SaveGameStateData state) {
            SaveGameActorData[] actors = PartyActors;
            SaveGameActorStatusEffectsData[] effects =
                state?.PartyConfigurationData?.ActorStatusEffects ?? Array.Empty<SaveGameActorStatusEffectsData>();

            _actorStats = new ActorStat[actors.Length][];
            _actorConditions = new ActorConditions[actors.Length];
            _actorKnownSpells = new ushort[actors.Length][];
            for (int i = 0; i < actors.Length; i++) {
                _actorStats[i] = StatEngine.FromSaved(actors[i]);
                _actorKnownSpells[i] = new[] {
                    unchecked((ushort)actors[i].KnownSpells1),
                    unchecked((ushort)actors[i].KnownSpells2),
                    unchecked((ushort)actors[i].KnownSpells3),
                };
                _actorConditions[i] = i < effects.Length
                    ? new ActorConditions(effects[i])
                    : new ActorConditions();
                int character = i;
                _actorConditions[i].EventRaised =
                    (condition, caught) => RecordConditionEvent(character, condition, caught);
                _actorConditions[i].NearDeathChanged = RecomputePartyDeathState;
            }
            // SAVEGAME.C:244 clears the byte on load, so a flag saved mid-hour is never announced.
            PartyDirtyFlags = 0;
        }

        /// <summary>
        /// <c>g_gameState.bPartyDirtyFlags</c>. <see cref="ConditionAnnouncements.DirtyBit"/> means a
        /// member caught an affliction the hourly tick has not announced yet.
        /// </summary>
        public int PartyDirtyFlags { get; set; }

        /// <summary>
        /// Apply an attribute change to a party member, and record it the way the original does.
        /// </summary>
        /// <remarks>
        /// <b>The bookkeeping is the point; the arithmetic is <see cref="StatEngine.Modify"/>'s.</b>
        /// <c>stat_combatant_modify</c> does the two writes at STAT.C:300-307 <i>inside itself</i>,
        /// so in the original no caller can forget them:
        /// <list type="bullet">
        /// <item>the attribute's "changed since you last looked" mark, on
        /// <see cref="StatEngine.StatChange.SignalsImprovement"/> — any change to a skill, but only
        /// a GAIN for Health or Stamina;</item>
        /// <item><c>bPartyDirtyFlags |= 1</c> on any increase.</item>
        /// </list>
        ///
        /// <para>The port returns that signal instead of acting on it, and until 2026-09-21 all
        /// thirteen call sites threw it away — so <see cref="CharacterSheetView"/>'s read-and-clear
        /// could only ever read 0 and no rating was ever highlighted (TASK-611). This exists so a
        /// caller that has a character index has one call that cannot get it wrong; it is the
        /// sibling of <see cref="RecordConditionEvent"/>, which does the same job for afflictions.
        /// </para>
        ///
        /// <para><b>Monsters are excluded by construction</b> — <c>charSlot != 0</c> in the
        /// original. This takes a character index, so there is no monster to exclude.</para>
        /// </remarks>
        public StatEngine.StatChange ModifyStatOf(int characterIndex, ActorAttribute attribute,
            long delta, StatChangeMode mode = StatChangeMode.Absolute, int studyBonusPer52 = 0) {
            ActorStat[] stats = StatsOf(characterIndex);
            ActorStat stat = stats != null && (int)attribute >= 0 && (int)attribute < stats.Length
                ? stats[(int)attribute]
                : null;
            if (stat == null) {
                return default;
            }
            StatEngine.StatChange change =
                StatEngine.Modify(stat, attribute, delta, mode, studyBonusPer52);
            RecordStatChange(characterIndex, attribute, change);
            return change;
        }

        /// <summary>
        /// The two writes <c>stat_combatant_modify</c> makes after a change (STAT.C:300-307), for a
        /// caller that already did the change itself.
        /// </summary>
        /// <remarks>
        /// Prefer <see cref="ModifyStatOf"/>, which cannot be called without doing this. This
        /// overload exists for the call sites that reach <see cref="StatEngine.Modify"/> directly
        /// because they hold the <see cref="ActorStat"/> rather than a character index.
        /// </remarks>
        public void RecordStatChange(int characterIndex, ActorAttribute attribute,
            StatEngine.StatChange change) {
            if (change.SignalsImprovement) {
                SetGlobalFlag(
                    CharacterSheetRow.ChangedFlagFor(characterIndex, (int)attribute), true);
            }
            if (change.Increased) {
                PartyDirtyFlags |= CharacterSheetRow.ImprovedDirtyBit;
            }
        }

        // stat_combatant_apply_condition's write (STAT.C:374-380): the member's CONDITION global
        // follows the catch both ways, and only a catch marks the party dirty.
        private void RecordConditionEvent(int character, ActorCondition condition, bool caught) {
            SetGlobalFlag(ConditionAnnouncements.FlagFor(character, condition), caught);
            if (caught) {
                PartyDirtyFlags |= ConditionAnnouncements.DirtyBit;
            }
        }

        /// <summary>
        /// The live party state, for <see cref="SaveGameWriter"/> to patch back over the saved
        /// records. Every member is written every time: unlike containers there is no dirty flag on
        /// an attribute, and writing an unchanged record is a no-op against the backing body.
        /// </summary>
        public IReadOnlyList<DirtyActorEdit> CollectDirtyActorEdits() {
            var edits = new List<DirtyActorEdit>(_actorStats.Length);
            for (int i = 0; i < _actorStats.Length; i++) {
                edits.Add(new DirtyActorEdit(i, _actorStats[i], _actorConditions[i],
                    i < _actorKnownSpells.Length ? _actorKnownSpells[i] : null));
            }
            return edits;
        }

        public IReadOnlyList<DirtyContainerEdit> CollectDirtyContainerEdits() {
            var edits = new List<DirtyContainerEdit>();
            foreach (var rec in _runtimeContainerRecords) {
                if (!rec.Runtime.Dirty && !rec.Runtime.HeaderDirty) {
                    continue;
                }


                int zoneLocalOffset = NormalizeZoneLocalOffset(rec.ZoneEntry.TempGamFileOffset);
                int bodyOffset = ResourceExtraction.ContainerGeometry.ContainerBodyOffset(
                    zoneLocalOffset, rec.ZoneEntry.Containers, rec.IndexInZone);

                List<RuntimeItem> items = rec.Runtime.Items;
                var itemBytes = new byte[items.Count * 4];
                for (int i = 0; i < items.Count; i++) {
                    itemBytes[i * 4 + 0] = items[i].ObjectId;
                    itemBytes[i * 4 + 1] = items[i].Variable;
                    itemBytes[i * 4 + 2] = (byte)(items[i].ItemFlags & 0xFF);
                    itemBytes[i * 4 + 3] = (byte)(items[i].ItemFlags >> 8);
                }

                var edit = new DirtyContainerEdit {
                    BodyOffset = bodyOffset,
                    NumberOfItems = (byte)items.Count,
                    LiveItemBytes = itemBytes,
                };

                // Looting only moves items, so the header stays as authored and is left alone. A
                // ground-bag claim or release rewrites the record's identity, and then the packed
                // header and the last-touch stamp go down too. Capacity and the subrecord mask are
                // immutable either way, so the record's size — and the patch's exactness — holds.
                // The shop record travels with the container when it carries one: a tavern's
                // entertainment fund is SPENT by the performance it pays for, and a fund that did
                // not persist would pay again on the next visit. Emitted whenever the container is
                // dirty at all — it is sixteen bytes patched in place, and the alternative is a
                // second dirty flag to keep in step with the first.
                int shopOffset = ResourceExtraction.ContainerGeometry.ShopOffset(
                    rec.Runtime.Capacity, rec.Runtime.DataTypes);
                if (shopOffset >= 0 && rec.Runtime.Shop != null) {
                    edit.ShopOffset = shopOffset;
                    edit.ShopBytes = ResourceExtraction.ContainerGeometry.PackShop(rec.Runtime.Shop);
                }

                if (rec.Runtime.HeaderDirty) {
                    edit.HeaderBytes = ResourceExtraction.ContainerGeometry.PackHeader(rec.Runtime);
                    if (rec.Runtime.Timestamp.HasValue) {
                        edit.TimestampOffset = ResourceExtraction.ContainerGeometry.TimestampOffset(
                            rec.Runtime.Capacity, rec.Runtime.DataTypes);
                        edit.Timestamp = rec.Runtime.Timestamp.Value;
                    }
                }

                edits.Add(edit);
            }
            return edits;
        }

        // A zone's TempGamFileOffset is sometimes already section-local and sometimes absolute
        // (offset by ZoneContainerSectionStart) — mirrors SaveGameExtractor.NormalizeZoneContainerOffset,
        // using the fixed zone-container section size as the section-local upper bound.
        private static int NormalizeZoneLocalOffset(int tempGamOffset) =>
            (tempGamOffset >= 0 && tempGamOffset < ResourceExtraction.SaveGameOffsets.ZoneContainerDataSize)
                ? tempGamOffset
                : tempGamOffset - ResourceExtraction.ContainerGeometry.ZoneContainerSectionStart;

        /// <summary>
        /// The party's ONE shared keys inventory — <c>g_gameState.shared_inventory</c>, which
        /// <c>boot_party_state_load_from_temp</c> (canassa BOOT.C:132) binds to
        /// <c>actorspawn_objfixed(0, 7, 0)</c>: the zone-0 container at (x=7, y=0), typed
        /// <see cref="SaveGameContainerType.SharedKeys"/>. Keys never enter a member's own pack;
        /// they divert here (spec docs/specs/inventory-item-handling.md §4), and the inventory
        /// screen's container window switches the view to it. Null when the loaded save has no
        /// such container.
        /// </summary>
        public RuntimeContainer SharedKeysInventory => GetRuntimeContainerAt(
            SharedInventoryZone, SharedInventoryActor, SharedInventoryY);

        /// <summary>
        /// The party's overflow pile — <c>g_gameState.ground_pile</c>, bound by
        /// <c>boot_party_state_load_from_temp</c> (canassa BOOT.C:133) to
        /// <c>actorspawn_objfixed(0, 8, 0)</c>: the zone-0 container one slot past the keys,
        /// shipped with capacity 18.
        ///
        /// <para>It is NOT a ground bag and has no world position — nothing claims it, it never
        /// moves, and the recycler never sees it. It is where an item nobody can carry goes
        /// (<c>cmbinv_actor_acquire_item</c>, CMBINV.C:1037), and the world loop offers it back the
        /// next time the party is on the map (WORLDLP.C:177). Null when the loaded save has no such
        /// container.</para>
        /// </summary>
        public RuntimeContainer GroundPile => GetRuntimeContainerAt(
            SharedInventoryZone, GroundPileActor, SharedInventoryY);

        private const int GroundPileActor = 8;

        // The fixed spawn coordinates BOOT.C:132 uses. Not a general "actor 7" lookup: the
        // container is keyed by LOCATION because its type is SharedKeys, not Inventory, so it is
        // deliberately absent from the by-actor index (which only holds member packs).
        private const int SharedInventoryZone = 0;
        private const int SharedInventoryActor = 7;
        private const int SharedInventoryY = 0;

        /// <summary>
        /// The saved combat record for an actor slot, or null when the slot has none.
        /// </summary>
        /// <remarks>
        /// <b>Indexes the 1730-entry ACTOR table, not the party.</b> One 22-byte
        /// <c>CombatantState</c> per actor, which is what a combat save patches by slot — resolving
        /// a slot through the six-entry party array would field a party member for any slot under
        /// six, the same trap <see cref="RosterStatsOf"/> exists to avoid.
        /// </remarks>
        public SaveGameCombatData CombatRecordOf(int actorSlot) =>
            actorSlot >= 0 && actorSlot < _combatActors.Length ? _combatActors[actorSlot] : null;

        /// <summary>
        /// The mutable runtime inventory container owned by character
        /// <paramref name="characterIndex"/>, or null if that member has no Inventory-type container
        /// in the current save.
        /// </summary>
        /// <remarks>
        /// <b>The argument is a CHARACTER index (0 = Locklear, 4 = James), not a seat in the party
        /// and not an actor number.</b> Pass an <c>ActivePartyIndices</c> entry, never a loop position
        /// over it — from chapter 3 the two differ and a position reads another character's pack. The
        /// stored actor number is 1-based, so it is one more than this. The parameter used to be called <c>actorNumber</c>, which is how the index came to
        /// be built from the raw field.
        ///
        /// <para>(This doc block sat above <see cref="CombatRecordOf"/> until 2026-09-03, which had
        /// two summaries as a result and this had none.)</para>
        /// </remarks>
        /// <summary>
        /// Rebuild a character's carried-item attribute modifiers from their pack —
        /// <c>stat_actor_recalc_equip_bonuses</c>, see <see cref="StatEngine.RecalculateItemModifiers"/>.
        /// </summary>
        public void RecalculateItemModifiers(int characterIndex) {
            ActorStat[] stats = StatsOf(characterIndex);
            RuntimeContainer pack = GetActorInventory(characterIndex);
            if (stats == null || pack == null || ObjectInfo == null) {
                return;
            }
            var carried = new List<int>(pack.Items.Count);
            foreach (RuntimeItem item in pack.Items) {
                carried.Add(item.ObjectId);
            }
            StatEngine.RecalculateItemModifiers(stats, carried, ObjectInfo.GetById);
        }

        public RuntimeContainer GetActorInventory(int characterIndex) =>
            _runtimeContainersByActor.TryGetValue(characterIndex, out RuntimeContainer rc) ? rc : null;

        /// <summary>
        /// Every active member's pack, skipping any member who has none.
        /// </summary>
        /// <remarks>
        /// <b>The party-wide item effects all want this set</b> — the dialog conditions that ask
        /// whether anyone is carrying something, and the sub-actions that mend armour or bless
        /// swords across the party. It lives here because the containers do. Packs are keyed by
        /// CHARACTER index: a walk over roster positions 0..n-1 reads the wrong packs as soon as the
        /// party is not characters 0-2 (chapter 3's James), which is how three copies went wrong.
        /// </remarks>
        public IEnumerable<RuntimeContainer> ActivePartyPacks {
            get {
                foreach (byte member in ActivePartyIndices ?? Array.Empty<byte>()) {
                    RuntimeContainer pack = GetActorInventory(member);
                    if (pack != null) {
                        yield return pack;
                    }
                }
            }
        }

        /// <summary>The mutable runtime container at an exact world location, or null if none.</summary>
        /// <remarks>
        /// <b>The first record in file order whose chapter band holds the current chapter</b>, as
        /// <c>actorspawn_objfixed</c> takes it (ACTSPAWN.C:130-132). With none in band, the last
        /// record — what this answered before, so a caller that filters by chapter itself still does.
        /// </remarks>
        public RuntimeContainer GetRuntimeContainerAt(int zone, int x, int y) {
            if (!_runtimeContainersByLocation.TryGetValue((zone, x, y), out List<RuntimeContainer> here)) {
                return null;
            }
            foreach (RuntimeContainer rc in here) {
                if (Chapter >= rc.MinChapter && Chapter <= rc.MaxChapter) {
                    return rc;
                }
            }
            return here[here.Count - 1];
        }

        /// <summary>
        /// The live container standing at an exact world location for the current chapter — the
        /// runtime-layer counterpart of <see cref="GetContainerAt"/>, and what world interaction
        /// must ask. The snapshot cannot answer for a ground bag: its record still reads as a
        /// parked <see cref="SaveGameContainerType.Free"/> slot there, so a pile dropped this
        /// session would resolve as "nothing here".
        /// </summary>
        public RuntimeContainer GetLiveContainerAt(int zone, int x, int y) {
            RuntimeContainer rc = GetRuntimeContainerAt(zone, x, y);
            if (rc == null || rc.ContainerType == SaveGameContainerType.Free
                || Chapter < rc.MinChapter || Chapter > rc.MaxChapter) {
                return null;
            }
            return rc;
        }

        /// <summary>Every runtime container that lives in <paramref name="zone"/>'s record list —
        /// including the parked <see cref="SaveGameContainerType.Free"/> pool slots, which is what
        /// the ground-bag claim scans. Note this is the record list's zone, not the container's own
        /// <c>Zone</c> field: a free slot's field reads 255 until a claim stamps it.</summary>
        public List<RuntimeContainer> PoolRecordsInZone(int zone) {
            var found = new List<RuntimeContainer>();
            foreach (var rec in _runtimeContainerRecords) {
                if (rec.ZoneEntry.ZoneNumber == zone) {
                    found.Add(rec.Runtime);
                }
            }
            return found;
        }

        /// <summary>All ground bags currently placed in <paramref name="zone"/> — what the world
        /// build spawns a "bag" entity for so dropped piles survive a zone reload.</summary>
        public List<RuntimeContainer> GroundBagsInZone(int zone) {
            var bags = new List<RuntimeContainer>();
            foreach (var rec in _runtimeContainerRecords) {
                RuntimeContainer rc = rec.Runtime;
                if (rec.ZoneEntry.ZoneNumber == zone && rc.ContainerType == SaveGameContainerType.Bag
                    && Chapter >= rc.MinChapter && Chapter <= rc.MaxChapter) {
                    bags.Add(rc);
                }
            }
            return bags;
        }

        /// <summary>
        /// Hand-placed loot the engine spawns into the world itself — the RES_SELF_SPAWN arm of
        /// <c>actorspawn_for_location_by_time</c> (ACTSPAWN.C:83), which appends a record to the
        /// visible pool when its residence is 9 and the chapter is inside its band. No WLD placement
        /// stands there: the DIE chest south of Yabon (zone 1 container 40) is one of these, and
        /// without this pass it was never in the world (TASK-557).
        /// </summary>
        public List<RuntimeContainer> SelfSpawnedLootInZone(int zone) {
            var loot = new List<RuntimeContainer>();
            foreach (var rec in _runtimeContainerRecords) {
                RuntimeContainer rc = rec.Runtime;
                if (rec.ZoneEntry.ZoneNumber == zone && rc.ContainerType == SaveGameContainerType.ScriptedLoot
                    && Chapter >= rc.MinChapter && Chapter <= rc.MaxChapter) {
                    loot.Add(rc);
                }
            }
            return loot;
        }

        /// <summary>
        /// Resolve the container a discarded item should go into at (<paramref name="x"/>,
        /// <paramref name="y"/>) — the two-step in <c>cmbinv_actor_drop_item_at_pos</c>
        /// (CMBINV.C:1047).
        ///
        /// <para>First <c>actorspawn_objfixed</c>: a container already sitting on that exact spot
        /// in this chapter simply receives the item. Only when there is none does the fallback
        /// <c>actorspawn_enc_location</c> claim a bag out of the zone's pool — which is the case
        /// that produces a new pile and the "shoved it into a spare bag" line.</para>
        ///
        /// <para>Returns null only when the zone has neither a free slot nor a recyclable bag, so
        /// the caller must refuse the drop rather than destroy the item.</para>
        /// </summary>
        public GroundDropTarget ResolveGroundDropTarget(int zone, int x, int y) {
            RuntimeContainer existing = GetRuntimeContainerAt(zone, x, y);
            if (existing != null && existing.ContainerType != SaveGameContainerType.Free
                && Chapter >= existing.MinChapter && Chapter <= existing.MaxChapter) {
                return new GroundDropTarget(existing, isNewBag: false, recycled: false);
            }

            RuntimeContainer slot = GroundContainerPool.SelectSlot(
                PoolRecordsInZone(zone), out bool recycled);
            if (slot == null) {
                return default;
            }

            // A recycled bag is leaving its old spot, and a claim re-keys the slot, so the location
            // index has to follow both moves or a stale key would resolve to the wrong pile.
            ForgetLocation(slot);
            GroundContainerPool.Claim(slot, zone, x, y, (int)GameTimeIn2Seconds);
            if (!_runtimeContainersByLocation.TryGetValue((zone, x, y), out List<RuntimeContainer> here)) {
                _runtimeContainersByLocation[(zone, x, y)] = here = new List<RuntimeContainer>();
            }
            here.Insert(0, slot);   // the claim answers first, as the single slot's overwrite did
            return new GroundDropTarget(slot, isNewBag: true, recycled: recycled);
        }

        /// <summary>
        /// Return an emptied ground bag to its zone's pool (<c>actorspawn_destroy_and_persist</c>)
        /// and drop it out of the location index. True when the bag was freed, which is the signal
        /// to despawn its world entity.
        /// </summary>
        public bool ReleaseGroundBagIfEmpty(RuntimeContainer container) {
            if (!GroundContainerPool.ReleaseIfEmpty(container)) {
                return false;
            }
            ForgetLocation(container);
            return true;
        }

        // Drop a container out of the location index, but only if that key still points at it —
        // a stale key belonging to some other record must survive untouched.
        private void ForgetLocation(RuntimeContainer container) {
            var key = (container.Zone, container.X, container.Y);
            if (_runtimeContainersByLocation.TryGetValue(key, out List<RuntimeContainer> here)
                && here.Remove(container) && here.Count == 0) {
                _runtimeContainersByLocation.Remove(key);
            }
        }

        /// <summary>
        /// Apply a chapter's starting state over the already-hydrated session.
        ///
        /// Mirrors the DOS engine's <c>go_to_chapter</c> (KRONDOR.EXE 0x41f0a): STARTUP.GAM
        /// is only a template — its state block leaves zone/position/clock zeroed, and the
        /// CHAPx.DAT record is what supplies the real chapter-1 start. Must be called after
        /// <see cref="Initialize"/>.
        /// </summary>
        /// <summary>
        /// Puts the party somewhere — the state half of a teleport, and nothing else.
        /// </summary>
        /// <remarks>
        /// Sets zone, tile, fine position and facing together, because they are one fact: leaving
        /// the tile and the fine position to be updated separately is how a party ends up rendered
        /// in one cell and colliding in another.
        ///
        /// <para><b>Z is deliberately untouched.</b> Ground height is a property of where you
        /// landed, not of the destination record — the original leaves it for the zone load to
        /// resolve, and <c>PartyMovement.SyncToCamera</c> samples the terrain under the party on the
        /// next sync. Writing a Z here would be inventing a number the data does not carry.</para>
        /// </remarks>
        public void PlaceAt(GameData.Resources.Location.Location location) {
            if (location == null) {
                throw new ArgumentNullException(nameof(location));
            }

            CurrentZone = (byte)location.ZoneNumber;
            // The tile-and-offset to fine-position conversion has one owner; see WorldPlacement.
            PositionX = (int)GameData.Resources.World.WorldPlacement.CentreOf(location.X, location.XOffset);
            PositionY = (int)GameData.Resources.World.WorldPlacement.CentreOf(location.Y, location.YOffset);
            Rotation = (short)location.ZRotation;
        }

        public void ApplyChapterStart(ChapterStartData chapter) {
            if (chapter == null) {
                throw new ArgumentNullException(nameof(chapter));
            }

            Chapter = chapter.ChapterNumber;
            CurrentZone = (byte)chapter.StartLocation.ZoneNumber;
            PositionX = chapter.PositionX;
            PositionY = chapter.PositionY;
            PositionZ = 0; // terrain height resolves at load; the engine leaves Z untouched here
            Rotation = (short)chapter.StartLocation.ZRotation;

            // go_to_chapter advances the clock to the start of the next whole day and ADDS the
            // chapter's base time; party gold carries across the same way (both 0 for a fresh
            // chapter 1).
            //
            // *** THE ARITHMETIC MOVED TO ChapterTransition, IT DID NOT CHANGE. *** Both rules were
            // written out here and nowhere else, so nothing tested them and the transition work
            // re-derived them from the disassembly rather than finding them. They are the same
            // rules; this now calls the modelled ones, which carry the evidence and eleven tests —
            // including that four different end-of-chapter times all land on one midnight, which is
            // what separates this from a plain `+ oneDay`.
            GameTimeIn2Seconds =
                ChapterTransition.NextChapterStart(GameTimeIn2Seconds) + chapter.GameTimeIn2Seconds;
            PartyGold = ChapterTransition.GoldAfter(PartyGold, chapter.Gold);

            // *** THE CAST SCREEN'S STICKY PAIR IS CLEARED AT EVERY CHAPTER START. ***
            // savegame_chapter_start_dispatch writes wPalEventMask = 0 and both of these to -1
            // together (SAVEGAME.C:175-177), so a new chapter opens the cast screen on the first
            // caster and the default school rather than wherever the last one left it.
            //
            // Not merely tidy: STARTUP.GAM ships 0/0 in those two words, not the sentinel, so
            // without this a new game would open on party slot 0 and SYMBOL1 — a school the
            // original never opens on. The shipped value is never seen because this runs after it.
            CastMenuCasterSlot = GameData.Resources.Spells.CastMenuSelection.None;
            CastMenuSchool = GameData.Resources.Spells.CastMenuSelection.None;

            MapMarkerVisible = chapter.FullMapIcon.Visible;
            MapMarkerXPercent = chapter.FullMapIcon.XPercent;
            MapMarkerYPercent = chapter.FullMapIcon.YPercent;
            MapMarkerIcon = chapter.FullMapIcon.IconIndex;
        }

        /// <summary>
        /// Replace the active-party roster (count + member ids, in display order).
        ///
        /// Faithful to <c>ExecuteDialog</c>'s ChangeParty action (dialog action type 17, KRONDOR.EXE
        /// 0x4a005) — the sole writer of <c>numberOfActivePartyCharacters</c> / <c>activePartyCharacters</c>.
        /// The roster ORDER shown on the HUD/inventory is exactly this authored order, not STARTUP.GAM's
        /// (which stores a template [0,1,2]); the chapter-setup dialog DIAL_Z20 #2000023 supplies the real
        /// order per chapter (chapter 1 = [0,2,1] = Locklear, Owyn, Gorath). Raises
        /// <see cref="PartyCompositionChanged"/> so the head strip and party UI rebuild.
        /// </summary>
        public void SetActiveParty(byte count, byte[] members) {
            byte[] source = members ?? Array.Empty<byte>();
            int n = Math.Min(count, source.Length);
            var indices = new byte[n];
            Array.Copy(source, indices, n);

            ActivePartyCount = (byte)n;
            ActivePartyIndices = indices;
            ActivePartySlots = (byte[])source.Clone();
            PartyCompositionChanged?.Invoke();
        }

        /// <summary><c>g_chapterDefaultSpeaker</c> (GSTATE.C:20), by chapter: Locklear, James,
        /// James, Gorath, James, Owyn, James, Owyn, Pug.</summary>
        private static readonly byte[] ChapterDefaultSpeaker = { 0, 4, 4, 1, 4, 2, 4, 2, 3 };

        private const byte PugCharacter = 3;
        private const byte GorathCharacter = 1;
        private const byte OwynCharacter = 2;

        /// <summary><c>cmbinv_find_equipped_in_category</c> on a fixed character's pack.</summary>
        private bool EquippedOfType(byte character, GameData.ObjectType type) {
            RuntimeContainer pack = GetActorInventory(character);
            return pack != null
                && GameData.Resources.Inventory.InventoryEquip.FindEquippedIndex(pack, type, ObjectInfo) >= 0;
        }

        /// <summary>The seed the original's minimising arm starts from — <c>mov di, 30000</c>.</summary>
        private const int StealthSeed = 30000;

        /// <summary>
        /// One member's attribute as every rule in the game reads it — <c>stat_actor_get(actor, stat,
        /// 0)</c>, the EFFECTIVE value.
        /// </summary>
        /// <remarks>
        /// <b>Not <c>ActorStat.Base</c>.</b> Mode 0 runs the whole chain — permanent modifier, the
        /// eight timed modifiers, the affliction penalties and the health scaling — and the original
        /// passes it to every skill check there is: haggling (SHOP.C:73), picking a lock
        /// (PICKLOCK.C:107), and the party searches. Reading the stored byte instead means a wounded
        /// or debuffed character performs as if healthy.
        ///
        /// <para><b>Measured in the running original on 2026-09-13</b>: Locklear's Haggling reads 37
        /// at full health and <b>22</b> with his health knocked to 10, while <c>Base</c> stays 37
        /// throughout; Scouting drops 39 -> 23 the same way. The port's own effective read is verified
        /// against the binary for all sixteen attributes of all three members, so this one call is
        /// what makes a rule agree.</para>
        /// </remarks>
        public int EffectiveStat(int characterIndex, ActorAttribute attribute) {
            ActorStat[] stats = StatsOf(characterIndex);
            var index = (int)attribute;
            if (stats == null || index < 0 || index >= stats.Length || stats[index] == null) {
                return 0;
            }
            return StatEngine.Get(stats[index], attribute, stats[(int)ActorAttribute.Health],
                StatReadMode.Effective, PartyEffectsFor(characterIndex, attribute, inCombat: false));
        }

        /// <summary>
        /// <c>stat_actor_get(char, 0x10, 0)</c> — the pool of health and stamina, EFFECTIVE.
        /// </summary>
        /// <remarks>
        /// <b>Stat 0x10 has no slot of its own.</b> STAT.C:109 answers it as
        /// <c>get(0, mode) + get(1, mode)</c>, so at mode 0 each half runs the whole chain —
        /// permanent modifier, the eight timed modifiers, affliction penalties — and the sum is what
        /// every "how is this member doing" question in the original asks:
        ///
        /// <list type="bullet">
        /// <item>the camp and inn party table, ENCAMP.C:495 — and its colouring at :527, which
        /// greys a member's figure below 80%. <b>That pair is the TABLE, not the rest rule.</b>
        /// This list said ":527, the camp screen's 'rested enough' test" until 2026-09-21 and was
        /// wrong: the camp screen's two behavioural 80% tests — the Camp button's gate at :82 and
        /// the rest loop's stop at :140 — both call <c>stat_party_all_above_pct(0x50)</c>, which
        /// reads mode <b>3</b> (<c>st->base</c>, i.e. <see cref="StatEngine.HealthPool"/>), not
        /// mode 0. The mislabel had been copied into CampMenu and made it test the wrong
        /// quantity (TASK-605, TASK-606);</item>
        /// <item>the inn's rest loop deciding whether to charge another night, MODALSCR.C:803;</item>
        /// <item>the temple's "is anyone still hurt", CHARSCRN.C:444;</item>
        /// <item>the <c>PartyCondition</c> Check 10 branch, EVTCOND.C:141;</item>
        /// <item>a spell's cost pool, CSPELL.C:266.</item>
        /// </list>
        ///
        /// <para><b>Not <see cref="StatEngine.HealthPool"/></b>, which is deliberately the STORED
        /// pair and belongs to the writers. Four of the readers above were summing that instead, so
        /// an afflicted member read as healthy: a drunk Owyn's pool is 73 in the original and was 80
        /// here. Measured 2026-09-13 (TASK-488, TASK-489).</para>
        /// </remarks>
        public int EffectivePool(int characterIndex) =>
            EffectiveStat(characterIndex, ActorAttribute.Health)
            + EffectiveStat(characterIndex, ActorAttribute.Stamina);

        /// <summary>The pool's ceiling — <c>stat_actor_get(char, 0x10, 1)</c>, the stored maxima.</summary>
        public int EffectivePoolMax(int characterIndex) {
            ActorStat[] stats = StatsOf(characterIndex);
            return stats == null
                ? 0
                : (stats[(int)ActorAttribute.Health]?.Max ?? 0)
                    + (stats[(int)ActorAttribute.Stamina]?.Max ?? 0);
        }

        /// <summary>
        /// Award a skill use to <b>every</b> active party member —
        /// <c>stat_party_broadcast_status_op(stat, delta, 3)</c> (canassa <c>SRC/CHAR/STAT.C</c>).
        /// </summary>
        /// <remarks>
        /// <b>The original's skill awards for party ACTIONS are party-wide, not personal.</b> Mode 3
        /// is the skill-use scaling (<see cref="StatChangeMode.SkillUse"/>), and the broadcast walks
        /// <c>activeParty</c> applying it to each member in turn — the whole party learns from
        /// spotting the ambush or slipping past it, not just whoever rolled. Eleven call sites use
        /// it: scouting and stealth in HOTSPOT.C, Barding in TOWNSCN.C, Haggling in SHOP.C, and a
        /// dialog sub-action.
        ///
        /// <para>The haggling and barding paths already did this by hand; the three hotspot rolls
        /// awarded only the member <see cref="PartyExtreme"/> named, so the party trained about a
        /// third as fast at the two skills it uses most while travelling (TASK-474).</para>
        /// </remarks>
        public void BroadcastSkillUse(ActorAttribute attribute, int delta) {
            foreach (byte character in ActivePartyIndices) {
                // Through ModifyStatOf, so each member's sheet mark and the party-dirty bit are
                // written with the change rather than left to this caller to remember (TASK-611).
                ModifyStatOf(character, attribute, delta, StatChangeMode.SkillUse,
                    StudyBonusFor(character, attribute));
            }
        }

        /// <summary>
        /// The party-wide value of an attribute, and who supplies it: the <b>highest</b> in the
        /// active party for every attribute except <see cref="ActorAttribute.Stealth"/>, which is
        /// the <b>lowest</b>.
        /// </summary>
        /// <param name="memberIndex">Character id of the member holding it, or -1 when the party is empty.</param>
        /// <remarks>
        /// *** STEALTH IS THE PARTY'S WORST, NOT ITS BEST. *** <c>getHighestValueInParty</c>
        /// (@0x43538, canassa <c>stat_party_find_extreme</c>) opens with
        /// <c>cmp [bp+attributeNumber], Stealth / jnz</c> and seeds that arm with <b>30000</b>,
        /// keeping the smallest — the party is only as quiet as its clumsiest member. Every other
        /// attribute keeps the largest from a seed of 0.
        ///
        /// <para>This used to maximise unconditionally, and its own remark said it covered "the
        /// maximising case, which is every attribute except 0xf" — while both Stealth callers
        /// (<c>HotspotService</c>'s two slip-past rolls) went through it anyway. Measured on
        /// 2026-09-13 with the same save in both games: Locklear 29, Owyn 31, Gorath 33, and the
        /// original answered <b>29</b> where this answered 33 — so the port slipped past encounters
        /// on a better roll than the original allows, and trained the wrong member for it
        /// (the skill-use award follows <paramref name="memberIndex"/>).</para>
        ///
        /// <para>It walks the active roster — not all six saved records — and reads the
        /// <i>effective</i> value, so wounds and equipment count. The original applies no
        /// dead/unconscious filter and neither do we.</para>
        /// </remarks>
        public int PartyExtreme(ActorAttribute attribute, out int memberIndex) {
            bool wantLowest = attribute == ActorAttribute.Stealth;
            memberIndex = -1;
            int best = wantLowest ? StealthSeed : 0;
            for (var i = 0; i < ActivePartyIndices.Length; i++) {
                int character = ActivePartyIndices[i];
                if (StatsOf(character) == null) {
                    continue;
                }
                // *** THROUGH THE PARTY-EFFECTS HOOK. *** This used to read past both the timed
                // modifiers and the afflictions, so the party's skill checks — scouting an ambush,
                // picking a lock, haggling — were decided on unmodified values while the character
                // sheet showed the player something else. EffectiveStat is that read, shared with
                // every single-member caller.
                int value = EffectiveStat(character, attribute);
                if (wantLowest ? value < best : value > best) {
                    best = value;
                    memberIndex = character;
                    _dialogTertiaryActorOverride = character;   // g_gameState.nEvtArgStat = member_idx
                }
            }
            // Strictly beyond the seed, exactly as the original: if nobody has any of this skill
            // there is no member to name, and the caller must not award anyone the experience. The
            // empty-party answer is the seed itself — 0, or 30000 for Stealth — which is also what
            // the original returns, both loops having run zero times.
            return best;
        }

        /// <summary>Game-clock ticks in one day (2-second ticks): 86400s / 2.</summary>

        /// <summary>
        /// Faithful GetContainerAtLocation (KRONDOR.EXE 0x5ac4a) over the surfaced zone
        /// containers, using the session's current <see cref="Chapter"/>. (x, y) are fine
        /// world coords. Returns null when no container sits at that exact spot for this chapter.
        /// </summary>
        /// <summary>
        /// The placement at a world location — <b>the save first, then the shipped file</b>.
        /// </summary>
        /// <remarks>
        /// <c>actorspawn_objfixed</c> does a TWO-PASS lookup and the save merely SHADOWS
        /// OBJFIXED.DAT; consulting only the save finds almost nothing, because most fixed objects
        /// are never written back. Doors are the clearest case: not one door placement exists in a
        /// save, so a save-only lookup answers null for every door in the game and anything built
        /// on it silently does nothing.
        /// </remarks>
        public SaveGameContainerData GetContainerAt(int zone, int x, int y) =>
            ContainerLocator.FindContainerAtLocation(
                ZoneContainers, FixedObjects, zone, x, y, Chapter);

        /// <summary>
        /// OBJFIXED.DAT — the shipped half of the placement lookup. Null until loaded, which
        /// degrades to the save-only behaviour every caller had before this source existed.
        /// </summary>
        public GameData.Resources.Data.FixedObjectSet FixedObjects { get; set; }

        /// <summary>
        /// Sets the chapter outright, for the one caller that borrows another: CHEAT CENTRAL's chest
        /// runs as chapter 9 and puts the real one back (TOWNSCN.C:715-720).
        /// </summary>
        public void SetChapter(int chapter) => Chapter = chapter;

        // Test seam: set containers + chapter without a full SaveGame hydration. Also rebuilds
        // the runtime layer (mirrors Initialize) so GetActorInventory/GetRuntimeContainerAt are
        // exercisable from the same seam.
        internal void SetZoneContainersForTest(SaveGameZoneContainerStateData containers, int chapter) {
            ZoneContainers = containers;
            Chapter = chapter;
            BuildRuntimeContainers();
        }

        /// <summary>
        /// Read a global/flag value faithfully (KRONDOR.EXE GetGlobalValue @0x42250 → the same
        /// bitfields SaveGameStateData parses). Returns null if no state is loaded and no test
        /// overlay bit is set for <paramref name="key"/>.
        /// </summary>
        /// <summary>
        /// A dialog has asked for the hotspot pass to run again where the party stands.
        /// </summary>
        /// <remarks>
        /// <b>Transient, and deliberately not saved.</b> The original holds this in a plain global
        /// that both world loops CLEAR ON ENTRY (WORLDLP.C:86, MAP.C:115), so a request never
        /// survives into a new loop — it is a message to the current iteration, not game state.
        ///
        /// <para>Raised by dialog sub-action 12 and consumed by the movement driver on a tick where
        /// nothing moved, which is the original's own <c>moved == 0 &amp;&amp; flag != 0</c> guard.
        /// It is deferred rather than run inline because a hotspot pass can raise a dialog, and
        /// running one from inside a resolving dialog re-enters it.</para>
        /// </remarks>
        public bool HotspotPassRequested { get; set; }

        /// <summary>
        /// The Wooden Chest's result 0x66: raise the camera once the inventory is closed
        /// (CMBINV.C:463-466). The travel screen takes it and clears it.
        /// </summary>
        public bool CameraLiftRequested { get; set; }

        /// <summary>How many dialog plays are in progress (DialogManager brackets each whole play).
        /// A requested hotspot pass waits for zero: the original's dialog is modal.</summary>
        public int DialogsPlaying { get; set; }

        /// <summary>
        /// The establishment's unpaid fund, while one of its dialogs is running.
        /// </summary>
        /// <remarks>
        /// <b>This is the original's <c>dwPopup_retry_state</c>, and it is deliberately a
        /// conversation-scoped global rather than a field on the container.</b> TOWNSCN.C:467-508
        /// loads it out of the speaking actor's <c>SUBREC_EVENT_STATE</c> before
        /// <c>dialog_play_record</c>, lets the dialog's sub-actions move it, writes it back clamped
        /// to <see cref="EstablishmentFundMax"/>, and zeroes it again. So a sub-action can change
        /// the fund without knowing which establishment it is talking to — which is what makes
        /// subtypes 8 and 11 expressible at all, since a dialog has no handle on the container.
        ///
        /// <para><b>The byte it comes from is the BARDING FUND</b> — the money the house owes for a
        /// performance (<c>SaveGameContainerShopData.BardingReward</c>, offset 7 of the block).
        /// canassa calls it <c>bPopup_retry_counter</c>, which is wrong: TOWNSCN.C:260-293 pays out
        /// <c>counter * 10</c> gold gated on the party's best Barding against offset 6, halving and
        /// quartering it by how well they played. Our own <c>Barding</c> port already names and
        /// spends it correctly.</para>
        ///
        /// <para>Transient on purpose, exactly like the original's global: it is loaded, used and
        /// cleared inside one conversation, and the container is where it actually lives.</para>
        /// </remarks>
        public int EstablishmentFund { get; set; }

        /// <summary>The byte's ceiling — the original clamps to 0xfa on the way back, not 0xff.</summary>
        public const int EstablishmentFundMax = 0xfa;

        /// <summary>
        /// The character an event has selected — the original's <c>nEvtArgActor0</c>.
        /// </summary>
        /// <remarks>
        /// <b>Not "the speaker", and that distinction is the whole of it.</b> It is a global the
        /// engine writes whenever something picks an actor out of the party, and the clearest case
        /// is barding: <c>stat_party_find_extreme</c> finds the best performer and TOWNSCN.C:262
        /// copies its answer into this. A dialog sub-action that then names "the actor" means
        /// whoever that lookup chose, not whoever is talking.
        ///
        /// <para>Transient, like <see cref="EstablishmentFund"/>: it is a working register the
        /// engine overwrites, never save state. -1 when nothing has selected anyone.</para>
        /// </remarks>
        public int EventActor { get; set; } = -1;

        /// <summary>
        /// <c>nEvtArgActor0</c> is global 30004, and it is ONE location.
        /// </summary>
        /// <remarks>
        /// <b>The port had two.</b> <see cref="EventActor"/> took every runtime write, while
        /// <c>GetGlobalValue(30004)</c> answered the byte parsed out of the save — so the readers
        /// never saw what the writers had done. Measured 2026-09-10: Limm's seal is given to
        /// operand 2, which is <c>g_speaker_kinds[0]</c>, seeded by the hub's own
        /// <c>SetTextVariable(slot 0, source 30)</c> from this global; the stale byte said Pug, who
        /// is not in the party, and the Glazer's Guild Seal landed in his pack where the bridge's
        /// item gate — active packs plus the shared inventory — cannot see it.
        ///
        /// <para>Seeded from the save on load and answered live thereafter. -1 means nothing has
        /// selected anyone, in which case the saved byte still answers.</para>
        /// </remarks>
        private const int CurrentActorGlobalKey = 30004;

        public int? GetGlobalValue(int key) {
            if (_flagOverlay != null && _flagOverlay.TryGetValue(key, out int overridden)) {
                return overridden;
            }

            // *** 50000+id IS "HOW MANY OF THAT OBJECT DOES THE PARTY HOLD". *** GSTATE.C:45 —
            // `if (id >= 0xc350 && id < 0xc3da) return itemtbl_partySize_by_kind(id - 50000)`.
            // Answered here rather than in the parsed save state because it needs the LIVE runtime
            // containers, which is also why it was missing: `TryGetGlobalValue` had no way to see
            // them and returned null, so every "do you have X" branch in the game read as false.
            if (key == CurrentActorGlobalKey && EventActor >= 0) {
                return EventActor;
            }

            // *** THE GOLD GLOBALS READ THE LIVE PURSE, NOT THE LOADED SAVE. *** GSTATE.C computes
            // 30001/30002/30003 from g_gameState.nParty_gold and lEvtArgGoldCost at the moment the
            // branch is taken. Falling through to the parsed save answers with the purse as it stood
            // at the LOAD and with the save's own copy of the quote — so every priced offer's own
            // affordability gate passed on a question nobody asked.
            //
            // Measured at Highcastle on 2026-09-13: a 173-royal repair quote against 143 royals took
            // dialog 1800035's Var 3 arm, the mender's "I seem to be short" never played, and the
            // party came out at -30. The inn had the same hole and worked around it in C#
            // (InnStay.CanAfford) after coming out at -20 on 2026-09-10; the rule belongs here,
            // where all four priced offers read it.
            // *** THE TIME OF DAY READS THE LIVE CLOCK TOO. *** The parsed save holds the clock as it
            // stood at the load, so a party that loaded at 02:20 stayed in the dark all morning:
            // Brother Jeremy's door (dial_z19:8862, Var 9 arm) answered "No one answered" at 07:34
            // because 30009 still said night (TASK-549). 30009/30010/30012 are the three globals
            // computed from game_time.
            switch (key) {
                case 30009:
                    return GameData.Resources.GameState.GameTime.IsNight(GameTimeIn2Seconds) ? 1 : 0;
                case 30010:
                    return GameData.Resources.GameState.GameTime.IsNight(GameTimeIn2Seconds) ? 0 : 1;
                case 30012:
                    return GameData.Resources.GameState.GameTime.HourOfDay(GameTimeIn2Seconds);
                // *** VAR 29 IS THE HOUSE'S FUND, LIVE. *** GSTATE.C:109-110 answers it from
                // dwPopup_retry_state, which TOWNSCN.C:466-473 loads from the scene container before
                // the hotspot's dialog. The save holds the same dword (RewardMoneyCounter), but it is
                // zeroed after every conversation, so reading it made every gambler and paying
                // tavern "out of funds" (Romney's Black Sheep, 2026-10-03).
                case 30029:
                    return EstablishmentFund > 0 ? 1 : 0;
                // *** VAR 5, THE CHAPTER'S SPEAKER, IS COMPUTED, NOT STORED. *** GSTATE.C:80:
                // Pug when he is in the party, else g_chapterDefaultSpeaker[chapter - 1]. Falling
                // through read whatever the save carried, so in chapter 4 -- Gorath and Owyn alone --
                // every dialog's slot 4 still named James ("James gritted his teeth" at their own
                // belongings chest, 2026-09-23).
                case DialogSlotPopulator.ChapterSpeakerGlobalKey:
                    return System.Array.IndexOf(ActivePartyIndices ?? Array.Empty<byte>(), PugCharacter) >= 0
                        ? PugCharacter
                        : ChapterDefaultSpeaker[Math.Max(1, Math.Min(ChapterDefaultSpeaker.Length, Chapter)) - 1];
                // *** THE CHAPTER READS THE LIVE SESSION, NOT THE LOADED SAVE. *** Var 7 is this
                // key. GSTATE.C:73 answers it from `g_gameState.nChapter` — ONE storage the engine
                // reads both ways, kept current because savegame_chapter_start_dispatch freads
                // CHAPx.DAT straight over the state head (which is why, as ChapterTransition says,
                // no shipped dialog ever writes 30007). The port splits that into the Chapter
                // property (live) and the parsed save's ChapterNumber (frozen at load), so falling
                // through reported the chapter the SAVE was loaded in: after an in-session
                // GoToChapter it still said 1.
                //
                // Every chapter-dispatching dialog then took its chapter-1 arm. Measured 2026-09-17:
                // the Krondor sewer ladder (2300004) branches `Var 7 == 1` to chapter 1's "advance
                // the chapter" action, so using it in chapter 2 jumped the party to chapter 3 and
                // skipped the whole of chapter 2 (TASK-570). Same ladder from a LOADED chapter-2
                // save, where hydration had set 30007 = 2, took the correct arm.
                //
                // The write side already targets the property (SetGlobalValue -> WriteEventField ->
                // Chapter), so without this the two disagreed about the same key.
                case DialogBranchWalker.ChapterGlobalKey:
                    return Chapter;
                case DialogBranchWalker.GoldSovereignsGlobalKey:
                    return Math.Min(ushort.MaxValue, Math.Max(0, PartyGold / 10));
                case DialogBranchWalker.GoldRoyalsGlobalKey:
                    return Math.Min(ushort.MaxValue, Math.Max(0, PartyGold));
                case DialogBranchWalker.CanAffordQuoteGlobalKey:
                    // Through GetGlobalValue so the quoting screen's overlay write wins over the
                    // save's byte — 30014 is one of the lEvtArg scratch values, which deliberately
                    // fall through to the overlay rather than to a session field.
                    return PartyGold
                        >= (GetGlobalValue(DialogSlotPopulator.QuotedAmountGlobalKey) ?? 0) ? 1 : 0;
            }

            if (key >= DialogBranchWalker.ItemCountGlobalBase
                && key < DialogBranchWalker.ItemCountGlobalBase + ItemCountGlobalSpan) {
                return CountCarriedAcrossParty(key - DialogBranchWalker.ItemCountGlobalBase);
            }

            // *** 51000+n IS "IS THE PARTY CARRYING NOTE n". *** EVTCOND.C:61-78 scans every ACTIVE
            // member's slots for item id 120 ('x') whose CONDITION equals n — the note number lives
            // in the slot's variable byte, not in the object id, so all notes share one object.
            if (key >= DialogBranchWalker.NoteGlobalBase
                && key < DialogBranchWalker.NoteGlobalBase + NoteGlobalSpan) {
                return PartyCarriesNote(key - DialogBranchWalker.NoteGlobalBase) ? 1 : 0;
            }

            // *** 52000+t IS A PALETTE-EVENT BIT, AND IT IS OFF BY ONE. *** GSTATE.C:50 passes
            // `id + 0x34df` to spellfx_event_mask_test_bit, which on 16 bits makes 52001 -> bit 0,
            // 52002 -> bit 1 and so on: the bit is t - 1, not t. (52000 itself computes 0xFFFF and
            // would index a 9-entry table wildly out of bounds; no shipped dialog asks it, and this
            // answers 0 rather than reproducing the read.) The mask itself is
            // g_gameState.wPalEventMask, which is PaletteEventMask here.
            if (key >= DialogBranchWalker.SpellTimerGlobalBase
                && key < DialogBranchWalker.SpellTimerGlobalBase + SpellTimerGlobalSpan) {
                int bit = key - DialogBranchWalker.SpellTimerGlobalBase - 1;
                return bit >= 0 && bit < PaletteEventBitCount
                    ? (PaletteEventMask >> bit) & 1
                    : 0;
            }

            // *** 53000+n ROLLS. *** RND(n) is `(rand() & 0xfff) % n` (RAND.H:19), so 0..n-1, and it
            // is a read WITH A SIDE EFFECT: asking twice gives two rolls. That is why the walker has
            // no `roll` parameter — the die belongs to whoever answers the key.
            if (key >= DialogBranchWalker.RandomGlobalBase
                && key < DialogBranchWalker.RandomGlobalBase + RandomGlobalSpan) {
                int bound = key - DialogBranchWalker.RandomGlobalBase;
                return bound > 0 ? UnityEngine.Random.Range(0, bound) : 0;
            }

            // The party checks. Each is its own query rather than a range test — see
            // PartyCheckValue, which answers the ones that read party state and leaves the rest to
            // fall through unchanged.
            if (key > DialogBranchWalker.PartyCheckGlobalBase
                && key <= DialogBranchWalker.PartyCheckGlobalBase + PartyCheckCount) {
                int? answered = PartyCheckValue(key - DialogBranchWalker.PartyCheckGlobalBase);
                if (answered.HasValue) {
                    return answered.Value;
                }
            }

            return _state?.TryGetGlobalValue(key);
        }

        /// <summary>The 0xc350..0xc3da window GSTATE.C tests — 138 object ids.</summary>
        private const int ItemCountGlobalSpan = 138;

        /// <summary>The 0xc738..0xc79c window EVTCOND.C scans — 100 note numbers.</summary>
        private const int NoteGlobalSpan = 100;

        /// <summary>Party checks run 40001..40013.</summary>
        private const int PartyCheckCount = 13;

        /// <summary>The 0xcb20..0xcb27 window GSTATE.C tests — seven spell timers.</summary>
        private const int SpellTimerGlobalSpan = 7;

        /// <summary>The 0xcf08..0xcf6c window GSTATE.C tests — 101 bounds.</summary>
        private const int RandomGlobalSpan = 101;

        /// <summary><c>g_aPalEventBitMask</c> has nine entries (SPELLFX.C:24).</summary>
        private const int PaletteEventBitCount = 9;

        /// <summary>The object id every note shares; its condition byte is the note number.</summary>
        private const int NoteObjectId = 120;

        /// <summary>The object Check 11 asks every member to be carrying.</summary>
        private const int CheckElevenObjectId = 0x59;

        /// <summary>The object Check 3 counts — 'x30', Standard Kingdom Armor.</summary>
        private const int DeliverableArmourObjectId = 0x30;

        /// <summary>Check 3 only counts a piece at <c>condition >= 0x46</c>.</summary>
        private const int DeliverableArmourCondition = 0x46;

        /// <summary>Check 3 is <c>count > 5</c> — six pieces, not five.</summary>
        private const int DeliverableArmourCount = 5;

        /// <summary>Rations, and the two that are not fit to eat.</summary>
        private const int RationsObjectId = 0x48;
        private const int PoisonedRationsObjectId = 0x49;
        private const int SpoiledRationsObjectId = 0x4a;

        /// <summary>
        /// The three containers Check 5 reads, as <c>actorspawn_objfixed</c> coordinates
        /// (EVTCOND.C:102-112). Fine world coords in zone 5, hard-coded in the original.
        /// </summary>
        private static readonly (int Zone, int X, int Y)[] PoisonedRationStashes = {
            (5, 0x16b2fb, 0x111547),
            (5, 0x16b2fb, 0x110f20),
            (5, 0x16b33a, 0x11083c),
        };

        /// <summary>The one container checks 12 and 13 both read (EVTCOND.C:155, :163).</summary>
        private const int RationStashZone = 3;
        private const int RationStashX = 0x13f560;
        private const int RationStashY = 0xf4ba0;

        /// <summary>
        /// Whether any ACTIVE member carries note <paramref name="note"/>.
        /// </summary>
        /// <remarks>
        /// <c>evtcond_range_d_read_handler</c>, EVTCOND.C:61-78. The active party only — a note in a
        /// benched member's pack does not answer, which is the same rule
        /// <see cref="CountCarriedAcrossParty"/> follows.
        /// </remarks>
        private bool PartyCarriesNote(int note) {
            foreach (byte member in ActivePartyIndices ?? Array.Empty<byte>()) {
                RuntimeContainer pack = GetActorInventory(member);
                if (pack?.Items == null) {
                    continue;
                }
                foreach (RuntimeItem item in pack.Items) {
                    if (item.ObjectId == NoteObjectId && item.Variable == note) {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// One party check, or null when this one is not answered here.
        /// </summary>
        /// <remarks>
        /// <b>Thirteen separate queries, not a range</b> — <c>evtcond_range_d_read_handler</c>
        /// (EVTCOND.C:80-170). <b>Every check any shipped dialog asks is answered here</b>: 1, 2,
        /// 3, 4, 5, 9, 10, 11, 12 and 13, which between them are all twelve shipped
        /// <c>PartyCondition</c> branches. The rest return null and fall through to the saved
        /// globals:
        ///
        /// <list type="bullet">
        /// <item><b>6</b> (Owyn holding a staff and Gorath a sword) exists in the engine and NO
        /// shipped dialog asks it; <b>7 and 8</b> are absent from the switch entirely and are
        /// always 0.</item>
        /// </list>
        ///
        /// <para><b>Recorded, not modelled: checks 3 and 4 are reads WITH A SIDE EFFECT in the
        /// original.</b> <c>evtcond_pty_inv_repair_cnt</c> ends by writing the damaged count into
        /// <c>lEvtArgValue</c> and MULTIPLYING <c>lEvtArgGoldCost</c> by it, so asking the
        /// condition is also what prices the job. The port does that multiply in the repair
        /// sub-action instead (<c>DialogExecutor.RepairPartyArmour</c>), which is where the player
        /// actually pays; a dialog that quoted a price off the CONDITION alone, without running
        /// the repair action, would quote the unit price. None ships that way — but the asymmetry
        /// is the original's own and is the thing to check first if a repair quote ever reads
        /// wrong.</para>
        /// See TASK-410.
        /// </remarks>
        private int? PartyCheckValue(int check) {
            byte[] roster = ActivePartyIndices ?? Array.Empty<byte>();
            switch (check) {
                case 1:
                    // EVERY member starving. The engine bails to 0 on the first rank of ZERO, so
                    // this is "all", not "any" — the inverted reading is the easy mistake.
                    foreach (byte member in roster) {
                        if (RankOf(member, ActorCondition.Starving) == 0) {
                            return 0;
                        }
                    }
                    return roster.Length > 0 ? 1 : 0;
                case 2:
                    foreach (byte member in roster) {
                        if (RankOf(member, ActorCondition.Plagued) != 0) {
                            return 1;
                        }
                    }
                    return 0;
                case 6:
                    // EVTCOND.C:120 (0x9c46): Owyn has a STAFF equipped and Gorath a SWORD. It is
                    // what the chapter-4 Sar-Sargoth dungeon's doors (Bloc 33/34/37/42/50) forbid on:
                    // until both have their weapons back from the chests, every doorway answers
                    // "let's have open those chests". With no case here it read the save's 0 and the
                    // party could never leave the cell block (2026-09-23).
                    return EquippedOfType(OwynCharacter, GameData.ObjectType.Staff)
                           && EquippedOfType(GorathCharacter, GameData.ObjectType.Sword) ? 1 : 0;
                case 9:
                    return AnyoneAfflicted(roster) ? 1 : 0;
                case 10:
                    // Afflicted, or not at full health+stamina: stat 0x10 current against max, the
                    // same pair the camp screen prints as "100 of 100".
                    if (AnyoneAfflicted(roster)) {
                        return 1;
                    }
                    foreach (byte member in roster) {
                        ActorStat[] stats = StatsOf(member);
                        if (stats == null) {
                            continue;
                        }
                        // EVTCOND.C:141 compares mode 0 against mode 1, so a member held below full
                        // by a modifier counts as needing attention even when the stored pair is at
                        // its maximum. See EffectivePool.
                        if (EffectivePool(member) != EffectivePoolMax(member)) {
                            return 1;
                        }
                    }
                    return 0;
                case 3:
                    // `count_out > 5` — pieces of ONE object at 70 condition or better, counted per
                    // slot. Not the repair count next door, which is a different total from the
                    // same walk.
                    return InventoryQuery.CountAtConditionAtLeast(
                        ActivePartyPacks, DeliverableArmourObjectId, DeliverableArmourCondition)
                        > DeliverableArmourCount ? 1 : 0;
                case 4:
                    // `repair_out > 0`. The dialog layer already asks this to decide whether an
                    // armourer has anything to say (`DialogManager.PartyHasDamagedArmour`); both
                    // go through the same walk so the topic and the branch cannot disagree.
                    return InventoryQuery.CountNeedingRepair(ActivePartyPacks, ObjectInfo) > 0
                        ? 1 : 0;
                case 5:
                    // ALL THREE, not any: the original seeds its result to 1 and clears it on the
                    // first stash that has none.
                    foreach ((int Zone, int X, int Y) stash in PoisonedRationStashes) {
                        if (ContainerCountOf(stash.Zone, stash.X, stash.Y, PoisonedRationsObjectId)
                            == 0) {
                            return 0;
                        }
                    }
                    return 1;
                case 11:
                    foreach (byte member in roster) {
                        if (CountCarriedBy(member, CheckElevenObjectId) == 0) {
                            return 0;
                        }
                    }
                    return roster.Length > 0 ? 1 : 0;
                case 12:
                    // Returns the COUNT, not a flag — the shipped branch tests it for 1 or more.
                    return ContainerCountOf(RationStashZone, RationStashX, RationStashY,
                        RationsObjectId);
                case 13:
                    return ContainerCountOf(RationStashZone, RationStashX, RationStashY,
                            PoisonedRationsObjectId) != 0
                        || ContainerCountOf(RationStashZone, RationStashX, RationStashY,
                            SpoiledRationsObjectId) != 0
                        ? 1 : 0;
                default:
                    return null;
            }
        }

        /// <summary>Any active member with any affliction EXCEPT Healing, which is index 4.</summary>
        private bool AnyoneAfflicted(byte[] roster) {
            foreach (byte member in roster) {
                for (var rank = 0; rank < ActorConditions.Count; rank++) {
                    if (rank == (int)ActorCondition.Healing) {
                        continue;
                    }
                    if (RankOf(member, (ActorCondition)rank) != 0) {
                        return true;
                    }
                }
            }

            return false;
        }

        private int RankOf(int member, ActorCondition condition) {
            ActorConditions conditions = ConditionsOf(member);
            return conditions == null ? 0 : conditions[condition];
        }

        /// <summary>
        /// How many of an object sit in the container at an exact world location —
        /// <c>itemtbl_inv_count_by_kind(actorspawn_objfixed(zone, x, y), id)</c>.
        /// </summary>
        /// <remarks>
        /// <b>The live copy first, then the placement.</b> The runtime layer is built from the
        /// save's own containers; anything the save does not carry resolves from OBJFIXED.DAT
        /// through <see cref="GetContainerAt"/> — the same two-pass order
        /// <c>actorspawn_objfixed</c> uses, for the same reason.
        ///
        /// <para><b>All four stashes the party checks name are in the SAVE, not in the shipped
        /// file.</b> Measured in STARTUP.GAM: none of the four coordinates appears among
        /// OBJFIXED.DAT's 581 placements, while every one of them is among the save's 2738
        /// containers — the three zone-5 ones holding four to five stacks of object 72 (rations,
        /// 7 apiece) and the zone-3 one empty. So the live pass is the one that answers here, and
        /// the fallback is for the general rule rather than for these.
        /// </remarks>
        private int ContainerCountOf(int zone, int x, int y, int objectId) {
            RuntimeContainer live = GetRuntimeContainerAt(zone, x, y);
            if (live != null) {
                return InventoryQuery.CountByKind(live, objectId);
            }

            SaveGameContainerData placed = GetContainerAt(zone, x, y);
            if (placed?.Items == null) {
                return 0;
            }

            var total = 0;
            foreach (SaveGameInventoryItemData item in placed.Items) {
                if (item != null && item.ObjectId == objectId) {
                    total += item.Variable != 0 ? item.Variable : 1;
                }
            }

            return total;
        }

        private int CountCarriedBy(byte member, int objectId) {
            RuntimeContainer pack = GetActorInventory(member);
            if (pack?.Items == null) {
                return 0;
            }
            var total = 0;
            foreach (RuntimeItem item in pack.Items) {
                if (item.ObjectId == objectId) {
                    total += Math.Max(1, (int)item.Variable);
                }
            }

            return total;
        }

        /// <summary>
        /// <c>itemtbl_partySize_by_kind</c>: the party's total of an object, and who has it.
        /// </summary>
        /// <remarks>
        /// The original writes <c>nEvtArgActor0</c> as it goes, so a dialog that asks whether the
        /// party carries something can then name the member holding it. <see cref="EventActor"/> is
        /// that global here, and it is set for the same reason and at the same moment.
        /// </remarks>
        private int CountCarriedAcrossParty(int objectId) {
            byte[] roster = ActivePartyIndices ?? Array.Empty<byte>();
            var packs = new List<RuntimeContainer>(roster.Length);
            foreach (byte member in roster) {
                packs.Add(GetActorInventory(member));
            }

            int total = InventoryQuery.CountAcrossParty(
                packs, SharedKeysInventory, objectId, out int holderSlot);
            if (holderSlot >= 0 && holderSlot < roster.Length) {
                EventActor = roster[holderSlot];
            }

            return total;
        }

        /// <summary>Active-party member display names, in roster order (@0 = lead).</summary>
        public IReadOnlyList<string> ActivePartyMemberNames {
            get {
                var names = new List<string>();
                byte[] idx = ActivePartyIndices ?? Array.Empty<byte>();
                string[] all = PartyActorNames ?? Array.Empty<string>();
                foreach (byte i in idx) {
                    if (i < all.Length) {
                        names.Add(all[i]);
                    }
                }
                return names;
            }
        }

        /// <summary>Set a global flag/value override in the in-session overlay (a dialog's
        /// SetFlagEffect, etc.). Read back by <see cref="GetGlobalValue"/> ahead of the loaded save.
        /// Note: temporary flags (SetFlagEffect.ForTicks — e.g. the corpse-flavor 8127 flag's
        /// ~2-game-hour auto-clear) currently persist for the session; the tick timer is a
        /// documented follow-up (needs a game clock).</summary>
        public void SetGlobalFlag(int key, bool set) => SetGlobalValue(key, set ? 1 : 0);

        /// <summary>
        /// Zero every global a chapter transition wipes — <c>ClearGlobalVars_400_5200</c> @0x74d9b.
        /// </summary>
        /// <remarks>
        /// <b>It clears through the ordinary setter, and that is the whole design.</b> The original
        /// is sixteen instructions — <c>for (i = 0; i &lt; 4800; i++) SetGlobalValue(400 + i, 0)</c>
        /// — and its <c>SetGlobalValue</c> is the same one 56 other callers use, from
        /// <c>ExecuteDialog</c> to <c>ChangeAttributeValue</c>. There is no block memset and no
        /// save-bypassing back door.
        ///
        /// <para>So the zeros land in the overlay like any other write, reach
        /// <see cref="DirtyGlobalFlags"/>, and are recorded by a save taken afterwards. That was
        /// once filed as a decision about what a save should carry; it is not a decision, it is what
        /// the game does.</para>
        ///
        /// <para><b>Going through <see cref="SetGlobalValue"/> also keeps the field routing.</b> A
        /// key in the range that names a <see cref="GameStateEventFields"/> field writes that field
        /// rather than the overlay — which is exactly what the original's setter does with the same
        /// key, so a loop that wrote the dictionary directly would diverge on precisely those.</para>
        ///
        /// <para>The range is HALF-OPEN: 5200 is the first key of a separately managed window with
        /// clears of its own, and is deliberately left alone. See
        /// <see cref="ChapterTransition.ClearedGlobalsEnd"/>.</para>
        /// </remarks>
        public void ClearChapterGlobals() {
            for (int key = ChapterTransition.ClearedGlobalsFirst;
                 key < ChapterTransition.ClearedGlobalsEnd;
                 key++) {
                SetGlobalValue(key, 0);
            }
        }

        /// <summary>Set a global to an arbitrary integer value in the in-session overlay. The
        /// original writes non-boolean globals directly (e.g. <c>mov global_30000, ax</c> in
        /// <c>UI_showItem</c> @0x5A778, which seeds the item-description dialog's Var-0 branch with
        /// the object id), so the overlay — already an int map — needs a value setter, not just the
        /// boolean <see cref="SetGlobalFlag"/>.</summary>
        public void SetGlobalValue(int key, int value) {
            // *** SOME "FLAGS" ARE GAME-STATE FIELDS. *** The event space has three regions and only
            // two are bitmaps: between them sits a switch onto named GameState fields
            // (gstate_event_write, GSTATE.C:118). Dropping those into the overlay stores them where
            // nothing reads, so the effect simply does not happen — and the shipped dialogs write
            // these ids 38 times, including the world-loop exit that ends a chapter.
            GameStateEventFields.Field field = GameStateEventFields.FieldFor(key);
            if (field != GameStateEventFields.Field.None) {
                WriteEventField(field, GameStateEventFields.ValueWritten(field, value));
                return;
            }
            (_flagOverlay ??= new Dictionary<int, int>())[key] = value;
        }

        /// <summary>
        /// Applies an event write that targets a named field rather than a flag.
        /// </summary>
        /// <remarks>
        /// Only the fields the session OWNS are routed. The three dialog scratch values
        /// (<c>lEvtArg*</c>) and the arg count live in the dialog layer, not here, so they keep
        /// falling through to the overlay where the dialog code already reads them — routing them
        /// into fields nothing reads would be worse than leaving them.
        /// </remarks>
        private void WriteEventField(GameStateEventFields.Field field, int value) {
            switch (field) {
                case GameStateEventFields.Field.Chapter:
                    Chapter = value;
                    break;
                case GameStateEventFields.Field.PartyDeathState:
                    PartyDeathState = (byte)value;
                    break;
                case GameStateEventFields.Field.WorldLoopExitRequest:
                    ChapterTransitionPending = (byte)value;
                    break;
                case GameStateEventFields.Field.ClearLastActionSnapshot:
                    LastRestTicks = 0;
                    break;
                default:
                    // A dialog scratch value: the overlay is where the dialog layer reads it.
                    (_flagOverlay ??= new Dictionary<int, int>())[
                        GameStateEventFields.FieldBase + FieldOffset(field)] = value;
                    break;
            }
        }

        /// <summary>The event-id offset a field sits at, for the scratch values kept in the overlay.</summary>
        private static int FieldOffset(GameStateEventFields.Field field) => field switch {
            GameStateEventFields.Field.EventArgCount => 0,
            GameStateEventFields.Field.EventArgGoldCost => 14,
            GameStateEventFields.Field.EventArgValue => 15,
            GameStateEventFields.Field.EventArgAuxValue => 18,
            _ => 0,
        };

        /// <summary>
        /// The flags this session has changed, for the save writer.
        /// </summary>
        /// <remarks>
        /// <b>Only what CHANGED.</b> The rest of the story state is already in the backing body and
        /// the writer applies these onto it, so a session that set one flag does not rewrite 1113
        /// bytes from a partial view.
        ///
        /// <para>The game-state fields that share this key space
        /// (<see cref="GameStateEventFields"/>) never reach the overlay — <see cref="SetGlobalValue"/>
        /// routes them to their own fields — so nothing here needs to filter them out.</para>
        /// </remarks>
        public IReadOnlyDictionary<int, int> DirtyGlobalFlags =>
            _flagOverlay ?? (IReadOnlyDictionary<int, int>)EmptyFlags;

        private static readonly Dictionary<int, int> EmptyFlags = new Dictionary<int, int>();

        /// <summary>
        /// Combat records changed by fights this session, for the save writer.
        /// </summary>
        /// <remarks>
        /// <b>Staged at the END of each fight, not read at save time.</b>
        /// <c>CombatRuntime.CollectDirtyCombatantEdits</c> can only answer while an encounter is
        /// live, and by the time a save runs the fight has been left and the roster dropped — so
        /// asking then returns an empty list for ever, which is precisely how enemy damage went
        /// missing. <c>HotspotService</c> collects on the way out instead.
        ///
        /// <para><b>Keyed by slot, last fight wins.</b> Two fights involving the same actor stage
        /// the later record; a list would carry both and let the writer apply them in whatever order
        /// they happened to be added.</para>
        ///
        /// <para>Not cleared by a save, exactly like <see cref="DirtyGlobalFlags"/>: the writer
        /// patches these onto a clone of the backing body every time, so re-applying the same record
        /// is a no-op and dropping them would lose the damage on the SECOND save.</para>
        /// </remarks>
        public IReadOnlyList<DirtyCombatantEdit> DirtyCombatantEdits =>
            _combatantEdits == null
                ? EmptyCombatantEdits
                : (IReadOnlyList<DirtyCombatantEdit>)new List<DirtyCombatantEdit>(_combatantEdits.Values);

        private static readonly DirtyCombatantEdit[] EmptyCombatantEdits = new DirtyCombatantEdit[0];

        private Dictionary<int, DirtyCombatantEdit> _combatantEdits;

        /// <summary>
        /// Stage what a finished fight did to its enemies, to be written by the next save.
        /// </summary>
        /// <param name="edits">
        /// <c>CombatRuntime.CollectDirtyCombatantEdits</c>'s answer. Null or empty is a no-op, which
        /// is what a fight nobody was hurt in produces.
        /// </param>
        /// <summary>
        /// Enemy actor records changed by fights this session, for the save writer.
        /// </summary>
        /// <remarks>
        /// The 95-byte counterpart of <see cref="DirtyCombatantEdits"/>, staged at the same moment
        /// and for the same reason: health lives in the actor record, and the roster is gone by the
        /// time a save runs. Keyed by actor slot, later fight wins.
        /// </remarks>
        public IReadOnlyList<DirtyRosterActorEdit> DirtyRosterActorEdits =>
            _rosterActorEdits == null
                ? EmptyRosterEdits
                : (IReadOnlyList<DirtyRosterActorEdit>)new List<DirtyRosterActorEdit>(_rosterActorEdits.Values);

        private static readonly DirtyRosterActorEdit[] EmptyRosterEdits = new DirtyRosterActorEdit[0];

        private Dictionary<int, DirtyRosterActorEdit> _rosterActorEdits;

        /// <inheritdoc cref="StageCombatantEdits"/>
        public void StageRosterActorEdits(IReadOnlyList<DirtyRosterActorEdit> edits) {
            if (edits == null || edits.Count == 0) {
                return;
            }
            _rosterActorEdits ??= new Dictionary<int, DirtyRosterActorEdit>();
            foreach (DirtyRosterActorEdit edit in edits) {
                _rosterActorEdits[edit.ActorSlot] = edit;
            }
        }

        public void StageCombatantEdits(IReadOnlyList<DirtyCombatantEdit> edits) {
            if (edits == null || edits.Count == 0) {
                return;
            }
            _combatantEdits ??= new Dictionary<int, DirtyCombatantEdit>();
            foreach (DirtyCombatantEdit edit in edits) {
                _combatantEdits[edit.ActorSlot] = edit;
            }
        }

        // Test seam (kept for existing tests): same in-session overlay.
        internal void SetGlobalFlagForTest(int key, bool set) => SetGlobalFlag(key, set);

        /// <summary>
        /// Give a party slot live attributes without hydrating a save — the seam spell costs,
        /// combat write-back and upkeep all need to be assertable.
        /// </summary>
        /// <remarks>
        /// Same shape and intent as <see cref="SetZoneContainersForTest"/>: internal, test-only, and
        /// writing the same array <see cref="StatsOf"/> reads, so a test exercises the real lookup
        /// rather than a parallel one.
        /// </remarks>
        // Test seam: give a party position a pack without hydrating a save. Keyed the same way
        // GetActorInventory reads it, so a test and the game disagree about nothing.
        internal void SetActorInventoryForTest(int characterIndex, RuntimeContainer pack) =>
            _runtimeContainersByActor[characterIndex] = pack;

        internal void SetActorStatsForTest(int characterIndex, ActorStat[] stats) {
            if (characterIndex < 0) {
                return;
            }
            if (characterIndex >= _actorStats.Length) {
                var grown = new ActorStat[characterIndex + 1][];
                Array.Copy(_actorStats, grown, _actorStats.Length);
                _actorStats = grown;
            }
            _actorStats[characterIndex] = stats;
        }

        /// <summary>
        /// Give a party character its condition ranks without hydrating a save.
        /// </summary>
        /// <remarks>
        /// Writes the same array <see cref="ConditionsOf"/> reads, so a test exercises the real
        /// lookup rather than a parallel one — the shape <see cref="SetActorStatsForTest"/> uses.
        /// </remarks>
        internal void SetActorConditionsForTest(int characterIndex, ActorConditions conditions) {
            if (characterIndex < 0) {
                return;
            }
            if (characterIndex >= _actorConditions.Length) {
                var grown = new ActorConditions[characterIndex + 1];
                Array.Copy(_actorConditions, grown, _actorConditions.Length);
                _actorConditions = grown;
            }
            _actorConditions[characterIndex] = conditions;
        }

        /// <summary>
        /// Give an ENCOUNTER ROSTER slot stats and a creature class without hydrating a save.
        /// </summary>
        /// <remarks>
        /// <b>Deliberately separate from <see cref="SetActorStatsForTest"/>, because the two tables
        /// are separate.</b> That seam writes the six-entry party array behind
        /// <see cref="StatsOf"/>; this one writes the 1730-slot roster table behind
        /// <see cref="RosterStatsOf"/>. Conflating them is exactly the bug this seam exists to catch:
        /// a roster lookup that reaches into the party array skips real enemies and fields party
        /// members as enemies, and no test built on a null session can see it.
        /// </remarks>
        internal void SetRosterActorForTest(int actorSlot, ActorStat[] stats, int creatureType = 0,
            byte gridX = 0, byte gridY = 0, bool dead = false) {
            if (actorSlot < 0) {
                return;
            }
            if (actorSlot >= _rosterStats.Length) {
                var grown = new ActorStat[actorSlot + 1][];
                Array.Copy(_rosterStats, grown, _rosterStats.Length);
                _rosterStats = grown;
            }
            if (actorSlot >= _creatureTypes.Length) {
                var grownTypes = new int[actorSlot + 1];
                Array.Copy(_creatureTypes, grownTypes, _creatureTypes.Length);
                _creatureTypes = grownTypes;
            }
            if (actorSlot >= _gridPositions.Length) {
                var grownPos = new (byte, byte)[actorSlot + 1];
                Array.Copy(_gridPositions, grownPos, _gridPositions.Length);
                _gridPositions = grownPos;
            }
            _rosterStats[actorSlot] = stats;
            _creatureTypes[actorSlot] = creatureType;
            _gridPositions[actorSlot] = (gridX, gridY);

            // *** THE COMBAT RECORD TOO, because that is where "is it dead" lives. ***
            // RosterActorIsFlaggedDead reads CombatStatus (the CombatantState flags byte), which is
            // what rgnenc_load_encounter_actors tests — so a fixture that sets only the STATS can
            // not express a dead actor at all, and one that expresses it by zeroing health is
            // asserting a rule the game does not have.
            if (actorSlot >= _combatActors.Length) {
                var grownCombat = new SaveGameCombatData[actorSlot + 1];
                Array.Copy(_combatActors, grownCombat, _combatActors.Length);
                _combatActors = grownCombat;
            }
            _combatActors[actorSlot] = new SaveGameCombatData(
                targetActorPointer: 0, creatureType: (short)creatureType,
                xOnGrid: gridX, yOnGrid: gridY, targetXOnGrid: 0, targetYOnGrid: 0,
                combatStatus: (byte)(dead ? (int)GameData.Resources.Combat.CombatantFlags.Dead : 0),
                animEffectType: 0, activeSpellEffectSlot: 0, unusedPadding: 0, animDurationTimer: 0,
                monsterSpellAbility: 0, meleeAttackType: 0, rangedAttackType: 0, movementAiType: 0,
                preferredArrowType: 0, lastSpellSymbolFile: 0, floatingDamageValue: 0,
                floatingDamageTimer: 0);
        }

        /// <summary>Point an encounter number at actor slots without hydrating a save.</summary>
        internal void SetEncounterRosterForTest(int encounterNumber, params short[] actorSlots) =>
            _encounterRosters[encounterNumber] = new List<short>(actorSlots ?? Array.Empty<short>());

        /// <summary>
        /// Drop all session state. Called when the player returns to the main menu
        /// so subsequent New Game / Load paths start from a clean slate.
        /// </summary>
        public void Clear() {
            IsActive = false;
            Source = GameSessionSource.Unloaded;
            _state = null;
            _flagOverlay = null;
            _dialogKeyObjectIdOverride = null;
            _dialogSecondaryActorOverride = null;
            _dialogTertiaryActorOverride = null;
            DialogsPlaying = 0;   // no play survives a load or a clear
            Chapter = 0;
            MapIcon = 0;
            MapMarkerVisible = false;
            MapMarkerXPercent = 0f;
            MapMarkerYPercent = 0f;
            MapMarkerIcon = 0;
            PartyGold = 0;
            GameTimeIn2Seconds = 0;
            PartyDeathState = 0;
            ChapterTransitionPending = 0;
            PreviousZone = 0;
            CurrentZone = 0;
            SubTileStepCount = 0;
            TileBoundaryCrossed = GameData.Resources.World.WorldStepTick.OnBoundary;
            PositionX = PositionY = PositionZ = 0;
            Rotation = 0;
            PartyActorNames = Array.Empty<string>();
            ActivePartyCount = 0;
            ActivePartyIndices = Array.Empty<byte>();
            PartyActors = Array.Empty<SaveGameActorData>();
            _actorStats = Array.Empty<ActorStat[]>();
            _actorConditions = Array.Empty<ActorConditions>();
            ZoneContainers = new SaveGameZoneContainerStateData(Array.Empty<SaveGameZoneContainerEntryData>());
            ObjectInfo = null;
            _runtimeContainersByLocation.Clear();
            _runtimeContainersByActor.Clear();
            _runtimeContainerRecords.Clear();
        }
    }

    /// <summary>
    /// Where a discarded item is going: the container that receives it, and whether that container
    /// is a bag the drop just claimed (as opposed to something already standing on the spot). The
    /// distinction drives the "shoved it into a spare bag" line and whether a world entity has to
    /// be spawned. A default value — <see cref="Container"/> null — means the zone had no slot to
    /// give and the drop must be refused.
    /// </summary>
    public readonly struct GroundDropTarget {
        public readonly RuntimeContainer Container;

        /// <summary>The container is a freshly claimed bag, not a pre-existing container.</summary>
        public readonly bool IsNewBag;

        /// <summary>The claim recycled a live bag because the zone's pool was exhausted, so an
        /// older pile's contents were destroyed (faithful to <c>actorspawn_enc_location</c>).</summary>
        public readonly bool Recycled;

        public GroundDropTarget(RuntimeContainer container, bool isNewBag, bool recycled) {
            Container = container;
            IsNewBag = isNewBag;
            Recycled = recycled;
        }

        public bool Resolved => Container != null;
    }

    public enum GameSessionSource {
        Unloaded = 0,
        NewGame,
        LoadSave,
    }
}
