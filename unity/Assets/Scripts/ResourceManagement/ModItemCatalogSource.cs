namespace BakAgain.ResourceManagement {
    using BakAgain.Core;
    using GameData.Resources.Content;
    using GameData.Resources.Object;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System.Collections.Generic;
    using System.IO;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Builds the MOD half of the item catalog from <c>Overrides/ObjectInfo/*.json</c> —
    /// the source TASK-259's registry has always been able to merge and nothing has ever supplied.
    /// </summary>
    /// <remarks>
    /// <b>A document is a PARTIAL, exactly like every other override in this game.</b> It names an
    /// item's <c>Number</c> and only the fields it changes; everything else comes from the shipped
    /// entry. That is <see cref="OverrideJsonMerge"/>'s reason for existing — deserializing a
    /// partial standalone would land every unmentioned field on its type default, which for
    /// <see cref="ObjectInfo"/> means a mod that renames a sword also zeroes its damage.
    ///
    /// <para><b>Overriding and adding are the same operation.</b> The key is
    /// <c>ContentKey.ForBase(objinfo, Number)</c> either way: a Number the archive already has
    /// replaces it (the common modding case) and a new Number adds an item. One keyspace is what
    /// <see cref="ContentRegistry.Merge"/> expects, and it is what makes provenance meaningful —
    /// the merged catalog can say which source last wrote each key.</para>
    ///
    /// <para><b>A new Number merges onto a BLANK item, not onto nothing.</b> Its document has to
    /// carry every field it cares about, but it still goes through the same merge so that adding
    /// and overriding cannot diverge in behaviour.</para>
    /// </remarks>
    internal static class ModItemCatalogSource {
        /// <summary>The overrides sub-directory this reads, beside SCX/BMX/PAL and the rest.</summary>
        internal const string Directory = "ObjectInfo";

        /// <summary>
        /// The mod source, or <c>null</c> when overrides are off or nothing is authored.
        /// </summary>
        /// <remarks>
        /// <b>Null rather than an empty source</b>: an empty source would still appear in the
        /// merged catalog's provenance as having been consulted, which would make "no mod is
        /// installed" and "a mod that changes nothing is installed" indistinguishable to anything
        /// reading provenance.
        /// </remarks>
        internal static IContentSource<ObjectInfo> FromOverrides(
            ObjectInfoSet baseline, ILogger logger = null) {
            if (!BakResourceSettings.OverrideEnabled || baseline == null) {
                return null;
            }

            string folder = Path.Join(BakResourceSettings.OverridePath, Directory);
            if (!System.IO.Directory.Exists(folder)) {
                return null;
            }

            var byNumber = new Dictionary<int, ObjectInfo>();
            foreach (ObjectInfo shipped in baseline.Items) {
                byNumber[shipped.Number] = shipped;
            }

            var entries = new List<ContentEntry<ObjectInfo>>();
            foreach (string file in System.IO.Directory.GetFiles(folder, "*.json")) {
                foreach (ObjectInfo merged in ItemsIn(file, byNumber, logger)) {
                    entries.Add(new ContentEntry<ObjectInfo>(
                        ContentKey.ForBase(ObjectInfoContentSource.Catalog, merged.Number), merged));
                }
            }

            return entries.Count == 0
                ? null
                : new ListContentSource<ObjectInfo>($"mod:{ObjectInfoContentSource.Catalog}", entries);
        }

        /// <summary>
        /// Every item one document defines. A document may hold a single object or an array of them.
        /// </summary>
        /// <remarks>
        /// <b>One bad document does not lose the others.</b> A mod is authored by hand, so a
        /// malformed file is expected rather than exceptional: it is logged and skipped, and the
        /// rest of the folder still loads. Failing the whole catalog would take the game down over
        /// a stray comma.
        /// </remarks>
        private static IEnumerable<ObjectInfo> ItemsIn(
            string file, IReadOnlyDictionary<int, ObjectInfo> byNumber, ILogger logger) {
            JToken root;
            try {
                root = JToken.Parse(File.ReadAllText(file));
            } catch (System.Exception ex) {
                ConditionalLoggingExtensions.LogWarning(
                    logger ?? LogManager.LoggerFactory.CreateLogger(nameof(ModItemCatalogSource)),
                    "Item override {File} is not valid JSON ({Reason}); skipped.",
                    file, ex.GetType().Name);
                yield break;
            }

            foreach (JToken token in root is JArray array ? (IEnumerable<JToken>)array : new[] { root }) {
                if (token is not JObject document) {
                    continue;
                }
                ObjectInfo merged = Merge(document, byNumber, file, logger);
                if (merged != null) {
                    yield return merged;
                }
            }
        }

        private static ObjectInfo Merge(
            JObject document, IReadOnlyDictionary<int, ObjectInfo> byNumber, string file,
            ILogger logger) {
            ILogger log = logger ?? LogManager.LoggerFactory.CreateLogger(nameof(ModItemCatalogSource));
            JToken number = document["Number"];
            if (number == null || number.Type != JTokenType.Integer) {
                // *** Without a Number there is nothing to key on. *** It cannot be inferred from
                // the file name or position: an override addresses one shipped item and the item id
                // is the only thing that says which.
                ConditionalLoggingExtensions.LogWarning(log,
                    "Item override in {File} has no integer Number; skipped.", file);
                return null;
            }

            var id = (int)number;
            ObjectInfo baselineItem = byNumber.TryGetValue(id, out ObjectInfo shipped)
                ? shipped
                // A new item merges onto a BLANK entry rather than onto nothing, so adding and
                // overriding go through one code path. The id is the archive's own resource id and
                // an added item belongs to no archive entry.
                : new ObjectInfo(string.Empty);
            var merged = (ObjectInfo)OverrideJsonMerge.OntoBaseline(
                document.ToString(), baselineItem, typeof(ObjectInfo), new JsonSerializerSettings());
            if (merged == null) {
                return null;
            }
            merged.Number = id;   // the merge cannot be allowed to move the key it was found by
            return merged;
        }
    }
}
