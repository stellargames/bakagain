namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using GameData;
    using BakAgain.Core.Services;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using GameData.Resources.Dialog.Actions;
    using NUnit.Framework;

    /// <summary>
    /// Dialog sub-actions (<c>wOp</c> 7) — the money three.
    /// </summary>
    /// <remarks>
    /// <b>Every one of these used to be dropped on the floor.</b> `SubAction` parsed and nothing
    /// dispatched it, so all 39 shipped instances did nothing. See TASK-304 for the rest, and for
    /// the three subtypes whose enum remarks describe the WRONG behaviour.
    ///
    /// <para>Case 7 is what pins the variable mapping the other two depend on: the original's body is
    /// <c>lEvtArgValue += Field2</c>, which is this enum's <c>IncrementGlobal30015</c> — so
    /// <c>lEvtArgValue</c> IS global 30015, and <c>lEvtArgGoldCost</c> is 30014.</para>
    /// </remarks>
    [TestFixture]
    public class DialogSubActionTests {
        private const int QuotedPrice = 30014;
        private const int EventValue = 30015;

        private GameSession _session;
        private DialogExecutor _executor;

        [SetUp]
        public void SetUp() {
            _session = new GameSession();
            _executor = new DialogExecutor(
                new NullLogger<DialogExecutor>(), _session, new GameClock(_session));
        }

        /// <summary>A full stat table with one Barding value set — what the engine needs to move it.</summary>
        private static GameData.Resources.Character.ActorStat[] StatsWithBarding(byte value) {
            var stats = new GameData.Resources.Character.ActorStat[
                GameData.Resources.Character.StatEngine.TableSize];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new GameData.Resources.Character.ActorStat { Base = 0, Max = 100 };
            }
            stats[(int)ActorAttribute.Barding].Base = value;
            return stats;
        }

        private void Run(SubActionType type, ushort field2 = 0) =>
            _executor.ApplySubAction(new SubAction { SubActionType = type, Field2 = field2 });

        [Test]
        public void PayPartyMoney_ChargesTheQuotedPrice() {
            _session.PartyGold = 500;
            _session.SetGlobalValue(QuotedPrice, 137);

            Run(SubActionType.PayPartyMoney);

            Assert.AreEqual(363, _session.PartyGold);
        }

        [Test]
        public void PayPartyMoney_ClampsAtZeroRatherThanGoingIntoDebt() {
            // The original's own ternary: gold >= cost ? gold - cost : 0.
            _session.PartyGold = 20;
            _session.SetGlobalValue(QuotedPrice, 137);

            Run(SubActionType.PayPartyMoney);

            Assert.AreEqual(0, _session.PartyGold);
        }

        [Test]
        public void RewardPartyMoney_PaysOutTheWorkingValue() {
            _session.PartyGold = 100;
            _session.SetGlobalValue(EventValue, 250);

            Run(SubActionType.RewardPartyMoney);

            Assert.AreEqual(350, _session.PartyGold);
        }

        [Test]
        public void IncrementGlobal30015_AddsToTheWorkingValueRatherThanReplacingIt() {
            // The distinction that names the subtype: `+=`, not `=`. Replacing it would make a
            // sequence of these keep only the last.
            _session.SetGlobalValue(EventValue, 40);

            Run(SubActionType.IncrementGlobal30015, field2: 25);

            Assert.AreEqual(65, _session.GetGlobalValue(EventValue));
        }

        [Test]
        public void IncrementGlobal30015_Accumulates_WhichIsWhyItIsAnIncrement() {
            _session.SetGlobalValue(EventValue, 0);

            Run(SubActionType.IncrementGlobal30015, field2: 10);
            Run(SubActionType.IncrementGlobal30015, field2: 10);
            Run(SubActionType.IncrementGlobal30015, field2: 5);

            Assert.AreEqual(25, _session.GetGlobalValue(EventValue));
        }

        [Test]
        public void AnUnimplementedSubtypeChangesNothing_AndIsNotSilent() {
            // *** The point of the default arm. *** An unbuilt subtype is not a no-op: whatever it
            // was meant to do does not happen. A warning is what separates "not built" from "quietly
            // broken" — the shape that cost this project four dead features in a day.
            //
            // *** ALL SEVENTEEN ARE BUILT NOW, so this arm is unreachable from the enum. *** It
            // stays because the dispatch is data-driven — a mod, or a future format change, can put
            // a value here that no member names, and silence would be the wrong answer to that.
            // The cast is the only way to reach it, which is itself the point.
            _session.PartyGold = 100;

            _executor.ApplySubAction(new SubAction { SubActionType = (SubActionType)99 });

            Assert.AreEqual(100, _session.PartyGold, "an unknown subtype must not half-apply");
        }

        [Test]
        public void ResolvingAnEncounterAsksTheWORLDToSettleIt_notJustTheFlag() {
            // *** THREE OPERATIONS, AND THE FLAG IS ONLY THE FIRST. *** This test asserted that the
            // executor wrote ENCOUNTER_FOUGHT(id) itself, which was one third of
            // rgnenc_mark_defended: the original also stops every roaming actor in the record and
            // kills every still-living actor on its roster. A flag-only resolve leaves the monsters
            // standing on the map after the conversation that settled them, so the whole operation
            // is the world's and the dialog asks for it. Shipped ids: 343 and 645.
            long? asked = null;
            _executor.ResolveEncounters = id => { asked = id; return 1; };

            Run(SubActionType.CancelCombatEncounter2, field2: 343);

            Assert.AreEqual(343L, asked);
        }

        [Test]
        public void ReArmingClearsTheFoughtFlagThroughTheWorld_notHere() {
            // Subtype 3 is the OPPOSITE of 4 despite its name. Healing the roster and clearing the
            // flags are one operation — half of it arms an encounter that fields the wounded and the
            // dead — so the dialog hands the whole thing over rather than clearing a flag itself.
            long? asked = null;
            _executor.RearmEncounter = id => asked = id;

            Run(SubActionType.CancelCombatEncounter, field2: 151);

            Assert.AreEqual(151L, asked);
        }

        [Test]
        public void RequestingAHotspotPassIsDEFERRED_NotRunInline() {
            // *** The deferral is the design, not an implementation detail. *** A hotspot pass can
            // raise a dialog; running one from inside a resolving dialog re-enters it. The original
            // sets a flag its world loop consumes on an iteration where nothing moved, and this
            // raises the same request for the movement driver to pick up.
            Assert.IsFalse(_session.HotspotPassRequested);

            Run(SubActionType.SetTutorialFlag);

            Assert.IsTrue(_session.HotspotPassRequested,
                "the pass must be REQUESTED, and by something else than this call");
        }
    

        [Test]
        public void ExtinguishTorches_PutsOutTheCARRIEDLightAndLeavesTheSpellsBurning() {
            // The pool key is the light SOURCE, not an object id: 0 is the lit item in the party's
            // hands, 1-3 are dragon's breath, candle glow and stardusk. Reading the subtype's name
            // as "all light" would blow out the spell that lights caves.
            var clock = new GameClock(_session);
            var executor = new DialogExecutor(new NullLogger<DialogExecutor>(), _session, clock);
            int carried = (int)GameData.Resources.World.LightSourceDecay.Source.Item;
            int candle = (int)GameData.Resources.World.LightSourceDecay.Source.CandleGlow;
            clock.ScheduleTimer(TimerType.Light, carried, 5000);
            clock.ScheduleTimer(TimerType.Light, candle, 5000);

            executor.ApplySubAction(new SubAction { SubActionType = SubActionType.ExtinguishTorches });

            Assert.IsFalse(clock.HasTimer(TimerType.Light, carried),
                "the carried light is still burning");
            Assert.AreEqual(5000, clock.RemainingTicks(TimerType.Light, candle),
                "the candle-glow timer lost time it should not have");
        }

        [Test]
        public void ExpireTimers_TICKSTheEntryOutRatherThanJustZeroingIt() {
            // Zeroing alone leaves a dead entry in the pool holding its effect on until the clock
            // next moves. The original's case 13 zeroes AND calls timerpool_tick(0); this is that
            // second half, and it is what actually removes the entry.
            var clock = new GameClock(_session);
            clock.ScheduleTimer(TimerType.Light, 0, 5000);

            Assert.AreEqual(1, clock.ExpireTimers(TimerType.Light, 0));
            Assert.IsFalse(clock.HasTimer(TimerType.Light, 0));
            Assert.AreEqual(0, clock.ExpireTimers(TimerType.Light, 0), "nothing left to expire");
        }

        [Test]
        public void RepairArmour_MultipliesTheQuotedPriceByWhatWasMended() {
            // *** THE QUOTED PRICE IS A UNIT PRICE. *** The original sets lEvtArgValue to the count
            // and multiplies lEvtArgGoldCost by it before PayPartyMoney (a separate authored
            // sub-action) deducts the total. With no party there is nothing to mend, so the count
            // is zero and the price multiplies to zero — the original's own behaviour, not a guard,
            // and the reason a party with pristine armour is charged nothing rather than a unit.
            _session.SetGlobalValue(QuotedPrice, 45);

            Run(SubActionType.CountArmorState);

            Assert.AreEqual(0, _session.GetGlobalValue(EventValue));
            Assert.AreEqual(0, _session.GetGlobalValue(QuotedPrice));
        }

        [Test]
        public void CapReward_RAISESTheHouseFundAndNeverLowersIt() {
            // *** max, NOT min. *** The body is `if (Field2 > X) X = Field2;` — a floor the dialog
            // puts under the fund. The enum described it as a min on the reward money, and both
            // halves of that were wrong: implementing it would have CAPPED a tavern's takings
            // instead of guaranteeing them.
            _session.EstablishmentFund = 40;

            Run(SubActionType.CapReward, field2: 25);
            Assert.AreEqual(40, _session.EstablishmentFund, "a smaller floor must change nothing");

            Run(SubActionType.CapReward, field2: 90);
            Assert.AreEqual(90, _session.EstablishmentFund);
        }

        [Test]
        public void TheHouseFundStartsAtZeroAndIsNotSaveState() {
            // It is the original's dwPopup_retry_state: loaded from the establishment before its
            // dialog and cleared after, never carried between conversations. Where it actually
            // lives is SaveGameContainerShopData.BardingReward on the container.
            Assert.AreEqual(0, new GameSession().EstablishmentFund);
            Assert.AreEqual(0xfa, GameSession.EstablishmentFundMax);
        }

        [Test]
        public void CancelCombatEncounter_ReArmsTheIdItNames() {
            // The name lies: this one RE-ARMS. It is the opposite of CancelCombatEncounter2, which
            // resolves — the two write 0 and 1 to the same flag.
            long? asked = null;
            _executor.RearmEncounter = id => asked = id;

            Run(SubActionType.CancelCombatEncounter, field2: 151);

            Assert.AreEqual(151L, asked);
        }

        [Test]
        public void WithNoWorldRunningTheReArmIsRefusedRatherThanHalfDone() {
            // *** Healing and clearing the flags are ONE operation. *** Clearing without healing
            // arms an encounter that fields the wounded and the dead, so a dialog fired with no
            // world behind it must do NEITHER — which is why the whole thing is one callback and
            // not a flag write here plus a heal somewhere else.
            _executor.RearmEncounter = null;

            Assert.DoesNotThrow(() => Run(SubActionType.CancelCombatEncounter, field2: 151));
            Assert.IsNull(_session.GetGlobalValue(
                BakAgain.World.Hotspots.HotspotRules.EncounterFoughtKey(151)),
                "no flag may be touched when the re-arm could not run");
        }

        [Test]
        public void TheTwoEncounterSubtypesAreOPPOSITES_notSiblings() {
            long? rearmed = null;
            long? resolved = null;
            _executor.RearmEncounter = id => rearmed = id;
            _executor.ResolveEncounters = id => { resolved = id; return 1; };

            Run(SubActionType.CancelCombatEncounter2, field2: 343);
            Assert.AreEqual(343L, resolved);
            Assert.IsNull(rearmed, "subtype 4 must not re-arm anything");

            resolved = null;
            Run(SubActionType.CancelCombatEncounter, field2: 343);
            Assert.AreEqual(343L, rearmed);
            Assert.IsNull(resolved, "subtype 3 must not resolve anything");
        }

        [Test]
        public void ResolvingPassesTheIdSTRAIGHTThrough_soZEROReachesTheAllFilter() {
            // *** Zero means EVERY loaded record, not record zero. *** Two of the five shipped
            // instances pass 0, and the original skips a record only when
            // `filter != 0 && filter != enc_id`. Swallowing the 0 here — as a "no id given" guard
            // would — resolves nothing where the game resolves them all.
            long? seen = -1;
            _executor.ResolveEncounters = id => { seen = id; return 3; };

            Run(SubActionType.CancelCombatEncounter2, field2: 0);

            Assert.AreEqual(GameData.Resources.World.EncounterDefeat.AllEncounters, seen);
        }

        [Test]
        public void WithNoWorldRunningNothingIsResolved() {
            // Resolving is three operations — the flag, the roamers, the roster. Writing just the
            // flag here would leave the monsters standing where a conversation had settled them,
            // which is what this did until the callback existed.
            _executor.ResolveEncounters = null;

            Assert.DoesNotThrow(() => Run(SubActionType.CancelCombatEncounter2, field2: 343));
            Assert.IsNull(_session.GetGlobalValue(
                BakAgain.World.Hotspots.HotspotRules.EncounterFoughtKey(343)));
        }

        [Test]
        public void BoostingASkillUsesTheEVENTActor_notTheSpeaker() {
            // *** nEvtArgActor0, not the slot table. *** The member is named "PrimarySpeaker" and
            // that is not what the original reads: the actor comes from whatever last picked one out
            // of the party — for barding, the performer stat_party_find_extreme chose. Resolving a
            // speaker instead boosts a different character whenever the two differ.
            _session.SetActorStatsForTest(3, StatsWithBarding(40));
            _session.EventActor = 3;
            _session.SetGlobalValue(EventValue, (int)ActorAttribute.Barding);

            Run(SubActionType.BoostPrimarySpeakerAttribute512);

            Assert.AreEqual(42, _session.StatsOf(3)[(int)ActorAttribute.Barding].Base,
                "512 is 2.0 in 256ths — two points, not 512");
        }

        [Test]
        public void WithNobodySelectedTheBoostIsSkipped() {
            // The global is a working register; a dialog that reaches this without anything having
            // selected an actor must not pick one for it.
            _session.SetActorStatsForTest(3, StatsWithBarding(40));
            _session.EventActor = -1;
            _session.SetGlobalValue(EventValue, (int)ActorAttribute.Barding);

            Assert.DoesNotThrow(() => Run(SubActionType.BoostPrimarySpeakerAttribute512));
            Assert.AreEqual(40, _session.StatsOf(3)[(int)ActorAttribute.Barding].Base);
        }

        /// <summary>A dialog raising a party-wide skill goes through the real engine.</summary>
        /// <remarks>
        /// `Type` is the change MODE, not a kind of attribute — the high byte of the op's first
        /// word, straight into stat_combatant_modify's mode argument. All 80 shipped instances
        /// carry 0, so hardcoding Absolute would look right until a mod authored anything else.
        /// </remarks>
        [Test]
        public void ChangeAttribute_RaisesTheWholePartyThroughStatEngine() {
            _session.SetActiveParty(1, new byte[] { 3 });
            _session.SetActorStatsForTest(3, StatsWithBarding(40));

            _executor.ApplyEntryGameplayActionsForTest(new GameData.Resources.Dialog.DialogEntry {
                Actions = { new ChangeAttributeAction {
                    Target = 1, Attribute = ActorAttribute.Barding,
                    // 2560 = ten points: the amount is in 256ths, like every delta that
                    // reaches stat_combatant_modify. Every shipped value is a multiple of 256.
                    MinimumAmount = 2560, MaximumAmount = 2560, Type = 0,
                } },
            });

            Assert.AreEqual(50, _session.StatsOf(3)[(int)ActorAttribute.Barding].Base);
        }

        /// <summary>
        /// The amount is a RANGE, rolled per use — `Min + RND(Max - Min)`.
        /// </summary>
        /// <remarks>
        /// 23 of the 80 shipped instances have Max != Min. Taking Min alone would pass a test
        /// written against one value and quietly remove the variance the author asked for, so this
        /// asserts the BAND rather than a number.
        /// </remarks>
        [Test]
        public void ChangeAttribute_RollsWithinTheAuthoredBand() {
            _session.SetActiveParty(1, new byte[] { 3 });
            var seen = new System.Collections.Generic.HashSet<int>();
            for (var i = 0; i < 60; i++) {
                _session.SetActorStatsForTest(3, StatsWithBarding(0));
                _executor.ApplyEntryGameplayActionsForTest(new GameData.Resources.Dialog.DialogEntry {
                    Actions = { new ChangeAttributeAction {
                        Target = 1, Attribute = ActorAttribute.Barding,
                        MinimumAmount = 2560, MaximumAmount = 3840, Type = 0,
                    } },
                });
                seen.Add(_session.StatsOf(3)[(int)ActorAttribute.Barding].Base);
            }

            foreach (int v in seen) {
                Assert.GreaterOrEqual(v, 10, "below the authored minimum of 2560/256");
                Assert.Less(v, 15, "RND(Max - Min) is exclusive of Max");
            }
            Assert.Greater(seen.Count, 1, "a band that never varies is Min being taken alone");
        }

        /// <summary>
        /// ApplyCondition ADDS to the rank rather than setting it.
        /// </summary>
        /// <remarks>
        /// stat_combatant_apply_condition reads the current rank, adds, and clamps to 0..100 — so a
        /// dialog can deepen a poison the party already has, or take some off with a negative
        /// amount. Setting would erase whatever the world had already done to them, and a test that
        /// only checked "is poisoned afterwards" would pass either way.
        /// </remarks>
        [Test]
        public void ApplyCondition_AddsToTheRankAndClampsAtBothEnds() {
            _session.SetActiveParty(1, new byte[] { 2 });
            _session.SetActorConditionsForTest(2, new GameData.Resources.Character.ActorConditions());
            _session.ConditionsOf(2)[ActorCondition.Poisoned] = 30;

            _executor.ApplyEntryGameplayActionsForTest(new GameData.Resources.Dialog.DialogEntry {
                Actions = { new ApplyConditionAction {
                    Target = 1, Condition = ActorCondition.Poisoned,
                    MinimumAmount = 25, MaximumAmount = 25,
                } },
            });
            Assert.AreEqual(55, _session.ConditionsOf(2)[ActorCondition.Poisoned], "added, not set");

            _executor.ApplyEntryGameplayActionsForTest(new GameData.Resources.Dialog.DialogEntry {
                Actions = { new ApplyConditionAction {
                    Target = 1, Condition = ActorCondition.Poisoned,
                    MinimumAmount = 900, MaximumAmount = 900,
                } },
            });
            Assert.AreEqual(100, _session.ConditionsOf(2)[ActorCondition.Poisoned], "clamped high");

            _executor.ApplyEntryGameplayActionsForTest(new GameData.Resources.Dialog.DialogEntry {
                Actions = { new ApplyConditionAction {
                    Target = 1, Condition = ActorCondition.Poisoned,
                    MinimumAmount = -900, MaximumAmount = -900,
                } },
            });
            Assert.AreEqual(0, _session.ConditionsOf(2)[ActorCondition.Poisoned], "clamped low");
        }

        /// <summary>
        /// GetPartyAttribute publishes the party's BEST into global 30013.
        /// </summary>
        /// <remarks>
        /// It changes nothing by itself, which is why it reads as a no-op until you notice what
        /// consumes it: a later condition branches on the answer. The party form asks for the best,
        /// the same reading every skill check uses — not a sum or an average.
        /// </remarks>
        [Test]
        public void GetPartyAttribute_PublishesTheBestForALaterConditionToRead() {
            _session.SetActiveParty(2, new byte[] { 2, 3 });
            _session.SetActorStatsForTest(2, StatsWithBarding(31));
            _session.SetActorStatsForTest(3, StatsWithBarding(67));

            _executor.ApplyEntryGameplayActionsForTest(new GameData.Resources.Dialog.DialogEntry {
                Actions = { new GetPartyAttributeAction {
                    Target = 1, Attribute = ActorAttribute.Barding,
                } },
            });

            // *** ASSERT THE RULE, NOT A NUMBER. *** What the attribute READS as depends on the
            // effects hook and the stat's own effective value, which a fixture that sets only Base
            // does not control — an earlier version of this test expected 67 and got 34. What the
            // action promises is that the party form publishes the BEST, so compare against the
            // same query the rest of the game uses.
            Assert.AreEqual(_session.PartyExtreme(ActorAttribute.Barding, out int _),
                _session.GetGlobalValue(30013),
                "the party form is the BEST of the attribute");
            Assert.AreNotEqual(0, _session.GetGlobalValue(30013),
                "a zero would pass the comparison above while publishing nothing");
        }

        /// <summary>
        /// SetReturnValue is scoped to ONE entry — it is not conversation state.
        /// </summary>
        /// <remarks>
        /// The original's nResult is a stack local in ExecuteDialog, set by the third pass and read
        /// when the loop ends. Ours is cleared at the top of every action walk for the same reason:
        /// a later entry with no SetReturnValue must answer NOTHING rather than repeat the last
        /// answer, or a conversation would end early wherever an earlier line had set one.
        /// </remarks>
        [Test]
        public void SetReturnValue_IsScopedToTheEntryThatCarriesIt() {
            _executor.ApplyEntryGameplayActionsForTest(new GameData.Resources.Dialog.DialogEntry {
                Actions = { new SetReturnValueAction { Value = -2 } },
            });
            Assert.AreEqual(-2, _executor.EntryReturnValue);

            _executor.ApplyEntryGameplayActionsForTest(new GameData.Resources.Dialog.DialogEntry {
                Actions = { new GlobalEffectAction() },
            });
            Assert.IsNull(_executor.EntryReturnValue,
                "an entry that sets nothing must answer nothing, not repeat the last answer");
        }
}
}
