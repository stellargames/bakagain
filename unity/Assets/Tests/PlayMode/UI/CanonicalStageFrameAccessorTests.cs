namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.UI;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine.UIElements;

    public class CanonicalStageFrameAccessorTests {
        // Deliberately not 1600x1200: a value that could not arise from the canonical
        // fallback, so the assertions cannot pass by accident.
        private static DesignFrame WideFillFrame() {
            return new DesignFrame { Width = 2560, Height = 1080, Fit = LayoutFit.Fill };
        }

        [Test]
        public void TryGetFrame_ReturnsTheFrameTheStageWasBuiltWith_IncludingFit() {
            var root = new VisualElement();
            VisualElement stage = CanonicalStage.GetOrCreate(root, WideFillFrame());

            Assert.IsTrue(CanonicalStage.TryGetFrame(stage, out DesignFrame frame, out bool isFallback));
            Assert.AreEqual(2560, frame.Width);
            Assert.AreEqual(1080, frame.Height);
            Assert.AreEqual(LayoutFit.Fill, frame.Fit, "Fit is the whole reason this accessor exists.");
            Assert.IsFalse(isFallback, "A real frame was supplied, so this is not the fallback.");
        }

        [Test]
        public void TryGetFrame_ReportsTheCanonicalFallback_WhenTheCallerHadNoResource() {
            var root = new VisualElement();
            VisualElement stage = CanonicalStage.GetOrCreate(root, null);

            Assert.IsTrue(CanonicalStage.TryGetFrame(stage, out DesignFrame frame, out bool isFallback));
            Assert.AreEqual(LayoutFit.Contain, frame.Fit);
            Assert.IsTrue(isFallback, "Provenance must survive the accessor — Task 3 relies on it.");
        }

        [Test]
        public void TryGetFrame_SeesTheUpgrade_WhenARealFrameLaterBeatsTheFallback() {
            var root = new VisualElement();
            VisualElement stage = CanonicalStage.GetOrCreate(root, null);
            CanonicalStage.GetOrCreate(root, WideFillFrame());

            Assert.IsTrue(CanonicalStage.TryGetFrame(stage, out DesignFrame frame, out bool isFallback));
            Assert.AreEqual(LayoutFit.Fill, frame.Fit, "Real-beats-fallback must be visible through the accessor.");
            Assert.IsFalse(isFallback);
        }

        [Test]
        public void TryGetFrame_RefusesAnElementThatIsNotAStage() {
            Assert.IsFalse(CanonicalStage.TryGetFrame(null, out _, out _));
            Assert.IsFalse(CanonicalStage.TryGetFrame(new VisualElement(), out _, out _));
        }

        [Test]
        public void TryGetFrame_ReturnsACopyThatCannotMutateTheStagesInternalState() {
            var root = new VisualElement();
            VisualElement stage = CanonicalStage.GetOrCreate(root, WideFillFrame());

            Assert.IsTrue(CanonicalStage.TryGetFrame(stage, out DesignFrame first, out _));
            first.Width = 1;
            first.Height = 1;
            first.Fit = LayoutFit.Contain;

            Assert.IsTrue(CanonicalStage.TryGetFrame(stage, out DesignFrame second, out _));
            Assert.AreEqual(2560, second.Width, "mutating the returned frame must not reach the stage's state");
            Assert.AreEqual(1080, second.Height, "mutating the returned frame must not reach the stage's state");
            Assert.AreEqual(LayoutFit.Fill, second.Fit, "mutating the returned frame must not reach the stage's state");
        }
        [Test]
        public void TheStageDoesNotPICK_soAClickOnNoEntryFallsThrough() {
            // *** THE COMBAT HUD ATE EVERY ARENA CLICK BECAUSE OF THIS DEFAULT. *** VisualElement
            // is PickingMode.Position unless told otherwise, so the stage picked over its whole
            // area. For a pushed screen that was harmless — the document beneath is deactivated —
            // but the combat HUD is raised with SetActive rather than a push, so its stage sat
            // above the live travel screen and swallowed target selection, corpse looting and tile
            // picking alike. Nothing logged, because a stage is not a button. See TASK-267.
            var root = new VisualElement();
            VisualElement stage = CanonicalStage.GetOrCreate(root, WideFillFrame());

            Assert.AreEqual(PickingMode.Ignore, stage.pickingMode);
        }

        [Test]
        public void AChildOfTheStageStillPicksNormally() {
            // The other half: Ignore on a parent does not disable its children, which is why the
            // combat buttons keep working. Asserted so a future "fix" that sets the whole subtree
            // cannot pass.
            var root = new VisualElement();
            VisualElement stage = CanonicalStage.GetOrCreate(root, WideFillFrame());
            var button = new VisualElement { pickingMode = PickingMode.Position };
            stage.Add(button);

            Assert.AreEqual(PickingMode.Position, button.pickingMode);
        }

    }
}
