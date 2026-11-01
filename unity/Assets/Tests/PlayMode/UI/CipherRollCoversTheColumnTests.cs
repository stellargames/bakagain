namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using BakAgain.UI.Puzzle;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Scene;
    using NUnit.Framework;
    using System.Collections;
    using System.Reflection;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A wheel's roll covers its column from the click frame on (TASK-1023).
    /// </summary>
    /// <remarks>
    /// The click repaints the ring first, so the NEW letter is already drawn at rest under the roll.
    /// The original redraws the bevelled box every frame before the two letters
    /// (CIPHER.C:332), so that letter is never seen until the roll lands on it. The port's roll
    /// window had no box of its own, and the finished letter showed through it from the click.
    /// </remarks>
    public class CipherRollCoversTheColumnTests {
        private const int Puzzle = 54;   // a shipped wordlock (BARROW, DIAL_Z17)

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator OnTheClickFrameTheRollCoversTheNewLetter() => UniTask.ToCoroutine(async () => {
            var scope = Object.FindAnyObjectByType<VContainer.Unity.LifetimeScope>();
            if (scope == null) {
                Assert.Ignore("no LifetimeScope: DI has not built the screens");
            }
            var screen = (PuzzleScreen)scope.Container.Resolve(typeof(PuzzleScreen));
            var service = (PuzzleService)scope.Container.Resolve(typeof(PuzzleService));
            var resources = (BakAgain.CutScenes.IResourceCache)scope.Container.Resolve(
                typeof(BakAgain.CutScenes.IResourceCache));
            var provider = (BakAgain.ResourceManagement.IResourceProviderService)scope.Container.Resolve(
                typeof(BakAgain.ResourceManagement.IResourceProviderService));
            var midi = (BakAgain.Audio.MidiPlaybackManager)scope.Container.Resolve(
                typeof(BakAgain.Audio.MidiPlaybackManager));
            var session = (BakAgain.Core.GameSession)scope.Container.Resolve(typeof(BakAgain.Core.GameSession));
            var dialogs = (BakAgain.UI.IDialogManager)scope.Container.Resolve(
                typeof(BakAgain.UI.IDialogManager));

            // No dialogs and no music: the narration would wait for a click, and the roll is the
            // screen's own drawing either way.
            screen.Construct(resources, null, null, new BakAgain.Core.GameSession());
            try {
                CipherPuzzle puzzle = await service.LoadAsync(Puzzle);
                Assert.NotNull(puzzle, "the shipped wordlock loads");
                screen.Open(puzzle);

                FieldInfo presenting = typeof(PuzzleScreen).GetField("_presenting",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                VisualElement stage = null;
                for (var frame = 0; frame < 600; frame++) {
                    await UniTask.Yield();
                    stage = CanonicalStage.Find(screen.GetComponent<UIDocument>().rootVisualElement);
                    if (stage?.Q(PuzzleScreen.LettersName) != null && !(bool)presenting.GetValue(screen)) {
                        break;
                    }
                }
                Assert.NotNull(stage?.Q(PuzzleScreen.LettersName), "the wheels were drawn");
                Assert.IsFalse((bool)presenting.GetValue(screen), "the presentation finished");

                screen.PrimaryAction(CipherPuzzleLayout.FirstColumnActionId);

                // Same frame as the click: no yield between it and these reads.
                VisualElement letters = stage.Q(PuzzleScreen.LettersName);
                VisualElement window = stage.Q(PuzzleScreen.LettersName + "Roll0");
                Assert.NotNull(window, "the roll started on the click");
                Assert.Greater(stage.IndexOf(window), stage.IndexOf(letters), "the roll is in front of the ring");

                VisualElement cover = window[0];
                Assert.AreEqual(1f, cover.style.backgroundColor.value.a, 1e-3f, "the roll's cell is opaque");
                float wl = window.style.left.value.value, wt = window.style.top.value.value;
                float ww = window.style.width.value.value, wh = window.style.height.value.value;
                Assert.LessOrEqual(cover.style.left.value.value, 0f);
                Assert.LessOrEqual(cover.style.top.value.value, 0f);
                Assert.GreaterOrEqual(cover.style.left.value.value + cover.style.width.value.value, ww);
                Assert.GreaterOrEqual(cover.style.top.value.value + cover.style.height.value.value, wh);

                // The new letter IS under the window — that is what the cover hides.
                var under = 0;
                foreach (VisualElement glyph in letters.Children()) {
                    if (glyph.style.backgroundImage.value.sprite == null) {
                        continue;   // a bevel
                    }
                    float gx = glyph.style.left.value.value, gy = glyph.style.top.value.value;
                    if (gx >= wl && gy >= wt && gx < wl + ww && gy < wt + wh) {
                        under++;
                    }
                }
                Assert.Greater(under, 0, "the repainted column's letter lies under the roll window");

                // The outgoing letter rests where the ring drew it, in the three emboss passes, and
                // the incoming one waits above it.
                VisualElement arriving = window[1], falling = window[2];
                Assert.AreEqual(0f, falling.style.top.value.value, "the old letter starts at rest");
                Assert.Less(arriving.style.top.value.value, 0f, "the new letter starts above");
                Assert.AreEqual(3, falling.childCount, "the roll draws the same three passes as the ring");

                await UniTask.Yield();
                Assert.Greater(falling.style.top.value.value, 0f, "the pair moves down a step a frame");
            } finally {
                screen.Close();
                screen.Construct(resources, provider, midi, session, dialogs);
            }
        });
    }
}
