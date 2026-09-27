namespace BakAgain.ResourceManagement.Loaders {
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.UI;
    using GameData.Resources.Menu;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Loads BICONS icon sprites for the REQ menu widgets (ImageButtons + Toggles)
    /// and drives the Toggle icon state. Reproduces the original engine's combined
    /// BICONS1/BICONS2 index scheme (sub_seg029_A9 @ 0x2b579) and the Toggle frame
    /// selection (menu_type_3_4 @ 0x2b898). Extracted from
    /// <see cref="UserInterfaceLoader"/>: the builders call <see cref="LoadAndApplyIcon"/>
    /// / <see cref="RegisterToggle"/>, and the loader forwards RefreshToggles /
    /// teardown here. Toggle on/off state comes from the screen's
    /// <see cref="IMenuStateProvider"/>.
    /// </summary>
    internal sealed class MenuIconLoader {
        // BaK loads BICONS1 + BICONS2 as one interleaved icon index space
        // (loadIcons1and2 @ 0x2b4d9): even -> BICONS1, odd -> BICONS2,
        // sub-image = index / 2.

        private readonly IMenuStateProvider _stateProvider;
        private readonly ILogger _logger;

        // Sprite cache keyed by addressable sub-resource key, and the handles to
        // release on teardown.
        private readonly Dictionary<string, Sprite> _spriteCache = new();
        private readonly List<AsyncOperationHandle> _spriteHandles = new();

        // Toggle/radio elements + their REQ entries by ActionId, plus the toggles the
        // pointer is currently over (so a post-click RefreshToggles keeps the hover
        // frame instead of dropping it while the mouse is still over the widget).
        private readonly Dictionary<int, UiElement> _toggleEntries = new();
        private readonly Dictionary<int, VisualElement> _toggleElements = new();
        private readonly HashSet<int> _hoveredToggles = new();

        /// <summary>Whether the screen has switched an entry off at runtime.</summary>
        /// <remarks>
        /// Owned by <see cref="UserInterfaceLoader"/> next to the icon overrides, because the gate
        /// outlives a rebuild the same way they do; read from here because this is the type that
        /// decides which sprite an entry wears.
        /// </remarks>
        private readonly System.Func<int, bool> _isGated;

        public MenuIconLoader(IMenuStateProvider stateProvider, ILogger logger,
            System.Func<int, bool> isGated = null) {
            _stateProvider = stateProvider;
            _logger = logger;
            _isGated = isGated;
        }

        /// <summary>Register a Toggle so <see cref="RefreshToggles"/> can drive its
        /// icon. The initial frame is applied by the loader's post-build
        /// RefreshToggles, matching the original (which draws all toggles once the
        /// menu is built).</summary>
        public void RegisterToggle(int actionId, UiElement entry, VisualElement icon) {
            _toggleEntries[actionId] = entry;
            _toggleElements[actionId] = icon;
        }

        /// <summary>Pointer entered/left a Toggle: track hover and re-apply its icon
        /// (the original's `di` highlight flag swaps to the +1 frame while hovered).</summary>
        public void SetToggleHovered(int actionId, UiElement entry, bool hovered) {
            if (hovered) {
                _hoveredToggles.Add(actionId);
            } else {
                _hoveredToggles.Remove(actionId);
            }
            bool on = _stateProvider != null && _stateProvider.GetToggleState(actionId);
            ApplyToggleIcon(actionId, entry, on, hovered);
        }

        /// <summary>Re-query every Toggle's state from the
        /// <see cref="IMenuStateProvider"/> and update its icon. Called after a click
        /// or a bulk model change (e.g. the Preferences "Defaults" button).</summary>
        public void RefreshToggles() {
            foreach (KeyValuePair<int, UiElement> kvp in _toggleEntries) {
                bool on = _stateProvider != null && _stateProvider.GetToggleState(kvp.Key);
                ApplyToggleIcon(kvp.Key, kvp.Value, on, _hoveredToggles.Contains(kvp.Key));
            }
        }

        /// <summary>Forget all registered Toggles on screen teardown.</summary>
        public void ClearToggles() {
            _toggleEntries.Clear();
            _toggleElements.Clear();
            _hoveredToggles.Clear();
        }

        /// <summary>Release every loaded icon sprite handle and drop the cache.</summary>
        public void ReleaseHandles() {
            foreach (AsyncOperationHandle handle in _spriteHandles) {
                if (handle.IsValid()) {
                    Addressables.Release(handle);
                }
            }
            _spriteHandles.Clear();
            _spriteCache.Clear();
        }

        // menu_type_3_4 (0x2b898): a Toggle draws IconBase when on, IconBase+2 when off, and the
        // highlight (`di` flag) adds +1 — so hovering shows IconBase+1 (on) / IconBase+3 (off),
        // exactly as an ImageButton swaps to IconBase+1 on hover.
        private void ApplyToggleIcon(int actionId, UiElement entry, bool on, bool hovered) {
            if (!_toggleElements.TryGetValue(actionId, out VisualElement icon)) {
                return;
            }

            // *** A GATED TOGGLE WEARS THE BLANK STONE AND NEITHER STATE NOR HOVER IS READ. ***
            // widget_menu_draw returns on the gate before it reaches the type test, so there is no
            // "disabled ON" frame — the sprite is the same one every gated widget wears.
            // Checked HERE and not only in the loader: a Toggle's face is repainted from
            // entry.IconBase by RefreshToggles, which does not consult the loader's icon overrides,
            // so a gate applied there alone would be painted over on the next refresh.
            if (_isGated != null && _isGated(actionId)) {
                LoadAndApplyIcon(icon, UserInterfaceLoader.DisabledButtonIcon);

                return;
            }

            LoadAndApplyIcon(icon, entry.IconBase + (on ? 0 : 2) + (hovered ? 1 : 0));
        }

        /// <summary>Load the BICONS sprite for a combined icon index and assign it to
        /// the element's background, caching by addressable key. Async, but the
        /// element is already in the tree so the sprite pops in when ready. Shared by
        /// Toggle and ImageButton.</summary>
        public void LoadAndApplyIcon(VisualElement element, int combinedIconIndex) {
            // *** A NEGATIVE INDEX IS "NO ICON", NOT AN ICON. *** Three shipped entries carry
            // IconBase -1 and are marked visible — COMBAT action 14 (the capability label, which
            // CombatMenu calls "drawn, never clickable") and SPELL actions 6 and 7 — so every open of
            // those screens asked Addressables for BICONS2.BMX#-1 and logged a load failure. It went
            // unnoticed because no test raised those screens until the reachability sweep started
            // instantiating every registration.
            if (combinedIconIndex < 0) {
                // *** CLEAR, DO NOT MERELY SKIP. *** Skipping is right on a fresh build, where the
                // element has no background yet, and wrong on a RE-apply: an entry whose icon is
                // being taken away would keep the one it already had. That is how the inventory's
                // Use button kept its hand on screens the original leaves bare — the icon is
                // rewritten per open, not rebuilt.
                element.style.backgroundImage = StyleKeyword.None;

                return;
            }

            string key = IconKeyForCombined(combinedIconIndex);
            if (_spriteCache.TryGetValue(key, out Sprite cached)) {
                ApplyIconSprite(element, cached);
                return;
            }
            AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(key);
            _spriteHandles.Add(handle);
            handle.Completed += op => {
                if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null) {
                    _logger.LogError("Failed to load menu icon sprite {Key}", key);
                    return;
                }
                _spriteCache[key] = op.Result;
                // The widget may have been re-themed/destroyed while loading.
                if (element.panel != null) {
                    ApplyIconSprite(element, op.Result);
                }
            };
        }

        // Draw the BICONS icon at its NATIVE size, anchored top-left — matching the original renderer
        // (menu_type_3_4 @ 0x2b898), which blits the bitmap at the element's origin at the bitmap's own
        // size. The element's Width/Height is the click/highlight region, NOT the icon size, so the
        // default background-size (100% 100%) stretched the icon to the click rect (e.g. the wide travel
        // nav arrows). Sizing to the sprite rect + top-left keeps the icon its true size within the region.
        private static void ApplyIconSprite(VisualElement element, Sprite sprite) =>
            element.SetBackgroundSpriteNativeSizeTopLeft(sprite);

        // The BICONS index scheme is RE knowledge and lives on the model now; the state offset is
        // added by the callers above, which is where "which visual state" belongs. It has to be
        // added BEFORE the resolve — see UiElement.IconKeyForCombined for why there is no base key.
        private static string IconKeyForCombined(int combined) =>
            UiElement.IconKeyForCombined(combined);
    }
}
