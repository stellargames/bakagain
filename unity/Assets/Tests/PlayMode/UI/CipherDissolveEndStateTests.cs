namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.UI.Puzzle;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using System.Collections;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// <see cref="PuzzleScreen.DissolveAsync"/> must always FINISH the swap, however it exits.
    /// </summary>
    /// <remarks>
    /// The dissolve reveals the readable riddle a glyph at a time over the alien one and hides the
    /// alien glyph it replaced, so at any instant mid-ramp the screen is a mix of the two — which
    /// is the point. What must never survive the call is that mix: every incoming glyph visible,
    /// the outgoing layer gone.
    ///
    /// <para>The ramp has three ways out — it runs to completion, the panel disappears under it, or
    /// there is nothing to dissolve — and the two short ones are exactly where a half-drawn screen
    /// would come from. These layers are deliberately NOT attached to a panel, which takes the
    /// early exit; the end state has to be the same one a full ramp leaves.</para>
    /// </remarks>
    public class CipherDissolveEndStateTests {
        private static VisualElement Layer(int glyphs, bool visible) {
            var layer = new VisualElement();
            for (var i = 0; i < glyphs; i++) {
                layer.Add(new VisualElement { visible = visible });
            }

            return layer;
        }

        [UnityTest]
        public IEnumerator EveryIncomingGlyphIsVisibleAndTheOutgoingLayerIsGone() => UniTask.ToCoroutine(async () => {
            var stage = new VisualElement();
            VisualElement outgoing = Layer(12, visible: true);
            VisualElement incoming = Layer(12, visible: false);
            stage.Add(outgoing);
            stage.Add(incoming);

            await PuzzleScreen.DissolveAsync(outgoing, incoming);

            for (var i = 0; i < incoming.childCount; i++) {
                Assert.IsTrue(incoming[i].visible, $"incoming glyph {i} was left hidden");
            }

            Assert.IsNull(outgoing.parent, "the alien layer outlived the dissolve");
            Assert.AreEqual(1, stage.childCount);
        });

        [UnityTest]
        public IEnumerator NothingToDissolveStillTakesTheOutgoingLayerDown() => UniTask.ToCoroutine(async () => {
            // A puzzle whose riddle drew no glyphs at all — an empty description, or a font that
            // failed the second load. Leaving the alien layer up would strand the screen on it.
            var stage = new VisualElement();
            VisualElement outgoing = Layer(5, visible: true);
            stage.Add(outgoing);

            await PuzzleScreen.DissolveAsync(outgoing, new VisualElement());
            await PuzzleScreen.DissolveAsync(outgoing, null);

            Assert.IsNull(outgoing.parent);
        });
    }
}
