namespace BakAgain.Tests.PlayMode.Testing {
    using System;
    using System.Collections;
    using System.IO;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using BakAgain.Testing;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>The build test harness's read-only query hook (docs/specs/2026-11-01-build-test-harness-design.md).</summary>
    public class HarnessHookTests {
        private string _portFile;
        private HarnessHook _hook;

        [SetUp]
        public void SetUp() {
            _portFile = Path.Combine(Path.GetTempPath(), "harness-port-" + Guid.NewGuid().ToString("N"));
            _hook = HarnessHook.Start(_portFile);
        }

        [TearDown]
        public void TearDown() {
            if (_hook != null) UnityEngine.Object.Destroy(_hook.gameObject);
            if (File.Exists(_portFile)) File.Delete(_portFile);
        }

        /// <summary>Sends <paramref name="lines"/> on one connection and collects one reply per line.</summary>
        private IEnumerator Ask(string[] lines, string[] replies) {
            int port = int.Parse(File.ReadAllText(_portFile).Trim());
            Task t = Task.Run(() => {
                using var client = new TcpClient("127.0.0.1", port);
                using NetworkStream s = client.GetStream();
                var w = new StreamWriter(s) { AutoFlush = true };
                var r = new StreamReader(s);
                for (int i = 0; i < lines.Length; i++) {
                    w.Write(lines[i] + "\n");
                    replies[i] = r.ReadLine();
                }
            });
            float until = Time.realtimeSinceStartup + 5f;
            while (!t.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(t.IsCompleted, "no reply within 5 s");
            if (t.IsFaulted) throw t.Exception;
        }

        [UnityTest]
        public IEnumerator Window_AnswersTheScreenSize() {
            var r = new string[1];
            yield return Ask(new[] { "window" }, r);
            StringAssert.StartsWith("{\"ok\":true", r[0]);
            StringAssert.Contains($"\"width\":{Screen.width}", r[0]);
        }

        [UnityTest]
        public IEnumerator AnUnknownQuery_IsRefused() {
            var r = new string[1];
            yield return Ask(new[] { "nonsense" }, r);
            StringAssert.StartsWith("{\"ok\":false", r[0]);
            StringAssert.Contains("nonsense", r[0]);
        }

        [UnityTest]
        public IEnumerator AMissingElement_IsRefusedByName() {
            var r = new string[1];
            yield return Ask(new[] { "rect no-such-element" }, r);
            StringAssert.StartsWith("{\"ok\":false", r[0]);
            StringAssert.Contains("no element no-such-element", r[0]);
        }

        [UnityTest]
        public IEnumerator AClassWithAnIndex_FindsThatElement() {
            var host = new GameObject("HarnessRectProbe", typeof(UIDocument));
            try {
                host.GetComponent<UIDocument>().panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
                VisualElement root = host.GetComponent<UIDocument>().rootVisualElement;
                for (int i = 0; i < 2; i++) {
                    var e = new VisualElement();
                    e.AddToClassList("harness-probe");
                    e.style.position = Position.Absolute;
                    e.style.left = 10 + 100 * i;
                    e.style.top = 20;
                    e.style.width = 50;
                    e.style.height = 30;
                    root.Add(e);
                }
                yield return null;
                yield return null;
                var r = new string[2];
                yield return Ask(new[] { "rect .harness-probe 1", "rect .harness-probe 2" }, r);
                StringAssert.StartsWith("{\"ok\":true", r[0]);
                float scale = Screen.width / root.panel.visualTree.layout.width;
                StringAssert.Contains($"\"x\":{Mathf.RoundToInt(110 * scale)},", r[0]);
                StringAssert.StartsWith("{\"ok\":false", r[1]);
            } finally {
                Object.Destroy(host);
            }
        }

        [UnityTest]
        public IEnumerator CombatOutsideAFight_IsRefusedWithoutThrowing() {
            var r = new string[1];
            yield return Ask(new[] { "combat" }, r);
            StringAssert.StartsWith("{\"ok\":false", r[0]);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ARequestWaitingWhenTheHookGoes_IsAnsweredStopping() {
            int port = int.Parse(File.ReadAllText(_portFile).Trim());
            string reply = null;
            Task t = Task.Run(() => {
                using var client = new TcpClient("127.0.0.1", port);
                using NetworkStream s = client.GetStream();
                new StreamWriter(s) { AutoFlush = true }.Write("window\n");
                reply = new StreamReader(s).ReadLine();
            });
            float until = Time.realtimeSinceStartup + 5f;
            while (_hook.Pending == 0 && Time.realtimeSinceStartup < until) System.Threading.Thread.Sleep(5);
            Assert.AreEqual(1, _hook.Pending, "the request never queued");
            Object.DestroyImmediate(_hook.gameObject);   // before any Update answers it
            _hook = null;
            until = Time.realtimeSinceStartup + 5f;
            while (!t.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(t.IsCompleted, "the client was left waiting");
            StringAssert.Contains("stopping", reply);
        }

        [UnityTest]
        public IEnumerator PartyWithoutAGame_IsRefused() {
            var r = new string[1];
            yield return Ask(new[] { "party" }, r);
            StringAssert.StartsWith("{\"ok\":false", r[0]);
            StringAssert.Contains("no game session", r[0]);
        }

        [UnityTest]
        public IEnumerator TwoRequestsOnOneConnection_GetTwoRepliesInOrder() {
            var r = new string[2];
            yield return Ask(new[] { "window", "nonsense" }, r);
            StringAssert.StartsWith("{\"ok\":true", r[0]);
            StringAssert.StartsWith("{\"ok\":false", r[1]);
        }
    }
}
