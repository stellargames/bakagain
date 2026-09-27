namespace BakAgain.Tests.UI.InGame {
    using BakAgain.UI.InGame;
    using NUnit.Framework;

    public class CompassMathTests {
        [Test]
        public void ScrollFraction_Zero_IsZero() {
            Assert.That(CompassMath.ScrollFraction(0), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void ScrollFraction_Half_IsHalf() {
            Assert.That(CompassMath.ScrollFraction(32768), Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void ScrollFraction_AlwaysInUnitRange() {
            foreach (int h in new[] { 0, 1, 16384, 32768, 49152, 65535 }) {
                float f = CompassMath.ScrollFraction((ushort)h);
                Assert.That(f, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f), $"heading {h}");
            }
        }
    }

    // The original never draws the compass in a fight (the arena loads cframe.scx and does not call
    // uiwidget_compass_draw), so the travel HUD hides it while one is on.
    public class CompassViewVisibilityTests {
        [Test]
        public void HidingThenShowing_TogglesTheWindow() {
            var view = new CompassView(new BakAgain.Core.GameSession(), Resources());
            var stage = new UnityEngine.UIElements.VisualElement();
            Build(stage, view);

            view.SetVisible(false);
            Assert.That(Window(stage).style.display.value, Is.EqualTo(UnityEngine.UIElements.DisplayStyle.None));
            view.SetVisible(true);
            Assert.That(Window(stage).style.display.value, Is.EqualTo(UnityEngine.UIElements.DisplayStyle.Flex));
        }

        [Test]
        public void AHideAskedForBeforeTheBuildLands_StillApplies() {
            var view = new CompassView(new BakAgain.Core.GameSession(), Resources());
            var stage = new UnityEngine.UIElements.VisualElement();

            view.SetVisible(false);
            Build(stage, view);

            Assert.That(Window(stage).style.display.value, Is.EqualTo(UnityEngine.UIElements.DisplayStyle.None));
        }

        private static BakAgain.Tests.Editor.Core.Fixtures.FakeResourceProviderService Resources() {
            var resources = new BakAgain.Tests.Editor.Core.Fixtures.FakeResourceProviderService();
            resources.Register("COMPASS.BMX#0", UnityEngine.Sprite.Create(new UnityEngine.Texture2D(4, 1),
                new UnityEngine.Rect(0, 0, 4, 1), UnityEngine.Vector2.zero));
            return resources;
        }

        private static void Build(UnityEngine.UIElements.VisualElement stage, CompassView view) =>
            Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                view.BuildAsync(stage, new UnityEngine.Rect(0, 0, 31, 10), new object()));

        private static UnityEngine.UIElements.VisualElement Window(UnityEngine.UIElements.VisualElement stage) =>
            UnityEngine.UIElements.UQueryExtensions.Q(stage, CompassView.WindowName);
    }
}
