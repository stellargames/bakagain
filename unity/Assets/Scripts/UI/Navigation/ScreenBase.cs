namespace BakAgain.UI.Navigation {
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Base for MonoBehaviour screen controllers managed by the <see cref="IScreenNavigator"/>.
    /// Show = optional async prep (<see cref="OnBeforeShowAsync"/>, e.g. loading the data an
    /// <c>IMenuStateProvider</c> must answer from the moment the REQ builds) → activate the
    /// GameObject (triggers BackgroundImageLoader/UserInterfaceLoader OnEnable + MenuLayerHost's
    /// input-layer push) → fade the <see cref="UIDocument"/> root in over <see cref="_fadeSeconds"/>
    /// → optional post-activation kick-off (<see cref="OnAfterShow"/>).
    /// Hide = fade the root out → deactivate (triggers each component's OnDisable teardown +
    /// input-layer pop).
    ///
    /// Only the navigator calls <see cref="ShowAsync"/>/<see cref="HideAsync"/> — screens never
    /// show/hide each other.
    /// </summary>
    public abstract class ScreenBase : MonoBehaviour, IScreen {
        [SerializeField] private float _fadeSeconds = 0.15f;   // 0 = instant (opt-out)

        /// <summary>Test-only seam — set the fade duration without going through the inspector.</summary>
        internal void SetFadeSecondsForTest(float seconds) {
            _fadeSeconds = seconds;
        }

        public async UniTask ShowAsync() {
            if (gameObject.activeSelf) {
                return; // idempotent per the IScreen contract
            }
            await OnBeforeShowAsync();
            gameObject.SetActive(true);
            await FadeAsync(0f, 1f);
            OnAfterShow();
        }

        public async UniTask HideAsync() {
            if (!gameObject.activeSelf) {
                return; // idempotent per the IScreen contract
            }
            OnBeforeHide();
            await FadeAsync(1f, 0f);
            gameObject.SetActive(false);
        }

        /// <summary>Fades the screen's <see cref="UIDocument"/> root opacity from/to over
        /// <see cref="_fadeSeconds"/> (0 = instant snap to <paramref name="to"/>). No-op if the
        /// GameObject has no UIDocument (e.g. a Canvas-based screen).</summary>
        private async UniTask FadeAsync(float from, float to) {
            VisualElement root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null || _fadeSeconds <= 0f) {
                if (root != null) {
                    root.style.opacity = to;
                }
                return;
            }
            root.style.opacity = from;
            float elapsed = 0f;
            while (elapsed < _fadeSeconds) {
                elapsed += Time.unscaledDeltaTime;
                root.style.opacity = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / _fadeSeconds));
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            root.style.opacity = to;
        }

        /// <summary>Async prep that must complete BEFORE the GameObject activates (state the
        /// REQ build reads on enable — toggle models, chapter marks).</summary>
        protected virtual UniTask OnBeforeShowAsync() => UniTask.CompletedTask;

        /// <summary>Post-activation kick-off (fire-and-forget content loads that need the
        /// loader awake — file-picker population, input-field models).</summary>
        protected virtual void OnAfterShow() {
        }

        /// <summary>Session-end settlement, run BEFORE the fade-out and deactivation — the place for
        /// game-state a screen owns for exactly as long as it is up (the loot screen returning an
        /// emptied ground bag to its zone pool). Component teardown belongs in <c>OnDisable</c>;
        /// this hook exists because that runs too late to be told apart from a scene teardown, and
        /// because it must still see the screen's collaborators wired.</summary>
        protected virtual void OnBeforeHide() {
        }
    }
}
