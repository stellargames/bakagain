namespace BakAgain.UI {
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The overflow report (TASK-779): every string the screen could not show in full, collected
    /// while the game runs. Whether a dialog can page depends on the path that shows it, not on the
    /// record (the full map's chapter caption never pages; the same flags elsewhere do), so the
    /// report is taken from the real layout rather than predicted from the text.
    /// </summary>
    /// <remarks>
    /// Each string is reported once, as a <c>[TextOverflow]</c> warning, so a pseudo-language or
    /// translated playthrough leaves its report in the log; <see cref="Entries"/> holds the same
    /// list for a probe or a test.
    /// </remarks>
    internal static class TextOverflowReport {
        /// <summary>A text block whose box cannot page laid out more lines than it holds.</summary>
        internal const string Box = "box";

        /// <summary>A record that pages but does not wait for its last page (SkipWait), so that
        /// page is replaced before anyone reads it — the chapter captions over the map.</summary>
        internal const string UnreadPage = "unread page";

        /// <summary>Two labels on one row of a fixed-column panel overlap — a translated label that
        /// runs into its value (the combat stats, melee and assessment panels).</summary>
        internal const string Collision = "collision";

        /// <summary>
        /// Once <paramref name="container"/> has laid out, report every pair of its labels on the
        /// same row whose boxes overlap. The panels place each label at a fixed x, so a longer
        /// translation runs into the next column instead of wrapping.
        /// </summary>
        /// <param name="descendants">Every label below <paramref name="container"/>, not only its
        /// children: a shop shelf's names each sit in their own cell and overhang it.</param>
        internal static void CheckRows(VisualElement container, bool descendants = false) {
            container?.schedule.Execute(() => {
                var labels = new List<Label>();
                IEnumerable<VisualElement> candidates = descendants
                    ? container.Query<Label>().ToList()
                    : container.Children();
                foreach (VisualElement child in candidates) {
                    if (child is Label l && !string.IsNullOrEmpty(l.text)
                        && l.resolvedStyle.display != DisplayStyle.None && !float.IsNaN(l.worldBound.width)) {
                        labels.Add(l);
                    }
                }
                foreach (Label a in labels) {
                    foreach (Label b in labels) {
                        if (a == b || a.worldBound.x > b.worldBound.x) {
                            continue;
                        }
                        Rect ra = a.worldBound, rb = b.worldBound;
                        bool sameRow = Mathf.Abs(ra.center.y - rb.center.y) < Mathf.Min(ra.height, rb.height) / 2f;
                        if (sameRow && ra.xMax > rb.xMin + 1f) {
                            Report(Collision, a.text + " | " + b.text, $"overlaps by {ra.xMax - rb.xMin:0} px");
                        }
                    }
                }
            }).StartingIn(100);
        }

        /// <summary>A fixed caption still wider than its button at the smallest fit.</summary>
        internal const string Caption = "caption";

        internal readonly struct Entry {
            internal Entry(string kind, string text, string detail) {
                Kind = kind;
                Text = text;
                Detail = detail;
            }

            internal string Kind { get; }
            internal string Text { get; }
            internal string Detail { get; }
        }

        private static readonly List<Entry> _entries = new();
        private static readonly HashSet<string> _seen = new();

        internal static IReadOnlyList<Entry> Entries => _entries;

        internal static void Report(string kind, string text, string detail) {
            if (string.IsNullOrEmpty(text) || !_seen.Add(kind + "\u0001" + text)) {
                return;
            }
            _entries.Add(new Entry(kind, text, detail));
            Debug.LogWarning($"[TextOverflow] {kind}: {detail}: \"{text.Replace("\n", "\\n")}\"");
        }

        internal static void Clear() {
            _entries.Clear();
            _seen.Clear();
        }
    }
}
