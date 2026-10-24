namespace BakAgain.Tests.PlayMode.UI.Spells {
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.CutScenes;
    using BakAgain.UI.Spells;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Character;
    using GameData.Resources.Spells;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// A school switch on the cast screen sounds 27_MORPH once the new symbols are loaded, and the
    /// first school the screen opens on does not: <c>if (school != prevSchool) audio_play(0x1b)</c>
    /// with <c>prevSchool</c> seeded from the first school (CSPELL.C:2331-2345). Both the faithful
    /// and the Enhanced screen switch through <c>PrimaryAction</c>.
    /// </summary>
    public class CastSchoolMorphCueTests {
        private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Instance;
        private const int MorphCue = 27;
        private readonly List<int> _cues = new();
        private GameObject _go;

        [SetUp]
        public void SetUp() {
            _cues.Clear();
            BakAgain.Audio.MenuSoundService.CueRequested += _cues.Add;
        }

        [TearDown]
        public void TearDown() {
            BakAgain.Audio.MenuSoundService.CueRequested -= _cues.Add;
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        [UnityTest]
        public IEnumerator SwitchingSchoolPlaysTheMorphOnce() => UniTask.ToCoroutine(async () => {
            CastScreen screen = Screen(school: 4);

            screen.PrimaryAction(CastMenuSelection.FirstSchoolActionId + 2);
            await Loaded(screen);

            Assert.That(_cues, Is.EqualTo(new[] { MorphCue }));
        });

        [UnityTest]
        public IEnumerator TheFirstSchoolIsNotASwitch() => UniTask.ToCoroutine(async () => {
            CastScreen screen = Screen(school: -1);

            screen.PrimaryAction(CastMenuSelection.FirstSchoolActionId + 2);
            await Loaded(screen);

            Assert.That(_cues, Is.Empty);
        });

        [Test]
        public void RePickingTheSchoolShowingIsSilent() {
            CastScreen screen = Screen(school: 2);

            screen.PrimaryAction(CastMenuSelection.FirstSchoolActionId + 2);

            Assert.That(_cues, Is.Empty);
        }

        private CastScreen Screen(int school) {
            var session = new GameSession();
            typeof(GameSession).GetProperty(nameof(GameSession.IsActive)).GetSetMethod(true)
                .Invoke(session, new object[] { true });
            session.SetActiveParty(1, new byte[] { 0 });
            var stats = new ActorStat[16];
            for (int i = 0; i < stats.Length; i++) stats[i] = new ActorStat { Base = 10, Max = 10 };
            session.SetActorStatsForTest(0, stats);

            _go = new GameObject("CastScreenUnderTest");   // active, with no REQ loader: OnEnable returns
            var screen = _go.AddComponent<CastScreen>();
            screen.Construct(new Symbols(), null, null, null, session, null);
            typeof(CastScreen).GetField("_school", Any).SetValue(screen, school);
            return screen;
        }

        private static async UniTask Loaded(CastScreen screen) {
            for (var frame = 0; frame < 600 && screen.Symbols == null; frame++) {
                await UniTask.Yield();
            }
            Assert.That(screen.Symbols, Is.Not.Null, "the new school's symbols never loaded");
            await UniTask.Yield();
        }

        private sealed class Symbols : IResourceCache {
            public UniTask<T> GetOrLoadAsync<T>(string key) where T : class =>
                UniTask.FromResult(typeof(T) == typeof(SpellSymbolLayout) ? new SpellSymbolLayout(key) as T : null);

            public void Clear() { }
        }
    }
}
