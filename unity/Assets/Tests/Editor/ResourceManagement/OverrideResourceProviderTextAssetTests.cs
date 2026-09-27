namespace BakAgain.Tests.Editor.ResourceManagement {
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using GameData.Resources.Text;
    using NUnit.Framework;
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.ResourceManagement.ResourceLocations;

    /// <summary>
    /// Task 9 shipped the merge side of the UI string override (<c>UiStringLoader</c> asks for a
    /// <see cref="TextAsset"/> and, when it gets one, merges it over the embedded catalog) but not
    /// the supply side: <see cref="OverrideResourceProvider"/> had no <see cref="TextAsset"/>
    /// handling, so <see cref="OverrideResourceProvider.LoadOverrideObject"/> fell through to
    /// <c>JsonConvert.DeserializeObject(json, typeof(TextAsset), ...)</c> — which cannot populate
    /// <c>TextAsset.text</c> (no public setter) from a flat key/value document that isn't a
    /// serialized <see cref="TextAsset"/> to begin with. The override path was inert: a mod's
    /// <c>uistrings.json</c> resolved to a location but produced nothing usable,
    /// <c>UiStringCatalog.FromJson</c> threw on the empty text, and <c>UiStringLoader</c> silently
    /// fell back to the embedded catalog every time — defeating the entire point of Task 9.
    ///
    /// <para>Same proof shape as
    /// <see cref="BakAgain.Tests.Editor.UI.DialogStyleTableOverrideTests"/>: the REAL
    /// <see cref="OverrideResourceLocator"/> resolves the key to a file on disk, and the REAL
    /// <see cref="OverrideResourceProvider.LoadOverrideObject"/> turns it into the resource — no
    /// fabricated <c>ProvideHandle</c>, no mocked file system.</para>
    /// </summary>
    public class OverrideResourceProviderTextAssetTests {
        private const string OverrideJson = "{\"base:uistring:menu.new_game\":\"Neue Reise\"}";

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

        /// <summary>
        /// THE PROOF. A mod's <c>uistrings.json</c>, resolved by the real locator and loaded by
        /// the real provider, arrives as a <see cref="TextAsset"/> carrying the file's raw text —
        /// verbatim, not run through <c>JsonConvert</c> — so <c>UiStringLoader</c>'s own
        /// <c>UiStringCatalog.FromJson(overrideText.text)</c> call (the merge Task 9 already
        /// implemented) finally has something parseable to work with.
        /// </summary>
        [Test]
        public void TextAssetOverrideOnDisk_ResolvedByTheRealLocator_ReturnsTheFileTextVerbatim() {
            // Written to <OverridePath>/JSON/uistrings.json. OverrideResourceLocator derives the
            // subdirectory from the KEY's own extension (".json" -> "JSON"), not from the
            // requested resource type, so this is exactly where Locate() below looks — verified by
            // tracing OverrideResourceLocator.Locate rather than assumed.
            _overrides.Write("JSON", "uistrings.json", OverrideJson);

            var locator = new OverrideResourceLocator();
            bool located = locator.Locate(
                UiStringCatalog.ResourceId, typeof(TextAsset), out IList<IResourceLocation> locations);

            Assert.IsTrue(located, "OverrideResourceLocator must resolve uistrings.json to JSON/uistrings.json");
            Assert.AreEqual(1, locations.Count);
            Assert.AreEqual(nameof(OverrideResourceProvider), locations[0].ProviderId);

            var textAsset = (TextAsset)OverrideResourceProvider.LoadOverrideObject(
                locations[0].InternalId, typeof(TextAsset), UiStringCatalog.ResourceId);

            Assert.IsNotNull(textAsset);
            Assert.AreEqual(OverrideJson, textAsset.text);

            // ...and it round-trips through the exact parse UiStringLoader performs, proving the
            // supply side (this fix) and the merge side (already Task 9's) actually connect.
            UiStringCatalog catalog = UiStringCatalog.FromJson(textAsset.text);
            Assert.AreEqual("Neue Reise", catalog.Get("base:uistring:menu.new_game"));
        }

        /// <summary>
        /// The trap this task fixes, executable: the SAME document through the generic JSON path —
        /// what <c>LoadOverrideObject</c> did before this fix, for want of a <see cref="TextAsset"/>
        /// branch — cannot produce a <see cref="TextAsset"/> with usable text. Deliberately
        /// tolerant of exactly HOW it fails (Newtonsoft may throw trying to construct a
        /// <c>UnityEngine.Object</c> subtype, or may return an instance with empty/null text): the
        /// point pinned here is only that no failure mode of the naive path yields usable text,
        /// which is the bug this fix closes.
        /// </summary>
        [Test]
        public void NaiveDeserialize_OfTheSameDocument_ProducesNoUsableText() {
            TextAsset result = null;
            Exception caught = null;
            try {
                result = (TextAsset)Newtonsoft.Json.JsonConvert.DeserializeObject(
                    OverrideJson, typeof(TextAsset));
            } catch (Exception ex) {
                caught = ex;
            }

            Assert.IsTrue(caught != null || result == null || string.IsNullOrEmpty(result.text),
                "the naive JSON path must not produce a TextAsset with usable text");
        }
    }
}
