namespace BakAgain.UI {
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// REQ elements an Enhanced layout has moved into its own containers, and how to put them back:
    /// parent, index and the inline placement the REQ gave them. Shared by the travel/map HUD and the
    /// inventory, which both re-place the faithful screen's own elements rather than rebuild them.
    /// </summary>
    public sealed class MovedElements {
        private readonly List<Saved> _saved = new();

        private readonly struct Saved {
            public readonly VisualElement Element, Parent;
            public readonly int Index;
            public readonly string Class;
            public readonly StyleEnum<Position> Position;
            public readonly StyleLength Left, Top, Width, Height;
            public readonly StyleBackground Background;
            public readonly StyleTranslate Translate;
            public readonly StyleScale Scale;
            public readonly PickingMode Picking;

            public Saved(VisualElement e, string cls) {
                Element = e; Parent = e.parent; Index = e.parent.IndexOf(e); Class = cls;
                Position = e.style.position;
                Left = e.style.left; Top = e.style.top; Width = e.style.width; Height = e.style.height;
                Background = e.style.backgroundImage; Translate = e.style.translate; Scale = e.style.scale;
                Picking = e.pickingMode;
            }
        }

        /// <summary>Moves <paramref name="e"/> into <paramref name="into"/> with the theme class
        /// <paramref name="cls"/>, clearing the inline REQ placement (which beats any class).</summary>
        public void Move(VisualElement e, VisualElement into, string cls, bool clearSize) {
            _saved.Add(new Saved(e, cls));
            e.style.position = StyleKeyword.Null;
            e.style.left = StyleKeyword.Null;
            e.style.top = StyleKeyword.Null;
            if (clearSize) {
                e.style.width = StyleKeyword.Null;
                e.style.height = StyleKeyword.Null;
            }
            e.AddToClassList(cls);
            into.Add(e);
        }

        /// <summary>Puts every element back, last moved first. An old parent that has since left the
        /// panel (its screen's root was cleared) takes it back anyway, harmlessly detached.</summary>
        public void RestoreAll() {
            for (int i = _saved.Count - 1; i >= 0; i--) {
                Saved s = _saved[i];
                s.Element.RemoveFromClassList(s.Class);
                s.Parent.Insert(Mathf.Min(s.Index, s.Parent.childCount), s.Element);
                s.Element.style.position = s.Position;
                s.Element.style.left = s.Left; s.Element.style.top = s.Top;
                s.Element.style.width = s.Width; s.Element.style.height = s.Height;
                s.Element.style.backgroundImage = s.Background; s.Element.style.translate = s.Translate;
                s.Element.style.scale = s.Scale;
                s.Element.pickingMode = s.Picking;
            }
            _saved.Clear();
        }

        /// <summary>A non-picking container with a theme class.</summary>
        public static VisualElement Box(string cls) {
            var c = new VisualElement { pickingMode = PickingMode.Ignore };
            c.AddToClassList(cls);
            return c;
        }
    }
}
