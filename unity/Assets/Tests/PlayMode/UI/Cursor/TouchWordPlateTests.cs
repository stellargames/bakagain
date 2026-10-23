namespace BakAgain.Tests.PlayMode.UI.Cursor {
    using System.Collections;
    using System.Reflection;
    using BakAgain.CutScenes;
    using BakAgain.UI.Cursor;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// TASK-975: a finger's hotspot word is POINTERG's dark lettering shown away from the finger, over
    /// whatever the scene has there; over the sky it was hard to read. It sits on a light plate with a
    /// dark rim now, and the mouse cursor stays plain.
    /// </summary>
    public class TouchWordPlateTests {
        private sealed class OneSprite : IResourceCache {
            public Sprite Sprite;
            public UniTask<T> GetOrLoadAsync<T>(string key) where T : class => UniTask.FromResult(Sprite as T);
            public void Clear() { }
        }

        private GameObject _host;
        private Texture2D _ink;

        [TearDown]
        public void TearDown() {
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_ink);
        }

        [UnityTest]
        public IEnumerator TheTouchWord_SitsOnALightPlate_AndTheMouseCursorDoesNot() {
            _host = new GameObject("TouchWordPlate");
            UIDocument doc = _host.AddComponent<UIDocument>();
            doc.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            var cm = _host.AddComponent<CursorManager>();
            _ink = new Texture2D(120, 40);
            var cache = new OneSprite { Sprite = Sprite.Create(_ink, new Rect(0, 0, 120, 40), Vector2.zero) };
            typeof(CursorManager).GetField("_resources", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(cm, cache);
            yield return null;
            yield return null;

            cm.ShowTouchWord("POINTERG", 5, new Vector2(400, 500));
            yield return null;
            yield return null;

            VisualElement root = doc.rootVisualElement;
            VisualElement word = root.Q("touch-word");
            Assert.IsNotNull(word, "the word is shown");
            Color plate = word.resolvedStyle.backgroundColor;
            Assert.Greater(plate.a, 0.8f, "the word has an opaque plate behind it");
            Assert.Greater(0.299f * plate.r + 0.587f * plate.g + 0.114f * plate.b, 0.7f, "a light one, under dark letters");
            Assert.Greater(word.resolvedStyle.borderTopWidth, 0f, "with a rim, so it reads on bright ground too");
            Rect ink = word.Q("touch-word-ink")?.worldBound ?? Rect.zero;
            Assert.AreEqual(new Vector2(120, 40), ink.size, "the letters keep their size");
            Assert.Greater(ink.xMin, word.worldBound.xMin); Assert.Less(ink.xMax, word.worldBound.xMax);
            Assert.Greater(ink.yMin, word.worldBound.yMin); Assert.Less(ink.yMax, word.worldBound.yMax);
            Assert.IsFalse(word.worldBound.Contains(new Vector2(400, 500)), "and the plate stays off the finger");

            VisualElement cursor = root.Q("software-cursor");
            Assert.AreEqual(0f, cursor.resolvedStyle.backgroundColor.a, "the mouse cursor is unchanged: no plate");
            Assert.AreEqual(0f, cursor.resolvedStyle.borderTopWidth);
        }
    }
}
