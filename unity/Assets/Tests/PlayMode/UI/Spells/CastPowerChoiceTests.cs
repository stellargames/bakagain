namespace BakAgain.Tests.PlayMode.UI.Spells {
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI.Spells;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Character;
    using NUnit.Framework;
    using GameData.Resources.Spells;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// While a power is being chosen the faithful screen reads only the ring and Escape
    /// (cspell_select_power, CSPELL.C:2056-2083): a portrait click must not hand the cast to someone
    /// else, whose spells and pool the chosen band was not worked out for, nor switch the school. The
    /// Enhanced screen lets a school click cancel the choice and switch (owner, 2026-10-18).
    /// </summary>
    public class CastPowerChoiceTests {
        private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject _go;
        private EnhancedPrefsStash _stash;

        [SetUp]
        public void SetUp() => _stash = EnhancedPrefsStash.Take();

        [TearDown]
        public void TearDown() {
            if (_go != null) Object.DestroyImmediate(_go);
            _stash.Restore();
        }

        private static ActorStat[] Caster() {
            var stats = new ActorStat[16];
            for (int i = 0; i < stats.Length; i++) stats[i] = new ActorStat { Base = 10, Max = 10 };
            return stats;   // AccuracyCasting max 10: a caster
        }

        [UnityTest]
        public IEnumerator APortraitClickDuringThePowerChoiceKeepsTheCaster() {
            var session = new GameSession();
            typeof(GameSession).GetProperty(nameof(GameSession.IsActive)).GetSetMethod(true).Invoke(session, new object[] { true });
            session.SetActiveParty(2, new byte[] { 0, 1 });
            session.SetActorStatsForTest(0, Caster());
            session.SetActorStatsForTest(1, Caster());

            _go = new GameObject("CastScreenUnderTest");
            _go.SetActive(false);
            var screen = _go.AddComponent<CastScreen>();
            screen.Construct(null, null, null, null, session, null);
            typeof(CastScreen).GetProperty(nameof(CastScreen.CasterId)).GetSetMethod(true).Invoke(screen, new object[] { 0 });
            typeof(CastScreen).GetField("_sliderSpell", Any).SetValue(screen, 3);

            yield return screen.SelectCaster(1).ToCoroutine();

            Assert.AreEqual(0, screen.CasterId, "the caster who picked the spell keeps it");
            Assert.AreEqual(3, (int)typeof(CastScreen).GetField("_sliderSpell", Any).GetValue(screen), "and the choice goes on");
        }

        /// <summary>A caster on school 4 with spell 3's power band open.</summary>
        private CastScreen ChoosingAPower(GameSession session) {
            _go = new GameObject("CastScreenUnderTest");
            _go.SetActive(false);
            var screen = _go.AddComponent<CastScreen>();
            screen.Construct(null, null, null, null, session, null);
            typeof(CastScreen).GetProperty(nameof(CastScreen.CasterId)).GetSetMethod(true).Invoke(screen, new object[] { 0 });
            typeof(CastScreen).GetField("_school", Any).SetValue(screen, 4);
            typeof(CastScreen).GetField("_sliderSpell", Any).SetValue(screen, 3);
            return screen;
        }

        private static GameSession OneCaster() {
            var session = new GameSession();
            typeof(GameSession).GetProperty(nameof(GameSession.IsActive)).GetSetMethod(true).Invoke(session, new object[] { true });
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetActorStatsForTest(0, Caster());
            return session;
        }

        private static int SliderSpell(CastScreen screen) => (int)typeof(CastScreen).GetField("_sliderSpell", Any).GetValue(screen);

        private const int School5Button = CastMenuSelection.FirstSchoolActionId + 5;

        // The faithful screen: a school button does nothing while the power is chosen (CSPELL.C:2056-2083,
        // the power loop reads only the ring and Escape).
        [Test]
        public void FaithfulScreen_ASchoolClickDuringThePowerChoiceIsIgnored() {
            CastScreen screen = ChoosingAPower(OneCaster());
            int committed = 0;
            screen.Committed += (_, _, _) => committed++;

            screen.PrimaryAction(School5Button);

            Assert.AreEqual(4, screen.School, "the school stays");
            Assert.AreEqual(3, SliderSpell(screen), "and the power choice goes on");
            Assert.AreEqual(0, committed);
        }

        // The Enhanced screen (owner, 2026-10-18): the same click drops the power choice, reports no cast,
        // and switches school.
        [Test]
        public void EnhancedScreen_ASchoolClickDuringThePowerChoiceCancelsItAndSwitchesSchool() {
            GameSession session = OneCaster();
            CastScreen screen = ChoosingAPower(session);
            var layout = new EnhancedCastLayout(ReqRoot(out VisualElement stage), session);
            Assert.IsTrue(layout.Apply(stage, Ring(), new HashSet<int>(), combat: false), "the Enhanced layout applies");
            typeof(CastScreen).GetField("_enhanced", Any).SetValue(screen, layout);
            int committed = 0;
            screen.Committed += (_, _, _) => committed++;

            screen.PrimaryAction(School5Button);

            Assert.AreEqual(5, screen.School, "the school switches");
            Assert.AreEqual(-1, SliderSpell(screen), "the power choice is dropped");
            Assert.AreEqual(0, committed, "no cast and no cancel is reported");
            layout.Revert();
        }

        /// <summary>The REQ_CAST elements the Enhanced layout moves: Exit, the six schools, the three portraits.</summary>
        private static VisualElement ReqRoot(out VisualElement stage) {
            var root = new VisualElement();
            stage = new VisualElement();
            root.Add(stage);
            var names = new List<string> { "imagebutton_1" };
            for (int i = 0; i < CastRingLayout.CategoryCount; i++) names.Add($"imagebutton_{CastMenuSelection.FirstSchoolActionId + i}");
            for (int i = 0; i < 3; i++) names.Add($"hotspot_{CastMenuSelection.FirstPartySlotActionId + i}");
            foreach (string n in names) stage.Add(new VisualElement { name = n });
            return root;
        }

        private static IReadOnlyList<RingPosition> Ring() =>
            Enumerable.Range(0, 30).Select(i => new RingPosition {
                X = 400 + (int)(300 * System.Math.Cos(i * 0.21)), Y = 600 + (int)(300 * System.Math.Sin(i * 0.21)),
            }).ToList();
    }
}
