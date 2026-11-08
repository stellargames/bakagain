namespace BakAgain.Tests.Editor.UI {
    using System.Reflection;
    using BakAgain.UI.InGame;
    using NUnit.Framework;
    using UnityEngine.UIElements;

    /// <summary>The Enhanced HUD draws the spell plaque only while a rune is up (TASK-1057).</summary>
    public class SpellPlaqueVisibilityTests {
        private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Instance;

        [Test]
        public void EnhancedHidesAnEmptyPlaque_TheFaithfulOneAlwaysShows() {
            var view = new SpellEffectCaptionView(null, null, null);
            var plaque = new VisualElement();
            var runes = new VisualElement();
            typeof(SpellEffectCaptionView).GetField("_plaque", Any).SetValue(view, plaque);
            typeof(SpellEffectCaptionView).GetField("_symbols", Any).SetValue(view, runes);

            view.SetVisible(true, onlyWithRunes: true);
            Assert.IsFalse(plaque.visible, "Enhanced, no spell: no bar");

            runes.Add(new VisualElement());
            view.SetVisible(true, onlyWithRunes: true);
            Assert.IsTrue(plaque.visible, "a spell up: the bar and its rune");

            runes.Clear();
            view.SetVisible(true);
            Assert.IsTrue(plaque.visible, "faithful: the plaque is always drawn");

            view.SetVisible(false, onlyWithRunes: true);
            Assert.IsFalse(plaque.visible);
        }
    }
}
