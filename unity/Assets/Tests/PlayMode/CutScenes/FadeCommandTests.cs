namespace BakAgain.Tests.PlayMode.CutScenes {
    using System.Collections;
    using BakAgain.CutScenes;
    using BakAgain.CutScenes.AnimationCommands;
    using BakAgain.Graphics;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation.FrameCommands;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UI;

    /// <summary>
    /// What the fade commands do to the palette — the guards that keep a malformed script from
    /// throwing, and the contract about what is left behind afterwards.
    /// </summary>
    /// <remarks>
    /// <b>The subtle one is the restore.</b> Both fades end with <c>SetPalette(original)</c> and NO
    /// render after it, so the SCREEN keeps the last faded frame while the palette STATE goes back
    /// for whatever command runs next. Adding a final render to "finish the fade properly" would
    /// snap the picture back to full colour — the change looks like a tidy-up and is a visible
    /// regression, which is why it is pinned here.
    /// </remarks>
    public class FadeCommandTests {
        private const int FullPalette = 256;

        private GameObject _host;
        private CutsceneState _state;

        private static Color[] Ramp(int n) {
            var c = new Color[n];
            for (var i = 0; i < n; i++) {
                c[i] = new Color(i / 255f, 0f, 0f, 1f);
            }
            return c;
        }

        private static int IndexOf(Color c) => Mathf.RoundToInt(c.r * 255f);

        // Every refusal below logs an error on purpose — that is the command telling the author its
        // script is malformed. The test runner treats an unhandled error log as a failure, so the
        // logs are ignored here rather than each message being matched verbatim; matching them would
        // pin the wording, which is not what these tests are about.
        [SetUp]
        public void SetUp() {
            _host = new GameObject("FadeCommandHost");
            RawImage raw = _host.AddComponent<RawImage>();
            raw.texture = new Texture2D(8, 8, TextureFormat.RGBA32, mipChain: false);
            _state = new CutsceneState(new UiImage(raw), Ramp(FullPalette));
        }

        [TearDown]
        public void TearDown() {
            LogAssert.ignoreFailingMessages = false;
            _state?.Dispose();
            if (_host != null) {
                Object.DestroyImmediate(_host);
            }
        }

        private static FadeOut Fade(int speed, int start, int length, int color) =>
            new FadeOut { Speed = speed, Start = start, Length = length, Color = color };

        private static FadeIn FadeUp(int speed, int start, int length, int color) =>
            new FadeIn { Speed = speed, Start = start, Length = length, Color = color };

        private static void AssertPaletteIsTheUntouchedRamp(Color[] palette) {
            Assert.AreEqual(FullPalette, palette.Length);
            for (var i = 0; i < FullPalette; i++) {
                Assert.AreEqual(i, IndexOf(palette[i]),
                    "entry " + i + " differs; the state palette must end as it began — either the "
                    + "fade was refused before writing anything, or it ran and restored");
            }
        }

        [UnityTest]
        public IEnumerator AStartIndexOffThePaletteIsREFUSED_NotClamped() =>
            UniTask.ToCoroutine(async () => {
                // A malformed script must not throw and must not half-apply. The guard returns before
                // any entry is written, which is what makes "refused" different from "clamped".
                LogAssert.ignoreFailingMessages = true;
                await Fade(speed: 1, start: FullPalette + 10, length: 4, color: 3).ToAction()(_state);

                AssertPaletteIsTheUntouchedRamp(_state.CurrentPalette);
            });

        [UnityTest]
        public IEnumerator ANegativeStartIndexIsRefusedTheSameWay() =>
            UniTask.ToCoroutine(async () => {
                LogAssert.ignoreFailingMessages = true;
                await Fade(speed: 1, start: -1, length: 4, color: 3).ToAction()(_state);

                AssertPaletteIsTheUntouchedRamp(_state.CurrentPalette);
            });

        [UnityTest]
        public IEnumerator ATargetColourOffThePaletteIsRefused() =>
            UniTask.ToCoroutine(async () => {
                // The target is read OUT of the current palette, so an out-of-range colour index is
                // an index error rather than a colour that simply cannot be shown.
                LogAssert.ignoreFailingMessages = true;
                await Fade(speed: 1, start: 0, length: 4, color: FullPalette + 1).ToAction()(_state);

                AssertPaletteIsTheUntouchedRamp(_state.CurrentPalette);
            });

        [UnityTest]
        public IEnumerator ALengthPastTheEndIsCLAMPED_AndTheFadeStillRuns() =>
            UniTask.ToCoroutine(async () => {
                // Unlike a bad start, an over-long length is recoverable: the original's own range
                // is trimmed and the fade happens. Refusing it would drop a fade the script asked
                // for; throwing would kill the cutscene.
                await Fade(speed: 1, start: FullPalette - 4, length: 64, color: 3).ToAction()(_state);

                // It ran to completion, and the restore put the state back — see the class remark.
                AssertPaletteIsTheUntouchedRamp(_state.CurrentPalette);
            });

        [UnityTest]
        public IEnumerator TheFadeLEAVESTheStatePaletteAsItFoundIt() =>
            UniTask.ToCoroutine(async () => {
                // *** The contract worth not "fixing". *** The loop writes the lerped palette every
                // iteration, but the final SetPalette(original) has no render after it — the screen
                // keeps the last faded frame while the STATE returns to normal for the next command.
                await Fade(speed: 1, start: 0, length: 16, color: 0).ToAction()(_state);

                AssertPaletteIsTheUntouchedRamp(_state.CurrentPalette);
            });

        [UnityTest]
        public IEnumerator AnEmptyPaletteIsRefusedRatherThanThrowing() =>
            UniTask.ToCoroutine(async () => {
                {
                    var bare = new GameObject("BareCutsceneHost");
                    RawImage raw = bare.AddComponent<RawImage>();
                    raw.texture = new Texture2D(8, 8, TextureFormat.RGBA32, mipChain: false);
                    var empty = new CutsceneState(new UiImage(raw), new Color[FullPalette]);
                    try {
                        // Nothing to assert beyond "it returned" — the point is that a cutscene with
                        // no palette loaded yet does not take the whole scene down.
                        await Fade(speed: 1, start: 0, length: 4, color: 0).ToAction()(empty);
                        Assert.Pass();
                    } finally {
                        empty.Dispose();
                        Object.DestroyImmediate(bare);
                    }
                }
            });
        // ------------------------------------------------------------------ FadeIn

        [UnityTest]
        public IEnumerator FadeInRUNSTHEOTHERWAY_FromTheFlatColourToTheRealPalette() =>
            UniTask.ToCoroutine(async () => {
                // *** THE ONE THING THAT DISTINGUISHES THE TWO FADES, and the one a port can invert
                // without anything looking wrong. *** FadeOut lerps each entry from its own colour
                // TOWARDS palette[Color]; FadeIn starts every entry AT palette[Color] and arrives at
                // its own colour. Same table, same range rules, same restore — opposite direction.
                //
                // Observed at the START rather than at the end, because both fades restore: the
                // finished state is identical either way, so only the first written frame tells them
                // apart.
                //
                // Read WITHOUT awaiting, which needs no hook in production: the loop writes its
                // first palette and renders before it ever awaits a frame, so by the time the call
                // returns to us the flat palette is already in the slot.
                Awaitable running = FadeUp(speed: 1, start: 0, length: 8, color: 5).ToAction()(_state);

                // *** try/finally, NOT a bare assert then await. *** A failing assertion throws, and
                // the await after it never runs — which leaks a LIVE FADE into the next test, whose
                // continuation then writes palettes into a state that test believes it owns. Found
                // by mutation: inverting the fade direction failed this test AND FadeOut's restore
                // test, and the second failure was entirely this leak rather than any coupling
                // between the two handlers.
                try {
                    Color[] first = _state.CurrentPalette;
                    for (var i = 0; i < 8; i++) {
                        Assert.AreEqual(5, IndexOf(first[i]),
                            "entry " + i + " should START at the source colour and grow towards its own");
                    }
                } finally {
                    await running;   // always finish, so the restore runs and the next test is clean
                }
            });

        [UnityTest]
        public IEnumerator FadeInLEAVESTheStatePaletteAsItFoundIt() =>
            UniTask.ToCoroutine(async () => {
                // Same contract as FadeOut's, and for the same reason: the final SetPalette has no
                // render after it, so the screen keeps the last faded frame while the STATE goes
                // back for whatever runs next.
                await FadeUp(speed: 1, start: 0, length: 8, color: 5).ToAction()(_state);

                AssertPaletteIsTheUntouchedRamp(_state.CurrentPalette);
            });

        [UnityTest]
        public IEnumerator FadeInRefusesTheSameThreeMalformedScripts() =>
            UniTask.ToCoroutine(async () => {
                // The guards are duplicated between the two handlers rather than shared, so each
                // one has to be asserted separately -- a fix to one does not fix the other.
                LogAssert.ignoreFailingMessages = true;

                await FadeUp(speed: 1, start: FullPalette + 10, length: 4, color: 3).ToAction()(_state);
                AssertPaletteIsTheUntouchedRamp(_state.CurrentPalette);

                await FadeUp(speed: 1, start: -1, length: 4, color: 3).ToAction()(_state);
                AssertPaletteIsTheUntouchedRamp(_state.CurrentPalette);

                await FadeUp(speed: 1, start: 0, length: 4, color: FullPalette + 1).ToAction()(_state);
                AssertPaletteIsTheUntouchedRamp(_state.CurrentPalette);
            });

    }
}
