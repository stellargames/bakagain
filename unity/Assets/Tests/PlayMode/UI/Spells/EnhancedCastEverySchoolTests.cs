namespace BakAgain.Tests.PlayMode.UI.Spells {
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.CutScenes;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI.InputCore;
    using BakAgain.UI.Spells;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Character;
    using GameData.Resources.Menu;
    using GameData.Resources.Spells;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The Enhanced spellbook opens every school, and shows a known spell it cannot cast now dull
    /// and inert (TASK-1023). The original gates the schools by the REQ it loads (the field one
    /// disables the four combat schools, CSPELL.C:2173-2178) and never draws an uncastable spell
    /// (cspell_check_castable, CSPELL.C:1568), so a combat spell learned from a scroll was nowhere
    /// to be seen outside a fight. Classic keeps both rules.
    /// </summary>
    public class EnhancedCastEverySchoolTests {
        private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Instance;
        private const int FieldSchool = 4;     // SYMBOL5: Scent of Sarig among them
        private const int CombatSchool = 0;    // SYMBOL1: The Fetters of Rime among them
        private const int ScentOfSarig = 8;

        private EnhancedPrefsStash _stash;
        private GameObject _go, _doc;
        private EnhancedCastLayout _layout;

        [SetUp]
        public void SetUp() => _stash = EnhancedPrefsStash.Take();

        [TearDown]
        public void TearDown() {
            _layout?.Revert();
            if (_go != null) Object.DestroyImmediate(_go);
            if (_doc != null) Object.DestroyImmediate(_doc);
            _stash.Restore();
        }

        private static string School(int i) => $"imagebutton_{CastMenuSelection.FirstSchoolActionId + i}";

        /// <summary>The field REQ's live schools: only the two field ones.</summary>
        private static HashSet<int> FieldReq() => new() {
            CastMenuSelection.FirstSchoolActionId + 4, CastMenuSelection.FirstSchoolActionId + 5,
        };

        private static VisualElement ReqRoot(VisualElement root, out VisualElement stage) {
            stage = new VisualElement();
            root.Add(stage);
            var names = new List<string> { "imagebutton_1" };
            for (int i = 0; i < CastRingLayout.CategoryCount; i++) names.Add(School(i));
            for (int i = 0; i < 3; i++) names.Add($"hotspot_{CastMenuSelection.FirstPartySlotActionId + i}");
            foreach (string n in names) {
                var e = new VisualElement { name = n, pickingMode = PickingMode.Position };
                // The REQ's own Clickable, as the loader adds it; it is the gate that drops the click.
                e.AddManipulator(new Clickable(() => { }));
                stage.Add(e);
            }
            return root;
        }

        private static IReadOnlyList<RingPosition> Ring() =>
            Enumerable.Range(0, 30).Select(i => new RingPosition {
                X = 400 + (int)(300 * System.Math.Sin(i * System.Math.PI / 15)),
                Y = 600 - (int)(300 * System.Math.Cos(i * System.Math.PI / 15)),
            }).ToList();

        private static GameSession Caster(params int[] known) {
            var session = new GameSession();
            typeof(GameSession).GetProperty(nameof(GameSession.IsActive)).GetSetMethod(true).Invoke(session, new object[] { true });
            session.SetActiveParty(1, new byte[] { 0 });
            session.CurrentZone = 1;
            var stats = new ActorStat[16];
            for (int i = 0; i < stats.Length; i++) stats[i] = new ActorStat { Base = 40, Max = 40 };
            session.SetActorStatsForTest(0, stats);
            ushort[] words = SpellBook.Empty();
            foreach (int id in known) SpellBook.Learn(words, id);
            typeof(GameSession).GetField("_actorKnownSpells", Any).SetValue(session, new[] { words });
            return session;
        }

        private static void Send<T>(VisualElement target, EventType type) where T : PointerEventBase<T>, new() {
            var imgui = new Event { type = type, mousePosition = target.worldBound.center, button = 0, clickCount = 1 };
            using (T evt = PointerEventBase<T>.GetPooled(imgui)) {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        [UnityTest]
        public IEnumerator APressOnASchoolTheReqDisables_OpensIt_InEnhanced() {
            GameOptions.Enhanced = true;
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                AssetDatabase.GUIDToAssetPath("e1ac2bea2a13f4a4abb255bc71d7cc98"));
            _doc = new GameObject("~CastScreen");
            UIDocument document = _doc.AddComponent<UIDocument>();
            document.panelSettings = settings;
            VisualElement root = ReqRoot(document.rootVisualElement, out VisualElement stage);
            _layout = new EnhancedCastLayout(root, Caster());
            var pressed = new List<int>();
            _layout.SchoolPressed += pressed.Add;
            Assert.IsTrue(_layout.Apply(stage, Ring(), FieldReq(), combat: false));
            yield return null;
            yield return null;

            VisualElement combat = root.Q(School(CombatSchool));
            Assert.Greater(combat.worldBound.width, 0f, "the stone is laid out");
            Assert.AreSame(combat, combat.panel.Pick(combat.worldBound.center), "the stone takes the press");
            Send<PointerDownEvent>(combat, EventType.MouseDown);
            Send<PointerUpEvent>(combat, EventType.MouseUp);
            CollectionAssert.AreEqual(new[] { CombatSchool }, pressed, "a combat school opens in the field");
            Assert.IsTrue(_layout.IsOpenedSchool(CombatSchool));

            // A school the REQ enables is the REQ's own click: the layout does not answer it twice.
            VisualElement field = root.Q(School(FieldSchool));
            Send<PointerDownEvent>(field, EventType.MouseDown);
            Send<PointerUpEvent>(field, EventType.MouseUp);
            CollectionAssert.AreEqual(new[] { CombatSchool }, pressed);
            Assert.IsFalse(_layout.IsOpenedSchool(FieldSchool));

            // Back to the REQ's placement, the extra click goes with it.
            _layout.Revert();
            _layout = null;
            Send<PointerDownEvent>(combat, EventType.MouseDown);
            Send<PointerUpEvent>(combat, EventType.MouseUp);
            CollectionAssert.AreEqual(new[] { CombatSchool }, pressed, "nothing opens it once reverted");
        }

        [Test]
        public void Classic_KeepsTheReqsSchoolGate() {
            // The faithful screen never applies the layout, so the loader's gate is the only answer:
            // an authored-disabled school takes no click.
            var disabled = new UiElement { ActionId = CastMenuSelection.FirstSchoolActionId, Disabled = 1, Visible = true };
            Assert.IsFalse(UserInterfaceLoader.TakesClicks(disabled, gated: false, navigableOverride: null));
        }

        private async UniTask<(CastScreen Screen, FakePointer Pointer, List<int> Cast)> OpenAsync(bool enhanced,
            int school, GameSession session) {
            var scope = Object.FindAnyObjectByType<VContainer.Unity.LifetimeScope>();
            if (scope == null) Assert.Ignore("no LifetimeScope: no resource cache");
            var cache = (IResourceCache)scope.Container.Resolve(typeof(IResourceCache));
            GameOptions.Enhanced = enhanced;
            var pointer = new FakePointer { IsPresent = true, CanPointOverride = true };
            _go = new GameObject("CastScreenUnderTest");
            _go.SetActive(false);
            var screen = _go.AddComponent<CastScreen>();
            screen.Construct(cache, pointer, null, null, session, null);
            typeof(CastScreen).GetProperty(nameof(CastScreen.CasterId)).GetSetMethod(true).Invoke(screen, new object[] { 0 });
            if (enhanced) {
                _layout = new EnhancedCastLayout(ReqRoot(new VisualElement(), out VisualElement req), session);
                Assert.IsTrue(_layout.Apply(req, Ring(), FieldReq(), combat: false));
                typeof(CastScreen).GetField("_enhanced", Any).SetValue(screen, _layout);
                typeof(CastScreen).GetField("_ring", Any).SetValue(screen, new CastRing("RING.DAT") { Positions = Ring().ToList() });
            }
            typeof(CastScreen).GetField("_school", Any).SetValue(screen, school);
            typeof(CastScreen).GetProperty(nameof(CastScreen.Symbols))
                .SetValue(screen, await cache.GetOrLoadAsync<SpellSymbolLayout>($"SYMBOL{school + 1}.DAT"));
            var cast = new List<int>();
            screen.Committed += (spell, _, _) => cast.Add(spell);
            return (screen, pointer, cast);
        }

        private static UniTask DrawSymbols(CastScreen screen, VisualElement stage) =>
            (UniTask)typeof(CastScreen).GetMethod("DrawSymbolsAsync", Any).Invoke(screen, new object[] { stage });

        private static List<(int X, int Y, int SpellId)> Drawn(CastScreen screen) =>
            (List<(int X, int Y, int SpellId)>)typeof(CastScreen).GetField("_drawnSymbols", Any).GetValue(screen);

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator Enhanced_AKnownSpellInAnOpenedSchool_IsDrawnDullAndAClickDoesNothing() => UniTask.ToCoroutine(async () => {
            GameSession session = Caster(SpellIds.FettersOfRime, ScentOfSarig);
            (CastScreen screen, FakePointer pointer, List<int> cast) = await OpenAsync(true, CombatSchool, session);
            VisualElement ring = _layout.RingSheet;

            await DrawSymbols(screen, ring);

            VisualElement fetters = ring.Q($"spell_symbol_{SpellIds.FettersOfRime}");
            Assert.NotNull(fetters, "the known combat spell is on the ring");
            Assert.IsTrue(fetters.ClassListContains("enhanced-cast__holo--dull"), "drawn dull");
            CollectionAssert.IsEmpty(Drawn(screen), "and not a symbol the hit tests know");

            SpellSymbolNode node = screen.Symbols.Nodes.First(n => n.SpellId == SpellIds.FettersOfRime);
            pointer.ScreenPosition = new Vector2(node.X, UnityEngine.Screen.height - node.Y);
            MethodInfo update = typeof(CastScreen).GetMethod("Update", Any);
            pointer.Primary.SetDown(true, pressedThisFrame: true);
            update.Invoke(screen, null);
            await UniTask.Yield();
            pointer.Primary.SetDown(false, releasedThisFrame: true);
            update.Invoke(screen, null);
            await UniTask.Yield();
            Assert.AreEqual(-1, (int)typeof(CastScreen).GetField("_sliderSpell", Any).GetValue(screen), "no spell chosen");
            Assert.AreEqual(-1, (int)typeof(CastScreen).GetField("_hoveredSpell", Any).GetValue(screen), "no hover");
            CollectionAssert.IsEmpty(cast, "nothing cast");

            // Its name is listed, dimmed like the symbol.
            await screen.ShowSpellNamesAsync();
            Assert.IsTrue(_layout.InfoSheet.Query<Label>().ToList()
                .Any(l => l.ClassListContains("enhanced-cast__holo--dull")), "the dull spell's name is listed dim");
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator Enhanced_ACastableSpellInALiveSchool_IsAsBefore() => UniTask.ToCoroutine(async () => {
            GameSession session = Caster(SpellIds.FettersOfRime, ScentOfSarig);
            (CastScreen screen, _, _) = await OpenAsync(true, FieldSchool, session);

            await DrawSymbols(screen, _layout.RingSheet);

            CollectionAssert.AreEqual(new[] { ScentOfSarig }, Drawn(screen).Select(d => d.SpellId), "castable and live");
            Assert.IsFalse(_layout.RingSheet.Q($"spell_symbol_{ScentOfSarig}").ClassListContains("enhanced-cast__holo--dull"));
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator Classic_AnUncastableSpellIsNotDrawnAtAll() => UniTask.ToCoroutine(async () => {
            GameSession session = Caster(ScentOfSarig);
            var stats = new ActorStat[16];
            for (int i = 0; i < stats.Length; i++) stats[i] = new ActorStat { Base = 1, Max = 40 };
            session.SetActorStatsForTest(0, stats);   // a pool of 2: below Scent of Sarig's cost
            (CastScreen screen, _, _) = await OpenAsync(false, FieldSchool, session);
            var stage = new VisualElement();

            await DrawSymbols(screen, stage);

            Assert.IsNull(stage.Q($"spell_symbol_{ScentOfSarig}"), "the original never shows a spell it cannot cast");
            CollectionAssert.IsEmpty(Drawn(screen));
        });
    }
}
