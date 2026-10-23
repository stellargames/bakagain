namespace BakAgain.Core.Services {
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Combat;
    using GameData.Resources.Layout;
    using GameData.Resources.Text;
    using Microsoft.Extensions.Logging;
    using System;

    /// <summary>
    /// Installs the UI string catalog: the embedded default, with a mod's <c>uistrings.json</c>
    /// merged over it per entry when the override provider supplies one. Later-source-wins, so a
    /// translation can replace part of the catalog without restating all of it.
    /// </summary>
    /// <remarks>
    /// Also installs the combat panels' columns (<see cref="CombatPanelLayout"/>, TASK-825), the one
    /// piece of layout a pack moves when its words cannot fit: the shipped columns, with the
    /// columns a <c>combatpanels.json</c> names moved, found the same way.
    /// </remarks>
    public static class UiStringLoader {
        public static async UniTask InstallAsync(IResourceProviderService resources, ILogger logger) {
            // The original English, translated by the active pack; a mod's own override (below) still
            // wins over both (TASK-773).
            UiStringCatalog catalog = UiStringCatalog.Embedded.TranslatedBy(LanguagePacks.Current);
            try {
                var overrideText = await resources.LoadAssetAsync<UnityEngine.TextAsset>(
                    UiStringCatalog.ResourceId, owner: typeof(UiStringLoader));
                if (overrideText != null) {
                    catalog = catalog.Merge(UiStringCatalog.FromJson(overrideText.text));
                    logger?.LogInformation("UI string override applied.");
                }
            } catch (Exception ex) {
                // A malformed or absent override must never stop the game starting — the embedded
                // default is always a complete catalog.
                //
                // Debug, not Warning: having no override is the normal state for every unmodded
                // player, and the resource layer signals "absent" by throwing (Addressables
                // completes the handle as Failed and awaiting it rethrows), so this branch runs on
                // EVERY session start. Logging that as a warning — with a stack trace attached —
                // reports a non-event as a problem and teaches people to skim past real warnings.
                logger?.LogDebug(ex, "No UI string override present; using the embedded catalog.");
            } finally {
                // Release the Addressables handle this load took out. The owner is the TYPE (this is
                // a static class — there is no instance to pass), so the release must name the same
                // type; releasing `this` from elsewhere would not touch it. The catalog is parsed
                // into a plain dictionary above, so nothing keeps a reference to the TextAsset and
                // holding the handle for the rest of the session would pin it for nothing.
                // Unconditional: a failed load still allocates a handle worth releasing.
                resources?.ReleaseAssets(typeof(UiStringLoader));
            }
            UiStrings.Catalog = catalog;
            CombatPanelLayout.Current = await LoadPanelLayoutAsync(resources);
        }

        private static async UniTask<CombatPanelLayout> LoadPanelLayoutAsync(IResourceProviderService resources) {
            if (resources == null) {
                return new CombatPanelLayout();
            }
            object owner = typeof(CombatPanelLayout);
            try {
                var text = await resources.LoadAssetAsync<UnityEngine.TextAsset>(CombatPanelLayout.ResourceId, owner);
                return text == null ? new CombatPanelLayout() : ReadPanelLayout(text.text);
            } finally {
                resources.ReleaseAssets(owner);
            }
        }

        /// <summary>
        /// The shipped columns with those <paramref name="json"/> names moved. A column that is not
        /// px is refused and keeps the original's; a document that does not parse moves nothing.
        /// </summary>
        internal static CombatPanelLayout ReadPanelLayout(string json) {
            var shipped = new CombatPanelLayout();
            var layout = new CombatPanelLayout();
            try {
                Newtonsoft.Json.JsonConvert.PopulateObject(json, layout);
            } catch (Exception e) {
                UnityEngine.Debug.LogWarning($"{CombatPanelLayout.ResourceId} skipped: {e.Message}");
                return shipped;
            }
            foreach (System.Reflection.PropertyInfo column in typeof(CombatPanelLayout).GetProperties()) {
                if (column.PropertyType == typeof(LayoutLength)
                    && ((LayoutLength)column.GetValue(layout)).Unit != LayoutLengthUnit.Px) {
                    BakAgain.UI.Layout.LayoutApplier.RefuseUnresolvable(
                        CombatPanelLayout.ResourceId + " " + column.Name, column.GetValue(layout).ToString(),
                        "the original's column is kept");
                    column.SetValue(layout, column.GetValue(shipped));
                }
            }
            return layout;
        }
    }
}
