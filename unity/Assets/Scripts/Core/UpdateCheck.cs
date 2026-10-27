namespace BakAgain.Core {
    using Cysharp.Threading.Tasks;
    using GameData.Mods;
    using System;
    using UnityEngine.Networking;

    /// <summary>
    /// Asks bakagain.org which release is newest (TASK-1017). The release workflow's
    /// <c>website/set-release.sh</c> writes the tag into <c>latest.txt</c>; GitHub's own "latest"
    /// skips pre-releases, so it cannot answer while every release is a beta.
    /// </summary>
    public static class UpdateCheck {
        public const string LatestUrl = "https://bakagain.org/latest.txt";
        public const string DownloadUrl = "https://bakagain.org/#download";

        private static UniTask<string> _newer;
        private static bool _started;

        /// <summary>The newer release's tag ("v0.4.1-beta"), or null: none, switched off, offline, or a
        /// dev build (no version to compare). Asked once per run; later calls share the answer.</summary>
        public static UniTask<string> NewerReleaseAsync() {
            if (!_started) {
                _started = true;
                _newer = FetchAsync().Preserve();
            }
            return _newer;
        }

        private static async UniTask<string> FetchAsync() {
            if (BakAgain.ResourceManagement.ModLayers.GameVersion is not SemVersion current || !GameOptions.CheckForUpdates) {
                return null;
            }
            try {
                using var request = UnityWebRequest.Get(LatestUrl);
                request.timeout = 5;
                await request.SendWebRequest();
                string tag = request.downloadHandler.text.Trim();
                return SemVersion.TryParse(tag.TrimStart('v'), out SemVersion latest) && latest.IsNewerThan(current) ? tag : null;
            } catch (Exception) {
                return null; // offline or the site is down: say nothing
            }
        }
    }
}
