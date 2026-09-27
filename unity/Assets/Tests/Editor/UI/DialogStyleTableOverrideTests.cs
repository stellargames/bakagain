namespace BakAgain.Tests.Editor.UI {
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using GameData.Resources.Dialog;
    using GameData.Resources.Layout;
    using Newtonsoft.Json;
    using NUnit.Framework;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using UnityEngine;
    using UnityEngine.ResourceManagement.ResourceLocations;
    using UnityEngine.TestTools;

    /// <summary>
    /// The assertion this whole phase was built toward: a mod author can move a dialog box by
    /// dropping a JSON file into their override directory, and the box moves.
    ///
    /// <para>Before this, <c>DialogStyleTable</c> was a <c>static readonly</c> array compiled into
    /// GameData.dll with no override path at all — a dialog's position was not editable by anyone
    /// but us. Giving it a resource identity (<c>DIALSTYL.DAT</c>) puts it on the existing
    /// override path, which resolves the key to
    /// <c>&lt;OverridePath&gt;/DAT/DIALSTYL.json</c>.</para>
    ///
    /// <para><b>Fixture values are asymmetric, non-round, and share no number with the shipped
    /// row</b> (shipped row 2 is 65/66/1470/606 <i>px</i>; the override is 13.75/62.5/71.25/18.75
    /// <i>percent</i>). Every assertion carries its unit, because a unit-blind assertion passes
    /// against a wrong unit and this project has shipped that defect four times.</para>
    /// </summary>
    public class DialogStyleTableOverrideTests {
        // Shipped row 2 — the default fallback style, the one most dialogs render with.
        private const int ShippedRow = 2;

        // A PARTIAL document: it names row 2's area and nothing else. This is the normal thing a
        // mod author writes ("I want the box lower and wider"), and it is exactly the shape that
        // a naive DeserializeObject destroys — see NaiveDeserialize_OfTheSamePartialDocument_...
        private const string PartialOverrideJson =
            "{\"Rows\":[null,null,{\"DefaultArea\":{\"Left\":\"13.75%\",\"Top\":\"62.5%\","
            + "\"Width\":\"71.25%\",\"Height\":\"18.75%\"}}]}";

        // Instance state, not statics: the fields are driven from an instance [SetUp], so statics
        // would only be correct while the runner stays sequential. TempOverrideDirectory also
        // makes the PlayerPrefs-backed BakResourceSettings.OverridePath swap survivable — see its
        // own remarks on why a [TearDown] alone is not enough for a persisted setting.
        private TempOverrideDirectory _overrides;

        [SetUp]
        public void BorrowTheOverrideDirectory() {
            _overrides = new TempOverrideDirectory();
        }

        [TearDown]
        public void ReturnTheOverrideDirectory() {
            _overrides?.Dispose();
            _overrides = null;
        }

        private string WriteOverrideFile(string json) {
            return _overrides.Write("DAT", "DIALSTYL.json", json);
        }

        /// <summary>
        /// THE PROOF. A partial override document on disk, resolved by the REAL
        /// <see cref="OverrideResourceLocator"/> (which consults only the key string and the
        /// override filesystem) and loaded by the REAL
        /// <see cref="OverrideResourceProvider"/> deserialization step, moves the box — and
        /// leaves the box a box.
        ///
        /// <para>The only part of the shipped path this does not exercise is Addressables'
        /// <c>ProvideHandle</c> plumbing, which cannot be fabricated from a test; the file read,
        /// the path resolution, the baseline merge and the deserialization are all the production
        /// code, called in production order.</para>
        /// </summary>
        [Test]
        public void PartialOverrideOnDisk_ResolvedByTheRealLocator_MovesTheBoxAndKeepsItsChrome() {
            WriteOverrideFile(PartialOverrideJson);

            // 1. The real locator maps the resource key to the override file. This is the step
            //    that only works because the table HAS a resource id with an extension.
            var locator = new OverrideResourceLocator();
            bool located = locator.Locate(
                DialogStyleTable.ResourceId, typeof(DialogStyleTable), out IList<IResourceLocation> locations);

            Assert.IsTrue(located, "OverrideResourceLocator must resolve DIALSTYL.DAT to DAT/DIALSTYL.json");
            Assert.AreEqual(1, locations.Count);
            Assert.AreEqual(nameof(OverrideResourceProvider), locations[0].ProviderId);

            // 2. The real provider step: read the file at that path and produce the resource.
            var table = (DialogStyleTable)OverrideResourceProvider.LoadOverrideObject(
                locations[0].InternalId, typeof(DialogStyleTable), DialogStyleTable.ResourceId);

            Assert.IsNotNull(table);
            DialogStyle row = table.Get(ShippedRow);

            // 3. THE BOX MOVED — to percentages of the frame, from the shipped px rect.
            Assert.AreEqual(LayoutLength.Percent(13.75f), row.DefaultArea.Left);
            Assert.AreEqual(LayoutLength.Percent(62.5f), row.DefaultArea.Top);
            Assert.AreEqual(LayoutLength.Percent(71.25f), row.DefaultArea.Width);
            Assert.AreEqual(LayoutLength.Percent(18.75f), row.DefaultArea.Height);
            // Unit, explicitly, as well as value — LayoutLength's equality covers it, but stating
            // it separately is what makes a future change to that equality visible here.
            Assert.AreEqual(LayoutLengthUnit.Percent, row.DefaultArea.Left.Unit);
            Assert.AreEqual(LayoutLengthUnit.Percent, row.DefaultArea.Height.Unit);

            // 4. AND IT IS STILL A BOX. The document never mentioned the pens, so they must still
            //    be the shipped row's — Fill=1, Border=1, Bevel=4 — not DialogStyle's all-zero
            //    type defaults, which would make DialogPanelBuilder.AddChrome early-return and
            //    render no parchment, no border and no bevel.
            Assert.AreEqual(1, row.FillPenColor);
            Assert.AreEqual(1, row.BorderPenColor);
            Assert.AreEqual(4, row.ShadowPenColor);
            Assert.IsTrue(row.UsesTexturedFill);
            Assert.IsTrue(row.HasBorder);
            Assert.IsTrue(row.HasDropShadow);
            // ...and the text still has its inset, or wrapped text would run flush to the border.
            Assert.AreEqual(50f, row.TextPadLeft, 1e-5f);
            Assert.AreEqual(50f, row.TextPadRight, 1e-5f);

            // 5. Rows the document did not mention are untouched, still in shipped px.
            Assert.AreEqual(LayoutLength.Px(65f), table.Get(5).DefaultArea.Left);
            Assert.AreEqual(LayoutLength.Px(726f), table.Get(5).DefaultArea.Height);
            Assert.AreEqual(LayoutLength.Px(1350f), table.Get(6).DefaultArea.Width);
            Assert.AreEqual(7, table.Rows.Length, "the merge must not append rows");
            Assert.IsFalse(table.IsDefined(0), "row 0 stays the original's unused padding");
        }

        /// <summary>
        /// The trap, executable. The SAME partial document through a plain
        /// <c>JsonConvert.DeserializeObject</c> — what the override path did before this task, and
        /// what it does for every other resource — moves the box and destroys it: all three chrome
        /// pens fall to <see cref="DialogStyle"/>'s zero defaults, and zero chrome makes the panel
        /// builder skip the panel outright.
        ///
        /// <para>This is not a hypothetical. It is why <see cref="OverrideJsonMerge"/> exists, and
        /// why <c>CreditsLayout</c>'s precedent does not carry over: CreditsLayout survives a
        /// partial document only because ITS type defaults ARE the shipped values.</para>
        /// </summary>
        [Test]
        public void NaiveDeserialize_OfTheSamePartialDocument_MovesTheBoxAndLosesTheBox() {
            DialogStyleTable table = JsonConvert.DeserializeObject<DialogStyleTable>(PartialOverrideJson);
            DialogStyle row = table.Get(ShippedRow);

            // The move lands...
            Assert.AreEqual(LayoutLength.Percent(13.75f), row.DefaultArea.Left);
            // ...and the box is gone with it.
            Assert.AreEqual(0, row.FillPenColor);
            Assert.AreEqual(0, row.BorderPenColor);
            Assert.AreEqual(0, row.ShadowPenColor);
            Assert.IsFalse(row.UsesTexturedFill);
            Assert.IsFalse(row.HasBorder);
            Assert.IsFalse(row.HasDropShadow);
            Assert.AreEqual(0f, row.TextPadLeft);
        }

        /// <summary>
        /// A whole-row document — the author restates every field of the row — must also work, and
        /// must be able to take chrome AWAY. That is the half a "fill in anything that looks like
        /// a default" merge would get wrong: writing <c>0</c> explicitly has to mean zero, or rows
        /// 2 and 5 could never be made borderless.
        /// </summary>
        [Test]
        public void WholeRowOverride_CanExplicitlyRemoveTheChrome() {
            WriteOverrideFile(
                "{\"Rows\":[null,null,{\"FillPenColor\":0,\"BorderPenColor\":0,\"ShadowPenColor\":0,"
                + "\"BodyTextPenColor\":13,\"TextShadowPenSource\":7,"
                + "\"DefaultArea\":{\"Anchor\":\"Center\",\"Left\":\"13.75%\",\"Top\":\"62.5%\","
                + "\"Width\":\"71.25%\",\"Height\":\"18.75%\"},"
                + "\"TextPadLeft\":4.75,\"TextPadRight\":8.125}]}");

            var locator = new OverrideResourceLocator();
            Assert.IsTrue(locator.Locate(
                DialogStyleTable.ResourceId, typeof(DialogStyleTable), out IList<IResourceLocation> locations));
            var table = (DialogStyleTable)OverrideResourceProvider.LoadOverrideObject(
                locations[0].InternalId, typeof(DialogStyleTable), DialogStyleTable.ResourceId);
            DialogStyle row = table.Get(ShippedRow);

            Assert.AreEqual(0, row.FillPenColor, "an explicit 0 must survive the merge");
            Assert.AreEqual(0, row.BorderPenColor);
            Assert.AreEqual(0, row.ShadowPenColor);
            Assert.AreEqual(13, row.BodyTextPenColor);
            Assert.AreEqual(7, row.TextShadowPenSource);
            Assert.AreEqual(6, row.TextShadowPenColor, "derived pen follows the source, minus one");
            Assert.AreEqual(LayoutAnchor.Center, row.DefaultArea.Anchor);
            Assert.AreEqual(LayoutLength.Percent(13.75f), row.DefaultArea.Left);
            Assert.AreEqual(4.75f, row.TextPadLeft, 1e-5f);
            Assert.AreEqual(8.125f, row.TextPadRight, 1e-5f);
        }

        /// <summary>
        /// No override file → the locator says no, and the load falls through to the shipped
        /// table. This is only the OVERRIDE half: it says nothing about
        /// <c>BakResourceLocator</c>'s DIALSTYL special case, which is what makes the shipped
        /// table reachable at all — that half needs the game archive and lives in
        /// <c>BakResourceLocatorSynthesizedResourceTests</c>.
        /// </summary>
        [Test]
        public void WithNoOverrideFile_TheOverrideLocatorDeclines_LeavingTheShippedTable() {
            var locator = new OverrideResourceLocator();

            bool located = locator.Locate(
                DialogStyleTable.ResourceId, typeof(DialogStyleTable), out IList<IResourceLocation> locations);

            Assert.IsFalse(located);
            Assert.IsEmpty(locations);
            // What the BakResourceProvider path then produces (via GeneralResourceProvider's
            // synthesized branch) is exactly this — the faithful shipped rect for row 2.
            DialogStyleTable shipped = DialogStyleTable.CreateShipped();
            Assert.AreEqual(LayoutLength.Px(65f), shipped.Get(ShippedRow).DefaultArea.Left);
            Assert.AreEqual(LayoutLength.Px(66f), shipped.Get(ShippedRow).DefaultArea.Top);
            Assert.AreEqual(LayoutLength.Px(1470f), shipped.Get(ShippedRow).DefaultArea.Width);
            Assert.AreEqual(LayoutLength.Px(606f), shipped.Get(ShippedRow).DefaultArea.Height);
        }

        /// <summary>
        /// The merge must not mutate the baseline it merges onto. A shared static baseline behind
        /// <see cref="DialogStyleTable.CreateShipped"/> would mean the first mod load permanently
        /// rewrote the shipped table for the rest of the session.
        /// </summary>
        [Test]
        public void MergingAnOverride_LeavesAFreshShippedTableUntouched() {
            WriteOverrideFile(PartialOverrideJson);
            var locator = new OverrideResourceLocator();
            Assert.IsTrue(locator.Locate(
                DialogStyleTable.ResourceId, typeof(DialogStyleTable), out IList<IResourceLocation> locations));

            OverrideResourceProvider.LoadOverrideObject(
                locations[0].InternalId, typeof(DialogStyleTable), DialogStyleTable.ResourceId);

            Assert.AreEqual(
                LayoutLength.Px(65f), DialogStyleTable.CreateShipped().Get(ShippedRow).DefaultArea.Left);
            Assert.AreEqual(1, DialogStyleTable.CreateShipped().Get(ShippedRow).FillPenColor);
        }

        // ---------------------------------------------------------------------------------------
        // Authoring diagnostics. A mod author edits these documents by hand with no schema and no
        // compiler; the three ways a document can be quietly ineffective must at least produce a
        // log line. None of these change what the merge does — each also asserts the merge
        // outcome, so a "fix" that started dropping or rewriting the document would show up here.
        // ---------------------------------------------------------------------------------------

        private DialogStyleTable LoadThroughTheRealOverridePath(string json) {
            WriteOverrideFile(json);
            var locator = new OverrideResourceLocator();
            Assert.IsTrue(locator.Locate(
                DialogStyleTable.ResourceId, typeof(DialogStyleTable), out IList<IResourceLocation> locations));
            return (DialogStyleTable)OverrideResourceProvider.LoadOverrideObject(
                locations[0].InternalId, typeof(DialogStyleTable), DialogStyleTable.ResourceId);
        }

        /// <summary>
        /// A misspelt field name is the most likely mistake in a hand-written document, and the
        /// most confusing: the file loads, the game runs, and nothing changed. Newtonsoft drops
        /// unknown members at <c>ToObject</c> without a word — so the merge names it first.
        /// </summary>
        [Test]
        public void AnOverrideNamingAFieldTheResourceDoesNotHave_IsReported() {
            LogAssert.Expect(LogType.Warning, new Regex(
                @"FillPenColour.*not a field of the shipped resource"));

            DialogStyleTable table = LoadThroughTheRealOverridePath(
                "{\"Rows\":[null,null,{\"FillPenColour\":3}]}");

            // ...and the row is untouched, which is exactly why the author needs telling.
            Assert.AreEqual(1, table.Get(ShippedRow).FillPenColor);
        }

        /// <summary>
        /// The five derived read-only properties on <see cref="DialogStyle"/> (<c>HasBorder</c> and
        /// friends) are the exact names the type's own doc comments use, so an author writing
        /// <c>"HasBorder": false</c> to turn off the border is the single most likely mistake in
        /// this whole authoring surface. Before this property also carried
        /// <c>[IgnoreDataMember]</c> alongside STJ's <c>[JsonIgnore]</c>, Newtonsoft's merge
        /// baseline (built via <c>JObject.FromObject</c>) still carried the key — so this looked
        /// like an ordinary scalar-onto-scalar match: no warning, and <c>ToObject</c> silently
        /// dropped the value for want of a setter. The quietest possible authoring failure.
        /// </summary>
        [Test]
        public void AnOverrideNamingADerivedReadOnlyProperty_IsReported() {
            LogAssert.Expect(LogType.Warning, new Regex(
                @"HasBorder.*not a field of the shipped resource"));

            DialogStyleTable table = LoadThroughTheRealOverridePath(
                "{\"Rows\":[null,null,{\"HasBorder\":false}]}");

            // The property has no setter, so the row's real chrome could never have moved anyway —
            // but the point of this test is that the author is now TOLD that, instead of the
            // document silently doing nothing.
            Assert.AreEqual(1, table.Get(ShippedRow).BorderPenColor);
            Assert.IsTrue(table.Get(ShippedRow).HasBorder);
        }

        /// <summary>
        /// The table has <see cref="DialogStyleTable.Length"/> rows and the dispatcher only ever
        /// produces 1..6, so a longer document is authoring an unreachable row. The merge keeps
        /// the extra entries (array merge is index-by-index and appends the tail) — that behaviour
        /// is pinned here as well as reported, so the diagnostics cannot quietly become a filter.
        /// </summary>
        [Test]
        public void AnOverrideWithMoreRowsThanTheShippedTable_IsReported() {
            LogAssert.Expect(LogType.Warning, new Regex(@"Rows.*10 entries.*shipped resource has 7"));

            DialogStyleTable table = LoadThroughTheRealOverridePath(
                "{\"Rows\":[null,null,null,null,null,null,null,null,null,{\"FillPenColor\":3}]}");

            Assert.AreEqual(10, table.Rows.Length, "the merge keeps them; only the warning is new");
            Assert.AreEqual(1, table.Get(ShippedRow).FillPenColor, "the shipped rows are untouched");
        }

        /// <summary>
        /// THE MOST NATURAL PARTIAL DOCUMENT, and it targets nothing. An author who wants to
        /// move "a row" writes <c>{"Rows":[{...}]}</c> — one entry, no leading nulls — and the
        /// merge lands it on row 0, which is the original's init padding. No dialog moves, and
        /// before this warning nothing said so: <c>Get(0)</c> throws by design and nothing ever
        /// indexes it, so the edit is inert in the quietest possible way.
        ///
        /// <para>The merge SEMANTICS are unchanged and pinned here alongside the warning — row 0
        /// really is replaced, and the shipped rows really are untouched. Only the silence is
        /// fixed.</para>
        /// </summary>
        [Test]
        public void AnOverrideWritingIntoTheShippedNullRow_IsReported() {
            LogAssert.Expect(LogType.Warning, new Regex(
                @"Rows\[0\].*ships as null"));

            DialogStyleTable table = LoadThroughTheRealOverridePath(
                "{\"Rows\":[{\"DefaultArea\":{\"Left\":\"10%\"}}]}");

            // The author's entry landed — on the row nothing reads.
            Assert.IsTrue(table.IsDefined(0), "the merge does replace row 0; semantics are unchanged");
            Assert.AreEqual(LayoutLength.Percent(10f), table.Get(0).DefaultArea.Left);
            // ...and the row they almost certainly meant is untouched.
            Assert.AreEqual(LayoutLength.Px(65f), table.Get(ShippedRow).DefaultArea.Left,
                "row 2 is where they meant it; the document never reached it");
        }

        /// <summary>
        /// The mirror of the test above: a null the BASELINE carries at an OBJECT property is a
        /// slot the author genuinely is entitled to fill in (every shipped row leaves
        /// <c>AspectRatio</c>/<c>Flow</c>/<c>Grid</c>/<c>Padding</c> null), so filling one in must
        /// stay silent. Without this, the fix for the row-0 trap becomes a warning on every
        /// legitimate layout edit — and a diagnostic that cries wolf is one nobody reads.
        /// </summary>
        [Test]
        public void AnOverrideFillingInANullOBJECTProperty_IsNotReported() {
            var warnings = new List<string>();
            Application.LogCallback capture = (condition, stackTrace, type) => {
                if (type == LogType.Warning) {
                    warnings.Add(condition);
                }
            };
            Application.logMessageReceived += capture;
            DialogStyleTable table;
            try {
                table = LoadThroughTheRealOverridePath(
                    "{\"Rows\":[null,null,{\"DefaultArea\":{\"Padding\":{\"Left\":\"4.5%\"}}}]}");
            } finally {
                Application.logMessageReceived -= capture;
            }

            Assert.AreEqual(LayoutLength.Percent(4.5f),
                table.Get(ShippedRow).DefaultArea.Padding.Left,
                "the author's padding landed");
            // LogAssert.NoUnexpectedReceived() cannot stand in here: it only fails on
            // Error/Assert/Exception, so a spurious WARNING would sail past it.
            CollectionAssert.IsEmpty(warnings,
                "filling in a null object property is legitimate authoring and must stay silent");
        }

        /// <summary>
        /// A scalar merged onto an object is discarded by Newtonsoft outright — the loudest kind
        /// of silence, since a whole row's worth of the document simply evaporates.
        /// </summary>
        [Test]
        public void AnOverridePuttingAScalarWhereTheResourceHasAnObject_IsReported() {
            LogAssert.Expect(LogType.Warning, new Regex(
                @"Rows\[2\].*Integer where the shipped resource has Object"));

            DialogStyleTable table = LoadThroughTheRealOverridePath("{\"Rows\":[null,null,5]}");

            Assert.AreEqual(LayoutLength.Px(65f), table.Get(ShippedRow).DefaultArea.Left,
                "the discarded scalar leaves the shipped row in place");
        }

        /// <summary>
        /// TASK-62. <c>DialogStyleTable.Type</c> is part of <see cref="GameData.Resources.IResource"/>,
        /// not a decorative derived property like <see cref="DialogStyle"/>'s <c>HasBorder</c> &amp;
        /// friends — but it has exactly the same no-setter shape, and until this task it carried
        /// neither <c>[JsonIgnore]</c> nor <c>[IgnoreDataMember]</c>. Because <c>Type</c> IS emitted
        /// to <c>generated/DAT/DIALSTYL.json</c> (it is the first key a mod author sees when they
        /// copy that file), an author writing <c>"Type": "REQ"</c> at the document root would have
        /// reached the merge baseline (<c>JObject.FromObject</c> has no reason to omit a plain
        /// get-only property), hit scalar-onto-scalar in the diagnostics walk (silently "matched", no
        /// warning), and then been discarded by <c>ToObject</c> for want of a setter — the exact trap
        /// <see cref="DialogStyle"/>'s own doc comment warns against, on the sibling type that is
        /// supposed to be the example of avoiding it.
        ///
        /// <para>This test is the falsifiable proof <c>Type</c> now carries
        /// <c>[IgnoreDataMember]</c> (STJ's <c>[JsonIgnore]</c> is deliberately NOT added — the
        /// extractor corpus keeps emitting <c>"Type": "DAT"</c>, since that key is genuinely
        /// informative, unlike the derived pens). Revert the attribute and this goes red: the
        /// warning below stops firing and the assertion after it would (if it still ran) be the only
        /// sign anything was ever wrong.</para>
        /// </summary>
        [Test]
        public void AnOverrideNamingType_IsReportedAndDoesNotSilentlyMatch() {
            LogAssert.Expect(LogType.Warning, new Regex(
                @"Type.*not a field of the shipped resource"));

            DialogStyleTable table = LoadThroughTheRealOverridePath("{\"Type\":\"REQ\"}");

            // The property has no setter, so an author's "Type" could never have changed anything —
            // the point of this test is that the author is now TOLD that, instead of the document
            // silently doing nothing. The table's real Type must also survive untouched.
            Assert.AreEqual(GameData.Resources.ResourceType.DAT, table.Type);
        }

        /// <summary>
        /// Generalises the fix above to the whole family of resources this project's mod-override
        /// merge can reach today — everything registered in
        /// <see cref="OverrideResourceProvider"/>'s baseline table (currently just
        /// <see cref="DialogStyleTable"/> itself; a future addition inherits this check for free).
        /// Mirrors, as an executable assertion rather than only a comment, the rule
        /// <see cref="DialogStyle"/>'s remarks state in prose: any public property with a getter but
        /// no public setter is a trap on the merge path unless it carries
        /// <c>[IgnoreDataMember]</c> (Newtonsoft's ignore — the serializer that actually builds the
        /// merge baseline and reads an override document). <c>[JsonIgnore]</c> alone does not
        /// satisfy this: it is System.Text.Json's attribute and Newtonsoft does not recognise it at
        /// all.
        ///
        /// <para>Scoped to merge-baseline types deliberately, not every <see
        /// cref="GameData.Resources.IResource"/> in GameData: a resource that is not merged onto a
        /// baseline is deserialized standalone via a plain
        /// <c>JsonConvert.DeserializeObject</c>, where a no-setter property is silently skipped by
        /// Newtonsoft's writability check regardless of this attribute — there is no diagnostics
        /// walk in that path for the attribute to change the behaviour of. The trap this task fixes
        /// is specific to <see cref="OverrideJsonMerge"/>'s baseline-comparison, so that is exactly
        /// the surface this test polices.</para>
        /// </summary>
        [Test]
        public void EveryMergeBaselineResource_HasNoUnfencedNoSetterProperties() {
            var offenders = new List<string>();

            foreach (System.Type resourceType in OverrideResourceProvider.ShippedBaselineTypes) {
                foreach (System.Reflection.PropertyInfo property in
                         resourceType.GetProperties(
                             System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) {
                    if (property.GetSetMethod(nonPublic: false) != null) {
                        continue; // Settable — Newtonsoft can write into it, so it is not the trap.
                    }
                    bool fenced = System.Attribute.IsDefined(property, typeof(System.Runtime.Serialization.IgnoreDataMemberAttribute));
                    if (!fenced) {
                        offenders.Add(resourceType.Name + "." + property.Name);
                    }
                }
            }

            CollectionAssert.IsEmpty(offenders,
                "these get-only properties on a merge-baseline resource are reachable by "
                + "OverrideJsonMerge but carry no [IgnoreDataMember], so an override document naming "
                + "them would be silently matched and discarded rather than reported: "
                + string.Join(", ", offenders));
        }
    }
}
