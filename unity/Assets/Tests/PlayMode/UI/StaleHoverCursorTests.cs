namespace BakAgain.Tests.PlayMode.UI {
    using System.Reflection;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI.Cursor;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Cursor;
    using GameData.Resources.Menu;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// A rebuild takes the hovered hotspot away without a PointerLeave, so it must let go of its
    /// cursor itself.
    /// </summary>
    /// <remarks>
    /// Measured 2026-10-02: clicking Romney's tavern door left the "Tavern" word-cursor over the
    /// Black Sheep Tavern, where nothing lies under the pointer; the original shows the arrow.
    /// </remarks>
    public class StaleHoverCursorTests {
        private GameObject _go;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        private sealed class RecordingCursor : ICursorManager {
            public int Last = int.MinValue;
            public void SelectSet(string setName) { }
            public void SetByIndex(int index) => Last = index;
            public void Set(GameCursor cursor) { }
            public void Hide() { }
            public void Show() { }
            public void WarpTo(Vector2 canonicalPosition) { }
            public Vector2 CanonicalPosition => Vector2.zero;
        }

        [Test]
        public void ARebuildDropsTheHoveredHotspotsCursor() {
            _go = new GameObject("loader");
            _go.SetActive(false);   // OnEnable would load by address
            var loader = _go.AddComponent<UserInterfaceLoader>();
            var cursor = new RecordingCursor();
            loader.Construct(cursor, null);
            typeof(UserInterfaceLoader).GetField("_userInterface", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(loader, new UserInterface("REQ_TEST") { MenuEntries = System.Array.Empty<UiElement>() });
            cursor.SetByIndex(7);   // "Tavern", as the hover over the door left it

            // The rest of the build needs a whole screen (layout, icons, labels) and fails here; the
            // reset comes straight after the stage is cleared, before any of that.
            try {
                var build = (UniTask)typeof(UserInterfaceLoader)
                    .GetMethod("BuildInto", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(loader, new object[] { new VisualElement() });
                build.GetAwaiter().GetResult();
            } catch (System.NullReferenceException) {
                // expected: no layout behind this bare loader
            }

            Assert.AreEqual(-1, cursor.Last, "the new scene starts with the default arrow");
        }
    }
}
