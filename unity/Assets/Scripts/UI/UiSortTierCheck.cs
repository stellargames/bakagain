#if UNITY_EDITOR || DEVELOPMENT_BUILD
namespace BakAgain.UI {
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Development-only sweep for the rule in <see cref="UiSortTier"/>: scans the enabled
    /// <see cref="UIDocument"/>s and logs any that share a sortingOrder while both are up.
    ///
    /// <para>Called at the moment an overlay goes up, because that is when a collision can first
    /// exist — the navigator's screens are mutually exclusive on their own. Compiled out of release
    /// builds; it walks every UIDocument in the scene, which is fine for a handful but is not
    /// something to do per frame.</para>
    /// </summary>
    public static class UiSortTierCheck {
        public static void WarnOnConflicts(string context) {
            var visible = new List<UiSortTier.Layer>();
            foreach (UIDocument document in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) {
                if (document == null || !document.isActiveAndEnabled) {
                    continue;
                }
                // A document with no root has nothing on screen yet, so it cannot be in a tie.
                if (document.rootVisualElement == null) {
                    continue;
                }
                visible.Add(new UiSortTier.Layer(document.name, document.sortingOrder));
            }

            IReadOnlyList<string> conflicts = UiSortTier.FindConflicts(visible);
            foreach (string conflict in conflicts) {
                Debug.LogError(
                    $"UI sort-tier conflict while {context}: {conflict}. Documents that are up at "
                    + "the same time must sit on different tiers — a tie is resolved by "
                    + "registration order, which nothing controls. See UnityProject/CLAUDE.md.");
            }
        }
    }
}
#endif
