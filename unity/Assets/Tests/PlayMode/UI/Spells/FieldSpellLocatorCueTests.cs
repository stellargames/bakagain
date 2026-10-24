namespace BakAgain.Tests.PlayMode.UI.Spells {
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.UI.Spells;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Spells;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A locator's cue is the SUCCESS sound: <c>audio_play(0xc)</c> / <c>(0xd)</c> sits inside the
    /// roll's success branch, and the failure branch only says "a complete waste of time"
    /// (SPELLFX.C:164-191).
    /// </summary>
    public class FieldSpellLocatorCueTests {
        private readonly List<int> _cues = new();
        private GameObject _mapGo;

        [SetUp]
        public void SetUp() {
            _cues.Clear();
            BakAgain.Audio.MenuSoundService.CueRequested += _cues.Add;
        }

        [TearDown]
        public void TearDown() {
            BakAgain.Audio.MenuSoundService.CueRequested -= _cues.Add;
            if (_mapGo != null) { Object.DestroyImmediate(_mapGo); }
        }

        [UnityTest]
        public IEnumerator AFailedLocatorPlaysNoCue([Values(FieldSpells.EyesOfIshap, FieldSpells.TheUnseen,
            FieldSpells.NacreCicatrix)] int spell) => UniTask.ToCoroutine(async () => {
            SeedSoTheNextRoll(spell, succeeds: false);

            await new FieldSpellCaster(null, null, null, null, null).CastAsync(0, spell, Power, 0);

            Assert.That(_cues, Is.Empty, "a failed roll is silent");
        });

        [UnityTest]
        public IEnumerator ASuccessfulLocatorPlaysItsCue([Values(FieldSpells.EyesOfIshap, FieldSpells.TheUnseen,
            FieldSpells.NacreCicatrix)] int spell) => UniTask.ToCoroutine(async () => {
            _mapGo = new GameObject("LocatorMapUnderTest");
            _mapGo.AddComponent<UIDocument>();
            var map = _mapGo.AddComponent<LocatorMapScreen>();
            _mapGo.SetActive(false);
            SeedSoTheNextRoll(spell, succeeds: true);

            UniTask cast = new FieldSpellCaster(null, null, null, null, map).CastAsync(0, spell, Power, 0);
            Assert.That(_mapGo.activeSelf, Is.True, "a success opens the map");
            Assert.That(_cues, Is.EqualTo(new[] { (int)SpellCastSound.ForCast(spell) }));
            map.PrimaryAction(18);   // close
            await cast;
        });

        /// <summary>A power where both outcomes are possible for every locator (Nacre needs more than 4).</summary>
        private const int Power = 6;

        private static void SeedSoTheNextRoll(int spell, bool succeeds) {
            for (var seed = 1; seed < 10000; seed++) {
                Random.InitState(seed);
                if (FieldSpells.LocatorSucceeds(spell, Random.Range(0, 100), Power) == succeeds) {
                    Random.InitState(seed);
                    return;
                }
            }
            Assert.Fail("no seed gives that roll");
        }
    }
}
