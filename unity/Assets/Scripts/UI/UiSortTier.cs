namespace BakAgain.UI {
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Guards the one rule that governs z-order between UI documents: <b>documents that can be
    /// visible at the same time must not share a sortingOrder</b>. They all render into one panel,
    /// where ties are broken by registration order — an ordering nothing controls and nothing
    /// should depend on.
    ///
    /// <para>Most of the screens are safe by construction rather than by discipline:
    /// <see cref="Navigation.ScreenNavigator"/> enables exactly the stack top, so two
    /// navigator-managed screens are never up together however their tiers are set. What this
    /// guard is actually for is the documents the navigator does <i>not</i> own — the dialog
    /// overlay, the software cursor — which appear over whatever happens to be showing.</para>
    /// </summary>
    public static class UiSortTier {
        /// <summary>One visible document, as far as this check cares.</summary>
        public readonly struct Layer {
            public Layer(string name, float sortingOrder) {
                Name = name;
                SortingOrder = sortingOrder;
            }

            public string Name { get; }
            /// <summary>Float, because that is what <c>UIDocument.sortingOrder</c> is — rounding to int
            /// here would report two documents at 0.4 and 0.6 as a tie when they are ordered fine.</summary>
            public float SortingOrder { get; }
        }

        /// <summary>
        /// Names every group of simultaneously-visible documents that share a sortingOrder, or an
        /// empty list when the rule holds. Pure, so the rule can be tested without a scene.
        /// </summary>
        public static IReadOnlyList<string> FindConflicts(IEnumerable<Layer> visible) {
            var byOrder = new Dictionary<float, List<string>>();
            if (visible != null) {
                foreach (Layer layer in visible) {
                    if (!byOrder.TryGetValue(layer.SortingOrder, out List<string> names)) {
                        names = new List<string>();
                        byOrder[layer.SortingOrder] = names;
                    }
                    names.Add(layer.Name ?? "<unnamed>");
                }
            }

            var conflicts = new List<string>();
            foreach (KeyValuePair<float, List<string>> pair in byOrder) {
                if (pair.Value.Count < 2) {
                    continue;
                }
                var message = new StringBuilder();
                message.Append("sortingOrder ").Append(pair.Key).Append(" is shared by ");
                for (int i = 0; i < pair.Value.Count; i++) {
                    if (i > 0) {
                        message.Append(", ");
                    }
                    message.Append(pair.Value[i]);
                }
                conflicts.Add(message.ToString());
            }
            return conflicts;
        }
    }
}
