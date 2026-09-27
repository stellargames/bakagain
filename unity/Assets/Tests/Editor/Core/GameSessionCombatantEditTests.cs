namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using GameData.Resources.Data;
    using NUnit.Framework;

    /// <summary>
    /// What the save writer is handed for the damage a fight did (TASK-226).
    /// </summary>
    /// <remarks>
    /// The bug these pin: wound an enemy, flee, come back, and it was at full health. Both halves of
    /// the mechanism existed — <c>CombatRuntime.CollectDirtyCombatantEdits</c> produced the records
    /// and <c>SaveGameWriter</c> applied them — and nothing joined the two, because the obvious join
    /// (ask at save time) cannot work: the encounter is gone by then.
    /// </remarks>
    public class GameSessionCombatantEditTests {
        // Same shape CombatRuntimeTests builds; only combatStatus varies, since that is the field
        // these tests use to tell one staged record from another.
        private static SaveGameCombatData Record(int status) =>
            new SaveGameCombatData(
                targetActorPointer: 0x4321, creatureType: 9,
                xOnGrid: 6, yOnGrid: 7, targetXOnGrid: 0, targetYOnGrid: 0,
                combatStatus: (byte)status, animEffectType: 0, activeSpellEffectSlot: 0,
                unusedPadding: 0, animDurationTimer: 0, monsterSpellAbility: 3,
                meleeAttackType: 4, rangedAttackType: 5, movementAiType: 6,
                preferredArrowType: -1, lastSpellSymbolFile: 0, floatingDamageValue: 0,
                floatingDamageTimer: -1);

        [Test]
        public void NothingIsOfferedUntilAFightStagesSomething() {
            Assert.IsEmpty(new GameSession().DirtyCombatantEdits);
        }

        [Test]
        public void AStagedEditIsOfferedToTheWriter() {
            var session = new GameSession();

            session.StageCombatantEdits(new[] { new DirtyCombatantEdit(400, Record(7)) });

            Assert.AreEqual(1, session.DirtyCombatantEdits.Count);
            Assert.AreEqual(400, session.DirtyCombatantEdits[0].ActorSlot);
            Assert.AreEqual(7, session.DirtyCombatantEdits[0].Record.CombatStatus);
        }

        [Test]
        public void TheLATERFightWinsForTheSameActor() {
            // *** The reason this is keyed by slot. *** Meeting the same creature twice must leave
            // ONE record — the newer one. A list would carry both and let the writer apply them in
            // whatever order they were added, so whether the enemy ends up wounded or healed would
            // depend on enumeration order.
            var session = new GameSession();

            session.StageCombatantEdits(new[] { new DirtyCombatantEdit(400, Record(7)) });
            session.StageCombatantEdits(new[] { new DirtyCombatantEdit(400, Record(3)) });

            Assert.AreEqual(1, session.DirtyCombatantEdits.Count, "one slot, one record");
            Assert.AreEqual(3, session.DirtyCombatantEdits[0].Record.CombatStatus, "the later fight");
        }

        [Test]
        public void DifferentActorsAccumulate() {
            var session = new GameSession();

            session.StageCombatantEdits(new[] { new DirtyCombatantEdit(400, Record(7)) });
            session.StageCombatantEdits(new[] { new DirtyCombatantEdit(401, Record(2)) });

            Assert.AreEqual(2, session.DirtyCombatantEdits.Count);
        }

        [Test]
        public void AFightThatHurtNobodyStagesNothing() {
            // What CollectDirtyCombatantEdits returns for an untouched fight, and what every path
            // that ends a fight hands over unconditionally.
            var session = new GameSession();

            session.StageCombatantEdits(null);
            session.StageCombatantEdits(new DirtyCombatantEdit[0]);

            Assert.IsEmpty(session.DirtyCombatantEdits);
        }

        [Test]
        public void ReadingTheEditsDoesNotConsumeThem() {
            // *** Deliberately not cleared, like DirtyGlobalFlags. *** The writer patches these onto
            // a CLONE of the backing body every time, so re-applying the same record is a no-op —
            // whereas dropping them after the first save would silently lose the damage on the
            // second one.
            var session = new GameSession();
            session.StageCombatantEdits(new[] { new DirtyCombatantEdit(400, Record(7)) });

            System.Collections.Generic.IReadOnlyList<DirtyCombatantEdit> first =
                session.DirtyCombatantEdits;
            System.Collections.Generic.IReadOnlyList<DirtyCombatantEdit> second =
                session.DirtyCombatantEdits;

            Assert.AreEqual(1, first.Count);
            Assert.AreEqual(1, second.Count, "reading twice must not empty the staging store");
        }
    
        // ---- the 95-byte half (TASK-230): enemy HEALTH, which the combat record does not carry ----

        private static GameData.Resources.Character.ActorStat[] Stats(byte health) {
            var s = new GameData.Resources.Character.ActorStat[16];
            for (int i = 0; i < s.Length; i++) {
                s[i] = new GameData.Resources.Character.ActorStat();
            }
            s[(int)GameData.ActorAttribute.Health] = new GameData.Resources.Character.ActorStat {
                Max = 30, Base = health, Effective = health,
            };
            return s;
        }

        [Test]
        public void NoRosterEditIsOfferedUntilAFightStagesOne() {
            Assert.IsEmpty(new GameSession().DirtyRosterActorEdits);
        }

        [Test]
        public void AStagedRosterEditIsOfferedToTheWriter() {
            var session = new GameSession();

            session.StageRosterActorEdits(new[] { new DirtyRosterActorEdit(400, Stats(9)) });

            Assert.AreEqual(1, session.DirtyRosterActorEdits.Count);
            Assert.AreEqual(400, session.DirtyRosterActorEdits[0].ActorSlot);
        }

        [Test]
        public void TheLATERFightWinsForTheSameRosterSlot() {
            var session = new GameSession();

            session.StageRosterActorEdits(new[] { new DirtyRosterActorEdit(400, Stats(9)) });
            session.StageRosterActorEdits(new[] { new DirtyRosterActorEdit(400, Stats(2)) });

            Assert.AreEqual(1, session.DirtyRosterActorEdits.Count);
            Assert.AreEqual(2,
                session.DirtyRosterActorEdits[0].Stats[(int)GameData.ActorAttribute.Health].Base);
        }

        [Test]
        public void TheTwoHALVESAreStagedSEPARATELY() {
            // *** They are different records and different tables. *** A test that only checked one
            // would have passed all through TASK-226, when the combat half was wired and the actor
            // half did not exist — which is exactly what happened.
            var session = new GameSession();

            session.StageCombatantEdits(new[] { new DirtyCombatantEdit(400, Record(1)) });

            Assert.AreEqual(1, session.DirtyCombatantEdits.Count);
            Assert.IsEmpty(session.DirtyRosterActorEdits, "staging one must not fabricate the other");
        }
}
}
