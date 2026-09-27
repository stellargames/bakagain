namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Data;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// The live half of the party: hydrating mutable attribute/affliction state from a loaded save,
    /// and handing it back for the writer to persist. The byte-level half of this round trip is
    /// covered by the .NET SaveGameActorWriteTests; these cover the session wiring.
    /// </summary>
    [TestFixture]
    public class RuntimeActorStateTests {
        private GameSession _session;

        [SetUp]
        public void SetUp() {
            _session = new GameSession();
            _session.Initialize(
                new SaveGameBuilder().WithBackingBody().WithPartyActors().Build(),
                GameSessionSource.NewGame);
        }

        [Test]
        public void EveryPartyMemberGetsLiveStateOnLoad() {
            Assert.That(_session.RuntimeActorCount, Is.EqualTo(6));
            Assert.That(_session.RuntimeActorCount, Is.EqualTo(_session.PartyActors.Length));

            for (int i = 0; i < _session.RuntimeActorCount; i++) {
                Assert.That(_session.StatsOf(i), Is.Not.Null, $"stats for character {i}");
                Assert.That(_session.ConditionsOf(i), Is.Not.Null, $"conditions for character {i}");
            }
        }

        [Test]
        public void LiveStateStartsEqualToTheSavedRecord() {
            SaveGameActorData saved = _session.PartyActors[0];
            ActorStat[] live = _session.StatsOf(0);

            Assert.That(live[(int)ActorAttribute.Health].Base, Is.EqualTo(saved.Health.Current));
            Assert.That(live[(int)ActorAttribute.Health].Max, Is.EqualTo(saved.Health.Maximum));
            Assert.That(live[(int)ActorAttribute.Stealth].Base, Is.EqualTo(saved.Stealth.Current));
        }

        [Test]
        public void AskingForSomebodyOutsideThePartyGivesNothingRatherThanThrowing() {
            Assert.That(_session.StatsOf(-1), Is.Null);
            Assert.That(_session.StatsOf(99), Is.Null);
            Assert.That(_session.ConditionsOf(99), Is.Null);
        }

        [Test]
        public void ChangingLiveStateDoesNotTouchTheLoadedRecord() {
            byte before = _session.PartyActors[1].Stealth.Current;

            StatEngine.Modify(_session.StatsOf(1)[(int)ActorAttribute.Stealth],
                ActorAttribute.Stealth, 5 * 256);

            Assert.That(_session.PartyActors[1].Stealth.Current, Is.EqualTo(before),
                "the immutable record is the save as loaded and must not drift");
        }

        [Test]
        public void ChangesAreHandedToTheWriterForEveryMember() {
            int before = _session.StatsOf(2)[(int)ActorAttribute.Haggling].Base;
            StatEngine.Modify(_session.StatsOf(2)[(int)ActorAttribute.Haggling],
                ActorAttribute.Haggling, 7 * 256);
            ConditionEngine.Apply(_session.ConditionsOf(2), ActorCondition.Poisoned, 25);

            IReadOnlyList<DirtyActorEdit> edits = _session.CollectDirtyActorEdits();

            Assert.That(edits.Count, Is.EqualTo(_session.RuntimeActorCount));
            DirtyActorEdit edit = edits[2];
            Assert.That(edit.CharacterIndex, Is.EqualTo(2));
            Assert.That(edit.Stats[(int)ActorAttribute.Haggling].Base, Is.EqualTo(before + 7));
            Assert.That(edit.Conditions[ActorCondition.Poisoned], Is.EqualTo(25));
        }

        [Test]
        public void ClearingTheSessionDropsTheLiveState() {
            _session.Clear();

            Assert.That(_session.RuntimeActorCount, Is.EqualTo(0));
            Assert.That(_session.StatsOf(0), Is.Null);
            Assert.That(_session.CollectDirtyActorEdits(), Is.Empty);
        }
    }
}
