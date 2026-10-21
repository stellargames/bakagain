namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using System.Reflection;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.Tests.TestSupport;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A screen re-enabled after a script reload in Play rebuilds instead of throwing (TASK-920).
    /// </summary>
    /// <remarks>
    /// Recompiling while the Editor plays reloads the domain: Unity re-creates every MonoBehaviour,
    /// restores only its serialized fields and calls OnDisable/OnEnable again, but not Awake. The
    /// loader built its renderers in Awake, so the OnEnable after the reload ran the build with
    /// <c>_icons</c> null and threw in <c>AddImageButton</c>; nothing observed the task, and UniTask
    /// logged the exception whenever the GC finalised it, against whatever ran then. Reproduced
    /// 2026-10-21: t906 loaded, a script edited and recompiled in Play, a GC, and the same
    /// NullReferenceException at UserInterfaceLoader.cs:1133.
    /// </remarks>
    public class UserInterfaceLoaderScriptReloadTests {
        private GameObject _go;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_go != null) {
                Object.DestroyImmediate(_go);
            }
            if (_panelSettings != null) {
                Object.DestroyImmediate(_panelSettings);
            }
        }

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator AScreenReEnabledAfterAScriptReloadBuildsAgain() {
            _go = new GameObject("ReqScreen");
            _go.SetActive(false);
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;
            var loader = _go.AddComponent<UserInterfaceLoader>();
            // The travel HUD's REQ: its six buttons are ImageButtons.
            Field("userInterfaceAddress").SetValue(loader, "REQ_MAIN.DAT");
            _go.SetActive(true);
            yield return Until(() => loader.IsBuilt, "the screen never built");

            // What the reload leaves: the component disabled, everything Awake made gone.
            _go.SetActive(false);
            foreach (string name in new[] {
                         "_document", "_actionHandler", "_stateProvider", "_filePickerSource",
                         "_filePickerRenderer", "_logger", "_icons", "_labels", "_inputForm",
                     }) {
                Field(name).SetValue(loader, null);
            }
            _go.SetActive(true);

            yield return Until(() => loader.IsBuilt, "the screen did not build again after the reload");
        }

        private static FieldInfo Field(string name) =>
            typeof(UserInterfaceLoader).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new System.MissingFieldException(nameof(UserInterfaceLoader), name);

        private static IEnumerator Until(System.Func<bool> condition, string failure) {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!condition()) {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), failure);
                yield return null;
            }
        }
    }
}
