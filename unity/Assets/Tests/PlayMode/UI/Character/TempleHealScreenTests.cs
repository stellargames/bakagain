namespace BakAgain.Tests.PlayMode.UI.Character {
    using BakAgain.Core;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using BakAgain.UI.Character;
    using GameData;
    using GameData.Resources.Character;
    using NUnit.Framework;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// The temple healing screen's loop — <c>charscreen_temple_heal_menu</c> @0x5877e. The pricing
    /// and cure rules are pinned in the .NET <c>TempleHealMenu</c> tests; these are about the screen
    /// wiring them together, which is where a correct rule can still be applied to the wrong member
    /// or in the wrong order.
    /// </summary>
    /// <remarks>
    /// The screen is a DI'd MonoBehaviour whose redraws need a real REQ build, so these drive the
    /// button arms directly and let the redraw no-op on a null stage — the same approach the
    /// inventory layout tests take.
    /// </remarks>
    [TestFixture]
    public class TempleHealScreenTests {
        private GameSession _session;
        private GameObject _go;
        private TempleHealScreen _screen;

        [SetUp]
        public void SetUp() {
            _session = new GameSession();
            _session.Initialize(
                new SaveGameBuilder().WithBackingBody().WithPartyActors()
                    .WithParty(new[] { "A", "B", "C" }, new byte[] { 0, 1, 2 }).Build(),
                GameSessionSource.NewGame);
            _session.PartyGold = 10000;
            _go = new GameObject("TempleHealScreenUnderTest");
            _go.SetActive(false); // no OnEnable: the REQ build and the redraw are not what is tested
            _screen = _go.AddComponent<TempleHealScreen>();
            _screen.Construct(null, null, null, _session, null);
        }

        [TearDown]
        public void TearDown() {
            if (_cueSpy != null) { BakAgain.Audio.MenuSoundService.CueRequested -= _cueSpy; _cueSpy = null; }
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        [Test]
        public void TheScreenOpensOnTheFirstMemberWhoNeedsSomething() {
            Afflict(character: 2, ActorCondition.Poisoned, 50);

            _screen.SetService(FullPrice, PlainMode);

            Assert.That(Slot(), Is.EqualTo(2));
        }

        [Test]
        public void WithNobodyAfflictedItOpensOnTheFirstMemberRatherThanPastTheEnd() {
            // The merely-wounded case a generous temple still sees: nobody prices above zero, and
            // running the scan off the end here would leave the screen showing nobody at all.
            _screen.SetService(FullPrice, TempleHealEntry.ModeThatTreatsWounds);

            Assert.That(Slot(), Is.EqualTo(0));
        }

        [Test]
        public void NextSkipsWhoeverNeedsNothing() {
            Afflict(character: 2, ActorCondition.Sick, 30);
            _screen.SetService(FullPrice, PlainMode);
            SetSlot(0);

            Advance();

            // Not "the next member" but "the next member with something to cure" — member 1 is well.
            Assert.That(Slot(), Is.EqualTo(2));
        }

        [Test]
        public void AdvancingPastTheLastMemberLeavesRatherThanWrapping() {
            Afflict(character: 0, ActorCondition.Sick, 30);
            _screen.SetService(FullPrice, PlainMode);
            SetSlot(2);

            Advance();

            // Nobody after slot 2, so the screen closes. A wrap would land back on the afflicted
            // member at slot 0 and tour the party forever — which, with curing falling into Next,
            // is a loop the player could not leave except by Done.
            Assert.That(Slot(), Is.EqualTo(2));
        }

        [Test]
        public void TheBillIsTheAfflictionPriceUntilTheTempleTreatsWounds() {
            Afflict(character: 0, ActorCondition.Sick, 50);
            _screen.SetService(FullPrice, PlainMode);
            long afflictionOnly = Bill(0);

            _screen.SetService(FullPrice, TempleHealEntry.ModeThatTreatsWounds);

            // The mode that treats wounds also BILLS for them, one royal per missing point — the
            // whole of the difference between the modes. The fixture's party is well below its
            // maxima, so the surcharge is visible.
            Assert.That(Bill(0), Is.GreaterThan(afflictionOnly));
        }

        [Test]
        public void APricelessMemberIsNotOfferedACure() {
            _screen.SetService(FullPrice, PlainMode);

            // The price function IS the needs-healing test: zero means nothing to cure.
            Assert.That(Bill(1), Is.Zero);
            Afflict(character: 1, ActorCondition.NearDeath, 10);
            Assert.That(Bill(1), Is.GreaterThan(0));
        }

        [Test]
        public void ATempleThatChargesMoreAlsoAsksForMore() {
            Afflict(character: 0, ActorCondition.Plagued, 40);
            _screen.SetService(FullPrice, PlainMode);
            long full = Bill(0);

            _screen.SetService(HalfPrice, PlainMode);

            // One function, two jobs: the multiplier the temple charges with is the same one the
            // needs-healing test is asked with.
            Assert.That(Bill(0), Is.LessThan(full));
        }

        // ---- the cure itself (CHARSCRN.C:512-525) ----------------------------------------------

        /// <summary>
        /// A paid cure restarts the awake clock from NOW: <c>dwLastActionTimeSnapshot = game_time</c>
        /// (CHARSCRN.C:519), as the inn does. Zero would make the whole game's age read as time awake,
        /// and the next hourly tick would drain the party (GSTATE.C:304-317).
        /// </summary>
        [UnityTest]
        public IEnumerator ACureRestartsTheRestClockFromNowNotFromZero() => UniTask.ToCoroutine(async () => {
            var log = new List<string>();
            Curing(log);
            _session.GameTimeIn2Seconds = 123456;
            _session.LastRestTicks = 1000;

            await Cure();

            Assert.That(_session.LastRestTicks, Is.EqualTo(123456));
        });

        /// <summary>
        /// The cure's sound, 12_SPELL2, after the "cured" dialog (CHARSCRN.C:517-518).
        /// </summary>
        [UnityTest]
        public IEnumerator ACurePlaysItsSoundAfterTheCuredDialog() => UniTask.ToCoroutine(async () => {
            var log = new List<string>();
            Curing(log);

            await Cure();

            Assert.That(log, Is.EqualTo(new[] { "dialog " + TempleHealMenu.CuredDialog, "cue 12" }));
        });

        /// <summary>Control: a purse that cannot pay hears the refusal and no cure sound.</summary>
        [UnityTest]
        public IEnumerator ARefusedCurePlaysNoSound() => UniTask.ToCoroutine(async () => {
            var log = new List<string>();
            Curing(log);
            _session.PartyGold = 0;

            await Cure();

            Assert.That(log, Is.EqualTo(new[] { "dialog " + TempleHealMenu.CannotAffordDialog }));
        });

        private System.Action<int> _cueSpy;

        private void Curing(List<string> log) {
            _screen.Construct(null, null, new LoggingDialogs(log), _session,
                new BakAgain.Core.Services.GameClock(_session));
            Afflict(character: 0, ActorCondition.Sick, 30);
            _screen.SetService(FullPrice, PlainMode);
            SetSlot(0);
            _cueSpy = id => log.Add("cue " + id);
            BakAgain.Audio.MenuSoundService.CueRequested += _cueSpy;
        }

        private UniTask Cure() =>
            (UniTask)_screen.GetType().GetMethod("CureAsync", Flags).Invoke(_screen, null);

        private sealed class LoggingDialogs : BakAgain.Tests.PlayMode.UI.Inventory.NoOpDialogs {
            private readonly List<string> _log;
            public LoggingDialogs(List<string> log) => _log = log;
            public override UniTask<int> ShowById(int id, System.Threading.CancellationToken ct = default) {
                _log.Add("dialog " + id);
                return base.ShowById(id, ct);
            }
        }

        // ---- reaching the screen's own state ----------------------------------------------------

        private void Afflict(int character, ActorCondition condition, int rank) =>
            _session.ConditionsOf(character)[condition] = rank;

        private int Slot() => (int)Field("_slot").GetValue(_screen);

        private void SetSlot(int slot) => Field("_slot").SetValue(_screen, slot);

        private void Advance() =>
            _screen.GetType().GetMethod("Advance", Flags).Invoke(_screen, null);

        private long Bill(int slot) =>
            (long)_screen.GetType().GetMethod("BillFor", Flags).Invoke(_screen, new object[] { slot });

        private static FieldInfo Field(string name) =>
            typeof(TempleHealScreen).GetField(name, Flags);

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        private const int FullPrice = 100;
        private const int HalfPrice = 50;

        /// <summary>Any mode that is not the wound-treating one.</summary>
        private const int PlainMode = 0;
    }
}
