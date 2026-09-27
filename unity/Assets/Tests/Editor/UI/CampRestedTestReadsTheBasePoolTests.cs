namespace BakAgain.Tests.Editor.UI {
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using BakAgain.UI;
    using GameData;
    using GameData.Resources.Character;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// WHICH pool the camp screen's rested test reads (TASK-606).
    /// </summary>
    /// <remarks>
    /// The rule itself — every active member at or above 80% — was never wrong. The quantity was:
    /// <c>stat_party_all_above_pct</c> (STAT.C:451-470) asks <c>stat_actor_get(char, 0x10, <b>3</b>)</c>,
    /// which is <c>st-&gt;base</c>, the stored pair, i.e. <see cref="StatEngine.HealthPool"/>.
    /// <c>PartyIsRested</c> read the EFFECTIVE pool instead, so an afflicted member dragged the
    /// party below the line and the game offered a rest the original refuses.
    ///
    /// <para>That test gates two things, which is why it matters twice over: the rest loop's stop
    /// condition (ENCAMP.C:140) and the Camp button itself (ENCAMP.C:82-87, which greys entry 0 and
    /// slides Exit into its place).</para>
    /// </remarks>
    public class CampRestedTestReadsTheBasePoolTests {
        private GameObject _go;
        private GameSession _session;
        private CampMenu _camp;

        [SetUp]
        public void SetUp() {
            _session = new GameSession();
            _session.Initialize(
                new SaveGameBuilder().WithBackingBody().WithPartyActors()
                    .WithParty(new[] { "A", "B", "C" }, new byte[] { 0, 1, 2 }).Build(),
                GameSessionSource.NewGame);

            // Inactive, so no OnEnable runs and the REQ loader never asks for an address it has not
            // been given — the same arrangement the MenuLayerHost tests use.
            _go = new GameObject("camp");
            _go.SetActive(false);
            _camp = _go.AddComponent<CampMenu>();
            typeof(CampMenu).GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_camp, _session);
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        private void SetPool(int character, int health, int healthMax, int stamina, int staminaMax) {
            ActorStat[] stats = _session.StatsOf(character);
            ActorStat h = stats[(int)ActorAttribute.Health];
            ActorStat s = stats[(int)ActorAttribute.Stamina];
            h.Base = (byte)health; h.Effective = (byte)health; h.Max = (byte)healthMax;
            s.Base = (byte)stamina; s.Effective = (byte)stamina; s.Max = (byte)staminaMax;
        }

        private void FillTheParty() {
            foreach (byte id in _session.ActivePartyIndices) {
                SetPool(id, 60, 60, 40, 40);
            }
        }

        [Test]
        public void AnAfflictedMemberAtFullBaseSTILLReadsAsRested() {
            // The whole bug in one case. Drunk drags the EFFECTIVE pool under the line while the
            // stored pair — the thing the original actually tests — has not moved.
            FillTheParty();
            byte drunk = _session.ActivePartyIndices[1];
            // Base 85 of max 100, so the line sits at 80 and the member clears it by five. Drunk at
            // rank 60 costs about fifteen points of EFFECTIVE pool -- measured, not guessed: at a
            // full 100 base it leaves 85, which is still above the line and tests nothing. The pool
            // has to straddle the threshold for the two readings to disagree.
            SetPool(drunk, 50, 60, 35, 40);
            ConditionEngine.Apply(_session.ConditionsOf(drunk), ActorCondition.Drunk, 60);

            ActorStat[] stats = _session.StatsOf(drunk);
            int basePool = StatEngine.HealthPool(
                stats[(int)ActorAttribute.Health], stats[(int)ActorAttribute.Stamina]);
            int effective = _session.EffectivePool(drunk);
            int threshold = CampMenu.RestedPercent * _session.EffectivePoolMax(drunk) / 100;

            // The fixture has to actually discriminate, or the assertion below is vacuous — if a
            // future change to Drunk stops splitting these two, this says so instead of passing.
            Assert.That(basePool, Is.GreaterThanOrEqualTo(threshold),
                "the base pool must clear the line, or this case tests nothing");
            Assert.That(effective, Is.LessThan(threshold),
                "the effective pool must fall below it, or this case tests nothing");

            Assert.IsTrue(_camp.PartyIsRested(),
                "the rested test reads the BASE pair (mode 3), so an afflicted member still counts");
        }

        [Test]
        public void AMemberGENUINELYBelowTheLineIsNotRested() {
            // The control: without it the case above is satisfied by a predicate that always says
            // rested, which would grey the Camp button forever.
            FillTheParty();
            SetPool(_session.ActivePartyIndices[2], 10, 60, 0, 40);

            Assert.IsFalse(_camp.PartyIsRested());
        }

        [Test]
        public void AFullyRestedPartyIsRested() {
            FillTheParty();

            Assert.IsTrue(_camp.PartyIsRested());
        }
    }
}
