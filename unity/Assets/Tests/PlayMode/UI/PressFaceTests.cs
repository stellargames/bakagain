namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using BakAgain.ResourceManagement.Loaders;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A REQ button's +1 face is its PRESSED face, not its hover face.
    /// </summary>
    /// <remarks>
    /// menupage_draw_entries passes <c>(entry == g_pMenuPressAnchor) ? g_wMenuDragSubMode : 0</c>
    /// (MENUPAGE.C:168-169), and the poll sets the anchor only while the button is held on the entry
    /// (MENUPAGE.C:438-491). Measured in combat: the original's shield stays on face 28 under the
    /// pointer; the port lit face 29 on every hover.
    /// </remarks>
    public class PressFaceTests {
        [UnityTest]
        public IEnumerator HoverLeavesTheFaceAndAPressLightsIt() {
            var host = new GameObject("PressFaceDoc");
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            var button = new VisualElement();
            doc.rootVisualElement.Add(button);
            yield return null;

            bool? lit = null;
            UserInterfaceLoader.RegisterPressFace(button, pressed => lit = pressed);
            try {
                using (var enter = PointerEnterEvent.GetPooled()) {
                    enter.target = button;
                    button.SendEvent(enter);
                }
                Assert.IsNull(lit, "hovering changes nothing");

                using (var down = PointerDownEvent.GetPooled()) {
                    down.target = button;
                    button.SendEvent(down);
                }
                Assert.AreEqual(true, lit, "the press lights it");

                using (var up = PointerUpEvent.GetPooled()) {
                    up.target = button;
                    button.SendEvent(up);
                }
                Assert.AreEqual(false, lit, "and the release puts it back");
            } finally {
                Object.DestroyImmediate(host);
            }
        }
    }
}
