namespace BakAgain.Combat {
    using System.Collections.Generic;
    using BakAgain.Core;
    using Cysharp.Threading.Tasks;
    using GameData;
    using GameData.Resources.Inventory;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using GameData.Resources.Spells;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Entering and leaving a tactical encounter, and driving its rounds — the matched pair the
    /// combat umbrella owns (docs/specs/combat-arena.md §"Entering and leaving are a matched pair").
    ///
    /// <para>The turn loop itself is <see cref="CombatEncounter"/> in the engine-independent layer;
    /// this is the Unity-side lifecycle around it: build the sides out of <see cref="GameSession"/>,
    /// advance rounds, and write the outcome back to the party on the way out.</para>
    ///
    /// <para><b>Deliberately has no screen, no camera and no AI.</b> Those are TASK-102, TASK-103 and
    /// TASK-97. This type is drivable and assertable with no scene at all, which is the point — the
    /// rules that decide a fight should be testable without a fight being rendered.</para>
    ///
    /// <para><b>The world zone stays loaded.</b> The original unloads it on entry and reloads it on
    /// exit, but that is its DOS memory strategy, not game logic — see
    /// <see cref="CombatModeEntry.UnloadsTheWorldZone"/> and the "don't rebuild the world for combat"
    /// rule in docs/unity-systems-map.md.</para>
    /// </summary>
    public sealed class CombatRuntime {
        private readonly GameSession _session;
        private readonly ILogger _logger;
        private readonly GameData.Resources.Combat.PartyCombatEntries _partyEntries;
        private readonly GameData.Resources.Object.ObjectInfoSet _objects;

        // An enemy's stat block, kept because a Combatant carries only three numbers and a swing
        // needs eight. Keyed by the combatant itself: a roster slot is not unique across a fight
        // (two wolves share one template) and ClassId means the creature type, not the table row.
        private readonly Dictionary<Combatant, ActorStat[]> _enemyStats =
            new Dictionary<Combatant, ActorStat[]>();

        // The actor-table slot each enemy came from. Kept for the same reason its stats are: the
        // Combatant does not carry it, and a saved combat record is keyed by exactly this number.
        private readonly Dictionary<Combatant, int> _enemySlots = new Dictionary<Combatant, int>();

        // Each enemy's place in its encounter roster, and the encounter's number. Together they address
        // the body container the original binds as a monster's actor_record — see PackOf.
        private readonly Dictionary<Combatant, int> _enemyRosterIndex = new Dictionary<Combatant, int>();
        private int _encounterNumber = -1;

        // Which encounter record this fight belongs to, so a removal can be written where the save
        // expects it. Unknown outside a roster fight (a test encounter, a summoned skirmish), which
        // is an ordinary case: the original also declines to persist what it cannot address.
        private GameData.Resources.World.EncounterActorPersistence.RecordAddress _encounterAddress =
            GameData.Resources.World.EncounterActorPersistence.RecordAddress.None;

        /// <summary>The live encounter, or null when the party is not in combat.</summary>
        public CombatEncounter Encounter { get; private set; }

        public bool InCombat => Encounter != null;

        // Plays a sound by id. Optional: the rules are testable without one, and the world runs
        // headless in tests where there is nothing to play through.
        private readonly System.Action<int> _playSfx;

        /// <summary>Plays a spell effect arm's sound sequence, keyed by its effect kind.</summary>
        /// <remarks>
        /// Separate from <see cref="_playSfx"/> because an arm is a SEQUENCE with waits and an
        /// explicit stop, which this rules object has no way to run — see
        /// <c>SpellEffectArmSound</c> for the rules and the audio layer for the timing.
        /// </remarks>
        private readonly System.Action<int> _playSpellArm;

        /// <summary>
        /// Put a struck combatant into the recoil state — <c>markActorHit</c> @0x6157d.
        /// </summary>
        /// <remarks>
        /// <b>A recoil is a COLOUR FLASH, not a displacement.</b> The remap index is what the sprite
        /// is redrawn through; an ordinary blow uses <see cref="HitReaction.BlowRemap"/>, read off
        /// <c>resolveSwingAttack</c>'s own call rather than chosen. Spells pass their own values and
        /// are deliberately left out until each one is read.
        ///
        /// <para><b>Nothing draws this yet</b> — the tint needs the zone's RMP block applied to the
        /// sprite's palette. The state is faithful and ticked; the picture is TASK-103's next slice.</para>
        /// </remarks>
        private static void MarkHit(Combatant struck, bool landed, int dealt,
            int remapIndex = HitReaction.BlowRemap) {
            if (struck == null || !landed) {
                return;   // apply_damage returned before the float (COMBAT.C:330-350)
            }
            if (dealt <= 0) {
                struck.DamageFloat = 0;   // "miss", and no CAF_KNOCKBACK (COMBAT.C:386-389)
                return;
            }
            // The floating number (COMBAT.C:382-385): the damage when it is under 1000, else nothing.
            struck.DamageFloat = dealt < 1000 ? dealt : (int?)null;
            (CombatantFlags flags, int timer, int remap) =
                HitReaction.Begin(struck.Flags, remapIndex);
            struck.Flags = flags;
            struck.HitReactionTimer = timer;
            struck.HitReactionRemap = remap;
        }

        /// <summary>An actor that attacks stops recoiling — the clear inside <c>resolveSwingAttack</c>.</summary>
        private static void ClearHitReaction(Combatant actor) {
            if (actor == null) {
                return;
            }
            actor.Flags &= ~CombatantFlags.Knockback;
            actor.HitReactionTimer = 0;
        }

        /// <summary>
        /// Count every recoil down by one combat-view redraw — <c>tickHitReactionTimers</c> @0x61598.
        /// </summary>
        /// <remarks>
        /// <b>A tick is a REDRAW, not a unit of time.</b> The original's only caller is
        /// <c>RenderWorldView</c>, so a recoil lasts two redraws of whatever animation is playing
        /// rather than two frames or a fixed duration — see <see cref="HitReaction"/> for why both
        /// of those readings are wrong.
        /// </remarks>
        public void TickHitReactions() {
            if (Encounter == null) {
                return;
            }
            foreach (Combatant c in Encounter.AllCombatants()) {
                if (c == null) {
                    continue;
                }
                (CombatantFlags flags, int timer) = HitReaction.Tick(c.Flags, c.HitReactionTimer);
                c.Flags = flags;
                c.HitReactionTimer = timer;
            }
        }

        /// <summary>Raises a DDX dialog by id, for the few outcomes the player has to be told about.</summary>
        /// <remarks>
        /// A settable property rather than a constructor argument, matching the other collaborators
        /// this class is handed after construction. Null in tests that do not care.
        /// </remarks>
        public System.Action<int> ShowDialog { get; set; }

        /// <summary>
        /// Puts a pack on screen for the player to take from — Nightfingers' theft, which the original runs
        /// through <c>combat_arena_suspend_char_screen</c> on the TARGET. Null leaves the theft unshown.
        /// </summary>
        public System.Action<RuntimeContainer> OpenStolenPack { get; set; }

        /// <summary>Whether the zone this fight is in is underground — <c>g_game_mode == 2</c>.</summary>
        /// <remarks>
        /// <b>A supplier rather than a value, because the runtime outlives the zone.</b>
        /// <c>HotspotService.Combat</c> is <c>??=</c>-cached for the life of the service and the
        /// service outlives any one zone, so a bool captured when it was built would answer for
        /// whichever zone happened to be loaded first — the same trap that froze the cast screen's
        /// zone definition for a whole session.
        ///
        /// <para>Null reads as above ground, which is the behaviour before this existed.</para>
        /// </remarks>
        public System.Func<bool> Underground { get; set; }

        public CombatRuntime(GameSession session, ILogger logger = null,
            GameData.Resources.Combat.PartyCombatEntries partyEntries = null,
            GameData.Resources.Object.ObjectInfoSet objects = null,
            System.Action<int> playSfx = null,
            System.Action<int> playSpellArm = null) {
            _session = session;
            _logger = logger;
            _partyEntries = partyEntries;
            _objects = objects;
            _playSfx = playSfx;
            _playSpellArm = playSpellArm;
        }

        /// <summary>
        /// SPELLWEA — which creatures take DOUBLE from which spell (TASK-115).
        /// </summary>
        /// <remarks>
        /// <b>Set it or the doubling never happens; leaving it null is the old behaviour, not a
        /// neutral default.</b> It is a property rather than a constructor argument because it is
        /// loaded asynchronously and the runtime is built synchronously — the same reason
        /// HotspotService preloads the other combat tables per zone.
        ///
        /// <para><b>A party member needs no guard, and that is checked rather than assumed.</b>
        /// The lookup is by creature type, which a party member does not meaningfully have (it
        /// reads 0) — but no live spell lists creature 0 in either table. SPELLWEA has 20 creature
        /// references and none is 0; SPELLRES has 111 and the only one is on spell 58, which is
        /// past the 45-spell keyspace and never read. So the tables cannot fire on a party member
        /// today, and a guard here would be dead code hiding that fact.</para>
        /// </remarks>
        public GameData.Resources.Spells.SpellAffinityTable SpellWeakness { get; set; }

        /// <summary>SPELLRES — which creatures RESIST which spell. <inheritdoc cref="SpellWeakness"/></summary>
        public GameData.Resources.Spells.SpellAffinityTable SpellResistance { get; set; }

        /// <summary>
        /// SPELLS.DAT, for the casts this runtime raises ITSELF rather than being handed.
        /// </summary>
        /// <remarks>
        /// <b>Every other cast arrives with its record already looked up</b> — the caller finds it
        /// in <c>SpellList.Spells</c> and passes it to <see cref="ResolveCast"/>. A cannon on a trap
        /// grid has no caller: it fires from inside a walk, several frames below anyone holding the
        /// table, so the table has to be reachable from here.
        ///
        /// <para>Set alongside the affinity tables in the one factory, for the same reason they are
        /// — the list loads asynchronously while this runtime is built on demand, so a second
        /// construction site that forgot it would silently stop cannons firing with no error.</para>
        /// </remarks>
        public GameData.Resources.Spells.SpellList Spells { get; set; }

        /// <summary>
        /// Where a cast's picture goes — (visual, from, to). The rules only name the visual
        /// (<see cref="SpellVisuals"/>); the arena draws it (TASK-117). Null in tests and outside
        /// the world, where nothing is drawn.
        /// </summary>
        public System.Action<SpellVisual, Combatant, Combatant> PlaySpellVisual { get; set; }

        /// <summary>The arena's grid cell size in world units (<c>START.DAT</c>, 300) — what a missed
        /// projectile's flight is stepped against (<see cref="SpellProjectileMiss"/>).</summary>
        public int ArenaCellSize { get; set; } = 300;

        /// <summary>
        /// The EXE-resident combat tables, for the class-vs-item affinity a swing is scaled by.
        /// </summary>
        /// <remarks>
        /// <b>Null is the old behaviour, not a neutral default.</b> Every swing in the game ran with
        /// a hard-coded modifier of 0 because the ROW selector for these tables
        /// (<c>g_aClassCombatGroup</c>) was never extracted — only the modifier table beside it was.
        /// <see cref="GameData.Resources.Combat.CombatAffinityTables.ModifierFor"/> answers 0 for an
        /// unloaded table, so a fight built without one behaves exactly as it did.
        /// </remarks>
        public GameData.Resources.Combat.CombatAffinityTables Affinity { get; set; }

        /// <summary>The class-vs-item affinity for this combatant holding this item.</summary>
        private int AffinityFor(Combatant actor, GameData.Resources.Object.ObjectInfo item) =>
            Affinity == null || item == null ? 0 : Affinity.ModifierFor(CreatureClassOf(actor), (int)item.Race);

        /// <summary>Whether this target takes double from this spell.</summary>
        public bool TargetIsVulnerableTo(Combatant target, int spellId) =>
            target != null && SpellWeakness != null && SpellWeakness.Lists(spellId, CreatureClassOf(target));

        /// <summary>Whether this target resists this spell.</summary>
        public bool TargetResists(Combatant target, int spellId) =>
            target != null && SpellResistance != null && SpellResistance.Lists(spellId, CreatureClassOf(target));

        /// <summary>
        /// Build an encounter from the session's active party and the given opposing roster.
        /// </summary>
        /// <remarks>
        /// <b>Side A is the party and side B is the encounter roster</b>, not the other way round —
        /// see <see cref="CombatModeEntry.TableAIsThePartyRoster"/>. The party side is copied out of
        /// the save's ACTIVE party in slot order, which is why <see cref="Combatant.PartySlot"/> is
        /// 1-based here: slot 0 means "not a party member" to <see cref="Combatant.IsPartyMember"/>.
        /// </remarks>
        /// <summary>
        /// The party surprised this encounter, so every enemy due to act before their first turn
        /// forfeits it — <see cref="CombatEncounterOpening.EnemiesForfeitTheirOpeningTurn"/>.
        /// </summary>
        /// <remarks>
        /// <b>Cleared by the first party turn, not by the first enemy.</b> The original's loop runs
        /// until the picker lands on a party member, so the drop covers however many enemies are due
        /// in that stretch — one forfeit each, and none afterwards.
        /// </remarks>
        public bool PartyHasTheDrop {
            get => _partyHasTheDrop;
            set => _partyHasTheDrop = value;
        }

        private bool _partyHasTheDrop;

        public CombatEncounter Enter(IReadOnlyList<Combatant> enemies, TrapPuzzle puzzle = null) {
            if (InCombat) {
                _logger?.LogWarning("CombatRuntime.Enter called while already in combat; ignoring.");
                return Encounter;
            }

            // *** DERIVED FROM THE PUZZLE, NOT PASSED ALONGSIDE IT. *** Both of these are facts about
            // the encounter's TRAPS.DAT record, and taking them as separate arguments let a caller
            // hand over a grid that disagreed with them. Reading them off the thing they describe is
            // what makes that impossible rather than merely unlikely.
            var encounter = new CombatEncounter {
                HasObjective = puzzle != null && TrapPuzzleGoal.IsTrapPuzzle(puzzle.Grid),
                // The row that objective is satisfied by, scanned once here because the terrain
                // does not move. Without it HasObjective is a condition with no way to discharge it.
                ObjectiveExitRow = puzzle != null && TrapPuzzleGoal.IsTrapPuzzle(puzzle.Grid)
                    ? TrapPuzzleGoal.ExitRow(puzzle.Grid)
                    : (int?)null,
                EscapeAllowed = puzzle?.AllowsRetreat ?? true,
            };

            byte[] active = _session?.ActivePartyIndices ?? System.Array.Empty<byte>();
            for (var slot = 0; slot < active.Length && slot < CombatModeEntry.SideSlots; slot++) {
                encounter.Party.Add(PartyCombatant(active[slot], slot));
            }

            if (enemies != null) {
                foreach (Combatant enemy in enemies) {
                    if (encounter.Enemies.Count >= CombatModeEntry.SideSlots) {
                        break;
                    }
                    enemy.PartySlot = 0;   // whatever the caller built, this side is not the party
                    encounter.Enemies.Add(enemy);
                }
            }

            PlaceCombatants(encounter, puzzle);
            // The party with its back to the camera, the enemies facing it (COMBAT.C:131-150).
            encounter.DeployFacings();
            encounter.BeginRound();
            Encounter = encounter;
            _logger?.LogInformation("Combat entered: {Party} party vs {Enemies} enemies.",
                encounter.Party.Count, encounter.Enemies.Count);
            return encounter;
        }

        /// <summary>
        /// Enter combat against an encounter's saved roster — the seven actor slots a Comb record
        /// points at, as <c>SaveGameWorldData.EnemyPartyActorSlots</c> holds them.
        /// </summary>
        /// <remarks>
        /// <b>A roster slot indexes the save's 1730-entry actor table, NOT the party.</b> That
        /// distinction is the whole correctness of this method: <see cref="GameSession.StatsOf"/>
        /// reads the six-entry party array, so resolving a roster slot through it skips nearly every
        /// enemy and, for a slot under six, fields a PARTY MEMBER as the enemy.
        /// <see cref="GameSession.RosterStatsOf"/> is the right table.
        ///
        /// <para><b>Empty slots are already gone.</b> The extractor drops the -1 fillers when it reads
        /// the record, so a short roster is an ordinary two-enemy encounter rather than a list with
        /// holes in it. A slot with no stats behind it is skipped rather than admitted at zero health,
        /// which would otherwise join the fight already dead.</para>
        ///
        /// <para><b><see cref="Combatant.ClassId"/> carries the creature class</b> — what it is
        /// documented to mean and what the species-keyed rules read: the always-acts class, the ones
        /// that vanish on death, the conjured ones that unsummon. Deliberately not the actor slot,
        /// since a slot numbered 54, 56 or 57 would then be immune or unsummonable by coincidence.</para>
        /// </remarks>
        public CombatEncounter EnterRoster(IReadOnlyList<short> actorSlots, TrapPuzzle puzzle = null,
            GameData.Resources.World.EncounterActorPersistence.RecordAddress? encounterAddress = null,
            int encounterNumber = -1) {
            _encounterAddress = encounterAddress
                ?? GameData.Resources.World.EncounterActorPersistence.RecordAddress.None;
            _encounterNumber = encounterNumber;
            var enemies = new List<Combatant>();
            if (actorSlots != null) {
                for (var rosterIndex = 0; rosterIndex < actorSlots.Count; rosterIndex++) {
                    short actorSlot = actorSlots[rosterIndex];
                    ActorStat[] stats = _session?.RosterStatsOf(actorSlot);
                    if (stats == null) {
                        _logger?.LogWarning("Roster actor slot {Slot} has no stats; skipping.", actorSlot);
                        continue;
                    }
                    RecoverSinceLastFought(stats, encounterNumber);
                    (int X, int Y) saved = _session.GridPositionOf(actorSlot);
                    var enemy = new Combatant {
                        PartySlot = 0,
                        ClassId = _session.CreatureTypeOf(actorSlot),
                        X = saved.X,
                        Y = saved.Y,
                        Health = StatValue(stats, ActorAttribute.Health),
                        Stamina = StatValue(stats, ActorAttribute.Stamina),
                        Speed = StatValue(stats, ActorAttribute.Speed),
                        Flags = CombatantFlags.Ready,
                        // The rows this actor walks, as rolled for it (TASK-528).
                        AiPatterns = _session.AiPatternsOf(actorSlot),
                    };
                    // *** Held onto, not read and dropped. *** Everything a swing needs beyond
                    // health lives here — accuracy, strength, defense — and re-reading it later is
                    // impossible: the Combatant does not carry the slot it came from.
                    _enemyStats[enemy] = stats;
                    _enemySlots[enemy] = actorSlot;
                    _enemyRosterIndex[enemy] = rosterIndex;
                    enemies.Add(enemy);
                }
            }
            return Enter(enemies, puzzle);
        }

        /// <summary>
        /// The MONSTXX.DAT templates, keyed by creature type — what a summon's stats are rolled
        /// from.
        /// </summary>
        /// <remarks>
        /// Set alongside the affinity tables in the one factory. Null leaves
        /// <see cref="Summon"/> unable to spawn anything, which is the behaviour before this
        /// existed rather than a silent zero-stat creature.
        /// </remarks>
        public IReadOnlyDictionary<int, GameData.Resources.Monster.MonsterStats> MonsterTemplates {
            get; set;
        }

        /// <summary>
        /// A summoning SPELL — <c>cspell_resolve_cast</c>'s kind-6 arm: the creature, then the bill.
        /// </summary>
        /// <remarks>
        /// <b>A summon spell is still a cast.</b> The original reaches <c>cspell_summon_monster</c>
        /// from inside <c>cspell_resolve_cast</c> (CSPELL.C:1329), whose kind-5/6/8 tail then bills the
        /// caster through <c>cspell_apply_damage_armor_wear</c> (CSPELL.C:1536), and the arena clears
        /// <c>CAF_READY</c> straight after (COMBAT.C:2426). Placing the creature through
        /// <see cref="Summon"/> alone — which the ITEM path rightly does, the item having been spent —
        /// made the spell free. The caster is billed even if the field is full, because the original's
        /// tail runs whether or not the append succeeded.
        /// </remarks>
        /// <returns>The conjured creature, or null when the spell names none or the fight has no room.</returns>
        public Combatant SummonByCast(Combatant caster, GameData.Resources.Spells.Spell spell, int spellId,
            int power, int x, int y, System.Func<int, int> rnd = null) {
            if (spell?.SummonedCreatureId is not { } creature) {
                return null;
            }
            Combatant summoned = Summon(creature, x, y, rnd: rnd);
            ResolveCast(caster, null, spell, spellId, power, rnd, groundCell: (x, y));
            return summoned;
        }

        /// <summary>
        /// Put a conjured creature on the grid — <c>cspell_summon_monster</c>'s spawn half.
        /// </summary>
        /// <param name="creatureType">The creature class to conjure.</param>
        /// <param name="x">Grid cell.</param>
        /// <param name="y">Grid cell.</param>
        /// <param name="hasIntactCrossbow">
        /// Whether the creature carries intact category-2 gear — see
        /// <see cref="GameData.Resources.Monster.MonsterStatRoll.TemplateCreatureFor"/>. False for a
        /// spell-conjured creature, which arrives with nothing.
        /// </param>
        /// <param name="rnd">The <c>rnd(n)</c> seam; defaults to the real generator.</param>
        /// <returns>The new combatant, or null when there is no template or no fight.</returns>
        /// <remarks>
        /// <b>This is the path <see cref="EnterRoster"/> could not provide.</b> That one builds
        /// enemies from session roster slots and reads their stats off the save; a conjured creature
        /// has neither a slot nor saved stats, which is why summoning needed a second entrance
        /// rather than an extra argument.
        ///
        /// <para><b>The stat block is registered in <c>_enemyStats</c>, and that is not
        /// bookkeeping.</b> A swing reads accuracy, strength and defence from there — a
        /// <see cref="Combatant"/> carries only health, stamina and speed — so a summon left out of
        /// that map fights at zero on every stat the map holds. It is deliberately NOT given an
        /// <c>_enemySlots</c> entry: it has no roster slot, and inventing one would make the
        /// end-of-fight persistence write over a real actor.</para>
        ///
        /// <para><b>It lands NOT ready</b> (<see cref="MonsterSummon.InitialFlags"/> assigns rather
        /// than ORs), so it does not act on the round it appears — otherwise the spell hands the
        /// caster a free extra action.</para>
        ///
        /// <para><b>And morale is zeroed BEFORE the roll</b>, which is the whole mechanism behind a
        /// summon never routing: the roll reads the template's morale only when the field is already
        /// non-zero.</para>
        /// </remarks>
        public Combatant Summon(int creatureType, int x, int y, bool hasIntactCrossbow = false,
            System.Func<int, int> rnd = null) {
            if (Encounter == null || MonsterTemplates == null) {
                return null;
            }

            // *** THE FIGHT HOLDS SEVEN ACTORS, AND UNTIL NOW A SUMMON COULD NEVER FAIL FOR ROOM. ***
            // combat_actor_slot_append @0x5c502 refuses at seven before it does anything else, and
            // MonsterSummon.NoRoomDialog has been modelled the whole time with nothing able to reach
            // it.
            //
            // Encounter.Party IS that seven-actor array: the original builds the party into it
            // wholesale and a summon appends to the next free slot, which is exactly what Summon
            // does below. So the count is the list's own Count and needs no flag test — a full
            // six-member party leaves room for one summon, a party of three for four.
            if (!MonsterSummon.HasRoom(Encounter.Party.Count)) {
                if (_logger != null) {
                    BakAgain.Core.ConditionalLoggingExtensions.LogInformation(_logger,
                        "Summon refused: the fight already holds {Count} actors.",
                        Encounter.Party.Count);
                }
                // *** THE PLAYER IS TOLD, AND THE SPELL IS STILL SPENT. *** The original shows
                // ddx 145 and gives up; nothing refunds the cast. A refusal that only wrote to the
                // log looked to the player exactly like a spell that did nothing.
                ShowDialog?.Invoke(MonsterSummon.NoRoomDialog);
                return null;
            }

            int templateId = GameData.Resources.Monster.MonsterStatRoll.TemplateCreatureFor(
                creatureType, hasIntactCrossbow);
            if (!MonsterTemplates.TryGetValue(templateId,
                    out GameData.Resources.Monster.MonsterStats template)) {
                // Not `_logger?.LogWarning(...)`: with two arguments that call is ambiguous between
                // the project's ConditionalLoggingExtensions overload and Microsoft's. The explicit
                // form resolves it, but throws on a null logger where `?.` would not — and a
                // CombatRuntime is legitimately built without one (every unit test does).
                if (_logger != null) {
                    BakAgain.Core.ConditionalLoggingExtensions.LogWarning(_logger,
                        "Summon of creature {Creature} has no MONSTXX template ({Template}); nothing spawned.",
                        creatureType, templateId);
                }
                return null;
            }

            System.Func<int, int> roll = rnd ?? (n => UnityEngine.Random.Range(0, n));
            IReadOnlyDictionary<ActorAttribute, int> rolled =
                GameData.Resources.Monster.MonsterStatRoll.Roll(template, roll);

            ActorStat[] stats = StatBlockFrom(rolled);
            var summon = new Combatant {
                PartySlot = 0,
                // The creature's OWN type, not the template's: the substitution changes where the
                // numbers come from and nothing else about what the thing is.
                ClassId = creatureType,
                X = x,
                Y = y,
                Health = StatValue(stats, ActorAttribute.Health),
                Stamina = StatValue(stats, ActorAttribute.Stamina),
                Speed = StatValue(stats, ActorAttribute.Speed),
                Flags = MonsterSummon.InitialFlags,
                // MONSTAT.C:72-80 rolls the three rows right after the stats, overwriting the summon's 1s.
                AiPatterns = (
                    GameData.Resources.Monster.MonsterStatRoll.RollOne(template.SpellcastPattern.Min, template.SpellcastPattern.Max, roll),
                    GameData.Resources.Monster.MonsterStatRoll.RollOne(template.CrossbowPattern.Min, template.CrossbowPattern.Max, roll),
                    GameData.Resources.Monster.MonsterStatRoll.RollOne(template.MeleeMovePattern.Min, template.MeleeMovePattern.Max, roll)),
            };

            _enemyStats[summon] = stats;

            // *** A SUMMON FIGHTS FOR THE PARTY. *** cspell_summon_monster builds its actor with
            // combat_actor_party_add, which appends to g_combat_actors_A — and A is the party's
            // side: combat_arena_enter assigns Active = A and Other = B (COMBAT.C:125-130),
            // combatenc_is_encounter_actor scans OTHER, and the sprite index splits A from B at
            // 100, the enemy-roster base. It joined the enemies here, so every conjured creature
            // fought the party that called it. See TASK-268.
            //
            // <para>PartySlot stays 0. It is on the party's SIDE without being one of the party's
            // CHARACTERS, and IsPartyMember answers the second question — which is what keeps the
            // stat lookups, the inventory pack and WriteBack correct for it.</para>
            Encounter.Party.Add(summon);

            // *** THE TILE MUST BE MARKED, OR THE NEXT SUMMON STANDS ON THIS ONE. ***
            // cspell_summon_monster ends with combatgrid_tile_set_word(gridX, gridY, actor), which
            // is what makes the cell occupied. Without it SummonPlacement.Accepts keeps answering
            // yes for a cell that already has a creature on it — found by driving the Horn of Algon
            // Kokoon, whose ONE use is two placements: both conjured creatures landed on the same
            // cell and nothing complained.
            Grid?.SetOccupied(x, y, true);

            // *** THE CREATION CUE, AFTER THE PLACEMENT AND BEFORE THE ANIMATION. *** The original
            // plays it between the optional tile prompt and writing the grid position (0x67688), so
            // it sounds once the summon is committed rather than when the spell is chosen. It is
            // mcreate -- the same cue the lighting spells use, not one of its own.
            _playSfx?.Invoke(MonsterSummon.Sound);
            return summon;
        }

        /// <summary>
        /// Dannon's Delusions' decoy — <c>cspell_summon_actor</c> (CSPELL.C:752): a helpless copy of
        /// <paramref name="copyOf"/> that stands on the field until its spell runs out.
        /// </summary>
        /// <remarks>
        /// Every value is <see cref="DecoySummon"/>'s: the copied creature CLASS (Owyn's 16, not his
        /// roster index — see <see cref="CreatureClassOf"/>), 1 health, 1 stamina, speed 0, ready. What
        /// keeps it from acting is its effect slot, not its speed: the pool marks an actor under
        /// Dannon's Delusions incapacitated, and <see cref="CombatEncounter.BeginRound"/> removes it on
        /// the round that slot expires, as the original's status tick does.
        /// </remarks>
        /// <returns>The decoy, or null when there is no fight, no one to copy, or no room.</returns>
        public Combatant SummonDecoy(Combatant copyOf, int duration, int x, int y) {
            if (Encounter == null || copyOf == null || !MonsterSummon.HasRoom(Encounter.Party.Count)) {
                return null;
            }
            var decoy = new Combatant {
                PartySlot = 0,
                ClassId = DecoySummon.CreatureTypeFor(CreatureClassOf(copyOf)),
                X = x,
                Y = y,
                Health = DecoySummon.Health,
                Stamina = DecoySummon.Stamina,
                Speed = DecoySummon.Speed,
                Flags = DecoySummon.InitialFlags,
            };
            Encounter.Party.Add(decoy);
            Grid?.SetOccupied(x, y, true);
            Encounter.Effects.Register(decoy, DecoySummon.Spell, 0, duration);
            _playSfx?.Invoke(MonsterSummon.Sound);
            return decoy;
        }

        /// <summary>
        /// Whether this combatant was conjured onto the field rather than entered from a roster.
        /// </summary>
        /// <remarks>
        /// <b>On the party's SIDE without being one of the party's CHARACTERS</b> — which is what
        /// <see cref="Summon"/> builds and nothing else does. The original has the same
        /// discriminator by construction: <c>combat_actor_party_add</c> appends to
        /// <c>g_combat_actors_A</c> with no roster slot, and every other occupant of that array is a
        /// real party member.
        ///
        /// <para>Asked because a summon's AI differs from its kind's in one measurable way — see
        /// <see cref="MonsterSummon.Morale"/>. Reading <see cref="Combatant.IsPartyMember"/> alone
        /// cannot tell a summon from an enemy, and reading the side alone cannot tell it from a
        /// character.</para>
        /// </remarks>
        public bool IsSummoned(Combatant actor) =>
            actor != null && !actor.IsPartyMember
            && Encounter != null && Encounter.Party.Contains(actor);

        /// <summary>
        /// An attribute-indexed stat block from a rolled template.
        /// </summary>
        /// <remarks>
        /// <b>Base AND Max, both set to the rolled figure</b> — <c>stats[i].max = stats[i].base =
        /// result</c>. So the creature starts at full health by construction and its ceiling is its
        /// own roll rather than the template's; setting only Base leaves Max at zero, and a stat
        /// whose Max is 0 is INERT (<see cref="ActorStat.Max"/>), so every one of them would read
        /// as absent.
        /// </remarks>
        private static ActorStat[] StatBlockFrom(IReadOnlyDictionary<ActorAttribute, int> rolled) {
            var stats = new ActorStat[System.Enum.GetValues(typeof(ActorAttribute)).Length];
            foreach (KeyValuePair<ActorAttribute, int> entry in rolled) {
                var i = (int)entry.Key;
                if (i < 0 || i >= stats.Length) {
                    continue;
                }
                var value = (byte)System.Math.Clamp(entry.Value, 0, byte.MaxValue);
                stats[i] = new ActorStat { Base = value, Max = value };
            }
            return stats;
        }

        /// <summary>
        /// What the acting combatant may do this turn — the HUD's shared capability cell.
        /// </summary>
        /// <remarks>
        /// <b>Recomputed per turn, never cached.</b> Both predicates depend on the distance to the
        /// nearest living opponent, so an enemy closing to melee takes the option away and stepping
        /// clear gives it back. A value worked out once when the fight started is wrong from the
        /// second turn onward.
        ///
        /// <para><b>Both halves are real.</b> Shooting was stubbed to false while the quiver count
        /// and the equipped weapon were out of reach from a <see cref="Combatant"/>; they are
        /// reachable now and <see cref="CanShootNow"/> computes it.</para>
        /// </remarks>
        public (bool CanShoot, bool CanCast) CapabilitiesFor(Combatant actor) {
            if (actor == null || Encounter == null) {
                return (false, false);
            }

            // Opponents, not everyone: an ally beside you does not block your cast.
            //
            // *** SIDE IS WHICH LIST YOU ARE IN, NOT WHETHER YOU HAVE A CHARACTER. *** A summon is
            // on the party's side with PartySlot 0, so asking IsPartyMember here would hand it the
            // party as its opponents. See TASK-268.
            System.Collections.Generic.List<Combatant> opponents =
                Encounter.Party.Contains(actor) ? Encounter.Enemies : Encounter.Party;
            int nearest = CombatCapability.NearestOpponent(actor.X, actor.Y, opponents);

            int terrain = Grid != null ? (int)Grid.TerrainAt(actor.X, actor.Y) : 0;
            ActorStat[] stats = StatsFor(actor);

            bool canCast = CombatCapability.CanCast(
                terrain,
                nearest,
                CombatStat(actor, ActorAttribute.AccuracyCasting),
                StatValue(stats, ActorAttribute.Health),
                CombatCapability.ShippedHealthThresholds,
                chapterEightWithoutRequiredItem: false);

            return (CanShootNow(actor, terrain, nearest, stats), canCast);
        }

        /// <summary>
        /// Whether the actor can take a ranged shot — ammunition, an intact crossbow, room, and the
        /// skill to use it.
        /// </summary>
        /// <remarks>
        /// <b><see cref="Combatant.ClassId"/> is not a creature type for a party member</b>, so it
        /// must not be handed to <see cref="CombatCapability.CanShoot"/> as one: party ids are small
        /// and would never collide with
        /// <see cref="CombatCapability.InnatelyMissileCreature"/> today, but the coincidence is the
        /// kind that starts working by accident later. Zero is passed instead.
        /// </remarks>
        private bool CanShootNow(Combatant actor, int terrain, int nearest, ActorStat[] stats) {
            RuntimeContainer pack = actor.IsPartyMember
                ? _session?.GetActorInventory(actor.ClassId)
                : null;

            return CombatCapability.CanShoot(
                terrain,
                nearest,
                QuarrelInventory.Count(pack),
                EquippedGear.HasIntact(EquipmentSlots(pack), CombatCapability.RangedWeaponCategory),
                actor.IsPartyMember ? 0 : actor.ClassId,
                CombatStat(actor, ActorAttribute.AccuracyCrossbow));
        }

        /// <summary>
        /// How many quarrels of each kind the actor carries, in kind order — what the SHOOT menu is
        /// built from.
        /// </summary>
        /// <remarks>
        /// <b>Per kind, not a total.</b> <see cref="CapabilitiesFor"/> asks for the total, because
        /// the HUD's capability cell only needs to know whether the character has anything to shoot;
        /// the shoot menu needs to know WHICH kinds, because that is what decides both the buttons
        /// it offers and which cell each one claims
        /// (<see cref="CombatMenuSlots.PackCells"/>).
        ///
        /// <para>An enemy answers all zeroes because it never opens this menu — not because it has no
        /// quarrels: those are in its body container, which <see cref="QuarrelsCarried"/> reads.</para>
        /// </remarks>
        public int[] QuarrelsFor(Combatant actor) {
            var counts = new int[QuarrelInventory.ObjectIdByKind.Length];
            if (actor == null || !actor.IsPartyMember) {
                return counts;
            }

            RuntimeContainer pack = _session?.GetActorInventory(actor.ClassId);
            for (var kind = 0; kind < counts.Length; kind++) {
                counts[kind] = QuarrelInventory.Count(pack, kind);
            }
            return counts;
        }

        /// <summary>
        /// What the SHOOT menu's parchment says for one cursor position —
        /// <c>combat_arena_draw_tgt_info_panel</c> (COMBAT.C:1152).
        /// </summary>
        /// <param name="acting">Whoever is taking the shot.</param>
        /// <param name="target">What the cursor is over, or null.</param>
        /// <param name="previewedKind">The kind whose NAME and COUNT are shown — the quarrel button
        /// under the cursor when there is one, else <paramref name="selectedKind"/>. See
        /// <see cref="ShootTargetPanel.PreviewedKind"/>.</param>
        /// <param name="selectedKind">The kind actually chosen, which is what the numbers describe.</param>
        /// <remarks>
        /// <b>The accuracy is the shot's own, computed the same way <see cref="ResolveShot"/> does</b>
        /// — the original calls one <c>combatenc_compute_hit_chance</c> for both, so a panel with a
        /// formula of its own would drift from the roll it is advertising. Only the 2%% floor is the
        /// panel's, and it is applied to the printed number rather than to the chance.
        ///
        /// <para><b>The terrain gate is not modelled.</b> The original also clears the target unless
        /// its tile passes <c>combatgrid_tile_has_terr_bit2</c>; nothing on our side carries that
        /// bit, so the panel here reports on a target the original might have refused. Stated rather
        /// than quietly dropped — <see cref="ShootTargetPanel.ShowsTargetStats"/> takes it as a
        /// parameter so the gate reappears the day the terrain flags land.</para>
        ///
        /// <para><b>An unarmed shooter contributes no damage rather than crashing.</b> The original
        /// dereferences the crossbow record without checking it, which is only safe because the
        /// menu cannot be reached without one.</para>
        /// </remarks>
        public ShootTargetPanelContent ShootPanel(
            Combatant acting, Combatant target, int previewedKind, int selectedKind) {
            if (acting == null) {
                return null;
            }
            IReadOnlyList<string> nameLines = GameData.Resources.Object.ObjectNameLines.Split(
                previewedKind >= 0 && previewedKind < QuarrelInventory.ObjectIdByKind.Length
                    ? _objects?.GetById(QuarrelInventory.ObjectIdByKind[previewedKind])
                    : null);

            if (!ShootTargetPanel.ShowsTargetStats(
                    alive: target != null && !target.IsDead,
                    onOpenTile: true,
                    encounterActor: target != null && !target.IsPartyMember)) {
                return ShootTargetPanel.WithoutTarget(
                    nameLines, QuarrelsCarried(acting, previewedKind));
            }

            ActorStat[] stats = StatsFor(acting);
            Equipped weapon = EquippedOfCategory(acting, RangedExchange.ShooterWearCategory);
            int weaponTerm = weapon.Info == null
                ? 0
                : RangedExchange.WeaponTerm(
                    weapon.Info.SwingAccuracy_ArmorMod_BowAccuracy, weapon.Condition);
            int chance = CombatFormulas.RangedHitChance(
                // The SAME read the roll uses — a preview computed from the stored byte would quote
                // odds a wounded shooter does not have.
                RangedExchange.EffectiveSkill(
                    CombatStat(acting, ActorAttribute.AccuracyCrossbow), weaponTerm),
                CombatGrid.ChebyshevDistance(acting.X, acting.Y, target.X, target.Y),
                RangedAmmoAccuracy.BonusFor(selectedKind, AmmoAccuracyOfRecord(selectedKind)));
            // The rolled kinds (8 and 9) are the AI's innate shots and have no button, so the menu
            // never selects one and this roll is unreachable from here -- see
            // QuarrelInventory.ObjectIdByKind, which stops at eight.
            int damage = CombatFormulas.RangedDamage(
                selectedKind, weapon.Info?.SwingBaseDamage ?? 0, QuarrelBaseDamage(selectedKind),
                _ => 0);
            return ShootTargetPanel.ForTarget(nameLines, chance, damage);
        }

        /// <summary>
        /// A combatant's health+stamina, current and maximum — the pool
        /// <c>combat_actor_stat_percent(actor, 1)</c> takes its percentage of.
        /// </summary>
        /// <remarks>
        /// <b>The maximum comes from the stat block, not from the combatant.</b> A monster without
        /// one answers <c>(0, 0)</c>, which every caller has to read as "no opinion" rather than as
        /// a combatant at zero percent.
        /// </remarks>
        public (int Pool, int MaxPool) PoolOf(Combatant actor) {
            if (actor == null) {
                return (0, 0);
            }
            ActorStat[] stats = StatsFor(actor);
            ActorStat health = StatOrNull(stats, ActorAttribute.Health);
            ActorStat stamina = StatOrNull(stats, ActorAttribute.Stamina);
            if (health == null || stamina == null) {
                return (0, 0);
            }
            return (actor.Health + actor.Stamina, health.Max + stamina.Max);
        }

        /// <summary>
        /// The acting character's name and four stats — <c>combat_actor_draw_stats_panel</c>.
        /// </summary>
        /// <returns>Null for anyone the original would not draw: a monster, or a combatant with no
        /// stat block.</returns>
        /// <remarks>
        /// <b>Party members only, and that is the whole guard in the original</b> — the routine's
        /// body sits behind <c>actor-&gt;charSlot</c>, so a monster's turn leaves the strip blank
        /// rather than drawing an empty parchment.
        ///
        /// <para>The values are read through <see cref="StatValue"/>, the same path every other
        /// consumer uses, so the panel shows what the fight is actually rolling against rather than
        /// the unmodified stored numbers.</para>
        /// </remarks>
        public (string Name, System.Collections.Generic.IReadOnlyList<int> Values)? ActorStatsFor(
            Combatant actor) {
            if (actor == null || !actor.IsPartyMember) {
                return null;
            }
            ActorStat[] stats = StatsFor(actor);
            if (stats == null) {
                return null;
            }

            System.Collections.Generic.IReadOnlyList<string> names =
                _session?.PartyActorNames ?? System.Array.Empty<string>();
            string name = actor.ClassId >= 0 && actor.ClassId < names.Count
                ? names[actor.ClassId]
                : string.Empty;

            var values = new System.Collections.Generic.List<int>();
            foreach ((ActorAttribute attribute, string _) in ActorStatsPanel.Rows) {
                // Every read in the original goes through stat_actor_get, the panel's included, so
                // the HUD shows what the rolls use: a hurt member reads low here too.
                values.Add(CombatStat(actor, attribute));
            }
            return (name, values);
        }

        /// <summary>
        /// Roll the Inspect assessment and lay it out —
        /// <see cref="CombatAssessment"/>.
        /// </summary>
        /// <param name="inspector">Whoever pressed Inspect; the roll is against THEIR Assessment.</param>
        /// <param name="target">The enemy being looked at; the values are theirs.</param>
        public System.Collections.Generic.IReadOnlyList<HudPanelLine> AssessmentLines(
            Combatant inspector, Combatant target, System.Func<int, int> rnd = null) {
            var lines = new System.Collections.Generic.List<HudPanelLine>();
            if (inspector == null || target == null) {
                return lines;
            }
            ActorStat[] targetStats = StatsFor(target);
            if (targetStats == null) {
                return lines;
            }

            (bool canShoot, bool canCast) = CapabilitiesFor(target);
            System.Collections.Generic.IReadOnlyList<CombatAssessment.Row> rows =
                CombatAssessment.RowsFor(canShoot, canCast);

            foreach ((CombatAssessment.Row row, int value, int x, int y) in CombatAssessment.Reveal(
                         rows,
                         CombatStat(inspector, CombatAssessment.RollAttribute),
                         attribute => CombatStat(target, attribute),
                         rnd ?? (n => UnityEngine.Random.Range(0, n)))) {
                lines.Add(new HudPanelLine(row.Label, x, y));
                lines.Add(new HudPanelLine(
                    value + (row.Percent ? ShootTargetPanel.PercentSign : string.Empty),
                    x + CombatAssessment.ValueOffsetX, y));
            }
            return lines;
        }

        /// <summary>The four numbers the melee preview panel prints.</summary>
        public readonly struct MeleePreview {
            public MeleePreview(bool showsSwing, int thrustDamage, int thrustAccuracy,
                int swingDamage, int swingAccuracy) {
                ShowsSwing = showsSwing;
                ThrustDamage = thrustDamage;
                ThrustAccuracy = thrustAccuracy;
                SwingDamage = swingDamage;
                SwingAccuracy = swingAccuracy;
            }

            /// <summary>Whether the whole right-hand column is drawn at all.</summary>
            public bool ShowsSwing { get; }

            public int ThrustDamage { get; }

            public int ThrustAccuracy { get; }

            public int SwingDamage { get; }

            public int SwingAccuracy { get; }
        }

        /// <summary>
        /// The numbers <c>combat_arena_hud_melee_panel</c> prints for an attack on
        /// <paramref name="target"/>.
        /// </summary>
        /// <remarks>
        /// <b>These are the PANEL's numbers, not the roll's</b> — see
        /// <see cref="MeleeStatsPanel"/>. Accuracy is a bare skill+weapon sum with no class
        /// affinity, no condition scaling and no subtraction of the target's defence; damage skips
        /// the weapon's condition scaling. Feeding the real formulas in here instead would make our
        /// readout disagree with the original's for every character in the game.
        /// </remarks>
        public MeleePreview MeleePreviewFor(Combatant acting, Combatant target) {
            if (acting == null || target == null) {
                return default;
            }
            int meleeAccuracy = CombatStat(acting, ActorAttribute.AccuracyMelee);
            int strength = CombatStat(acting, ActorAttribute.Strength);
            Equipped weapon = EquippedOfCategory(acting, MeleeWeaponCategory);
            int swingBase = weapon.Info?.SwingBaseDamage ?? 0;
            int thrustBase = weapon.Info?.ThrustBaseDamage ?? 0;

            return new MeleePreview(
                showsSwing: MeleeStatsPanel.ShowsSwingColumn(
                    CombatGrid.OrthogonallyAdjacent(acting.X, acting.Y, target.X, target.Y),
                    acting.Health + acting.Stamina),
                thrustDamage: MeleeStatsPanel.DamageShown(thrustBase, strength,
                    CombatFormulas.WeaponEnchantmentBonus(weapon.Flags, thrustBase)),
                thrustAccuracy: MeleeStatsPanel.AccuracyShown(
                    meleeAccuracy, weapon.Info?.ThrustAccuracy ?? 0),
                swingDamage: MeleeStatsPanel.DamageShown(swingBase, strength,
                    CombatFormulas.WeaponEnchantmentBonus(weapon.Flags, swingBase)),
                swingAccuracy: MeleeStatsPanel.AccuracyShown(
                    meleeAccuracy, weapon.Info?.SwingAccuracy_ArmorMod_BowAccuracy ?? 0));
        }

        /// <summary>What a melee click did.</summary>
        public enum MeleeClick {
            /// <summary>Nothing happened and the character still has their turn.</summary>
            Refused,

            /// <summary>The attacker walked in but could not act on arrival; the turn is spent.</summary>
            ApproachedOnly,

            /// <summary>The attack was made; the turn is spent.</summary>
            Struck,
        }

        /// <summary>
        /// The player's melee — <c>combat_arena_resolve_menu_action</c> case 0 (COMBAT.C:2344).
        /// </summary>
        /// <param name="attack">
        /// <see cref="CombatActionDispatch.MeleeAttack.Thrust"/> for a left click,
        /// <see cref="CombatActionDispatch.MeleeAttack.Swing"/> for a right one.
        /// </param>
        /// <remarks>
        /// <b>The two buttons are two different attacks with different reach.</b> A thrust walks the
        /// attacker into contact first and strikes on arrival; a swing never moves anybody and is
        /// simply refused unless the target is already orthogonally adjacent. So the same click on
        /// the same enemy either engages or refuses depending on the button — implementing both as
        /// "attack if adjacent" removes the game's only click-to-engage.
        ///
        /// <para><b>A DIAGONAL NEIGHBOUR IS NOT ADJACENT.</b> The swing refuses one, and the thrust
        /// treats it as two tiles away and walks (<c>combat_actor_melee_approach</c> increments the
        /// distance for a pure diagonal), so a diagonal enemy is engaged rather than hit in place.
        /// </para>
        ///
        /// <para><b>The approach is what spends the turn on a thrust.</b> The original clears Ready
        /// inside <c>melee_approach</c> and only on its success path — a thrust that cannot find a
        /// route leaves the character still to act, which is why this answers
        /// <see cref="MeleeClick.Refused"/> rather than silently eating the turn.</para>
        /// </remarks>
        public MeleeClick ResolveMeleeClick(Combatant acting, Combatant target,
            CombatActionDispatch.MeleeAttack attack, System.Func<int, int> rnd = null) {
            if (acting == null || target == null || target.IsDead
                || attack == CombatActionDispatch.MeleeAttack.None) {
                return MeleeClick.Refused;
            }

            // Reach and movement are one budget: you cannot strike what you could not have walked to.
            int distance = CombatGrid.ChebyshevDistance(acting.X, acting.Y, target.X, target.Y);
            if (!CombatActionDispatch.WithinReach(distance, acting.Speed)) {
                return MeleeClick.Refused;
            }

            bool adjacent = CombatGrid.OrthogonallyAdjacent(acting.X, acting.Y, target.X, target.Y);
            if (attack == CombatActionDispatch.MeleeAttack.Swing) {
                if (!adjacent
                    || !CombatActionDispatch.HasReservesFor(attack, acting.Health + acting.Stamina)) {
                    return MeleeClick.Refused;
                }
                ResolveMelee(acting, target, attack, rnd);
                return MeleeClick.Struck;
            }

            if (!adjacent) {
                // CACTOR.C:1742: the approach records its defender before it paths, and a failed step
                // leaves it recorded (TASK-540).
                acting.Target = target;
                if (!StepIntoContact(acting, target)) {
                    return MeleeClick.Refused;   // no route in; the turn is not spent
                }
            }
            if (!CombatActionDispatch.ThrustSurvivesTheApproach(!acting.CanAct(false))) {
                return MeleeClick.ApproachedOnly;
            }
            ResolveMelee(acting, target, attack, rnd);
            return MeleeClick.Struck;
        }

        /// <summary>
        /// Whether a combatant standing on one cell could walk to another — the probe
        /// <see cref="GameData.Resources.Combat.ArenaLayout"/> prunes with.
        /// </summary>
        /// <remarks>
        /// <b>The same dry-run walk the movement code uses, not a separate flood fill.</b>
        /// <c>arena_cellReachesMostOfGrid</c> @0x2fe55 stands a throwaway actor on the cell and
        /// probes with <c>combat_actor_walkToTargetCell</c>, so "reachable" means exactly what it
        /// means to a moving combatant — diagonal-squeeze rules and all. Answering it with a flood
        /// fill would prune a different set.
        ///
        /// <para>The original gives the probe 52 steps, enough to cross an 8x13 grid; we pass the
        /// grid's own diagonal for the same reason rather than repeating the constant.</para>
        /// </remarks>
        public bool CellReaches(int fromX, int fromY, int toX, int toY) {
            if (Grid == null) {
                return false;
            }

            var probe = new Combatant { X = fromX, Y = fromY, Speed = ProbeSpeed };
            CombatWalk.WalkResult walk = CombatWalk.Walk(Grid, probe, toX, toY, ProbeSpeed,
                probe: true);
            return walk.PathClear;
        }

        /// <summary>Steps a layout probe is given — enough to cross the grid.</summary>
        private const int ProbeSpeed = CombatGrid.Width + CombatGrid.Height;

        /// <summary>
        /// The player's move — a click on the arena floor with nobody under the cursor.
        /// </summary>
        /// <remarks>
        /// <b>Movement is not a menu command, and that is why <c>CombatCommands.Command</c> has no
        /// Move entry.</b> The original dispatches it from a grid click in the same handler as
        /// melee: an actor under the cursor is the melee arm
        /// (<c>combat_arena_resolve_menu_action</c> @0x627e3), nobody under it is this one, @0x6279f.
        /// Together those two are the whole player interaction with the arena.
        ///
        /// <para>Three rules of that arm, none of them guessable from the UI:</para>
        /// <list type="number">
        /// <item><b>A move CLEARS the target.</b> Walking away drops it rather than keeping it at
        /// range.</item>
        /// <item><b>The move ENDS THE TURN — unconditionally.</b> The arm clears <c>mayAct</c> with
        /// no "did it actually get anywhere" test, so a click on a tile the actor cannot reach
        /// still costs the turn. That is deliberately not softened here: the melee arm is the one
        /// that refuses without spending anything, and making this one forgiving would be a
        /// different game.</item>
        /// <item><b>Facing happens after arriving</b>, through <c>face_target_or_nearest</c> — and
        /// with the target just cleared, that means the nearest enemy. <see cref="Combatant"/> has
        /// no facing field, so this is the one rule of the three not modelled; it is a render
        /// concern (<c>ArenaFacing</c>) and belongs with whoever draws the actor.</item>
        /// </list>
        ///
        /// <para><c>combat_actor_move_to_cursor</c> @0x5d1ba also does
        /// <c>combat_moveStepsRemaining++</c> immediately before the walk. Nothing in that body
        /// reads it back, so whether a player-ordered move is granted a step beyond the actor's
        /// budget is <b>unverified</b> and not modelled — see the task note before adding it.</para>
        /// </remarks>
        /// <returns>Whether the actor ended up somewhere new.</returns>
        public bool MoveToTile(Combatant acting, int x, int y, System.Func<int, int> rnd = null) {
            if (acting == null || Grid == null || Encounter == null
                || !CombatGrid.InBounds(x, y)) {
                return false;
            }

            System.Func<int, int> roll = rnd ?? (n => UnityEngine.Random.Range(0, n));
            int startX = acting.X;
            int startY = acting.Y;
            AnnounceShove(CombatWalk.Walk(Grid, acting, x, y, acting.Speed, puzzle: Puzzle,
                occupiedByLiveCombatant: TileHoldsLiveCombatant,
                onHazard: (hurt, hazard) => ApplyWalkHazard(hurt, hazard, roll)));

            // Walk moved the combatant itself; put it back and re-apply through MoveTo so the
            // grid's occupancy follows it, exactly as the monster paths do.
            int endX = acting.X;
            int endY = acting.Y;
            acting.X = startX;
            acting.Y = startY;
            MoveTo(acting, endX, endY);

            // *** A REFUSED MOVE COSTS NOTHING. *** The original gates the whole arm on
            // `combatgrid_cursor_tile_movable()` (COMBAT.C:2324) and only clears CAF_READY — and
            // the target — INSIDE it; the else branch just drops back to the idle state. Clearing
            // Ready unconditionally spends the turn on a click that moved nobody, which from the
            // player's side is indistinguishable from a broken control: measured in a live fight,
            // a speed-4 actor at (2,2) clicking (2,6), exactly four cells, reported success,
            // consumed the turn and stood still. Four such clicks deadlocked the whole encounter.
            if (endX == startX && endY == startY) {
                return false;
            }

            acting.Target = null;
            acting.Flags &= ~CombatantFlags.Ready;
            return true;
        }

        /// <summary>
        /// Walk the attacker to a tile beside the target. False when there is no route.
        /// </summary>
        /// <remarks>
        /// The same <see cref="CombatWalk"/> the monster turn uses, aimed at the target's tile: the
        /// walk stops at the last free tile before an occupied one, which is the contact square.
        /// </remarks>
        private bool StepIntoContact(Combatant mover, Combatant target) {
            int startX = mover.X;
            int startY = mover.Y;
            System.Func<int, int> hazardRoll = n => UnityEngine.Random.Range(0, n);
            AnnounceShove(CombatWalk.Walk(Grid, mover, target.X, target.Y, mover.Speed,
                puzzle: Puzzle, occupiedByLiveCombatant: TileHoldsLiveCombatant,
                onHazard: (hurt, hazard) => ApplyWalkHazard(hurt, hazard, hazardRoll)));
            int endX = mover.X;
            int endY = mover.Y;
            mover.X = startX;
            mover.Y = startY;
            if (endX == startX && endY == startY) {
                return false;
            }
            MoveTo(mover, endX, endY);

            // A hazard on the way can now kill the mover, which it could not before hazards were
            // wired. A corpse is not in contact with anything.
            return !mover.IsDead
                && CombatGrid.OrthogonallyAdjacent(mover.X, mover.Y, target.X, target.Y);
        }

        /// <summary>
        /// The pack a combatant's gear is read from: a member's inventory, or a monster's body.
        /// </summary>
        /// <remarks>
        /// <b>A monster has a pack, and it is the container its corpse is looted from.</b> The original
        /// binds every enemy's <c>actor_record</c> to <c>actorspawn_objfixed(100, slot, encounter)</c>
        /// (CBENC.C:102), and every gear lookup — quarrels, the crossbow, armour, wear — reads that
        /// record whoever the actor is. This used to answer only for the party, so no monster ever
        /// had a quarrel to fire (TASK-502). A summon or decoy has no roster slot, and no pack.
        /// </remarks>
        /// <summary>
        /// Whether this combatant knows the spell — the <c>spellsKnown</c> bit that closes
        /// <c>cspell_check_castable</c> (CSPELL.C) for every actor, creature or party member.
        /// </summary>
        /// <remarks>
        /// A creature's words come from its roster record, the same 95 bytes its stats do: entry 306's
        /// Rogue Mage knows 3, 6 and 23 and nothing else (TASK-539). A summon or decoy has no record and
        /// knows nothing (<see cref="GameData.Resources.Combat.MonsterSummon.KnowsSpells"/>).
        /// </remarks>
        public bool KnowsSpell(Combatant c, int spellId) {
            if (c == null || _session == null) {
                return false;
            }
            ushort[] words = c.IsPartyMember ? _session.KnownSpellsOf(c.ClassId)
                : _enemySlots.TryGetValue(c, out int slot) ? _session.RosterKnownSpellsOf(slot)
                : null;
            return words != null && SpellBook.IsKnown(words, spellId);
        }

        /// <summary>
        /// What this combatant can put behind a spell, or -1 when it can cast nothing at all — the
        /// power arm of <c>cspell_check_castable</c> (CSPELL.C:1579-1597).
        /// </summary>
        /// <remarks>
        /// <b>In chapter 8 nobody casts without an equipped Crystal Staff, creature or party
        /// member</b>, and the staff's charge caps the pool. The AI's spell picker reaches the same
        /// check (<c>cspell_ai_pick_castable_spell</c>, CSPELL.C:289), which is why Timirianya's
        /// Pantathians, who carry nothing, never cast there.
        /// </remarks>
        public int CastingBudget(Combatant c) {
            if (c == null) {
                return -1;
            }
            var context = new GameData.Resources.Spells.SpellCastContext {
                Chapter = _session?.Chapter ?? 0,
                Inventory = PackOf(c),
                HealthStaminaPool = c.Health + c.Stamina,
            };
            return GameData.Resources.Spells.SpellCasting.TryGetPowerBudget(
                context, requireReadyFlag: true, out int budget) ? budget : -1;
        }

        private RuntimeContainer PackOf(Combatant c) {
            if (c == null || _session == null) {
                return null;
            }
            if (c.IsPartyMember) {
                return _session.GetActorInventory(c.ClassId);
            }
            return _encounterNumber >= 0 && _enemyRosterIndex.TryGetValue(c, out int slot)
                ? _session.GetLiveContainerAt(BodyContainerZone, slot, _encounterNumber)
                : null;
        }

        /// <summary>The container "zone" monster bodies live in — the same one corpse loot opens.</summary>
        private const int BodyContainerZone = 100;

        /// <summary>Quarrels of one kind in this combatant's pack — a monster's included.</summary>
        private int QuarrelsCarried(Combatant actor, int kind) =>
            QuarrelInventory.Count(PackOf(actor), kind);

        /// <summary>Take one quarrel of a kind out of the shooter's pack.</summary>
        private void SpendQuarrel(Combatant actor, int kind) {
            if (_objects == null) {
                return;
            }
            RuntimeContainer pack = PackOf(actor);
            if (pack == null) {
                return;
            }
            GameData.Resources.Inventory.InventoryConsume.TryConsumeOne(
                pack, QuarrelInventory.ObjectIdByKind[kind], _objects.GetById);
        }

        /// <summary>
        /// The pack's items as the equipment lookup sees them.
        /// </summary>
        /// <remarks>
        /// <b>Condition depends on the item TYPE, not the item.</b> Only a
        /// <see cref="ObjectFlags.Degradable"/> type stores a real condition in
        /// <see cref="RuntimeItem.Variable"/>; anything else reads as
        /// <see cref="EquippedGear.UntrackedCondition"/>, and Broken overrides both.
        /// </remarks>
        private System.Collections.Generic.IEnumerable<EquippedGear.Slot> EquipmentSlots(
            RuntimeContainer pack) {
            if (pack == null || _objects == null) {
                yield break;
            }
            foreach (RuntimeItem item in pack.Items) {
                GameData.Resources.Object.ObjectInfo info = _objects.GetById(item.ObjectId);
                if (info == null) {
                    continue;
                }
                yield return new EquippedGear.Slot(
                    (item.ItemFlags & (ushort)ItemFlags.Equipped) != 0,
                    (int)info.ObjectType,
                    EquippedGear.ConditionOf(
                        (item.ItemFlags & (ushort)ItemFlags.Broken) != 0,
                        (info.Flags & GameData.Resources.Object.ObjectFlags.Degradable) != 0,
                        item.Variable));
            }
        }

        /// <summary>
        /// Spend a combatant's turn defending, and return what they recovered.
        /// </summary>
        /// <remarks>
        /// <b>Lives here rather than in the HUD handler</b> because it needs both halves of the
        /// fight's state — the combatant and the session's live stats — and because a turn being
        /// resolved is the runtime's business, not the menu's.
        ///
        /// <para>The recovery is applied through <see cref="StatEngine.ModifyHealthPool"/>, the same
        /// path <c>stat_combatant_modify</c>'s combo attribute takes elsewhere, and is capped at
        /// <see cref="RestAction.HealCapPercent"/> — resting never heals to full.</para>
        ///
        /// <para><b>A monster rests AND recovers.</b> It used to get the flag and the spent turn with
        /// the heal skipped, for want of a stat block rather than by any rule — the original heals it
        /// unconditionally.</para>
        ///
        /// <para><b>This is Rest, not Defend</b> — menu id 19, not 32. Defending is
        /// <see cref="ResolveDefend"/> and does something else entirely.</para>
        /// </remarks>
        public int ResolveRest(Combatant actor) {
            if (actor == null) {
                return 0;
            }

            ActorStat[] stats = StatsFor(actor);
            ActorStat health = StatOrNull(stats, ActorAttribute.Health);
            ActorStat stamina = StatOrNull(stats, ActorAttribute.Stamina);

            // *** Report what was APPLIED, not what was computed. *** HealAmount has a floor of 1,
            // so a combatant with no stat block behind it would otherwise answer 1 while moving
            // nothing — a return value that reads as a heal the caller could log or display.
            if (health == null || stamina == null) {
                RestAction.Apply(actor, recovers: false, maxHealth: 0, maxStamina: 0);
                return 0;
            }

            // *** THE GATE IS THE CHARACTER'S CONDITIONS, AND RestAction ALREADY COMPUTES IT. ***
            // My earlier comment here called it "six skill-train bytes on its aSkillTrainRate row",
            // which is what canassa's expression LOOKS like and is not what it reads: the base+
            // displacement runs past that 12-byte array into abActorStatusRanks, the per-character
            // row of seven condition ranks. RestAction.RecoveryAllowed had established that on
            // 2026-08-22 and had no caller — so resting healed a poisoned, plagued or starving
            // character, which the original refuses.
            //
            // A monster passes null and always recovers, which is the `!actor->charSlot ||` arm.
            int healed = RestAction.Apply(actor, recovers: RestAction.RecoveryAllowed(ConditionRanksOf(actor)),
                maxHealth: health.Max, maxStamina: stamina.Max);

            if (healed > 0) {
                // *** HEAL THE COMBATANT, NOT THE SAVED RECORD. *** The fight's damage lives on the
                // Combatant; the session's ActorStat still holds what the party walked in with, and
                // WriteBack only ever copies combatant -> session. Healing the session stats
                // therefore did nothing twice over: the pool it read was the UNDAMAGED one, already
                // at RestAction.HealCapPercent, so ModifyHealthPool's `sum < target` refused the
                // heal outright — and even a heal that landed would have been overwritten by the
                // next WriteBack.
                //
                // Measured in a live fight on 2026-09-11: combatant hp30 sta0, session hp66 sta26;
                // ResolveRest answered "healed 3" and moved neither. Resting therefore never helped
                // anyone who had actually been hurt, which is precisely backwards, and it left the
                // party unable to end a fight: at zero stamina they cannot close on an enemy, a
                // downed member bars retreat ("He could not abandon his friend to certain death"),
                // and rest could not give the stamina back.
                //
                // Sourced from the combatant and written back, which is what ApplySpellDamage does
                // a few hundred lines down; the two now agree instead of pulling opposite ways.
                var pool = new ActorStat { Base = (byte)Clamp(actor.Health), Max = health.Max };
                var poolStamina = new ActorStat { Base = (byte)Clamp(actor.Stamina), Max = stamina.Max };
                StatEngine.ModifyHealthPool(pool, poolStamina, (long)healed << 8,
                    RestAction.HealCapPercent, out _);
                actor.Health = pool.Base;
                actor.Stamina = poolStamina.Base;
                WriteBack(actor);
            }
            return healed;
        }

        /// <summary>
        /// Raise a guard for the round — the Defend command (menu id 32).
        /// </summary>
        /// <remarks>
        /// <b>Not <see cref="ResolveRest"/>.</b> The two are separate menu entries and separate
        /// actions: resting heals and sets <see cref="CombatantFlags.DefendCommand"/>, which feeds
        /// nothing in the to-hit path; defending sets <see cref="CombatantFlags.Parry"/>, which
        /// <see cref="CombatFormulas.MeleeHits"/> already reads and applies to the attacker's ROLL.
        /// So this makes the defender genuinely harder to hit the moment it is called.
        ///
        /// <para>No roll, no recovery, no stat block needed — two flag operations, which is why a
        /// monster and a party member take exactly the same path.</para>
        /// </remarks>
        public void ResolveDefend(Combatant actor) {
            if (actor == null) {
                return;
            }
            DefendAction.Apply(actor);
        }

        /// <summary>
        /// Tick one combatant's poison — <see cref="PoisonTick"/>.
        /// </summary>
        /// <remarks>
        /// <b>Nothing called PoisonTick before this.</b> A poisoned combatant never took poison
        /// damage, and because the flag was still set and still displayed, the condition looked like
        /// it was working.
        ///
        /// <para><b>Weakness and resistance are passed as false, and that is a GAP rather than a
        /// rule.</b> They come from SPELLWEA/SPELLRES (TASK-115), which nothing loads yet, and those
        /// tables are keyed by SPELL — whether a damage-type poison tick consults them at all is not
        /// established. Guessing either way changes how much damage poison does.</para>
        ///
        /// <para>Damage reaches the save for a party member, for the same reason a swing does.</para>
        /// </remarks>
        private void TickPoison(Combatant actor) {
            if (actor == null || actor.IsDead) {
                return;
            }

            // Poison is a direct attack in PoisonTick's own reading, so the shield and the negation
            // apply to it exactly as they do to a blow.
            // Damage flags 1: a class with that bit in its masks takes half again, or half.
            (bool weak, bool resists) = DamageAffinityOf(actor, 1);
            PoisonTick.Result result = PoisonTick.Apply(
                actor, n => UnityEngine.Random.Range(0, n), absorbPool: AbsorbPoolOf(actor),
                weakToDamageType: weak, resistsDamageType: resists, negated: DamageNegatedFor(actor));
            if (result.Ticked) {
                CommitAbsorb(actor, result.AbsorbPool);
            }
            // apply_damage with knockback 2 (COMBAT.C:209): the tick floats and flinches like a blow,
            // through remap 2, and an emptied health bar dies there (COMBAT.C:377-409). A shield that
            // soaked it all, or an immune class, returned before the float.
            MarkHit(actor,
                result.Ticked && !result.AbsorbPool.HasValue && actor.ClassId != CombatEncounter.AlwaysActsClassId,
                result.Damage, remapIndex: 2);
            if (result.Damage > 0 && actor.IsPartyMember) {
                WriteBack(actor);
            }
            if (result.Died) {
                KillAndSettle(actor);
            }
        }

        /// <summary>
        /// What the most recent death left behind, for whoever spawns corpses and loot.
        /// </summary>
        /// <remarks>
        /// <b>The three outcomes are not cosmetic.</b> A corpse is what the loot screen opens; a
        /// removal is persisted so the actor does not come back on revisit; a conjured creature is
        /// deleted with NOTHING persisted, because persistence is keyed by roster index and a summon
        /// has none — writing one would stamp "gone" onto a real roster member. Held rather than
        /// acted on here: spawning the body is TASK-101's, and inventing it would be worse than
        /// leaving the field clear.
        /// </remarks>
        public DeathOutcome? LastDeath { get; private set; }

        // What each death left behind, kept per combatant because LastDeath is only the newest and
        // the field can hold several bodies at once.
        private readonly Dictionary<Combatant, DeathOutcome> _deathOutcomes =
            new Dictionary<Combatant, DeathOutcome>();

        /// <summary>Whether this combatant's death left a body on the field.</summary>
        /// <remarks>
        /// <b>False for the living and for the vanished alike.</b> Only
        /// <see cref="DeathOutcome.LeavesCorpse"/> puts something on the ground:
        /// <see cref="DeathOutcome.RemovedFromField"/> takes the creature away, and
        /// <see cref="DeathOutcome.Unsummoned"/> deletes a conjured one outright. Drawing a body for
        /// either would leave a corpse the loot screen has no container for.
        /// </remarks>
        public bool LeavesCorpse(Combatant c) =>
            c != null
            && _deathOutcomes.TryGetValue(c, out DeathOutcome outcome)
            && outcome == DeathOutcome.LeavesCorpse;

        /// <summary>
        /// Records that a dead enemy is gone for good, so a revisit does not find it standing.
        /// </summary>
        /// <remarks>
        /// <b>Only some deaths persist, and the three gates are all in the original.</b>
        /// <c>combat_arena_actor_die</c> ends with
        /// <code>
        /// if (removed)
        ///     for (i = 0; i &lt; g_combat_count_B; i++)
        ///         if (&amp;g_combat_actors_B[i] == actor) { rgnenc_persist_actor_removed(g_encounter_id, i); return; }
        /// </code>
        /// so:
        /// <list type="number">
        ///   <item><b>The body must actually be gone.</b> A corpse left on the field is not a
        ///     removal, and a conjured creature is deleted with nothing written — see
        ///     <see cref="DeathOutcome"/>, whose three cases are exactly this test.</item>
        ///   <item><b>The dead actor must be in the ENEMY array.</b> A party member is in the other
        ///     one and never reaches the call, which is why a fallen companion is not stamped
        ///     "removed" into an encounter's roster.</item>
        ///   <item><b>The encounter must have a record slot.</b>
        ///     <c>rgnenc_persist_actor_removed</c> scans the chunk's record-id list and returns
        ///     without writing when the id is absent — the sixth encounter in a chunk has no
        ///     entry.</item>
        /// </list>
        ///
        /// <para><b>The slot is the index in the enemy array, not the roster slot the actor came
        /// from.</b> Those coincide only because the roster's empty fillers are dropped before the
        /// array is built; the original indexes the live array, so this does too. An enemy skipped
        /// at entry for having no stats would shift the rest — logged there, and the reason that
        /// warning matters.</para>
        ///
        /// <para>The pose is deliberately zeroed rather than kept: the record says "gone", not
        /// "gone from here" (<see cref="GameData.Resources.World.EncounterObjectStates.MarkRemoved"/>).</para>
        /// </remarks>
        private void PersistRemoval(Combatant dead, DeathOutcome outcome) {
            if (outcome != DeathOutcome.RemovedFromField || dead == null || Encounter == null) {
                return;
            }
            if (!_encounterAddress.IsKnown || _session?.EncounterActorStates == null) {
                return;
            }

            int slot = Encounter.Enemies.IndexOf(dead);
            if (slot < 0 || slot >= GameData.Resources.World.EncounterObjectStates.SlotsPerRecord) {
                return;
            }

            _session.EncounterActorStates.MarkRemoved(
                _encounterAddress.RefPair, _encounterAddress.RecordIndex, slot);
            _logger?.LogDebug(
                "Encounter actor removed for good: refPair {Pair}, record {Record}, slot {Slot}.",
                _encounterAddress.RefPair, _encounterAddress.RecordIndex, slot);
        }

        /// <summary>
        /// Writes every surviving enemy back onto the world at the tile it finished the fight on —
        /// <c>combat_actor_deploy_encounter</c> (CACTOR.C:343).
        /// </summary>
        /// <param name="cellSize">The arena's grid cell size (<c>START.DAT</c>).</param>
        /// <param name="underground">
        /// Whether the zone is underground. Underground the stored pose is KEPT — see
        /// <see cref="GameData.Resources.World.EncounterObjectStates.MarkPlaced"/> — so a dungeon
        /// fight does not move its survivors.
        /// </param>
        /// <remarks>
        /// <b>The original's name says "deploy" and it deploys nothing.</b> It runs at fight END,
        /// from <c>combat_arena_finalize_round</c>, and is the counterpart to
        /// <see cref="PersistRemoval"/>: that one records the dead that vanish, this one records where each BODY
        /// lies (TASK-534: 'visible' is the body list, not the living, which TASK-239 had inverted).
        ///
        /// <para><b>THE POSE IS AN OFFSET FROM THE PARTY'S TILE, NOT A WORLD POSITION.</b>
        /// <c>combatgrid_tile_to_world_rotated</c> (CMBTGRID.C:486) ends by subtracting
        /// <c>(camera / 64000) * 64000</c> — the camera's tile origin — and that is the frame
        /// <see cref="GameData.Resources.World.EncounterActorPlacement"/> reads it back in. Storing
        /// an absolute position would put every survivor a whole tile out, and only in zones whose
        /// tile index is non-zero, which is exactly the kind of bug that looks like it works.</para>
        ///
        /// <para><b>Must run before the party moves.</b> That origin is the party's own tile, so on
        /// the flight path this has to happen ahead of the relocation rather than after it.</para>
        ///
        /// <para><b>The de-confliction MOVES the actor, and looks at the whole field.</b> The
        /// original asks <c>combat_actor_visible_at_tile</c>, which sees party members too, and
        /// assigns the walked tile back onto <c>actor-&gt;inner-&gt;gridX/Y</c> as it goes — so the
        /// occupancy each actor tests against includes the moves already made. Reproduced by reading
        /// the live tiles rather than a snapshot.</para>
        ///
        /// <para><b>Known divergence: the animation facing is not modelled.</b> The original composes
        /// <c>anim_facing * 45° + snapped camera yaw + 180°</c> from <c>g_anim_pool_B</c>, which has
        /// no equivalent here — our arena draws every combatant at the party's heading. Index 0 is
        /// used, giving "facing back down the party's bearing", which is right for an actor that was
        /// fighting the party and wrong only by however far it had itself turned.</para>
        /// </remarks>
        /// <summary>
        /// A survivor's recovery since its encounter was last fought — <c>combatenc_pty_load_chap_state</c>
        /// (CBENC.C:79-84): the hours since, in 8.8, onto the combined pool, capped at full.
        /// </summary>
        /// <remarks>
        /// Applied to the roster actor's own stats, as the original applies it to the loaded combatant
        /// that is written back when the fight ends. Past 127 hours the original's 16-bit shift drains
        /// instead — see <see cref="GameData.Resources.World.EncounterFoughtTimes.RecoveryDelta"/>.
        /// </remarks>
        private void RecoverSinceLastFought(ActorStat[] stats, int encounterNumber) {
            if (_session == null || encounterNumber < 0) {
                return;
            }
            ActorStat health = StatOrNull(stats, ActorAttribute.Health);
            ActorStat stamina = StatOrNull(stats, ActorAttribute.Stamina);
            if (health == null || stamina == null) {
                return;
            }
            long delta = GameData.Resources.World.EncounterFoughtTimes.RecoveryDelta(
                (uint)_session.GameTimeIn2Seconds, _session.EncounterFoughtTimes.FoughtAt(encounterNumber));
            if (delta != 0) {
                StatEngine.ModifyHealthPool(health, stamina, delta,
                    GameData.Resources.World.EncounterFoughtTimes.RecoveryHealTargetPercent, out _);
            }
        }

        public void PersistSurvivors(int cellSize, bool underground) {
            if (Encounter == null || cellSize <= 0) {
                return;
            }
            if (!_encounterAddress.IsKnown || _session?.EncounterActorStates == null) {
                return;
            }

            long originX = (long)GameData.Resources.World.WorldPlacement.TileOf(_session.PositionX)
                * GameData.Resources.World.WorldPlacement.TileSize;
            long originY = (long)GameData.Resources.World.WorldPlacement.TileOf(_session.PositionY)
                * GameData.Resources.World.WorldPlacement.TileSize;

            int slots = System.Math.Min(Encounter.Enemies.Count,
                GameData.Resources.World.EncounterObjectStates.SlotsPerRecord);
            for (var slot = 0; slot < slots; slot++) {
                // *** BODIES, NOT SURVIVORS (TASK-534). *** g_active_combatants holds only the dead:
                // combat_actor_place_on_free_tile registers an actor there only when CAF_DEAD is set
                // (CACTOR.C:152-156) and a death registers it (COMBAT.C:227), so the original writes
                // each BODY as placed and leaves a survivor's state word alone.
                Combatant survivor = Encounter.Enemies[slot];
                if (!LeavesCorpse(survivor)) {
                    continue;
                }

                (int X, int Y) tile = CombatEndPersistence.FreeTileFrom(survivor.X, survivor.Y,
                    (x, y) => OccupiedByAnother(survivor, x, y));
                // The original assigns the walked tile back onto the actor, so the next survivor's
                // search sees this one's new place.
                survivor.X = tile.X;
                survivor.Y = tile.Y;

                (int across, int away) =
                    CombatArenaPlacement.CellOffset(tile.X, tile.Y, cellSize);
                (int dx, int dy) = BakAgain.World.Collision.ProximityMath.Rotate(
                    across, away, _session.Rotation);

                _session.EncounterActorStates.MarkPlaced(
                    _encounterAddress.RefPair, _encounterAddress.RecordIndex, slot,
                    (int)(_session.PositionX + dx - originX),
                    (int)(_session.PositionY + dy - originY),
                    // *** THE ACTOR'S OWN FACING, NOT A CONSTANT. *** This passed a literal 0 until
                    // the arena had somewhere to keep a facing at all; TASK-242 recorded the cost as
                    // "a monster that had rotated to face a flanking party member persists looking
                    // at where the party stood". Combatant.FacingOctant is now that place, holding
                    // the same eighth-turn index the original keeps per creature in creatueBitmapAnim
                    // and folds into a world heading the same way (TASK-324).
                    (short)CombatEndPersistence.FacingFor(survivor.FacingOctant,
                        (ushort)_session.Rotation),
                    underground);
            }
        }

        /// <summary>
        /// Whether a tile is held by a live combatant other than <paramref name="mover"/> —
        /// <c>combat_actor_visible_at_tile</c>, which does not care which side the occupant is on.
        /// </summary>
        private bool OccupiedByAnother(Combatant mover, int x, int y) {
            foreach (Combatant other in Encounter.AllCombatants()) {
                if (other != null && other != mover && LeavesCorpse(other) && other.X == x && other.Y == y) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The combat records a save should write for this fight, or an empty list outside one.
        /// </summary>
        /// <param name="existingRecordFor">
        /// The record currently in the save for an actor slot — <c>GameSession.CombatRecordOf</c>.
        /// </param>
        /// <remarks>
        /// <b>The ENEMY side only.</b> The original walks <c>g_combat_actors_B</c> and writes each at
        /// its roster slot; the party's combat state is not in this block, which is consistent with
        /// the party being written back through its character records instead.
        ///
        /// <para><b>A slot with no existing record is skipped, not invented.</b> The mapping is a
        /// patch of the record already there (see
        /// <see cref="CombatStatePersistence.WithLiveState"/>) — most of the 22 bytes come from the
        /// creature's own data, so composing one from nothing would write zeros the engine reads as
        /// real values.</para>
        /// </remarks>
        public IReadOnlyList<GameData.Resources.Data.DirtyCombatantEdit> CollectDirtyCombatantEdits(
            System.Func<int, GameData.Resources.Data.SaveGameCombatData> existingRecordFor) {
            var edits = new List<GameData.Resources.Data.DirtyCombatantEdit>();
            if (Encounter == null || existingRecordFor == null) {
                return edits;
            }

            foreach (Combatant enemy in Encounter.Enemies) {
                if (!_enemySlots.TryGetValue(enemy, out int slot)) {
                    continue;
                }
                GameData.Resources.Data.SaveGameCombatData existing = existingRecordFor(slot);
                if (existing == null) {
                    continue;
                }
                edits.Add(new GameData.Resources.Data.DirtyCombatantEdit(
                    slot, CombatStatePersistence.WithLiveState(existing, enemy)));
            }
            return edits;
        }

        /// <summary>
        /// The actor records a save should write for this fight's enemies, or an empty list outside
        /// one.
        /// </summary>
        /// <remarks>
        /// <b>The 95-byte half of surviving a fight; <see cref="CollectDirtyCombatantEdits"/> is the
        /// 22-byte half.</b> Health and stamina live in the actor record, not the combat one, so
        /// staging combatant edits alone left every enemy healing itself the moment the fight ended.
        ///
        /// <para><b>The stats are live, not copied.</b> <see cref="GameSession.RosterStatsOf"/> hands
        /// back the same array <see cref="WriteBack"/> mutates, so an edit carries whatever the fight
        /// did without this method having to know what that was.</para>
        ///
        /// <para>The original writes the whole record back for each surviving enemy
        /// (<c>SaveEncounterNpcsToTempGam</c>, IDA <c>0x63265</c>) — including the dead, which is how
        /// a corpse stays a corpse. Slotless combatants (summons, decoys) are skipped: the roster
        /// never named them, so there is no record of theirs to write.</para>
        /// </remarks>
        public IReadOnlyList<GameData.Resources.Data.DirtyRosterActorEdit>
            CollectDirtyRosterActorEdits() {
            var edits = new List<GameData.Resources.Data.DirtyRosterActorEdit>();
            if (Encounter == null || _session == null) {
                return edits;
            }

            foreach (Combatant enemy in Encounter.Enemies) {
                if (!_enemySlots.TryGetValue(enemy, out int slot)) {
                    continue;
                }
                ActorStat[] stats = _session.RosterStatsOf(slot);
                if (stats == null) {
                    continue;
                }
                edits.Add(new GameData.Resources.Data.DirtyRosterActorEdit(slot, stats));
            }
            return edits;
        }

        /// <summary>What a retreat attempt did, and which line the party hears.</summary>
        public readonly struct RetreatOutcome {
            public RetreatOutcome(bool escaped, int dialogRecord) {
                Escaped = escaped;
                DialogRecord = dialogRecord;
            }

            /// <summary>True when the party got out and the fight is over.</summary>
            public bool Escaped { get; }

            /// <summary>The DDX record to play. Always set — every path says something.</summary>
            public int DialogRecord { get; }
        }

        /// <summary>
        /// Attempt to leave the fight — the retreat command (menu id 33).
        /// </summary>
        /// <param name="actor">Whoever pressed it; loses the turn if the attempt fails.</param>
        /// <param name="roll">The <c>RND(n)</c> seam, injectable so a test can pin the coin flip.</param>
        /// <remarks>
        /// <b>An attempt, not an exit.</b> Three conditions gate it and only one is the coin flip —
        /// see <see cref="CombatCommands.EscapeRollPasses"/>. This method does not call
        /// <see cref="Leave"/>: the caller owns that, because the escape dialog plays first and
        /// tearing the fight down underneath it would leave the line talking about a fight that no
        /// longer exists.
        ///
        /// <para><b>The two "someone is down" tests are deliberately different.</b> The escape gate
        /// counts only real party members (the original tests <c>charSlot != 0</c>), so a dead summon
        /// does not stop you leaving; the refusal selector compares living slots against all slots on
        /// the party's side, which a dead summon DOES change. Collapsing them into one predicate is
        /// the easy mistake and it puts the wrong line on screen whenever a conjured ally has
        /// fallen.</para>
        ///
        /// <para><b>A failed attempt still costs the turn</b>
        /// (<see cref="CombatCommands.FailedRetreatSpendsTheTurn"/>) — Ready is cleared here, which
        /// is what stops retreat being a free reroll every round.</para>
        /// </remarks>
        public RetreatOutcome ResolveRetreat(Combatant actor, System.Func<int, int> roll = null) {
            if (actor == null || Encounter == null) {
                return new RetreatOutcome(false, CombatCommands.RetreatRefusedDialog);
            }

            var anyPartyMemberDead = false;
            var anySlotDown = false;
            foreach (Combatant c in Encounter.Party) {
                if (!c.IsDead) {
                    continue;
                }
                anySlotDown = true;
                anyPartyMemberDead |= c.IsPartyMember;
            }

            int r = roll != null ? roll(100) : UnityEngine.Random.Range(0, 100);
            if (CombatCommands.EscapeRollPasses(anyPartyMemberDead, r, Encounter.EscapeAllowed)) {
                return new RetreatOutcome(true, CombatCommands.RetreatEscapeDialog);
            }

            actor.Flags &= ~CombatantFlags.Ready;
            return new RetreatOutcome(false, CombatCommands.RetreatRefusalDialog(anySlotDown));
        }

        /// <summary>
        /// The character's seven condition ranks, or null for anything without a character row.
        /// </summary>
        /// <remarks>
        /// <b>Null is the monster case and it means "recovers", not "unknown".</b>
        /// <see cref="RestAction.RecoveryAllowed"/> reads it that way deliberately — the original
        /// skips the whole test for an actor with no character slot.
        /// </remarks>
        private System.Collections.Generic.IReadOnlyList<int> ConditionRanksOf(Combatant combatant) {
            if (combatant == null || !combatant.IsPartyMember || _session == null) {
                return null;
            }
            ActorConditions conditions = _session.ConditionsOf(combatant.ClassId);
            if (conditions == null) {
                return null;
            }
            var ranks = new int[ActorConditions.Count];
            for (var i = 0; i < ranks.Length; i++) {
                ranks[i] = conditions[(ActorCondition)i];
            }
            return ranks;
        }

        /// <summary>
        /// The combatant's stat block, whichever side it is on.
        /// </summary>
        /// <remarks>
        /// <b>Both sides have one, and code that assumed only the party did was wrong.</b>
        /// <see cref="Combatant.ClassId"/> means the party index for a party member and the creature
        /// type for an enemy, so it reaches the party table for one and nothing for the other —
        /// which is why an enemy's block is kept in <c>_enemyStats</c> at entry rather than looked
        /// up here.
        /// </remarks>
        /// <summary>
        /// The study bonus a combatant's own emphasis marks earn for one attribute.
        /// </summary>
        /// <remarks>
        /// Null for anyone who is not a party member — <c>STAT.C:271</c> gates the whole bonus on
        /// <c>charSlot != 0</c>, so a monster gets none whatever the flags say. The character index
        /// is <c>ClassId</c>, which is what <see cref="StatsFor"/> already uses to reach the same
        /// actor's stats.
        /// </remarks>
        /// <summary>
        /// Records a combatant's advanced rating on their sheet — the sink half of
        /// <c>stat_combatant_modify</c>'s STAT.C:300-307 writes.
        /// </summary>
        /// <remarks>
        /// <b>Null for anything that is not a party member</b>, which is the original's
        /// <c>charSlot != 0</c> gate and the same shape <see cref="StudyOf"/> uses — a monster has
        /// no sheet and no row in the 6350 range to write to. <c>ClassId</c> is the character index
        /// for a party member, as it is everywhere else on this path.
        /// </remarks>
        private System.Action<ActorAttribute, GameData.Resources.Character.StatEngine.StatChange>
            MarkOf(Combatant combatant) {
            if (combatant == null || !combatant.IsPartyMember || _session == null) {
                return null;
            }
            int character = combatant.ClassId;
            return (attribute, change) => _session.RecordStatChange(character, attribute, change);
        }

        private System.Func<ActorAttribute, int> StudyOf(Combatant combatant) {
            if (combatant == null || !combatant.IsPartyMember || _session == null) {
                return null;
            }
            int character = combatant.ClassId;
            return attribute => _session.StudyBonusFor(character, attribute);
        }

        public ActorStat[] StatsFor(Combatant combatant) {
            if (combatant == null) {
                return null;
            }
            if (combatant.IsPartyMember) {
                return _session?.StatsOf(combatant.ClassId);
            }
            return _enemyStats.TryGetValue(combatant, out ActorStat[] stats) ? stats : null;
        }

        /// <summary>
        /// Resolve one melee swing between two combatants, applying the outcome to the defender.
        /// </summary>
        /// <param name="rnd">The <c>rnd(n)</c> seam; defaults to the real generator.</param>
        /// <remarks>
        /// <b>The primitive the whole fight was missing.</b> <see cref="MeleeExchange"/>,
        /// <see cref="CombatFormulas"/> and the advancement rules were all ported and none of them
        /// had a caller — nothing in the game could take damage. This is the one place a swing is
        /// resolved, so the player's attack and a monster's resolve identically rather than through
        /// two implementations that drift.
        ///
        /// <para><b>Advancement is only ever offered for the PARTY's stats.</b> The stat objects a
        /// monster carries are the shared roster template, so training them would improve every
        /// creature of that type for the rest of the game.</para>
        ///
        /// <para><b>Equipment is read for the party only</b>, because a monster's weapon is not an
        /// inventory item — see <see cref="EquippedOfCategory"/>. The class-vs-item affinity is now
        /// fed from <see cref="Affinity"/>: at most a 2% swing, and the one thing it visibly does is
        /// stop Gorath (creature class 15, the only party member in combat group 2) taking the
        /// mismatch penalty on elven weapons.</para>
        /// </remarks>
        public MeleeExchange.Result ResolveMelee(Combatant attacker, Combatant defender,
            CombatActionDispatch.MeleeAttack attack, System.Func<int, int> rnd = null, int fixedDamage = 0) {
            if (attacker == null || defender == null) {
                return MeleeExchange.Result.Miss;
            }

            // COMBAT.C:471/606: a swing at a living defender records it as the attacker's target, on
            // both sides — which is what the AI's Engaged and TargetingTheLeader roles read (TASK-528).
            if (!defender.IsDead) {
                attacker.Target = defender;
            }

            ActorStat[] attackerStats = StatsFor(attacker);
            ActorStat[] defenderStats = StatsFor(defender);

            Equipped weapon = EquippedOfCategory(attacker, MeleeWeaponCategory);
            Equipped armour = EquippedOfCategory(defender, ArmorCategory);

            // *** WHICH PAIR OF WEAPON FIELDS IS READ IS THE WHOLE DIFFERENCE BETWEEN THE TWO
            // ATTACKS. *** They pair exactly as their names say -- swing accuracy with swing damage,
            // thrust with thrust -- and the reason that was got wrong here for months is that
            // canassa's two function names are swapped. See CombatActionDispatch.AccuracyOf.
            var swing = new MeleeExchange.Attacker(
                CombatStat(attacker, ActorAttribute.AccuracyMelee),
                CombatStat(attacker, ActorAttribute.Strength),
                hasWeapon: weapon.Info != null,
                weaponAccuracy: CombatActionDispatch.AccuracyOf(attack,
                    weapon.Info?.SwingAccuracy_ArmorMod_BowAccuracy ?? 0,
                    weapon.Info?.ThrustAccuracy ?? 0),
                weaponBase: CombatActionDispatch.DamageBaseOf(attack,
                    weapon.Info?.SwingBaseDamage ?? 0, weapon.Info?.ThrustBaseDamage ?? 0),
                classGroupModifier: AffinityFor(attacker, weapon.Info),
                weaponConditionPercent: weapon.Condition,
                weaponFlags: weapon.Flags,
                fixedDamage: fixedDamage);

            // CanAct(false) is the "not incapacitated" reading DefenseRating wants: a combatant who
            // cannot act does not defend, which is what makes a downed target easy to finish.
            int defense = CombatStat(defender, ActorAttribute.Defense);
            Equipped blade = EquippedOfCategory(attacker, (int)GameData.ObjectType.Sword);
            int swingMask = GameData.Resources.Combat.CombatDamageMask.ForSwing(
                blade.Info != null ? (int)blade.Flags : 0);
            (bool swingWeak, bool swingResists) = DamageAffinityOf(defender, swingMask);
            var guard = new MeleeExchange.Defender(
                CombatFormulas.DefenseRating(defense, defender.CanAct(false), armour.Flags),
                CombatFormulas.ArmorRating(defense, armour.Info != null, armour.Condition,
                    // The same field again, and its third meaning: on an Armor item it is the
                    // armour rating. The name carries all three because the file has one slot.
                    armour.Info?.SwingAccuracy_ArmorMod_BowAccuracy ?? 0,
                    // The DEFENDER's own class against the armour they are wearing — a different
                    // pairing from the attacker's, and getActorArmorRating reads it off the actor
                    // whose rating it is computing.
                    classGroupModifier: AffinityFor(defender, armour.Info)),
                absorbPool: AbsorbPoolOf(defender), negated: DamageNegatedFor(defender),
                // *** A PLAIN SWING HAS A DAMAGE TYPE. *** cbstat_armor_coverage_mask starts at
                // 0x580 before any coating is read, and the first equipped SWORD is the only item
                // that colours it — not the weapon being swung, which may be a staff or a bow.
                weakToDamageType: swingWeak, resistsDamageType: swingResists,
                // The worn armour's own flags: armour enchanted against an element cancels that
                // element's bonus outright (TASK-479, cbstat_damage_apply_protection).
                armorFlags: armour.Info != null ? armour.Flags : default);

            var advancement = new MeleeExchange.Advancement(
                attacker.IsPartyMember ? StatOrNull(attackerStats, ActorAttribute.AccuracyMelee) : null,
                attacker.IsPartyMember ? StatOrNull(attackerStats, ActorAttribute.Strength) : null,
                defender.IsPartyMember ? StatOrNull(defenderStats, ActorAttribute.Defense) : null,
                StudyOf(attacker), StudyOf(defender),
                MarkOf(attacker), MarkOf(defender));

            // *** THE SWING BILLS THE ATTACKER BEFORE IT ROLLS — AND ONLY THE SWING. *** The
            // charge is COMBAT.C:473, inside the swing; the thrust never touches the attacker.
            // Billing both is what a single melee path does, and it makes every thrust cost a
            // point it should not — see CombatActionDispatch.BillsTheAttacker.
            if (CombatActionDispatch.BillsTheAttacker(attack)) {
                BillForSwing(attacker, rnd);
            }

            // *** THE TWO TURN TO EACH OTHER. *** COMBAT.C:480-490 (swing) and 610-617 (thrust), as
            // the blow is struck: the defender faces the attacker -- squared to an even octant when it
            // parries, because the parry pose is played in direction mode 3 -- and the attacker faces
            // the defender in its attack pose, also mode 3. The defender keeps this until its own turn.
            if (!defender.IsDead) {
                bool parries = (defender.Flags & CombatantFlags.Parry) != 0 && defender.CanAct(strict: false);
                CombatEncounter.FaceToward(defender, attacker, evenOnly: parries);
                CombatEncounter.FaceToward(attacker, defender, evenOnly: true);
            }

            MeleeExchange.Result result = MeleeExchange.Resolve(attacker, defender, swing, guard,
                rnd ?? (n => UnityEngine.Random.Range(0, n)), advancement);
            // *** ONLY ON A HIT. *** Result.Miss carries a null pool because no damage was applied,
            // and committing that null would DELETE a shield the swing never touched.
            if (result.Hit) {
                CommitAbsorb(defender, result.AbsorbPool);
            }

            // *** THE SWINGER STOPS RECOILING, WHETHER OR NOT IT CONNECTS. ***
            // resolveSwingAttack @0x5fc69 clears its own 0x40 bit between billing the attacker for
            // the swing and damaging the target, so the clear belongs to the ACT of attacking.
            ClearHitReaction(attacker);
            // *** ARMED ON THE ATTEMPT, NOT ON THE HIT. *** resolveSwingAttack plays the swing
            // before it knows whether the blow lands — a miss still swings. The renderer picks this
            // up on the next redraw, which is the gap the flag exists to cross.
            attacker.SwingPending = true;
            if (result.Hit) {
                MarkHit(defender, result.Landed, result.Dealt);
                PoisonOnHitFrom(defender, swingMask, result.Damage, rnd);
            } else if (defender != null) {
                // A missed blow floats "miss" over the defender (COMBAT.C:543, :664 — value 1 with
                // a negative countdown); DamageFloat 0 is how the port says "miss".
                defender.DamageFloat = 0;
            }

            PlaySwingCue(attacker, defender, result.Hit);

            // *** THE ARMOUR WEARS ONLY ON A LANDED HIT; THE WEAPON ALSO ON AN UNPARRIED MISS. *** The
            // armour wear happens INSIDE the original's to-hit roll — `if (threshold < accuracy) {
            // damage_equipped_items(target, 4, 0x100); result = 1; }` — so it is the hit itself that
            // scuffs the armour, before any damage is worked out. The weapon wears in the caller's
            // hit branch and ONLY when there is a weapon: bare hands wear nothing.
            if (result.Hit) {
                System.Func<int, int> roll = rnd ?? (n => UnityEngine.Random.Range(0, n));
                WearEquipped(defender, ArmorCategory, CombatFormulas.ArmorWearOnMeleeHit, roll);
                if (weapon.Info != null) {
                    // The heavy attack is twice as hard on the weapon: 0x100 in the swing against
                    // 0x80 in the thrust.
                    WearEquipped(attacker, MeleeWeaponCategory,
                        CombatActionDispatch.WearSeverityOf(attack), roll);
                }
            } else if (weapon.Info != null && defender != null
                       && !((defender.Flags & CombatantFlags.Parry) != 0 && defender.CanAct(false))) {
                // A miss nobody parried still works the weapon (COMBAT.C:551-552, :670-671), at the
                // same severity as a hit. Measured in the original: 0x4 stamped, 73 -> 71 once.
                WearEquipped(attacker, MeleeWeaponCategory,
                    CombatActionDispatch.WearSeverityOf(attack), rnd ?? (n => UnityEngine.Random.Range(0, n)));
            }

            // *** A SWING THAT PUTS SOMEONE DOWN HAS TO KILL THEM. *** MeleeExchange REPORTS
            // DefenderDown and deliberately does not act on it — it zeroes the pools and stops.
            // Without this the defender is at 0 health but never flagged Dead, so IsDead stays
            // false: the fight never reaches IsOver(), the corpse never appears, and the body goes
            // on blocking its tile. CombatEncounter.Kill is what clears all three, and it had no
            // caller at all until now.
            if (result.DefenderDown) {
                ArmRevivalIfEligible(defender, rnd ?? (n => UnityEngine.Random.Range(0, n)));
                KillAndSettle(defender);
            }

            // The party's live stats are the save's, so a hit has to reach them or the damage is
            // undone the moment the fight ends and Leave writes the combatant back.
            if (result.Hit && defender.IsPartyMember) {
                WriteBack(defender);
            }
            return result;
        }

        /// <summary>
        /// Resolve one ranged shot — <c>combataiturn_ranged_attack</c> (CBTAITRN.C:102).
        /// </summary>
        /// <param name="quarrelKind">
        /// The kind chosen on the SHOOT menu. <b>Not consumed here</b> — see the remarks.
        /// </param>
        /// <remarks>
        /// <b>The ranged counterpart of <see cref="ResolveMelee"/>, and it differs from it in three
        /// ways that a copy of the melee path would get wrong.</b>
        ///
        /// <list type="number">
        /// <item><b>The target's defence does nothing.</b>
        /// <see cref="CombatFormulas.RangedHitChance"/> takes distance and the quarrel's accuracy and
        /// never looks at the defender — so armour does not help against a bolt, and the defender is
        /// paid no advancement for being shot at (<see cref="CombatAdvancement.OnShotDeclared"/>).</item>
        /// <item><b>The shooter's weapon wears on a MISS too.</b> The original's
        /// <c>cbstat_damage_equipped_items(attacker, 2, 0x100)</c> is the routine's last statement,
        /// outside the hit branch, while the target's armour wear is inside it —
        /// <see cref="RangedExchange.WeaponWearsEvenOnAMiss"/>.</item>
        /// <item><b>Damage has no Strength term.</b> It is the crossbow's swing base plus the
        /// quarrel's, so a weak archer shoots as hard as a strong one.</item>
        /// </list>
        ///
        /// <para><b>The shooter's weapon term is the third difference, and it is a BONUS.</b>
        /// <c>combataiturn_armor_eff_stat</c> reads no armour — see
        /// <see cref="RangedExchange.WeaponTerm"/> — so a character holding an intact crossbow shoots
        /// more accurately, not less. It used to be passed as zero here; the body below now supplies
        /// it, and the shoot menu's panel quotes the same number
        /// (<see cref="ShootPanel"/>).</para>
        ///
        /// <para><b>The quarrel is not spent here.</b> The original consumes it through
        /// <c>combataiturn_sel_consum_qrl(actor, kind, 1)</c> on the arena side, and the pack write is
        /// the caller's business — <see cref="ResolveMelee"/> draws the same line with wear.</para>
        /// </remarks>
        public bool ResolveShot(Combatant attacker, Combatant defender, int quarrelKind,
            System.Func<int, int> rnd = null) {
            if (attacker == null || defender == null || defender.IsDead) {
                return false;
            }

            // *** THE QUARREL IS SPENT BEFORE THE SHOT, AND ITS ABSENCE CANCELS IT. ***
            // combataiturn_sel_consum_qrl runs first and the caller fires only when it answers a
            // kind: `quarrel_slot = ...(actor, quarrel_slot, 1); if (quarrel_slot != -1) attack`.
            // Firing and then deducting would let the last quarrel be shot twice; not deducting at
            // all — which is what this did — gives a character unlimited ammunition.
            //
            // *** EXCEPT A THROWN ROCK, WHICH NEVER ASKS. *** The routines that throw one call
            // combataiturn_ranged_attack(actor, target, 8) directly (CBTAIACT.C:34, :188) with no
            // selection in front — kind 8 is not a quarrel, and the selector would refuse it.
            if (quarrelKind != RangedShotSound.ThrownRockKind) {
                int firedKind = QuarrelInventory.SelectKind(
                    attacker.ClassId, quarrelKind, kind => QuarrelsCarried(attacker, kind));
                if (firedKind == QuarrelInventory.NoKind) {
                    return false;
                }
                if (QuarrelInventory.Spends(firedKind)) {
                    SpendQuarrel(attacker, firedKind);
                }
                quarrelKind = firedKind;
            }

            System.Func<int, int> roll = rnd ?? (n => UnityEngine.Random.Range(0, n));
            ActorStat[] attackerStats = StatsFor(attacker);
            ActorStat[] defenderStats = StatsFor(defender);
            Equipped weapon = EquippedOfCategory(attacker, RangedExchange.ShooterWearCategory);

            // Paid for taking the shot, before the roll — win or lose.
            CombatAdvancement.OnShotDeclared(
                attacker.IsPartyMember ? StatOrNull(attackerStats, ActorAttribute.AccuracyCrossbow) : null,
                StudyOf(attacker), MarkOf(attacker));

            // *** THE WEAPON'S OWN TERM, WHICH USED TO BE PASSED AS 0. *** The helper the original
            // calls is named armor_eff_stat and has no armour in it: it looks up the intact
            // CROSSBOW (category 2) and returns its accuracy scaled by condition — the ranged twin
            // of the melee formula's weaponAccuracy * condition / 100. Omitting it made every shot
            // less accurate than the original, for every shooter holding a bow rather than only
            // armoured ones.
            int weaponTerm = weapon.Info == null
                ? 0
                : RangedExchange.WeaponTerm(
                    weapon.Info.SwingAccuracy_ArmorMod_BowAccuracy, weapon.Condition);

            int chance = CombatFormulas.RangedHitChance(
                RangedExchange.EffectiveSkill(
                    CombatStat(attacker, ActorAttribute.AccuracyCrossbow), weaponTerm),
                CombatGrid.ChebyshevDistance(attacker.X, attacker.Y, defender.X, defender.Y),
                RangedAmmoAccuracy.BonusFor(quarrelKind, AmmoAccuracyOfRecord(quarrelKind)));

            bool hit = CombatFormulas.RangedHits(roll(100), chance);

            // *** THE CUE PLAYS ON A MISS TOO, AND IT IS NOT ONE SOUND. *** resolveRangedAttack's
            // two to-hit branches converge before the cue, so a miss still sounds -- and most shots
            // miss, which makes a cue behind a hit test wrong in the common case. A thrown rock
            // plays an IMPACT after its flight; a crossbow plays a DISCHARGE before one; a thrown
            // weapon with no crossbow equipped is silent. See RangedShotSound.
            int? shotCue = RangedShotSound.Cue(quarrelKind, weapon.Info != null);
            if (shotCue.HasValue) {
                _playSfx?.Invoke(shotCue.Value);
            }

            // OUTSIDE the hit branch, deliberately: see remark 2.
            if (weapon.Info != null) {
                WearEquipped(attacker, RangedExchange.ShooterWearCategory,
                    RangedExchange.ShooterWearSeverity, roll);
            }
            if (!hit) {
                return false;
            }

            CombatAdvancement.OnShotHit(
                attacker.IsPartyMember ? StatOrNull(attackerStats, ActorAttribute.AccuracyCrossbow) : null,
                StudyOf(attacker), MarkOf(attacker));

            int rolled = CombatFormulas.RangedDamage(quarrelKind,
                weapon.Info?.SwingBaseDamage ?? 0, QuarrelBaseDamage(quarrelKind), roll);

            // *** THE ARRIVAL, WHICH IS A DIFFERENT EVENT FROM THE DEPARTURE. *** The cue above is
            // the shot leaving and plays on a miss too; these are the shot landing and play only
            // here. A magic bolt makes both of them. See RangedShotSound.HitCues.
            if (_playSfx != null) {
                foreach (int cue in RangedShotSound.HitCues(quarrelKind)) {
                    _playSfx(cue);
                }
            }
            if (rolled < 0) {
                // The kind has no quarrel record at all, which the original answers with -1 and
                // then feeds to the damage call. Treated as a shot that hits for nothing rather
                // than as healing.
                rolled = 0;
            }

            Equipped armour = EquippedOfCategory(defender, RangedExchange.TargetWearCategory);
            int defense = CombatStat(defender, ActorAttribute.Defense);
            DamageOutcome outcome = CombatFormulas.ApplyDamage(
                rolled, defender.Stamina, defender.Health,
                immune: false, applyArmor: true,
                CombatFormulas.ArmorRating(defense, armour.Info != null, armour.Condition,
                    armour.Info?.SwingAccuracy_ArmorMod_BowAccuracy ?? 0, classGroupModifier: 0),
                absorbPool: AbsorbPoolOf(defender), fromDirectAttack: true,
                negated: DamageNegatedFor(defender),
                weakToDamageType: false, resistsDamageType: false, roll);
            defender.Stamina = outcome.Stamina;
            defender.Health = outcome.Health;
            CommitAbsorb(defender, outcome.AbsorbPool);
            // fromDirectAttack, the same as a swing: a landed shot flinches its target.
            MarkHit(defender, outcome.Landed, outcome.DamageDealt);

            WearEquipped(defender, RangedExchange.TargetWearCategory,
                RangedExchange.TargetWearSeverity, roll);

            // Same three obligations the melee path has once someone goes down, for the same reason:
            // nothing else clears the flag, frees the tile or writes a party member back.
            if (defender.Health <= 0) {
                KillAndSettle(defender);
            }
            if (defender.IsPartyMember) {
                WriteBack(defender);
            }
            return true;
        }

        /// <summary>
        /// Resolve one spell cast in combat — <c>cspell_resolve_cast</c> / <c>Cast_Spell</c>.
        /// </summary>
        /// <param name="spell">
        /// The catalogue record, <b>passed in rather than looked up</b>. The original disposes the
        /// spell catalogue as the cast screen closes and hands the record's values to its handlers,
        /// so a consumer that looked one up would depend on something the original guarantees is
        /// gone — the same reason <c>CastScreen.Committed</c> carries the duration.
        /// </param>
        /// <param name="power">The power invested on the slider, which is also the cost.</param>
        /// <returns>Whether the cast landed.</returns>
        /// <remarks>
        /// <b>A combat cast is billed through the damage pipeline, not off the health pool.</b>
        /// <see cref="SpellCasting.ApplyCost"/> is the FIELD path; the combat one hands the cost to
        /// apply-damage with armour off and the shield flag set, so none of the ordinary defences
        /// make casting cheaper —
        /// <see cref="SpellCasting.CombatCostBypassesArmourAndShields"/>. Using the field path here
        /// would let a well-armoured mage cast at a discount.
        ///
        /// <para><b>Almost nothing can miss.</b> <see cref="SpellHitResolution.CanMiss"/> wants
        /// targeting type 0, a real target and a non-negated cost, all three; everything else lands
        /// automatically. And when it does roll, it rolls the CROSSBOW formula on the caster's
        /// Casting skill — a spell's chance falls off with distance exactly as a bolt's does, which
        /// nothing in the spell data hints at.</para>
        ///
        /// <para><b>Not modelled here:</b> the storm amplifier is passed as false — it is a global
        /// with no source on our side yet — and the lingering per-spell handlers (summons, grid
        /// elements, timed effects) are TASK-110's dispatcher. What this owns is the cast's
        /// arithmetic: cost, to-hit, magnitude, and the health it moves.</para>
        /// </remarks>
        /// <summary>Whether the next cast resolved is amplified by the Infinity Pool.</summary>
        /// <remarks>
        /// <c>g_bStormAmplify</c>. One-shot: <see cref="ResolveCast"/> takes it and clears it, so an
        /// amplified cast that is never resolved does not amplify a later one.
        ///
        /// <para><b>The original clears it at the END of the resolution, not at the read, and that
        /// is not the same rule.</b> <c>cspell_aoe_storm_damage_all</c> — reached from inside the
        /// resolver — reads the flag a second time and adds 50% to its own flat base damage, so an
        /// amplified AoE storm is boosted TWICE: once on the cost here, once on that flat number.
        /// We clear at the read because that AoE arm is not ported; whoever ports it must move the
        /// clear to the end of the resolution or the second boost silently goes missing.</para>
        /// </remarks>
        public bool SurchargeNextCast { get; set; }

        /// <param name="groundCell">
        /// The cell the cast was aimed at, for a spell whose target is a CELL rather than an actor
        /// (<c>SpellTargetingRules.CastsWithoutATarget</c>). Null for an actor-aimed cast. The
        /// original reads <c>g_cursor_tile_x/y</c> at the moment the handler runs; we carry it in
        /// because our resolve is not on the same call stack as the click.
        /// </param>
        public bool ResolveCast(Combatant caster, Combatant target,
            GameData.Resources.Spells.Spell spell, int spellId, int power,
            System.Func<int, int> rnd = null, (int Column, int Row)? groundCell = null) {
            // *** A NEGATIVE POWER IS A REAL CALL, NOT A BAD ONE. *** The original's prologue
            // records the sign in a flag and negates the cost, and that flag then gates three
            // separate things — SpellHitResolution.CanMiss (a negated cast never rolls),
            // SpellCastSound.ForCombatCast (no cue, no wind-up), and the armour wear billed to the
            // caster. This method used to reject `power <= 0` at the door and then pass
            // `costWasNegated: false` to both helpers, so the whole negated path was modelled in
            // GameData and unreachable from production.
            //
            // *** AND A NEGATED CAST IS THE ONE THAT MAY HAVE NO CASTER. *** castCombatSpell
            // @0x68201 is how the arena's own traps cast: it synthesises a caster on its stack
            // (health.max 1, no inventory, creatureType = the -2 sentinel, marked incapacitated),
            // takes its grid position from the two globals the emitter set, and passes -power. We
            // have nothing to synthesise from and nothing that needs one — a null caster skips
            // exactly the three caster-owned steps below, and the to-hit roll it would have fed is
            // already skipped by the negated flag. A caster-less cast at a POSITIVE power is still
            // refused: that would be a bug, not a trap.
            bool costWasNegated = GameData.Resources.Spells.SpellCostModifiers.IsNegated(power);
            if (spell == null || power == 0 || (caster == null && !costWasNegated)) {
                return false;
            }

            // *** THOUGHTS LIKE CLOUDS BLOCKS THE TYPE-2 DELIVERY, AND IT BLOCKS IT FOR FREE. ***
            // The routine's FIRST act is to look for the effect in the caster's own slots and
            // return — ahead of the sound and ahead of the charge (CSPELL.C:1202, the V102CD copy).
            // So this has to sit above ChargeCasterForCast rather than beside the delivery itself:
            // put it there and a silenced caster would pay for a heal that never lands.
            if (SpellCastTail.DeliveryFor(spell.TargetingType) == SpellCastTail.Delivery.Type2Routine
                && SpellCastTail.Type2IsBlocked(SilencedForCasting(caster))) {
                return false;
            }

            // *** TOUCH OF LIMS-KRAGMA IS A CHARGE, NOT A TOUCH. *** Its handler
            // (CSPELL.C:1417-1423) sets `pSpell = 0` when the caster is already within one cell, and
            // the delivery switch below it is wrapped in `if (pSpell != 0)` — the same switch that
            // bills the caster at :1520/:1536. So an adjacent cast delivers nothing AND costs
            // nothing, which is why this sits ABOVE ChargeCasterForCast rather than beside the
            // per-spell arms. At two cells or more the original walks the caster into contact first
            // and the cast proceeds normally.
            //
            // Deliberately NOT the Skyfire shape: there the cast runs and only the magnitude is
            // zero. The two read alike and differ exactly here, in whether the caster pays.
            if (caster != null && target != null && spellId == SpellIds.TouchOfLimsKragma
                && SpellCastTail.LimsKragmaEndsTheCast(
                    CombatGrid.ChebyshevDistance(caster.X, caster.Y, target.X, target.Y))) {
                return false;
            }

            System.Func<int, int> roll = rnd ?? (n => UnityEngine.Random.Range(0, n));
            ActorStat[] casterStats = caster != null ? StatsFor(caster) : null;

            // *** TWO SPELLS ARE NOT BILLED HERE, BECAUSE THEIR OWN ROUTINE PAYS. ***
            // cspell_resolve_cast's cases 21 and 27 run a dedicated routine and zero pSpell, which
            // skips the tail that bills the cast (CSPELL.C:1425, 1442, 1520-1535). Mad God's Rage
            // charges 3 per strike from inside its storm; Winds of Eortis charges its running cost
            // once. Charging here as well would bill them twice. Only when the aimed target does not
            // resist: a resisted cast takes the ordinary path.
            bool handlerEndsTheCast = SpellCastTail.HandlerEndsTheCast(spellId) && caster != null
                && target != null && !TargetResists(target, spellId);

            if (caster != null) {
                // *** THE BILL IS THE COST THE PLAYER CHOSE — EXCEPT ON THE TYPE-2 DELIVERY. ***
                // The dispatcher saves the incoming cost before the surcharge is added and before
                // the sign is stripped, and the payment routine receives that saved value; the one
                // exception is handed the running cost instead, so it alone bills the Infinity
                // Pool's surcharge (SpellCastTail.AmountBilled). TASK-465.
                //
                // SurchargeNextCast is READ here and still CLEARED at its existing point below: the
                // miss arm reads it too, and consuming it early would make a missed surcharged cast
                // cost the plain price. The running cost passes targetIsWeak FALSE on purpose — the
                // weakness doubling is undone immediately before the delivery switch
                // (SpellCastTail.UndoWeakness), so a type-2 delivery never sees it.
                int billed = SpellCastTail.AmountBilled(
                    power,
                    SpellCostModifiers.Effective(power, SurchargeNextCast, targetIsWeak: false),
                    spell.TargetingType);
                if (!handlerEndsTheCast) {
                    ChargeCasterForCast(caster, casterStats, billed, roll);
                }
                // *** ONLY THE WIND-UP KINDS TEACH, AND NEVER A NEGATED CAST. *** The award pair sits
                // inside cspell_resolve_cast's `if (isNeg == 0)` kind switch, in the 0/2/3/7/8 arm
                // (CSPELL.C:1305); the swing arm (1, 4 and default) and the grid kinds pay nothing.
                if (!costWasNegated && SpellEffectApplication.AwardsCastingSkill(spell.TargetingType)) {
                    CombatAdvancement.OnSpellCast(
                        caster.IsPartyMember ? StatOrNull(casterStats, ActorAttribute.AccuracyCasting) : null,
                        StudyOf(caster), MarkOf(caster));
                }
            }

            bool hit = SpellHitResolution.AutomaticResult;
            if (SpellHitResolution.CanMiss(spell.TargetingType, costWasNegated,
                    hasTarget: target != null)) {
                hit = CombatFormulas.RangedHits(roll(100), CombatFormulas.RangedHitChance(
                    StatValue(casterStats, ActorAttribute.AccuracyCasting),
                    CombatGrid.ChebyshevDistance(caster.X, caster.Y, target.X, target.Y),
                    SpellHitResolution.AmmunitionBonus));
            }
            if (caster != null) {
                caster.FlightMissEnd = null;
                caster.FlightInterceptedBy = null;
            }
            // *** A MISSED PROJECTILE FLIES ON, AND CAN STRIKE SOMEBODY ELSE. *** The original's miss
            // is a deflected flight, not a no-op: world_rndr_ranged_attack_anim turns the heading by
            // ±[0x400,0x6FF] and flies until the shot leaves the view, and the first living bystander
            // it crosses is struck — cspell_apply_hit_at writes it back as the target with the hit
            // flag set (WORLDHIT.C:614-729, CSPELL.C:430). So everything below runs as a hit on THAT
            // combatant: its resistance, its damage, Flamecast's splash around it.
            if (!hit && caster != null && target != null
                && CombatEffectSprite.FliesProjectile(spell.AnimationEffectType)) {
                Combatant aimed = target;
                SpellProjectileMiss.Result miss = SpellProjectileMiss.Fly(
                    caster.X, caster.Y, aimed.X, aimed.Y, ArenaCellSize, roll,
                    (x, y) => LiveCombatantAt(x, y) is { } o && o != caster && o != aimed);
                if (miss.Intercepted is { } cell) {
                    target = LiveCombatantAt(cell.X, cell.Y);
                    caster.FlightInterceptedBy = target;
                    hit = true;
                } else {
                    caster.FlightMissEnd = (miss.EndX, miss.EndY);
                }
            }
            // *** THE PROJECTILE LEAVES BEFORE THE ROLL IS KNOWN. *** In the original the flight
            // happens between the two cues and its outcome IS the hit, so the launch is heard on a
            // miss as well — the same division RangedShotSound draws for a bolt. Placed above the
            // miss return for that reason and not by accident.
            if (SpellProjectileSound.Flies(target != null, spell.AnimationEffectType)) {
                _playSfx?.Invoke(SpellProjectileSound.LaunchCue);
            }
            // One line per cast with its roll, beside the AI's per-turn line: a spell that "did
            // nothing" is a miss or it is a bug, and without this the two read the same.
            _logger?.LogDebug($"Cast {spellId} pw {power} by cls{caster?.ClassId} ({caster?.X},{caster?.Y}) "
                + $"at cls{target?.ClassId} ({target?.X},{target?.Y}): hit={hit}");

            if (handlerEndsTheCast) {
                if (spellId == SpellIds.MadGodsRage) {
                    RunMadGodsRage(caster, target, roll);
                } else {
                    RunWindsOfEortis(caster, target, SpellCostModifiers.Effective(power,
                        SurchargeNextCast, targetIsWeak: TargetIsVulnerableTo(target, spellId)), roll);
                }
                return true;
            }

            if (!hit) {
                // *** A MISSED CAST IS STILL REFLECTED. *** The pending-tile test lives in the
                // delivery switch, which a miss reaches — only the damage inside the `else` is
                // gated on the hit. Returning before the wall was consulted made a Mirrorwall
                // useless against exactly the casts a player most wants it to stop, and it is the
                // caster's own accuracy that decides how often that happens.
                //
                // The cost is computed here rather than below because the miss returns first; it is
                // the same SpellCostModifiers.Effective the hit path uses, read before
                // ReflectCastFrom consumes the surcharge.
                if (ReflectorOnPathTo(caster, target, spell) is { } missedWall) {
                    int missCost = SpellCostModifiers.Effective(power, SurchargeNextCast,
                        targetIsWeak: TargetIsVulnerableTo(target, spellId));
                    ReflectCastFrom(missedWall, caster, spell, spellId, missCost, roll);
                }
                return false;
            }

            if (caster != null && !costWasNegated
                && SpellEffectApplication.AwardsCastingSkill(spell.TargetingType)) {
                CombatAdvancement.OnSpellHit(
                    caster.IsPartyMember ? StatOrNull(casterStats, ActorAttribute.AccuracyCasting) : null,
                    StudyOf(caster), MarkOf(caster));
            }

            // *** THE CAST'S OWN CUE, AND A HEAL MAKES NONE. *** cspell_resolve_cast wraps its
            // whole kind switch in `if (isNeg == 0)`, so a negated cost skips the cue and the
            // wind-up or swing together. Kind decides which: ranged for 0/2/3/7/8, melee for 1, 4
            // and ANYTHING UNRECOGNISED, and silence only for the two kinds with cases of their own
            // — the tile effect and the summon.
            //
            // Different table from SpellCastSound.ForCast, which is the FIELD caster's per-spell
            // cue. The same spell cast in a fight and out of one does not make the same noise.
            if (SpellCastSound.ForCombatCast(spell.TargetingType, costWasNegated)
                is { } castCue) {
                _playSfx?.Invoke(castCue);
            }

            // *** THE SAME SWITCH HAS AN ARM THAT PLACES A TILE, and it is why kind 5 is silent
            // above rather than merely undecided. *** cspell_resolve_cast's kind-5 case is
            // combatgrid_place_tile_fx_at_cur, inside the very `if (isNeg == 0)` gate the cue and
            // the swing share — so a negated cast places nothing, exactly as it makes no noise.
            if (!costWasNegated && spell.TileEffectId is { } tileKind
                && groundCell is { } tileCell) {
                PlaceClickedTileEffect(tileCell, tileKind, spell, power);
            }

            // *** AND THE SPELL'S OWN CUE, WHICH USED TO PLAY ON THE FIELD INSTEAD. *** Cast_Spell
            // is reached only from combat — every caller is an arena routine or a monster caster —
            // so the cues its switch and its dedicated handlers push belong here and nowhere else.
            // Eleven of them were on SpellCastSound.ForCast, which only the field caster reads, so
            // each sounded where the original is silent and was silent where it is not. TASK-292.
            //
            // Grief of 1000 Nights is the one guarded case: its cue plays only against a target the
            // spell works on, which is why the lookup takes the flag rather than the table carrying
            // it.
            bool susceptible = target == null
                || GameData.Resources.Spells.SpellPerSpellHandlers.GriefAffects(target.ClassId);
            if (SpellCastSound.ForCombatSpell(spellId, susceptible) is { } perSpellCue) {
                _playSfx?.Invoke(perSpellCue);
            }

            // A ground- or crystal-aimed spell reaches here with no target at all, by design —
            // SpellTargetingRules.CastsWithoutATarget. It has landed; there is simply nobody for the
            // magnitude to be applied to, and the lingering effect is TASK-110's.
            if (target == null) {
                // *** ONE CELL-AIMED SPELL HAS A BODY, AND IT IS BLACK NIMBUS. *** SPELLDOC calls
                // it "Deactivates trap crystals" and cspell_resolve_cast's arm 37 is
                // cspell_chance_pushback_actor(intensity) — a roll, and on success the crystal-chain
                // push at the CURSOR tile. The cue is already out: it is the per-spell cue played a
                // few lines above (SpellCastSound maps 37 to 1, sound_arrow), which is the same
                // audio_play(1) that routine opens with.
                //
                // The roll's input is the cost AFTER the amplifier and the weakness doubling, not
                // the raw power — see SpellPerSpellHandlers.BlackNimbusChancePercent. Computed here
                // rather than hoisted above this branch because hoisting would make every OTHER
                // cell-aimed spell consume SurchargeNextCast, and those spells do nothing at all
                // today; a state mutation is not the place to be clever.
                if (spellId == SpellIds.BlackNimbus && groundCell is { } aimed && Puzzle != null) {
                    bool nimbusSurcharged = SurchargeNextCast;
                    SurchargeNextCast = false;
                    int nimbusChanceInput = SpellCostModifiers.Effective(
                        power, nimbusSurcharged, targetIsWeak: false);
                    if (SpellPerSpellHandlers.BlackNimbusSucceeds(roll(100), nimbusChanceInput)) {
                        Puzzle.CollapseUntilIsolated(aimed.Column, aimed.Row);
                    }
                }
                return true;
            }

            // The burst where it lands — reached only with a target, only on a hit, and only for a
            // spell whose animation IS a projectile, which together are exactly the branch
            // Spell_ApplyHitWithProjectile plays it in.
            if (SpellProjectileSound.Flies(true, spell.AnimationEffectType)) {
                _playSfx?.Invoke(SpellProjectileSound.ImpactCue);
            }

            // *** THE MODIFIERS SCALE THE COST; THE MAGNITUDE IS COMPUTED FROM THE RESULT. ***
            // cspell_resolve_cast amplifies and doubles its `intensity` argument — which is the cost
            // the caller invested, not a damage number — and only then calls
            // cspell_compute_effect_magnitude on it. Applying them to the magnitude afterwards is a
            // DIFFERENT RULE, not a reordering: that function has flat arms (a fixed damage word,
            // the ward's zero, spell 0x15's 1000) which ignore the cost entirely, so doubling the
            // result doubles numbers the original leaves untouched — and its divide arm truncates,
            // so 2c/m and 2*(c/m) part company too. This method had it the wrong way round.
            //
            // *** THE AMPLIFIER IS AN INVENTORY ITEM, NOT WEATHER. *** g_bStormAmplify is raised in
            // exactly one place — the Infinity Pool's arm of the arena's item dispatch — and read
            // here. The "storm" in the 1993 name is the AoE spell that reads it a SECOND time, not
            // the source. See SurchargeNextCast.
            bool surcharged = SurchargeNextCast;
            SurchargeNextCast = false;
            int effectiveCost = SpellCostModifiers.Effective(power, surcharged,
                targetIsWeak: TargetIsVulnerableTo(target, spellId));

            // *** A COST-TIMES-DURATION SPELL REGISTERS A LINGERING EFFECT, IT DOES NOT DEAL
            // DAMAGE. *** cspell_resolve_cast switches on nEffect_subkind — which is what our
            // SpellCalculation enum IS, offset by one — and its case for this kind adds a status
            // effect rather than falling through to the magnitude. SpellEffectMagnitude answers 0
            // for it deliberately (see DurationMagnitude's remarks): treating that zero as "no
            // effect" gives every buff and debuff in the game nothing at all.
            // *** STEELFIRE MARKS THE TARGET'S SWORD, AND THE CAST GOES ON. *** Case 25 (CSPELL.C:1433
            // -> 1112) ORs 0x200 onto the first equipped sword in the TARGET's pack. No sword, no
            // flag, and the bill stands.
            if (spellId == SpellIds.Steelfire && !TargetResists(target, spellId)) {
                ApplySteelfire(target);
            }

            if (spell.Calculation == SpellCalculation.CostTimesDuration) {
                // CSPELL.C:1344 adds the status effect only when the resistance bitmap is clear; a
                // resisting creature is billed and takes nothing.
                if (!SpellEffectApplication.ResistanceSkipsEffect(TargetResists(target, spellId))) {
                    RegisterLingeringEffect(target, spell, spellId, effectiveCost);
                }
            }

            // The other non-damage arm: paint a field on the floor under the target. Kind is the
            // record's damage word and the timer is its duration word times the power, which is
            // why Wrath of Killian (damage word 1) is the spell that lays down burning ground.
            if (spell.Calculation == SpellCalculation.CombatGridElement) {
                PaintTileEffect(target, spell, effectiveCost);
            }

            // *** BOTH ARMS ABOVE BREAK; NEITHER RETURNS. *** cspell_resolve_cast's calculation switch
            // (CSPELL.C:1336-1368) registers the effect or paints the tile and then falls through to
            // the per-spell switch (:1370), the arm (:1457) and the delivery, like every other kind.
            // Returning here made Despair Thy Eyes' -20 accuracy unreachable (TASK-658) — measured in
            // the original: a Shade's melee accuracy 51 -> 31. The delivery is not idle for them
            // either: a Cost x Duration magnitude is 0 (case 2 of cspell_compute_effect_magnitude), so
            // it deals nothing, but a grid-element magnitude is cost x damage word (case 3), so
            // Wrath of Killian also hits the aimed target for its cost.

            int intensity = SpellEffectMagnitude.Calculate(spell, spellId, effectiveCost,
                targetHasMetalGear: HasMetalGear(target));

            // *** A RESISTED CAST IS AUDIBLE EVEN WHEN IT CHANGES NOTHING. *** The ping fires on
            // the RESISTANCE bitmap — a different table from the weakness one that scaled the cost
            // above — and fires before the delivery switch, so it sounds whether or not anything
            // then happens. That is the point of it: the player hears that the spell was resisted.
            if (TargetResists(target, spellId)) {
                _playSfx?.Invoke(SpellCastSound.ResistedCue);
            }

            // *** FIVE SPELLS COMPUTE A MAGNITUDE THE ORIGINAL NEVER DELIVERS. *** Strength Drain
            // and Evil Seek zero it in their own handler, Bane of Black Slayers zeroes it unless the
            // target is creature 22, and Dannon's Delusions and Firestorm zero it in the
            // post-animation hook. All five carry a calculation and a damage field that say
            // otherwise, so a port that stops at the arithmetic gives every one of them damage the
            // game does not deal — Bane's 50-75 against everything rather than against one creature.
            //
            // Zeroed rather than returned early, because the original zeroes the variable and both
            // delivery arms then read it: a type-2 spell in this set would move the pool by nothing
            // rather than skip the routine.
            //
            // *** THE TWO POST-ANIMATION ONES ARE APPLIED UNCONDITIONALLY AND THAT IS AN
            // ASSUMPTION. *** SpellCastTail.HooksRequireAnimationResult: the original skips the
            // whole six-spell lookup when the animation routine reports nothing back. We have no
            // animation result to gate on, so this takes the reporting branch. It is the right
            // default — the visual is the spell — but it is a choice, not a reading.
            if (SpellPerSpellHandlers.ZeroesMagnitude(spellId,
                    targetIsBlackSlayer: target.ClassId == (int)GameData.Resources.World.CreatureType.BlackSlayer)
                || SpellCastTail.ZeroesItsOwnMagnitude(spellId)) {
                intensity = 0;
            }

            // *** UNFORTUNATE FLUX'S DAMAGE IS ITS VORTEX'S ZAPS. *** Its record computes nothing
            // (NonCostRelated), and the post-animation case 20 replaces the magnitude with the global
            // the vortex's overlay passes add a 2-5 strike to (CSPELL.C:1492-1494; IDA fluxZapDamage
            // @0x3efbc). Measured in the original: 66 on SAVE91's first Shade cast, delivered whole to
            // its pool (TASK-659). Same assumption as the two hooks above: the animation reported.
            if (SpellCastTail.HookFor(spellId) == SpellCastTail.PostAnimationHook.MagnitudeFromGlobal) {
                intensity = GameData.Resources.Combat.SpellParticles.FluxZapTotal(roll);
            }

            // *** EVIL SEEK DEALS ALL ITS DAMAGE HERE, AS A CHAIN. *** Case 44 (CSPELL.C:1438) calls
            // cspell_chain_damage with the investment and zeroes the magnitude; the delivery below
            // then has nothing left to deal. Zeroing without the chain billed the cast and did
            // nothing at all (TASK-529).
            //
            // The per-spell switch sits inside CSPELL.C:1370's "the aimed target does not resist",
            // so a resisted first victim starts no chain at all: the cost is paid and nobody is hit.
            // Seen live in the original on an entry-306 rogue (TASK-541). Resisters met LATER in the
            // chain are different — they take nothing and pass it on (ApplyEvilSeekChain).
            if (spellId == SpellIds.EvilSeek && !TargetResists(target, spellId)) {
                ApplyEvilSeekChain(caster, target, effectiveCost, roll);
            }

            // *** DESPAIR THY EYES IS A DEBUFF, NOT DAMAGE. *** Its whole effect is -20 on all
            // three accuracies, applied here in the per-spell switch — the magnitude it computed
            // goes on to the delivery unchanged and is not what the spell is for.
            //
            // Gated on the RESISTANCE bitmap like the rest of that switch: a creature listed as
            // resistant takes nothing. And it reaches a party member and a monster through
            // different machinery, which TryAddStatusEffect owns — timed slots for one, a
            // permanent 8.8 attribute change for the other.
            //
            // *** STRENGTH DRAIN IS A TRANSFER, AND ITS MAGNITUDE IS ALREADY ZEROED ABOVE. ***
            // Its handler computes its own amount from the cost — a DIVISION, so a mod raising the
            // damage field weakens the spell — runs the transfer, and throws the delivery magnitude
            // away. Reading the record's calculation as the effect gives a damage spell instead.
            // The recoil half needs somebody to give the Strength to, so a caster-less cast of it
            // would be the drain without the transfer — a different spell. No trap casts it.
            if (spellId == SpellIds.StrengthDrain && caster != null) {
                ApplyDrainWithRecoil(caster, target, spellId,
                    SpellPerSpellHandlers.StrengthDrained(effectiveCost, spell.Damage));
            }

            if (spellId == SpellIds.DespairThyEyes && !TargetResists(target, spellId)) {
                // Its creation cue (case 3's audio_play(0x3a)) is played above, from
                // SpellCastSound.ForCombatSpell, with the other per-spell cues.
                foreach (ActorAttribute accuracy in SpellPerSpellHandlers.DespairAttributes) {
                    TryAddStatusEffect(target, accuracy,
                        SpellPerSpellHandlers.DespairAccuracyPenalty);
                }
            }

            // *** INVITATION DRAGS THE TARGET TOWARD THE CASTER. *** Case 30 (CSPELL.C:1436 -> 637)
            // walks it min(distance, intensity) cells at the caster's cell, and a fleeing target then
            // re-picks where it is running to. The cast carries on into the ordinary tail.
            if (spellId == SpellIds.Invitation && caster != null && !TargetResists(target, spellId)) {
                Invite(caster, target, effectiveCost, roll);
            }

            // *** NIGHTFINGERS BURNS A GLORY HAND AND OPENS THE TARGET'S PACK. *** Case 12 (CSPELL.C:1084)
            // removes the caster's first Glory Hand and puts the target's pack up for the player to
            // take from. The hand is gone either way; the spell never chooses what is stolen.
            if (spellId == SpellIds.Nightfingers && caster != null && !TargetResists(target, spellId)) {
                Steal(caster, target);
            }

            // *** THE HEAL IS ITS OWN DELIVERY, NOT NEGATIVE DAMAGE. *** An earlier version of
            // this method said the restoring spells all lived in the per-spell handlers. That was
            // half right: combat_arena_apply_damage does gate on `damage >= 1` and so cannot
            // restore anything, but the DELIVERY SWITCH routes targeting type 2 somewhere else
            // entirely — combat_ai_resolve_hit (CSPELL.C:1197, the V102CD copy), which moves the
            // target's combined pool by the magnitude with an 80% ceiling. That is the game's
            // ordinary heal and it is now delivered here.
            if (SpellCastTail.DeliveryFor(spell.TargetingType)
                == SpellCastTail.Delivery.Type2Routine) {
                _playSfx?.Invoke(SpellCastSound.PoolDeliveryCue);
                ApplyPoolHeal(target, intensity);
                return true;
            }

            // *** THE ARM RUNS ON A CAST THAT LANDED. *** cspell_resolve_cast's tail is
            // `if (pSpell != 0) cspell_invoke_effect(...)`, so an arm belongs to a cast that was not
            // cancelled by one of the per-spell cases above. Only its AUDIO is modelled; the drawing
            // is TASK-117's.
            _playSpellArm?.Invoke(spell.AnimationEffectType);
            PlaySpellVisual?.Invoke(SpellVisuals.OneShot(spellId, spell, intensity), caster, target);
            if (spell.AnimationEffectType == ThyMastersWill.EffectKind) {
                ApplyThyMastersWill(caster, target, spell, roll);
            }

            // *** THE POST-ANIMATION SWITCH, ON A HIT (CSPELL.C:1460-1486). *** Two spells have their
            // whole effect here. Final Rest (0x20) takes the target off the field and kills it with
            // no death animation; its record deals nothing. The Fetters of Rime (0x24) plays 0x4d and
            // hangs a Grief of 1000 Nights slot of cost x Duration on the target — Grief is what
            // freezes it (ActiveSpellEffectPool.IncapacitatingSpells). The cost is the one already
            // amplified and weakness-doubled, as the original's `intensity` is at this point.
            switch (SpellCastTail.HookFor(spellId)) {
                case SpellCastTail.PostAnimationHook.KillOutright:
                    // Its target is always a body (the type-7 cursor wants CAF_DEAD), so this is
                    // the corpse leaving the field: quietly, persisted as gone, and off the grid,
                    // which is what keeps a Black Slayer from ever rising (SlayerRevival.RisesThisTick).
                    KillAndSettle(target, playAnimation: false);
                    target.X = GameData.Resources.Combat.SlayerRevival.OffGrid;
                    target.Y = GameData.Resources.Combat.SlayerRevival.OffGrid;
                    break;
                case SpellCastTail.PostAnimationHook.RegisterGriefOfAThousandNights:
                    _playSfx?.Invoke(SpellCastSound.FettersCue);
                    Encounter.Effects.Register(target, SpellIds.GriefOfAThousandNights,
                        investedCost: 0, duration: effectiveCost * spell.Duration);
                    break;
            }

            // *** FLAMECAST SPLASHES, AND THE CASTER CAN BURN. *** The post-effect switch's case 4
            // (CSPELL.C:1487) runs before the delivery and before a mirrorwall is consulted, and is
            // skipped only for a cast from a tile step — a cannon, which here has no caster.
            if (caster != null && spellId == SpellIds.Flamecast) {
                ApplyFlamecastSplash(target, intensity, roll);
            }

            // *** THE MAGNITUDE IS SIGNED AND STAYS SIGNED. *** The original hands
            // cspell_compute_effect_magnitude's result straight to combat_arena_apply_damage, whose
            // `damage >= 1` guard is what makes a negative one a no-op. Taking the absolute value
            // and putting the sign back afterwards was a round trip that existed only to carry the
            // doubling, and the doubling does not belong here.
            //
            // The zero check sits BELOW the pool-heal branch on purpose: the original guards only
            // the apply-damage arm with `magnitude != 0`, and reaches combat_ai_resolve_hit
            // regardless.
            // *** A MIRRORWALL ON THE FLIGHT PATH TAKES THE CAST INSTEAD OF THE TARGET. *** The
            // original's delivery arm is `if (pending) tile_spell(caster, ...) else damage(target)`
            // — an either/or, and the pending test comes ABOVE the magnitude guard, so an
            // intercepted cast reflects even when its magnitude is zero. Placed above the zero
            // check for that reason.
            if (ReflectorOnPathTo(caster, target, spell) is { } wall) {
                ReflectCastFrom(wall, caster, spell, spellId, effectiveCost, roll);
                return true;
            }

            if (intensity == 0) {
                return true;
            }

            ApplySpellDamage(target, intensity, roll);
            return true;
        }

        /// <summary>
        /// Thy Master's Will's arm (CSPELL.C:852): a wyvern turns and runs, and the caster spends
        /// one of the spell's component. See <see cref="ThyMastersWill"/>.
        /// </summary>
        /// <remarks>
        /// The route is <c>combatenc_actor_flee_tile_east</c>: flag the creature as fleeing, drop its
        /// target and pick the exit cell now. <see cref="RunFleeStep"/> keeps a destination that is
        /// already set, and the resolver sends a fleeing monster there every turn after.
        /// </remarks>
        private void ApplyThyMastersWill(Combatant caster, Combatant target,
            GameData.Resources.Spells.Spell spell, System.Func<int, int> roll) {
            if (target == null || target.IsPartyMember || !ThyMastersWill.Affects(target.ClassId)) {
                return;
            }
            _playSfx?.Invoke(FieldSpells.GeneralSound);
            target.Flags |= CombatantFlags.Fleeing;
            target.Target = null;
            if (Grid != null) {
                target.FleeDestination = MonsterFleeDestination.Choose(FleeTileBlocked, () => roll(100));
            }
            RuntimeContainer pack = PackOf(caster);
            if (pack != null && _objects != null) {
                GameData.Resources.Inventory.InventoryConsume.TryConsumeOne(pack, spell.ObjectId, _objects.GetById);
            }
        }

        /// <summary>
        /// The first Mirrorwall the cast's projectile crosses, or null when nothing catches it.
        /// </summary>
        private (int Column, int Row)? ReflectorOnPathTo(Combatant caster, Combatant target,
            GameData.Resources.Spells.Spell spell) {
            if (caster == null || target == null || Grid == null
                || !SpellReflection.CanBeIntercepted(true, spell.AnimationEffectType)) {
                return null;
            }

            return FirstReflectorBetween(caster.X, caster.Y, target.X, target.Y);
        }

        /// <summary>The first reflecting cell on the line between two cells, or null.</summary>
        private (int Column, int Row)? FirstReflectorBetween(int fromX, int fromY,
            int toX, int toY) {
            foreach ((int x, int y) in CombatLineOfFire.CellsCrossed(fromX, fromY, toX, toY)) {
                if (x == toX && y == toY) {
                    return null;
                }
                if (Grid.TerrainAt(x, y) == SpellReflection.Reflector) {
                    return (x, y);
                }
            }
            return null;
        }

        /// <summary>
        /// Re-resolves an intercepted cast from the wall against its own caster —
        /// <c>cspell_apply_step_tile_spell(caster, spell_id, intensity, -1)</c>.
        /// </summary>
        /// <remarks>
        /// <b>It is the same routine a cannon fires through, and the shape is the same: a caster
        /// that is not an actor, and a NEGATIVE power.</b> The original builds a stub combatant on
        /// its stack, standing on the wall's cell with <c>creatureType = -1</c> and the dead flag
        /// set, and hands it to the cast routine with <c>-intensity</c>. That sign is what takes the
        /// reflected cast off the to-hit path and silences its cue and wind-up, exactly as it does
        /// for <see cref="FireCannonAt"/> — so this goes through <see cref="ResolveCast"/> with a
        /// null caster rather than through a second copy of the delivery.
        ///
        /// <para><b>The cost, not the magnitude, is what comes back.</b> The original passes its
        /// <c>intensity</c> — the invested cost after the amplifier and the weakness doubling — and
        /// the reflected cast computes its own magnitude from that. Passing the magnitude would
        /// square the calculation for every cost-scaled spell.</para>
        ///
        /// <para><b>A MISSED cast reflects too</b>, which took a second call site: the pending test
        /// lives in the delivery switch, which a miss still reaches, and only the damage inside the
        /// `else` is gated on the hit. <see cref="ResolveCast"/> returns early on a miss, so the
        /// miss arm computes the invested cost itself rather than hoisting that computation past
        /// the surcharge consumption.</para>
        ///
        /// <para><b>The reflection CANNOT chain, and that is geometry rather than a limitation.</b>
        /// The original could in principle: its stub stands on the wall and the throw-back has a
        /// flight of its own. But the wall that reflects is always the FIRST one crossed from the
        /// caster, so every cell between it and the caster has already been tested and found not to
        /// be a reflector — the return path is provably clear. A second wall further along the line
        /// is beyond the first and is never reached. So passing a null caster here costs nothing,
        /// and an earlier note calling the missing chain a gap was wrong.</para>
        /// </remarks>
        private void ReflectCastFrom((int Column, int Row) wall, Combatant caster,
            GameData.Resources.Spells.Spell spell, int spellId, int cost,
            System.Func<int, int> roll) {
            // The routine clears the amplifier on the way in, before its one guard — so a swallowed
            // Strength Drain still spends it.
            SurchargeNextCast = false;
            if (!SpellReflection.ReflectsSpell(spellId)) {
                return;
            }

            // The cue belongs to the shared routine, not to the cannon that also calls it.
            _playSfx?.Invoke(CannonLine.FireCue);

            _logger?.LogDebug("A Mirrorwall at {0},{1} threw {2} back at its caster.",
                wall.Column, wall.Row, spell.Name);
            ResolveCast(null, caster, spell, spellId, -cost, roll);
        }

        /// <summary>
        /// Strength Drain's transfer — <c>cspell_apply_damage_with_recoil</c> (CSPELL.C:1132).
        /// </summary>
        /// <remarks>
        /// <b>The spell is a TRANSFER, and the second half is invisible from the spell record.</b>
        /// It takes Strength from the target and gives Strength to the CASTER — which is why it is
        /// worth its 10-20 cost against a strong enemy rather than being a plain debuff. The record,
        /// the description and the dispatcher all say nothing about it.
        ///
        /// <para><b>The clamp comes before either write.</b> The drain is read back against the
        /// target's CURRENT Strength first, so draining a nearly-spent enemy gives the caster nearly
        /// nothing — taking half the requested amount instead over-rewards a cast aimed at someone
        /// who has little left.</para>
        ///
        /// <para><b>The caster's gain is at HALF SCALE on the permanent path, and that asymmetry is
        /// the original's.</b> Both halves choose between a timed modifier and a permanent attribute
        /// change, and the LOSS paths agree — <c>-drain</c> plain against <c>drain × -256</c> in 8.8
        /// are the same points. The GAIN paths do not: the timed one passes <c>drain/2</c> while the
        /// permanent one passes <c>(drain/2) × 128</c>, half the fixed-point scale. So a monster
        /// caster banks half what a party caster does. That is why the gain is NOT routed through
        /// <see cref="TryAddStatusEffect"/> like the loss is —
        /// <see cref="SpellCastRoutines.PermanentCasterGainPoints"/> carries the halving.</para>
        /// </remarks>
        private void ApplyDrainWithRecoil(Combatant caster, Combatant target, int spellId,
            int requestedDrain) {
            if (caster == null || target == null || TargetResists(target, spellId)) {
                return;
            }

            int current = StatValue(StatsFor(target), ActorAttribute.Strength);
            if (SpellCastRoutines.DrainKillsOutright(target.ClassId, current, requestedDrain)) {
                // A Wind Elemental IS the attribute this spell steals — a hard-coded creature check
                // inside the routine, and the caster gains nothing from the kill because the routine
                // returns there.
                target.Flags |= CombatantFlags.Dead;

                return;
            }

            int drain = SpellCastRoutines.ActualDrain(requestedDrain, current);
            TryAddStatusEffect(target, ActorAttribute.Strength, -drain);

            if (caster.IsPartyMember) {
                _session?.AddSpellStatusEffect(caster.ClassId, ActorAttribute.Strength,
                    SpellCastRoutines.CasterGain(drain), inCombat: true);

                return;
            }

            ActorStat[] casterStats = StatsFor(caster);
            if (casterStats != null && casterStats[(int)ActorAttribute.Strength] != null) {
                StatEngine.Modify(casterStats[(int)ActorAttribute.Strength], ActorAttribute.Strength,
                    (long)SpellCastRoutines.PermanentCasterGainPoints(drain) << 8);
            }
        }

        /// <summary>
        /// Apply a timed stat change to a combatant — <c>cspell_try_add_status_effect</c>
        /// (CSPELL.C:1053) for a party member, <c>stat_combatant_modify</c> for anything else.
        /// </summary>
        /// <remarks>
        /// <b>The two arms are not an implementation detail; the original branches on
        /// <c>charSlot != 0</c> at every call site.</b> Only party members have a modifier table at
        /// all — it is six characters wide — so a monster takes the change straight onto its stat
        /// instead, with the value shifted into the fixed-point form
        /// <see cref="StatEngine.Modify"/> expects. Routing a monster through the table would be
        /// wrong twice over: it has no row, and its debuff would then be permanent and
        /// combat-only rather than immediate.
        ///
        /// <para>The party arm uses the ROSTER index (<see cref="Combatant.ClassId"/>), not
        /// <see cref="Combatant.PartySlot"/>. Those coincide only while the active party is the
        /// first three characters in order — see <c>GameSession.AddSpellStatusEffect</c>.</para>
        /// </remarks>
        private void TryAddStatusEffect(Combatant actor, ActorAttribute attribute, int value) {
            if (actor == null) {
                return;
            }

            if (actor.IsPartyMember) {
                _session?.AddSpellStatusEffect(actor.ClassId, attribute, value, inCombat: true);

                return;
            }

            ActorStat[] stats = StatsFor(actor);
            if (stats != null && (int)attribute < stats.Length && stats[(int)attribute] != null) {
                StatEngine.Modify(stats[(int)attribute], attribute, (long)value << 8);
            }
        }

        /// <summary>
        /// Bills the caster — <c>Spell_ChargeCasterForCast</c>.
        /// </summary>
        /// <remarks>
        /// <b>Armour off and shields off, but the immunity tests still apply.</b> That asymmetry is
        /// the whole point of <see cref="SpellCasting.CombatCostIsWaived"/>: the waivers sit ABOVE
        /// everything the flags control, so they are asked separately here rather than folded into
        /// the damage call.
        ///
        /// <para>Two of the four waivers have no source yet — the wind elemental's creature type and
        /// a creature type of -1 are monster-caster cases the player never hits — so what is wired
        /// is the one that can occur for a party member: an incapacitated caster casts free.</para>
        /// </remarks>
        /// <summary>
        /// The point a swing costs its attacker — <c>resolveSwingAttack</c> (COMBAT.C:466).
        /// </summary>
        /// <remarks>
        /// Through the damage pipeline with <b>no armour and a null absorb pool</b>, because the
        /// original passes <c>applyArmor = 0</c> and the self-inflicted flag: see
        /// <see cref="GameData.Resources.Combat.MeleeExchange.SwingCost"/> for why that flag means
        /// a shield never pays for your own swing.
        /// </remarks>
        private void BillForSwing(Combatant attacker, System.Func<int, int> rnd) {
            if (attacker == null
                || !MeleeExchange.SwingIsBilled(attacker.Health + attacker.Stamina)) {
                return;
            }

            DamageOutcome paid = CombatFormulas.ApplyDamage(
                MeleeExchange.SwingCost, attacker.Stamina, attacker.Health,
                immune: false, applyArmor: false, armorRating: 0,
                absorbPool: null, fromDirectAttack: false, negated: false,
                weakToDamageType: false, resistsDamageType: false,
                rnd ?? (n => UnityEngine.Random.Range(0, n)));
            attacker.Stamina = paid.Stamina;
            attacker.Health = paid.Health;
            if (attacker.IsPartyMember) {
                WriteBack(attacker);
            }
        }

        private void ChargeCasterForCast(Combatant caster, ActorStat[] casterStats, int cost,
            System.Func<int, int> rnd) {
            if (SpellCasting.CombatCostIsWaived(
                    casterIsWindElemental: false,
                    casterUnderDannonsDelusions: false,
                    casterCreatureTypeIsMinusOne: false,
                    casterIncapacitated: !caster.CanAct(false))) {
                return;
            }

            // *** absorbPool IS DELIBERATELY NULL: A SHIELD DOES NOT PAY FOR YOUR OWN CAST. ***
            // combat_arena_apply_damage takes a sixth argument that skips the Hocho's Haven absorb
            // entirely, and it is 1 at exactly three call sites in the original — the cost of
            // swinging (COMBAT.C:473), the attacker's share of a resolved hit (CBTAI.C:54), and
            // this, the cast's own bill (CSPELL.C:248). Every other call passes 0. So the flag
            // means "damage the actor inflicts on itself", and a shield never absorbs it. Passing
            // AbsorbPoolOf(caster) here would let a shielded caster cast for free.
            PayFromCaster(caster, cost, rnd);
        }

        /// <summary>
        /// Takes a caster's own bill out of its pool: no shield, and the chapter-8 Crystal Staff wears
        /// by the same amount — <c>cspell_apply_damage_armor_wear</c>.
        /// </summary>
        private void PayFromCaster(Combatant caster, int cost, System.Func<int, int> rnd) {
            DamageOutcome paid = CombatFormulas.ApplyDamage(
                cost, caster.Stamina, caster.Health,
                immune: false, applyArmor: false, armorRating: 0,
                absorbPool: null, fromDirectAttack: false, negated: false,
                weakToDamageType: false, resistsDamageType: false, rnd);
            caster.Stamina = paid.Stamina;
            caster.Health = paid.Health;
            if (caster.IsPartyMember) {
                WriteBack(caster);
            }

            DrainCrystalStaff(caster, cost);
        }

        /// <summary>
        /// The chapter-8 second charge: a cast also drains the caster's equipped Crystal Staff.
        /// </summary>
        /// <remarks>
        /// <b>As well as the caster, not instead of them</b> — the original's payment routine bills
        /// the actor and then, in chapter 8 only, looks for an equipped Crystal Staff in the caster's
        /// own pack and subtracts the same cost from its variable byte, flooring at zero. Hence the
        /// call sitting after the payment above rather than replacing part of it.
        ///
        /// <para><b>Not verified in a running game, and it cannot be from here:</b> the rule exists
        /// for exactly one chapter, and reaching chapter 8 is a matter of playing the game rather
        /// than of driving a screen. Recorded on TASK-387 as such — "unreachable in the original" is
        /// a finding, not a verification.</para>
        /// </remarks>
        /// <summary>
        /// Mad God's Rage: a storm over the target's whole side that goes round until nobody is left
        /// or the caster is spent — <c>cspell_aoe_storm_damage_all</c> (CSPELL.C:879, IDA
        /// Cast_Mad_Gods_Rage @0x67acc).
        /// </summary>
        /// <remarks>
        /// Each round, every living opponent that does not resist draws <c>RND(count) &lt;
        /// count/2 + 1</c>. A struck one takes <c>15 + RND(5)</c>, half again if the cast was
        /// surcharged, and on <c>RND(3) == 0</c> five more with cue 0x1d. Each strike also costs the
        /// caster 3. The rolls come in the original's order: strike, damage, explosion.
        ///
        /// <para>ponytail: resolved in one frame. The quake frames, the per-round pause, the tile
        /// flashes and the transient knockback pose are presentation (TASK-117). Only the cues are
        /// played, and a round's thunder overlaps where the original waits between strikes.</para>
        /// </remarks>
        private void RunMadGodsRage(Combatant caster, Combatant target, System.Func<int, int> roll) {
            bool surcharged = SurchargeNextCast;
            SurchargeNextCast = false;
            if (SpellCastSound.ForCombatSpell(SpellIds.MadGodsRage) is { } quake) {
                _playSfx?.Invoke(quake);
            }

            var side = new List<Combatant>();
            foreach (Combatant c in Encounter.AllCombatants()) {
                if (c.IsPartyMember == target.IsPartyMember) {
                    side.Add(c);
                }
            }

            bool anyoneEligible = true;
            while (SpellCastRoutines.MadGodsRageGoesRoundAgain(anyoneEligible, PoolOf(caster).Pool,
                       PoolOf(caster).MaxPool)) {
                anyoneEligible = false;
                foreach (Combatant struck in side) {
                    if (struck.IsDead || TargetResists(struck, SpellIds.MadGodsRage)) {
                        continue;
                    }
                    anyoneEligible = true;
                    if (!SpellCastRoutines.MadGodsRageStrikes(side.Count, roll(side.Count))) {
                        continue;
                    }

                    _playSpellArm?.Invoke(SpellEffectArmSound.StormFlashKind);
                    PlaySpellVisual?.Invoke(new SpellVisual(SpellVisualKind.StormFlash), caster, struck);
                    int rollUnder5 = roll(5);
                    bool exploded = roll(SpellCastRoutines.MadGodsRageExplosionOneIn) == 0;
                    if (exploded) {
                        // The extra hit's white flash and sparks, colour 0xAF spread 25 (CSPELL.C:961).
                        PlaySpellVisual?.Invoke(new SpellVisual(SpellVisualKind.SparkBurst, 0xaf, 25, Tint: 3),
                            caster, struck);
                    }
                    if (exploded && SpellCastSound.PerTarget(SpellIds.MadGodsRage) is { } boom) {
                        _playSfx?.Invoke(boom);
                    }
                    ApplySpellDamage(struck,
                        SpellCastRoutines.MadGodsRageDamage(surcharged, rollUnder5, exploded), roll);
                    PayFromCaster(caster, SpellCastRoutines.MadGodsRageCostPerActorPerRound, roll);
                }
            }
        }

        /// <summary>
        /// Winds of Eortis: the caster pays, the wind blows, and a victim that does not resist is
        /// pushed one cell per point of cost directly away — <c>cspell_perform_ranged_hit</c>
        /// (CSPELL.C:653, IDA Cast_Winds_of_Eortis @0x67526) and <c>cspell_actor_walk_steps</c> (:357).
        /// </summary>
        /// <remarks>
        /// The bill is the RUNNING cost, surcharge and weakness included: the routine is handed the
        /// dispatcher's live intensity, not the saved one. The push stops at the first cell the victim
        /// cannot enter. The inert direction (straight along -Y, see
        /// <see cref="SpellCastRoutines.KnockbackDy"/>) spends the allowance and moves nobody.
        ///
        /// <para><b>The victim is always the aimed target, as in the original.</b>
        /// <c>cspell_perform_ranged_hit</c> flies its whirlwind with <c>hit = 1</c> already set, and
        /// a flight that already hit never intercepts (WORLDHIT.C, <c>*p_hit_out != 0</c> clears the
        /// bystander); case 27 then clears <c>pSpell</c>, so the generic miss/interception after the
        /// switch never runs for it (CSPELL.C:653-668, 1425-1427). The to-hit roll only decides the
        /// caster's second skill award. Checked 2026-10-02. River Song's transient slot and the walk
        /// animation are presentation.</para>
        /// </remarks>
        private void RunWindsOfEortis(Combatant caster, Combatant victim, int cost,
            System.Func<int, int> roll) {
            SurchargeNextCast = false;
            PayFromCaster(caster, cost, roll);
            if (SpellCastSound.ForCombatSpell(SpellIds.WindsOfEortis) is { } wind) {
                _playSfx?.Invoke(wind);
            }
            if (TargetResists(victim, SpellIds.WindsOfEortis)) {
                // The immune path is the only one that reaches kind 12's 100-frame whirlwind
                // (CSPELL.C:824); the ordinary one flies the growing whirlwind instead (CSPELL.C:653).
                PlaySpellVisual?.Invoke(new SpellVisual(SpellVisualKind.Whirlwind), caster, victim);
                return;
            }
            PlaySpellVisual?.Invoke(new SpellVisual(SpellVisualKind.WhirlwindFlight), caster, victim);

            int direction = SpellCastRoutines.KnockbackDirection(victim.X - caster.X, victim.Y - caster.Y);
            int dx = SpellCastRoutines.KnockbackDx(direction);
            int dy = SpellCastRoutines.KnockbackDy(direction);
            for (int steps = SpellCastRoutines.KnockbackCells(cost); steps > 0 && !victim.IsDead; steps--) {
                if (SpellCastRoutines.KnockbackIsInert(direction)) {
                    continue;
                }
                if (Grid.IsBlocked(victim.X + dx, victim.Y + dy)) {
                    break;
                }
                MoveTo(victim, victim.X + dx, victim.Y + dy);
            }
        }

        /// <summary>
        /// Steelfire's mark: the first equipped sword in the target's pack gets
        /// <see cref="ItemFlags.SteelFired"/> — <c>Cast_Steelfire</c> (IDA @0x68166).
        /// </summary>
        private void ApplySteelfire(Combatant target) {
            RuntimeContainer pack = PackOf(target);
            int index = SpellCastRoutines.SteelfireTarget(pack, _objects);
            if (index < 0) {
                return;
            }
            RuntimeItem sword = pack.Items[index];
            sword.ItemFlags = SpellCastRoutines.ApplySteelfire(sword.ItemFlags);
            pack.Dirty = true;
        }

        /// <summary>
        /// Invitation's pull — <c>Cast_Invitiation</c> (IDA @0x674af): the target walks at the
        /// caster's cell for <see cref="SpellCastRoutines.InvitationPull"/> steps, through the same
        /// <see cref="CombatWalk"/> a monster turn uses, and a fleeing target re-picks its exit.
        /// </summary>
        private void Invite(Combatant caster, Combatant target, int power, System.Func<int, int> roll) {
            int steps = SpellCastRoutines.InvitationPull(
                CombatGrid.ChebyshevDistance(caster.X, caster.Y, target.X, target.Y), power);
            int startX = target.X;
            int startY = target.Y;
            AnnounceShove(CombatWalk.Walk(Grid, target, caster.X, caster.Y, steps,
                puzzle: Puzzle, occupiedByLiveCombatant: TileHoldsLiveCombatant,
                onHazard: (hurt, hazard) => ApplyWalkHazard(hurt, hazard, roll)));
            int endX = target.X;
            int endY = target.Y;
            target.X = startX;
            target.Y = startY;
            MoveTo(target, endX, endY);
            if (SpellCastRoutines.InvitationRerollsAFleeingTargetsDestination
                && (target.Flags & CombatantFlags.Fleeing) != 0) {
                target.FleeDestination = MonsterFleeDestination.Choose(FleeTileBlocked, () => roll(100));
            }
        }

        /// <summary>
        /// Nightfingers' theft — <c>Cast_Nightfingers</c> (IDA @0x680ac): the caster's Glory Hand is
        /// consumed and the target's pack is handed to <see cref="OpenStolenPack"/>.
        /// </summary>
        /// <remarks>
        /// ponytail: the stolen item's flight back to the caster, which the original plays only if the
        /// target's count changed, is presentation and is not played.
        /// </remarks>
        private void Steal(Combatant caster, Combatant target) {
            RuntimeContainer casterPack = PackOf(caster);
            if (casterPack != null && _objects != null) {
                GameData.Resources.Inventory.InventoryConsume.TryConsumeOne(
                    casterPack, SpellCastRoutines.GloryHandObjectId, _objects.GetById);
            }
            RuntimeContainer loot = PackOf(target);
            if (loot != null) {
                OpenStolenPack?.Invoke(loot);
            }
        }

        private void DrainCrystalStaff(Combatant caster, int cost) {
            if (caster == null || !caster.IsPartyMember || _session == null || cost <= 0) {
                return;
            }
            if (!SpellCastTail.DrainsCrystalStaff(_session.Chapter,
                    casterHasEquippedCrystalStaff: true)) {
                // The chapter half first: it is false for seven chapters out of eight, so the pack
                // walk below is skipped entirely for almost every cast in the game.
                return;
            }

            RuntimeContainer pack = _session.GetActorInventory(caster.ClassId);
            if (pack?.Items == null) {
                return;
            }
            foreach (RuntimeItem item in pack.Items) {
                if (item == null || item.ObjectId != CrystalStaffObjectId
                    || (item.ItemFlags & (ushort)GameData.ItemFlags.Equipped) == 0) {
                    continue;
                }
                item.Variable = (byte)SpellCastTail.DrainStaff(item.Variable, cost);
                return;
            }
        }

        /// <summary>The Crystal Staff's object id — the same one Raw Manna recharges.</summary>
        private const int CrystalStaffObjectId = 1;

        /// <summary>
        /// Paints a spell field on the cell under the target — the grid arm of
        /// <c>cspell_resolve_cast</c>.
        /// </summary>
        /// <remarks>
        /// <b>The record supplies both halves and neither is named for what it does here.</b> The
        /// kind comes from the spell's DAMAGE word and the timer from its DURATION word times the
        /// invested power (<see cref="SpellEffectApplication.GridElementStrength"/>) — so a grid
        /// spell's power comes from a duration field even though nothing about it lasts for a
        /// duration, and its element from a damage field even though the field's damage is a flat
        /// band decided elsewhere (<see cref="TerrainDamage"/>).
        ///
        /// <para><b>It goes under the TARGET, not under the cursor.</b> The calculation-driven arm
        /// stamps the cell the target occupies; the delivery-category arm that paints where you
        /// click is a different path (Mirrorwall and Gambit of the Eight), still unbuilt.</para>
        /// </remarks>
        private void PaintTileEffect(Combatant target, GameData.Resources.Spells.Spell spell,
            int power) {
            if (target == null || Grid == null) {
                return;
            }

            int timer = SpellEffectApplication.GridElementStrength(power, spell.Duration);
            if (timer <= 0) {
                return;
            }
            Grid.SetTileEffect(target.X, target.Y, (CombatTerrain)spell.Damage, timer);
        }

        /// <summary>
        /// Paints the spell's own tile effect on the cell the player clicked — the kind-5 arm of
        /// <c>cspell_resolve_cast</c>'s wind-up switch, <c>combatgrid_place_tile_fx_at_cur</c>.
        /// </summary>
        /// <remarks>
        /// <b>A different field from <see cref="PaintTileEffect"/>, read from a different word, put
        /// somewhere else.</b> That one stamps the cell the TARGET stands on with the spell's
        /// DAMAGE word; this one stamps the cell you CLICKED with its <c>EffectSubject</c>. Two
        /// spells take this arm — Mirrorwall (7) and Gambit of the Eight (8) — and neither has a
        /// target to stand on, so the two paths never compete for a cell.
        ///
        /// <para><b>Both guards, and a refusal is silent.</b> The original writes only if the cell
        /// holds no grid element AND its terrain reads 0. So a Mirrorwall cannot be laid on a
        /// crystal, a cannon, another wall or anybody's feet — and the cast is still paid for,
        /// because the placement sits inside the wind-up switch and the billing is further down.
        /// Reporting the refusal would be a kindness the original does not extend.</para>
        ///
        /// <para><b>The amplifier is consumed here.</b> The original amplifies <c>intensity</c> in
        /// its prologue, so the cost this arm reads is already scaled — a surcharged Mirrorwall
        /// stands half again as long. Taken inline for the same reason
        /// <see cref="SpellIds.BlackNimbus"/> takes it inline: hoisting it would spend the surcharge
        /// on every cell-aimed spell, including the ones that still do nothing.</para>
        /// </remarks>
        private void PlaceClickedTileEffect((int Column, int Row) cell, int kind,
            GameData.Resources.Spells.Spell spell, int power) {
            if (Grid == null
                || Grid.TerrainAt(cell.Column, cell.Row) != CombatTerrain.Open
                || Grid.IsOccupied(cell.Column, cell.Row)) {
                return;
            }

            bool surcharged = SurchargeNextCast;
            SurchargeNextCast = false;
            int timer = SpellEffectApplication.GridElementStrength(
                SpellCostModifiers.Effective(power, surcharged, targetIsWeak: false),
                spell.Duration);
            if (timer <= 0) {
                return;
            }
            Grid.SetTileEffect(cell.Column, cell.Row, (CombatTerrain)kind, timer);
        }

        /// <summary>
        /// Ages the arena's spell fields and burns whoever is standing in one —
        /// <c>cspell_tick_damage_terrain</c>.
        /// </summary>
        /// <returns>How many combatants the floor hurt.</returns>
        /// <remarks>
        /// <b>The kind is READ BEFORE the cell is aged, and the damage switches on that captured
        /// value.</b> The original takes <c>field = terrain(x,y)</c>, then ticks, then switches on
        /// <c>field</c> — so a field on its LAST tick still burns whoever is standing in it, using
        /// the kind it had on the way in.
        ///
        /// <para>Reading the terrain after the tick instead looks equivalent and is not: the tick
        /// reverts an expiring cell to <see cref="CombatTerrain.Open"/>, so the later read finds
        /// bare floor and the field lapses without its final bite. That is exactly the mistake this
        /// method was written with, and the test named for the last tick is what caught it.</para>
        /// </remarks>
        public int TickTerrainEffects(System.Func<int, int> rnd = null) {
            if (Grid == null || Encounter == null) {
                return 0;
            }

            System.Func<int, int> roll = rnd ?? (n => UnityEngine.Random.Range(0, n));
            var burned = 0;
            for (var y = 0; y < CombatGrid.Height; y++) {
                for (var x = 0; x < CombatGrid.Width; x++) {
                    int kind = (int)Grid.TerrainAt(x, y);
                    Grid.TickTileEffect(x, y);

                    Combatant occupant = LiveCombatantAt(x, y);
                    if (!TerrainDamage.Burns(kind, occupant != null, occupantResists: false)) {
                        continue;
                    }

                    ApplySpellDamage(occupant, TerrainDamage.DamageFor(roll), roll);
                    burned++;
                }
            }
            return burned;
        }

        /// <summary>
        /// Starts the revival clock on a creature that can come back.
        /// </summary>
        /// <remarks>
        /// <b>Rolled AT DEATH, not when the sweep first notices.</b> The countdown is set on the way
        /// down, so two Black Slayers that fall in the same round still get back up at different
        /// times — a sweep that assigned it on first sight would synchronise them.
        ///
        /// <para><b>A creature that FLED is barred twice over</b> and this only handles the near
        /// half: <see cref="SlayerRevival.IsCandidate"/> rejects the fled flag, and the exit path
        /// kills a routing creature without playing the death animation — which is the only place
        /// the countdown is ever set. Either alone would be enough.</para>
        /// </remarks>
        private void ArmRevivalIfEligible(Combatant fallen, System.Func<int, int> rnd) {
            if (fallen == null || !SlayerRevival.IsEligibleSpecies(fallen.ClassId)
                || fallen.IsPartyMember) {
                return;
            }
            if ((fallen.Flags & CombatantFlags.Fleeing) != 0) {
                return;
            }
            fallen.RevivalCountdown = SlayerRevival.RollCountdown(rnd);
        }

        /// <summary>
        /// Counts the fallen Black Slayers down and gets them back up —
        /// <c>monster_tickBlackSlayerRevival</c> (0x6B01C) and the rise at CBTAIACT.C:278.
        /// </summary>
        /// <returns>How many rose.</returns>
        /// <remarks>
        /// <b>The sweep does not run unless a risen Black Slayer is already in the fight</b>
        /// (<see cref="SlayerRevival.SweepRuns"/>), so an encounter carrying only the transforming
        /// creature never sees a single revival — the first riser needs one to rise for.
        ///
        /// <para><b>A fallen creature keeps its grid coordinates.</b> <c>Kill</c> frees the tile's
        /// occupancy but leaves X and Y alone, which is what lets
        /// <see cref="SlayerRevival.RisesThisTick"/>'s on-grid test pass — a corpse carried off the
        /// field could not come back.</para>
        ///
        /// <para><b>The tile flourish is presentation and is NOT ported here.</b> The original
        /// paints kind 9 for 400 and then burns it down in a tight loop of fifteen terrain ticks
        /// per frame until it clears — that is how it runs the rise animation for a fixed span, not
        /// a four-hundred-round field. Handing 400 to the per-round sweep would leave crystal ground
        /// under the riser for the rest of the fight. What the state owes is the rise itself.</para>
        /// </remarks>
        public int TickSlayerRevivals(System.Func<int, int> rnd = null) {
            if (Encounter == null) {
                return 0;
            }

            var slayers = 0;
            foreach (Combatant c in Encounter.AllCombatants()) {
                if (c != null && c.ClassId == SlayerRevival.RisenType) {
                    slayers++;
                }
            }
            if (!SlayerRevival.SweepRuns(slayers)) {
                return 0;
            }

            var risen = 0;
            foreach (Combatant c in Encounter.AllCombatants()) {
                if (c == null || c.RevivalCountdown == SlayerRevival.NoCountdown
                    || !SlayerRevival.IsCandidate(c.ClassId, (int)c.Flags)) {
                    continue;
                }

                if (!SlayerRevival.RisesThisTick(c.RevivalCountdown, c.X)) {
                    c.RevivalCountdown--;
                    continue;
                }

                if (!SlayerRevival.CanRiseOnTile(TileHoldsLiveCombatant(c.X, c.Y))) {
                    // Somebody is standing on the grave; it waits rather than losing its turn.
                    continue;
                }

                // *** THE CUE IS INSIDE THE TILE TEST. *** A body waiting under something that has
                // blocked its grave is silent; only the rise itself sounds. See
                // SlayerRevival.RisingSound.
                _playSfx?.Invoke(SlayerRevival.RisingSound);
                Revive(c);
                risen++;
            }
            return risen;
        }

        /// <summary>
        /// Gets one fallen creature back up — the state half of the rise.
        /// </summary>
        /// <remarks>
        /// <b>WHAT GETS UP IS NOT WHAT WENT DOWN.</b> The transforming creature becomes the risen
        /// one (<see cref="SlayerRevival.TypeAfterRising"/>), one way, and it comes back at FULL
        /// strength rather than as a weakened survivor.
        ///
        /// <para><b>The flags are ASSIGNED, not OR-ed</b> — see
        /// <see cref="SlayerRevival.FlagsAfterRising"/>. That is the one line a port gets wrong:
        /// OR-ing Ready leaves the Dead bit set and produces a fully-healed corpse that never acts
        /// and can never be killed again.</para>
        /// </remarks>
        private void Revive(Combatant fallen) {
            ActorStat[] stats = StatsFor(fallen);
            ActorStat health = StatOrNull(stats, ActorAttribute.Health);
            ActorStat stamina = StatOrNull(stats, ActorAttribute.Stamina);

            fallen.ClassId = SlayerRevival.TypeAfterRising(fallen.ClassId);
            if (SlayerRevival.RisesAtFullStrength) {
                fallen.Health = health?.Max ?? fallen.Health;
                fallen.Stamina = stamina?.Max ?? fallen.Stamina;
            }
            fallen.Flags = SlayerRevival.FlagsAfterRising(fallen.Flags);
            fallen.RevivalCountdown = SlayerRevival.NoCountdown;
            MoveTo(fallen, fallen.X, fallen.Y);   // it occupies its tile again

            if (_logger != null) {
                LoggerExtensions.LogInformation(_logger,
                    "A fallen creature rose as a Black Slayer at ({X}, {Y}).", fallen.X, fallen.Y);
            }
        }

        /// <summary>
        /// The living combatant on a tile, or null — what the cursor is over.
        /// </summary>
        /// <remarks>
        /// <b>This is how a target is picked, and it is the whole rule.</b>
        /// <c>combat_actor_terr_under_cur</c> (CACTOR.C:741) is nothing but
        /// <c>combatgrid_tile_terrain(g_cursor_tile_x, g_cursor_tile_y)</c> — the grid's
        /// tile-to-occupant map at the cursor's cell. There is no sprite test anywhere in the
        /// original's pick, which is why a tall creature cannot be grabbed from a cell it does not
        /// stand on (TASK-589).
        /// </remarks>
        public Combatant CombatantAtCell(int x, int y) => LiveCombatantAt(x, y);

        /// <summary>
        /// Whether <paramref name="actor"/> could walk to a cell this turn — the original's move-map
        /// bit 1, which <c>combatgrid_build_move_attack_map</c> sets from
        /// <c>combatgrid_actor_try_step_tile</c>, a dry run of the walker bounded by the actor's own
        /// movement (CMBTGRID.C:1419-1431), and which picks the cursor's move marker
        /// (<c>combatgrid_cursor_tile_movable</c>, CMBTGRID.C:1445; COMBAT.C:2324-2326).
        /// </summary>
        public bool CellMovable(Combatant actor, int x, int y) =>
            actor != null && Grid != null && CombatGrid.InBounds(x, y)
            && CombatWalk.Walk(Grid, actor, x, y, actor.Speed, probe: true, puzzle: Puzzle,
                occupiedByLiveCombatant: TileHoldsLiveCombatant).Arrived;

        /// <summary>The living combatant on a tile, or null.</summary>
        private Combatant LiveCombatantAt(int x, int y) {
            foreach (Combatant c in Encounter.AllCombatants()) {
                if (c != null && !c.IsDead && c.X == x && c.Y == y) {
                    return c;
                }
            }
            return null;
        }

        /// <summary>
        /// Hangs a timed effect on the target — <c>cspell_status_effect_add</c>, reached from
        /// <c>cspell_resolve_cast</c>'s subkind switch.
        /// </summary>
        /// <remarks>
        /// <b>The duration is not the record's duration.</b> It is
        /// <see cref="SpellEffectApplication.DurationMagnitude"/> — the power times the record's
        /// duration field, or DIVIDED by it when that field is negative, which is the same
        /// sign-flips-the-arithmetic trick the damage calculation uses with no flag to announce it.
        /// Reading the field as a plain length inverts every spell that scales down.
        ///
        /// <para><b>Then one tick is added when the target is not ready</b>
        /// (<see cref="SpellEffectApplication.AdjustDurationForTarget"/>) — a flat bonus applied
        /// after the arithmetic, depending on the TARGET's state rather than the caster's or the
        /// spell's. Easy to miss and it shifts every duration by one.</para>
        ///
        /// <para><b>A full pool silently does nothing.</b> Twenty slots are shared by everyone on
        /// the field, and <c>Register</c> answers -1 rather than evicting — so the cast still costs
        /// and still spends the turn, and simply leaves no effect. That is the original's behaviour;
        /// logged here so it is visible rather than mysterious.</para>
        ///
        /// <para><b>The flag byte carries the spell's colour</b> — a value that reads as
        /// presentation doing duty as effect data
        /// (<see cref="SpellEffectApplication.EffectFlagIsTheSpellColour"/>). We have no colour on
        /// the record yet, so 0 goes in and the per-spell handlers that read it will need it.</para>
        /// </remarks>
        private void RegisterLingeringEffect(Combatant target, GameData.Resources.Spells.Spell spell,
            int spellId, int power) {
            if (target == null || Encounter == null) {
                return;
            }

            int duration = SpellEffectApplication.DurationMagnitude(power, spell.Duration);
            duration = SpellEffectApplication.AdjustDurationForTarget(
                duration, (target.Flags & CombatantFlags.Ready) != 0);
            if (duration <= 0) {
                return;
            }

            int slot = Encounter.Effects.Register(target, spellId, power, duration);
            if (slot == GameData.Resources.Spells.ActiveSpellEffectPool.None) {
                _logger?.LogDebug(
                    $"Spell {spellId} landed but the effect pool is full; no lingering effect.");
                return;
            }

            // Incapacitation is derived from the chain, never set by hand — see Combatant.Incapacitated.
            Encounter.Effects.RefreshIncapacitation(target);
        }

        /// <summary>
        /// The targeting-type-2 delivery: move the target's combined pool, capped at four fifths —
        /// <c>combat_ai_resolve_hit</c> (CSPELL.C:1197, the <c>#ifdef V102CD</c> copy).
        /// </summary>
        /// <param name="magnitude">
        /// SIGNED, and deliberately so. The original passes the magnitude straight through to
        /// <c>stat_combatant_modify(target, 0x10, magnitude &lt;&lt; 8, 0x50)</c>, so a negative one
        /// takes the pool down rather than healing.
        /// </param>
        /// <remarks>
        /// <b>A heal cannot take anyone past four fifths of full</b>, and casting on someone already
        /// there does nothing while still costing — <see cref="SpellCastRoutines.HealTargetPercent"/>
        /// is passed to <see cref="StatEngine.ModifyHealthPool"/>, which models that ceiling.
        ///
        /// <para><b>Six afflictions block it outright</b> — not reduce it
        /// (<see cref="SpellCastRoutines.AfflictionsThatBlockHealing"/>). So a poisoned or starving
        /// character cannot be healed by magic at all, a substantial tactical rule that no part of
        /// the spell data expresses. A monster is always healable: it has no affliction row.</para>
        ///
        /// <para><b>Applied to the COMBATANT's live pool, through scratch stats.</b> The combat
        /// values are the ones a fight reads and <see cref="WriteBack"/> copies outward; writing the
        /// session's ActorStat directly would heal the save and leave the fight unchanged. The
        /// maxima come from the real stat block because the ceiling is a fraction of them.</para>
        /// </remarks>
        private void ApplyPoolHeal(Combatant target, int magnitude) {
            ActorStat[] stats = StatsFor(target);
            ActorStat healthStat = StatOrNull(stats, ActorAttribute.Health);
            ActorStat staminaStat = StatOrNull(stats, ActorAttribute.Stamina);
            if (healthStat == null || staminaStat == null) {
                return;
            }

            if (!SpellCastRoutines.HealApplies(
                    target.IsPartyMember ? target.PartySlot : 0,
                    target.IsPartyMember ? _session?.ConditionsOf(target.ClassId) : null)) {
                return;
            }

            var health = new ActorStat { Base = (byte)Clamp(target.Health), Max = healthStat.Max };
            var stamina = new ActorStat { Base = (byte)Clamp(target.Stamina), Max = staminaStat.Max };
            ActorConditions victimConditions =
                target.IsPartyMember ? _session?.ConditionsOf(target.ClassId) : null;
            StatEngine.ModifyHealthPool(health, stamina, (long)magnitude << 8,
                SpellCastRoutines.HealTargetPercent, out bool drained,
                conditions: victimConditions, inCombat: true);

            target.Health = health.Base;
            target.Stamina = stamina.Base;
            if (target.IsPartyMember) {
                WriteBack(target);
            }
            // The rank the pool routine just wrote is what the party-down byte is derived from.
            if (drained && victimConditions != null) {
                _session?.RecomputePartyDeathState();
            }
        }

        private static int Clamp(int value) => value < 0 ? 0 : (value > 0xff ? 0xff : value);

        /// <summary>
        /// Applies a hazard a <see cref="CombatWalk"/> stepped into.
        /// </summary>
        /// <remarks>
        /// <b>Every hazard a walk fired used to be discarded.</b> All three call sites passed the
        /// puzzle and the cover predicate but no <c>onHazard</c>, so crystal ground, tile traps and
        /// cannons were computed and thrown away — and because <see cref="CombatWalk"/> reads the
        /// actor's death back out of this callback, a walk could also never be cut short by what it
        /// walked into. TASK-241's audit could not see it: <see cref="CombatWalk"/> itself has three
        /// production callers, so the type looks consumed while its hazard half is dead.
        ///
        /// <para><b>A tile trap's damage is the cell's own countdown, and it is now supplied.</b>
        /// This note used to say the number was per-tile and nothing extracted it, so every trap
        /// dealt 0. It is <c>combatgrid_tile_field4</c> — the timer that
        /// <see cref="GameData.Resources.Combat.CombatGrid.SetTileEffect"/> already stores — which
        /// makes Gambit of the Eight's "Damage: Variable" the power it was cast at times its
        /// duration word. All three hazards now land: the trap here, crystal ground through the
        /// damage below, a cannon through <see cref="FireCannonAt"/>.</para>
        ///
        /// <para>The zero guard is the original's: its apply-damage arm is gated on
        /// <c>damage >= 1</c>, so a hazard with nothing to deal is not a no-op call, it is not a
        /// call.</para>
        /// </remarks>
        private void ApplyWalkHazard(Combatant actor, CombatWalk.Hazard hazard,
            System.Func<int, int> rnd) {
            if (actor == null) {
                return;
            }

            if (hazard.Kind == CombatWalk.HazardKind.CannonShot) {
                FireCannonAt(actor, hazard, rnd);
                return;
            }

            // *** THE CUE COMES BEFORE THE DAMAGE, because it belongs to the cine. *** The
            // terrain-3 arm plays combat_actor_play_short_cine and only then applies the 100, so a
            // step that kills still sounds first. Above the damage guard for the same reason.
            if (hazard.Kind == CombatWalk.HazardKind.CrystalGround) {
                _playSfx?.Invoke(CombatWalk.CrystalGroundSoundId);
                // The cine's picture, and the other nine of its ten zaps (SpellVfx.CrystalZapAsync).
                PlaySpellVisual?.Invoke(new SpellVisual(SpellVisualKind.CrystalZap), null, actor);
            }

            // The trap's own cue is cspell_tile_trap_trigger's FIRST line, before it has even read
            // the charge — so a trap that turns out to hold nothing is still heard.
            if (hazard.Kind == CombatWalk.HazardKind.TileTrap) {
                _playSfx?.Invoke(CombatWalk.TileTrapSoundId);
            }

            if (hazard.Damage <= 0) {
                return;
            }
            ApplySpellDamage(actor, hazard.Damage, rnd);
        }

        /// <summary>
        /// The noise a shove makes, if it fired a crystal.
        /// </summary>
        /// <remarks>
        /// <b>A fired crystal deals no damage — the whole consequence is audible and visible.</b>
        /// <c>PushTrapElement</c> @0x2ebd5 flips the run to the firing state, plays a short cine,
        /// flips it back, and calls <c>combatGrid_playTileFx</c>, which is a sound and a particle
        /// burst on a phantom actor. Then it takes the diamond off the grid: each shove SPENDS a
        /// diamond to clear a run, which is the puzzle.
        ///
        /// <para>The run itself is <see cref="TrapPuzzle.TraceCrystalLine"/> and the burst is not
        /// modelled — neither has anywhere to be drawn yet. The sound does, so it plays.</para>
        /// </remarks>
        private void AnnounceShove(CombatWalk.WalkResult walk) {
            if (walk.Shove?.Result == PushResult.CrystalFired) {
                _playSfx?.Invoke(TrapPuzzle.FiredSoundId);
            }

            // *** A CANNON FIRED AT A CRYSTAL IS A CUE AND NOTHING ELSE. *** The original runs the
            // four-direction cannon scan against a throwaway combatant standing on the cell the
            // crystal landed on, so every cannon that sees it casts Flamecast at a stub that is
            // discarded — no damage, no post switched off. Playing the muzzle cue is the whole of
            // the observable behaviour; see CombatWalk.Shove.CannonsFired.
            for (var i = 0; i < (walk.Shove?.CannonsFired ?? 0); i++) {
                _playSfx?.Invoke(CannonLine.MuzzleCue);
            }
        }

        /// <summary>
        /// A cannon that had line on the tile shoots whoever stepped there.
        /// </summary>
        /// <remarks>
        /// <b>A cannon deals no number — it casts.</b> <c>Cast_Flamecast</c> @0x2fb80 publishes the
        /// cannon's own cell in the two grid globals and calls
        /// <c>castCombatSpell(target, Flamecast, power = 20, actorId = -2)</c>, and
        /// <c>castCombatSpell</c> @0x68201 synthesises a caster on its stack and passes
        /// <b>-power</b> to the cast routine.
        ///
        /// <para>That negative sign is the whole of what makes this different from a combatant's
        /// cast, and the port already models it in two places: the cast never rolls to hit
        /// (<see cref="GameData.Resources.Spells.SpellHitResolution.CanMiss"/>) and it makes no
        /// CASTER cue and no wind-up
        /// (<see cref="GameData.Resources.Spells.SpellCastSound.ForCombatCast"/>) — the two cues
        /// below are the cannon's and the tile cast's, which is a different pair.
        /// Both took a hard-coded <c>false</c> until this call existed. So the shot goes through
        /// <see cref="ResolveCast"/> like everything else, with a null caster, rather than through a
        /// second copy of the delivery whose drift nobody would notice.</para>
        ///
        /// <para><b>The cannon's tile is not used yet, and that is a known gap.</b> The original
        /// stands its synthetic caster there so any distance-dependent part of the effect measures
        /// from the cannon. Flamecast's magnitude is cost-scaled and reads no distance, so nothing
        /// observable turns on it today — but a mod that made a cannon fire a distance-scaled spell
        /// would be wrong here. <see cref="CannonLine.Shot"/> carries the tile when it is wanted.</para>
        /// </remarks>
        private void FireCannonAt(Combatant target, CombatWalk.Hazard hazard,
            System.Func<int, int> rnd) {
            if (Spells?.Spells == null
                || !Spells.Spells.TryGetValue(CannonLine.SpellId,
                    out GameData.Resources.Spells.Spell spell)) {
                _logger?.LogDebug(
                    "A cannon at {0},{1} had line but SPELLS.DAT is not loaded, so it fired nothing.",
                    hazard.SourceX, hazard.SourceY);
                return;
            }

            // *** BOTH CUES COME BEFORE THE CAST, in this order. *** The muzzle is
            // combatgrid_actor_step_to_tile's own audio_play(1); the fire cue is the first thing
            // cspell_apply_step_tile_spell does once it has cleared the spell-42 guard. ResolveCast
            // then plays a THIRD cue, the projectile's launch, which is id 1 again — see
            // CannonLine.MuzzleCue, that doubling is the original's and not a bug here.
            _playSfx?.Invoke(CannonLine.MuzzleCue);
            _playSfx?.Invoke(CannonLine.FireCue);

            ResolveCast(null, target, spell, CannonLine.SpellId, -CannonLine.Intensity, rnd);
        }


        /// <summary>Whether this caster is under Thoughts Like Clouds.</summary>
        /// <remarks>
        /// Type 0x1f in the original's status pool is spell 31 by the same spell-id-as-type rule
        /// that makes type 6 the ward — SPELLDOC calls it "Confuses enemy spellcasters", and its
        /// record is CostTimesDuration with a Duration of -2, so it lasts <c>power / 2</c> ticks.
        /// </remarks>
        private bool SilencedForCasting(Combatant caster) =>
            caster != null && Encounter != null
            && Encounter.Effects.Find(caster, SpellIds.ThoughtsLikeClouds)
               != GameData.Resources.Spells.ActiveSpellEffectPool.None;

        /// <summary>The defender's remaining absorb shield, or null when none is up.</summary>
        /// <remarks>
        /// <b>Hocho's Haven IS the shield.</b> <c>combat_arena_apply_damage</c> looks the pool up by
        /// type 6 (COMBAT.C:339), and the persistent register writes the SPELL ID into that field
        /// (CSPELL.C:1358) — so type 6 is spell 6. Its slot's duration doubles as the shield's
        /// points, which is why the original names the field <c>nDuration_or_hp</c>: the record's
        /// Duration word is 4, so a power-10 cast is a 40-point shield.
        ///
        /// <para>The neighbouring type-1 test in that guard is NOT spell 1. Dannon's Delusions is
        /// CostTimesDamage and never reaches the persistent register; the literal kind 1 comes from
        /// <c>cspell_summon_actor</c>. The pool's type field carries two overlapping namespaces and
        /// this is one of the places they collide.</para>
        /// </remarks>
        private int? AbsorbPoolOf(Combatant target) {
            if (target == null || Encounter == null) {
                return null;
            }
            int slot = Encounter.Effects.Find(target, SpellIds.HochosHaven);
            return slot == GameData.Resources.Spells.ActiveSpellEffectPool.None
                ? (int?)null
                : Encounter.Effects[slot].Duration;
        }

        /// <summary>Whether the defender's damage is zeroed outright — Skin of the Dragon.</summary>
        /// <remarks>
        /// Type 0x17 is spell 23 by the same spell-id-as-type rule. Its record's Duration word is
        /// -1, so the negative branch of the duration formula gives <c>power / 1</c> — the negation
        /// lasts as many ticks as the power invested.
        /// </remarks>
        private bool DamageNegatedFor(Combatant target) =>
            target != null && Encounter != null
            && Encounter.Effects.Find(target, SpellIds.SkinOfTheDragon)
               != GameData.Resources.Spells.ActiveSpellEffectPool.None;

        /// <summary>Write an absorb shield's remaining points back, or drop it when it broke.</summary>
        /// <remarks>
        /// <b>Not optional.</b> The original mutates the slot in place; reading the pool without
        /// writing it back would absorb the same damage for ever.
        /// </remarks>
        private void CommitAbsorb(Combatant target, int? poolAfter) {
            if (target == null || Encounter == null) {
                return;
            }
            int slot = Encounter.Effects.Find(target, SpellIds.HochosHaven);
            if (slot == GameData.Resources.Spells.ActiveSpellEffectPool.None) {
                return;
            }
            if (poolAfter.HasValue) {
                Encounter.Effects[slot].Duration = poolAfter.Value;
            } else {
                Encounter.Effects.Remove(target, slot);
            }
        }

        /// <summary>
        /// The damage-type mask a SPELL's damage carries — <c>combat_arena_apply_damage(target,
        /// magnitude, 0, 1, <b>0x200</b>, 0)</c> on <c>cspell_resolve_cast</c>'s kind-0 arm.
        /// </summary>
        /// <remarks>
        /// The caster's own bill goes through the same routine with a mask of <b>0</b>
        /// (<c>cspell_apply_damage_armor_wear</c>, CSPELL.C:248), so neither scaler touches it —
        /// which is why the two cost call sites below still pass false.
        /// </remarks>
        private const int SpellDamageMask = 0x200;

        /// <summary>
        /// The creature class the affinity tables are indexed by. <b>NOT
        /// <see cref="Combatant.ClassId"/> for a party member</b>, which carries the roster index.
        /// </summary>
        /// <remarks>
        /// The original's <c>CombatantState.creatureType</c> is a real creature class for everybody,
        /// party included — read live from the chapter-1 arena on 2026-09-12: Locklear 17, Gorath 15,
        /// Owyn 16, and the save's own <c>SaveGameCombatData.CreatureType</c> carries the same three.
        /// Three of those rows have affinity flags, so asking the table for class 0/1/2 answers "no
        /// affinity" for the entire party.
        /// </remarks>
        private int CreatureClassOf(Combatant c) =>
            c == null ? 0
            : !c.IsPartyMember ? c.ClassId
            : _partyEntries?.EntryFor(c.ClassId + 1)?.CreatureType ?? c.ClassId;

        /// <summary>
        /// Whether this actor takes half again as much, or half, from a damage type —
        /// <c>cbstat_apply_proficiency_bonus</c> and <c>cbstat_apply_weakness_penalty</c>
        /// (CBSTAT.C:145-161), which <c>combat_arena_apply_damage</c> applies to every blow.
        /// </summary>
        private (bool Weak, bool Resists) DamageAffinityOf(Combatant target, int effectMask) {
            GameData.Resources.Combat.CreatureAffinity row =
                Affinity?.AffinityOf(CreatureClassOf(target));

            return row == null
                ? (false, false)
                : ((row.WeaknessFlags & effectMask) != 0, (row.ResistanceFlags & effectMask) != 0);
        }

        /// <summary>
        /// Flamecast's splash — <c>combat_arena_splash_dmg_near</c> (COMBAT.C:417): everyone but the
        /// target within <see cref="SpellCastRoutines.FlamecastSplashRadius"/>, party first, then the
        /// encounter, each unless it resists spell 4.
        /// </summary>
        private void ApplyFlamecastSplash(Combatant target, int magnitude, System.Func<int, int> rnd) {
            if (target == null || Encounter == null) {
                return;
            }
            foreach (IReadOnlyList<Combatant> side in new IReadOnlyList<Combatant>[] { Encounter.Party, Encounter.Enemies }) {
                foreach (Combatant c in side) {
                    if (c == target || c.IsDead || TargetResists(c, SpellIds.Flamecast)) {
                        continue;
                    }
                    int distance = CombatGrid.ChebyshevDistance(target.X, target.Y, c.X, c.Y);
                    int damage = SpellCastRoutines.FlamecastSplashDamage(magnitude, distance);
                    if (distance <= SpellCastRoutines.FlamecastSplashRadius && damage >= 1) {
                        ApplySpellDamage(c, damage, rnd);
                    }
                }
            }
        }

        /// <summary>
        /// Evil Seek's chain — <c>cspell_chain_damage</c> (CSPELL.C:565).
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>The power starts at twice the investment and keeps 80% per hop after the first,
        /// integer-truncated. A hop that truncates to 0 ends the chain.</item>
        /// <item>The next victim is the <b>first living actor, in roster order, not yet hit</b> —
        /// <c>cspell_find_alive_actor_not_list</c> (:486). No distance is involved.</item>
        /// <item>A victim the spell's resistance list names takes nothing, but is still marked hit and
        /// still passes the chain on.</item>
        /// <item>The chain runs through the side opposite the caster (the original swaps its actor
        /// lists when an encounter actor casts), for at most as many hops as that side holds, and
        /// never more than the visited list's seven.</item>
        /// </list>
        /// </remarks>
        private void ApplyEvilSeekChain(Combatant caster, Combatant target, int effectiveCost,
            System.Func<int, int> rnd) {
            if (target == null || Encounter == null) {
                return;
            }
            bool casterIsEnemy = caster != null ? Encounter.Enemies.Contains(caster) : Encounter.Party.Contains(target);
            IReadOnlyList<Combatant> side = casterIsEnemy
                ? (IReadOnlyList<Combatant>)Encounter.Party
                : Encounter.Enemies;
            var hit = new List<Combatant>(SpellCastRoutines.EvilSeekVisitedCapacity);
            int power = SpellCastRoutines.EvilSeekInitialPower(effectiveCost);
            for (int hop = 0;
                 target != null && hop < side.Count && hop < SpellCastRoutines.EvilSeekVisitedCapacity;
                 hop++) {
                if (hop > 0) {
                    power = power * SpellCastRoutines.EvilSeekFalloffPercent / 100;
                }
                if (SpellCastRoutines.EvilSeekEndsAtZeroPower(power)) {
                    break;
                }
                // Each hop flies from the last victim (the caster, first) — CSPELL.C:565-607.
                PlaySpellVisual?.Invoke(new SpellVisual(SpellVisualKind.HopBurst),
                    hit.Count > 0 ? hit[hit.Count - 1] : caster, target);
                if (!TargetResists(target, SpellIds.EvilSeek)) {
                    ApplySpellDamage(target, power, rnd);
                }
                hit.Add(target);
                target = null;
                foreach (Combatant candidate in side) {
                    if (!candidate.IsDead && !hit.Contains(candidate)) {
                        target = candidate;
                        break;
                    }
                }
            }
        }

        /// <summary>Damage from a spell, which bypasses armour but NOT the defensive spells.</summary>
        /// <remarks>
        /// <b>Only the armour is bypassed. The shield and the negation are not.</b>
        /// <c>combat_arena_apply_damage</c>'s last argument gates three separate things, and a spell
        /// aimed at a target wants a different answer for the first than for the other two:
        /// <list type="bullet">
        /// <item><c>apply_magic_defense = 0</c> — no armour, which is what "bypasses armour the way
        /// the cost does" refers to, and it is right.</item>
        /// <item><c>source_type = 0</c> — so the type-6 absorb pool (Hocho's Haven, COMBAT.C:339)
        /// and the type-0x17 negation (Skin of the Dragon, COMBAT.C:355) <b>both apply</b>.</item>
        /// </list>
        ///
        /// <para><b>This read <c>fromDirectAttack: false</c> until 2026-09-21</b>, copied from the
        /// self-cost calls a few hundred lines up, which really are <c>source_type = 1</c>. The
        /// armour reason is sound and the other two came along with it — so Hocho's Haven and Skin
        /// of the Dragon stopped a swing and a shot but let every spell through. Every one of the
        /// original's spell-at-target sites passes 0 (CSPELL.C:529, :550, :592, :1035, :1528,
        /// :2444); the only sites passing 1 are self-damage and recoil.</para>
        /// </remarks>
        private void ApplySpellDamage(Combatant target, int magnitude, System.Func<int, int> rnd) {
            (bool weak, bool resists) = DamageAffinityOf(target, SpellDamageMask);
            DamageOutcome outcome = CombatFormulas.ApplyDamage(
                magnitude, target.Stamina, target.Health,
                immune: false, applyArmor: false, armorRating: 0,
                absorbPool: AbsorbPoolOf(target), fromDirectAttack: true,
                negated: DamageNegatedFor(target),
                weakToDamageType: weak, resistsDamageType: resists, rnd);
            target.Stamina = outcome.Stamina;
            target.Health = outcome.Health;
            // Not optional — the original mutates the slot in place, so a pool read without a
            // write-back absorbs the same damage for ever.
            CommitAbsorb(target, outcome.AbsorbPool);
            // The original reaches this through Spell_PlayHitPaletteFlash @0x66d33, which marks the
            // target with the SAME rung a blow uses. Effects that differ carry their own constants —
            // a storm is 3, a heal is 4 — and get them when those effects are ported.
            MarkHit(target, outcome.Landed, outcome.DamageDealt);

            if (target.Health <= 0) {
                KillAndSettle(target);
            }
            if (target.IsPartyMember) {
                WriteBack(target);
            }
        }

        /// <summary>
        /// Whether the target counts as carrying metal — <c>IsUsingMetal</c> @0x639ba.
        /// </summary>
        /// <remarks>
        /// <b>A staff counts.</b> The lookup aliases Sword to Staff, so the predicate's own name
        /// overstates what it tests. Only Skyfire consults it, and against a target with none of the
        /// three its 40 damage is zero — a real tactical rule, not a rounding detail.
        /// </remarks>
        private bool HasMetalGear(Combatant target) =>
            EquippedOfCategory(target, ArmorCategory).Info != null
            || EquippedOfCategory(target, MeleeWeaponCategory).Info != null
            || EquippedOfCategory(target, StaffCategory).Info != null;

        /// <summary>The accuracy field of the object <see cref="RangedAmmoAccuracy"/> names, or 0.</summary>
        private int AmmoAccuracyOfRecord(int quarrelKind) {
            int objectId = RangedAmmoAccuracy.RecordFor(quarrelKind);
            // nDefense_or_range_close is the fourth stat word, which our extractor names for its
            // other two meanings; on ammunition it is the accuracy the shot adds.
            return objectId == RangedAmmoAccuracy.NoRecord
                ? 0
                : _objects?.GetById(objectId)?.SwingAccuracy_ArmorMod_BowAccuracy ?? 0;
        }

        /// <summary>The quarrel's own damage contribution, or null when the kind has no record.</summary>
        private int? QuarrelBaseDamage(int quarrelKind) {
            if (quarrelKind < 0 || quarrelKind >= QuarrelInventory.ObjectIdByKind.Length) {
                return null;
            }
            GameData.Resources.Object.ObjectInfo quarrel = _objects?.GetById(QuarrelInventory.ObjectIdByKind[quarrelKind]);
            return quarrel?.SwingBaseDamage;
        }

        /// <summary>
        /// Wears every equipped item of a category — <c>cbstat_damage_equipped_items</c>.
        /// </summary>
        /// <remarks>
        /// <b>EVERY matching item, not the first one.</b> The original walks the whole pack, and
        /// asking for a sword also reaches a staff
        /// (<see cref="ItemDegradation.CategoryMatches"/>) — so a character carrying both equipped
        /// has both worn. <see cref="EquippedOfCategory"/> deliberately stops at the first INTACT
        /// item because that is the one that fights; wear is the other question and needs the walk.
        ///
        /// <para><b>A monster wears nothing</b>, for the same reason it fights bare-handed: its
        /// weapon is not an inventory item.</para>
        ///
        /// <para>The pack is marked dirty only when something actually changed, so an attack that
        /// passes every degrade roll does not make a save rewrite the container.</para>
        /// </remarks>
        private void WearEquipped(Combatant combatant, int category, int severity,
            System.Func<int, int> rnd) {
            if (_objects == null) {
                return;
            }
            RuntimeContainer pack = PackOf(combatant);
            if (pack == null) {
                return;
            }

            var requested = (ObjectType)category;
            foreach (RuntimeItem item in pack.Items) {
                GameData.Resources.Object.ObjectInfo info = _objects.GetById(item.ObjectId);
                if (!ItemDegradation.Wears(requested, info, (ItemFlags)item.ItemFlags)) {
                    continue;
                }

                ItemDegradation.Result worn =
                    ItemDegradation.Apply(info, item.Variable, (ItemFlags)item.ItemFlags, severity, rnd);
                if (worn.Condition == item.Variable && (ushort)worn.Flags == item.ItemFlags) {
                    continue;
                }

                item.Variable = (byte)worn.Condition;
                item.ItemFlags = (ushort)worn.Flags;
                pack.Dirty = true;

                if (worn.Snapped) {
                    // The one item failure the game gives a sound to.
                    _playSfx?.Invoke(ItemDegradation.SnapSoundId);
                }
            }
        }

        /// <summary>
        /// The cue a swing makes — see <see cref="MeleeSwingSound"/>.
        /// </summary>
        /// <remarks>
        /// <b>Parry is the defender's flag AND its ability to act.</b> The original requires both, so
        /// a downed combatant's raised guard makes no clang — the same <c>CanAct</c> reading the
        /// defense rating uses.
        /// </remarks>
        private void PlaySwingCue(Combatant attacker, Combatant defender, bool hit) {
            if (_playSfx == null) {
                return;
            }

            bool attackerStaff = EquippedOfCategory(attacker, StaffCategory).Info != null;
            if (hit) {
                _playSfx(MeleeSwingSound.Hit(attacker.ClassId, attackerStaff));
                return;
            }

            bool parried = (defender.Flags & CombatantFlags.Parry) != 0 && defender.CanAct(false);
            _playSfx(MeleeSwingSound.MissCue(parried, defender.ClassId,
                EquippedOfCategory(defender, StaffCategory).Info != null, attackerStaff));
        }

        /// <summary>Equipment category 3 — a staff, which is what the swing cues read for material.</summary>
        public const int StaffCategory = 3;

        /// <summary>Equipment category 1 — the melee weapon.</summary>
        /// <remarks>
        /// <c>combat_arena_resolve_melee_swing</c> asks for category 1 and nothing else, so a
        /// crossbow in hand does not swing.
        /// </remarks>
        public const int MeleeWeaponCategory = 1;

        /// <summary>Equipment category 4 — worn armour.</summary>
        public const int ArmorCategory = 4;

        /// <summary>
        /// <b>The weapon's accuracy and damage fields are NOT crossed — this used to say they
        /// were, and every melee in the game was resolved on the wrong pair because of it.</b>
        /// </summary>
        /// <remarks>
        /// The claim was assembled out of two different routines: the to-hit came from the SWING
        /// (<c>combat_arena_melee_attack</c>, <c>nDefense_or_range_close</c>) and the damage from
        /// the THRUST (<c>resolve_melee_swing</c>, which passes <c>attack_type = 1</c> and so reads
        /// <c>nThrust_damage</c>). Read either body whole and it pairs like its name.
        ///
        /// <para><b>What is genuinely swapped is canassa's two function NAMES.</b>
        /// <c>combat_arena_resolve_melee_swing</c> is the thrust and <c>combat_arena_melee_attack</c>
        /// is the swing — see <see cref="CombatActionDispatch.AccuracyOf"/>, which now owns the
        /// pairing and the evidence.</para>
        /// </remarks>
        public static bool MeleeWeaponFieldsPairByName => true;

        /// <summary>An equipped item, or an empty value meaning bare-handed / unarmoured.</summary>
        private readonly struct Equipped {
            public Equipped(GameData.Resources.Object.ObjectInfo info, int condition,
                ItemFlags flags) {
                Info = info;
                Condition = condition;
                Flags = flags;
            }

            public GameData.Resources.Object.ObjectInfo Info { get; }

            /// <summary>Percent. <see cref="EquippedGear.UntrackedCondition"/> when not tracked.</summary>
            public int Condition { get; }

            public ItemFlags Flags { get; }
        }

        /// <summary>
        /// The combatant's equipped, intact item of a category.
        /// </summary>
        /// <remarks>
        /// <b>Party members only.</b> A monster's weapon is not an inventory item — its damage comes
        /// from the creature's own stats — so an enemy always reads as bare-handed here, which is
        /// what the original does too.
        ///
        /// <para>Condition is only real for a <see cref="ObjectFlags.Degradable"/> type; everything
        /// else reads as <see cref="EquippedGear.UntrackedCondition"/>, and Broken beats both. This
        /// is the same rule <see cref="EquipmentSlots"/> applies, asked of one category.</para>
        /// </remarks>
        private Equipped EquippedOfCategory(Combatant combatant, int category) {
            var none = new Equipped(null, EquippedGear.UntrackedCondition, default);
            if (_objects == null) {
                return none;
            }

            RuntimeContainer pack = PackOf(combatant);
            if (pack == null) {
                return none;
            }

            foreach (RuntimeItem item in pack.Items) {
                GameData.Resources.Object.ObjectInfo info = _objects.GetById(item.ObjectId);
                if (info == null) {
                    continue;
                }
                int condition = EquippedGear.ConditionOf(
                    (item.ItemFlags & (ushort)ItemFlags.Broken) != 0,
                    (info.Flags & GameData.Resources.Object.ObjectFlags.Degradable) != 0,
                    item.Variable);
                if (EquippedGear.SlotSatisfies(
                        (item.ItemFlags & (ushort)ItemFlags.Equipped) != 0,
                        (int)info.ObjectType, condition, category)) {
                    return new Equipped(info, condition, (ItemFlags)item.ItemFlags);
                }
            }
            return none;
        }

        private static ActorStat StatOrNull(ActorStat[] stats, ActorAttribute attribute) {
            int i = (int)attribute;
            return stats != null && i >= 0 && i < stats.Length ? stats[i] : null;
        }

        /// <summary>The grid this fight is being fought on. Null outside a fight.</summary>
        public CombatGrid Grid { get; private set; }

        /// <summary>
        /// Walls the arena down to the ground the world actually offers, called once the grid exists
        /// and <b>before</b> any combatant is placed on it.
        /// </summary>
        /// <remarks>
        /// Set by whoever owns the world probe — the runtime has no view of the terrain itself.
        /// Null leaves the grid as constructed, which is what an authored trap puzzle wants.
        /// </remarks>
        public System.Action<CombatGrid> LayOutGround { get; set; }

        /// <summary>The encounter's trap puzzle, or null for an ordinary fight.</summary>
        /// <remarks>Kept because the walk needs it: only cannons read it, and only a puzzle has any.</remarks>
        public TrapPuzzle Puzzle { get; private set; }

        /// <summary>
        /// Move a combatant, keeping the grid's occupancy honest.
        /// </summary>
        /// <remarks>
        /// <b>Neither <see cref="CombatWalk"/> nor <see cref="CombatMovement"/> touches occupancy</b>
        /// — they take a grid and move an actor, and the caller owns the bookkeeping. Skipping it
        /// leaves a phantom blocker on every tile anyone has ever stood on, and the fight silently
        /// seizes up as the grid fills with them.
        /// </remarks>
        private void MoveTo(Combatant combatant, int x, int y) {
            if (combatant.X == x && combatant.Y == y) {
                return;
            }
            Grid.SetOccupied(combatant.X, combatant.Y, false);
            combatant.X = x;
            combatant.Y = y;
            Grid.SetOccupied(x, y, true);
        }

        /// <summary>Whether a tile holds a LIVING combatant — cannon line-of-sight cover.</summary>
        /// <remarks>
        /// <b>The grid's own occupancy cannot stand in for this.</b> It also carries elements, and
        /// one element kind is deliberately transparent to a shot; a corpse is not cover either.
        /// </remarks>
        private bool TileHoldsLiveCombatant(int x, int y) {
            foreach (Combatant c in Encounter.Party) {
                if (!c.IsDead && c.X == x && c.Y == y) {
                    return true;
                }
            }
            foreach (Combatant c in Encounter.Enemies) {
                if (!c.IsDead && c.X == x && c.Y == y) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Play out one monster's turn: decide, close the distance, and swing if it got there.
        /// </summary>
        /// <param name="monster">Whoever is acting.</param>
        /// <param name="resolver">The AI. Required — there is no sensible default for "what would it do".</param>
        /// <param name="rnd">The <c>rnd(n)</c> seam; defaults to the real generator.</param>
        /// <remarks>
        /// <b>This is the seam <see cref="AdvanceToPartyTurn"/> has always taken and nobody ever
        /// passed</b>, so every enemy forfeited its turn — a fight the party could not lose.
        ///
        /// <para><b>Walking toward the target's own tile is deliberate, not sloppy aiming.</b> The
        /// tile is occupied, so the walk is refused on the last step and the monster stops beside it,
        /// which is exactly where it needs to be. Picking an adjacent tile explicitly would mean
        /// choosing WHICH side to approach from — a decision the original does not make here.</para>
        ///
        /// <para><b>Cast is the one decision not carried out here</b>, because the spell catalogue is
        /// not here — <c>HotspotService.RunEnemyTurn</c> delivers it. Melee, Flee, Retreat and Shoot
        /// (<see cref="FireMonsterShot"/>) are all acted on. Shoot used to be logged and dropped, so
        /// every crossbow and species shooter in the game forfeited its turn (TASK-502).</para>
        /// </remarks>
        public MonsterTurnResolver.Decision ResolveEnemyTurn(Combatant monster,
            MonsterTurnResolver resolver, System.Func<int, int> rnd = null) {
            if (monster == null || resolver == null || Encounter == null) {
                return default;
            }

            // *** THE WANDERING ROUTINE WALKS FIRST, AND DECIDES FROM WHERE IT LANDED. ***
            // combataiact_random_move_attack (CBTAIACT.C:39-84) is class 0x31's whole turn, and it
            // relocates BEFORE it looks at anything: random tile, reroll until reachable, walk, and
            // only then combatenc_find_nearest_actor. Deciding first and walking after — which is
            // what every other routine here does — measured the distance from the cell the creature
            // was about to leave, so MonsterTurnRoutines.AfterWandering got the one number its own
            // parameter doc says it must not be given (TASK-615).
            if (GameData.Resources.Combat.CombatAi.SpeciesRoutineOf(monster.ClassId)
                == GameData.Resources.Combat.CombatAi.SpeciesRoutine.WalkRandomTileThenAttackOrBrace) {
                WanderToRandomTile(monster, rnd);
                // flags & 2 is CAF_DEAD: a creature that died on its own walk does not then act.
                // The guard is 1.02-CD-only and we target that build — the floppy attacks anyway.
                if (!GameData.Resources.Combat.MonsterTurnRoutines.ActsAfterMoving(monster.IsDead)) {
                    return default;
                }
            }

            MonsterTurnResolver.Decision decision = resolver.Resolve(Encounter, monster);
            try {
                // A caster that cast nothing still moves or guards; the decision it reports stays a Cast.
                if (decision.Fallback is { } fallback) {
                    CarryOutEnemyTurn(monster, new MonsterTurnResolver.Decision(fallback, decision.Target,
                        maxSteps: decision.MaxSteps), rnd);
                    return decision;
                }
                return CarryOutEnemyTurn(monster, decision, rnd);
            } finally {
                if (decision.ClearsTargetAfterTurn) {
                    monster.Target = null;
                }
            }
        }

        /// <summary>
        /// The wandering routine's opening move: relocate to a RANDOM reachable tile.
        /// </summary>
        /// <remarks>
        /// <b>Not toward anything.</b> <c>combataiact_random_move_attack</c> (CBTAIACT.C:43-49)
        /// rolls <c>RND2(8)</c> and <c>RND(0xd)</c> — the grid's own 8x13 — builds the move/attack
        /// map once, and rerolls until the rolled tile carries its bit 1, which
        /// <c>combatgrid_actor_try_step_tile</c> sets from a DRY RUN of the walker bounded by the
        /// actor's own movement (CMBTGRID.C:1401,1419). <see cref="CombatWalk.Walk"/>'s
        /// <c>probe</c> is that dry run, so the reroll asks the same question the original does.
        ///
        /// <para><b>The creature's own tile is a legal roll and is not skipped</b> — it probes as
        /// reachable, so "wandered nowhere" is an outcome the original has and this keeps.</para>
        ///
        /// <para><b>Deviation, deliberate:</b> the original's <c>while</c> has no bound and spins
        /// until it rolls a reachable tile. A cap is kept here because this runs on the Editor's
        /// main thread, and exhausting it simply leaves the creature where it stands — the same
        /// outcome as rolling its own tile, which is why the fallback needs no separate rule. With
        /// 104 tiles and the actor's own always reachable, exhausting 200 rolls is not a case the
        /// shipped grids produce.</para>
        /// </remarks>
        private void WanderToRandomTile(Combatant monster, System.Func<int, int> rnd) {
            if (monster == null || Grid == null) {
                return;
            }

            System.Func<int, int> pick = rnd ?? (n => UnityEngine.Random.Range(0, n));
            var destX = -1;
            var destY = -1;
            for (var tries = 0; tries < WanderTileRolls; tries++) {
                int x = pick(CombatGrid.Width);
                int y = pick(CombatGrid.Height);
                CombatWalk.WalkResult reach = CombatWalk.Walk(Grid, monster, x, y, monster.Speed,
                    probe: true, puzzle: Puzzle,
                    occupiedByLiveCombatant: TileHoldsLiveCombatant);
                if (reach.Arrived) {
                    destX = x;
                    destY = y;
                    break;
                }
            }

            if (destX < 0) {
                return;
            }

            int startX = monster.X;
            int startY = monster.Y;
            CombatWalk.WalkResult walk = CombatWalk.Walk(Grid, monster, destX, destY, monster.Speed,
                puzzle: Puzzle, occupiedByLiveCombatant: TileHoldsLiveCombatant,
                onHazard: (hurt, hazard) => ApplyWalkHazard(hurt, hazard, pick));
            AnnounceShove(walk);

            // Walk moved the combatant itself; put it back and re-apply through MoveTo so the
            // grid's occupancy follows it — the same two-step the approach walk below uses.
            int endX = monster.X;
            int endY = monster.Y;
            monster.X = startX;
            monster.Y = startY;
            MoveTo(monster, endX, endY);
        }

        /// <summary>How many random tiles the wandering routine will roll before giving up.</summary>
        private const int WanderTileRolls = 200;

        /// <summary>Carries out a decided enemy turn — see <see cref="ResolveEnemyTurn"/>.</summary>
        private MonsterTurnResolver.Decision CarryOutEnemyTurn(Combatant monster,
            MonsterTurnResolver.Decision decision, System.Func<int, int> rnd) {

            // A routing monster leaves the field instead of taking any other action —
            // monster_combatTurn (@0x64501) tests CAF_FLEE right after the morale check and jumps
            // past the whole species switch and capability cascade when it is set.
            if (decision.Action == AiAction.Flee) {
                RunFleeStep(monster, rnd);
                return decision;
            }

            // An engaged caster spends the whole turn backing away instead of casting — see
            // AiAction.Retreat. Like the rout, the search needs the grid, so the resolver decides
            // and this carries it out.
            if (decision.Action == AiAction.Retreat) {
                RunRetreatStep(monster, rnd);
                return decision;
            }

            // *** CREATURE 0x36 PUTS ITS STRENGTH BACK EVERY TURN, attack or not. *** It is the first
            // line of combataiact_ranged_attack — see MonsterHeavyRangedTurn.RestoresStrengthEachTurn.
            if (CombatAi.SpeciesRoutineOf(monster.ClassId) == CombatAi.SpeciesRoutine.RangedPoisonAttack) {
                ActorStat[] restored = StatsFor(monster);
                if (restored != null) {
                    int strength = (int)MonsterHeavyRangedTurn.RestoredAttribute;
                    restored[strength].Base = restored[strength].Max;
                }
            }

            // *** A SHOT IS FIRED, AND A SHOOTER WITH NOTHING TO FIRE CLOSES IN. *** Without a quarrel
            // every crossbow attempt of the original's pattern loop declines and the turn falls to its
            // advance — which is the melee path below, so the decision carries on into it.
            if (decision.Action == AiAction.Shoot && decision.Target != null
                && FireMonsterShot(monster, decision, rnd)) {
                return decision;
            }

            // *** THE ROW WALKS REST AND RAISE A GUARD. *** combatenc_actor_enter_defense and
            // combatenc_set_flag8_clear_flag1 are the party's REST and Defend commands, taken by the
            // monster turns as outcomes of their own (TASK-528).
            if (decision.Action == AiAction.Rest) {
                ResolveRest(monster);
                return decision;
            }
            if (decision.Action == AiAction.Defend) {
                ResolveDefend(monster);
                return decision;
            }

            if ((decision.Action != AiAction.MeleeOrMove && decision.Action != AiAction.Shoot)
                || decision.Target == null) {
                _logger?.LogDebug(
                    $"Monster turn: {decision.Action} is decided but not carried out yet.");
                return decision;
            }

            Combatant target = decision.Target;

            // *** THE 0x13 ROUTINE NEVER WALKS. *** combataiact_pick_melee_or_missl (CBTAIACT.C:22-37) swings when
            // the nearest actor is within Chebyshev 1, diagonals included, and otherwise casts or shoots. The
            // approach walk below sent a speed-0 Brak Nurr at an unreachable cell and ended its turn without the
            // blow (TASK-525, measured on SAVE71 with Gorath beside it: the original swung, the port never did).
            if (decision.Action == AiAction.MeleeOrMove
                && CombatAi.SpeciesRoutineOf(monster.ClassId) == CombatAi.SpeciesRoutine.MeleeAdjacentElseCastOrShoot) {
                if (decision.MaxSteps == null
                    && CombatGrid.ChebyshevDistance(monster.X, monster.Y, target.X, target.Y) <= 1) {
                    SwingSpeciesBlow(monster, target, rnd);
                }
                return decision;
            }

            int startX = monster.X;
            int startY = monster.Y;
            System.Func<int, int> hazardRoll = rnd ?? (n => UnityEngine.Random.Range(0, n));
            // *** BESIDE the target, not ON it. *** The original stores an approach cell in
            // inner->dest (CMBTAI.C:330-366) and walks to that; passing target.X/target.Y walked at
            // a cell that is always occupied and, as often as not, on the wrong side. Measured
            // 2026-09-12 in the same fight on both sides: the original's monster went (6,7) ->
            // (5,4) with dest (5,1), ours went (6,7) -> (4,4). See CombatAi.ApproachCell.
            //
            // Picked here rather than in the resolver for the reason RunRetreatStep gives below:
            // the choice needs the grid's blocked test and the resolver is documented as deciding
            // only.
            (int destX, int destY) = CombatAi.ApproachCell(
                monster.X, monster.Y, target.X, target.Y, Grid.IsBlocked);
            CombatWalk.WalkResult walk = CombatWalk.Walk(Grid, monster, destX, destY,
                decision.MaxSteps ?? monster.Speed, puzzle: Puzzle,
                occupiedByLiveCombatant: TileHoldsLiveCombatant,
                onHazard: (hurt, hazard) => ApplyWalkHazard(hurt, hazard, hazardRoll));
            AnnounceShove(walk);

            // Walk moved the combatant itself; put it back and re-apply through MoveTo so the grid's
            // occupancy follows it.
            int endX = monster.X;
            int endY = monster.Y;
            monster.X = startX;
            monster.Y = startY;
            MoveTo(monster, endX, endY);

            if (monster.IsDead) {
                // A hazard on the way killed it — the walk is over and so is the turn.
                return decision;
            }

            // *** A WALK THAT DID NOT ARRIVE DROPS THE TARGET. ***
            // combataipath_select_target ends with
            //     if (target && (gridX != destX || gridY != destY)
            //         && combataipath_actor_walk_path(actor, 0) == 0) target = 0;
            // (CMBTAI.C:371-376) — the walk IS that call, so a monster that could not reach the cell
            // beside its target stops aiming at it and picks again next turn. Keeping the target
            // instead is what leaves one shuffling against an obstacle for the rest of the fight.
            //
            // The swing is skipped with it, because follow_tgt_check's attack arm is inside an
            // `if (target != 0)`. A monster that was ALREADY beside its target has dest == its own
            // cell, arrives trivially, and is unaffected.
            if (monster.X != destX || monster.Y != destY) {
                monster.Target = null;
                return decision;
            }

            // *** A CAPPED ADVANCE IS A CASTER'S FALLBACK, AND IT ONLY WALKS. *** combat_ai_take_turn's
            // tail (CBTAI.C:373-381) caps the speed and calls combataipath_select_target(actor, 100, 0),
            // which ends in combataipath_actor_walk_path and nothing else (CMBTAI.C). Swinging here
            // had Timirianya's Pantathians -- casters with nothing castable -- punch Pug and Owyn to
            // death in the Riftworld Mine, where the original's close in and stand.
            if (decision.MaxSteps == null) {
                SwingIfAdjacent(monster, target, rnd);
            }
            return decision;
        }

        /// <summary>
        /// A monster's ranged turn, carried out: the crossbow cascade's shot, or its species' own attack.
        /// </summary>
        /// <returns>
        /// False only when a crossbow shooter has nothing to fire, which the caller answers with the
        /// advance. Every species attack here always goes off.
        /// </returns>
        /// <remarks>
        /// <b>Three families, and only the shot through <see cref="ResolveShot"/> rolls to hit.</b>
        /// <list type="bullet">
        /// <item>No species routine — <c>combataiturn_action_disp_base</c> (CBTAITRN.C:233): the best
        /// kind carried. With none, the original's pattern attempts all decline.</item>
        /// <item>0x13's far branch and the charging classes throw a rock: the same shot, kind 8, no
        /// pack (CBTAIACT.C:34, :188).</item>
        /// <item>The other four routines strike without a to-hit roll or armour, each with its own band
        /// and cues (CBTAIACT.C:85-257) — see <see cref="StrikeFromRange"/>.</item>
        /// </list>
        ///
        /// <para><b>Not drawn:</b> the projectile, the status-4 particle burst and the knockback frames.
        /// The damage, the cues and the recoil are the rule; the picture is VFX work (TASK-117).</para>
        /// </remarks>
        private bool FireMonsterShot(Combatant monster, MonsterTurnResolver.Decision decision,
            System.Func<int, int> rnd) {
            System.Func<int, int> roll = rnd ?? (n => UnityEngine.Random.Range(0, n));
            Combatant target = decision.Target;
            switch (CombatAi.SpeciesRoutineOf(monster.ClassId)) {
                case null: {
                    int kind = QuarrelInventory.SelectKind(monster.ClassId, QuarrelInventory.AllKinds,
                        k => QuarrelsCarried(monster, k));
                    if (kind == QuarrelInventory.NoKind) {
                        return false;
                    }
                    ResolveShot(monster, target, kind, rnd);
                    return true;
                }

                case CombatAi.SpeciesRoutine.MeleeAdjacentElseCastOrShoot:
                case CombatAi.SpeciesRoutine.ShootAtRangeElseDelegate:
                    ResolveShot(monster, target, RangedShotSound.ThrownRockKind, rnd);
                    return true;

                case CombatAi.SpeciesRoutine.RangedAttackTurn: {
                    MonsterTurnRoutines.RangedTurn shot = decision.Ranged;
                    if (shot.Choice == MonsterTurnRoutines.RangedChoice.HeavyShot) {
                        _playSfx?.Invoke(MonsterTurnRoutines.HeavyShotLaunchCue);
                        _playSfx?.Invoke(MonsterTurnRoutines.HeavyShotImpactCue);
                    }
                    StrikeFromRange(target, Between(shot.MinDamage, shot.MaxDamage, roll),
                        MonsterTurnRoutines.RangedTurnDamageFlags, roll);
                    return true;
                }

                case CombatAi.SpeciesRoutine.RangedKnockbackElseCloseIn:
                    _playSfx?.Invoke(MonsterMeleeTurn.LaunchCue);
                    StrikeFromRange(target,
                        Between(MonsterMeleeTurn.Damage.Min, MonsterMeleeTurn.Damage.Max, roll),
                        MonsterMeleeTurn.DamageFlags, roll);
                    _playSfx?.Invoke(MonsterMeleeTurn.LandingCue);
                    return true;

                case CombatAi.SpeciesRoutine.RandomRangedVariantBeyondTwoTiles: {
                    MonsterVariantAttackTurn.Variant variant = MonsterVariantAttackTurn.VariantFor(
                        roll(MonsterVariantAttackTurn.Variants.Length));
                    int healthMax = StatsFor(monster)?[(int)ActorAttribute.Health].Max ?? 0;
                    int healthPercent = healthMax > 0 ? monster.Health * 100 / healthMax : 0;
                    StrikeFromRange(target, MonsterVariantAttackTurn.ScaleByHealth(
                            Between(variant.DamageMin, variant.DamageMax, roll), healthPercent),
                        MonsterVariantAttackTurn.DamageFlags, roll);
                    return true;
                }

                case CombatAi.SpeciesRoutine.RangedPoisonAttack:
                    _playSfx?.Invoke(MonsterHeavyRangedTurn.ImpactCue);
                    StrikeFromRange(target,
                        Between(MonsterHeavyRangedTurn.Damage.Min, MonsterHeavyRangedTurn.Damage.Max, roll),
                        MonsterHeavyRangedTurn.DamageFlags, roll);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary><c>RNDR(lo, hi)</c> — a roll in <c>[lo, hi]</c>, both ends inclusive.</summary>
        private static int Between(int lo, int hi, System.Func<int, int> roll) => lo + roll(hi - lo + 1);

        /// <summary>
        /// A species attack's blow — <c>combat_arena_apply_damage(target, n, 0, knock, 0x200, 0)</c>.
        /// </summary>
        /// <remarks>
        /// <b>No to-hit roll and no armour, but a shield and negation still count.</b> The third
        /// argument is 0, which skips the defence term a crossbow shot pays; the sixth is 0, a direct
        /// attack, so an absorb shield takes it the way it takes a blow.
        /// </remarks>
        private void StrikeFromRange(Combatant target, int damage, int damageFlags,
            System.Func<int, int> roll) {
            if (target == null || target.IsDead) {
                return;
            }
            (bool weak, bool resists) = DamageAffinityOf(target, damageFlags);
            DamageOutcome outcome = CombatFormulas.ApplyDamage(
                damage, target.Stamina, target.Health,
                immune: false, applyArmor: false, armorRating: 0,
                absorbPool: AbsorbPoolOf(target), fromDirectAttack: true,
                negated: DamageNegatedFor(target),
                weakToDamageType: weak, resistsDamageType: resists, roll);
            target.Stamina = outcome.Stamina;
            target.Health = outcome.Health;
            CommitAbsorb(target, outcome.AbsorbPool);
            MarkHit(target, outcome.Landed, outcome.DamageDealt);

            if (target.Health <= 0) {
                KillAndSettle(target);
            }
            if (target.IsPartyMember) {
                WriteBack(target);
            }
        }

        /// <summary>
        /// Attack <paramref name="target"/> if the monster ended up in contact with it.
        /// </summary>
        /// <remarks>
        /// <b>The monster chooses, it does not always swing.</b> It rolls against its own swing
        /// accuracy and thrusts when the roll misses or the turn has no speed left, so a creature
        /// with a poor weapon makes the light attack most of the time.
        /// </remarks>
        private static bool IsFixedBlowSpecies(Combatant monster) {
            GameData.Resources.Combat.CombatAi.SpeciesRoutine? routine =
                GameData.Resources.Combat.CombatAi.SpeciesRoutineOf(monster.ClassId);
            return routine == GameData.Resources.Combat.CombatAi.SpeciesRoutine.MeleeAdjacentElseCastOrShoot
                || routine == GameData.Resources.Combat.CombatAi.SpeciesRoutine.WalkRandomTileThenAttackOrBrace;
        }

        /// <summary>
        /// The 0x13/0x31 blow: <c>combat_arena_melee_attack(actor, target, RNDR(0x19, 0x31))</c>, straight from the
        /// species routine (CBTAIACT.C:29, :62). The weapon still decides the hit and wears; a hit deals 25..49;
        /// the generic contact-parry roll (CMBTAI.C:381-388) is never taken (TASK-525).
        /// </summary>
        /// <remarks>
        /// <b>Always the swing, and so always billed.</b> The species routine calls the charged routine itself
        /// (COMBAT.C:463, which takes its point at :473 before rolling) and never reaches the generic AI's
        /// swing-or-thrust choice (<see cref="MonsterMeleeChoice"/>, CBENC.C:829). Picking there sent a speed-0
        /// Brak Nurr to the thrust, which bills nothing: on SAVE71 its stamina held at 88 while the original's
        /// fell a point per swing, 88 to 83 (TASK-537).
        /// </remarks>
        private void SwingSpeciesBlow(Combatant monster, Combatant target, System.Func<int, int> rnd) {
            System.Func<int, int> pickRoll = rnd ?? (n => UnityEngine.Random.Range(0, n));
            int blow = GameData.Resources.Combat.MonsterTurnRoutines.MeleeMinDamage + pickRoll(
                GameData.Resources.Combat.MonsterTurnRoutines.MeleeMaxDamage
                - GameData.Resources.Combat.MonsterTurnRoutines.MeleeMinDamage + 1);
            ResolveMelee(monster, target, CombatActionDispatch.MeleeAttack.Swing, rnd, fixedDamage: blow);
        }

        private void SwingIfAdjacent(Combatant monster, Combatant target, System.Func<int, int> rnd) {
            // *** ORTHOGONALLY ADJACENT, NOT MERELY ONE CELL AWAY. ***
            // combataipath_follow_tgt_check reaches its follow-up only when
            // combatgrid_actors_ortho_adj holds — Chebyshev 1 AND not a pure diagonal
            // (CMBTGRID.C:1368) — and combat_actor_step_to_tgt_adj only ever approaches through the
            // four orthogonal neighbours. The predicate was already here and already used for the
            // PLAYER's aim; only the monster path asked the looser question.
            //
            // *** THE DIAGONAL ARM IS PORTED UPSTREAM, not here. *** The original does not merely
            // decline on a diagonal: it clears the target, runs combataipath_select_target again, and
            // attacks only if the NEW target is ortho-adjacent (CMBTAI.C:408-426). That happens in
            // MonsterTurnResolver.WalkMeleeMoveRow -> Follow, in the same turn, before this runs;
            // compared live in both games on dir.G01/SAVE05 (TASK-461).
            //
            // *** AND THIS GATE CANNOT CURRENTLY FIRE FROM THE MONSTER PATH. *** Since TASK-438's
            // tail landed, ResolveEnemyTurn returns early unless the monster reached `dest`, and
            // ApproachCell only ever yields an ORTHOGONAL neighbour of the target — so anything that
            // gets here is already ortho-adjacent. It is kept because the rule is real and the two
            // call paths are not permanently welded together, but a reader should not take it for
            // load-bearing: the behaviour it describes is enforced upstream.
            if (target == null
                || !CombatGrid.OrthogonallyAdjacent(monster.X, monster.Y, target.X, target.Y)) {
                return;
            }
            System.Func<int, int> pickRoll = rnd ?? (n => UnityEngine.Random.Range(0, n));

            // *** TWO SPECIES SWING FOR A FIXED 25-49 AND NEVER PARRY. *** See SwingSpeciesBlow.
            if (IsFixedBlowSpecies(monster)) {
                SwingSpeciesBlow(monster, target, rnd);
                return;
            }

            // *** A QUARTER OF CONTACT TURNS ARE A GUARD, NOT A BLOW. ***
            // combataipath_followup_action rolls RND(100) and parries on 25 or less, attacking only
            // above it (CMBTAI.C:381-388). Swinging on every adjacent turn makes each melee enemy a
            // third again as dangerous as the game intends.
            if (GameData.Resources.Combat.CombatAi.ContactParries(pickRoll(100))) {
                monster.Flags |= CombatantFlags.Parry;
                monster.Flags &= ~CombatantFlags.Ready;
                return;
            }
            Equipped held = EquippedOfCategory(monster, MeleeWeaponCategory);
            ResolveMelee(monster, target, MonsterMeleeChoice.Pick(
                pickRoll(100),
                StatValue(StatsFor(monster), ActorAttribute.AccuracyMelee),
                held.Info?.SwingAccuracy_ArmorMod_BowAccuracy ?? 0,
                monster.Speed), rnd);
        }

        /// <summary>
        /// One turn of a rout: walk toward the flee destination and leave the field on reaching it
        /// — <c>combatenc_flee_walk_and_exit_field</c> (@0x64175).
        /// </summary>
        /// <remarks>
        /// <b>Fleeing is "walk toward the destination, and leave the battle on arrival" — not
        /// "stand still and be skipped".</b> Until this existed the resolver returned
        /// <see cref="AiAction.Flee"/> and <see cref="ResolveEnemyTurn"/> carried out only
        /// <see cref="AiAction.MeleeOrMove"/>, so a routed monster never moved, could never arrive,
        /// and therefore never left the fight — it just forfeited every remaining turn while still
        /// counting as a live enemy.
        ///
        /// <para><b>The destination is picked here rather than in the resolver, which is the one
        /// place this departs from the original's layout.</b> There the pick happens inside the
        /// morale check (<c>combatenc_morale_flee_check</c> @0x63f23) at decision time. The scan
        /// needs the grid and the resolver has none — it is documented as deciding only — so the
        /// pick lives with the movement that consumes it. Behaviour is unchanged because
        /// <see cref="Combatant.FleeDestination"/> is written once and reused: the monster still
        /// scans exactly once per rout, on the first turn it routs.</para>
        ///
        /// <para>The original tops the move budget up to a minimum of one step
        /// (<c>if (combat_moveStepsRemaining == 0) combat_moveStepsRemaining = 1;</c>), so even a
        /// speed-0 creature edges toward the exit rather than being trapped in the fight forever.</para>
        ///
        /// <para>Arrival is a real removal, not a death: <c>handleActorDeath(actor, 0)</c>, whose
        /// zero argument is the same one <see cref="CombatEncounter.Kill"/> takes as
        /// <c>playAnimation: false</c> — no corpse, no loot, and persisted as gone so a revisit
        /// does not find it standing. The damage path passes 1 instead, which is what makes that
        /// argument the corpse/no-corpse switch.</para>
        /// </remarks>
        private void RunFleeStep(Combatant monster, System.Func<int, int> rnd) {
            if (monster == null || Grid == null) {
                return;
            }

            // Chosen once per rout and then kept: the scan is deliberately noisy, so re-rolling it
            // every turn would make the monster wander instead of leave.
            if (monster.FleeDestination == null) {
                System.Func<int, int> roll = rnd ?? (_ => 0);
                monster.FleeDestination =
                    MonsterFleeDestination.Choose(FleeTileBlocked, () => roll(100));
            }

            if (monster.FleeDestination == null) {
                // The scan accepted nothing — see Combatant.FleeDestination for why this stands
                // still where the original would walk to a stale tile.
                _logger?.LogDebug("Routed monster found no flee destination; it holds its ground.");
                return;
            }

            (int X, int Y) destination = monster.FleeDestination.Value;
            int startX = monster.X;
            int startY = monster.Y;
            int steps = monster.Speed > 0 ? monster.Speed : 1;

            System.Func<int, int> hazardRoll = rnd ?? (n => UnityEngine.Random.Range(0, n));
            AnnounceShove(CombatWalk.Walk(Grid, monster, destination.X, destination.Y, steps,
                puzzle: Puzzle, occupiedByLiveCombatant: TileHoldsLiveCombatant,
                onHazard: (hurt, hazard) => ApplyWalkHazard(hurt, hazard, hazardRoll)));

            // Walk moves the combatant directly; put it back and re-apply through MoveTo so the
            // grid's occupancy follows it, exactly as the melee path does.
            int endX = monster.X;
            int endY = monster.Y;
            monster.X = startX;
            monster.Y = startY;
            MoveTo(monster, endX, endY);

            if (monster.IsDead) {
                // A hazard on the way killed it outright — that is an ordinary death, corpse and
                // all, and it never reaches the exit.
                return;
            }

            if (monster.X != destination.X || monster.Y != destination.Y) {
                return;
            }

            KillAndSettle(monster, playAnimation: false);
            _logger?.LogDebug(
                $"Routed monster reached ({destination.X},{destination.Y}) and left the field.");
        }

        /// <summary>
        /// One turn of a caster's disengage — <c>combataiturn_pick_tile_or_attack</c>
        /// (CBTAITRN.C:32), the routine every cascade turn function opens with.
        /// </summary>
        /// <remarks>
        /// <b>It evaluates every cell by standing in it.</b> For each unblocked, reachable tile the
        /// original temporarily writes the caster's position there, re-runs the nearest-living-enemy
        /// search, and restores the position; the cell that leaves the greatest distance wins. That
        /// is a "where am I safest" search rather than a step away from the threat, so the caster can
        /// end up anywhere its move budget reaches.
        ///
        /// <para><b>The sweep order is transcribed, not tidied.</b> The original counts x down from
        /// 7 and y down from 12, and <see cref="MonsterSpellcasting.RetreatCellIsBetter"/> breaks an
        /// exact tie in favour of the LATER candidate on a 51% roll — so the order decides which of
        /// two equally safe cells is preferred, and reversing it would quietly change the answer.</para>
        ///
        /// <para><b>Reachability is approximated by the move budget, which is the one liberty here.</b>
        /// The original probes with <c>combataipath_actor_walk_path(actor, 1)</c> — a dry run that
        /// walks the real path under <c>g_acting_actor_speed</c> and restores the position — so a
        /// cell behind a wall is rejected even when it is close. This uses Chebyshev distance against
        /// the same budget, which agrees everywhere the path is clear and is more permissive around
        /// an obstacle. Tightening it means running the pathfinder up to 104 times a turn; if a
        /// retreat is ever seen stepping into a pocket it cannot reach, that is the thing to fix.</para>
        ///
        /// <para><b>Giving up on the retreat is not standing still.</b> With nowhere better, or on a
        /// 15% roll that throws away a cell it did find, the original hands the turn to
        /// <c>combataipath_select_action</c> — an ACTION table that mostly ends in an attack, not a
        /// movement AI despite what <see cref="MonsterSpellcasting.DefersToMovementAi"/> is called.
        /// The caster is already in contact by definition, so the faithful outcome here is the swing
        /// it would have made.</para>
        /// </remarks>
        private void RunRetreatStep(Combatant monster, System.Func<int, int> rnd) {
            if (monster == null || Grid == null || Encounter == null) {
                return;
            }

            System.Func<int, int> roll = rnd ?? (n => UnityEngine.Random.Range(0, n));
            System.Collections.Generic.List<Combatant> opponents =
                Encounter.Party.Contains(monster) ? Encounter.Enemies : Encounter.Party;

            int budget = monster.Speed > 0 ? monster.Speed : 1;
            int bestDistance = CombatCapability.NearestOpponent(monster.X, monster.Y, opponents);
            int bestX = monster.X;
            int bestY = monster.Y;

            for (int x = CombatGrid.Width - 1; x >= 0; x--) {
                for (int y = CombatGrid.Height - 1; y >= 0; y--) {
                    if (Grid.IsBlocked(x, y) || TileHoldsLiveCombatant(x, y)
                        || CombatGrid.ChebyshevDistance(monster.X, monster.Y, x, y) > budget) {
                        continue;
                    }
                    int candidate = CombatCapability.NearestOpponent(x, y, opponents);
                    if (MonsterSpellcasting.RetreatCellIsBetter(candidate, bestDistance, roll(100))) {
                        bestDistance = candidate;
                        bestX = x;
                        bestY = y;
                    }
                }
            }

            bool foundSomewhereBetter = bestX != monster.X || bestY != monster.Y;
            if (MonsterSpellcasting.DefersToMovementAi(foundSomewhereBetter, roll(100),
                    mayAttack: true)) {
                SwingIfAdjacent(monster, NearestLiveOpponent(monster, opponents), rnd);
                return;
            }

            int startX = monster.X;
            int startY = monster.Y;
            System.Func<int, int> hazardRoll = rnd ?? (n => UnityEngine.Random.Range(0, n));
            AnnounceShove(CombatWalk.Walk(Grid, monster, bestX, bestY, budget,
                puzzle: Puzzle, occupiedByLiveCombatant: TileHoldsLiveCombatant,
                onHazard: (hurt, hazard) => ApplyWalkHazard(hurt, hazard, hazardRoll)));

            // Walk moves the combatant directly; put it back and re-apply through MoveTo so the
            // grid's occupancy follows it, exactly as the flee and melee paths do.
            int endX = monster.X;
            int endY = monster.Y;
            monster.X = startX;
            monster.Y = startY;
            MoveTo(monster, endX, endY);
            _logger?.LogDebug(
                $"Engaged caster disengaged to ({monster.X},{monster.Y}) instead of casting.");
        }

        /// <summary>The nearest living member of <paramref name="opponents"/>, or null.</summary>
        private static Combatant NearestLiveOpponent(Combatant monster,
            System.Collections.Generic.IEnumerable<Combatant> opponents) {
            Combatant best = null;
            int bestDistance = int.MaxValue;
            foreach (Combatant candidate in opponents) {
                if (candidate == null || candidate.IsDead) {
                    continue;
                }
                int distance =
                    CombatGrid.ChebyshevDistance(monster.X, monster.Y, candidate.X, candidate.Y);
                if (distance < bestDistance) {
                    bestDistance = distance;
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>
        /// Whether the flee scan may not choose this tile — <c>GridCellIsBlocked</c> (@0x2d7b7).
        /// </summary>
        /// <remarks>
        /// <b>This is a stricter test than <see cref="CombatGrid.IsBlocked"/> and the difference is
        /// deliberate.</b> That one answers "may I step onto this", which lets a walker onto a trap
        /// or crystal precisely so it fires. The original's destination scan calls a different
        /// predicate, which also refuses a tile carrying a trap element — a routed monster is not
        /// steered onto one as its chosen goal.
        /// </remarks>
        private bool FleeTileBlocked(int x, int y) =>
            Grid.IsBlocked(x, y)
            || Grid.TerrainAt(x, y) == CombatTerrain.Trap
            || TileHoldsLiveCombatant(x, y);

        /// <summary>
        /// Put the enemy side on the grid, starting from each one's saved tile.
        /// </summary>
        /// <remarks>
        /// <b>Saved positions are a wish, not an assignment.</b> Two actors can carry the same tile
        /// and terrain can block one, so each is resolved through
        /// <see cref="CombatPlacement.FindTile"/> and the tile is then marked taken — otherwise the
        /// second enemy of a pair lands on top of the first.
        ///
        /// <para><b>A trap puzzle brings its own grid AND its own party tiles, and both override
        /// P1.DAT.</b> TRAPS.DAT places up to three party members explicitly (markers -15/-16/-17)
        /// because a puzzle's entry points are part of its design — dropping the party on their
        /// ordinary battle tiles can start them the wrong side of a crystal run and make a solvable
        /// room unsolvable. Slots the file does not place keep their P1.DAT tile.</para>
        ///
        /// <para><b>Underground the arena is 8x7, not 8x13.</b> <c>combatgrid_load_and_init</c> walls
        /// off rows 7..12 whenever <c>g_game_mode == 2</c> — and does it BEFORE loading TRAPS.DAT, so
        /// it applies to a trap puzzle too and the puzzle's own cells then overwrite it. This asks
        /// <see cref="Underground"/> for the ordinary path; the puzzle path gets the same flag from
        /// <c>TrapPuzzleBuilder.Build</c>, which walls the rows off in the same order.</para>
        /// </remarks>
        private void PlaceCombatants(CombatEncounter encounter, TrapPuzzle puzzle) {
            // The puzzle's grid already carries its terrain and has its elements marked occupied, so
            // it must be adopted rather than rebuilt — a fresh grid would drop every crystal.
            Grid = puzzle?.Grid ?? new CombatGrid(Underground?.Invoke() ?? false);
            Puzzle = puzzle;

            // *** THE GROUND IS LAID OUT BEFORE ANYBODY STANDS ON IT. *** The original measures and
            // builds the arena and only then runs combat_arena_actor_turn_loop, which is where the
            // actors arrive (HOTSPOT.C:509 and :757 gate on combatgrid_tiles_over_thresh first).
            // Laying it out afterwards let the world-openness pass and the reachability prune wall
            // off cells that already had a combatant on them: measured above ground at zone 1
            // (684011, 822324), row 6 came out solid OutOfBounds wall to wall with both monsters
            // stranded on single cells at (2,7) and (6,7), so neither side could close and the
            // encounter could only be retreated from. Place() already consults Grid.IsBlocked, so
            // running the layout first is all it takes for everyone to land on open ground.
            LayOutGround?.Invoke(Grid);

            if (puzzle != null) {
                for (var slot = 0; slot < puzzle.PartyStarts.Length
                        && slot < encounter.Party.Count; slot++) {
                    (int X, int Y)? start = puzzle.PartyStarts[slot];
                    if (!start.HasValue) {
                        continue;
                    }
                    encounter.Party[slot].X = start.Value.X;
                    encounter.Party[slot].Y = start.Value.Y;
                }
            }

            // *** Party first. *** Whichever side is placed first keeps the tiles it asked for, and
            // the original builds the party pool from P1.DAT before the encounter's actors exist.
            foreach (Combatant member in encounter.Party) {
                Place(member);
            }
            foreach (Combatant enemy in encounter.Enemies) {
                Place(enemy);
            }
        }

        /// <summary>
        /// Settle one combatant onto a free tile, starting from the one it asked for.
        /// </summary>
        private void Place(Combatant combatant) {
            (int X, int Y)? tile = CombatPlacement.FindTile(combatant.X, combatant.Y,
                (x, y) => CombatPlacement.TileAccepts(
                    Grid.IsBlocked(x, y) || Grid.IsOccupied(x, y), occupiedBySelf: false));
            if (!tile.HasValue) {
                _logger?.LogWarning(
                    $"No free tile for a combatant; leaving it at ({combatant.X}, {combatant.Y}).");
                return;
            }
            combatant.X = tile.Value.X;
            combatant.Y = tile.Value.Y;
            Grid.SetOccupied(combatant.X, combatant.Y, true);
        }

        private Combatant PartyCombatant(byte characterIndex, int slot) {
            ActorStat[] stats = _session?.StatsOf(characterIndex);
            // charSlot is 1-based and equals the character's position + 1 — the same convention the
            // container owner numbering already uses (position is always one below the actor number).
            GameData.Resources.Data.SaveGameCombatData entry =
                _partyEntries?.EntryFor(characterIndex + 1);
            return new Combatant {
                X = entry?.XOnGrid ?? 0,
                Y = entry?.YOnGrid ?? 0,
                PartySlot = slot + 1,          // 1-based: slot 0 is reserved for "not a party member"
                ClassId = characterIndex,
                Health = StatValue(stats, ActorAttribute.Health),
                Stamina = StatValue(stats, ActorAttribute.Stamina),
                Speed = StatValue(stats, ActorAttribute.Speed),
                Flags = CombatantFlags.Ready,
                // The party walks the same rows when auto-resolve plays it; its entry carries them.
                AiPatterns = entry == null
                    ? ((int, int, int)?)null
                    : (entry.MeleeAttackType, entry.RangedAttackType, entry.MovementAiType),
            };
        }

        /// <summary>
        /// Whether a combatant is equipped to shoot — the equipment half of
        /// <c>combatenc_show_missile_stat_row</c> (CBENC.C:507): an intact crossbow and a quarrel it can fire.
        /// </summary>
        /// <remarks>The position half (not standing on terrain 1, nobody adjacent) is the resolver's.</remarks>
        public bool CanShoot(Combatant combatant) {
            if (combatant == null) {
                return false;
            }
            Equipped bow = EquippedOfCategory(combatant, (int)GameData.ObjectType.Crossbow);
            return bow.Info != null && bow.Condition > 0
                && QuarrelInventory.SelectKind(combatant.ClassId, QuarrelInventory.AllKinds,
                    k => QuarrelsCarried(combatant, k)) != QuarrelInventory.NoKind;
        }

        /// <summary>
        /// The stat's live value. <b><c>Base</c> is the current reading and <c>Max</c> the ceiling</b>
        /// — the convention the rest of the game already uses (InnService gates rest on
        /// <c>Base &lt; Max</c>). There is no <c>Current</c> member; assuming one is the obvious
        /// mistake here.
        /// </summary>
        private static int StatValue(ActorStat[] stats, ActorAttribute attribute) {
            int i = (int)attribute;
            return stats != null && i >= 0 && i < stats.Length && stats[i] != null ? stats[i].Base : 0;
        }

        /// <summary>
        /// A combat stat as the original reads it — <c>stat_actor_get(actor, stat, 0)</c>, scaled by
        /// the fight's CURRENT health.
        /// </summary>
        /// <remarks>
        /// *** A WOUNDED FIGHTER IS A WORSE FIGHTER, AND THAT WAS MISSING ENTIRELY. ***
        /// <c>g_abStatRatio</c> is <b>1 — fully weighted</b> for Speed, Strength, Defense and all
        /// three accuracies, and STAT.C applies it OUTSIDE the <c>charSlot != 0</c> guard, so it
        /// reaches monsters as well as party members. Measured in the running original with
        /// Locklear's health written from 55 down to 10 of 100: melee accuracy <b>58 -> 11</b>,
        /// crossbow 47 -> 9, strength 17 -> 4. <see cref="StatValue"/> returns the stored byte, so
        /// every one of those swings landed as if he were fresh (TASK-478).
        ///
        /// <para><b>The health it scales by is the COMBATANT's, not the record's.</b> The original's
        /// actor and its combat state are one object; ours keeps the pool on
        /// <see cref="Combatant.Health"/> and writes it back to the party record only when the fight
        /// ends, so reading <c>stats[Health]</c> here would scale by the pool the member had when
        /// they walked in.</para>
        ///
        /// <para><b>Modifiers and afflictions are party-only</b>, matching the
        /// <c>if (a-&gt;charSlot != 0)</c> the original wraps that half in — a monster has no
        /// modifier table and no condition ranks. <c>inCombat: true</c> is what lets a combat-only
        /// buff count, which is the flag's whole purpose.</para>
        /// </remarks>
        /// <summary>
        /// Re-read every combatant's Speed from its live pool, for the picker — see the call site.
        /// </summary>
        private void RefreshSpeeds() {
            if (Encounter == null) {
                return;
            }
            foreach (Combatant c in Encounter.AllCombatants()) {
                if (StatsFor(c) != null) {
                    c.Speed = CombatStat(c, ActorAttribute.Speed);
                }
            }
        }

        /// <summary>
        /// A landed blow that carries poison leaves the condition behind —
        /// <c>cbstat_apply_drain_tick</c>, which <c>combat_arena_apply_damage</c> calls whenever the
        /// damage mask has bit 1 and the blow did something.
        /// </summary>
        /// <remarks>
        /// <b>Nothing set <see cref="CombatantFlags.Poisoned"/> before this</b>, so
        /// <see cref="GameData.Resources.Combat.PoisonTick"/> — which is implemented and ticked —
        /// had nothing to act on, and a poisoned weapon was just a slightly harder one (TASK-480).
        ///
        /// <para>The rank goes to the party member's condition table as well as to the flag, and to
        /// a MONSTER only as the flag: <c>stat_combatant_apply_condition</c> writes
        /// <c>abActorStatusRanks</c> under <c>charSlot != 0</c>, so a creature carries the poison
        /// without carrying a rank.</para>
        /// </remarks>
        private void PoisonOnHitFrom(Combatant defender, int damageMask, int damage,
            System.Func<int, int> rnd) {
            GameData.Resources.Combat.CreatureAffinity row =
                Affinity?.AffinityOf(CreatureClassOf(defender));
            Equipped armour = EquippedOfCategory(defender, ArmorCategory);
            if (!GameData.Resources.Combat.PoisonOnHit.Applies(damage, damageMask,
                    row?.ResistanceFlags ?? 0, armour.Info != null ? armour.Flags : default)) {
                return;
            }

            defender.Flags |= CombatantFlags.Poisoned;
            if (!defender.IsPartyMember) {
                return;
            }
            ActorConditions conditions = _session?.ConditionsOf(defender.ClassId);
            if (conditions != null) {
                ConditionEngine.Apply(conditions, ActorCondition.Poisoned,
                    GameData.Resources.Combat.PoisonOnHit.Rank(
                        rnd ?? (n => UnityEngine.Random.Range(0, n))));
            }
        }

        private int CombatStat(Combatant who, ActorAttribute attribute) {
            ActorStat[] stats = StatsFor(who);
            int index = (int)attribute;
            if (who == null || stats == null || index < 0 || index >= stats.Length
                || stats[index] == null) {
                return 0;
            }
            int healthIndex = (int)ActorAttribute.Health;
            ActorStat recorded = healthIndex < stats.Length ? stats[healthIndex] : null;
            var liveHealth = new ActorStat {
                Base = (byte)System.Math.Max(0, System.Math.Min(255, who.Health)),
                Max = recorded?.Max ?? 0,
            };
            return StatEngine.Get(stats[index], attribute, liveHealth, StatReadMode.Effective,
                who.IsPartyMember
                    ? _session?.PartyEffectsFor(who.ClassId, attribute, inCombat: true)
                    : null);
        }

        /// <summary>
        /// Advance until a party member is up, resolving whatever comes before them, and return that
        /// combatant — or null when the encounter has ended.
        /// </summary>
        /// <remarks>
        /// <b>Monster turns resolve inside the picker loop</b>, so the caller never steps through
        /// them (spec §"Round and turn structure"). <paramref name="resolveEnemyTurn"/> is the seam
        /// TASK-97's AI plugs into; with none supplied an enemy simply forfeits, which keeps this
        /// type drivable before the AI exists.
        ///
        /// <para><b>The round boundary is the picker running dry</b> — there is no round counter.
        /// When nobody can act, the round is begun again, which re-arms everyone.</para>
        /// </remarks>
        public Combatant AdvanceToPartyTurn(System.Action<Combatant> resolveEnemyTurn = null,
            int maxTurns = 256) {
            UniTask<Combatant> run = AdvanceToPartyTurnAsync(
                monster => {
                    resolveEnemyTurn?.Invoke(monster);
                    return UniTask.CompletedTask;
                },
                maxTurns);

            // *** SAFE ONLY BECAUSE THE CALLBACK ABOVE COMPLETES SYNCHRONOUSLY. *** The loop awaits
            // nothing of its own, so with a synchronous per-monster callback the task is already
            // finished here and this neither blocks nor deadlocks. If that ever stops being true
            // this reads it deliberately rather than hanging: the status says so.
            if (run.Status != UniTaskStatus.Succeeded) {
                if (_logger != null) {
                    LoggerExtensions.LogError(_logger,
                        "CombatRuntime: the synchronous advance did not complete synchronously "
                        + "({Status}); use AdvanceToPartyTurnAsync.", run.Status);
                }
                return null;
            }
            return run.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Advance to the next party turn, giving each enemy in between a turn that can take TIME.
        /// </summary>
        /// <remarks>
        /// <b>The paced form, and the only implementation.</b> The original spends no time in the
        /// turn loop itself and all of it inside each action's animation
        /// (<see cref="ArenaPacing"/>), so the wait belongs to the per-monster callback rather than
        /// to this loop — which is why the callback is what became awaitable and the loop did not
        /// gain a delay of its own.
        ///
        /// <para>The synchronous <see cref="AdvanceToPartyTurn"/> delegates here, so there is one
        /// copy of the turn order, the round boundary, the poison tick and the speed re-read.</para>
        /// </remarks>
        public async UniTask<Combatant> AdvanceToPartyTurnAsync(
            System.Func<Combatant, UniTask> resolveEnemyTurn = null, int maxTurns = 256) {
            if (!InCombat) {
                return null;
            }

            // *** THE PARTY MEMBER WHO JUST ACTED TURNS TO THEIR TARGET. *** combat_arena_advance_turn
            // (COMBAT.C:1434) runs combatenc_actor_face_target on the outgoing actor before it picks
            // again. A member still Ready has not finished (a refused click), so is left alone.
            Combatant outgoing = Encounter.Current;
            if (outgoing != null && outgoing.IsPartyMember
                && (outgoing.Flags & CombatantFlags.Ready) == 0) {
                Encounter.FaceTarget(outgoing);
            }

            for (var guard = 0; guard < maxTurns; guard++) {
                if (Encounter.IsOver()) {
                    return null;
                }

                if (Encounter.RoundComplete()) {
                    // The floor burns once per round, before the round is handed out — so a field
                    // that lapses this round takes its last bite first. See TickTerrainEffects.
                    TickTerrainEffects();
                    TickSlayerRevivals();
                    Encounter.BeginRound(Grid);
                }

                // *** The OUTGOING actor's poison ticks as the picker runs. *** That is the
                // original's step 1 (combat_actor_pick_next): tick, then pick. Ticking the incoming
                // actor instead poisons whoever is about to act rather than whoever just did, which
                // shifts the damage by a turn and hides it from anyone who dies before acting again.
                TickPoison(Encounter.Current);

                // *** SPEED IS RE-READ AT PICK TIME, NOT SNAPSHOTTED AT ENTRY. ***
                // combatenc_pick_next (CBENC.C:726-738) compares `stat_actor_get(cand, 2, 0)` for
                // every candidate and then takes the winner's as the acting budget — the live,
                // health-scaled value, so a fighter wounded this round is slower the next. Ours kept
                // the speed they walked in with, because `Combatant.Speed` is a field the picker
                // reads and `CombatEncounter` has no way to ask the stat engine. Refreshing it here,
                // immediately before the pick, is the same moment the original reads it (TASK-478).
                RefreshSpeeds();

                Combatant next = Encounter.PickNext();
                if (next == null) {
                    return null;
                }

                if (next.IsPartyMember) {
                    // The opening round is over the moment the party is due to act; from here on
                    // enemies take their turns normally.
                    _partyHasTheDrop = false;
                    return next;
                }

                if (_partyHasTheDrop) {
                    // *** THE SURPRISE TAKES A TURN AWAY, IT DOES NOT GRANT ONE. ***
                    // runCombatEncounter's pre-round loop (@0x62b2d) clears the enemy's ready bit
                    // instead of running its AI, for every enemy due before the party's first turn.
                    // So this is exactly one forfeited action each, once — see
                    // CombatEncounterOpening.EnemiesForfeitTheirOpeningTurn.
                    Encounter.EndTurn();
                    continue;
                }

                if (resolveEnemyTurn != null) {
                    await resolveEnemyTurn(next);
                }
                // combatenc_ai_run_turn (CBENC.C:984): after acting, a monster faces its target or
                // nearest opponent -- unless it is fleeing. Not on the forfeit above, which the
                // original's pre-round loop never gives an AI turn at all.
                if ((next.Flags & CombatantFlags.Fleeing) == 0) {
                    Encounter.FaceTarget(next);
                }
                Encounter.EndTurn();
            }

            // A fight that cannot reach a party turn in this many picks is a bug in whatever is
            // resolving enemy turns, not a long fight — say so rather than spinning.
            if (_logger != null) {
                // Qualified: the project's own ConditionalLoggingExtensions.LogError has the same
                // shape as Microsoft's, so the unqualified call is ambiguous.
                LoggerExtensions.LogError(_logger,
                    "CombatRuntime: no party turn within {Max} picks; abandoning.", maxTurns);
            }
            return null;
        }

        /// <summary>
        /// End the encounter and write the party's state back to the session.
        /// </summary>
        /// <remarks>
        /// <b>Nobody dies in combat.</b> Teardown rewrites any party member left below one health to
        /// exactly <see cref="CombatModeEntry.DownedPartyMemberHealth"/> health and
        /// <see cref="CombatModeEntry.DownedPartyMemberStamina"/> stamina — a downed member walks out
        /// alive. Party death is not a combat outcome, so a port that lets a party member die here
        /// diverges on the first bad fight.
        /// </remarks>
        public void Leave() {
            if (!InCombat) {
                return;
            }

            var downed = 0;
            foreach (Combatant c in Encounter.Party) {
                if (!c.IsPartyMember) {
                    continue;
                }
                if (c.Health < CombatModeEntry.DownedPartyMemberHealth) {
                    c.Health = CombatModeEntry.DownedPartyMemberHealth;
                    c.Stamina = CombatModeEntry.DownedPartyMemberStamina;
                    c.Flags &= ~CombatantFlags.Dead;
                    downed++;
                }
                WriteBack(c);
            }

            _logger?.LogInformation("Combat left: {Downed} party member(s) walked out downed.", downed);
            Encounter = null;
            // The stat map is per-fight and holds the only reference to each dead enemy's block.
            _enemyStats.Clear();
            _enemySlots.Clear();
            _enemyRosterIndex.Clear();
            _encounterNumber = -1;
            _deathOutcomes.Clear();
        }

        /// <remarks>
        /// <b>The two sides reach different tables, and <see cref="Combatant.ClassId"/> cannot pick
        /// between them.</b> For a party combatant it is the party index; for an enemy it is the
        /// creature class, so writing an enemy back through it would stamp its health onto whichever
        /// party member shares that number. An enemy's slot is the one it entered with
        /// (<c>_enemySlots</c>), and <see cref="GameSession.RosterStatsOf"/> is the table that slot
        /// indexes — the same pair <see cref="EnterRoster"/> already reads to build the fight.
        ///
        /// <para>This used to return immediately for anything that was not a party member, so the
        /// damage done to an enemy went nowhere at all (TASK-230).</para>
        /// </remarks>
        /// <summary>
        /// Everything a death owes the rest of the game, in one place.
        /// </summary>
        /// <remarks>
        /// <b>Four call sites used to repeat three lines each</b> — melee, ranged, spell damage and
        /// the routed-monster exit — which is how the fourth obligation below came to be missing
        /// from all four rather than from one.
        ///
        /// <para>The obligations: clear the dead flag and free the tile
        /// (<see cref="CombatEncounter.Kill"/>), remember what the death left behind so the arena
        /// knows whether to draw a corpse, persist a removal into the encounter's save record, and
        /// — for a party member — put them into Near-death.</para>
        /// </remarks>
        /// <summary>Kills one combatant the way a landed blow does.</summary>
        /// <remarks>
        /// A seam rather than making <see cref="KillAndSettle"/> public: the production callers are
        /// the melee, ranged and spell resolvers, and driving a real swing to a guaranteed kill
        /// would test the damage roll rather than what a death settles.
        /// </remarks>
        internal DeathOutcome KillForTest(Combatant victim) => KillAndSettle(victim);

        private DeathOutcome KillAndSettle(Combatant victim, bool playAnimation = true) {
            DeathOutcome outcome = Encounter.Kill(victim, playAnimation: playAnimation, grid: Grid);
            LastDeath = outcome;
            _deathOutcomes[victim] = outcome;
            PersistRemoval(victim, outcome);
            MarkPartyMemberNearDeath(victim);
            return outcome;
        }

        /// <summary>
        /// A party member who dies in the arena goes to full Near-death, and the party-down byte is
        /// re-derived — <c>combat_arena_actor_die</c> (<c>COMBAT.C:278</c>), whose last act is
        /// <c>stat_combatant_apply_condition(actor, 6, 100)</c>.
        /// </summary>
        /// <remarks>
        /// <b>This is the bridge that lets a LOST FIGHT end the game.</b> Combat models a downed
        /// actor with <see cref="Combatant.Incapacitated"/>, which lives only for the length of the
        /// encounter; the party-down byte is derived from the SAVE's Near-death ranks. Without this
        /// write the two never met, so a party could be wiped out in the arena and walk away with a
        /// clean condition sheet — and <c>PartyDeathState</c> could only ever be set by falling
        /// into a pit.
        ///
        /// <para><b>The recompute belongs here, at the change</b>, not on a tick — see
        /// <see cref="GameSession.RecomputePartyDeathState"/> for why the timing matters.</para>
        ///
        /// <para><c>inCombat</c> suppresses the Near-death announcement, matching
        /// <c>g_wInCombatMode</c>: the arena is no place for a condition popup. Health and stamina
        /// are deliberately not passed — the death path has already zeroed them and
        /// <see cref="WriteBack"/> carries them to the save.</para>
        /// </remarks>
        private void MarkPartyMemberNearDeath(Combatant victim) {
            if (victim == null || !victim.IsPartyMember || _session == null) {
                return;
            }
            ActorConditions conditions = _session.ConditionsOf(victim.ClassId);
            if (conditions == null) {
                return;
            }
            ConditionEngine.Apply(conditions, ActorCondition.NearDeath, ActorConditions.MaxRank,
                inCombat: true);
            _session.RecomputePartyDeathState();
        }

        /// <summary>
        /// Re-reads the party's health and stamina from the session — the other half of the
        /// original's round trip through the combat inventory screen.
        /// </summary>
        /// <remarks>
        /// <b><see cref="WriteBack"/> only goes one way, and that is the bug this exists to close.</b>
        /// A fight's <see cref="Combatant"/> carries its own Health and Stamina, copied in when the
        /// encounter starts, and <c>WriteBack</c> flushes them OUT to the session when it ends.
        /// Nothing carried a change the other way — so a character who drank a healing potion on the
        /// combat inventory screen had the potion consumed (the container is shared) and the healing
        /// then overwritten by the fight's stale value on the way out.
        ///
        /// <para>The original does this by copying every party combatant into
        /// <c>g_gameState.characters</c> before opening the screen and copying them back after,
        /// restoring each saved <c>inner</c> pointer — see
        /// <see cref="CombatItemUse.PartyStateRoundTripsThroughTheScreen"/>. We have no snapshot to
        /// restore because the containers are already shared; only the stats need bringing back.</para>
        ///
        /// <para><b>Party members only.</b> An enemy has no screen to be edited on, and its stats
        /// live in the roster rather than the party, so re-reading them would undo the damage the
        /// fight has just done.</para>
        /// </remarks>
        public void RefreshPartyStatsFromSession() {
            if (Encounter == null || _session == null) {
                return;
            }
            foreach (Combatant c in Encounter.Party) {
                if (c == null || !c.IsPartyMember) {
                    continue;
                }
                ActorStat[] stats = _session.StatsOf(c.ClassId);
                if (stats == null) {
                    continue;
                }
                c.Health = StatValue(stats, ActorAttribute.Health);
                c.Stamina = StatValue(stats, ActorAttribute.Stamina);
            }
        }

        private void WriteBack(Combatant c) {
            ActorStat[] stats;
            if (c.IsPartyMember) {
                stats = _session?.StatsOf(c.ClassId);
            } else if (_enemySlots.TryGetValue(c, out int slot)) {
                stats = _session?.RosterStatsOf(slot);
            } else {
                // An enemy with no slot is one nothing placed — a summon or decoy. It has no saved
                // record to write over, and inventing one would field a creature the roster never
                // named.
                return;
            }
            if (stats == null) {
                return;
            }
            SetStat(stats, ActorAttribute.Health, c.Health);
            SetStat(stats, ActorAttribute.Stamina, c.Stamina);
        }

        private static void SetStat(ActorStat[] stats, ActorAttribute attribute, int value) {
            int i = (int)attribute;
            if (i < 0 || i >= stats.Length || stats[i] == null) {
                return;
            }
            // Base is a byte, and combat arithmetic is not: clamp rather than wrap. Wrapping would
            // turn a heavy hit into a full-health character, which is worse than the damage.
            int clamped = value < 0 ? 0 : (value > stats[i].Max ? stats[i].Max : value);
            stats[i].Base = (byte)clamped;
        }
    }
}
