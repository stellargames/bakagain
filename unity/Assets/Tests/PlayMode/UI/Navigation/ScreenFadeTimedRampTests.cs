namespace BakAgain.Tests.PlayMode.UI.Navigation {
    using System.Collections;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// TASK-978: a transition with its own original ramp (the cipher's fade to black, the inventory's
    /// fade up) takes that ramp's time on the real cover, not the world fade's 33 stepped writes.
    /// </summary>
    public class ScreenFadeTimedRampTests {
        private GameObject _host;
        private PanelSettings _settings;

        [TearDown]
        public void TearDown() {
            GameObject cover = GameObject.Find("BakScreenFade");
            if (cover != null) Object.DestroyImmediate(cover);
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_settings);
        }

        private static VisualElement CoverElement() {
            UIDocument doc = GameObject.Find("BakScreenFade")?.GetComponent<UIDocument>();
            return doc?.rootVisualElement?.childCount > 0 ? doc.rootVisualElement[0] : null;
        }

        [UnityTest]
        public IEnumerator ATimedRampTakesItsOwnTime_AndTheWorldFadeKeepsItsSteps() => UniTask.ToCoroutine(async () => {
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _host = new GameObject("PanelToBorrow");
            _host.AddComponent<UIDocument>().panelSettings = _settings;
            await UniTask.Yield();
            IScreenFade fade = new ScreenFade();

            float t0 = Time.realtimeSinceStartup;
            await fade.FadeOutAsync(ScreenFade.CipherFadeOutSeconds);
            float outTook = Time.realtimeSinceStartup - t0;
            Assert.AreEqual(1f, CoverElement().style.opacity.value, 0.001f, "black at the end");
            Assert.Less(outTook, 0.15f, "the cipher's ramp (0.05 s), not the world fade's");

            t0 = Time.realtimeSinceStartup;
            await fade.FadeInAsync(0f);
            Assert.Less(Time.realtimeSinceStartup - t0, 0.05f, "zero seconds is a cut");
            Assert.AreEqual(DisplayStyle.None, CoverElement().style.display.value, "and clears out of the way");

            await fade.FadeOutAsync(0f);
            t0 = Time.realtimeSinceStartup;
            await fade.FadeInAsync(ScreenFade.InventoryFadeInSeconds);
            Assert.Less(Time.realtimeSinceStartup - t0, 0.15f, "the inventory's ramp up");
            Assert.AreEqual(DisplayStyle.None, CoverElement().style.display.value);

            // Control: the world fade is untouched -- its 33 stepped writes, at least a frame each.
            int f0 = Time.frameCount;
            await fade.FadeOutAsync();
            Assert.GreaterOrEqual(Time.frameCount - f0, 33, "the world fade still steps through its 33 writes");
        });
    }
}
