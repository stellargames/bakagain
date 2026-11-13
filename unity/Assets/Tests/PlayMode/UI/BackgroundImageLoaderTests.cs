namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using BakAgain.ResourceManagement.Loaders;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A background that fails to load leaves the stage without one and throws nothing of its own: the
    /// load's failure is already logged by Addressables (seen in the Editor log, 2026-11-13: a boot
    /// without the game data logged the missing key and then a NullReferenceException from the loader).
    /// </summary>
    public class BackgroundImageLoaderTests {
        [UnityTest]
        public IEnumerator AFailedLoadThrowsNothingOfItsOwn() {
            var host = new GameObject("BackgroundDoc");
            host.SetActive(false);
            try {
                host.AddComponent<UIDocument>().panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
                var loader = host.AddComponent<BackgroundImageLoader>();
                typeof(BackgroundImageLoader).GetField("imageAddress", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(loader, "NO_SUCH_BACKGROUND.SCX");
                LogAssert.Expect(LogType.Error, new Regex("NO_SUCH_BACKGROUND.SCX"));
                host.SetActive(true);
                for (int i = 0; i < 30; i++) {
                    yield return null;
                }   // an exception logged by the loader fails the test on its own
            } finally {
                Object.Destroy(host);
            }
        }
    }
}
