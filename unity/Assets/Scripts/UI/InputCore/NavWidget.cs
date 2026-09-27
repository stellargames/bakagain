namespace BakAgain.UI.InputCore {
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;

    // One keyboard/gamepad-navigable widget in a NavigableLayer, built by a loader
    // (UserInterfaceLoader / DialogManager) from a UiElement or choice button. The layer drives
    // focus + activation over these; Primary/Secondary are the same delegates the mouse path invokes.
    public sealed class NavWidget {
        private readonly Func<Rect> _canonicalRect;

        // Static rect — REQ menus, whose coords come straight from the DAT and are already canonical.
        public NavWidget(VisualElement element, string label, Rect canonicalRect, Action primary, Action secondary, int actionId = -1)
            : this(element, label, () => canonicalRect, primary, secondary, actionId) {
        }

        // Live rect — surfaces whose geometry only resolves after layout (dialog choice buttons). A
        // value captured at construction would be the pre-layout (0,0,0,0); reading it lazily yields the
        // resolved rect when spatial nav / cursor warp actually need it.
        public NavWidget(VisualElement element, string label, Func<Rect> canonicalRect, Action primary, Action secondary, int actionId = -1) {
            Element = element;
            Label = label;
            _canonicalRect = canonicalRect;
            Primary = primary;
            Secondary = secondary;
            ActionId = actionId;
        }

        public VisualElement Element { get; }
        public string Label { get; }
        // Canonical 1600x1200 px rect; used as the spatial-nav origin/target (matches menu_navigateFocus).
        public Rect CanonicalRect => _canonicalRect();
        public Action Primary { get; }    // left click / Enter / accelerator
        public Action Secondary { get; }  // right click / help

        /// <summary>The REQ entry's action id, or <c>-1</c> when this widget is not a REQ entry
        /// (a dialog choice button). Lets a screen filter its own entry list by action instead of
        /// by an element-name convention.</summary>
        public int ActionId { get; }

        public Vector2 Center {
            get {
                Rect r = _canonicalRect();
                return new Vector2(r.x + r.width / 2f, r.y + r.height / 2f);
            }
        }
    }
}
