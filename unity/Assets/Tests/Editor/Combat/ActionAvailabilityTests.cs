namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Combat;
    using GameData.Resources.Text;
    using NUnit.Framework;
    using Command = GameData.Resources.Combat.CombatCommands.Command;

    /// <summary>Which stones are greyed and why (TASK-972 phase 1, task 7).</summary>
    public class ActionAvailabilityTests {
        private static string NameOf(CombatRuntime r, Combatant c) => r.LogNameForTest(c);

        [TestCase(4, 4)]   // orthogonal
        [TestCase(4, 5)]   // diagonal
        public void CastAndShootAreGreyedWhenAnyFoeIsWithinOneCell_DiagonalsToo(int x, int y) {
            (CombatRuntime runtime, CombatEncounter fight) =
                CombatRuntimeTests.ArcherAgainst(((byte)x, (byte)y));
            var a = runtime.AvailabilityFor(fight.Party[0]);

            foreach (Command c in new[] { Command.Cast, Command.Shoot }) {
                Assert.IsFalse(a[c].Enabled, c.ToString());
                Assert.AreEqual(UiTemplates.EnhancedAvailFoeAdjacent, a[c].ReasonKey, c.ToString());
            }
            Assert.AreEqual(UiTemplates.Format(UiTemplates.EnhancedAvailFoeAdjacent,
                ("name", NameOf(runtime, fight.Party[0]))), a[Command.Cast].Reason);
        }

        [Test]
        public void CastAndShootAreOfferedWithRoomToSpare() {
            (CombatRuntime runtime, CombatEncounter fight) =
                CombatRuntimeTests.ArcherAgainst(((byte)7, (byte)0));
            var a = runtime.AvailabilityFor(fight.Party[0]);

            Assert.IsTrue(a[Command.Shoot].Enabled);
            Assert.IsTrue(a[Command.Cast].Enabled);
            Assert.AreEqual("", a[Command.Shoot].ReasonKey);
        }

        [Test]
        public void ShootWithoutQuarrelsSaysSo() {
            (CombatRuntime runtime, CombatEncounter fight) =
                CombatRuntimeTests.ArcherAgainst(((byte)7, (byte)0), bolts: 0);

            Assert.AreEqual(UiTemplates.EnhancedAvailNoQuarrels,
                runtime.AvailabilityFor(fight.Party[0])[Command.Shoot].ReasonKey);
        }

        [Test]
        public void ShootWithoutACrossbowAndCastWithoutCastingSaySo() {
            (CombatRuntime runtime, CombatEncounter fight) =
                CombatRuntimeTests.ArcherAgainst(((byte)7, (byte)0), crossbowFlags: 0);
            Assert.AreEqual(UiTemplates.EnhancedAvailNoCrossbow,
                runtime.AvailabilityFor(fight.Party[0])[Command.Shoot].ReasonKey);

            (CombatRuntime r2, CombatEncounter f2) = CombatRuntimeTests.FightWithEnemiesAt(
                CombatRuntimeTests.StatBlock(health: 30, casting: 0), ((byte)7, (byte)0));
            Assert.AreEqual(UiTemplates.EnhancedAvailNotACaster,
                r2.AvailabilityFor(f2.Party[0])[Command.Cast].ReasonKey);
        }

        [Test]
        public void RetreatSaysWhoIsDown() {
            (CombatRuntime runtime, CombatEncounter fight) =
                CombatRuntimeTests.ArcherAgainst(((byte)7, (byte)0));
            Assert.IsTrue(runtime.AvailabilityFor(fight.Party[0])[Command.BackOrRetreat].Enabled);

            var second = new Combatant { PartySlot = 2, ClassId = 1, Flags = CombatantFlags.Dead };
            fight.Party.Add(second);
            var retreat = runtime.AvailabilityFor(fight.Party[0])[Command.BackOrRetreat];

            Assert.IsFalse(retreat.Enabled);
            Assert.AreEqual(UiTemplates.EnhancedAvailHeroDown, retreat.ReasonKey);
            Assert.AreEqual(UiTemplates.Format(UiTemplates.EnhancedAvailHeroDown,
                ("name", NameOf(runtime, second))), retreat.Reason);
        }

        [Test]
        public void RetreatIsRefusedWhenTheEncounterForbidsIt() {
            (CombatRuntime runtime, CombatEncounter fight) =
                CombatRuntimeTests.ArcherAgainst(((byte)7, (byte)0));
            fight.EscapeAllowed = false;

            var retreat = runtime.AvailabilityFor(fight.Party[0])[Command.BackOrRetreat];
            Assert.IsFalse(retreat.Enabled);
            Assert.AreEqual(UiTemplates.EnhancedAvailRetreatForbidden, retreat.ReasonKey);
        }

        [Test]
        public void RestWhilePoisonedIsEnabledButWarned() {
            var session = new GameSession();
            session.SetActorStatsForTest(0, CombatRuntimeTests.StatBlock(health: 30));
            session.SetActorConditionsForTest(0, new GameData.Resources.Character.ActorConditions());
            session.SetActiveParty(1, new byte[] { 0 });
            var runtime = new CombatRuntime(session, null,
                CombatRuntimeTests.Entries(((byte)3, (byte)4)));
            CombatEncounter fight = runtime.EnterRoster(null);
            Assert.IsFalse(runtime.AvailabilityFor(fight.Party[0])[Command.Rest].Warned);

            session.ConditionsOf(0)[ActorCondition.Poisoned] = 2;
            var rest = runtime.AvailabilityFor(fight.Party[0])[Command.Rest];

            Assert.IsTrue(rest.Enabled, "the turn is still spent");
            Assert.IsTrue(rest.Warned);
            Assert.AreEqual(UiTemplates.EnhancedAvailNoHealCondition, rest.ReasonKey);
            Assert.AreEqual(UiTemplates.Format(UiTemplates.EnhancedAvailNoHealCondition,
                ("condition", GameData.Resources.Character.ActorLabels.ConditionName(
                    ActorCondition.Poisoned))), rest.Reason);
        }

        [Test]
        public void DefendAndPackAreAlwaysOn() {
            (CombatRuntime runtime, CombatEncounter fight) =
                CombatRuntimeTests.ArcherAgainst(((byte)4, (byte)4));
            var a = runtime.AvailabilityFor(fight.Party[0]);
            Assert.IsTrue(a[Command.Defend].Enabled);
            Assert.IsTrue(a[Command.CharacterScreen].Enabled);
        }

        [Test]
        public void AutoIsRefusedOnATrapExitGrid() {
            var session = new GameSession();
            session.SetActorStatsForTest(0, CombatRuntimeTests.StatBlock(health: 30));
            session.SetActiveParty(1, new byte[] { 0 });
            var runtime = new CombatRuntime(session, null,
                CombatRuntimeTests.Entries(((byte)3, (byte)4)));

            CombatEncounter fight = runtime.EnterRoster(null, TrapPuzzleBuilder.Build(new[] {
                ((int)TrapElementType.Exit, 1, 11),
            }));
            var auto = runtime.AvailabilityFor(fight.Party[0])[Command.AutoResolve];

            Assert.IsFalse(auto.Enabled);
            Assert.AreEqual(UiTemplates.EnhancedAvailAutoRefused, auto.ReasonKey);
        }

        [Test]
        public void AutoIsOfferedInAnOrdinaryFight() {
            (CombatRuntime runtime, CombatEncounter fight) =
                CombatRuntimeTests.ArcherAgainst(((byte)7, (byte)0));
            Assert.IsTrue(runtime.AvailabilityFor(fight.Party[0])[Command.AutoResolve].Enabled);
        }
    }
}
