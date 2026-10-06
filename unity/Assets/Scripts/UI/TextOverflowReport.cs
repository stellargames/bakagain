namespace BakAgain.UI {
    using System.Collections.Generic;
    using UnityEngine;

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
