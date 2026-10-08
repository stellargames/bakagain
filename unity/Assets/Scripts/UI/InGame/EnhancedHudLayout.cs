namespace BakAgain.UI.InGame {
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Enhanced full-screen travel's HUD (spec 2026-10-08 §3): the REQ_MAIN elements themselves,
    /// reparented into one container on the document root. Every element keeps its action id, so
    /// clicks go through InGameScreen's existing Primary/SecondaryAction. All geometry is in the
    /// theme (<c>.enhanced-hud*</c>); this class only moves elements and clears their inline REQ placement.
    /// </summary>
    /// <remarks>
    /// The container is added to the document root AFTER the canonical stage, so it sits above the
    /// world's click area (hotspot_192), which full-screen stretches over the whole window.
    /// </remarks>
    public sealed class EnhancedHudLayout {
        // REQ_MAIN: follow-road is a Toggle, so it is toggle_19, not imagebutton_19.
        private static readonly string[] Portraits = { "hotspot_2", "hotspot_3", "hotspot_4" };
        private static readonly string[] Buttons = {
            "toggle_19", "imagebutton_50", "imagebutton_46",
            "imagebutton_48", "imagebutton_18", "imagebutton_24" };
        private static readonly string[] Arrows = { "imagebutton_72", "imagebutton_80", "imagebutton_75", "imagebutton_77" };
        private const string Compass = CompassView.WindowName;

        private readonly VisualElement _root;
        private readonly List<Saved> _saved = new();
        private VisualElement _hud;
        private Label _status;
        private bool _warned;

        private readonly struct Saved {
            public readonly VisualElement Element, Parent;
            public readonly int Index;
            public readonly StyleEnum<Position> Position;
            public readonly StyleLength Left, Top, Width, Height;
            public Saved(VisualElement e) {
                Element = e; Parent = e.parent; Index = e.parent.IndexOf(e);
                Position = e.style.position;
                Left = e.style.left; Top = e.style.top; Width = e.style.width; Height = e.style.height;
            }
        }

        public EnhancedHudLayout(VisualElement root) => _root = root;

        public bool Applied => _hud != null;

        /// <summary>The status line's font size as a fraction of the HUD's height.</summary>
        public float StatusFontFraction { get; set; } = 0.024f;

        /// <summary>
        /// Moves the elements into the HUD. False, with nothing moved, when one is missing (the
        /// compass is built asynchronously, so an early call can find it absent); warns once.
        /// </summary>
        public bool Apply() {
            if (Applied) return true;
            var all = new List<string>(Portraits);
            all.AddRange(Buttons);
            all.Add(Compass);
            all.AddRange(Arrows);
            foreach (string name in all) {
                if (_root.Q(name) == null) {
                    if (!_warned) {
                        Debug.LogWarning($"EnhancedHud: REQ_MAIN element '{name}' missing; keeping the faithful layout.");
                        _warned = true;
                    }
                    return false;
                }
            }

            _hud = new VisualElement { name = "enhanced-hud", pickingMode = PickingMode.Ignore };
            _hud.AddToClassList("enhanced-hud");
            VisualElement portraits = Box("enhanced-hud__portraits");
            VisualElement buttons = Box("enhanced-hud__buttons");
            VisualElement top = Box("enhanced-hud__top");
            _status = new Label { name = "enhanced-hud-status", pickingMode = PickingMode.Ignore };
            _status.AddToClassList("enhanced-hud__status");
            _hud.Add(portraits); _hud.Add(buttons); _hud.Add(top);
            _root.Add(_hud);

            foreach (string n in Portraits) Move(_root.Q(n), portraits, "enhanced-hud__portrait", clearSize: true);
            // Buttons and the compass keep their REQ size: a button's face is its icon at native size
            // (inline, re-applied on every press), the compass strips are sized from the window in
            // panel px. The theme scales the button column as a whole instead.
            foreach (string n in Buttons) Move(_root.Q(n), buttons, "enhanced-hud__button", clearSize: false);
            Move(_root.Q(Compass), top, "enhanced-hud__compass", clearSize: false);
            top.Add(_status);
            foreach (string n in Arrows) _root.Q(n).style.display = DisplayStyle.None;

            // Square portraits from the theme's heights, and the status font from the window height —
            // sizes derived from layout data, not coordinates.
            _hud.RegisterCallback<GeometryChangedEvent>(_ => {
                if (_hud == null) return;
                foreach (VisualElement e in _hud.Query(className: "enhanced-hud__portrait").ToList()) e.style.width = e.resolvedStyle.height;
                _status.style.fontSize = _hud.resolvedStyle.height * StatusFontFraction;
            });
            return true;
        }

        public void Revert() {
            if (!Applied) return;
            for (int i = _saved.Count - 1; i >= 0; i--) {
                Saved s = _saved[i];
                s.Element.RemoveFromClassList("enhanced-hud__portrait");
                s.Element.RemoveFromClassList("enhanced-hud__button");
                s.Element.RemoveFromClassList("enhanced-hud__compass");
                s.Parent.Insert(Mathf.Min(s.Index, s.Parent.childCount), s.Element);
                s.Element.style.position = s.Position;
                s.Element.style.left = s.Left; s.Element.style.top = s.Top;
                s.Element.style.width = s.Width; s.Element.style.height = s.Height;
            }
            _saved.Clear();
            foreach (string n in Arrows) {
                VisualElement a = _root.Q(n);
                if (a != null) a.style.display = StyleKeyword.Null;
            }
            _hud.RemoveFromHierarchy();
            _hud = null;
            _status = null;
        }

        public void SetStatus(string text) {
            if (_status != null && _status.text != text) _status.text = text;
        }

        private void Move(VisualElement e, VisualElement into, string cls, bool clearSize) {
            _saved.Add(new Saved(e));
            // Inline REQ placement beats any class, so it has to go for the theme to apply.
            e.style.position = StyleKeyword.Null;
            e.style.left = StyleKeyword.Null; e.style.top = StyleKeyword.Null;
            if (clearSize) {
                e.style.width = StyleKeyword.Null; e.style.height = StyleKeyword.Null;
            }
            e.AddToClassList(cls);
            into.Add(e);
        }

        private static VisualElement Box(string cls) {
            var c = new VisualElement { pickingMode = PickingMode.Ignore };
            c.AddToClassList(cls);
            return c;
        }
    }
}
