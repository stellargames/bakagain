namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Core;
    using BakAgain.Graphics;
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
    /// THE FENCE for the open wipe's FRAME BOUNDARY — the one thing every other wipe test is blind
    /// to.
    ///
    /// <para><b>What was wrong.</b> Reading the panel's resolved rect (rather than its declared
    /// <c>LayoutHint</c>) means <c>ShowEntryCore</c> must yield at least one frame before the wipe
    /// starts: <c>WaitForResolvedRect</c>'s fast path needs <c>IsResolved(panel.layout)</c>, which is
    /// false by construction for a panel built moments earlier. So frame N laid the panel out, fired
    /// its <c>GeometryChangedEvent</c> — <b>and repainted it at 100%</b> — with the mask only
    /// arriving in frame N+1. Every shipped dialog that plays the wipe (5,896 of 5,932 text-bearing
    /// entries) popped in at full size for one frame, collapsed to zero, then revealed. The old
    /// px-only code had no such window: its whole prologue, mask included, ran synchronously in the
    /// frame the panel was added.</para>
    ///
    /// <para><b>Why the existing evidence missed it.</b> Every other fence checks geometry VALUES —
    /// <c>DialogOpenWipeRestoreTests</c> the restored hint, <c>DialogOpenWipeCancellationTests</c>
    /// the mask lifecycle, <c>DialogManagerWaitForResolvedRectTests</c> the timeout bound — and all
    /// of them are right about every frame from the first mask frame onward. The live phase run's
    /// 28-frame mask sample began at the first mask frame for the same reason. None of them could
    /// see the frame BEFORE it, which is the only frame the bug lives in.</para>
    ///
    /// <para>So this test asserts a frame-ordering property instead of a number: <b>the panel is
    /// never painted at full size in a frame with no wipe mask clipping it.</b> It samples at
    /// <c>LastPostLateUpdate</c> — after UI Toolkit's own layout/repaint pass for the frame, so the
    /// sample is the state that frame actually rendered, not the state mid-Update.</para>
    /// </summary>
    public class DialogOpenWipeNoUnmaskedFrameTests {
        private const int MaxSampledFrames = 600;

        [SetUp]
        public void RequireShippedGameData() {
            ShippedGameData.RequireOrIgnore();
        }

        /// <summary>
        /// THE FENCE. Drives a real <see cref="DialogManager"/> show of a wipe-playing entry through
        /// a real, laid-out <c>UIDocument</c>, and inspects every frame from before the panel exists
        /// until the wipe has finished.
        ///
        /// <para>The entry carries no <c>Text</c> deliberately: text substitution needs a live
        /// <c>GameSession</c> this fixture does not construct, and the panel's size comes from the
        /// style row's <c>DefaultArea</c> either way. Flags are left at 0 so
        /// <c>DialogOpenWipe.ShouldPlay</c> is true — the shipped majority case.</para>
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheDialogPanel_IsNeverPaintedAtFullSize_BeforeTheWipeMaskExists() =>
            UniTask.ToCoroutine(async () => {
                // An EMPTY override directory: no document is written, because the shipped path is
                // exactly what is under test. The scope is here to guarantee that — so a stray
                // DIALSTYL.json in the developer's real overrides folder cannot change the geometry
                // this test is timing.
                using (new TempOverrideDirectory())
                using (new IsolatedResourceLocators()) {
                    ResourceManagementInitializer.InitializeResourceManagement();

                    var settings = ScriptableObject.CreateInstance<PanelSettings>();
                    settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                    settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                    settings.match = 1f;
                    settings.referenceResolution = new Vector2Int(Canonical.Width, Canonical.Height);

                    var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                    try {
                        var document = host.GetComponent<UIDocument>();
                        document.panelSettings = settings;
                        DialogManager manager = host.AddComponent<DialogManager>();
                        manager.SetActivePalette(new Color[256]);

                        // Fire-and-forget: DisplayEntry leaves the panel up, and this test has to
                        // observe the frames WHILE it runs, not after it returns.
                        manager.DisplayEntry(new DialogEntry()).Forget();

                        int firstMaskFrame = -1;
                        int unmaskedVisibleFrame = -1;

                        for (int frame = 0; frame < MaxSampledFrames; frame++) {
                            // After UI Toolkit has laid out and repainted this frame's panels, so
                            // what is sampled is what was drawn.
                            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);

                            VisualElement stage = CanonicalStage.Find(document.rootVisualElement);
                            VisualElement panel = stage?.Q("BakDialogPanel");
                            VisualElement mask = stage?.Q("DialogOpenWipeMask");

                            if (mask != null && firstMaskFrame < 0) {
                                firstMaskFrame = frame;
                            }
                            if (firstMaskFrame >= 0 && mask == null) {
                                break; // the wipe finished and the panel is legitimately unmasked now
                            }

                            // Deliberately NOT gated on a non-zero panel.layout: the invariant is
                            // "a panel on the stage is not showing until a mask is clipping it",
                            // full stop. Requiring an already-laid-out rect would make detection
                            // depend on whether UI Toolkit's layout pass for this frame happened
                            // before or after the panel was added — the exact frame-ordering
                            // uncertainty this test exists to remove, reintroduced into the test
                            // itself. The fix satisfies the stronger form (hidden from the moment
                            // the panel is added until SetStep(0f) reveals it inside the mask).
                            bool visible = panel != null
                                && panel.resolvedStyle.visibility == Visibility.Visible;
                            if (visible && mask == null) {
                                unmaskedVisibleFrame = frame;
                                break;
                            }
                        }

                        // Order matters: the violation breaks the sampling loop, so it can leave
                        // firstMaskFrame at -1. Asserting the guard first would report "the wipe
                        // never played" for what is actually the bug this test exists to catch.
                        Assert.AreEqual(-1, unmaskedVisibleFrame,
                            $"frame {unmaskedVisibleFrame}: the dialog panel was on the stage and "
                            + $"visible with no wipe mask clipping it (first mask frame was "
                            + $"{firstMaskFrame}). That is the full-size pop-in before the reveal, on "
                            + "every shipped dialog — a rendered-game difference this phase does not "
                            + "allow.");
                        Assert.GreaterOrEqual(firstMaskFrame, 0,
                            "the wipe never played at all, so this test proved nothing — check that "
                            + "the entry's flags still opt IN to the open wipe");

                        manager.ClearDialog();
                    } finally {
                        Object.DestroyImmediate(host);
                        Object.DestroyImmediate(settings);
                    }
                }
            });
    }
}
