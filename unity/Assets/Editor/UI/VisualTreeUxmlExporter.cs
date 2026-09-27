namespace BakAgain.Editor.UI {
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Serialises a live (runtime-built) <see cref="VisualElement"/> tree back to a
    /// UXML document string. Unity ships no public "VisualElement -&gt; UXML" API, so
    /// this walks the tree and emits tags, <c>name</c>, USS <c>class</c> list,
    /// <c>text</c>, and the inline layout styles the builders set. It is a
    /// debug/authoring export, not a perfect round-trip:
    /// <list type="bullet">
    /// <item>Only built-in <c>UnityEngine.UIElements</c> types keep their tag; any
    /// custom subclass is emitted as a plain <c>ui:VisualElement</c> (structure
    /// preserved, type generalised).</item>
    /// <item>Runtime <c>background-image</c> sprites have no project-asset path, so
    /// they cannot be written as <c>url(...)</c> and are dropped (counted in
    /// <c>droppedImageCount</c>).</item>
    /// <item>Only inline styles are captured; everything from USS classes resolves
    /// at load time from the panel's theme (e.g. ClassicTheme).</item>
    /// </list>
    /// </summary>
    public static class VisualTreeUxmlExporter {
        private const string EngineNamespace = "UnityEngine.UIElements";

        /// <summary>Serialise <paramref name="root"/>'s children as the top-level
        /// elements of a UXML document. If <paramref name="styleSheetSrc"/> is given
        /// (a UXML <c>src</c> reference to the panel's theme/stylesheet), a
        /// <c>&lt;Style&gt;</c> is emitted so the document carries its own styling and
        /// previews correctly in UI Builder — otherwise the class rules and font only
        /// resolve when the document is shown under that theme's panel.</summary>
        public static string Export(VisualElement root, out int droppedImageCount, string styleSheetSrc = null) {
            droppedImageCount = 0;
            var sb = new StringBuilder();
            sb.AppendLine("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:uie=\"UnityEditor.UIElements\"");
            sb.AppendLine("         xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"");
            sb.AppendLine("         editor-extension-mode=\"False\">");
            if (!string.IsNullOrEmpty(styleSheetSrc)) {
                sb.AppendLine($"    <Style src=\"{Escape(styleSheetSrc)}\" />");
            }
            if (root != null) {
                foreach (VisualElement child in root.Children()) {
                    WriteElement(child, sb, 1, ref droppedImageCount);
                }
            }
            sb.AppendLine("</ui:UXML>");
            return sb.ToString();
        }

        private static void WriteElement(VisualElement el, StringBuilder sb, int depth, ref int droppedImages) {
            string indent = new string(' ', depth * 4);
            string tag = TagFor(el);

            var attrs = new List<string>();
            if (!string.IsNullOrEmpty(el.name)) {
                attrs.Add($"name=\"{Escape(el.name)}\"");
            }
            string classes = ClassList(el);
            if (!string.IsNullOrEmpty(classes)) {
                attrs.Add($"class=\"{Escape(classes)}\"");
            }
            if (el is TextElement text && !string.IsNullOrEmpty(text.text)) {
                attrs.Add($"text=\"{Escape(text.text)}\"");
            }
            if (el is BakAgain.UI.ArchiveImage archive && !string.IsNullOrEmpty(archive.address)) {
                attrs.Add($"address=\"{Escape(archive.address)}\"");
            }
            string style = InlineStyle(el, ref droppedImages);
            if (!string.IsNullOrEmpty(style)) {
                attrs.Add($"style=\"{Escape(style)}\"");
            }

            string open = attrs.Count > 0 ? $"{tag} {string.Join(" ", attrs)}" : tag;

            // A TextElement's text is an attribute, so treat it as childless even if it
            // has internal label children; other containers recurse their logical children.
            var children = new List<VisualElement>();
            if (!(el is TextElement)) {
                foreach (VisualElement c in el.Children()) {
                    children.Add(c);
                }
            }

            if (children.Count == 0) {
                sb.AppendLine($"{indent}<{open} />");
                return;
            }
            sb.AppendLine($"{indent}<{open}>");
            foreach (VisualElement c in children) {
                WriteElement(c, sb, depth + 1, ref droppedImages);
            }
            sb.AppendLine($"{indent}</{tag}>");
        }

        private static string TagFor(VisualElement el) {
            Type t = el.GetType();
            // Our archive-backed image round-trips via its full type name + address
            // attribute (it's a [UxmlElement], so UXML resolves the qualified name).
            if (t == typeof(BakAgain.UI.ArchiveImage)) {
                return t.FullName;
            }
            // Built-in controls map to the ui: (UnityEngine.UIElements) prefix; other
            // custom subclasses generalise to VisualElement so the document still loads.
            return t.Namespace == EngineNamespace ? "ui:" + t.Name : "ui:VisualElement";
        }

        private static string ClassList(VisualElement el) {
            var kept = new List<string>();
            foreach (string c in el.GetClasses()) {
                // Skip the framework's auto-added control classes (unity-button, …);
                // the control's own tag re-adds them on load.
                if (!c.StartsWith("unity-", StringComparison.Ordinal)) {
                    kept.Add(c);
                }
            }
            return string.Join(" ", kept);
        }

        // Emits the inline (element-set) styles only — never the USS-resolved values,
        // which must keep coming from the classes. Covers the layout properties the
        // builders assign; extend the list if a screen sets others inline.
        private static string InlineStyle(VisualElement el, ref int droppedImages) {
            IStyle s = el.style;
            var parts = new List<string>();

            Add(parts, "position", Enum(s.position));
            Add(parts, "left", Len(s.left));
            Add(parts, "top", Len(s.top));
            Add(parts, "right", Len(s.right));
            Add(parts, "bottom", Len(s.bottom));
            Add(parts, "width", Len(s.width));
            Add(parts, "height", Len(s.height));
            Add(parts, "display", Enum(s.display));
            Add(parts, "visibility", Enum(s.visibility));
            Add(parts, "flex-grow", Flt(s.flexGrow));
            Add(parts, "flex-shrink", Flt(s.flexShrink));
            Add(parts, "flex-direction", Enum(s.flexDirection));
            Add(parts, "opacity", Flt(s.opacity));
            Add(parts, "background-color", Col(s.backgroundColor));
            Add(parts, "color", Col(s.color));

            if (!(el is BakAgain.UI.ArchiveImage) && s.backgroundImage.keyword != StyleKeyword.Null) {
                // A runtime sprite/texture with no serialisable asset path — skip it.
                // (ArchiveImage is exempt: its image round-trips via the address attribute.)
                droppedImages++;
            }

            return parts.Count == 0 ? null : string.Join(" ", parts) + " ";
        }

        private static void Add(List<string> parts, string name, string value) {
            if (value != null) {
                parts.Add($"{name}: {value};");
            }
        }

        private static string Len(StyleLength v) {
            switch (v.keyword) {
                case StyleKeyword.Auto: return "auto";
                case StyleKeyword.None: return "none";
                case StyleKeyword.Initial: return "initial";
                case StyleKeyword.Undefined:
                    Length l = v.value;
                    string n = l.value.ToString(CultureInfo.InvariantCulture);
                    return l.unit == LengthUnit.Percent ? n + "%" : n + "px";
                default: return null; // Null -> not set inline
            }
        }

        private static string Flt(StyleFloat v) =>
            v.keyword == StyleKeyword.Null ? null : v.value.ToString(CultureInfo.InvariantCulture);

        private static string Enum<T>(StyleEnum<T> v) where T : struct, IConvertible =>
            v.keyword == StyleKeyword.Null ? null : PascalToKebab(v.value.ToString());

        private static string Col(StyleColor v) {
            if (v.keyword == StyleKeyword.Null) {
                return null;
            }
            Color c = v.value;
            return string.Format(CultureInfo.InvariantCulture, "rgba({0}, {1}, {2}, {3})",
                Mathf.RoundToInt(c.r * 255f), Mathf.RoundToInt(c.g * 255f), Mathf.RoundToInt(c.b * 255f), c.a);
        }

        private static string PascalToKebab(string s) {
            var sb = new StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++) {
                char c = s[i];
                if (char.IsUpper(c)) {
                    if (i > 0) {
                        sb.Append('-');
                    }
                    sb.Append(char.ToLowerInvariant(c));
                } else {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string Escape(string s) => s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}
