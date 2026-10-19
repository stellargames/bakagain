namespace BakAgain.Tests.PlayMode.UI.Spells {
    using System.Collections;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.UI.Spells;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Character;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// While a power is being chosen the faithful screen reads only the ring and Escape
    /// (cspell_select_power, CSPELL.C:2056-2083): a portrait click must not hand the cast to someone
    /// else, whose spells and pool the chosen band was not worked out for.
    /// </summary>
    public class CastPowerChoiceTests {
        private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject _go;

        [TearDown]
        public void TearDown() {
            if (_go != null) Object.DestroyImmediate(_go);
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
    }
}
