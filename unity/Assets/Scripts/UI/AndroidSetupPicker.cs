namespace BakAgain.UI {
    using System;
    using System.IO;
    using System.Text;
    using Cysharp.Threading.Tasks;
    using UnityEngine;

    /// <summary>
    /// The setup wizard's Android pickers (TASK-1014): the system folder or file picker, then the pick
    /// copied into <see cref="StagingDir"/> by <c>org.bakagain.setup.BakSetupPicker</c> (Plugins/Android).
    /// </summary>
    internal static class AndroidSetupPicker {
        private const string JavaClass = "org.bakagain.setup.BakSetupPicker";
        private const int LogTailChars = 200 * 1024;

        /// <summary>What a pick left in the staging folder. <see cref="Error"/>: null, "space", "permission" or "io".</summary>
        internal sealed class PickResult {
            public PickResult(string displayName, string stagingDir, string error) {
                DisplayName = displayName;
                StagingDir = stagingDir;
                Error = error;
            }

            public string DisplayName { get; }
            public string StagingDir { get; }
            public string Error { get; }
        }

        [Serializable]
        private sealed class Reply {
            public string name;
            public string error;
        }

        internal static string StagingDir => Path.Combine(Application.temporaryCachePath, "setup-pick");

        internal static void ClearStaging() {
            if (Directory.Exists(StagingDir)) Directory.Delete(StagingDir, true);
        }

        /// <summary>Null when the player backs out of the picker.</summary>
        internal static UniTask<PickResult> PickFolderAsync() => PickAsync("pickFolder");

        internal static UniTask<PickResult> PickFilesAsync() => PickAsync("pickFiles");

        private static async UniTask<PickResult> PickAsync(string method) {
            ClearStaging();
            Directory.CreateDirectory(StagingDir);
            var done = new UniTaskCompletionSource<PickResult>();
            using (var picker = new AndroidJavaClass(JavaClass)) {
                picker.CallStatic(method, Activity(), StagingDir, new Callback(done, StagingDir));
            }
            PickResult result = await done.Task;
            await UniTask.SwitchToMainThread(); // the copy calls back from its worker thread
            return result;
        }

        /// <summary>Sends the end of the log as text through the system share sheet.</summary>
        internal static void ShareLog() {
            string text = null;
            string path = Application.consoleLogPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(Math.Max(0, stream.Length - LogTailChars), SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                text = reader.ReadToEnd();
            }
            using var picker = new AndroidJavaClass(JavaClass);
            // An Android player writes no log file: its log is this app's logcat.
            text ??= picker.CallStatic<string>("readLog", LogTailChars);
            picker.CallStatic("shareText", Activity(), "BaK-Again log " + Application.version, text);
        }

        private static AndroidJavaObject Activity() {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return player.GetStatic<AndroidJavaObject>("currentActivity");
        }

        private sealed class Callback : AndroidJavaProxy {
            private readonly UniTaskCompletionSource<PickResult> _done;
            private readonly string _staging; // read here: Unity's paths are main-thread only, and this runs on the copy's thread

            public Callback(UniTaskCompletionSource<PickResult> done, string staging) : base(JavaClass + "$Callback") {
                _done = done;
                _staging = staging;
            }

            [UnityEngine.Scripting.Preserve]
            public void onPicked(string json) {
                try {
                    Reply reply = JsonUtility.FromJson<Reply>(json);
                    _done.TrySetResult(new PickResult(reply.name ?? "", _staging,
                        string.IsNullOrEmpty(reply.error) ? null : reply.error));
                } catch (Exception e) {
                    _done.TrySetException(e); // never leave the wizard waiting
                }
            }

            [UnityEngine.Scripting.Preserve]
            public void onCancelled() => _done.TrySetResult(null);
        }
    }
}
