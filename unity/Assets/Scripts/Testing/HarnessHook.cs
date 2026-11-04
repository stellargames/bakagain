#if UNITY_EDITOR || DEVELOPMENT_BUILD
namespace BakAgain.Testing {
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;

    /// <summary>
    /// The build test harness's read-only query hook (docs/specs/2026-11-01-build-test-harness-design.md):
    /// a localhost line protocol, one request line in, one JSON line out, answered on the main thread.
    /// </summary>
    /// <remarks>
    /// Development builds and the Editor only, and only when the player is launched with <c>-bak-hook</c>.
    /// Listens on 127.0.0.1 on a port the OS picks and writes it to <c>persistentDataPath/harness-port</c>.
    /// It never changes game state: a check that needs a state the game cannot reach by playing uses a save.
    /// </remarks>
    public sealed class HarnessHook : MonoBehaviour {
        internal const string Flag = "-bak-hook";
        internal const string PortFileName = "harness-port";

        private readonly ConcurrentQueue<(string Line, TaskCompletionSource<string> Reply)> _requests = new();
        private TcpListener _listener;

        /// <summary>Requests waiting for the main thread (tests).</summary>
        internal int Pending => _requests.Count;
        private Thread _thread;
        private volatile bool _stopping;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfAsked() {
            if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), Flag) < 0) {
                return;
            }
            Start(Path.Combine(Application.persistentDataPath, PortFileName));
        }

        /// <summary>Starts a hook that writes its port to <paramref name="portFile"/>.</summary>
        internal static HarnessHook Start(string portFile) {
            var go = new GameObject("HarnessHook");
            DontDestroyOnLoad(go);
            HarnessHook hook = go.AddComponent<HarnessHook>();
            hook._listener = new TcpListener(IPAddress.Loopback, 0);
            hook._listener.Start();
            File.WriteAllText(portFile, ((IPEndPoint)hook._listener.LocalEndpoint).Port + "\n");
            hook._thread = new Thread(hook.Accept) { IsBackground = true, Name = "HarnessHook" };
            hook._thread.Start();
            Debug.Log($"HarnessHook listening on 127.0.0.1:{((IPEndPoint)hook._listener.LocalEndpoint).Port}");
            return hook;
        }

        private void Accept() {
            while (!_stopping) {
                TcpClient client;
                try {
                    client = _listener.AcceptTcpClient();
                } catch (SocketException) {
                    return; // stopped
                } catch (ObjectDisposedException) {
                    return;
                } catch (InvalidOperationException) {
                    return; // stopped between the check and the accept
                }
                new Thread(() => Serve(client)) { IsBackground = true, Name = "HarnessHook client" }.Start();
            }
        }

        private void Serve(TcpClient client) {
            using (client) {
                try {
                    NetworkStream s = client.GetStream();
                    var reader = new StreamReader(s);
                    var writer = new StreamWriter(s) { AutoFlush = true, NewLine = "\n" };
                    string line;
                    while (!_stopping && (line = reader.ReadLine()) != null) {
                        var reply = new TaskCompletionSource<string>();
                        _requests.Enqueue((line, reply));
                        writer.WriteLine(reply.Task.Result);
                    }
                } catch (IOException) {
                    // the client went away
                } catch (ObjectDisposedException) {
                }
            }
        }

        private void Update() {
            while (_requests.TryDequeue(out (string Line, TaskCompletionSource<string> Reply) r)) {
                string answer;
                try {
                    answer = HarnessQueries.Answer(r.Line);
                } catch (Exception e) {
                    answer = HarnessQueries.Refuse(e.GetType().Name + ": " + e.Message);
                }
                r.Reply.SetResult(answer);
            }
        }

        private void OnDestroy() {
            _stopping = true;
            _listener?.Stop();
            // A request no Update will answer now: answer it, or its client waits out its timeout.
            while (_requests.TryDequeue(out (string Line, TaskCompletionSource<string> Reply) r)) {
                r.Reply.TrySetResult(HarnessQueries.Refuse("stopping"));
            }
        }
    }
}
#endif
