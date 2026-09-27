namespace BakAgain.Tests.Editor.World.Hotspots {
    using System.Collections.Generic;
    using BakAgain.World.Hotspots;
    using GameData.Resources.GameState;
    using GameData.Resources.World;
    using NUnit.Framework;

    /// <summary>
    /// The hotspot dispatch pass — canassa <c>hotspotevt_disp_pending_events</c>. The halt on a zone
    /// transition and the re-checked availability gate are what a port loses by treating this as a
    /// simple foreach.
    /// </summary>
    public class HotspotDispatcherTests {
        private sealed class FakeHost : IHotspotHost {
            public readonly Dictionary<int, int> Globals = new();
            public readonly List<TileEventTrigger> FlagWrites = new();
            public bool ChanceComesUp = true;

            public int ReadGlobal(int key) => Globals.TryGetValue(key, out int v) ? v : 0;
            public void WriteGlobal(int key, int value) => Globals[key] = value;
            public int DoneFlagKey(int index) => 100000 + index;
            public int ScoutTriedFlagKey(int index) => 200000 + index;
            public int ScoutedFlagKey(int index) => 210000 + index;
            public bool SneaksPast;
            public readonly List<bool> AvoidanceScoutedArgs = new();

            public bool DueThisStep = true;

            public bool EncounterIsDueThisStep(TileEventTrigger t, int index) => DueThisStep;

            public bool RollEncounterAvoidance(TileEventTrigger t, bool scouted) {
                AvoidanceScoutedArgs.Add(scouted);

                return SneaksPast;
            }
            public void PlayDialog(uint dialogId, bool modal) => Dialogs.Add(dialogId);
            public uint BlockDialogId(TileEventTrigger t) => 0;
            public bool IsAmbush(TileEventTrigger t) => false;
            public bool EncounterFought(TileEventTrigger t) => false;
            public readonly List<TileEventTrigger> CombatStarted = new List<TileEventTrigger>();
            public readonly List<int> CombatStartedAt = new List<int>();
            public bool CombatAvailable;   // a bare fake names no roster, so there is nobody to fight

            // True unless a test says otherwise — the interface's own default for a host that
            // cannot tell, and what every pre-existing test here assumes.
            public bool RoomToFight = true;
            public readonly List<TileEventTrigger> GroundAsked = new List<TileEventTrigger>();
            public bool EnoughGroundToFight(TileEventTrigger t) {
                GroundAsked.Add(t);
                return RoomToFight;
            }

            public bool StartCombat(TileEventTrigger t, int hotspotIndex = -1) {
                if (!CombatAvailable) {
                    return false;
                }
                CombatStarted.Add(t);
                CombatStartedAt.Add(hotspotIndex);
                return true;
            }

            public bool RollScouting(TileEventTrigger t) => false;

            public bool ApplyChanceFlagWrite(TileEventTrigger t) {
                FlagWrites.Add(t);

                return ChanceComesUp;
            }

            public readonly List<uint> Dialogs = new();
            public uint Dialog = 1900047;
            public uint SpeakDialogId(TileEventTrigger t) => Dialog;
            public readonly List<int> Entered = new();
            public int TownScene = 11;
            public uint TownDialog;
            public int TownSceneNumber(TileEventTrigger t) => TownScene;
            public uint TownDialogId(TileEventTrigger t) => TownDialog;
            public void EnterLocation(int scene) => Entered.Add(scene);
            // The town gate now ASKS; the fake records the offer so a test can tell the two apart.
            public readonly System.Collections.Generic.List<int> TownOffers = new();
            public void OfferTownEntry(TileEventTrigger trigger, int scene, int hotspotIndex) =>
                TownOffers.Add(scene);
            public readonly List<TileEventTrigger> Approached = new();
            public void ApproachBeforeLocation(TileEventTrigger t) => Approached.Add(t);
            public bool ZoneCrossable = true;
            public readonly System.Collections.Generic.List<int> ZoneOffers = new();
            public bool ZoneCrossingIsOffered(TileEventTrigger t) => ZoneCrossable;
            public void OfferZoneCrossing(TileEventTrigger t, int index) => ZoneOffers.Add(index);
            public void MarkActedThisChunk(int index) => WriteGlobal(ScoutTriedFlagKey(index), 1);
        }

        private static TileEventTrigger Trigger(TileEventType type) => new() { Type = type };

        private static List<int> All(int count) {
            var pending = new List<int>();

            for (var i = 0; i < count; i++) {
                pending.Add(i);
            }

            return pending;
        }

        [Test]
        public void AnEncounterThePartyCannotSlipPastStartsTheFightAndStopsThePass() {
            // The gate's other arm. Until now this reported the hotspot unhandled: the fight had
            // nowhere to go, so walking into an ambush did nothing at all.
            var host = new FakeHost { CombatAvailable = true };
            TileEventTrigger comb = Trigger(TileEventType.Comb);

            HotspotDispatchResult result = new HotspotDispatcher(host)
                .Dispatch(new[] { comb }, new[] { 0 });

            Assert.AreEqual(1, result.Fired, "the encounter is handled, not unhandled");
            Assert.AreEqual(0, result.Unhandled);
            Assert.IsTrue(result.Halted, "a fight stops the rest of the pass");
            CollectionAssert.Contains(host.CombatStarted, comb);
        }

        [Test]
        public void OpeningAFightWritesNoPostEvent_AndHandsTheHotspotIndexOn() {
            // *** THE COMB ARM USED TO APPLY OnFire HERE. *** It is the fight's OUTCOME that writes
            // it, so opening one must write nothing — otherwise losing the fight sets the flag
            // winning it does. The index goes with the fight because the settle needs it and this
            // is the last place that knows it.
            var host = new FakeHost { CombatAvailable = true };
            var comb = new TileEventTrigger {
                Type = TileEventType.Comb, OnFire = new SetFlagEffect { Flag = 8100, Set = true },
            };
            // The Comb hotspot sits at position 2 so a threaded index cannot be confused with the
            // loop counter or with the -1 that means "none".
            var triggers = new List<TileEventTrigger> {
                Trigger(TileEventType.Enab), Trigger(TileEventType.Enab), comb,
            };

            new HotspotDispatcher(host).Dispatch(triggers, new[] { 2 });

            CollectionAssert.IsEmpty(host.FlagWrites);
            Assert.IsFalse(host.Globals.ContainsKey(8100));
            CollectionAssert.AreEqual(new[] { 2 }, host.CombatStartedAt);
        }

        [Test]
        public void NoRoomToFightRefusesTheEncounterWithoutOpeningOrConsumingIt() {
            // hotspotevt_type1_encounter_run returns outright: no arena, no dialog, nothing marked.
            // The encounter stays armed and will be offered again from somewhere there is room.
            var host = new FakeHost { CombatAvailable = true, RoomToFight = false };
            var comb = new TileEventTrigger {
                Type = TileEventType.Comb, OnFire = new SetFlagEffect { Flag = 8100, Set = true },
            };

            HotspotDispatchResult result =
                new HotspotDispatcher(host).Dispatch(new[] { comb }, new[] { 0 });

            Assert.AreEqual(1, result.Refused);
            Assert.AreEqual(0, result.Fired, "a refusal is not a firing");
            Assert.AreEqual(0, result.Unhandled, "nor a missing runtime — the rule said no");
            Assert.IsFalse(result.Halted, "and it does not stop the rest of the pass");
            CollectionAssert.IsEmpty(host.CombatStarted);
            Assert.IsFalse(host.Globals.ContainsKey(8100));
        }

        [Test]
        public void TheGroundCheckComesAFTERTheStealthGate() {
            // The original's order. A party that can slip past does so whether or not there is room
            // to fight — checking the ground first would make a cramped spot swallow the escape.
            var host = new FakeHost {
                CombatAvailable = true, RoomToFight = false, SneaksPast = true,
            };

            HotspotDispatchResult result = new HotspotDispatcher(host)
                .Dispatch(new[] { Trigger(TileEventType.Comb) }, new[] { 0 });

            Assert.AreEqual(1, result.Fired, "the party slipped past");
            Assert.AreEqual(0, result.Refused);
        }

        [Test]
        public void AnEncounterThatFieldsNobodyIsStillReportedUnhandled() {
            // A record naming an empty roster must not open an empty arena, and must not be counted
            // as handled either — otherwise a broken record looks like a fight that happened.
            var host = new FakeHost();   // names no roster

            HotspotDispatchResult result = new HotspotDispatcher(host)
                .Dispatch(new[] { Trigger(TileEventType.Comb) }, new[] { 0 });

            Assert.AreEqual(0, result.Fired);
            Assert.AreEqual(1, result.Unhandled);
            Assert.IsFalse(result.Halted);
        }

        [Test]
        public void EveryQueuedHotspotIsVisitedInOrder() {
            var host = new FakeHost();
            var triggers = new List<TileEventTrigger> {
                Trigger(TileEventType.Enab), Trigger(TileEventType.Disa), Trigger(TileEventType.Enab),
            };

            HotspotDispatchResult result = new HotspotDispatcher(host).Dispatch(triggers, All(3));

            Assert.AreEqual(3, result.Fired);
            CollectionAssert.AreEqual(triggers, host.FlagWrites);
        }

        [Test]
        public void AZoneTransitionStopsThePassAndTheRestNeverDispatch() {
            // The hotspots after it belong to a zone the party has just left.
            var host = new FakeHost();
            var triggers = new List<TileEventTrigger> {
                Trigger(TileEventType.Enab), Trigger(TileEventType.Zone), Trigger(TileEventType.Enab),
            };

            HotspotDispatchResult result = new HotspotDispatcher(host).Dispatch(triggers, All(3));

            Assert.IsTrue(result.Halted);
            Assert.AreEqual(1, host.FlagWrites.Count, "the hotspot after the zone must not dispatch");
        }

        [Test]
        public void KindsWithNoRuntimeAreCountedNotFaked() {
            // Town/Bkgr used to be in this list and now have a runtime, so they moved out. What is
            // left is genuinely unbuilt: combat, traps and the sound kind.
            var host = new FakeHost();
            var triggers = new List<TileEventTrigger> {
                Trigger(TileEventType.Comb), Trigger(TileEventType.Trap), Trigger(TileEventType.Soun),
            };

            HotspotDispatchResult result = new HotspotDispatcher(host).Dispatch(triggers, All(3));

            Assert.AreEqual(0, result.Fired);
            Assert.AreEqual(3, result.Unhandled);
            Assert.IsFalse(result.Halted, "only a zone transition halts unconditionally");
        }

        [Test]
        public void AHotspotAlreadyMarkedDoneDoesNotDispatch() {
            var host = new FakeHost();
            host.Globals[host.DoneFlagKey(0)] = 1;

            new HotspotDispatcher(host).Dispatch(new List<TileEventTrigger> { Trigger(TileEventType.Enab) }, All(1));

            CollectionAssert.IsEmpty(host.FlagWrites);
        }

        [Test]
        public void TheRequiresGateMustBeSetAndTheForbidsGateMustBeClear() {
            // Asymmetric, and swapping them leaves a hotspot that fires exactly when it should not.
            var host = new FakeHost();
            var required = new TileEventTrigger {
                Type = TileEventType.Enab, Requires = new FlagCondition { Flag = 700 },
            };
            var forbidden = new TileEventTrigger {
                Type = TileEventType.Enab, Forbids = new FlagCondition { Flag = 701 },
            };
            var triggers = new List<TileEventTrigger> { required, forbidden };

            new HotspotDispatcher(host).Dispatch(triggers, All(2));
            CollectionAssert.AreEqual(new[] { forbidden }, host.FlagWrites,
                "an unmet Requires blocks; an unset Forbids does not");

            host.FlagWrites.Clear();
            host.Globals[700] = 1;
            host.Globals[701] = 1;
            new HotspotDispatcher(host).Dispatch(triggers, All(2));
            CollectionAssert.AreEqual(new[] { required }, host.FlagWrites,
                "a met Requires allows; a set Forbids blocks");
        }

        [Test]
        public void AnEarlierHotspotCanCloseTheGateOnALaterOneInTheSamePass() {
            // Why availability is re-checked per hotspot rather than once for the batch.
            var host = new FakeHost();
            var opener = new TileEventTrigger {
                Type = TileEventType.Enab, OnFire = new SetFlagEffect { Flag = 900, Set = true },
            };
            var blocked = new TileEventTrigger {
                Type = TileEventType.Enab, Forbids = new FlagCondition { Flag = 900 },
            };

            new HotspotDispatcher(host).Dispatch(new List<TileEventTrigger> { opener, blocked }, All(2));

            CollectionAssert.AreEqual(new[] { opener }, host.FlagWrites);
        }

        [Test]
        public void AFailedChanceStillMarksTheHotspotDone() {
            // The post-event and the done flag sit OUTSIDE the chance branch for these two kinds.
            var host = new FakeHost { ChanceComesUp = false };
            var trigger = new TileEventTrigger {
                Type = TileEventType.Disa, FireOnce = 1,
                OnFire = new SetFlagEffect { Flag = 950, Set = true },
            };

            HotspotDispatchResult result =
                new HotspotDispatcher(host).Dispatch(new List<TileEventTrigger> { trigger }, All(1));

            Assert.AreEqual(0, result.Fired, "the chance did not come up");
            Assert.AreEqual(1, host.Globals[950], "but the record's own effect still applied");
            Assert.AreEqual(1, host.Globals[host.DoneFlagKey(0)]);
        }

        [Test]
        public void AHotspotThatIsNotFireOnceIsNotMarkedDone() {
            var host = new FakeHost();
            var trigger = new TileEventTrigger { Type = TileEventType.Enab, FireOnce = 0 };

            new HotspotDispatcher(host).Dispatch(new List<TileEventTrigger> { trigger }, All(1));

            Assert.AreEqual(0, host.ReadGlobal(host.DoneFlagKey(0)));
        }

        [Test]
        public void NothingQueuedIsNotAnError() {
            var host = new FakeHost();

            HotspotDispatchResult result = new HotspotDispatcher(host)
                .Dispatch(new List<TileEventTrigger>(), new List<int>());

            Assert.AreEqual(0, result.Fired);
            Assert.IsFalse(result.Halted);

            Assert.DoesNotThrow(() => new HotspotDispatcher(host).Dispatch(null, null));
        }

        [Test]
        public void AnIndexOutsideTheTableIsSkippedRatherThanThrowing() {
            var host = new FakeHost();

            Assert.DoesNotThrow(() => new HotspotDispatcher(host)
                .Dispatch(new List<TileEventTrigger> { Trigger(TileEventType.Enab) }, new List<int> { 5, -1 }));
            CollectionAssert.IsEmpty(host.FlagWrites);
        }

        [Test]
        public void ADialHotspotSpeaksItsRecordsDialog() {
            var host = new FakeHost();

            HotspotDispatchResult result = new HotspotDispatcher(host)
                .Dispatch(new List<TileEventTrigger> { Trigger(TileEventType.Dial) }, All(1));

            CollectionAssert.AreEqual(new uint[] { 1900047 }, host.Dialogs);
        }

        [Test]
        public void ADialRecordNamingNoDialogStillCountsAsHavingActed() {
            // The bookkeeping is not conditional on there being something to say.
            var host = new FakeHost { Dialog = 0 };
            var trigger = new TileEventTrigger { Type = TileEventType.Dial, FireOnce = 1 };

            HotspotDispatchResult result =
                new HotspotDispatcher(host).Dispatch(new List<TileEventTrigger> { trigger }, All(1));

            CollectionAssert.IsEmpty(host.Dialogs);
            Assert.AreEqual(1, result.Fired);
            Assert.AreEqual(1, host.Globals[host.DoneFlagKey(0)]);
        }

        [Test]
        public void ANormalDialSilencesItselfForTheRestOfTheChunk() {
            var host = new FakeHost();
            var triggers = new List<TileEventTrigger> { Trigger(TileEventType.Dial) };

            new HotspotDispatcher(host).Dispatch(triggers, All(1));
            new HotspotDispatcher(host).Dispatch(triggers, All(1));

            Assert.AreEqual(1, host.Dialogs.Count, "the per-chunk debounce stops the second");
        }

        [Test]
        public void ARepeatableDialKeepsTalkingAndIsNeverMarkedDone() {
            // Repeatable skips BOTH the done flag and the debounce — that is the whole mechanism.
            var host = new FakeHost();
            var trigger = new TileEventTrigger {
                Type = TileEventType.Dial, Repeatable = 1, FireOnce = 1,
            };
            var triggers = new List<TileEventTrigger> { trigger };

            new HotspotDispatcher(host).Dispatch(triggers, All(1));
            new HotspotDispatcher(host).Dispatch(triggers, All(1));

            Assert.AreEqual(2, host.Dialogs.Count);
            Assert.AreEqual(0, host.ReadGlobal(host.DoneFlagKey(0)),
                "FireOnce does not apply to a repeatable record");
        }

        [Test]
        public void TheChanceComparisonIsInclusive() {
            // Same rule as the scouting roll: chance 0 still fires on a roll of 0.
            Assert.IsTrue(HotspotRules.ChanceFires(0, 0));
            Assert.IsFalse(HotspotRules.ChanceFires(1, 0));
            Assert.IsTrue(HotspotRules.ChanceFires(100, 100));
        }

        /// <summary>
        /// A town gate OFFERS its scene; it does not enter one.
        /// </summary>
        /// <remarks>
        /// <b>This test used to assert the opposite</b>, and that was the bug: the original writes
        /// the entry's pending flag as <c>dialog_play_record(record.dialogId, 0) == 0</c>
        /// (HOTSPOT.C:618), so the scene is entered only on the Yes. Measured at LaMut's gate on
        /// 2026-09-13 — the original asks "Do you think we should go in for supplies?" and a No
        /// leaves the party on the road, while the port walked in without asking. TASK-470.
        /// </remarks>
        [Test]
        public void ATownHotspotOFFERSTheSceneItsRecordNames_ItDoesNotEnterIt() {
            var host = new FakeHost {TownScene = 11, TownDialog = 0};
            var triggers = new List<TileEventTrigger> {Trigger(TileEventType.Town)};

            HotspotDispatchResult result = new HotspotDispatcher(host).Dispatch(triggers, All(1));

            CollectionAssert.AreEqual(new[] {11}, host.TownOffers);
            CollectionAssert.IsEmpty(host.Entered);
            Assert.AreEqual(1, result.Fired);
        }

        /// <summary>
        /// <c>Bkgr</c> asks too — it is the same handler and the same gate.
        /// </summary>
        /// <remarks>
        /// <c>hotspotevt_dialog_popup_run</c> (kind 0) and <c>hotspotevt_show_record_message</c>
        /// (kind 6) are the same six lines against a different DEF family. The two shipped Bkgr
        /// records whose dialog offers no choice answer 0 — <c>nResult</c> opens at 0
        /// (DIALOG.C:837) — and 0 is the yes, so nothing that used to enter stops entering.
        /// </remarks>
        [Test]
        public void ABkgrHotspotAsksTheSameWayATownGateDoes() {
            var host = new FakeHost {TownScene = 4};
            var triggers = new List<TileEventTrigger> {Trigger(TileEventType.Bkgr)};

            new HotspotDispatcher(host).Dispatch(triggers, All(1));

            CollectionAssert.AreEqual(new[] {4}, host.TownOffers);
            CollectionAssert.IsEmpty(host.Entered);
        }

        [Test]
        public void ATownRecordNamingNoSceneEntersNothing() {
            var host = new FakeHost {TownScene = 0};
            var triggers = new List<TileEventTrigger> {Trigger(TileEventType.Town)};

            HotspotDispatchResult result = new HotspotDispatcher(host).Dispatch(triggers, All(1));

            CollectionAssert.IsEmpty(host.Entered);
            CollectionAssert.IsEmpty(host.TownOffers);
            Assert.AreEqual(0, result.Fired);
            Assert.AreEqual(1, result.Unhandled);
        }

        /// <summary>Both kinds hand the scene, the trigger and its index to the asking arm.</summary>
        [Test]
        public void TheOfferCarriesWhatTheAskingArmNeeds() {
            var host = new FakeHost {TownScene = 7, TownDialog = 1234};
            var triggers = new List<TileEventTrigger> {Trigger(TileEventType.Town)};

            new HotspotDispatcher(host).Dispatch(triggers, All(1));

            CollectionAssert.AreEqual(new[] {7}, host.TownOffers);
            // Nothing downstream of the answer has happened yet: no dialog played through the
            // fire-and-forget path, no approach, no scene.
            CollectionAssert.IsEmpty(host.Dialogs);
            CollectionAssert.IsEmpty(host.Approached);
            CollectionAssert.IsEmpty(host.Entered);
        }

        [Test]
        public void EnteringALocationEndsThePass() {
            // Deliberate deviation: the original runs the scene inline and carries on afterwards. We
            // cannot block the pass, so the rest of it would otherwise fire BEFORE the location
            // instead of after. The party is still on the tile, so what is dropped is re-evaluated.
            var host = new FakeHost {TownScene = 11};
            var triggers = new List<TileEventTrigger> {
                Trigger(TileEventType.Town), Trigger(TileEventType.Enab),
            };

            HotspotDispatchResult result = new HotspotDispatcher(host).Dispatch(triggers, All(2));

            Assert.IsTrue(result.Halted);
            CollectionAssert.IsEmpty(host.FlagWrites, "the Enab after the town entry must not have fired");
        }

        [Test]
        public void TheDispatcherOffersAndOwesNothingElse() {
            // Every shipped town record asks for the approach, and the order matters: the party is
            // put where the record wants them and left facing its heading, which is also where they
            // stand when the location closes.
            //
            // *** THE ORDER MOVED, NOT THE RULE. *** Both kinds now go through OfferTownEntry, and
            // the approach is sequenced there — after the answer, because none of it happens on a
            // no (TASK-470). What the dispatcher still owes is the offer itself.
            var host = new FakeHost {TownScene = 11};
            var trigger = Trigger(TileEventType.Town);

            new HotspotDispatcher(host).Dispatch(new List<TileEventTrigger> {trigger}, All(1));

            CollectionAssert.AreEqual(new[] {11}, host.TownOffers);
            CollectionAssert.IsEmpty(host.Approached);
        }

        [Test]
        public void ARecordNamingNoSceneDoesNotMoveTheParty() {
            var host = new FakeHost {TownScene = 0};

            new HotspotDispatcher(host).Dispatch(
                new List<TileEventTrigger> {Trigger(TileEventType.Town)}, All(1));

            CollectionAssert.IsEmpty(host.Approached);
        }

        // ---- the zone boundary ---------------------------------------------------------------

        [Test]
        public void AZoneBoundaryOffersTheCrossing() {
            var host = new FakeHost();

            HotspotDispatchResult result = new HotspotDispatcher(host).Dispatch(
                new List<TileEventTrigger> {Trigger(TileEventType.Zone)}, All(1));

            CollectionAssert.AreEqual(new[] {0}, host.ZoneOffers);
            Assert.That(result.Halted, Is.True);
        }

        [Test]
        public void AnInertBoundaryOffersNothingButStillHalts() {
            // A record naming no confirm prompt never crosses — and the halt is NOT conditional on
            // the crossing, because anything queued behind it belongs to a zone being left.
            var host = new FakeHost {ZoneCrossable = false};

            HotspotDispatchResult result = new HotspotDispatcher(host).Dispatch(
                new List<TileEventTrigger> {Trigger(TileEventType.Zone)}, All(1));

            CollectionAssert.IsEmpty(host.ZoneOffers);
            Assert.That(result.Halted, Is.True);
        }

        [Test]
        public void HotspotsQueuedBehindABoundaryNeverDispatch() {
            // Table order is load-bearing: the ones after a Zone trigger belong to the old zone.
            var host = new FakeHost();

            new HotspotDispatcher(host).Dispatch(
                new List<TileEventTrigger> {Trigger(TileEventType.Zone), Trigger(TileEventType.Dial)},
                All(2));

            CollectionAssert.AreEqual(new[] {0}, host.ZoneOffers);
            CollectionAssert.IsEmpty(host.Dialogs);
        }

        [Test]
        public void UnhandledKindsAreNamedNotJustCounted() {
            // The count alone says something is unwired without saying which task it belongs to.
            var host = new FakeHost();
            var triggers = new List<TileEventTrigger> {
                Trigger(TileEventType.Comb), Trigger(TileEventType.Trap), Trigger(TileEventType.Soun),
            };

            HotspotDispatchResult result = new HotspotDispatcher(host).Dispatch(triggers, All(3));

            CollectionAssert.AreEqual(
                new[] { TileEventType.Comb, TileEventType.Trap, TileEventType.Soun },
                result.UnhandledKinds);
        }

        [Test]
        public void ARepeatedKindIsNamedOnce() {
            // The number is reported separately, so repeating the kind adds nothing to the message.
            var host = new FakeHost();
            var triggers = new List<TileEventTrigger> {
                Trigger(TileEventType.Comb), Trigger(TileEventType.Comb), Trigger(TileEventType.Comb),
            };

            HotspotDispatchResult result = new HotspotDispatcher(host).Dispatch(triggers, All(3));

            Assert.AreEqual(3, result.Unhandled);
            CollectionAssert.AreEqual(new[] { TileEventType.Comb }, result.UnhandledKinds);
        }

        [Test]
        public void APassThatHandlesEverythingNamesNoKinds() {
            var host = new FakeHost();
            var triggers = new List<TileEventTrigger> { Trigger(TileEventType.Enab) };

            HotspotDispatchResult result = new HotspotDispatcher(host).Dispatch(triggers, All(1));

            Assert.AreEqual(0, result.Unhandled);
            Assert.IsNotNull(result.UnhandledKinds, "never null — an empty pass still formats");
            Assert.IsEmpty(result.UnhandledKinds);
        }

        [Test]
        public void AnEmptyDispatchStillHasAKindList() =>
            // The early return builds its own result; it must not hand back a null list either.
            Assert.IsEmpty(new HotspotDispatcher(new FakeHost()).Dispatch(null, null).UnhandledKinds);

        [Test]
        public void Comb_WhenThePartySlipsPast_TheEncounterIsHandledWithoutAnyArena() {
            // *** The half that works today. *** Walking away from an ambush needs no combat at all,
            // so this is real behaviour rather than a stub: the trigger fires and is consumed.
            var host = new FakeHost { SneaksPast = true };
            var dispatcher = new HotspotDispatcher(host);

            HotspotDispatchResult result =
                dispatcher.Dispatch(new List<TileEventTrigger> { Trigger(TileEventType.Comb) }, All(1));

            Assert.AreEqual(1, result.Fired);
            Assert.AreEqual(0, result.Unhandled);
        }

        [Test]
        public void Comb_PassesTheSCOUTEDFlagToTheRoll_NotTheScoutTRIEDOne() {
            // The two flags sit ten apart and mean different things: 5200 says a roll happened, 5210
            // says it succeeded. Only the second earns the sneak-past attempt, so reading the wrong
            // one would let a party that FAILED to spot an ambush walk around it anyway.
            var host = new FakeHost { SneaksPast = false };
            // Tried but not spotted: the activate pass always sets scout-tried before rolling, so
            // this is the state EVERY queued ambush arrives in. It must not block the roll.
            host.Globals[host.ScoutTriedFlagKey(0)] = 1;
            var dispatcher = new HotspotDispatcher(host);

            dispatcher.Dispatch(new List<TileEventTrigger> { Trigger(TileEventType.Comb) }, All(1));
            CollectionAssert.AreEqual(new[] { false }, host.AvoidanceScoutedArgs);

            host.AvoidanceScoutedArgs.Clear();
            host.Globals[host.ScoutedFlagKey(0)] = 1;     // now actually spotted
            dispatcher.Dispatch(new List<TileEventTrigger> { Trigger(TileEventType.Comb) }, All(1));
            CollectionAssert.AreEqual(new[] { true }, host.AvoidanceScoutedArgs);
        }
}
}
