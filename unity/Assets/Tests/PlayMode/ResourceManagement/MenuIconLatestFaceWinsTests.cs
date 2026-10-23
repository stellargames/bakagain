namespace BakAgain.Tests.PlayMode.ResourceManagement {
    using System.Collections;
    using BakAgain.Core;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.Tests.TestSupport;
    using Microsoft.Extensions.Logging.Abstractions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A button wears the face it was asked for LAST, whichever sprite arrives first (TASK-947).
    /// </summary>
    /// <remarks>
    /// A rebuild asks for the lit faces; a screen that gates the entry straight after asks for the bare
    /// stone. When the stone was already loaded it went on at once and the lit face, arriving later,
    /// painted over it: the combat menu's bare stones during an item summon's tile pick kept their icons.
    /// </remarks>
    public class MenuIconLatestFaceWinsTests {
        private const int Lit = 28;                                   // COMBAT's Defend face
        private const int Bare = UserInterfaceLoader.DisabledButtonIcon;

        [UnityTest]
        public IEnumerator ALitFaceArrivingLateDoesNotPaintOverTheBareStoneAskedForAfterIt() {
            ShippedGameData.RequireOrIgnore();
            ResourceManagementInitializer.InitializeResourceManagement();
            var host = new GameObject("~MenuIconHost");
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            try {
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                var warm = new VisualElement();
                var button = new VisualElement();
                document.rootVisualElement.Add(warm);
                document.rootVisualElement.Add(button);
                var icons = new MenuIconLoader(null, NullLogger.Instance);

                icons.LoadAndApplyIcon(warm, Bare);   // the stone is loaded already, as it was live
                for (int frame = 0; frame < 30 && warm.style.backgroundImage.value.sprite == null; frame++) {
                    yield return null;
                }
                Sprite stone = warm.style.backgroundImage.value.sprite;
                Assert.IsNotNull(stone, "the control: the stone loads");

                icons.LoadAndApplyIcon(button, Lit);   // not loaded yet: arrives on a later update
                icons.LoadAndApplyIcon(button, Bare);  // cached: on at once
                for (int frame = 0; frame < 30; frame++) {
                    yield return null;
                }

                Assert.AreSame(stone, button.style.backgroundImage.value.sprite, "the bare stone stays");
                icons.ReleaseHandles();
            } finally {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
            }
        }
    }
}
