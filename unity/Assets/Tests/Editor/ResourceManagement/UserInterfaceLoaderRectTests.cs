namespace BakAgain.Tests.ResourceManagement {
    using BakAgain.ResourceManagement.Loaders;
    using GameData.Resources.Menu;
    using NUnit.Framework;
    using UnityEngine;

    public class UserInterfaceLoaderRectTests {
        [Test]
        public void TryGetElementRect_ReturnsReqRect_ForKnownActionId() {
            // UserInterface requires an id via its constructor and MenuEntries is UiElement[]
            // (not List<UiElement>), so the object initializer is adapted accordingly; the
            // point of the test is still to exercise the pure TryGetRect helper.
            var ui = new UserInterface("test") {
                MenuEntries = new[] {
                    new UiElement { ActionId = 192, XPosition = 65, YPosition = 66, Width = 1470, Height = 606 },
                    new UiElement { ActionId = 75, XPosition = 630, YPosition = 732, Width = 80, Height = 78 },
                },
            };
            Assert.IsTrue(UserInterfaceLoader.TryGetRect(ui, 192, out Rect r));
            Assert.AreEqual(new Rect(65, 66, 1470, 606), r);
            Assert.IsTrue(UserInterfaceLoader.TryGetRect(ui, 75, out Rect a));
            Assert.AreEqual(new Rect(630, 732, 80, 78), a);
        }

        [Test]
        public void TryGetElementRect_False_ForUnknownActionId() {
            var ui = new UserInterface("test") { MenuEntries = System.Array.Empty<UiElement>() };
            Assert.IsFalse(UserInterfaceLoader.TryGetRect(ui, 999, out Rect r));
            Assert.AreEqual(default(Rect), r);
        }
    }
}
