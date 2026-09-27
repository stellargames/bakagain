namespace BakAgain.Core.Services {
    using GameData.Resources.Data;
    using GameData.Resources.Dialog;
    using System;

    /// <summary>
    /// Projects <see cref="GameSession"/> into the <see cref="DialogSlotContext"/> the dialog
    /// text-variable slots are filled from. One place, because two callers need the same projection:
    /// <see cref="DialogExecutor"/> (which seeds the table and applies ops as it walks) and
    /// <c>DialogManager</c> (which seeds one for an entry handed to it directly, with no walk).
    ///
    /// <para>Built fresh per dialog play, matching the engine's own lifetime —
    /// <c>dialog_play_record</c> re-seeds the slot table on every play, so the companions a
    /// narration line names are genuinely re-rolled each time it is shown.</para>
    /// </summary>
    public static class DialogSlotContextFactory {
        public static DialogSlotContext FromSession(GameSession session) {
            if (session == null) {
                return new DialogSlotContext();
            }
            int chapterSpeaker =
                session.GetGlobalValue(DialogSlotPopulator.ChapterSpeakerGlobalKey) ?? 0;
            // The actor the dialog is "about". Modelled now (TEMP.GAM +0x0596), but a save that
            // predates it — or a session hydrated before anything set it — still degrades to the
            // chapter speaker rather than to member 0 by accident.
            int primaryActor =
                session.GetGlobalValue(DialogSlotPopulator.CurrentActorGlobalKey) ?? chapterSpeaker;

            byte[] roster = session.ActivePartyIndices ?? Array.Empty<byte>();
            var rosterIds = new int[roster.Length];
            for (int i = 0; i < roster.Length; i++) {
                rosterIds[i] = roster[i];
            }

            // *** THE ACTOR REGISTER NEVER NAMES SOMEONE WHO IS NOT HERE. *** V102CD's
            // dialog_combatant_name_table_init (DIALOG.C:793-800) runs at the start of every play:
            // if nEvtArgActor0 is not in the active party it becomes global 30005, the chapter's
            // speaker, and the global itself is rewritten (TASK-493).
            if (rosterIds.Length > 0 && Array.IndexOf(rosterIds, primaryActor) < 0) {
                primaryActor = chapterSpeaker;
                session.EventActor = chapterSpeaker;
            }

            return new DialogSlotContext {
                // Roster order and member IDS, not portrait positions — the random picker's
                // constraints ({2,3,5}, {0,1,4}, …) are expressed in member ids.
                PartyRoster = rosterIds,
                ActorNames = session.PartyActorNames ?? Array.Empty<string>(),
                ChapterSpeakerId = chapterSpeaker,
                CurrentActorId = primaryActor,
                PrimaryActorId = primaryActor,
                // No 300xx accessor exists for these — the engine reads them straight out of dseg,
                // so they come off the session's parsed misc state rather than the global map.
                SecondaryActorId = session.DialogSecondaryActorId,
                TertiaryActorId = session.DialogTertiaryActorId,
                CreatureType = session.DialogCreatureType,
                KeyObjectId = session.DialogKeyObjectId,
                PartyMoneyInRoyals = session.PartyGold,
                // Which word the person behind the counter gets. Off the session rather than a
                // global: the engine keeps it in dseg for the lifetime of the shop screen, and
                // that screen is what writes it.
                IsRestEncounter = session.OpenShopRunsAnInn,
                QuotedAmount = session.GetGlobalValue(DialogSlotPopulator.QuotedAmountGlobalKey) ?? 0,
                Global30015 = session.GetGlobalValue(30015) ?? 0,
                Global30018 = session.GetGlobalValue(30018) ?? 0,
                CreatureNameOf = session.CreatureNameOf,
                ObjectNameOf = id => session.ObjectInfo?.GetById(id)?.Name ?? "",
                // Kind 27's second write — DIAL_Z24's "@1", the value in "My @0 rating is limited
                // to a mere @1". Resolved against the PRIMARY actor, captured here rather than read
                // per-invoke so the value cannot drift mid-play if the global moves: the engine
                // reads `actors_Locklear[nEvtArgActor0]` (0x48ce9), the same actor global this
                // context's PrimaryActorId comes from.
                AttributeValueOf = index => AttributeMaximumFor(session, primaryActor, index),
                Random = bound => bound <= 0 ? 0 : UnityEngine.Random.Range(0, bound),
            };
        }

        /// <summary>
        /// The primary actor's MAXIMUM value for attribute <paramref name="index"/>, or 0 when the
        /// party has no such member or the index is outside the 16 the executable knows.
        ///
        /// <para>Maximum, not current: <c>PopulateDialogSlotText</c> case 27 (0x48cc5) pushes the
        /// <c>Maximum</c> enumerator as <c>GetAttributeFromActor</c>'s <c>whichValue</c>. The
        /// index → property mapping itself lives in <see cref="ActorAttributeValues"/> (GameData),
        /// where it is unit-tested and where the comment explaining why the order is the
        /// executable's rather than <c>ActorAttribute</c>'s belongs.</para>
        ///
        /// <para><c>PartyActors</c> is indexed by member id, which is what the actor globals hold —
        /// the same indexing <c>InventoryMenu</c> uses against <c>ActivePartyIndices</c>. It is NOT
        /// a roster position, so no translation through <c>ActivePartyIndices</c> is wanted here.</para>
        /// </summary>
        private static int AttributeMaximumFor(GameSession session, int actorId, int index) {
            SaveGameActorData[] actors = session.PartyActors;
            if (actors == null || actorId < 0 || actorId >= actors.Length) {
                return 0;
            }
            return ActorAttributeValues.MaximumOf(actors[actorId], index);
        }
    }
}
