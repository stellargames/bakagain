namespace BakAgain.Core.Services {
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Dialog;
    using GameData.Resources.Dialog.Actions;
    using GameData.Resources.Dialog.Branches;
    using GameData.Resources.Data;
    using GameData.Resources.GameState;
    using GameData.Resources.Inventory;
    using GameData.Money;
    using GameData.Resources.Spells;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Dialog game-logic, UI-free (the Core half of the former DialogManager god-class — see the
    /// 2026-07-12 screen-navigation architecture doc §3.3): resolve a DDX id to its leaf entry via
    /// the faithful branch walk (applying visited entries' global effects against
    /// <see cref="GameSession"/>), apply effects, and run the chapter-setup dialog
    /// (<c>go_to_chapter_impl → dialog_Show(2000023)</c>, KRONDOR.EXE 0x41f0a) purely for its side
    /// effects. The UI side (<see cref="IDialogManager"/>) receives resolved entries to render and
    /// never mutates game state.
    /// </summary>
    public sealed class DialogExecutor {
        // Chapter-setup dialog (DIAL_Z20 first entry). Chapter-independent id; it branches
        // internally on Var 7 (= global 30007, the chapter number).
        private const int ChapterSetupDialogId = 2000023;

        private readonly ILogger<DialogExecutor> _logger;
        private readonly GameSession _session;
        private readonly GameClock _clock;
        private BakAgain.UI.IDialogResourceLoader _resources;
        private GameData.Resources.Location.TeleportDestinationSet _teleportDestinations;
        private readonly GameData.Resources.Location.PendingTeleport _teleport;

        /// <param name="provider">
        /// Optional so a dialog driven from a test still runs — a dialog's behaviour does not depend
        /// on its audio. Music needs it; sound effects go through
        /// <see cref="BakAgain.Audio.MenuSoundService"/>'s singleton, as everything else in the port
        /// that plays one does.
        /// </param>
        public DialogExecutor(ILogger<DialogExecutor> logger, GameSession session, GameClock clock,
            BakAgain.ResourceManagement.IResourceProviderService provider = null,
            GameData.Resources.Location.PendingTeleport teleport = null) {
            _logger = logger;
            _session = session;
            _clock = clock;
            _provider = provider;
            _teleport = teleport;
            _resources = new DialogResourceLoader(logger);
        }

        /// <summary>
        /// Hand the executor a different loader — the seam a test uses to supply a dialog directly.
        /// </summary>
        /// <remarks>
        /// <b>A setter rather than a constructor parameter, and that is not a style choice.</b>
        /// VContainer picks the constructor with the MOST parameters and does not honour C# default
        /// values, so an optional <c>IDialogResourceLoader</c> made it demand a registration that
        /// does not exist: the whole graph failed with <c>No such registration of type:
        /// BakAgain.UI.IDialogResourceLoader</c> and the game would not boot. The PlayMode suite
        /// stayed green through it — nothing there builds the container — and it took a play-verify
        /// to see. 2026-09-13.
        /// </remarks>
        public void UseResourceLoader(BakAgain.UI.IDialogResourceLoader resources) {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        private readonly BakAgain.ResourceManagement.IResourceProviderService _provider;

        /// <summary>
        /// Plays the entry's audio for one of <c>ExecuteDialog</c>'s three passes.
        /// </summary>
        /// <remarks>
        /// <b>779 of these ship and none of them made a sound until 2026-09-03.</b> The action
        /// parsed, <see cref="PlayAudioTiming"/> was modelled to say WHICH pass fires it, and
        /// nothing ever fired one — the largest single gap the dialog-coverage count found
        /// (TASK-313). <c>DialogManager</c> plays the UI click on a choice button and that was the
        /// whole of a conversation's sound.
        /// </remarks>
        private void PlayEntryAudio(DialogEntry entry, PlayAudioTiming timing) {
            foreach (DialogActionBase action in entry.Actions) {
                if (action is PlayAudioAction audio && audio.Timing == timing) {
                    PlayDialogAudio(audio.AudioId);
                }
            }
        }

        /// <summary>
        /// One dialog cue: nothing, a sound effect, or a track.
        /// </summary>
        /// <remarks>
        /// The id alone decides — see <see cref="DialogAudio"/>, including why <b>id 0 is silence
        /// on the build we target</b> and why that matters for 157 of the shipped instances.
        /// </remarks>
        private void PlayDialogAudio(int audioId) {
            switch (DialogAudio.KindOf(audioId)) {
                case DialogAudio.Kind.Sound:
                    BakAgain.Audio.MenuSoundService.Instance?.Play(audioId);
                    break;
                case DialogAudio.Kind.Song:
                    if (_provider != null && BakAgain.Audio.MidiPlaybackManager.Instance != null) {
                        BakAgain.Audio.MidiPlaybackManager.Instance
                            .PlayTrackAsync(audioId, _provider, owner: this).Forget();
                    }
                    break;
            }
        }

        /// <summary>
        /// Load the DDX for <paramref name="id"/>, find its entry, and walk conditional/default
        /// branches to the first leaf with displayable text (faithful port of ExecuteDialog's
        /// traversal, KRONDOR.EXE 0x494bb). Visited entries' global-flag effects are applied as the
        /// walk proceeds — e.g. the corpse-flavor entry sets flag 8127, so a second resolve walks
        /// the flag-set branch. Returns null (with logging) when the dialog/entry is missing.
        /// </summary>
        public async UniTask<DialogEntry> ResolveLeafEntryAsync(int id) =>
            (await ResolvePlayAsync(id))?.Entry;

        /// <summary>
        /// Every dialog this executor plays is loaded through here, so the teleport table is always
        /// there by the time a Teleport action runs.
        /// </summary>
        /// <remarks>
        /// ApplyTeleport is synchronous and reads the table, and it used to be loaded only by
        /// <see cref="ResolvePlayAsync"/>. Chapter setup loads its dialog directly, so on a session
        /// whose first dialog was the chapter-setup one — any chapter change before the party has
        /// spoken to anyone — chapter 2's scene teleport (destination 30) was dropped with "the
        /// table is unloaded". The table is forty rows and cached for the loader's lifetime.
        /// </remarks>
        private async UniTask<Dialog> LoadDialogAsync(int id) {
            _teleportDestinations ??= await _resources.GetTeleportDestinationsAsync();
            return await _resources.LoadDialogAsync(id);
        }

        /// <summary>
        /// Resolve a dialog as the engine plays it: seed the six text-variable slots, walk to the
        /// leaf, and let <b>every entry the walk touches</b> write its own slots on the way past.
        ///
        /// <para>That last part is not a detail. 57 shipped entries are text-less routers whose only
        /// job is to fill a text variable and branch to the leaf that reads it — resolving to the
        /// leaf alone leaves those tokens showing the seeded default instead of what the dialog
        /// meant (<c>dialog_play_record</c>'s op loop, DIALOG.C:855-870).</para>
        ///
        /// <para>The slots come back with the entry because they are the state of THIS play; a
        /// second play of the same dialog re-seeds and may name different companions.</para>
        /// </summary>
        public async UniTask<DialogPlay> ResolvePlayAsync(int id) {
            Dialog dialog = await LoadDialogAsync(id);
            if (dialog == null) {
                _logger.LogError("Failed to load Dialog with id {DialogId}", id);
                return null;
            }
            // Entry IDs in DDX files are full ids (e.g. 1_600_003 in DIAL_Z16); match the full id
            // so all 32 DDX files resolve consistently.
            DialogEntry entry = dialog.Entries.FirstOrDefault(e => e.Id == (uint)id);
            if (entry == null) {
                _logger.LogError("Failed to find DialogEntry with id {DialogEntryId}", id);
                return null;
            }
            DialogSlotContext context = DialogSlotContextFactory.FromSession(_session);
            DialogSlotTable slots = DialogSlotPopulator.CreateForPlay(context);
            long elapsedTicks = 0;
            bool autoAdvanced = false;
            void Visit(DialogEntry visited) {
                DialogSlotPopulator.ApplyEntryActions(slots, visited, context);
                ApplyEntryTimers(visited);
                ApplyEntryGameplayActions(visited, slots);
                elapsedTicks += AccumulatedTicks(visited);
                autoAdvanced |= SkipsTimeUnacknowledged(visited);
            }

            var pushed = new System.Collections.Generic.Stack<string>();
            DialogEntry leaf = DialogBranchWalker.WalkToLeaf(dialog, entry, _session.GetGlobalValue,
                ApplyEffect, Visit, RandomBranchRoll, pushed);

            (dialog, leaf) = await WalkFollowingIdTargets(dialog, leaf, Visit, id, pushed);

            SpendDialogTime(elapsedTicks, autoAdvanced);
            return new DialogPlay(leaf, slots, context, dialog, pushed);
        }


        /// <summary>
        /// Continue a walk across branches that name their target by dialog ID rather than by
        /// offset, loading each named dialog in turn.
        /// </summary>
        /// <remarks>
        ///  *** A walk that stops with no text has usually not finished — it has hit the OTHER
        ///  addressing mode. *** The walker resolves branch targets as offsets within one DDX, so
        ///  a branch that names its target by dialog id lands nowhere and reads as the end of the
        ///  conversation. The original picks between the two modes on bit 31 of the key and, for
        ///  an id, re-derives the file from it before loading (dialog_load_record_by_key). That is
        ///  how every NPC reaches the shared ask-about pages in DIAL_Z20 — Squire Phillip's
        ///  conversation ended exactly where his topic list belongs because of this.
        /// </remarks>
        private async UniTask<(Dialog Dialog, DialogEntry Leaf)> WalkFollowingIdTargets(
            Dialog dialog, DialogEntry leaf, Action<DialogEntry> visit, int fromId,
            System.Collections.Generic.Stack<string> pushed = null) {
            for (int hop = 0; hop < DialogBranchWalker.MaxIdAddressedHops; hop++) {
                if (!string.IsNullOrEmpty(leaf?.Text)) {
                    break;
                }
                int? target = DialogBranchWalker.IdAddressedTargetOf(leaf, _session.GetGlobalValue,
                    RandomBranchRoll);
                if (target == null) {
                    break;
                }
                // This record has a next one, so what it pushes is stacked (DIALOG.C:1441).
                DialogBranchWalker.PushTargets(leaf, pushed);
                Dialog crossed = await LoadDialogAsync(target.Value);
                DialogEntry crossedEntry =
                    crossed?.Entries.FirstOrDefault(e => e.Id == (uint)target.Value);
                if (crossedEntry == null) {
                    _logger.LogWarning(
                        "Dialog {DialogId} branches to dialog id {TargetId}, which did not resolve",
                        fromId, target.Value);
                    break;
                }
                dialog = crossed;
                leaf = DialogBranchWalker.WalkToLeaf(crossed, crossedEntry, _session.GetGlobalValue,
                    ApplyEffect, visit, RandomBranchRoll, pushed);
            }
            return (dialog, leaf);
        }

        /// <summary>
        /// The answer to a topic the player picked from an ask-about menu, or null when the branch
        /// leads nowhere.
        /// </summary>
        /// <remarks>
        /// <b>A menu page's branches are not continuations, so nothing else reaches them.</b>
        /// <c>NextLine</c> asks "having said that, is there more", and picks the same
        /// default/conditional branch the routing walk would — a page has neither kind, only
        /// <c>KeywordChoiceBranch</c>es, one per topic. The player's choice is what selects among
        /// them, which is why following it is a separate question from continuing a conversation.
        ///
        /// <para>The chosen topic's target is addressed the same two ways any branch is, so the
        /// answer may live in another DDX and the walk continues through
        /// <see cref="WalkFollowingIdTargets"/> exactly as a resolve does.</para>
        ///
        /// <para>The slots and context carry over rather than being re-seeded: the answer is part of
        /// the same conversation, and re-seeding here would re-roll the random companion names
        /// mid-exchange.</para>
        /// </remarks>
        public async UniTask<DialogPlay> FollowChoiceAsync(DialogPlay play, int branchIndex) {
            DialogEntry entry = play?.Entry;
            if (entry?.Branches == null || branchIndex < 0 || branchIndex >= entry.Branches.Count) {
                return null;
            }
            DialogBranchBase branch = entry.Branches[branchIndex];
            Dialog dialog = play.Dialog;
            DialogEntry target = null;
            if (branch.TargetOffset is int offset) {
                target = dialog?.Entries.FirstOrDefault(e => e.Offset == offset);
            } else if (branch.TargetId is int id) {
                dialog = await LoadDialogAsync(id);
                target = dialog?.Entries.FirstOrDefault(e => e.Id == (uint)id);
            }
            if (target == null) {
                return null;
            }

            // A pick is a next record, so the menu's own push is stacked: every topic page pushes
            // itself, which is what brings it back once the answer runs out.
            DialogBranchWalker.PushTargets(entry, play.Pushed);
            return await WalkFromAsync(play, dialog, target, branchIndex);
        }

        /// <summary>
        /// The record an earlier entry pushed, played when a tree ends — <c>dialog_play_record</c>'s
        /// pop when <c>record_key == 0 &amp;&amp; stackDepth != 0</c> (DIALOG.C:1466). Null when nothing waits.
        /// </summary>
        /// <remarks>
        /// 113 NPC routers push a farewell line before handing off to a topic menu, and every topic menu
        /// pushes itself when a topic is picked — so GoodBye (or Escape) plays the farewell and a
        /// finished answer brings the menu back (TASK-543).
        /// <para><paramref name="fallbackId"/> is the empty-topic fallback: the popped record is
        /// thrown away and that one plays instead (DIALOG.C:1472-1474) — see
        /// <c>DialogManager.RunChainAsync</c> for when it applies (TASK-800).</para>
        /// </remarks>
        public async UniTask<DialogPlay> ResumePushedAsync(DialogPlay play, int? fallbackId = null) {
            bool fallback = fallbackId.HasValue;
            while (play?.Pushed != null && play.Pushed.Count > 0) {
                string key = play.Pushed.Pop();
                if (fallback) {
                    key = "base:dialog:" + fallbackId.Value;
                    fallback = false;
                }
                (Dialog dialog, DialogEntry entry) = await EntryForKeyAsync(key);
                if (entry != null) {
                    return await WalkFromAsync(play, dialog, entry, 0);
                }
                _logger.LogWarning("Pushed dialog record {Key} did not resolve", key);
            }
            return null;
        }

        // base:dialog:<id> loads by id; base:ddx:dial_zNN:<offset> loads file NN (any id in its range).
        private async UniTask<(Dialog Dialog, DialogEntry Entry)> EntryForKeyAsync(string key) {
            int split = key?.LastIndexOf(':') ?? -1;
            if (split < 2 || !int.TryParse(key.Substring(split + 1), out int number)) {
                return (null, null);
            }
            if (key.StartsWith("base:dialog:")) {
                Dialog byId = await LoadDialogAsync(number);
                return (byId, byId?.Entries.FirstOrDefault(e => e.Id == (uint)number));
            }
            if (!int.TryParse(key.Substring(split - 2, 2), out int file)) {
                return (null, null);
            }
            Dialog byFile = await LoadDialogAsync(
                file * GameData.Resources.Animation.CutsceneDialogCommand.IdsPerFile);
            return (byFile, byFile?.Entries.FirstOrDefault(e => e.Key == key));
        }

        private async UniTask<DialogPlay> WalkFromAsync(DialogPlay play, Dialog dialog,
            DialogEntry target, int fromId) {
            long elapsedTicks = 0;
            bool autoAdvanced = false;
            void Visit(DialogEntry visited) {
                DialogSlotPopulator.ApplyEntryActions(play.Slots, visited, play.Context);
                ApplyEntryTimers(visited);
                ApplyEntryGameplayActions(visited, play.Slots);
                elapsedTicks += AccumulatedTicks(visited);
                autoAdvanced |= SkipsTimeUnacknowledged(visited);
            }

            DialogEntry leaf = DialogBranchWalker.WalkToLeaf(dialog, target, _session.GetGlobalValue,
                ApplyEffect, Visit, RandomBranchRoll, play.Pushed);
            (dialog, leaf) = await WalkFollowingIdTargets(dialog, leaf, Visit, fromId, play.Pushed);
            SpendDialogTime(elapsedTicks, autoAdvanced);
            return new DialogPlay(leaf, play.Slots, play.Context, dialog, play.Pushed);
        }

        /// <summary>
        /// The next line of a conversation, or null when the one just shown ends it.
        /// </summary>
        /// <remarks>
        /// <b>Most spoken dialog in the game is a chain, not a single line.</b> 3464 of 5932
        /// text-bearing entries across the shipped DDX carry a branch onward, and resolving a
        /// dialog stops at the FIRST of them because for conditional routing the first text is the
        /// answer. Continuing is the separate question, asked once a line has been read.
        ///
        /// <para>The entry's own gameplay actions and slot writes are applied here, as it is
        /// passed — the same treatment every entry the resolve walk touches gets, so a line that
        /// sets a text variable has done so before the line that reads it is rendered.</para>
        /// </remarks>
        /// <summary>
        /// The roll a <c>TakeRandomBranch</c> entry picks with — the original's
        /// <c>GetRandomNumber() &amp; 0xFFF</c>.
        /// </summary>
        /// <remarks>
        /// The raw 12-bit window, not a pre-narrowed "one of n": the walker takes the remainder
        /// itself, and 4096 does not divide evenly by most branch counts. Narrowing here would
        /// quietly replace the shipped distribution with a uniform one.
        /// </remarks>
        private static int RandomBranchRoll() =>
            UnityEngine.Random.Range(0, DialogBranchWalker.RandomBranchRollWindow);

        /// <summary>
        /// The next line, crossing into another dialog when the branch is addressed by id.
        /// </summary>
        /// <remarks>
        /// <b>A conversation can hand off to a shared page mid-chain, and only the LOAD used to
        /// follow that.</b> <see cref="WalkFollowingIdTargets"/> ran from the initial resolve and
        /// from following a topic choice, but never from continuing a line — so a chain that ended
        /// on a text-less entry whose branch is an id target simply stopped there, leaving that
        /// empty entry on screen as a blank panel.
        ///
        /// <para>Squire Phillip is the shipped example. His conversation walks nine lines in
        /// DIAL_Z30 and ends at offset 123064, which has no text and one branch: no
        /// <c>TargetOffset</c> at all, and <c>TargetKey base:dialog:2000001</c> — the shared
        /// ask-about page in DIAL_Z20 that offers Nearest Town and Inns. The offset walk has
        /// nothing to follow, returns null, and the loop ends on the empty entry.</para>
        ///
        /// <para>The hop only fires for an entry with no text, which is the same guard the resolve
        /// walk uses: a line that has something to say is the answer, not a redirection.</para>
        /// </remarks>
        public async UniTask<(DialogPlay Play, DialogEntry Entry)> NextLineAsync(DialogPlay play) {
            DialogEntry next = NextLine(play);
            if (next == null) {
                // *** A LINE CAN NAME ITS OWN NEXT RECORD BY ID. *** NextLine indexes one file by
                // key, so a text line whose branch is base:dialog:<id> read as the end of the
                // conversation. A grave's epitaph does exactly this: DIAL_Z01 100035 "Baby Irisa…"
                // continues to DIAL_Z00 196, "Shall we dig up this grave?", and the port dug without
                // ever asking. The original follows the key whatever file it names. TASK-535.
                int? target = play?.Entry == null || string.IsNullOrEmpty(play.Entry.Text)
                    ? null
                    : DialogBranchWalker.IdAddressedTargetOf(play.Entry, _session.GetGlobalValue,
                        RandomBranchRoll);
                if (target == null) {
                    return (play, null);
                }
                (Dialog crossedDialog, DialogEntry crossedEntry) =
                    await EntryForKeyAsync("base:dialog:" + target.Value);
                if (crossedEntry == null) {
                    _logger.LogWarning("Dialog line {Key} continues to dialog id {TargetId}, which did not resolve",
                        play.Entry.Key, target.Value);
                    return (play, null);
                }
                // This record has a next one, so what it pushes is stacked (DIALOG.C:1441).
                DialogBranchWalker.PushTargets(play.Entry, play.Pushed);
                DialogPlay crossed = await WalkFromAsync(play, crossedDialog, crossedEntry, target.Value);
                return (crossed, crossed.Entry);
            }
            if (play?.Dialog == null || !string.IsNullOrEmpty(next.Text)) {
                return (play, next);
            }

            // *** A CONTINUATION CAN LAND ON A ROUTER, AND A ROUTER IS NOT A LINE. *** The initial
            // resolve walks a text-less entry to its leaf (WalkToLeaf) and only THEN follows an
            // id-addressed target; continuing a line did the second half without the first. So a
            // chain whose next record routes by OFFSET stopped on an entry that draws nothing and,
            // having no text, could not continue either — NextLine's first guard rejects it.
            // The Garrison's arrival narration (DIAL_Z19 2002) defaults to 3689, exactly such a
            // router ("do they carry the ruby?"), so Captain Belford never asked for it and the
            // chapter-1 quest could not be handed in. TASK-559.
            DialogEntry routed = DialogBranchWalker.WalkToLeaf(play.Dialog, next,
                _session.GetGlobalValue, ApplyEffect, VisitContinued, RandomBranchRoll, play.Pushed);

            (Dialog dialog, DialogEntry leaf) = await WalkFollowingIdTargets(
                play.Dialog, routed, _ => { }, 0, play.Pushed);
            if (ReferenceEquals(leaf, next)) {
                return (play, next);
            }

            // NextLine has already run the continuation's OWN actions; the records the route
            // crosses on the way to the leaf have not been run. Re-visiting `next` here would
            // repeat them, and they are not all idempotent — 4490 carries GiveItem/RemoveItem.
            void VisitContinued(DialogEntry visited) {
                if (ReferenceEquals(visited, next)) {
                    return;
                }
                DialogSlotPopulator.ApplyEntryActions(play.Slots, visited, play.Context);
                ApplyEntryTimers(visited);
                ApplyEntryGameplayActions(visited, play.Slots);
            }
            return (new DialogPlay(leaf, play.Slots, play.Context, dialog, play.Pushed), leaf);
        }

        public DialogEntry NextLine(DialogPlay play) {
            if (play?.Dialog == null || play.Entry == null) {
                return null;
            }

            DialogEntry next = DialogBranchWalker.NextLine(
                play.Dialog, play.Entry, _session.GetGlobalValue, RandomBranchRoll);
            if (next == null) {
                return null;
            }
            DialogBranchWalker.PushTargets(play.Entry, play.Pushed);

            // Effects ride on the entry's actions as GlobalEffectAction, exactly as the resolve
            // walk reads them — see DialogBranchWalker.ApplyEffects.
            foreach (DialogActionBase action in next.Actions) {
                if (action is GlobalEffectAction g && g.Effect != null) {
                    ApplyEffect(g.Effect);
                }
            }

            DialogSlotPopulator.ApplyEntryActions(play.Slots, next, play.Context);
            ApplyEntryTimers(next);
            ApplyEntryGameplayActions(next, play.Slots);

            return next;
        }

        /// <summary>
        /// Apply a dialog effect to live game state as ExecuteDialog would. Handles SetFlagEffect
        /// (including its <c>ForTicks</c> auto-clear timer) and SetFlagsEffect (masked multi-flag
        /// write). Other kinds are ignored until a consumer needs them (work-todo #10).
        /// </summary>
        public void ApplyEffect(Effect effect) {
            switch (effect) {
                case SetFlagEffect flag:
                    _session.SetGlobalFlag(flag.Flag, flag.Set);
                    ScheduleTemporaryFlagClear(flag);
                    break;
                case SetFlagsEffect flags:
                    foreach (FlagState f in flags.Flags) {
                        _session.SetGlobalFlag(f.Flag, f.Set);
                    }
                    break;
                case SetVarEffect v:
                    _session.SetGlobalValue(v.GlobalKey, v.Value);
                    break;
                case RawGlobalWriteEffect raw:
                    // An absolute key the decoder could not name. These used to arrive as
                    // SetVarEffect carrying a raw key, where `30000 + Var` would have written 86277
                    // instead of 56277 — a global nothing reads, losing the write with no error.
                    _session.SetGlobalValue(raw.Key, raw.Value);
                    break;
                case SetFlagBitsEffect bits:
                    // *** NEVER OCCURS: zero instances across the whole generated tree. *** It is
                    // the decoder's fallback for a group write whose mask semantics are unconfirmed,
                    // and the spec blocks the decode on an IDA pass of the setter. Guessing how its
                    // bits map to keys would write to the wrong globals, so this reports instead.
                    _logger?.LogWarning(
                        "Dialog effect SetFlagBits(group {Group}, {Count} bits) is not applied — the "
                        + "group's key arithmetic is unconfirmed. It does not occur in shipped data, "
                        + "so this is an override or a decoder change.", bits.Group, bits.Bits.Count);
                    break;
            }
        }

        /// <summary>
        /// A temporary flag (dialog op 14, <c>SetTemporaryFlag</c>) is set now and cleared when its
        /// timer runs out: DIALOG.C:1306 does <c>gstate_event_write(key, 1)</c> immediately, then
        /// <c>timerpool_upsert(4, key, 0x40, duration)</c> — kind 4 = clear-flag, mode 0x40 =
        /// overwrite the existing entry for this key. So re-triggering restarts the countdown
        /// rather than stacking a second one. The clear is scheduled unconditionally in the
        /// original (all eight shipped temporary flags set rather than clear).
        /// </summary>
        private void ScheduleTemporaryFlagClear(SetFlagEffect flag) {
            if (flag.ForTicks is not uint ticks || _clock == null) {
                return;
            }
            _clock.ScheduleTimer(TimerType.ClearFlag, flag.Flag, ticks, replaceExisting: true);
        }

        /// <summary>
        /// Dialog op 22 (<c>SetTimer</c>): <c>timerpool_upsert(kind, key, mode, ticks)</c> straight
        /// through (DIALOG.C:1310). The extractor splits the entry's key into
        /// <see cref="SetTimerAction.OnExpiry"/> for the flag kinds and
        /// <see cref="SetTimerAction.TimerTarget"/> for the rest, so it is rebuilt here.
        ///
        /// <para>The one shipped use of this op is the corpse-flavour flag 8127 in DIAL_Z00: clear
        /// it 3600 ticks (two hours) after the body was examined, restarting the countdown on each
        /// fresh look. It carries <see cref="TimerFlag.Overwrite"/> — which is why those names had
        /// to be right (TASK-148): read the other way round, every glance at a corpse would stack
        /// another two hours instead of restarting the one already running.</para>
        /// </summary>
        public void ApplyTimerAction(SetTimerAction action) {
            if (_clock == null) {
                return;
            }
            int key = action.OnExpiry is SetFlagEffect onExpiry ? onExpiry.Flag : action.TimerTarget ?? 0;

            if (!_clock.ScheduleTimer(action.Type, key, action.Time,
                    accumulate: (action.Flag & TimerFlag.Accumulate) != 0,
                    replaceExisting: (action.Flag & TimerFlag.Overwrite) != 0)) {
                _logger.LogWarning(
                    "Timer pool full; dropped {TimerType} timer for key {Key} ({Ticks} ticks).",
                    action.Type, key, action.Time);
            }
        }

        /// <summary>
        /// Game time this entry's ops add (dialog op 13). The extractor exposes it in real seconds;
        /// the clock counts 2-second ticks.
        /// </summary>
        /// <summary>
        /// True when this entry both skips time and dismisses ITSELF — the original's
        /// <c>flags &amp; 0x40</c> branch in <c>dialog_wait_for_acknowledge</c>, which is the only
        /// thing that sets <c>g_dwDialogInputCooldown</c> and so the only thing that makes
        /// <c>recomputeParty</c> false. A page the player acknowledges does not.
        /// </summary>
        private static bool SkipsTimeUnacknowledged(DialogEntry entry) =>
            AccumulatedTicks(entry) > 0
            && (entry.Flags & DialogEntryFlags.AutoAdvanceTimer) != 0;

        private static long AccumulatedTicks(DialogEntry entry) {
            long ticks = 0;
            foreach (DialogActionBase action in entry.Actions) {
                if (action is AdvanceTimeAction advance) {
                    ticks += advance.Seconds / 2;
                }
            }
            return ticks;
        }

        /// <summary>
        /// Spend the time a dialog took, once its ops have all run (DIALOG.C:1505-1516).
        ///
        /// <para><b>In hour-sized steps, not one lump.</b> The engine loops
        /// <c>for (; 0 &lt; accum; accum -= 0x708)</c>, advancing at most an hour each pass, so
        /// every hourly tick in between actually happens. That matters because the shipped dialogs
        /// skip real time: 139 of them carry this op, from fifteen minutes up to <b>192 hours</b>.
        /// A week-long skip has to age the party a week — afflictions drifting, rations eaten each
        /// day — not once.</para>
        ///
        /// <para><b>A long skip counts as rest.</b> Past twelve hours (0x5460) the engine restamps
        /// the last-action snapshot on every pass and again at the end, so a character does not
        /// come out of a multi-day journey exhausted.</para>
        ///
        /// <para>Deviation, deliberate: the original spends this when the dialog is dismissed,
        /// while this runs at the end of the branch walk. The two differ only if something reads
        /// the clock while the text is on screen, which nothing does today — and resolving is the
        /// only point the UI-free executor is given.</para>
        /// </summary>
        private void SpendDialogTime(long ticks, bool autoAdvanced = false) {
            if (ticks <= 0 || _clock == null) {
                return;
            }
            bool countsAsRest = ticks > RestingSkipTicks;
            // Not time spent awake (TASK-494) — see GameSession.DialogTimeSkipInProgress.
            _session.DialogTimeSkipInProgress = true;
            // Mirrors g_dwDialogInputCooldown: a page that dismissed itself does not earn a meal.
            _session.DialogTimeSkipAutoAdvanced = autoAdvanced;
            try {
                for (long remaining = ticks; remaining > 0; remaining -= GameClock.TicksPerHour) {
                    if (countsAsRest) {
                        _session.LastRestTicks = _clock.Ticks;
                    }
                    _clock.Advance(Math.Min(GameClock.TicksPerHour, remaining));
                }
            } finally {
                _session.DialogTimeSkipInProgress = false;
                _session.DialogTimeSkipAutoAdvanced = false;
            }
            if (countsAsRest) {
                _session.LastRestTicks = _clock.Ticks;
            }
        }

        /// <summary>0x5460 ticks — twelve hours. Skip more than this and the party has effectively
        /// slept through it.</summary>
        private const long RestingSkipTicks = 0x5460;

        // Every entry the walk touches runs its ops, so a SetTimer on a router entry counts just
        // like one on the leaf — same rule the slot writes follow.
        private void ApplyEntryTimers(DialogEntry entry) {
            foreach (DialogActionBase action in entry.Actions) {
                if (action is SetTimerAction timer) {
                    ApplyTimerAction(timer);
                }
            }
        }

        /// <summary>
        /// Run the chapter-setup dialog for <paramref name="chapter"/> purely for its side effects
        /// — no UI. It branches on the chapter and applies that chapter's ChangeParty action, which
        /// sets the runtime party/head ORDER (chapter 1 = Locklear, Owyn, Gorath), plus its global
        /// story-flag effects. Call after the CHAPx.DAT apply and before the full-map / chapter
        /// dialog, matching the original sequence.
        /// </summary>
        public async UniTask RunChapterSetupAsync(int chapter) {
            Dialog dialog = await LoadDialogAsync(ChapterSetupDialogId);
            if (dialog == null) {
                _logger.LogError("Chapter-setup dialog {DialogId} failed to load; party order not applied.", ChapterSetupDialogId);
                return;
            }
            DialogEntry start = dialog.Entries.FirstOrDefault(e => e.Id == (uint)ChapterSetupDialogId);
            if (start == null) {
                _logger.LogError("Chapter-setup dialog {DialogId} has no root entry; party order not applied.", ChapterSetupDialogId);
                return;
            }

            // Resolve the chapter branch (VarCondition Var 7 = global 30007) against the chapter
            // the engine has just entered: the DOS engine writes global_30007_chapter from
            // CHAPx.DAT before running this dialog, so force 30007 rather than trusting the
            // STARTUP.GAM template value; every other global falls through to the live session.
            int? GetGlobal(int key) => key == 30007 ? chapter : _session.GetGlobalValue(key);

            // dialog_play_record seeds the speaker-slot table for this play too (DIALOG.C:789) and
            // each op list's SetTextVariable writes into it (:861), so a per-speaker operand in the
            // setup tree names a real character — chapter 5's ChangeAttribute raises Locklear, then
            // James, through slot 0 (TASK-533).
            DialogSlotContext slotContext = DialogSlotContextFactory.FromSession(_session);
            DialogSlotTable slots = DialogSlotPopulator.CreateForPlay(slotContext);
            DialogBranchWalker.ExecuteActions(dialog, start, GetGlobal,
                action => ApplyChapterSetupAction(action, slots, slotContext));
            _logger.LogInformation(
                "Chapter-setup dialog {DialogId} run for chapter {Chapter}; active party [{Party}].",
                ChapterSetupDialogId, chapter, string.Join(", ", _session.ActivePartyMemberNames));
        }

        /// <summary>
        /// The gameplay side-effects a played dialog carries — the actions that change the party
        /// rather than the text.
        /// </summary>
        /// <remarks>
        /// <para>These fire on <b>every entry the walk touches</b>, not just the leaf, for the same
        /// reason the text-variable actions do: the original runs each visited entry's op list as it
        /// passes (<c>dialog_play_record</c>'s loop), and several shipped entries are text-less
        /// routers whose whole purpose is a side effect.</para>
        /// <para>The per-speaker operand form needs the slot table; chapter setup builds its own the
        /// same way (<see cref="RunChapterSetupAsync"/>).</para>
        /// </remarks>
        /// <summary>Test seam: drive the gameplay walk without a whole dialog around it.</summary>
        internal void ApplyEntryGameplayActionsForTest(DialogEntry entry) =>
            ApplyEntryGameplayActions(entry, slots: null);

        private void ApplyEntryGameplayActions(DialogEntry entry, DialogSlotTable slots) {
            EntryReturnValue = null;

            // *** THE THREE PASSES ARE AN ORDER, NOT THREE PLACES. *** ExecuteDialog runs an
            // entry's actions in three sequential passes and PlayAudioTiming says which one fires a
            // cue; our port does the gameplay work in one walk, so the ordering is reproduced around
            // it — pass 1 before, pass 2 in data order among the actions themselves (the case arm
            // below), pass 3 after.
            PlayEntryAudio(entry, PlayAudioTiming.BeforeActions);

            foreach (DialogActionBase action in entry.Actions) {
                switch (action) {
                    case HealAction heal:
                        ApplyHeal(heal.Target, heal.Amount, slots);
                        break;
                    case LearnSpellAction learn:
                        ApplyLearnSpell(learn, slots);
                        break;
                    case GiveItemAction give:
                        ApplyGiveItem(give, slots);
                        break;
                    case TeleportAction teleport:
                        ApplyTeleport(teleport);
                        break;
                    case PlayAudioAction audio when audio.Timing == PlayAudioTiming.WithActions:
                        PlayDialogAudio(audio.AudioId);
                        break;
                    case SetReturnValueAction result:
                        EntryReturnValue = result.Value;
                        break;
                    case ApplyConditionAction condition:
                        ApplyConditionTo(condition, slots);
                        break;
                    case GetPartyAttributeAction query:
                        PublishPartyAttribute(query, slots);
                        break;
                    case UseItemAction use:
                        ConsumeFromParty(use);
                        break;
                    case RemoveItemAction remove:
                        ApplyRemoveItem(remove);
                        break;
                    case ChangeAttributeAction attribute:
                        ApplyChangeAttribute(attribute, slots);
                        break;
                    case SubAction sub:
                        ApplySubAction(sub);
                        break;
                    case ChangePartyAction party:
                        // *** IT IS AN ORDINARY ACTION, NOT A CHAPTER-SETUP ONE. *** DIALOG.C's
                        // case 17 sits in the SAME op switch as GiveItem (case 2) and every other
                        // arm here, so a conversation that changes the party changes it. Handling
                        // it only in ApplyChapterSetupAction dropped dialog 2300011's
                        // `{3, Owyn, James, Gorath}` in silence: James spoke, his topic menu came
                        // up headed "James asked about:", and chapter 2 carried on two-handed.
                        ApplyChangeParty(party);
                        break;
                }
            }

            PlayEntryAudio(entry, PlayAudioTiming.AfterActions);
        }

        /// <summary>The dialog's quoted price — <c>lEvtArgGoldCost</c>, global 30014.</summary>
        private const int QuotedPriceKey = 30014;

        /// <summary>The dialog's working value — <c>lEvtArgValue</c>, global 30015.</summary>
        private const int EventValueKey = 30015;

        /// <summary>The wager's second roll — global 30018.</summary>
        private const int AuxValueKey = 30018;

        /// <summary>Where a wager's outcome is published for the dialog to branch on — global
        /// 30000.</summary>
        private const int OutcomeKey = 30000;

        /// <summary>
        /// A two-roll wager against the house.
        /// </summary>
        /// <remarks>
        /// <b>Both rolls are published before the settlement</b>, as globals 30015 and 30018, so a
        /// dialog line can read the numbers back and narrate them. The outcome goes to 30000.
        ///
        /// <para><b>A DRAW WRITES NOTHING — established from the disassembly.</b> The decompiled C
        /// tests the same condition twice and so cannot reach its third arm; that is not an
        /// artifact, the binary really does compare a third time with <c>jl</c> after the second
        /// arm's <c>jge</c> (0x410c8 against 0x4109b), which only reaches equality and then falls to
        /// the return. So on a draw the outcome global keeps whatever the LAST wager left in it, and
        /// neither the purse nor the fund moves. Writing a "2" there would invent a state the
        /// shipped game never reaches.</para>
        /// </remarks>
        private void SettleWager(SubAction sub) {
            int partyRoll = DialogWager.RollDie(RandomBranchRoll(), sub.Field2);
            int houseRoll = DialogWager.RollDie(RandomBranchRoll(), sub.Field4);
            _session.SetGlobalValue(EventValueKey, partyRoll);
            _session.SetGlobalValue(AuxValueKey, houseRoll);

            DialogWager.Result result = DialogWager.Settle(partyRoll, houseRoll,
                _session.GetGlobalValue(QuotedPriceKey) ?? 0, sub.Field6,
                _session.EstablishmentFund);
            if (!result.Settled) {
                _logger.LogInformation("Dialog wager drew at {Roll}; nothing moved.", partyRoll);

                return;
            }

            _session.SetGlobalValue(OutcomeKey, result.Outcome);
            _session.EstablishmentFund = result.Fund;
            if (result.GoldDelta >= 0) {
                _session.PartyGold += result.GoldDelta;
            } else {
                SpendGold(-result.GoldDelta);
            }

            _logger.LogInformation(
                "Dialog wager {Party} vs {House}: party {Outcome}, purse {Delta}, fund {Fund}.",
                partyRoll, houseRoll,
                result.Outcome == DialogWager.PartyWins ? "won" : "lost",
                result.GoldDelta, result.Fund);
        }

        /// <summary>
        /// One of the seventeen hardcoded game-state effects (<c>wOp</c> 7).
        /// </summary>
        /// <remarks>
        /// <b>Eight are implemented, and the rest say so rather than passing silently.</b> Every
        /// subtype used to be dropped on the floor — the model parsed and nothing dispatched it — so
        /// 39 shipped effects did nothing at all. A warning for the unbuilt ones is the difference
        /// between "not built yet" and "quietly broken", which is the distinction that cost this
        /// project four dead features in a day.
        ///
        /// <para>These three are implemented because the dispatch body confirms them and the
        /// machinery is already here. Case 7 is what pins the variable mapping:
        /// <c>lEvtArgValue += Field2</c> is this enum's <c>IncrementGlobal30015</c>, so
        /// <c>lEvtArgValue</c> IS global 30015 and <c>lEvtArgGoldCost</c> is 30014 — which then makes
        /// sense of case 0 subtracting the quoted price and case 1 paying out the working value.</para>
        ///
        /// <para><b>Several subtypes are documented WRONG on the enum</b> — 2 repairs rather than
        /// counts, 3 RE-ARMS an encounter while 4 resolves one, 11 is a max on a per-NPC ledger
        /// rather than a min on the reward, 12 is a hotspot pass rather than a tutorial flag, 9
        /// repairs condition rather than "charge", and 13's key is a light SOURCE rather than an
        /// object. Every one is corrected in <see cref="SubActionType" />'s own remarks; do not
        /// implement any of them from the member names. See TASK-304.</para>
        /// </remarks>
        public void ApplySubAction(SubAction sub) {
            switch (sub.SubActionType) {
                case SubActionType.PayPartyMoney:
                    // Clamped at zero by SpendGold, as the original's own ternary does.
                    SpendGold(_session.GetGlobalValue(QuotedPriceKey) ?? 0);
                    break;
                case SubActionType.RewardPartyMoney:
                    _session.PartyGold += _session.GetGlobalValue(EventValueKey) ?? 0;
                    break;
                case SubActionType.IncrementGlobal30015:
                    _session.SetGlobalValue(EventValueKey,
                        (_session.GetGlobalValue(EventValueKey) ?? 0) + sub.Field2);
                    break;
                case SubActionType.CountArmorState:
                    RepairPartyArmour();
                    break;
                case SubActionType.CancelCombatEncounter:
                    ReArmEncounter(sub.Field2);
                    break;
                case SubActionType.CancelCombatEncounter2:
                    ResolveEncounter(sub.Field2);
                    break;
                case SubActionType.BlessAllPartySwords:
                    _logger.LogInformation(
                        "Dialog blessed {Count} equipped sword(s) to the third tier.",
                        InventoryQuery.BlessEquippedSwords(
                            _session.ActivePartyPacks, _session.ObjectInfo));
                    break;
                case SubActionType.MoveContainer:
                case SubActionType.MoveContainerKeepSource:
                    // The two are the SAME body — see the enum. 5 falls through into 6 with no
                    // break, so the original does it twice; a second identical copy changes
                    // nothing, so once is faithful in outcome.
                    CopyContainer(StockroomZone, StockroomX, StockroomY, ShopShelfZone, ShopShelfX,
                        ShopShelfY);
                    break;
                case SubActionType.RelocateContainers:
                    CopyContainer(StockroomZone, StockroomX, 1, VaultZone, VaultXFirst, VaultY);
                    CopyContainer(StockroomZone, 30, 1, VaultZone, VaultXSecond, VaultY);
                    break;
                case SubActionType.EmptyTrapCacheContainer:
                    EmptyContainer(TrapCacheZone, TrapCacheX, TrapCacheY);
                    break;
                case SubActionType.GambleRoll:
                    SettleWager(sub);
                    break;
                case SubActionType.CapReward:
                    // max, not min, and not on the reward money — see the enum. Field2 is a floor
                    // the dialog puts under the house's fund; it never lowers it.
                    _session.EstablishmentFund =
                        System.Math.Max(_session.EstablishmentFund, sub.Field2);
                    break;
                case SubActionType.BoostPrimarySpeakerAttribute512:
                    BoostEventActorSkill();
                    break;
                case SubActionType.SyncOwynPugSpells:
                    ShareMagiciansSpellbooks();
                    break;
                case SubActionType.ExtinguishTorches:
                    // The pool key is the light SOURCE, and 0 is the lit item in the party's hands
                    // — LightSourceDecay.Source.Item. The spell lights (dragon's breath, candle
                    // glow, stardusk) are keys 1..3 and are deliberately left burning.
                    _clock?.ExpireTimers(TimerType.Light,
                        (int)GameData.Resources.World.LightSourceDecay.Source.Item);
                    break;
                case SubActionType.SetTutorialFlag:
                    // Not a tutorial flag — it asks the world loop to re-run the hotspot pass where
                    // the party stands. Settled from the global's READERS (WORLDLP.C:185,
                    // MAP.C:208); see SubActionType. DEFERRED on purpose: a pass can raise a dialog,
                    // and running one from inside a resolving dialog re-enters it.
                    _session.HotspotPassRequested = true;
                    break;
                default:
                    _logger.LogWarning(
                        "Dialog sub-action {Subtype} is not implemented; the dialog's effect did not "
                        + "happen. See TASK-304.", sub.SubActionType);
                    break;
            }
        }

        // *** THE FIRST ARGUMENT IS THE ZONE, THOUGH canassa CALLS IT `kind`. ***
        // actorspawn_objfixed(kind, x, y) matches it against the OBJFIXED record's `kind` header
        // field, which reads as an object category — but every caller passes a zone id, plainly so
        // in itemuse_ground_pile_open_inv, which passes g_gameState.nZoneId for it. So these are
        // (zone, x, y) triples and GetLiveContainerAt takes them directly.
        private const int StockroomZone = 0;
        private const int StockroomX = 20;
        private const int StockroomY = 0;
        private const int ShopShelfZone = 1;
        private const int ShopShelfX = 2;
        private const int ShopShelfY = 3;
        private const int VaultZone = 15;
        private const int VaultXFirst = 60;
        private const int VaultXSecond = 64;
        private const int VaultY = 3;
        private const int TrapCacheZone = 3;
        private const int TrapCacheX = 1308000;
        private const int TrapCacheY = 1002400;

        /// <summary>
        /// Copies one fixed container's contents over another's, leaving the source untouched.
        /// </summary>
        /// <remarks>
        /// <b>It is a COPY, and the source keeps everything</b> — <c>itemuse_actor_spawn_clone_inv</c>
        /// (ITEMUSE.C:555) writes the destination's item list from the source's and then calls
        /// <c>actorspawn_destroy_and_persist</c>, which flushes the record and frees the in-memory
        /// actor. Nothing is emptied and nothing is deleted; "dispose source" in the enum's old
        /// wording described a destruction that is only the allocation going away.
        ///
        /// <para><b>The Equipped bit is cleared on every item copied</b> (<c>flags &amp;= 0xffbf</c>).
        /// An item that was being worn where it came from must not read as worn in a chest.</para>
        ///
        /// <para>The original dereferences both actors without a null check, so it assumes the two
        /// records exist in this chapter. Ours warns instead — a chapter band that excludes one of
        /// them would otherwise be an exception in the middle of a conversation.</para>
        ///
        /// <para><b>THE LOOKUP HERE IS THE SAVE ONLY, WHICH IS HALF OF WHAT THE ORIGINAL DOES.</b>
        /// <c>actorspawn_objfixed</c> reads TEMP.GAM and then falls back to OBJFIXED.DAT;
        /// <see cref="GameSession.GetLiveContainerAt"/> is the first pass alone, because only a
        /// save-backed container has a mutable runtime object to write into. A placement that
        /// exists only in the shipped file cannot be a destination until container
        /// spawn-and-persist is built (TASK-304). <see cref="Warn"/> tells the two apart rather
        /// than reporting both as "nothing there", which is the distinction that decides whether
        /// the effect is unreachable or merely unwritable.</para>
        /// </remarks>
        private void CopyContainer(int fromZone, int fromX, int fromY,
            int toZone, int toX, int toY) {
            RuntimeContainer source = _session.GetLiveContainerAt(fromZone, fromX, fromY);
            RuntimeContainer target = _session.GetLiveContainerAt(toZone, toX, toY);
            if (source == null || target == null) {
                _logger.LogWarning(
                    "Dialog container copy skipped: ({FromZone},{FromX},{FromY}) -> "
                    + "({ToZone},{ToX},{ToY}); source {Source}, target {Target}, chapter "
                    + "{Chapter}.", fromZone, fromX, fromY, toZone, toX, toY,
                    Warn(source, fromZone, fromX, fromY), Warn(target, toZone, toX, toY),
                    _session.Chapter);
                return;
            }

            target.Items.Clear();
            foreach (RuntimeItem item in source.Items) {
                RuntimeItem copy = item.Clone();
                copy.ItemFlags &= unchecked((ushort)~(ushort)GameData.ItemFlags.Equipped);
                target.Items.Add(copy);
            }

            _logger.LogInformation(
                "Dialog copied {Count} item(s) from ({FromZone},{FromX},{FromY}) to "
                + "({ToZone},{ToX},{ToY}).", target.Items.Count, fromZone, fromX, fromY,
                toZone, toX, toY);
        }

        /// <summary>
        /// Empties one fixed container.
        /// </summary>
        /// <remarks>
        /// <c>itemCount = 0</c> then <c>actorspawn_destroy_and_persist</c> (EVTCOND.C case 14): the
        /// items are dropped, the record is written back and the actor freed. The container itself
        /// stays where it is.
        /// </remarks>
        private void EmptyContainer(int zone, int x, int y) {
            RuntimeContainer container = _session.GetLiveContainerAt(zone, x, y);
            if (container == null) {
                _logger.LogWarning(
                    "Dialog container empty skipped: ({Zone},{X},{Y}) is {State} in chapter "
                    + "{Chapter}.", zone, x, y, Warn(container, zone, x, y), _session.Chapter);
                return;
            }

            _logger.LogInformation("Dialog emptied {Count} item(s) from ({Zone},{X},{Y}).",
                container.Items.Count, zone, x, y);
            container.Items.Clear();
        }

        /// <summary>
        /// Why a container lookup came back empty, in words worth logging.
        /// </summary>
        /// <remarks>
        /// <b>"Not in the save" and "not anywhere" are different problems.</b> The first is this
        /// port's gap — the placement is in OBJFIXED.DAT and has no runtime object to write into;
        /// the second means the dialog names a spot that holds nothing in this chapter, which for
        /// subtypes 5 and 6 is true in every chapter. Collapsing them into one message would hide
        /// which.
        /// </remarks>
        private string Warn(RuntimeContainer found, int zone, int x, int y) {
            if (found != null) {
                return "found";
            }

            return _session.GetContainerAt(zone, x, y) != null
                ? "shipped-only (no runtime container to write into)"
                : "absent";
        }

        /// <summary>The delta sub-action 15 applies, in 256ths — two whole points.</summary>
        /// <remarks>
        /// <b>512 is 2.0, not 512.</b> <c>stat_combatant_modify</c> takes its delta in 256ths and
        /// carries only the whole part into the stored value, banking the remainder in the stat's
        /// own fraction. Passing 512 as points would hand out a skill gain two hundred times too
        /// large — and the clamp would hide it by pinning the skill at its ceiling.
        /// </remarks>
        private const int SkillBoostDelta = 512;

        /// <summary>
        /// Two points of a skill for the actor the last event selected.
        /// </summary>
        /// <remarks>
        /// <b>Which skill comes from global 30015 and which actor from <c>nEvtArgActor0</c></b> —
        /// neither is in the sub-action's own fields. The original is
        /// <c>stat_combatant_modify(&amp;characters[nEvtArgActor0], lEvtArgValue, 0x200, 0)</c>
        /// (0x41270), so a dialog sets the attribute number in a global first and this reads it back.
        ///
        /// <para><b>It is NOT the speaker, despite the member's name.</b>
        /// <see cref="GameSession.EventActor"/> is written by whatever last picked an actor out of
        /// the party — for the barding path, the performer <c>stat_party_find_extreme</c> chose.
        /// Resolving "the primary speaker" from the slot table instead would boost whoever happens
        /// to be talking, which is a different character whenever the two differ.</para>
        ///
        /// <para>Mode 0 is <see cref="StatChangeMode.Absolute"/> — no proportional scaling. The
        /// study bonus, the fraction carry, the per-stat clamps and the improvement signal are all
        /// <see cref="StatEngine.Modify"/>'s, which is the port of that same routine; this only
        /// chooses the actor, the attribute and the delta.</para>
        /// </remarks>
        private void BoostEventActorSkill() {
            int actor = _session.EventActor;
            var attribute = (ActorAttribute)(_session.GetGlobalValue(EventValueKey) ?? -1);
            ActorStat[] stats = actor < 0 ? null : _session.StatsOf(actor);
            if (stats == null || (int)attribute < 0 || (int)attribute >= stats.Length
                || stats[(int)attribute] == null) {
                _logger.LogWarning(
                    "Dialog skill boost skipped: actor {Actor}, attribute {Attribute} — nothing has "
                    + "selected an actor, or the attribute is out of range.", actor, (int)attribute);

                return;
            }

            // Through the session so the sheet mark and the party-dirty bit follow the change
            // (TASK-611); it holds the character index this already resolved.
            StatEngine.StatChange change =
                _session.ModifyStatOf(actor, attribute, SkillBoostDelta, StatChangeMode.Absolute);
            _logger.LogInformation(
                "Dialog boosted {Attribute} of character {Actor} to {Value}.",
                attribute, actor, change.Value);
        }

        /// <summary>Owyn — character 2 of the six-strong roster (<c>CHR_OWYN</c>).</summary>
        private const int Owyn = 2;

        /// <summary>Pug — character 3 (<c>CHR_PUG</c>).</summary>
        /// <remarks>
        /// <c>SaveGameSection0.ChapterSpeaker</c> names the same id the same way, from the same
        /// table; these are the character-table indices, not party positions.
        /// </remarks>
        private const int Pug = 3;

        /// <summary>
        /// The two magicians end up knowing the union of what either one knew.
        /// </summary>
        /// <remarks>
        /// <b>It reaches them through the character table, not the party</b> — the original indexes
        /// <c>characters[CHR_OWYN]</c> and <c>characters[CHR_PUG]</c> directly, so it works on
        /// whichever of them is out of the party as much as on the one in it. Resolving them
        /// through the active roster would silently do nothing whenever Pug is not travelling,
        /// which for most of the game he is not.
        /// </remarks>
        private void ShareMagiciansSpellbooks() {
            int gained = SpellBook.Share(_session.KnownSpellsOf(Owyn), _session.KnownSpellsOf(Pug));
            _logger.LogInformation(
                "Dialog merged Owyn's and Pug's spellbooks; {Count} spell(s) newly known between "
                + "them.", gained);
        }

        /// <summary>
        /// Mends every damaged piece of party armour, and prices the job per piece.
        /// </summary>
        /// <remarks>
        /// <b>The subtype REPAIRS; it does not count, whatever its frozen enum name says.</b> The
        /// routine behind it takes a <c>do_repair</c> flag — the dialog CONDITION side passes 0 and
        /// only counts, this sub-action passes 1 (EVTCOND.C:21, case 2 at :193). Implementing it
        /// from the name gives a counter and leaves the armour the player just paid to have mended
        /// still broken. See <see cref="SubActionType" />.
        ///
        /// <para><b>The two global writes are not optional, and they are what makes the price
        /// right.</b> The original sets <c>lEvtArgValue</c> to the number repaired and then
        /// multiplies <c>lEvtArgGoldCost</c> by it, so the quoted price is a UNIT price and
        /// <see cref="SubActionType.PayPartyMoney" /> (authored as a separate sub-action) deducts
        /// the total. Skipping the multiply mends a whole party's armour for the price of one
        /// piece.</para>
        ///
        /// <para>Note the multiply happens whether or not anything was repaired: a party with
        /// nothing damaged multiplies the price by zero and is charged nothing, which is the
        /// original's own behaviour rather than a guard added here.</para>
        /// </remarks>
        private void RepairPartyArmour() {
            int repaired = InventoryQuery.RepairArmour(
                _session.ActivePartyPacks, _session.ObjectInfo);

            _session.SetGlobalValue(EventValueKey, repaired);
            _session.SetGlobalValue(QuotedPriceKey,
                (_session.GetGlobalValue(QuotedPriceKey) ?? 0) * repaired);
            _logger.LogInformation("Dialog repaired {Count} piece(s) of party armour.", repaired);
        }

        /// <summary>
        /// Puts a defeated encounter back — set by composition, because the service that does it
        /// depends on the dialog layer.
        /// </summary>
        /// <remarks>
        /// <b>A callback rather than a constructor dependency, and that is forced.</b>
        /// <c>HotspotService</c> takes <c>IDialogManager</c>, which owns this executor, so asking
        /// for the service here would close a DI cycle. The same lazy-accessor treatment
        /// <c>HotspotService</c> itself gives <c>IGameFlow</c> for the same reason.
        /// </remarks>
        public System.Action<long> RearmEncounter { get; set; }

        /// <summary>
        /// Resolves encounters — the same callback treatment, and for the same DI reason.
        /// </summary>
        public System.Func<long, int> ResolveEncounters { get; set; }

        /// <summary>
        /// Re-arms an encounter — clears every "you have already done this" flag and puts its
        /// monsters back on their feet.
        /// </summary>
        /// <remarks>
        /// <b>The opposite of <see cref="ResolveEncounter" />, not its sibling</b>, whatever the two
        /// enum names suggest: this one writes <c>ENCOUNTER_FOUGHT(id) = 0</c> and that one writes 1.
        ///
        /// <para><b>Healing and clearing are ONE operation.</b> <c>sub_ovr188_BCD</c> @0x75d3d
        /// clears the two transient hotspot blocks and the encounter's fought flag and then calls
        /// <c>combatenc_rearm_roster_actors</c>; doing only the flags arms an encounter that fields
        /// the wounded and the dead, and doing only the heal leaves monsters nobody can meet.
        /// <c>HotspotService.RearmEncounter</c> is that whole operation and is what the eleven
        /// self-re-arming encounters already use, so a dialog asking for it runs the same code
        /// rather than a second copy of the rule.</para>
        /// </remarks>
        private void ReArmEncounter(int encounterNumber) {
            if (RearmEncounter == null) {
                _logger.LogWarning(
                    "Dialog sub-action re-arms encounter {Encounter}, but no world is running to "
                    + "do it; the encounter stays resolved.", encounterNumber);

                return;
            }

            RearmEncounter(encounterNumber);
        }

        /// <summary>
        /// Resolves an encounter — it does not happen, and its actors are settled where they stand.
        /// </summary>
        /// <remarks>
        /// <b>This is the subtype that peacefully resolves a fight</b>, and the enum has it as the
        /// sibling of the one that does not: subtype 3 CLEARS the fought flag and re-arms, this one
        /// sets it. Do not take either from its name.
        ///
        /// <para><b>*** RESOLVING IS THREE OPERATIONS AND THE FLAG IS ONLY THE FIRST. ***</b> This
        /// wrote the flag and nothing else until 2026-09-03, which is one third of
        /// <c>rgnenc_mark_defended</c>: the original also stops every roaming actor in the record
        /// and kills every still-living actor on its roster. A flag-only version leaves the monsters
        /// standing on the map for the party to walk into after the conversation that was supposed
        /// to have settled them. It now runs <c>HotspotService.ResolveEncounters</c> — the same code
        /// a won fight runs — rather than a dialog-shaped copy.</para>
        ///
        /// <para><b>Id 0 means EVERY loaded record, not record 0</b>, and two of the five shipped
        /// instances pass it. The original skips a record only when
        /// <c>filter != 0 &amp;&amp; filter != enc_id</c> (<c>sub_ovr188_AAB</c> @0x75c1b), so a
        /// zero filter disables the test entirely.</para>
        /// </remarks>
        private void ResolveEncounter(int encounterNumber) {
            if (ResolveEncounters == null) {
                _logger.LogWarning(
                    "Dialog sub-action resolves encounter {Encounter}, but no world is running to "
                    + "settle it; nothing was resolved.", encounterNumber);

                return;
            }

            int resolved = ResolveEncounters(encounterNumber);
            _logger.LogInformation("Dialog resolved {Count} encounter(s) for filter {Filter}.",
                resolved, encounterNumber);
        }

        /// <summary>
        /// The dialog's "here, take this" (<c>wOp</c> 2).
        /// </summary>
        /// <remarks>
        /// Three things about this action are not what its field names suggest:
        /// <list type="bullet">
        ///   <item><b>Two object ids are money, not items.</b> 53 gives Amount SOVEREIGNS (ten
        ///   royals each) and 54 gives Amount royals; neither touches an inventory. 22 of the 116
        ///   shipped uses are one of these.</item>
        ///   <item><b><c>Amount</c> is the item's condition, not a quantity.</b> For a real item it
        ///   goes straight into the slot's condition byte — one item, that worn. Only for the two
        ///   money ids does it read as a count.</item>
        ///   <item><b><c>Cost</c> is charged only for what was accepted</b>, and in the party-wide
        ///   form it is divided by the party size and charged per member who had room. A party with
        ///   full packs therefore pays less, and the purse is clamped at zero either way.</item>
        /// </list>
        /// <para>The actor operand is masked to 7 bits before the usual party-wide/per-speaker
        /// split.</para>
        /// </remarks>
        private void ApplyGiveItem(GiveItemAction give, DialogSlotTable slots) {
            if (DialogMoneyGrant.IsMoney(give.ObjectId)) {
                // Straight to the purse, touching no pack — see DialogMoneyGrant.
                _session.PartyGold += DialogMoneyGrant.RoyalsFor(give.ObjectId, give.Amount);
                return;
            }

            int operand = give.Actor & 0x7f;
            int member = slots?.ResolveActorOperand(operand) ?? (operand <= 1
                ? DialogSlotTable.PartyWide
                : DialogSlotTable.Unresolved);
            if (member == DialogSlotTable.Unresolved) {
                _logger.LogWarning(
                    "Dialog GiveItem not applied: operand {Actor} resolved to no party member "
                    + "(object {ObjectId}).", give.Actor, give.ObjectId);
                return;
            }

            if (member != DialogSlotTable.PartyWide) {
                if (GiveTo(member, give)) {
                    SpendGold(give.Cost);
                }
                return;
            }

            byte[] roster = _session.ActivePartyIndices;
            int share = roster.Length > 0 ? give.Cost / roster.Length : 0;
            foreach (byte character in roster) {
                if (GiveTo(character, give)) {
                    SpendGold(share);
                }
            }
        }

        /// <summary>
        /// The roster a dialog names — <c>ExecuteDialog</c>'s action type 17 (DIALOG.C case 17,
        /// KRONDOR.EXE 0x4a005), the sole writer of <c>partySize</c> / <c>activeParty</c>.
        /// </summary>
        /// <remarks>
        /// Four fields written together and nothing else: the count and three member ids, in the
        /// authored display order. <see cref="GameSession.SetActiveParty"/> trims the array to the
        /// count — the original reads only the first <c>partySize</c> slots and leaves the rest
        /// holding whatever was there.
        ///
        /// <para>The original follows the write with <c>worldloop_pty_stat7_flag_cd</c>, which only
        /// re-gates REQ_MAIN's cast button on whether anyone in the NEW roster has attribute 7. Not
        /// ported here: this layer holds no menu page, and the port decides that button elsewhere.
        /// The composition change itself is announced by <c>PartyCompositionChanged</c>.</para>
        /// </remarks>
        private void ApplyChangeParty(ChangePartyAction party) {
            if (party == null) {
                return;
            }
            _session.SetActiveParty(
                (byte)party.PartySize,
                new[] { (byte)party.Member1, (byte)party.Member2, (byte)party.Member3 });
        }

        /// <summary>
        /// Hands one member the item. False only when the named member, everybody else in the
        /// party, and the ground pile were all full — see <see cref="InventoryAcquire.TryGive"/>.
        /// </summary>
        /// <remarks>
        /// <b>The argument is a party position, not an actor number.</b> Until 2026-09-12 this
        /// looked the pack up by <c>actors[member].ActorNumber</c>, which is 1-based, so every gift
        /// went to the pack of the member AFTER the one named and a gift to the last position found
        /// no container and was silently refused. Every other caller of
        /// <see cref="GameSession.GetActorInventory"/> already passed the position.
        /// </remarks>
        private bool GiveTo(int member, GiveItemAction give) {
            SaveGameActorData[] actors = _session.PartyActors;
            if (member < 0 || member >= actors.Length) {
                return false;
            }
            RuntimeContainer pack = _session.GetActorInventory(member);
            if (pack == null) {
                return false;
            }
            // Amount is the condition byte here, not a count — one item, that worn.
            var item = new RuntimeItem((byte)give.ObjectId, (byte)give.Amount, 0);
            // The ring, not the pack, for a key — see InventoryAcquire.TryGive.
            return InventoryAcquire.TryGive(pack, item, _session.ObjectInfo,
                _session.SharedKeysInventory, _session.ConditionsOf(member), CascadeAround(member));
        }

        /// <summary>
        /// Where a gift goes when the member it was addressed to cannot carry it: the rest of the
        /// active party in roster order, then the ground pile — the tail of
        /// <c>cmbinv_actor_acquire_item</c> (CMBINV.C:1022).
        /// </summary>
        /// <remarks>
        /// Each member is paired with their OWN conditions because the original re-reads the
        /// holder's status row inside the loop: food that cascades feeds whoever ends up with it.
        /// The ground pile is paired with a null — it has no hunger.
        /// </remarks>
        private (RuntimeContainer, ActorConditions)[] CascadeAround(int receiver) {
            var fallbacks = new List<(RuntimeContainer, ActorConditions)>();
            foreach (byte member in _session.ActivePartyIndices ?? Array.Empty<byte>()) {
                if (member == receiver) {
                    continue;
                }
                RuntimeContainer pack = _session.GetActorInventory(member);
                if (pack != null) {
                    fallbacks.Add((pack, _session.ConditionsOf(member)));
                }
            }
            RuntimeContainer ground = _session.GroundPile;
            if (ground != null) {
                fallbacks.Add((ground, null));
            }
            return fallbacks.ToArray();
        }

        private void SpendGold(int amount) {
            _session.PartyGold -= amount;
            if (_session.PartyGold < 0) {
                _session.PartyGold = 0;
            }
        }

        /// <summary>
        /// Teaches a spell to the member in the action's speaker slot.
        /// </summary>
        /// <remarks>
        /// Always per-speaker: the original has no party-wide branch for this op, so an operand of
        /// 0 or 1 has no meaning and is refused rather than broadcast. All four shipped uses name
        /// speaker slot 3.
        /// </remarks>
        private void ApplyLearnSpell(LearnSpellAction learn, DialogSlotTable slots) {
            int member = slots.ResolveActorOperand(learn.Actor);
            if (member < 0) {
                _logger.LogWarning(
                    "Dialog LearnSpell not applied: operand {Actor} resolved to no party member "
                    + "(spell {SpellId}).", learn.Actor, learn.SpellId);
                return;
            }
            ushort[] known = _session.KnownSpellsOf(member);
            if (known == null) {
                return;
            }
            if (SpellBook.Learn(known, learn.SpellId)) {
                _logger.LogInformation("Dialog taught spell {SpellId} to member {Member}.",
                    learn.SpellId, member);
            }
        }

        /// <summary>
        /// The heal, for both operand forms. <paramref name="slots"/> may be null (chapter setup),
        /// in which case only the party-wide form can be honoured.
        /// </summary>
        private void ApplyHeal(int target, int amount, DialogSlotTable slots) {
            int member = slots?.ResolveActorOperand(target) ?? (target <= 1
                ? DialogSlotTable.PartyWide
                : DialogSlotTable.Unresolved);

            if (member == DialogSlotTable.Unresolved) {
                _logger.LogWarning(
                    "Dialog Heal not applied: operand {Target} resolved to no party member.", target);
                return;
            }

            if (member == DialogSlotTable.PartyWide) {
                foreach (byte character in _session.ActivePartyIndices) {
                    HealMember(character, amount);
                }
                return;
            }
            HealMember(member, amount);
        }

        private void HealMember(int member, int amount) {
            ActorStat[] stats = _session.StatsOf(member);
            if (stats == null) {
                return;
            }
            if (CharacterHeal.Apply(stats, _session.ConditionsOf(member), amount)) {
                // A full heal stamps the rest clock, which is party state rather than the
                // character's — the original writes it inside the per-character heal.
                _session.LastRestTicks = _session.GameTimeIn2Seconds;
            }
        }


        /// <summary>
        /// The dialog's "you are now over there" — a ladder, a tunnel or a scripted move.
        /// </summary>
        /// <remarks>
        /// <b>It QUEUES; it does not move anybody.</b> A dialog only fills the engine's one-slot
        /// <c>teleportationData</c> global; the move itself is <c>ProcessTeleportation</c> @0x4ebe7,
        /// run from the world loop once whatever was on top of it has closed. Writing the position
        /// straight into the session here left the party holding zone-11 coordinates while the
        /// zone-2 world was still built and drawn — measured 2026-09-10 at Krondor's sewer door,
        /// where the party "arrived" in the sewers and the location screen carried on showing the
        /// town.
        ///
        /// <para>The two halves are drained by different loops on purpose — see
        /// <see cref="GameData.Resources.Location.PendingTeleport"/>. A destination that names a GDS
        /// scene is taken by the location loop (so a temple teleport lands INSIDE the destination
        /// temple); the world half is taken afterwards, by
        /// <c>LocationScenePlayer.ApplyQueuedTeleportAsync</c> when a location was open and by
        /// <c>GameFlow.PumpWorldLoop</c> when one was not.</para>
        /// </remarks>
        private void ApplyTeleport(TeleportAction teleport) {
            GameData.Resources.Location.TeleportDestination destination =
                _teleportDestinations?.ById(teleport.DestinationId);
            if (destination?.Location == null) {
                _logger.LogWarning(
                    "Dialog teleport to destination {DestinationId} ignored: the table is unloaded "
                    + "or has no such row.", teleport.DestinationId);
                return;
            }

            if (_teleport == null) {
                _logger.LogWarning(
                    "Dialog teleport to destination {DestinationId} dropped: no teleport slot "
                    + "injected (test wiring).", teleport.DestinationId);
                return;
            }

            _teleport.Queue(destination);
        }

        /// <summary>
        /// A dialog changing someone's stat — DIALOG.C's <c>case 9</c>.
        /// </summary>
        /// <remarks>
        /// <b>Three things about this are not visible from the model's field names.</b>
        ///
        /// <list type="number">
        ///   <item><b><c>Type</c> is the change MODE</b>, not a kind of attribute. It is the high
        ///   byte of the op's first word and goes straight to <c>stat_combatant_modify</c>'s mode
        ///   argument. All 80 shipped instances carry 0
        ///   (<see cref="StatChangeMode.Absolute"/>), so a port that hardcoded absolute would look
        ///   right — until a mod authored anything else.</item>
        ///   <item><b>The amount is a RANGE, rolled per use</b>: <c>Min + RND(Max - Min)</c> when
        ///   the two differ, which 23 of the 80 do. Taking Min alone quietly removes the variance
        ///   the author asked for.</item>
        ///   <item><b>Attribute 0x10 overrides the mode with 100.</b>
        ///   <c>if (op-&gt;nA2 == 0x10) val = 'd';</c> — <c>'d'</c> is 100, and for the
        ///   health/stamina combo the mode is read as a PERCENTAGE TARGET rather than a scaling
        ///   rule. 22 of the 80 are that attribute, so this is not an edge case.</item>
        /// </list>
        ///
        /// <para>Target follows the same operand rule as <see cref="ApplyHeal"/>: 1 or less is the
        /// whole party, above that a speaker slot.</para>
        /// </remarks>
        private void ApplyChangeAttribute(ChangeAttributeAction change, DialogSlotTable slots) {
            // *** THE SPAN IS COMPUTED IN UNSIGNED 16-BIT, AND IT HAS TO BE. *** The original
            // snapshots both ends as `(unsigned short)` before comparing and subtracting, while our
            // extractor reads Min SIGNED and Max UNSIGNED — so a damage roll authored as
            // min -1280, max 65280 is a span of 1,024 (four points), not 66,560. Subtracting as
            // read would turn a scratch into an instant kill.
            int amount = RollBand(change.MinimumAmount, change.MaximumAmount);

            int member = slots?.ResolveActorOperand(change.Target) ?? (change.Target <= 1
                ? DialogSlotTable.PartyWide
                : DialogSlotTable.Unresolved);
            if (member == DialogSlotTable.Unresolved) {
                _logger.LogWarning(
                    "Dialog ChangeAttribute not applied: operand {Target} resolved to no party "
                    + "member ({Attribute}).", change.Target, change.Attribute);

                return;
            }

            if (member == DialogSlotTable.PartyWide) {
                foreach (byte character in _session.ActivePartyIndices) {
                    ChangeMemberAttribute(character, change, amount);
                }

                return;
            }

            ChangeMemberAttribute(member, change, amount);
        }

        /// <summary>
        /// What this entry's <c>SetReturnValue</c> asked the dialog to answer, if it carried one.
        /// </summary>
        /// <remarks>
        /// <b>The original's <c>nResult</c>, and it ENDS the dialog as well as answering it.</b>
        /// DIALOG.C's third pass is <c>else if (op-&gt;wOp == 0x15) { done = 1; nResult = op-&gt;nA1; }</c>
        /// — so a dialog does not merely report a value on its way out, the value IS the way out.
        ///
        /// <para>Scoped to one entry: cleared at the top of every action walk and read by the caller
        /// immediately after, which is the same lifetime the original's stack local has. It is not
        /// conversation state and must not be treated as such — a second entry with no
        /// <c>SetReturnValue</c> answers nothing rather than repeating the last answer.</para>
        /// </remarks>
        public int? EntryReturnValue { get; private set; }

        /// <summary>Where a queried attribute lands for a later condition to read — global 30013.</summary>
        /// <remarks>
        /// <c>nEvtArgDlgResult</c>, returned by <c>gstate</c>'s case 13 (GSTATE.C:79). Our save model
        /// already named the field <c>Global30013AttributeValue</c> — it knew what the slot was for
        /// before anything wrote it.
        /// </remarks>
        private const int AttributeResultKey = 30013;

        /// <summary>
        /// Rolls an amount authored as a band — <c>Min + RND(Max - Min)</c>, in UNSIGNED 16-bit.
        /// </summary>
        /// <remarks>
        /// Shared by <see cref="ApplyConditionTo"/> and <see cref="ApplyChangeAttribute"/> because
        /// the original writes the same five lines in both arms, including the unsigned snapshot of
        /// both ends. Subtracting as read would turn a small negative band into an enormous one.
        /// </remarks>
        private static int RollBand(int minimum, int maximum) {
            var low = (ushort)minimum;
            var high = (ushort)maximum;

            return high == low ? minimum : minimum + UnityEngine.Random.Range(0, high - low);
        }

        /// <summary>
        /// A dialog inflicting or relieving an affliction — DIALOG.C's <c>case 8</c>.
        /// </summary>
        /// <remarks>
        /// <b>It ADDS to the rank; it does not set it.</b>
        /// <c>stat_combatant_apply_condition</c> (STAT.C:355) reads the current rank, adds the
        /// amount and clamps to 0..100 — so a dialog can deepen a poison the party already has, or
        /// take some off with a negative amount. Setting the rank would erase whatever the world had
        /// already done to them.
        ///
        /// <para><b>Not mirrored: the condition EVENT FLAG.</b> The original also writes a
        /// per-actor flag when a rank crosses zero in either direction (and skips that for
        /// conditions 3, 4, and 6 in combat). The key's layout is <c>slot * 7 + index</c> into a
        /// range this port has not identified, and inventing one would be worse than leaving the
        /// rank correct and the flag absent. See TASK-313.</para>
        /// </remarks>
        private void ApplyConditionTo(ApplyConditionAction apply, DialogSlotTable slots) {
            int amount = RollBand(apply.MinimumAmount, apply.MaximumAmount);
            int member = slots?.ResolveActorOperand(apply.Target) ?? (apply.Target <= 1
                ? DialogSlotTable.PartyWide
                : DialogSlotTable.Unresolved);
            if (member == DialogSlotTable.Unresolved) {
                _logger.LogWarning(
                    "Dialog ApplyCondition not applied: operand {Target} resolved to no party "
                    + "member ({Condition}).", apply.Target, apply.Condition);

                return;
            }

            if (member == DialogSlotTable.PartyWide) {
                foreach (byte character in _session.ActivePartyIndices) {
                    RaiseCondition(character, apply.Condition, amount);
                }

                return;
            }

            RaiseCondition(member, apply.Condition, amount);
        }

        private void RaiseCondition(int character, ActorCondition condition, int amount) {
            ActorConditions conditions = _session.ConditionsOf(character);
            if (conditions == null || amount == 0) {
                return;   // the original's own `amount != 0` guard
            }

            int raised = conditions[condition] + amount;
            conditions[condition] = System.Math.Clamp(raised, 0, ActorConditions.MaxRank);
        }

        /// <summary>
        /// A dialog asking what someone's attribute is — DIALOG.C's <c>case 10</c>.
        /// </summary>
        /// <remarks>
        /// <b>The party form asks for the BEST, not a sum or an average</b>
        /// (<c>stat_party_find_extreme</c>), which is the same reading every skill check in the game
        /// uses. The answer goes into <see cref="AttributeResultKey"/> for a later condition to
        /// branch on — this action never changes anything by itself, which is why it reads as a
        /// no-op until you notice what consumes it.
        /// </remarks>
        private void PublishPartyAttribute(GetPartyAttributeAction query, DialogSlotTable slots) {
            int member = slots?.ResolveActorOperand(query.Target) ?? (query.Target <= 1
                ? DialogSlotTable.PartyWide
                : DialogSlotTable.Unresolved);

            int value;
            if (member == DialogSlotTable.PartyWide) {
                value = _session.PartyExtreme(query.Attribute, out int _);
            } else if (member == DialogSlotTable.Unresolved) {
                _logger.LogWarning(
                    "Dialog GetPartyAttribute not applied: operand {Target} resolved to no party "
                    + "member ({Attribute}).", query.Target, query.Attribute);

                return;
            } else {
                ActorStat[] stats = _session.StatsOf(member);
                var index = (int)query.Attribute;
                value = stats != null && index >= 0 && index < stats.Length && stats[index] != null
                    ? StatEngine.Get(stats[index], query.Attribute,
                        stats[(int)ActorAttribute.Health], StatReadMode.Effective,
                        _session.PartyEffectsFor(member, query.Attribute, inCombat: false))
                    : 0;
            }

            _session.SetGlobalValue(AttributeResultKey, value);
        }

        /// <summary>
        /// The one object whose CONDITION has to match before it is taken.
        /// </summary>
        /// <remarks>
        /// <c>ACTOR_ITEM(...).item_id != 'x' || condition == nA2</c> — the test is written against
        /// the character literal <c>'x'</c>, which is 120. For every other object the condition is
        /// ignored, so <c>Amount</c> means nothing there; three shipped instances name 120 and do
        /// rely on it.
        /// </remarks>
        private const int ConditionMatchedObjectId = 'x';

        /// <summary>
        /// A dialog taking something back — DIALOG.C's <c>case 3</c>.
        /// </summary>
        /// <remarks>
        /// <b>Four rules here are not guessable from the two field names.</b>
        ///
        /// <list type="bullet">
        ///   <item><b>The same two ids are money</b> as on the giving side: 53 is sovereigns (ten
        ///   royals each) and 54 royals, and neither touches an inventory. Most shipped instances
        ///   are 53. The purse is clamped at zero afterwards, which
        ///   <see cref="SpendGold"/> already does.</item>
        ///   <item><b>The SHARED inventory is searched too</b>, after the active members — the loop
        ///   runs <c>i &lt;= partySize</c> and the extra pass is
        ///   <c>g_gameState.shared_inventory</c>. Searching only the party would leave a dialog
        ///   unable to take back something stowed in the keys pack.</item>
        ///   <item><b>Exactly ONE item goes, whatever <c>Amount</c> says.</b> The original sets both
        ///   loop counters to 999 on the first match. <c>Amount</c> is the condition filter, not a
        ///   quantity — removing <c>Amount</c> copies would strip a stack the author meant to
        ///   thin by one.</item>
        ///   <item><b>The freed slot takes the LAST item</b>, rather than the rest shifting up:
        ///   <c>ACTOR_ITEM(p, slot) = ACTOR_ITEM(p, --itemCount)</c>. That reorders what is left,
        ///   and the grid packs from list order, so it is visible.</item>
        /// </list>
        /// </remarks>
        private void ApplyRemoveItem(RemoveItemAction remove) {
            if (DialogMoneyGrant.IsMoney(remove.ObjectId)) {
                SpendGold(DialogMoneyGrant.RoyalsFor(remove.ObjectId, remove.Amount));

                return;
            }

            foreach (RuntimeContainer pack in SearchedForRemoval()) {
                for (var i = 0; i < pack.Items.Count; i++) {
                    RuntimeItem item = pack.Items[i];
                    if (item == null || item.ObjectId != remove.ObjectId) {
                        continue;
                    }
                    if (remove.ObjectId == ConditionMatchedObjectId
                        && item.Variable != remove.Amount) {
                        continue;
                    }

                    pack.Items[i] = pack.Items[pack.Items.Count - 1];
                    pack.Items.RemoveAt(pack.Items.Count - 1);
                    pack.Dirty = true;
                    _logger.LogInformation("Dialog took object {ObjectId} back.", remove.ObjectId);

                    return;   // one item, and the original stops looking too
                }
            }

            _logger.LogInformation(
                "Dialog asked for object {ObjectId} back and nobody had one.", remove.ObjectId);
        }

        /// <summary>
        /// A dialog spending the party's supplies — DIALOG.C's <c>case 23</c>.
        /// </summary>
        /// <remarks>
        /// <b>*** IT TAKES `Amount` OF THEM, WHICH IS THE OPPOSITE OF
        /// <see cref="ApplyRemoveItem"/>. ***</b> The two actions have the same two fields and read
        /// alike; this one is <c>for (n = 0; n &lt; nA2; n++) consume_one(nA1);</c> while
        /// <c>RemoveItem</c> stops after the first match however large its Amount. Carrying one
        /// rule across to the other is the obvious mistake and there is nothing in the field names
        /// to stop it.
        ///
        /// <para><b>The party only — no shared inventory</b>, which is again the opposite of
        /// <see cref="ApplyRemoveItem"/>. <c>itemtbl_pty_consum_one_kind</c> loops
        /// <c>slot &lt; partySize</c> and stops there.</para>
        ///
        /// <para><b>It names the supplier as the event actor.</b> The routine sets
        /// <c>nEvtArgActor0</c> to the first member up front and overwrites it with whoever actually
        /// had one — so a line that follows can speak about the character who paid. That is the same
        /// register <see cref="GameSession.EventActor"/> holds for the barding performer.</para>
        ///
        /// <para>One instance ships (object 72, amount 1, DIAL_Z30), so the loop and the actor are
        /// both unexercised by the shipped data — reason to write them from the source rather than
        /// from what that one case would have needed.</para>
        /// </remarks>
        private void ConsumeFromParty(UseItemAction use) {
            byte[] roster = _session.ActivePartyIndices ?? System.Array.Empty<byte>();
            for (var taken = 0; taken < use.Amount; taken++) {
                // Reset per pass, as the original does: the default is the first member, and only a
                // successful take moves it.
                _session.EventActor = roster.Length > 0 ? roster[0] : -1;
                foreach (byte member in roster) {
                    RuntimeContainer pack = _session.GetActorInventory(member);
                    if (pack != null && InventoryConsume.TryConsumeOne(
                            pack, use.ObjectId, _session.ObjectInfo != null
                                ? _session.ObjectInfo.GetById
                                : null)) {
                        _session.EventActor = member;
                        break;
                    }
                }
            }
        }

        /// <summary>Every pack a removal looks in: the active party, then the shared inventory.</summary>
        private System.Collections.Generic.IEnumerable<RuntimeContainer> SearchedForRemoval() {
            foreach (RuntimeContainer pack in _session.ActivePartyPacks) {
                yield return pack;
            }

            RuntimeContainer shared = _session.SharedKeysInventory;
            if (shared != null) {
                yield return shared;
            }
        }

        /// <summary>The combo attribute's mode is a percentage target, and the original forces it.</summary>
        private const int HealthPoolFullPercent = 100;

        private void ChangeMemberAttribute(int character, ChangeAttributeAction change, int amount) {
            ActorStat[] stats = _session.StatsOf(character);
            if (stats == null) {
                return;
            }

            var index = (int)change.Attribute;
            if (change.Attribute == ActorAttribute.HealthStaminaCombo) {
                ActorStat health = stats[(int)ActorAttribute.Health];
                ActorStat stamina = stats[(int)ActorAttribute.Stamina];
                if (health != null && stamina != null) {
                    StatEngine.ModifyHealthPool(health, stamina, amount, HealthPoolFullPercent,
                        out bool drained, conditions: _session.ConditionsOf(character));
                    if (drained) {
                        _session.RecomputePartyDeathState();
                    }
                }

                return;
            }

            if (index >= 0 && index < stats.Length && stats[index] != null) {
                // Through the session so the sheet mark follows the change (TASK-611).
                _session.ModifyStatOf(character, change.Attribute, amount,
                    (StatChangeMode)change.Type);
            }
        }

        // Sink for DialogBranchWalker.ExecuteActions over the chapter-setup dialog. ChangeParty
        // sets the runtime party/head order; GlobalEffect story flags apply via ApplyEffect.
        // GiveItem goes through the same ApplyGiveItem the play path uses, slot table and all.
        // PushDialogEntry is control-flow the walker already consumed.
        private void ApplyChapterSetupAction(DialogActionBase action, DialogSlotTable slots,
            DialogSlotContext slotContext) {
            switch (action) {
                case SetTextVariableAction text:
                    DialogSlotPopulator.Assign(slots, text.Slot, text.Source, text.Aux, slotContext);
                    break;
                case TeleportAction teleport:
                    // Chapters 2, 3 and 6 queue a scene teleport (TELEPORT.DAT 30, 31, 37 are all
                    // zone 255): the world loop plays that location scene once the chapter starts.
                    ApplyTeleport(teleport);
                    break;
                case ChangeAttributeAction attribute:
                    ApplyChangeAttribute(attribute, slots);
                    break;
                case ChangePartyAction party:
                    ApplyChangeParty(party);
                    break;
                case GlobalEffectAction g when g.Effect != null:
                    ApplyEffect(g.Effect);
                    break;
                case SetTimerAction timer:
                    ApplyTimerAction(timer);
                    break;
                case GiveItemAction give:
                    // *** ONE ARM, AND IT GETS THE SLOT TABLE LIKE EVERY OTHER ACTION HERE. ***
                    // This used to be two: money went to the purse and a real ITEM was logged as
                    // "needs actor-inventory mutator" and dropped. Both halves of that were out of
                    // date. The mutator has existed since TASK-24, and the slot table chapter setup
                    // "does not have" is built by the caller and already handed to ChangeAttribute,
                    // Heal and LearnSpell (TASK-533) — GiveItem was simply never moved over with
                    // them.
                    //
                    // The operand is the per-speaker form: `give.Actor & 0x7f`, where <= 1 means the
                    // whole active party and anything above indexes speakerKinds[operand - 2]. That
                    // is 83 of the 116 shipped GiveItem uses, and ApplyGiveItem has resolved it
                    // correctly all along — it was reached on the play path and not on this one.
                    //
                    // What it cost: chapter 3's setup (2000023) hands James the Brass Spyglass
                    // (object 7) and the Silver Spider (object 111), both `actor 2` = speaker slot
                    // 0. Neither has ever arrived on this path — the same two warnings appear on an
                    // earlier successful crossing in the same log — and g50007/g50111 read 0 in
                    // chapter 3 (TASK-577).
                    ApplyGiveItem(give, slots);
                    break;
                case HealAction heal:
                    ApplyHeal(heal.Target, heal.Amount, slots);
                    break;
                case RemoveItemAction remove:
                    // Chapter 2's tail (DIAL_Z20 @1107) takes 100 sovereigns and gives 100 back when
                    // the purse is under 1000 royals: the take clamps at zero, so the party starts the
                    // chapter on exactly 1000. Dropping the take gave 296 + 1000 (TASK-524, live).
                    ApplyRemoveItem(remove);
                    break;
                case LearnSpellAction learn:
                    ApplyLearnSpell(learn, slots);
                    break;
                case PushDialogEntryAction:
                    break;
                default:
                    _logger.LogDebug("Chapter-setup dialog action {Action} not handled.", action.GetType().Name);
                    break;
            }
        }
    }
}
