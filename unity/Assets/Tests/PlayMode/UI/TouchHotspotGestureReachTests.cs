namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using System.Reflection;
    using BakAgain.ResourceManagement.Loaders;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Menu;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// TASK-968 (owner, phone): on a location the word only followed a finger that came down ON a
    /// hotspot. The gesture listens on the document root, and the root ignored picking, so a finger
    /// that came down between hotspots picked nothing and UI Toolkit never sent it to the screen at
    /// all. A screen that takes the touch gesture now picks every point of its window.
    /// </summary>
    public class TouchHotspotGestureReachTests {
        private GameObject _doc, _loaderGo;

        [TearDown]
        public void TearDown() {
            if (_doc != null) Object.DestroyImmediate(_doc);
            if (_loaderGo != null) Object.DestroyImmediate(_loaderGo);
        }

        private IEnumerator Build(bool gesture) {
            _doc = new GameObject("TouchReachDoc");
            UIDocument doc = _doc.AddComponent<UIDocument>();
            doc.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            VisualElement root = doc.rootVisualElement;
            yield return null;

            _loaderGo = new GameObject("loader");
            _loaderGo.SetActive(false);   // OnEnable would load by address
            var loader = _loaderGo.AddComponent<UserInterfaceLoader>();
            loader.Construct(null, null);
            typeof(UserInterfaceLoader).GetField("_userInterface", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(loader, new UserInterface("REQ_TEST") { MenuEntries = System.Array.Empty<UiElement>() });
            if (gesture) loader.TouchAsksFirst = _ => false;   // what LocationScreen.Bind sets
            try {
                var build = (UniTask)typeof(UserInterfaceLoader)
                    .GetMethod("BuildInto", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(loader, new object[] { root });
                build.GetAwaiter().GetResult();
            } catch (System.NullReferenceException) {
                // expected: no layout behind this bare loader; the root is settled before that
            }
            yield return null;
            yield return null;
        }

        private VisualElement PickBetweenHotspots() {
            VisualElement root = _doc.GetComponent<UIDocument>().rootVisualElement;
            Vector2 middle = root.panel.visualTree.layout.center;
            VisualElement picked = root.panel.Pick(middle);
            return picked != null && (picked == root || root.Contains(picked)) ? picked : null;
        }

        [UnityTest]
        public IEnumerator AFingerBetweenHotspots_ReachesTheScreen_WhenItTakesTheTouchGesture() {
            yield return Build(gesture: true);
            Assert.IsNotNull(PickBetweenHotspots(), "a finger down off every hotspot never reached the gesture");
        }

        [UnityTest]
        public IEnumerator AScreenWithoutTheGesture_StillLetsClicksThrough() {
            yield return Build(gesture: false);
            Assert.IsNull(PickBetweenHotspots(), "a HUD base must not start catching clicks meant for what is beneath");
        }
    }
}
