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
    /// The parchment behind a full-screen dialog actually reaches the stage.
    /// </summary>
    /// <remarks>
    /// <b>Why this fixture exists.</b> The backdrop renderer was built, wired and green, and the
    /// dialog a player meets first still drew its text straight over the world — because the
    /// predicate that decides whether to draw it tested one of the original's two triggers.
    /// <c>ExecuteDialog</c> tests <c>flags &amp; 2</c> at 0x49903; the branch taken when that flag
    /// is CLEAR (0x49b21) tests <c>dialogType == 6</c> and draws the same parchment. Every unit
    /// test around the pieces passed throughout, because none of them asked the question a player
    /// asks: is there parchment behind this dialog?
    ///
    /// <para>So these assert on the element the manager actually built, for the SHIPPED entry, not
    /// on a predicate in isolation.</para>
    ///
    /// <para><b>What is deliberately not asserted here:</b> that the flag form punches the world
    /// viewport back through the parchment and the type-6 form does not. This fixture has no world
    /// to punch — <c>WorldViewportHole()</c> finds nothing and both forms take the full-frame
    /// branch — so an assertion would pass no matter which way the code read. That distinction is
    /// pinned in .NET instead, on <c>DialogBackdrop.RedrawsWorldViewport</c>.</para>
    /// </remarks>
    public class DialogParchmentBackdropReachesTheStageTests {
        [SetUp]
        public void RequireShippedGameData() {
            // The backdrop is a real resource load (DIALOG.SCX out of KRONDOR.001). Without the
            // shipped data the manager logs and draws nothing, which would make these vacuous.
            ShippedGameData.RequireOrIgnore();
        }

        /// <summary>
        /// THE FENCE. The shipped narrative the player meets outside Squire Phillip's — DIAL_Z30
        /// @120988, "Someone was calling." — is <see cref="DialogType.PlainFullScreen"/> carrying
        /// only <c>Legacy10</c>. It gets the parchment from its TYPE, with no flag involved.
        /// </summary>
        [UnityTest]
        public IEnumerator TheShippedFullScreenNarrative_GetsTheParchment() =>
            UniTask.ToCoroutine(async () => {
                VisualElement backdrop = await RenderAndFindBackdrop(
                    DialogType.PlainFullScreen, DialogEntryFlags.Legacy10);

                Assert.IsNotNull(backdrop,
                    "a PlainFullScreen entry must be drawn on the parchment even with no flag set: "
                    + "ExecuteDialog's flagless branch at 0x49b21 tests dialogType == 6 and loads "
                    + "Dialog.scr. Without this the narrative renders over whatever is on screen, "
                    + "which is the bug this fixture exists to keep fixed");
            });

        /// <summary>The other trigger: an ordinary type carrying <c>IsolatePalette</c>.</summary>
        [UnityTest]
        public IEnumerator AnOrdinaryTypeCarryingTheFlag_AlsoGetsTheParchment() =>
            UniTask.ToCoroutine(async () => {
                VisualElement backdrop = await RenderAndFindBackdrop(
                    DialogType.Normal, DialogEntryFlags.IsolatePalette);

                Assert.IsNotNull(backdrop,
                    "the flagged trigger (0x49903) must keep working — it is the form that frames "
                    + "a speaker against the landscape");
            });

        /// <summary>
        /// And the negative, so "draw parchment behind everything" cannot pass this fixture:
        /// neither trigger fires, so the dialog really does render over what is on screen.
        /// </summary>
        [UnityTest]
        public IEnumerator AnOrdinaryTypeWithNoFlag_GetsNoParchment() =>
            UniTask.ToCoroutine(async () => {
                VisualElement backdrop = await RenderAndFindBackdrop(
                    DialogType.Normal, DialogEntryFlags.Legacy10);

                Assert.IsNull(backdrop,
                    "an ordinary boxed dialog paints its own chrome and leaves the screen behind "
                    + "it alone; a parchment here would sit under every conversation in the game");
            });

        /// <summary>
        /// THE SECOND FENCE (TASK-235). A full-screen dialog irises open across the whole screen,
        /// so the parchment is what the wipe UNCOVERS — it must be inside the growing clip mask
        /// while the wipe runs, not sitting on screen already.
        /// </summary>
        [UnityTest]
        public IEnumerator TheParchmentIsInsideTheMaskWhileTheWipeRuns() =>
            UniTask.ToCoroutine(async () => {
                await WithDialogManager(async manager => {
                    // Not awaited: the wipe happens INSIDE DisplayEntry, so awaiting it here would
                    // only ever observe the finished state — which is exactly the state that looks
                    // identical whether or not the parchment was ever in the mask.
                    UniTask showing = manager.DisplayEntry(FullScreenEntry(wipe: true));

                    VisualElement maskedBackdrop = await PollForMaskedBackdrop(manager);
                    await showing;

                    Assert.IsNotNull(maskedBackdrop,
                        "the parchment must be reparented into DialogOpenWipeMask for the duration "
                        + "of the wipe. ExecuteDialog nulls the entry pointer at 0x49c58 for "
                        + "dialogType 6 so the wipe covers the full screen and the parchment is "
                        + "what opens; leaving it on the stage irises the text alone");
                });
            });

        /// <summary>
        /// And the discriminator: a dialog that is NOT full-screen wipes only its own panel rect,
        /// so its backdrop stays put. Without this, "reparent the backdrop always" would pass.
        /// </summary>
        [UnityTest]
        public IEnumerator AnAreaWipeLeavesItsBackdropOnTheStage() =>
            UniTask.ToCoroutine(async () => {
                await WithDialogManager(async manager => {
                    // Normal type + IsolatePalette: it HAS a parchment, but its wipe is area-only,
                    // so a backdrop found inside the mask would mean the full-screen rule leaked.
                    UniTask showing = manager.DisplayEntry(new DialogEntry {
                        Text = "\tI didn't expect to see you again quite so soon.",
                        DialogType = DialogType.Normal,
                        Flags = DialogEntryFlags.IsolatePalette,
                    });

                    VisualElement maskedBackdrop = await PollForMaskedBackdrop(manager);
                    await showing;

                    Assert.IsNull(maskedBackdrop,
                        "only dialogType 6 nulls the entry pointer; every other dialog wipes "
                        + "dialog_getDialogArea and leaves the rest of the screen alone");
                });
            });

        /// <summary>The parchment is handed back to the stage, stretched, once the wipe is done.</summary>
        [UnityTest]
        public IEnumerator TheParchmentIsReturnedToTheStageStretchedAfterwards() =>
            UniTask.ToCoroutine(async () => {
                await WithDialogManager(async manager => {
                    await manager.DisplayEntry(FullScreenEntry(wipe: true));

                    VisualElement backdrop = Root(manager).Q("BakDialogBackdrop");
                    Assert.IsNotNull(backdrop, "the parchment must survive the wipe");
                    Assert.AreEqual("BakStage", backdrop.parent?.name ?? "<none>",
                        "left parented to the mask, the parchment vanishes with it");
                    // Stretched, not frozen at the px the mask pinned it to — a full-frame layer
                    // holding px would stop following the frame on the next resize.
                    //
                    // *** Asserted on the INLINE style, not resolvedStyle. *** resolvedStyle lags a
                    // layout pass, so straight after the reparent it still reports whatever the
                    // last animated step wrote — a moving target that read -4 on one run and -17 on
                    // the next. The inline value is what the restore sets and what decides the
                    // element's behaviour from here; the resolved value merely catches up to it.
                    Assert.AreEqual(0f, backdrop.style.left.value.value, 0.001f, "left inset");
                    Assert.AreEqual(0f, backdrop.style.top.value.value, 0.001f, "top inset");
                    Assert.AreEqual(0f, backdrop.style.right.value.value, 0.001f, "right inset");
                    Assert.AreEqual(0f, backdrop.style.bottom.value.value, 0.001f, "bottom inset");
                    Assert.AreEqual(StyleKeyword.Null, backdrop.style.width.keyword,
                        "a width left pinned in px is the freeze this restore exists to prevent");
                    Assert.AreEqual(StyleKeyword.Null, backdrop.style.height.keyword, "height");
                });
            });

        // Poll rather than assume a frame count: the wipe starts only after the panel's first
        // resolved layout, which is at least one frame out and not a fixed number of them.
        private static async UniTask<VisualElement> PollForMaskedBackdrop(DialogManager manager) {
            for (int frame = 0; frame < 60; frame++) {
                VisualElement mask = Root(manager).Q("DialogOpenWipeMask");
                if (mask != null) {
                    return mask.Q("BakDialogBackdrop");
                }
                await UniTask.Yield();
            }
            return null;
        }

        private static VisualElement Root(DialogManager manager) =>
            manager.GetComponent<UIDocument>().rootVisualElement;

        private static DialogEntry FullScreenEntry(bool wipe) =>
            new DialogEntry {
                Text = "\tSomeone was calling.",
                DialogType = DialogType.PlainFullScreen,
                Flags = wipe
                    ? DialogEntryFlags.Legacy10
                    : DialogEntryFlags.Legacy10 | DialogEntryFlags.SkipOpenWipe,
            };

        // *** Unlike the assertions above, these need a REAL PANEL. *** The wipe only starts once
        // the panel reports a resolved rect, and an unattached UIDocument never lays out — so
        // WaitForResolvedRect times out, the wipe is skipped, and a test looking for the mask finds
        // nothing and blames the production code. A throwaway PanelSettings is what the inventory
        // layout fixtures use for the same reason.
        private static async UniTask WithDialogManager(System.Func<DialogManager, UniTask> body) {
            using (new IsolatedResourceLocators()) {
                ResourceManagementInitializer.InitializeResourceManagement();
                var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
                try {
                    host.GetComponent<UIDocument>().panelSettings = settings;
                    DialogManager manager = host.AddComponent<DialogManager>();
                    manager.SetActivePalette(new Color[256]);
                    await body(manager);
                } finally {
                    Object.DestroyImmediate(host);
                    Object.DestroyImmediate(settings);
                }
            }
        }

        // DisplayEntry renders and returns, leaving the tree up for inspection (ShowEntry would
        // block on a dismiss). SkipOpenWipe keeps the reveal animation out of it — the wipe is a
        // separate, already-tested concern.
        private static async UniTask<VisualElement> RenderAndFindBackdrop(
            DialogType type, DialogEntryFlags flags) {
            using (new IsolatedResourceLocators()) {
                ResourceManagementInitializer.InitializeResourceManagement();

                var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                try {
                    DialogManager manager = host.AddComponent<DialogManager>();
                    // The palette is not what is under test; the pens only have to resolve.
                    manager.SetActivePalette(new Color[256]);

                    await manager.DisplayEntry(new DialogEntry {
                        Text = "\tSomeone was calling.",
                        DialogType = type,
                        Flags = flags | DialogEntryFlags.SkipOpenWipe,
                    });

                    return host.GetComponent<UIDocument>().rootVisualElement.Q("BakDialogBackdrop");
                } finally {
                    Object.DestroyImmediate(host);
                }
            }
        }
    }
}
