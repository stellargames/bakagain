namespace BakAgain.Tests.Editor.UI {
    using BakAgain.UI.Navigation;
    using NUnit.Framework;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Every navigator-managed screen prefab must start <b>inactive</b>.
    /// </summary>
    /// <remarks>
    /// <see cref="ScreenBase.ShowAsync"/> opens with an idempotent early return when the GameObject
    /// is already active. That is correct for a re-show, but it means a prefab saved ACTIVE is never
    /// really shown through the navigator at all: its first <c>ShowAsync</c> does nothing, so
    /// <c>OnBeforeShowAsync</c> and <c>OnAfterShow</c> never fire and the fade-in is skipped — while
    /// the navigator still records it as shown, so it looks fine from the outside.
    ///
    /// <para>MainMenu.prefab shipped that way and swallowed the hook that starts the menu's music
    /// (TASK-176). Nothing about the symptom pointed at the prefab, which is exactly why this is a
    /// test and not a comment.</para>
    /// </remarks>
    public class ScreenPrefabsStartInactiveTests {
        [Test]
        public void EveryScreenPrefabStartsInactive() {
            var offenders = new List<string>();
            var checkedAny = false;

            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab")) {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var root = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null || root.GetComponent<ScreenBase>() == null) {
                    continue;
                }

                checkedAny = true;
                if (root.activeSelf) {
                    offenders.Add(path);
                }
            }

            Assert.IsTrue(checkedAny, "found no ScreenBase prefabs at all — the scan is broken, "
                + "not the project");
            CollectionAssert.IsEmpty(offenders,
                "these screen prefabs are saved active, so their first ShowAsync is a no-op and "
                + "their show hooks never fire");
        }
    }
}
