namespace BakAgain.UI.InputCore {
    using UnityEngine.InputSystem;

    /// <summary>
    /// The click and inspect buttons as the UI sees them (TASK-1080). UI Toolkit's pointer events come from
    /// the scene's <c>InputSystemUIInputModule</c> and its own actions asset ("UI/Click", "UI/RightClick"),
    /// not from ours, so a moved click or inspect is mirrored onto that asset (and onto the adapter's UI map,
    /// whose Click is the Skip): every widget's <c>evt.button</c> and <c>clicked</c> then follows it.
    /// </summary>
    public static class UiPointerBindings {
        private const string LeftButton = "<Mouse>/leftButton", RightButton = "<Mouse>/rightButton";

        /// <summary>Points <paramref name="asset"/>'s UI/Click and UI/RightClick mouse bindings at the moved
        /// buttons (null, or the button itself: back to the physical left and right buttons).</summary>
        public static void Mirror(InputActionAsset asset, string select, string inspect) {
            if (asset == null) {
                return;
            }
            Point(asset.FindAction("UI/Click"), LeftButton, select);
            Point(asset.FindAction("UI/RightClick"), RightButton, inspect);
        }

        private static void Point(InputAction action, string physical, string path) {
            if (action == null) {
                return;
            }
            for (int i = 0; i < action.bindings.Count; i++) {
                if (action.bindings[i].path != physical) {
                    continue;
                }
                if (path == null || path == physical) {
                    action.RemoveBindingOverride(i);
                } else {
                    action.ApplyBindingOverride(i, path);
                }
                return;
            }
        }
    }
}
