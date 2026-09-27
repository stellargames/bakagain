namespace BakAgain.ResourceManagement {
    using BakAgain.Core;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Merges a mod-override JSON document ONTO a shipped baseline object, at the JSON level,
    /// and deserializes the result.
    ///
    /// <para><b>Why this exists.</b> The usual override path deserializes a document standalone
    /// (<c>OverrideResourceProvider</c> calls <c>JsonConvert.DeserializeObject</c>), which is
    /// correct only when the resource type's own defaults ARE the shipped values — as
    /// <c>CreditsLayout</c>'s are, since its geometry is baked into the type as property
    /// initialisers. It is NOT correct for a resource whose shipped content is a table of rows
    /// the row TYPE knows nothing about: <c>DialogStyle</c>'s defaults are all-zero pens and an
    /// all-Auto area, so a document naming only the field an author wants to change would land
    /// every other field on zero. All-zero chrome makes <c>DialogPanelBuilder</c> skip the panel
    /// outright — the author moves a box and loses the box. And a partial document is the normal
    /// thing a mod author writes, so silently degrading is not an option.</para>
    ///
    /// <para><b>Why at the JSON level.</b> Once a document has been deserialized onto a POCO,
    /// "the author omitted <c>FillPenColor</c>" and "the author wrote <c>FillPenColor: 0</c>" are
    /// the same thing — both are the byte 0. The JSON is the last place the two are still
    /// distinguishable, so the merge has to happen there. That distinction is worth keeping:
    /// rows 2 and 5 ship with a border, and an author must be able to take it away.</para>
    ///
    /// <para>Merge semantics are Newtonsoft's <see cref="JObject.Merge(object, JsonMergeSettings)"/>
    /// with <see cref="MergeArrayHandling.Merge"/>: objects merge key-by-key (recursively) and
    /// arrays merge index-by-index, so <c>{"Rows":[null,null,{"DefaultArea":{"Left":"10%"}}]}</c>
    /// reaches row 2's left edge and nothing else. A <c>null</c> in the document is IGNORED
    /// rather than merged, which is what makes those leading <c>null</c>s usable as
    /// "leave this row alone" placeholders; the flip side is that a document cannot null out a
    /// field the baseline has set — irrelevant here, since the shipped rows leave every nullable
    /// field (AspectRatio/Flow/Grid/Padding) null already.</para>
    /// </summary>
    internal static class OverrideJsonMerge {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(OverrideJsonMerge));

        private static readonly JsonMergeSettings MergeSettings = new JsonMergeSettings {
            // Index-by-index, so an array entry addresses a row rather than being appended.
            MergeArrayHandling = MergeArrayHandling.Merge,
            // A null in the document leaves the baseline's value in place — see the class remarks.
            MergeNullValueHandling = MergeNullValueHandling.Ignore
        };

        /// <summary>
        /// Deserialize <paramref name="json"/> as an overlay on <paramref name="baseline"/>,
        /// producing a new instance of <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="json">The override document. May name any subset of the fields.</param>
        /// <param name="baseline">A freshly built shipped instance; not mutated.</param>
        /// <param name="resourceType">The type to produce (the baseline's type).</param>
        /// <param name="settings">The settings the non-merged override path would have used.</param>
        public static object OntoBaseline(
            string json, object baseline, Type resourceType, JsonSerializerSettings settings) {
            if (baseline == null) {
                throw new ArgumentNullException(nameof(baseline));
            }
            if (resourceType == null) {
                throw new ArgumentNullException(nameof(resourceType));
            }

            // The baseline is a plain POCO graph we built ourselves — no polymorphic members, so
            // it needs none of the caller's TypeNameHandling/serialization-binder machinery, and
            // involving that machinery here could only ever go wrong (the binder throws for any
            // type outside its own family). The MERGED document, by contrast, is deserialized
            // with exactly the caller's settings, so a merged resource loads identically to an
            // unmerged one.
            JObject merged = JObject.FromObject(baseline, JsonSerializer.CreateDefault());
            JToken overlay = JToken.Parse(json);
            if (!(overlay is JObject overlayObject)) {
                throw new JsonSerializationException(
                    "An override document for " + resourceType.Name + " must be a JSON object, " +
                    "but the file's root is a " + overlay.Type + ".");
            }

            // Diagnostics only — reported BEFORE the merge, against the untouched baseline, and
            // with no influence on what the merge then does. See ReportAuthoringProblems.
            ReportAuthoringProblems(merged, overlayObject, string.Empty, resourceType.Name);

            merged.Merge(overlayObject, MergeSettings);

            return merged.ToObject(resourceType, JsonSerializer.Create(settings));
        }

        /// <summary>
        /// Walk the override document against the shipped baseline and log anything an author
        /// probably did not mean. Purely advisory: this changes nothing about the merge, it only
        /// stops the three ways a document can be quietly ineffective from being <i>silent</i>.
        ///
        /// <list type="bullet">
        /// <item><b>A field the resource does not have</b> — a misspelling. It survives the merge
        /// as a stray JSON property and is then dropped at <c>ToObject</c> (Newtonsoft ignores
        /// unknown members), so the author's edit simply does nothing.</item>
        /// <item><b>More array entries than the shipped resource has</b> — e.g. a ten-row
        /// <c>Rows</c> array against a seven-row table. The merge appends them, so the entries
        /// exist, but nothing indexes that far (the dialog dispatcher only ever produces rows
        /// 1..6), which is not obvious from the document.</item>
        /// <item><b>A scalar where the resource has an object or an array</b> (or the reverse) —
        /// Newtonsoft's merge discards a value that cannot be merged onto a container, so that
        /// part of the document is dropped outright.</item>
        /// <item><b>An array slot the shipped resource ships as <c>null</c></b> — an UNUSED index.
        /// The merge keeps what the author wrote, but nothing indexes it. This is not a hypothetical
        /// corner: <c>{"Rows":[{"DefaultArea":{"Left":"10%"}}]}</c> is the most natural "just one
        /// row" document anybody writes, and for <c>DIALSTYL.DAT</c> it lands on row 0 — the
        /// original's init padding, which <c>DialogStyleTable.Get(0)</c> throws for by design. No
        /// dialog moves and, before this warning, nothing said so.</item>
        /// </list>
        ///
        /// <para>Note the asymmetry with a null encountered anywhere else: a null the BASELINE
        /// carries at an object property really is "a slot the author is entitled to fill in"
        /// (<c>AspectRatio</c>/<c>Flow</c>/<c>Grid</c>/<c>Padding</c> all ship null and are
        /// genuinely authorable). A null ARRAY ELEMENT is the opposite — it is how a shipped table
        /// spells "this index does not exist" — so only the array case warns.</para>
        ///
        /// <para>Warnings rather than exceptions, deliberately: a mod that is 90% right should
        /// still load, with the 10% named in the log, rather than refuse to load at all.</para>
        /// </summary>
        private static void ReportAuthoringProblems(
            JToken baseline, JToken overlay, string path, string resourceName) {
            // A null in the document is a deliberate "leave this one alone" placeholder, and a
            // null in the baseline is a slot the author is entitled to fill in.
            if (overlay == null || overlay.Type == JTokenType.Null
                || baseline == null || baseline.Type == JTokenType.Null) {
                return;
            }

            if (baseline is JObject baselineObject) {
                if (!(overlay is JObject overlayObject)) {
                    WarnShapeMismatch(baseline, overlay, path, resourceName);
                    return;
                }
                foreach (JProperty property in overlayObject.Properties()) {
                    JToken baselineChild = baselineObject[property.Name];
                    if (baselineChild == null) {
                        Logger.LogWarning(
                            "Override document for {Resource} names '{Path}', which is not a field of "
                            + "the shipped resource; it will be ignored. Check the spelling against the "
                            + "generated JSON for this resource.",
                            resourceName, Join(path, property.Name));
                        continue;
                    }
                    ReportAuthoringProblems(
                        baselineChild, property.Value, Join(path, property.Name), resourceName);
                }
                return;
            }

            if (baseline is JArray baselineArray) {
                if (!(overlay is JArray overlayArray)) {
                    WarnShapeMismatch(baseline, overlay, path, resourceName);
                    return;
                }
                if (overlayArray.Count > baselineArray.Count) {
                    Logger.LogWarning(
                        "Override document for {Resource} gives '{Path}' {OverrideCount} entries, but "
                        + "the shipped resource has {BaselineCount}. Entries past the shipped length are "
                        + "kept, yet nothing reads them — the game only ever indexes the shipped range.",
                        resourceName, DescribePath(path), overlayArray.Count, baselineArray.Count);
                }
                int shared = Math.Min(overlayArray.Count, baselineArray.Count);
                for (var index = 0; index < shared; index++) {
                    JToken baselineEntry = baselineArray[index];
                    JToken overlayEntry = overlayArray[index];
                    // A null ARRAY ELEMENT in the shipped resource is an unused index, not an
                    // empty slot on offer — see the class remarks. Writing into it is almost
                    // always an off-by-N: the author meant a later row and forgot the leading
                    // null placeholders that address it.
                    if (baselineEntry != null && baselineEntry.Type == JTokenType.Null
                        && overlayEntry != null && overlayEntry.Type != JTokenType.Null) {
                        Logger.LogWarning(
                            "Override document for {Resource} fills in '{Path}[{Index}]', which the "
                            + "shipped resource ships as null — an index that exists as padding and "
                            + "that nothing ever reads. Your entry is kept, but it will not change "
                            + "anything you can see. To address a LATER entry, pad with leading "
                            + "nulls: [null, null, {{ ... }}] reaches index 2.",
                            resourceName, DescribePath(path), index);
                    }
                    ReportAuthoringProblems(
                        baselineEntry, overlayEntry, path + "[" + index + "]", resourceName);
                }
                return;
            }

            // The baseline is a scalar. A container merged onto it is the mirror-image mistake.
            if (overlay is JContainer) {
                WarnShapeMismatch(baseline, overlay, path, resourceName);
            }
        }

        private static void WarnShapeMismatch(
            JToken baseline, JToken overlay, string path, string resourceName) {
            // .ToString() on the token types, not the enum values themselves: the log sink
            // renders a non-string value as JSON, which would quote them ("Integer") and read
            // like a value rather than a kind.
            Logger.LogWarning(
                "Override document for {Resource} sets '{Path}' to {OverrideType} where the shipped "
                + "resource has {BaselineType}; the merge discards it, so this part of the document has "
                + "no effect.",
                resourceName, DescribePath(path), overlay.Type.ToString(), baseline.Type.ToString());
        }

        private static string Join(string path, string name) =>
            path.Length == 0 ? name : path + "." + name;

        // The root of a document has no field name of its own; say so rather than log an empty ''.
        private static string DescribePath(string path) =>
            path.Length == 0 ? "(the document root)" : path;
    }
}
