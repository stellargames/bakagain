namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Tests.TestSupport;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// **Every widget a screen calls navigable must actually be reachable.** TASK-299's per-screen
    /// contract, generalised: rather than a list of screens to keep in step with the DI container,
    /// this sweeps every <see cref="UserInterfaceLoader"/> VContainer has instantiated.
    /// </summary>
    /// <remarks>
    /// <b>This is the test two dead features needed on 2026-09-03.</b> Both were the same shape — a
    /// widget in the loader's own nav list that no click or keypress could ever reach, because the
    /// element carried <c>req-hidden</c> (<c>visibility: hidden</c>, which UI Toolkit will not pick
    /// and <c>NavigableLayer.CanFocus</c> refuses):
    ///
    /// <list type="bullet">
    /// <item>REQ_CAMP's Stop button — so a rest could not be abandoned.</item>
    /// <item>COMBAT/SHOOT action 22, the character panel — its whole chain to
    /// <c>OpenCombatInventory</c> was already built and wired, and the class alone kept the click
    /// from arriving.</item>
    /// </list>
    ///
    /// <para>Neither showed up as a failing test, a log line, or an exception. They read as "that
    /// button does nothing", which is indistinguishable from unimplemented — so they sat.</para>
    ///
    /// <para><b>SCREENS ARE RAISED ONE AT A TIME, AND THAT IS NOT A STYLE CHOICE.</b> Activating
    /// them together makes every one below the top report its whole widget set unreachable —
    /// <c>NavigableLayer.OnActiveChanged</c> turns picking off for any layer an Exclusive one covers,
    /// by design. A sweep that raised nineteen screens at once "found" fourteen broken, including one
    /// fixed minutes earlier; every finding was its own doing. Restore each before raising the
    /// next.</para>
    ///
    /// <para><b>COVERAGE IS WHAT THE TEST SCENE HAS, NOT EVERY SCREEN.</b> It sweeps the
    /// <see cref="UserInterfaceLoader"/> instances that exist when it runs — four, at the time of
    /// writing, against roughly twenty-five registered screen prefabs, because VContainer's
    /// <c>RegisterComponentInNewPrefab</c> instantiates on first resolve and most screens are never
    /// resolved in a test run. The count is logged on every run, pass included, so a green result
    /// cannot be read as "every screen was examined". Raising it means resolving each registration to
    /// force instantiation, which risks side effects across the suite and wants its own pass.</para>
    ///
    /// <para><b>What it deliberately does NOT assert</b> is that <c>panel.Pick</c> returns the widget
    /// itself: sibling entries legitimately overlap (REQ_CAMP authors Camp and Stop at the same
    /// rect, one live at a time), so the topmost pick is often a different, correct element. The
    /// invariant is the self-contradiction — listed as navigable, yet unreachable by construction.
    /// </para>
    /// </remarks>
    public class ScreenReachabilityTests {
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator NoScreenListsAWidgetThatCannotBeReached() => UniTask.ToCoroutine(async () => {
            // *** RESOLVE EVERY REGISTRATION FIRST, OR THIS SWEEPS ALMOST NOTHING. ***
            // RegisterComponentInNewPrefab instantiates on FIRST RESOLVE, so a test run that never
            // asks for a screen never creates it — four existed unaided, against nineteen that do
            // once asked. Reading the scope's own prefab fields keeps this in step with the container
            // instead of a hand-maintained list. They come up inactive; the loop below is what raises
            // them, one at a time.
            var scope = Object.FindAnyObjectByType<VContainer.Unity.LifetimeScope>();
            if (scope != null) {
                foreach (System.Reflection.FieldInfo field in scope.GetType().GetFields(
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)) {
                    if (!typeof(MonoBehaviour).IsAssignableFrom(field.FieldType)) {
                        continue;
                    }

                    try {
                        scope.Container.Resolve(field.FieldType);
                    } catch (System.Exception) {
                        // Not every field is a registration; an unresolvable one is not a finding.
                    }
                }
            }

            UserInterfaceLoader[] loaders = Object.FindObjectsByType<UserInterfaceLoader>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (loaders.Length == 0) {
                Assert.Ignore("no UserInterfaceLoader in the scene — DI has not built the screens");
            }

            var unreachable = new List<string>();
            var unbuilt = new List<string>();

            foreach (UserInterfaceLoader loader in loaders) {
                GameObject go = loader.gameObject;
                bool wasActive = go.activeSelf;
                try {
                    go.SetActive(true);
                    // The panel builds asynchronously off OnEnable, and resolvedStyle needs a layout
                    // pass after that, so yield rather than guess a frame count.
                    for (var frame = 0; frame < 90 && !loader.IsBuilt; frame++) {
                        await UniTask.Yield();
                    }

                    if (!loader.IsBuilt) {
                        unbuilt.Add(go.name);
                        continue;
                    }

                    await UniTask.Yield();
                    await UniTask.Yield();

                    foreach (NavWidget widget in loader.CurrentNavWidgets.ToArray()) {
                        VisualElement element = widget.Element;
                        if (element == null) {
                            unreachable.Add($"{go.name} action {widget.ActionId}: no element");
                            continue;
                        }

                        if (element.resolvedStyle.visibility == Visibility.Hidden) {
                            unreachable.Add(
                                $"{go.name} action {widget.ActionId}: listed navigable but " +
                                "visibility:hidden — no click or keypress can reach it");
                        } else if (element.pickingMode == PickingMode.Ignore) {
                            unreachable.Add(
                                $"{go.name} action {widget.ActionId}: listed navigable but " +
                                "picking is off");
                        }
                    }
                } catch (System.Exception error) {
                    // A screen that cannot be raised at all is TASK-299's other half; name it rather
                    // than letting the sweep die on it.
                    unbuilt.Add($"{go.name} threw: {error.GetType().Name}");
                } finally {
                    go.SetActive(wasActive);
                }
            }

            // *** SAY WHAT WAS NOT COVERED, EVEN ON A PASS. *** A screen whose panel never builds is
            // checked for nothing, and if that only appears in a failure message then a green run
            // silently means "no screen had a problem" and "no screen was examined" at the same
            // time. Some legitimately need session state to raise, so this reports rather than fails.
            Debug.Log($"[reachability] examined {loaders.Length - unbuilt.Count} of {loaders.Length} " +
                $"screens; not built: {(unbuilt.Count == 0 ? "none" : string.Join(", ", unbuilt))}");

            // Reported together: one run should name every offender, not just the first.
            Assert.IsEmpty(unreachable,
                "widgets listed as navigable that nothing can reach:\n  " +
                string.Join("\n  ", unreachable) +
                (unbuilt.Count > 0 ? "\n(screens whose panel never built: " + string.Join(", ", unbuilt) + ")" : ""));
        });
    }
}
