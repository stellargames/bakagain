namespace BakAgain.Tests.TestSupport {
    using UnityEngine;

    /// <summary>
    /// An AudioListener that is on only while no scene provides one, for the length of a test run.
    /// </summary>
    /// <remarks>
    /// Most fixtures play cues with no camera, so Unity logged "There are no audio listeners in the
    /// scene" every frame: 17,433 lines in one full PlayMode run on 2026-10-26 (TASK-1004). A plain
    /// listener would make the opposite complaint in the tests that load a scene with its own, so
    /// this one steps aside whenever another is enabled.
    /// </remarks>
    public sealed class StandbyAudioListener : MonoBehaviour {
        private AudioListener _listener;
        private static GameObject _instance;

        /// <summary>
        /// Up from the first frame of a Play session that starts with no listener at all — the test
        /// runner's start scene, before any fixture can run. The game's own scene has one on its
        /// camera, so in the game this never creates anything.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void WhenNothingListens() {
            if (FindObjectsByType<AudioListener>().Length == 0) {
                Create();
            }
        }

        /// <summary>The run's listener; one per Play session, gone when Play ends.</summary>
        public static GameObject Create() {
            if (_instance != null) {
                return _instance;
            }
            var go = new GameObject(nameof(StandbyAudioListener));
            _instance = go;
            Object.DontDestroyOnLoad(go);
            go.AddComponent<StandbyAudioListener>();
            return go;
        }

        private void Awake() {
            _listener = gameObject.AddComponent<AudioListener>();
        }

        private void Update() {
            bool another = false;
            foreach (AudioListener l in FindObjectsByType<AudioListener>()) {
                if (l != _listener && l.isActiveAndEnabled) {
                    another = true;
                    break;
                }
            }
            _listener.enabled = !another;
        }
    }
}
