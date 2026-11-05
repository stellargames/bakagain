namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using BakAgain.ResourceManagement.Loaders;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A REQ entry's left click fires with Shift held, which is what Shift+click on a foe (Swing) and on a
    /// portrait (the character sheet) rely on.
    /// </summary>
    /// <remarks>
    /// UI Toolkit's default activation filter wants the modifiers EXACTLY as given, none, so a Clickable
    /// ignored a Shift+click. Linux dev build, 2026-11-05: Gorath's Shift+click on a moredhel never reached
    /// the world-viewport hotspot (its press bubbled past the Clickable with m_ActivePointerId -1); Owyn's
    /// a moment earlier had got through only because Shift had not registered yet.
    /// </remarks>
    public class ShiftClickTests {
        [UnityTest]
        public IEnumerator APrimaryClickFiresWithAndWithoutShift_TheDefaultClickableOnlyWithout() {
            var host = new GameObject("ShiftClickDoc");
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            var ours = new VisualElement { style = { width = 50, height = 50 } };
            var plain = new VisualElement { style = { width = 50, height = 50 } };
            doc.rootVisualElement.Add(ours);
            doc.rootVisualElement.Add(plain);
            int oursClicks = 0, plainClicks = 0;
            ours.AddManipulator(UserInterfaceLoader.PrimaryClickable(() => oursClicks++));
            plain.AddManipulator(new Clickable(() => plainClicks++));
            yield return null;
            try {
                Click(plain, EventModifiers.Shift);
                Assert.AreEqual(0, plainClicks, "control: UI Toolkit's own Clickable ignores a Shift+click");
                Click(ours, EventModifiers.None);
                Click(ours, EventModifiers.Shift);
                Assert.AreEqual(2, oursClicks, "a plain click and a Shift+click both fire");
            } finally {
                Object.DestroyImmediate(host);
            }
        }

        private static void Click(VisualElement target, EventModifiers modifiers) {
            Vector2 center = target.worldBound.center;
            using (PointerDownEvent down = PointerDownEvent.GetPooled(
                       new Event { type = EventType.MouseDown, mousePosition = center, button = 0, modifiers = modifiers })) {
                down.target = target;
                target.SendEvent(down);
            }
            using (PointerUpEvent up = PointerUpEvent.GetPooled(
                       new Event { type = EventType.MouseUp, mousePosition = center, button = 0, modifiers = modifiers })) {
                up.target = target;
                target.SendEvent(up);
            }
        }
    }
}
