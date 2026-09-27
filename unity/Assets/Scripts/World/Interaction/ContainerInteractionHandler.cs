namespace BakAgain.World.Interaction {
    using System.Linq;
    using BakAgain.Core;
    using BakAgain.UI;
    using BakAgain.UI.Inventory;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Character;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Scene;
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// The shared "interact with a world container" mechanism (the common shape of DOS
    /// handle_Corpse/handle_Well/handle_Container), driven entirely by
    /// <see cref="WorldEntity.Interaction"/>: resolve the container at the entity's location,
    /// show the profile-selected dialog, and on a primary click open the loot screen when the
    /// container should be looted. For a <c>HasLock</c> container (chest), the lock state is a
    /// branch (<see cref="Decide"/>): an Open chest shows the open ddx and loots; a
    /// A locked chest goes to the picklock, a puzzle chest to the cipher screen, and a trapped
    /// one through the disarm flow — all three silently, as the original does.
    /// (corpse/well) keep the existing profile-driven path unchanged.
    /// </summary>
    public sealed class ContainerInteractionHandler : IWorldInteractionHandler {
        private readonly GameSession _session;
        private readonly IDialogManager _dialog;
        private readonly InventoryMenu _inventoryMenu;
        private readonly BakAgain.UI.Puzzle.PuzzleService _puzzles;
        private readonly BakAgain.UI.Navigation.IScreenNavigator _navigator;
        private readonly BakAgain.Core.Services.IGameClock _clock;
        private readonly System.Func<int, int, UniTask> _explode;

        public ContainerInteractionHandler(GameSession session, IDialogManager dialog,
            InventoryMenu inventoryMenu, BakAgain.UI.Navigation.IScreenNavigator navigator,
            BakAgain.UI.Puzzle.PuzzleService puzzles, BakAgain.Core.Services.IGameClock clock,
            System.Func<int, int, UniTask> explode) {
            _explode = explode;
            _clock = clock;
            _session = session;
            _dialog = dialog;
            _inventoryMenu = inventoryMenu;
            _puzzles = puzzles;
            _navigator = navigator;
        }

        public string Behavior => "container";

        // Chest describe ddx (handle_Container @0x77284): examine per state + open-primary + not-actionable.
        private const int DdxNotActionable = 154;
        private const int DdxOpenPrimary   = 194;
        private const int DdxExamineOpen   = 195;
        private const int DdxChest         = 91;   // locked/trapped describe
        private const int DdxPuzzle        = 92;

        /// <summary>ddx 11 — what the cipher screen says on the way in (CIPHER.C:62).</summary>
        private const int PuzzleIntroDialog = 11;

        /// <summary>No dialog — the decision acts instead of speaking.</summary>
        public const int NoDialog = 0;

        public readonly struct ChestDecision {
            public readonly int DialogId;
            public readonly bool OpenLoot;

            /// <summary>Open the picklock screen on this chest's lock.</summary>
            public readonly bool OpensLock;
            public readonly bool OpensPuzzle;
            public readonly bool OpensTrap;

            public ChestDecision(int dialogId, bool openLoot, bool opensLock = false,
                bool opensPuzzle = false, bool opensTrap = false) {
                DialogId = dialogId;
                OpenLoot = openLoot;
                OpensLock = opensLock;
                OpensPuzzle = opensPuzzle;
                OpensTrap = opensTrap;
            }
        }

        /// <summary>Pure lock-aware decision for a HasLock (chest) container: which ddx to show and
        /// whether to open the loot screen. Faithful to handle_Container; lockpicking/traps/puzzle
        /// opening is deferred (Slices B/C/D), so only an Open chest loots. Public for unit testing.</summary>
        public static ChestDecision Decide(InteractionProfile profile, SaveGameContainerData container, bool isPrimary) {
            if (container == null || !profile.ActionableContainerTypes.Contains(container.ContainerType)) {
                return new ChestDecision(DdxNotActionable, false);
            }
            ChestLockState state = ChestLock.Resolve(container.LockData);
            if (!isPrimary) {
                return new ChestDecision(state == ChestLockState.Open ? DdxExamineOpen
                                       : state == ChestLockState.Puzzle ? DdxPuzzle : DdxChest, false);
            }
            if (state == ChestLockState.Open) {
                return new ChestDecision(DdxOpenPrimary, true);
            }
            if (state == ChestLockState.Locked) {
                // handle_Container's click switch (case 2 @0x77469) calls the picklock screen with
                // no prompt of its own, and the ddx ids around here are DESCRIBES for the examine
                // path rather than a confirm — so this decision carries no dialog.
                //
                // *** BUT THERE IS SOMETHING TO ASK, AND THIS COMMENT USED TO DENY IT. ***
                // picklock_screen_run opens with dialog_play_record(0x4f, 1) — "It appears to be
                // locked. Shall we try to open it?" — so the prompt exists, it just belongs to the
                // lock screen rather than to the handler. TryOpenLockAsync asks it. Observed in the
                // original at a zone-1 chest on 2026-09-07, where the port went straight in.
                return new ChestDecision(NoDialog, false, opensLock: true);
            }
            if (state == ChestLockState.Trapped) {
                // Its own branch, and NOT a describe: every trap path ends at a yes/no prompt, so
                // the chest can only ever be opened on a choice. Which prompt, and whether a disarm
                // is even offered, is decided in TryTrappedAsync where the spell and the party's
                // lockpicking are visible.
                return new ChestDecision(NoDialog, false, opensTrap: true);
            }
            if (state == ChestLockState.Puzzle) {
                // No dialog from the DECISION — handle_Container's case 1 (@0x77454) hands the
                // puzzle id to the cipher runner with no prompt of its own, and ddx 92 is the
                // EXAMINE text, which the branch above still uses. Showing 92 here would describe a
                // box the player is already looking into.
                //
                // The cipher screen does speak on the way in, but that is ddx 11 and it belongs to
                // the runner — see TryOpenPuzzleAsync. Same division as the picklock prompt.
                return new ChestDecision(NoDialog, false, opensPuzzle: true);
            }

            return new ChestDecision(DdxChest, false);
        }

        public async UniTask HandleAsync(WorldEntity entity, bool isPrimary) {
            InteractionProfile profile = entity.Interaction;
            if (profile == null) {
                return;
            }

            // *** EVERY WORLD-OBJECT CLICK MAKES THIS NOISE, AND THIS HANDLER IS SEVENTEEN OF THEM. ***
            // Swept all 27 DOS handle_* handlers for their audio_PlaySound argument: 24 of the 30
            // sites push sound_click (0x30), and the only exceptions are handle_Door's two
            // sound_dooropen and handle_Pit's sound_rope — both of which we already play. Six of our
            // eight behaviour keys played the click; "container" did not, and it is the key the
            // profile table routes Ashes, Bush, Corn, Pillar, DeadAnimal, RockPile, Crystals,
            // ScareCrow, SiegeEngine, WayMarker, StoneSlab, TreeStump, Well, Dirt, Corpse, Container
            // and Bag through — 174 of the 307 interactable entities in the shipped zone data. So
            // more than half of all world clicks were silent, in the single most-clicked thing there
            // is.
            //
            // It goes FIRST, as handle_Ashes @0x77050 does: before the dialog origin, before the
            // left/right branch, and independent of what is or is not at the location. The cue is
            // acknowledgement of the CLICK, not of any outcome — a right-click that only describes
            // still sounds, and so does a left-click that finds nothing.
            Audio.MenuSoundService.Instance?.Play(FixedObjectClick.ClickSound);
            (int bakX, int bakY) = BakCoordinateConverter.ToBakXY(entity.transform.position);
            int zone = _session.CurrentZone;
            // The LIVE container, not the save snapshot: a ground bag dropped this session exists
            // only in the runtime layer, and asking the snapshot would answer "nothing here" for a
            // pile standing in front of the party.
            RuntimeContainer container = _session.GetLiveContainerAt(zone, bakX, bakY);

            if (profile.HasLock) {
                // Lock state is authored data a runtime bag can never carry, so the chest branch
                // still reads it off the snapshot record — EXCEPT for a lock the party has already
                // picked this session, which only the runtime layer knows about. Without that
                // check a chest opened a moment ago offers its lock again.
                SaveGameContainerData snapshot = _session.GetContainerAt(zone, bakX, bakY);
                if (container?.Unlocked == true) {
                    await _dialog.ShowById(isPrimary ? DdxOpenPrimary : DdxExamineOpen);
                    if (isPrimary && _inventoryMenu != null) {
                        TryOpenLoot(zone, bakX, bakY, entity.EntityType, chestMessage: true);
                    }

                    return;
                }
                ChestDecision d = Decide(profile, snapshot, isPrimary);
                if (d.DialogId != NoDialog) {
                    await _dialog.ShowById(d.DialogId);
                }
                if (d.OpenLoot && _inventoryMenu != null) {
                    TryOpenLoot(zone, bakX, bakY, entity.EntityType, chestMessage: true);
                } else if (d.OpensLock && _inventoryMenu != null) {
                    await TryOpenLockAsync(snapshot, container, zone, bakX, bakY, entity.EntityType);
                } else if (d.OpensPuzzle) {
                    await TryOpenPuzzleAsync(snapshot, container, zone, bakX, bakY, entity.EntityType);
                } else if (d.OpensTrap) {
                    await TryTrappedAsync(snapshot, container, zone, bakX, bakY, entity.EntityType);
                }
                return;
            }

            // Non-lock container (corpse/well/bag/sign): existing profile-driven path.
            //
            // *** THE LIVE LAYER IS THE FIRST SOURCE, NOT THE ONLY ONE. *** A ground bag dropped
            // this session exists only at runtime, which is why the live lookup comes first — but
            // an AUTHORED fixed object exists only in the snapshot and has no runtime counterpart,
            // so asking the live layer alone answered "nothing here" for every one of them. That is
            // what made a way marker say "this must not be very important" instead of naming the
            // town: all five signs in zone 1 resolve in the snapshot and none in the live layer.
            //
            // The original searches both, in this order — actorspawn_objfixed reads TEMP.GAM first
            // and OBJFIXED.DAT second (ACTSPAWN.C:110-124), which is the same two passes.
            SaveGameContainerData authored = container == null
                ? _session.GetContainerAt(zone, bakX, bakY)
                : null;
            int dialogId = container != null
                ? InteractionDialogResolver.Resolve(
                    profile, container.ContainerType, container.DialogId, isPrimary)
                : InteractionDialogResolver.Resolve(profile, authored, isPrimary);
            await _dialog.ShowById(dialogId);
            if (isPrimary && profile.OpensLoot && _inventoryMenu != null) {
                TryOpenLoot(zone, bakX, bakY, entity.EntityType);
            }
        }

        /// <summary>
        /// A trapped chest — <c>handle_Container</c>'s case 0. See <see cref="ChestTrap"/>.
        /// </summary>
        /// <remarks>
        /// <b>Nothing here opens the chest on the player's behalf.</b> Detected or not, disarmed or
        /// not, every path ends at a yes/no, so the trap can only go off on a choice.
        /// </remarks>
        private async UniTask TryTrappedAsync(SaveGameContainerData snapshot,
            RuntimeContainer container, int zone, int bakX, int bakY,
            GameData.Resources.World.WorldEntityType entityType) {
            // A trap disarmed a moment ago is only known to the runtime layer, exactly like a lock
            // picked this session — without this the chest offers its trap again.
            int trapDamage = container?.TrapDisarmed == true ? 0 : snapshot?.LockData?.TrapDamage ?? 0;
            bool detected = ChestTrap.Detected(SarigActive(), trapDamage);

            if (detected && await _dialog.ShowConfirmById(ChestTrap.DetectedPromptDialog)) {
                int best = _session.PartyExtreme((GameData.ActorAttribute)ChestTrap.DisarmAttribute, out int member);
                if (ChestTrap.DisarmSucceeds(best, snapshot?.LockData?.Difficulty ?? 0)) {
                    await _dialog.ShowById(ChestTrap.DisarmedDialog);
                    // Permanent: the state lives on the container, so a session flag would re-arm
                    // the trap on the next visit.
                    if (container != null) {
                        container.TrapDisarmed = true;
                    }
                    AwardDisarm(member);
                    TryOpenLoot(zone, bakX, bakY, entityType, chestMessage: true);

                    return;
                }
                // A FAILED DISARM DOES NOT SPRING THE TRAP — it falls through to exactly the prompt
                // an undetected trap shows, so failing costs the attempt and nothing else.
                trapDamage = snapshot?.LockData?.TrapDamage ?? 0;   // still armed
            }

            if (!await _dialog.ShowConfirmById(ChestTrap.OpenPromptFor(trapDamage))) {
                return;
            }

            // *** SAYING YES TO A LIVE TRAP SPRINGS IT. *** This used to open the chest and nothing
            // else: trapDamage picked the PROMPT and was then dropped, so a trapped chest was an
            // untrapped one with an extra question (TASK-265).
            if (trapDamage > 0) {
                await SpringTrapAsync(container, trapDamage, bakX, bakY);
            }

            TryOpenLoot(zone, bakX, bakY, entityType, chestMessage: true);
        }

        /// <summary>
        /// The explosion — <c>handle_Container</c> @0x7752b.
        /// </summary>
        /// <remarks>
        /// <b>The damage hits the WHOLE PARTY</b> and lands on the combined health-and-stamina pool,
        /// not on Health and not on the character who opened the box.
        ///
        /// <para><b>And the amount is SHIFTED.</b> <see cref="ChestTrap.DamageDelta"/> is
        /// <c>-(trapDamage &lt;&lt; 8)</c> because the byte is whole points in a fixed-point amount;
        /// passing it raw deals a 256th of it. That is the one error here that looks like nothing
        /// on screen.</para>
        ///
        /// <para><b>The trap is then SPENT</b>, permanently, which is what lets the next visit ask
        /// the ex-trapped question instead of the live one.</para>
        ///
        /// <para>The explosion and its flash play between the cue and the line, as in the original
        /// (<c>WorldRuntime.PlayChestExplosionAsync</c>).</para>
        /// </remarks>
        private async UniTask SpringTrapAsync(RuntimeContainer container, int trapDamage, int bakX, int bakY) {
            Audio.MenuSoundService.Instance?.Play(ChestTrap.ExplosionCue);
            if (_explode != null) {
                await _explode(bakX, bakY);
            }
            await _dialog.ShowById(ChestTrap.DetonationDialog);

            long delta = ChestTrap.DamageDelta(trapDamage);
            foreach (byte member in _session.ActivePartyIndices ?? System.Array.Empty<byte>()) {
                ActorStat[] stats = _session.StatsOf(member);
                if (stats == null) {
                    continue;
                }
                ActorStat health = stats[(int)GameData.ActorAttribute.Health];
                ActorStat stamina = stats[(int)GameData.ActorAttribute.Stamina];
                if (health == null || stamina == null) {
                    continue;
                }
                StatEngine.ModifyHealthPool(health, stamina, delta,
                    ChestTrap.DamageHealTargetPercent, out bool drained,
                    conditions: _session.ConditionsOf(member));
                if (drained) {
                    _session.RecomputePartyDeathState();
                }
            }

            // Spent, and on the runtime container so it survives the visit -- the same place the
            // disarm flag lives.
            if (container != null) {
                container.TrapDisarmed = true;
            }
        }

        /// <summary>Whether the trap-detection spell is running.</summary>
        /// <remarks>
        /// <b>Scent of Sarig is the entire detection mechanic</b>, so this one query decides whether
        /// the player is warned at all. Spell timers live on the clock, keyed the way
        /// <c>FieldSpellCaster</c> schedules them — read the writer's key rather than assuming the
        /// condition vocabulary's numbering matches.
        /// </remarks>
        private bool SarigActive() =>
            _clock != null
            && _clock.HasTimer(GameData.Resources.Dialog.Actions.TimerType.Spell,
                GameData.Resources.Spells.FieldSpells.EventIdOf(ChestTrap.DetectionSpell));

        /// <summary>The disarming member's skill improves — only on success.</summary>
        private void AwardDisarm(int member) {
            if (member < 0) {
                return;
            }
            // Through ModifyStatOf so the sheet mark follows the change (TASK-611).
            _session.ModifyStatOf(member, (GameData.ActorAttribute)ChestTrap.DisarmAttribute,
                ChestTrap.DisarmSkillAward,
                GameData.Resources.Character.StatChangeMode.SkillUse,
                _session.StudyBonusFor(member, (GameData.ActorAttribute)ChestTrap.DisarmAttribute));
        }

        /// <summary>
        /// Open the picklock screen on a locked chest.
        /// </summary>
        /// <remarks>
        /// <b>The refusal is the screen's, not this handler's.</b> The original assembles the
        /// working set first and only says "you need keys or picklocks" (ddx 86) when it comes out
        /// empty — so a party with keys but no picks still gets the screen and simply cannot pick.
        /// Deciding here would have to duplicate that assembly to know the answer.
        /// </remarks>
        private async UniTask TryOpenLockAsync(SaveGameContainerData snapshot,
            RuntimeContainer container, int zone, int bakX, int bakY,
            GameData.Resources.World.WorldEntityType entityType) {
            int difficulty = snapshot?.LockData?.Difficulty ?? 0;
            // *** THE ORIGINAL ASKS FIRST. *** picklock_screen_run opens with
            // dialog_play_record(0x4f, 1) and only proceeds on yes, so the prompt belongs to every
            // lock, not to handle_Container. Before SetLock, because the original asks before it
            // assembles the working set.
            if (!await _inventoryMenu.AskToOpenLockAsync(
                    GameData.Resources.Character.LockPicking.LockContext.Container)) {
                return;
            }

            if (!_inventoryMenu.SetLock(difficulty)) {
                await _dialog.ShowById(PicklockWorkingSet.NothingToTryDialog);

                return;
            }

            await _navigator.Push(_inventoryMenu);

            // *** THE PUSH RETURNS WHEN THE SCREEN IS SHOWN, NOT WHEN IT CLOSES. *** Reading
            // LockOpened on the next line read it a frame after the screen appeared, so it was
            // always false and a chest the party picked open stayed locked. Wait for the screen's
            // own answer. A lock the party gave up on comes back false and leaves the chest as it
            // was.
            if (!await _inventoryMenu.LockOutcomeAsync() || container == null) {
                return;
            }

            container.Unlocked = true;
            TryOpenLoot(zone, bakX, bakY, entityType, chestMessage: true);
        }

        /// <summary>
        /// Open the cipher puzzle on a puzzle chest, and open the box if it is solved.
        /// </summary>
        /// <remarks>
        /// <b>The screen's answer IS the decision.</b> The original stores
        /// <c>UI_RunCipherPuzzle</c>'s return value in the very flag the other arms set to open the
        /// loot (0x77464), so solving the riddle opens the chest and giving up leaves it shut —
        /// there is no separate check afterwards.
        ///
        /// <para>The puzzle's table is not a file: it is the tail of DDX record
        /// <c>(puzzleId - 1) + 0x19f0a1</c>, which for the shipped chests is DIAL_Z17.</para>
        /// </remarks>
        private async UniTask TryOpenPuzzleAsync(SaveGameContainerData snapshot,
            RuntimeContainer container, int zone, int bakX, int bakY,
            GameData.Resources.World.WorldEntityType entityType) {
            int puzzleId = snapshot?.LockData?.PuzzleChest ?? 0;
            if (puzzleId <= 0 || _puzzles == null) {
                return;
            }

            // *** THE CIPHER SCREEN INTRODUCES ITSELF. *** cipher_dial_puzzle_run opens with
            // `dialog_play_record(0xb, 1)` (CIPHER.C:62) — ddx 11, "Rough in its construction and
            // banded with iron, the moredhel box would be impossible to open without solving its
            // wordlock…" — before the dial is drawn, and BEFORE the puzzle table is loaded. We went
            // straight to the dial. Observed in the original at zone-1 puzzle chest 35 and settled
            // by dismissing with a KEY, so no second click could have produced it.
            //
            // It belongs to the runner, not to this handler — the same shape as the picklock
            // prompt — but PuzzleService has no dialog layer, and this is the only caller. Move it
            // in if a second one ever appears.
            await _dialog.ShowById(PuzzleIntroDialog);

            // PuzzleService says which file or entry failed, so there is nothing to add here.
            CipherPuzzle puzzle = await _puzzles.LoadAsync(puzzleId);
            if (puzzle == null) {
                return;
            }

            bool solved = await _puzzles.RunAsync(puzzle);
            if (!solved || container == null) {
                return;
            }

            container.Unlocked = true;
            TryOpenLoot(zone, bakX, bakY, entityType, chestMessage: true);
        }

        /// <summary>
        /// Open the loot screen on whatever is here, <b>including an empty container</b>. The
        /// original never consults the item count: every world-container handler in WCURSOR.C
        /// (<c>wcursor_zone_loot_body</c>, <c>wcursor_zone_open_container</c>,
        /// <c>wcursor_open_container_at_cursor</c>, <c>wcursor_interact_fixedobj_container</c>)
        /// calls <c>cmbinv_inventory_screen_run</c> unconditionally once the actor record resolves.
        /// Seeing an empty chest open and be empty is the answer the player asked for; refusing to
        /// open it reads as a broken click.
        /// </summary>
        private void TryOpenLoot(int zone, int bakX, int bakY,
            GameData.Resources.World.WorldEntityType entityType, bool chestMessage = false) {
            RuntimeContainer runtime = _session.GetLiveContainerAt(zone, bakX, bakY);
            // A missing container is the analogue of the original's actor record failing to
            // resolve — there is nothing to show. An empty one is not that.
            if (runtime != null) {
                WriteOpenEvent(zone, bakX, bakY);
                // Robbed while the party was away? Decided now, as the original does (TASK-506).
                _session.ExposeStashOnOpen?.Invoke(runtime);
                _inventoryMenu.SetContainer(runtime, entityType);
                OpenLootAsync(zone, bakX, bakY, chestMessage).Forget();
            }
        }

        /// <summary>
        /// Show the loot screen; for a chest, say the chest's own message once it closes.
        /// </summary>
        /// <remarks>
        /// <b>A chest speaks AFTER it is looted, not before.</b> The chest click
        /// (canassa <c>INPUT/WCURSOR.C</c>, the <c>bHandled</c> arm) runs
        /// <c>cmbinv_inventory_screen_run</c> and only then keeps SUBREC_INTERACT_MSG as
        /// <c>deferred_msg</c>, which the cleanup plays (:702-706). Nothing here did, so every
        /// chest's message went unsaid -- and Moraeulf's VICTORY chest's ("This is the chest Moreaulf
        /// told us about, but the Waani isn't here!") is the one that sets 8110 and opens Squire
        /// Phillip's Waani topic, without which chapter 7 cannot be finished.
        /// </remarks>
        private async Cysharp.Threading.Tasks.UniTaskVoid OpenLootAsync(int zone, int bakX, int bakY,
            bool chestMessage) {
            long message = chestMessage
                ? FixedObjectAccess.InteractDialogId(_session.GetContainerAt(zone, bakX, bakY)) ?? 0
                : 0;
            if (message == 0) {
                await _navigator.Push(_inventoryMenu);
                return;
            }
            await _navigator.PushAndWaitAsync(_inventoryMenu);
            await _dialog.ShowById((int)message);
        }

        /// <summary>
        /// The story flag a container sets by being OPENED — <c>cmbinv_inventory_screen_run</c>
        /// (canassa <c>SRC/SCREENS/CMBINV.C</c>), which writes it as the screen comes up, before
        /// its loop begins:
        /// <code>
        /// pSub08 = actorrec_get_subrecord(actor, SUBREC_HOTSPOT);
        /// if (pSub08 != 0 &amp;&amp; pSub08->wGame_state_event_id != 0)
        ///     gstate_event_write(pSub08->wGame_state_event_id, 1);
        /// </code>
        /// </summary>
        /// <remarks>
        /// <b>*** IT IS GlobalDataKey2, AND THAT FIELD IS A WRITE, NOT A GATE. ***</b> The 9-byte
        /// <c>ActorSubrec08_HotspotAction</c> is <c>wPad_0, wGame_state_event_id, bWarp_kind,
        /// bWarp_dest, bHas_hotspot, bHotspot_x, bHotspot_y</c>, so the subrecord's second word is
        /// what the extractor calls <c>GlobalDataKey2</c>.
        /// <see cref="GameData.Resources.Data.SaveGameContainerEncounterData.GlobalDataKey2"/>
        /// documents itself by inheriting Key1's "the global whose value GATES this encounter",
        /// which is true of Key1 and wrong of Key2 — nothing reads Key2, the act of opening writes
        /// it.
        ///
        /// <para><b>Six shipped containers carry one and none of them fired.</b> Brother Jeremy's
        /// box (56012, zone 1) is the chapter-1 case: he will not hand over Thiful's Bird
        /// Migrations until it is set, so the quest could not be finished. The others are 56315
        /// twice in zone 9, 6860 and 30017 in zone 12, and 7993 in zone 3. TASK-560.</para>
        ///
        /// <para><b>Here rather than in the callers</b>, because this is the one funnel every open
        /// path runs through — plain, post-lock, post-puzzle and post-trap — which is exactly how
        /// the original has it: one write, in the one screen they all open.</para>
        /// </remarks>
        private void WriteOpenEvent(int zone, int bakX, int bakY) {
            int key = OpenEventKeyOf(_session.GetContainerAt(zone, bakX, bakY));
            if (key != 0) {
                _session.SetGlobalFlag(key, true);
            }
        }

        /// <summary>
        /// The story flag this container sets by being opened, or 0 when it sets none.
        /// </summary>
        /// <remarks>
        /// <b>The SECOND word of the encounter subrecord, not the first.</b> Key1's name in the
        /// original is <c>wPad_0</c> and it is read as a gate (the rift machine and the catapult
        /// do so); <c>wGame_state_event_id</c> — our Key2 — is the one <c>gstate_event_write</c>
        /// takes on open. Static so the mapping is pinned by a test; whether it actually fires on
        /// open is proven by opening a chest, not by a mock.
        /// </remarks>
        public static int OpenEventKeyOf(GameData.Resources.Data.SaveGameContainerData container) =>
            container?.EncounterData?.GlobalDataKey2 ?? 0;
    }
}
