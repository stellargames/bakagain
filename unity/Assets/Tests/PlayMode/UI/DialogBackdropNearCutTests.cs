namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.World.Rendering;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// TASK-1059 (owner): while a dialog page shows the world through its window, nothing stands
    /// between the camera and the speaker. A tree beside the camera showed the near fade's screen-door
    /// dither as a coarse checker block in the backdrop; the page now removes every sprite nearer than
    /// the fade's whole-from distance outright, and the travel view gets its fade back when the page goes.
    /// </summary>
    public class DialogBackdropNearCutTests {
        private GameObject _go;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_go != null) Object.DestroyImmediate(_go);
            if (_panelSettings != null) Object.DestroyImmediate(_panelSettings);
            WorldRenderProfile.ApplySpriteNearFade();
        }

        private static float Hide => Shader.GetGlobalFloat(WorldRenderProfile.SpriteNearHideId);
        private static float Show => Shader.GetGlobalFloat(WorldRenderProfile.SpriteNearShowId);

        [Test]
        public void APageOverTheWorldCutsTheNearSpritesWithoutAFade_AndTheFadeComesBackAfter() {
            _go = new GameObject("PageOverTheWorld");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            document.panelSettings = _panelSettings;
            VisualElement root = document.rootVisualElement;

            WorldRenderProfile.ApplySpriteNearFade();
            float hide = Hide, show = Show;
            Assert.Greater(show - hide, 1f, "control: the travel view fades near sprites over a range");

            var page = new VisualElement { name = "test-page" };
            root.Add(page);
            BakAgain.UI.DialogManager.HideWorldUnder(page);

            Assert.AreEqual(show, Show, 1e-4f, "whole from the same distance");
            Assert.Less(Show - Hide, 0.01f, "and nothing nearer drawn at all: no dithered band");

            page.RemoveFromHierarchy();
            Assert.AreEqual(hide, Hide, 1e-4f, "the fade comes back with the page gone");
            Assert.AreEqual(show, Show, 1e-4f);
        }
    }
}
