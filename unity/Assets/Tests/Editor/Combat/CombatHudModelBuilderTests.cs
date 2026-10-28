namespace BakAgain.Tests.Editor.Combat {
    using System.Linq;
    using BakAgain.Combat;
    using BakAgain.Core;
    using BakAgain.UI.Combat.Enhanced;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using NUnit.Framework;

    /// <summary>The Enhanced HUD's model from a live fight (TASK-972 phase 1, Task 8).</summary>
    public class CombatHudModelBuilderTests {
        private static (CombatRuntime Runtime, CombatEncounter Fight, ActorStat[] Hero, GameSession Session) Duel() {
            ActorStat[] hero = CombatRuntimeTests.StatBlock(health: 20);
            hero[(int)ActorAttribute.Health] = new ActorStat { Base = 20, Max = 40 };   // half health: stats drag
            hero[(int)ActorAttribute.Stamina] = new ActorStat { Base = 10, Max = 40 };
            hero[(int)ActorAttribute.Speed] = new ActorStat { Base = 4, Max = 99 };
            hero[(int)ActorAttribute.Strength] = new ActorStat { Base = 12, Max = 99 };
            var session = new GameSession();
            session.SetActorStatsForTest(0, hero);
            session.SetActiveParty(1, new byte[] { 0 });
            ActorStat[] enemy = CombatRuntimeTests.StatBlock(health: 20);
            enemy[(int)ActorAttribute.Defense] = new ActorStat { Base = 40, Max = 99 };
            session.SetRosterActorForTest(400, enemy, gridX: 4, gridY: 4);
            session.SetRosterActorForTest(401, enemy, gridX: 7, gridY: 6);
            var runtime = new CombatRuntime(session, null, CombatRuntimeTests.Entries(((byte)3, (byte)4)));
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400, 401 });
            return (runtime, fight, hero, session);
        }

        [Test]
        public void TheModelIsTheFightAsItStands_ReadWithoutWriting() {
            (CombatRuntime r, CombatEncounter f, ActorStat[] hero, GameSession session) = Duel();
            Assert.AreSame(f.Party[0], r.AdvanceToPartyTurn(), "the hero's turn (the foes forfeit theirs)");
            // A lapsed timed modifier on every stat: a Get-path read would free it (and mark the block dirty).
            var slots = new ActorStatModifiers.Slot[ActorStatModifiers.SlotsPerCharacter];
            slots[0] = new ActorStatModifiers.Slot((int)ActorStatModifiers.ModifierFlags.Expires | 1, 0xffff, 1, 0, 1);
            session.CommitStatModifierSlots(0, slots);
            session.GameTimeIn2Seconds = 100;
            byte[] cached = hero.Select(s => s?.Effective ?? 0).ToArray();

            CombatHudModel m = CombatHudModelBuilder.Build(r, null, BoardMarks.Empty, 2);
            r.ForecastNoTarget(f.Party[0], ForecastAction.Rest);
            r.ForecastMelee(f.Party[0], f.Enemies[0], CombatActionDispatch.MeleeAttack.Thrust);

            // The HUD's poll and a hover's forecast write nothing: no cached byte, no freed modifier.
            CollectionAssert.AreEqual(cached, hero.Select(s => s?.Effective ?? 0).ToArray(), "cached stat bytes");
            Assert.IsFalse(session.StatModifierSlotsFor(0)[0].IsEmpty, "no lapsed modifier was freed");

            // Control: the Classic turn-start read (CapabilitiesFor) IS the original's Get, and frees it.
            r.CapabilitiesFor(f.Party[0]);
            Assert.IsTrue(session.StatModifierSlotsFor(0)[0].IsEmpty, "the Classic read still frees it");

            Assert.IsFalse(m.TheirTurn);
            Assert.AreEqual(f.Party[0].PartySlot, m.ActorSlot);
            Assert.AreEqual((20, 40, 10, 40), (m.Hero.Health, m.Hero.HealthMax, m.Hero.Stamina, m.Hero.StaminaMax));
            Assert.AreEqual(4, m.Hero.SpeedBase, "base = before Health drags it down");
            Assert.Less(m.Hero.Speed, m.Hero.SpeedBase, "current = scaled by Health (half)");
            Assert.AreEqual(12, m.Hero.StrengthBase);
            Assert.AreEqual(new[] { 'A', 'B' }, m.Foes.Select(x => x.Letter).ToArray(), "letters by roster order");
            Assert.IsTrue(m.Foes[0].Rows.All(row => !row.Known && row.Value == "?"), "nothing known before Inspect");
            Assert.IsTrue(m.Stones.ContainsKey(CombatCommands.Command.Defend), "the stones' availability");
            Assert.AreEqual(1, m.Party.Count);
            Assert.AreEqual(BakAgain.UI.InGame.RingVariant.Active, m.Party[0].State);
            r.Knowledge.Record(0, ActorAttribute.Defense, 40);
            FoeStatRow defense = CombatHudModelBuilder.Build(r, null, BoardMarks.Empty, 2).Foes[0].Rows
                .Single(row => row.ShortLabel == GameData.Resources.Text.UiTemplates.Format(
                    GameData.Resources.Text.UiTemplates.HudShortDefense));
            Assert.IsTrue(defense.Known);
            StringAssert.Contains("40", defense.Value);
        }

        [Test]
        public void NoFightNoModel() {
            var r = new CombatRuntime(new GameSession(), null, CombatRuntimeTests.Entries(((byte)3, (byte)4)));
            Assert.IsNull(CombatHudModelBuilder.Build(r, null, BoardMarks.Empty, 2));
        }
    }
}
