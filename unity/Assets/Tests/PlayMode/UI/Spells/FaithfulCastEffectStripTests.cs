namespace BakAgain.Tests.PlayMode.UI.Spells {
    using System.Collections;
    using BakAgain.Core;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI.InGame;
    using BakAgain.UI.Spells;
    using GameData.Resources.Spells;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The faithful field cast screen keeps the running-spell runes on its top rod (TASK-928). The
    /// original presents only the world-view rect of cast.scx (CSPELL.C:2115-2121), so the travel
    /// frame around it stays on screen — with the plaque and runes spellfx_draw_events_caption drew
    /// there (SPELLFX.C:379-404, called by the travel loop, WORLDLP.C:126).
    /// </summary>
    public class FaithfulCastEffectStripTests {
        private EnhancedPrefsStash _stash;
        private GameObject _go;
        private GameSession _session;
        private int _savedMask;

        [SetUp]
        public void SetUp() => _stash = EnhancedPrefsStash.Take();   // Enhanced off: the faithful screen

        [TearDown]
        public void TearDown() {
            if (_go != null) _go.SetActive(false);
            if (_session != null) _session.PaletteEventMask = _savedMask;
            _stash.Restore();
        }

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator TheFieldCastScreenShowsTheRunningSpellRunes() {
            var scope = Object.FindAnyObjectByType<VContainer.Unity.LifetimeScope>();
            Assert.IsNotNull(scope, "the test scene's container");
            var screen = (CastScreen)scope.Container.Resolve(typeof(CastScreen));
            _session = (GameSession)scope.Container.Resolve(typeof(GameSession));
            _savedMask = _session.PaletteEventMask;
            // Two effects running, as after Scent of Sarig and Dragon's Breath in the side-by-side.
            _session.PaletteEventMask = SpellPaletteEvents.BitFor(0) | SpellPaletteEvents.BitFor(1);

            _go = screen.gameObject;
            screen.CombatCaster = null;   // a field cast
            _go.SetActive(true);

            var loader = _go.GetComponent<UserInterfaceLoader>();
            VisualElement runes = null;
            float deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline) {
                runes = loader.IsBuilt ? _go.GetComponent<UIDocument>().rootVisualElement
                    .Q(SpellEffectCaptionView.SymbolsName) : null;
                if (runes != null && runes.childCount > 0) break;
                yield return null;
            }

            Assert.IsNotNull(runes, "the cast screen draws the effect strip");
            Assert.IsNotNull(_go.GetComponent<UIDocument>().rootVisualElement
                .Q(SpellEffectCaptionView.PlaqueName), "on its plaque");
            Assert.AreEqual(2, runes.childCount, "one rune per running effect");
        }
    }
}
