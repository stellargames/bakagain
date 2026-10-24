namespace BakAgain.Tests.PlayMode.UI.Spells {
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.CutScenes;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI.InputCore;
    using BakAgain.UI.Spells;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Character;
    using GameData.Resources.Spells;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The Enhanced cast screen under a finger and a mouse, driven through the screen's own per-frame
    /// pointer handling (TASK-878 round 10, the owner on a phone): a tap only chooses a spell and
    /// Cast casts it; a tap on another symbol or off the ring leaves a power choice; and the gems up
    /// to the chosen power are lit apart from the rest of the band.
    /// </summary>
    public class EnhancedCastTouchTests {
        private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Instance;

        // Canonical spots: the ring is a circle of radius 300 round (400,600); the symbols sit well
        // inside it, further than a finger's reach (CastRingLayout.TouchReach) from any gem.
        private const int Fixed = 8;       // Scent of Sarig: cost 5 only
        private const int Ranged = 18;     // Nacre Cicatrix: 5-14
        private const int Wide = 3;        // 1-20
        private static readonly Vector2Int FixedAt = new(400, 600);
        private static readonly Vector2Int RangedAt = new(400, 700);
        private static readonly Vector2Int WideAt = new(400, 500);
        private static readonly Vector2Int OffTheRing = new(1300, 100);

        private EnhancedPrefsStash _stash;
        private GameObject _go;
        private EnhancedCastLayout _layout;

        [SetUp]
        public void SetUp() => _stash = EnhancedPrefsStash.Take();

        [TearDown]
        public void TearDown() {
            _layout?.Revert();
            if (_go != null) Object.DestroyImmediate(_go);
            _stash.Restore();
        }

        private sealed class Catalogue : IResourceCache {
            private readonly SpellList _spells = new("SPELLS.DAT");
            private readonly SpellDescriptions _docs = new("SPELLDOC.DAT");

            public Catalogue() {
                foreach ((int id, int min, int max) in new[] { (Fixed, 5, 5), (Ranged, 5, 14), (Wide, 1, 20) }) {
                    _spells.Spells[id] = new Spell($"spell{id}") { MinimumCost = min, MaximumCost = max };
                    _docs.Spells.Add(new SpellDescription { SpellNumber = id, Name = $"spell {id}" });
                }
            }

            public UniTask<T> GetOrLoadAsync<T>(string key) where T : class =>
                UniTask.FromResult(_spells as T ?? _docs as T);

            public void Clear() { }
        }

        private static GameSession Caster() {
            var session = new GameSession();
            typeof(GameSession).GetProperty(nameof(GameSession.IsActive)).GetSetMethod(true).Invoke(session, new object[] { true });
            session.SetActiveParty(1, new byte[] { 0 });
            var stats = new ActorStat[16];
            for (int i = 0; i < stats.Length; i++) stats[i] = new ActorStat { Base = 40, Max = 40 };
            session.SetActorStatsForTest(0, stats);   // a pool of 80: every band above is affordable
            return session;
        }

        private static IReadOnlyList<RingPosition> RingPositions() =>
            Enumerable.Range(0, 30).Select(i => new RingPosition {
                X = 400 + (int)(300 * System.Math.Sin(i * System.Math.PI / 15)),
                Y = 600 - (int)(300 * System.Math.Cos(i * System.Math.PI / 15)),
            }).ToList();

        /// <summary>The Enhanced cast screen on a caster, with three symbols on the ring.</summary>
        private (CastScreen Screen, FakePointer Pointer, List<(int Spell, int Power)> Cast) Open(bool finger) {
            GameOptions.Enhanced = true;
            var pointer = new FakePointer { IsPresent = !finger, CanPointOverride = true };
            _go = new GameObject("CastScreenUnderTest");
            _go.SetActive(false);
            var screen = _go.AddComponent<CastScreen>();
            GameSession session = Caster();
            screen.Construct(new Catalogue(), pointer, null, null, session, null);
            typeof(CastScreen).GetProperty(nameof(CastScreen.CasterId)).GetSetMethod(true).Invoke(screen, new object[] { 0 });

            _layout = new EnhancedCastLayout(ReqRoot(out VisualElement stage), session);
            var ring = new CastRing("RING.DAT") { Positions = RingPositions().ToList() };
            Assert.IsTrue(_layout.Apply(stage, ring.Positions, new HashSet<int>(), combat: false));
            typeof(CastScreen).GetField("_enhanced", Any).SetValue(screen, _layout);
            typeof(CastScreen).GetField("_ring", Any).SetValue(screen, ring);
            var symbols = (List<(int X, int Y, int SpellId)>)typeof(CastScreen).GetField("_drawnSymbols", Any).GetValue(screen);
            symbols.Add((FixedAt.x, FixedAt.y, Fixed));
            symbols.Add((RangedAt.x, RangedAt.y, Ranged));
            symbols.Add((WideAt.x, WideAt.y, Wide));

            var cast = new List<(int, int)>();
            screen.Committed += (spell, power, _) => cast.Add((spell, power));
            return (screen, pointer, cast);
        }

        /// <summary>The REQ_CAST elements the Enhanced layout moves.</summary>
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

        private static void Frame(CastScreen screen) => typeof(CastScreen).GetMethod("Update", Any).Invoke(screen, null);

        /// <summary>A canonical point as the pointer reports it: the layout's sheet is in no panel, so
        /// the screen reads it back as the canonical point itself (CanonicalConversion's no-panel arm).</summary>
        private static Vector2 At(Vector2Int canonical) => new(canonical.x, Screen.height - canonical.y);

        /// <summary>A press and its release at one point, a frame each, through the screen's Update.</summary>
        private static IEnumerator Tap(CastScreen screen, FakePointer p, Vector2Int at) {
            p.ScreenPosition = At(at);
            p.Primary.SetDown(true, pressedThisFrame: true);
            Frame(screen);
            yield return null;
            p.Primary.SetDown(true);
            Frame(screen);
            yield return null;
            p.Primary.SetDown(false, releasedThisFrame: true);
            Frame(screen);
            yield return null;
            p.Primary.SetDown(false);
            Frame(screen);
            yield return null;
            yield return null;
        }

        private static int Field(CastScreen screen, string name) => (int)typeof(CastScreen).GetField(name, Any).GetValue(screen);

        private static void OpenPowerChoice(CastScreen screen, int spell, int min, int max) =>
            screen.BeginPowerSelection(spell, min, max);

        // A. "Tapping Scent of Sarig casts it immediately": a fixed cost had nothing to choose, so the
        // pick committed on the spot. On touch the tap chooses; the Cast tablet casts.
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator Finger_ATapOnAFixedCostSpell_ChoosesItAndCastsNothing() {
            (CastScreen screen, FakePointer p, List<(int, int)> cast) = Open(finger: true);

            yield return Tap(screen, p, FixedAt);

            CollectionAssert.IsEmpty(cast, "a tap casts nothing");
            Assert.AreEqual(Fixed, Field(screen, "_sliderSpell"), "the spell is chosen");
            Assert.AreEqual(CastRingLayout.PositionForPower(5), Field(screen, "_hoveredPosition"),
                "at its one power, so Cast has something to cast");
        }

        // The mouse keeps the original's flow: a click on a fixed-cost symbol casts it.
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator Mouse_AClickOnAFixedCostSpell_StillCastsIt() {
            (CastScreen screen, FakePointer p, List<(int, int)> cast) = Open(finger: false);

            yield return Tap(screen, p, FixedAt);

            CollectionAssert.AreEqual(new[] { (Fixed, 5) }, cast);
        }

        // B. A tap on another symbol leaves the power choice and chooses that spell instead, as a
        // school tap leaves it and switches school.
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator Finger_ATapOnAnotherSymbol_CancelsTheChoiceAndChoosesThatSpell() {
            (CastScreen screen, FakePointer p, List<(int, int)> cast) = Open(finger: true);
            OpenPowerChoice(screen, Wide, 1, 20);

            yield return Tap(screen, p, RangedAt);

            Assert.AreEqual(Ranged, Field(screen, "_sliderSpell"), "the tapped spell is the one being chosen now");
            CollectionAssert.IsEmpty(cast, "nothing cast, and no cancel reported: the screen stays up");
        }

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator Mouse_AClickOnAnotherSymbol_CancelsTheChoiceAndChoosesThatSpell() {
            (CastScreen screen, FakePointer p, List<(int, int)> cast) = Open(finger: false);
            OpenPowerChoice(screen, Wide, 1, 20);

            yield return Tap(screen, p, RangedAt);

            Assert.AreEqual(Ranged, Field(screen, "_sliderSpell"));
            CollectionAssert.IsEmpty(cast);
        }

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator ATapOffTheRing_CancelsThePowerChoice([Values(true, false)] bool finger) {
            (CastScreen screen, FakePointer p, List<(int, int)> cast) = Open(finger);
            OpenPowerChoice(screen, Wide, 1, 20);

            yield return Tap(screen, p, OffTheRing);

            Assert.AreEqual(-1, Field(screen, "_sliderSpell"), "back to picking a spell");
            CollectionAssert.IsEmpty(cast);
        }

        // The control: a finger that chose a power on the ring and lifted off it keeps the choice
        // (the Enhanced screen's "keep" rule); only a tap that STARTED off the ring cancels.
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator Finger_SlidingOffTheRingAfterChoosingAPower_KeepsTheChoice() {
            (CastScreen screen, FakePointer p, List<(int, int)> cast) = Open(finger: true);
            OpenPowerChoice(screen, Wide, 1, 20);
            RingPosition gem = RingPositions()[9];

            p.ScreenPosition = At(new Vector2Int(gem.X, gem.Y));
            p.Primary.SetDown(true, pressedThisFrame: true);
            Frame(screen);
            yield return null;
            p.ScreenPosition = At(OffTheRing);
            p.Primary.SetDown(true);
            Frame(screen);
            yield return null;
            p.Primary.SetDown(false, releasedThisFrame: true);
            Frame(screen);
            yield return null;

            Assert.AreEqual(Wide, Field(screen, "_sliderSpell"), "still choosing");
            Assert.AreEqual(9, Field(screen, "_hoveredPosition"), "at the power chosen on the ring");
            CollectionAssert.IsEmpty(cast);
        }

        // C. Three gem states while a power is chosen: every gem up to the power (those below the
        // spell's minimum too) in the chosen colour, the rest of the band gold, the others dim.
        [Test]
        public void TheGemsUpToTheChosenPower_AreLitApartFromTheRestOfTheBand() {
            (CastScreen screen, _, _) = Open(finger: false);
            OpenPowerChoice(screen, Ranged, 5, 14);
            typeof(CastScreen).GetField("_hoveredPosition", Any).SetValue(screen, CastRingLayout.PositionForPower(10));
            MethodInfo paint = typeof(CastScreen).GetMethod("PaintGem", Any);

            string[] gems = Enumerable.Range(0, 30).Select(i => {
                var gem = new VisualElement();
                paint.Invoke(screen, new object[] { gem, i });
                return (string)gem.userData;
            }).ToArray();

            for (int i = 0; i < 9; i++) Assert.AreEqual("gem-chosen", gems[i], $"gem {i}, below the power");
            Assert.AreEqual("gem-hover", gems[9], "the power itself");
            for (int i = 10; i < 14; i++) Assert.AreEqual("gem-lit", gems[i], $"gem {i}, affordable but not chosen");
            for (int i = 14; i < 30; i++) StringAssert.EndsWith("dim", gems[i], $"gem {i}, outside the band");
        }
    }
}
