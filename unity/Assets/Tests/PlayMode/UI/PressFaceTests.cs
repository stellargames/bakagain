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
            doc.panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
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

        // GitHub #3 / TASK-1000: the main menu's TextButtons had no pressed look. The original has one:
        // widget_draw_text_button with `pressed` set fills pen 0x0b instead of 0x0e, swaps the outline and
        // edge pens (1 and 4) and draws the label in pen 6 (WIDGET.C:351-403), and it is lit by the same
        // press anchor as the image buttons. A gated button draws its greyed label and is never pressed.
        [UnityTest]
        public IEnumerator ATextButtonWearsThePressedLookOnlyWhileHeld() {
            var host = new GameObject("TextPressDoc");
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            var button = new Button();
            var gatedButton = new Button();
            doc.rootVisualElement.Add(button);
            doc.rootVisualElement.Add(gatedButton);
            yield return null;
            try {
                UserInterfaceLoader.RegisterTextPressFace(button, () => false);
                UserInterfaceLoader.RegisterTextPressFace(gatedButton, () => true);

                Send<PointerDownEvent>(button);
                Assert.IsTrue(button.ClassListContains(UserInterfaceLoader.TextButtonPressedClass), "held: pressed");
                Send<PointerUpEvent>(button);
                Assert.IsFalse(button.ClassListContains(UserInterfaceLoader.TextButtonPressedClass), "released: plain");

                Send<PointerDownEvent>(gatedButton);
                Assert.IsFalse(gatedButton.ClassListContains(UserInterfaceLoader.TextButtonPressedClass), "gated: never");
            } finally {
                Object.DestroyImmediate(host);
            }
        }

        private static void Send<T>(VisualElement target) where T : PointerEventBase<T>, new() {
            using (T e = PointerEventBase<T>.GetPooled()) {
                e.target = target;
                target.SendEvent(e);
            }
        }
    }
}
