namespace BakAgain.UI.Navigation {
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Base for MonoBehaviour screen controllers managed by the <see cref="IScreenNavigator"/>.
    /// Show = optional async prep (<see cref="OnBeforeShowAsync"/>, e.g. loading the data an
    /// <c>IMenuStateProvider</c> must answer from the moment the REQ builds) → activate the
    /// GameObject (triggers BackgroundImageLoader/UserInterfaceLoader OnEnable + MenuLayerHost's
    /// input-layer push) → optional build while still invisible (<see cref="BuildWhileHiddenAsync"/>)
    /// → fade the <see cref="UIDocument"/> root in over <see cref="_fadeSeconds"/>
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

        public UniTask ShowAsync() => ShowAsync(fade: true);

        public UniTask HideAsync() => HideAsync(fade: true);

        public async UniTask ShowAsync(bool fade) {
            if (gameObject.activeSelf) {
                return; // idempotent per the IScreen contract
            }
            await OnBeforeShowAsync();
            gameObject.SetActive(true);
            VisualElement root = TakeEveryClick(this);
            if (root != null) {
                root.style.opacity = 0f;
            }
            await BuildWhileHiddenAsync();
            await FadeAsync(fade ? 0f : 1f, 1f);
            OnAfterShow();
        }

        public async UniTask HideAsync(bool fade) {
            if (!gameObject.activeSelf) {
                return; // idempotent per the IScreen contract
            }
            OnBeforeHide();
            if (fade) {
                await FadeAsync(1f, 0f);
            }
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Makes a raised screen's document take every click in the window, so none reaches a HUD
        /// beneath it (TASK-960). Call it right after the screen's GameObject is activated: the
        /// document builds a NEW root on every enable, and that root ignores picking.
        /// </summary>
        /// <remarks>
        /// <para>The combat HUD (tier -1) and a location (tier -3) are not on the navigator's stack, so a
        /// pushed screen does not hide them; the travel HUD stays up under the overlay panels. And the
        /// REQ stage ignores picking (TASK-267), so a screen covered only what its own entries
        /// covered: a click on an empty part of the Enhanced inventory pressed the combat HUD's Defend
        /// and passed the turn (TASK-960, and TASK-954 for the cast screen). The original runs each
        /// screen's own menu loop and never hit-tests the HUD while it is up.</para>
        /// <para>The root fills the window and its children keep their own picking, so the screen's
        /// controls are unaffected; a click on nothing of the screen now lands on the screen.</para>
        /// </remarks>
        /// <returns>The document's root, or null without one.</returns>
        public static VisualElement TakeEveryClick(Component screen) {
            VisualElement root = screen.GetComponent<UIDocument>()?.rootVisualElement;
            if (root != null) {
                root.pickingMode = PickingMode.Position;
            }
            return root;
        }

        /// <summary>Fades the screen's <see cref="UIDocument"/> root opacity from/to over
        /// <see cref="_fadeSeconds"/> (0 = instant snap to <paramref name="to"/>). No-op if the
        /// GameObject has no UIDocument (e.g. a Canvas-based screen).</summary>
        private async UniTask FadeAsync(float from, float to) {
            VisualElement root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null || _fadeSeconds <= 0f || from == to) {
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

        /// <summary>
        /// Runs with the screen active but at opacity 0, and the fade-in waits for it: a screen whose
        /// layout settles a few frames after it is built builds here, and is first seen settled.
        /// </summary>
        protected virtual UniTask BuildWhileHiddenAsync() => UniTask.CompletedTask;

        /// <summary>Post-activation kick-off (fire-and-forget content loads that need the
        /// loader awake — file-picker population, input-field models).</summary>
        protected virtual void OnAfterShow() {
        }

        /// <summary>Runs BEFORE the fade-out and deactivation of EVERY hide — a screen pushed over this
        /// one hides it too, and the pop shows it again. So this is for what the surface itself
        /// holds (an input layer, a built panel), never for the visit: that is
        /// <see cref="OnClosing"/>.</summary>
        protected virtual void OnBeforeHide() {
        }

        void IScreen.OnLeftStack() => OnClosing();

        /// <summary>Session-end settlement, run once when the screen leaves the navigator's stack and
        /// before it hides — the place for game-state a screen owns for exactly as long as it is open
        /// (the loot screen returning an emptied ground bag to its zone pool, a shop's visit). Not
        /// run when another screen merely covers this one (TASK-925). It may run while the screen is
        /// already hidden, when it is removed from beneath another.</summary>
        protected virtual void OnClosing() {
        }
    }
}
