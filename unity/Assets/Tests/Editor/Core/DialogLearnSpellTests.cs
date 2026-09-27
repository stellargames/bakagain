namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using GameData.Resources.Dialog;
    using GameData.Resources.Dialog.Actions;
    using GameData.Resources.Spells;
    using NUnit.Framework;
    using System.Reflection;

    /// <summary>
    /// A dialog teaching a spell — the story-grant path, as opposed to reading a scroll.
    /// </summary>
    /// <remarks>
    /// The rule itself (<see cref="SpellBook.Learn"/>) is pinned in the .NET suite; what these cover
    /// is the wiring around it, and specifically WHICH CHARACTER learns. The operand is not a member
    /// id — it indexes the speaker slots, biased by two — so reading it directly would teach the
    /// wrong person, silently and permanently.
    /// </remarks>
    [TestFixture]
    public class DialogLearnSpellTests {
        private GameSession _session;
        private DialogExecutor _executor;

        private const int Spell = 7;

        [SetUp]
        public void SetUp() {
            _session = new GameSession();
            _session.Initialize(
                new SaveGameBuilder().WithBackingBody().WithPartyActors()
                    .WithParty(new[] { "A", "B", "C" }, new byte[] { 0, 1, 2 }).Build(),
                GameSessionSource.NewGame);
            _executor = new DialogExecutor(new NullLogger<DialogExecutor>(), _session,
                new GameClock(_session));
        }

        [Test]
        public void TheMemberInTheNamedSpeakerSlotLearnsIt() {
            DialogSlotTable slots = SlotsNaming(member: 1);

            Apply(new LearnSpellAction { Actor = DialogSlotTable.FirstSpeakerOperand, SpellId = Spell },
                slots);

            Assert.That(SpellBook.IsKnown(_session.KnownSpellsOf(1), Spell), Is.True);
            // And nobody else: the operand names one speaker, not the party.
            Assert.That(SpellBook.IsKnown(_session.KnownSpellsOf(0), Spell), Is.False);
            Assert.That(SpellBook.IsKnown(_session.KnownSpellsOf(2), Spell), Is.False);
        }

        [Test]
        public void APartyWideOperandTeachesNobody() {
            // 0 and 1 mean "the whole party" for the ops that have a party-wide form. This op has
            // none — the original never broadcasts it — so the safe answer is nobody rather than
            // everybody.
            Apply(new LearnSpellAction { Actor = 0, SpellId = Spell }, SlotsNaming(member: 1));

            for (var member = 0; member < 3; member++) {
                Assert.That(SpellBook.IsKnown(_session.KnownSpellsOf(member), Spell), Is.False,
                    $"member {member} must not have been taught by a party-wide operand");
            }
        }

        [Test]
        public void ASlotHoldingNobodyTeachesNobody() {
            // An unfilled slot must not fall back to "somebody" — the wrong character learning a
            // story spell is not recoverable by the player.
            Apply(new LearnSpellAction { Actor = DialogSlotTable.FirstSpeakerOperand, SpellId = Spell },
                new DialogSlotTable());

            for (var member = 0; member < 3; member++) {
                Assert.That(SpellBook.IsKnown(_session.KnownSpellsOf(member), Spell), Is.False);
            }
        }

        [Test]
        public void TeachingItTwiceLeavesItLearnedOnce() {
            DialogSlotTable slots = SlotsNaming(member: 1);
            var action = new LearnSpellAction {
                Actor = DialogSlotTable.FirstSpeakerOperand, SpellId = Spell,
            };

            Apply(action, slots);
            ushort[] afterFirst = (ushort[])_session.KnownSpellsOf(1).Clone();
            Apply(action, slots);

            Assert.That(_session.KnownSpellsOf(1), Is.EqualTo(afterFirst));
        }

        /// <summary>A slot table whose first speaker slot holds <paramref name="member"/>.</summary>
        private static DialogSlotTable SlotsNaming(int member) {
            var slots = new DialogSlotTable();
            slots.Kinds[0] = member;   // the first speaker slot holds this party member

            return slots;
        }

        private void Apply(LearnSpellAction action, DialogSlotTable slots) =>
            typeof(DialogExecutor)
                .GetMethod("ApplyLearnSpell", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_executor, new object[] { action, slots });
    }
}
