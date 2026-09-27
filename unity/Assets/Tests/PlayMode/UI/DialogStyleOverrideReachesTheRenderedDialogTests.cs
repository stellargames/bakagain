namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Dialog;
    using NUnit.Framework;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The keystone: <see cref="DialogManager"/> must take its style rows from the RESOURCE
    /// SYSTEM, so that a mod author's <c>DAT/DIALSTYL.json</c> actually reaches the panel the
    /// player sees.
    ///
    /// <para><b>Why this fixture exists.</b> Every other test around the dialog style table proves
    /// the pieces — the locator resolves the key, the provider merges the document onto the
    /// shipped baseline, the merged table carries the author's rect. None of them touches the one
    /// line that connects the pieces to the renderer. Replace
    /// <c>await _resources.GetStyleTableAsync()</c> in <see cref="DialogManager"/> with
    /// <c>new DialogStyleTable()</c> and every one of those tests still passes, while every mod
    /// override silently stops working: the panel would render at the shipped rect forever and
    /// nothing would say so. These two tests are the ones that go red.</para>
    ///
    /// <para>They drive the whole production stack — real
    /// <c>ResourceManagementInitializer.InitializeResourceManagement</c> with overrides enabled,
    /// real Addressables, real locator/provider/merge, real
    /// <c>DialogManager</c>/<c>DialogPanelBuilder</c>/<c>LayoutApplier</c> — and assert on the UI
    /// Toolkit styles of the element the manager actually built. Nothing is stubbed, so there is
    /// no seam at which a passing test could stop corresponding to what the player sees.</para>
    ///
    /// <para>Shipped row 2 (the default fallback style, which a bare <c>DialogEntry</c> resolves
    /// to) is <c>65 / 66 / 1470 / 606</c> <b>px</b>; the override below is
    /// <c>13.75 / 62.5 / 71.25 / 18.75</c> <b>percent</b> — a different unit AND a different
    /// magnitude on all four edges, so no fallback, no defaulting and no unit-blind path can
    /// satisfy these assertions by accident.</para>
    /// </summary>
    public class DialogStyleOverrideReachesTheRenderedDialogTests {
        // A PARTIAL document naming row 2's area and nothing else — the normal thing a mod author
        // writes. It is partial on purpose: the assertions below check both that the move landed
        // AND that the chrome the document never mentioned survived, which together can only be
        // true if the merged resource (not a locally constructed table, and not a naive
        // standalone deserialize) reached the renderer.
        private const string PartialOverrideJson =
            "{\"Rows\":[null,null,{\"DefaultArea\":{\"Left\":\"13.75%\",\"Top\":\"62.5%\","
            + "\"Width\":\"71.25%\",\"Height\":\"18.75%\"}}]}";

        private static readonly Length OverriddenLeft = new Length(13.75f, LengthUnit.Percent);
        private static readonly Length OverriddenTop = new Length(62.5f, LengthUnit.Percent);
        private static readonly Length OverriddenWidth = new Length(71.25f, LengthUnit.Percent);
        private static readonly Length OverriddenHeight = new Length(18.75f, LengthUnit.Percent);

        [SetUp]
        public void RequireShippedGameData() {
            // ResourceManagementInitializer registers BakResourceLocator, whose constructor reads
            // KRONDOR.001's directory. Using the real initializer (rather than hand-registering
            // the override half) is the point: it is the production entry point, in production
            // order, with overrides enabled.
            ShippedGameData.RequireOrIgnore();
        }

        /// <summary>
        /// THE FENCE. The panel <see cref="DialogManager"/> builds for a shown dialog
        /// (<c>ShowEntryCore</c>) is placed at the override document's rect, in the override
        /// document's unit — and still wears the chrome the document did not mention.
        /// </summary>
        [UnityTest]
        public IEnumerator TheShownDialogPanel_IsPlacedByTheOverrideDocument_NotByAShippedTable() =>
            UniTask.ToCoroutine(async () => {
                using (var overrides = new TempOverrideDirectory())
                using (new IsolatedResourceLocators()) {
                    overrides.Write("DAT", "DIALSTYL.json", PartialOverrideJson);
                    ResourceManagementInitializer.InitializeResourceManagement();

                    var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                    try {
                        DialogManager manager = host.AddComponent<DialogManager>();
                        // Supplying the palette keeps the test off OPTIONS.PAL — the palette is
                        // not what is under test, and the pens only have to resolve to something.
                        manager.SetActivePalette(new Color[256]);

                        // DisplayEntry renders and returns, leaving the panel up for inspection
                        // (ShowEntry would block on a dismiss). Same ShowEntryCore either way.
                        await manager.DisplayEntry(NoWipeEntry());

                        VisualElement panel =
                            host.GetComponent<UIDocument>().rootVisualElement.Q("BakDialogPanel");
                        Assert.IsNotNull(panel, "DialogManager built no panel at all");
                        AssertPlacedByTheOverride(panel);
                        Assert.IsNotNull(panel.Q("BakDialogChrome"),
                            "the override named only the area, so row 2's shipped chrome pens must "
                            + "have survived the merge — a panel with no chrome means the renderer "
                            + "got DialogStyle's all-zero type defaults instead of the merged row");
                    } finally {
                        Object.DestroyImmediate(host);
                    }
                }
            });

        /// <summary>
        /// The same fence for the OTHER call site: <see cref="DialogManager.BuildStyledBoxAsync"/>,
        /// which builds a dialog-styled box into a caller's own tree (the inventory's stat block).
        /// It resolves its style through the same resource, so it needs its own assertion —
        /// removing the await at one call site must not be able to hide behind the other.
        /// </summary>
        [UnityTest]
        public IEnumerator TheStyledBoxBuiltForACallersTree_IsPlacedByTheOverrideDocument() =>
            UniTask.ToCoroutine(async () => {
                using (var overrides = new TempOverrideDirectory())
                using (new IsolatedResourceLocators()) {
                    overrides.Write("DAT", "DIALSTYL.json", PartialOverrideJson);
                    ResourceManagementInitializer.InitializeResourceManagement();

                    var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                    try {
                        DialogManager manager = host.AddComponent<DialogManager>();
                        manager.SetActivePalette(new Color[256]);
                        var callersTree = new VisualElement { name = "CallersOwnTree" };

                        VisualElement box = await manager.BuildStyledBoxAsync(NoWipeEntry(), callersTree);

                        Assert.IsNotNull(box, "BuildStyledBoxAsync built no box at all");
                        AssertPlacedByTheOverride(box);
                        Assert.IsNotNull(box.Q("BakDialogChrome"),
                            "the styled box exists to carry the real chrome — the merged row's pens");
                    } finally {
                        Object.DestroyImmediate(host);
                    }
                }
            });

        // A bare entry: no source dialog type and no actor, so DialogTypeResolver returns the
        // default fallback row 2 — the row the override document moves. SkipOpenWipe keeps the
        // reveal animation (and its px-only wipe rect) out of a percentage-area test; the wipe is
        // a separate, already-tested concern.
        private static DialogEntry NoWipeEntry() {
            return new DialogEntry { Flags = DialogEntryFlags.SkipOpenWipe };
        }

        // Value AND unit on every edge. LayoutApplier writes a percentage as a percentage, so a
        // translator that flattened it to px would still produce "13.75" — asserting the Length
        // (which carries its unit) is what makes that visible.
        private static void AssertPlacedByTheOverride(VisualElement element) {
            Assert.AreEqual(Position.Absolute, element.style.position.value);
            Assert.AreEqual(OverriddenLeft, element.style.left.value, "left edge");
            Assert.AreEqual(OverriddenTop, element.style.top.value, "top edge");
            Assert.AreEqual(OverriddenWidth, element.style.width.value, "width");
            Assert.AreEqual(OverriddenHeight, element.style.height.value, "height");
            Assert.AreEqual(LengthUnit.Percent, element.style.left.value.unit,
                "the author wrote percentages; a px value here means the override never arrived");
        }
    }
}
