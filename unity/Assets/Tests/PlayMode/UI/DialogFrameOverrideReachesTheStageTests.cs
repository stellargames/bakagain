namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Dialog;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// THE FENCE for the dialog surface's <see cref="DesignFrame"/>.
    ///
    /// <para><b>What was wrong.</b> <see cref="DialogManager"/> passed <c>null</c> to
    /// <c>CanonicalStage.GetOrCreate</c> — the "no frame-bearing resource in hand" fallback. It
    /// produced a correct-looking dialog for exactly one reason: the fallback happens to be the
    /// canonical 1600x1200 frame, which is also what the data says. So the dialog rendered right
    /// while reading nothing, and <see cref="LayoutFit"/> — the property that decides whether a
    /// screen pillarboxes or spans the window — was unreachable for dialogs entirely.</para>
    ///
    /// <para><b>Why the fixture frame is 800x600.</b> Nothing in the shipped data, the type
    /// defaults or <c>CanonicalStage</c>'s own fallback can produce it. A test written against the
    /// canonical numbers would have passed before this task as happily as after, which is the
    /// exact way "wired but never read" data has slipped through three times in this effort.
    /// Restore the <c>null</c> at the call site and the first test below goes red.</para>
    ///
    /// <para>Whole production stack, nothing stubbed: real
    /// <c>ResourceManagementInitializer.InitializeResourceManagement</c> with overrides enabled,
    /// real Addressables, real <c>OverrideResourceLocator</c>/<c>OverrideResourceProvider</c>/
    /// <c>OverrideJsonMerge</c>, real <see cref="DialogManager"/> and <c>CanonicalStage</c>.</para>
    /// </summary>
    public class DialogFrameOverrideReachesTheStageTests {
        // A frame no fallback can produce, in the document a mod author actually writes.
        private const string FrameOverrideJson =
            "{\"Frame\":{\"Width\":800,\"Height\":600}}";

        // The whole point of the property: Fit is a per-screen policy, and stating it must be
        // enough on its own — the dimensions come from the shipped baseline underneath.
        private const string FillOverrideJson =
            "{\"Frame\":{\"Fit\":\"Fill\"}}";

        // A document that moves a row and never mentions the frame — the common case. It must
        // still arrive with the canonical frame, which can only happen if the merge BASELINE was
        // stamped (OverrideResourceProvider.ShippedDialogStyleTable). A baseline with a 0x0 frame
        // would look identical on screen, because CanonicalStage warns and falls back to the same
        // numbers — so this one asserts on the resource, where the two are still distinguishable.
        private const string RowsOnlyOverrideJson =
            "{\"Rows\":[null,null,{\"DefaultArea\":{\"Left\":\"13.75%\"}}]}";

        [SetUp]
        public void RequireShippedGameData() {
            ShippedGameData.RequireOrIgnore();
        }

        /// <summary>
        /// THE FENCE. The stage <see cref="DialogManager"/> hosts the dialog panel in is sized by
        /// the style table's frame, not by <c>CanonicalStage</c>'s null fallback.
        /// </summary>
        [UnityTest]
        public IEnumerator TheDialogStage_IsSizedByTheOverrideDocumentsFrame_NotByTheCanonicalFallback() =>
            UniTask.ToCoroutine(async () => {
                using (var overrides = new TempOverrideDirectory())
                using (new IsolatedResourceLocators()) {
                    overrides.Write("DAT", "DIALSTYL.json", FrameOverrideJson);
                    ResourceManagementInitializer.InitializeResourceManagement();

                    var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                    try {
                        VisualElement stage = await ShowADialogAndReturnItsStage(host);

                        Assert.AreEqual(new Length(800f, LengthUnit.Pixel), stage.style.width.value,
                            "the stage width must come from DIALSTYL.json's Frame; 1600 here means "
                            + "the manager is still passing null and getting the canonical fallback");
                        Assert.AreEqual(new Length(600f, LengthUnit.Pixel), stage.style.height.value,
                            "the stage height must come from DIALSTYL.json's Frame");
                    } finally {
                        Object.DestroyImmediate(host);
                    }
                }
            });

        /// <summary>
        /// <see cref="LayoutFit"/> reachability — the reason the frame had to be wired at all.
        /// A document naming ONLY the fit makes the dialog surface span the window (a 100%
        /// stage) instead of sitting in a fixed pillarboxed box, and the dimensions it never
        /// mentioned come from the shipped baseline.
        ///
        /// <para>That second half is asserted on the RESOURCE, not on the stage, and it is a
        /// separate fence from <see cref="AnOverrideThatNeverMentionsTheFrame_StillCarriesTheCanonicalOne"/>:
        /// that one pins the merge BASELINE (a document silent about <c>Frame</c> entirely), this
        /// one pins the deep merge WITHIN <c>Frame</c> (a document that states one of its three
        /// fields). Under <c>Fit=Fill</c> <c>CanonicalStage.ApplyFrame</c> ignores the dimensions
        /// altogether, so the rendered stage cannot tell a deep-merged <c>{1600,1200,Fill}</c> from
        /// a wholesale-replaced <c>{0,0,Fill}</c> — and a regression to object-level replace would
        /// mean an author writing <c>{"Frame":{"Width":1920}}</c> got <c>{1920,0,Contain}</c>, which
        /// <c>ApplyFrame</c>'s non-positive guard then discards back to canonical, silently
        /// ignoring the width they asked for.</para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheDialogSurface_SpansTheWindow_WhenTheDocumentAsksForFill() =>
            UniTask.ToCoroutine(async () => {
                using (var overrides = new TempOverrideDirectory())
                using (new IsolatedResourceLocators()) {
                    overrides.Write("DAT", "DIALSTYL.json", FillOverrideJson);
                    ResourceManagementInitializer.InitializeResourceManagement();

                    var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                    try {
                        VisualElement stage = await ShowADialogAndReturnItsStage(host);

                        Assert.AreEqual(new Length(100f, LengthUnit.Percent), stage.style.width.value,
                            "Fit=Fill must make the stage span its parent; a pixel width here means "
                            + "the fit never reached CanonicalStage");
                        Assert.AreEqual(new Length(100f, LengthUnit.Percent), stage.style.height.value,
                            "Fit=Fill must make the stage span its parent");

                        // The deep merge INSIDE Frame: the document stated Fit alone, so Width and
                        // Height must have survived from the shipped baseline's frame rather than
                        // being replaced along with it. Invisible on the stage under Fill.
                        UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<DialogStyleTable> handle =
                            Addressables.LoadAssetAsync<DialogStyleTable>(DialogStyleTable.ResourceId);
                        DialogStyleTable table = handle.WaitForCompletion();
                        Assert.AreEqual(LayoutFit.Fill, table.Frame.Fit,
                            "the document's Fit never reached the merged table");
                        Assert.AreEqual(Canonical.Width, table.Frame.Width,
                            "0 here means Frame was replaced wholesale instead of merged key-by-key: "
                            + "a document naming one of its three fields would drop the other two");
                        Assert.AreEqual(Canonical.Height, table.Frame.Height,
                            "0 here means Frame was replaced wholesale instead of merged key-by-key");
                    } finally {
                        Object.DestroyImmediate(host);
                    }
                }
            });

        /// <summary>
        /// The merge baseline carries the frame. A document that moves a row and says nothing
        /// about the frame must still arrive with the canonical space — asserted on the RESOURCE
        /// rather than on the rendered stage, because a 0x0 frame renders identically (CanonicalStage
        /// warns and falls back to the same numbers) and would therefore be invisible on screen.
        /// </summary>
        [UnityTest]
        public IEnumerator AnOverrideThatNeverMentionsTheFrame_StillCarriesTheCanonicalOne() =>
            UniTask.ToCoroutine(async () => {
                using (var overrides = new TempOverrideDirectory())
                using (new IsolatedResourceLocators()) {
                    overrides.Write("DAT", "DIALSTYL.json", RowsOnlyOverrideJson);
                    ResourceManagementInitializer.InitializeResourceManagement();

                    // WaitForCompletion rather than await: the Tests assembly does not reference
                    // UniTask.Addressables (the game assembly does), and the provider is
                    // synchronous anyway — the table is synthesized in code, not read from disk.
                    UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<DialogStyleTable> handle =
                        Addressables.LoadAssetAsync<DialogStyleTable>(DialogStyleTable.ResourceId);
                    DialogStyleTable table = handle.WaitForCompletion();
                    await UniTask.Yield();

                    // The document did reach the table...
                    Assert.AreEqual(LayoutLength.Percent(13.75f), table.Get(2).DefaultArea.Left,
                        "the override document never reached the merged table at all");
                    // ...and the frame it never mentioned survived the merge as the shipped one.
                    Assert.AreEqual(Canonical.Width, table.Frame.Width,
                        "0 here means OverrideResourceProvider's merge baseline was not stamped, so "
                        + "any override document silently collapses the dialog frame");
                    Assert.AreEqual(Canonical.Height, table.Frame.Height,
                        "0 here means the merge baseline was not stamped");
                    Assert.AreEqual(LayoutFit.Contain, table.Frame.Fit,
                        "Contain is the faithful default and must survive a document that is silent about it");
                }
            });

        /// <summary>
        /// The load-order question this task had to answer: dialogs render OVER other screens, so
        /// can a dialog's real frame collide with the real frame of the screen beneath?
        ///
        /// <para><b>No — they never meet.</b> <c>DialogOverlay</c> is its own <c>UIDocument</c> (its
        /// own sortingOrder tier), so <see cref="DialogManager"/> calls
        /// <c>CanonicalStage.GetOrCreate</c> against the overlay's own document root and builds a
        /// stage that is a DIFFERENT element from the screen's. <c>CanonicalStage</c>'s
        /// reconciliation is per-document-root, and the two roots never share one. This test pins
        /// that: a screen stage built from a deliberately different frame keeps its own size while
        /// the dialog stage takes the style table's — neither upgrades, downgrades or conflicts
        /// with the other.</para>
        ///
        /// <para>Within the overlay's own root the frame is a singleton — one shared
        /// <c>DIALSTYL.DAT</c> for every dialog ever shown — so successive dialogs reconcile
        /// against an identical frame, which <c>FramesEqual</c> treats as a silent no-op. That is
        /// the structural reason the frame belongs on the style table and not on the per-DDX
        /// <c>Dialog</c>: 32 DDX files could disagree, one table cannot.</para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheDialogStage_IsTheOverlaysOwn_AndDoesNotReconcileAgainstTheScreenBeneath() =>
            UniTask.ToCoroutine(async () => {
                using (var overrides = new TempOverrideDirectory())
                using (new IsolatedResourceLocators()) {
                    overrides.Write("DAT", "DIALSTYL.json", FrameOverrideJson);
                    ResourceManagementInitializer.InitializeResourceManagement();

                    // Stands in for the screen beneath: its own document root, its own real frame,
                    // deliberately unequal to the dialog's so a collision could not hide.
                    var screenRoot = new VisualElement { name = "ScreenBeneathDocumentRoot" };
                    VisualElement screenStage = CanonicalStage.GetOrCreate(
                        screenRoot, new DesignFrame { Width = 1234, Height = 987 });

                    var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                    try {
                        VisualElement dialogStage = await ShowADialogAndReturnItsStage(host);

                        Assert.AreNotSame(screenStage, dialogStage,
                            "the dialog must build its own stage on the overlay's document root");
                        Assert.AreEqual(new Length(800f, LengthUnit.Pixel), dialogStage.style.width.value,
                            "the dialog stage takes the style table's frame");
                        Assert.AreEqual(new Length(1234f, LengthUnit.Pixel), screenStage.style.width.value,
                            "the screen beneath keeps its own frame — a dialog must not resize it");

                        // A second dialog on the SAME overlay reconciles against the identical
                        // singleton frame: a no-op, not a conflict.
                        VisualElement again = await ShowADialogAndReturnItsStage(host);
                        Assert.AreSame(dialogStage, again, "the overlay reuses its stage");
                        Assert.AreEqual(new Length(800f, LengthUnit.Pixel), again.style.width.value,
                            "a second dialog must not disturb the frame the first one applied");
                    } finally {
                        Object.DestroyImmediate(host);
                    }
                }
            });

        // Shows a dialog through the real manager and hands back the stage it hosted the panel in.
        private static async UniTask<VisualElement> ShowADialogAndReturnItsStage(GameObject host) {
            DialogManager manager = host.GetComponent<DialogManager>();
            if (manager == null) {
                manager = host.AddComponent<DialogManager>();
                // The palette is not what is under test; the pens only have to resolve to something.
                manager.SetActivePalette(new Color[256]);
            }

            // DisplayEntry renders and returns, leaving the panel up for inspection (ShowEntry
            // would block on a dismiss). Same ShowEntryCore either way. SkipOpenWipe keeps the
            // reveal animation out of a frame test.
            await manager.DisplayEntry(new DialogEntry { Flags = DialogEntryFlags.SkipOpenWipe });

            VisualElement stage = CanonicalStage.Find(host.GetComponent<UIDocument>().rootVisualElement);
            Assert.IsNotNull(stage, "DialogManager built no canonical stage at all");
            return stage;
        }
    }
}
