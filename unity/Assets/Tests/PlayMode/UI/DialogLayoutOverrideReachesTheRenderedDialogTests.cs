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
    /// The sibling fence to <see cref="DialogStyleOverrideReachesTheRenderedDialogTests"/>, for the
    /// panel's INTERNAL geometry (<see cref="DialogLayout"/>) rather than the box's own rect.
    ///
    /// <para><b>Why it is separate.</b> The style rows and the layout travel on the same resource
    /// but through different code: the rows reach <c>LayoutApplier.Apply</c> via
    /// <c>DialogManager.ResolveArea</c>, while the layout is handed to
    /// <c>DialogPanelBuilder</c> as an argument. Drop that argument — pass <c>null</c>, or forget
    /// to thread <c>styleTable.Layout</c> through — and every row-placement test still passes
    /// while every override of the pill, the body offsets or the chrome edges silently stops
    /// working, because <c>DialogPanelBuilder</c> falls back to the shipped defaults and the
    /// fallback looks exactly like the faithful game. These tests are the ones that go red.</para>
    ///
    /// <para>There are TWO such arguments, on two independent lines: <c>ShowEntryCore</c>'s (the
    /// shown dialog panel) and <c>BuildStyledBoxAsync</c>'s (a dialog-styled box built into a
    /// caller's own tree — the inventory stat block). Each has its own test here, so dropping the
    /// argument at one site cannot hide behind the other.</para>
    ///
    /// <para>They drive the whole production stack — real <c>InitializeResourceManagement</c> with
    /// overrides enabled, real Addressables, real locator/provider/JSON-merge, real
    /// <c>DialogManager</c> / <c>DialogPanelBuilder</c> / <c>LayoutApplier</c> — and read back the
    /// UI Toolkit styles of the elements the manager actually built.</para>
    /// </summary>
    public class DialogLayoutOverrideReachesTheRenderedDialogTests {
        // A PARTIAL document naming three layout values and nothing else. Partial on purpose: the
        // assertions check both that these landed AND that everything the document never mentioned
        // (the shipped rows, the rest of the layout) survived, which together can only be true if
        // the MERGED resource reached the renderer.
        //
        // 31 / 13.75% / 47 are unlike every shipped value (6 / 180px / 6), so no fallback, no
        // defaulting and no unit-blind path can satisfy these assertions by accident.
        private const string PartialOverrideJson =
            "{\"Layout\":{\"ChromeBorderWidth\":31.0,\"NarrativeBodyTop\":\"13.75%\","
            + "\"SpeakerPillBorderWidth\":47.0}}";

        [SetUp]
        public void RequireShippedGameData() {
            ShippedGameData.RequireOrIgnore();
        }

        /// <summary>
        /// A DialogManager wired far enough to render an entry that HAS text. Supplying the
        /// palette keeps the test off OPTIONS.PAL — the palette is not what is under test and the
        /// pens only have to resolve to something. The <c>GameSession</c> is the one collaborator
        /// the text path genuinely needs: <c>ShowEntryCore</c> resolves <c>@N</c> text variables
        /// through <c>DialogSlotPopulator.BuildSlots(entry, session…)</c> for any non-empty text,
        /// so a bare <c>AddComponent</c> NREs there. The rest of the constructor's collaborators
        /// are only reached on the wait-for-input path, which <c>DisplayEntry</c> does not take.
        /// </summary>
        private static DialogManager Wire(GameObject host) {
            DialogManager manager = host.AddComponent<DialogManager>();
            manager.Construct(null, null, null, new BakAgain.Core.GameSession(), null, null);
            manager.SetActivePalette(new Color[256]);
            return manager;
        }

        /// <summary>
        /// A bordered dialog (the default fallback row 2) wears the override document's chrome
        /// edge width — and still sits at the shipped rect the document never mentioned.
        /// </summary>
        [UnityTest]
        public IEnumerator TheChromeEdgeWidth_ComesFromTheOverrideDocument_NotFromABuilderConstant() =>
            UniTask.ToCoroutine(async () => {
                using (var overrides = new TempOverrideDirectory())
                using (new IsolatedResourceLocators()) {
                    overrides.Write("DAT", "DIALSTYL.json", PartialOverrideJson);
                    ResourceManagementInitializer.InitializeResourceManagement();

                    var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                    try {
                        DialogManager manager = Wire(host);

                        // A bare entry resolves to row 2 — bordered, so it builds chrome.
                        await manager.DisplayEntry(new DialogEntry { Flags = DialogEntryFlags.SkipOpenWipe });

                        VisualElement panel =
                            host.GetComponent<UIDocument>().rootVisualElement.Q("BakDialogPanel");
                        Assert.IsNotNull(panel, "DialogManager built no panel at all");

                        VisualElement box = panel.Q("BakDialogChrome");
                        Assert.IsNotNull(box, "row 2's shipped chrome pens must have survived the merge");
                        Assert.AreEqual(31f, box.style.borderTopWidth.value,
                            "the chrome edge width must come from the override document, not from "
                            + "DialogLayout's shipped default (6) — a 6 here means the layout never "
                            + "reached DialogPanelBuilder");
                        Assert.AreEqual(31f, box.style.borderLeftWidth.value, "all four edges");

                        // Untouched by the document: the panel is still at row 2's shipped rect,
                        // and the panel shadow still at the shipped offset. A whole-document
                        // replace (rather than a merge) would have lost both.
                        Assert.AreEqual(new Length(65f, LengthUnit.Pixel), panel.style.left.value,
                            "the document named no rect, so row 2's shipped one must survive");
                        Assert.AreEqual(-6f, panel.Q("BakDialogShadow").style.translate.value.x.value,
                            "the document named no shadow offset, so the shipped 6 must survive");
                    } finally {
                        Object.DestroyImmediate(host);
                    }
                }
            });

        /// <summary>
        /// A borderless narrative strip (row 3) starts its body text at the override document's
        /// inset, in the document's UNIT — the assertion a translator that flattened the
        /// percentage to px would fail while still reporting "13.75".
        /// </summary>
        [UnityTest]
        public IEnumerator TheNarrativeBodyInset_ComesFromTheOverrideDocument_KeepingItsUnit() =>
            UniTask.ToCoroutine(async () => {
                using (var overrides = new TempOverrideDirectory())
                using (new IsolatedResourceLocators()) {
                    overrides.Write("DAT", "DIALSTYL.json", PartialOverrideJson);
                    ResourceManagementInitializer.InitializeResourceManagement();

                    var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                    try {
                        DialogManager manager = Wire(host);

                        // PlainWithoutBox resolves to row 3 — borderless, so the body keeps its
                        // top inset instead of being vertically centred in the box.
                        await manager.DisplayEntry(new DialogEntry {
                            Text = "A narrative line.",
                            DialogType = DialogType.PlainWithoutBox,
                            Flags = DialogEntryFlags.SkipOpenWipe,
                        });

                        VisualElement body = host.GetComponent<UIDocument>()
                            .rootVisualElement.Q("BakDialogPanel").Q("BakDialogBody");
                        Assert.IsNotNull(body, "no body label was built");
                        Assert.AreEqual(new Length(13.75f, LengthUnit.Percent), body.style.top.value);
                        Assert.AreEqual(LengthUnit.Percent, body.style.top.value.unit,
                            "the author wrote a percentage; a px value here means the override "
                            + "never arrived (the shipped default is 180px)");
                    } finally {
                        Object.DestroyImmediate(host);
                    }
                }
            });

        /// <summary>
        /// The speaker pill's rim — the value that used to be a bare <c>6</c> literal in the
        /// builder, duplicating the chrome's width without referencing it. It is its own datum
        /// now, so the document moves it independently: 47 on the pill while the chrome carries 31
        /// from the same document. A builder that had wired the pill to <c>ChromeBorderWidth</c>
        /// would report 31 here.
        /// </summary>
        [UnityTest]
        public IEnumerator TheSpeakerPillRim_IsItsOwnDatum_MovedIndependentlyOfTheChrome() =>
            UniTask.ToCoroutine(async () => {
                using (var overrides = new TempOverrideDirectory())
                using (new IsolatedResourceLocators()) {
                    overrides.Write("DAT", "DIALSTYL.json", PartialOverrideJson);
                    ResourceManagementInitializer.InitializeResourceManagement();

                    var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                    try {
                        DialogManager manager = Wire(host);

                        // ColoredWithoutBox + a '#Name#' prefix is what builds the pill.
                        await manager.DisplayEntry(new DialogEntry {
                            Text = "#Gorath#Spoken body.",
                            DialogType = DialogType.ColoredWithoutBox,
                            Flags = DialogEntryFlags.SkipOpenWipe,
                        });

                        VisualElement pill = host.GetComponent<UIDocument>()
                            .rootVisualElement.Q("BakDialogPanel").Q("BakDialogSpeakerPill");
                        Assert.IsNotNull(pill, "no speaker pill was built");
                        Assert.AreEqual(47f, pill.style.borderTopWidth.value,
                            "the pill rim must come from SpeakerPillBorderWidth in the document");
                        Assert.AreNotEqual(31f, pill.style.borderTopWidth.value,
                            "and must NOT be wired to ChromeBorderWidth, which the same document "
                            + "sets to 31");
                        // The pill's shipped padding is what the document did not name — it must
                        // still be there, or the merge lost the rest of the layout.
                        Assert.AreEqual(new Length(90f, LengthUnit.Pixel), pill.style.paddingLeft.value);
                        Assert.AreEqual(new Length(18f, LengthUnit.Pixel), pill.style.paddingTop.value);
                    } finally {
                        Object.DestroyImmediate(host);
                    }
                }
            });

        /// <summary>
        /// THE SECOND CALL SITE. <see cref="DialogManager.BuildStyledBoxAsync"/> — the inventory's
        /// "More Info" stat block and anything else that wants a dialog-styled box in its OWN tree —
        /// threads <c>styleTable.Layout</c> into <c>DialogPanelBuilder.BuildChrome</c> on a line of
        /// its own, separate from <c>ShowEntryCore</c>'s.
        ///
        /// <para>Every other test in this fixture goes through <c>DisplayEntry</c>; none of them
        /// touches that line. Pass <c>null</c> there instead of <c>styleTable.Layout</c> and they
        /// all stay green while a mod author who sets <c>ChromeBorderWidth</c> watches dialog frames
        /// thicken and the stat block keep its shipped 6px rim, with no diagnostic. This is the test
        /// that goes red — same document, same assertion, the other entry point.</para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheStyledBoxChromeEdgeWidth_ComesFromTheOverrideDocument_AtTheOtherCallSite() =>
            UniTask.ToCoroutine(async () => {
                using (var overrides = new TempOverrideDirectory())
                using (new IsolatedResourceLocators()) {
                    overrides.Write("DAT", "DIALSTYL.json", PartialOverrideJson);
                    ResourceManagementInitializer.InitializeResourceManagement();

                    var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                    try {
                        DialogManager manager = Wire(host);
                        var callersTree = new VisualElement { name = "CallersOwnTree" };

                        // Same bare entry as the panel test — row 2, bordered, so it builds chrome —
                        // but built into the caller's tree instead of shown through the overlay.
                        VisualElement box = await manager.BuildStyledBoxAsync(
                            new DialogEntry { Flags = DialogEntryFlags.SkipOpenWipe }, callersTree);

                        Assert.IsNotNull(box, "BuildStyledBoxAsync built no box at all");
                        VisualElement chrome = box.Q("BakDialogChrome");
                        Assert.IsNotNull(chrome, "row 2's shipped chrome pens must have survived the merge");
                        Assert.AreEqual(31f, chrome.style.borderTopWidth.value,
                            "the styled box's edge width must come from the override document too — "
                            + "a 6 here means BuildStyledBoxAsync stopped passing styleTable.Layout "
                            + "to BuildChrome, which no other test in this fixture would notice");
                        Assert.AreEqual(31f, chrome.style.borderLeftWidth.value, "all four edges");

                        // Untouched by the document: the box is still at row 2's shipped rect, so a
                        // whole-document replace (rather than a merge) is ruled out here too.
                        Assert.AreEqual(new Length(65f, LengthUnit.Pixel), box.style.left.value,
                            "the document named no rect, so row 2's shipped one must survive");
                    } finally {
                        Object.DestroyImmediate(host);
                    }
                }
            });
    }
}
