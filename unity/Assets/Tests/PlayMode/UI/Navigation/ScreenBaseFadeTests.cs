namespace BakAgain.Tests.PlayMode.UI.Navigation {
    using System.Collections;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    public class ScreenBaseFadeTests {
        private sealed class TestScreen : ScreenBase { }   // uses ScreenBase defaults

        [UnityTest]
        public IEnumerator InstantFade_ShowEnablesAtFullOpacity_HideDisables() => UniTask.ToCoroutine(async () => {
            var go = new GameObject("s", typeof(UIDocument));
            var screen = go.AddComponent<TestScreen>();
            screen.SetFadeSecondsForTest(0f);                  // opt-out = instant
            go.SetActive(false);
            var doc = go.GetComponent<UIDocument>();
            await screen.ShowAsync();
            Assert.IsTrue(go.activeSelf, "shown = active");
            Assert.AreEqual(1f, doc.rootVisualElement.resolvedStyle.opacity, 0.001f, "opaque when shown");
            await screen.HideAsync();
            Assert.IsFalse(go.activeSelf, "hidden = inactive");
            Object.DestroyImmediate(go);
        });
    }
}
